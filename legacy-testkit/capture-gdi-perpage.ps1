#requires -Version 5.1
<#
  capture-gdi-perpage.ps1 -- THE per-page verdict. We already confirmed the GDI tray
  field is applied (Tray1 vs Tray2 differ). Now: does the tray vary PER PAGE in one job?

  Renders three 3-page jobs with IDENTICAL content, varying only the per-page tray:
    all-Tray2 (T2,T2,T2), all-Tray1 (T1,T1,T1), mixed (T2,T1,T2)  -- all to files, no paper.
  The PXL body is deterministic (identical content => identical bytes except the tray),
  so:
    mixed differs from BOTH all-Tray1 and all-Tray2  -> page 2 got a different tray -> PER-PAGE WORKS.
    mixed == all-Tray2 (or == all-Tray1)             -> collapsed to one tray -> per-page NOT honored.

  Run: powershell -ExecutionPolicy Bypass -File .\capture-gdi-perpage.ps1
#>

[CmdletBinding()]
param([string] $Printer = 'SHARP#1', [int] $WaitSeconds = 30)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'GdiPrint.psm1') -Force
function Fail { param([string] $m, [int] $c = 1) Write-Host "`nFAIL: $m" -ForegroundColor Red; exit $c }
if ($IsWindows -eq $false) { Fail "Windows-only. Run on the workstation in Windows PowerShell 5.1." 6 }

$dir = Join-Path $env:SystemRoot 'Temp\testkit-capture'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$fT2 = Join-Path $dir 'pp-allT2.prn'
$fT1 = Join-Path $dir 'pp-allT1.prn'
$fMx = Join-Path $dir 'pp-mixed.prn'
Remove-Item $fT2, $fT1, $fMx -Force -ErrorAction SilentlyContinue

$bins = @(Get-GdiBins -Printer $Printer)
function Find-Id { param($Pairs, $Pattern) foreach ($p in $Pairs) { $id, $n = $p -split '\|', 2; if ($n -match $Pattern) { return [int]$id } } return $null }
$t1 = Find-Id $bins '(?i)tray\s*1'
$t2 = Find-Id $bins '(?i)tray\s*2'
if ($null -eq $t1 -or $null -eq $t2) { Fail "Tray1/Tray2 not found." 5 }
Write-Host ("Tray1 id={0}  Tray2 id={1}" -f $t1, $t2) -ForegroundColor Green

Write-Host "Rendering all-Tray2, all-Tray1, and mixed (T2,T1,T2) -- identical content, to files ..." -ForegroundColor Cyan
Write-Host ("  " + (Invoke-GdiSameContent -Printer $Printer -OutFile $fT2 -BinIds @($t2, $t2, $t2)))
Write-Host ("  " + (Invoke-GdiSameContent -Printer $Printer -OutFile $fT1 -BinIds @($t1, $t1, $t1)))
Write-Host ("  " + (Invoke-GdiSameContent -Printer $Printer -OutFile $fMx -BinIds @($t2, $t1, $t2)))
foreach ($f in @($fT2, $fT1, $fMx)) {
    for ($t = 2; $t -le $WaitSeconds; $t += 2) { Start-Sleep -Seconds 2; if ((Test-Path $f) -and (Get-Item $f).Length -gt 0) { break } }
    if (-not (Test-Path $f) -or (Get-Item $f).Length -eq 0) { Fail "No output for $f." 4 }
}

function Get-PxlBody { param($File)
    $bytes = [IO.File]::ReadAllBytes($File)
    $s = [Text.Encoding]::GetEncoding('ISO-8859-1').GetString($bytes)
    $i = $s.IndexOf('@PJL ENTER LANGUAGE'); if ($i -lt 0) { return $bytes }
    $nl = $s.IndexOf("`n", $i); if ($nl -lt 0) { return $bytes }
    return $bytes[($nl + 1)..($bytes.Length - 1)]
}
function Bytes-Equal { param($A, $B)
    if ($A.Length -ne $B.Length) { return $false }
    for ($k = 0; $k -lt $A.Length; $k++) { if ($A[$k] -ne $B[$k]) { return $false } }
    return $true
}
function Diff-Count { param($A, $B)
    $n = [Math]::Min($A.Length, $B.Length); $d = [Math]::Abs($A.Length - $B.Length)
    for ($k = 0; $k -lt $n; $k++) { if ($A[$k] -ne $B[$k]) { $d++ } }
    return $d
}

$bT2 = Get-PxlBody $fT2; $bT1 = Get-PxlBody $fT1; $bMx = Get-PxlBody $fMx
$allDiffer   = -not (Bytes-Equal $bT1 $bT2)
$mixEqT2     = Bytes-Equal $bMx $bT2
$mixEqT1     = Bytes-Equal $bMx $bT1

Write-Host ""
Write-Host "== RESULT ==" -ForegroundColor Yellow
Write-Host ("  body sizes: allT2={0}  allT1={1}  mixed={2}" -f $bT2.Length, $bT1.Length, $bMx.Length)
Write-Host ("  all-Tray1 vs all-Tray2 differ : {0}" -f $allDiffer)
Write-Host ("  mixed == all-Tray2            : {0}" -f $mixEqT2)
Write-Host ("  mixed == all-Tray1            : {0}" -f $mixEqT1)
Write-Host ("  bytes differing mixed vs allT2: {0}   mixed vs allT1: {1}" -f (Diff-Count $bMx $bT2), (Diff-Count $bMx $bT1))

Write-Host ""
if (-not $allDiffer) {
    Write-Host "VERDICT: even all-Tray1 vs all-Tray2 are identical here -> tray not applied. (Unexpected; rerun capture-gdi-tray.ps1.)" -ForegroundColor Red
}
elseif ($mixEqT2 -or $mixEqT1) {
    Write-Host "VERDICT: mixed == a single-tray job -> the driver COLLAPSED per-page tray to one tray." -ForegroundColor Red
    Write-Host "  -> per-page tray is NOT honored in one job. Inline mixed-media is not achievable via this path."
} else {
    Write-Host "VERDICT: mixed differs from BOTH all-Tray1 and all-Tray2 -> the tab page got a DIFFERENT tray." -ForegroundColor Green
    Write-Host "  -> PER-PAGE TRAY WORKS via GDI. Mixed-media inline IS achievable on this driver."
    Write-Host "     Next: confirm on real paper with 'capture-gdi.ps1 -Print' (tab stock in Tray 1, letter in Tray 2)."
}
