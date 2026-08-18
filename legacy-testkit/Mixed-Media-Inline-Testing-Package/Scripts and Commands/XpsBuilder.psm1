#requires -Version 5.1
<#
  XpsBuilder.psm1 -- authors a minimal but valid XPS (OpenXPS/OPC) package with a
  job-level PrintTicket and optional per-page PrintTickets, then leaves it on disk
  so it can be submitted verbatim via PrintQueue.AddJob(..., fastCopy: $true).

  Authored by hand (System.IO.Compression) rather than via WPF so the exact bytes
  are transparent, diffable, and buildable/validatable off-Windows.

  Pages carry only a simple coloured mark + page-number bars (no fonts needed) --
  enough to tell sheets apart. The point is which TRAY feeds, not the content.

  New-XpsPackage
    -Path        output .xps file
    -Prefixes    the ordered prefix->URI map from Read-PrintCapabilities (.Prefixes)
    -JobFeatures @( @{ Feature='psk:JobInputBin'; Option='spc0000:Tray2' }, ... )
    -Pages       @( @{ MediaType='psk:Plain' }, @{ MediaType='spc0000:User...275' }, ... )
                 a page with MediaType=$null gets no page-level ticket.
#>

$script:PsfUri = 'http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework'
$script:PskUri = 'http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords'
$script:XpsNs  = 'http://schemas.microsoft.com/xps/2005/06'

function New-PrintTicketXml {
    param([Parameter(Mandatory)] $Prefixes, [Parameter(Mandatory)][object[]] $Features)

    $decl = ''
    $havePsf = $false; $havePsk = $false
    foreach ($k in $Prefixes.Keys) {
        if ($k -eq '(default)') { continue }
        if ($k -eq 'psf') { $havePsf = $true }
        if ($k -eq 'psk') { $havePsk = $true }
        $decl += ' xmlns:{0}="{1}"' -f $k, $Prefixes[$k]
    }
    if (-not $havePsf) { $decl += ' xmlns:psf="{0}"' -f $script:PsfUri }
    if (-not $havePsk) { $decl += ' xmlns:psk="{0}"' -f $script:PskUri }

    $body = ''
    foreach ($f in $Features) {
        $body += ('  <psf:Feature name="{0}"><psf:Option name="{1}" /></psf:Feature>{2}' -f `
            $f.Feature, $f.Option, [Environment]::NewLine)
    }

    $tpl = @'
<?xml version="1.0" encoding="UTF-8"?>
<psf:PrintTicket{DECL} version="1">
{BODY}</psf:PrintTicket>
'@
    return $tpl.Replace('{DECL}', $decl).Replace('{BODY}', $body)
}

function New-FixedPageXml {
    param([int] $PageNumber)

    $paths = '  <Path Fill="#FF1F6F78" Data="M 60,72 L 756,72 L 756,152 L 60,152 Z" />' + [Environment]::NewLine
    for ($j = 0; $j -lt $PageNumber; $j++) {
        $x = 60 + ($j * 56)
        $paths += ('  <Path Fill="#FF14323A" Data="M {0},190 L {1},190 L {1},230 L {0},230 Z" />{2}' -f `
            $x, ($x + 40), [Environment]::NewLine)
    }

    $tpl = @'
<?xml version="1.0" encoding="UTF-8"?>
<FixedPage xmlns="{XPS}" Width="816" Height="1056" xml:lang="en-US">
{PATHS}</FixedPage>
'@
    return $tpl.Replace('{XPS}', $script:XpsNs).Replace('{PATHS}', $paths)
}

