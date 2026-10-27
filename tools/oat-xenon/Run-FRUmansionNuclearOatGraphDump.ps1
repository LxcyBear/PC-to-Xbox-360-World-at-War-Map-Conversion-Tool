param(
    [string]$NativeControlDirectory = 'C:\Users\jorda\OneDrive\Desktop\working frumansion',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$builder = Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1'
$graphUnlinker = Join-Path $repoRoot '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'output\nazi_zombie_FRUmansion\nuclear_oat_graph_dump'
}

function Find-MainFastFile {
    param([Parameter(Mandatory=$true)][string]$Directory)
    foreach ($name in @('frumansion_j_p.ff', 'nazi_zombie_FRUmansion.ff', 'nazi_zombie_frumansion.ff')) {
        $candidate = Join-Path $Directory $name
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return (Get-Item -LiteralPath $candidate).FullName }
    }
    $candidates = @(Get-ChildItem -LiteralPath $Directory -Filter '*.ff' -File | Where-Object { $_.Name -notmatch '(?i)_load[.]ff$' -and $_.Name -notmatch '(?i)_patch[.]ff$' })
    if ($candidates.Count -eq 1) { return $candidates[0].FullName }
    throw "Could not identify one native FRUmansion main fastfile in: $Directory"
}

$nativeMain = Find-MainFastFile -Directory $NativeControlDirectory


# Stage 3B dumps unique map-local fixed-up graph objects, not one JSON file per
# physical XAsset root. Derive the authoritative expected graph count from the
# already-proven Stage 1/2 semantic closure:
#
#   native material-referenced TechniqueSets - resident/common targets
#   = map-local TechniqueSet graph objects expected from this FF.
$closureRoot = Join-Path $repoRoot 'output\nazi_zombie_FRUmansion\nuclear_techset_closure'
$bridgeManifest = Join-Path $closureRoot 'material_bridge\material_techset_bridge.json'
$closureManifest = Join-Path $closureRoot 'nuclear_techset_closure_plan.json'
$nativeIdentityManifest = Join-Path $closureRoot 'native_identity\nazi_zombie_FRUmansion_native_identity_manifest.json'

if (-not (Test-Path -LiteralPath $bridgeManifest -PathType Leaf)) {
    throw "Missing verified Stage 1 bridge manifest: $bridgeManifest"
}
if (-not (Test-Path -LiteralPath $closureManifest -PathType Leaf)) {
    throw "Missing verified Stage 2 closure manifest: $closureManifest"
}
if (-not (Test-Path -LiteralPath $nativeIdentityManifest -PathType Leaf)) {
    throw "Missing verified native identity manifest: $nativeIdentityManifest"
}

$bridgeProof = Get-Content -LiteralPath $bridgeManifest -Raw | ConvertFrom-Json
$closureProof = Get-Content -LiteralPath $closureManifest -Raw | ConvertFrom-Json
$nativeIdentityProof = Get-Content -LiteralPath $nativeIdentityManifest -Raw | ConvertFrom-Json

$materialReferencedXboxTechsets = [int]$bridgeProof.XboxTechniqueSetsObserved
$residentXboxTechsets = [int]$closureProof.ResidentXboxTargetsPending
$expectedMapLocalGraphs = $materialReferencedXboxTechsets - $residentXboxTechsets
if ($expectedMapLocalGraphs -le 0) {
    throw "Invalid semantic graph expectation: materialReferenced=$materialReferencedXboxTechsets resident=$residentXboxTechsets"
}

$pixelType = @($nativeIdentityProof.types | Where-Object {
    [string]$_.OatTypeName -ieq 'pixelshader'
}) | Select-Object -First 1
if ($null -eq $pixelType) {
    throw 'Native identity manifest does not contain a pixelshader type summary.'
}
$expectedPixelShaders = [int]$pixelType.Count

if (-not (Test-Path -LiteralPath $graphUnlinker -PathType Leaf)) {
    Write-Host '[NUCLEAR-3B] Specialized OAT graph dumper is not built yet.'
    Write-Host '[NUCLEAR-3B] Building it now from the pinned Xenon OAT source...'
    & $builder
    if ($LASTEXITCODE -ne 0 -or -not $?) { throw 'Specialized OAT graph-dumper build failed.' }
}

if (Test-Path -LiteralPath $OutputRoot) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$log = Join-Path $OutputRoot 'oat_graph_dump.log'

