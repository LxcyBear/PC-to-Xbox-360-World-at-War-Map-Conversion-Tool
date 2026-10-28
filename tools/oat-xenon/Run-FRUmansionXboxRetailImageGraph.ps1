param(
    [string]$XboxGameRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\XBOX GAME FILES',
    [string]$CensusManifest = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$graphUnlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($CensusManifest)) {
    $CensusManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_xbox_retail_image_census\nuclear_4r_xbox_retail_image_census.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_xbox_retail_image_graph'
}

if (-not (Test-Path -LiteralPath $XboxGameRoot -PathType Container)) {
    throw "Xbox retail game-file folder not found: $XboxGameRoot"
}
if (-not (Test-Path -LiteralPath $CensusManifest -PathType Leaf)) {
    throw "Xbox retail image census manifest not found: $CensusManifest"
}
if (-not (Test-Path -LiteralPath $graphUnlinker -PathType Leaf)) {
    throw "Specialized Xenon OAT Unlinker not found: $graphUnlinker"
}

function Normalize-ImageIdentity {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    $v = $Value.Trim().Replace('\', '/')
    while ($v.StartsWith(',', [StringComparison]::Ordinal)) {
        $v = $v.Substring(1)
    }
    if ($v.EndsWith('_wj', [StringComparison]::OrdinalIgnoreCase)) {
        $v = $v.Substring(0, $v.Length - 3)
    }
    return $v.ToLowerInvariant()
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

$census = Get-Content -LiteralPath $CensusManifest -Raw | ConvertFrom-Json
if ([string]$census.format -ne 'WaW360.Nuclear4R.XboxRetailImageCensus.v1') {
    throw "Unexpected census format: $($census.format)"
}

$matches = @($census.matches)
if ($matches.Count -eq 0) {
    throw 'Xbox retail image census contains zero stock matches.'
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$zoneDumpRoot = Join-Path $OutputRoot 'zone_dumps'
$consolidated = Join-Path $OutputRoot 'graph'
$graphDir = Join-Path $consolidated 'xenon_image_graphs'
$pixelDir = Join-Path $consolidated 'xenon_image_pixels'
$headerDir = Join-Path $consolidated 'xenon_image_headers'
$logRoot = Join-Path $OutputRoot 'zone_logs'
foreach ($path in @($zoneDumpRoot, $graphDir, $pixelDir, $headerDir, $logRoot)) {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

$zoneCache = @{}
$zoneOrder = New-Object 'System.Collections.Generic.List[string]'
$resolved = New-Object 'System.Collections.Generic.List[object]'
$unresolved = New-Object 'System.Collections.Generic.List[object]'
$totalCardMemory = [int64]0
$totalPixelBytes = [int64]0
$zoneDumpFailures = 0

function Get-ZoneGraphIndex {
    param(
        [Parameter(Mandatory=$true)][string]$ZonePath,
        [Parameter(Mandatory=$true)][string]$ZoneName
    )

    $key = [IO.Path]::GetFullPath($ZonePath).ToLowerInvariant()
    if ($zoneCache.ContainsKey($key)) {
        return $zoneCache[$key]
    }

    $safeZone = Get-SafeStem -Value ([IO.Path]::GetFileNameWithoutExtension($ZoneName))
    $ordinal = $zoneCache.Count + 1
    $dump = Join-Path $zoneDumpRoot ("{0:D3}_{1}" -f $ordinal, $safeZone)
    $log = Join-Path $logRoot ("{0:D3}_{1}.log" -f $ordinal, $safeZone)

    New-Item -ItemType Directory -Force -Path $dump | Out-Null

    Write-Host ("[NUCLEAR-4R-XBOX-GRAPH] dumping zone {0}: {1}" -f $ordinal, $ZoneName)

    $previous = $ErrorActionPreference
    $code = -1
    try {
        $ErrorActionPreference = 'Continue'
        & $graphUnlinker '--verbose' '--no-color' '--include-assets' 'image' '--image-format' 'IWI' '--output-folder' $dump $ZonePath 2>&1 |
            Out-File -LiteralPath $log -Encoding utf8
        $code = [int]$LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }

    $index = @{}
    $graphFiles = @(
        Get-ChildItem -LiteralPath (Join-Path $dump 'xenon_image_graphs') -Filter '*.json' -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName
    )

    foreach ($graphFile in $graphFiles) {
        try {
            $doc = Get-Content -LiteralPath $graphFile.FullName -Raw | ConvertFrom-Json
            if ([string]$doc.format -ne 'WaW360.T4Xenon.ImageGraph.v1') { continue }
            $name = [string]$doc.name
            $identity = Normalize-ImageIdentity -Value $name
            if ([string]::IsNullOrWhiteSpace($identity)) { continue }

            if (-not $index.ContainsKey($identity)) {
                $index[$identity] = New-Object 'System.Collections.Generic.List[object]'
            }
            $index[$identity].Add([pscustomobject]@{
                GraphFile = $graphFile.FullName
                Doc = $doc
                ListedName = $name
            })
        }
        catch {}
    }

    $result = [pscustomobject]@{
        Zone = $ZoneName
        ZonePath = $ZonePath
        DumpRoot = $dump
        Log = $log
        ExitCode = $code
        Graphs = $graphFiles.Count
        Index = $index
    }

    if ($code -ne 0) {
        $script:zoneDumpFailures++
    }

    $zoneCache[$key] = $result
    $zoneOrder.Add($key)
    return $result
}

Write-Host '[NUCLEAR-4R-XBOX-GRAPH] ======================================================'
Write-Host '[NUCLEAR-4R-XBOX-GRAPH] RETAIL XBOX STOCK IMAGE GRAPH EXTRACTION'
Write-Host '[NUCLEAR-4R-XBOX-GRAPH] ======================================================'
Write-Host "[NUCLEAR-4R-XBOX-GRAPH] Xbox root           : $XboxGameRoot"
Write-Host "[NUCLEAR-4R-XBOX-GRAPH] Census              : $CensusManifest"
Write-Host "[NUCLEAR-4R-XBOX-GRAPH] Stock identities    : $($matches.Count)"
Write-Host '[NUCLEAR-4R-XBOX-GRAPH] Production source: user-owned retail Xbox 360 WaW fastfiles.'
Write-Host '[NUCLEAR-4R-XBOX-GRAPH] No PC retail WaW dependency is used.'
Write-Host ''

$counter = 0
foreach ($match in @($matches | Sort-Object { [int]$_.Index })) {
    $counter++
    $targetName = [string]$match.Name
    $targetKey = Normalize-ImageIdentity -Value $targetName
    $chosen = $null
    $attempted = New-Object 'System.Collections.Generic.List[string]'

    foreach ($zone in @($match.Zones)) {
        $zonePath = [string]$zone.ZonePath
        $zoneName = [string]$zone.Zone
        if (-not (Test-Path -LiteralPath $zonePath -PathType Leaf)) {
            # Allow the census to move with the user's local Xbox root.
            $candidate = Join-Path $XboxGameRoot $zoneName
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $zonePath = $candidate
            }
            else {
                $recursive = @(Get-ChildItem -LiteralPath $XboxGameRoot -Filter $zoneName -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1)
                if ($recursive.Count -eq 1) { $zonePath = $recursive[0].FullName }
            }
        }
        if (-not (Test-Path -LiteralPath $zonePath -PathType Leaf)) {
            $attempted.Add("${zoneName}:not-found")
            continue
        }

        $zoneGraph = Get-ZoneGraphIndex -ZonePath $zonePath -ZoneName $zoneName
        $attempted.Add("${zoneName}:exit=$($zoneGraph.ExitCode),graphs=$($zoneGraph.Graphs)")

        if (-not $zoneGraph.Index.ContainsKey($targetKey)) { continue }

        $candidates = @($zoneGraph.Index[$targetKey].ToArray())
        if ($candidates.Count -eq 0) { continue }

        $exact = @($candidates | Where-Object { [string]$_.ListedName -ceq $targetName } | Select-Object -First 1)
        $chosenEntry = if ($exact.Count -eq 1) { $exact[0] } else { $candidates[0] }

        $chosen = [pscustomobject]@{
            Zone = $zoneName
            ZonePath = $zonePath
            ZoneClass = [string]$zone.ZoneClass
            ZoneGraph = $zoneGraph
            Entry = $chosenEntry
        }
        break
    }

    if ($null -eq $chosen) {
        $unresolved.Add([pscustomobject]@{
            Index = [int]$match.Index
            Name = $targetName
            Normalized = $targetKey
            BestClass = [string]$match.BestClass
            Attempts = [object[]]$attempted.ToArray()
        })
        Write-Host ("[NUCLEAR-4R-XBOX-GRAPH] [{0,3}/{1,3}] MISS  root #{2}: {3}" -f $counter, $matches.Count, [int]$match.Index, $targetName) -ForegroundColor Yellow
        continue
    }

    $sourceDoc = $chosen.Entry.Doc
    $sourceDump = [string]$chosen.ZoneGraph.DumpRoot
    $safe = Get-SafeStem -Value $targetName
    $prefix = ("{0:D5}_{1}" -f [int]$match.Index, $safe)

    $sourcePixel = Resolve-GraphBlob -Root $sourceDump -Relative ([string]$sourceDoc.pixelsFile)
    $sourceHeader = Resolve-GraphBlob -Root $sourceDump -Relative ([string]$sourceDoc.loadDef.textureHeaderFile)

    if (-not (Test-Path -LiteralPath $sourceHeader -PathType Leaf)) {
        $unresolved.Add([pscustomobject]@{
            Index = [int]$match.Index
            Name = $targetName
            Normalized = $targetKey
            BestClass = [string]$match.BestClass
            Attempts = @("chosen $($chosen.Zone) but texture header missing")
        })
        continue
    }

    $pixelRel = ''
    if ([int64]$sourceDoc.pixelsBytes -gt 0) {
        if (-not (Test-Path -LiteralPath $sourcePixel -PathType Leaf)) {
            $unresolved.Add([pscustomobject]@{
                Index = [int]$match.Index
                Name = $targetName
                Normalized = $targetKey
                BestClass = [string]$match.BestClass
                Attempts = @("chosen $($chosen.Zone) but pixel blob missing")
            })
            continue
        }
        $pixelRel = "xenon_image_pixels/$prefix.pixels.bin"
        Copy-Item -LiteralPath $sourcePixel -Destination (Join-Path $consolidated $pixelRel.Replace('/', '\')) -Force
        $totalPixelBytes += [int64](Get-Item -LiteralPath $sourcePixel).Length
    }

    $headerRel = "xenon_image_headers/$prefix.d3dtexture.bin"
    Copy-Item -LiteralPath $sourceHeader -Destination (Join-Path $consolidated $headerRel.Replace('/', '\')) -Force

    # Clone the Xbox-native graph but bind it to the exact FRUmansion root identity.
    $sourceDoc.name = $targetName
    $sourceDoc.pixelsFile = $pixelRel
    $sourceDoc.loadDef.textureHeaderFile = $headerRel

    $graphRel = "xenon_image_graphs/$prefix.json"
    $graphPath = Join-Path $consolidated $graphRel.Replace('/', '\')
    $sourceDoc | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $graphPath -Encoding UTF8

    $cardBytes = [int64]$sourceDoc.cardMemoryBytes
    $totalCardMemory += $cardBytes

    $resolved.Add([pscustomobject]@{
        Index = [int]$match.Index
        Name = $targetName
        Normalized = $targetKey
        BestClass = [string]$match.BestClass
        SourceZone = [string]$chosen.Zone
        SourceZoneClass = [string]$chosen.ZoneClass
        SourceZonePath = [string]$chosen.ZonePath
        SourceListedName = [string]$chosen.Entry.ListedName
        CardMemoryBytes = $cardBytes
        PixelsBytes = [int64]$sourceDoc.pixelsBytes
        GraphFile = $graphRel
        PixelsFile = $pixelRel
        TextureHeaderFile = $headerRel
    })

    if (($counter % 50) -eq 0 -or $counter -eq $matches.Count) {
        Write-Host ("[NUCLEAR-4R-XBOX-GRAPH] progress {0}/{1}: resolved={2}, unresolved={3}, zonesDumped={4}" -f
            $counter, $matches.Count, $resolved.Count, $unresolved.Count, $zoneCache.Count)
    }
}

$graphFiles = @(Get-ChildItem -LiteralPath $graphDir -Filter '*.json' -File -ErrorAction SilentlyContinue)
$pixelFiles = @(Get-ChildItem -LiteralPath $pixelDir -Filter '*.pixels.bin' -File -ErrorAction SilentlyContinue)
$headerFiles = @(Get-ChildItem -LiteralPath $headerDir -Filter '*.d3dtexture.bin' -File -ErrorAction SilentlyContinue)

$graphPass =
    $resolved.Count -gt 0 -and
    $graphFiles.Count -eq $resolved.Count -and
    $headerFiles.Count -eq $resolved.Count

$zoneRows = New-Object 'System.Collections.Generic.List[object]'
foreach ($zoneKey in $zoneOrder.ToArray()) {
    $z = $zoneCache[$zoneKey]
    $zoneRows.Add([pscustomobject]@{
        Zone = [string]$z.Zone
        ZonePath = [string]$z.ZonePath
        OatExitCode = [int]$z.ExitCode
        Graphs = [int]$z.Graphs
        Log = [string]$z.Log
    })
}

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4R.XboxRetailImageGraph.v1'
    xboxGameRoot = (Resolve-Path -LiteralPath $XboxGameRoot).Path
    censusManifest = (Resolve-Path -LiteralPath $CensusManifest).Path
    stockIdentitiesRequested = $matches.Count
    stockGraphsResolved = $resolved.Count
    stockGraphsUnresolved = $unresolved.Count
    zonesDumped = $zoneCache.Count
    zoneDumpFailures = $zoneDumpFailures
    imageGraphs = $graphFiles.Count
    pixelBlobs = $pixelFiles.Count
    textureHeaders = $headerFiles.Count
    xenonCardMemoryBytes = $totalCardMemory
    xenonPixelBytes = $totalPixelBytes
    graphRoot = $consolidated
    graphPass = $graphPass
    zones = [object[]]$zoneRows.ToArray()
    resolved = [object[]]$resolved.ToArray()
    unresolved = [object[]]$unresolved.ToArray()
    productionSourcePolicy = @(
        'Stock/base image payloads are sourced locally from the user-owned retail Xbox 360 WaW fastfiles.',
        'The extracted payloads are already Xenon/Xbox-native; no PC retail WaW install is required.',
        'The converter repository does not contain or distribute these retail payloads.',
        'The native FRUmansion oracle remains identity/order/validation evidence and is not used as the stock payload source for this stage.'
    )
}

$manifestPath = Join-Path $OutputRoot 'nuclear_4r_xbox_retail_image_graph.json'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$reportPath = Join-Path $OutputRoot 'nuclear_4r_xbox_retail_image_graph.txt'
$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4R-XBOX-GRAPH - RETAIL XBOX STOCK IMAGE GRAPH')
$report.Add(('=' * 96))
$report.Add(("Stock identities requested: {0:N0}" -f $matches.Count))
$report.Add(("Stock graphs resolved:      {0:N0}" -f $resolved.Count))
$report.Add(("Stock graphs unresolved:    {0:N0}" -f $unresolved.Count))
$report.Add(("Retail zones dumped:        {0:N0}" -f $zoneCache.Count))
$report.Add(("Zone dump failures:         {0:N0}" -f $zoneDumpFailures))
$report.Add(("Graph files:                {0:N0}" -f $graphFiles.Count))
$report.Add(("Pixel blobs:                {0:N0}" -f $pixelFiles.Count))
$report.Add(("Texture headers:            {0:N0}" -f $headerFiles.Count))
$report.Add(("Xenon cardMemory bytes:     {0:N0}" -f $totalCardMemory))
$report.Add(("Xenon pixel bytes:          {0:N0}" -f $totalPixelBytes))
$report.Add(("GRAPH PASS:                 {0}" -f $graphPass))
$report.Add('')
$report.Add('UNRESOLVED STOCK IDENTITIES')
foreach ($u in $unresolved.ToArray()) {
    $report.Add(("  root #{0,4}: {1} | {2}" -f $u.Index, $u.Name, (($u.Attempts | ForEach-Object { [string]$_ }) -join '; ')))
}
[IO.File]::WriteAllLines($reportPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4R-XBOX-GRAPH] ====================== RESULT ========================='
Write-Host "[NUCLEAR-4R-XBOX-GRAPH] Requested stock roots : $($matches.Count)"
Write-Host "[NUCLEAR-4R-XBOX-GRAPH] Resolved stock graphs : $($resolved.Count)"
Write-Host "[NUCLEAR-4R-XBOX-GRAPH] Unresolved stock graph: $($unresolved.Count)"
Write-Host "[NUCLEAR-4R-XBOX-GRAPH] Retail zones dumped   : $($zoneCache.Count)"
Write-Host "[NUCLEAR-4R-XBOX-GRAPH] OAT zone failures     : $zoneDumpFailures"
Write-Host "[NUCLEAR-4R-XBOX-GRAPH] Xenon cardMemory      : $totalCardMemory bytes"
Write-Host "[NUCLEAR-4R-XBOX-GRAPH] Graph root            : $consolidated"
Write-Host "[NUCLEAR-4R-XBOX-GRAPH] Manifest              : $manifestPath"
Write-Host "[NUCLEAR-4R-XBOX-GRAPH] Report                : $reportPath"
Write-Host "[NUCLEAR-4R-XBOX-GRAPH] Graph pass            : $graphPass"
Write-Host '[NUCLEAR-4R-XBOX-GRAPH] ================================================================='

if (-not $graphPass) {
    Write-Host '[NUCLEAR-4R-XBOX-GRAPH] PARTIAL: preserved every verified Xbox-native graph; unresolved stock identities remain fenced.' -ForegroundColor Yellow
    exit 4
}

Write-Host '[NUCLEAR-4R-XBOX-GRAPH] PASS: all retail-Xbox stock matches have verified Xbox-native image graphs.' -ForegroundColor Green
