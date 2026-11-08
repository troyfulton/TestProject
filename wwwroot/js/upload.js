/**
 * Resumable chunked upload client with pause/resume, network-drop simulation,
 * retries, and localStorage recovery. Server offset is authoritative after failures.
 *
 * Pause = cooperative wait between chunks (in-flight request still finishes).
 * Simulate disconnect = abort the active fetch like a dropped connection, keep session.
 */

import {
  cancelUpload,
  completeUpload,
  createUploadSession,
  getUploadSession,
  uploadChunk
} from "./api.js";

const STORAGE_KEY = "maplarge.upload.sessions";

function loadStoredSessions() {
  try {
    return JSON.parse(localStorage.getItem(STORAGE_KEY) || "[]");
  } catch {
    return [];
  }
}

function saveStoredSessions(sessions) {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(sessions));
}

export function listRecoverableUploads() {
  return loadStoredSessions();
}

export function rememberUploadSession(session) {
  const sessions = loadStoredSessions().filter((s) => s.uploadId !== session.uploadId);
  sessions.push(session);
  saveStoredSessions(sessions);
}

export function forgetUploadSession(uploadId) {
  saveStoredSessions(loadStoredSessions().filter((s) => s.uploadId !== uploadId));
}

async function sha256Hex(arrayBuffer) {
  const hash = await crypto.subtle.digest("SHA-256", arrayBuffer);
  return [...new Uint8Array(hash)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

function sleep(ms, signal) {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(new DOMException("Aborted", "AbortError"));
      return;
    }
    const timer = setTimeout(resolve, ms);
    signal?.addEventListener(
      "abort",
      () => {
        clearTimeout(timer);
        reject(new DOMException("Aborted", "AbortError"));
      },
      { once: true }
    );
  });
}

/**
 * Coordinates a single resumable upload.
 */
export class ResumableUploader {
  constructor({ config, onProgress, onStatus }) {
    this.config = config;
    this.onProgress = onProgress || (() => {});
    this.onStatus = onStatus || (() => {});
    this.paused = false;
    this.networkDropped = false;
    this.abortController = null;
    this.pauseWaiters = [];
    this.interruptWaiters = [];
  }

  /**
   * Cooperative pause: waits before the next chunk. Does not abort the current request.
   */
  pause() {
    this.paused = true;
    this.onStatus("paused");
  }

  resume() {
    this.paused = false;
    this.onStatus("uploading");
    for (const resolve of this.pauseWaiters.splice(0)) {
      resolve();
    }
    for (const resolve of this.interruptWaiters.splice(0)) {
      resolve();
    }
  }

  /**
   * Aborts the in-flight chunk request to mimic a lost connection.
   * Keeps the server session and localStorage recovery metadata.
   */
  simulateNetworkDrop() {
    if (!this.abortController) {
      return;
    }
    this.networkDropped = true;
    this.paused = false;
    this.onStatus("network_interrupted");
    this.abortController.abort();
  }

  async waitIfPaused() {
    if (!this.paused) {
      return;
    }
    await new Promise((resolve) => this.pauseWaiters.push(resolve));
  }

  async waitForReconnect() {
    this.onStatus("network_interrupted");
    await new Promise((resolve) => this.interruptWaiters.push(resolve));
  }

  async cancel(uploadId) {
    this.networkDropped = false;
    this.abortController?.abort();
    if (uploadId) {
      try {
        await cancelUpload(uploadId);
      } catch {
        // Session may already be gone.
      }
      forgetUploadSession(uploadId);
    }
    this.onStatus("cancelled");
  }

