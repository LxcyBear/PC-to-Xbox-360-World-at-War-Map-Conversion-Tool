param(
    [string]$MapAssetsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\map assets',
    [string]$RetailDeepManifest = '',
    [string]$OutputRoot = '',
    [switch]$ReuseExistingDumps
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$graphUnlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($RetailDeepManifest)) {
    $RetailDeepManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_xbox_retail_deep_image_search\nuclear_4r_xbox_retail_deep_image_search.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_map_asset_deep_image_search'
}

if (-not (Test-Path -LiteralPath $MapAssetsRoot -PathType Container)) {
    throw "Packaged-map corpus root not found: $MapAssetsRoot"
}
if (-not (Test-Path -LiteralPath $RetailDeepManifest -PathType Leaf)) {
    throw "Retail deep-search manifest not found: $RetailDeepManifest"
}
if (-not (Test-Path -LiteralPath $graphUnlinker -PathType Leaf)) {
    throw "Specialized Xenon OAT Unlinker not found: $graphUnlinker"
}

function Normalize-Identity {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }

    $v = $Value.Trim().Replace('\', '/').TrimStart('/')
    while ($v.StartsWith(',', [StringComparison]::Ordinal)) {
        $v = $v.Substring(1)
    }

    $marker = $v.ToLowerInvariant().LastIndexOf('/images/')
    if ($marker -ge 0) {
        $v = $v.Substring($marker + 8)
    }
    elseif ($v.StartsWith('images/', [StringComparison]::OrdinalIgnoreCase)) {
        $v = $v.Substring(7)
    }

    foreach ($ext in @('.iwi', '.dds')) {
        if ($v.EndsWith($ext, [StringComparison]::OrdinalIgnoreCase)) {
            $v = $v.Substring(0, $v.Length - $ext.Length)
            break
        }
    }

    if ($v.EndsWith('_wj', [StringComparison]::OrdinalIgnoreCase)) {
        $v = $v.Substring(0, $v.Length - 3)
    }

    return $v.ToLowerInvariant()
}

