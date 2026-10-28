[CmdletBinding()]
param(
    [string]$PcMapsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps',
    [string]$MapName = 'nazi_zombie_FRUmansion',
    [string]$MaterialSlotManifest = '',
    [string]$PcOwnedImageBridgeManifest = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($MaterialSlotManifest)) {
    $MaterialSlotManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_5b_material_slot_image_closure\nuclear_5b_material_slot_image_closure.json'
}
if ([string]::IsNullOrWhiteSpace($PcOwnedImageBridgeManifest)) {
    $PcOwnedImageBridgeManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_pc_image_bridge\nuclear_4r_pc_image_bridge.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_5e_frumansion_pc_ff_identity_census'
}

foreach ($required in @($MaterialSlotManifest,$PcOwnedImageBridgeManifest,$unlinker)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required file not found: $required"
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

function Add-Directory {
    param(
        [System.Collections.Generic.List[object]]$List,
        [hashtable]$Seen,
        [string]$Path,
        [string]$Kind
    )

    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return }

    $full=(Resolve-Path -LiteralPath $Path).Path
    $key=$full.ToLowerInvariant()
    if ($Seen.ContainsKey($key)) { return }

    $Seen[$key]=$true
    $List.Add([pscustomobject]@{ Kind=$Kind; Path=$full })
}

$stage5b=Get-Content -LiteralPath $MaterialSlotManifest -Raw | ConvertFrom-Json
$pcBridge=Get-Content -LiteralPath $PcOwnedImageBridgeManifest -Raw | ConvertFrom-Json

if ([string]$stage5b.format -ne 'WaW360.Nuclear5B.MaterialSlotImageClosure.v1' -or
    [int]$stage5b.targetImages -ne 43) {
    throw 'Stage 5E requires the 43-image Stage 5B material-slot manifest.'
}
if ([string]$pcBridge.format -ne 'WaW360.Nuclear4R.PcOwnedImageBridge.v1') {
    throw 'Stage 5E requires the Stage 4R PC-owned image bridge manifest.'
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
    throw "Expected 43 mapped PC identities; found $($targets.Count)."
}

$targetByKey=@{}
foreach ($target in $targets.ToArray()) {
    $key=[string]$target.PcKey
    if (-not $targetByKey.ContainsKey($key)) {
        $targetByKey[$key]=New-Object 'System.Collections.Generic.List[object]'
    }
    $targetByKey[$key].Add($target)
}

$dirs=New-Object 'System.Collections.Generic.List[object]'
$seenDirs=@{}
$mapDir=Join-Path $PcMapsRoot $MapName
Add-Directory -List $dirs -Seen $seenDirs -Path $mapDir -Kind 'pc-map-folder'
Add-Directory -List $dirs -Seen $seenDirs -Path (Join-Path $env:LOCALAPPDATA "Activision\CoDWaW\mods\$MapName") -Kind 'local-mod'
Add-Directory -List $dirs -Seen $seenDirs -Path (Join-Path $env:LOCALAPPDATA "Activision\CoDWaW\usermaps\$MapName") -Kind 'local-usermap'
Add-Directory -List $dirs -Seen $seenDirs -Path (Join-Path $env:APPDATA "Activision\CoDWaW\mods\$MapName") -Kind 'roaming-mod'
Add-Directory -List $dirs -Seen $seenDirs -Path (Join-Path $env:APPDATA "Activision\CoDWaW\usermaps\$MapName") -Kind 'roaming-usermap'

$fastFiles=New-Object 'System.Collections.Generic.List[object]'
$seenFfs=@{}
foreach ($dir in $dirs.ToArray()) {
    foreach ($ff in @(Get-ChildItem -LiteralPath ([string]$dir.Path) -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName)) {
        $key=$ff.FullName.ToLowerInvariant()
        if ($seenFfs.ContainsKey($key)) { continue }
        $seenFfs[$key]=$true
        $fastFiles.Add([pscustomobject]@{
            Kind=[string]$dir.Kind
            Path=$ff.FullName
            Name=$ff.Name
        })
    }
}

$bridgeFf=[string]$pcBridge.pcFastFile
if (-not [string]::IsNullOrWhiteSpace($bridgeFf) -and (Test-Path -LiteralPath $bridgeFf -PathType Leaf)) {
    $key=(Resolve-Path -LiteralPath $bridgeFf).Path.ToLowerInvariant()
    if (-not $seenFfs.ContainsKey($key)) {
        $seenFfs[$key]=$true
        $fastFiles.Add([pscustomobject]@{
            Kind='stage4r-selected'
            Path=(Resolve-Path -LiteralPath $bridgeFf).Path
            Name=[IO.Path]::GetFileName($bridgeFf)
        })
    }
}