  /**
   * Starts or resumes an upload for the selected File object.
   */
  async uploadFile({ file, destinationDirectory, existingUploadId }) {
    this.abortController = new AbortController();
    let signal = this.abortController.signal;
    const chunkSize = (this.config.uploads.chunkSizeMB || 8) * 1024 * 1024;
    const maxRetries = this.config.uploads.maxRetries ?? 5;
    const initialDelay = (this.config.uploads.retryInitialDelaySeconds || 2) * 1000;
    const verifyChecksum = !!this.config.uploads.verifyChunkChecksum;

    let session;
    if (existingUploadId) {
      session = await getUploadSession(existingUploadId, signal);
      const remembered = loadStoredSessions().find((s) => s.uploadId === existingUploadId);
      if (
        !remembered ||
        remembered.fileName !== file.name ||
        remembered.fileSize !== file.size ||
        remembered.lastModified !== file.lastModified
      ) {
        throw new Error("Selected file does not match the interrupted upload session.");
      }
    } else {
      session = await createUploadSession(destinationDirectory, file.name, file.size, signal);
    }

    rememberUploadSession({
      uploadId: session.uploadId,
      fileName: file.name,
      fileSize: file.size,
      lastModified: file.lastModified,
      destinationDirectory,
      destinationPath: session.destinationPath
    });

    this.uploadId = session.uploadId;
    this.onStatus("uploading");
    const startedAt = performance.now();
    let lastOffset = session.offset;

    const emitProgress = (offset, { bytesThisInterval = 0, intervalMs = 0 } = {}) => {
      const elapsedSec = Math.max((performance.now() - startedAt) / 1000, 0.001);
      const intervalSec = Math.max(intervalMs / 1000, 0.001);
      const overallBytesPerSec = offset / elapsedSec;
      const recentBytesPerSec = bytesThisInterval > 0 ? bytesThisInterval / intervalSec : overallBytesPerSec;
      this.onProgress({
        uploadId: session.uploadId,
        offset,
        total: file.size,
        percent: file.size === 0 ? 100 : Math.floor((offset / file.size) * 100),
        bytesPerSec: recentBytesPerSec,
        averageBytesPerSec: overallBytesPerSec
      });
    };

    emitProgress(session.offset);
    let offset = session.offset;

    while (offset < file.size) {
      await this.waitIfPaused();
      if (signal.aborted && this.networkDropped) {
        // Handled below when the chunk fetch aborts.
      } else {
        signal.throwIfAborted();
      }

      const end = Math.min(offset + chunkSize, file.size);
      // Read once: reuse the same buffer for checksum + PUT body (avoids double disk/memory read).
      const buffer = await file.slice(offset, end).arrayBuffer();
      const checksum = verifyChecksum ? await sha256Hex(buffer) : null;

      let attempt = 0;
      for (;;) {
        try {
          if (!this.abortController || this.abortController.signal.aborted) {
            this.abortController = new AbortController();
            signal = this.abortController.signal;
          }

          const chunkStarted = performance.now();
          const updated = await uploadChunk(session.uploadId, offset, buffer, checksum, signal);
          const chunkElapsed = performance.now() - chunkStarted;
          const bytesThisInterval = Math.max(updated.offset - lastOffset, 0);
          offset = updated.offset;
          lastOffset = offset;
          emitProgress(offset, { bytesThisInterval, intervalMs: chunkElapsed });
          break;
        } catch (err) {
          if (err.name === "AbortError") {
            if (this.networkDropped) {
              this.networkDropped = false;
              // Wait until the user clicks Resume (simulates reconnect).
              await this.waitForReconnect();
              this.abortController = new AbortController();
              signal = this.abortController.signal;

              // Server offset is authoritative after an ambiguous disconnect.
              const authoritative = await getUploadSession(session.uploadId, signal);
              offset = authoritative.offset;
              lastOffset = offset;
              emitProgress(offset);
              this.onStatus("uploading");
              break;
            }
            throw err;
          }

          try {
            const authoritative = await getUploadSession(session.uploadId, signal);
            offset = authoritative.offset;
            lastOffset = offset;
            emitProgress(offset);
            if (offset >= end) {
              break;
            }
          } catch {
            // Fall through to retry.
          }

          attempt += 1;
          if (attempt > maxRetries) {
            this.onStatus("error");
            throw err;
          }

          const delay = initialDelay * 2 ** (attempt - 1);
          this.onStatus(`retrying (${attempt}/${maxRetries})`);
          await sleep(delay, signal);
          this.onStatus("uploading");
        }
      }
    }

    const completed = await completeUpload(session.uploadId, null, signal);
    forgetUploadSession(session.uploadId);
    emitProgress(file.size);
    this.onStatus("complete");
    return completed;
  }
}
