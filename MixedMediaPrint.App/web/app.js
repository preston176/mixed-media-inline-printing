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
// After a PDF loads, the grid starts collapsed into a single stack -- fanning
// every page tile out immediately is overwhelming for a large document. This
// only gates what renderGrid() draws; pageSlots is already the real sequence.
let gridRevealed = true;

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
  els.grid.classList.toggle("stack-mode", !gridRevealed);
  if (!gridRevealed) {
    renderStack();
    return;
  }
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
    tile.addEventListener("dragleave", onTileDragLeave);
    tile.addEventListener("drop", onTileDrop);
    tile.addEventListener("dragend", onTileDragEnd);
    els.grid.appendChild(tile);
  });
}

function renderStack() {
  const stack = document.createElement("div");
  stack.className = "stack-card";
  stack.setAttribute("role", "button");
  stack.tabIndex = 0;

  const visual = document.createElement("div");
  visual.className = "stack-visual";

  [2, 1].forEach((i) => {
    const back = document.createElement("div");
    back.className = "stack-layer stack-layer-back";
    back.style.setProperty("--i", i);
    visual.appendChild(back);
  });

  const front = document.createElement("div");
  front.className = "stack-layer stack-layer-front";
  const img = document.createElement("img");
  img.src = "data:image/png;base64," + pdfThumbnails[0];
  front.appendChild(img);
  visual.appendChild(front);

  const count = document.createElement("span");
  count.className = "stack-count";
  count.textContent = String(pdfThumbnails.length);
  visual.appendChild(count);

  stack.appendChild(visual);

  const label = document.createElement("div");
  label.className = "stack-label";
  label.textContent = `${pdfThumbnails.length} page${pdfThumbnails.length === 1 ? "" : "s"} loaded — click to fan out`;
  stack.appendChild(label);

  const reveal = () => {
    gridRevealed = true;
    renderGrid();
  };
  stack.addEventListener("click", reveal);
  stack.addEventListener("keydown", (event) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      reveal();
    }
  });

  els.grid.appendChild(stack);
}

// Drag state lives here rather than on dataTransfer, since dataTransfer.getData
// is only reliably readable on "drop" -- dragover needs it too, to show which
// side of the hovered tile the dragged tile would land on.
let dragState = null;

function dropSide(event, tileEl) {
  const rect = tileEl.getBoundingClientRect();
  return event.clientX - rect.left > rect.width / 2 ? "after" : "before";
}

function clearDropIndicators() {
  els.grid.querySelectorAll(".drop-before, .drop-after").forEach((t) => {
    t.classList.remove("drop-before", "drop-after");
  });
}

// Converts a "drop before/after this index" intent into the splice target,
// accounting for the index shift caused by removing the dragged item first --
// this is what makes the landing spot match the insertion-line indicator
// regardless of whether the drag moves the tile forward or backward.
function finishDrag(overIndex, insertAfter) {
  const fromIndex = dragState ? dragState.fromIndex : undefined;
  dragState = null;
  clearDropIndicators();
  els.grid.classList.remove("grid-dragging");
  if (fromIndex === undefined || overIndex === undefined || fromIndex === overIndex) return;
  const insertionIndex = insertAfter ? overIndex + 1 : overIndex;
  const target = fromIndex < insertionIndex ? insertionIndex - 1 : insertionIndex;
  if (target === fromIndex) return;
  const [moved] = pageSlots.splice(fromIndex, 1);
  pageSlots.splice(target, 0, moved);
  renderGrid();
}

function onTileDragStart(event) {
  const fromIndex = parseInt(event.currentTarget.dataset.index, 10);
  dragState = { fromIndex };
  event.dataTransfer.setData("text/plain", String(fromIndex));
  event.dataTransfer.effectAllowed = "move";
  event.currentTarget.classList.add("dragging");
  els.grid.classList.add("grid-dragging");
}
function onTileDragOver(event) {
  event.preventDefault();
  event.dataTransfer.dropEffect = "move";
  if (!dragState) return;
  const tile = event.currentTarget;
  const overIndex = parseInt(tile.dataset.index, 10);
  clearDropIndicators();
  if (overIndex === dragState.fromIndex) return;
  const side = dropSide(event, tile);
  dragState.overIndex = overIndex;
  dragState.insertAfter = side === "after";
  tile.classList.add(side === "after" ? "drop-after" : "drop-before");
}
function onTileDragLeave(event) {
  event.currentTarget.classList.remove("drop-before", "drop-after");
}
function onTileDrop(event) {
  event.preventDefault();
  event.stopPropagation();
  if (!dragState) return;
  finishDrag(dragState.overIndex, dragState.insertAfter);
}
function onTileDragEnd(event) {
  event.currentTarget.classList.remove("dragging");
  finishDrag(undefined, undefined); // safety net: cleans up if the drag was cancelled (e.g. Esc) with no drop event
}
function onGridDragOver(event) {
  event.preventDefault();
  event.dataTransfer.dropEffect = "move";
}
function onGridDrop(event) {
  // Dropped in the grid's empty space rather than on a tile -- send it to the end.
  event.preventDefault();
  if (!dragState) return;
  finishDrag(pageSlots.length - 1, true);
}
els.grid.addEventListener("dragover", onGridDragOver);
els.grid.addEventListener("drop", onGridDrop);

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

// The dropped File object has bytes but no real filesystem path (the WebView2 page is
// sandboxed like any web page) -- so unlike the native picker, the host needs the raw
// bytes here and persists them to a temp file itself before running the same pipeline.
async function arrayBufferToBase64(buffer) {
  let binary = "";
  const bytes = new Uint8Array(buffer);
  const chunkSize = 0x8000;
  for (let i = 0; i < bytes.length; i += chunkSize) {
    binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunkSize));
  }
  return btoa(binary);
}

async function handleDroppedPdf(file) {
  const isPdf = file.type === "application/pdf" || file.name.toLowerCase().endsWith(".pdf");
  if (!isPdf) {
    appendLog(`FAILED: "${file.name}" is not a PDF.`);
    return;
  }
  els.loadPdfBtn.disabled = true;
  try {
    const pdfBase64 = await arrayBufferToBase64(await file.arrayBuffer());
    post({ type: "dropPdf", fileName: file.name, pdfBase64 });
  } catch (err) {
    els.loadPdfBtn.disabled = false;
    appendLog("Could not read the dropped file: " + err.message);
  }
}

els.loadPdfBtn.addEventListener("dragenter", (event) => {
  event.preventDefault();
  els.loadPdfBtn.dataset.dragging = "true";
});
els.loadPdfBtn.addEventListener("dragover", (event) => {
  event.preventDefault();
  event.dataTransfer.dropEffect = "copy";
});
els.loadPdfBtn.addEventListener("dragleave", () => {
  delete els.loadPdfBtn.dataset.dragging;
});
els.loadPdfBtn.addEventListener("drop", (event) => {
  event.preventDefault();
  delete els.loadPdfBtn.dataset.dragging;
  const file = event.dataTransfer.files[0];
  if (file) handleDroppedPdf(file);
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
        gridRevealed = false;
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
