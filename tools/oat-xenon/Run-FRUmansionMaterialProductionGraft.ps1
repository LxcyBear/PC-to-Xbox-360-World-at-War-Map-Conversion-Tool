[CmdletBinding()]
param(
    [string]$NativeControlDirectory = 'C:\Users\jorda\OneDrive\Desktop\working frumansion',
    [string]$RendererGraphRoot = '',
    [string]$BindingRoot = '',
    [string]$PcImageBridgeRoot = '',
    [string]$XModelGraphRoot = '',
    [string]$XModelBindingRoot = '',
    [string]$XModelReplaySuiteRoot = '',
    [string]$MaterialGraphRoot = '',
    [string]$MaterialReplayRoot = '',
    [string]$MaterialBindingRoot = '',
    [string]$OutputRoot = '',
    [string]$CanonicalBaseName = 'nazi_zombie_FRUmansion'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$graphUnlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($RendererGraphRoot)) {
    $RendererGraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_oat_graph_dump'
}
if ([string]::IsNullOrWhiteSpace($BindingRoot)) {
    $BindingRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_root_binding'
}
if ([string]::IsNullOrWhiteSpace($PcImageBridgeRoot)) {
    $PcImageBridgeRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_pc_image_bridge'
}
if ([string]::IsNullOrWhiteSpace($XModelGraphRoot)) {
    $XModelGraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4xb_xmodel_relocatable_graph'
}
if ([string]::IsNullOrWhiteSpace($XModelBindingRoot)) {
    $XModelBindingRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4w_xmodel_root_binding'
}
if ([string]::IsNullOrWhiteSpace($XModelReplaySuiteRoot)) {
    $XModelReplaySuiteRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4xc_xmodel_replay_suite'
}
if ([string]::IsNullOrWhiteSpace($MaterialGraphRoot)) {
    $MaterialGraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4zb_material_relocatable_graph'
}
if ([string]::IsNullOrWhiteSpace($MaterialReplayRoot)) {
    $MaterialReplayRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4zc_material_replay'
}
if ([string]::IsNullOrWhiteSpace($MaterialBindingRoot)) {
    $MaterialBindingRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4zd_material_root_binding'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4zd_material_production_graft'
}

function Find-MainFastFile {
    param([Parameter(Mandatory=$true)][string]$Directory)

    foreach ($name in @('frumansion_j_p.ff', 'nazi_zombie_FRUmansion.ff', 'nazi_zombie_frumansion.ff')) {
        $candidate = Join-Path $Directory $name
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }

    $candidates = @(
        Get-ChildItem -LiteralPath $Directory -Filter '*.ff' -File |
            Where-Object {
                $_.Name -notmatch '(?i)_load[.]ff$' -and
                $_.Name -notmatch '(?i)_patch[.]ff$'
            }
    )
    if ($candidates.Count -eq 1) { return $candidates[0].FullName }

    throw "Could not identify one native FRUmansion main fastfile in: $Directory"
}

$nativeMain = Find-MainFastFile -Directory $NativeControlDirectory

$rendererBinding = Join-Path $BindingRoot 'nuclear_xenon_root_binding.json'
$identityDir = Join-Path $BindingRoot 'native_identity'
$identityManifest = @(
    Get-ChildItem -LiteralPath $identityDir -Filter '*_native_identity_manifest.json' -File -ErrorAction SilentlyContinue |
        Sort-Object FullName
)
if ($identityManifest.Count -ne 1) {
    throw "Expected exactly one native identity manifest under '$identityDir'; found $($identityManifest.Count)."
}

