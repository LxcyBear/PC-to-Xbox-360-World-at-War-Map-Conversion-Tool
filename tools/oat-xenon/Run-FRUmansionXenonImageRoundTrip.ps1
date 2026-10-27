param(
    [string]$MapName = 'nazi_zombie_FRUmansion',
    [string]$NativeDumpRoot = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$linker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Linker.exe'
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($NativeDumpRoot)) {
    $NativeDumpRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4j_native_image_mass\dump'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4l_xenon_image_roundtrip'
}

if (-not (Test-Path -LiteralPath $linker -PathType Leaf)) {
    throw "Pinned Xenon OAT Linker not found: $linker"
}
if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) {
    throw "Pinned Xenon OAT Unlinker not found: $unlinker"
}
if (-not (Test-Path -LiteralPath $NativeDumpRoot -PathType Container)) {
    throw "Stage 4J native image dump not found: $NativeDumpRoot"
}

function Get-RelativePathCompat {
    param([string]$BasePath, [string]$TargetPath)

    $baseFull = [IO.Path]::GetFullPath($BasePath)
    $targetFull = [IO.Path]::GetFullPath($TargetPath)
    if (-not $baseFull.EndsWith('\')) { $baseFull += '\' }

    if ($targetFull.StartsWith($baseFull, [StringComparison]::OrdinalIgnoreCase)) {
        return $targetFull.Substring($baseFull.Length)
    }
    return $targetFull
}

function Read-IwiHeader {
    param([string]$Path)

    $bytes = New-Object byte[] 28
    $stream = [IO.File]::OpenRead($Path)
    try {
        $read = $stream.Read($bytes, 0, $bytes.Length)
    }
    finally {
        $stream.Dispose()
    }

    if ($read -lt 28 -or
        $bytes[0] -ne [byte][char]'I' -or
        $bytes[1] -ne [byte][char]'W' -or
        $bytes[2] -ne [byte][char]'i') {
        return $null
    }

    return [pscustomobject]@{
        Version = [int]$bytes[3]
        Format = [int]$bytes[4]
        Flags = [int]$bytes[5]
        Width = [BitConverter]::ToUInt16($bytes, 6)
        Height = [BitConverter]::ToUInt16($bytes, 8)
        Depth = [BitConverter]::ToUInt16($bytes, 10)
    }
}

function Get-IwiPayloadSha256 {
    param([string]$Path)

    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 28) {
        throw "IWI too small: $Path"
    }

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($bytes, 28, $bytes.Length - 28)
        return ([BitConverter]::ToString($hash)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Normalize-AssetNameFromDump {
    param([string]$Root, [System.IO.FileInfo]$File)

    $relative = (Get-RelativePathCompat -BasePath $Root -TargetPath $File.FullName).Replace('\', '/')
    if ($relative.StartsWith('images/', [StringComparison]::OrdinalIgnoreCase)) {
        $relative = $relative.Substring(7)
    }
    if ($relative.EndsWith('.iwi', [StringComparison]::OrdinalIgnoreCase)) {
        $relative = $relative.Substring(0, $relative.Length - 4)
    }
    return $relative
}

$selected = New-Object 'System.Collections.Generic.List[object]'

foreach ($file in @(Get-ChildItem -LiteralPath $NativeDumpRoot -Filter '*.iwi' -File -Recurse)) {
    $header = Read-IwiHeader -Path $file.FullName
    if ($null -eq $header) { continue }
    if ($header.Version -ne 6 -or $header.Format -ne 0x0D) { continue }

    $assetName = Normalize-AssetNameFromDump -Root $NativeDumpRoot -File $file
    $selected.Add([pscustomobject]@{
        AssetName = $assetName
        SourcePath = $file.FullName
        Bytes = [int64]$file.Length
        Width = [int]$header.Width
        Height = [int]$header.Height
        Depth = [int]$header.Depth
        Flags = [int]$header.Flags
        PayloadSha256 = Get-IwiPayloadSha256 -Path $file.FullName
    })
}

$selectedArray = [object[]]$selected.ToArray()
if ($selectedArray.Count -eq 0) {
    throw 'Stage 4L found no IWI v6 fmt=0x0D images in the Stage 4J dump.'
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

$work = Join-Path $OutputRoot 'workspace'
$rawImages = Join-Path $work 'raw\images'
$zoneSource = Join-Path $work 'zone_source'
$linkOut = Join-Path $OutputRoot 'linked'
$roundtripRoot = Join-Path $OutputRoot 'roundtrip_dump'
$linkLog = Join-Path $OutputRoot 'oat_linker.log'
$unlinkLog = Join-Path $OutputRoot 'oat_unlinker.log'
$project = 'nuclear_4l_bc3'
$zoneFile = Join-Path $zoneSource ($project + '.zone')

New-Item -ItemType Directory -Force -Path $rawImages | Out-Null
New-Item -ItemType Directory -Force -Path $zoneSource | Out-Null
New-Item -ItemType Directory -Force -Path $linkOut | Out-Null
New-Item -ItemType Directory -Force -Path $roundtripRoot | Out-Null

$zoneLines = New-Object 'System.Collections.Generic.List[string]'
# OAT zone definitions use the public game id plus an explicit platform.
# T4 + xbox360 resolves internally to the T4Xenon game implementation.
$zoneLines.Add('>game,T4')
$zoneLines.Add('>platform,xbox360')
$zoneLines.Add(('>name,{0}' -f $project))
$zoneLines.Add('')

foreach ($item in ($selectedArray | Sort-Object AssetName)) {
    $assetRelative = $item.AssetName.Replace('/', '\') + '.iwi'
    $destination = Join-Path $rawImages $assetRelative
    $destinationDir = Split-Path -Parent $destination
    New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null
    Copy-Item -LiteralPath $item.SourcePath -Destination $destination -Force
    $zoneLines.Add(('image,{0}' -f $item.AssetName))
}

[IO.File]::WriteAllLines($zoneFile, $zoneLines, [Text.UTF8Encoding]::new($false))

$inputBytes = ($selectedArray | Measure-Object -Property Bytes -Sum).Sum
if ($null -eq $inputBytes) { $inputBytes = 0 }

Write-Host '[NUCLEAR-4L] ================================================================'
Write-Host '[NUCLEAR-4L] REAL T4 XENON IMAGE ENCODER ROUND-TRIP - IWI v6 fmt=0x0D'
Write-Host '[NUCLEAR-4L] ================================================================'
Write-Host "[NUCLEAR-4L] Input images : $($selectedArray.Count)"
Write-Host "[NUCLEAR-4L] Input bytes  : $([int64]$inputBytes)"
Write-Host "[NUCLEAR-4L] OAT Linker   : $linker"
Write-Host "[NUCLEAR-4L] Workspace    : $work"
Write-Host ''
Write-Host '[NUCLEAR-4L] Oracle use is validation-only: native-dumped IWIs prove the encoder.'
Write-Host '[NUCLEAR-4L] No 4L asset payload is a production converter source.'
Write-Host ''

$linkArgs = @(
    '--verbose',
    '--no-color',
    '--base-folder', $work,
    '--source-search-path', $zoneSource,
    '--asset-search-path', (Join-Path $work 'raw'),
    '--output-folder', $linkOut,
    $project
)

Push-Location $work
try {
    & $linker @linkArgs 2>&1 | Tee-Object -FilePath $linkLog
    $linkCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($linkCode -ne 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4L] LINK FAILED. Log tail:' -ForegroundColor Yellow
    Get-Content -LiteralPath $linkLog -Tail 120
    exit $linkCode
}

$linkedCandidates = @(Get-ChildItem -LiteralPath $linkOut -Filter '*.ff' -File -Recurse)
if ($linkedCandidates.Count -ne 1) {
    throw "Expected exactly one linked image-only FF under '$linkOut', found $($linkedCandidates.Count)."
}
$linkedFastFile = $linkedCandidates[0].FullName

Write-Host ''
Write-Host "[NUCLEAR-4L] Linked FF     : $linkedFastFile"
Write-Host "[NUCLEAR-4L] Linked bytes  : $($linkedCandidates[0].Length)"
Write-Host '[NUCLEAR-4L] Reopening native FF and dumping images back to IWI...'
Write-Host ''

$unlinkArgs = @(
    '--verbose',
    '--no-color',
    '--include-assets', 'image',
    '--image-format', 'IWI',
    '--output-folder', $roundtripRoot,
    $linkedFastFile
)

Push-Location (Split-Path -Parent $linkedFastFile)
try {
    & $unlinker @unlinkArgs 2>&1 | Tee-Object -FilePath $unlinkLog
    $unlinkCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($unlinkCode -ne 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4L] ROUND-TRIP UNLINK FAILED. Log tail:' -ForegroundColor Yellow
    Get-Content -LiteralPath $unlinkLog -Tail 120
    exit $unlinkCode
}

$roundtripFiles = @(Get-ChildItem -LiteralPath $roundtripRoot -Filter '*.iwi' -File -Recurse)
$roundtripByName = @{}
foreach ($file in $roundtripFiles) {
    $name = Normalize-AssetNameFromDump -Root $roundtripRoot -File $file
    $roundtripByName[$name.ToLowerInvariant()] = $file
}

$results = New-Object 'System.Collections.Generic.List[object]'
$missing = 0
$headerMismatch = 0
$payloadMismatch = 0
$payloadExact = 0

foreach ($item in $selectedArray) {
    $key = $item.AssetName.ToLowerInvariant()
    if (-not $roundtripByName.ContainsKey($key)) {
        $missing++
        $results.Add([pscustomobject]@{
            AssetName = $item.AssetName
            Status = 'missing'
            InputBytes = $item.Bytes
            OutputBytes = 0
            PayloadExact = $false
        })
        continue
    }

    $outFile = $roundtripByName[$key]
    $outHeader = Read-IwiHeader -Path $outFile.FullName
    $sameHeader =
        $null -ne $outHeader -and
        $outHeader.Version -eq 6 -and
        $outHeader.Format -eq 0x0D -and
        $outHeader.Width -eq $item.Width -and
        $outHeader.Height -eq $item.Height -and
        $outHeader.Depth -eq $item.Depth

    if (-not $sameHeader) { $headerMismatch++ }

    $outPayloadSha = Get-IwiPayloadSha256 -Path $outFile.FullName
    $samePayload = $outPayloadSha -eq $item.PayloadSha256
    if ($samePayload) { $payloadExact++ } else { $payloadMismatch++ }

    $status =
        if ($sameHeader -and $samePayload) { 'exact' }
        elseif (-not $sameHeader) { 'header-mismatch' }
        else { 'payload-mismatch' }

    $outputFlags = -1
    if ($null -ne $outHeader) {
        $outputFlags = [int]$outHeader.Flags
    }

    $results.Add([pscustomobject]@{
        AssetName = $item.AssetName
        Status = $status
        InputBytes = $item.Bytes
        OutputBytes = [int64]$outFile.Length
        InputPayloadSha256 = $item.PayloadSha256
        OutputPayloadSha256 = $outPayloadSha
        PayloadExact = $samePayload
        InputFlags = $item.Flags
        OutputFlags = $outputFlags
        Width = $item.Width
        Height = $item.Height
        Depth = $item.Depth
    })
}

$resultsArray = [object[]]$results.ToArray()
$proofPass =
    $payloadExact -eq $selectedArray.Count -and
    $payloadMismatch -eq 0 -and
    $headerMismatch -eq 0 -and
    $missing -eq 0

$manifestPath = Join-Path $OutputRoot 'nuclear_4l_xenon_image_roundtrip.json'
$manifest = [ordered]@{
    format = 'WaW360.Nuclear4L.XenonImageRoundTrip.v1'
    mapName = $MapName
    iwiVersion = 6
    iwiFormat = 13
    inputImages = $selectedArray.Count
    inputIwiBytes = [int64]$inputBytes
    linkedFastFile = $linkedFastFile
    linkedFastFileBytes = [int64]$linkedCandidates[0].Length
    roundtripImages = $roundtripFiles.Count
    payloadExactImages = $payloadExact
    payloadMismatchImages = $payloadMismatch
    headerMismatchImages = $headerMismatch
    missingImages = $missing
    encoderProofPass = $proofPass
    notes = @(
        'Stage 4L validates OAT T4Xenon image encoding using oracle-derived test vectors only.',
        'Production conversion must source IWI/DDS pixels from the user-owned PC map/game asset set, never from the native oracle dump.',
        'XMAs remain external under sounds\ and are not part of this stage.',
        'High-resolution streamed texture payloads remain candidates for the shared highmip\ package directory when the source asset is actually streamed.'
    )
    results = $resultsArray
}

$manifest | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host ''
Write-Host '[NUCLEAR-4L] ================= ENCODER PROOF RESULT =========================='
Write-Host "[NUCLEAR-4L] Input images       : $($selectedArray.Count)"
Write-Host "[NUCLEAR-4L] Roundtrip images   : $($roundtripFiles.Count)"
Write-Host "[NUCLEAR-4L] Payload-exact      : $payloadExact"
Write-Host "[NUCLEAR-4L] Payload mismatch   : $payloadMismatch"
Write-Host "[NUCLEAR-4L] Header mismatch    : $headerMismatch"
Write-Host "[NUCLEAR-4L] Missing            : $missing"
Write-Host "[NUCLEAR-4L] Linked FF bytes    : $($linkedCandidates[0].Length)"
Write-Host "[NUCLEAR-4L] Manifest           : $manifestPath"
Write-Host '[NUCLEAR-4L] ================================================================='

if (-not $proofPass) {
    Write-Host '[NUCLEAR-4L] ENCODER PROOF: FAILED' -ForegroundColor Yellow
    Write-Host '[NUCLEAR-4L] First non-exact assets:'
    $resultsArray |
        Where-Object { $_.Status -ne 'exact' } |
        Select-Object -First 30 |
        ForEach-Object {
            Write-Host ("  {0,-18} {1}" -f $_.Status, $_.AssetName)
        }
    exit 5
}

Write-Host '[NUCLEAR-4L] ENCODER PROOF: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4L] OAT can losslessly carry the dominant v6 fmt=0x0D corpus through native T4 Xenon FF encoding.'
Write-Host '[NUCLEAR-4L] Next: expose this OAT encoder as the production PC-IWI/DDS image bridge and integrate image payload mass into 4F.'
