[CmdletBinding()]
param(
    [string]$XboxGameRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\XBOX GAME FILES',
    [string]$Stage5EManifest = '',
    [string]$NativeImageGraphRoot = '',
    [string]$ExistingRetailDumpRoot = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$graphUnlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($Stage5EManifest)) {
    $Stage5EManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_5e_frumansion_pc_ff_identity_census\nuclear_5e_frumansion_pc_ff_identity_census.json'
}
if ([string]::IsNullOrWhiteSpace($NativeImageGraphRoot)) {
    $NativeImageGraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4n_xenon_image_graph\dump'
}
if ([string]::IsNullOrWhiteSpace($ExistingRetailDumpRoot)) {
    $ExistingRetailDumpRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_xbox_retail_deep_image_search\zone_dumps'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_5f_oracle_retail_image_equivalence'
}

foreach ($required in @($Stage5EManifest,$graphUnlinker)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required file not found: $required"
    }
}
if (-not (Test-Path -LiteralPath $NativeImageGraphRoot -PathType Container)) {
    throw "Native validation image graph root not found: $NativeImageGraphRoot"
}
if (-not (Test-Path -LiteralPath $XboxGameRoot -PathType Container)) {
    throw "Retail Xbox game root not found: $XboxGameRoot"
}

function Normalize-Identity {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }

    $v=$Value.Trim().Trim('"').Replace('\','/').TrimStart('/')
    while ($v.StartsWith(',', [StringComparison]::Ordinal)) {
        $v=$v.Substring(1)
    }

    if ($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)) {
        $v=$v.Substring(0,$v.Length-3)
    }

    return $v.ToLowerInvariant()
}

function Get-GraphContainerRoot {
    param([string]$GraphFile)

    $full=[IO.Path]::GetFullPath($GraphFile)
    $needle=[IO.Path]::DirectorySeparatorChar + 'xenon_image_graphs' + [IO.Path]::DirectorySeparatorChar
    $idx=$full.IndexOf($needle,[StringComparison]::OrdinalIgnoreCase)
    if ($idx -lt 0) {
        throw "Graph path is not under xenon_image_graphs: $GraphFile"
    }

    return $full.Substring(0,$idx)
}

function Resolve-GraphBlob {
    param(
        [string]$GraphFile,
        [string]$RelativePath
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath)) { return $null }
    $root=Get-GraphContainerRoot -GraphFile $GraphFile
    return Join-Path $root $RelativePath.Replace('/','\').TrimStart('\')
}

function Get-GraphShape {
    param([object]$Doc)

    $format=-1
    $levels=-1
    $flags=-1
    if ($null -ne $Doc.loadDef) {
        if ($Doc.loadDef.PSObject.Properties.Name -contains 'format') { $format=[int]$Doc.loadDef.format }
        if ($Doc.loadDef.PSObject.Properties.Name -contains 'levelCount') { $levels=[int]$Doc.loadDef.levelCount }
        if ($Doc.loadDef.PSObject.Properties.Name -contains 'flags') { $flags=[int]$Doc.loadDef.flags }
    }

    return [pscustomobject]@{
        Width=[int]$Doc.width
        Height=[int]$Doc.height
        Depth=[int]$Doc.depth
        MapType=[int]$Doc.mapType
        Semantic=[int]$Doc.semantic
        CardMemoryBytes=[int64]$Doc.cardMemoryBytes
        PixelsBytes=[int64]$Doc.pixelsBytes
        Format=$format
        LevelCount=$levels
        Flags=$flags
    }
}

function Test-ExactShape {
    param(
        [object]$A,
        [object]$B
    )

    return (
        [int]$A.Width -eq [int]$B.Width -and
        [int]$A.Height -eq [int]$B.Height -and
        [int]$A.Depth -eq [int]$B.Depth -and
        [int]$A.MapType -eq [int]$B.MapType -and
        [int]$A.Semantic -eq [int]$B.Semantic -and
        [int64]$A.CardMemoryBytes -eq [int64]$B.CardMemoryBytes -and
        [int64]$A.PixelsBytes -eq [int64]$B.PixelsBytes -and
        [int]$A.Format -eq [int]$B.Format -and
        [int]$A.LevelCount -eq [int]$B.LevelCount -and
        [int]$A.Flags -eq [int]$B.Flags
    )
}

