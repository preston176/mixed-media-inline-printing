# Mixed-Media Inline Printing — Native Desktop App (replaces PowerShell/Word)

## Context

This repo is a PowerShell-based R&D testkit that spent six sessions proving a printing
technique on Sharp BP-70C65/BP-71C65 PCL6 printers: interleaving tab-divider pages
(pulled from one tray, printed with rotated, positioned label text on die-cut tab
stock) with body pages (pulled from another tray) — all inline, in **one** print job.
Standard Windows print submission (PrintTicket/XPS) was proven dead on this driver
family (it collapses per-page settings to one value for the whole job). The working
mechanism is raw GDI (`DEVMODE` + `ResetDC` between pages via P/Invoke), and the
current best end-to-end script drives it through hidden Word COM automation
(`print-mixed-test.ps1`) so it can reuse a real docx template for the tab content.

The user wants this turned into a real Windows desktop app — no PowerShell, no
Word/COM dependency — that does the same job the script does, so it can be handed to
an actual operator instead of run from a terminal. Patent/IP work is explicitly
deferred; not part of this build.

Confirmed scope decisions (already gathered from the user, do not re-ask):
- Body page content = real PDF documents the operator loads.
- Tab geometry fixed to the current 5-cut template only (no template designer, no
  other cut counts) for v1.
- Single workstation, single printer per run (no multi-printer/networked scope) —
  but calibration must still be **stored and reusable** per printer, since two
  distinct physical devices are already known (`SHARP#1`/BP-70C65,
  `SHARP BP-71C65 PCL6`) with opposite rotation directions.
- Dev happens on macOS; the app only runs/tests on the Windows workstation.

## Load-bearing correction found during research

The prototype's own comments (`legacy-testkit/print-tab-from-docx.ps1:29-35`,
`legacy-testkit/PLAYBOOK-printer-prep.md`)
show that **only per-page TRAY (`dmDefaultSource`) switching is decisively proven** —
via an isolating byte-diff test that varies bins only. Per-page **media type**
(`dmMediaType`) was only ever set *together with* a tray change, never isolated, and
the current best production script explicitly does not set media type at all: it
relies on the tray being pre-configured at the printer's own control panel
("Tray 1 → paper type = Tab Paper"). So the new app must treat **tray as the primary
per-page routing signal**; setting `dmMediaType` per page is a defensive extra, not
something the design may rely on to route two media types out of *one* tray without
its own dedicated hardware test.

## Repository reorganization (done)

Everything that was at repo root was untracked in git (`git status` showed all `??`),
so this was a plain, fully-reversible file move — no history at stake. **Completed:**

1. Created `legacy-testkit/` at repo root.
2. Moved everything that was at root into it: all `.ps1`/`.psm1` files
   (`GdiPrint.psm1`, `PrintCaps.psm1`, `PrinterSelect.psm1`, `ScenarioResolver.psm1`,
   `XpsBuilder.psm1`, `XpsPrintApi.psm1`, `testkit.ps1`, `pull-test.ps1`,
   `edit-tab-docx.ps1`, `print-tab-from-docx.ps1`, `print-mixed-test.ps1`, every
   `capture-gdi-*.ps1`, `simulate-tabpos.py`), all docx/pdf test artifacts
   (`5th-cut-1-to-500.docx`, `BP-70C65-Phase1-Findings.docx`, the `mixed-test-tab*`/
   `tab*-formixed` docx files, `tabpos-preview.pdf`/`.png`, the status-report docx,
   the stray `~$...` Word lock file), the docs (`README.md`, `SESSION.md`,
   `errors.md`, `testing-report.md`, `PLAYBOOK.md`, `PLAYBOOK-printer-prep.md`,
   `docs/`), `tests/`, and `Mixed-Media-Inline-Testing-Package/` +
   `Mixed-Media-Inline-Testing-Package.zip`.
3. Wrote this implementation plan to repo root as `IMPLEMENTATION_PLAN.md`.
4. Left `.gitignore` at root (it governs the whole repo, including the new .NET
   solution that lands there next).

Result: root contains only `IMPLEMENTATION_PLAN.md` and `.gitignore` until Phase 0
below adds the actual `.sln`/projects. All prior research stays fully intact and
referenceable under `legacy-testkit/` — the new code still points back to it (e.g.
"ported from `legacy-testkit/GdiPrint.psm1`") rather than duplicating context.

