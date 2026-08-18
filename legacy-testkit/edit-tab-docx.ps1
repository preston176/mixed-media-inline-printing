#requires -Version 5.1
<#
  edit-tab-docx.ps1 -- given the real 5th-cut-1-to-500.docx template, produce a
  SINGLE-PAGE docx for ONE tab: its {{N}} merge tag replaced with the plain number,
  and its position shifted by a nudge (inches) -- in ALL THREE places Word redundantly
  encodes the same position, so the file stays internally consistent:
    1. wp:positionH/wp:positionV posOffset (EMU) -- the modern DrawingML anchor
    2. a:xfrm's a:off x/y (EMU) -- kept in sync with #1 by Word internally
    3. the legacy VML fallback's style="...margin-left:...pt;margin-top:...pt..."
  (mc:AlternateContent wraps #1 and #3 as SIBLINGS, not nested -- the edit scope has
  to be the whole AlternateContent block to reach both.)

  DELIBERATELY single-page output (not the full 500-page template with one anchor
  edited): a print-automation step calling Word's PrintOut() can then be called with
  no page-range arguments and still only ever print the one page, avoiding needing to
  get the WdPrintOutRange enum's exact value right from memory (unverifiable without
  Word on this dev machine) just to slice one page out of 500. All other parts of the
  docx (styles, fonts, theme) are carried over unchanged from the template.

  VERIFIED before being ported here: this exact algorithm was prototyped in Python
  against the real template (off-hardware, on macOS, since this dev machine has no
  Word/PowerShell-on-Windows to test the .ps1 itself directly). Confirmed: EMU/pt
  arithmetic exactly matches expectation, the produced docx opens without corruption,
  and the edited page renders with the plain number at the shifted position.

  NOT VERIFIED: whether Word's rendering of wps:bodyPr vert="vert" (the rotated text)
  survives this edit identically in real Word -- the macOS verification tool
  (QuickLook) rendered the text horizontally, not rotated, which may just be a
  QuickLook limitation (this edit never touches the vert attribute) rather than a
  real problem -- confirm on the actual target machine before trusting it.

  Run:
    powershell -ExecutionPolicy Bypass -File .\edit-tab-docx.ps1 -TabNumber 2 -NudgeXIn -0.143333 -NudgeYIn 0
#>

[CmdletBinding()]
param(
    [string] $TemplatePath,
    [Parameter(Mandatory)][int] $TabNumber,
    [string] $Text,                     # optional: printed instead of the bare number.
                                         # TabNumber still selects WHICH of the 500
                                         # template positions/geometry to use -- Text
                                         # only overrides what gets displayed there.
    [double] $NudgeXIn = 0,
    [double] $NudgeYIn = 0,
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
function Fail { param([string] $m, [int] $c = 1) Write-Host "`nFAIL: $m" -ForegroundColor Red; exit $c }

# $PSScriptRoot isn't reliably populated yet while param() defaults are evaluated --
# only once the script body starts running -- so the default path is computed here,
# not in the param block. $MyInvocation.MyCommand.Path is a fallback for the rare case
# $PSScriptRoot is still empty (e.g. certain dot-sourcing/invocation contexts).
if (-not $TemplatePath) {
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $TemplatePath = Join-Path $scriptDir '5th-cut-1-to-500.docx'
}

if (-not (Test-Path $TemplatePath)) { Fail "Template not found: $TemplatePath" 2 }
if ($TabNumber -lt 1 -or $TabNumber -gt 500) { Fail "TabNumber must be 1..500." 2 }
if (-not $OutputPath) { $OutputPath = Join-Path (Split-Path $TemplatePath -Parent) ("tab{0}-edited.docx" -f $TabNumber) }

# Two separate assemblies: ZipFile (the helper class) lives in .FileSystem, but
# ZipArchiveMode (the enum our code references directly) lives in the base
# System.IO.Compression assembly -- both must be loaded explicitly.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$EMU_PER_INCH = 914400
$PT_PER_INCH = 72.0
$nudgeXEmu = [int][Math]::Round($NudgeXIn * $EMU_PER_INCH)
$nudgeYEmu = [int][Math]::Round($NudgeYIn * $EMU_PER_INCH)
$nudgeXPt = $NudgeXIn * $PT_PER_INCH
$nudgeYPt = $NudgeYIn * $PT_PER_INCH

function Shift-PosOffset {
    # Checks IsMatch BEFORE replacing rather than comparing before/after text: when
    # Delta is exactly 0 (e.g. no Y correction was needed), a successful match+replace
    # produces text IDENTICAL to the input, which a before/after string comparison
    # can't tell apart from "no match found at all".
    param([string] $Text, [string] $Which, [int] $Delta)
    $pattern = "(<wp:position${Which}[^>]*><wp:posOffset>)(-?\d+)(</wp:posOffset>)"
    if (-not [regex]::IsMatch($Text, $pattern)) { throw "position$Which posOffset not found in the target anchor" }
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator] {
        param($m)
        $m.Groups[1].Value + ([string]([int]$m.Groups[2].Value + $Delta)) + $m.Groups[3].Value
    }
    return [regex]::Replace($Text, $pattern, $evaluator)
}

function Shift-Off {
    param([string] $Text, [int] $DeltaX, [int] $DeltaY)
    $pattern = '(<a:off x=")(-?\d+)(" y=")(-?\d+)(")'
    if (-not [regex]::IsMatch($Text, $pattern)) { throw "a:off x/y not found in the target anchor" }
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator] {
        param($m)
        $x = [int]$m.Groups[2].Value + $DeltaX
        $y = [int]$m.Groups[4].Value + $DeltaY
        "$($m.Groups[1].Value)$x$($m.Groups[3].Value)$y$($m.Groups[5].Value)"
    }
    return [regex]::Replace($Text, $pattern, $evaluator)
}

