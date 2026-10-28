param(
    [string]$XboxGameRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\XBOX GAME FILES',
    [string]$ResidualManifest = '',
    [string]$OutputRoot = '',
    [switch]$ReuseExistingDumps
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$graphUnlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($ResidualManifest)) {
    $ResidualManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_residual_image_forensics\nuclear_4r_residual_image_forensics.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_xbox_retail_deep_image_search'
}

if (-not (Test-Path -LiteralPath $XboxGameRoot -PathType Container)) {
    throw "Xbox retail root not found: $XboxGameRoot"
}
if (-not (Test-Path -LiteralPath $ResidualManifest -PathType Leaf)) {
    throw "Residual forensic manifest not found: $ResidualManifest"
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
    if ($v.Length -gt 80) { $v = $v.Substring(0, 80) }
    if ([string]::IsNullOrWhiteSpace($v)) { $v = 'zone' }
    return $v
}

$residual = Get-Content -LiteralPath $ResidualManifest -Raw | ConvertFrom-Json
if ([string]$residual.format -ne 'WaW360.Nuclear4R.ResidualImageForensics.v1') {
    throw "Unexpected residual manifest format: $($residual.format)"
}

$targets = @($residual.images)
if ($targets.Count -eq 0) {
    throw 'Residual forensic manifest contains zero images.'
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
    Get-ChildItem -LiteralPath $XboxGameRoot -Filter '*.ff' -File -Recurse -ErrorAction Stop |
    Sort-Object FullName
)
if ($fastFiles.Count -eq 0) {
    throw "No retail Xbox .ff files found under: $XboxGameRoot"
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

Write-Host '[NUCLEAR-4R-XBOX-DEEP] ======================================================='
Write-Host '[NUCLEAR-4R-XBOX-DEEP] RETAIL XBOX DEEP IMAGE PAYLOAD SEARCH'
Write-Host '[NUCLEAR-4R-XBOX-DEEP] ======================================================='
Write-Host "[NUCLEAR-4R-XBOX-DEEP] Residual targets : $($targets.Count)"
Write-Host "[NUCLEAR-4R-XBOX-DEEP] Retail FFs       : $($fastFiles.Count)"
Write-Host "[NUCLEAR-4R-XBOX-DEEP] Xbox root        : $XboxGameRoot"
Write-Host '[NUCLEAR-4R-XBOX-DEEP] This searches actual extracted Xenon image graphs, not only OAT --list identities.'
Write-Host '[NUCLEAR-4R-XBOX-DEEP] Retail payloads are read locally; nothing is committed or distributed.'
Write-Host ''

$ordinal = 0
foreach ($ff in $fastFiles) {
    $ordinal++
    $safe = Get-SafeStem -Value ([IO.Path]::GetFileNameWithoutExtension($ff.Name))
    $zoneDump = Join-Path $dumpRoot ("{0:D3}_{1}" -f $ordinal, $safe)
    $zoneLog = Join-Path $logRoot ("{0:D3}_{1}.log" -f $ordinal, $safe)

    $canReuse = $ReuseExistingDumps.IsPresent -and (Test-Path -LiteralPath $zoneDump -PathType Container)
    $exitCode = 0

    if (-not $canReuse) {
        if (Test-Path -LiteralPath $zoneDump) {
            Remove-Item -LiteralPath $zoneDump -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path $zoneDump | Out-Null

        Write-Host ("[NUCLEAR-4R-XBOX-DEEP] [{0,3}/{1,3}] dumping {2}" -f $ordinal, $fastFiles.Count, $ff.Name)

        $savedPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & $graphUnlinker '--verbose' '--no-color' '--include-assets' 'image' '--image-format' 'IWI' '--output-folder' $zoneDump $ff.FullName 2>&1 |
                Out-File -LiteralPath $zoneLog -Encoding utf8
            $exitCode = [int]$LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $savedPreference
        }
    }
    else {
        Write-Host ("[NUCLEAR-4R-XBOX-DEEP] [{0,3}/{1,3}] reusing {2}" -f $ordinal, $fastFiles.Count, $ff.Name)
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
        Zone = $ff.Name
        ZonePath = $ff.FullName
        ExitCode = $exitCode
        Graphs = $graphs.Count
        ResidualMatches = $zoneMatches
        DumpRoot = $zoneDump
        Log = $zoneLog
    })

    if ($zoneMatches -gt 0) {
        Write-Host ("[NUCLEAR-4R-XBOX-DEEP]            -> {0} residual match(es)" -f $zoneMatches) -ForegroundColor Cyan
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
            PriorClassification = [string]$target.Classification
            Normalized = $key
        })
        continue
    }

    $hits = @(
        $foundByKey[$key].ToArray() |
        Sort-Object Zone, GraphFile
    )

    $resolved.Add([pscustomobject]@{
        Index = [int]$target.Index
        Name = [string]$target.Name
        PriorClassification = [string]$target.Classification
        Normalized = $key
        RetailHits = $hits.Count
        Zones = @($hits | Select-Object -ExpandProperty Zone -Unique)
        Hits = $hits
    })
}

