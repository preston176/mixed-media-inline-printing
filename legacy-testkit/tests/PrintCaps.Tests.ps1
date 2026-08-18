#requires -Version 5.1
<#
  Tests for PrintCaps.psm1 -- the pure PrintCapabilities parser.

  Dependency-free (no Pester) so it runs identically under pwsh 7 (dev macOS)
  and Windows PowerShell 5.1 (the target workstation). No network, no install.

  Fixtures are structurally faithful to the Microsoft PrintSchema format, with
  clearly-fake vendor values (FAKE_..._DO_NOT_USE) so nothing here can ever be
  mistaken for a real BP-70C65 bin string.

  Run:  pwsh -NoProfile -File ./tests/PrintCaps.Tests.ps1
#>

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path (Join-Path $PSScriptRoot '..') 'PrintCaps.psm1') -Force

$script:count = 0
$script:failures = 0

function Test {
    param([string] $Name, [scriptblock] $Body)
    $script:count++
    try {
        & $Body
        Write-Host "  PASS  $Name" -ForegroundColor Green
    } catch {
        $script:failures++
        Write-Host "  FAIL  $Name" -ForegroundColor Red
        Write-Host "        $($_.Exception.Message)" -ForegroundColor Red
    }
}
function Assert-Equal { param($Expected, $Actual, $Msg)
    if ($Expected -ne $Actual) { throw "Expected [$Expected] but got [$Actual]. $Msg" } }
function Assert-True { param($Cond, $Msg)
    if (-not $Cond) { throw "Expected true but was false. $Msg" } }

# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------
$nsDecl = @'
  xmlns:psf="http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework"
  xmlns:psk="http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords"
  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
  xmlns:xsd="http://www.w3.org/2001/XMLSchema"
  xmlns:ns0000="http://schemas.sharp.example/INVENTED/private"
'@

$fixtureFull = @"
<?xml version="1.0" encoding="UTF-8"?>
<psf:PrintCapabilities $nsDecl version="1">
  <psf:Feature name="psk:JobInputBin">
    <psf:Option name="psk:AutoSelect">
      <psf:Property name="psk:DisplayName">
        <psf:Value xsi:type="xsd:string">Auto Select</psf:Value>
      </psf:Property>
    </psf:Option>
    <psf:Option name="ns0000:FAKE_Bypass_DO_NOT_USE">
      <psf:Property name="psk:DisplayName">
        <psf:Value xsi:type="xsd:string">Bypass Tray</psf:Value>
      </psf:Property>
    </psf:Option>
    <psf:Option name="ns0000:FAKE_Tray4_DO_NOT_USE" />
  </psf:Feature>
  <psf:Feature name="psk:PageMediaType">
    <psf:Option name="ns0000:FAKE_TabStock_DO_NOT_USE">
      <psf:Property name="psk:DisplayName">
        <psf:Value xsi:type="xsd:string">Tab Paper</psf:Value>
      </psf:Property>
    </psf:Option>
  </psf:Feature>
  <psf:Feature name="ns0000:FAKE_CoversInserts_DO_NOT_USE">
    <psf:Option name="ns0000:FAKE_InsertBlank_DO_NOT_USE" />
  </psf:Feature>
  <psf:Feature name="psk:JobDuplexAllDocumentsContiguously">
    <psf:Option name="psk:TwoSidedLongEdge" />
  </psf:Feature>
</psf:PrintCapabilities>
"@

$fixtureNoInputBin = @"
<?xml version="1.0" encoding="UTF-8"?>
<psf:PrintCapabilities $nsDecl version="1">
  <psf:Feature name="psk:PageMediaType">
    <psf:Option name="ns0000:FAKE_TabStock_DO_NOT_USE" />
  </psf:Feature>
</psf:PrintCapabilities>
"@

$fixtureNoFeatures = @"
<?xml version="1.0" encoding="UTF-8"?>
<psf:PrintCapabilities $nsDecl version="1">
</psf:PrintCapabilities>
"@

$fixtureMalformed = '<psf:PrintCapabilities xmlns:psf="urn:x"><psf:Feature name="oops"></psf:PrintCapabilities>'

# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------
Test "well-formed caps parses and is Ok" {
    $r = Read-PrintCapabilities -Xml $fixtureFull
    Assert-True $r.Ok "Ok should be true"
    Assert-Equal $null $r.ErrorKind "no error kind"
}

Test "finds the JobInputBin feature with all its options" {
    $r = Read-PrintCapabilities -Xml $fixtureFull
    $bin = $r.Features | Where-Object { $_.Name -eq 'psk:JobInputBin' }
    Assert-True ($null -ne $bin) "JobInputBin feature present"
    Assert-Equal 3 (@($bin.Options).Count) "three options under JobInputBin"
}

Test "reads exact option name strings verbatim, including vendor prefix" {
    $bin = (Read-PrintCapabilities -Xml $fixtureFull).Features |
        Where-Object { $_.Name -eq 'psk:JobInputBin' }
    $names = @($bin.Options.Name)
    Assert-True ($names -contains 'ns0000:FAKE_Bypass_DO_NOT_USE') "vendor bin name kept verbatim"
    Assert-True ($names -contains 'psk:AutoSelect') "standard bin name kept verbatim"
}

Test "captures DisplayName when the option has one" {
    $opt = ((Read-PrintCapabilities -Xml $fixtureFull).Features |
        Where-Object { $_.Name -eq 'psk:JobInputBin' }).Options |
        Where-Object { $_.Name -eq 'ns0000:FAKE_Bypass_DO_NOT_USE' }
    Assert-Equal 'Bypass Tray' $opt.DisplayName "display name read"
}

Test "missing DisplayName yields empty string, not an error" {
    $opt = ((Read-PrintCapabilities -Xml $fixtureFull).Features |
        Where-Object { $_.Name -eq 'psk:JobInputBin' }).Options |
        Where-Object { $_.Name -eq 'ns0000:FAKE_Tray4_DO_NOT_USE' }
    Assert-Equal '' $opt.DisplayName "empty display name"
}

Test "Relevant includes bin/media/insert/cover features and excludes unrelated ones" {
    $r = Read-PrintCapabilities -Xml $fixtureFull
    $rel = @($r.Relevant.Name)
    Assert-True ($rel -contains 'psk:JobInputBin') "input bin is relevant"
    Assert-True ($rel -contains 'psk:PageMediaType') "media type is relevant"
    Assert-True ($rel -contains 'ns0000:FAKE_CoversInserts_DO_NOT_USE') "covers/inserts is relevant"
    Assert-True ($rel -notcontains 'psk:JobDuplexAllDocumentsContiguously') "duplex excluded"
}

Test "prefix map exposes the vendor namespace URI" {
    $r = Read-PrintCapabilities -Xml $fixtureFull
    Assert-Equal 'http://schemas.sharp.example/INVENTED/private' $r.Prefixes['ns0000'] "vendor URI mapped"
}

Test "malformed XML fails closed as MalformedXml" {
    $r = Read-PrintCapabilities -Xml $fixtureMalformed
    Assert-True (-not $r.Ok) "not Ok"
    Assert-Equal 'MalformedXml' $r.ErrorKind "MalformedXml kind"
}

Test "no Feature elements fails closed as NoFeatures" {
    $r = Read-PrintCapabilities -Xml $fixtureNoFeatures
    Assert-True (-not $r.Ok) "not Ok"
    Assert-Equal 'NoFeatures' $r.ErrorKind "NoFeatures kind"
}

Test "features present but no input bin fails closed as NoInputBin, still returns what was found" {
    $r = Read-PrintCapabilities -Xml $fixtureNoInputBin
    Assert-True (-not $r.Ok) "not Ok"
    Assert-Equal 'NoInputBin' $r.ErrorKind "NoInputBin kind"
    Assert-True (@($r.Relevant.Name) -contains 'psk:PageMediaType') "still surfaces the media type it did find"
}

# ---------------------------------------------------------------------------
Write-Host ""
Write-Host ("{0}/{1} passed" -f ($script:count - $script:failures), $script:count)
if ($script:failures -gt 0) { exit 1 } else { exit 0 }
