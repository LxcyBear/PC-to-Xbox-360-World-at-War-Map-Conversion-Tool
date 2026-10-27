[CmdletBinding()]
param(
    [string]$NativeFastFile = 'C:\Users\jorda\OneDrive\Desktop\working frumansion\nazi_zombie_FRUmansion.ff',
    [string]$NativeIdentityManifest = '',
    [string]$OutputRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($NativeIdentityManifest)) {
    $identityDir = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_root_binding\native_identity'
    $identity = @(Get-ChildItem -LiteralPath $identityDir -Filter '*_native_identity_manifest.json' -File -ErrorAction SilentlyContinue |
        Sort-Object FullName |
        Select-Object -First 1)
    if ($identity.Count -ne 1) {
        throw "Could not identify verified native identity manifest under: $identityDir"
    }
    $NativeIdentityManifest = $identity[0].FullName
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4t_native_xmodel_layout'
}

if (-not (Test-Path -LiteralPath $NativeFastFile -PathType Leaf)) {
    throw "Native Xbox FRUmansion fastfile not found: $NativeFastFile"
}
if (-not (Test-Path -LiteralPath $NativeIdentityManifest -PathType Leaf)) {
    throw "Native identity manifest not found: $NativeIdentityManifest"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

Write-Host '[NUCLEAR-4T] ================================================================'
Write-Host '[NUCLEAR-4T] NATIVE XBOX XMODEL 0xD4 FIXED-BODY BOUNDARY PROOF'
Write-Host '[NUCLEAR-4T] ================================================================'
Write-Host "[NUCLEAR-4T] Native oracle : $NativeFastFile"
Write-Host "[NUCLEAR-4T] Identities    : $NativeIdentityManifest"
Write-Host "[NUCLEAR-4T] Output        : $OutputRoot"
Write-Host ''
Write-Host '[NUCLEAR-4T] Policy: read-only native evidence; no game fastfile is emitted.'
Write-Host '[NUCLEAR-4T] Control: test the legacy PC-shaped 0xE4 boundary in parallel.'
Write-Host ''

$dotnetArgs = @(
    'run',
    '--project', '.\src\WaW360.Cli\WaW360.Cli.csproj',
    '-c', 'Release',
    '--',
    'xenon-native-xmodel-layout',
    $NativeFastFile,
    $NativeIdentityManifest,
    $OutputRoot
)

Push-Location $repo
try {
    & dotnet @dotnetArgs
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

$manifest = Join-Path $OutputRoot 'nuclear_4t_native_xmodel_layout.json'
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
    throw "Stage 4T did not write its evidence manifest: $manifest"
}

$result = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json

Write-Host ''
Write-Host '[NUCLEAR-4T] ================= NATIVE XMODEL RESULT =========================='
Write-Host "[NUCLEAR-4T] Native XModels       : $([int]$result.NativeXModelRoots)"
Write-Host "[NUCLEAR-4T] Unique 0xD4 matches  : $([int]$result.UniqueD4Matches)"
Write-Host "[NUCLEAR-4T] Missing 0xD4         : $([int]$result.MissingD4Matches)"
Write-Host "[NUCLEAR-4T] Ambiguous 0xD4       : $([int]$result.AmbiguousD4Matches)"
Write-Host "[NUCLEAR-4T] Legacy 0xE4 matches  : $([int]$result.LegacyE4Matches)"
Write-Host "[NUCLEAR-4T] 0xD4-only identities : $([int]$result.D4OnlyMatches)"
Write-Host "[NUCLEAR-4T] Exact boundary proof : $([bool]$result.ExactD4BoundaryProof)"
Write-Host "[NUCLEAR-4T] Manifest             : $manifest"
Write-Host '[NUCLEAR-4T] ================================================================='

if ([bool]$result.ExactD4BoundaryProof) {
    Write-Host ''
    Write-Host '[NUCLEAR-4T] XMODEL 0xD4 BOUNDARY: PASS' -ForegroundColor Green
    Write-Host '[NUCLEAR-4T] Native FRUmansion proves the Xbox fixed body ends at 0xD4, not 0xE4.'
    Write-Host '[NUCLEAR-4T] Next: rebuild the PC XModel proof into native 0xD4 roots and re-enable typed promotion.'
    exit 0
}

Write-Host ''
Write-Host '[NUCLEAR-4T] XMODEL 0xD4 BOUNDARY: INCOMPLETE' -ForegroundColor Yellow
Write-Host '[NUCLEAR-4T] Review missing/ambiguous rows before changing the production serializer.'
if ($code -ne 0) { exit $code }
exit 4
