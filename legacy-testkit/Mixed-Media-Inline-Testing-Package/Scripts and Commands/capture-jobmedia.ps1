#requires -Version 5.1
<#
  capture-jobmedia.ps1 -- distinguish "driver ignores our media" from "driver honors
  media at the JOB level but not per-page".

  It submits SINGLE-page jobs (so the page ticket == the job media) with media = Plain
  and media = Tab Paper, through a FILE-port clone of the EXISTING Sharp driver, and
  prints the MEDIATYPE command each one produced in the driver's PCL.

  PRODUCTION-SAFE: clones the installed driver, renders to a FILE, nothing prints on
  the device, no driver is installed/changed.

  Run ELEVATED, Windows PowerShell 5.1 (STA), via:
    powershell -ExecutionPolicy Bypass -File .\capture-jobmedia.ps1
    powershell -ExecutionPolicy Bypass -File .\capture-jobmedia.ps1 -Media Tab   # just one

  Interpretation:
    Plain and Tab produce DIFFERENT MEDIATYPE  -> driver honors media at the job level;
                                                  the per-page failure is specifically
                                                  per-page (verdict solidified).
    Both produce the SAME / DEFAULT MEDIATYPE   -> our submission path isn't delivering
                                                  media at all; the driver verdict is
                                                  UNPROVEN and the method needs rethink.
#>

[CmdletBinding()]
param(
    [ValidateSet('Both', 'Plain', 'Tab')][string] $Media = 'Both',
    [string] $CapsXml,
    [string] $SourceQueue = 'SHARP#1',
    [string] $DriverName,
    [int]    $WaitSeconds = 45
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'PrintCaps.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'ScenarioResolver.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'XpsBuilder.psm1') -Force

function Fail { param([string] $m, [int] $c = 1) Write-Host "`nFAIL: $m" -ForegroundColor Red; exit $c }

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
         ).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $admin) { Fail "Not elevated. Run in an Administrator Windows PowerShell." 3 }
if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    Fail "Not STA. Run via 'powershell.exe -File' (Windows PowerShell 5.1)." 3
}

Add-Type -AssemblyName System.Printing
Add-Type -AssemblyName ReachFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName WindowsBase

if (-not $CapsXml) { $CapsXml = Join-Path $PSScriptRoot 'discovery/print-capabilities.xml' }
if (-not (Test-Path $CapsXml)) { Fail "No capabilities at $CapsXml -- run 'testkit.ps1 query-caps' first." 2 }
$caps = Read-PrintCapabilities -Xml (Get-Content -Raw -Path $CapsXml)
if ($caps.ErrorKind -eq 'MalformedXml') { Fail "capabilities parse error: $($caps.ErrorMessage)" 4 }
if (-not $DriverName) { $DriverName = (Get-Printer -Name $SourceQueue).DriverName }

function Resolve-OrFail {
    param([string] $FeaturePattern, [string] $OptionPattern, [string] $Label)
    $r = Resolve-CapsOption -Caps $caps -FeaturePattern $FeaturePattern -OptionPattern $OptionPattern
    if (-not $r.Ok) { Fail "could not resolve $Label ($($r.ErrorKind))" 5 }
    return $r
}

function New-MediaTicket {
    param([string] $MediaOption)
    $xml = New-PrintTicketXml -Prefixes $caps.Prefixes -Features @(
        @{ Feature = 'psk:PageMediaType'; Option = $MediaOption }
        @{ Feature = 'psk:PageMediaSize'; Option = 'psk:NorthAmericaLetter' }
    )
    $ms = New-Object System.IO.MemoryStream
    $sw = New-Object System.IO.StreamWriter($ms, (New-Object System.Text.UTF8Encoding($false)))
    $sw.Write($xml); $sw.Flush(); $ms.Position = 0
    return New-Object System.Printing.PrintTicket($ms)
}

function New-PageVisual {
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $dc.DrawRectangle([System.Windows.Media.Brushes]::SteelBlue, $null,
        (New-Object System.Windows.Rect(60, 72, 696, 120)))
    $dc.Close()
    return $dv
}

