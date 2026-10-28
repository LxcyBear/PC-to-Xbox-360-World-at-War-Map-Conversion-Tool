param(
    [string]$PcOwnedBridgeManifest = '',
    [string]$MapAssetBridgeManifest = '',
    [string]$ModDependencyBridgeManifest = '',
    [string]$XboxRetailGraphManifest = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($PcOwnedBridgeManifest)) {
    $PcOwnedBridgeManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_pc_image_bridge\nuclear_4r_pc_image_bridge.json'
}
if ([string]::IsNullOrWhiteSpace($MapAssetBridgeManifest)) {
    $MapAssetBridgeManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4s_map_asset_image_bridge\nuclear_4s_map_asset_image_bridge.json'
}
if ([string]::IsNullOrWhiteSpace($ModDependencyBridgeManifest)) {
    $ModDependencyBridgeManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4s_mod_dependency_image_bridge\nuclear_4s_mod_dependency_image_bridge.json'
}
if ([string]::IsNullOrWhiteSpace($XboxRetailGraphManifest)) {
    $XboxRetailGraphManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_xbox_retail_image_graph\nuclear_4r_xbox_retail_image_graph.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4t_production_image_graph'
}

function Get-SafeStem {
    param([string]$Value)
    $v = $Value -replace '[^A-Za-z0-9_.-]', '_'
    if ($v.Length -gt 90) { $v = $v.Substring(0, 90) }
    if ([string]::IsNullOrWhiteSpace($v)) { $v = 'image' }
    return $v
}

function Resolve-GraphBlob {
    param([string]$Root, [string]$Relative)
    if ([string]::IsNullOrWhiteSpace($Relative)) { return $null }
    return Join-Path $Root $Relative.Replace('/', '\').TrimStart('\')
}

function Add-GraphSource {
    param(
        [System.Collections.Generic.List[object]]$Sources,
        [string]$ManifestPath,
        [string]$ExpectedFormat,
        [string]$PassProperty,
        [string]$GraphRootProperty,
        [string]$Label,
        [bool]$Required
    )

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        if ($Required) { throw "Required $Label manifest not found: $ManifestPath" }
        Write-Host "[NUCLEAR-4T] Optional source absent: $Label" -ForegroundColor Yellow
        return
    }

    $doc = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ([string]$doc.format -ne $ExpectedFormat) {
        throw "$Label manifest format '$($doc.format)' != '$ExpectedFormat'"
    }
    if (-not [bool]$doc.$PassProperty) {
        throw "$Label manifest does not have $PassProperty=True: $ManifestPath"
    }

    $graphRoot = [string]$doc.$GraphRootProperty
    if (-not (Test-Path -LiteralPath $graphRoot -PathType Container)) {
        throw "$Label graph root not found: $graphRoot"
    }

    $Sources.Add([pscustomobject]@{
        Label = $Label
        ManifestPath = (Resolve-Path -LiteralPath $ManifestPath).Path
        GraphRoot = (Resolve-Path -LiteralPath $graphRoot).Path
    })
}

$sources = New-Object 'System.Collections.Generic.List[object]'

Add-GraphSource -Sources $sources -ManifestPath $PcOwnedBridgeManifest -ExpectedFormat 'WaW360.Nuclear4R.PcOwnedImageBridge.v1' -PassProperty 'bridgePass' -GraphRootProperty 'graphRoot' -Label 'frumansion-owned-pc' -Required $false
Add-GraphSource -Sources $sources -ManifestPath $MapAssetBridgeManifest -ExpectedFormat 'WaW360.Nuclear4S.MapAssetImageBridge.v1' -PassProperty 'bridgePass' -GraphRootProperty 'graphRoot' -Label 'packaged-map-pc' -Required $true
Add-GraphSource -Sources $sources -ManifestPath $ModDependencyBridgeManifest -ExpectedFormat 'WaW360.Nuclear4S.ModDependencyImageBridge.v1' -PassProperty 'bridgePass' -GraphRootProperty 'graphRoot' -Label 'mod-dependency-pc' -Required $true

