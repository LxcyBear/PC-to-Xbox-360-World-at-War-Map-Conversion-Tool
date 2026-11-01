[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# One-pass source provenance only. Original PC map raw weapons from IWD or
# another map's cached ORIGINAL PC OAT lists may explain absent typed roots,
# but do not prove native Xbox WeaponDef source ownership. Likewise differing
# map.ff/mod.ff blobs stay fenced until engine override order is proven.
# No OAT calls, no .NET build, no FF output, no retail asset copying.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$work=Join-Path $base 'native_material_mapping'
$provenancePath=Join-Path $work 'pc_g11_lightning_conflict_stock_provenance\aztec_43_conflicts_24_missing_stock_name_provenance.json'
$baselinePath=Join-Path $base 'second_map_baseline.json'
$sizePath=Join-Path $work 'pc_g11_native_ff_size_audit\aztec_native_ff_size_and_source_ledger.json'
$out=Join-Path $work 'pc_g11_lightning_six_weapons_43_override_sources'
$json=Join-Path $out 'aztec_six_weapon_source_candidates_and_43_override_roles.json'
$md=Join-Path $out 'aztec_six_weapon_source_candidates_and_43_override_roles.md'
$csv=Join-Path $out 'aztec_43_source_override_conflicts.csv'
foreach($p in @($provenancePath,$baselinePath,$sizePath)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Required prior proof absent: $p"}
}
$p=Get-Content -LiteralPath $provenancePath -Raw|ConvertFrom-Json
$b=Get-Content -LiteralPath $baselinePath -Raw|ConvertFrom-Json
$s=Get-Content -LiteralPath $sizePath -Raw|ConvertFrom-Json
if([string]$p.schema -ne 'WaW360.AztecAsset43Conflict24MissingCachedStockProvenance.v1' -or
   [string]$p.status -ne 'AZTEC_43_DIFFERENT_PC_BODIES_24_MISSING_ROOTS_CATALOGED_NOT_RESOLVED' -or
   [int]$p.totalDifferentSourceBodyConflicts -ne 43 -or
   [int]$p.nativeRootsWithoutOriginalPcOwnedBody -ne 24 -or
   [int]$p.stockCacheAcceptedZones -ne 67 -or
   [int]$p.stockCacheStaleZones -ne 0 -or
   -not [bool]$p.stock67ZoneCatalogComplete -or
   [int]$p.offlineStockBodyNameMatchesForMissing -ne 18 -or
   [bool]$p.sourceDuplicateLoadOrderVerified -or
   [bool]$p.XboxHardwareReady -or
   [string]$b.format -ne 'WaW360.SecondMapBaseline.v1' -or
   [string]$b.mapName -ine 'nazi_zombie_aztec_V2.2' -or
   [string]$s.schema -ne 'WaW360.AztecNativeFfSizeAndSourceCoverage.v1' -or
   [string]$s.converted.sha256 -ine 'F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6'){
    throw 'Existing 43-conflict/24-missing source proof or 67-retail-zone baseline changed.'
}
$ffPath=[string]$s.converted.path
if(-not(Test-Path -LiteralPath $ffPath -PathType Leaf) -or
   (Get-FileHash -LiteralPath $ffPath -Algorithm SHA256).Hash -ine [string]$s.converted.sha256){
    throw 'Protected OAT-passing Aztec FF SHA-256 changed. Refuse stale source joins.'
}

$absent=@($p.originalPcMissingBodyRoots|
    Where-Object {$_.offlineRetailSourceNameClassification -eq 'NO_NAME_IN_67_VALID_RETAIL_OAT_ZONE_LISTS'})
$expected=@(
    'zombie_perk_bottle_doubletap','zombie_perk_bottle_jugg',
    'zombie_perk_bottle_revive','zombie_perk_bottle_sleight',
    'm7_launcher_zombie','m7_launcher_zombie_upgraded'
)
if($absent.Count -ne 6 -or
   @($absent|Where-Object {$_.type -ne 'weapon'}).Count -ne 0 -or
   @($absent|Where-Object {$_.name -notin $expected}).Count -ne 0 -or
   @($absent|Select-Object -ExpandProperty name -Unique).Count -ne 6){
    throw 'Six exact not-listed original-PC/Xbox stock WeaponDef root identities changed.'
}
$names=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
foreach($item in $absent){
    $items=New-Object 'System.Collections.Generic.List[object]'
    $names.Add([string]$item.name,$items)
}
Write-Host '[AZTEC-SIX-WEAPONS] ==========================================================='
Write-Host '[AZTEC-SIX-WEAPONS] Six missing weapon roots: original PC map files / IWD / cached PC OAT source-zone names'
Write-Host '[AZTEC-SIX-WEAPONS] 43 differing Xbox source bodies: identify map.ff / mod.ff overlap; select NONE'
Write-Host '[AZTEC-SIX-WEAPONS] NO dotnet, NO OAT scan, NO FF generation, NO Xbox test.'
Write-Host '[AZTEC-SIX-PROGRESS] [####----------------] 20% | 67-zone and protected candidate SHA locked'

# Original Aztec map asset archives (never Xbox retail game archives).
# Inspect only ZIP DIRECTORY ENTRY filenames, not archive members' content.
$sourceDirs=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$sourceFiles=@($b.fastfiles|Where-Object {$_.Role -in @('map.ff','map_patch.ff','mod.ff')})
if($sourceFiles.Count -ne 3){throw 'Expected exact original PC map/patch/mod source roles.'}
foreach($f in $sourceFiles){
    $file=[string]$f.Path
    if(-not(Test-Path -LiteralPath $file -PathType Leaf) -or
       [long](Get-Item -LiteralPath $file).Length -ne [long]$f.Bytes){
        throw "Aztec original PC FF source changed: $file"
    }
    [void]$sourceDirs.Add((Split-Path -Path $file -Parent))
}
$archivesSeen=0;$archiveFailures=0;$looseFilesChecked=0
foreach($dir in $sourceDirs){
    $archives=@(Get-ChildItem -LiteralPath $dir -Filter '*.iwd' -Recurse -File -ErrorAction SilentlyContinue)
    foreach($archive in $archives){
        $archivesSeen++
        try{
            $zip=[IO.Compression.ZipFile]::OpenRead($archive.FullName)
            try{
                foreach($entry in $zip.Entries){
                    $entryName=([string]$entry.FullName).Replace('\','/')
                    if($entryName -notmatch '(?i)(^|/)weapons(/|$)'){continue}
                    $stem=[IO.Path]::GetFileNameWithoutExtension($entryName)
                    if(-not $names.ContainsKey($stem)){continue}
                    $names[$stem].Add([pscustomobject]@{
                        sourceKind='ORIGINAL_PC_MAP_IWD_WEAPON_FILENAME_ONLY'
                        archive=$archive.FullName
                        item=$entry.FullName
                        archiveCompressedFileBytes=[long]$archive.Length
                        sourceRole='original-map-IWD'
                        sourceByteOwnershipNotProven=$true
                        xboxWeaponDefCompatible=$false
                    })
                }
            }finally{$zip.Dispose()}
        }catch{
            $archiveFailures++
            Write-Host "[AZTEC-SIX-WEAPONS] IWD archive not readable (not counted as absence): $($archive.FullName)"
        }
    }
    $loose=@(Get-ChildItem -LiteralPath $dir -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object {$_.FullName -match '(?i)[\\/]weapons[\\/]' -and $names.ContainsKey($_.BaseName)})
    $looseFilesChecked+=$loose.Count
    foreach($item in $loose){
        $names[$item.BaseName].Add([pscustomobject]@{
            sourceKind='ORIGINAL_PC_MAP_LOOSE_WEAPON_FILENAME_ONLY'
            archive=$null;item=$item.FullName
            originalFileSha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
            sourceRole='original-map-loose'
            sourceByteOwnershipNotProven=$true
            xboxWeaponDefCompatible=$false
        })
    }
}
Write-Host "[AZTEC-SIX-WEAPONS] Original PC map IWDs inspected=$archivesSeen; unreadable=$archiveFailures; matching loose weapon filenames=$looseFilesChecked"
Write-Host '[AZTEC-SIX-PROGRESS] [########------------] 40% | exact cached original-PC OAT WeaponDef names across other map projects'

# Another PC map's OAT listing may contain a genuine top-level weapon
# asset declaration absent from Aztec. Those are SOURCE HINTS only.
# Require the original PC FF file/metadata fingerprint to match exactly.
$pcOatLogs=0;$trustedPcLogs=0;$rejectedPcLogs=0
$mapRoots=Join-Path $repo 'output\second_map_baselines'
foreach($other in @(Get-ChildItem -LiteralPath $mapRoots -Directory -ErrorAction SilentlyContinue)){
    $cached=Join-Path $other.FullName 'oat_asset_lists'
    $manifest=Join-Path $other.FullName 'second_map_baseline.json'
    if(-not(Test-Path -LiteralPath $cached -PathType Container) -or
       -not(Test-Path -LiteralPath $manifest -PathType Leaf)){continue}
    try{$manifestObject=Get-Content -LiteralPath $manifest -Raw|ConvertFrom-Json}catch{continue}
    if([string]$manifestObject.format -ne 'WaW360.SecondMapBaseline.v1'){continue}
    $files=@($manifestObject.fastfiles)
    $index=0
    foreach($file in $files){
        $index++
        if(-not [bool]$file.Success){continue}
        $pcPath=[string]$file.Path
        $stem=([IO.Path]::GetFileNameWithoutExtension($pcPath)) -replace '[^A-Za-z0-9_.-]','_'
        $log=Join-Path $cached ('{0:D2}_{1}.list.log' -f $index,$stem)
        $meta=Join-Path $cached ('{0:D2}_{1}.meta.json' -f $index,$stem)
        if(-not(Test-Path -LiteralPath $log -PathType Leaf)){continue}
        $pcOatLogs++
        $valid=$false
        try{
            if(Test-Path -LiteralPath $meta -PathType Leaf){
                $m=Get-Content -LiteralPath $meta -Raw|ConvertFrom-Json
                if(Test-Path -LiteralPath $pcPath -PathType Leaf){
                    $item=Get-Item -LiteralPath $pcPath
                    $fingerprint="$($item.FullName)|$([long]$item.Length)|$($item.LastWriteTimeUtc.Ticks)"
                    $valid=([string]$m.Fingerprint -ceq $fingerprint -and [int]$m.ExitCode -eq 0)
                }
            }
        }catch{$valid=$false}
        if(-not $valid){$rejectedPcLogs++;continue}
        $trustedPcLogs++
        foreach($line in [IO.File]::ReadLines($log)){
            $text=$line.TrimStart()
            if($text.StartsWith('[',[StringComparison]::Ordinal)){
                $end=$text.IndexOf(']')
                if($end -gt 0){$text=$text.Substring($end+1).TrimStart()}
            }
            if($text -notmatch '(?i)^weapon\s*,\s*(.*?)\s*$'){continue}
            $listed=([string]$Matches[1]).Trim().Trim('"')
            $referenceOnly=$listed.StartsWith(',',[StringComparison]::Ordinal)
            $n=$listed.TrimStart(',')
            if(-not $names.ContainsKey($n)){continue}
            $names[$n].Add([pscustomobject]@{
                sourceKind=$(if($referenceOnly){'OTHER_PC_FF_OAT_REFERENCE_NAME_ONLY'}else{'OTHER_PC_FF_OAT_OWNED_WEAPON_NAME_ONLY'})
                sourceRole=[string]$file.Role
                sourcePcFastFile=$pcPath
                sourceBaseline=$manifest
                sourceOatList=$log
                referenceOnly=$referenceOnly
                originalPcSourceFfSha256=$null
                sourceByteOwnershipNotProven=$true
                xboxWeaponDefCompatible=$false
            })
        }
    }
}
Write-Host "[AZTEC-SIX-WEAPONS] Cached source PC OAT logs=$pcOatLogs; fingerprint-accepted=$trustedPcLogs; rejected=$rejectedPcLogs"

# A same-name serialized payload conflict isn't solved by a role ranking.
# Preserve both candidate SHA+byte lengths and classify cross-role scope.
$conflicts=@($p.realBodyConflicts)
if($conflicts.Count -ne 43){throw 'Exact 43 differing source body hashes changed.'}
$rows=New-Object 'System.Collections.Generic.List[object]'
$crossRole=0;$withinRole=0;$differentLengths=0
foreach($item in $conflicts){
    $roles=@($item.sourceRoles|Sort-Object -Unique)
    $sizes=@($item.sourceBodyByteSizes|Sort-Object -Unique)
    $hasCrossRole=$roles.Count -gt 1
    if($hasCrossRole){$crossRole++}else{$withinRole++}
    if($sizes.Count -gt 1){$differentLengths++}
    $rows.Add([pscustomobject]@{
        Type=[string]$item.type
        NativeRootOrdinal=[int]$item.nativeRootOrdinal
        Name=[string]$item.name
        CandidateCount=[int]$item.candidateCount
        SourceRoles=($roles -join ';')
        DifferentXboxBlobHashes=[int]$item.differingXboxBodyHashes
        DifferentSerializedLengths=($sizes.Count -gt 1)
        ConvertedXboxBodyLengths=($sizes -join ';')
        RoleRelationship=$(if($hasCrossRole){'DIFFERENT_PC_FASTFILE_ROLES'}else{'SAME_PC_FASTFILE_ROLE_MULTIPLE_BODIES'})
        VerifiedOverrideWinner='NONE_ENGINE_SOURCE_LOAD_ORDER_NOT_PROVEN'
        ProductionSourceSelected=$false
    })
}
New-Item -ItemType Directory -Path $out -Force|Out-Null
$rows|Export-Csv -LiteralPath $csv -Encoding UTF8 -NoTypeInformation
$weaponRows=New-Object 'System.Collections.Generic.List[object]'
$weaponsWithCandidate=0
foreach($item in $absent){
    $candidate=@($names[[string]$item.name].ToArray())
    if($candidate.Count -gt 0){$weaponsWithCandidate++}
    $weaponRows.Add([pscustomobject]@{
        nativeRootOrdinal=[int]$item.nativeRootOrdinal
        name=[string]$item.name
        offline67XboxRetailZoneBodyNameMatches=0
        originalPcAztecTopLevelWeaponBodyProven=$false
        potentialPcSourceFileNameOrOatCandidates=$candidate
        candidateCount=$candidate.Count
        nextAction=$(if($candidate.Count -eq 0){'LOCATE_ORIGINAL_PC_WEAPON_SOURCE_DEFINITION_OR_PROVE_GAME_RUNTIME_EXTERNAL'}
            else{'VERIFY_ORIGINAL_PC_OWNED_WEAPONDEF_BODY_AND_XBOX_GRAPH_SERIALIZATION'})
        hardwareReady=$false
    })
    Write-Host "[AZTEC-SIX-WEAPONS] weapon $($item.name): source candidate evidence rows=$($candidate.Count) (NONE proves native Xbox WeaponDef)"
}
$report=[ordered]@{
    schema='WaW360.AztecSixWeapon43OverrideSourceTriage.v1'
    status='AZTEC_SIX_WEAPONS_SOURCE_SEARCH_AND_43_PC_BODY_CONFLICT_ROLES_TRIAGED_NO_SELECTION'
    priorXboxRetailZoneListsAccepted=67
    priorXboxZoneCacheStaleEntries=0
    priorSixOriginalPcUnmatchedAndRetailUnlistedWeaponRootCount=6
    originalPcMapIwdArchivesInspected=$archivesSeen
    unreadableIwdArchives=$archiveFailures
    sourcePcFastFileCachedOatListsSeen=$pcOatLogs
    sourcePcFastFileCachedOatListsFingerprintAccepted=$trustedPcLogs
    sourcePcFastFileCachedOatListsRejected=$rejectedPcLogs
    sixWeaponSourceHintsFound=$weaponsWithCandidate
    sixWeaponRoots=$weaponRows.ToArray()
    totalConflictingSourceBodies=43
    crossFastFileRoleConflicts=$crossRole
    sameRoleSourceBodyConflicts=$withinRole
    differingXenonSerializedLengthConflicts=$differentLengths
    conflictRows=$rows.ToArray()
    verifiedSourceFastFileRuntimeOverrideSemantics=$false
    automaticallySelectedConflictingSources=0
    sourceOwnedSixWeaponXboxSerializationProven=0
    retailOwnedWeaponBytesReadOrCopied=$false
    emittedAztecXboxFastFile=$false
    xboxRuntimeResidencyVerified=$false
    gate11Closed=$false
    xboxHardwareReady=$false
}
$report|ConvertTo-Json -Depth 14|Set-Content -LiteralPath $json -Encoding UTF8
$mdLines=New-Object 'System.Collections.Generic.List[string]'
[void]$mdLines.Add('# Aztec: six elusive weapon roots and 43 genuine competing PC source bodies')
[void]$mdLines.Add('')
[void]$mdLines.Add("Six native WeaponDef names absent from all 67 Xbox retail-zone listings. PC map IWD archives inspected=$archivesSeen; other PC map OAT lists accepted=$trustedPcLogs/$pcOatLogs. **No native WeaponDef was emitted or owned-body proven by this audit.**")
[void]$mdLines.Add('')
[void]$mdLines.Add('| Native root | Weapon | PC filename/OAT source hint rows |')
[void]$mdLines.Add('|---:|---|---:|')
foreach($r in $weaponRows){[void]$mdLines.Add("| $($r.nativeRootOrdinal) | $($r.name) | $($r.candidateCount) |")}
[void]$mdLines.Add('')
[void]$mdLines.Add("43 differing real native converted source bodies: cross-fastfile roles=$crossRole; multiple bodies within same role=$withinRole; serialized body lengths different=$differentLengths.")
[void]$mdLines.Add('No conflict was automatically resolved: original PC zone load order and overwrite semantics are still unproven.')
[void]$mdLines.Add('')
[void]$mdLines.Add('Exact conflict role and byte-length CSV accompanies the JSON, with all 43 source identities.')
[void]$mdLines.Add('')
[void]$mdLines.Add('**Important:** A cached PC OAT type/name match or PC map IWD loose weapon filename is not an original Xbox WeaponDef body. A loaded original Xbox retail asset or stock-zone identity is not equivalent to game runtime availability. No donor bytes, no OAT, no FF changes.')
[IO.File]::WriteAllLines($md,[string[]]$mdLines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-SIX-PROGRESS] [###############-----] 75% | classify 43 source-body conflicts by original PC file roles'
Write-Host "[AZTEC-SIX-WEAPONS] Conflicting Xbox blobs=43: cross-FF roles=$crossRole, same-role=$withinRole, different serialized lengths=$differentLengths"
Write-Host "[AZTEC-SIX-WEAPONS] Six weapon roots with source filename/OAT name hints=$weaponsWithCandidate/6; NONE full WeaponDef native proof"
Write-Host '[AZTEC-SIX-PROGRESS] [####################] 100% | read-only source-worklist complete'
Write-Host "[AZTEC-SIX-WEAPONS] STATUS=$($report.status)"
Write-Host "[AZTEC-SIX-WEAPONS] JSON: $json"
Write-Host "[AZTEC-SIX-WEAPONS] Markdown: $md"
Write-Host "[AZTEC-SIX-WEAPONS] Conflict roles CSV: $csv"
Write-Host '[AZTEC-SIX-WEAPONS] No source conflict resolved or FF/Xbox touched. Still need source body/override proof.'
