#requires -Version 5.1
<#
  capture-pcl.ps1 -- render our XPS through the Sharp driver to a FILE and show the
  per-page tray/media commands, so per-page routing can be verified remotely with
  NO paper and nobody at the printer.

  Submission engine: XpsDocumentWriter (SYNCHRONOUS). StartXpsPrintJob was tried
  first but never rendered on this v3/GDI driver (jobId=0 / E_PENDING / no output).
  XpsDocumentWriter renders through the GDI print pipeline and returns when spooled.

  Run ONLY this way (bypasses the interactive execution policy), in an ELEVATED
  Windows PowerShell 5.1 (Add-Printer needs admin; XpsDocumentWriter needs STA,
  which powershell.exe -File provides by default):
    powershell -ExecutionPolicy Bypass -File .\capture-pcl.ps1 -XpsPath <payload.xps>
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $XpsPath,
    [string] $SourceQueue = 'SHARP#1',
    [string] $DriverName,
    [string] $CaptureName = 'TESTKIT-CAPTURE',
    [string] $OutFile,
    [int]    $WaitSeconds = 45
)

$ErrorActionPreference = 'Stop'

function Fail { param([string] $m, [int] $c = 1) Write-Host "`nFAIL: $m" -ForegroundColor Red; exit $c }

if (-not (Test-Path $XpsPath)) { Fail "Payload not found: $XpsPath  (build one: pull-test.ps1 -Scenario mixed -BuildOnly)" 2 }
$XpsPath = (Resolve-Path $XpsPath).Path

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
         ).IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
if (-not $admin) { Fail "Not elevated. Re-run in an Administrator Windows PowerShell (needs Add-Printer)." 3 }

$apt = [System.Threading.Thread]::CurrentThread.GetApartmentState()
if ($apt -ne 'STA') { Fail "Thread is $apt, not STA. Run via 'powershell.exe -File' (Windows PowerShell 5.1), not pwsh 7." 3 }

Add-Type -AssemblyName System.Printing
Add-Type -AssemblyName ReachFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName WindowsBase

if (-not $DriverName) { $DriverName = (Get-Printer -Name $SourceQueue).DriverName }
if (-not $OutFile) {
    $stem = Split-Path (Split-Path $XpsPath -Parent) -Leaf
    $OutFile = Join-Path (Join-Path $env:SystemRoot 'Temp\testkit-capture') ("$stem.prn")
}
New-Item -ItemType Directory -Force -Path (Split-Path $OutFile) | Out-Null
Remove-Item $OutFile -Force -ErrorAction SilentlyContinue

Write-Host "Driver          : $DriverName"
Write-Host "Capture printer : $CaptureName"
Write-Host "Output (file)   : $OutFile   [SYSTEM-writable]"
Write-Host "Payload         : $XpsPath"
Write-Host "Engine          : XpsDocumentWriter (synchronous)"

try {
    Get-Printer -Name $CaptureName -ErrorAction SilentlyContinue | Remove-Printer -ErrorAction SilentlyContinue
    Get-PrinterPort -Name $OutFile -ErrorAction SilentlyContinue | Remove-PrinterPort -ErrorAction SilentlyContinue
    Add-PrinterPort -Name $OutFile
    Add-Printer -Name $CaptureName -DriverName $DriverName -PortName $OutFile
    Write-Host "  temp printer created." -ForegroundColor Green

    Write-Host ""
    Write-Host "Loading XPS + writing to the capture printer ..." -ForegroundColor Cyan
    $xdoc   = New-Object System.Windows.Xps.Packaging.XpsDocument($XpsPath, [System.IO.FileAccess]::Read)
    $seq    = $xdoc.GetFixedDocumentSequence()
    $queue  = (New-Object System.Printing.LocalPrintServer).GetPrintQueue($CaptureName)
    $writer = [System.Printing.PrintQueue]::CreateXpsDocumentWriter($queue)
    $writer.Write($seq)     # synchronous: returns once spooled
    $xdoc.Close()
    Write-Host "  writer returned (job spooled)." -ForegroundColor Green

    # File-port write finalizes as the spooler drains the job.
    Write-Host ""
    Write-Host "Waiting for the port to finish writing (up to ${WaitSeconds}s) ..." -ForegroundColor Cyan
    $lastSize = -1; $stable = 0
    for ($t = 2; $t -le $WaitSeconds; $t += 2) {
        Start-Sleep -Seconds 2
        $size = if (Test-Path $OutFile) { (Get-Item $OutFile).Length } else { -1 }
        Write-Host ("  t+{0,3}s  file={1}" -f $t, ($(if ($size -ge 0) { $size } else { 'nofile' })))
        if ($size -gt 0) {
            if ($size -eq $lastSize) { $stable++ } else { $stable = 0 }
            if ($stable -ge 2) { break }
        }
        $lastSize = $size
    }

    if (-not (Test-Path $OutFile) -or (Get-Item $OutFile).Length -eq 0) {
        Write-Host ""
        Write-Host "== recent PrintService/Admin events (best effort) ==" -ForegroundColor Cyan
        try {
            Get-WinEvent -LogName 'Microsoft-Windows-PrintService/Admin' -MaxEvents 8 -ErrorAction Stop |
                ForEach-Object { "  [{0:HH:mm:ss}] {1}" -f $_.TimeCreated, (($_.Message -split "`n")[0]) }
        } catch { Write-Host "  (PrintService/Admin log not enabled)" -ForegroundColor DarkGray }
        Fail "No PCL written even via XpsDocumentWriter -- see events above." 4
    }

    $size = (Get-Item $OutFile).Length
    Write-Host ""
    Write-Host "captured $size bytes -> $OutFile" -ForegroundColor Green

    $bytes = [IO.File]::ReadAllBytes($OutFile)
    $s = [Text.Encoding]::GetEncoding('ISO-8859-1').GetString($bytes)

    Write-Host ""
    Write-Host "== @PJL lines ==" -ForegroundColor Cyan
    $pjl = [regex]::Matches($s, '@PJL[^\r\n]*') | ForEach-Object { $_.Value.Trim() }
    if ($pjl) { $pjl | ForEach-Object { "  $_" } } else { Write-Host "  (none)" -ForegroundColor Yellow }

    Write-Host ""
    Write-Host "== tray / media tokens anywhere in the stream ==" -ForegroundColor Cyan
    $tok = [regex]::Matches($s, '(?i)(MEDIASOURCE|MEDIATYPE|MediaSource|MediaType|Tab\s*Paper|Tray\s*\d)') |
        ForEach-Object { $_.Value } | Group-Object | Sort-Object Count -Descending
    if ($tok) { $tok | ForEach-Object { "  {0,4}x  {1}" -f $_.Count, $_.Name } }
    else { Write-Host "  (none as ASCII -- media may be encoded in binary PXL operators)" -ForegroundColor Yellow }

    Write-Host ""
    Write-Host "MIXED payload: the tab page should show a DIFFERENT media/tray than the body"
    Write-Host "pages. Same media on every page = per-page tickets NOT applied (writer flattened"
    Write-Host "them or the driver ignores them) -> next step is the per-page visual collator."
}
finally {
    Get-Printer -Name $CaptureName -ErrorAction SilentlyContinue | Remove-Printer -ErrorAction SilentlyContinue
    Get-PrinterPort -Name $OutFile -ErrorAction SilentlyContinue | Remove-PrinterPort -ErrorAction SilentlyContinue
    Write-Host "  temp printer/port removed." -ForegroundColor DarkGray
}