if ($fastFiles.Count -eq 0) {
    throw "No FRUmansion PC fastfiles were found under the map/mod/usermap roots."
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$logRoot=Join-Path $OutputRoot 'ff_logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

$hitIndex=@{}
$ffRows=New-Object 'System.Collections.Generic.List[object]'
$scanFailures=0
$totalImageRows=0

Write-Host '[NUCLEAR-5E-IMG] ============================================================'
Write-Host '[NUCLEAR-5E-IMG] FRUMANSION PC FASTFILE IDENTITY CENSUS'
Write-Host '[NUCLEAR-5E-IMG] ============================================================'
Write-Host "[NUCLEAR-5E-IMG] Targets       : $($targets.Count)"
Write-Host "[NUCLEAR-5E-IMG] Unique PC IDs : $($targetByKey.Count)"
Write-Host "[NUCLEAR-5E-IMG] PC fastfiles  : $($fastFiles.Count)"
Write-Host ''

$ordinal=0
foreach ($ff in $fastFiles.ToArray()) {
    $ordinal++

    $saved=$ErrorActionPreference
    $lines=@()
    $code=-1
    try {
        $ErrorActionPreference='Continue'
        $lines=@(& $unlinker '--list' '--no-color' ([string]$ff.Path) 2>&1 | ForEach-Object { [string]$_ })
        $code=[int]$LASTEXITCODE
    }
    finally {
        $ErrorActionPreference=$saved
    }

    $safeKind=([string]$ff.Kind -replace '[^A-Za-z0-9_.-]','_')
    $safeFf=([IO.Path]::GetFileNameWithoutExtension([string]$ff.Name) -replace '[^A-Za-z0-9_.-]','_')
    $log=Join-Path $logRoot ("{0:D2}_{1}_{2}.list.log" -f $ordinal,$safeKind,$safeFf)
    [IO.File]::WriteAllLines($log,[string[]]$lines,[Text.UTF8Encoding]::new($false))

    $imageRows=0
    $matched=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

    foreach ($raw in $lines) {
        $line=Strip-LogPrefix -Line $raw
        if (-not $line.StartsWith('image,',[StringComparison]::OrdinalIgnoreCase)) { continue }

        $imageRows++
        $listed=$line.Substring(6).Trim()
        $key=Normalize-ImageIdentity -Value $listed
        if (-not $targetByKey.ContainsKey($key)) { continue }

        [void]$matched.Add($key)

        if (-not $hitIndex.ContainsKey($key)) {
            $hitIndex[$key]=New-Object 'System.Collections.Generic.List[object]'
        }

        $hitIndex[$key].Add([pscustomobject]@{
            Kind=[string]$ff.Kind
            FastFile=[string]$ff.Path
            FastFileName=[string]$ff.Name
            ListedName=$listed
            ExitCode=$code
            Log=$log
        })
    }

    $totalImageRows += $imageRows
    if ($code -ne 0) { $scanFailures++ }

    $ffRows.Add([pscustomobject]@{
        Kind=[string]$ff.Kind
        FastFile=[string]$ff.Path
        ExitCode=$code
        ImageRows=$imageRows
        TargetMatches=$matched.Count
        Log=$log
    })

    Write-Host ("[NUCLEAR-5E-IMG] [{0}/{1}] {2,-24} {3} -> images={4}, matches={5}, exit={6}" -f
        $ordinal,$fastFiles.Count,[string]$ff.Kind,[string]$ff.Name,$imageRows,$matched.Count,$code)
}

$dumpLog=''
if ($pcBridge.PSObject.Properties.Name -contains 'pcFastFileDumpLog') {
    $dumpLog=[string]$pcBridge.pcFastFileDumpLog
}
if ([string]::IsNullOrWhiteSpace($dumpLog)) {
    $dumpLog=Join-Path $repo '.cache\nuclear_4r_pc_fastfile_images.log'
}

$dumpErrorLines=@()
if (Test-Path -LiteralPath $dumpLog -PathType Leaf) {
    $dumpErrorLines=@(
        Get-Content -LiteralPath $dumpLog -ErrorAction SilentlyContinue |
        Where-Object { $_ -match '(?i)Could not find data for image|not dumping image|image.*error|error.*image' } |
        ForEach-Object { [string]$_ }
    )
}

$results=New-Object 'System.Collections.Generic.List[object]'
$found=0
$missing=0
$withDumpError=0

foreach ($target in $targets.ToArray()) {
    $hits=@()
    if ($hitIndex.ContainsKey([string]$target.PcKey)) {
        $hits=@(
            $hitIndex[[string]$target.PcKey].ToArray() |
            Sort-Object Kind,FastFileName,ListedName
        )
    }

    $present=$hits.Count -gt 0
    if ($present) { $found++ } else { $missing++ }

    $relatedErrors=@(
        $dumpErrorLines |
        Where-Object {
            (Normalize-ImageIdentity -Value ([string]$_)).Contains([string]$target.PcKey) -or
            ([string]$_).IndexOf([string]$target.PcImage,[StringComparison]::OrdinalIgnoreCase) -ge 0
        }
    )
    if ($relatedErrors.Count -gt 0) { $withDumpError++ }

    $results.Add([pscustomobject]@{
        Index=[int]$target.Index
        NativeName=[string]$target.NativeName
        PcImage=[string]$target.PcImage
        FoundInFrumansionPcFastFile=$present
        HitCount=$hits.Count
        Hits=[object[]]$hits
        RelatedStage4RDumpErrors=[object[]]$relatedErrors
    })
}

$manifest=[ordered]@{
    format='WaW360.Nuclear5E.FRumansionPcFastFileIdentityCensus.v1'
    stage5BManifest=(Resolve-Path -LiteralPath $MaterialSlotManifest).Path
    pcOwnedImageBridgeManifest=(Resolve-Path -LiteralPath $PcOwnedImageBridgeManifest).Path
    targets=$targets.Count
    uniqueMappedPcIdentities=$targetByKey.Count
    fastFilesScanned=$fastFiles.Count
    scanFailures=$scanFailures
    totalImageRows=$totalImageRows
    foundInFrumansionPcFastFiles=$found
    notFoundInFrumansionPcFastFiles=$missing
    targetsWithStage4RDumpErrors=$withDumpError
    stage4RDumpLog=$(if (Test-Path -LiteralPath $dumpLog -PathType Leaf) { (Resolve-Path -LiteralPath $dumpLog).Path } else { $dumpLog })
    fastFiles=[object[]]$ffRows.ToArray()
    images=[object[]]$results.ToArray()
    policy=@(
        'This stage searches FRUmansion PC fastfile identity tables using the 43 PC image identities proven by Stage 5B.',
        'A fastfile identity hit proves the XAsset identity is present/referenced; it does not by itself prove pixel payload ownership.',
        'Stage 4R dump-error correlation is diagnostic evidence for external or otherwise unavailable PC image backing.',
        'No native Xbox oracle image payload bytes are copied or used as production input.'
    )
}

$jsonPath=Join-Path $OutputRoot 'nuclear_5e_frumansion_pc_ff_identity_census.json'
$txtPath=Join-Path $OutputRoot 'nuclear_5e_frumansion_pc_ff_identity_census.txt'
$manifest | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report=New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 5E - FRUMANSION PC FASTFILE IDENTITY CENSUS')
$report.Add(('=' * 104))
$report.Add(("Targets:                         {0:N0}" -f $targets.Count))
$report.Add(("PC fastfiles scanned:            {0:N0}" -f $fastFiles.Count))
$report.Add(("OAT scan failures:               {0:N0}" -f $scanFailures))
$report.Add(("Found in FRUmansion PC FFs:      {0:N0}" -f $found))
$report.Add(("Not found in FRUmansion PC FFs:  {0:N0}" -f $missing))
$report.Add(("Targets with Stage4R dump error: {0:N0}" -f $withDumpError))
$report.Add('')
$report.Add('TARGETS')
foreach ($row in $results.ToArray() | Sort-Object Index) {
    $report.Add(("  root #{0,4}: {1}" -f $row.Index,$row.NativeName))
    $report.Add(("           PC identity: {0}" -f $row.PcImage))
    if ([bool]$row.FoundInFrumansionPcFastFile) {
        foreach ($hit in @($row.Hits)) {
            $report.Add(("           FF HIT <- {0}/{1} as {2}" -f $hit.Kind,$hit.FastFileName,$hit.ListedName))
        }
    }
    else {
        $report.Add('           FF HIT <- NOT FOUND')
    }
    foreach ($err in @($row.RelatedStage4RDumpErrors | Select-Object -First 3)) {
        $report.Add(("           DUMP ERROR <- {0}" -f $err))
    }
}
[IO.File]::WriteAllLines($txtPath,[string[]]$report.ToArray(),[Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-5E-IMG] ====================== RESULT ==============================='
Write-Host "[NUCLEAR-5E-IMG] Targets                         : $($targets.Count)"
Write-Host "[NUCLEAR-5E-IMG] PC fastfiles scanned            : $($fastFiles.Count)"
Write-Host "[NUCLEAR-5E-IMG] OAT scan failures               : $scanFailures"
Write-Host "[NUCLEAR-5E-IMG] Found in FRUmansion PC FFs      : $found"
Write-Host "[NUCLEAR-5E-IMG] Not found in FRUmansion PC FFs  : $missing"
Write-Host "[NUCLEAR-5E-IMG] Targets with Stage4R dump error : $withDumpError"
Write-Host "[NUCLEAR-5E-IMG] Manifest                        : $jsonPath"
Write-Host "[NUCLEAR-5E-IMG] Report                          : $txtPath"
Write-Host '[NUCLEAR-5E-IMG] ================================================================='
Write-Host '[NUCLEAR-5E-IMG] PASS: FRUmansion PC fastfile identity census complete.' -ForegroundColor Green
