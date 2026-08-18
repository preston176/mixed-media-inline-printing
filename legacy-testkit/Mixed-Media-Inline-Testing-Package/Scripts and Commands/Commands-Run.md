# Commands Run

Reference list of the actual command lines executed against the printers during testing. Grouped by script, not strictly by date (see the status report's Part 2 for the date grouping and its confidence caveats).

## Discovery

```powershell
powershell -ExecutionPolicy Bypass -File .\testkit.ps1 query-caps
```
Resolved the real queue name and confirmed `SHARP BP-71C65 PCL6` is a different physical device from the originally documented `SHARP#1` (BP-70C65).

## Raw GDI probe (per-page tray switching, before the docx pipeline existed)

```powershell
powershell -ExecutionPolicy Bypass -File .\capture-gdi.ps1 -Printer "SHARP BP-71C65 PCL6" -Print
powershell -ExecutionPolicy Bypass -File .\capture-gdi-labeled.ps1 -Printer "SHARP BP-71C65 PCL6"
```

## Tab position and rotation calibration (raw GDI)

```powershell
powershell -ExecutionPolicy Bypass -File .\capture-gdi-tabpos.ps1 -Printer "SHARP BP-71C65 PCL6" -TabNumber 1
powershell -ExecutionPolicy Bypass -File .\capture-gdi-tabpos.ps1 -Printer "SHARP BP-71C65 PCL6" -TabNumber 1 -NudgeXIn -0.625 -SinglePage
powershell -ExecutionPolicy Bypass -File .\capture-gdi-tabpos.ps1 -Printer "SHARP BP-71C65 PCL6" -TabNumber 2 -NudgeXIn -0.625 -SinglePage -Copies 5
powershell -ExecutionPolicy Bypass -File .\capture-gdi-tabpos.ps1 -Printer "SHARP#1" -TabNumber 2 -SinglePage -Copies 5 -EscapementTenthDeg 2700
```
Confirmed rotation direction is per-device: `900` correct on the BP-71C65, `2700` correct on `SHARP#1` (opposite of each other).

## Docx-driven pipeline (current production path)

```powershell
powershell -ExecutionPolicy Bypass -File .\edit-tab-docx.ps1 -TabNumber 2 -NudgeXIn -0.143333 -NudgeYIn 0
```
```powershell
powershell -ExecutionPolicy Bypass -File .\print-tab-from-docx.ps1 -Printer "SHARP BP-71C65 PCL6" -TabNumber 2
powershell -ExecutionPolicy Bypass -File .\print-tab-from-docx.ps1 -Printer "SHARP#1" -TabNumber 2
powershell -ExecutionPolicy Bypass -File .\print-tab-from-docx.ps1 -Printer "SHARP#1" -TabNumber 1 -Count 5
```
```powershell
powershell -ExecutionPolicy Bypass -File .\print-mixed-test.ps1 -Printer "SHARP#1" -TabNumber 2
powershell -ExecutionPolicy Bypass -File .\print-mixed-test.ps1 -Printer "SHARP BP-71C65 PCL6" -TabNumber 2 -Copies 5
powershell -ExecutionPolicy Bypass -File .\print-mixed-test.ps1 -Printer "SHARP BP-71C65 PCL6" -TabNumber 2 -NudgeXIn -0.625 -Text "RECORDS"
```
`print-mixed-test.ps1` is the decisive test: one Word `PrintOut()` call, one job, BODY (Tray 2) / TAB (Tray 1) / BODY (Tray 2), confirming Word's own print pipeline preserves per-page tray switching, not just raw GDI.

## Notes on fixes discovered only by running these on the real machine

- Printer-name mismatch: several scripts defaulted to `-Printer 'SHARP#1'`, which silently failed against `SHARP BP-71C65 PCL6` until passed explicitly.
- `System.IO.Compression.ZipArchiveMode` required loading `System.IO.Compression` in addition to `System.IO.Compression.FileSystem` — two separate assemblies.
- `$PSScriptRoot` is not reliably populated while a script's own `param()` block default values are being evaluated, only once the script body starts running.
- A zero-delta regex replace (e.g. no Y nudge needed) was indistinguishable from "no match found" when checked by comparing text before/after; fixed by checking `[regex]::IsMatch` directly.
- Array splatting (`@('-Text', $value)`) does not re-parse elements as named parameters; only hashtable splatting (`@{ Text = $value }`) does.
