#requires -Version 5.1
<#
  capture-gdi.ps1 -- probe per-page media/tray via the native GDI path (DEVMODE + ResetDC),
  where the PrintTicket/XPS layer flattened it. Two gates:

    GATE 1 (info)  : render 3 pages (Plain / Tab / Plain) to a FILE and read the driver's
                     PCL for per-page MEDIATYPE / MEDIASOURCE. No paper, no device output.
    GATE 2 (paper) : actually print those 3 sheets on the physical printer. OPT-IN only
                     (-Print) and requires a typed confirmation, so it never prints by accident.

  Run:
    powershell -ExecutionPolicy Bypass -File .\capture-gdi.ps1            # Gate 1 only (safe)
    powershell -ExecutionPolicy Bypass -File .\capture-gdi.ps1 -Print     # Gate 1, then Gate 2 (paper)
#>

[CmdletBinding()]
param(
    [string] $Printer = 'SHARP#1',
    [string] $OutFile,
    [switch] $Print,
    [int]    $WaitSeconds = 45
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'GdiPrint.psm1') -Force

function Fail { param([string] $m, [int] $c = 1) Write-Host "`nFAIL: $m" -ForegroundColor Red; exit $c }

if ($IsWindows -eq $false) { Fail "Windows-only (GDI). Run on the workstation in Windows PowerShell 5.1." 6 }
if (-not $OutFile) { $OutFile = Join-Path (Join-Path $env:SystemRoot 'Temp\testkit-capture') 'gdi-mixed.prn' }
New-Item -ItemType Directory -Force -Path (Split-Path $OutFile) | Out-Null
Remove-Item $OutFile -Force -ErrorAction SilentlyContinue

# --- shared: what the driver exposes at the GDI level, and the IDs we need ---
Write-Host "== Driver media types (GDI DeviceCapabilities) ==" -ForegroundColor Cyan
$media = @(Get-GdiMediaTypes -Printer $Printer); $media | ForEach-Object { "  $_" }
Write-Host ""
Write-Host "== Driver input bins (GDI DeviceCapabilities) ==" -ForegroundColor Cyan
$bins = @(Get-GdiBins -Printer $Printer); $bins | ForEach-Object { "  $_" }

function Find-Id { param($Pairs, $Pattern)
    foreach ($p in $Pairs) { $id, $name = $p -split '\|', 2; if ($name -match $Pattern) { return [int]$id } }
    return $null
}
$plainId = Find-Id $media '(?i)^Plain-1$'; if ($null -eq $plainId) { $plainId = Find-Id $media '(?i)plain' }
$tabId   = Find-Id $media '(?i)tab'
$tray1Id = Find-Id $bins  '(?i)tray\s*1'
$tray2Id = Find-Id $bins  '(?i)tray\s*2'

