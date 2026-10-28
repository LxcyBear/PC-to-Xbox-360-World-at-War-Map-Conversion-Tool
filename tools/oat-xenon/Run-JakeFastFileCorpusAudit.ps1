[CmdletBinding()]
param(
    [string]$CorpusRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\jakes stuff',
    [string]$ReadinessManifest = '',
    [string]$PcImageIdentityManifest = '',
    [string]$NativeIdentityManifest = '',
    [string]$OutputRoot = '',
    [switch]$InventoryOnly,
    [switch]$Rescan,
    [switch]$SkipArchivedFastFiles,
    [long]$MaxArchivedFastFileBytes = 1073741824
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$base = Join-Path $repo 'output\nazi_zombie_FRUmansion'
if (-not $ReadinessManifest) { $ReadinessManifest = Join-Path $base 'nuclear_4f_full_content_readiness\nuclear_4f_full_content_readiness.json' }
if (-not $PcImageIdentityManifest) { $PcImageIdentityManifest = Join-Path $base 'nuclear_5e_frumansion_pc_ff_identity_census\nuclear_5e_frumansion_pc_ff_identity_census.json' }
if (-not $NativeIdentityManifest) { $NativeIdentityManifest = Join-Path $base 'nuclear_xenon_root_binding\native_identity\nazi_zombie_FRUmansion_native_identity_manifest.json' }
if (-not $OutputRoot) { $OutputRoot = Join-Path $base 'nuclear_5t_jake_fastfile_corpus_audit' }

$prebuilt = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
$graphUnlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
foreach ($file in @($CorpusRoot,$ReadinessManifest,$PcImageIdentityManifest,$NativeIdentityManifest,$prebuilt)) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Required source/tool missing: $file" }
}
if ($MaxArchivedFastFileBytes -le 0) { throw 'MaxArchivedFastFileBytes must be positive.' }
$corpus = (Resolve-Path -LiteralPath $CorpusRoot).Path
$tools = @($prebuilt)
if (Test-Path -LiteralPath $graphUnlinker -PathType Leaf) { $tools += $graphUnlinker }

