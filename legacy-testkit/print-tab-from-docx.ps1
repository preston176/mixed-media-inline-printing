#requires -Version 5.1
<#
  print-tab-from-docx.ps1 -- end-to-end pipeline: resolve THIS printer's real tray/DPI/
  margin data, edit a single-page copy of 5th-cut-1-to-500.docx for each tab number in
  a range (real number substituted, position auto-corrected + manually nudged), then
  print them in sequence via Word COM automation -- silently, no visible window.

  -TabNumber is the FIRST tab to print; -Count (default 1) is how many CONSECUTIVE
  tabs to print after it, in order (1, 2, 3, ...). This matches how an operator loads
  a real 5-cut set: each physical sheet is already cut at a DIFFERENT one of the 5
  positions, stacked so position 1 feeds first -- so the script must print tab 1's
  content first, then tab 2's, etc., in the same order the tray will feed them, not
  the same tab repeated. Use -Copies (default 1) to additionally repeat EACH tab in
  the sequence that many times (e.g. -Count 5 -Copies 2 prints 1,1,2,2,3,3,4,4,5,5).

  Reuses, rather than duplicates:
    - GdiPrint.psm1's Get-GdiBins / Get-GdiDeviceInfo (same tray/DPI/margin resolution
      already confirmed working via the raw-GDI probe scripts)
    - edit-tab-docx.ps1 (the single-page docx-editing script, verified separately)

  TRAY SELECTION -- reasoned, not yet confirmed via Word on this driver:
    Word's Section.PageSetup.FirstPageTray/OtherPagesTray use the same DMBIN_* numbering
    as the DEVMODE dmDefaultSource field our GDI code already sets directly (this is
    long-standing, documented Windows printing behavior, not specific to this project) --
    so the SAME Tray1 bin ID Get-GdiBins resolves should carry over correctly. This is
    the first time this project prints via Word rather than raw GDI, though, so treat
    the first run as a real test of that assumption, not a given.

  MEDIA TYPE -- NOT set by this script. Word's object model has no documented property
  for DEVMODE's dmMediaType (a newer field than dmDefaultSource/tray). If the tab tray
  (whichever bin -TabTrayPattern resolves to -- Tray 1 by default, or e.g. the Bypass
  Tray) is configured at the printer's own control panel to feed Tab Paper by default
  (per PLAYBOOK.md's setup step), tray selection alone should be enough; if precise media-type
  control turns out to matter, it isn't reachable from here and needs setting elsewhere
  (the queue's default Printing Preferences, same mechanism already used for hole-punch).

  ALWAYS prints to the physical device -- opt-in only, requires typed confirmation.
  Word runs hidden (Visible=$false) but Word.Application / the printed page are real;
  this is not a dry run once you type PRINT.

  Run (operator loads the physical 5-cut set with position 1 on top):
    powershell -ExecutionPolicy Bypass -File .\print-tab-from-docx.ps1 -Printer "SHARP BP-71C65 PCL6" -TabNumber 1 -Count 5
#>

[CmdletBinding()]
param(
    [string] $TemplatePath,
    [Parameter(Mandatory)][string] $Printer,
    [Parameter(Mandatory)][int] $TabNumber,
    [int] $Count = 1,
    [string[]] $Text,                   # optional: custom label(s) instead of the bare
                                         # number, one per tab in the sequence in order
                                         # (e.g. -Text "January","February" for -Count 2).
                                         # Tabs beyond the end of this list fall back to
                                         # their bare number. TabNumber still selects
                                         # WHICH template position/geometry is used.
    [double] $NudgeXIn = 0,
    [double] $NudgeYIn = 0,
    [int] $Copies = 1,
    [string] $TabTrayPattern = '(?i)tray\s*1',  # which driver-reported bin feeds tab
                                                 # stock; e.g. '(?i)bypass' to use the
                                                 # Bypass Tray instead of Tray 1
    [switch] $FlipTabX,   # mirror the tab position horizontally -- use if switching to a
                           # different tray (e.g. Bypass) prints the tab mirrored, since
                           # some trays feed/register the sheet differently than others
    [switch] $FlipTabY    # same, vertically
)

$ErrorActionPreference = 'Stop'
# $PSScriptRoot isn't reliably populated yet while param() defaults are evaluated --
# only once the script body starts running (this is exactly what broke here before).
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $TemplatePath) { $TemplatePath = Join-Path $scriptDir '5th-cut-1-to-500.docx' }

Import-Module (Join-Path $scriptDir 'GdiPrint.psm1') -Force
function Fail { param([string] $m, [int] $c = 1) Write-Host "`nFAIL: $m" -ForegroundColor Red; exit $c }
if ($IsWindows -eq $false) { Fail "Windows-only. Run on the workstation in Windows PowerShell 5.1." 6 }
if (-not (Test-Path $TemplatePath)) { Fail "Template not found: $TemplatePath" 2 }
if ($TabNumber -lt 1 -or $TabNumber -gt 500) { Fail "TabNumber must be 1..500." 2 }
if ($Count -lt 1 -or ($TabNumber + $Count - 1) -gt 500) { Fail "TabNumber..TabNumber+Count-1 must stay within 1..500." 2 }

function Find-Id { param($Pairs, $Pattern) foreach ($p in $Pairs) { $id, $n = $p -split '\|', 2; if ($n -match $Pattern) { return [int]$id } } return $null }
function Find-Name { param($Pairs, $Id) foreach ($p in $Pairs) { $binId, $n = $p -split '\|', 2; if ([int]$binId -eq $Id) { return $n.Trim() } } return "bin id $Id" }

Write-Host "== Driver input bins ==" -ForegroundColor Cyan
$bins = @(Get-GdiBins -Printer $Printer); $bins | ForEach-Object { "  $_" }
$tTab = Find-Id $bins $TabTrayPattern
if ($null -eq $tTab) { Fail "No bin matching '$TabTrayPattern' found in the bin list above. Check -Printer or -TabTrayPattern." 5 }
$tTabName = Find-Name $bins $tTab
Write-Host ("Tab tray: '{0}' (id={1})" -f $tTabName, $tTab) -ForegroundColor Green

Write-Host ""
Write-Host "== Printer DPI / imageable area (GetDeviceCaps) ==" -ForegroundColor Cyan
$info = Get-GdiDeviceInfo -Printer $Printer
$info | Format-List | Out-String | Write-Host

# --- hard-sourced from 5th-cut-1-to-500.docx (see edit-tab-docx.ps1 for provenance) ---
$EMU_PER_INCH = 914400
$templateXEmu = 7162495
$templateWEmu = 557784
$templateHEmu = 1828800
$templateYByPosition = @{ 1 = 412394; 2 = 2174443; 3 = 4155033; 4 = 6071616; 5 = 7697419 }
$SAFETY_PX = 20
function Get-Correction { param($pos, $size, $limit)
    if ($pos -lt 0) { return -$pos + $SAFETY_PX }
    elseif (($pos + $size) -gt $limit) { return -(($pos + $size) - $limit) - $SAFETY_PX }
    else { return 0 }
}

$lastTab = $TabNumber + $Count - 1
Write-Host ""
Write-Host ("This will PHYSICALLY PRINT tabs {0}..{1} via Word on '{2}' ('{3}', {4} copy/copies each)." -f $TabNumber, $lastTab, $Printer, $tTabName, $Copies) -ForegroundColor Yellow
Write-Host "  Load the physical 5-cut set into '$tTabName' so tab $TabNumber's position is on TOP -- the tray feeds top-first, and this prints in that same order."
Write-Host "  Word runs hidden -- no window will appear, but this is a real print, not a preview."
$confirm = Read-Host "  Type PRINT to send to the device (anything else aborts)"
if ($confirm -ne 'PRINT') { Write-Host "  Aborted -- nothing sent." -ForegroundColor Yellow; exit 0 }

Write-Host ""
Write-Host "== Printing via Word (silent) ==" -ForegroundColor Cyan
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
try {
    $word.ActivePrinter = $Printer
    for ($tabNum = $TabNumber; $tabNum -le $lastTab; $tabNum++) {
        $position = (($tabNum - 1) % 5) + 1
        $yEmu = $templateYByPosition[$position]
        Write-Host ("--- Tab {0} -> cut position {1} of 5 ---" -f $tabNum, $position) -ForegroundColor Cyan

        $xIn = $templateXEmu / $EMU_PER_INCH
        $yIn = $yEmu / $EMU_PER_INCH
        $wIn = $templateWEmu / $EMU_PER_INCH
        $hIn = $templateHEmu / $EMU_PER_INCH
        $xPxPhysical = [int][Math]::Round($xIn * $info.DpiX)
        $yPxPhysical = [int][Math]::Round($yIn * $info.DpiY)
        $wPx = [int][Math]::Round($wIn * $info.DpiX)
        $hPx = [int][Math]::Round($hIn * $info.DpiY)
        $xPxGdiBase = $xPxPhysical - $info.PhysicalOffsetX
        $yPxGdiBase = $yPxPhysical - $info.PhysicalOffsetY
        $xPxGdi = $xPxGdiBase + [int][Math]::Round($NudgeXIn * $info.DpiX)
        $yPxGdi = $yPxGdiBase + [int][Math]::Round($NudgeYIn * $info.DpiY)
        $xPxGdi += Get-Correction $xPxGdi $wPx $info.HorzRes
        $yPxGdi += Get-Correction $yPxGdi $hPx $info.VertRes

        # Applied AFTER margin correction -- some trays (commonly Bypass vs. a cassette
        # tray) register/feed the sheet differently, which can mirror where content
        # lands. Same fix as capture-gdi-tabpos.ps1's FlipTabX/FlipTabY on the raw-GDI path.
        if ($FlipTabX) {
            $newX = $info.HorzRes - $xPxGdi - $wPx
            Write-Host ("  FlipTabX: x {0} -> {1}" -f $xPxGdi, $newX) -ForegroundColor Yellow
            $xPxGdi = $newX
        }
        if ($FlipTabY) {
            $newY = $info.VertRes - $yPxGdi - $hPx
            Write-Host ("  FlipTabY: y {0} -> {1}" -f $yPxGdi, $newY) -ForegroundColor Yellow
            $yPxGdi = $newY
        }

        $totalNudgeXIn = ($xPxGdi - $xPxGdiBase) / $info.DpiX
        $totalNudgeYIn = ($yPxGdi - $yPxGdiBase) / $info.DpiY
        Write-Host ("  Shift: x={0:N4}in  y={1:N4}in" -f $totalNudgeXIn, $totalNudgeYIn)

        $textIndex = $tabNum - $TabNumber
        # Hashtable splatting (not array splatting): array elements like '-Text' don't
        # get re-parsed as flag names, they just bind positionally as raw values --
        # exactly what broke this same pattern in print-mixed-test.ps1. A hashtable's
        # keys map to named parameters correctly.
        $textArgs = if ($Text -and $textIndex -lt $Text.Count) { @{ Text = $Text[$textIndex] } } else { @{} }
        if ($textArgs.Count) { Write-Host ("  Label: '{0}' (instead of the bare number)" -f $Text[$textIndex]) }

        $editedPath = Join-Path $scriptDir ("tab{0}-print.docx" -f $tabNum)
        & (Join-Path $scriptDir 'edit-tab-docx.ps1') -TemplatePath $TemplatePath -TabNumber $tabNum `
            -NudgeXIn $totalNudgeXIn -NudgeYIn $totalNudgeYIn -OutputPath $editedPath @textArgs
        if ($LASTEXITCODE) { Fail "edit-tab-docx.ps1 failed for tab $tabNum (exit $LASTEXITCODE)." 7 }

        $doc = $word.Documents.Open($editedPath, $false, $true)  # ReadOnly=$true, no prompts
        try {
            foreach ($section in $doc.Sections) {
                $section.PageSetup.FirstPageTray = $tTab
                $section.PageSetup.OtherPagesTray = $tTab
            }
            # No page-range args: $editedPath is deliberately a single-page document, so the
            # safe default (print everything in it) prints exactly one page each call -- see
            # edit-tab-docx.ps1 for why that avoids needing the WdPrintOutRange enum here.
            # Looping PrintOut() $Copies times rather than passing Word's Copies parameter
            # positionally -- not confident enough of PrintOut's exact positional argument
            # order from memory to risk a miscount silently landing the value elsewhere.
            for ($c = 1; $c -le $Copies; $c++) {
                Write-Host ("  Printing tab {0}, copy {1} of {2}..." -f $tabNum, $c, $Copies)
                $doc.PrintOut()
            }
        } finally {
            $doc.Close([ref]$false)
        }
    }
} finally {
    $word.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
}

Write-Host ""
Write-Host ("Sent tabs {0}..{1} to '{2}' via Word, in that order, {3} copy/copies each." -f $TabNumber, $lastTab, $Printer, $Copies) -ForegroundColor Green
Write-Host "  Check the physical sheets against the tray's actual feed order: does each printed number land on the matching physical cut position?" -ForegroundColor Green