$imageBridgeManifest = Join-Path $PcImageBridgeRoot 'nuclear_4r_pc_image_bridge.json'
$xmodelCorpusManifest = Join-Path $XModelGraphRoot 'nuclear_4xb_xmodel_relocatable_graph.json'
$xmodelBindingManifest = Join-Path $XModelBindingRoot 'nuclear_4w_xmodel_root_binding.json'
$xmodelSuiteManifest = Join-Path $XModelReplaySuiteRoot 'nuclear_4xc_replay_suite.json'
$materialGraphManifest = Join-Path $MaterialGraphRoot 'nuclear_4zb_material_relocatable_graph.json'
$materialReplayManifest = Join-Path $MaterialReplayRoot 'nuclear_4zc_material_replay.json'
$materialBindingManifest = Join-Path $MaterialBindingRoot 'nuclear_4zd_material_root_binding.json'
$materialBindingRunner = Join-Path $PSScriptRoot 'Run-FRUmansionMaterialRootBinding.ps1'
$materialPipelineRunner = Join-Path $PSScriptRoot 'Run-FRUmansionMaterialPipeline.ps1'

$needsIdentityRefresh = $true
if (Test-Path -LiteralPath $materialGraphManifest -PathType Leaf) {
    try {
        $existingMaterialGraph = Get-Content -LiteralPath $materialGraphManifest -Raw | ConvertFrom-Json
        if ($existingMaterialGraph.PSObject.Properties.Name -contains 'trueMaterialIdentityRecovered' -and
            [bool]$existingMaterialGraph.trueMaterialIdentityRecovered) {
            $needsIdentityRefresh = $false
        }
    }
    catch {
        $needsIdentityRefresh = $true
    }
}

if ($needsIdentityRefresh) {
    Write-Host '[NUCLEAR-4Z-D] Material graph predates true-identity recovery; rebuilding 4Z-A -> 4Z-B -> 4Z-C once...'
    & powershell -ExecutionPolicy Bypass -File $materialPipelineRunner
    if ($LASTEXITCODE -ne 0 -or -not $?) {
        throw "Corrected Material pipeline rebuild failed with exit code $LASTEXITCODE."
    }
}

Write-Host '[NUCLEAR-4Z-D] Refreshing calibrated 4Z-D0 Material root binding...'
& powershell -ExecutionPolicy Bypass -File $materialBindingRunner -GraphRoot $MaterialGraphRoot -BindingRoot $BindingRoot -OutputRoot $MaterialBindingRoot
if ($LASTEXITCODE -ne 0 -or -not $?) {
    throw "4Z-D0 Material root binding failed with exit code $LASTEXITCODE."
}

foreach ($required in @(
    $rendererBinding,
    $identityManifest[0].FullName,
    $imageBridgeManifest,
    $xmodelCorpusManifest,
    $xmodelBindingManifest,
    $xmodelSuiteManifest,
    $materialGraphManifest,
    $materialReplayManifest,
    $materialBindingManifest,
    $graphUnlinker
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Missing required Stage 4Z-D input: $required"
    }
}
if (-not (Test-Path -LiteralPath $RendererGraphRoot -PathType Container)) {
    throw "Missing renderer graph root: $RendererGraphRoot"
}

$imageBridge = Get-Content -LiteralPath $imageBridgeManifest -Raw | ConvertFrom-Json
$xmodelCorpus = Get-Content -LiteralPath $xmodelCorpusManifest -Raw | ConvertFrom-Json
$xmodelBinding = Get-Content -LiteralPath $xmodelBindingManifest -Raw | ConvertFrom-Json
$xmodelSuite = Get-Content -LiteralPath $xmodelSuiteManifest -Raw | ConvertFrom-Json
$materialGraph = Get-Content -LiteralPath $materialGraphManifest -Raw | ConvertFrom-Json
$materialReplay = Get-Content -LiteralPath $materialReplayManifest -Raw | ConvertFrom-Json
$materialBinding = Get-Content -LiteralPath $materialBindingManifest -Raw | ConvertFrom-Json

if ([string]$imageBridge.format -ne 'WaW360.Nuclear4R.PcOwnedImageBridge.v1' -or
    -not [bool]$imageBridge.bridgePass) {
    throw 'Stage 4Z-D requires a passing PC-owned Stage 4R image bridge.'
}

