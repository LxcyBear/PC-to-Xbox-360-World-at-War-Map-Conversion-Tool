param(
    [string]$MapCorpusManifest = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$linker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Linker.exe'
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($MapCorpusManifest)) {
    $MapCorpusManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_map_asset_corpus\nuclear_4r_map_asset_corpus.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4s_map_asset_image_bridge'
}

if (-not (Test-Path -LiteralPath $MapCorpusManifest -PathType Leaf)) {
    throw "Packaged-map corpus manifest not found: $MapCorpusManifest"
}
if (-not (Test-Path -LiteralPath $linker -PathType Leaf)) {
    throw "Pinned Xenon OAT Linker not found: $linker"
}
if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) {
    throw "Specialized Xenon OAT Unlinker not found: $unlinker"
}

function Invoke-NativeTee {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][object[]]$Arguments,
        [Parameter(Mandatory=$true)][string]$LogPath
    )

    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $FilePath @Arguments 2>&1 |
            Tee-Object -FilePath $LogPath |
            ForEach-Object { Write-Host ([string]$_) }
        return [int]$LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
}

$corpus = Get-Content -LiteralPath $MapCorpusManifest -Raw | ConvertFrom-Json
if ([string]$corpus.format -ne 'WaW360.Nuclear4R.MapAssetCorpus.v1') {
    throw "Unexpected map corpus format: $($corpus.format)"
}

