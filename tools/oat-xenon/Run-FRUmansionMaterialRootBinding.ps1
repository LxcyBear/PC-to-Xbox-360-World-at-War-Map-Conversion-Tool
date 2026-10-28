[CmdletBinding()]
param(
    [string]$GraphRoot = '',
    [string]$BridgeRoot = '',
    [string]$BindingRoot = '',
    [string]$OutputRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($GraphRoot)) {
    $GraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4zb_material_relocatable_graph'
}
if ([string]::IsNullOrWhiteSpace($BridgeRoot)) {
    $BridgeRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_material_techset_bridge'
}
if ([string]::IsNullOrWhiteSpace($BindingRoot)) {
    $BindingRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_root_binding'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4zd_material_root_binding'
}

$bridgeManifest = Join-Path $BridgeRoot 'material_techset_bridge.json'
$identityDir = Join-Path $BindingRoot 'native_identity'
$identityManifests = @(
    Get-ChildItem -LiteralPath $identityDir -Filter '*_native_identity_manifest.json' -File -ErrorAction SilentlyContinue |
        Sort-Object FullName
)

if ($identityManifests.Count -ne 1) {
    throw "Expected exactly one native identity manifest under '$identityDir'; found $($identityManifests.Count)."
}
$nativeIdentityManifest = $identityManifests[0].FullName

foreach ($required in @($bridgeManifest, $nativeIdentityManifest)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "4Z-D0 input missing: $required"
    }
}
if (-not (Test-Path -LiteralPath $GraphRoot -PathType Container)) {
    throw "4Z-D0 graph root missing: $GraphRoot"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

Write-Host '[NUCLEAR-4Z-D0] ============================================================='
Write-Host '[NUCLEAR-4Z-D0] PC MATERIAL -> 6,811-ROOT NATIVE SLOT BINDING'
Write-Host '[NUCLEAR-4Z-D0] ============================================================='
Write-Host "[NUCLEAR-4Z-D0] Graph root      : $GraphRoot"
Write-Host "[NUCLEAR-4Z-D0] Bridge manifest : $bridgeManifest"
Write-Host "[NUCLEAR-4Z-D0] Native identity : $nativeIdentityManifest"
Write-Host '[NUCLEAR-4Z-D0] Policy          : direct identity first; semantic fallback only after calibration'
Write-Host ''

Push-Location $repo
try {
    & dotnet run --project '.\src\WaW360.Cli\WaW360.Cli.csproj' -c Release -- xenon-material-root-bind $GraphRoot $bridgeManifest $nativeIdentityManifest $OutputRoot
    $code = [int]$LASTEXITCODE
}
finally {
    Pop-Location
}

$manifest = Join-Path $OutputRoot 'nuclear_4zd_material_root_binding.json'
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
    throw "4Z-D0 binding manifest was not written: $manifest"
}

$result = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json

Write-Host ''
Write-Host '[NUCLEAR-4Z-D0] ================= MATERIAL BINDING RESULT ===================='
Write-Host "[NUCLEAR-4Z-D0] Corpus materials       : $([int]$result.CorpusMaterials)"
Write-Host "[NUCLEAR-4Z-D0] Native Material roots  : $([int]$result.NativeMaterialRoots)"
Write-Host "[NUCLEAR-4Z-D0] Direct identity        : $([int]$result.DirectIdentityMatches)"
Write-Host "[NUCLEAR-4Z-D0] Semantic matches       : $([int]$result.SemanticMatches)"
Write-Host "[NUCLEAR-4Z-D0] Unmatched              : $([int]$result.UnmatchedMaterials)"
Write-Host "[NUCLEAR-4Z-D0] Ambiguous              : $([int]$result.AmbiguousMaterials)"
Write-Host "[NUCLEAR-4Z-D0] Calibration pairs      : $([int]$result.SharedCalibrationPairs)"
Write-Host "[NUCLEAR-4Z-D0] Calibration matches    : $([int]$result.SharedCalibrationMatches)"
Write-Host "[NUCLEAR-4Z-D0] Calibration agreement  : $([double]$result.SharedCalibrationAgreement)"
Write-Host "[NUCLEAR-4Z-D0] Fingerprint mode       : $([string]$result.SemanticFingerprintMode)"
Write-Host "[NUCLEAR-4Z-D0] Semantic fallback      : $([bool]$result.SemanticFallbackEnabled)"
Write-Host "[NUCLEAR-4Z-D0] Binding pass           : $([bool]$result.BindingPass)"
Write-Host "[NUCLEAR-4Z-D0] Manifest               : $manifest"
Write-Host '[NUCLEAR-4Z-D0] ================================================================='

if ($code -ne 0 -or -not [bool]$result.BindingPass) {
    Write-Host ''
    Write-Host '[NUCLEAR-4Z-D0] MATERIAL ROOT BINDING: BLOCKED' -ForegroundColor Yellow
    if (@($result.Unmatched).Count -gt 0) {
        Write-Host ('[NUCLEAR-4Z-D0] Unmatched examples: ' + (@($result.Unmatched | Select-Object -First 12) -join ' | '))
    }
    if (@($result.Ambiguities).Count -gt 0) {
        Write-Host ('[NUCLEAR-4Z-D0] Ambiguous examples: ' + (@($result.Ambiguities | Select-Object -First 8 | ForEach-Object { "$($_.CorpusName):$($_.Kind)" }) -join ' | '))
    }
    exit 4
}

Write-Host ''
Write-Host '[NUCLEAR-4Z-D0] 502/502 MATERIAL ROOT BINDING: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4Z-D0] Next: 4Z-D consumes this explicit binding manifest for the 6,811-root production graft.'
exit 0
