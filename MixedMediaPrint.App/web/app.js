const bridge = window.chrome && window.chrome.webview;

const els = {
  printer: document.getElementById("printer"),
  tabNumber: document.getElementById("tabNumber"),
  text: document.getElementById("text"),
  trayStatus: document.getElementById("trayStatus"),
  loadPdfBtn: document.getElementById("loadPdfBtn"),
  pdfFileLabel: document.getElementById("pdfFileLabel"),
  grid: document.getElementById("pageGrid"),
  preset: document.getElementById("preset"),
  tabTrayPattern: document.getElementById("tabTrayPattern"),
  bodyTrayPattern: document.getElementById("bodyTrayPattern"),
  flipTabX: document.getElementById("flipTabX"),
  flipTabY: document.getElementById("flipTabY"),
  nudgeX: document.getElementById("nudgeX"),
  nudgeY: document.getElementById("nudgeY"),
  copies: document.getElementById("copies"),
  queryTraysBtn: document.getElementById("queryTraysBtn"),
  log: document.getElementById("log"),
  printBtn: document.getElementById("printBtn"),
  overlay: document.getElementById("confirmOverlay"),
  confirmSummary: document.getElementById("confirmSummary"),
  confirmInput: document.getElementById("confirmInput"),
  confirmCancelBtn: document.getElementById("confirmCancelBtn"),
  confirmPrintBtn: document.getElementById("confirmPrintBtn"),
};

// The exact, hardware-confirmed commands from legacy-testkit/README.md -- not placeholders.
const PRESETS = {
  default: { tabTrayPattern: "(?i)tray\\s*1", bodyTrayPattern: "(?i)tray\\s*2", flipTabX: false, flipTabY: false, nudgeX: "-0.625", nudgeY: "0" },
  bypass: { tabTrayPattern: "(?i)bypass", bodyTrayPattern: "(?i)tray\\s*1", flipTabX: false, flipTabY: true, nudgeX: "0", nudgeY: "0" },
};

// The page grid model: an ordered array of slots, each { kind: "placeholder" | "pdf" | "tab" },
// a "pdf" slot also carrying pageIndex into pdfThumbnails. This is the one source of truth for
// both what's drawn in the grid and what gets sent as the print sequence -- no separate state
// to keep in sync.
let pageSlots = [
  { kind: "placeholder" },
  { kind: "tab" },
  { kind: "placeholder" },
];
let pdfThumbnails = [];

function applyPreset(name) {
  const p = PRESETS[name];
  if (!p) return;
  els.tabTrayPattern.value = p.tabTrayPattern;
  els.bodyTrayPattern.value = p.bodyTrayPattern;
  els.flipTabX.checked = p.flipTabX;
  els.flipTabY.checked = p.flipTabY;
  els.nudgeX.value = p.nudgeX;
  els.nudgeY.value = p.nudgeY;
}

function post(message) {
  if (bridge) bridge.postMessage(message);
}

function appendLog(line) {
  els.log.textContent += line + "\n";
  els.log.scrollTop = els.log.scrollHeight;
}

function refreshTrays() {
  const printer = els.printer.value;
  if (!printer || !els.tabTrayPattern.value || !els.bodyTrayPattern.value) return;
  post({
    type: "refreshTrays",
    printer,
    tabTrayPattern: els.tabTrayPattern.value,
    bodyTrayPattern: els.bodyTrayPattern.value,
  });
}

function tabCaption() {
  const custom = els.text.value.trim();
  if (custom) return custom;
  const selected = els.tabNumber.selectedOptions[0];
  return selected ? "#" + selected.textContent : "TAB";
}

