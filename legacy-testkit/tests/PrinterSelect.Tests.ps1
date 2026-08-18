#requires -Version 5.1
<#
  Tests for the pure part of PrinterSelect.psm1 -- validating an operator's typed
  choice against the printer list. The interactive wrapper (Get-Printer/Read-Host)
  is Windows-only and thin; this covers the logic that decides accept/reject.

  Run:  pwsh -NoProfile -File ./tests/PrinterSelect.Tests.ps1
#>

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path (Join-Path $PSScriptRoot '..') 'PrinterSelect.psm1') -Force

$script:count = 0
$script:failures = 0
function Test { param([string] $Name, [scriptblock] $Body)
    $script:count++
    try { & $Body; Write-Host "  PASS  $Name" -ForegroundColor Green }
    catch { $script:failures++; Write-Host "  FAIL  $Name" -ForegroundColor Red
            Write-Host "        $($_.Exception.Message)" -ForegroundColor Red } }
function Assert-Equal { param($Expected, $Actual, $Msg)
    if ($Expected -ne $Actual) { throw "Expected [$Expected] but got [$Actual]. $Msg" } }
function Assert-True { param($Cond, $Msg)
    if (-not $Cond) { throw "Expected true but was false. $Msg" } }

$names = @('BP-70C65 PCL6', 'Front Office MFP', 'Warehouse Label')

Test "a valid 1-based number selects the matching printer" {
    $r = Resolve-PrinterChoice -Names $names -Choice '1'
    Assert-True $r.Ok "should accept"
    Assert-Equal 'BP-70C65 PCL6' $r.Name "first printer"
}

Test "the number is 1-based, not 0-based" {
    $r = Resolve-PrinterChoice -Names $names -Choice '3'
    Assert-True $r.Ok "should accept"
    Assert-Equal 'Warehouse Label' $r.Name "third printer"
}

Test "surrounding whitespace is tolerated" {
    $r = Resolve-PrinterChoice -Names $names -Choice '  2  '
    Assert-True $r.Ok "should accept"
    Assert-Equal 'Front Office MFP' $r.Name "second printer"
}

Test "zero is out of range" {
    $r = Resolve-PrinterChoice -Names $names -Choice '0'
    Assert-True (-not $r.Ok) "reject"
    Assert-Equal 'OutOfRange' $r.ErrorKind "out of range"
}

Test "a number past the end is out of range" {
    $r = Resolve-PrinterChoice -Names $names -Choice '4'
    Assert-True (-not $r.Ok) "reject"
    Assert-Equal 'OutOfRange' $r.ErrorKind "out of range"
}

Test "non-numeric input is rejected, not coerced" {
    $r = Resolve-PrinterChoice -Names $names -Choice 'BP-70C65'
    Assert-True (-not $r.Ok) "reject"
    Assert-Equal 'NotANumber' $r.ErrorKind "not a number"
}

Test "empty input is rejected" {
    $r = Resolve-PrinterChoice -Names $names -Choice ''
    Assert-True (-not $r.Ok) "reject"
    Assert-Equal 'Empty' $r.ErrorKind "empty"
}

# ---------------------------------------------------------------------------
Write-Host ""
Write-Host ("{0}/{1} passed" -f ($script:count - $script:failures), $script:count)
if ($script:failures -gt 0) { exit 1 } else { exit 0 }
