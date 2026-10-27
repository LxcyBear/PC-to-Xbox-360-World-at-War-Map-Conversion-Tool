param(
    [string]$NativeControlDirectory = 'C:\Users\jorda\OneDrive\Desktop\working frumansion',
    [string]$GraphRoot = '',
    [string]$BindingRoot = '',
    [string]$ImageGraphRoot = '',
    [string]$OutputRoot = '',
    [string]$CanonicalBaseName = 'nazi_zombie_FRUmansion'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$graphUnlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($GraphRoot)) {
    $GraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_oat_graph_dump'
}
if ([string]::IsNullOrWhiteSpace($BindingRoot)) {
    $BindingRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_root_binding'
}
if ([string]::IsNullOrWhiteSpace($ImageGraphRoot)) {
    $ImageGraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4n_xenon_image_graph\dump'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4p_full_image_graft'
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
$bindingManifest = Join-Path $BindingRoot 'nuclear_xenon_root_binding.json'
$identityDir = Join-Path $BindingRoot 'native_identity'
$identityManifest = @(Get-ChildItem -LiteralPath $identityDir -Filter '*_native_identity_manifest.json' -File |
    Sort-Object FullName |
    Select-Object -First 1)

if (-not (Test-Path -LiteralPath $GraphRoot -PathType Container)) {
    throw "Missing fixed-up renderer graph dump: $GraphRoot"
}
if (-not (Test-Path -LiteralPath $ImageGraphRoot -PathType Container)) {
    throw "Missing Stage 4N fixed-up image graph dump: $ImageGraphRoot"
}
if (-not (Test-Path -LiteralPath $bindingManifest -PathType Leaf)) {
    throw "Missing Stage 4C root-binding manifest: $bindingManifest"
}
if ($identityManifest.Count -ne 1) {
    throw "Could not identify the Stage 4C native identity manifest under: $identityDir"
}
if (-not (Test-Path -LiteralPath $graphUnlinker -PathType Leaf)) {
    throw "Missing specialized Xenon OAT Unlinker: $graphUnlinker"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

Write-Host '[NUCLEAR-4P] ================================================================'
Write-Host '[NUCLEAR-4P] 6,811-ROOT RENDERER + REAL IMAGE FULL-SEQUENCE GRAFT'
Write-Host '[NUCLEAR-4P] ================================================================'
Write-Host "[NUCLEAR-4P] Renderer graph : $GraphRoot"
Write-Host "[NUCLEAR-4P] Image graph    : $ImageGraphRoot"
Write-Host "[NUCLEAR-4P] Native oracle  : $nativeMain"
Write-Host "[NUCLEAR-4P] Root binding   : $bindingManifest"
Write-Host "[NUCLEAR-4P] Identity order : $($identityManifest[0].FullName)"
Write-Host "[NUCLEAR-4P] Output         : $OutputRoot"
Write-Host ''
Write-Host '[NUCLEAR-4P] Expected real image graft: 378 roots / 35,438,592 LARGE_RUNTIME bytes.'
Write-Host '[NUCLEAR-4P] SAFETY: current image graph is oracle-derived validation material.'
Write-Host '[NUCLEAR-4P] This stage can PASS validation but MUST remain NOT HARDWARE APPROVED.'
Write-Host ''

Push-Location $repo
try {
    $args = @(
        'run',
        '--project', '.\src\WaW360.Cli\WaW360.Cli.csproj',
        '-c', 'Release',
        '--',
        'xenon-graph-emit',
        $GraphRoot,
        $nativeMain,
        $identityManifest[0].FullName,
        $bindingManifest,
        $OutputRoot,
        $CanonicalBaseName,
        $graphUnlinker,
        '87',
        '897',
        $ImageGraphRoot,
        '378',
        'true'
    )

    & dotnet @args
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($code -ne 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4P] FULL-SEQUENCE IMAGE GRAFT: FAILED' -ForegroundColor Yellow
    Write-Host '[NUCLEAR-4P] Do NOT copy this FF to the Xbox.'

    $gateLog = Join-Path $OutputRoot 'oat_hard_gate\nazi_zombie_FRUmansion_oat_xenon_list.log'
    if (Test-Path -LiteralPath $gateLog -PathType Leaf) {
        Write-Host ''
        Write-Host '[NUCLEAR-4P] OAT GATE LOG TAIL'
        Write-Host '[NUCLEAR-4P] ---------------------------------------------------------------'
        Get-Content -LiteralPath $gateLog -Tail 120
        Write-Host '[NUCLEAR-4P] ---------------------------------------------------------------'
    }

    exit $code
}

$candidate = Join-Path $OutputRoot (Split-Path -Leaf $nativeMain)

Write-Host ''
Write-Host '[NUCLEAR-4P] ================================================================'
Write-Host '[NUCLEAR-4P] FULL-SEQUENCE IMAGE GRAFT: VALIDATION PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4P] 6,811-root sequence + renderer + 378 real Xenon images survived layout replay and OAT.'
Write-Host "[NUCLEAR-4P] VALIDATION FF: $candidate" -ForegroundColor Cyan
Write-Host '[NUCLEAR-4P] NOT FOR XBOX TESTING: image payload source is still oracle validation data.'
Write-Host '[NUCLEAR-4P] Next: produce the same image graph from legitimate PC IWI/DDS sources, then reassess 4F.'
Write-Host '[NUCLEAR-4P] ================================================================'
