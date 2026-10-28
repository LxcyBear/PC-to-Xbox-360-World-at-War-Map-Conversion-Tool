param(
    [string]$DependencyCorpusManifest = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$linker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Linker.exe'
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($DependencyCorpusManifest)) {
    $DependencyCorpusManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_mod_dependency_corpus\nuclear_4r_mod_dependency_corpus.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4s_mod_dependency_image_bridge'
}

if (-not (Test-Path -LiteralPath $DependencyCorpusManifest -PathType Leaf)) { throw "Dependency corpus manifest not found: $DependencyCorpusManifest" }
if (-not (Test-Path -LiteralPath $linker -PathType Leaf)) { throw "Pinned Xenon OAT Linker not found: $linker" }
if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) { throw "Specialized Xenon OAT Unlinker not found: $unlinker" }

function Invoke-NativeTee {
    param([string]$FilePath, [object[]]$Arguments, [string]$LogPath)
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $FilePath @Arguments 2>&1 | Tee-Object -FilePath $LogPath | ForEach-Object { Write-Host ([string]$_) }
        return [int]$LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
}

$corpus = Get-Content -LiteralPath $DependencyCorpusManifest -Raw | ConvertFrom-Json
if ([string]$corpus.format -ne 'WaW360.Nuclear4R.ModDependencyCorpus.v1') {
    throw "Unexpected dependency corpus format: $($corpus.format)"
}

$resolved = @($corpus.resolved)
if ($resolved.Count -eq 0) { throw 'Dependency corpus contains zero resolved image sources.' }

if (Test-Path -LiteralPath $OutputRoot) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }

$work = Join-Path $OutputRoot 'workspace'
$rawImages = Join-Path $work 'raw\images'
$zoneSource = Join-Path $work 'zone_source'
$linkOut = Join-Path $OutputRoot 'linked'
$graphOut = Join-Path $OutputRoot 'graph_dump'
$linkLog = Join-Path $OutputRoot 'oat_linker.log'
$unlinkLog = Join-Path $OutputRoot 'oat_graph_unlinker.log'
$project = 'nuclear_4s_mod_dependency_images'
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

Write-Host '[NUCLEAR-4S-MOD] ============================================================'
Write-Host '[NUCLEAR-4S-MOD] UGX/ZCT SOURCE IMAGE -> XENON GRAPH BRIDGE'
Write-Host '[NUCLEAR-4S-MOD] ============================================================'
Write-Host "[NUCLEAR-4S-MOD] Source identities : $($resolved.Count)"
Write-Host "[NUCLEAR-4S-MOD] Manifest          : $DependencyCorpusManifest"
Write-Host ''

try {
    foreach ($item in ($resolved | Sort-Object { [int]$_.Index })) {
        $name = [string]$item.Name
        $extension = [string]$item.Extension
        if ([string]::IsNullOrWhiteSpace($extension)) { $extension = '.iwi' }
        if (-not $extension.StartsWith('.')) { $extension = '.' + $extension }

        $sourcePath = [string]$item.SourcePath
        $archiveEntry = [string]$item.ArchiveEntry
        $corpusName = [string]$item.Corpus
        $sourceKind = [string]$item.SourceKind

        $assetRelative = $name.Replace('/', '\') + $extension
        $destination = Join-Path $rawImages $assetRelative
        $destinationDir = Split-Path -Parent $destination
        if (-not [string]::IsNullOrWhiteSpace($destinationDir)) {
            New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null
        }

        try {
            if ([string]::IsNullOrWhiteSpace($archiveEntry)) {
                if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "source file not found: $sourcePath" }
                Copy-Item -LiteralPath $sourcePath -Destination $destination -Force
            }
            else {
                if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "archive not found: $sourcePath" }

                $archiveKey = $sourcePath.ToLowerInvariant()
                if (-not $archiveHandles.ContainsKey($archiveKey)) {
                    $archiveHandles[$archiveKey] = [IO.Compression.ZipFile]::OpenRead($sourcePath)
                }
                $entry = $archiveHandles[$archiveKey].GetEntry($archiveEntry)
                if ($null -eq $entry) { throw "archive entry not found: $archiveEntry" }

                $src = $entry.Open()
                $dst = [IO.File]::Create($destination)
                try { $src.CopyTo($dst) }
                finally { $dst.Dispose(); $src.Dispose() }
            }

            $zoneLines.Add(('image,"{0}"' -f $name.Replace('"', '')))
            $materialized.Add([pscustomobject]@{
                Index = [int]$item.Index
                Name = $name
                Corpus = $corpusName
                SourceKind = $sourceKind
                SourcePath = $sourcePath
                ArchiveEntry = $archiveEntry
                Extension = $extension
                MaterializedPath = $destination
            })

            Write-Host ("[NUCLEAR-4S-MOD] materialized root #{0}: {1} <- {2}/{3}" -f [int]$item.Index, $name, $corpusName, $sourceKind)
        }
        catch {
            $sourceErrors.Add(("root #{0} {1}: {2}" -f [int]$item.Index, $name, $_.Exception.Message))
        }
    }
}
finally {
    foreach ($zip in $archiveHandles.Values) { try { $zip.Dispose() } catch {} }
}

[IO.File]::WriteAllLines($zoneFile, [string[]]$zoneLines.ToArray(), [Text.UTF8Encoding]::new($false))

$materializedArray = [object[]]$materialized.ToArray()
$linkCode = -1
$unlinkCode = -1

