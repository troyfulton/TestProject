/**
 * Reusable dialog-hosted file manager using the native HTML dialog element.
 */

import { FileBrowser } from "./file-browser.js";

/**
 * Reads navigational UI state from the current URL query string.
 */
export function readUrlNavigationState() {
  const params = new URLSearchParams(window.location.search);
  return {
    initialPath: params.get("path") || "",
    initialPage: Number(params.get("page") || "1") || 1,
    initialQuery: params.get("q") || "",
    initialSort: params.get("sort") || undefined,
    initialDirection: params.get("direction") || undefined
  };
}

/**
 * Opens a modal file manager dialog.
 * Honors current URL query parameters by default (path, page, q, sort, direction).
 * @param {object} options
 * @param {object} options.config Client config from /api/config
 * @param {"file"|"any"|"none"} [options.selectionMode]
 * @param {string} [options.initialPath]
 * @param {number} [options.initialPage]
 * @param {string} [options.initialQuery]
 * @param {string} [options.initialSort]
 * @param {string} [options.initialDirection]
 * @param {boolean} [options.syncUrl] When true (default), dialog reads/writes URL state
 * @param {string} [options.title]
 * @returns {Promise<object|null>} Selected entry or null if cancelled
 */
export function openDialog(options) {
  return new Promise((resolve) => {
    const previousFocus = document.activeElement;
    const dialog = document.createElement("dialog");
    dialog.className = "file-dialog";
    dialog.setAttribute("aria-modal", "true");
    dialog.setAttribute("aria-label", options.title || "File manager");

    const title = document.createElement("h2");
    title.textContent = options.title || "Select a file";
    title.style.margin = "0";
    title.style.fontSize = "1.1rem";

    const closeBtn = document.createElement("button");
    closeBtn.type = "button";
    closeBtn.className = "btn secondary";
    closeBtn.textContent = "Close";

    const chrome = document.createElement("div");
    chrome.className = "dialog-chrome";
    chrome.append(title, closeBtn);

    const body = document.createElement("div");
    body.className = "dialog-body";
    const mount = document.createElement("div");
    body.append(mount);

    dialog.append(chrome, body);
    document.body.append(dialog);

    let settled = false;
    let browser = null;

    const finish = (value) => {
      if (settled) {
        return;
      }
      settled = true;
      browser?.dispose();
      dialog.close();
      dialog.remove();
      if (previousFocus && typeof previousFocus.focus === "function") {
        previousFocus.focus();
      }
      resolve(value);
    };

    closeBtn.addEventListener("click", () => finish(null));
    dialog.addEventListener("cancel", (event) => {
      event.preventDefault();
      finish(null);
    });

    // Prefer explicit options; otherwise seed from the current deep-link URL.
    const fromUrl = readUrlNavigationState();
    const syncUrl = options.syncUrl !== false;

    browser = new FileBrowser(mount, {
      config: options.config,
      mode: "dialog",
      selectionMode: options.selectionMode || "file",
      initialPath: options.initialPath ?? fromUrl.initialPath,
      initialPage: options.initialPage ?? fromUrl.initialPage,
      initialQuery: options.initialQuery ?? fromUrl.initialQuery,
      initialSort: options.initialSort ?? fromUrl.initialSort,
      initialDirection: options.initialDirection ?? fromUrl.initialDirection,
      syncUrl,
      onSelect: (item) => finish(item)
    });

    dialog.showModal();
    closeBtn.focus();
    browser.start();
  });
}

/** Public API surface used by the host page and optional embedders. */
export const FileManager = {
  openDialog,
  readUrlNavigationState
};