Write-Host ""
Write-Host ("Resolved: Plain media id={0}  Tab media id={1}  Tray1 bin id={2}  Tray2 bin id={3}" -f `
    $plainId, $tabId, $tray1Id, $tray2Id) -ForegroundColor Green
if ($null -eq $tray1Id -or $null -eq $tray2Id) { Fail "Could not find Tray1/Tray2 in the driver's GDI bin list (above)." 5 }
if ($null -eq $plainId -or $null -eq $tabId) {
    Write-Host "  (This driver reports no media types via GDI DeviceCapabilities -> testing per-page TRAY only, via dmDefaultSource.)" -ForegroundColor Yellow
    $plainId = 0; $tabId = 0
}
$mediaIds = @($plainId, $tabId, $plainId)         # 0 => media not set (drive by tray)
$binIds   = @($tray2Id, $tray1Id, $tray2Id)       # page 2 from Tray1, pages 1/3 from Tray2

# =========================== GATE 1 -- INFO (byte-diff, to files, no paper) ===========================
# Reliable method: this driver hides the tray in the binary PXL, so we do NOT grep PJL.
# Render all-Tray2 / all-Tray1 / mixed with identical content and byte-compare the bodies.
Write-Host ""
Write-Host "================= GATE 1: INFO (render to files, no paper) =================" -ForegroundColor Cyan
$dir = Split-Path $OutFile
$fT2 = Join-Path $dir 'gate1-allT2.prn'; $fT1 = Join-Path $dir 'gate1-allT1.prn'; $fMx = Join-Path $dir 'gate1-mixed.prn'
Remove-Item $fT2, $fT1, $fMx -Force -ErrorAction SilentlyContinue
Invoke-GdiSameContent -Printer $Printer -OutFile $fT2 -BinIds @($tray2Id, $tray2Id, $tray2Id) | Out-Null
Invoke-GdiSameContent -Printer $Printer -OutFile $fT1 -BinIds @($tray1Id, $tray1Id, $tray1Id) | Out-Null
Invoke-GdiSameContent -Printer $Printer -OutFile $fMx -BinIds @($tray2Id, $tray1Id, $tray2Id) | Out-Null
foreach ($f in @($fT2, $fT1, $fMx)) {
    for ($t = 2; $t -le $WaitSeconds; $t += 2) { Start-Sleep -Seconds 2; if ((Test-Path $f) -and (Get-Item $f).Length -gt 0) { break } }
    if (-not (Test-Path $f) -or (Get-Item $f).Length -eq 0) { Fail "Gate 1 produced no file: $f" 4 }
}
function Get-PxlBody { param($File)
    $bytes = [IO.File]::ReadAllBytes($File); $x = [Text.Encoding]::GetEncoding('ISO-8859-1').GetString($bytes)
    $i = $x.IndexOf('@PJL ENTER LANGUAGE'); if ($i -lt 0) { return $bytes }
    $nl = $x.IndexOf("`n", $i); if ($nl -lt 0) { return $bytes }
    return $bytes[($nl + 1)..($bytes.Length - 1)]
}
function Bytes-Equal { param($A, $B) if ($A.Length -ne $B.Length) { return $false } for ($k = 0; $k -lt $A.Length; $k++) { if ($A[$k] -ne $B[$k]) { return $false } } return $true }
$bT2 = Get-PxlBody $fT2; $bT1 = Get-PxlBody $fT1; $bMx = Get-PxlBody $fMx
$applied = -not (Bytes-Equal $bT1 $bT2)
$perPage = $applied -and -not (Bytes-Equal $bMx $bT2) -and -not (Bytes-Equal $bMx $bT1)
Write-Host ("  bodies: allT2={0}  allT1={1}  mixed={2}   trayApplied={3}   perPage={4}" -f $bT2.Length, $bT1.Length, $bMx.Length, $applied, $perPage)
Write-Host ""
if ($perPage) {
    Write-Host "GATE 1 VERDICT: per-page TRAY reaches the driver via GDI -> inline is achievable." -ForegroundColor Green
} elseif ($applied) {
    Write-Host "GATE 1 VERDICT: tray applied job-level but collapses per-page." -ForegroundColor Red
} else {
    Write-Host "GATE 1 VERDICT: GDI tray field not applied at all." -ForegroundColor Red
}

# =========================== GATE 2 -- PHYSICAL PAPER ===========================
Write-Host ""
Write-Host "================= GATE 2: PHYSICAL PRINT (real paper) =================" -ForegroundColor Cyan
if (-not $Print) {
    Write-Host "  Skipped. Re-run with -Print to physically print 3 sheets on '$Printer'." -ForegroundColor Yellow
    return
}
if (-not $perPage) {
    Write-Host "  Gate 1 showed no per-page switching, so a physical print won't demonstrate mixed media." -ForegroundColor Yellow
}
Write-Host "  This will PHYSICALLY PRINT 3 sheets on '$Printer' (uses paper)." -ForegroundColor Yellow
Write-Host "  Expected if it works: page1 body (Tray 2), page2 TAB (Tray 1), page3 body (Tray 2)."
Write-Host "  Make sure Tray 1 = tab stock and Tray 2 = letter are loaded."
$confirm = Read-Host "  Type PRINT to send to the device (anything else aborts)"
if ($confirm -ne 'PRINT') { Write-Host "  Aborted -- nothing sent." -ForegroundColor Yellow; return }

$s2 = Invoke-GdiThreePage -Printer $Printer -MediaIds $mediaIds -BinIds $binIds   # no -OutFile => device
Write-Host "  $s2" -ForegroundColor Green
Write-Host "  Sent to '$Printer'. WATCH the printer: body / TAB / body, and note which tray fed each sheet."
