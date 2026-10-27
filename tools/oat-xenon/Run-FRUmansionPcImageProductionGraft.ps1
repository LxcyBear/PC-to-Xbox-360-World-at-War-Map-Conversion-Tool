param(
    [string]$NativeControlDirectory = 'C:\Users\jorda\OneDrive\Desktop\working frumansion',
    [string]$RendererGraphRoot = '',
    [string]$BindingRoot = '',
    [string]$PcImageBridgeRoot = '',
    [string]$OutputRoot = '',
    [string]$CanonicalBaseName = 'nazi_zombie_FRUmansion'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

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
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4s_pc_image_graft'
}

function Find-MainFastFile {
    param([Parameter(Mandatory=$true)][string]$Directory)

    foreach ($name in @('frumansion_j_p.ff', 'nazi_zombie_FRUmansion.ff', 'nazi_zombie_frumansion.ff')) {
        $candidate = Join-Path $Directory $name
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }

    $candidates = @(Get-ChildItem -LiteralPath $Directory -Filter '*.ff' -File |
        Where-Object { $_.Name -notmatch '(?i)_load[.]ff$' -and $_.Name -notmatch '(?i)_patch[.]ff$' })

    if ($candidates.Count -eq 1) { return $candidates[0].FullName }
    throw "Could not identify one native FRUmansion main fastfile in: $Directory"
}

$nativeMain = Find-MainFastFile -Directory $NativeControlDirectory
$bindingManifest = Join-Path $BindingRoot 'nuclear_xenon_root_binding.json'
$identityDir = Join-Path $BindingRoot 'native_identity'
$identityManifest = @(Get-ChildItem -LiteralPath $identityDir -Filter '*_native_identity_manifest.json' -File |
    Sort-Object FullName |
    Select-Object -First 1)
$bridgeManifestPath = Join-Path $PcImageBridgeRoot 'nuclear_4r_pc_image_bridge.json'

if (-not (Test-Path -LiteralPath $RendererGraphRoot -PathType Container)) { throw "Missing renderer graph root: $RendererGraphRoot" }
if (-not (Test-Path -LiteralPath $bindingManifest -PathType Leaf)) { throw "Missing root binding manifest: $bindingManifest" }
if ($identityManifest.Count -ne 1) { throw "Could not identify native identity manifest under: $identityDir" }
if (-not (Test-Path -LiteralPath $bridgeManifestPath -PathType Leaf)) { throw "Missing Stage 4R manifest: $bridgeManifestPath" }
if (-not (Test-Path -LiteralPath $graphUnlinker -PathType Leaf)) { throw "Missing specialized Xenon OAT Unlinker: $graphUnlinker" }

$bridge = Get-Content -LiteralPath $bridgeManifestPath -Raw | ConvertFrom-Json
if ([string]$bridge.format -ne 'WaW360.Nuclear4R.PcOwnedImageBridge.v1') {
    throw "Unexpected Stage 4R manifest format: $($bridge.format)"
}
if (-not [bool]$bridge.bridgePass) {
    throw 'Stage 4R did not prove a PC-owned image graph; Stage 4S remains blocked.'
}

$imageGraphRoot = [string]$bridge.graphRoot
$expectedImages = [int]$bridge.resolvedSources
$missingImages = [int]$bridge.missingSources
$nonPortable = [int]$bridge.nonPortableNativeNames
$productionCardMemory = [int64]$bridge.xenonCardMemoryBytes

if ($expectedImages -le 0) { throw 'Stage 4R resolved zero production image sources.' }
if (-not (Test-Path -LiteralPath $imageGraphRoot -PathType Container)) { throw "Stage 4R graph root not found: $imageGraphRoot" }

if (Test-Path -LiteralPath $OutputRoot) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }

Write-Host '[NUCLEAR-4S] ================================================================'
Write-Host '[NUCLEAR-4S] 6,811-ROOT PC-SOURCED IMAGE PRODUCTION GRAFT'
Write-Host '[NUCLEAR-4S] ================================================================'
Write-Host "[NUCLEAR-4S] PC-backed images : $expectedImages"
Write-Host "[NUCLEAR-4S] PC Xenon pixels  : $productionCardMemory bytes"
Write-Host "[NUCLEAR-4S] Missing ordinary : $missingImages"
Write-Host "[NUCLEAR-4S] Generated/native : $nonPortable"
Write-Host "[NUCLEAR-4S] Image graph root : $imageGraphRoot"
Write-Host "[NUCLEAR-4S] Native sequence  : $($identityManifest[0].FullName)"
Write-Host ''
Write-Host '[NUCLEAR-4S] Provenance: image payloads are sourced from PC IWI/DDS/IWD only.'
Write-Host '[NUCLEAR-4S] Safety: this remains a validation candidate until the separate 4F critical-content gate closes.'
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
        $bindingManifest,
        $OutputRoot,
        $CanonicalBaseName,
        $graphUnlinker,
        '87',
        '897',
        $imageGraphRoot,
        [string]$expectedImages,
        'true',
        '0'
    )

    & dotnet @args
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

