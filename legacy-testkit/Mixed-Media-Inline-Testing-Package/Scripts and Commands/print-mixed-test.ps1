#requires -Version 5.1
<#
  print-mixed-test.ps1 -- THE decisive mixed-media-inline test via the Word/docx
  pipeline: ONE Word PrintOut() call, ONE job, three pages -- BODY (Tray 2), TAB
  (Tray 1, the real template's position + rotation), BODY (Tray 2).

  WHY THIS TEST, SPECIFICALLY (it is not redundant with earlier work):
  Per-page tray switching within one job is ALREADY CONFIRMED working on this
  hardware via raw GDI (DEVMODE + ResetDC between pages -- see capture-gdi-tabpos.ps1
  and the scripts before it). What is NOT yet confirmed is whether WORD'S OWN print
  pipeline -- the mechanism actually chosen for the production path, via
  print-tab-from-docx.ps1 -- preserves that same per-page/per-section switching, or
  collapses it the way the original PrintTicket/XPS path did (see SESSION.md's
  Phase-1 verdict). Word's per-SECTION paper source (Section.PageSetup.FirstPageTray)
  is a decades-old feature (e.g. letterhead-first-page documents) that predates
  PrintTicket/XPS and is REASONED to work through the same GDI/DEVMODE mechanism the
  raw-GDI probe already proved works -- but that is an expectation, not a confirmed
  fact for THIS driver via Word. This script is the actual test of that expectation.

  HOW THE TEST DOCUMENT IS BUILT: reuses edit-tab-docx.ps1 UNCHANGED to produce the
  real, correctly-shifted single-page TAB content, then wraps it with two generic
  BODY paragraphs, each pair separated by a section break (a <w:sectPr> inside a
  paragraph's <w:pPr> ends a section; OOXML section starts default to a new page) --
  so the result is a 3-section, 3-page document sharing the template's real fonts
  and styles.

  VERIFIED (off-hardware, on macOS): the produced XML is well-formed, has exactly 3
  <w:sectPr> (3 sections), and all expected text is present in the correct order.
  NOT VERIFIED: full visual page-by-page rendering -- macOS QuickLook's docx renderer
  already proved unreliable earlier (it silently failed to render wps:bodyPr
  vert="vert" rotation), and it also did not render this multi-section document as
  separate pages -- most likely a renderer limitation, not a flaw in the document
  (the structural checks above are clean), but this cannot be fully confirmed without
  opening it in real Word. Tray-per-section printing itself is UNTESTED -- that's the
  whole point of running this.

  ALWAYS prints to the physical device -- opt-in only, requires typed confirmation.

  Run:
    powershell -ExecutionPolicy Bypass -File .\print-mixed-test.ps1 -Printer "SHARP#1" -TabNumber 2
#>

[CmdletBinding()]
param(
    [string] $TemplatePath,
    [Parameter(Mandatory)][string] $Printer,
    [Parameter(Mandatory)][int] $TabNumber,
    [string] $Text,                     # optional: custom label instead of the bare
                                         # number. TabNumber still selects WHICH
                                         # template position/geometry is used.
    [double] $NudgeXIn = 0,
    [double] $NudgeYIn = 0,
    [int] $Copies = 1
)

$ErrorActionPreference = 'Stop'
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $TemplatePath) { $TemplatePath = Join-Path $scriptDir '5th-cut-1-to-500.docx' }
Import-Module (Join-Path $scriptDir 'GdiPrint.psm1') -Force
function Fail { param([string] $m, [int] $c = 1) Write-Host "`nFAIL: $m" -ForegroundColor Red; exit $c }
if ($IsWindows -eq $false) { Fail "Windows-only. Run on the workstation in Windows PowerShell 5.1." 6 }
if (-not (Test-Path $TemplatePath)) { Fail "Template not found: $TemplatePath" 2 }
if ($TabNumber -lt 1 -or $TabNumber -gt 500) { Fail "TabNumber must be 1..500." 2 }

function Find-Id { param($Pairs, $Pattern) foreach ($p in $Pairs) { $id, $n = $p -split '\|', 2; if ($n -match $Pattern) { return [int]$id } } return $null }

Write-Host "== Driver input bins ==" -ForegroundColor Cyan
$bins = @(Get-GdiBins -Printer $Printer); $bins | ForEach-Object { "  $_" }
$tBody = Find-Id $bins '(?i)tray\s*2'
$tTab  = Find-Id $bins '(?i)tray\s*1'
if ($null -eq $tBody -or $null -eq $tTab) { Fail "Tray1/Tray2 not found in the bin list above. Check -Printer." 5 }
Write-Host ("Tray1 (tab) id={0}  Tray2 (body) id={1}" -f $tTab, $tBody) -ForegroundColor Green

# --- same position/margin auto-correction as print-tab-from-docx.ps1 ---
$EMU_PER_INCH = 914400
$templateXEmu = 7162495
$templateYByPosition = @{ 1 = 412394; 2 = 2174443; 3 = 4155033; 4 = 6071616; 5 = 7697419 }
$templateWEmu = 557784
$templateHEmu = 1828800
$position = (($TabNumber - 1) % 5) + 1
$yEmu = $templateYByPosition[$position]
Write-Host ("Tab #{0} -> cut position {1} of 5" -f $TabNumber, $position) -ForegroundColor Cyan

$info = Get-GdiDeviceInfo -Printer $Printer
$xIn = $templateXEmu / $EMU_PER_INCH; $yIn = $yEmu / $EMU_PER_INCH
$wIn = $templateWEmu / $EMU_PER_INCH; $hIn = $templateHEmu / $EMU_PER_INCH
$xPxPhysical = [int][Math]::Round($xIn * $info.DpiX)
$yPxPhysical = [int][Math]::Round($yIn * $info.DpiY)
$wPx = [int][Math]::Round($wIn * $info.DpiX); $hPx = [int][Math]::Round($hIn * $info.DpiY)
$xPxGdi = ($xPxPhysical - $info.PhysicalOffsetX) + [int][Math]::Round($NudgeXIn * $info.DpiX)
$yPxGdi = ($yPxPhysical - $info.PhysicalOffsetY) + [int][Math]::Round($NudgeYIn * $info.DpiY)
$SAFETY_PX = 20
function Get-Correction { param($pos, $size, $limit)
    if ($pos -lt 0) { return -$pos + $SAFETY_PX }
    elseif (($pos + $size) -gt $limit) { return -(($pos + $size) - $limit) - $SAFETY_PX }
    else { return 0 }
}
$corrXPx = Get-Correction $xPxGdi $wPx $info.HorzRes
$corrYPx = Get-Correction $yPxGdi $hPx $info.VertRes
$totalNudgeXIn = $NudgeXIn + ($corrXPx / $info.DpiX)
$totalNudgeYIn = $NudgeYIn + ($corrYPx / $info.DpiY)
Write-Host ("Total shift to apply: x={0:N4}in  y={1:N4}in" -f $totalNudgeXIn, $totalNudgeYIn) -ForegroundColor Green

# --- get the shifted single-page TAB content (reuse edit-tab-docx.ps1 unchanged) ---
Write-Host ""
Write-Host "== Editing tab docx ==" -ForegroundColor Cyan
# Hashtable splatting (not array splatting): array elements like '-Text' don't get
# re-parsed as flag names, they just bind positionally as raw values -- which is
# exactly what broke here before. A hashtable's keys map to named parameters correctly.
$textArgs = if ($Text) { @{ Text = $Text } } else { @{} }
$tabPath = Join-Path $scriptDir ("tab{0}-formixed.docx" -f $TabNumber)
& (Join-Path $scriptDir 'edit-tab-docx.ps1') -TemplatePath $TemplatePath -TabNumber $TabNumber `
    -NudgeXIn $totalNudgeXIn -NudgeYIn $totalNudgeYIn -OutputPath $tabPath @textArgs
if ($LASTEXITCODE) { Fail "edit-tab-docx.ps1 failed (exit $LASTEXITCODE)." 7 }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zip = [System.IO.Compression.ZipFile]::Open($tabPath, [System.IO.Compression.ZipArchiveMode]::Read)
try {
    $entry = $zip.GetEntry('word/document.xml')
    $stream = $entry.Open()
    $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
    $tabDocXml = $reader.ReadToEnd()
    $reader.Close(); $stream.Close()
} finally { $zip.Dispose() }

$rootOpen = [regex]::Match($tabDocXml, '<w:document[^>]*>').Value
$sectPr = [regex]::Match($tabDocXml, '<w:sectPr.*?</w:sectPr>|<w:sectPr[^/]*/>', [System.Text.RegularExpressions.RegexOptions]::Singleline).Value
$tabAnchorBlock = [regex]::Match($tabDocXml, '<mc:AlternateContent>.*?</mc:AlternateContent>', [System.Text.RegularExpressions.RegexOptions]::Singleline).Value
if (-not $rootOpen -or -not $sectPr -or -not $tabAnchorBlock) { Fail "Could not extract the shifted tab anchor from $tabPath" 8 }

function New-BodyParagraph {
    # Yellow-highlighted + the ACTUAL resolved bin id (not just a generic "Tray 2" label)
    # -- for glancing at a stack of printed sheets and spotting a mismatch quickly. This
    # is still a claim printed BY the script, not independent proof of the real tray used
    # (see the standing caveat: verify against what the printer/tray physically did).
    param([string] $Label, [int] $TrayId, [string] $SectPrXml)
    $pPr = if ($SectPrXml) { "<w:pPr>$SectPrXml</w:pPr>" } else { '' }
    $fullLabel = "$Label (resolved bin id: $TrayId)"
    "<w:p>$pPr<w:r><w:rPr><w:b/><w:sz w:val=""96""/><w:shd w:val=""clear"" w:color=""auto"" w:fill=""FFFF00""/></w:rPr><w:t>$fullLabel</w:t></w:r></w:p>"
}

$body1 = New-BodyParagraph -Label 'BODY -- expect Tray 2' -TrayId $tBody -SectPrXml $sectPr
$tabPara = "<w:p><w:pPr>$sectPr</w:pPr><w:r><w:rPr><w:noProof/></w:rPr>$tabAnchorBlock</w:r></w:p>"
$body2 = New-BodyParagraph -Label 'BODY -- expect Tray 2' -TrayId $tBody -SectPrXml $null

$mixedDocXml = $rootOpen + '<w:body>' + $body1 + $tabPara + $body2 + $sectPr + '</w:body></w:document>'

$mixedPath = Join-Path $scriptDir ("mixed-test-tab{0}.docx" -f $TabNumber)
Copy-Item $tabPath $mixedPath -Force
$zip = [System.IO.Compression.ZipFile]::Open($mixedPath, [System.IO.Compression.ZipArchiveMode]::Update)
try {
    $entry = $zip.GetEntry('word/document.xml')
    $entry.Delete()
    $newEntry = $zip.CreateEntry('word/document.xml')
    $writeStream = $newEntry.Open()
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($mixedDocXml)
    $writeStream.Write($bytes, 0, $bytes.Length)
    $writeStream.Close()
} finally { $zip.Dispose() }
Write-Host "Wrote $mixedPath (3 sections: BODY / TAB / BODY)" -ForegroundColor Green

Write-Host ""
Write-Host ("This will PHYSICALLY PRINT 3 pages via Word on '$Printer', {0} time(s), same job/position every time:" -f $Copies) -ForegroundColor Yellow
Write-Host "  page 1 = BODY (Tray 2), page 2 = TAB (Tray 1), page 3 = BODY (Tray 2)."
Write-Host "  Make sure Tray 1 = tab stock and Tray 2 = letter/plain are loaded."
$confirm = Read-Host "  Type PRINT to send to the device (anything else aborts)"
if ($confirm -ne 'PRINT') { Write-Host "  Aborted -- nothing sent." -ForegroundColor Yellow; exit 0 }

Write-Host ""
Write-Host "== Printing via Word (silent) ==" -ForegroundColor Cyan
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
try {
    $doc = $word.Documents.Open($mixedPath, $false, $true)
    try {
        $word.ActivePrinter = $Printer
        $trays = @($tBody, $tTab, $tBody)
        Write-Host ("  Document has {0} section(s); expecting 3." -f $doc.Sections.Count)
        for ($i = 0; $i -lt $doc.Sections.Count; $i++) {
            $doc.Sections.Item($i + 1).PageSetup.FirstPageTray = $trays[$i]
            $doc.Sections.Item($i + 1).PageSetup.OtherPagesTray = $trays[$i]
        }
        # Looping PrintOut() $Copies times (same 3-page job every time) rather than
        # passing Word's Copies parameter positionally -- not confident enough of
        # PrintOut's exact positional argument order from memory to risk a miscount.
        for ($c = 1; $c -le $Copies; $c++) {
            Write-Host ("  Printing set {0} of {1} (3 pages)..." -f $c, $Copies)
            $doc.PrintOut()
        }
    } finally {
        $doc.Close([ref]$false)
    }
} finally {
    $word.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
}

Write-Host ""
Write-Host ("Sent {0} set(s) of BODY/TAB/BODY to '{1}' via Word, same position every time. Check each physical sheet against what it says it expects, and whether it's consistent across sets." -f $Copies, $Printer) -ForegroundColor Green
Write-Host "  All 3 correct -> Word's print pipeline preserves per-page tray switching; the docx/Word production path is viable." -ForegroundColor Green
Write-Host "  Collapsed to one tray -> Word behaves like the original PrintTicket/XPS path; fall back to raw-GDI drawing for the real service." -ForegroundColor Yellow
