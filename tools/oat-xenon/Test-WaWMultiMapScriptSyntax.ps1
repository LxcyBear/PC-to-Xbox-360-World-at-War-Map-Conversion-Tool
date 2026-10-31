[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Parse ALL multi-map PowerShell scripts without executing any of them.
# Windows PowerShell 5.1's built-in AST parser detects invalid interpolation
# such as "$variable:" in double-quoted strings BEFORE a map conversion run.
$dir = $PSScriptRoot
# Include CodXe beta layout checks alongside the multi-map scripts, so
# a parser error in a newly shipped compatibility diagnostic is caught BEFORE
# any data inspection or FF operation on Windows PowerShell 5.1.
$files = @(
    @(Get-ChildItem -LiteralPath $dir -Filter '*WaWMultiMap*.ps1' -File)
    @(Get-Item -LiteralPath (Join-Path $dir 'Test-CodXeT4BetaLayout.ps1') -ErrorAction Stop)
    @(Get-Item -LiteralPath (Join-Path $dir 'Stage-CodXeT4BetaPackage.ps1') -ErrorAction Stop)
    @(Get-Item -LiteralPath (Join-Path $dir 'Stage-AztecFirstExceptionCanary.ps1') -ErrorAction Stop)
    @(Get-Item -LiteralPath (Join-Path $dir 'Verify-AztecXboxCopiedFFs.ps1') -ErrorAction Stop)
    @(Get-Item -LiteralPath (Join-Path $dir 'Stage-AztecWorldFirstExceptionCanary.ps1') -ErrorAction Stop)
    @(Get-Item -LiteralPath (Join-Path $dir 'Verify-AztecWorldXboxCopiedFFs.ps1') -ErrorAction Stop)
    @(Get-Item -LiteralPath (Join-Path $dir 'Plan-Aztec1903NativeDrawRelocations.ps1') -ErrorAction Stop)
    @(Get-Item -LiteralPath (Join-Path $dir 'Build-Aztec1903DrawPointerRelocationCandidate.ps1') -ErrorAction Stop)
    @(Get-Item -LiteralPath (Join-Path $dir 'Verify-AztecSharedDrawPointerCodec.ps1') -ErrorAction Stop)
    @(Get-Item -LiteralPath (Join-Path $dir 'Audit-Aztec1903StaticDrawScalarEquivalence.ps1') -ErrorAction Stop)
    @(Get-Item -LiteralPath (Join-Path $dir 'Build-AztecGate10SourceFlagsCandidate.ps1') -ErrorAction Stop)
    @(Get-Item -LiteralPath (Join-Path $dir 'Build-AztecGate10SourceLightCandidate.ps1') -ErrorAction Stop)
    @(Get-Item -LiteralPath (Join-Path $dir 'Verify-AztecGate10LiveLayoutRebase.ps1') -ErrorAction Stop)
    @(Get-Item -LiteralPath (Join-Path $dir 'Run-AztecGate8NativeStaticDrawAbiWitness.ps1') -ErrorAction Stop)
    @(Get-Item -LiteralPath (Join-Path $dir 'Audit-Aztec100PercentPackage.ps1') -ErrorAction Stop)
    @(Get-Item -LiteralPath (Join-Path $dir 'Test-WaWRuntimeScriptDependency.ps1') -ErrorAction Stop)
) | Sort-Object Name
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