## Stack

- **WPF, .NET 10**, split into two projects so most logic builds/tests on macOS:
  - `MixedMediaPrint.Core` (`net10.0`, no Windows dependency) — job model, tab
    geometry math, calibration store, preview rendering. The Windows-only P/Invoke
    print engine inside it is marked `[SupportedOSPlatform("windows")]` so it's a
    compile-time warning, not a runtime surprise, when touched from portable code.
  - `MixedMediaPrint.App` (`net10.0-windows`, WPF) — UI only, references Core.
  - `MixedMediaPrint.Tests` (xUnit) — runs on macOS and Windows.
- P/Invoke: port [legacy-testkit/GdiPrint.psm1](legacy-testkit/GdiPrint.psm1)'s
  `DllImport` declarations into C# almost mechanically (this C# already compiles
  clean via `dotnet build`, confirmed in `legacy-testkit/errors.md` DEV-2). Replace
  the raw byte-offset `DEVMODE` pokes
  (`OFF_DMFIELDS = 72` etc.) with a proper `[StructLayout(LayoutKind.Sequential)]
  DEVMODEW` struct with named fields — same behavior, removes a class of
  wrong-offset bugs.

## PDF rendering

Use **PDFium** (BSD-3/Apache-2.0, permissive — not MuPDF, which is AGPL/commercial).
Wrap it via **PDFtoImage** (MIT, actively maintained, wraps `pdfium-binaries`) for v1:
render each body page to a bitmap at print DPI, then draw it with
`Graphics.FromHdc(hdc).DrawImage(...)`. Use for both the WPF job-setup thumbnails and
the actual print-time body-page content.

Later optimization (not v1): PDFium's native `FPDF_RenderPage(HDC, ...)` can render a
page directly onto an arbitrary device context, including a printer HDC, with no
intermediate bitmap — bypasses bitmap-DPI capping and per-page allocation. None of
the portable wrapper packages expose this (they stick to the cross-platform
`FPDF_RenderPageBitmap`), so this would mean a small hand-written P/Invoke surface
against the same `pdfium.dll` the wrapper already deploys. Worth doing once the
pipeline is proven; not worth the extra native-surface risk on day one.

## GDI + GDI+ hybrid drawing (confirmed sound, with one hard rule)

Keep the proven raw P/Invoke job/page lifecycle (`StartDoc`/`StartPage`/`ResetDC`/
`EndPage`/`EndDoc` + per-page `DEVMODE`) exactly as `legacy-testkit/GdiPrint.psm1`
does it. For
*drawing* page content, wrap the HDC with `Graphics.FromHdc(hdc)` — this is literally
how `System.Drawing.Printing.PrintDocument` works internally, not exotic. It gives
GDI+ text (`DrawString` with `RotateTransform`, `MeasureString` for auto-fit) and
`DrawImage` for the rasterized PDF body pages.

**Hard rule:** `Graphics.FromHdc` does a `SaveDC` on creation and `Dispose()` does the
matching `RestoreDC`. Calling `ResetDC` while a `Graphics` instance on that HDC is
still alive corrupts GDI's state stack. Every page must create a **fresh** `Graphics`
after `StartPage`, and dispose it (via `using`) *before* the next `ResetDC` call —
never reused across a `ResetDC`. Enforce this structurally with a
`GdiPrintJob.RenderPage(Action<Graphics> draw)` helper that owns the `using` block
and wraps drawing in `try/finally`, so a mid-page exception can't poison the next
page's `ResetDC` — don't leave this to caller discipline.

