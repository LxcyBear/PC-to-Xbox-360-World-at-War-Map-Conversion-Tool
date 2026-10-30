[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Parse ALL multi-map PowerShell scripts without executing any of them.
# Windows PowerShell 5.1's built-in AST parser detects invalid interpolation
# such as "$variable:" in double-quoted strings BEFORE a map conversion run.
$dir = $PSScriptRoot
$files = @(Get-ChildItem -LiteralPath $dir -Filter '*WaWMultiMap*.ps1' -File |
    Sort-Object Name)
if ($files.Count -eq 0) {
    throw "No WaW multi-map scripts found in $dir"
}

$failed = 0
foreach ($file in $files) {
    $tokens = $null
    $parseErrors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile(
        $file.FullName,
        [ref]$tokens,
        [ref]$parseErrors
    )
    if ($null -ne $parseErrors -and $parseErrors.Count -gt 0) {
        $failed++
        Write-Host "[PS51-SYNTAX] FAIL $($file.Name)"
        foreach ($issue in $parseErrors) {
            Write-Host "[PS51-SYNTAX]   line=$($issue.Extent.StartLineNumber) col=$($issue.Extent.StartColumnNumber) $($issue.Message)"
        }
    } else {
        Write-Host "[PS51-SYNTAX] PASS $($file.Name)"
    }
}

if ($failed -gt 0) {
    throw "$failed multi-map PowerShell script(s) have parser errors."
}
Write-Host "[PS51-SYNTAX] PASS $($files.Count) multi-map scripts. No builds, FFs, or OAT calls were performed."
