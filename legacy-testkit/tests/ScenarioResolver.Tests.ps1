#requires -Version 5.1
<#
  Tests for ScenarioResolver.psm1 -- resolves human intents ("Tray 1", "Tab
  stock") to the driver's exact option strings, pulled from the parsed caps.

  Dependency-free; runs under pwsh 7 (dev) and Windows PowerShell 5.1 (target).
  Fixtures use clearly-fake vendor values (FAKE_..._DO_NOT_USE).

  Run:  pwsh -NoProfile -File ./tests/ScenarioResolver.Tests.ps1
#>

$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'
Import-Module (Join-Path $root 'PrintCaps.psm1') -Force
Import-Module (Join-Path $root 'ScenarioResolver.psm1') -Force

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

$nsDecl = @'
  xmlns:psf="http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework"
  xmlns:psk="http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords"
  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
  xmlns:xsd="http://www.w3.org/2001/XMLSchema"
  xmlns:ns0000="http://schemas.sharp.example/INVENTED/private"
'@

$capsXml = @"
<?xml version="1.0" encoding="UTF-8"?>
<psf:PrintCapabilities $nsDecl version="1">
  <psf:Feature name="psk:JobInputBin">
    <psf:Option name="psk:AutoSelect">
      <psf:Property name="psk:DisplayName"><psf:Value xsi:type="xsd:string">Auto Select</psf:Value></psf:Property>
    </psf:Option>
    <psf:Option name="ns0000:FAKE_Tray1_DO_NOT_USE">
      <psf:Property name="psk:DisplayName"><psf:Value xsi:type="xsd:string">Tray 1</psf:Value></psf:Property>
    </psf:Option>
    <psf:Option name="ns0000:FAKE_Tray2_DO_NOT_USE">
      <psf:Property name="psk:DisplayName"><psf:Value xsi:type="xsd:string">Tray 2</psf:Value></psf:Property>
    </psf:Option>
    <psf:Option name="ns0000:FAKE_Bypass_DO_NOT_USE">
      <psf:Property name="psk:DisplayName"><psf:Value xsi:type="xsd:string">Bypass Tray</psf:Value></psf:Property>
    </psf:Option>
  </psf:Feature>
  <psf:Feature name="psk:PageMediaType">
    <psf:Option name="ns0000:FAKE_Plain_DO_NOT_USE">
      <psf:Property name="psk:DisplayName"><psf:Value xsi:type="xsd:string">Plain Paper</psf:Value></psf:Property>
    </psf:Option>
    <psf:Option name="ns0000:FAKE_TabStock_DO_NOT_USE">
      <psf:Property name="psk:DisplayName"><psf:Value xsi:type="xsd:string">Tab Paper</psf:Value></psf:Property>
    </psf:Option>
  </psf:Feature>
</psf:PrintCapabilities>
"@

$caps = Read-PrintCapabilities -Xml $capsXml
Assert-True $caps.Ok "fixture caps should parse Ok (sanity)"

# ---------------------------------------------------------------------------
Test "resolves an input bin by display-name pattern, returning the exact string verbatim" {
    $r = Resolve-CapsOption -Caps $caps -FeaturePattern 'InputBin' -OptionPattern 'Tray\s*1'
    Assert-True $r.Ok "should resolve"
    Assert-Equal 'ns0000:FAKE_Tray1_DO_NOT_USE' $r.Value "verbatim option string"
    Assert-Equal 'psk:JobInputBin' $r.FeatureName "feature it came from"
}

Test "resolves the other tray independently" {
    $r = Resolve-CapsOption -Caps $caps -FeaturePattern 'InputBin' -OptionPattern 'Tray\s*2'
    Assert-True $r.Ok "should resolve"
    Assert-Equal 'ns0000:FAKE_Tray2_DO_NOT_USE' $r.Value "verbatim option string"
}

Test "resolves a media type scoped to the media feature" {
    $r = Resolve-CapsOption -Caps $caps -FeaturePattern 'MediaType' -OptionPattern 'Tab'
    Assert-True $r.Ok "should resolve"
    Assert-Equal 'ns0000:FAKE_TabStock_DO_NOT_USE' $r.Value "tab media string"
    Assert-Equal 'psk:PageMediaType' $r.FeatureName "from the media feature"
}

Test "fails closed as NotFound when nothing matches" {
    $r = Resolve-CapsOption -Caps $caps -FeaturePattern 'InputBin' -OptionPattern 'NoSuchTray'
    Assert-True (-not $r.Ok) "not Ok"
    Assert-Equal 'NotFound' $r.ErrorKind "NotFound kind"
}

Test "fails closed as Ambiguous when more than one matches" {
    $r = Resolve-CapsOption -Caps $caps -FeaturePattern 'InputBin' -OptionPattern 'Tray'
    Assert-True (-not $r.Ok) "not Ok"
    Assert-Equal 'Ambiguous' $r.ErrorKind "Ambiguous kind"
    Assert-True (@($r.Candidates).Count -ge 2) "lists the competing candidates"
}

Test "feature scoping: an input-bin search never leaks a media-type option" {
    # 'Tab Paper' exists only under PageMediaType; searching input bins must miss it.
    $r = Resolve-CapsOption -Caps $caps -FeaturePattern 'InputBin' -OptionPattern 'Tab Paper'
    Assert-True (-not $r.Ok) "not Ok"
    Assert-Equal 'NotFound' $r.ErrorKind "scoped out"
}

# ---------------------------------------------------------------------------
Write-Host ""
Write-Host ("{0}/{1} passed" -f ($script:count - $script:failures), $script:count)
if ($script:failures -gt 0) { exit 1 } else { exit 0 }