$resolved = @($corpus.resolved)
if ($resolved.Count -eq 0) {
    throw 'Packaged-map corpus contains zero resolved source identities.'
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

$work = Join-Path $OutputRoot 'workspace'
$rawImages = Join-Path $work 'raw\images'
$zoneSource = Join-Path $work 'zone_source'
$linkOut = Join-Path $OutputRoot 'linked'
$graphOut = Join-Path $OutputRoot 'graph_dump'
$linkLog = Join-Path $OutputRoot 'oat_linker.log'
$unlinkLog = Join-Path $OutputRoot 'oat_graph_unlinker.log'
$project = 'nuclear_4s_map_asset_images'
$zoneFile = Join-Path $zoneSource ($project + '.zone')

foreach ($path in @($rawImages, $zoneSource, $linkOut, $graphOut)) {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

$zoneLines = New-Object 'System.Collections.Generic.List[string]'
$zoneLines.Add('>game,T4')
$zoneLines.Add('>platform,xbox360')
$zoneLines.Add(('>name,{0}' -f $project))
$zoneLines.Add('')

$materialized = New-Object 'System.Collections.Generic.List[object]'
$sourceErrors = New-Object 'System.Collections.Generic.List[string]'
$archiveHandles = @{}

Write-Host '[NUCLEAR-4S-MAPS] ============================================================'
Write-Host '[NUCLEAR-4S-MAPS] PACKAGED-MAP SOURCE IMAGE -> XENON GRAPH BRIDGE'
Write-Host '[NUCLEAR-4S-MAPS] ============================================================'
Write-Host "[NUCLEAR-4S-MAPS] Source identities : $($resolved.Count)"
Write-Host "[NUCLEAR-4S-MAPS] Manifest          : $MapCorpusManifest"
Write-Host '[NUCLEAR-4S-MAPS] Source bytes come only from the user-supplied packaged-map corpus.'
Write-Host '[NUCLEAR-4S-MAPS] Native/oracle payload bytes are not used.'
Write-Host ''

try {
    foreach ($item in ($resolved | Sort-Object { [int]$_.Index })) {
        $name = [string]$item.Name
        $extension = [string]$item.Extension
        if ([string]::IsNullOrWhiteSpace($extension)) { $extension = '.iwi' }
        if (-not $extension.StartsWith('.')) { $extension = '.' + $extension }

        $sourcePath = [string]$item.SourcePath
        $archiveEntry = [string]$item.ArchiveEntry
        $package = [string]$item.Package
        $sourceKind = [string]$item.SourceKind

        $assetRelative = $name.Replace('/', '\') + $extension
        $destination = Join-Path $rawImages $assetRelative
        $destinationDir = Split-Path -Parent $destination
        if (-not [string]::IsNullOrWhiteSpace($destinationDir)) {
            New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null
        }

        try {
            if ([string]::IsNullOrWhiteSpace($archiveEntry)) {
                if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                    throw "source file not found: $sourcePath"
                }
                Copy-Item -LiteralPath $sourcePath -Destination $destination -Force
            }
            else {
                if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                    throw "archive not found: $sourcePath"
                }

                $archiveKey = $sourcePath.ToLowerInvariant()
                if (-not $archiveHandles.ContainsKey($archiveKey)) {
                    $archiveHandles[$archiveKey] = [IO.Compression.ZipFile]::OpenRead($sourcePath)
                }

                $zip = $archiveHandles[$archiveKey]
                $entry = $zip.GetEntry($archiveEntry)
                if ($null -eq $entry) {
                    throw "archive entry not found: $archiveEntry"
                }

                $src = $entry.Open()
                $dst = [IO.File]::Create($destination)
                try {
                    $src.CopyTo($dst)
                }
                finally {
                    $dst.Dispose()
                    $src.Dispose()
                }
            }

            $zoneLines.Add(('image,"{0}"' -f $name.Replace('"', '')))
            $materialized.Add([pscustomobject]@{
                Index = [int]$item.Index
                Name = $name
                Package = $package
                SourceKind = $sourceKind
                SourcePath = $sourcePath
                ArchiveEntry = $archiveEntry
                Extension = $extension
                MaterializedPath = $destination
            })

            Write-Host ("[NUCLEAR-4S-MAPS] materialized root #{0}: {1} <- {2}/{3}" -f
                [int]$item.Index, $name, $package, $sourceKind)
        }
        catch {
            $sourceErrors.Add(("root #{0} {1}: {2}" -f [int]$item.Index, $name, $_.Exception.Message))
        }
    }
}
finally {
    foreach ($zip in $archiveHandles.Values) {
        try { $zip.Dispose() } catch {}
    }
}

[IO.File]::WriteAllLines($zoneFile, [string[]]$zoneLines.ToArray(), [Text.UTF8Encoding]::new($false))

$materializedArray = [object[]]$materialized.ToArray()
$linkCode = -1
$unlinkCode = -1
$linkedFastFile = $null
$graphErrors = New-Object 'System.Collections.Generic.List[string]'
$cardMemoryBytes = [int64]0
$pixelBytes = [int64]0

if ($sourceErrors.Count -eq 0 -and $materializedArray.Count -gt 0) {
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
        $linkCode = Invoke-NativeTee -FilePath $linker -Arguments $linkArgs -LogPath $linkLog
    }
    finally {
        Pop-Location
    }

    if ($linkCode -eq 0) {
        $linked = @(Get-ChildItem -LiteralPath $linkOut -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue)
        if ($linked.Count -ne 1) {
            $graphErrors.Add("Expected one linked image-only FF; found $($linked.Count).")
        }
        else {
            $linkedFastFile = $linked[0].FullName
            $unlinkArgs = @(
                '--verbose',
                '--no-color',
                '--include-assets', 'image',
                '--image-format', 'IWI',
                '--output-folder', $graphOut,
                $linkedFastFile
            )

            Push-Location (Split-Path -Parent $linkedFastFile)
            try {
                $unlinkCode = Invoke-NativeTee -FilePath $unlinker -Arguments $unlinkArgs -LogPath $unlinkLog
            }
            finally {
                Pop-Location
            }
        }
    }
}

$graphDir = Join-Path $graphOut 'xenon_image_graphs'
$pixelDir = Join-Path $graphOut 'xenon_image_pixels'
$headerDir = Join-Path $graphOut 'xenon_image_headers'

$graphFiles = @()
$pixelFiles = @()
$headerFiles = @()

if (Test-Path -LiteralPath $graphDir -PathType Container) {
    $graphFiles = @(Get-ChildItem -LiteralPath $graphDir -Filter '*.json' -File -Recurse -ErrorAction SilentlyContinue)
}
if (Test-Path -LiteralPath $pixelDir -PathType Container) {
    $pixelFiles = @(Get-ChildItem -LiteralPath $pixelDir -Filter '*.pixels.bin' -File -Recurse -ErrorAction SilentlyContinue)
}
if (Test-Path -LiteralPath $headerDir -PathType Container) {
    $headerFiles = @(Get-ChildItem -LiteralPath $headerDir -Filter '*.d3dtexture.bin' -File -Recurse -ErrorAction SilentlyContinue)
}