function Shift-MarginProp {
    # own function-scoped params so the nested MatchEvaluator closes over THESE, not a
    # shared loop variable -- avoids relying on regex backreference syntax ($1/${1})
    # inside a PowerShell-interpolated string, which needs fragile backtick-escaping.
    param([string] $Style, [string] $Prop, [double] $Delta)
    $pattern = "($Prop`:)(-?[\d.]+)pt"
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator] {
        param($m)
        $newVal = [double]$m.Groups[2].Value + $Delta
        "$($m.Groups[1].Value)$($newVal.ToString('F2'))pt"
    }
    return [regex]::Replace($Style, $pattern, $evaluator)
}

function Shift-VmlStyle {
    param([string] $Text, [double] $DeltaXPt, [double] $DeltaYPt)
    if (-not [regex]::IsMatch($Text, 'style="([^"]*)"')) { throw "VML style attribute not found in the target anchor" }
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator] {
        param($m)
        $style = $m.Groups[1].Value
        $style = Shift-MarginProp -Style $style -Prop 'margin-left' -Delta $DeltaXPt
        $style = Shift-MarginProp -Style $style -Prop 'margin-top' -Delta $DeltaYPt
        "style=`"$style`""
    }
    return [regex]::Replace($Text, 'style="([^"]*)"', $evaluator, 1)
}

$zip = [System.IO.Compression.ZipFile]::Open($TemplatePath, [System.IO.Compression.ZipArchiveMode]::Read)
try {
    $entry = $zip.GetEntry('word/document.xml')
    if (-not $entry) { Fail "word/document.xml not found in $TemplatePath" 3 }
    $stream = $entry.Open()
    $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
    $docXml = $reader.ReadToEnd()
    $reader.Close(); $stream.Close()
} finally { $zip.Dispose() }

$tag = "{{$TabNumber}}"
$blocks = [regex]::Matches($docXml, '<mc:AlternateContent>.*?</mc:AlternateContent>', [System.Text.RegularExpressions.RegexOptions]::Singleline)
$target = $null
foreach ($b in $blocks) { if ($b.Value.Contains($tag)) { $target = $b; break } }
if (-not $target) { Fail "Tab $TabNumber's AlternateContent block ($tag) not found in the template." 4 }

try {
    $block = $target.Value
    $block = Shift-PosOffset -Text $block -Which 'H' -Delta $nudgeXEmu
    $block = Shift-PosOffset -Text $block -Which 'V' -Delta $nudgeYEmu
    $block = Shift-Off -Text $block -DeltaX $nudgeXEmu -DeltaY $nudgeYEmu
    $block = Shift-VmlStyle -Text $block -DeltaXPt $nudgeXPt -DeltaYPt $nudgeYPt
} catch { Fail "Editing tab $TabNumber's anchor failed: $($_.Exception.Message)" 5 }

$displayText = if ($Text) { $Text } else { "$TabNumber" }
$occurrences = ([regex]::Matches($block, [regex]::Escape($tag))).Count
$block = $block.Replace($tag, $displayText)

# Build a MINIMAL SINGLE-PAGE document.xml (root namespaces + one paragraph containing
# just this one tab's anchor + one sectPr for page setup), rather than splicing the
# edit back into the full 500-page template. This is deliberate, not a shortcut: a
# single-page output means Word's PrintOut() can be called with no page-range
# arguments at all and still only ever print the one page -- avoiding needing to get
# the WdPrintOutRange enum's exact integer value right from memory (unverifiable from
# this dev machine) to slice one page out of 500. Other parts of the docx (styles,
# fonts, theme) are carried over unchanged, so the real Arial Bold styling still
# applies -- verified visually against this exact technique before it was ported here.
$rootOpen = [regex]::Match($docXml, '<w:document[^>]*>').Value
if (-not $rootOpen) { Fail "Could not find the <w:document> root element in the template." 4 }
$sectPr = [regex]::Match($docXml, '<w:sectPr.*?</w:sectPr>|<w:sectPr[^/]*/>', [System.Text.RegularExpressions.RegexOptions]::Singleline).Value
if (-not $sectPr) { Fail "Could not find a <w:sectPr> (page setup) in the template." 4 }

$newDocXml = $rootOpen + '<w:body>' + '<w:p><w:r><w:rPr><w:noProof/></w:rPr>' + $block + '</w:r></w:p>' + $sectPr + '</w:body></w:document>'

Copy-Item $TemplatePath $OutputPath -Force
$zip = [System.IO.Compression.ZipFile]::Open($OutputPath, [System.IO.Compression.ZipArchiveMode]::Update)
try {
    $entry = $zip.GetEntry('word/document.xml')
    $entry.Delete()
    $newEntry = $zip.CreateEntry('word/document.xml')
    $writeStream = $newEntry.Open()
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($newDocXml)
    $writeStream.Write($bytes, 0, $bytes.Length)
    $writeStream.Close()
} finally { $zip.Dispose() }

Write-Host ("Tab {0}: replaced {1} occurrence(s) of {2} with '{3}'." -f $TabNumber, $occurrences, $tag, $displayText) -ForegroundColor Green
Write-Host ("Shifted by x={0:N3}in ({1} EMU / {2:N2}pt)  y={3:N3}in ({4} EMU / {5:N2}pt)" -f `
    $NudgeXIn, $nudgeXEmu, $nudgeXPt, $NudgeYIn, $nudgeYEmu, $nudgeYPt) -ForegroundColor Green
Write-Host "Wrote $OutputPath" -ForegroundColor Green
Write-Host "Open it in Word and check: does the number look right, positioned correctly, AND still rotated (vert=vert)?" -ForegroundColor Yellow
