#requires -Version 5.1
<#
  capture-gdi-tabpos.ps1 -- prints the TAB label at the REAL position from the
  physical tab-stock template (5th-cut-1-to-500.docx), not a placeholder box.

  Where the numbers below come from (extracted directly from the template's OOXML,
  not guessed -- see each tab's <wp:anchor>/<wp:positionH>/<wp:positionV>/<wp:extent>):
    - 500 individual tab pages, each one floating text box anchored relative to the
      PAGE (not the margin), constant across all 500:
        X offset = 7162495 EMU, box width = 557784 EMU, box height = 1828800 EMU
    - Y offset cycles through exactly 5 values (the 5 physical cut positions),
      repeating every 5 tabs (tab N -> position ((N-1) mod 5) + 1):
        position 1 = 412394 EMU   position 2 = 2174443 EMU   position 3 = 4155033 EMU
        position 4 = 6071616 EMU  position 5 = 7697419 EMU
    - Font: Arial Bold, 14pt (sz=28 half-points), paragraph centered.
    - wps:bodyPr vert="vert" -- the text is in WORD'S VERTICAL TEXT MODE (each
      character rotated 90 deg so the label reads correctly when the tab flap is
      viewed side-on). This is reproduced here via CreateFont's escapement.
      ROTATION DIRECTION IS PER-DEVICE, NOT A WINDOWS-WIDE CONSTANT -- confirmed
      by testing both printers, not assumed:
        "SHARP BP-71C65 PCL6" -> -EscapementTenthDeg 900 is correct (2700 backwards)
        "SHARP#1" (BP-70C65)  -> -EscapementTenthDeg 2700 is correct (900 backwards)
      The script's default (900) matches the BP-71C65 only. ALWAYS pass
      -EscapementTenthDeg explicitly for SHARP#1, and re-verify from scratch
      (print one sheet, check by eye) on any printer neither of these two.
  (914400 EMU = 1 inch; page is 8.5x11in / 12240x15840 twips, confirmed from the
  same template's <w:pgSz>.)

  Also queries this PRINTER'S actual DPI and imageable area (GetDeviceCaps) to:
    (a) convert the above inches into this device's real pixels -- never hardcoded
        for a specific DPI, and
    (b) check whether the computed box is even inside the printable area before
        sending anything. If the box falls outside it, the script AUTO-CORRECTS by
        the minimum amount needed (computed fresh from THIS printer's own margins,
        never a hardcoded per-device number) rather than requiring a manually-tuned
        -NudgeXIn/-NudgeYIn per machine.

  Works against ANY queue passed via -Printer -- e.g. both SHARP#1 (BP-70C65) and
  "SHARP BP-71C65 PCL6" (confirmed different hardware) -- since every printer-specific
  fact (bins, media, DPI, margins) is queried live, never assumed from the model name.
  TWO things are genuinely per-device and can't be auto-detected, both confirmed to
  actually differ between these two printers (not just theoretical risks):
    - Tray feed orientation -- use -FlipTabX / -FlipTabY once you've confirmed on
      paper which axis (if any) a given printer's tab tray mirrors.
    - Rotation direction (-EscapementTenthDeg) -- see above. Re-verify per printer;
      do not assume the default carries over.

  ALWAYS prints to the physical device -- opt-in only, requires typed confirmation.

  Run (repeat per printer, with that printer's exact queue name):
    powershell -ExecutionPolicy Bypass -File .\capture-gdi-tabpos.ps1 -Printer "SHARP#1" -TabNumber 1
    powershell -ExecutionPolicy Bypass -File .\capture-gdi-tabpos.ps1 -Printer "SHARP BP-71C65 PCL6" -TabNumber 1
#>

[CmdletBinding()]
param(
    [string] $Printer = 'SHARP#1',
    [int] $TabNumber = 1,               # 1..500 -- only (TabNumber-1) % 5 matters for position
    [int] $EscapementTenthDeg = 900,    # GDI tenths-of-a-degree; 900 confirmed correct on paper (2700 printed backwards)
    [int] $FontHeight = 0,               # 0 = auto-compute from this printer's real DPI (target: the
                                         # template's actual 14pt Arial Bold) -- pass explicitly to override
    [double] $NudgeXIn = 0,             # manual fine-tune shift, inches (+right/-left) -- for matching the physical
                                         # tab's real position; the hardware margin itself is now auto-corrected below
    [double] $NudgeYIn = 0,
    [switch] $FlipTabX,                  # mirror the TAB box horizontally -- use if this printer's tab tray feeds
                                         # mirrored left-right vs the body tray (confirm on paper first)
    [switch] $FlipTabY,                  # same, vertically (top-to-bottom)
    [switch] $SinglePage,                # print only the TAB sheet -- faster iteration while calibrating
    [int] $Copies = 1                    # repeat the SAME tab sheet N times (single-page mode only)
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'GdiPrint.psm1') -Force
function Fail { param([string] $m, [int] $c = 1) Write-Host "`nFAIL: $m" -ForegroundColor Red; exit $c }
if ($IsWindows -eq $false) { Fail "Windows-only. Run on the workstation in Windows PowerShell 5.1." 6 }

# --- hard-sourced from 5th-cut-1-to-500.docx (see header) ---
$EMU_PER_INCH = 914400
$templateXEmu = 7162495
$templateWEmu = 557784
$templateHEmu = 1828800
$templateYByPosition = @{ 1 = 412394; 2 = 2174443; 3 = 4155033; 4 = 6071616; 5 = 7697419 }

$position = (($TabNumber - 1) % 5) + 1
$yEmu = $templateYByPosition[$position]
Write-Host ("Tab #{0} -> cut position {1} of 5" -f $TabNumber, $position) -ForegroundColor Cyan

function Find-Id { param($Pairs, $Pattern) foreach ($p in $Pairs) { $id, $n = $p -split '\|', 2; if ($n -match $Pattern) { return [int]$id } } return $null }

Write-Host ""
Write-Host "== Driver input bins ==" -ForegroundColor Cyan
$bins = @(Get-GdiBins -Printer $Printer); $bins | ForEach-Object { "  $_" }
$tBody = Find-Id $bins '(?i)tray\s*2'
$tTab  = Find-Id $bins '(?i)tray\s*1'
if ($null -eq $tBody -or $null -eq $tTab) { Fail "Tray1/Tray2 not found in the bin list above. Check -Printer." 5 }

Write-Host ""
Write-Host "== Driver media types ==" -ForegroundColor Cyan
$media = @(Get-GdiMediaTypes -Printer $Printer); $media | ForEach-Object { "  $_" }
$mBody = Find-Id $media '(?i)^Plain-1$'; if ($null -eq $mBody) { $mBody = Find-Id $media '(?i)plain' }
$mTab  = Find-Id $media '(?i)tab'
if ($null -eq $mBody -or $null -eq $mTab) {
    Write-Host "  (No usable media types via GDI -> driving by tray only.)" -ForegroundColor Yellow
    $mBody = 0; $mTab = 0
}

Write-Host ""
Write-Host "== Printer DPI / imageable area (GetDeviceCaps) ==" -ForegroundColor Cyan
$info = Get-GdiDeviceInfo -Printer $Printer
$info | Format-List | Out-String | Write-Host

if ($FontHeight -eq 0) {
    # Auto-compute from THIS printer's real DPI to match the template's actual 14pt Arial
    # Bold (sz=28 half-points) -- a fixed device-unit number would only look right at
    # whatever DPI it happened to be tuned against (confirmed: looked fine at 600dpi,
    # came out much smaller on a printer with a different DPI).
    $FontHeight = [int][Math]::Round(14 * $info.DpiY / 72)
    Write-Host ("Font height auto-computed for this printer: {0}px (target 14pt Arial Bold, DPI={1})" -f $FontHeight, $info.DpiY) -ForegroundColor Cyan
}

# template inches -> this printer's pixels
$xIn = $templateXEmu / $EMU_PER_INCH
$yIn = $yEmu / $EMU_PER_INCH
$wIn = $templateWEmu / $EMU_PER_INCH
$hIn = $templateHEmu / $EMU_PER_INCH
$xPxPhysical = [int][Math]::Round($xIn * $info.DpiX)
$yPxPhysical = [int][Math]::Round($yIn * $info.DpiY)
$wPx = [int][Math]::Round($wIn * $info.DpiX)
$hPx = [int][Math]::Round($hIn * $info.DpiY)

Write-Host ("Template position (inches from page top-left): x={0:N3} y={1:N3} w={2:N3} h={3:N3}" -f $xIn, $yIn, $wIn, $hIn)
Write-Host ("Same, in THIS printer's pixels (physical-page origin): x={0} y={1} w={2} h={3}" -f $xPxPhysical, $yPxPhysical, $wPx, $hPx)

# GDI draws relative to the IMAGEABLE area's origin, not the physical page's -- shift by the offset.
$xPxGdi = $xPxPhysical - $info.PhysicalOffsetX
$yPxGdi = $yPxPhysical - $info.PhysicalOffsetY
Write-Host ("Offset-adjusted for GDI drawing (imageable-area origin): x={0} y={1}" -f $xPxGdi, $yPxGdi) -ForegroundColor Green

if ($FlipTabX) {
    $newX = $info.HorzRes - $xPxGdi - $wPx
    Write-Host ("FlipTabX: x {0} -> {1} (mirrored within this printer's imageable width {2}px)" -f $xPxGdi, $newX, $info.HorzRes) -ForegroundColor Yellow
    $xPxGdi = $newX
}
if ($FlipTabY) {
    $newY = $info.VertRes - $yPxGdi - $hPx
    Write-Host ("FlipTabY: y {0} -> {1} (mirrored within this printer's imageable height {2}px)" -f $yPxGdi, $newY, $info.VertRes) -ForegroundColor Yellow
    $yPxGdi = $newY
}

if ($NudgeXIn -ne 0 -or $NudgeYIn -ne 0) {
    $nudgeXPx = [int][Math]::Round($NudgeXIn * $info.DpiX)
    $nudgeYPx = [int][Math]::Round($NudgeYIn * $info.DpiY)
    $xPxGdi += $nudgeXPx
    $yPxGdi += $nudgeYPx
    Write-Host ("Applied manual nudge: x{0:N3}in ({1}px) y{2:N3}in ({3}px) -> box now at x={4} y={5}" -f `
        $NudgeXIn, $nudgeXPx, $NudgeYIn, $nudgeYPx, $xPxGdi, $yPxGdi) -ForegroundColor Yellow
    Write-Host "  (this moves the label away from the template's literal position -- verify on paper it still lands on the physical tab)" -ForegroundColor Yellow
}

Write-Host ""
$SAFETY_PX = 20   # small buffer past the exact boundary; real-world rasterization has some slop
function Get-Correction { param($pos, $size, $limit)
    if ($pos -lt 0) { return -$pos + $SAFETY_PX }
    elseif (($pos + $size) -gt $limit) { return -(($pos + $size) - $limit) - $SAFETY_PX }
    else { return 0 }
}
$corrX = Get-Correction $xPxGdi $wPx $info.HorzRes
$corrY = Get-Correction $yPxGdi $hPx $info.VertRes
if ($corrX -ne 0 -or $corrY -ne 0) {
    Write-Host ("MARGIN CHECK: box falls outside this printer's imageable area (0..{0} x 0..{1} px)." -f $info.HorzRes, $info.VertRes) -ForegroundColor Red
    Write-Host ("  AUTO-CORRECTING (computed fresh for THIS printer, not a hardcoded value): x+={0}px ({1:N3}in) y+={2}px ({3:N3}in)" -f `
        $corrX, ($corrX / $info.DpiX), $corrY, ($corrY / $info.DpiY)) -ForegroundColor Yellow
    $xPxGdi += $corrX; $yPxGdi += $corrY
    Write-Host ("  Box now at x={0} y={1} -- this moves the label off the template's literal position; verify it still lands on the physical tab." -f $xPxGdi, $yPxGdi) -ForegroundColor Yellow
} else {
    Write-Host "MARGIN CHECK: PASS -- the tab box is fully inside this printer's imageable area." -ForegroundColor Green
}

if ($Copies -gt 1 -and -not $SinglePage) { Fail "-Copies only applies with -SinglePage (the 3-page body/TAB/body test always prints once)." 2 }

Write-Host ""
if ($SinglePage) {
    Write-Host ("This will PHYSICALLY PRINT {0} TAB sheet(s) on '$Printer' (uses paper, tray/media = tab stock)." -f $Copies) -ForegroundColor Yellow
    Write-Host "  Make sure Tray 1 = tab stock is loaded."
} else {
    Write-Host "This will PHYSICALLY PRINT 3 sheets on '$Printer' (uses paper)." -ForegroundColor Yellow
    Write-Host "  Make sure Tray 1 = tab stock and Tray 2 = letter/plain are loaded."
}
Write-Host ("  Draws a box outline at the computed position plus the number '{0}'," -f $TabNumber)
Write-Host "  rotated (escapement=$EscapementTenthDeg -- 900 is the confirmed-correct default; 2700 was backwards)."
$confirm = Read-Host "  Type PRINT to send to the device (anything else aborts)"
if ($confirm -ne 'PRINT') { Write-Host "  Aborted -- nothing sent." -ForegroundColor Yellow; exit 0 }

if ($SinglePage) {
    $result = Invoke-GdiTabPositionOnePage -Printer $Printer `
        -TabMediaId $mTab -TabBinId $tTab `
        -TabText "$TabNumber" -TabX $xPxGdi -TabY $yPxGdi -TabW $wPx -TabH $hPx `
        -EscapementTenthDeg $EscapementTenthDeg -FontHeight $FontHeight -Copies $Copies
} else {
    $result = Invoke-GdiTabPositionTest -Printer $Printer `
        -BodyMediaId $mBody -BodyBinId $tBody -TabMediaId $mTab -TabBinId $tTab `
        -TabText "$TabNumber" -TabX $xPxGdi -TabY $yPxGdi -TabW $wPx -TabH $hPx `
        -EscapementTenthDeg $EscapementTenthDeg -FontHeight $FontHeight
}
Write-Host "  $result" -ForegroundColor Green
Write-Host "  Sent to '$Printer'. Check the TAB sheet: does the box land on the physical tab, and does the number read right-side up?"