if ([string]$xmodelCorpus.format -ne 'WaW360.Nuclear4XB.XModelRelocatableGraph.v1' -or
    -not [bool]$xmodelCorpus.corpusGraphPass -or
    [int]$xmodelCorpus.resultRows -ne 280 -or
    [int]$xmodelCorpus.passedModels -ne 280 -or
    [int]$xmodelCorpus.failedModels -ne 0) {
    throw 'Stage 4Z-D requires the refreshed 280/280 passing 4X-B relocatable graph corpus.'
}

if ([string]$xmodelBinding.format -ne 'WaW360.Nuclear4W.XModelRootBinding.v3' -or
    -not [bool]$xmodelBinding.bindingPass -or
    [int]$xmodelBinding.boundOwnedModels -ne 280 -or
    [int]$xmodelBinding.boundReferenceRoots -ne 9) {
    throw 'Stage 4Z-D requires the passing 4W binding: 280 owned + 9 reference XModel roots.'
}

if ([string]$xmodelSuite.format -ne 'WaW360.Nuclear4XC.XModelReplaySuite.v1' -or
    -not [bool]$xmodelSuite.pass -or
    @($xmodelSuite.cases).Count -ne 4) {
    throw 'Stage 4Z-D requires the passing 4X-C representative replay suite.'
}

if ([string]$materialGraph.format -ne 'WaW360.Nuclear4ZB.MaterialRelocatableGraph.v1' -or
    -not [bool]$materialGraph.graphPass -or
    -not [bool]$materialGraph.trueMaterialIdentityRecovered -or
    [int]$materialGraph.materials -ne 502 -or
    [int]$materialGraph.reusableAliasCount -ne 0 -or
    [int]$materialGraph.waterTextures -ne 0) {
    throw 'Stage 4Z-D requires the passing 502/502 4Z-B Material graph corpus.'
}

if (-not [bool]$materialReplay.ReplayPass -or
    [int]$materialReplay.Materials -ne 502 -or
    -not [bool]$materialReplay.ExactIdentitySequence -or
    -not [bool]$materialReplay.OatZeroWarningsZeroErrors -or
    -not [bool]$materialReplay.GraphCorpusRedumpExact -or
    -not [bool]$materialReplay.WrapperRoundTripVerified) {
    throw 'Stage 4Z-D requires the passing 502-Material 4Z-C exact replay proof.'
}

if (-not [bool]$materialBinding.BindingPass -or
    [int]$materialBinding.CorpusMaterials -ne 502 -or
    @($materialBinding.OwnedBindings).Count -ne 502 -or
    [int]$materialBinding.UnmatchedMaterials -ne 0 -or
    [int]$materialBinding.AmbiguousMaterials -ne 0) {
    throw 'Stage 4Z-D requires the passing 502/502 calibrated Material root binding.'
}