if (-not (Test-Path -LiteralPath $XboxRetailGraphManifest -PathType Leaf)) {
    throw "Required retail Xbox graph manifest not found: $XboxRetailGraphManifest"
}
$xboxStock = Get-Content -LiteralPath $XboxRetailGraphManifest -Raw | ConvertFrom-Json
if ([string]$xboxStock.format -ne 'WaW360.Nuclear4R.XboxRetailImageGraph.v1') {
    throw "Retail Xbox graph manifest format '$($xboxStock.format)' is unexpected."
}
$xboxStockResolved = [int]$xboxStock.stockGraphsResolved
$xboxStockUnresolved = [int]$xboxStock.stockGraphsUnresolved
if ($xboxStockResolved -le 0) {
    throw 'Retail Xbox graph extraction produced zero verified stock image graphs.'
}
$xboxStockGraphRoot = [string]$xboxStock.graphRoot
if (-not (Test-Path -LiteralPath $xboxStockGraphRoot -PathType Container)) {
    throw "Retail Xbox stock graph root not found: $xboxStockGraphRoot"
}
$sources.Add([pscustomobject]@{
    Label = 'retail-xbox-stock'
    ManifestPath = (Resolve-Path -LiteralPath $XboxRetailGraphManifest).Path
    GraphRoot = (Resolve-Path -LiteralPath $xboxStockGraphRoot).Path
})

