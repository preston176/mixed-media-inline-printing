#requires -Version 5.1
<#
  pull-test.ps1 -- submit real jobs to see which tray the driver pulls from.

  Uses the strings resolved LIVE from the machine's own capabilities (no
  hardcoding, fails closed if anything can't be resolved). Saves the exact XPS
  submitted, logs everything, then submits via PrintQueue.AddJob(fastCopy: $true)
  so the private spc0000: options ship through unaltered.

  Scenarios:
    baseline : one sheet forced from Tray 2 (psk:JobInputBin). Proves a tray
               override survives AddJob at all.
    mixed    : body / TAB / body via per-page psk:PageMediaType (Plain vs Tab
               Paper). Relies on the machine routing each media to its tray
               (Tray 1 = Tab Paper, Tray 2 = Plain). This is the gate.

  Run on the Windows box, in Windows PowerShell 5.1:
    powershell -ExecutionPolicy Bypass -File .\pull-test.ps1 -QueueName "SHARP#1"
    powershell -ExecutionPolicy Bypass -File .\pull-test.ps1 -QueueName "SHARP#1" -Scenario baseline
#>

[CmdletBinding()]
param(
    [ValidateSet('baseline', 'mixed', 'both')][string] $Scenario = 'both',
    [string] $QueueName,
    [string] $CapsXml,
    [switch] $BuildOnly   # build + save payload.xps, do NOT submit (for FILE-port capture)
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'PrintCaps.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'ScenarioResolver.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'PrinterSelect.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'XpsBuilder.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'XpsPrintApi.psm1') -Force

function Fail { param([string] $Message, [int] $Code = 1)
    Write-Host ""; Write-Host "FAIL: $Message" -ForegroundColor Red; exit $Code }

# --- load capabilities (the source of every string) -----------------------
if (-not $CapsXml) { $CapsXml = Join-Path $PSScriptRoot 'discovery/print-capabilities.xml' }
if (-not (Test-Path $CapsXml)) {
    Fail "Capabilities file not found: $CapsXml`n  Run '.\testkit.ps1 query-caps' first, then re-run this." 2
}
$caps = Read-PrintCapabilities -Xml (Get-Content -Raw -Path $CapsXml)
if ($caps.ErrorKind -eq 'MalformedXml') { Fail "Capabilities XML did not parse: $($caps.ErrorMessage)" 4 }

function Resolve-OrFail {
    param([string] $FeaturePattern, [string] $OptionPattern, [string] $Label)
    $r = Resolve-CapsOption -Caps $caps -FeaturePattern $FeaturePattern -OptionPattern $OptionPattern
    if (-not $r.Ok) {
        Fail "Could not resolve $Label  (/$FeaturePattern/ ~ /$OptionPattern/): $($r.ErrorKind). Refusing to guess." 5
    }
    Write-Host ("  {0,-18} -> {1}   ({2})" -f $Label, $r.Value, $r.DisplayName) -ForegroundColor Green
    return $r.Value
}

$want = @()
if ($Scenario -in 'baseline', 'both') { $want += 'baseline' }
if ($Scenario -in 'mixed', 'both') { $want += 'mixed' }

Write-Host "Resolving strings from $CapsXml" -ForegroundColor Cyan
$tray2 = $null; $autoBin = $null; $plain = $null; $tab = $null
if ('baseline' -in $want) {
    $tray2 = Resolve-OrFail 'InputBin' 'Tray\s*2' 'Tray 2 bin'
}
if ('mixed' -in $want) {
    $autoBin = Resolve-OrFail 'InputBin'  'Auto'        'Auto bin'
    $plain   = Resolve-OrFail 'MediaType' '^psk:Plain$' 'Plain media'
    $tab     = Resolve-OrFail 'MediaType' 'Tab'         'Tab Paper media'
}

# --- scenario definitions --------------------------------------------------
$defs = @{
    baseline = @{
        JobFeatures = @(
            @{ Feature = 'psk:JobInputBin';  Option = $tray2 }
            @{ Feature = 'psk:PageMediaSize'; Option = 'psk:NorthAmericaLetter' }
        )
        Pages = @(@{ MediaType = $null })
        Watch = "ONE sheet should feed from TRAY 2."
    }
    mixed = @{
        JobFeatures = @(
            @{ Feature = 'psk:JobInputBin';  Option = $autoBin }
            @{ Feature = 'psk:PageMediaSize'; Option = 'psk:NorthAmericaLetter' }
        )
        Pages = @(@{ MediaType = $plain }, @{ MediaType = $tab }, @{ MediaType = $plain })
        Watch = "THREE sheets: body / TAB / body. Tab page should feed from the TAB tray (Tray 1); body pages from TRAY 2."
    }
}

