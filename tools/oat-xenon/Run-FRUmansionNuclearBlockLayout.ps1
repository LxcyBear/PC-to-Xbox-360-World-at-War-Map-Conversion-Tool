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

if ([string]::IsNullOrWhiteSpace($GraphRoot)) {
    $GraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_oat_graph_dump'
}
if ([string]::IsNullOrWhiteSpace($BindingRoot)) {
    $BindingRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_root_binding'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_block_layout'
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
    throw "Missing Stage 3B fixed-up graph dump: $GraphRoot"
}
if (-not (Test-Path -LiteralPath $bindingManifest -PathType Leaf)) {
    throw "Missing Stage 4C root-binding manifest: $bindingManifest"
}
if ($identityManifest.Count -ne 1) {
    throw "Could not identify the Stage 4C native identity manifest under: $identityDir"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

Write-Host '[NUCLEAR-4D] ================================================================'
Write-Host '[NUCLEAR-4D] OAT-STYLE XENON BLOCK CURSOR + INSERT-SLOT LAYOUT'
Write-Host '[NUCLEAR-4D] ================================================================'
Write-Host "[NUCLEAR-4D] Graph root       : $GraphRoot"
Write-Host "[NUCLEAR-4D] Native oracle    : $nativeMain"
Write-Host "[NUCLEAR-4D] 4C binding       : $bindingManifest"
Write-Host "[NUCLEAR-4D] Native identities: $($identityManifest[0].FullName)"
Write-Host "[NUCLEAR-4D] Output           : $OutputRoot"
Write-Host ''
Write-Host '[NUCLEAR-4D] Full native root order; all-name-shell non-renderer baseline.'
Write-Host '[NUCLEAR-4D] TEMP=scratch max, VIRTUAL=insert slots/graph, PHYSICAL=shader physicalPart.'
Write-Host ''

Push-Location $repo
try {
    dotnet run --project '.\src\WaW360.Cli\WaW360.Cli.csproj' -c Release -- xenon-graph-layout "$GraphRoot" "$nativeMain" "$($identityManifest[0].FullName)" "$bindingManifest" "$OutputRoot" "$CanonicalBaseName" 87 897
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($code -ne 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4D] BLOCK LAYOUT FAILED.' -ForegroundColor Yellow
    Write-Host '[NUCLEAR-4D] Stage 4E byte emission remains fenced.'
    exit $code
}

Write-Host ''
Write-Host '[NUCLEAR-4D] BLOCK LAYOUT CLOSURE: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4D] TEMP/VIRTUAL/PHYSICAL cursors and renderer insert slots are deterministic.'
Write-Host '[NUCLEAR-4D] Next frontier: Stage 4E writes the actual big-endian renderer bytes.'
