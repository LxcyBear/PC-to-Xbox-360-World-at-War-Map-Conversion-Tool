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
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4m_xenon_remaining_image_roundtrip'
}

if (-not (Test-Path -LiteralPath $linker -PathType Leaf)) { throw "Pinned Xenon OAT Linker not found: $linker" }
if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) { throw "Pinned Xenon OAT Unlinker not found: $unlinker" }
if (-not (Test-Path -LiteralPath $NativeDumpRoot -PathType Container)) { throw "Stage 4J native image dump not found: $NativeDumpRoot" }

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
    try { $read = $stream.Read($bytes, 0, $bytes.Length) }
    finally { $stream.Dispose() }

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
    if ($bytes.Length -lt 28) { throw "IWI too small: $Path" }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($bytes, 28, $bytes.Length - 28)
        return ([BitConverter]::ToString($hash)).Replace('-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
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
    if ($header.Version -ne 6) { continue }
    if ($header.Format -eq 0x0D) { continue }

    $assetName = Normalize-AssetNameFromDump -Root $NativeDumpRoot -File $file
    $selected.Add([pscustomobject]@{
        AssetName = $assetName
        SourcePath = $file.FullName
        Bytes = [int64]$file.Length
        Width = [int]$header.Width
        Height = [int]$header.Height
        Depth = [int]$header.Depth
        Flags = [int]$header.Flags
        Format = [int]$header.Format
        PayloadSha256 = Get-IwiPayloadSha256 -Path $file.FullName
    })
}

$selectedArray = [object[]]$selected.ToArray()
if ($selectedArray.Count -eq 0) {
    throw 'Stage 4M found no remaining IWI v6 images after excluding fmt=0x0D.'
}

if (Test-Path -LiteralPath $OutputRoot) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }

$work = Join-Path $OutputRoot 'workspace'
$rawImages = Join-Path $work 'raw\images'
$zoneSource = Join-Path $work 'zone_source'
$linkOut = Join-Path $OutputRoot 'linked'
$roundtripRoot = Join-Path $OutputRoot 'roundtrip_dump'
$linkLog = Join-Path $OutputRoot 'oat_linker.log'
$unlinkLog = Join-Path $OutputRoot 'oat_unlinker.log'
$project = 'nuclear_4m_remaining_images'
$zoneFile = Join-Path $zoneSource ($project + '.zone')

New-Item -ItemType Directory -Force -Path $rawImages | Out-Null
New-Item -ItemType Directory -Force -Path $zoneSource | Out-Null
New-Item -ItemType Directory -Force -Path $linkOut | Out-Null
New-Item -ItemType Directory -Force -Path $roundtripRoot | Out-Null

$zoneLines = New-Object 'System.Collections.Generic.List[string]'
$zoneLines.Add('>game,T4')
$zoneLines.Add('>platform,xbox360')
$zoneLines.Add(('>name,{0}' -f $project))
$zoneLines.Add('')

foreach ($item in ($selectedArray | Sort-Object AssetName)) {
    $assetRelative = $item.AssetName.Replace('/', '\') + '.iwi'
    $destination = Join-Path $rawImages $assetRelative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $item.SourcePath -Destination $destination -Force

    # Quote asset names so commas or other parser-significant punctuation remain part of the identity.
    $zoneLines.Add(('image,"{0}"' -f $item.AssetName.Replace('"', '')))
}

[IO.File]::WriteAllLines($zoneFile, $zoneLines, [Text.UTF8Encoding]::new($false))

$inputBytes = ($selectedArray | Measure-Object -Property Bytes -Sum).Sum
if ($null -eq $inputBytes) { $inputBytes = 0 }

Write-Host '[NUCLEAR-4M] ================================================================'
Write-Host '[NUCLEAR-4M] REMAINING T4 XENON IMAGE FORMAT ROUND-TRIP'
Write-Host '[NUCLEAR-4M] ================================================================'
Write-Host "[NUCLEAR-4M] Input images : $($selectedArray.Count)"
Write-Host "[NUCLEAR-4M] Input bytes  : $([int64]$inputBytes)"
Write-Host '[NUCLEAR-4M] Excluded     : v6 fmt=0x0D (already proven 378/378 exact by Stage 4L)'
Write-Host ''
Write-Host '[NUCLEAR-4M] Input format mass:'
foreach ($g in @($selectedArray | Group-Object Format | Sort-Object { [int]$_.Name })) {
    $bytes = ($g.Group | Measure-Object -Property Bytes -Sum).Sum
    if ($null -eq $bytes) { $bytes = 0 }
    Write-Host ("  fmt=0x{0:X2} images={1,4} bytes={2,12:N0}" -f [int]$g.Name, $g.Count, [int64]$bytes)
}
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
finally { Pop-Location }

if ($linkCode -ne 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4M] LINK FAILED. Log tail:' -ForegroundColor Yellow
    Get-Content -LiteralPath $linkLog -Tail 120
    exit $linkCode
}

$linkedCandidates = @(Get-ChildItem -LiteralPath $linkOut -Filter '*.ff' -File -Recurse)
if ($linkedCandidates.Count -ne 1) {
    throw "Expected exactly one linked image-only FF under '$linkOut', found $($linkedCandidates.Count)."
}
$linkedFastFile = $linkedCandidates[0].FullName

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
finally { Pop-Location }

if ($unlinkCode -ne 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4M] ROUND-TRIP UNLINK FAILED. Log tail:' -ForegroundColor Yellow
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