**Rotation-sign risk:** the calibrated GDI `CreateFont` escapement values (900 on
BP-71C65, 2700 on SHARP#1 — tenths of a degree) do not automatically carry the same
sign convention into GDI+'s `RotateTransform(degrees)`. Treat the *magnitude* (90°)
as known, the *sign* as needing re-verification per printer once ported (see
Validation checkpoints below).

## Project structure

```
MixedMediaPrint.sln
MixedMediaPrint.Core/
  Printing/Gdi/
    NativeMethods.cs         # winspool.drv + gdi32.dll P/Invoke, ported from legacy-testkit/GdiPrint.psm1
    DevModeW.cs               # typed DEVMODEW struct
    DevModeBuilder.cs         # OpenPrinter/DocumentProperties -> per-page DEVMODE bytes (bin + media)
    PrinterCapabilities.cs    # DeviceCapabilities: bins + media types (DC_BINS/DC_MEDIATYPES)
    DeviceInfo.cs             # GetDeviceCaps: DPI, physical offsets, HorzRes/VertRes
    GdiPrintJob.cs            # StartDoc/StartPage/ResetDC/EndPage/EndDoc + RenderPage(Action<Graphics>) helper
  Rendering/
    IPdfPageSource.cs / PdfiumPdfPageSource.cs   # PDFtoImage-backed page rendering
    TabGeometry.cs            # 5-cut template constants, position-cycling, EMU->px, margin correction, flip
    ITextMeasurer.cs / SkiaTextMeasurer.cs / GdiPlusTextMeasurer.cs
    TabLabelFitter.cs         # shared, unit-testable auto-shrink-to-fit
    TabLabelRenderer.cs       # Windows-only: rotated, fitted label via Graphics.FromHdc
  Calibration/
    PrinterCalibrationProfile.cs   # escapement, per-tray-role nudge/flip, tray/media patterns, device fingerprint
    CalibrationStore.cs             # JSON under %AppData%\MixedMediaPrint\calibration\<queue-name>.json
  JobModel/
    PrintJobPlan.cs / BodyRangeItem.cs / TabRunItem.cs   # ordered items incl. "N consecutive tabs x M copies" semantics
    JobExpander.cs             # plan -> flat PageInstance[] (role, tray-role, content ref, copy index)
  Execution/
    PrintEngine.cs             # capability resolution -> expansion -> per-page DEVMODE/draw loop
    RunMode.cs                 # Preview | DryRunToFile | Physical
MixedMediaPrint.App/
  Views/   PrinterSetupView, CalibrationTestPrintView, JobSetupView, RunView
  ViewModels/  (one per view, MVVM)
MixedMediaPrint.Tests/
```

**Tab geometry constants** (from
[legacy-testkit/print-mixed-test.ps1](legacy-testkit/print-mixed-test.ps1) and
`legacy-testkit/capture-gdi-tabpos.ps1`, already extracted, no docx needed):
X=7162495 EMU, W=557784 EMU, H=1828800 EMU; Y by cut position 1..5 =
{412394, 2174443, 4155033, 6071616, 7697419}; position = `((tabNumber-1) mod 5)+1`.
`EMU_PER_INCH = 914400`. Font: Arial Bold, `round(14 * DpiY / 72)` px — plus **new**
auto-shrink-to-fit logic (`TabLabelFitter`, measure + reduce until it fits the box),
since no such logic exists anywhere today and arbitrary custom labels are a real
production need (testing-report.md flags this as an untested gap).

**Sequential printing semantics** to preserve (from
`legacy-testkit/print-tab-from-docx.ps1`):
"N consecutive tabs starting at tab X, each optionally repeated M copies in place"
(e.g. Count=5,Copies=2 → 1,1,2,2,3,3,4,4,5,5). Generalize the job model to an ordered
sequence of body-PDF-page-ranges interleaved with tab runs — the real production job
is a long PDF with tabs inserted throughout, not just a single test tab.

**Calibration profile**, keyed per **(tab-tray, body-tray) scenario**, not per
printer and not per individual tray in isolation — flip/nudge turn out to be
properties of which *pair* of trays is playing tab/body, not independently
composable per tray. Confirmed by two real, hardware-validated commands in
`legacy-testkit/README.md`, for the same physical printer:

```json
{
  "printerQueueName": "SHARP BP-71C65 PCL6",
  "deviceFingerprint": { "dpiX": 600, "dpiY": 600, "horzRes": 4960, "vertRes": 6496 },
  "rotation": { "escapementTenthDeg": 900 },
  "scenarios": [
    {
      "tabTrayPattern": "(?i)tray\\s*1", "bodyTrayPattern": "(?i)tray\\s*2",
      "flipX": false, "flipY": false, "nudgeXIn": -0.625, "nudgeYIn": 0.0,
      "confirmedOnPaper": true, "note": "README.md 'this one worked' -- TabNumber 3, default trays"
    },
    {
      "tabTrayPattern": "(?i)bypass", "bodyTrayPattern": "(?i)tray\\s*1",
      "flipX": false, "flipY": true, "nudgeXIn": 0.0, "nudgeYIn": 0.0,
      "confirmedOnPaper": true, "note": "README.md -- TabNumber 3, Text 'EMAIL CORRESPONDENCE'"
    }
  ],
  "lastVerifiedUtc": "2026-08-17",
  "lastVerificationResult": "PASS-PERPAGE-TRAY"
}
```

The two scenarios above aren't hypothetical placeholders — they're the exact
parameters from the two commands in `legacy-testkit/README.md`, both confirmed
working on real hardware. Note they don't even agree on which physical bin plays
which role (Tray 1 is "tab" in scenario 1, "body" in scenario 2), which is
exactly why a scenario is the (tab-pattern, body-pattern) pair, not a
role-labeled single tray.

`PrintEngine` compares `deviceFingerprint` against the live `DeviceInfo` at startup
and blocks with a clear warning on mismatch — a direct, cheap guard against the exact
mistake this project already made once (`SHARP#1` vs `SHARP BP-71C65 PCL6` being
different physical units, `legacy-testkit/errors.md` ERR-3). Tray/media resolution
stays pattern-based against the live capability list at run time, fail-closed on
ambiguous or missing matches — carrying forward
`legacy-testkit/ScenarioResolver.psm1`'s design philosophy (not its code, which is
Phase-1/XPS-era and not reused).

