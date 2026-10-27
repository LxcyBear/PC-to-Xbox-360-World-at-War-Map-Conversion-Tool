param(
    [string]$MapName = 'nazi_zombie_FRUmansion',
    [string]$OutputRoot = '',
    [string]$NativeIdentityManifest = '',
    [string]$NativeDumpRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4k_native_image_closure'
}

if ([string]::IsNullOrWhiteSpace($NativeIdentityManifest)) {
    $NativeIdentityManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_root_binding\native_identity\nazi_zombie_FRUmansion_native_identity_manifest.json'
}

if ([string]::IsNullOrWhiteSpace($NativeDumpRoot)) {
    $NativeDumpRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4j_native_image_mass\dump'
}

if (-not (Test-Path -LiteralPath $NativeIdentityManifest -PathType Leaf)) {
    throw "Native identity manifest not found: $NativeIdentityManifest"
}
if (-not (Test-Path -LiteralPath $NativeDumpRoot -PathType Container)) {
    throw "Stage 4J native image dump not found: $NativeDumpRoot"
}

function Get-RelativePathCompat {
    param(
        [string]$BasePath,
        [string]$TargetPath
    )

    $baseFull = [IO.Path]::GetFullPath($BasePath)
    $targetFull = [IO.Path]::GetFullPath($TargetPath)

    if (-not $baseFull.EndsWith('\')) {
        $baseFull += '\'
    }

    if ($targetFull.StartsWith($baseFull, [StringComparison]::OrdinalIgnoreCase)) {
        return $targetFull.Substring($baseFull.Length)
    }

    return $targetFull
}

function Normalize-NativeImageName {
    param([string]$Name)

    $value = $Name.Replace('\', '/')
    if ($value.EndsWith('_wj', [StringComparison]::OrdinalIgnoreCase)) {
        return $value.Substring(0, $value.Length - 3)
    }

    return $value
}

function Normalize-DumpedImageName {
    param(
        [string]$Root,
        [System.IO.FileInfo]$File
    )

    $relative = (Get-RelativePathCompat -BasePath $Root -TargetPath $File.FullName).Replace('\', '/')
    if ($relative.StartsWith('images/', [StringComparison]::OrdinalIgnoreCase)) {
        $relative = $relative.Substring(7)
    }
    if ($relative.EndsWith('.iwi', [StringComparison]::OrdinalIgnoreCase)) {
        $relative = $relative.Substring(0, $relative.Length - 4)
    }

    return $relative
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

    if ($read -lt 12 -or
        $bytes[0] -ne [byte][char]'I' -or
        $bytes[1] -ne [byte][char]'W' -or
        $bytes[2] -ne [byte][char]'i') {
        return [pscustomobject]@{
            Valid = $false
            Version = 0
            Format = 0
            Flags = 0
            Width = 0
            Height = 0
            Depth = 0
        }
    }

    return [pscustomobject]@{
        Valid = $true
        Version = [int]$bytes[3]
        Format = [int]$bytes[4]
        Flags = [int]$bytes[5]
        Width = [BitConverter]::ToUInt16($bytes, 6)
        Height = [BitConverter]::ToUInt16($bytes, 8)
        Depth = [BitConverter]::ToUInt16($bytes, 10)
    }
}

$identity = Get-Content -LiteralPath $NativeIdentityManifest -Raw | ConvertFrom-Json
if ([string]$identity.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or -not [bool]$identity.typeSequenceVerified) {
    throw 'Stage 4K requires a verified native identity manifest.'
}

$nativeImages = @($identity.entries |
    Where-Object {
        ([string]$_.TypeName -ieq 'image') -or
        ([string]$_.OatTypeName -ieq 'image')
    } |
    ForEach-Object { [string]$_.Name })

$dumpFiles = @(Get-ChildItem -LiteralPath $NativeDumpRoot -Filter '*.iwi' -File -Recurse)

$dumpRecords = New-Object 'System.Collections.Generic.List[object]'
$byExact = @{}
$byNormalized = @{}

foreach ($file in $dumpFiles) {
    $name = Normalize-DumpedImageName -Root $NativeDumpRoot -File $file
    $normalized = Normalize-NativeImageName -Name $name
    $header = Read-IwiHeader -Path $file.FullName

    $record = [pscustomobject]@{
        Name = $name
        NormalizedName = $normalized
        Bytes = [int64]$file.Length
        RelativePath = Get-RelativePathCompat -BasePath $NativeDumpRoot -TargetPath $file.FullName
        HeaderValid = [bool]$header.Valid
        Version = [int]$header.Version
        Format = [int]$header.Format
        Flags = [int]$header.Flags
        Width = [int]$header.Width
        Height = [int]$header.Height
        Depth = [int]$header.Depth
    }

    $dumpRecords.Add($record)

    $exactKey = $name.ToLowerInvariant()
    if (-not $byExact.ContainsKey($exactKey)) {
        $byExact[$exactKey] = $record
    }

    $normKey = $normalized.ToLowerInvariant()
    if (-not $byNormalized.ContainsKey($normKey) -or $record.Bytes -gt $byNormalized[$normKey].Bytes) {
        $byNormalized[$normKey] = $record
    }
}

$matches = New-Object 'System.Collections.Generic.List[object]'
$missing = New-Object 'System.Collections.Generic.List[object]'

foreach ($nativeName in $nativeImages) {
    $exactKey = $nativeName.ToLowerInvariant()
    $normalized = Normalize-NativeImageName -Name $nativeName
    $normKey = $normalized.ToLowerInvariant()

    $record = $null
    $mode = ''

    if ($byExact.ContainsKey($exactKey)) {
        $record = $byExact[$exactKey]
        $mode = 'exact'
    }
    elseif ($byNormalized.ContainsKey($normKey)) {
        $record = $byNormalized[$normKey]
        $mode = 'normalized'
    }

    if ($null -ne $record) {
        $matches.Add([pscustomobject]@{
            NativeName = $nativeName
            DumpName = $record.Name
            MatchMode = $mode
            Bytes = $record.Bytes
            Version = $record.Version
            Format = $record.Format
            Flags = $record.Flags
            Width = $record.Width
            Height = $record.Height
            Depth = $record.Depth
            HeaderValid = $record.HeaderValid
            RelativePath = $record.RelativePath
        })
    }
    else {
        $class =
            if ($nativeName.StartsWith('*')) { 'generated-star' }
            elseif ($nativeName.StartsWith('$')) { 'function-dollar' }
            elseif ($nativeName.StartsWith(',')) { 'comma-special' }
            elseif ($nativeName.StartsWith('~')) { 'streamed-tilde' }
            elseif ($nativeName.EndsWith('_wj', [StringComparison]::OrdinalIgnoreCase)) { 'xenon-wj' }
            else { 'ordinary' }

        $missing.Add([pscustomobject]@{
            NativeName = $nativeName
            NormalizedName = $normalized
            Classification = $class
            WindowsLiteralFilenameSafe = ($nativeName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -lt 0)
        })
    }
}

$formatGroups = @($matches |
    Where-Object { $_.HeaderValid } |
    Group-Object Version,Format |
    ForEach-Object {
        $items = @($_.Group)
        [pscustomobject]@{
            Version = [int]$items[0].Version
            Format = [int]$items[0].Format
            Images = $items.Count
            Bytes = [int64](($items | Measure-Object -Property Bytes -Sum).Sum)
        }
    } |
    Sort-Object Bytes -Descending)

$missingGroups = @($missing |
    Group-Object Classification |
    ForEach-Object {
        [pscustomobject]@{
            Classification = $_.Name
            Images = $_.Count
        }
    } |
    Sort-Object Images -Descending)

$matchedBytes = ($matches | Measure-Object -Property Bytes -Sum).Sum
if ($null -eq $matchedBytes) { $matchedBytes = 0 }

$uniqueDumpBytes = ($dumpRecords | Measure-Object -Property Bytes -Sum).Sum
if ($null -eq $uniqueDumpBytes) { $uniqueDumpBytes = 0 }

$exactMatches = @($matches | Where-Object { $_.MatchMode -eq 'exact' }).Count
$normalizedMatches = @($matches | Where-Object { $_.MatchMode -eq 'normalized' }).Count
$unsafeMissing = @($missing | Where-Object { -not $_.WindowsLiteralFilenameSafe }).Count

# Windows PowerShell 5.1 can bind inline property/cast arithmetic unexpectedly.
# Collapse collection counts to ordinary scalar Int32 values first, then perform
# the floating-point division as a separate statement.
$nativeImageCount = [Convert]::ToInt32($nativeImages.Count)
$dumpedIwiCount = [Convert]::ToInt32($dumpRecords.Count)
$matchedRootCount = [Convert]::ToInt32($matches.Count)
$missingRootCount = [Convert]::ToInt32($missing.Count)

$identityCoverage = [double]0.0
if ($nativeImageCount -gt 0) {
    $identityCoverage = [Convert]::ToDouble($matchedRootCount) / [Convert]::ToDouble($nativeImageCount)
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$jsonPath = Join-Path $OutputRoot 'nuclear_4k_native_image_closure.json'
$txtPath = Join-Path $OutputRoot 'nuclear_4k_native_image_closure.txt'

$result = [ordered]@{
    format = 'WaW360.Nuclear4K.NativeImageClosure.v1'
    mapName = $MapName
    nativeIdentityManifest = $NativeIdentityManifest
    nativeDumpRoot = $NativeDumpRoot
    nativeImageRoots = $nativeImageCount
    dumpedIwiFiles = $dumpedIwiCount
    dumpedIwiBytes = [int64]$uniqueDumpBytes
    matchedNativeRoots = $matchedRootCount
    matchedNativeBytes = [int64]$matchedBytes
    identityCoverage = $identityCoverage
    exactMatches = $exactMatches
    normalizedMatches = $normalizedMatches
    missingNativeRoots = $missingRootCount
    missingWindowsUnsafeNames = $unsafeMissing
    formatGroups = @($formatGroups)
    missingGroups = @($missingGroups)
    missing = @($missing)
    largestMatched = @($matches | Sort-Object Bytes -Descending | Select-Object -First 100)
}

$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$lines = New-Object 'System.Collections.Generic.List[string]'
$lines.Add('NUCLEAR STAGE 4K - NATIVE IMAGE IDENTITY / FORMAT CLOSURE')
$lines.Add(('=' * 94))
$lines.Add(("Map:                       {0}" -f $MapName))
$lines.Add(("Native image roots:        {0:N0}" -f $nativeImageCount))
$lines.Add(("Dumped native IWIs:        {0:N0}" -f $dumpedIwiCount))
$lines.Add(("Dumped IWI bytes:          {0:N0}" -f [int64]$uniqueDumpBytes))
$lines.Add(("Matched native roots:      {0:N0}" -f $matchedRootCount))
$lines.Add(("Identity coverage:         {0:P2}" -f $result.identityCoverage))
$lines.Add(("Exact-name matches:        {0:N0}" -f $exactMatches))
$lines.Add(("_wj-normalized matches:    {0:N0}" -f $normalizedMatches))
$lines.Add(("Missing native roots:      {0:N0}" -f $missingRootCount))
$lines.Add(("Missing filename-unsafe:   {0:N0}" -f $unsafeMissing))
$lines.Add('')
$lines.Add('FORMAT MASS (matched native roots; largest first)')
$lines.Add('version format images         bytes')
foreach ($group in $formatGroups) {
    $lines.Add(("{0,7}   0x{1:X2} {2,6:N0} {3,13:N0}" -f $group.Version, $group.Format, $group.Images, $group.Bytes))
}
$lines.Add('')
$lines.Add('MISSING CLASSIFICATION')
foreach ($group in $missingGroups) {
    $lines.Add(("  {0,-20} {1,4:N0}" -f $group.Classification, $group.Images))
}
$lines.Add('')
$lines.Add('MISSING NATIVE IMAGE ROOTS')
foreach ($item in $missing) {
    $lines.Add(("  [{0}] safe={1}  {2}" -f $item.Classification, $item.WindowsLiteralFilenameSafe, $item.NativeName))
}
$lines.Add('')
$lines.Add('LARGEST MATCHED IMAGES')
foreach ($item in @($matches | Sort-Object Bytes -Descending | Select-Object -First 60)) {
    $lines.Add(("  {0,11:N0} B  v{1} fmt=0x{2:X2} {3}x{4}x{5}  {6}" -f
        $item.Bytes, $item.Version, $item.Format, $item.Width, $item.Height, $item.Depth, $item.NativeName))
}
$lines.Add('')
$lines.Add('NOTE: Stage 4K is oracle evidence only. It does not copy native image payloads into converter output.')
$lines.Add('NOTE: XMAs remain external package assets under sounds\ and are outside this image census.')
$lines.Add('NOTE: The final package follows the agreed shared usermap layout; high-resolution streamed texture payloads belong under highmip\ when applicable.')
$lines.Add('NOTE: Literal Windows filename safety is reported because generated T4 image names beginning with * cannot be emitted as ordinary files under their asset names.')

[IO.File]::WriteAllLines($txtPath, $lines, [Text.UTF8Encoding]::new($false))

Write-Host '[NUCLEAR-4K] ================================================================'
Write-Host '[NUCLEAR-4K] NATIVE IMAGE IDENTITY / FORMAT CLOSURE'
Write-Host '[NUCLEAR-4K] ================================================================'
Write-Host "[NUCLEAR-4K] Native roots          : $nativeImageCount"
Write-Host "[NUCLEAR-4K] Dumped IWIs           : $dumpedIwiCount"
Write-Host "[NUCLEAR-4K] Dumped bytes          : $([int64]$uniqueDumpBytes)"
Write-Host "[NUCLEAR-4K] Matched roots         : $matchedRootCount"
Write-Host ("[NUCLEAR-4K] Identity coverage     : {0:P2}" -f $result.identityCoverage)
Write-Host "[NUCLEAR-4K] Exact matches         : $exactMatches"
Write-Host "[NUCLEAR-4K] _wj normalized        : $normalizedMatches"
Write-Host "[NUCLEAR-4K] Missing roots         : $missingRootCount"
Write-Host "[NUCLEAR-4K] Missing filename-unsafe: $unsafeMissing"
Write-Host ''
Write-Host '[NUCLEAR-4K] FORMAT MASS:'
foreach ($group in $formatGroups) {
    Write-Host ("  v{0} fmt=0x{1:X2}  images={2,4}  bytes={3,12:N0}" -f
        $group.Version, $group.Format, $group.Images, $group.Bytes)
}
Write-Host ''
Write-Host '[NUCLEAR-4K] MISSING CLASSIFICATION:'
foreach ($group in $missingGroups) {
    Write-Host ("  {0,-20} {1,4}" -f $group.Classification, $group.Images)
}
Write-Host ''
Write-Host '[NUCLEAR-4K] MISSING ROOTS:'
foreach ($item in $missing) {
    Write-Host ("  [{0}] safe={1}  {2}" -f $item.Classification, $item.WindowsLiteralFilenameSafe, $item.NativeName)
}
Write-Host ''
Write-Host "[NUCLEAR-4K] report  : $txtPath"
Write-Host "[NUCLEAR-4K] manifest: $jsonPath"
Write-Host '[NUCLEAR-4K] ================================================================'
Write-Host '[NUCLEAR-4K] PASS: native image closure classified; choose codec work by byte mass.'