$resolvedArray = [object[]]$resolved.ToArray()
$unresolvedArray = [object[]]$unresolved.ToArray()

$nativeInlineResolved = @(
    $resolvedArray |
    Where-Object { $_.PriorClassification -in @('native-inline-image-evidence','native-inline-iwi-evidence') }
)
$ordinaryResolved = @(
    $resolvedArray |
    Where-Object { $_.PriorClassification -eq 'unresolved-ordinary' }
)

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4R.XboxRetailDeepImageSearch.v1'
    xboxGameRoot = (Resolve-Path -LiteralPath $XboxGameRoot).Path
    residualManifest = (Resolve-Path -LiteralPath $ResidualManifest).Path
    residualTargets = $targets.Count
    retailFastFilesScanned = $fastFiles.Count
    zoneFailures = $zoneFailures
    imageGraphsScanned = $totalGraphs
    resolvedFromRetailPayloads = $resolved.Count
    unresolvedAfterRetailPayloadSearch = $unresolved.Count
    nativeInlineResolvedFromRetail = $nativeInlineResolved.Count
    unresolvedOrdinaryResolvedFromRetail = $ordinaryResolved.Count
    zones = [object[]]$zoneStats.ToArray()
    resolved = $resolvedArray
    unresolved = $unresolvedArray
    policy = @(
        'This stage performs a deep payload-level search of user-owned retail Xbox 360 WaW fastfiles.',
        'A deep match proves that a residual identity has a real Xenon image graph in retail Xbox content even if it was absent from the earlier --list census.',
        'No retail payload is committed, uploaded, or distributed by this stage.',
        'Native FRUmansion/oracle payloads remain evidence only.'
    )
}

$jsonPath = Join-Path $OutputRoot 'nuclear_4r_xbox_retail_deep_image_search.json'
$txtPath = Join-Path $OutputRoot 'nuclear_4r_xbox_retail_deep_image_search.txt'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4R-XBOX-DEEP - RETAIL XBOX DEEP IMAGE PAYLOAD SEARCH')
$report.Add(('=' * 96))
$report.Add(("Residual targets:                    {0:N0}" -f $targets.Count))
$report.Add(("Retail fastfiles scanned:            {0:N0}" -f $fastFiles.Count))
$report.Add(("OAT zone failures:                   {0:N0}" -f $zoneFailures))
$report.Add(("Image graphs scanned:                {0:N0}" -f $totalGraphs))
$report.Add(("Resolved from retail payloads:       {0:N0}" -f $resolved.Count))
$report.Add(("Still unresolved:                    {0:N0}" -f $unresolved.Count))
$report.Add(("Native-inline resolved from retail:  {0:N0}" -f $nativeInlineResolved.Count))
$report.Add(("Ordinary unresolved found in retail: {0:N0}" -f $ordinaryResolved.Count))
$report.Add('')
$report.Add('RESOLVED')
foreach ($row in $resolvedArray) {
    $report.Add(("  root #{0,4}: {1} <- {2}" -f $row.Index, $row.Name, ($row.Zones -join '; ')))
}
$report.Add('')
$report.Add('STILL UNRESOLVED')
foreach ($row in $unresolvedArray) {
    $report.Add(("  root #{0,4}: {1} [{2}]" -f $row.Index, $row.Name, $row.PriorClassification))
}
[IO.File]::WriteAllLines($txtPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4R-XBOX-DEEP] ====================== RESULT =========================='
Write-Host "[NUCLEAR-4R-XBOX-DEEP] Residual targets                   : $($targets.Count)"
Write-Host "[NUCLEAR-4R-XBOX-DEEP] Retail FFs scanned                 : $($fastFiles.Count)"
Write-Host "[NUCLEAR-4R-XBOX-DEEP] OAT zone failures                  : $zoneFailures"
Write-Host "[NUCLEAR-4R-XBOX-DEEP] Image graphs scanned               : $totalGraphs"
Write-Host "[NUCLEAR-4R-XBOX-DEEP] Resolved from retail payloads      : $($resolved.Count)"
Write-Host "[NUCLEAR-4R-XBOX-DEEP] Still unresolved                   : $($unresolved.Count)"
Write-Host "[NUCLEAR-4R-XBOX-DEEP] Native-inline resolved from retail : $($nativeInlineResolved.Count)"
Write-Host "[NUCLEAR-4R-XBOX-DEEP] rhi3/ordinary resolved from retail : $($ordinaryResolved.Count)"
Write-Host "[NUCLEAR-4R-XBOX-DEEP] Manifest                           : $jsonPath"
Write-Host "[NUCLEAR-4R-XBOX-DEEP] Report                             : $txtPath"
Write-Host '[NUCLEAR-4R-XBOX-DEEP] ================================================================='
Write-Host '[NUCLEAR-4R-XBOX-DEEP] PASS: deep retail payload census complete.' -ForegroundColor Green