function renderGrid() {
  els.grid.innerHTML = "";
  pageSlots.forEach((slot, index) => {
    const tile = document.createElement("div");
    tile.className = "tile" + (slot.kind === "tab" ? " tab-tile" : "");
    tile.draggable = true;
    tile.dataset.index = String(index);

    if (slot.kind === "pdf") {
      const img = document.createElement("img");
      img.src = "data:image/png;base64," + pdfThumbnails[slot.pageIndex];
      tile.appendChild(img);
      const caption = document.createElement("span");
      caption.className = "tile-caption";
      caption.textContent = "Page " + (slot.pageIndex + 1);
      tile.appendChild(caption);
    } else if (slot.kind === "tab") {
      const badge = document.createElement("div");
      badge.className = "tab-badge";
      badge.textContent = "TAB";
      tile.appendChild(badge);
      const caption = document.createElement("span");
      caption.className = "tile-caption";
      caption.textContent = tabCaption();
      tile.appendChild(caption);
    } else {
      const badge = document.createElement("div");
      badge.className = "placeholder-badge";
      badge.textContent = "BODY";
      tile.appendChild(badge);
    }

    tile.addEventListener("dragstart", onTileDragStart);
    tile.addEventListener("dragover", onTileDragOver);
    tile.addEventListener("drop", onTileDrop);
    tile.addEventListener("dragend", onTileDragEnd);
    els.grid.appendChild(tile);
  });
}

function onTileDragStart(event) {
  event.dataTransfer.setData("text/plain", event.currentTarget.dataset.index);
  event.currentTarget.classList.add("dragging");
}
function onTileDragOver(event) {
  event.preventDefault();
}
function onTileDrop(event) {
  event.preventDefault();
  const from = parseInt(event.dataTransfer.getData("text/plain"), 10);
  const to = parseInt(event.currentTarget.dataset.index, 10);
  if (Number.isNaN(from) || Number.isNaN(to) || from === to) return;
  const [moved] = pageSlots.splice(from, 1);
  pageSlots.splice(to, 0, moved);
  renderGrid();
}
function onTileDragEnd(event) {
  event.currentTarget.classList.remove("dragging");
}

function buildSequence() {
  return pageSlots.map((s) => (s.kind === "pdf" ? { kind: "pdf", pageIndex: s.pageIndex } : { kind: s.kind }));
}

function buildRequest() {
  const nudgeXIn = parseFloat(els.nudgeX.value);
  const nudgeYIn = parseFloat(els.nudgeY.value);
  const copies = parseInt(els.copies.value, 10);
  const tabCount = pageSlots.filter((s) => s.kind === "tab").length;

  if (!els.printer.value) return { error: "Select a printer." };
  if (!els.tabTrayPattern.value || !els.bodyTrayPattern.value) return { error: "Tab tray and body tray patterns are required." };
  if (Number.isNaN(nudgeXIn)) return { error: "Nudge X must be a number." };
  if (Number.isNaN(nudgeYIn)) return { error: "Nudge Y must be a number." };
  if (!Number.isInteger(copies) || copies < 1) return { error: "Copies must be a positive whole number." };
  if (tabCount !== 1) return { error: "The page grid must contain exactly one TAB tile." };

  return {
    request: {
      printer: els.printer.value,
      tabNumber: parseInt(els.tabNumber.value, 10),
      text: els.text.value.trim() === "" ? null : els.text.value,
      nudgeXIn,
      nudgeYIn,
      copies,
      tabTrayPattern: els.tabTrayPattern.value,
      bodyTrayPattern: els.bodyTrayPattern.value,
      flipTabX: els.flipTabX.checked,
      flipTabY: els.flipTabY.checked,
      sequence: buildSequence(),
    },
  };
}

function showConfirm(data) {
  els.confirmSummary.textContent =
    `Printer:    ${data.printer}\n` +
    `Tab #${data.tabNumber} -> cut position ${data.position} of 5, text "${data.displayText}"\n` +
    `Tab tray:   ${data.tabTrayName} (id=${data.tabTrayId})\n` +
    `Body tray:  ${data.bodyTrayName} (id=${data.bodyTrayId})\n` +
    `Copies:     ${data.copies} set(s), ${data.sectionCount} page(s) each\n\n` +
    `This PHYSICALLY PRINTS on the device above.`;
  els.confirmInput.value = "";
  els.confirmPrintBtn.disabled = true;
  els.overlay.classList.remove("hidden");
  els.confirmInput.focus();
}

