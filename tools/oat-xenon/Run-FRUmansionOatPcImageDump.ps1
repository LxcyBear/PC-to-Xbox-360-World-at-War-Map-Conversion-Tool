param(
    [string]$PcMapsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps',
    [string]$MapName = 'nazi_zombie_FRUmansion',
    [string]$PcFastFile = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4h_oat_pc_image_dump'
}

if ([string]::IsNullOrWhiteSpace($PcFastFile)) {
    $mapDir = Join-Path $PcMapsRoot $MapName
    $candidates = @(
        (Join-Path $mapDir 'nazi_zombie_frumansion.ff'),
        (Join-Path $mapDir 'nazi_zombie_FRUmansion.ff'),
        (Join-Path $mapDir ($MapName + '.ff'))
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }

    if ($candidates.Count -eq 0) {
        $candidates = @(Get-ChildItem -LiteralPath $mapDir -Filter '*.ff' -File |
            Where-Object {
                $_.Name -notmatch '(?i)_load[.]ff$' -and
                $_.Name -notmatch '(?i)_patch[.]ff$'
            } |
            Sort-Object Name |
            ForEach-Object { $_.FullName })
    }

    if ($candidates.Count -eq 0) {
        throw "Could not identify the PC main fastfile under: $mapDir"
    }

    $PcFastFile = $candidates[0]
}

if (-not (Test-Path -LiteralPath $PcFastFile -PathType Leaf)) {
    throw "PC fastfile not found: $PcFastFile"
}
if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) {
    throw "OAT Unlinker not found: $unlinker"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$dumpRoot = Join-Path $OutputRoot 'dump'
$log = Join-Path $OutputRoot 'oat_pc_image_dump.log'

Write-Host '[NUCLEAR-4H] ================================================================'
Write-Host '[NUCLEAR-4H] OAT PC FASTFILE IMAGE DUMP PROBE'
Write-Host '[NUCLEAR-4H] ================================================================'
Write-Host "[NUCLEAR-4H] PC fastfile : $PcFastFile"
Write-Host "[NUCLEAR-4H] OAT Unlinker: $unlinker"
Write-Host "[NUCLEAR-4H] Output      : $OutputRoot"
Write-Host ''
Write-Host '[NUCLEAR-4H] Asking OAT to resolve/dump GfxImage assets directly from the PC zone.'
Write-Host '[NUCLEAR-4H] Output format is IWI v6 so the result can feed the existing Xenon image path.'
Write-Host ''

$searchPaths = @(
    (Split-Path -Parent $PcFastFile),
    (Join-Path (Split-Path -Parent $PcFastFile) 'images')
) | Where-Object { Test-Path -LiteralPath $_ }

$searchArg = ($searchPaths -join ';')

Push-Location (Split-Path -Parent $PcFastFile)
try {
    $args = @(
        '--verbose',
        '--no-color',
        '--include-assets', 'image',
        '--image-format', 'IWI',
        '--output-folder', $dumpRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($searchArg)) {
        $args += @('--search-path', $searchArg)
    }

    $args += $PcFastFile

    & $unlinker @args 2>&1 | Tee-Object -FilePath $log
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
if ($null -eq $totalBytes) { $totalBytes = 0 }

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4H.OatPcImageDump.v1'
    pcFastFile = $PcFastFile
    oatExitCode = $code
    dumpedIwiFiles = $iwiFiles.Count
    dumpedIwiBytes = [int64]$totalBytes
    dumpRoot = $dumpRoot
    logFile = $log
    files = @($iwiFiles | Sort-Object Length -Descending | ForEach-Object {
        [ordered]@{
            name = $_.Name
            relativePath = [IO.Path]::GetRelativePath($dumpRoot, $_.FullName)
            bytes = $_.Length
        }
    })
}

$manifestPath = Join-Path $OutputRoot 'nuclear_4h_oat_pc_image_dump.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host ''
Write-Host '[NUCLEAR-4H] ================= IMAGE DUMP RESULT ============================='
Write-Host "[NUCLEAR-4H] OAT exit code : $code"
Write-Host "[NUCLEAR-4H] Dumped IWIs   : $($iwiFiles.Count)"
Write-Host "[NUCLEAR-4H] Dumped bytes  : $([int64]$totalBytes)"
Write-Host "[NUCLEAR-4H] Manifest      : $manifestPath"

if ($iwiFiles.Count -gt 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4H] Largest dumped images:'
    $iwiFiles |
        Sort-Object Length -Descending |
        Select-Object -First 30 |
        ForEach-Object {
            Write-Host ("  {0,12:N0} B  {1}" -f $_.Length, [IO.Path]::GetRelativePath($dumpRoot, $_.FullName))
        }
}

Write-Host ''
Write-Host '[NUCLEAR-4H] OAT log tail:'
Get-Content -LiteralPath $log -Tail 100
Write-Host '[NUCLEAR-4H] ================================================================='

if ($code -ne 0) {
    Write-Host '[NUCLEAR-4H] OAT PC IMAGE DUMP: FAILED' -ForegroundColor Yellow
    exit $code
}

if ($iwiFiles.Count -eq 0) {
    Write-Host '[NUCLEAR-4H] OAT PC IMAGE DUMP: ZERO IMAGES' -ForegroundColor Yellow
    Write-Host '[NUCLEAR-4H] That means the PC zone references external/common image stores; next step is adding the retail WaW asset search path.'
    exit 4
}

Write-Host '[NUCLEAR-4H] OAT PC IMAGE DUMP: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4H] Next: match these dumped PC images to the 928 native identities and feed them through OAT Xenon encoding.'