foreach ($item in $selectedArray) {
    $key = $item.AssetName.ToLowerInvariant()
    if (-not $roundtripByName.ContainsKey($key)) {
        $results.Add([pscustomobject]@{
            AssetName = $item.AssetName
            InputFormat = $item.Format
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
        $outHeader.Format -eq $item.Format -and
        $outHeader.Width -eq $item.Width -and
        $outHeader.Height -eq $item.Height -and
        $outHeader.Depth -eq $item.Depth

    $outPayloadSha = Get-IwiPayloadSha256 -Path $outFile.FullName
    $samePayload = $outPayloadSha -eq $item.PayloadSha256

    $status =
        if ($sameHeader -and $samePayload) { 'exact' }
        elseif (-not $sameHeader) { 'header-mismatch' }
        else { 'payload-mismatch' }

    $results.Add([pscustomobject]@{
        AssetName = $item.AssetName
        InputFormat = $item.Format
        Status = $status
        InputBytes = $item.Bytes
        OutputBytes = [int64]$outFile.Length
        PayloadExact = $samePayload
        InputPayloadSha256 = $item.PayloadSha256
        OutputPayloadSha256 = $outPayloadSha
    })
}

$resultsArray = [object[]]$results.ToArray()
$formatResults = @($resultsArray |
    Group-Object InputFormat |
    ForEach-Object {
        $items = [object[]]$_.Group
        $exact = @($items | Where-Object { $_.Status -eq 'exact' }).Count
        $payloadMismatch = @($items | Where-Object { $_.Status -eq 'payload-mismatch' }).Count
        $headerMismatch = @($items | Where-Object { $_.Status -eq 'header-mismatch' }).Count
        $missing = @($items | Where-Object { $_.Status -eq 'missing' }).Count
        $bytes = ($items | Measure-Object -Property InputBytes -Sum).Sum
        if ($null -eq $bytes) { $bytes = 0 }
        [pscustomobject]@{
            Format = [int]$_.Name
            Images = $items.Count
            InputBytes = [int64]$bytes
            Exact = $exact
            PayloadMismatch = $payloadMismatch
            HeaderMismatch = $headerMismatch
            Missing = $missing
            Pass = ($exact -eq $items.Count)
        }
    } |
    Sort-Object InputBytes -Descending)

$totalExact = @($resultsArray | Where-Object { $_.Status -eq 'exact' }).Count
$totalPayloadMismatch = @($resultsArray | Where-Object { $_.Status -eq 'payload-mismatch' }).Count
$totalHeaderMismatch = @($resultsArray | Where-Object { $_.Status -eq 'header-mismatch' }).Count
$totalMissing = @($resultsArray | Where-Object { $_.Status -eq 'missing' }).Count
$proofPass = $totalExact -eq $selectedArray.Count

$manifestPath = Join-Path $OutputRoot 'nuclear_4m_xenon_remaining_image_roundtrip.json'
$manifest = [ordered]@{
    format = 'WaW360.Nuclear4M.XenonRemainingImageRoundTrip.v1'
    mapName = $MapName
    inputImages = $selectedArray.Count
    inputIwiBytes = [int64]$inputBytes
    excludedFormat = 13
    linkedFastFile = $linkedFastFile
    linkedFastFileBytes = [int64]$linkedCandidates[0].Length
    roundtripImages = $roundtripFiles.Count
    exactImages = $totalExact
    payloadMismatchImages = $totalPayloadMismatch
    headerMismatchImages = $totalHeaderMismatch
    missingImages = $totalMissing
    encoderProofPass = $proofPass
    formatResults = $formatResults
    notes = @(
        'Stage 4M validates every remaining IWI v6 format present in the native oracle dump after Stage 4L proved fmt=0x0D.',
        'Oracle-derived IWIs are validation vectors only and are never production converter source payloads.',
        'Production image conversion must resolve pixels from user-owned PC IWI/DDS sources.',
        'XMAs remain external under sounds\; streamed high-resolution texture data belongs under highmip\ when applicable.'
    )
}

$manifest | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host ''
Write-Host '[NUCLEAR-4M] ================= FORMAT PROOF RESULT ==========================='
foreach ($g in $formatResults) {
    Write-Host ("[NUCLEAR-4M] fmt=0x{0:X2}: images={1}; exact={2}; payloadMismatch={3}; headerMismatch={4}; missing={5}; bytes={6:N0}; pass={7}" -f
        $g.Format, $g.Images, $g.Exact, $g.PayloadMismatch, $g.HeaderMismatch, $g.Missing, $g.InputBytes, $g.Pass)
}
Write-Host ''
Write-Host "[NUCLEAR-4M] Total input         : $($selectedArray.Count)"
Write-Host "[NUCLEAR-4M] Total exact         : $totalExact"
Write-Host "[NUCLEAR-4M] Payload mismatch    : $totalPayloadMismatch"
Write-Host "[NUCLEAR-4M] Header mismatch     : $totalHeaderMismatch"
Write-Host "[NUCLEAR-4M] Missing             : $totalMissing"
Write-Host "[NUCLEAR-4M] Linked FF bytes     : $($linkedCandidates[0].Length)"
Write-Host "[NUCLEAR-4M] Manifest            : $manifestPath"
Write-Host '[NUCLEAR-4M] ================================================================='

if (-not $proofPass) {
    Write-Host '[NUCLEAR-4M] REMAINING FORMAT PROOF: FAILED' -ForegroundColor Yellow
    $resultsArray |
        Where-Object { $_.Status -ne 'exact' } |
        Select-Object -First 40 |
        ForEach-Object { Write-Host ("  fmt=0x{0:X2} {1,-18} {2}" -f $_.InputFormat, $_.Status, $_.AssetName) }
    exit 5
}

Write-Host '[NUCLEAR-4M] REMAINING FORMAT PROOF: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4M] Combined with Stage 4L, the ordinary native IWI v6 image-format corpus is encoder-proven.'
Write-Host '[NUCLEAR-4M] Next: production PC asset-resolution bridge and integration into the full block-aware map emission path.'
