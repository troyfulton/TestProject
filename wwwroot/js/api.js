/**
 * Relative-origin HTTP helpers with normalized ProblemDetails errors.
 */

export class ApiError extends Error {
  constructor(status, title, detail, errorCode) {
    super(detail || title || `HTTP ${status}`);
    this.name = "ApiError";
    this.status = status;
    this.title = title;
    this.detail = detail;
    this.errorCode = errorCode;
  }
}

async function parseError(response) {
  let title = response.statusText || "Request failed";
  let detail = title;
  let errorCode = null;
  try {
    const body = await response.json();
    title = body.title || title;
    detail = body.detail || title;
    errorCode = body.errorCode || null;
  } catch {
    // Non-JSON error bodies are fine.
  }
  return new ApiError(response.status, title, detail, errorCode);
}

async function request(url, options = {}) {
  const response = await fetch(url, {
    ...options,
    headers: {
      Accept: "application/json",
      ...(options.body && !(options.body instanceof Blob) && !(options.body instanceof ArrayBuffer)
        ? { "Content-Type": "application/json" }
        : {}),
      ...(options.headers || {})
    }
  });

  if (!response.ok) {
    throw await parseError(response);
  }

  if (response.status === 204) {
    return null;
  }

  const contentType = response.headers.get("content-type") || "";
  if (contentType.includes("application/json")) {
    return response.json();
  }

  return response;
}

export function getConfig(signal) {
  return request("/api/config", { signal });
}

export function listDirectory({ path = "", page = 1, pageSize, sort, direction, signal } = {}) {
  const params = new URLSearchParams();
  if (path) params.set("path", path);
  params.set("page", String(page));
  if (pageSize) params.set("pageSize", String(pageSize));
  if (sort) params.set("sort", sort);
  if (direction) params.set("direction", direction);
  return request(`/api/files?${params.toString()}`, { signal });
}

export function searchFiles({ q, path = "", recursive = true, signal } = {}) {
  const params = new URLSearchParams();
  params.set("q", q);
  if (path) params.set("path", path);
  params.set("recursive", String(!!recursive));
  return request(`/api/search?${params.toString()}`, { signal });
}

export function createFolder(path, name, signal) {
  return request("/api/files/folders", {
    method: "POST",
    body: JSON.stringify({ path, name }),
    signal
  });
}

export function deletePath(path, recursive, signal) {
  return request("/api/files/delete", {
    method: "POST",
    body: JSON.stringify({ path, recursive }),
    signal
  });
}

export function moveItem(sourcePath, destinationDirectory, destinationName, signal) {
  return request("/api/files/move", {
    method: "POST",
    body: JSON.stringify({ sourcePath, destinationDirectory, destinationName }),
    signal
  });
}

export function copyItem(sourcePath, destinationDirectory, destinationName, signal) {
  return request("/api/files/copy", {
    method: "POST",
    body: JSON.stringify({ sourcePath, destinationDirectory, destinationName }),
    signal
  });
}

export function downloadUrl(path) {
  const params = new URLSearchParams();
  params.set("path", path);
  return `/api/files/download?${params.toString()}`;
}

export function createUploadSession(destinationDirectory, fileName, fileSize, signal) {
  return request("/api/uploads", {
    method: "POST",
    body: JSON.stringify({ destinationDirectory, fileName, fileSize }),
    signal
  });
}

export function getUploadSession(uploadId, signal) {
  return request(`/api/uploads/${encodeURIComponent(uploadId)}`, { signal });
}

export async function uploadChunk(uploadId, offset, blob, checksumHex, signal) {
  const response = await fetch(`/api/uploads/${encodeURIComponent(uploadId)}/chunk?offset=${offset}`, {
    method: "PUT",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/octet-stream",
      ...(checksumHex ? { "X-Chunk-Checksum": checksumHex } : {})
    },
    body: blob,
    signal
  });

  if (!response.ok) {
    throw await parseError(response);
  }

  return response.json();
}

export function completeUpload(uploadId, sha256, signal) {
  return request(`/api/uploads/${encodeURIComponent(uploadId)}/complete`, {
    method: "POST",
    body: JSON.stringify({ sha256: sha256 || null }),
    signal
  });
}

export function cancelUpload(uploadId, signal) {
  return request(`/api/uploads/${encodeURIComponent(uploadId)}`, {
    method: "DELETE",
    signal
  });
}

/**
 * Creates missing demonstration folders and files. Existing entries are not replaced.
 */
export function seedDemoData(signal) {
  return request("/api/demo/seed", {
    method: "POST",
    signal
  });
}
