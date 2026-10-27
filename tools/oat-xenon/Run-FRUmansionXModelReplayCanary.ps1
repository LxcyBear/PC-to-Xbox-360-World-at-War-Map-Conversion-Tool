[CmdletBinding()]
param(
    [int]$Ordinal = 0,
    [string]$GraphRoot = '',
    [string]$OutputRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($GraphRoot)) {
    $GraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4xb_xmodel_relocatable_graph'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4xc_xmodel_replay'
}

$resultsRoot = Join-Path $GraphRoot 'results'
$resultFiles = @(Get-ChildItem -LiteralPath $resultsRoot -Filter ('{0:D3}_*.json' -f $Ordinal) -File -ErrorAction SilentlyContinue)
if ($resultFiles.Count -ne 1) {
    throw "Expected exactly one passing 4X-B result for ordinal $Ordinal; found $($resultFiles.Count)."
}

$row = Get-Content -LiteralPath $resultFiles[0].FullName -Raw | ConvertFrom-Json
if (-not [bool]$row.Pass) {
    throw "4X-B row for ordinal $Ordinal is not passing: $([string]$row.Error)"
}

$graphFile = [string]$row.GraphFile
$referenceFf = [string]$row.FastFile
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
foreach ($required in @($graphFile, $referenceFf, $unlinker)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required replay input missing: $required"
    }
}

$safe = ([string]$row.Name) -replace '[^A-Za-z0-9_.-]', '_'
$out = Join-Path $OutputRoot ('{0:D3}_{1}' -f $Ordinal, $safe)
if (Test-Path -LiteralPath $out) {
    Remove-Item -LiteralPath $out -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $out | Out-Null

Write-Host '[NUCLEAR-4X-C] =============================================================='
Write-Host '[NUCLEAR-4X-C] STANDALONE XENON XMODEL REPLAY'
Write-Host '[NUCLEAR-4X-C] =============================================================='
Write-Host "[NUCLEAR-4X-C] Ordinal      : $Ordinal"
Write-Host "[NUCLEAR-4X-C] Model        : $([string]$row.Name)"
Write-Host "[NUCLEAR-4X-C] Graph        : $graphFile"
Write-Host "[NUCLEAR-4X-C] Reference FF : $referenceFf"
Write-Host "[NUCLEAR-4X-C] Output       : $out"
Write-Host ''

& dotnet run --project (Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj') -c Release -- xenon-xmodel-replay $graphFile $referenceFf $out $unlinker
$exitCode = [int]$LASTEXITCODE
if ($exitCode -ne 0) {
    Write-Host ''
    Write-Host "[NUCLEAR-4X-C] REPLAY BLOCKED (exit $exitCode)" -ForegroundColor Yellow
    exit $exitCode
}

$manifest = Join-Path $out 'nuclear_4xc_xmodel_replay.json'
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
    throw "Replay command returned success but manifest is missing: $manifest"
}
$result = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
if (-not [bool]$result.ReplayPass) {
    throw 'Replay command returned success but ReplayPass is false.'
}

Write-Host ''
Write-Host '[NUCLEAR-4X-C] STANDALONE XMODEL REPLAY: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4X-C] OAT reopened the replayed FF cleanly and the relocatable graph redump matched exactly.'
exit 0
