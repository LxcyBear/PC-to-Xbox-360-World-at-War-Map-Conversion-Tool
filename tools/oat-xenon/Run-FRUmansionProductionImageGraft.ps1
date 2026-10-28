param(
    [string]$NativeControlDirectory = 'C:\Users\jorda\OneDrive\Desktop\working frumansion',
    [string]$RendererGraphRoot = '',
    [string]$BindingRoot = '',
    [string]$ProductionImageManifest = '',
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
if ([string]::IsNullOrWhiteSpace($ProductionImageManifest)) {
    $ProductionImageManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4t_production_image_graph\nuclear_4t_production_image_graph.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4u_production_image_graft'
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

if (-not (Test-Path -LiteralPath $RendererGraphRoot -PathType Container)) { throw "Missing renderer graph root: $RendererGraphRoot" }
if (-not (Test-Path -LiteralPath $bindingManifest -PathType Leaf)) { throw "Missing root binding manifest: $bindingManifest" }
if ($identityManifest.Count -ne 1) { throw "Could not identify native identity manifest under: $identityDir" }
if (-not (Test-Path -LiteralPath $ProductionImageManifest -PathType Leaf)) { throw "Missing production image manifest: $ProductionImageManifest" }
if (-not (Test-Path -LiteralPath $graphUnlinker -PathType Leaf)) { throw "Missing specialized Xenon OAT Unlinker: $graphUnlinker" }

$images = Get-Content -LiteralPath $ProductionImageManifest -Raw | ConvertFrom-Json
if ([string]$images.format -ne 'WaW360.Nuclear4T.ProductionImageGraph.v1') {
    throw "Unexpected production image manifest format: $($images.format)"
}
if (-not [bool]$images.mergePass) {
    throw 'Production image graph merge did not pass.'
}

$imageGraphRoot = [string]$images.graphRoot
$expectedImages = [int]$images.imageGraphs
$expectedBytes = [int64]$images.xenonCardMemoryBytes

if ($expectedImages -le 0) { throw 'Production image graph contains zero images.' }
if (-not (Test-Path -LiteralPath $imageGraphRoot -PathType Container)) {
    throw "Production image graph root not found: $imageGraphRoot"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

Write-Host '[NUCLEAR-4U] ================================================================'
Write-Host '[NUCLEAR-4U] 6,811-ROOT PRODUCTION IMAGE GRAFT'
Write-Host '[NUCLEAR-4U] ================================================================'
Write-Host "[NUCLEAR-4U] Production images : $expectedImages"
Write-Host "[NUCLEAR-4U] Xenon image bytes : $expectedBytes"
Write-Host "[NUCLEAR-4U] Image graph root  : $imageGraphRoot"
Write-Host "[NUCLEAR-4U] Native sequence   : $($identityManifest[0].FullName)"
Write-Host '[NUCLEAR-4U] Hardware remains fenced until Stage 4F critical-content readiness passes.'
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
    $code = [int]$LASTEXITCODE
}
finally {
    Pop-Location
}

$emitterManifestPath = Join-Path $OutputRoot 'nuclear_xenon_renderer_emit.json'
if (-not (Test-Path -LiteralPath $emitterManifestPath -PathType Leaf)) {
    throw "Production emitter manifest was not written: $emitterManifestPath"
}

$emitter = Get-Content -LiteralPath $emitterManifestPath -Raw | ConvertFrom-Json
$actualBytes = [int64]$emitter.LargeRuntimeBytes + [int64]$emitter.LargeBytes

$productionPass =
    $code -eq 0 -and
    [bool]$emitter.ValidationPass -and
    [bool]$emitter.LayoutReplayExact -and
    [bool]$emitter.ExactIdentitySequence -and
    [bool]$emitter.OatZeroWarningsZeroErrors -and
    [int]$emitter.RealImageRoots -eq $expectedImages -and
    $actualBytes -eq $expectedBytes

$summary = [ordered]@{
    format = 'WaW360.Nuclear4U.ProductionImageGraft.v1'
    productionImageManifest = (Resolve-Path -LiteralPath $ProductionImageManifest).Path
    imageGraphRoot = $imageGraphRoot
    expectedImages = $expectedImages
    actualRealImageRoots = [int]$emitter.RealImageRoots
    expectedImageBytes = $expectedBytes
    actualLargeRuntimeBytes = [int64]$emitter.LargeRuntimeBytes
    actualLargeBytes = [int64]$emitter.LargeBytes
    actualImageBytes = $actualBytes
    nativeRoots = [int]$emitter.NativeRoots
    oatListedRoots = [int]$emitter.OatListedRoots
    layoutReplayExact = [bool]$emitter.LayoutReplayExact
    exactIdentitySequence = [bool]$emitter.ExactIdentitySequence
    oatZeroWarningsZeroErrors = [bool]$emitter.OatZeroWarningsZeroErrors
    validationPass = [bool]$emitter.ValidationPass
    productionImageGraftPass = $productionPass
    hardwareApproved = $false
    hardwareFence = 'Stage 4F full critical-content readiness remains required before Xbox testing.'
    outputFastFile = [string]$emitter.OutputFastFile
}

$summaryPath = Join-Path $OutputRoot 'nuclear_4u_production_image_graft.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host ''
Write-Host '[NUCLEAR-4U] ====================== RESULT ==================================='
Write-Host "[NUCLEAR-4U] Expected image roots : $expectedImages"
Write-Host "[NUCLEAR-4U] Actual image roots   : $([int]$emitter.RealImageRoots)"
Write-Host "[NUCLEAR-4U] Expected image bytes : $expectedBytes"
Write-Host "[NUCLEAR-4U] Actual image bytes   : $actualBytes"
Write-Host "[NUCLEAR-4U] OAT roots            : $([int]$emitter.OatListedRoots) / 6811"
Write-Host "[NUCLEAR-4U] Layout replay exact  : $([bool]$emitter.LayoutReplayExact)"
Write-Host "[NUCLEAR-4U] Identity sequence    : $([bool]$emitter.ExactIdentitySequence)"
Write-Host "[NUCLEAR-4U] OAT 0 warn / 0 error : $([bool]$emitter.OatZeroWarningsZeroErrors)"
Write-Host "[NUCLEAR-4U] Production graft pass: $productionPass"
Write-Host "[NUCLEAR-4U] Hardware approved    : False"
Write-Host "[NUCLEAR-4U] Summary              : $summaryPath"
Write-Host '[NUCLEAR-4U] ================================================================='

if (-not $productionPass) {
    Write-Host '[NUCLEAR-4U] PRODUCTION IMAGE GRAFT: FAILED' -ForegroundColor Yellow

    $gateDir = Join-Path $OutputRoot 'oat_hard_gate'
    if (Test-Path -LiteralPath $gateDir -PathType Container) {
        $gateLogs = @(Get-ChildItem -LiteralPath $gateDir -Filter '*.log' -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1)
        if ($gateLogs.Count -eq 1) {
            Write-Host ''
            Write-Host '[NUCLEAR-4U] OAT hard-gate tail:'
            Get-Content -LiteralPath $gateLogs[0].FullName -Tail 100
        }
    }

    exit 4
}

Write-Host '[NUCLEAR-4U] PRODUCTION IMAGE GRAFT: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4U] Legitimate multi-source image payloads now survive the full 6,811-root Xenon graph.'
Write-Host '[NUCLEAR-4U] Next: credit this image count/mass in Stage 4F and continue closing the remaining 43 source identities plus non-image critical families.'
