#requires -Version 5.1
<#
  capture-collator.ps1 -- deliver PER-PAGE PrintTickets via the WPF visual collator
  (XpsDocumentWriter.CreateVisualsCollator -> Write(visual, printTicket)) and capture
  the driver's PCL, to test whether per-page media (Plain vs Tab Paper) reaches the
  driver as per-page commands.

  Why this and not the XPS file: XpsDocumentWriter.Write(FixedDocumentSequence)
  flattened the embedded per-page tickets (job showed a single MEDIATYPE). The
  collator's Write(visual, ticket) is the documented per-page mechanism.

  Run ELEVATED, Windows PowerShell 5.1 (STA), via:
    powershell -ExecutionPolicy Bypass -File .\capture-collator.ps1
#>

[CmdletBinding()]
param(
    [string] $CapsXml,
    [string] $SourceQueue = 'SHARP#1',
    [string] $DriverName,
    [string] $CaptureName = 'TESTKIT-COLLATE',
    [string] $OutFile,
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

function Resolve-OrFail {
    param([string] $FeaturePattern, [string] $OptionPattern, [string] $Label)
    $r = Resolve-CapsOption -Caps $caps -FeaturePattern $FeaturePattern -OptionPattern $OptionPattern
    if (-not $r.Ok) { Fail "could not resolve $Label ($($r.ErrorKind))" 5 }
    Write-Host ("  {0,-12} -> {1}   ({2})" -f $Label, $r.Value, $r.DisplayName) -ForegroundColor Green
    return $r.Value
}
Write-Host "Resolving media strings ..." -ForegroundColor Cyan
$plain = Resolve-OrFail 'MediaType' '^psk:Plain$' 'Plain'
$tab   = Resolve-OrFail 'MediaType' 'Tab'         'Tab Paper'

if (-not $DriverName) { $DriverName = (Get-Printer -Name $SourceQueue).DriverName }
if (-not $OutFile) { $OutFile = Join-Path (Join-Path $env:SystemRoot 'Temp\testkit-capture') 'collate-mixed.prn' }
New-Item -ItemType Directory -Force -Path (Split-Path $OutFile) | Out-Null
Remove-Item $OutFile -Force -ErrorAction SilentlyContinue

function New-MediaTicket {
    param([string] $MediaOption)
    $xml = New-PrintTicketXml -Prefixes $caps.Prefixes -Features @(
        @{ Feature = 'psk:PageMediaType';  Option = $MediaOption }
        @{ Feature = 'psk:PageMediaSize';  Option = 'psk:NorthAmericaLetter' }
    )
    $ms = New-Object System.IO.MemoryStream
    $sw = New-Object System.IO.StreamWriter($ms, (New-Object System.Text.UTF8Encoding($false)))
    $sw.Write($xml); $sw.Flush(); $ms.Position = 0
    return New-Object System.Printing.PrintTicket($ms)
}

function New-PageVisual {
    param([int] $N)
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $dc.DrawRectangle([System.Windows.Media.Brushes]::SteelBlue, $null,
        (New-Object System.Windows.Rect(60, (60 + $N * 40), 696, 60)))
    $dc.Close()
    return $dv
}

Write-Host ""
Write-Host "Driver          : $DriverName"
Write-Host "Capture printer : $CaptureName -> $OutFile"
Write-Host "Pages           : 1=Plain  2=Tab Paper  3=Plain (each with its own ticket)"

try {
    Get-Printer -Name $CaptureName -ErrorAction SilentlyContinue | Remove-Printer -ErrorAction SilentlyContinue
    Get-PrinterPort -Name $OutFile -ErrorAction SilentlyContinue | Remove-PrinterPort -ErrorAction SilentlyContinue
    Add-PrinterPort -Name $OutFile
    Add-Printer -Name $CaptureName -DriverName $DriverName -PortName $OutFile
    Write-Host "  temp printer created." -ForegroundColor Green

    $queue    = (New-Object System.Printing.LocalPrintServer).GetPrintQueue($CaptureName)
    $writer   = [System.Printing.PrintQueue]::CreateXpsDocumentWriter($queue)
    $collator = $writer.CreateVisualsCollator()

    Write-Host ""
    Write-Host "Writing pages with per-page tickets ..." -ForegroundColor Cyan
    $collator.BeginBatchWrite()
    $collator.Write((New-PageVisual 1), (New-MediaTicket $plain))
    $collator.Write((New-PageVisual 2), (New-MediaTicket $tab))
    $collator.Write((New-PageVisual 3), (New-MediaTicket $plain))
    $collator.EndBatchWrite()
    Write-Host "  collator finished (job spooled)." -ForegroundColor Green

    Write-Host ""
    Write-Host "Waiting for the port to finish writing (up to ${WaitSeconds}s) ..." -ForegroundColor Cyan
    $lastSize = -1; $stable = 0
    for ($t = 2; $t -le $WaitSeconds; $t += 2) {
        Start-Sleep -Seconds 2
        $size = if (Test-Path $OutFile) { (Get-Item $OutFile).Length } else { -1 }
        Write-Host ("  t+{0,3}s  file={1}" -f $t, ($(if ($size -ge 0) { $size } else { 'nofile' })))
        if ($size -gt 0) { if ($size -eq $lastSize) { $stable++ } else { $stable = 0 }; if ($stable -ge 2) { break } }
        $lastSize = $size
    }
    if (-not (Test-Path $OutFile) -or (Get-Item $OutFile).Length -eq 0) { Fail "No PCL written." 4 }

    $bytes = [IO.File]::ReadAllBytes($OutFile)
    $s = [Text.Encoding]::GetEncoding('ISO-8859-1').GetString($bytes)
    Write-Host ""
    Write-Host "captured $((Get-Item $OutFile).Length) bytes -> $OutFile" -ForegroundColor Green

    Write-Host ""
    Write-Host "== every '@PJL SET MEDIATYPE=' (per-page = multiple, differing) ==" -ForegroundColor Cyan
    $mt = [regex]::Matches($s, '@PJL SET MEDIATYPE=[^\r\n]*') | ForEach-Object { $_.Value }
    Write-Host ("  count = {0}" -f $mt.Count)
    $mt | ForEach-Object { "  $_" }

    Write-Host ""
    Write-Host "== all @PJL lines mentioning MEDIA / TRAY / TAB ==" -ForegroundColor Cyan
    [regex]::Matches($s, '@PJL[^\r\n]*') | ForEach-Object { $_.Value } |
        Where-Object { $_ -match '(?i)MEDIA|TRAY|TAB|INSERT|BIN' } | ForEach-Object { "  $_" }

    Write-Host ""
    Write-Host "VERDICT GUIDE:" -ForegroundColor Yellow
    Write-Host "  Multiple MEDIATYPE lines with a Tab value on page 2  -> per-page media REACHES the driver (gate passes)."
    Write-Host "  Still a single MEDIATYPE=DEFAULTMEDIATYPE            -> driver ignores per-page media via the print path."
}
finally {
    Get-Printer -Name $CaptureName -ErrorAction SilentlyContinue | Remove-Printer -ErrorAction SilentlyContinue
    Get-PrinterPort -Name $OutFile -ErrorAction SilentlyContinue | Remove-PrinterPort -ErrorAction SilentlyContinue
    Write-Host "  temp printer/port removed." -ForegroundColor DarkGray
}