if ($sourceErrors.Count -eq 0 -and $materializedArray.Count -gt 0) {
    $linkArgs = @('--verbose','--no-color','--base-folder',$work,'--source-search-path',$zoneSource,'--asset-search-path',(Join-Path $work 'raw'),'--output-folder',$linkOut,$project)

    Push-Location $work
    try { $linkCode = Invoke-NativeTee -FilePath $linker -Arguments $linkArgs -LogPath $linkLog }
    finally { Pop-Location }

    if ($linkCode -eq 0) {
        $linked = @(Get-ChildItem -LiteralPath $linkOut -Filter '*.ff' -File -Recurse)
        if ($linked.Count -eq 1) {
            $unlinkArgs = @('--verbose','--no-color','--include-assets','image','--image-format','IWI','--output-folder',$graphOut,$linked[0].FullName)
            Push-Location $linked[0].DirectoryName
            try { $unlinkCode = Invoke-NativeTee -FilePath $unlinker -Arguments $unlinkArgs -LogPath $unlinkLog }
            finally { Pop-Location }
        }
    }
}

$graphDir = Join-Path $graphOut 'xenon_image_graphs'
$pixelDir = Join-Path $graphOut 'xenon_image_pixels'
$headerDir = Join-Path $graphOut 'xenon_image_headers'
$graphFiles = @()
$pixelFiles = @()
$headerFiles = @()
if (Test-Path -LiteralPath $graphDir -PathType Container) { $graphFiles = @(Get-ChildItem -LiteralPath $graphDir -Filter '*.json' -File -Recurse) }
if (Test-Path -LiteralPath $pixelDir -PathType Container) { $pixelFiles = @(Get-ChildItem -LiteralPath $pixelDir -Filter '*.pixels.bin' -File -Recurse) }
if (Test-Path -LiteralPath $headerDir -PathType Container) { $headerFiles = @(Get-ChildItem -LiteralPath $headerDir -Filter '*.d3dtexture.bin' -File -Recurse) }

$graphNames = @{}
$graphErrors = New-Object 'System.Collections.Generic.List[string]'
$cardMemoryBytes = [int64]0
$pixelBytes = [int64]0

foreach ($file in $graphFiles) {
    try {
        $g = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        if ([string]$g.format -ne 'WaW360.T4Xenon.ImageGraph.v1') { throw "unexpected graph format '$($g.format)'" }
        $name = [string]$g.name
        if ([string]::IsNullOrWhiteSpace($name)) { throw 'empty image name' }

        $graphNames[$name.ToLowerInvariant()] = $true
        $cardMemoryBytes += [int64]$g.cardMemoryBytes
        $pixelBytes += [int64]$g.pixelsBytes
    }
    catch {
        $graphErrors.Add("$($file.Name): $($_.Exception.Message)")
    }
}

foreach ($item in $materializedArray) {
    if (-not $graphNames.ContainsKey(([string]$item.Name).ToLowerInvariant())) {
        $graphErrors.Add("Missing output graph for '$($item.Name)'.")
    }
}

if ($graphFiles.Count -ne $materializedArray.Count) { $graphErrors.Add("Graph count $($graphFiles.Count) != source count $($materializedArray.Count).") }
if ($pixelFiles.Count -ne $materializedArray.Count) { $graphErrors.Add("Pixel count $($pixelFiles.Count) != source count $($materializedArray.Count).") }
if ($headerFiles.Count -ne $materializedArray.Count) { $graphErrors.Add("Header count $($headerFiles.Count) != source count $($materializedArray.Count).") }

$bridgePass = $resolved.Count -gt 0 -and $materializedArray.Count -eq $resolved.Count -and $sourceErrors.Count -eq 0 -and $linkCode -eq 0 -and $unlinkCode -eq 0 -and $graphErrors.Count -eq 0

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4S.ModDependencyImageBridge.v1'
    sourceManifest = (Resolve-Path -LiteralPath $DependencyCorpusManifest).Path
    requestedSources = $resolved.Count
    materializedSources = $materializedArray.Count
    sourceErrors = [object[]]$sourceErrors.ToArray()
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
    graphErrors = [object[]]$graphErrors.ToArray()
}

$manifestPath = Join-Path $OutputRoot 'nuclear_4s_mod_dependency_image_bridge.json'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host ''
Write-Host '[NUCLEAR-4S-MOD] ====================== RESULT ==============================='
Write-Host "[NUCLEAR-4S-MOD] Requested sources     : $($resolved.Count)"
Write-Host "[NUCLEAR-4S-MOD] Materialized sources  : $($materializedArray.Count)"
Write-Host "[NUCLEAR-4S-MOD] Source errors         : $($sourceErrors.Count)"
Write-Host "[NUCLEAR-4S-MOD] OAT linker exit       : $linkCode"
Write-Host "[NUCLEAR-4S-MOD] OAT graph unlink exit : $unlinkCode"
Write-Host "[NUCLEAR-4S-MOD] Image graphs          : $($graphFiles.Count)"
Write-Host "[NUCLEAR-4S-MOD] Pixel blobs           : $($pixelFiles.Count)"
Write-Host "[NUCLEAR-4S-MOD] Texture headers       : $($headerFiles.Count)"
Write-Host "[NUCLEAR-4S-MOD] Xenon cardMemory      : $cardMemoryBytes bytes"
Write-Host "[NUCLEAR-4S-MOD] Bridge pass           : $bridgePass"
Write-Host "[NUCLEAR-4S-MOD] Manifest              : $manifestPath"
Write-Host '[NUCLEAR-4S-MOD] ================================================================='

if (-not $bridgePass) {
    Write-Host '[NUCLEAR-4S-MOD] DEPENDENCY IMAGE BRIDGE: BLOCKED' -ForegroundColor Yellow
    exit 4
}

Write-Host '[NUCLEAR-4S-MOD] DEPENDENCY IMAGE BRIDGE: PASS' -ForegroundColor Green
