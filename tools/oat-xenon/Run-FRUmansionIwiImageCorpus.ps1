param(
    [string]$PcMapsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps',
    [string]$MapName = 'nazi_zombie_FRUmansion'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$outputRoot = Join-Path $repo 'output'
$bindingRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_root_binding'
$identityDir = Join-Path $bindingRoot 'native_identity'
$identityManifest = @(Get-ChildItem -LiteralPath $identityDir -Filter '*_native_identity_manifest.json' -File |
    Sort-Object FullName |
    Select-Object -First 1)

if (-not (Test-Path -LiteralPath $PcMapsRoot -PathType Container)) {
    throw "PC maps root not found: $PcMapsRoot"
}
if ($identityManifest.Count -ne 1) {
    throw "Could not identify Stage 4C native identity manifest under: $identityDir"
}

Write-Host '[NUCLEAR-4G] ================================================================'
Write-Host '[NUCLEAR-4G] IWD IWI IMAGE CORPUS -> NATIVE XENON IMAGE CENSUS'
Write-Host '[NUCLEAR-4G] ================================================================'
Write-Host "[NUCLEAR-4G] PC maps root      : $PcMapsRoot"
Write-Host "[NUCLEAR-4G] Map               : $MapName"
Write-Host "[NUCLEAR-4G] Native identities : $($identityManifest[0].FullName)"
Write-Host ''
Write-Host '[NUCLEAR-4G] Goal: identify exactly which .iwi formats account for the 928 missing native image roots.'
Write-Host '[NUCLEAR-4G] No FF is emitted; this stage chooses the highest-payoff image codec work.'
Write-Host ''

Push-Location $repo
try {
    dotnet run --project '.\src\WaW360.Cli\WaW360.Cli.csproj' -c Release -- linker-v2-iwi-image-corpus "$PcMapsRoot" "$MapName" "$outputRoot" "$($identityManifest[0].FullName)"
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

$report = Join-Path $outputRoot "$MapName\nuclear_4g_iwi_image_corpus\nuclear_4g_iwi_image_corpus.txt"

Write-Host ''
if (Test-Path -LiteralPath $report -PathType Leaf) {
    Write-Host '[NUCLEAR-4G] ================= IMAGE CORPUS REPORT ==========================='
    Get-Content -LiteralPath $report
    Write-Host '[NUCLEAR-4G] ================================================================='
}

if ($code -ne 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4G] IMAGE CORPUS CENSUS: BLOCKED' -ForegroundColor Yellow
    exit $code
}

Write-Host ''
Write-Host '[NUCLEAR-4G] IMAGE CORPUS CENSUS: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4G] Next target is the largest matched IWI format group by bytes.'
