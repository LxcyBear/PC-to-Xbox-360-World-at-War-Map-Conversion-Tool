[CmdletBinding()]
param(
    [string]$MaterialSlotManifest = '',
    [string]$UGXRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\UGX',
    [string]$ZCTRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\ZCT',
    [string]$MapAssetsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\map assets',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($MaterialSlotManifest)) {
    $MaterialSlotManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_5b_material_slot_image_closure\nuclear_5b_material_slot_image_closure.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_5d_mapped_pc_dependency_ff_census'
}

foreach ($required in @($MaterialSlotManifest,$unlinker)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required file not found: $required"
    }
}
foreach ($root in @($UGXRoot,$ZCTRoot,$MapAssetsRoot)) {
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "Dependency corpus root not found: $root"
    }
}

function Normalize-ImageIdentity {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }

    $v=$Value.Trim().Trim('"').Replace('\','/').TrimStart('/')
    while ($v.StartsWith(',', [StringComparison]::Ordinal)) {
        $v=$v.Substring(1)
    }

    $marker=$v.ToLowerInvariant().LastIndexOf('/images/')
    if ($marker -ge 0) {
        $v=$v.Substring($marker+8)
    }
    elseif ($v.StartsWith('images/',[StringComparison]::OrdinalIgnoreCase)) {
        $v=$v.Substring(7)
    }

    foreach ($ext in @('.iwi','.dds')) {
        if ($v.EndsWith($ext,[StringComparison]::OrdinalIgnoreCase)) {
            $v=$v.Substring(0,$v.Length-$ext.Length)
            break
        }
    }

    if ($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)) {
        $v=$v.Substring(0,$v.Length-3)
    }

    return $v.ToLowerInvariant()
}

function Strip-LogPrefix {
    param([string]$Line)
    if ($null -eq $Line) { return '' }
    $text=$Line.Trim()
    $prefix=$text.IndexOf('] ')
    if ($text.StartsWith('[',[StringComparison]::Ordinal) -and $prefix -ge 0) {
        return $text.Substring($prefix+2).Trim()
    }
    return $text
}

