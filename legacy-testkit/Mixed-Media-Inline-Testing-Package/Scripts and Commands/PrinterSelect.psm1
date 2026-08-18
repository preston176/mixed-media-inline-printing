#requires -Version 5.1
<#
  PrinterSelect.psm1 -- interactive printer picker for the testkit.

  Resolve-PrinterChoice (pure, unit-tested): validate an operator's typed choice
  against the printer-name list. Returns Ok + Name, or Ok=$false + ErrorKind
  ('Empty' | 'NotANumber' | 'OutOfRange'). Never coerces a bad choice.

  Select-PrinterInteractive (Windows-only, thin): print a numbered menu, read a
  choice, loop until valid. Uses Get-Printer / Read-Host, so it is not covered by
  the off-Windows test run.
#>

function Resolve-PrinterChoice {
    [CmdletBinding()]
    param(
        [string[]] $Names,
        [AllowEmptyString()][string] $Choice
    )

    $result = [pscustomobject]@{ Ok = $false; Name = $null; ErrorKind = $null }
    $trimmed = ("$Choice").Trim()

    if ($trimmed -eq '') { $result.ErrorKind = 'Empty'; return $result }
    if ($trimmed -notmatch '^\d+$') { $result.ErrorKind = 'NotANumber'; return $result }

    $index = [int] $trimmed
    if ($index -lt 1 -or $index -gt $Names.Count) { $result.ErrorKind = 'OutOfRange'; return $result }

    $result.Ok = $true
    $result.Name = $Names[$index - 1]
    return $result
}

function Select-PrinterInteractive {
    [CmdletBinding()]
    param(
        [object[]] $Printers,
        [int] $MaxAttempts = 3
    )

    if (-not $Printers) { $Printers = @(Get-Printer | Sort-Object Name) }
    if ($Printers.Count -eq 0) { throw 'No printers are installed on this machine.' }

    $names = @($Printers | ForEach-Object { $_.Name })

    Write-Host ""
    Write-Host "Select a printer:" -ForegroundColor Cyan
    for ($i = 0; $i -lt $names.Count; $i++) {
        "  [{0}] {1}" -f ($i + 1), $names[$i] | Write-Host
    }

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $raw = Read-Host "Enter number [1-$($names.Count)]"
        $choice = Resolve-PrinterChoice -Names $names -Choice $raw
        if ($choice.Ok) { return $choice.Name }
        Write-Host "  Not accepted ($($choice.ErrorKind)). Try again." -ForegroundColor Yellow
    }

    throw "No valid printer selected after $MaxAttempts attempts."
}

Export-ModuleMember -Function Resolve-PrinterChoice, Select-PrinterInteractive