$graphNames = @{}
$graphRows = New-Object 'System.Collections.Generic.List[object]'

foreach ($file in $graphFiles) {
    try {
        $g = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        if ([string]$g.format -ne 'WaW360.T4Xenon.ImageGraph.v1') {
            $graphErrors.Add("$($file.Name): unexpected graph format '$($g.format)'.")
            continue
        }

        $name = [string]$g.name
        if ([string]::IsNullOrWhiteSpace($name)) {
            $graphErrors.Add("$($file.Name): empty image name.")
            continue
        }

        $key = $name.ToLowerInvariant()
        if ($graphNames.ContainsKey($key)) {
            $graphErrors.Add("Duplicate output graph identity '$name'.")
            continue
        }

        $graphNames[$key] = $true
        $cardMemoryBytes += [int64]$g.cardMemoryBytes
        $pixelBytes += [int64]$g.pixelsBytes

        $graphRows.Add([pscustomobject]@{
            Name = $name
            GraphFile = $file.FullName
            CardMemoryBytes = [int64]$g.cardMemoryBytes
            PixelsBytes = [int64]$g.pixelsBytes
            Width = [int]$g.width
            Height = [int]$g.height
            Depth = [int]$g.depth
        })
    }
    catch {
        $graphErrors.Add("$($file.Name): graph parse failed: $($_.Exception.Message)")
    }
}

foreach ($item in $materializedArray) {
    if (-not $graphNames.ContainsKey(([string]$item.Name).ToLowerInvariant())) {
        $graphErrors.Add("Missing output graph for source-backed image '$($item.Name)'.")
    }
}

if ($graphFiles.Count -ne $materializedArray.Count) {
    $graphErrors.Add("Graph count $($graphFiles.Count) != materialized source count $($materializedArray.Count).")
}
if ($pixelFiles.Count -ne $materializedArray.Count) {
    $graphErrors.Add("Pixel blob count $($pixelFiles.Count) != materialized source count $($materializedArray.Count).")
}
if ($headerFiles.Count -ne $materializedArray.Count) {
    $graphErrors.Add("Texture header count $($headerFiles.Count) != materialized source count $($materializedArray.Count).")
}

$bridgePass =
    $resolved.Count -gt 0 -and
    $materializedArray.Count -eq $resolved.Count -and
    $sourceErrors.Count -eq 0 -and
    $linkCode -eq 0 -and
    $unlinkCode -eq 0 -and
    $graphErrors.Count -eq 0

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4S.MapAssetImageBridge.v1'
    sourceManifest = (Resolve-Path -LiteralPath $MapCorpusManifest).Path
    requestedSources = $resolved.Count
    materializedSources = $materializedArray.Count
    sourceErrors = [object[]]$sourceErrors.ToArray()
    linkedFastFile = $linkedFastFile
    oatLinkExitCode = $linkCode
    oatUnlinkExitCode = $unlinkCode
    graphRoot = $graphOut
    imageGraphs = $graphFiles.Count
    pixelBlobs = $pixelFiles.Count
    textureHeaders = $headerFiles.Count
    xenonCardMemoryBytes = $cardMemoryBytes
    xenonPixelBytes = $pixelBytes
    bridgePass = $bridgePass
    sources = $materializedArray
    graphs = [object[]]$graphRows.ToArray()
    graphErrors = [object[]]$graphErrors.ToArray()
    productionSourcePolicy = @(
        'Input pixels come only from source files found in the user-supplied packaged custom-map corpus.',
        'This stage does not use native/oracle FRUmansion image payload bytes.',
        'This stage does not use PC retail WaW as a stock dependency source.',
        'Converted graphs remain local build artifacts and are not committed or distributed by the repository.',
        'A successful bridge proves technical Xenon conversion; final production inclusion still requires the 4F gate.'
    )
}