function Normalize-Type([string]$value) {
    $s = $value.Trim().ToLowerInvariant()
    switch ($s) {
        'clipmap' { return 'clipmap_pvs' }
        'gameworldsp' { return 'gameworld_sp' }
        'techniqueset' { return 'techset' }
        default { return $s }
    }
}
function Normalize-Identity([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return '' }
    $v = $value.Trim().Trim('"').Replace('\','/').TrimStart('/')
    while ($v.StartsWith(',')) { $v = $v.Substring(1) }
    $mark = $v.ToLowerInvariant().LastIndexOf('/images/')
    if ($mark -ge 0) { $v = $v.Substring($mark + 8) }
    elseif ($v.StartsWith('images/',[StringComparison]::OrdinalIgnoreCase)) { $v = $v.Substring(7) }
    if ($v.EndsWith('.iwi',[StringComparison]::OrdinalIgnoreCase) -or
        $v.EndsWith('.dds',[StringComparison]::OrdinalIgnoreCase)) { $v = $v.Substring(0,$v.Length-4) }
    if ($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)) { $v = $v.Substring(0,$v.Length-3) }
    return $v.ToLowerInvariant()
}
function Strip-Prefix([string]$line) {
    $s = $line.Trim()
    if ($s.StartsWith('[')) {
        $end = $s.IndexOf(']')
        if ($end -ge 0 -and $end+1 -lt $s.Length) { $s=$s.Substring($end+1).TrimStart() }
    }
    $colon=$s.IndexOf(':')
    if ($colon -gt 0 -and $colon -lt 12 -and $s.Substring(0,$colon) -match '^[A-Za-z ]+$') {
        $s=$s.Substring($colon+1).TrimStart()
    }
    return $s
}
function Hash-Key([string]$text) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).Replace('-','').Substring(0,24))
    } finally { $sha.Dispose() }
}
function Folder-Lane([string]$path) {
    $relative = $path.Substring($corpus.Length).TrimStart('\','/')
    $segments = $relative.Split([char[]]@('\','/'),[StringSplitOptions]::RemoveEmptyEntries)
    if ($segments.Length -gt 1) { return $segments[0] }
    return '(root)'
}

$readiness=Get-Content -LiteralPath $ReadinessManifest -Raw | ConvertFrom-Json
$pcManifest=Get-Content -LiteralPath $PcImageIdentityManifest -Raw | ConvertFrom-Json
$native=Get-Content -LiteralPath $NativeIdentityManifest -Raw | ConvertFrom-Json
if (-not [bool]$readiness.ProperSizeGate -or
    [int]$readiness.ProductionRawFileRoots -ne 8 -or
    [int]$readiness.CriticalFallbackRoots -ne 70) {
    throw '5T requires the verified 5R/4F readiness snapshot (70 critical blockers, 8 PC RawFiles).'
}
if ([int]$pcManifest.targets -ne 43 -or [int]$pcManifest.foundInFrumansionPcFastFiles -ne 43) {
    throw '5T requires the verified 43-image PC identity census (5E).'
}
if ([string]$native.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
    -not [bool]$native.typeSequenceVerified) { throw 'Native root sequence not verified.' }

$pcByIndex=@{}
foreach ($row in @($pcManifest.images)) {
    $pcByIndex[[int]$row.Index]=[string]$row.PcImage
}
$nativeByKey=@{}
foreach ($row in @($native.entries)) {
    $kind=Normalize-Type $(if ([string]$row.OatTypeName) { [string]$row.OatTypeName } else { [string]$row.TypeName })
    $key="$kind|$([string]$row.Name)"
    if (-not $nativeByKey.ContainsKey($key)) { $nativeByKey[$key]=[int]$row.Index }
}

$targets = New-Object 'System.Collections.Generic.List[object]'
$targetByKey=@{}
foreach ($blocker in @($readiness.CriticalBlockers)) {
    $str=[string]$blocker
    if ($str -notmatch "^critical (?:fallback|synthetic dependency) ([a-z_]+): (?:native root #([0-9]+) )?'([^']+)'") {
        throw "Unrecognized critical blocker format; refusing incomplete corpus audit: $str"
    }
    $kind=Normalize-Type $Matches[1]
    $name=[string]$Matches[3]
    $idx=-1
    if ($Matches[2]) { $idx=[int]$Matches[2] }
    else {
        $key="$kind|$name"
        if ($nativeByKey.ContainsKey($key)) { $idx=[int]$nativeByKey[$key] }
    }
    $pcName=$(if ($kind -eq 'image' -and $idx -ge 0 -and $pcByIndex.ContainsKey($idx)) { [string]$pcByIndex[$idx] } else { '' })
    $target=[pscustomobject]@{ Index=$idx; Type=$kind; Name=$name; PcImage=$pcName; Blocker=$str }
    $targets.Add($target)
    foreach ($candidate in @(
        [pscustomobject]@{ Identity=$name; Role='native-name' },
        [pscustomobject]@{ Identity=$pcName; Role='mapped-pc-image' }
    )) {
        if (-not $candidate.Identity) { continue }
        $key="$kind|$(Normalize-Identity $candidate.Identity)"
        if (-not $targetByKey.ContainsKey($key)) {
            $targetByKey[$key]=New-Object 'System.Collections.Generic.List[object]'
        }
        $targetByKey[$key].Add([pscustomobject]@{ Target=$target; Role=$candidate.Role })
    }
}
if ($targets.Count -ne [int]$readiness.CriticalFallbackRoots) {
    throw "Target census drift: parsed $($targets.Count), expected $($readiness.CriticalFallbackRoots)."
}
if (@($targets.ToArray() | Where-Object Type -eq 'image').Count -ne 43) {
    throw '5T expected exactly 43 unresolved image roots.'
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$logsDir=Join-Path $OutputRoot 'list_logs'
$tmpDir=Join-Path $OutputRoot '_temporary_archived_ff'
New-Item -ItemType Directory -Force -Path $logsDir | Out-Null
$hits=New-Object 'System.Collections.Generic.List[object]'
$ffJobs=New-Object 'System.Collections.Generic.List[object]'
$otherFiles=New-Object 'System.Collections.Generic.List[object]'
$archiveFfs=New-Object 'System.Collections.Generic.List[object]'
$failures=New-Object 'System.Collections.Generic.List[object]'
$inventory=New-Object 'System.Collections.Generic.List[object]'

Write-Host '[NUCLEAR-5T-JAKE] ========================================================='
Write-Host '[NUCLEAR-5T-JAKE] THREE-TOOL FASTFILE / IWI / IWD / ZIP CORPUS AUDIT'
Write-Host "[NUCLEAR-5T-JAKE] Root: $corpus"
Write-Host "[NUCLEAR-5T-JAKE] Critical targets: $($targets.Count) (43 images + 27 other)"
Write-Host '[NUCLEAR-5T-JAKE] READ ONLY: does not import or promote donor payloads.'
Write-Host '[NUCLEAR-5T-JAKE] ========================================================='

function Record-Hit([string]$type,[string]$listed,[string]$zone,[string]$lane,[string]$evidence,[string]$platform,[string]$archiveEntry,[bool]$reference,[bool]$scanClean) {
    $type=Normalize-Type $type
    $keys=@("$type|$(Normalize-Identity $listed)")
    # Loose images and archive entries can sit outside a conventional images/
    # folder. Try the filename stem as a secondary exact identity, never fuzzy.
    if ($type -eq 'image' -and $evidence -in @('loose-image-file','archive-image-file')) {
        $stem=[IO.Path]::GetFileNameWithoutExtension($listed.Replace('\','/'))
        $keys+=("$type|$(Normalize-Identity $stem)")
    }
    $seenRoots=New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($key in @($keys | Select-Object -Unique)) {
      if (-not $targetByKey.ContainsKey($key)) { continue }
      foreach ($match in $targetByKey[$key]) {
        $t=$match.Target
        $rootKey="$($t.Index)|$($t.Type)|$($t.Name)|$($match.Role)"
        if (-not $seenRoots.Add($rootKey)) { continue }
        $hits.Add([pscustomobject]@{
            RootIndex=[int]$t.Index; Type=[string]$t.Type; NativeName=[string]$t.Name
            PcImage=[string]$t.PcImage; MatchRole=[string]$match.Role
            Evidence=$evidence; ReferenceOnly=$reference; ScanClean=$scanClean
            Platform=$platform; Lane=$lane; SourceFile=$zone; ArchiveEntry=$archiveEntry
            ListedName=$listed
        })
      }
    }
}

# Map SDKs ship original artwork as TGA/PNG/JPEG as well as game-ready IWI/DDS.
# Inventory all recognized source-image extensions, without decoding or copying.
$imageExtensions=@('.iwi','.dds','.tga','.png','.jpg','.jpeg','.bmp','.tif','.tiff')
$scanExtensions=@('.ff','.iwd','.zip','.7z','.rar') + $imageExtensions
$allFiles = @(Get-ChildItem -LiteralPath $corpus -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension.ToLowerInvariant() -in $scanExtensions } |
    Sort-Object FullName)
foreach ($file in $allFiles) {
    $lane=Folder-Lane $file.FullName
    $ext=$file.Extension.ToLowerInvariant()
    $inventory.Add([pscustomobject]@{ Lane=$lane; Kind=$ext; Path=$file.FullName; Bytes=[int64]$file.Length })
    if ($ext -eq '.ff') {
        $ffJobs.Add([pscustomobject]@{ DisplayPath=$file.FullName; Path=$file.FullName
            Lane=$lane; Length=[int64]$file.Length; Ticks=$file.LastWriteTimeUtc.Ticks
            ArchivePath=''; EntryName='' })
    } elseif ($ext -in $imageExtensions) {
        Record-Hit 'image' $file.FullName $file.FullName $lane 'loose-image-file' 'unknown' '' $false $true
    } elseif ($ext -in @('.7z','.rar')) {
        $failures.Add([pscustomobject]@{ SourceFile=$file.FullName; Error='Compressed RAR/7z not indexed; extract into the corpus folder first.' })
    } elseif ($ext -in @('.zip','.iwd')) {
        try {
            $zip=[IO.Compression.ZipFile]::OpenRead($file.FullName)
            try {
                foreach ($entry in $zip.Entries) {
                    $extension=[IO.Path]::GetExtension($entry.FullName).ToLowerInvariant()
                    if ($extension -in $imageExtensions) {
                        Record-Hit 'image' $entry.FullName $file.FullName $lane 'archive-image-file' 'unknown' $entry.FullName $false $true
                    } elseif ($extension -eq '.ff') {
                        $job=[pscustomobject]@{ DisplayPath="$($file.FullName)::$($entry.FullName)"
                            Path=''; Lane=$lane; Length=[int64]$entry.Length
                            Ticks=$file.LastWriteTimeUtc.Ticks; ArchivePath=$file.FullName; EntryName=$entry.FullName }
                        $archiveFfs.Add($job)
                        if (-not $SkipArchivedFastFiles) { $ffJobs.Add($job) }
                    }
                }
            } finally { $zip.Dispose() }
        } catch {
            $failures.Add([pscustomobject]@{ SourceFile=$file.FullName; Error="Archive indexing: $($_.Exception.Message)" })
            Write-Host "[NUCLEAR-5T-JAKE] Archive warning: $($file.FullName)" -ForegroundColor Yellow
        }
    }
}
Write-Host "[NUCLEAR-5T-JAKE] Files inventoried: $($allFiles.Count); physical FF: $(@($ffJobs.ToArray() | Where-Object { -not $_.ArchivePath }).Count); FF entries in archives: $($archiveFfs.Count)"
foreach ($laneGroup in @($inventory.ToArray() | Group-Object Lane | Sort-Object Name)) {
    $ffCount=@($laneGroup.Group | Where-Object Kind -eq '.ff').Count
    $compressed=@($laneGroup.Group | Where-Object Kind -in @('.zip','.iwd','.7z','.rar')).Count
    $sources=@($laneGroup.Group | Where-Object { $_.Kind -in $imageExtensions }).Count
    Write-Host "[NUCLEAR-5T-JAKE] Folder '$($laneGroup.Name)' files=$($laneGroup.Count) FF=$ffCount archives=$compressed images=$sources"
}
Write-Host "[NUCLEAR-5T-JAKE] Image file hits from loose/IWD/ZIP: $($hits.Count)"

$listResults=New-Object 'System.Collections.Generic.List[object]'
$reuse=0
$scanned=0
$number=0
foreach ($job in $ffJobs.ToArray()) {
    $number++
    $id=Hash-Key "$($job.DisplayPath)|$($job.Length)|$($job.Ticks)"
    $log=Join-Path $logsDir "$id.list.log"
    $meta=Join-Path $logsDir "$id.meta.json"
    $clean=$false
    $exit=-1
    $platform='unknown'
    $toolUsed=''
    $lines=@()

    if ((-not $Rescan) -and (Test-Path -LiteralPath $log -PathType Leaf) -and
        (Test-Path -LiteralPath $meta -PathType Leaf)) {
        try {
            $prior=Get-Content -LiteralPath $meta -Raw | ConvertFrom-Json
            if ([string]$prior.SourceFile -eq [string]$job.DisplayPath -and
                [long]$prior.Length -eq [long]$job.Length -and
                [long]$prior.Ticks -eq [long]$job.Ticks -and
                [int]$prior.ExitCode -eq 0) {
                $lines=@(Get-Content -LiteralPath $log)
                $exit=0
                $clean=$true
                $toolUsed=[string]$prior.Tool
                $platform=[string]$prior.Platform
                $reuse++
            }
        } catch {}
    }
    if (-not $clean -and -not $InventoryOnly) {
        $source=[string]$job.Path
        $temporary=''
        try {
            if ($job.ArchivePath) {
                if ([long]$job.Length -gt $MaxArchivedFastFileBytes) {
                    throw "Archive FF exceeds safety limit ($MaxArchivedFastFileBytes B)."
                }
                New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
                $temporary=Join-Path $tmpDir "$id.ff"
                $zip=[IO.Compression.ZipFile]::OpenRead([string]$job.ArchivePath)
                try {
                    $entry=$zip.GetEntry([string]$job.EntryName)
                    if ($null -eq $entry -or [long]$entry.Length -ne [long]$job.Length) {
                        throw 'Archive entry changed since inventory.'
                    }
                    $sourceStream=$entry.Open()
                    $destStream=[IO.File]::Create($temporary)
                    try { $sourceStream.CopyTo($destStream) }
                    finally { $sourceStream.Dispose(); $destStream.Dispose() }
                } finally { $zip.Dispose() }
                $source=$temporary
            }
            foreach ($tool in $tools) {
                $old=$ErrorActionPreference
                try {
                    $ErrorActionPreference='Continue'
                    $lines=@(& $tool '--list' '--no-color' $source 2>&1 | ForEach-Object { [string]$_ })
                    $exit=[int]$LASTEXITCODE
                } finally { $ErrorActionPreference=$old }
                $toolUsed=$tool
                if ($exit -eq 0) { break }
            }
            $clean=($exit -eq 0)
            if ($clean) {
                $loaded=@($lines | Where-Object { $_ -match 'Loaded zone.*\(T4' } | Select-Object -First 1)
                if ($loaded.Count -gt 0) {
                    $platform=$(if ($loaded[0] -match '(?i)xbox360') { 'xbox360' } else { 'pc-t4' })
                }
            }
            [IO.File]::WriteAllLines($log,[string[]]$lines,[Text.UTF8Encoding]::new($false))
            ([ordered]@{ SourceFile=$job.DisplayPath; Length=[long]$job.Length; Ticks=[long]$job.Ticks
                ExitCode=$exit; Tool=$toolUsed; Platform=$platform } | ConvertTo-Json) |
                Set-Content -LiteralPath $meta -Encoding UTF8
            $scanned++
        } catch {
            $clean=$false
            $failures.Add([pscustomobject]@{ SourceFile=$job.DisplayPath; Error=$_.Exception.Message })
        } finally {
            if ($temporary -and (Test-Path -LiteralPath $temporary)) {
                Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
            }
        }
    }

    $assetRows=0
    if ($clean) {
        foreach ($raw in $lines) {
            $line=Strip-Prefix ([string]$raw)
            $comma=$line.IndexOf(',')
            if ($comma -le 0) { continue }
            $type=Normalize-Type $line.Substring(0,$comma)
            if ($type -notin @('image','fx','weapon','gfxworld','clipmap_pvs','comworld','gameworld_sp')) { continue }
            $listed=$line.Substring($comma+1).Trim()
            if (-not $listed) { continue }
            $assetRows++
            $isReference=$listed.StartsWith(',')
            Record-Hit $type $listed $job.DisplayPath $job.Lane 'fastfile-asset-list' $platform ([string]$job.EntryName) $isReference $true
        }
    } elseif (-not $InventoryOnly) {
        $failures.Add([pscustomobject]@{ SourceFile=$job.DisplayPath; Error="OAT --list failed (exit=$exit). See $log" })
    }
    $listResults.Add([pscustomobject]@{ Path=$job.DisplayPath; Lane=$job.Lane; Archived=[bool]$job.ArchivePath
        Bytes=[long]$job.Length; Success=$clean; Platform=$platform; ExitCode=$exit
        ParsedAssetRows=$assetRows; LogFile=$log; OatTool=$toolUsed })
    if (($number % 10) -eq 0 -or $number -eq 1 -or $number -eq $ffJobs.Count) {
        Write-Host "[NUCLEAR-5T-JAKE] FF $number/$($ffJobs.Count) | cached=$reuse scanned=$scanned | matches=$($hits.Count) | last=$([IO.Path]::GetFileName($job.DisplayPath))"
    }
}

$hitRows=@($hits.ToArray() | Sort-Object RootIndex,Type,NativeName,Evidence,SourceFile,ArchiveEntry,MatchRole -Unique)
$rootResults=New-Object 'System.Collections.Generic.List[object]'
foreach ($t in $targets.ToArray()) {
    $found=@($hitRows | Where-Object { $_.Type -eq $t.Type -and $_.NativeName -eq $t.Name -and $_.RootIndex -eq $t.Index })
    $backed=@($found | Where-Object { $_.Evidence -in @('loose-image-file','archive-image-file') }).Count
    $listed=@($found | Where-Object { $_.Evidence -eq 'fastfile-asset-list' -and -not $_.ReferenceOnly }).Count
    $reference=@($found | Where-Object { $_.ReferenceOnly }).Count
    $rootResults.Add([pscustomobject]@{ Index=[int]$t.Index; Type=$t.Type; Name=$t.Name; PcImage=$t.PcImage
        PhysicalImageCandidateHits=$backed; OwnedFastFileIdentityHits=$listed; ReferenceIdentityHits=$reference
        CandidateCount=$found.Count; Status=$(if ($backed -gt 0) { 'physical-image-candidate' } elseif ($listed -gt 0) {
            'zone-identity-only' } elseif ($reference -gt 0) { 'external-reference-only' } else { 'not-found' })
        Candidates=[object[]]$found })
}
$byType=@($rootResults.ToArray() | Group-Object Type | Sort-Object Name | ForEach-Object {
    [pscustomobject]@{ Type=$_.Name; Targets=$_.Count
        ImageFiles=@($_.Group | Where-Object Status -eq 'physical-image-candidate').Count
        ZoneIdentities=@($_.Group | Where-Object Status -eq 'zone-identity-only').Count
        ReferencesOnly=@($_.Group | Where-Object Status -eq 'external-reference-only').Count
        NotFound=@($_.Group | Where-Object Status -eq 'not-found').Count }
})
$manifest=[ordered]@{
    format='WaW360.Nuclear5T.JakeFastFileCorpusAudit.v1'
    corpusRoot=$corpus
    readinessManifest=(Resolve-Path -LiteralPath $ReadinessManifest).Path
    targetCount=$targets.Count
    filesInventoried=$allFiles.Count
    physicalFastFiles=@($ffJobs.ToArray() | Where-Object { -not $_.ArchivePath }).Count
    archivedFastFiles=$archiveFfs.Count
    archivedFastFilesSkipped=[bool]$SkipArchivedFastFiles
    inventoryOnly=[bool]$InventoryOnly
    cachedOatLists=$reuse
    newlyScannedOatLists=$scanned
    cleanFastFiles=@($listResults.ToArray() | Where-Object Success).Count
    failedOrUnscannedFastFiles=@($listResults.ToArray() | Where-Object { -not $_.Success }).Count
    totalHits=$hitRows.Count
    byType=$byType
    sourceImageFiles=@($inventory.ToArray() | Where-Object { $_.Kind -in $imageExtensions }).Count
    sourceImageExtensions=[string[]]$imageExtensions
    inventories=[object[]]$inventory.ToArray()
    fastFiles=[object[]]$listResults.ToArray()
    rootTargets=[object[]]$rootResults.ToArray()
    failures=[object[]]$failures.ToArray()
    policy=@(
        'A fastfile --list hit proves an identity, not that the zone owns or can export its payload.',
        'IWI/DDS/TGA/PNG/JPEG/BMP/TIFF file presence is only a source candidate; codec/platform/semantic compatibility still require validation.',
        'The corpus remains untouched; no donor bytes are copied into production output or the Git repository.',
        'The audit does not lower 4F blocker counts or enable Xbox hardware testing.'
    )
}
$json=Join-Path $OutputRoot 'nuclear_5t_jake_fastfile_corpus_audit.json'
$report=Join-Path $OutputRoot 'nuclear_5t_jake_fastfile_corpus_audit.txt'
$csv=Join-Path $OutputRoot 'nuclear_5t_jake_matches.csv'
$hitRows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8
$manifest | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $json -Encoding UTF8
$summary=New-Object 'System.Collections.Generic.List[string]'
$summary.Add('NUCLEAR-5T JAKE FASTFILE CORPUS AUDIT')
$summary.Add('========================================================================')
$summary.Add("Corpus: $corpus")
$summary.Add("Files inventoried: $($allFiles.Count)")
$summary.Add("Physical FFs: $($manifest.physicalFastFiles)")
$summary.Add("Loose image files (IWI/DDS/raw artwork): $($manifest.sourceImageFiles)")
$summary.Add("FFs in ZIP/IWD: $($archiveFfs.Count)")
$summary.Add("Clean OAT FF listings: $($manifest.cleanFastFiles)")
$summary.Add("Cached listings: $reuse")
$summary.Add("New listings: $scanned")
$summary.Add("Matches: $($hitRows.Count)")
$summary.Add('BY TYPE')
foreach ($row in $byType) {
    $summary.Add(("{0,-14} targets={1,3} imageFiles={2,3} zoneIDs={3,3} refsOnly={4,3} missing={5,3}" -f
        $row.Type,$row.Targets,$row.ImageFiles,$row.ZoneIdentities,$row.ReferencesOnly,$row.NotFound))
}
$summary.Add('')
$summary.Add('TARGETS')
foreach ($row in $rootResults.ToArray()) {
    $summary.Add(("  root #{0,4}: {1,-14} {2} => {3} ({4})" -f $row.Index,$row.Type,$row.Name,$row.Status,$row.CandidateCount))
    if ($row.PcImage) { $summary.Add("       PC identity: $($row.PcImage)") }
    foreach ($match in @($row.Candidates | Select-Object -First 6)) {
        $summary.Add("       $($match.Evidence) [$($match.Platform)] $($match.SourceFile) :: $($match.ArchiveEntry) :: $($match.ListedName)")
    }
}
if ($failures.Count -gt 0) {
    $summary.Add('SCAN FAILURES / LIMITS')
    foreach ($failure in @($failures.ToArray() | Select-Object -First 80)) {
        $summary.Add("  $($failure.SourceFile): $($failure.Error)")
    }
}
[IO.File]::WriteAllLines($report,[string[]]$summary.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[NUCLEAR-5T-JAKE] ================= RESULT ================================='
foreach ($row in $byType) {
    Write-Host ("[NUCLEAR-5T-JAKE] {0,-14} targets={1,3} image-files={2,3} zone-ids={3,3} refs={4,3} absent={5,3}" -f
        $row.Type,$row.Targets,$row.ImageFiles,$row.ZoneIdentities,$row.ReferencesOnly,$row.NotFound)
}
Write-Host "[NUCLEAR-5T-JAKE] OAT lists: clean=$($manifest.cleanFastFiles) failed/unscanned=$($manifest.failedOrUnscannedFastFiles) cached=$reuse"
Write-Host "[NUCLEAR-5T-JAKE] Manifest: $json"
Write-Host "[NUCLEAR-5T-JAKE] Matches:  $csv"
Write-Host "[NUCLEAR-5T-JAKE] Report:   $report"
Write-Host '[NUCLEAR-5T-JAKE] No source bytes have been grafted; 4F readiness unchanged.'
Write-Host '[NUCLEAR-5T-JAKE] ========================================================='
