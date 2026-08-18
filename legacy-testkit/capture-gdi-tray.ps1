#requires -Version 5.1
<#
  capture-gdi-tray.ps1 -- CONTROL: does the GDI DEVMODE tray field (dmDefaultSource) take
  effect at all on this driver? Prints ONE identical page to Tray 1 and one to Tray 2
  (rendered to files, no paper), then compares the driver's output.

  This settles whether the tray change is applied before we ask whether per-page works.
  This driver hides the tray in the binary PCL-XL body (not a PJL line), so we compare
  the PJL text AND the binary body.

  Run: powershell -ExecutionPolicy Bypass -File .\capture-gdi-tray.ps1
#>

[CmdletBinding()]
param([string] $Printer = 'SHARP#1', [int] $WaitSeconds = 30)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'GdiPrint.psm1') -Force
function Fail { param([string] $m, [int] $c = 1) Write-Host "`nFAIL: $m" -ForegroundColor Red; exit $c }
if ($IsWindows -eq $false) { Fail "Windows-only. Run on the workstation in Windows PowerShell 5.1." 6 }

$dir = Join-Path $env:SystemRoot 'Temp\testkit-capture'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$fileA = Join-Path $dir 'tray1.prn'
$fileB = Join-Path $dir 'tray2.prn'
Remove-Item $fileA, $fileB -Force -ErrorAction SilentlyContinue

$bins = @(Get-GdiBins -Printer $Printer)
Write-Host "== input bins ==" -ForegroundColor Cyan; $bins | ForEach-Object { "  $_" }
function Find-Id { param($Pairs, $Pattern) foreach ($p in $Pairs) { $id, $n = $p -split '\|', 2; if ($n -match $Pattern) { return [int]$id } } return $null }
$t1 = Find-Id $bins '(?i)tray\s*1'
$t2 = Find-Id $bins '(?i)tray\s*2'
if ($null -eq $t1 -or $null -eq $t2) { Fail "Tray1/Tray2 not found in bins above." 5 }
Write-Host ("Tray1 id={0}  Tray2 id={1}" -f $t1, $t2) -ForegroundColor Green

Write-Host ""
Write-Host "Rendering one page to Tray1, one to Tray2 (to files) ..." -ForegroundColor Cyan
Write-Host ("  " + (Invoke-GdiOnePage -Printer $Printer -OutFile $fileA -BinId $t1))
Write-Host ("  " + (Invoke-GdiOnePage -Printer $Printer -OutFile $fileB -BinId $t2))
foreach ($f in @($fileA, $fileB)) {
    for ($t = 2; $t -le $WaitSeconds; $t += 2) { Start-Sleep -Seconds 2; if ((Test-Path $f) -and (Get-Item $f).Length -gt 0) { break } }
    if (-not (Test-Path $f) -or (Get-Item $f).Length -eq 0) { Fail "No output produced for $f." 4 }
}

function Get-StablePjl { param($File)
    $s = [Text.Encoding]::GetEncoding('ISO-8859-1').GetString([IO.File]::ReadAllBytes($File))
    $vol = 'SPOOLTIME|JOBNAME|PCNAME|DRIVERNAME|USERNAME|PCLOGINID|COPIESSTAMP|JOB NAME|JOB ID'
    [regex]::Matches($s, '@PJL[^\r\n]*') | ForEach-Object { $_.Value.Trim() } | Where-Object { $_ -notmatch $vol }
}
function Get-PxlBody { param($File)
    $bytes = [IO.File]::ReadAllBytes($File)
    $s = [Text.Encoding]::GetEncoding('ISO-8859-1').GetString($bytes)
    $i = $s.IndexOf('@PJL ENTER LANGUAGE'); if ($i -lt 0) { return $bytes }
    $nl = $s.IndexOf("`n", $i); if ($nl -lt 0) { return $bytes }
    return $bytes[($nl + 1)..($bytes.Length - 1)]
}

$sizeA = (Get-Item $fileA).Length; $sizeB = (Get-Item $fileB).Length
$pjlDiff = Compare-Object (Get-StablePjl $fileA) (Get-StablePjl $fileB)
$bodyA = Get-PxlBody $fileA; $bodyB = Get-PxlBody $fileB
$bodiesEqual = ($bodyA.Length -eq $bodyB.Length)
$firstDiff = -1
if ($bodiesEqual) { for ($k = 0; $k -lt $bodyA.Length; $k++) { if ($bodyA[$k] -ne $bodyB[$k]) { $bodiesEqual = $false; $firstDiff = $k; break } } }
elseif ($bodyA.Length -ne $bodyB.Length) { $firstDiff = [Math]::Min($bodyA.Length, $bodyB.Length) }

Write-Host ""
Write-Host "== RESULT ==" -ForegroundColor Yellow
Write-Host ("  Tray1 file: {0} bytes   Tray2 file: {1} bytes" -f $sizeA, $sizeB)
Write-Host ("  PJL (stable) differs: {0}" -f [bool]$pjlDiff)
if ($pjlDiff) { $pjlDiff | ForEach-Object { "    {0} {1}" -f $_.SideIndicator, $_.InputObject } }
Write-Host ("  PCL-XL body identical: {0}{1}" -f $bodiesEqual, ($(if (-not $bodiesEqual -and $firstDiff -ge 0) { " (first difference at body offset $firstDiff)" } else { '' })))

Write-Host ""
if ($pjlDiff -or -not $bodiesEqual) {
    Write-Host "VERDICT: Tray1 vs Tray2 output DIFFERS -> dmDefaultSource IS applied (tray honored at job level via GDI)." -ForegroundColor Green
    Write-Host "  -> per-page tray is genuinely worth testing; the earlier 'no MEDIASOURCE' was just PJL-only grep."
    Write-Host "     Next: I'll add per-page detection at the tray marker we just located."
} else {
    Write-Host "VERDICT: Tray1 vs Tray2 output is IDENTICAL -> dmDefaultSource is NOT taking effect on this driver." -ForegroundColor Red
    Write-Host "  -> the driver ignores the GDI tray field; per-page tray via GDI is not achievable either."
}