$manifestPath = Join-Path $OutputRoot 'nuclear_4s_map_asset_image_bridge.json'
$reportPath = Join-Path $OutputRoot 'nuclear_4s_map_asset_image_bridge.txt'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4S-MAPS - PACKAGED-MAP SOURCE IMAGE -> XENON GRAPH BRIDGE')
$report.Add(('=' * 96))
$report.Add(("Requested sources:       {0:N0}" -f $resolved.Count))
$report.Add(("Materialized sources:    {0:N0}" -f $materializedArray.Count))
$report.Add(("Source errors:            {0:N0}" -f $sourceErrors.Count))
$report.Add(("OAT linker exit:          {0}" -f $linkCode))
$report.Add(("OAT graph unlinker exit:  {0}" -f $unlinkCode))
$report.Add(("Image graphs:             {0:N0}" -f $graphFiles.Count))
$report.Add(("Pixel blobs:              {0:N0}" -f $pixelFiles.Count))
$report.Add(("Texture headers:          {0:N0}" -f $headerFiles.Count))
$report.Add(("Xenon cardMemory bytes:   {0:N0}" -f $cardMemoryBytes))
$report.Add(("Xenon pixel bytes:        {0:N0}" -f $pixelBytes))
$report.Add(("BRIDGE PASS:              {0}" -f $bridgePass))
$report.Add('')
$report.Add('SOURCES')
foreach ($row in $materializedArray) {
    $report.Add(("  root #{0,4}: {1} <- {2}/{3}" -f $row.Index, $row.Name, $row.Package, $row.SourceKind))
}
if ($sourceErrors.Count -gt 0) {
    $report.Add('')
    $report.Add('SOURCE ERRORS')
    foreach ($error in $sourceErrors.ToArray()) {
        $report.Add("  $error")
    }
}
if ($graphErrors.Count -gt 0) {
    $report.Add('')
    $report.Add('GRAPH ERRORS')
    foreach ($error in $graphErrors.ToArray()) {
        $report.Add("  $error")
    }
}

[IO.File]::WriteAllLines($reportPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4S-MAPS] ====================== RESULT =============================='
Write-Host "[NUCLEAR-4S-MAPS] Requested sources      : $($resolved.Count)"
Write-Host "[NUCLEAR-4S-MAPS] Materialized sources   : $($materializedArray.Count)"
Write-Host "[NUCLEAR-4S-MAPS] Source errors          : $($sourceErrors.Count)"
Write-Host "[NUCLEAR-4S-MAPS] OAT linker exit        : $linkCode"
Write-Host "[NUCLEAR-4S-MAPS] OAT graph unlink exit  : $unlinkCode"
Write-Host "[NUCLEAR-4S-MAPS] Image graphs           : $($graphFiles.Count)"
Write-Host "[NUCLEAR-4S-MAPS] Pixel blobs            : $($pixelFiles.Count)"
Write-Host "[NUCLEAR-4S-MAPS] Texture headers        : $($headerFiles.Count)"
Write-Host "[NUCLEAR-4S-MAPS] Xenon cardMemory       : $cardMemoryBytes bytes"
Write-Host "[NUCLEAR-4S-MAPS] Xenon pixel bytes      : $pixelBytes"
Write-Host "[NUCLEAR-4S-MAPS] Bridge pass            : $bridgePass"
Write-Host "[NUCLEAR-4S-MAPS] Manifest               : $manifestPath"
Write-Host "[NUCLEAR-4S-MAPS] Report                 : $reportPath"
Write-Host '[NUCLEAR-4S-MAPS] ================================================================='

if ($sourceErrors.Count -gt 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4S-MAPS] Source errors:' -ForegroundColor Yellow
    foreach ($error in @($sourceErrors.ToArray() | Select-Object -First 20)) {
        Write-Host "  $error"
    }
}

if ($graphErrors.Count -gt 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4S-MAPS] Graph errors:' -ForegroundColor Yellow
    foreach ($error in @($graphErrors.ToArray() | Select-Object -First 20)) {
        Write-Host "  $error"
    }
}

if (-not $bridgePass) {
    Write-Host ''
    Write-Host '[NUCLEAR-4S-MAPS] PACKAGED-MAP IMAGE BRIDGE: BLOCKED' -ForegroundColor Yellow
    exit 4
}

Write-Host ''
Write-Host '[NUCLEAR-4S-MAPS] PACKAGED-MAP IMAGE BRIDGE: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4S-MAPS] These source-backed images now have verified Xenon image graphs.'