function Capture-OneMedia {
    param([string] $Label, [string] $MediaOption)

    $printer = "TESTKIT-JM-$Label"
    $out = Join-Path (Join-Path $env:SystemRoot 'Temp\testkit-capture') ("jobmedia-$Label.prn")
    New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null
    Remove-Item $out -Force -ErrorAction SilentlyContinue

    try {
        Get-Printer -Name $printer -ErrorAction SilentlyContinue | Remove-Printer -ErrorAction SilentlyContinue
        Get-PrinterPort -Name $out -ErrorAction SilentlyContinue | Remove-PrinterPort -ErrorAction SilentlyContinue
        Add-PrinterPort -Name $out
        Add-Printer -Name $printer -DriverName $DriverName -PortName $out

        $queue  = (New-Object System.Printing.LocalPrintServer).GetPrintQueue($printer)
        $writer = [System.Printing.PrintQueue]::CreateXpsDocumentWriter($queue)
        $writer.Write((New-PageVisual), (New-MediaTicket $MediaOption))   # single-page job, this media

        $lastSize = -1; $stable = 0
        for ($t = 2; $t -le $WaitSeconds; $t += 2) {
            Start-Sleep -Seconds 2
            $size = if (Test-Path $out) { (Get-Item $out).Length } else { -1 }
            if ($size -gt 0) { if ($size -eq $lastSize) { $stable++ } else { $stable = 0 }; if ($stable -ge 2) { break } }
            $lastSize = $size
        }
        if (-not (Test-Path $out) -or (Get-Item $out).Length -eq 0) { return @{ Label = $Label; Option = $MediaOption; Lines = @('<no file produced>'); Size = 0 } }

        $s = [Text.Encoding]::GetEncoding('ISO-8859-1').GetString([IO.File]::ReadAllBytes($out))
        $mt = [regex]::Matches($s, '@PJL SET MEDIATYPE=[^\r\n]*') | ForEach-Object { $_.Value }
        return @{ Label = $Label; Option = $MediaOption; Lines = @($mt); Size = (Get-Item $out).Length; File = $out }
    }
    finally {
        Get-Printer -Name $printer -ErrorAction SilentlyContinue | Remove-Printer -ErrorAction SilentlyContinue
        Get-PrinterPort -Name $out -ErrorAction SilentlyContinue | Remove-PrinterPort -ErrorAction SilentlyContinue
    }
}

Write-Host "Driver: $DriverName" -ForegroundColor Cyan
$plainOpt = (Resolve-OrFail 'MediaType' '^psk:Plain$' 'Plain').Value
$tabOpt   = (Resolve-OrFail 'MediaType' 'Tab'         'Tab Paper').Value
Write-Host ("  Plain -> {0}" -f $plainOpt)
Write-Host ("  Tab   -> {0}" -f $tabOpt)

$runs = @()
if ($Media -in 'Both', 'Plain') { Write-Host "`nCapturing PLAIN job ..." -ForegroundColor Cyan; $runs += (Capture-OneMedia 'Plain' $plainOpt) }
if ($Media -in 'Both', 'Tab')   { Write-Host "Capturing TAB job ..."   -ForegroundColor Cyan; $runs += (Capture-OneMedia 'Tab'   $tabOpt) }

Write-Host ""
Write-Host "==================== RESULT ====================" -ForegroundColor Yellow
foreach ($r in $runs) {
    Write-Host ("{0,-6} (ticket media = {1}, {2} bytes):" -f $r.Label, $r.Option, $r.Size) -ForegroundColor White
    $r.Lines | ForEach-Object { "    $_" }
}

if ($Media -eq 'Both' -and $runs.Count -eq 2) {
    $a = ($runs[0].Lines -join '|'); $b = ($runs[1].Lines -join '|')
    Write-Host ""
    if ($a -ne $b) {
        Write-Host "VERDICT: Plain and Tab produced DIFFERENT MEDIATYPE." -ForegroundColor Green
        Write-Host "  -> the driver DOES honor media at the job level. The mixed-media failure is"
        Write-Host "     specifically PER-PAGE. Phase-1 verdict solidified."
    } else {
        Write-Host "VERDICT: Plain and Tab produced the SAME MEDIATYPE." -ForegroundColor Red
        Write-Host "  -> our submission path is not delivering the media type at all. The 'driver"
        Write-Host "     only does job-level media' claim is UNPROVEN; the test method needs rethink."
    }
}
