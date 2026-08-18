#requires -Version 5.1
<#
  testkit.ps1 -- BP-70C65 Testkit (Phase 1, integration validation)

  WINDOWS-ONLY. The printing stack this depends on (System.Printing.PrintQueue,
  GetPrintCapabilitiesAsXml, and later PrintQueue.AddJob) exists only on Windows.
  PowerShell 7 runs on macOS/Linux, but these APIs do NOT -- run this on the
  Windows workstation that has the Sharp BP-70C65 driver installed, under
  Windows PowerShell 5.1 (STA by default, printing assemblies in the GAC).

  Implemented so far: query-caps ONLY. The other subcommands are stubs pending
  Phase-1 review of the discovery output -- they refuse to run.

  Ground rules honoured here:
    - No hardcoded tray names. Bin strings come from the machine's own XML.
    - No silent fallback. If capabilities can't be read or contain no input-bin
      feature, we fail loud and exit non-zero. We never guess a tray.
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('query-caps', 'baseline', 'xps-mixed', 'bank-wrap', 'duplex-boundary')]
    [string] $Command,

    [string] $QueueName,

    # Reserved for later subcommands -- not implemented yet.
    [string] $Pdf,
    [ValidateSet('5', '10', '25')] [string] $Cut,
    [int] $Tabs
)

$ErrorActionPreference = 'Stop'

# Pure PrintCapabilities parser (unit-tested in tests/PrintCaps.Tests.ps1).
Import-Module (Join-Path $PSScriptRoot 'PrintCaps.psm1') -Force
# Interactive printer picker (selection logic unit-tested in tests/PrinterSelect.Tests.ps1).
Import-Module (Join-Path $PSScriptRoot 'PrinterSelect.psm1') -Force

function Fail {
    param([string] $Message, [int] $Code = 1)
    Write-Host ""
    Write-Host "FAIL: $Message" -ForegroundColor Red
    exit $Code
}

