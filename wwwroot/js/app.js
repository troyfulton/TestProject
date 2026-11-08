/**
 * Application entry: load public config, mount the full-page manager, wire dialog demo.
 */

import { getConfig, seedDemoData } from "./api.js";
import { confirmAction, FileBrowser } from "./file-browser.js";
import { FileManager } from "./dialog.js";

function showToast(message, duration = 4000) {
  const toast = document.getElementById("toast");
  if (!toast) {
    return;
  }
  toast.hidden = false;
  toast.textContent = message;
  clearTimeout(showToast._timer);
  showToast._timer = setTimeout(() => {
    toast.hidden = true;
  }, duration);
}

async function main() {
  const root = document.getElementById("file-manager-root");
  const dialogBtn = document.getElementById("open-dialog-btn");
  const generateBtn = document.getElementById("generate-test-data-btn");

  let config;
  try {
    config = await getConfig();
  } catch (err) {
    root.textContent = err.message || "Failed to load configuration.";
    return;
  }

  const browser = new FileBrowser(root, {
    config,
    mode: "page",
    syncUrl: true
  });
  await browser.start();

  if (config.features.enableGenerateTestData && generateBtn) {
    generateBtn.hidden = false;
    generateBtn.addEventListener("click", async () => {
      const confirmed = await confirmAction(
        "Create the demo folders and files that are missing? Existing files stay as they are.",
        { confirmLabel: "Generate" }
      );
      if (!confirmed) {
        return;
      }

      generateBtn.disabled = true;
      const previousLabel = generateBtn.textContent;
      generateBtn.textContent = "Generating…";
      browser.setBusy("Generating test data…");
      try {
        const result = await seedDemoData();
        browser.setBusy(null);
        await browser.refresh({ replaceUrl: true });
        showToast(
          `Test data ready. Created ${result.createdDirectories} folders and ${result.createdFiles} files. ${result.skippedExisting} already existed.`,
          8000
        );
      } catch (err) {
        browser.setBusy(null);
        showToast(err.message || "Could not generate test data.");
      } finally {
        generateBtn.disabled = false;
        generateBtn.textContent = previousLabel;
      }
    });
  }

  if (config.features.enableDialogMode && dialogBtn) {
    dialogBtn.hidden = false;
    dialogBtn.addEventListener("click", async () => {
      // Dialog reads current URL query params (path/page/q/sort/direction) by default.
      const selected = await FileManager.openDialog({
        config,
        selectionMode: "any",
        title: "File manager dialog",
        syncUrl: true
      });

      // After close, re-sync the full-page browser with whatever URL state the dialog left.
      browser.readUrl();
      await browser.refresh({ replaceUrl: true });

      if (selected) {
        showToast(`Selected: ${selected.path || selected.name}`);
      }
    });
  }

  // Expose for console/demo usage without polluting module internals.
  window.FileManager = FileManager;
}

main();