function Load-GraphRecord {
    param(
        [string]$GraphFile,
        [string]$Zone,
        [string]$ZonePath
    )

    try {
        $doc=Get-Content -LiteralPath $GraphFile -Raw | ConvertFrom-Json
        if ([string]$doc.format -ne 'WaW360.T4Xenon.ImageGraph.v1') { return $null }

        $pixelPath=Resolve-GraphBlob -GraphFile $GraphFile -RelativePath ([string]$doc.pixelsFile)
        if ([int64]$doc.pixelsBytes -le 0 -or
            [string]::IsNullOrWhiteSpace([string]$doc.pixelsFile) -or
            -not (Test-Path -LiteralPath $pixelPath -PathType Leaf)) {
            return [pscustomobject]@{
                Name=[string]$doc.name
                GraphFile=$GraphFile
                Zone=$Zone
                ZonePath=$ZonePath
                PixelFile=$pixelPath
                PixelHash=''
                Shape=Get-GraphShape -Doc $doc
                HasPixelPayload=$false
            }
        }

        $hash=(Get-FileHash -LiteralPath $pixelPath -Algorithm SHA256).Hash.ToLowerInvariant()
        return [pscustomobject]@{
            Name=[string]$doc.name
            GraphFile=$GraphFile
            Zone=$Zone
            ZonePath=$ZonePath
            PixelFile=$pixelPath
            PixelHash=$hash
            Shape=Get-GraphShape -Doc $doc
            HasPixelPayload=$true
        }
    }
    catch {
        return $null
    }
}

$stage5e=Get-Content -LiteralPath $Stage5EManifest -Raw | ConvertFrom-Json
if ([string]$stage5e.format -ne 'WaW360.Nuclear5E.FRumansionPcFastFileIdentityCensus.v1' -or
    [int]$stage5e.targets -ne 43 -or
    [int]$stage5e.foundInFrumansionPcFastFiles -ne 43 -or
    [int]$stage5e.targetsWithStage4RDumpErrors -ne 43) {
    throw 'Stage 5F requires the passing 43/43 Stage 5E FRUmansion PC fastfile identity proof.'
}

$targets=@($stage5e.images | Sort-Object Index)
if ($targets.Count -ne 43) {
    throw "Expected 43 Stage 5E targets; found $($targets.Count)."
}

$nativeGraphDir=Join-Path $NativeImageGraphRoot 'xenon_image_graphs'
if (-not (Test-Path -LiteralPath $nativeGraphDir -PathType Container)) {
    throw "Native validation xenon_image_graphs directory not found: $nativeGraphDir"
}

$nativeIndex=@{}
foreach ($file in @(Get-ChildItem -LiteralPath $nativeGraphDir -Filter '*.json' -File -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName)) {
    $record=Load-GraphRecord -GraphFile $file.FullName -Zone 'FRUmansion-validation-oracle' -ZonePath ''
    if ($null -eq $record) { continue }

    $key=Normalize-Identity -Value ([string]$record.Name)
    if ([string]::IsNullOrWhiteSpace($key)) { continue }

    if (-not $nativeIndex.ContainsKey($key)) {
        $nativeIndex[$key]=New-Object 'System.Collections.Generic.List[object]'
    }
    $nativeIndex[$key].Add($record)
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$retailDumpRoot=$ExistingRetailDumpRoot
$retailGraphFiles=@()

if (Test-Path -LiteralPath $retailDumpRoot -PathType Container) {
    $retailGraphFiles=@(
        Get-ChildItem -LiteralPath $retailDumpRoot -Filter '*.json' -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/]xenon_image_graphs[\\/]' } |
        Sort-Object FullName
    )
}

$generatedRetailDumps=$false
$retailDumpFailures=0