$imageGraphRoot = [string]$imageBridge.graphRoot
$expectedImages = [int]$imageBridge.resolvedSources
if ($expectedImages -le 0 -or -not (Test-Path -LiteralPath $imageGraphRoot -PathType Container)) {
    throw "Stage 4Z-D image graph is unavailable or empty: $imageGraphRoot"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

Write-Host '[NUCLEAR-4Z-D] ================================================================'
Write-Host '[NUCLEAR-4Z-D] 6,811-ROOT PC-BACKED MATERIAL PRODUCTION GRAFT'
Write-Host '[NUCLEAR-4Z-D] ================================================================'
Write-Host "[NUCLEAR-4Z-D] Native oracle       : $nativeMain"
Write-Host "[NUCLEAR-4Z-D] Native identity     : $($identityManifest[0].FullName)"
Write-Host "[NUCLEAR-4Z-D] Renderer graph      : $RendererGraphRoot"
Write-Host "[NUCLEAR-4Z-D] PC image graph      : $imageGraphRoot"
Write-Host "[NUCLEAR-4Z-D] PC-backed images    : $expectedImages"
Write-Host "[NUCLEAR-4Z-D] XModel graph corpus : $XModelGraphRoot"
Write-Host '[NUCLEAR-4Z-D] Owned XModels       : 280'
Write-Host '[NUCLEAR-4Z-D] XModel references   : 9 (reference shells preserved)'
Write-Host "[NUCLEAR-4Z-D] Material graph corpus: $MaterialGraphRoot"
Write-Host '[NUCLEAR-4Z-D] PC-backed Materials : 502'
Write-Host '[NUCLEAR-4Z-D] Material replay     : exact 502/502 PASS'
Write-Host "[NUCLEAR-4Z-D] Material binding    : 502/502 PASS (direct=$([int]$materialBinding.DirectIdentityMatches), semantic=$([int]$materialBinding.SemanticMatches))"
Write-Host ''
Write-Host '[NUCLEAR-4Z-D] Safety: full root sequence + layout replay + OAT 0/0 are mandatory.'
Write-Host '[NUCLEAR-4Z-D] Hardware remains fenced until Stage 4F critical-content readiness passes.'
Write-Host ''

Push-Location $repo
try {
    $args = @(
        'run',
        '--project', '.\src\WaW360.Cli\WaW360.Cli.csproj',
        '-c', 'Release',
        '--',
        'xenon-graph-emit',
        $RendererGraphRoot,
        $nativeMain,
        $identityManifest[0].FullName,
        $rendererBinding,
        $OutputRoot,
        $CanonicalBaseName,
        $graphUnlinker,
        '87',
        '897',
        $imageGraphRoot,
        [string]$expectedImages,
        'true',
        '0',
        $XModelGraphRoot,
        $xmodelBindingManifest,
        $MaterialGraphRoot,
        $materialBindingManifest
    )

    & dotnet @args
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

$emitterManifestPath = Join-Path $OutputRoot 'nuclear_xenon_renderer_emit.json'
if (-not (Test-Path -LiteralPath $emitterManifestPath -PathType Leaf)) {
    throw "Stage 4Z-D emitter manifest was not written: $emitterManifestPath"
}

$emitter = Get-Content -LiteralPath $emitterManifestPath -Raw | ConvertFrom-Json

$pass =
    $code -eq 0 -and
    [bool]$emitter.ValidationPass -and
    [bool]$emitter.LayoutReplayExact -and
    [bool]$emitter.ExactIdentitySequence -and
    [bool]$emitter.OatZeroWarningsZeroErrors -and
    [int]$emitter.OatListedRoots -eq 6811 -and
    [int]$emitter.RealXModelRoots -eq 280 -and
    [int]$emitter.XModelReferenceRoots -eq 9 -and
    [int]$emitter.RealMaterialRoots -eq 502 -and
    [int]$emitter.RealImageRoots -eq $expectedImages

$summary = [ordered]@{
    format = 'WaW360.Nuclear4ZD.MaterialProductionGraft.v1'
    nativeFastFile = $nativeMain
    nativeIdentityManifest = $identityManifest[0].FullName
    rendererBindingManifest = $rendererBinding
    pcImageBridgeManifest = $imageBridgeManifest
    xmodelCorpusManifest = $xmodelCorpusManifest
    xmodelBindingManifest = $xmodelBindingManifest
    xmodelReplaySuiteManifest = $xmodelSuiteManifest
    materialGraphManifest = $materialGraphManifest
    materialReplayManifest = $materialReplayManifest
    materialBindingManifest = $materialBindingManifest
    nativeRoots = [int]$emitter.NativeRoots
    oatListedRoots = [int]$emitter.OatListedRoots
    pcBackedImages = [int]$emitter.RealImageRoots
    pcBackedMaterials = [int]$emitter.RealMaterialRoots
    materialReferenceRoots = [int]$emitter.MaterialReferenceRoots
    pcBackedOwnedXModels = [int]$emitter.RealXModelRoots
    preservedXModelReferenceRoots = [int]$emitter.XModelReferenceRoots
    exactIdentitySequence = [bool]$emitter.ExactIdentitySequence
    oatZeroWarningsZeroErrors = [bool]$emitter.OatZeroWarningsZeroErrors
    layoutReplayExact = [bool]$emitter.LayoutReplayExact
    productionMaterialGraftPass = $pass
    hardwareApproved = $false
    hardwareFence = 'Stage 4F full critical-content readiness remains mandatory.'
    outputFastFile = [string]$emitter.OutputFastFile
    emitterManifest = $emitterManifestPath
}

$summaryPath = Join-Path $OutputRoot 'nuclear_4zd_material_production_graft.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host ''
Write-Host '[NUCLEAR-4Z-D] ================= MATERIAL GRAFT RESULT ========================'
Write-Host "[NUCLEAR-4Z-D] Native roots           : $([int]$emitter.NativeRoots)"
Write-Host "[NUCLEAR-4Z-D] OAT roots              : $([int]$emitter.OatListedRoots) / 6811"
Write-Host "[NUCLEAR-4Z-D] Real Material roots    : $([int]$emitter.RealMaterialRoots) / 502"
Write-Host "[NUCLEAR-4Z-D] Material references    : $([int]$emitter.MaterialReferenceRoots)"
Write-Host "[NUCLEAR-4Z-D] Real XModel roots      : $([int]$emitter.RealXModelRoots) / 280"
Write-Host "[NUCLEAR-4Z-D] XModel references      : $([int]$emitter.XModelReferenceRoots) / 9"
Write-Host "[NUCLEAR-4Z-D] PC-backed image roots  : $([int]$emitter.RealImageRoots)"
Write-Host "[NUCLEAR-4Z-D] Layout replay exact    : $([bool]$emitter.LayoutReplayExact)"
Write-Host "[NUCLEAR-4Z-D] Exact identity sequence: $([bool]$emitter.ExactIdentitySequence)"
Write-Host "[NUCLEAR-4Z-D] OAT 0 warn / 0 error   : $([bool]$emitter.OatZeroWarningsZeroErrors)"
Write-Host "[NUCLEAR-4Z-D] Production graft pass  : $pass"
Write-Host '[NUCLEAR-4Z-D] Hardware approved      : False'
Write-Host "[NUCLEAR-4Z-D] Summary                : $summaryPath"
Write-Host '[NUCLEAR-4Z-D] ================================================================='

if (-not $pass) {
    Write-Host ''
    Write-Host '[NUCLEAR-4Z-D] MATERIAL PRODUCTION GRAFT: BLOCKED' -ForegroundColor Yellow
    Write-Host '[NUCLEAR-4Z-D] Do NOT copy this FF to Xbox.'

    if ($null -ne $emitter.PSObject.Properties['OatGateError'] -and
        -not [string]::IsNullOrWhiteSpace([string]$emitter.OatGateError)) {
        Write-Host "[NUCLEAR-4Z-D] OAT gate: $([string]$emitter.OatGateError)"
    }

    $gateDir = Join-Path $OutputRoot 'oat_hard_gate'
    if (Test-Path -LiteralPath $gateDir -PathType Container) {
        $gateLog = @(
            Get-ChildItem -LiteralPath $gateDir -Filter '*.log' -File -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTime -Descending |
                Select-Object -First 1
        )
        if ($gateLog.Count -eq 1) {
            Write-Host ''
            Write-Host '[NUCLEAR-4Z-D] OAT hard-gate tail:'
            Get-Content -LiteralPath $gateLog[0].FullName -Tail 100
        }
    }

    exit 4
}

Write-Host ''
Write-Host '[NUCLEAR-4Z-D] 502-MATERIAL / 6,811-ROOT PRODUCTION GRAFT: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4Z-D] All 502 PC-backed Xenon Materials survived alongside renderer, images, and 280 XModels.'
Write-Host '[NUCLEAR-4Z-D] Next: rerun 4F with Material production credit; hardware remains fenced until critical fallbacks close.'
exit 0