function hideConfirm() {
  els.overlay.classList.add("hidden");
}

els.preset.addEventListener("change", () => {
  if (els.preset.value !== "custom") applyPreset(els.preset.value);
  refreshTrays();
});
els.printer.addEventListener("change", refreshTrays);
els.queryTraysBtn.addEventListener("click", refreshTrays);
els.text.addEventListener("input", renderGrid);
els.tabNumber.addEventListener("change", renderGrid);

els.loadPdfBtn.addEventListener("click", () => {
  els.loadPdfBtn.disabled = true;
  post({ type: "pickPdf" });
});

els.printBtn.addEventListener("click", () => {
  const { request, error } = buildRequest();
  if (error) {
    appendLog("FAILED: " + error);
    return;
  }
  els.printBtn.disabled = true;
  post({ type: "prepare", request });
});

els.confirmInput.addEventListener("input", () => {
  els.confirmPrintBtn.disabled = els.confirmInput.value.trim().toUpperCase() !== "PRINT";
});
els.confirmCancelBtn.addEventListener("click", () => {
  hideConfirm();
  appendLog("Aborted -- nothing sent.");
  els.printBtn.disabled = false;
});
els.confirmPrintBtn.addEventListener("click", () => {
  hideConfirm();
  post({ type: "print" });
});

if (bridge) {
  bridge.addEventListener("message", (event) => {
    const msg = event.data;
    switch (msg.type) {
      case "log":
        appendLog(msg.line);
        break;
      case "printers": {
        els.printer.innerHTML = "";
        for (const name of msg.names) {
          const opt = document.createElement("option");
          opt.value = name;
          opt.textContent = name;
          els.printer.appendChild(opt);
        }
        if (msg.preferred) els.printer.value = msg.preferred;
        refreshTrays();
        break;
      }
      case "printersError":
        appendLog("Could not enumerate installed printers: " + msg.message);
        break;
      case "trayDiscovery":
        els.trayStatus.textContent =
          `Tab tray: ${msg.tabTrayName} (id=${msg.tabTrayId})   Body tray: ${msg.bodyTrayName} (id=${msg.bodyTrayId})`;
        els.trayStatus.classList.remove("error");
        break;
      case "trayError":
        els.trayStatus.textContent = msg.message;
        els.trayStatus.classList.add("error");
        break;
      case "pdfLoaded": {
        els.loadPdfBtn.disabled = false;
        pdfThumbnails = msg.thumbnails;
        const tabSlot = pageSlots.find((s) => s.kind === "tab") || { kind: "tab" };
        pageSlots = pdfThumbnails.map((_, i) => ({ kind: "pdf", pageIndex: i }));
        pageSlots.push(tabSlot);
        els.pdfFileLabel.textContent = `${msg.fileName} (${msg.pageCount} page${msg.pageCount === 1 ? "" : "s"})`;
        renderGrid();
        appendLog(`Loaded ${msg.fileName}: ${msg.pageCount} page(s). Drag the TAB tile to where it belongs.`);
        break;
      }
      case "pdfPickCancelled":
        els.loadPdfBtn.disabled = false;
        break;
      case "pdfError":
        els.loadPdfBtn.disabled = false;
        appendLog("Could not load PDF: " + msg.message);
        break;
      case "prepared":
        els.printBtn.disabled = false;
        showConfirm(msg);
        break;
      case "prepareError":
        els.printBtn.disabled = false;
        appendLog("FAILED: " + msg.message);
        break;
      case "printDone":
        els.printBtn.disabled = false;
        break;
      case "printError":
        els.printBtn.disabled = false;
        appendLog("FAILED: " + msg.message);
        break;
    }
  });
}

applyPreset("bypass");
renderGrid();
post({ type: "loadPrinters" });