if ($xboxStockUnresolved -gt 0) {
    Write-Host ("[NUCLEAR-4T] Retail Xbox source is partial: {0} verified payloads, {1} identity-only roots fenced." -f $xboxStockResolved, $xboxStockUnresolved) -ForegroundColor Yellow
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

$graphOut = Join-Path $OutputRoot 'graph'
$graphDir = Join-Path $graphOut 'xenon_image_graphs'
$pixelDir = Join-Path $graphOut 'xenon_image_pixels'
$headerDir = Join-Path $graphOut 'xenon_image_headers'
foreach ($path in @($graphDir, $pixelDir, $headerDir)) {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

Write-Host '[NUCLEAR-4T] ================================================================'
Write-Host '[NUCLEAR-4T] PRODUCTION IMAGE GRAPH MERGE'
Write-Host '[NUCLEAR-4T] ================================================================'
Write-Host "[NUCLEAR-4T] Sources: $($sources.Count)"
foreach ($source in $sources.ToArray()) {
    Write-Host ("[NUCLEAR-4T]   {0,-22} {1}" -f $source.Label, $source.GraphRoot)
}
Write-Host ''

$seenNames = @{}
$rows = New-Object 'System.Collections.Generic.List[object]'
$errors = New-Object 'System.Collections.Generic.List[string]'
$totalCardMemory = [int64]0
$totalPixelBytes = [int64]0
$ordinal = 0

foreach ($source in $sources.ToArray()) {
    $sourceGraphDir = Join-Path $source.GraphRoot 'xenon_image_graphs'
    $sourceGraphs = @()
    if (Test-Path -LiteralPath $sourceGraphDir -PathType Container) {
        $sourceGraphs = @(Get-ChildItem -LiteralPath $sourceGraphDir -Filter '*.json' -File -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName)
    }

    Write-Host ("[NUCLEAR-4T] Reading {0}: {1} graph(s)" -f $source.Label, $sourceGraphs.Count)

    foreach ($graphFile in $sourceGraphs) {
        $ordinal++

        try {
            $g = Get-Content -LiteralPath $graphFile.FullName -Raw | ConvertFrom-Json
            if ([string]$g.format -ne 'WaW360.T4Xenon.ImageGraph.v1') {
                throw "unexpected graph format '$($g.format)'"
            }

            $name = [string]$g.name
            if ([string]::IsNullOrWhiteSpace($name)) { throw 'empty image name' }

            $key = $name.ToLowerInvariant()
            if ($seenNames.ContainsKey($key)) {
                $prior = $seenNames[$key]
                throw "duplicate exact image identity '$name' also supplied by '$($prior.Label)'"
            }

            $sourceHeader = Resolve-GraphBlob -Root $source.GraphRoot -Relative ([string]$g.loadDef.textureHeaderFile)
            if ($null -eq $sourceHeader -or -not (Test-Path -LiteralPath $sourceHeader -PathType Leaf)) {
                throw "missing texture header '$($g.loadDef.textureHeaderFile)'"
            }

            $sourcePixel = $null
            if ([int64]$g.pixelsBytes -gt 0) {
                $sourcePixel = Resolve-GraphBlob -Root $source.GraphRoot -Relative ([string]$g.pixelsFile)
                if ($null -eq $sourcePixel -or -not (Test-Path -LiteralPath $sourcePixel -PathType Leaf)) {
                    throw "missing pixel blob '$($g.pixelsFile)'"
                }

                $actualPixelBytes = [int64](Get-Item -LiteralPath $sourcePixel).Length
                if ($actualPixelBytes -ne [int64]$g.pixelsBytes) {
                    throw "pixel blob length $actualPixelBytes != graph pixelsBytes $([int64]$g.pixelsBytes)"
                }
            }

            $safe = Get-SafeStem -Value $name
            $stem = ("{0:D5}_{1}" -f $ordinal, $safe)

            $headerRel = "xenon_image_headers/$stem.d3dtexture.bin"
            Copy-Item -LiteralPath $sourceHeader -Destination (Join-Path $graphOut $headerRel.Replace('/', '\')) -Force
            $g.loadDef.textureHeaderFile = $headerRel

            $pixelRel = ''
            if ($null -ne $sourcePixel) {
                $pixelRel = "xenon_image_pixels/$stem.pixels.bin"
                Copy-Item -LiteralPath $sourcePixel -Destination (Join-Path $graphOut $pixelRel.Replace('/', '\')) -Force
            }
            $g.pixelsFile = $pixelRel

            $graphRel = "xenon_image_graphs/$stem.json"
            $graphPath = Join-Path $graphOut $graphRel.Replace('/', '\')
            $g | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $graphPath -Encoding UTF8

            $seenNames[$key] = [pscustomobject]@{ Label = [string]$source.Label; Graph = $graphRel }

            $card = [int64]$g.cardMemoryBytes
            $pixels = [int64]$g.pixelsBytes
            $totalCardMemory += $card
            $totalPixelBytes += $pixels

            $rows.Add([pscustomobject]@{
                Name = $name
                Source = [string]$source.Label
                SourceManifest = [string]$source.ManifestPath
                SourceGraph = $graphFile.FullName
                GraphFile = $graphRel
                PixelsFile = $pixelRel
                TextureHeaderFile = $headerRel
                CardMemoryBytes = $card
                PixelsBytes = $pixels
            })
        }
        catch {
            $errors.Add(("{0}: {1}: {2}" -f $source.Label, $graphFile.FullName, $_.Exception.Message))
        }
    }
}

$rowArray = [object[]]$rows.ToArray()
$bySource = @(
    $rowArray |
    Group-Object Source |
    Sort-Object Name |
    ForEach-Object {
        [pscustomobject]@{
            Source = $_.Name
            Images = $_.Count
            CardMemoryBytes = [int64](($_.Group | Measure-Object -Property CardMemoryBytes -Sum).Sum)
            PixelBytes = [int64](($_.Group | Measure-Object -Property PixelsBytes -Sum).Sum)
        }
    }
)

$graphFiles = @(Get-ChildItem -LiteralPath $graphDir -Filter '*.json' -File -ErrorAction SilentlyContinue)
$headerFiles = @(Get-ChildItem -LiteralPath $headerDir -Filter '*.d3dtexture.bin' -File -ErrorAction SilentlyContinue)
$pixelFiles = @(Get-ChildItem -LiteralPath $pixelDir -Filter '*.pixels.bin' -File -ErrorAction SilentlyContinue)

$mergePass = $errors.Count -eq 0 -and $rowArray.Count -gt 0 -and $graphFiles.Count -eq $rowArray.Count -and $headerFiles.Count -eq $rowArray.Count

$sourceRows = New-Object 'System.Collections.Generic.List[object]'
foreach ($source in $sources.ToArray()) {
    $sourceRows.Add([pscustomobject]@{
        label = [string]$source.Label
        manifest = [string]$source.ManifestPath
        graphRoot = [string]$source.GraphRoot
    })
}

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4T.ProductionImageGraph.v1'
    graphRoot = $graphOut
    sources = [object[]]$sourceRows.ToArray()
    imageGraphs = $graphFiles.Count
    pixelBlobs = $pixelFiles.Count
    textureHeaders = $headerFiles.Count
    xenonCardMemoryBytes = $totalCardMemory
    xenonPixelBytes = $totalPixelBytes
    retailStockGraphsResolved = $xboxStockResolved
    retailStockGraphsUnresolved = $xboxStockUnresolved
    retailStockUnresolved = [object[]]@($xboxStock.unresolved)
    bySource = $bySource
    images = $rowArray
    errors = [object[]]$errors.ToArray()
    mergePass = $mergePass
}

$manifestPath = Join-Path $OutputRoot 'nuclear_4t_production_image_graph.json'
$reportPath = Join-Path $OutputRoot 'nuclear_4t_production_image_graph.txt'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4T - PRODUCTION IMAGE GRAPH MERGE')
$report.Add(('=' * 96))
$report.Add(("Merged image graphs:       {0:N0}" -f $graphFiles.Count))
$report.Add(("Pixel blobs:               {0:N0}" -f $pixelFiles.Count))
$report.Add(("Texture headers:           {0:N0}" -f $headerFiles.Count))
$report.Add(("Xenon cardMemory bytes:    {0:N0}" -f $totalCardMemory))
$report.Add(("Xenon pixel bytes:         {0:N0}" -f $totalPixelBytes))
$report.Add(("Retail stock resolved:     {0:N0}" -f $xboxStockResolved))
$report.Add(("Retail stock fenced:       {0:N0}" -f $xboxStockUnresolved))
$report.Add(("Merge errors:              {0:N0}" -f $errors.Count))
$report.Add(("MERGE PASS:                {0}" -f $mergePass))
$report.Add('')
$report.Add('BY SOURCE')
foreach ($row in $bySource) {
    $report.Add(("  {0,-24} images={1,4:N0} cardMemory={2,12:N0} pixels={3,12:N0}" -f $row.Source, $row.Images, $row.CardMemoryBytes, $row.PixelBytes))
}
if ($errors.Count -gt 0) {
    $report.Add('')
    $report.Add('ERRORS')
    foreach ($error in $errors.ToArray()) { $report.Add("  $error") }
}
[IO.File]::WriteAllLines($reportPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4T] ====================== RESULT ==================================='
Write-Host "[NUCLEAR-4T] Merged image graphs : $($graphFiles.Count)"
Write-Host "[NUCLEAR-4T] Pixel blobs         : $($pixelFiles.Count)"
Write-Host "[NUCLEAR-4T] Texture headers     : $($headerFiles.Count)"
Write-Host "[NUCLEAR-4T] Xenon cardMemory    : $totalCardMemory bytes"
Write-Host "[NUCLEAR-4T] Xenon pixel bytes   : $totalPixelBytes"
Write-Host "[NUCLEAR-4T] Retail stock verified: $xboxStockResolved"
Write-Host "[NUCLEAR-4T] Retail stock fenced  : $xboxStockUnresolved"
Write-Host "[NUCLEAR-4T] Merge errors        : $($errors.Count)"
foreach ($row in $bySource) {
    Write-Host ("[NUCLEAR-4T]   {0}: {1} image(s), {2} bytes" -f $row.Source, $row.Images, $row.CardMemoryBytes)
}
Write-Host "[NUCLEAR-4T] Merge pass          : $mergePass"
Write-Host "[NUCLEAR-4T] Graph root          : $graphOut"
Write-Host "[NUCLEAR-4T] Manifest            : $manifestPath"
Write-Host "[NUCLEAR-4T] Report              : $reportPath"
Write-Host '[NUCLEAR-4T] ================================================================='

if (-not $mergePass) {
    Write-Host '[NUCLEAR-4T] PRODUCTION IMAGE GRAPH MERGE: BLOCKED' -ForegroundColor Yellow
    exit 4
}

Write-Host '[NUCLEAR-4T] PRODUCTION IMAGE GRAPH MERGE: PASS' -ForegroundColor Green
