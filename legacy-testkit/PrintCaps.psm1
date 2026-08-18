#requires -Version 5.1
<#
  PrintCaps.psm1 -- pure PrintCapabilities parser.

  No spooler, no System.Printing, no Windows I/O: it takes the raw
  PrintCapabilities XML as a string and returns a structured summary, so it can
  be unit-tested off-Windows. testkit.ps1 fetches the XML from the driver (that
  part is Windows-only) and hands it here.

  Returns a result object:
    Ok           [bool]    safe to proceed (well-formed + has an input-bin feature)
    ErrorKind    [string]  $null | 'MalformedXml' | 'NoFeatures' | 'NoInputBin'
    ErrorMessage [string]
    Prefixes     [ordered] xmlns prefix -> URI (vendor namespaces included)
    Features     [array]   every feature: @{ Name; Options=@(@{ Name; DisplayName }) }
    Relevant     [array]   subset whose name touches tray/media/insert behaviour

  Fails closed: malformed XML, no features, or no input-bin feature all yield
  Ok=$false with a specific ErrorKind. It never guesses a tray.
#>

# Features that decide tray / media / insert behaviour. Sharp exposes tab and
# mixed-media via its [Inserts] tab (Tab Paper Print / Covers/Inserts) plus a
# [Tab Paper] media type from the bypass tray, so we widen past the standard
# bin/media keywords to Insert / Cover / Tab as well.
$script:RelevantPattern = 'InputBin|MediaType|Insert|Cover|Tab'
$script:PsfUri = 'http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework'

function Read-PrintCapabilities {
    [CmdletBinding()]
    param([AllowEmptyString()][string] $Xml = '')

    $result = [pscustomobject]@{
        Ok           = $false
        ErrorKind    = $null
        ErrorMessage = $null
        Prefixes     = [ordered]@{}
        Features     = @()
        Relevant     = @()
    }

    if ([string]::IsNullOrWhiteSpace($Xml)) {
        $result.ErrorKind = 'MalformedXml'
        $result.ErrorMessage = 'PrintCapabilities XML was empty.'
        return $result
    }

    $doc = New-Object System.Xml.XmlDocument
    try {
        $doc.LoadXml($Xml)
    } catch {
        $result.ErrorKind = 'MalformedXml'
        $result.ErrorMessage = "PrintCapabilities XML did not parse: $($_.Exception.Message)"
        return $result
    }

    foreach ($attr in $doc.DocumentElement.Attributes) {
        if ($attr.Name -eq 'xmlns') { $result.Prefixes['(default)'] = $attr.Value }
        elseif ($attr.Name -like 'xmlns:*') { $result.Prefixes[$attr.Name.Substring(6)] = $attr.Value }
    }

    $ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
    $ns.AddNamespace('psf', $script:PsfUri)

    $featureNodes = $doc.SelectNodes('//psf:Feature', $ns)
    if ($null -eq $featureNodes -or $featureNodes.Count -eq 0) {
        $result.ErrorKind = 'NoFeatures'
        $result.ErrorMessage = 'No <psf:Feature> elements found in PrintCapabilities.'
        return $result
    }

    $features = @()
    foreach ($f in $featureNodes) {
        $options = @()
        foreach ($o in $f.SelectNodes('psf:Option', $ns)) {
            $displayName = ''
            $valueNode = $o.SelectSingleNode("psf:Property[@name='psk:DisplayName']/psf:Value", $ns)
            if ($valueNode) { $displayName = $valueNode.InnerText.Trim() }
            $options += [pscustomobject]@{
                Name        = $o.GetAttribute('name')
                DisplayName = $displayName
            }
        }
        $features += [pscustomobject]@{
            Name    = $f.GetAttribute('name')
            Options = $options
        }
    }
    $result.Features = $features
    $result.Relevant = @($features | Where-Object { $_.Name -match $script:RelevantPattern })

    if (-not (@($features.Name) -match 'InputBin')) {
        $result.ErrorKind = 'NoInputBin'
        $result.ErrorMessage = 'PrintCapabilities contained no *InputBin feature; refusing to guess a tray.'
        return $result
    }

    $result.Ok = $true
    return $result
}

Export-ModuleMember -Function Read-PrintCapabilities
