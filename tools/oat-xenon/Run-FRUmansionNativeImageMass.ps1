param(
    [string]$NativeDirectory = 'C:\Users\jorda\OneDrive\Desktop\working frumansion',
    [string]$MapName = 'nazi_zombie_FRUmansion',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4j_native_image_mass'
}

if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) {
    throw "OAT Unlinker not found: $unlinker"
}
if (-not (Test-Path -LiteralPath $NativeDirectory -PathType Container)) {
    throw "Native oracle directory not found: $NativeDirectory"
}

$preferred = @(
    (Join-Path $NativeDirectory 'frumansion_j_p.ff'),
    (Join-Path $NativeDirectory 'nazi_zombie_FRUmansion.ff'),
    (Join-Path $NativeDirectory 'nazi_zombie_frumansion.ff')
) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }

if ($preferred.Count -gt 0) {
    $nativeFastFile = $preferred[0]
}
else {
    $candidates = @(Get-ChildItem -LiteralPath $NativeDirectory -Filter '*.ff' -File |
        Where-Object { $_.Name -notmatch '(?i)_load[.]ff$' } |
        Sort-Object Length -Descending)

    if ($candidates.Count -eq 0) {
        throw "No native Xbox main fastfile found under: $NativeDirectory"
    }

    $nativeFastFile = $candidates[0].FullName
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$dumpRoot = Join-Path $OutputRoot 'dump'
$log = Join-Path $OutputRoot 'oat_native_image_dump.log'

Write-Host '[NUCLEAR-4J] ================================================================'
Write-Host '[NUCLEAR-4J] NATIVE XBOX IMAGE PAYLOAD MASS CENSUS'
Write-Host '[NUCLEAR-4J] ================================================================'
Write-Host "[NUCLEAR-4J] Native FF    : $nativeFastFile"
Write-Host "[NUCLEAR-4J] FF disk bytes: $((Get-Item -LiteralPath $nativeFastFile).Length)"
Write-Host "[NUCLEAR-4J] OAT Unlinker : $unlinker"
Write-Host "[NUCLEAR-4J] Output       : $OutputRoot"
Write-Host ''
Write-Host '[NUCLEAR-4J] Oracle-only diagnostic: native image payloads are dumped for mass/identity study.'
Write-Host '[NUCLEAR-4J] Nothing from this dump is copied into a converter output candidate.'
Write-Host ''

Push-Location $NativeDirectory
try {
    $arguments = @(
        '--verbose',
        '--no-color',
        '--include-assets', 'image',
        '--image-format', 'IWI',
        '--output-folder', $dumpRoot,
        $nativeFastFile
    )

    & $unlinker @arguments 2>&1 | Tee-Object -FilePath $log
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

$iwiFiles = @()
if (Test-Path -LiteralPath $dumpRoot -PathType Container) {
    $iwiFiles = @(Get-ChildItem -LiteralPath $dumpRoot -Filter '*.iwi' -File -Recurse)
}

$totalBytes = ($iwiFiles | Measure-Object -Property Length -Sum).Sum
if ($null -eq $totalBytes) {
    $totalBytes = 0
}

$generated = @($iwiFiles | Where-Object {
    $stem = [IO.Path]::GetFileNameWithoutExtension($_.Name)
    $stem.StartsWith('*') -or
    $stem.StartsWith('$') -or
    $stem.StartsWith(',')
})
$wj = @($iwiFiles | Where-Object {
    [IO.Path]::GetFileNameWithoutExtension($_.Name) -match '(?i)_wj$'
})
$ordinary = @($iwiFiles | Where-Object {
    -not $generated.Contains($_) -and -not $wj.Contains($_)
})

$generatedBytes = ($generated | Measure-Object -Property Length -Sum).Sum
$wjBytes = ($wj | Measure-Object -Property Length -Sum).Sum
$ordinaryBytes = ($ordinary | Measure-Object -Property Length -Sum).Sum
if ($null -eq $generatedBytes) { $generatedBytes = 0 }
if ($null -eq $wjBytes) { $wjBytes = 0 }
if ($null -eq $ordinaryBytes) { $ordinaryBytes = 0 }

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4J.NativeImageMass.v1'
    nativeFastFile = $nativeFastFile
    nativeFastFileBytes = (Get-Item -LiteralPath $nativeFastFile).Length
    oatExitCode = $code
    dumpedImages = $iwiFiles.Count
    dumpedImageBytes = [int64]$totalBytes
    generatedOrFunctionImages = $generated.Count
    generatedOrFunctionBytes = [int64]$generatedBytes
    xenonWjImages = $wj.Count
    xenonWjBytes = [int64]$wjBytes
    ordinaryImages = $ordinary.Count
    ordinaryBytes = [int64]$ordinaryBytes
    dumpRoot = $dumpRoot
    logFile = $log
    largest = @($iwiFiles | Sort-Object Length -Descending | Select-Object -First 100 | ForEach-Object {
        [ordered]@{
            relativePath = [IO.Path]::GetRelativePath($dumpRoot, $_.FullName)
            bytes = $_.Length
        }
    })
}

$manifestPath = Join-Path $OutputRoot 'nuclear_4j_native_image_mass.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host ''
Write-Host '[NUCLEAR-4J] ================= NATIVE IMAGE MASS ============================='
Write-Host "[NUCLEAR-4J] OAT exit code        : $code"
Write-Host "[NUCLEAR-4J] Dumped images        : $($iwiFiles.Count)"
Write-Host "[NUCLEAR-4J] Dumped IWI bytes     : $([int64]$totalBytes)"
Write-Host "[NUCLEAR-4J] Generated/function   : $($generated.Count) images / $([int64]$generatedBytes) B"
Write-Host "[NUCLEAR-4J] _wj identities       : $($wj.Count) images / $([int64]$wjBytes) B"
Write-Host "[NUCLEAR-4J] Ordinary identities  : $($ordinary.Count) images / $([int64]$ordinaryBytes) B"
Write-Host "[NUCLEAR-4J] Manifest             : $manifestPath"

if ($iwiFiles.Count -gt 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4J] Largest native images:'
    $iwiFiles |
        Sort-Object Length -Descending |
        Select-Object -First 40 |
        ForEach-Object {
            Write-Host ("  {0,12:N0} B  {1}" -f $_.Length, [IO.Path]::GetRelativePath($dumpRoot, $_.FullName))
        }
}

Write-Host ''
Write-Host '[NUCLEAR-4J] OAT log tail:'
Get-Content -LiteralPath $log -Tail 100
Write-Host '[NUCLEAR-4J] ================================================================='

if ($code -ne 0) {
    Write-Host '[NUCLEAR-4J] NATIVE IMAGE MASS CENSUS: FAILED' -ForegroundColor Yellow
    exit $code
}

if ($iwiFiles.Count -eq 0) {
    Write-Host '[NUCLEAR-4J] NATIVE IMAGE MASS CENSUS: ZERO IMAGES' -ForegroundColor Yellow
    exit 4
}

Write-Host '[NUCLEAR-4J] NATIVE IMAGE MASS CENSUS: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4J] Use this mass result to decide whether images remain the highest-profit 4F target.'
