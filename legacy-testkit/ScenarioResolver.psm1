#requires -Version 5.1
<#
  ScenarioResolver.psm1 -- resolves a human intent ("the tray holding tab stock",
  "Tab Paper media") to the driver's *exact* option string, pulled from the
  parsed PrintCapabilities (see PrintCaps.psm1). Never hardcodes a value; fails
  closed when a match is missing or ambiguous, so the harness refuses to submit
  rather than guessing a tray.

  Resolve-CapsOption -Caps <parsed caps> -FeaturePattern <regex> -OptionPattern <regex>

  Searches options of every feature whose Name matches FeaturePattern, keeping
  options whose Name OR DisplayName matches OptionPattern. Returns:
    Ok           [bool]
    ErrorKind    [string]  $null | 'NotFound' | 'Ambiguous'
    ErrorMessage [string]
    Value        [string]  the exact option Name to put in a PrintTicket
    DisplayName  [string]
    FeatureName  [string]  which feature it came from
    Candidates   [array]   all matches (for diagnosing Ambiguous)
#>

function Resolve-CapsOption {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Caps,
        [Parameter(Mandatory)][string] $FeaturePattern,
        [Parameter(Mandatory)][string] $OptionPattern
    )

    $result = [pscustomobject]@{
        Ok           = $false
        ErrorKind    = $null
        ErrorMessage = $null
        Value        = $null
        DisplayName  = $null
        FeatureName  = $null
        Candidates   = @()
    }

    $features = @($Caps.Features | Where-Object { $_.Name -match $FeaturePattern })
    if ($features.Count -eq 0) {
        $result.ErrorKind = 'NotFound'
        $result.ErrorMessage = "No feature matched /$FeaturePattern/."
        return $result
    }

    # NB: avoid the automatic $Matches variable that -match populates.
    $hits = @()
    foreach ($f in $features) {
        foreach ($o in $f.Options) {
            if (($o.Name -match $OptionPattern) -or ($o.DisplayName -match $OptionPattern)) {
                $hits += [pscustomobject]@{
                    Value       = $o.Name
                    DisplayName = $o.DisplayName
                    FeatureName = $f.Name
                }
            }
        }
    }
    $result.Candidates = $hits

    if ($hits.Count -eq 0) {
        $result.ErrorKind = 'NotFound'
        $result.ErrorMessage = "No option under /$FeaturePattern/ matched /$OptionPattern/."
        return $result
    }
    if ($hits.Count -gt 1) {
        $result.ErrorKind = 'Ambiguous'
        $result.ErrorMessage = ("Pattern /$OptionPattern/ matched {0} options: {1}" -f
            $hits.Count, (($hits | ForEach-Object { $_.Value }) -join ', '))
        return $result
    }

    $result.Ok          = $true
    $result.Value       = $hits[0].Value
    $result.DisplayName = $hits[0].DisplayName
    $result.FeatureName = $hits[0].FeatureName
    return $result
}

Export-ModuleMember -Function Resolve-CapsOption