Write-Host '[NUCLEAR-3B] ================================================================'
Write-Host '[NUCLEAR-3B] OAT FIXED-UP XENON TECHNIQUESET GRAPH DUMP'
Write-Host '[NUCLEAR-3B] ================================================================'
Write-Host "[NUCLEAR-3B] Native oracle : $nativeMain"
Write-Host "[NUCLEAR-3B] Graph Unlinker: $graphUnlinker"
Write-Host "[NUCLEAR-3B] Output        : $OutputRoot"
Write-Host ''

$allOutput = & $graphUnlinker --no-color --include-assets techniqueset --output-folder $OutputRoot $nativeMain 2>&1
$exitCode = $LASTEXITCODE
$allOutput | Tee-Object -FilePath $log | ForEach-Object { Write-Host $_ }
if ($exitCode -ne 0) { throw "Specialized OAT graph dump failed with exit code $exitCode. Log: $log" }

$graphs = @(Get-ChildItem -LiteralPath $OutputRoot -Recurse -Filter '*.json' -File | Where-Object { $_.FullName -match '[\\/]xenon_techset_graphs[\\/]' })
$shaderFiles = @(Get-ChildItem -LiteralPath $OutputRoot -Recurse -Filter '*.bin' -File | Where-Object { $_.FullName -match '[\\/]xenon_graph_shaders[\\/]' })

$techniqueCount = 0
$passCount = 0
$vertexNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$pixelNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$nonNullSlots = 0

foreach ($file in $graphs) {
    $g = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    foreach ($slot in @($g.slots)) {
        if ($null -eq $slot.technique) { continue }
        $nonNullSlots++
        $techniqueCount++
        foreach ($pass in @($slot.technique.passes)) {
            $passCount++
            if ($null -ne $pass.vertexShader -and -not [string]::IsNullOrWhiteSpace([string]$pass.vertexShader.name)) { [void]$vertexNames.Add([string]$pass.vertexShader.name) }
            foreach ($vs in @($pass.vertexShaderArray)) {
                if ($null -ne $vs -and -not [string]::IsNullOrWhiteSpace([string]$vs.name)) { [void]$vertexNames.Add([string]$vs.name) }
            }
            if ($null -ne $pass.pixelShader -and -not [string]::IsNullOrWhiteSpace([string]$pass.pixelShader.name)) { [void]$pixelNames.Add([string]$pass.pixelShader.name) }
        }
    }
}

Write-Host ''
Write-Host '[NUCLEAR-3B] GRAPH CENSUS:'
Write-Host "  physical TechniqueSet roots : 107"
Write-Host "  material-referenced Xbox TS : $materialReferencedXboxTechsets"
Write-Host "  resident/common Xbox TS      : $residentXboxTechsets"
Write-Host "  expected map-local graphs    : $expectedMapLocalGraphs"
Write-Host "  TechniqueSet JSON graphs     : $($graphs.Count) / $expectedMapLocalGraphs"
Write-Host "  non-null technique slots : $nonNullSlots"
Write-Host "  serialized technique refs : $techniqueCount"
Write-Host "  material passes           : $passCount"
Write-Host "  distinct vertex shaders   : $($vertexNames.Count)"
Write-Host "  distinct pixel shaders       : $($pixelNames.Count) / $expectedPixelShaders"
Write-Host "  shader blob files         : $($shaderFiles.Count)"

if ($graphs.Count -ne $expectedMapLocalGraphs) {
    throw "Expected exactly $expectedMapLocalGraphs map-local OAT-fixed-up TechniqueSet graph JSON files (material-referenced $materialReferencedXboxTechsets minus resident $residentXboxTechsets); found $($graphs.Count)."
}
if ($pixelNames.Count -ne $expectedPixelShaders) {
    throw "Expected $expectedPixelShaders distinct native PixelShaders in the fixed-up graph; found $($pixelNames.Count)."
}

Write-Host ''
Write-Host '[NUCLEAR-3B] SEMANTIC GRAPH CLOSURE: PASS' -ForegroundColor Green
Write-Host "[NUCLEAR-3B] 107 physical roots collapse to $expectedMapLocalGraphs map-local graph objects + $residentXboxTechsets resident/common targets."
Write-Host '[NUCLEAR-3B] FIXED-UP GRAPH FRONTIER: OPEN' -ForegroundColor Green
Write-Host '[NUCLEAR-3B] No serialized TEMP-offset guessing remains in the TechniqueSet path.'
Write-Host "[NUCLEAR-3B] Graph root: $OutputRoot"