if ($retailGraphFiles.Count -eq 0) {
    $generatedRetailDumps=$true
    $retailDumpRoot=Join-Path $OutputRoot 'retail_zone_dumps'
    $logRoot=Join-Path $OutputRoot 'retail_zone_logs'
    New-Item -ItemType Directory -Force -Path $retailDumpRoot | Out-Null
    New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

    $ffs=@(
        Get-ChildItem -LiteralPath $XboxGameRoot -Filter '*.ff' -File -Recurse -ErrorAction Stop |
        Sort-Object FullName
    )
    if ($ffs.Count -eq 0) {
        throw "No retail Xbox FFs found under: $XboxGameRoot"
    }

    Write-Host "[NUCLEAR-5F-IMG] Existing retail graph corpus not found; dumping $($ffs.Count) Xbox FFs once..."

    $ordinal=0
    foreach ($ff in $ffs) {
        $ordinal++
        $safe=([IO.Path]::GetFileNameWithoutExtension($ff.Name) -replace '[^A-Za-z0-9_.-]','_')
        $dump=Join-Path $retailDumpRoot ("{0:D3}_{1}" -f $ordinal,$safe)
        $log=Join-Path $logRoot ("{0:D3}_{1}.log" -f $ordinal,$safe)
        New-Item -ItemType Directory -Force -Path $dump | Out-Null

        $saved=$ErrorActionPreference
        $code=-1
        try {
            $ErrorActionPreference='Continue'
            & $graphUnlinker '--no-color' '--include-assets' 'image' '--image-format' 'IWI' '--output-folder' $dump $ff.FullName 2>&1 |
                Out-File -LiteralPath $log -Encoding utf8
            $code=[int]$LASTEXITCODE
        }
        finally {
            $ErrorActionPreference=$saved
        }

        if ($code -ne 0) { $retailDumpFailures++ }

        Write-Host ("[NUCLEAR-5F-IMG] [{0,3}/{1,3}] {2} exit={3}" -f $ordinal,$ffs.Count,$ff.Name,$code)
    }

    $retailGraphFiles=@(
        Get-ChildItem -LiteralPath $retailDumpRoot -Filter '*.json' -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/]xenon_image_graphs[\\/]' } |
        Sort-Object FullName
    )
}

if ($retailGraphFiles.Count -eq 0) {
    throw "No retail Xenon image graphs were available after retail corpus preparation."
}

$retailByHash=@{}
$retailGraphsParsed=0
$retailGraphsWithPixels=0

Write-Host '[NUCLEAR-5F-IMG] ============================================================'
Write-Host '[NUCLEAR-5F-IMG] ORACLE VALIDATION FINGERPRINT -> RETAIL XBOX EQUIVALENCE'
Write-Host '[NUCLEAR-5F-IMG] ============================================================'
Write-Host "[NUCLEAR-5F-IMG] Targets                  : $($targets.Count)"
Write-Host "[NUCLEAR-5F-IMG] Native validation graphs : $($nativeIndex.Count)"
Write-Host "[NUCLEAR-5F-IMG] Retail graph files       : $($retailGraphFiles.Count)"
Write-Host "[NUCLEAR-5F-IMG] Retail dumps generated   : $generatedRetailDumps"
Write-Host '[NUCLEAR-5F-IMG] Oracle bytes are fingerprint evidence only; accepted donors must come from retail Xbox.'
Write-Host ''

foreach ($file in $retailGraphFiles) {
    $zoneRoot=Get-GraphContainerRoot -GraphFile $file.FullName
    $zone=[IO.Path]::GetFileName($zoneRoot)
    $record=Load-GraphRecord -GraphFile $file.FullName -Zone $zone -ZonePath $zoneRoot
    if ($null -eq $record) { continue }

    $retailGraphsParsed++
    if (-not [bool]$record.HasPixelPayload) { continue }
    $retailGraphsWithPixels++

    $hash=[string]$record.PixelHash
    if (-not $retailByHash.ContainsKey($hash)) {
        $retailByHash[$hash]=New-Object 'System.Collections.Generic.List[object]'
    }
    $retailByHash[$hash].Add($record)
}

$results=New-Object 'System.Collections.Generic.List[object]'
$oracleGraphsFound=0
$oraclePixelFingerprints=0
$exactRetailMatches=0
$hashOnlyShapeMismatch=0
$noRetailHashMatch=0