function Get-SafeStem {
    param([string]$Value)
    $v = $Value -replace '[^A-Za-z0-9_.-]', '_'
    if ($v.Length -gt 70) { $v = $v.Substring(0, 70) }
    if ([string]::IsNullOrWhiteSpace($v)) { $v = 'item' }
    return $v
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

function Get-PackageName {
    param([string]$Root, [string]$Path)

    $relative = Get-RelativePathCompat -BasePath $Root -TargetPath $Path
    $relative = $relative.Replace('\', '/').TrimStart('/')
    if ([string]::IsNullOrWhiteSpace($relative)) { return '(root)' }

    $slash = $relative.IndexOf('/')
    if ($slash -lt 0) { return '(root)' }

    return $relative.Substring(0, $slash)
}

$retailDeep = Get-Content -LiteralPath $RetailDeepManifest -Raw | ConvertFrom-Json
if ([string]$retailDeep.format -ne 'WaW360.Nuclear4R.XboxRetailDeepImageSearch.v1') {
    throw "Unexpected retail deep-search format: $($retailDeep.format)"
}

$targets = @($retailDeep.unresolved)
if ($targets.Count -eq 0) {
    Write-Host '[NUCLEAR-4R-MAPS-DEEP] Retail deep search already resolved every residual image.'
    exit 0
}

$targetByKey = @{}
foreach ($target in $targets) {
    $key = Normalize-Identity -Value ([string]$target.Name)
    if ([string]::IsNullOrWhiteSpace($key)) { continue }
    if (-not $targetByKey.ContainsKey($key)) {
        $targetByKey[$key] = New-Object 'System.Collections.Generic.List[object]'
    }
    $targetByKey[$key].Add($target)
}

$fastFiles = @(
    Get-ChildItem -LiteralPath $MapAssetsRoot -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue |
    Sort-Object FullName
)
if ($fastFiles.Count -eq 0) {
    throw "No packaged-map .ff files found under: $MapAssetsRoot"
}

if (-not $ReuseExistingDumps.IsPresent -and (Test-Path -LiteralPath $OutputRoot)) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$dumpRoot = Join-Path $OutputRoot 'zone_dumps'
$logRoot = Join-Path $OutputRoot 'zone_logs'
New-Item -ItemType Directory -Force -Path $dumpRoot | Out-Null
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

$foundByKey = @{}
$zoneStats = New-Object 'System.Collections.Generic.List[object]'
$zoneFailures = 0
$totalGraphs = 0

Write-Host '[NUCLEAR-4R-MAPS-DEEP] ======================================================='
Write-Host '[NUCLEAR-4R-MAPS-DEEP] PACKAGED-MAP DEEP IMAGE PAYLOAD SEARCH'
Write-Host '[NUCLEAR-4R-MAPS-DEEP] ======================================================='
Write-Host "[NUCLEAR-4R-MAPS-DEEP] Residual targets : $($targets.Count)"
Write-Host "[NUCLEAR-4R-MAPS-DEEP] Packaged FFs     : $($fastFiles.Count)"
Write-Host "[NUCLEAR-4R-MAPS-DEEP] Corpus root      : $MapAssetsRoot"
Write-Host '[NUCLEAR-4R-MAPS-DEEP] This searches emitted image-graph identities, not only dumped IWI filenames.'
Write-Host '[NUCLEAR-4R-MAPS-DEEP] No packaged-map payload is auto-grafted by this search.'
Write-Host ''

$ordinal = 0
foreach ($ff in $fastFiles) {
    $ordinal++

    $packageName = Get-PackageName -Root $MapAssetsRoot -Path $ff.FullName
    $safePackage = Get-SafeStem -Value $packageName
    $safeZone = Get-SafeStem -Value ([IO.Path]::GetFileNameWithoutExtension($ff.Name))
    $zoneDump = Join-Path $dumpRoot ("{0:D3}_{1}_{2}" -f $ordinal, $safePackage, $safeZone)
    $zoneLog = Join-Path $logRoot ("{0:D3}_{1}_{2}.log" -f $ordinal, $safePackage, $safeZone)

    $canReuse = $ReuseExistingDumps.IsPresent -and (Test-Path -LiteralPath $zoneDump -PathType Container)
    $exitCode = 0

    if (-not $canReuse) {
        if (Test-Path -LiteralPath $zoneDump) {
            Remove-Item -LiteralPath $zoneDump -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path $zoneDump | Out-Null

        Write-Host ("[NUCLEAR-4R-MAPS-DEEP] [{0,2}/{1,2}] {2} :: {3}" -f $ordinal, $fastFiles.Count, $packageName, $ff.Name)

        $searchPaths = New-Object 'System.Collections.Generic.List[string]'
        $seenPaths = @{}

        $packageRoot = Split-Path -Parent $ff.FullName
        foreach ($candidatePath in @($MapAssetsRoot, $packageRoot)) {
            if (-not (Test-Path -LiteralPath $candidatePath -PathType Container)) { continue }
            $full = (Resolve-Path -LiteralPath $candidatePath).Path
            $key = $full.ToLowerInvariant()
            if ($seenPaths.ContainsKey($key)) { continue }
            $seenPaths[$key] = $true
            $searchPaths.Add($full)
        }

        $args = @(
            '--verbose',
            '--no-color',
            '--include-assets', 'image',
            '--image-format', 'IWI',
            '--output-folder', $zoneDump
        )
        if ($searchPaths.Count -gt 0) {
            $args += @('--search-path', (($searchPaths.ToArray()) -join ';'))
        }
        $args += $ff.FullName

        $savedPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & $graphUnlinker @args 2>&1 |
                Out-File -LiteralPath $zoneLog -Encoding utf8
            $exitCode = [int]$LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $savedPreference
        }
    }
    else {
        Write-Host ("[NUCLEAR-4R-MAPS-DEEP] [{0,2}/{1,2}] reusing {2} :: {3}" -f $ordinal, $fastFiles.Count, $packageName, $ff.Name)
    }

    if ($exitCode -ne 0) { $zoneFailures++ }

    $graphDir = Join-Path $zoneDump 'xenon_image_graphs'
    $graphs = @()
    if (Test-Path -LiteralPath $graphDir -PathType Container) {
        $graphs = @(
            Get-ChildItem -LiteralPath $graphDir -Filter '*.json' -File -Recurse -ErrorAction SilentlyContinue |
            Sort-Object FullName
        )
    }
    $totalGraphs += $graphs.Count

    $zoneMatches = 0
    foreach ($graphFile in $graphs) {
        try {
            $doc = Get-Content -LiteralPath $graphFile.FullName -Raw | ConvertFrom-Json
            if ([string]$doc.format -ne 'WaW360.T4Xenon.ImageGraph.v1') { continue }

            $name = [string]$doc.name
            $key = Normalize-Identity -Value $name
            if (-not $targetByKey.ContainsKey($key)) { continue }

            if (-not $foundByKey.ContainsKey($key)) {
                $foundByKey[$key] = New-Object 'System.Collections.Generic.List[object]'
            }

            $foundByKey[$key].Add([pscustomobject]@{
                Package = $packageName
                Zone = $ff.Name
                ZonePath = $ff.FullName
                ListedName = $name
                GraphFile = $graphFile.FullName
                CardMemoryBytes = [int64]$doc.cardMemoryBytes
                PixelsBytes = [int64]$doc.pixelsBytes
                Width = [int]$doc.width
                Height = [int]$doc.height
                Depth = [int]$doc.depth
                Streaming = [bool]$doc.streaming
                DelayLoadPixels = [bool]$doc.delayLoadPixels
            })
            $zoneMatches++
        }
        catch {}
    }

    $zoneStats.Add([pscustomobject]@{
        Package = $packageName
        Zone = $ff.Name
        ZonePath = $ff.FullName
        ExitCode = $exitCode
        Graphs = $graphs.Count
        ResidualMatches = $zoneMatches
        DumpRoot = $zoneDump
        Log = $zoneLog
    })

    if ($zoneMatches -gt 0) {
        Write-Host ("[NUCLEAR-4R-MAPS-DEEP]            -> {0} residual graph match(es)" -f $zoneMatches) -ForegroundColor Cyan
    }
}

$resolved = New-Object 'System.Collections.Generic.List[object]'
$unresolved = New-Object 'System.Collections.Generic.List[object]'

foreach ($target in ($targets | Sort-Object { [int]$_.Index })) {
    $key = Normalize-Identity -Value ([string]$target.Name)

    if (-not $foundByKey.ContainsKey($key)) {
        $unresolved.Add([pscustomobject]@{
            Index = [int]$target.Index
            Name = [string]$target.Name
            PriorClassification = [string]$target.PriorClassification
            Normalized = $key
        })
        continue
    }

    $hits = @(
        $foundByKey[$key].ToArray() |
        Sort-Object Package, Zone, GraphFile
    )

    $resolved.Add([pscustomobject]@{
        Index = [int]$target.Index
        Name = [string]$target.Name
        PriorClassification = [string]$target.PriorClassification
        Normalized = $key
        Hits = $hits.Count
        Packages = @($hits | Select-Object -ExpandProperty Package -Unique)
        Zones = @($hits | Select-Object -ExpandProperty Zone -Unique)
        Graphs = $hits
    })
}

$resolvedArray = [object[]]$resolved.ToArray()
$unresolvedArray = [object[]]$unresolved.ToArray()

$packageResolution = @(
    $resolvedArray |
    ForEach-Object {
        foreach ($package in @($_.Packages)) {
            [pscustomobject]@{ Package = [string]$package; Name = [string]$_.Name }
        }
    } |
    Group-Object Package |
    Sort-Object @{ Expression = { $_.Count }; Descending = $true }, Name |
    ForEach-Object {
        [pscustomobject]@{
            Package = $_.Name
            ResolvedIdentities = @($_.Group | Select-Object -ExpandProperty Name -Unique).Count
        }
    }
)

$nativeInlineResolved = @(
    $resolvedArray |
    Where-Object { $_.PriorClassification -in @('native-inline-image-evidence','native-inline-iwi-evidence') }
)
$ordinaryResolved = @(
    $resolvedArray |
    Where-Object { $_.PriorClassification -eq 'unresolved-ordinary' }
)

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4R.MapAssetDeepImageSearch.v1'
    mapAssetsRoot = (Resolve-Path -LiteralPath $MapAssetsRoot).Path
    retailDeepManifest = (Resolve-Path -LiteralPath $RetailDeepManifest).Path
    residualTargets = $targets.Count
    packagedFastFilesScanned = $fastFiles.Count
    zoneFailures = $zoneFailures
    imageGraphsScanned = $totalGraphs
    resolvedFromPackagedMapGraphs = $resolved.Count
    unresolvedAfterPackagedMapGraphs = $unresolved.Count
    nativeInlineResolvedFromPackagedMaps = $nativeInlineResolved.Count
    unresolvedOrdinaryResolvedFromPackagedMaps = $ordinaryResolved.Count
    packageResolution = $packageResolution
    zones = [object[]]$zoneStats.ToArray()
    resolved = $resolvedArray
    unresolved = $unresolvedArray
    policy = @(
        'This stage deeply searches image-graph identities emitted from user-supplied packaged custom-map fastfiles.',
        'Matches are dependency/provenance evidence and are not automatically grafted into FRUmansion.',
        'Production reuse still requires a legitimate source basis plus Xenon conversion/graft and 4F validation.',
        'Retail Xbox remains authoritative for stock/base assets when a retail payload exists.'
    )
}

$jsonPath = Join-Path $OutputRoot 'nuclear_4r_map_asset_deep_image_search.json'
$txtPath = Join-Path $OutputRoot 'nuclear_4r_map_asset_deep_image_search.txt'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4R-MAPS-DEEP - PACKAGED-MAP DEEP IMAGE PAYLOAD SEARCH')
$report.Add(('=' * 96))
$report.Add(("Residual targets:                         {0:N0}" -f $targets.Count))
$report.Add(("Packaged fastfiles scanned:               {0:N0}" -f $fastFiles.Count))
$report.Add(("OAT zone failures:                        {0:N0}" -f $zoneFailures))
$report.Add(("Image graphs scanned:                     {0:N0}" -f $totalGraphs))
$report.Add(("Resolved from packaged-map graphs:        {0:N0}" -f $resolved.Count))
$report.Add(("Still unresolved:                         {0:N0}" -f $unresolved.Count))
$report.Add(("Native-inline resolved from packaged maps:{0,6:N0}" -f $nativeInlineResolved.Count))
$report.Add(("Ordinary/rhi3 resolved from packaged maps:{0,6:N0}" -f $ordinaryResolved.Count))
$report.Add('')
$report.Add('PACKAGE RESOLUTION')
foreach ($row in $packageResolution) {
    $report.Add(("  {0,-40} {1,4:N0}" -f $row.Package, $row.ResolvedIdentities))
}
$report.Add('')
$report.Add('RESOLVED')
foreach ($row in $resolvedArray) {
    $report.Add(("  root #{0,4}: {1} <- {2}" -f $row.Index, $row.Name, ($row.Packages -join '; ')))
}
$report.Add('')
$report.Add('STILL UNRESOLVED')
foreach ($row in $unresolvedArray) {
    $report.Add(("  root #{0,4}: {1} [{2}]" -f $row.Index, $row.Name, $row.PriorClassification))
}
[IO.File]::WriteAllLines($txtPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4R-MAPS-DEEP] ====================== RESULT =========================='
Write-Host "[NUCLEAR-4R-MAPS-DEEP] Residual targets                        : $($targets.Count)"
Write-Host "[NUCLEAR-4R-MAPS-DEEP] Packaged FFs scanned                    : $($fastFiles.Count)"
Write-Host "[NUCLEAR-4R-MAPS-DEEP] OAT zone failures                       : $zoneFailures"
Write-Host "[NUCLEAR-4R-MAPS-DEEP] Image graphs scanned                    : $totalGraphs"
Write-Host "[NUCLEAR-4R-MAPS-DEEP] Resolved from packaged-map graphs       : $($resolved.Count)"
Write-Host "[NUCLEAR-4R-MAPS-DEEP] Still unresolved                        : $($unresolved.Count)"
Write-Host "[NUCLEAR-4R-MAPS-DEEP] Native-inline resolved from packaged map: $($nativeInlineResolved.Count)"
Write-Host "[NUCLEAR-4R-MAPS-DEEP] rhi3/ordinary resolved from packaged map: $($ordinaryResolved.Count)"
foreach ($row in $packageResolution) {
    Write-Host ("[NUCLEAR-4R-MAPS-DEEP]   {0}: {1} resolved" -f $row.Package, $row.ResolvedIdentities)
}
Write-Host "[NUCLEAR-4R-MAPS-DEEP] Manifest                                : $jsonPath"
Write-Host "[NUCLEAR-4R-MAPS-DEEP] Report                                  : $txtPath"
Write-Host '[NUCLEAR-4R-MAPS-DEEP] ================================================================='
Write-Host '[NUCLEAR-4R-MAPS-DEEP] PASS: packaged-map deep payload census complete; nothing was auto-grafted.' -ForegroundColor Green
