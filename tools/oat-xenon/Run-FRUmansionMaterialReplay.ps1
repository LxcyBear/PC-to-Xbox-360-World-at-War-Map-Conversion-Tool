[CmdletBinding()]
param(
    [string]$CorpusRoot = '',
    [string]$GraphRoot = '',
    [string]$OutputRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($CorpusRoot)) {
    $CorpusRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4z_material_corpus'
}
if ([string]::IsNullOrWhiteSpace($GraphRoot)) {
    $GraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4zb_material_relocatable_graph'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4zc_material_replay'
}

$corpusManifest = Join-Path $CorpusRoot 'nuclear_4za_material_corpus.json'
$graphManifest = Join-Path $GraphRoot 'nuclear_4zb_material_relocatable_graph.json'
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

foreach ($required in @($corpusManifest, $graphManifest, $unlinker)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "4Z-C replay input missing: $required"
    }
}

$corpus = Get-Content -LiteralPath $corpusManifest -Raw | ConvertFrom-Json
$graph = Get-Content -LiteralPath $graphManifest -Raw | ConvertFrom-Json

if ([string]$corpus.format -ne 'WaW360.Nuclear4ZA.MaterialCorpus.v1' -or
    -not [bool]$corpus.corpusPass -or
    -not [bool]$corpus.exactMaterialIdentitySet -or
    -not [bool]$corpus.oatListZeroWarningsZeroErrors -or
    -not [bool]$corpus.oatRedumpZeroWarningsZeroErrors) {
    throw '4Z-C requires the passing 4Z-A Material corpus.'
}

if ([string]$graph.format -ne 'WaW360.Nuclear4ZB.MaterialRelocatableGraph.v1' -or
    -not [bool]$graph.graphPass -or
    -not [bool]$graph.exactIdentitySet -or
    -not [bool]$graph.oatZeroWarningsZeroErrors) {
    throw '4Z-C requires the passing 4Z-B Material relocatable graph corpus.'
}

if ([int]$corpus.materials -ne [int]$graph.materials) {
    throw "4Z-C Material count mismatch: corpus=$([int]$corpus.materials), graphs=$([int]$graph.materials)."
}

if ([int]$graph.reusableAliasCount -ne 0) {
    throw "4Z-C simple replay requires zero reusable aliases; found $([int]$graph.reusableAliasCount)."
}

if ([int]$graph.waterTextures -ne 0) {
    throw "4Z-C current replay path requires zero water textures; found $([int]$graph.waterTextures)."
}

$referenceFf = [string]$corpus.linkedFastFile
if (-not (Test-Path -LiteralPath $referenceFf -PathType Leaf)) {
    throw "4Z-C 4Z-A reference FF is missing: $referenceFf"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

Write-Host '[NUCLEAR-4Z-C] =============================================================='
Write-Host '[NUCLEAR-4Z-C] STANDALONE EXACT XENON MATERIAL CORPUS REPLAY'
Write-Host '[NUCLEAR-4Z-C] =============================================================='
Write-Host "[NUCLEAR-4Z-C] Materials      : $([int]$graph.materials)"
Write-Host "[NUCLEAR-4Z-C] Graph root     : $GraphRoot"
Write-Host "[NUCLEAR-4Z-C] Reference FF   : $referenceFf"
Write-Host "[NUCLEAR-4Z-C] Output         : $OutputRoot"
Write-Host '[NUCLEAR-4Z-C] Replay model   : real Material graphs + named TechniqueSet/Image shells'
Write-Host '[NUCLEAR-4Z-C] Hard gate      : exact root identity + OAT 0/0 + exact 502-graph redump'
Write-Host ''

Push-Location $repo
try {
    & dotnet run --project '.\src\WaW360.Cli\WaW360.Cli.csproj' -c Release -- xenon-material-replay $GraphRoot $referenceFf $OutputRoot $unlinker
    $code = [int]$LASTEXITCODE
}
finally {
    Pop-Location
}

$manifest = Join-Path $OutputRoot 'nuclear_4zc_material_replay.json'
if ($code -ne 0) {
    Write-Host ''
    Write-Host "[NUCLEAR-4Z-C] MATERIAL REPLAY: BLOCKED (exit $code)" -ForegroundColor Yellow
    if (Test-Path -LiteralPath $manifest -PathType Leaf) {
        $result = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
        foreach ($blocker in @($result.Blockers)) {
            Write-Host "[NUCLEAR-4Z-C] BLOCKER: $blocker"
        }
    }
    exit $code
}

if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
    throw "4Z-C command returned success but replay manifest is missing: $manifest"
}

$result = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
if (-not [bool]$result.ReplayPass -or
    [int]$result.Materials -ne [int]$graph.materials -or
    -not [bool]$result.ExactIdentitySequence -or
    -not [bool]$result.OatZeroWarningsZeroErrors -or
    -not [bool]$result.GraphCorpusRedumpExact -or
    -not [bool]$result.WrapperRoundTripVerified) {
    throw '4Z-C replay command returned success without the complete exact replay proof.'
}

Write-Host ''
Write-Host '[NUCLEAR-4Z-C] ================= MATERIAL REPLAY RESULT ====================='
Write-Host "[NUCLEAR-4Z-C] Materials             : $([int]$result.Materials)"
Write-Host "[NUCLEAR-4Z-C] Roots                 : $([int]$result.Roots)"
Write-Host "[NUCLEAR-4Z-C] Exact identity        : $([bool]$result.ExactIdentitySequence)"
Write-Host "[NUCLEAR-4Z-C] OAT 0/0               : $([bool]$result.OatZeroWarningsZeroErrors)"
Write-Host "[NUCLEAR-4Z-C] Graph corpus exact    : $([bool]$result.GraphCorpusRedumpExact)"
Write-Host "[NUCLEAR-4Z-C] Wrapper round-trip    : $([bool]$result.WrapperRoundTripVerified)"
Write-Host "[NUCLEAR-4Z-C] Replay pass           : $([bool]$result.ReplayPass)"
Write-Host "[NUCLEAR-4Z-C] Manifest              : $manifest"
Write-Host '[NUCLEAR-4Z-C] ================================================================='

Write-Host ''
Write-Host '[NUCLEAR-4Z-C] 502-MATERIAL STANDALONE EXACT REPLAY: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4Z-C] Next: graft these proven Material graphs into the 6,811-root production emitter.'
exit 0
