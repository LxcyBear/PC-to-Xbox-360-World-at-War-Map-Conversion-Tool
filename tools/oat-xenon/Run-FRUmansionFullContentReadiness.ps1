param(
    [string]$PcMapsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps',
    [string]$MapName = 'nazi_zombie_FRUmansion',
    [string]$NativeControlDirectory = 'C:\Users\jorda\OneDrive\Desktop\working frumansion',
    [double]$MinimumSizeRatio = 0.80,
    [double]$MaximumSizeRatio = 1.20
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$outputRoot = Join-Path $repo 'output'
$graphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_oat_graph_dump'
$bindingRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_root_binding'
$bindingManifest = Join-Path $bindingRoot 'nuclear_xenon_root_binding.json'
$identityDir = Join-Path $bindingRoot 'native_identity'
$identityManifest = @(Get-ChildItem -LiteralPath $identityDir -Filter '*_native_identity_manifest.json' -File |
    Sort-Object FullName |
    Select-Object -First 1)

$productionImageGraftManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4s_pc_image_graft\nuclear_4s_pc_image_graft.json'

function Find-MainFastFile {
    param([Parameter(Mandatory=$true)][string]$Directory)

    foreach ($name in @('frumansion_j_p.ff', 'nazi_zombie_FRUmansion.ff', 'nazi_zombie_frumansion.ff')) {
        $candidate = Join-Path $Directory $name
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }

    $candidates = @(Get-ChildItem -LiteralPath $Directory -Filter '*.ff' -File |
        Where-Object {
            $_.Name -notmatch '(?i)_load[.]ff$' -and
            $_.Name -notmatch '(?i)_patch[.]ff$'
        })

    if ($candidates.Count -eq 1) {
        return $candidates[0].FullName
    }

    throw "Could not identify one native FRUmansion main fastfile in: $Directory"
}

$nativeMain = Find-MainFastFile -Directory $NativeControlDirectory

if (-not (Test-Path -LiteralPath $PcMapsRoot -PathType Container)) {
    throw "PC maps root not found: $PcMapsRoot"
}
if (-not (Test-Path -LiteralPath $graphRoot -PathType Container)) {
    throw "Missing nuclear graph dump: $graphRoot"
}
if (-not (Test-Path -LiteralPath $bindingManifest -PathType Leaf)) {
    throw "Missing Stage 4C root-binding manifest: $bindingManifest"
}
if ($identityManifest.Count -ne 1) {
    throw "Could not identify Stage 4C native identity manifest under: $identityDir"
}

$productionImageCredit = $null
if (Test-Path -LiteralPath $productionImageGraftManifest -PathType Leaf) {
    try {
        $stage4s = Get-Content -LiteralPath $productionImageGraftManifest -Raw | ConvertFrom-Json
        if ([string]$stage4s.format -eq 'WaW360.Nuclear4S.PcImageProductionGraft.v1' -and
            [bool]$stage4s.productionImageGraftPass) {
            $productionImageCredit = $productionImageGraftManifest
        }
    }
    catch {
        throw "Stage 4S manifest exists but could not be validated: $($_.Exception.Message)"
    }
}

Write-Host '[NUCLEAR-4F] ================================================================'
Write-Host '[NUCLEAR-4F] FULL-CONTENT MASS / PRE-HARDWARE GATE'
Write-Host '[NUCLEAR-4F] ================================================================'
Write-Host "[NUCLEAR-4F] PC maps root       : $PcMapsRoot"
Write-Host "[NUCLEAR-4F] Map                : $MapName"
Write-Host "[NUCLEAR-4F] Native oracle      : $nativeMain"
Write-Host "[NUCLEAR-4F] Renderer graph     : $graphRoot"
Write-Host "[NUCLEAR-4F] Native identities  : $($identityManifest[0].FullName)"
Write-Host "[NUCLEAR-4F] Renderer bindings  : $bindingManifest"
Write-Host "[NUCLEAR-4F] 4S image credit    : $(if ($null -eq $productionImageCredit) { '<none>' } else { $productionImageCredit })"
Write-Host "[NUCLEAR-4F] Size window        : $([math]::Round($MinimumSizeRatio * 100))% .. $([math]::Round($MaximumSizeRatio * 100))% of decompressed native oracle"
Write-Host ''
Write-Host '[NUCLEAR-4F] POLICY: no Xbox test candidate until size + critical-content gates both pass.'
Write-Host ''

$minInvariant = $MinimumSizeRatio.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$maxInvariant = $MaximumSizeRatio.ToString([System.Globalization.CultureInfo]::InvariantCulture)

Push-Location $repo
try {
    $args = @(
        'run',
        '--project', '.\src\WaW360.Cli\WaW360.Cli.csproj',
        '-c', 'Release',
        '--',
        'linker-v2-full-content-readiness',
        $PcMapsRoot,
        $MapName,
        $outputRoot,
        $nativeMain,
        $graphRoot,
        $identityManifest[0].FullName,
        $bindingManifest,
        $minInvariant,
        $maxInvariant
    )
    if ($null -ne $productionImageCredit) {
        $args += $productionImageCredit
    }

    & dotnet @args
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

$report = Join-Path $outputRoot "$MapName\nuclear_4f_full_content_readiness\nuclear_4f_full_content_readiness.txt"

Write-Host ''
if (Test-Path -LiteralPath $report -PathType Leaf) {
    Write-Host '[NUCLEAR-4F] ================= FULL CONTENT REPORT =========================='
    Get-Content -LiteralPath $report
    Write-Host '[NUCLEAR-4F] ================================================================='
}

if ($code -ne 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4F] FULL-CONTENT READINESS: BLOCKED' -ForegroundColor Yellow
    Write-Host '[NUCLEAR-4F] This is expected until the large missing families are promoted.'
    Write-Host '[NUCLEAR-4F] Do NOT hardware-test the 4E renderer-proof candidate.'
    exit $code
}

Write-Host ''
Write-Host '[NUCLEAR-4F] FULL-CONTENT READINESS: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4F] Size and critical-content gates are closed. Full integrated emission may begin.'
