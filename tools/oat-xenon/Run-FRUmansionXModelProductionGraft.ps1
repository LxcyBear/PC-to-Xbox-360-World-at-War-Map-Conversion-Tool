[CmdletBinding()]
param(
    [string]$NativeControlDirectory = 'C:\Users\jorda\OneDrive\Desktop\working frumansion',
    [string]$RendererGraphRoot = '',
    [string]$BindingRoot = '',
    [string]$ProductionImageGraphRoot = '',
    [string]$XModelGraphRoot = '',
    [string]$XModelBindingRoot = '',
    [string]$XModelReplaySuiteRoot = '',
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
if ([string]::IsNullOrWhiteSpace($ProductionImageGraphRoot)) {
    $ProductionImageGraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4t_production_image_graph'
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
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4y_xmodel_production_graft'
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

$productionImageManifest = Join-Path $ProductionImageGraphRoot 'nuclear_4t_production_image_graph.json'
$xmodelCorpusManifest = Join-Path $XModelGraphRoot 'nuclear_4xb_xmodel_relocatable_graph.json'
$xmodelBindingManifest = Join-Path $XModelBindingRoot 'nuclear_4w_xmodel_root_binding.json'
$xmodelSuiteManifest = Join-Path $XModelReplaySuiteRoot 'nuclear_4xc_replay_suite.json'

foreach ($required in @(
    $rendererBinding,
    $identityManifest[0].FullName,
    $productionImageManifest,
    $xmodelCorpusManifest,
    $xmodelBindingManifest,
    $xmodelSuiteManifest,
    $graphUnlinker
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Missing required Stage 4Y input: $required"
    }
}
if (-not (Test-Path -LiteralPath $RendererGraphRoot -PathType Container)) {
    throw "Missing renderer graph root: $RendererGraphRoot"
}

$productionImages = Get-Content -LiteralPath $productionImageManifest -Raw | ConvertFrom-Json
$xmodelCorpus = Get-Content -LiteralPath $xmodelCorpusManifest -Raw | ConvertFrom-Json
$xmodelBinding = Get-Content -LiteralPath $xmodelBindingManifest -Raw | ConvertFrom-Json
$xmodelSuite = Get-Content -LiteralPath $xmodelSuiteManifest -Raw | ConvertFrom-Json

if ([string]$productionImages.format -ne 'WaW360.Nuclear4T.ProductionImageGraph.v1' -or
    -not [bool]$productionImages.mergePass) {
    throw 'Stage 4Y requires a passing Stage 4T production image graph.'
}

if ([string]$xmodelCorpus.format -ne 'WaW360.Nuclear4XB.XModelRelocatableGraph.v1' -or
    -not [bool]$xmodelCorpus.corpusGraphPass -or
    [int]$xmodelCorpus.resultRows -ne 280 -or
    [int]$xmodelCorpus.passedModels -ne 280 -or
    [int]$xmodelCorpus.failedModels -ne 0) {
    throw 'Stage 4Y requires the refreshed 280/280 passing 4X-B relocatable graph corpus.'
}

if ([string]$xmodelBinding.format -ne 'WaW360.Nuclear4W.XModelRootBinding.v3' -or
    -not [bool]$xmodelBinding.bindingPass -or
    [int]$xmodelBinding.boundOwnedModels -ne 280 -or
    [int]$xmodelBinding.boundReferenceRoots -ne 9) {
    throw 'Stage 4Y requires the passing 4W binding: 280 owned + 9 reference XModel roots.'
}

if ([string]$xmodelSuite.format -ne 'WaW360.Nuclear4XC.XModelReplaySuite.v1' -or
    -not [bool]$xmodelSuite.pass -or
    @($xmodelSuite.cases).Count -ne 4) {
    throw 'Stage 4Y requires the passing 4X-C representative replay suite.'
}

$imageGraphRoot = [string]$productionImages.graphRoot
$expectedImages = [int]$productionImages.imageGraphs
if ($expectedImages -le 0 -or -not (Test-Path -LiteralPath $imageGraphRoot -PathType Container)) {
    throw "Stage 4Y image graph is unavailable or empty: $imageGraphRoot"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

Write-Host '[NUCLEAR-4Y] ================================================================'
Write-Host '[NUCLEAR-4Y] 6,811-ROOT PC-BACKED XMODEL PRODUCTION GRAFT'
Write-Host '[NUCLEAR-4Y] ================================================================'
Write-Host "[NUCLEAR-4Y] Native oracle       : $nativeMain"
Write-Host "[NUCLEAR-4Y] Native identity     : $($identityManifest[0].FullName)"
Write-Host "[NUCLEAR-4Y] Renderer graph      : $RendererGraphRoot"
Write-Host "[NUCLEAR-4Y] Production image graph: $imageGraphRoot"
Write-Host "[NUCLEAR-4Y] Production images   : $expectedImages"
Write-Host "[NUCLEAR-4Y] XModel graph corpus : $XModelGraphRoot"
Write-Host '[NUCLEAR-4Y] Owned XModels       : 280'
Write-Host '[NUCLEAR-4Y] XModel references   : 9 (reference shells preserved)'
Write-Host ''
Write-Host '[NUCLEAR-4Y] Safety: full root sequence + layout replay + OAT 0/0 are mandatory.'
Write-Host '[NUCLEAR-4Y] Hardware remains fenced until Stage 4F critical-content readiness passes.'
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
        $xmodelBindingManifest
    )

    & dotnet @args
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

$emitterManifestPath = Join-Path $OutputRoot 'nuclear_xenon_renderer_emit.json'
if (-not (Test-Path -LiteralPath $emitterManifestPath -PathType Leaf)) {
    throw "Stage 4Y emitter manifest was not written: $emitterManifestPath"
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
    [int]$emitter.RealImageRoots -eq $expectedImages

$summary = [ordered]@{
    format = 'WaW360.Nuclear4Y.XModelProductionGraft.v1'
    nativeFastFile = $nativeMain
    nativeIdentityManifest = $identityManifest[0].FullName
    rendererBindingManifest = $rendererBinding
    productionImageManifest = $productionImageManifest
    xmodelCorpusManifest = $xmodelCorpusManifest
    xmodelBindingManifest = $xmodelBindingManifest
    xmodelReplaySuiteManifest = $xmodelSuiteManifest
    nativeRoots = [int]$emitter.NativeRoots
    oatListedRoots = [int]$emitter.OatListedRoots
    pcBackedImages = [int]$emitter.RealImageRoots
    pcBackedOwnedXModels = [int]$emitter.RealXModelRoots
    preservedXModelReferenceRoots = [int]$emitter.XModelReferenceRoots
    exactIdentitySequence = [bool]$emitter.ExactIdentitySequence
    oatZeroWarningsZeroErrors = [bool]$emitter.OatZeroWarningsZeroErrors
    layoutReplayExact = [bool]$emitter.LayoutReplayExact
    productionXModelGraftPass = $pass
    hardwareApproved = $false
    hardwareFence = 'Stage 4F full critical-content readiness remains mandatory.'
    outputFastFile = [string]$emitter.OutputFastFile
    emitterManifest = $emitterManifestPath
}

$summaryPath = Join-Path $OutputRoot 'nuclear_4y_xmodel_production_graft.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host ''
Write-Host '[NUCLEAR-4Y] ================= XMODEL GRAFT RESULT =========================='
Write-Host "[NUCLEAR-4Y] Native roots           : $([int]$emitter.NativeRoots)"
Write-Host "[NUCLEAR-4Y] OAT roots              : $([int]$emitter.OatListedRoots) / 6811"
Write-Host "[NUCLEAR-4Y] Real XModel roots      : $([int]$emitter.RealXModelRoots) / 280"
Write-Host "[NUCLEAR-4Y] XModel references      : $([int]$emitter.XModelReferenceRoots) / 9"
Write-Host "[NUCLEAR-4Y] Production image roots : $([int]$emitter.RealImageRoots)"
Write-Host "[NUCLEAR-4Y] Layout replay exact    : $([bool]$emitter.LayoutReplayExact)"
Write-Host "[NUCLEAR-4Y] Exact identity sequence: $([bool]$emitter.ExactIdentitySequence)"
Write-Host "[NUCLEAR-4Y] OAT 0 warn / 0 error   : $([bool]$emitter.OatZeroWarningsZeroErrors)"
Write-Host "[NUCLEAR-4Y] Production graft pass  : $pass"
Write-Host '[NUCLEAR-4Y] Hardware approved      : False'
Write-Host "[NUCLEAR-4Y] Summary                : $summaryPath"
Write-Host '[NUCLEAR-4Y] ================================================================='

if (-not $pass) {
    Write-Host ''
    Write-Host '[NUCLEAR-4Y] XMODEL PRODUCTION GRAFT: BLOCKED' -ForegroundColor Yellow
    Write-Host '[NUCLEAR-4Y] Do NOT copy this FF to Xbox.'

    if ($null -ne $emitter.PSObject.Properties['OatGateError'] -and
        -not [string]::IsNullOrWhiteSpace([string]$emitter.OatGateError)) {
        Write-Host "[NUCLEAR-4Y] OAT gate: $([string]$emitter.OatGateError)"
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
            Write-Host '[NUCLEAR-4Y] OAT hard-gate tail:'
            Get-Content -LiteralPath $gateLog[0].FullName -Tail 100
        }
    }

    exit 4
}

Write-Host ''
Write-Host '[NUCLEAR-4Y] 280-XMODEL / 6,811-ROOT PRODUCTION GRAFT: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4Y] All 280 PC-backed Xenon XModels survived the integrated root sequence and OAT hard gate.'
Write-Host '[NUCLEAR-4Y] Next: rerun 4F with XModel production credit; hardware remains fenced until critical fallbacks close.'
exit 0