foreach ($target in $targets) {
    $nativeName=[string]$target.NativeName
    $key=Normalize-Identity -Value $nativeName

    $oracleCandidates=@()
    if ($nativeIndex.ContainsKey($key)) {
        $oracleCandidates=@($nativeIndex[$key].ToArray())
    }

    $oracle=@($oracleCandidates | Where-Object { [bool]$_.HasPixelPayload } | Select-Object -First 1)
    if ($oracleCandidates.Count -gt 0) { $oracleGraphsFound++ }

    if ($oracle.Count -eq 0) {
        $results.Add([pscustomobject]@{
            Index=[int]$target.Index
            NativeName=$nativeName
            PcImage=[string]$target.PcImage
            Status='no-oracle-pixel-fingerprint'
            OracleGraph=$(if ($oracleCandidates.Count -gt 0) { [string]$oracleCandidates[0].GraphFile } else { '' })
            OraclePixelHash=''
            RetailHashHits=0
            ExactRetailCandidates=@()
            ShapeRejectedCandidates=@()
        })
        continue
    }

    $oracleRecord=$oracle[0]
    $oraclePixelFingerprints++
    $hash=[string]$oracleRecord.PixelHash

    $hashHits=@()
    if ($retailByHash.ContainsKey($hash)) {
        $hashHits=@($retailByHash[$hash].ToArray())
    }

    $exact=@()
    $rejected=@()
    foreach ($candidate in $hashHits) {
        if (Test-ExactShape -A $oracleRecord.Shape -B $candidate.Shape) {
            $exact += $candidate
        }
        else {
            $rejected += $candidate
        }
    }

    $status='no-retail-pixel-hash-match'
    if ($exact.Count -gt 0) {
        $status='exact-retail-equivalent'
        $exactRetailMatches++
    }
    elseif ($rejected.Count -gt 0) {
        $status='pixel-hash-match-shape-mismatch'
        $hashOnlyShapeMismatch++
    }
    else {
        $noRetailHashMatch++
    }

    $results.Add([pscustomobject]@{
        Index=[int]$target.Index
        NativeName=$nativeName
        PcImage=[string]$target.PcImage
        Status=$status
        OracleGraph=[string]$oracleRecord.GraphFile
        OraclePixelHash=$hash
        OracleShape=$oracleRecord.Shape
        RetailHashHits=$hashHits.Count
        ExactRetailCandidates=[object[]]@(
            $exact |
            ForEach-Object {
                [pscustomobject]@{
                    Name=[string]$_.Name
                    Zone=[string]$_.Zone
                    GraphFile=[string]$_.GraphFile
                    PixelFile=[string]$_.PixelFile
                    PixelHash=[string]$_.PixelHash
                    Shape=$_.Shape
                }
            }
        )
        ShapeRejectedCandidates=[object[]]@(
            $rejected |
            ForEach-Object {
                [pscustomobject]@{
                    Name=[string]$_.Name
                    Zone=[string]$_.Zone
                    GraphFile=[string]$_.GraphFile
                    PixelHash=[string]$_.PixelHash
                    Shape=$_.Shape
                }
            }
        )
    })
}

$exactRows=@($results.ToArray() | Where-Object Status -eq 'exact-retail-equivalent')
$uniqueExact=0
$ambiguousExact=0
foreach ($row in $exactRows) {
    if (@($row.ExactRetailCandidates).Count -eq 1) { $uniqueExact++ }
    elseif (@($row.ExactRetailCandidates).Count -gt 1) { $ambiguousExact++ }
}

$manifest=[ordered]@{
    format='WaW360.Nuclear5F.OracleRetailImageEquivalence.v1'
    stage5EManifest=(Resolve-Path -LiteralPath $Stage5EManifest).Path
    xboxGameRoot=(Resolve-Path -LiteralPath $XboxGameRoot).Path
    nativeValidationGraphRoot=(Resolve-Path -LiteralPath $NativeImageGraphRoot).Path
    retailDumpRoot=(Resolve-Path -LiteralPath $retailDumpRoot).Path
    retailDumpsGenerated=$generatedRetailDumps
    retailDumpFailures=$retailDumpFailures
    targets=$targets.Count
    oracleGraphsFound=$oracleGraphsFound
    oraclePixelFingerprints=$oraclePixelFingerprints
    retailGraphsParsed=$retailGraphsParsed
    retailGraphsWithPixels=$retailGraphsWithPixels
    exactRetailEquivalents=$exactRetailMatches
    uniqueExactRetailEquivalents=$uniqueExact
    ambiguousExactRetailEquivalents=$ambiguousExact
    pixelHashShapeMismatches=$hashOnlyShapeMismatch
    noRetailHashMatch=$noRetailHashMatch
    images=[object[]]$results.ToArray()
    policy=@(
        'Native/oracle image payload bytes are used only to compute validation fingerprints and expected Xenon image shape.',
        'No oracle pixel/header bytes are copied into production output by this stage.',
        'A production donor is eligible only when a user-owned retail Xbox image has an exact SHA-256 pixel match and exact Xenon structural shape.',
        'Any eventual production graph/pixel/header bytes must be copied from the matched retail Xbox source, never from the oracle.',
        'No match means the image still requires a legitimate PC/custom source reconstruction path.'
    )
}