function New-XpsPackage {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)] $Prefixes,
        [Parameter(Mandatory)][object[]] $JobFeatures,
        [Parameter(Mandatory)][object[]] $Pages
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
    if (Test-Path $Path) { Remove-Item $Path -Force }
    $enc = New-Object System.Text.UTF8Encoding($false)
    $zip = [System.IO.Compression.ZipFile]::Open($Path, 'Create')

    $addPart = {
        param([string] $Name, [string] $Content)
        $entry = $zip.CreateEntry($Name)
        $stream = $entry.Open()
        $writer = New-Object System.IO.StreamWriter($stream, $enc)
        $writer.Write($Content); $writer.Flush(); $writer.Dispose(); $stream.Dispose()
    }

    try {
        $nl = [Environment]::NewLine

        # --- content-type overrides + [Content_Types].xml ---
        $overrides = '  <Override PartName="/Metadata/Job_PT.xml" ContentType="application/vnd.ms-printing.printticket+xml" />' + $nl
        for ($i = 1; $i -le $Pages.Count; $i++) {
            if ($null -ne $Pages[$i - 1].MediaType) {
                $overrides += ('  <Override PartName="/Metadata/Page{0}_PT.xml" ContentType="application/vnd.ms-printing.printticket+xml" />{1}' -f $i, $nl)
            }
        }
        $ct = @'
<?xml version="1.0" encoding="UTF-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
  <Default Extension="fdseq" ContentType="application/vnd.ms-package.xps-fixeddocumentsequence+xml" />
  <Default Extension="fdoc" ContentType="application/vnd.ms-package.xps-fixeddocument+xml" />
  <Default Extension="fpage" ContentType="application/vnd.ms-package.xps-fixedpage+xml" />
{OVR}</Types>
'@
        & $addPart '[Content_Types].xml' ($ct.Replace('{OVR}', $overrides))

        # --- package root relationship -> fixed document sequence ---
        & $addPart '_rels/.rels' @'
<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.microsoft.com/xps/2005/06/fixedrepresentation" Target="/FixedDocumentSequence.fdseq" />
</Relationships>
'@

        # --- fixed document sequence + its job PrintTicket relationship ---
        & $addPart 'FixedDocumentSequence.fdseq' @'
<?xml version="1.0" encoding="UTF-8"?>
<FixedDocumentSequence xmlns="http://schemas.microsoft.com/xps/2005/06">
  <DocumentReference Source="/Documents/1/FixedDocument.fdoc" />
</FixedDocumentSequence>
'@
        & $addPart '_rels/FixedDocumentSequence.fdseq.rels' @'
<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rIdJPT" Type="http://schemas.microsoft.com/xps/2005/06/printticket" Target="/Metadata/Job_PT.xml" />
</Relationships>
'@

        # --- fixed document listing the pages ---
        $pageRefs = ''
        for ($i = 1; $i -le $Pages.Count; $i++) {
            $pageRefs += ('  <PageContent Source="/Documents/1/Pages/{0}.fpage" />{1}' -f $i, $nl)
        }
        $fdoc = @'
<?xml version="1.0" encoding="UTF-8"?>
<FixedDocument xmlns="http://schemas.microsoft.com/xps/2005/06">
{REFS}</FixedDocument>
'@
        & $addPart 'Documents/1/FixedDocument.fdoc' ($fdoc.Replace('{REFS}', $pageRefs))

        # --- job PrintTicket ---
        & $addPart 'Metadata/Job_PT.xml' (New-PrintTicketXml -Prefixes $Prefixes -Features $JobFeatures)

        # --- pages, their content, and per-page tickets ---
        for ($i = 1; $i -le $Pages.Count; $i++) {
            & $addPart ("Documents/1/Pages/{0}.fpage" -f $i) (New-FixedPageXml -PageNumber $i)
            $mt = $Pages[$i - 1].MediaType
            if ($null -ne $mt) {
                $ptRel = @'
<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rIdPPT" Type="http://schemas.microsoft.com/xps/2005/06/printticket" Target="/Metadata/Page{N}_PT.xml" />
</Relationships>
'@
                & $addPart ("Documents/1/Pages/_rels/{0}.fpage.rels" -f $i) ($ptRel.Replace('{N}', "$i"))
                $pagePt = New-PrintTicketXml -Prefixes $Prefixes -Features @(@{ Feature = 'psk:PageMediaType'; Option = $mt })
                & $addPart ("Metadata/Page{0}_PT.xml" -f $i) $pagePt
            }
        }
    }
    finally {
        $zip.Dispose()
    }

    return (Get-Item $Path)
}

Export-ModuleMember -Function New-XpsPackage, New-PrintTicketXml, New-FixedPageXml