function Invoke-QueryCaps {
    param([string] $QueueName)

    # Fail closed off-Windows. Get-Printer and System.Printing are Windows-only;
    # without this guard the first cmdlet throws a raw CommandNotFoundException.
    # ($IsWindows exists only in PS 6+; in Windows PowerShell 5.1 it is $null,
    #  and 5.1 only runs on Windows, so a $null value correctly passes through.)
    if ($IsWindows -eq $false) {
        Fail ("This tool talks to the Windows print spooler and must run on the Windows " +
              "workstation with the Sharp driver. Detected non-Windows host ($($PSVersionTable.OS)).") 6
    }

    $discoveryDir = Join-Path (Get-Location) 'discovery'
    New-Item -ItemType Directory -Force -Path $discoveryDir | Out-Null

    # 1. Available printers + selection ----------------------------------
    Write-Host "== Available printers ==" -ForegroundColor Cyan
    $printers = @(Get-Printer | Sort-Object Name)
    $printers | Select-Object Name, DriverName, PortName | Format-Table -AutoSize | Out-Host
    $printers | Select-Object Name, DriverName, PortName, Shared, Type |
        Format-List | Out-File -Encoding UTF8 (Join-Path $discoveryDir 'printers.txt')

    if (-not $QueueName) {
        # No name supplied -> prompt the operator to pick one (the playbook flow).
        $QueueName = Select-PrinterInteractive -Printers $printers
    }
    if (-not ($printers.Name -contains $QueueName)) {
        Fail "Queue '$QueueName' not found among installed printers. Names are case- and space-sensitive." 2
    }
    Write-Host ""
    Write-Host "Selected queue: $QueueName" -ForegroundColor Green

    # 2. Printer properties ----------------------------------------------
    Write-Host ""
    Write-Host "== Printer properties: $QueueName ==" -ForegroundColor Cyan
    try {
        $props = Get-PrinterProperty -PrinterName $QueueName
        $props | Select-Object PropertyName, Value | Format-Table -AutoSize | Out-Host
        $props | Format-List | Out-File -Encoding UTF8 (Join-Path $discoveryDir 'printer-properties.txt')
    } catch {
        Write-Host "  (Get-PrinterProperty failed: $($_.Exception.Message))" -ForegroundColor Yellow
    }

    # 3. PrintCapabilities as raw XML ------------------------------------
    # Load the Windows-only printing assemblies. Fail loud if absent.
    try {
        Add-Type -AssemblyName System.Printing
        Add-Type -AssemblyName ReachFramework
    } catch {
        Fail ("Could not load the Windows printing assemblies (System.Printing / ReachFramework). " +
              "This must run on Windows. $($_.Exception.Message)") 3
    }

    try {
        $server = New-Object System.Printing.LocalPrintServer
        $queue  = $server.GetPrintQueue($QueueName)
    } catch {
        Fail "Could not open print queue '$QueueName' via System.Printing. $($_.Exception.Message)" 3
    }

    # Invoke GetPrintCapabilitiesAsXml via reflection -- the reliable path
    # across driver/framework versions, per project guidance.
    try {
        $method = $queue.GetType().GetMethod('GetPrintCapabilitiesAsXml', [Type[]]@())
        if ($null -eq $method) { Fail "GetPrintCapabilitiesAsXml() not found on PrintQueue." 4 }
        $stream = $method.Invoke($queue, $null)
        if ($null -eq $stream) { Fail "GetPrintCapabilitiesAsXml() returned null." 4 }
        $stream.Position = 0
        $reader = New-Object System.IO.StreamReader($stream)
        $capsXml = $reader.ReadToEnd()
        $reader.Dispose()
    } catch {
        Fail "GetPrintCapabilitiesAsXml() invocation failed. $($_.Exception.Message)" 4
    }

    if ([string]::IsNullOrWhiteSpace($capsXml)) {
        Fail "PrintCapabilities XML was empty." 4
    }

    $capsPath = Join-Path $discoveryDir 'print-capabilities.xml'
    [System.IO.File]::WriteAllText($capsPath, $capsXml, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ""
    Write-Host "Saved raw PrintCapabilities XML -> $capsPath" -ForegroundColor Green

    # 4. Parse + display -- delegated to the unit-tested PrintCaps parser so the
    #    resolution / fail-closed logic is covered by tests, not re-derived here.
    $caps = Read-PrintCapabilities -Xml $capsXml
    if ($caps.ErrorKind -eq 'MalformedXml') { Fail "$($caps.ErrorMessage) Inspect $capsPath by hand." 4 }
    if ($caps.ErrorKind -eq 'NoFeatures')   { Fail "$($caps.ErrorMessage) Inspect $capsPath by hand." 5 }

    Write-Host ""
    Write-Host "== XML namespace prefixes (vendor-private namespaces matter) ==" -ForegroundColor Cyan
    foreach ($k in $caps.Prefixes.Keys) { "  {0,-12} = {1}" -f $k, $caps.Prefixes[$k] | Out-Host }

    # Detailed dump of the features that decide tray / media / insert behaviour.
    # The option Name is the exact string a PrintTicket must carry.
    Write-Host ""
    Write-Host "== Input-bin / media / insert features (option name = the PrintTicket string) ==" -ForegroundColor Cyan
    foreach ($f in $caps.Relevant) {
        Write-Host ""
        Write-Host ("  FEATURE: {0}" -f $f.Name) -ForegroundColor White
        if (@($f.Options).Count -eq 0) {
            Write-Host "    (no options listed)" -ForegroundColor Yellow
            continue
        }
        foreach ($o in $f.Options) {
            if ($o.DisplayName) { "    {0,-42} display: {1}" -f $o.Name, $o.DisplayName | Out-Host }
            else                { "    {0}" -f $o.Name | Out-Host }
        }
    }

    # Full flat list so nothing is hidden from review.
    Write-Host ""
    Write-Host "== All feature names present ==" -ForegroundColor Cyan
    foreach ($f in $caps.Features) { "  " + $f.Name | Out-Host }

    # Fail closed AFTER showing the landscape, so a human sees what the driver
    # did expose even when no input-bin feature is present.
    if ($caps.ErrorKind -eq 'NoInputBin') { Fail $caps.ErrorMessage 5 }

    Write-Host ""
    Write-Host "Discovery complete. Human step: map the physical TAB tray and the physical" -ForegroundColor Green
    Write-Host "STANDARD tray to the exact option strings above. Nothing was guessed." -ForegroundColor Green
}

switch ($Command) {
    'query-caps' { Invoke-QueryCaps -QueueName $QueueName; break }

    { $_ -in 'baseline', 'xps-mixed', 'bank-wrap', 'duplex-boundary' } {
        Fail "'$Command' is not implemented yet -- Phase-1 review of query-caps output comes first." 10
    }

    default {
        Write-Host "BP-70C65 Testkit"
        Write-Host "Usage:"
        Write-Host "  .\testkit.ps1 query-caps -QueueName ""<printer name>"""
        Write-Host "  (baseline / xps-mixed / bank-wrap / duplex-boundary -- not implemented yet)"
        exit 2
    }
}