## Dry-run tiers (needed because dev is on macOS, hardware is on Windows)

- **Tier 0 — Preview** (macOS + Windows, zero P/Invoke): SkiaSharp-rendered preview
  of the job (tab positions, labels, page order) sharing the real `TabGeometry`/
  `TabLabelFitter` code — successor to `legacy-testkit/simulate-tabpos.py`, but
  built into the app.
- **Tier 1 — Dry-run-to-file** (Windows only, no paper): the real `PrintEngine`/
  `GdiPrintJob` pipeline runs for real, but `StartDoc`'s `lpszOutput` points at a
  file, exactly like `legacy-testkit/GdiPrint.psm1` already supports. Ship a
  permanent **"Verify per-page tray switching"** self-test on the Printer Setup
  screen that reproduces `legacy-testkit/capture-gdi-perpage.ps1`'s byte-diff
  harness against whatever printer
  is selected — turns the one-off tribal-knowledge verdict into a repeatable
  regression check for any future driver/hardware change.
- **Tier 2 — Physical print**: same typed-confirmation gate as the current scripts,
  as a required step on the Run screen before anything touches paper.

## Risks requiring hardware validation

| # | Risk | Why it matters |
|---|---|---|
| R1 | Per-page `dmMediaType` in isolation is unconfirmed (see correction above) | v1 must not rely on media-type-alone routing out of one tray; needs its own isolated byte-diff test before being trusted |
| R2 | GDI escapement → GDI+ `RotateTransform` sign may not match, even on an already-calibrated printer | New risk introduced by the drawing-code port; verify sign per printer |
| R3 | `ResetDC` while a `Graphics` instance is alive corrupts state | Mitigated structurally by `GdiPrintJob.RenderPage`, treat any per-page draw exception as a whole-job abort |
| R4 | Auto-fit-to-box text sizing has never been hardware-tested (testing-report.md) | Dedicated hardware pass: print a range of label lengths at auto-computed sizes |
| R5 | Rotation/flip differ per printer *and per tray* on the same unit | Addressed by keying calibration at tray-role granularity (done above) |
| R6 | New rendering path (PDFium bitmap + GDI+ text) isn't either previously-proven content path (raw-GDI rectangles, Word-rendered docx) | Needs its own fresh hardware confirmation once built, not inherited "proven" status |
| R7 | If a body PDF's page size ever differs from what's loaded in the body tray, per-page `dmPaperSize` may also need setting — untested | Confirm v1 assumes one consistent page size job-wide, or scope out |

## Phased implementation plan

