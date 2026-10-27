param(
    [string]$NativeControlDirectory = 'C:\Users\jorda\OneDrive\Desktop\working frumansion',
    [string]$GraphRoot = '',
    [string]$BindingRoot = '',
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
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_renderer_emit'
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
    if ($candidates.Count -eq 1) { return $candidates[0].FullName }
    throw "Could not identify one native FRUmansion main fastfile in: $Directory"
}

$nativeMain = Find-MainFastFile -Directory $NativeControlDirectory
$bindingManifest = Join-Path $BindingRoot 'nuclear_xenon_root_binding.json'
$identityDir = Join-Path $BindingRoot 'native_identity'
$identityManifest = @(Get-ChildItem -LiteralPath $identityDir -Filter '*_native_identity_manifest.json' -File |
    Sort-Object FullName |
    Select-Object -First 1)

if (-not (Test-Path -LiteralPath $GraphRoot -PathType Container)) {
    throw "Missing Stage 3B fixed-up graph dump: $GraphRoot"
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

Write-Host '[NUCLEAR-4E] ================================================================'
Write-Host '[NUCLEAR-4E] REAL XENON RENDERER BYTE EMISSION + OAT HARD GATE'
Write-Host '[NUCLEAR-4E] ================================================================'
Write-Host "[NUCLEAR-4E] Graph root       : $GraphRoot"
Write-Host "[NUCLEAR-4E] Native oracle    : $nativeMain"
Write-Host "[NUCLEAR-4E] 4C binding       : $bindingManifest"
Write-Host "[NUCLEAR-4E] Native identities: $($identityManifest[0].FullName)"
Write-Host "[NUCLEAR-4E] OAT Unlinker     : $graphUnlinker"
Write-Host "[NUCLEAR-4E] Output           : $OutputRoot"
Write-Host ''
Write-Host '[NUCLEAR-4E] Writing real MaterialPixelShader + TechniqueSet/Technique/Pass/VS/Decl/Arg/Literal graphs.'
Write-Host '[NUCLEAR-4E] Shader cached/physical blobs are copied from the fixed-up graph dump, not from donor FF payloads.'
Write-Host '[NUCLEAR-4E] Candidate is NOT hardware-approved unless OAT resolves all 6,811 native identities with 0 warnings/errors.'
Write-Host ''

Push-Location $repo
try {
    dotnet run --project '.\src\WaW360.Cli\WaW360.Cli.csproj' -c Release -- xenon-graph-emit "$GraphRoot" "$nativeMain" "$($identityManifest[0].FullName)" "$bindingManifest" "$OutputRoot" "$CanonicalBaseName" "$graphUnlinker" 87 897
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($code -ne 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4E] RENDERER EMISSION / OAT HARD GATE FAILED.' -ForegroundColor Yellow
    Write-Host '[NUCLEAR-4E] Do NOT copy this candidate to the Xbox yet.'

    $gateLog = Join-Path $OutputRoot 'oat_hard_gate\nazi_zombie_FRUmansion_oat_xenon_list.log'
    if (Test-Path -LiteralPath $gateLog -PathType Leaf) {
        Write-Host ''
        Write-Host '[NUCLEAR-4E] OAT HARD-GATE LOG TAIL'
        Write-Host '[NUCLEAR-4E] ---------------------------------------------------------------'
        Get-Content -LiteralPath $gateLog -Tail 120
        Write-Host '[NUCLEAR-4E] ---------------------------------------------------------------'
    }

    exit $code
}

$candidate = Join-Path $OutputRoot (Split-Path -Leaf $nativeMain)

Write-Host ''
Write-Host '[NUCLEAR-4E] ================================================================'
Write-Host '[NUCLEAR-4E] REAL RENDERER CANDIDATE: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4E] 4D replay exact + wrapper round-trip + OAT identity sequence + 0 warnings/errors.'
Write-Host "[NUCLEAR-4E] LATEST FF TO TEST: $candidate" -ForegroundColor Cyan
Write-Host '[NUCLEAR-4E] ================================================================'