$jsonPath=Join-Path $OutputRoot 'nuclear_5f_oracle_retail_image_equivalence.json'
$txtPath=Join-Path $OutputRoot 'nuclear_5f_oracle_retail_image_equivalence.txt'
$manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report=New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 5F - ORACLE VALIDATION FINGERPRINT -> RETAIL XBOX IMAGE EQUIVALENCE')
$report.Add(('=' * 112))
$report.Add(("Targets:                           {0:N0}" -f $targets.Count))
$report.Add(("Oracle graphs found:               {0:N0}" -f $oracleGraphsFound))
$report.Add(("Oracle pixel fingerprints:         {0:N0}" -f $oraclePixelFingerprints))
$report.Add(("Retail graphs parsed:              {0:N0}" -f $retailGraphsParsed))
$report.Add(("Retail graphs with pixels:         {0:N0}" -f $retailGraphsWithPixels))
$report.Add(("Exact retail equivalents:          {0:N0}" -f $exactRetailMatches))
$report.Add(("Unique exact equivalents:          {0:N0}" -f $uniqueExact))
$report.Add(("Ambiguous exact equivalents:       {0:N0}" -f $ambiguousExact))
$report.Add(("Pixel-hash/shape mismatches:       {0:N0}" -f $hashOnlyShapeMismatch))
$report.Add(("No retail pixel-hash match:        {0:N0}" -f $noRetailHashMatch))
$report.Add('')
$report.Add('TARGETS')
foreach ($row in $results.ToArray() | Sort-Object Index) {
    $report.Add(("  root #{0,4}: {1} -> {2}" -f $row.Index,$row.NativeName,$row.Status))
    $report.Add(("           PC identity: {0}" -f $row.PcImage))
    foreach ($candidate in @($row.ExactRetailCandidates)) {
        $report.Add(("           RETAIL EXACT <- {0} / {1}" -f $candidate.Zone,$candidate.Name))
    }
}
[IO.File]::WriteAllLines($txtPath,[string[]]$report.ToArray(),[Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-5F-IMG] ====================== RESULT ==============================='
Write-Host "[NUCLEAR-5F-IMG] Targets                       : $($targets.Count)"
Write-Host "[NUCLEAR-5F-IMG] Oracle graphs found           : $oracleGraphsFound"
Write-Host "[NUCLEAR-5F-IMG] Oracle pixel fingerprints     : $oraclePixelFingerprints"
Write-Host "[NUCLEAR-5F-IMG] Retail graphs parsed          : $retailGraphsParsed"
Write-Host "[NUCLEAR-5F-IMG] Retail graphs with pixels     : $retailGraphsWithPixels"
Write-Host "[NUCLEAR-5F-IMG] Exact retail equivalents      : $exactRetailMatches"
Write-Host "[NUCLEAR-5F-IMG] Unique exact equivalents      : $uniqueExact"
Write-Host "[NUCLEAR-5F-IMG] Ambiguous exact equivalents   : $ambiguousExact"
Write-Host "[NUCLEAR-5F-IMG] Pixel hash / shape mismatches : $hashOnlyShapeMismatch"
Write-Host "[NUCLEAR-5F-IMG] No retail pixel hash match    : $noRetailHashMatch"
Write-Host "[NUCLEAR-5F-IMG] Manifest                      : $jsonPath"
Write-Host "[NUCLEAR-5F-IMG] Report                        : $txtPath"
Write-Host '[NUCLEAR-5F-IMG] ================================================================='
Write-Host '[NUCLEAR-5F-IMG] PASS: equivalence census complete; no oracle payload promoted.' -ForegroundColor Green
