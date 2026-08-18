---
title: "Mixed-Media Inline Printing: Testing Progress Report"
author: "Diagnostic testkit, Sessions 1 through 6"
date: "2026-08-07"
---

**Where it stands.** Every architectural question has a confirmed answer: the right tray feeds, in the right job structure, with the right rotation. The one open item is a numeric position calibration, not a design question.

# Part 1: What Happened

## Session 1: PrintTicket/XPS submission (BP-70C65)

PrintTicket/XPS submission does not work on the BP-70C65's v3/GDI driver: it collapses per-page settings to one value for the whole job. The only theoretical fix, a V4/XPSDrv driver, was declined for the production printer. This path is closed for good on this hardware. See `BP-70C65-Phase1-Findings.docx` for the full evidence trail.

## Session 2: GDI probe (BP-71C65)

Raw GDI (`DEVMODE` plus `ResetDC` between pages) preserves per-page tray switching within one job on the BP-71C65, the workaround every later session built on. Tab content itself was not yet right: position and visibility issues remained.

## Session 3: Position debugging

Tab placement was significantly off, including a slot-mapping mismatch where one tab's content appeared at another tab's physical position.

## Session 4: Rotation debugging

Position moved closer to correct, but text was printing mirrored and backwards. Root cause: rotation direction is per-device, not a fixed constant, and needed separate calibration per printer.

## Session 5: First mixed-media job

The first real mixed-media job: body pages correctly fed from Tray 2, and tab position was structurally correct, with a residual vertical offset (text landing slightly low).

## Session 6: Current state

Correct tray and position targeting confirmed overall, with the same slight low offset still being narrowed down. Using the copier's own position preset instead of a software nudge was tried and rejected: it shifts the whole page rather than just the tab, moving unrelated content as a side effect.

## Status Matrix

| Session | Tray switching | Tab position | Rotation | Body tray | Mixed job (1 job) | Custom text |
|---|---|---|---|---|---|---|
| 1: PrintTicket/XPS (BP-70C65) | ✗ | n/a | n/a | n/a | ✗ | n/a |
| 2: GDI probe (BP-71C65) | ✓ | ✗ | n/a | n/a | n/a | n/a |
| 3: Position debugging | ✓ | ✗ | n/a | n/a | n/a | n/a |
| 4: Rotation debugging | ✓ | ✗ (closer) | ✗ | n/a | n/a | n/a |
| 5: First mixed job | ✓ | ✓ (structural) | ✓ | ✓ | ✓ | n/a |
| 6: Current | ✓ | ⚠ (slightly low) | ✓ | ✓ | ✓ | ✓ |

✓ confirmed working. ✗ confirmed not working. ⚠ working, not yet exact. n/a not applicable or not tested that session.

## Open Items

**Final X/Y nudge value.** Structurally correct; bisecting between a value that undershoots and one that overshoots to find the exact figure.

**Copier-side position presets are off the table as a fix.** Confirmed to shift unrelated content (body pages) along with the tab.

**Custom text sizing.** The tab text box is a fixed 0.61in wide; multi-character labels beyond a bare number have not been stress-tested for fit.

```{=openxml}
<w:p><w:r><w:br w:type="page"/></w:r></w:p>
```

# Part 2: The Technical Side

Scripts, commands, and root causes, grouped by the date they were actually run. Reconstructed from chat history: confidence varies. The 29 July and 7 August boundaries are anchored to explicit context markers seen during those exchanges; the split between 30 July and the following session is inferred from work sequence, not a hard marker, since not every exchange carries a timestamp. Flag anything misplaced here.

## 29 July (high confidence)

- `testkit.ps1 query-caps`: discovered the queue is `SHARP BP-71C65 PCL6`, a different physical device than the originally documented `SHARP#1` (BP-70C65).
- `capture-gdi.ps1`: fixed a printer-name mismatch (the script's default did not match the real queue name), then confirmed per-page tray switching works via raw GDI.
- `capture-gdi-labeled.ps1`: added on-page text labels naming the expected tray, for physical/visual confirmation.

## 30 July (approximate)

- Analysis of `5th-cut-1-to-500.docx` (the real tab-stock template): extracted exact tab position, box size, and rotation data directly from the template's OOXML.
- `capture-gdi-tabpos.ps1` (first version): positioned and rotated tab text using the template's real coordinates; found the literal template position fails this printer's margin check.
- First `tabpos-preview.pdf` simulation: a dependency-free PDF mockup to check position and rotation candidates before spending paper.

## Yesterday, 6 August (approximate)

- `capture-gdi-tabpos.ps1` refinements: manual nudge parameter, single-page mode, multi-copy mode, proper text centering, automatic margin correction, tray-mirroring flags.
- Confirmed the correct rotation direction is per-device: `900` on the BP-71C65, `2700` on `SHARP#1`, opposite of each other.
- Fixed a font-size bug where the printed text came out much smaller than intended on `SHARP#1`, traced to a hardcoded pixel size that only looked right at the BP-71C65's specific DPI.

## Today, 7 August (high confidence)

- `edit-tab-docx.ps1`: edits a copy of the real template docx directly (position, rotation-safe text substitution) instead of redrawing it via raw GDI.
- `print-tab-from-docx.ps1`: silent, hidden Word automation that prints the edited docx, including sequential tab ranges (`-Count`) for a real 5-cut set loaded in physical order.
- `print-mixed-test.ps1`: the decisive BODY/TAB/BODY mixed-media test through Word's own print pipeline, not just raw GDI, confirming Word preserves per-page tray switching too.
- Several bug fixes surfaced only by running on the real machine: a PowerShell parameter-default timing issue, a missing .NET assembly reference, a false-negative match check, and an array-vs-hashtable splatting mistake that misdirected a parameter value.
- Added `-Text` to print custom labels instead of the bare tab number.
- This report.