function Get-PackageName {
    param([string]$Root,[string]$File)

    $rootFull=[IO.Path]::GetFullPath($Root)
    if (-not $rootFull.EndsWith('\')) { $rootFull += '\' }
    $full=[IO.Path]::GetFullPath($File)

    if (-not $full.StartsWith($rootFull,[StringComparison]::OrdinalIgnoreCase)) {
        return [IO.Path]::GetFileName((Split-Path -Parent $File))
    }

    $rel=$full.Substring($rootFull.Length)
    $first=$rel.Split([char]92)[0]
    if ([string]::IsNullOrWhiteSpace($first) -or $first.EndsWith('.ff',[StringComparison]::OrdinalIgnoreCase)) {
        return [IO.Path]::GetFileNameWithoutExtension($File)
    }
    return $first
}

$stage5b=Get-Content -LiteralPath $MaterialSlotManifest -Raw | ConvertFrom-Json
if ([string]$stage5b.format -ne 'WaW360.Nuclear5B.MaterialSlotImageClosure.v1' -or
    [int]$stage5b.targetImages -ne 43) {
    throw 'Stage 5D requires the 43-image Stage 5B material-slot manifest.'
}

$targets=New-Object 'System.Collections.Generic.List[object]'
foreach ($row in @($stage5b.images | Sort-Object Index)) {
    $pc=[string]$row.MappedPcImage
    if ([string]::IsNullOrWhiteSpace($pc)) {
        throw "Mapped PC identity missing for root #$([int]$row.Index): $([string]$row.Name)"
    }

    $targets.Add([pscustomobject]@{
        Index=[int]$row.Index
        NativeName=[string]$row.Name
        PcImage=$pc
        PcKey=Normalize-ImageIdentity -Value $pc
    })
}
if ($targets.Count -ne 43) {
    throw "Expected 43 mapped PC image identities; found $($targets.Count)."
}

$targetByKey=@{}
foreach ($target in $targets.ToArray()) {
    $key=[string]$target.PcKey
    if (-not $targetByKey.ContainsKey($key)) {
        $targetByKey[$key]=New-Object 'System.Collections.Generic.List[object]'
    }
    $targetByKey[$key].Add($target)
}

$corpora=@(
    [pscustomobject]@{ Name='UGX'; Root=(Resolve-Path -LiteralPath $UGXRoot).Path },
    [pscustomobject]@{ Name='ZCT'; Root=(Resolve-Path -LiteralPath $ZCTRoot).Path },
    [pscustomobject]@{ Name='MAP_ASSETS'; Root=(Resolve-Path -LiteralPath $MapAssetsRoot).Path }
)

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$logRoot=Join-Path $OutputRoot 'ff_logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

$hitIndex=@{}
$ffRows=New-Object 'System.Collections.Generic.List[object]'
$totalFfs=0
$scanFailures=0
$totalImageRows=0

Write-Host '[NUCLEAR-5D-IMG] ============================================================'
Write-Host '[NUCLEAR-5D-IMG] MAPPED PC IDENTITY -> DEPENDENCY FASTFILE CENSUS'
Write-Host '[NUCLEAR-5D-IMG] ============================================================'
Write-Host "[NUCLEAR-5D-IMG] Targets       : $($targets.Count)"
Write-Host "[NUCLEAR-5D-IMG] Unique PC IDs : $($targetByKey.Count)"
Write-Host '[NUCLEAR-5D-IMG] Corpora       : UGX + ZCT + map-assets'
Write-Host ''

foreach ($corpus in $corpora) {
    $ffs=@(
        Get-ChildItem -LiteralPath ([string]$corpus.Root) -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName
    )

    Write-Host ("[NUCLEAR-5D-IMG] {0}: {1} fastfile(s)" -f $corpus.Name,$ffs.Count)
    $ordinal=0

    foreach ($ff in $ffs) {
        $ordinal++
        $totalFfs++

        $saved=$ErrorActionPreference
        $lines=@()
        $code=-1
        try {
            $ErrorActionPreference='Continue'
            $lines=@(& $unlinker '--list' '--no-color' $ff.FullName 2>&1 | ForEach-Object { [string]$_ })
            $code=[int]$LASTEXITCODE
        }
        finally {
            $ErrorActionPreference=$saved
        }

        $safeCorpus=([string]$corpus.Name -replace '[^A-Za-z0-9_.-]','_')
        $safeFf=($ff.BaseName -replace '[^A-Za-z0-9_.-]','_')
        $log=Join-Path $logRoot ("{0}_{1:D3}_{2}.list.log" -f $safeCorpus,$ordinal,$safeFf)
        [IO.File]::WriteAllLines($log,[string[]]$lines,[Text.UTF8Encoding]::new($false))

        $imageCount=0
        $matchedInFf=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

        foreach ($raw in $lines) {
            $line=Strip-LogPrefix -Line $raw
            if (-not $line.StartsWith('image,',[StringComparison]::OrdinalIgnoreCase)) { continue }

            $imageCount++
            $listed=$line.Substring(6).Trim()
            $key=Normalize-ImageIdentity -Value $listed
            if (-not $targetByKey.ContainsKey($key)) { continue }

            [void]$matchedInFf.Add($key)
            if (-not $hitIndex.ContainsKey($key)) {
                $hitIndex[$key]=New-Object 'System.Collections.Generic.List[object]'
            }

            $hitIndex[$key].Add([pscustomobject]@{
                Corpus=[string]$corpus.Name
                Package=Get-PackageName -Root ([string]$corpus.Root) -File $ff.FullName
                FastFile=$ff.FullName
                FastFileName=$ff.Name
                ListedName=$listed
                ExitCode=$code
                Log=$log
            })
        }

        $totalImageRows += $imageCount
        if ($code -ne 0) { $scanFailures++ }

        $ffRows.Add([pscustomobject]@{
            Corpus=[string]$corpus.Name
            Package=Get-PackageName -Root ([string]$corpus.Root) -File $ff.FullName
            FastFile=$ff.FullName
            ExitCode=$code
            ImageRows=$imageCount
            TargetMatches=$matchedInFf.Count
            Log=$log
        })

        if ($matchedInFf.Count -gt 0) {
            Write-Host ("[NUCLEAR-5D-IMG]   {0} -> {1} mapped image hit(s)" -f $ff.Name,$matchedInFf.Count) -ForegroundColor Cyan
        }
    }
}

$results=New-Object 'System.Collections.Generic.List[object]'
$resolved=0
$missing=0
$multiSource=0

foreach ($target in $targets.ToArray()) {
    $hits=@()
    if ($hitIndex.ContainsKey([string]$target.PcKey)) {
        $hits=@(
            $hitIndex[[string]$target.PcKey].ToArray() |
            Sort-Object Corpus,Package,FastFileName,ListedName
        )
    }

    $present=$hits.Count -gt 0
    if ($present) { $resolved++ } else { $missing++ }

    $sources=@(
        $hits |
        ForEach-Object { "$($_.Corpus)/$($_.Package)" } |
        Sort-Object -Unique
    )
    if ($sources.Count -gt 1) { $multiSource++ }

    $results.Add([pscustomobject]@{
        Index=[int]$target.Index
        NativeName=[string]$target.NativeName
        PcImage=[string]$target.PcImage
        FoundInDependencyFastFile=$present
        HitCount=$hits.Count
        SourceGroups=$sources
        Hits=[object[]]$hits
    })
}

$byCorpus=@(
    $results.ToArray() |
    Where-Object { [bool]$_.FoundInDependencyFastFile } |
    ForEach-Object { $_.Hits } |
    ForEach-Object { $_ } |
    Group-Object Corpus |
    Sort-Object @{Expression={$_.Count};Descending=$true}, @{Expression={$_.Name};Descending=$false} |
    ForEach-Object {
        [pscustomobject]@{ Corpus=$_.Name; Hits=$_.Count }
    }
)

$manifest=[ordered]@{
    format='WaW360.Nuclear5D.MappedPcDependencyFastFileCensus.v1'
    stage5BManifest=(Resolve-Path -LiteralPath $MaterialSlotManifest).Path
    targets=$targets.Count
    uniqueMappedPcIdentities=$targetByKey.Count
    fastFilesScanned=$totalFfs
    scanFailures=$scanFailures
    totalImageRows=$totalImageRows
    foundInDependencyFastFiles=$resolved
    notFoundInDependencyFastFiles=$missing
    multiSourceTargets=$multiSource
    corpusSummary=$byCorpus
    fastFiles=[object[]]$ffRows.ToArray()
    images=[object[]]$results.ToArray()
    policy=@(
        'This stage searches dependency/custom PC fastfile identity tables using the mapped PC image names recovered from Stage 5B.',
        'No asset payload bytes are copied by this census.',
        'A hit identifies a candidate owning/reference fastfile and package for a missing image dependency.',
        'Production credit still requires a real source payload and PC-to-Xenon conversion or validated Xenon-native source.'
    )
}

$jsonPath=Join-Path $OutputRoot 'nuclear_5d_mapped_pc_dependency_ff_census.json'
$txtPath=Join-Path $OutputRoot 'nuclear_5d_mapped_pc_dependency_ff_census.txt'
$manifest | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report=New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 5D - MAPPED PC IDENTITY -> DEPENDENCY FASTFILE CENSUS')
$report.Add(('=' * 104))
$report.Add(("Targets:                         {0:N0}" -f $targets.Count))
$report.Add(("Fastfiles scanned:               {0:N0}" -f $totalFfs))
$report.Add(("OAT scan failures:               {0:N0}" -f $scanFailures))
$report.Add(("Found in dependency fastfiles:   {0:N0}" -f $resolved))
$report.Add(("Not found in dependency FFs:     {0:N0}" -f $missing))
$report.Add(("Multi-source targets:             {0:N0}" -f $multiSource))
$report.Add('')
$report.Add('CORPUS SUMMARY')
foreach ($row in $byCorpus) {
    $report.Add(("  {0,-20} {1,5:N0}" -f $row.Corpus,$row.Hits))
}
$report.Add('')
$report.Add('TARGETS')
foreach ($row in $results.ToArray() | Sort-Object Index) {
    $report.Add(("  root #{0,4}: {1}" -f $row.Index,$row.NativeName))
    $report.Add(("           PC identity: {0}" -f $row.PcImage))
    if ([bool]$row.FoundInDependencyFastFile) {
        foreach ($hit in @($row.Hits)) {
            $report.Add(("           HIT <- {0}/{1}: {2} as {3}" -f $hit.Corpus,$hit.Package,$hit.FastFileName,$hit.ListedName))
        }
    }
    else {
        $report.Add('           HIT <- NOT FOUND')
    }
}
[IO.File]::WriteAllLines($txtPath,[string[]]$report.ToArray(),[Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-5D-IMG] ====================== RESULT ==============================='
Write-Host "[NUCLEAR-5D-IMG] Targets                       : $($targets.Count)"
Write-Host "[NUCLEAR-5D-IMG] Fastfiles scanned             : $totalFfs"
Write-Host "[NUCLEAR-5D-IMG] OAT scan failures             : $scanFailures"
Write-Host "[NUCLEAR-5D-IMG] Found in dependency fastfiles : $resolved"
Write-Host "[NUCLEAR-5D-IMG] Not found in dependency FFs   : $missing"
Write-Host "[NUCLEAR-5D-IMG] Multi-source targets           : $multiSource"
foreach ($row in $byCorpus) {
    Write-Host ("[NUCLEAR-5D-IMG]   {0}: {1} hit(s)" -f $row.Corpus,$row.Hits)
}
Write-Host "[NUCLEAR-5D-IMG] Manifest                      : $jsonPath"
Write-Host "[NUCLEAR-5D-IMG] Report                        : $txtPath"
Write-Host '[NUCLEAR-5D-IMG] ================================================================='
Write-Host '[NUCLEAR-5D-IMG] PASS: mapped-PC dependency fastfile census complete.' -ForegroundColor Green