# --- Windows? decide whether we submit -------------------------------------
$onWindows = ($IsWindows -ne $false)   # $null on Windows PowerShell 5.1 => treat as Windows
# Build-only never submits, so it never needs a queue -> don't prompt for one.
if ($onWindows -and -not $QueueName -and -not $BuildOnly) { $QueueName = Select-PrinterInteractive }
if (-not $QueueName) { $QueueName = '<not-submitted>' }

$stamp = (Get-Date).ToString('yyyyMMdd-HHmmss')
$exit = 0

foreach ($name in $want) {
    $d = $defs[$name]
    $dir = Join-Path (Join-Path $PSScriptRoot 'runs') ("{0}-{1}" -f $stamp, $name)
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $xps = Join-Path $dir 'payload.xps'

    Write-Host ""
    Write-Host "== Scenario: $name ==" -ForegroundColor Cyan
    New-XpsPackage -Path $xps -Prefixes $caps.Prefixes -JobFeatures $d.JobFeatures -Pages $d.Pages | Out-Null
    $size = (Get-Item $xps).Length
    Write-Host "  built payload.xps ($size bytes)"

    $log = @()
    $log += "scenario:  $name"
    $log += "timestamp: $stamp"
    $log += "queue:     $QueueName"
    $log += "caps:      $CapsXml"
    $log += "job features:"
    foreach ($f in $d.JobFeatures) { $log += "  $($f.Feature) = $($f.Option)" }
    $log += "pages:"
    for ($i = 0; $i -lt $d.Pages.Count; $i++) { $log += "  page $($i + 1) PageMediaType = $($d.Pages[$i].MediaType)" }
    $log += "payload:   $xps ($size bytes)"

    if ($BuildOnly) {
        Write-Host "  (build-only: payload saved, not submitted)" -ForegroundColor Yellow
        $log += "submit:    skipped (build-only)"
    }
    elseif ($onWindows) {
        try {
            # Log the queue's driver class -- confirms WHY we use the XPS Print API
            # (AddJob fastCopy needs an XPSDrv printer; this queue is expected False).
            Add-Type -AssemblyName System.Printing
            $server = New-Object System.Printing.LocalPrintServer
            $queue = $server.GetPrintQueue($QueueName)
            Write-Host "  queue IsXpsDevice = $($queue.IsXpsDevice)"
            $log += "IsXpsDevice: $($queue.IsXpsDevice)"

            # Submit the exact XPS bytes via StartXpsPrintJob -- preserves job,
            # per-page, and vendor-private (spc0000:) tickets on a GDI driver.
            $status = Submit-XpsFile -PrinterName $QueueName -JobName "testkit-$name-$stamp" -XpsPath $xps -TimeoutSeconds 20
            $log += "submit:    $status"
            if ($status -match 'completion=FAILED' -or $status -match 'completion=CANCELLED') {
                $exit = 1
                Write-Host "  SUBMIT FAILED: $status" -ForegroundColor Red
            }
            elseif ($status -match 'completion=COMPLETED') {
                Write-Host "  submitted + COMPLETED: $status" -ForegroundColor Green
            }
            else {
                # Accepted into the queue but not yet terminal (printer paused / out of paper).
                Write-Host "  ACCEPTED (still processing): $status" -ForegroundColor Yellow
                Write-Host "  -> job is queued. If nothing prints, the printer is likely waiting for paper." -ForegroundColor Yellow
                Write-Host "     Inspect: Get-PrintJob -PrinterName '$QueueName'"
            }
        }
        catch {
            $exit = 1
            Write-Host "  SUBMIT FAILED: $($_.Exception.Message)" -ForegroundColor Red
            $log += "submit:    FAILED"
            $log += ($_.Exception.ToString())
        }
    }
    else {
        Write-Host "  (not Windows -> built + saved, not submitted)" -ForegroundColor Yellow
        $log += "submit:    skipped (not Windows)"
    }

    $log += "WATCH: $($d.Watch)"
    Set-Content -Path (Join-Path $dir 'log.txt') -Value $log -Encoding UTF8
    Write-Host "  WATCH: $($d.Watch)" -ForegroundColor White
    Write-Host "  artifacts: $dir"
}

Write-Host ""
Write-Host "Done. Payloads + logs are under .\runs\" -ForegroundColor Cyan
exit $exit