$emitterManifestPath = Join-Path $OutputRoot 'nuclear_xenon_renderer_emit.json'
if (-not (Test-Path -LiteralPath $emitterManifestPath -PathType Leaf)) {
    throw "Stage 4S emitter manifest was not written: $emitterManifestPath"
}

$emitter = Get-Content -LiteralPath $emitterManifestPath -Raw | ConvertFrom-Json

$productionPass =
    $code -eq 0 -and
    [bool]$emitter.ValidationPass -and
    [bool]$emitter.LayoutReplayExact -and
    [bool]$emitter.ExactIdentitySequence -and
    [bool]$emitter.OatZeroWarningsZeroErrors -and
    [int]$emitter.RealImageRoots -eq $expectedImages -and
    [int64]$emitter.LargeRuntimeBytes -eq $productionCardMemory

$summary = [ordered]@{
    format = 'WaW360.Nuclear4S.PcImageProductionGraft.v1'
    pcImageBridgeManifest = $bridgeManifestPath
    imageGraphRoot = $imageGraphRoot
    pcBackedImages = $expectedImages
    missingOrdinaryImages = $missingImages
    generatedNativeImages = $nonPortable
    expectedLargeRuntimeBytes = $productionCardMemory
    actualLargeRuntimeBytes = [int64]$emitter.LargeRuntimeBytes
    nativeRoots = [int]$emitter.NativeRoots
    oatListedRoots = [int]$emitter.OatListedRoots
    exactIdentitySequence = [bool]$emitter.ExactIdentitySequence
    oatZeroWarningsZeroErrors = [bool]$emitter.OatZeroWarningsZeroErrors
    layoutReplayExact = [bool]$emitter.LayoutReplayExact
    productionImageGraftPass = $productionPass
    hardwareApproved = $false
    hardwareFence = '4F full critical-content readiness is still required; Stage 4S alone never authorizes Xbox testing.'
    outputFastFile = [string]$emitter.OutputFastFile
}

$summaryPath = Join-Path $OutputRoot 'nuclear_4s_pc_image_graft.json'
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host ''
Write-Host '[NUCLEAR-4S] ================= PC IMAGE GRAFT RESULT ========================='
Write-Host "[NUCLEAR-4S] PC-backed image roots : $expectedImages"
Write-Host "[NUCLEAR-4S] LARGE_RUNTIME expected : $productionCardMemory"
Write-Host "[NUCLEAR-4S] LARGE_RUNTIME actual   : $([int64]$emitter.LargeRuntimeBytes)"
Write-Host "[NUCLEAR-4S] OAT roots              : $([int]$emitter.OatListedRoots) / 6811"
Write-Host "[NUCLEAR-4S] Exact identity sequence: $([bool]$emitter.ExactIdentitySequence)"
Write-Host "[NUCLEAR-4S] OAT 0 warn / 0 error   : $([bool]$emitter.OatZeroWarningsZeroErrors)"
Write-Host "[NUCLEAR-4S] Production graft pass  : $productionPass"
Write-Host "[NUCLEAR-4S] Hardware approved      : False"
Write-Host "[NUCLEAR-4S] Summary                : $summaryPath"
Write-Host '[NUCLEAR-4S] ================================================================='

if (-not $productionPass) {
    Write-Host '[NUCLEAR-4S] PC IMAGE PRODUCTION GRAFT: FAILED' -ForegroundColor Yellow
    Write-Host '[NUCLEAR-4S] Do NOT copy this FF to Xbox.'
    exit 4
}

Write-Host '[NUCLEAR-4S] PC IMAGE PRODUCTION GRAFT: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4S] Legitimate PC-sourced images now survive the full 6,811-root Xenon graph.'
Write-Host '[NUCLEAR-4S] Next: feed this production image count/mass into 4F and continue closing non-image critical families.'
Write-Host '[NUCLEAR-4S] Do NOT Xbox-test yet; 4F remains the hardware gate.'
