#requires -Version 5.1
<#
  capture-gdi-labeled.ps1 -- physical/visual confirmation of the per-page GDI tray
  result: prints 3 REAL pages, each with a large on-page text label naming the tray
  it was told to feed from, so you can confirm by eye which tray actually fed each
  sheet instead of reading it out of a byte-diff.

  This is NOT the decisive test (that's capture-gdi-perpage.ps1 / capture-gdi.ps1
  Gate 1, which needs IDENTICAL content per page to isolate the tray variable, since
  this driver hides tray/media in its binary PCL-XL body). Run this only after Gate 1
  already showed per-page works, as a human-readable confirmation on the real device.

  ALWAYS prints to the physical device -- opt-in only, requires typed confirmation.

  Run: powershell -ExecutionPolicy Bypass -File .\capture-gdi-labeled.ps1 -Printer "SHARP BP-71C65 PCL6"
#>

[CmdletBinding()]
param([string] $Printer = 'SHARP#1')

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'GdiPrint.psm1') -Force
function Fail { param([string] $m, [int] $c = 1) Write-Host "`nFAIL: $m" -ForegroundColor Red; exit $c }
if ($IsWindows -eq $false) { Fail "Windows-only. Run on the workstation in Windows PowerShell 5.1." 6 }

function Find-Id { param($Pairs, $Pattern)
    foreach ($p in $Pairs) { $id, $n = $p -split '\|', 2; if ($n -match $Pattern) { return [int]$id } }
    return $null
}

Write-Host "== Driver input bins (GDI DeviceCapabilities) ==" -ForegroundColor Cyan
$bins = @(Get-GdiBins -Printer $Printer); $bins | ForEach-Object { "  $_" }
$t1 = Find-Id $bins '(?i)tray\s*1'
$t2 = Find-Id $bins '(?i)tray\s*2'
if ($null -eq $t1 -or $null -eq $t2) { Fail "Tray1/Tray2 not found in the bin list above. Check -Printer." 5 }
Write-Host ("Tray1 id={0}  Tray2 id={1}" -f $t1, $t2) -ForegroundColor Green

Write-Host ""
Write-Host "== Driver media types (GDI DeviceCapabilities) ==" -ForegroundColor Cyan
$media = @(Get-GdiMediaTypes -Printer $Printer); $media | ForEach-Object { "  $_" }
$plainId = Find-Id $media '(?i)^Plain-1$'; if ($null -eq $plainId) { $plainId = Find-Id $media '(?i)plain' }
$tabId = Find-Id $media '(?i)tab'
if ($null -eq $plainId -or $null -eq $tabId) {
    Write-Host "  (No usable media types via GDI -> labels drive tray only, media left default.)" -ForegroundColor Yellow
    $plainId = 0; $tabId = 0
}

$binIds   = @($t2, $t1, $t2)
$mediaIds = @($plainId, $tabId, $plainId)
$labels = @(
    "PAGE 1`nEXPECT: TRAY 2 (BODY)",
    "PAGE 2`nEXPECT: TRAY 1 (TAB)",
    "PAGE 3`nEXPECT: TRAY 2 (BODY)"
)

Write-Host ""
Write-Host "This will PHYSICALLY PRINT 3 labeled sheets on '$Printer' (uses paper)." -ForegroundColor Yellow
Write-Host "  Make sure Tray 1 = tab stock and Tray 2 = letter/plain are loaded."
Write-Host "  Each sheet prints its own expected tray in large text -- read the paper, not a hex dump."
$confirm = Read-Host "  Type PRINT to send to the device (anything else aborts)"
if ($confirm -ne 'PRINT') { Write-Host "  Aborted -- nothing sent." -ForegroundColor Yellow; exit 0 }

$result = Invoke-GdiLabeledPages -Printer $Printer -Labels $labels -MediaIds $mediaIds -BinIds $binIds
Write-Host "  $result" -ForegroundColor Green
Write-Host "  Sent to '$Printer'. Read each sheet: does the printed label match the tray it actually fed from?"