1. **Phase 0 — Scaffolding. Done.** Solution created as `MixedMediaPrint.slnx`
   (.NET 10's `dotnet new sln` now defaults to the XML `.slnx` format, not
   `.sln`) with three projects: `MixedMediaPrint.Core` (net10.0),
   `MixedMediaPrint.App` (net10.0-windows, WPF), `MixedMediaPrint.Tests` (xUnit).
   Ported `NativeMethods`/`DevModeW`/`DevModeBuilder`/`PrinterCapabilities`/
   `DeviceInfo` from `legacy-testkit/GdiPrint.psm1` into
   `MixedMediaPrint.Core/Printing/Gdi/`, splitting each into a portable, pure
   piece (`DevModeFieldWriter`, `CapabilityListParser`) and a thin
   `[SupportedOSPlatform("windows")]` wrapper that owns the actual P/Invoke calls
   — so the delicate part (DEVMODE field/bit manipulation, capability-buffer
   parsing) is unit-tested on macOS, not just "compiles." 13 xUnit tests pass,
   including one that asserts the typed `DevModeW` struct places `dmFields`/
   `dmDefaultSource`/`dmMediaType` at the exact byte offsets (72/88/196) the
   original PowerShell poked directly — a direct regression check that the port
   didn't silently change the hardware-proven mechanism. `dotnet build
   MixedMediaPrint.slnx` succeeds with 0 warnings/0 errors for all three
   projects (the App project needs `<EnableWindowsTargeting>true</EnableWindowsTargeting>`
   to compile off Windows — this validates the build but not runtime behavior,
   which still needs the actual workstation).
2. **Phase 1 — Print engine + Tier-1 dry run. C# port done; hardware checkpoint
   outstanding.** Implemented `GdiPrintJob` (StartDoc/StartPage/ResetDC/EndPage/
   EndDoc + `AbortDoc` on failure) with the enforced per-page `Graphics` discipline
   (fresh `Graphics.FromHdc` per page, disposed before `EndPage`/the next
   `ResetDC`, any draw exception aborts the whole job rather than limping on).
   Ported `legacy-testkit/capture-gdi-perpage.ps1`'s byte-diff self-test into
   `MixedMediaPrint.Core/Printing/Diagnostics/`, split the same way as Phase 0:
   `PclBodyExtractor` and `PerPageTrayVerdictEvaluator` are portable/pure (9 new
   xUnit tests, 22 total now), `PerPageTraySelfTest` is the thin Windows-only
   orchestrator that drives real `GdiPrintJob` runs to files.

   Added a small `MixedMediaPrint.Cli` console project — there's no WPF UI to
   drive hardware checkpoints until Phase 4, but every phase from here needs to
   run something real on the workstation, so this exists purely as that harness
   (`list-bins`, `list-media`, `device-info`, `selftest-tray` commands). It may
   get folded into or replaced by the Phase 4 UI later; it's a means to validate
   sooner, not a planned end-user surface.

   **First Windows checkpoint (still to run):**
   ```
   dotnet run --project MixedMediaPrint.Cli -- list-bins "SHARP BP-71C65 PCL6"
   dotnet run --project MixedMediaPrint.Cli -- selftest-tray "SHARP BP-71C65 PCL6" <TrayA id> <TrayB id>
   ```
   Expect `VERDICT: ... PER-PAGE TRAY WORKS via GDI` — proof the port didn't lose
   the one thing already decisively proven on this hardware.
3. **Phase 2 — Tab geometry + label rendering. C# port + Tier-0 preview done;
   hardware checkpoint outstanding.** Ported `TabGeometry`
   ([MixedMediaPrint.Core/Rendering/TabGeometry.cs](MixedMediaPrint.Core/Rendering/TabGeometry.cs))
   — the 5-cut template constants, position cycling, EMU→px conversion, margin
   auto-correction, and flip, matching `legacy-testkit/print-mixed-test.ps1`'s
   operation *order* specifically (offset → nudge → margin-correct → flip),
   **not** `legacy-testkit/capture-gdi-tabpos.ps1`'s earlier order (offset → flip
   → nudge → margin-correct) — the two scripts disagree, and print-mixed-test.ps1
   is the one with field evidence behind it (see the README's real, working
   commands). 15 pure unit tests, including hand-verified box math at an
   identity-DPI device.

   Built the auto-shrink-to-fit logic that never existed anywhere before
   (`ITextMeasurer` / `SkiaTextMeasurer` / `GdiPlusTextMeasurer` /
   `TabLabelFitter`) — closes the "custom text sizing... not yet stress-tested
   for fit" gap flagged in `legacy-testkit/testing-report.md`. Fully portable and
   unit-tested with a deterministic fake measurer, plus smoke tests against the
   real SkiaSharp measurer.

   Built the Tier-0 preview (`TabPreviewRenderer`, SkiaSharp, zero P/Invoke) —
   successor to `legacy-testkit/simulate-tabpos.py` but sharing the actual
   production geometry/fitting code. Added a `preview-tab` command to
   `MixedMediaPrint.Cli` and visually confirmed a rendered sample (long custom
   label "EMAIL CORRESPONDENCE" at tab #3): page outline, dashed imageable-margin
   boundary, a green (fits) box at the correct position, legible rotated text.
   Built `TabLabelRenderer` (Windows-only, `Graphics.FromHdc`) for the real
   print-time path in Phase 3.

   **Rotation-sign note:** derived that Skia's `RotateDegrees` and GDI+'s
   `RotateTransform` share the same y-down/positive-is-clockwise convention as
   each other, but this does **not** mean the raw GDI `CreateFont` escapement
   sign (900/2700, confirmed correct per-device on hardware) carries over
   unchanged — that historical API has its own quirky sign convention (see the
   code comments in `TabPreviewRenderer.cs`/`TabLabelRenderer.cs`). Risk R2
   stands: `TabLabelRenderer.Draw` takes `rotationDegrees` as a required,
   per-printer calibration input with no default — it must be re-verified on
   paper, not assumed.

   **Windows checkpoint (still to run):** reproduce
   `legacy-testkit/capture-gdi-tabpos.ps1`'s single-tab calibration print via
   `TabLabelRenderer` + `GdiPrintJob`, to nail down the real rotation sign and
   confirm the box lands correctly on physical tab stock.
4. **Phase 3 — PDF + job model. Done, including the CLI wiring for Tier-1
   dry runs.** Integrated PDFtoImage 5.4.0 for body-page rendering
   ([IPdfPageSource.cs](MixedMediaPrint.Core/Rendering/IPdfPageSource.cs) /
   [PdfiumPdfPageSource.cs](MixedMediaPrint.Core/Rendering/PdfiumPdfPageSource.cs))
   — genuinely cross-platform (PDFium ships native binaries for Windows/macOS/
   Linux), so unlike the GDI layer this is tested for real on macOS against an
   actual PDF fixture, not mocked. **This forced an unplanned but necessary
   upgrade:** PDFtoImage 5.4.0 requires SkiaSharp 4.150.1, whose major-version
   jump obsoletes (`error: true`) the `SKPaint.TextSize`/`MeasureText(string)`/
   `FontMetrics` API Phase 2 was built on. Ported `SkiaTextMeasurer` and
   `TabPreviewRenderer` to the current `SKFont`-based API
   (`SKCanvas.DrawText(..., SKTextAlign, SKFont, SKPaint)`); re-verified the
   preview PNG renders identically before/after. Confirmed the exact
   `PDFtoImage.Conversion`/`RenderOptions` API surface from source (GitHub) and
   NuGet before writing against it, rather than guessing signatures.

   Implemented `PrintJobPlan`/`BodyRangeItem`/`TabRunItem`/`PageInstance` and
   `JobExpander` ([MixedMediaPrint.Core/JobModel/](MixedMediaPrint.Core/JobModel/))
   — pure, portable, with the exact "N consecutive tabs × M copies in place"
   semantics from `legacy-testkit/print-tab-from-docx.ps1` (`Count=5,Copies=2` →
   `1,1,2,2,3,3,4,4,5,5`). Added `TrayResolver` (pattern → exact bin, fail-closed
   on zero *or* multiple matches — stricter than the original scripts, which
   only checked for zero). Implemented `PrintEngine`
   ([Execution/PrintEngine.cs](MixedMediaPrint.Core/Execution/PrintEngine.cs)):
   resolves trays from live capabilities → expands the job plan → drives
   `GdiPrintJob` with the right per-page DEVMODE, drawing PDF body pages via
   `Image.FromStream` + `Graphics.DrawImage` and tab pages via
   `TabLabelRenderer`, for `RunMode.DryRunToFile` or `RunMode.Physical`.

   Wired a `dryrun-job` command into `MixedMediaPrint.Cli` (body/TAB/body from a
   real PDF, the same shape `print-mixed-test.ps1` validated, scaled to
   arbitrary PDFs) so Phase 3's end-to-end path has a ready-made hardware
   checkpoint, not just unit tests. 74 tests total (19 new this phase), all
   passing; full solution builds with 0 warnings.

   **Windows checkpoint (still to run):** `dryrun-job` against
   `SHARP BP-71C65 PCL6` with a real multi-page PDF; inspect the captured file
   for correct tray sequencing, then (separately, deliberately) try
   `RunMode.Physical` once that looks right.
5. **Phase 4 — Full WPF UI. Built; unverified beyond `dotnet build` (see below).**
   Added the calibration persistence layer first
   ([Calibration/](MixedMediaPrint.Core/Calibration/):
   `PrinterCalibrationProfile`/`CalibrationScenario`/`JsonFileCalibrationStore`) —
   portable, JSON-on-disk, round-trip tested on macOS, seeded conceptually by the
   two real confirmed scenarios recorded earlier in this document. One
   refinement made while implementing: `RotationDegrees` stores the GDI+
   `Graphics.RotateTransform` angle `TabLabelRenderer` actually uses, not the
   historical raw-GDI escapement value — the plan's earlier sketch conflated the
   two; they're different conventions and only the new one is what this engine
   consumes.

   Built four screens as a linear MVVM wizard (`ShellViewModel` swaps
   `CurrentViewModel`; `App.xaml` maps each ViewModel type to its View via
   `DataTemplate`, so navigation is "ViewModel-first" — no code-behind
   navigation logic):
   - **PrinterSetupView** — pick a printer, see live bins/media/DPI, manage
     (tab-tray, body-tray) scenarios, run the per-page tray self-test from
     Phase 1 as a button instead of a CLI command.
   - **CalibrationTestPrintView** — live `TabPreviewRenderer` preview that
     updates as rotation/nudge/flip/tab-number change, plus a "print test tab"
     dry-run action.
   - **JobSetupView** — load a PDF, add body-page ranges and tab runs, **reorder
     via move-up/move-down buttons rather than drag-and-drop** — a deliberate
     substitution: functionally equivalent, but verifiable by reading the code
     where real drag-and-drop's visual behavior would not be, given this
     machine can't run WPF.
   - **RunView** — page-count summary, the three-tier `RunMode` selector
     (Preview literally just describes the expanded plan as text via
     `JobExpander`, touching nothing; DryRunToFile and Physical both go through
     `PrintEngine`), and the typed "PRINT" confirmation gate before
     `RunMode.Physical` — the same safety pattern every legacy script used.

   **What "built" means here, precisely:** `dotnet build` succeeds with 0
   warnings across all four projects, including full XAML markup compilation
   for every view (this catches x:Class mismatches, malformed XAML, unresolved
   types, and most `StaticResource` lookups). It does **not** catch `{Binding}`
   path typos, layout/visual issues, or runtime behavior — WPF resolves binding
   paths at runtime, and this machine cannot run a Windows GUI to observe that.
   Treat this phase as "compiles clean, logic reviewed" rather than "verified
   working" until it's actually run on Windows.
6. **Phase 5 — Hardware validation & risk burn-down.** Run against both known
   devices; run the isolated media-type-only byte-diff test to close R1;
   stress-test auto-fit across label lengths (R4); confirm the device-fingerprint
   mismatch guard actually fires (regression test for the `ERR-3` class of mistake).
7. **Phase 6 — Polish.** Optional `FPDF_RenderPage`-direct-to-HDC upgrade if job
   sizes warrant it; simple self-contained publish (single-workstation scope, no
   installer pipeline needed); in-app operator guidance replacing
   `legacy-testkit/PLAYBOOK.md`/`legacy-testkit/PLAYBOOK-printer-prep.md`.

## Verification

- Phases 0-3: `dotnet build` + `dotnet test` on macOS (Core + Tests projects only;
  App project requires Windows to build since it's `net10.0-windows`).
- Each "Windows checkpoint" above: run on the actual workstation against
  `SHARP BP-71C65 PCL6` (and `SHARP#1` if available), compare results against the
  documented expectations in `legacy-testkit/testing-report.md`'s Status Matrix
  (tray switching, tab position, rotation, body tray, mixed job, custom text).
- Final acceptance: a real multi-tab job printed end-to-end from a loaded PDF,
  checked against the same criteria as `legacy-testkit/PLAYBOOK.md`'s Scenario 1
  (tab page from the tab tray, body pages from the body tray, correct order) plus
  the new auto-fit and multi-tab-run behavior this app adds beyond the original
  script.
