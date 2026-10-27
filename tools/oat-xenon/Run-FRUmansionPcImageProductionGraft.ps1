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

function Invoke-Stage4SProbe {
    param(
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter(Mandatory=$true)][string]$ProbeRoot,
        [int]$ImageLimit = -1
    )

    if (Test-Path -LiteralPath $ProbeRoot) { Remove-Item -LiteralPath $ProbeRoot -Recurse -Force }

    $probeArgs = @(
        'run',
        '--project', '.\src\WaW360.Cli\WaW360.Cli.csproj',
        '-c', 'Release',
        '--',
        'xenon-graph-emit',
        $RendererGraphRoot,
        $nativeMain,
        $identityManifest[0].FullName,
        $bindingManifest,
        $ProbeRoot,
        $CanonicalBaseName,
        $graphUnlinker,
        '87',
        '897'
    )

    if ($ImageLimit -ge 0) {
        $probeArgs += @(
            $imageGraphRoot,
            [string]$expectedImages,
            'true',
            [string]$ImageLimit
        )
    }

    Push-Location $repo
    try {
        & dotnet @probeArgs
        $probeCode = $LASTEXITCODE
    }
    finally { Pop-Location }

    $probeManifest = Join-Path $ProbeRoot 'nuclear_xenon_renderer_emit.json'
    if (-not (Test-Path -LiteralPath $probeManifest -PathType Leaf)) {
        return [pscustomobject]@{
            Name = $Name
            ExitCode = $probeCode
            ValidationPass = $false
            RealImages = 0
            OatRoots = 0
            OatError = 'probe emitter manifest missing'
        }
    }

    $probe = Get-Content -LiteralPath $probeManifest -Raw | ConvertFrom-Json
    return [pscustomobject]@{
        Name = $Name
        ExitCode = $probeCode
        ValidationPass = [bool]$probe.ValidationPass
        RealImages = [int]$probe.RealImageRoots
        OatRoots = [int]$probe.OatListedRoots
        OatError = [string]$probe.OatGateError
    }
}

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

    Write-Host ''
    Write-Host '[NUCLEAR-4S] ================= FAILURE FORENSICS ============================='
    Write-Host "[NUCLEAR-4S] OAT gate error: $([string]$emitter.OatGateError)"

    $resolvedRoots = @($bridge.resolved | Sort-Object { [int]$_.Index })
    if ($resolvedRoots.Count -gt 0) {
        Write-Host '[NUCLEAR-4S] PC-backed native roots in graft order:'
        for ($ri = 0; $ri -lt $resolvedRoots.Count; $ri++) {
            Write-Host ("  ordinal {0}: root #{1}  {2}" -f ($ri + 1), [int]$resolvedRoots[$ri].Index, [string]$resolvedRoots[$ri].Name)
        }
    }

    $gateLog = Join-Path $OutputRoot 'oat_hard_gate\nazi_zombie_FRUmansion_oat_xenon_list.log'
    if (-not (Test-Path -LiteralPath $gateLog -PathType Leaf)) {
        $gateLogs = @(Get-ChildItem -LiteralPath (Join-Path $OutputRoot 'oat_hard_gate') -Filter '*.log' -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1)
        if ($gateLogs.Count -eq 1) { $gateLog = $gateLogs[0].FullName }
    }
    if (Test-Path -LiteralPath $gateLog -PathType Leaf) {
        Write-Host ''
        Write-Host '[NUCLEAR-4S] OAT hard-gate tail:'
        Get-Content -LiteralPath $gateLog -Tail 80
    }

    Write-Host ''
    Write-Host '[NUCLEAR-4S] Running automatic isolation probes...' -ForegroundColor Cyan
    $probeBase = Join-Path (Split-Path -Parent $OutputRoot) 'nuclear_4s_pc_image_graft_forensics'
    if (Test-Path -LiteralPath $probeBase) { Remove-Item -LiteralPath $probeBase -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $probeBase | Out-Null

    $control = Invoke-Stage4SProbe -Name 'renderer-only' -ProbeRoot (Join-Path $probeBase 'renderer_only')
    $one = $null
    if ($expectedImages -gt 0) {
        $one = Invoke-Stage4SProbe -Name 'first-pc-image' -ProbeRoot (Join-Path $probeBase 'first_pc_image') -ImageLimit 1
    }

    Write-Host ''
    Write-Host ("[NUCLEAR-4S] Probe renderer-only : pass={0} roots={1}/6811 exit={2}" -f
        $control.ValidationPass, $control.OatRoots, $control.ExitCode)
    if (-not [string]::IsNullOrWhiteSpace($control.OatError)) {
        Write-Host "[NUCLEAR-4S]   OAT: $($control.OatError)"
    }

    if ($null -ne $one) {
        $firstName = if ($resolvedRoots.Count -gt 0) { [string]$resolvedRoots[0].Name } else { '<unknown>' }
        $firstIndex = if ($resolvedRoots.Count -gt 0) { [int]$resolvedRoots[0].Index } else { -1 }
        Write-Host ("[NUCLEAR-4S] Probe first image   : pass={0} images={1} roots={2}/6811 exit={3}" -f
            $one.ValidationPass, $one.RealImages, $one.OatRoots, $one.ExitCode)
        Write-Host "[NUCLEAR-4S]   first native image = root #$firstIndex '$firstName'"
        if (-not [string]::IsNullOrWhiteSpace($one.OatError)) {
            Write-Host "[NUCLEAR-4S]   OAT: $($one.OatError)"
        }

        if ($control.ValidationPass -and -not $one.ValidationPass) {
            Write-Host '[NUCLEAR-4S] DIAGNOSIS: renderer control passes; the FIRST PC image graft is sufficient to break OAT.' -ForegroundColor Yellow
        }
        elseif ($control.ValidationPass -and $one.ValidationPass -and -not $productionPass) {
            Write-Host '[NUCLEAR-4S] DIAGNOSIS: renderer + first PC image passes; the SECOND image (or two-image interaction) triggers failure.' -ForegroundColor Yellow
        }
        elseif (-not $control.ValidationPass) {
            Write-Host '[NUCLEAR-4S] DIAGNOSIS: renderer-only control also fails; this is not specific to the PC image graft.' -ForegroundColor Yellow
        }
    }

    $forensicSummary = [ordered]@{
        format = 'WaW360.Nuclear4S.PcImageGraftForensics.v1'
        originalGraftManifest = $emitterManifestPath
        originalOatError = [string]$emitter.OatGateError
        pcBackedRoots = $resolvedRoots
        rendererOnly = $control
        firstPcImage = $one
    }
    $forensicPath = Join-Path $probeBase 'nuclear_4s_pc_image_graft_forensics.json'
    $forensicSummary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $forensicPath -Encoding UTF8
    Write-Host "[NUCLEAR-4S] Forensics summary: $forensicPath"
    Write-Host '[NUCLEAR-4S] ================================================================='
    exit 4
}

Write-Host '[NUCLEAR-4S] PC IMAGE PRODUCTION GRAFT: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4S] Legitimate PC-sourced images now survive the full 6,811-root Xenon graph.'
Write-Host '[NUCLEAR-4S] Next: feed this production image count/mass into 4F and continue closing non-image critical families.'
Write-Host '[NUCLEAR-4S] Do NOT Xbox-test yet; 4F remains the hardware gate.'
