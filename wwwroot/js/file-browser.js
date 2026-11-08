/**
 * Directory browser UI with deep-linkable state, search, and file operations.
 */

import * as api from "./api.js";
import { listRecoverableUploads, ResumableUploader } from "./upload.js";

function formatBytes(bytes) {
  if (bytes == null || Number.isNaN(bytes)) {
    return "—";
  }
  if (bytes === 0) {
    return "0 B";
  }
  const units = ["B", "KB", "MB", "GB", "TB"];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value.toFixed(value >= 10 || unit === 0 ? 0 : 1)} ${units[unit]}`;
}

function formatSpeed(bytesPerSec) {
  if (bytesPerSec == null || Number.isNaN(bytesPerSec) || bytesPerSec <= 0) {
    return "—";
  }
  return `${formatBytes(bytesPerSec)}/s`;
}

function formatDate(iso) {
  if (!iso) {
    return "—";
  }
  return new Date(iso).toLocaleString();
}

function el(tag, props = {}, children = []) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(props)) {
    if (key === "className") {
      node.className = value;
    } else if (key === "text") {
      node.textContent = value;
    } else if (key === "htmlFor") {
      node.htmlFor = value;
    } else if (key.startsWith("on") && typeof value === "function") {
      node.addEventListener(key.slice(2).toLowerCase(), value);
    } else if (value === false || value == null) {
      // skip
    } else if (value === true) {
      node.setAttribute(key, "");
    } else {
      node.setAttribute(key, String(value));
    }
  }
  for (const child of children) {
    if (child == null) continue;
    node.append(child.nodeType ? child : document.createTextNode(String(child)));
  }
  return node;
}

/**
 * Accessible modal prompt that works in embedded browser previews
 * where window.prompt / window.confirm are often blocked.
 */
function promptFields({ title, fields, confirmLabel = "OK", danger = false }) {
  return new Promise((resolve) => {
    const dialog = el("dialog", {
      className: "input-dialog",
      "aria-modal": "true",
      "aria-label": title
    });

    const heading = el("h2", { text: title });
    const form = el("form", { method: "dialog", className: "input-dialog-form" });
    const values = {};
    const inputs = [];

    for (const field of fields) {
      const id = `field-${field.name}-${Math.random().toString(36).slice(2, 8)}`;
      const label = el("label", { htmlFor: id, text: field.label });
      const input = el("input", {
        id,
        type: "text",
        name: field.name,
        value: field.value || "",
        required: !!field.required,
        autocomplete: "off"
      });
      inputs.push(input);
      form.append(label, input);
    }

    const error = el("p", { className: "input-dialog-error", hidden: true });
    const actions = el("div", { className: "input-dialog-actions" }, [
      el("button", {
        type: "button",
        className: "btn secondary",
        onClick: () => {
          dialog.close();
          cleanup(null);
        }
      }, ["Cancel"]),
      el("button", {
        type: "submit",
        className: danger ? "btn danger" : "btn",
        value: "confirm"
      }, [confirmLabel])
    ]);

    form.append(error, actions);
    dialog.append(heading, form);
    document.body.append(dialog);

    const cleanup = (result) => {
      dialog.remove();
      resolve(result);
    };

    form.addEventListener("submit", (event) => {
      event.preventDefault();
      for (const input of inputs) {
        values[input.name] = input.value.trim();
        if (input.required && !values[input.name]) {
          error.hidden = false;
          error.textContent = "Please fill in the required fields.";
          input.focus();
          return;
        }
      }
      dialog.close();
      cleanup(values);
    });

    dialog.addEventListener("cancel", (event) => {
      event.preventDefault();
      cleanup(null);
    });

    dialog.showModal();
    inputs[0]?.focus();
    inputs[0]?.select();
  });
}

export async function confirmAction(message, { confirmLabel = "Confirm", danger = false } = {}) {
  return new Promise((resolve) => {
    const dialog = el("dialog", {
      className: "input-dialog",
      "aria-modal": "true",
      "aria-label": "Confirm"
    });
    dialog.append(
      el("h2", { text: "Confirm" }),
      el("p", { className: "input-dialog-message", text: message }),
      el("div", { className: "input-dialog-actions" }, [
        el("button", {
          type: "button",
          className: "btn secondary",
          onClick: () => {
            dialog.close();
            dialog.remove();
            resolve(false);
          }
        }, ["Cancel"]),
        el("button", {
          type: "button",
          className: danger ? "btn danger" : "btn",
          onClick: () => {
            dialog.close();
            dialog.remove();
            resolve(true);
          }
        }, [confirmLabel])
      ])
    );
    document.body.append(dialog);
    dialog.addEventListener("cancel", (event) => {
      event.preventDefault();
      dialog.remove();
      resolve(false);
    });
    dialog.showModal();
  });
}

function showMessage(message) {
  return confirmAction(message, { confirmLabel: "OK" }).then(() => undefined);
}

/**
 * Search legend popup: what name search matches, with clickable examples.
 */
function showSearchHelp({ onUseExample } = {}) {
  return new Promise((resolve) => {
    const dialog = el("dialog", {
      className: "input-dialog search-help-dialog",
      "aria-modal": "true",
      "aria-labelledby": "search-help-title"
    });

    const close = (value = null) => {
      dialog.close();
      dialog.remove();
      resolve(value);
    };

    const example = (query, description) => {
      const button = el("button", {
        type: "button",
        className: "search-help-example",
        onClick: () => {
          onUseExample?.(query);
          close(query);
        }
      });
      button.append(
        el("code", { text: query }),
        el("span", { text: description })
      );
      return button;
    };

    const param = (name, description) => {
      const item = el("li", { className: "search-help-param" });
      item.append(el("code", { text: name }), document.createTextNode(` — ${description}`));
      return item;
    };

    dialog.append(
      el("h2", { id: "search-help-title", text: "Search help" }),
      el("p", {
        className: "input-dialog-message",
        text: "Search matches file and folder names only (not file contents). Matching is case-insensitive and finds the query text anywhere in the name. Results start from the current folder and include subfolders."
      }),
      el("h3", { className: "search-help-heading", text: "Legend" }),
      el("ul", { className: "search-help-list" }, [
        el("li", { text: "Names only — files and folders" }),
        el("li", { text: "Substring match — report finds report.txt and MyReports" }),
        el("li", { text: "Case ignored — PDF matches Quarterly.pdf" }),
        el("li", { text: "Extensions count as part of the name — .csv finds data.csv" }),
        el("li", { text: "No wildcards, operators, or content search" })
      ]),
      el("h3", { className: "search-help-heading", text: "URL query parameters" }),
      el("p", {
        className: "input-dialog-message search-help-note",
        text: "Deep links keep UI state in the address bar. Example: /?path=Documents&q=readme&page=1&sort=Name&direction=asc"
      }),
      el("ul", { className: "search-help-list" }, [
        param("q", "Search text for file and folder names"),
        param("path", "Folder to search from (and the browse location)"),
        param("page", "Browse page number when not searching"),
        param("sort", "Browse sort field: Name, Size, or LastModified"),
        param("direction", "Browse sort direction: asc or desc")
      ]),
      el("h3", { className: "search-help-heading", text: "Examples" }),
      el("div", { className: "search-help-examples" }, [
        example("readme", "Files or folders with “readme” in the name"),
        example(".txt", "Text files by extension"),
        example("Projects", "A folder or file named like Projects"),
        example("2024", "Names containing a year or similar digits")
      ]),
      el("div", { className: "input-dialog-actions" }, [
        el("button", {
          type: "button",
          className: "btn",
          onClick: () => close(null)
        }, ["Close"])
      ])
    );

    dialog.addEventListener("cancel", (event) => {
      event.preventDefault();
      close(null);
    });

    document.body.append(dialog);
    dialog.showModal();
  });
}

/**
 * Mounts a file manager into a container element.
 */
export class FileBrowser {
  constructor(container, options = {}) {
    this.container = container;
    this.config = options.config;
    this.mode = options.mode || "page"; // page | dialog
    this.selectionMode = options.selectionMode || "none"; // none | file | any
    this.onSelect = options.onSelect || null;
    // Page mode syncs URL by default. Dialog mode opts in via options.syncUrl
    // so deep links (path/page/q/sort/direction) are honored when opened.
    this.syncUrl = options.syncUrl != null ? !!options.syncUrl : this.mode === "page";
    this.state = {
      path: options.initialPath || "",
      page: options.initialPage || 1,
      pageSize: options.config?.browsing?.defaultPageSize || 100,
      sort: options.initialSort || options.config?.browsing?.defaultSort || "Name",
      direction: options.initialDirection || "asc",
      q: options.initialQuery || "",
      listing: null,
      searchResults: null,
      selected: null,
      loading: false,
      error: null,
      busyMessage: null
    };
    this.abortController = null;
    this.uploader = null;
    this.boundPopState = (event) => this.onPopState(event);
    this.renderShell();
    if (this.syncUrl) {
      window.addEventListener("popstate", this.boundPopState);
      this.readUrl();
    }
  }

  dispose() {
    this.abortController?.abort();
    if (this.syncUrl) {
      window.removeEventListener("popstate", this.boundPopState);
    }
    this.container.replaceChildren();
  }

  readUrl() {
    const params = new URLSearchParams(window.location.search);
    this.state.path = params.get("path") || "";
    this.state.page = Number(params.get("page") || "1") || 1;
    this.state.q = params.get("q") || "";
    this.state.sort = params.get("sort") || this.state.sort;
    this.state.direction = params.get("direction") || this.state.direction;
  }

  writeUrl(replace = false) {
    if (!this.syncUrl) {
      return;
    }
    const params = new URLSearchParams();
    if (this.state.path) params.set("path", this.state.path);
    if (this.state.page > 1) params.set("page", String(this.state.page));
    if (this.state.q) params.set("q", this.state.q);
    if (this.state.sort && this.state.sort !== "Name") params.set("sort", this.state.sort);
    if (this.state.direction && this.state.direction !== "asc") params.set("direction", this.state.direction);
    const url = `${window.location.pathname}${params.toString() ? `?${params}` : ""}`;
    if (replace) {
      history.replaceState(null, "", url);
    } else {
      history.pushState(null, "", url);
    }
  }

  onPopState() {
    this.readUrl();
    this.refresh();
  }

  async start() {
    this.render();
    await this.refresh({ replaceUrl: true });
  }

  async refresh({ replaceUrl = false } = {}) {
    this.abortController?.abort();
    this.abortController = new AbortController();
    const signal = this.abortController.signal;
    this.state.loading = true;
    this.state.error = null;
    this.render();

    try {
      if (this.state.q && this.config.features.enableSearch) {
        this.state.searchResults = await api.searchFiles({
          q: this.state.q,
          path: this.state.path,
          signal
        });
        this.state.listing = null;
      } else {
        this.state.searchResults = null;
        this.state.listing = await api.listDirectory({
          path: this.state.path,
          page: this.state.page,
          pageSize: this.state.pageSize,
          sort: this.state.sort,
          direction: this.state.direction,
          signal
        });
        this.state.page = this.state.listing.page;
      }
      this.writeUrl(replaceUrl);
    } catch (err) {
      if (err.name === "AbortError") {
        return;
      }
      this.state.error = err.message || "Failed to load directory.";
      this.state.listing = null;
      this.state.searchResults = null;
    } finally {
      this.state.loading = false;
      this.render();
    }
  }

  /**
   * Shows or clears the panel overlay used while a long operation such as demo seeding runs.
   * Pass a falsy message to clear it.
   */
  setBusy(message) {
    this.state.busyMessage = message || null;
    if (this.root) {
      this.render();
    }
  }

  renderShell() {
    this.container.replaceChildren();
    this.root = el("div", { className: "file-manager" });
    this.container.append(this.root);
  }

  render() {
    const features = this.config.features;
    this.root.replaceChildren();

    if (this.state.busyMessage) {
      this.root.append(el("div", { className: "busy-overlay", text: this.state.busyMessage }));
    }

    const actionsToolbar = el("div", { className: "toolbar toolbar-actions" }, [
      features.enableCreateFolder
        ? el("button", { type: "button", className: "btn secondary", onClick: () => this.createFolder() }, ["New folder"])
        : null,
      features.enableUpload
        ? (() => {
            const input = el("input", {
              type: "file",
              id: `upload-${this.mode}`,
              hidden: true,
              onChange: (e) => this.handleUploadSelected(e)
            });
            const label = el("label", { className: "btn secondary", htmlFor: input.id }, ["Upload"]);
            const wrap = el("span", {}, [input, label]);
            return wrap;
          })()
        : null,
      features.enableDownload
        ? el("button", {
            type: "button",
            className: "btn secondary",
            disabled: !this.state.selected || this.state.selected.isDirectory,
            onClick: () => this.downloadSelected()
          }, ["Download"])
        : null,
      features.enableCopy
        ? el("button", {
            type: "button",
            className: "btn secondary",
            disabled: !this.state.selected,
            onClick: () => this.copyOrMove("copy")
          }, ["Copy"])
        : null,
      features.enableMove
        ? el("button", {
            type: "button",
            className: "btn secondary",
            disabled: !this.state.selected,
            onClick: () => this.copyOrMove("move")
          }, ["Move"])
        : null,
      features.enableDelete
        ? el("button", {
            type: "button",
            className: "btn danger",
            disabled: !this.state.selected,
            onClick: () => this.deleteSelected()
          }, ["Delete"])
        : null
    ]);
    this.root.append(actionsToolbar);

    if (features.enableSearch) {
      const searchToolbar = el("div", { className: "toolbar toolbar-search" }, [
        el("label", {
          className: "search-label",
          htmlFor: `search-input-${this.mode}`,
          text: "Search"
        }),
        el("input", {
          id: `search-input-${this.mode}`,
          type: "search",
          className: "search-input",
          value: this.state.q,
          placeholder: "Search file and folder names…",
          "aria-label": "Search file and folder names",
          onKeydown: (e) => {
            if (e.key === "Enter") {
              this.state.q = e.target.value.trim();
              this.state.page = 1;
              this.refresh();
            }
          },
          onChange: (e) => {
            this.state.q = e.target.value.trim();
          }
        }),
        el("button", {
          type: "button",
          className: "btn",
          onClick: () => {
            const input = this.root.querySelector(`#search-input-${this.mode}`);
            this.state.q = input ? input.value.trim() : "";
            this.state.page = 1;
            this.refresh();
          }
        }, ["Search"]),
        this.state.q
          ? el("button", {
              type: "button",
              className: "btn secondary",
              onClick: () => {
                this.state.q = "";
                this.state.page = 1;
                this.refresh();
              }
            }, ["Clear"])
          : null,
        el("button", {
          type: "button",
          className: "link-button search-help-link",
          title: "Search query legend, URL parameters, and examples",
          "aria-haspopup": "dialog",
          onClick: () => this.openSearchHelp()
        }, ["Search help"])
      ]);
      this.root.append(searchToolbar);
    }
    this.root.append(this.renderBreadcrumbs());
    this.root.append(this.renderStats());

    if (this.state.loading) {
      this.root.append(el("div", { className: "status-panel", text: "Loading…" }));
    } else if (this.state.error) {
      this.root.append(el("div", { className: "status-panel error", text: this.state.error }));
    } else {
      this.root.append(this.renderTable());
      if (!this.state.q) {
        this.root.append(this.renderPager());
      }
    }

    if (features.enableUpload) {
      this.root.append(this.renderUploadPanel());
    }

    if (this.mode === "dialog" && this.selectionMode !== "none") {
      this.root.append(
        el("div", { className: "pager" }, [
          el("button", {
            type: "button",
            className: "btn",
            disabled: !this.state.selected || (this.selectionMode === "file" && this.state.selected.isDirectory),
            onClick: () => this.onSelect?.(this.state.selected)
          }, ["Select"])
        ])
      );
    }
  }

  renderBreadcrumbs() {
    const parts = this.state.path ? this.state.path.split("/").filter(Boolean) : [];
    const nodes = [
      el("button", {
        type: "button",
        onClick: () => {
          this.state.path = "";
          this.state.page = 1;
          this.state.q = "";
          this.refresh();
        }
      }, ["Home"])
    ];

    let cumulative = "";
    for (const part of parts) {
      cumulative = cumulative ? `${cumulative}/${part}` : part;
      const target = cumulative;
      nodes.push(el("span", { className: "sep", text: "/" }));
      nodes.push(
        el("button", {
          type: "button",
          onClick: () => {
            this.state.path = target;
            this.state.page = 1;
            this.state.q = "";
            this.refresh();
          }
        }, [part])
      );
    }

    return el("nav", { className: "breadcrumbs", "aria-label": "Breadcrumb" }, nodes);
  }

  renderStats() {
    if (this.state.searchResults) {
      const s = this.state.searchResults;
      const truncation = s.truncated ? ` (truncated: ${s.truncationReason || "limit"})` : "";
      return el("div", { className: "stats" }, [
        el("span", { text: `Search results: ${s.resultCount}${truncation}` })
      ]);
    }

    const listing = this.state.listing;
    if (!listing) {
      return el("div", { className: "stats" }, [el("span", { text: " " })]);
    }

    const dirSize = listing.directorySizeComplete
      ? formatBytes(listing.directoryFileSizeBytes)
      : "not computed (directory too large)";

    return el("div", { className: "stats" }, [
      el("span", { text: `Folders (directory): ${listing.directoryFolderCount}` }),
      el("span", { text: `Files (directory): ${listing.directoryFileCount}` }),
      el("span", { text: `Size (directory): ${dirSize}` }),
      el("span", { text: `Page items: ${listing.pageItemCount}` }),
      el("span", { text: `Page file size: ${formatBytes(listing.pageFileSizeBytes)}` })
    ]);
  }

  renderTable() {
    const items = this.state.searchResults?.items || this.state.listing?.items || [];
    if (items.length === 0) {
      return el("div", { className: "status-panel", text: this.state.q ? "No matching files or folders." : "This folder is empty." });
    }

    const head = el("thead", {}, [
      el("tr", {}, [
        el("th", {}, [
          el("button", { type: "button", onClick: () => this.setSort("Name") }, [
            `Name${this.sortMarker("Name")}`
          ])
        ]),
        el("th", { text: "Type" }),
        el("th", {}, [
          el("button", { type: "button", onClick: () => this.setSort("Size") }, [
            `Size${this.sortMarker("Size")}`
          ])
        ]),
        el("th", {}, [
          el("button", { type: "button", onClick: () => this.setSort("LastModified") }, [
            `Modified${this.sortMarker("LastModified")}`
          ])
        ])
      ])
    ]);

    const body = el("tbody");
    for (const item of items) {
      const selected = this.state.selected?.path === item.path;
      const row = el("tr", {
        className: selected ? "selected" : "",
        tabIndex: 0,
        onClick: () => this.selectItem(item),
        onDblclick: () => this.activateItem(item),
        onKeydown: (e) => {
          if (e.key === "Enter") {
            this.activateItem(item);
          }
        }
      }, [
        el("td", {}, [
          el("div", { className: "name-cell" }, [
            el("span", { className: "icon", text: item.isDirectory ? "📁" : "📄" }),
            el("span", { className: "label", text: item.name })
          ])
        ]),
        el("td", { text: item.isDirectory ? "Folder" : (item.extension || "File") }),
        el("td", { text: item.isDirectory ? "—" : formatBytes(item.size) }),
        el("td", { text: formatDate(item.lastModified) })
      ]);
      body.append(row);
    }

    return el("div", { className: "table-wrap" }, [
      el("table", { className: "file-table", role: "grid" }, [head, body])
    ]);
  }

  sortMarker(field) {
    if (this.state.sort !== field) {
      return "";
    }
    return this.state.direction === "asc" ? " ▲" : " ▼";
  }

  setSort(field) {
    if (this.state.sort === field) {
      this.state.direction = this.state.direction === "asc" ? "desc" : "asc";
    } else {
      this.state.sort = field;
      this.state.direction = "asc";
    }
    this.state.page = 1;
    this.refresh();
  }

  renderPager() {
    const listing = this.state.listing;
    if (!listing) {
      return el("div");
    }

    return el("div", { className: "pager" }, [
      el("span", { text: `Page ${listing.page} of ${listing.totalPages} (${listing.totalItems} items)` }),
      el("div", {}, [
        el("button", {
          type: "button",
          className: "btn secondary",
          disabled: listing.page <= 1,
          onClick: () => {
            this.state.page = listing.page - 1;
            this.refresh();
          }
        }, ["Previous"]),
        " ",
        el("button", {
          type: "button",
          className: "btn secondary",
          disabled: listing.page >= listing.totalPages,
          onClick: () => {
            this.state.page = listing.page + 1;
            this.refresh();
          }
        }, ["Next"])
      ])
    ]);
  }

  renderUploadPanel() {
    const recoverable = listRecoverableUploads();
    const panel = el("div", { className: "upload-panel" });
    this.uploadStatusEl = el("div", { className: "upload-row", text: "No active upload." });
    this.uploadProgressEl = el("div", { className: "progress" }, [el("span")]);
    panel.append(this.uploadStatusEl, this.uploadProgressEl);

    const controls = el("div", { className: "upload-row" }, [
      el("button", {
        type: "button",
        className: "btn secondary",
        title: "Cooperative pause between chunks (current request still finishes)",
        onClick: () => this.uploader?.pause()
      }, ["Pause"]),
      el("button", {
        type: "button",
        className: "btn secondary",
        title: "Continue after pause or simulated disconnect",
        onClick: () => this.uploader?.resume()
      }, ["Resume"]),
      el("button", {
        type: "button",
        className: "btn secondary",
        title: "Abort the in-flight chunk request like a dropped connection; session stays recoverable",
        onClick: () => {
          this.uploader?.simulateNetworkDrop();
          this.setUploadStatus("Simulated disconnect — click Resume to reconnect and continue.");
        }
      }, ["Simulate disconnect"]),
      el("button", {
        type: "button",
        className: "btn danger",
        onClick: async () => {
          const id = this.currentUploadId;
          await this.uploader?.cancel(id);
          this.currentUploadId = null;
          this.setUploadStatus("Upload cancelled.");
          this.setUploadProgress(0);
        }
      }, ["Cancel upload"])
    ]);
    panel.append(controls);

    if (recoverable.length) {
      const list = el("div", { className: "upload-row" });
      list.append(el("strong", { text: "Recoverable uploads: " }));
      for (const session of recoverable) {
        list.append(
          el("button", {
            type: "button",
            className: "btn secondary",
            onClick: () => this.resumeRecoverable(session)
          }, [`Resume ${session.fileName}`])
        );
      }
      panel.append(list);
    }

    return panel;
  }

  setUploadStatus(text) {
    if (this.uploadStatusEl) {
      this.uploadStatusEl.textContent = text;
    }
  }

  setUploadProgress(percent) {
    const bar = this.uploadProgressEl?.querySelector("span");
    if (bar) {
      bar.style.width = `${percent}%`;
    }
  }

  selectItem(item) {
    this.state.selected = item;
    this.render();
  }

  activateItem(item) {
    if (item.isDirectory) {
      this.state.path = item.path;
      this.state.page = 1;
      this.state.q = "";
      this.state.selected = null;
      this.refresh();
      return;
    }

    if (this.mode === "dialog" && this.selectionMode !== "none") {
      this.onSelect?.(item);
      return;
    }

    if (this.config.features.enableDownload) {
      window.location.href = api.downloadUrl(item.path);
    }
  }

  async openSearchHelp() {
    await showSearchHelp({
      onUseExample: (query) => {
        this.state.q = query;
        this.state.page = 1;
        this.refresh();
      }
    });
  }

  async createFolder() {
    const result = await promptFields({
      title: "New folder",
      confirmLabel: "Create",
      fields: [
        {
          name: "name",
          label: "Folder name",
          value: "big-file",
          required: true
        }
      ]
    });
    if (!result) {
      return;
    }

    try {
      await api.createFolder(this.state.path, result.name);
      await this.refresh();
    } catch (err) {
      await showMessage(err.message || "Could not create folder.");
    }
  }

  downloadSelected() {
    const item = this.state.selected;
    if (!item || item.isDirectory) {
      return;
    }
    window.location.href = api.downloadUrl(item.path);
  }

  async deleteSelected() {
    const item = this.state.selected;
    if (!item) {
      return;
    }

    let recursive = false;
    if (item.isDirectory) {
      const confirmed = await confirmAction(
        `Delete folder "${item.name}" and ALL nested contents? This cannot be undone.`,
        { confirmLabel: "Delete", danger: true }
      );
      if (!confirmed) {
        return;
      }
      recursive = true;
    } else {
      const confirmed = await confirmAction(`Delete file "${item.name}"?`, {
        confirmLabel: "Delete",
        danger: true
      });
      if (!confirmed) {
        return;
      }
    }

    try {
      await api.deletePath(item.path, recursive);
      this.state.selected = null;
      await this.refresh();
    } catch (err) {
      await showMessage(err.message || "Delete failed.");
    }
  }

  async copyOrMove(operation) {
    const item = this.state.selected;
    if (!item) {
      return;
    }

    const result = await promptFields({
      title: operation === "copy" ? "Copy item" : "Move item",
      confirmLabel: operation === "copy" ? "Copy" : "Move",
      fields: [
        {
          name: "destinationDirectory",
          label: "Destination folder path (blank = home)",
          value: this.state.path
        },
        {
          name: "destinationName",
          label: "Destination name",
          value: item.name,
          required: true
        }
      ]
    });
    if (!result) {
      return;
    }

    this.state.busyMessage = operation === "copy" ? "Copying…" : "Moving…";
    this.render();
    try {
      if (operation === "copy") {
        await api.copyItem(item.path, result.destinationDirectory, result.destinationName || undefined);
      } else {
        await api.moveItem(item.path, result.destinationDirectory, result.destinationName || undefined);
      }
      this.state.selected = null;
      await this.refresh();
    } catch (err) {
      await showMessage(err.message || `${operation} failed.`);
    } finally {
      this.state.busyMessage = null;
      this.render();
    }
  }

  async handleUploadSelected(event) {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) {
      return;
    }
    await this.runUpload(file, null);
  }

  async resumeRecoverable(session) {
    const input = el("input", { type: "file", hidden: true });
    document.body.append(input);
    input.addEventListener("change", async () => {
      const file = input.files?.[0];
      input.remove();
      if (!file) {
        return;
      }
      await this.runUpload(file, session.uploadId);
    });
    input.click();
  }

  async runUpload(file, existingUploadId) {
    this.uploader = new ResumableUploader({
      config: this.config,
      onProgress: ({ percent, offset, total, uploadId, bytesPerSec, averageBytesPerSec }) => {
        if (uploadId) {
          this.currentUploadId = uploadId;
        }
        this.setUploadProgress(percent);
        const recent = formatSpeed(bytesPerSec);
        const average = formatSpeed(averageBytesPerSec);
        this.setUploadStatus(
          `Uploading ${file.name}: ${formatBytes(offset)} / ${formatBytes(total)} (${percent}%) — ${recent} current, ${average} avg`
        );
      },
      onStatus: (status) => {
        if (status === "paused") {
          this.setUploadStatus(`Paused (between chunks): ${file.name}`);
        } else if (status === "network_interrupted") {
          this.setUploadStatus(`Disconnected (simulated): ${file.name} — click Resume to continue`);
        } else if (status === "complete") {
          this.setUploadStatus(`Completed: ${file.name}`);
        } else if (status === "cancelled") {
          this.setUploadStatus("Upload cancelled.");
        } else {
          this.setUploadStatus(`${status}: ${file.name}`);
        }
      }
    });

    try {
      // Track id for cancel; create session happens inside uploadFile.
      this.currentUploadId = existingUploadId || null;
      const result = await this.uploader.uploadFile({
        file,
        destinationDirectory: this.state.path,
        existingUploadId
      });
      this.currentUploadId = null;
      this.setUploadStatus(`Uploaded ${result.fileName}`);
      this.setUploadProgress(100);
      await this.refresh();
    } catch (err) {
      if (err.name !== "AbortError") {
        this.setUploadStatus(err.message || "Upload failed.");
      }
    }
  }
}
