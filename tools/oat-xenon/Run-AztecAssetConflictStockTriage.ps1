[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Read-only: exact 31 XModel/FX/Weapon + 12 XAnim native body conflicts,
# and 24 native roots without exact original-PC owned proof. Reuses
# user-owned Xbox FF OAT --list cache as type/name provenance ONLY.
# Does NOT read or copy Xbox retail payloads, choose body precedence,
# mutate/build FF, invoke OAT, or assert stock runtime residency.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$threePath=Join-Path $base 'pc_g11_lightning_three_class_native_root_resolution\aztec_three_class_native_root_body_resolution.json'
$xanimPath=Join-Path $base 'pc_g11_lightning_xanim_602_root_selection\aztec_602_xanim_source_body_selection.json'
$sizePath=Join-Path $base 'pc_g11_native_ff_size_audit\aztec_native_ff_size_and_source_ledger.json'
$cacheRoot=Join-Path $base 'pc_fastfile_image_sources\stock_identity_logs'
$out=Join-Path $base 'pc_g11_lightning_conflict_stock_provenance'
$json=Join-Path $out 'aztec_43_conflicts_24_missing_stock_name_provenance.json'
$md=Join-Path $out 'aztec_43_conflicts_24_missing_stock_name_provenance.md'
$csv=Join-Path $out 'aztec_unresolved_native_roots.csv'
foreach($path in @($threePath,$xanimPath,$sizePath)){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Prior original-PC Xbox source/native evidence missing: $path"
    }
}
$t=Get-Content -LiteralPath $threePath -Raw|ConvertFrom-Json
$x=Get-Content -LiteralPath $xanimPath -Raw|ConvertFrom-Json
$s=Get-Content -LiteralPath $sizePath -Raw|ConvertFrom-Json
if([string]$t.schema -ne 'WaW360.AztecThreeOwnedClassNativeRootSelection.v1' -or
   [string]$t.status -ne 'AZTEC_MODELS_FX_WEAPONS_EXACT_BODY_HASH_SELECTION_AND_GAPS_PASS_NOT_GRAFTED' -or
   [int]$t.nativeThreeClassRoots -ne 586 -or
   [int]$t.sourceAuthenticXboxBlobRecords -ne 623 -or
   @($t.exactNativeRootSelection).Count -ne 586 -or
   @($t.missingOrConflictingNativeRoots).Count -ne 55 -or
   [bool]$t.hardwareReady -or
   [string]$x.schema -ne 'WaW360.AztecXAnim602SourceBodyResolution.v1' -or
   [int]$x.nativeXboxXAnimRoots -ne 602 -or
   [int]$x.nativeRootsWithSingleSource -ne 590 -or
   [int]$x.nativeRootsWithConflictingDuplicateBodies -ne 12 -or
   @($x.nativeRootBodySelection).Count -ne 602 -or
   [bool]$x.hardwareReady -or
   [string]$s.schema -ne 'WaW360.AztecNativeFfSizeAndSourceCoverage.v1' -or
   [string]$s.converted.sha256 -ine 'F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6'){
    throw 'Exact 623 native source bodies, 586 roots, 43 conflicting source names, or 12 native XAnim conflicts changed.'
}
if(-not(Test-Path -LiteralPath ([string]$s.converted.path) -PathType Leaf) -or
   (Get-FileHash -LiteralPath ([string]$s.converted.path) -Algorithm SHA256).Hash -ine
      [string]$s.converted.sha256){
    throw 'Protected OAT-passing 4,510,695B FF SHA-256 changed: refusing stale proof joins.'
}
$missing=@($t.missingOrConflictingNativeRoots|
    Where-Object {[string]$_.decision -eq 'NO_EXACT_NAME_TYPED_SOURCE_BODY'})
$conflict3=@($t.missingOrConflictingNativeRoots|
    Where-Object {[string]$_.decision -eq 'DUPLICATE_BODY_SHA_CONFLICT_NO_SELECTION'})
$conflictX=@($x.nativeRootBodySelection|
    Where-Object {[string]$_.decision -eq 'DUPLICATE_CONFLICT_REFUSED'})
if($missing.Count -ne 24 -or $conflict3.Count -ne 31 -or $conflictX.Count -ne 12){
    throw "Source conflict partition changed: missing=$($missing.Count) threeTypeConflicts=$($conflict3.Count) xanimConflicts=$($conflictX.Count)"
}
Write-Host '[AZTEC-PROVENANCE] ==========================================================='
Write-Host '[AZTEC-PROVENANCE] 31 source-body conflicts + 12 XAnim conflicts + 24 missing bodies'
Write-Host '[AZTEC-PROVENANCE] Evidence only; no OAT, no FF build, no Xbox retail byte graft'
Write-Host '[AZTEC-PROVENANCE-PROGRESS] [####----------------] 20% | proof SHA / source roots pass'

# Index only the 24 missing type/name keys, to avoid expensive wholesale
# asset catalogs. The OAT comma-prefix is an asset REFERENCE in that
# listed zone, not an owned body. Offline owned-body listings never prove
# that CodXe loaded the body when the map is activated.
$targetByKey=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
foreach($e in $missing){
    $key=([string]$e.category).ToLowerInvariant()+'|'+[string]$e.nativeRootName
    if($targetByKey.ContainsKey($key)){throw "Duplicate native missing root key: $key"}
    $targetRows=New-Object 'System.Collections.Generic.List[object]'
    $targetByKey.Add($key,$targetRows)
}
$metaFiles=@()
if(Test-Path -LiteralPath $cacheRoot -PathType Container){
    $metaFiles=@(Get-ChildItem -LiteralPath $cacheRoot -Filter '*.meta.json' -File)
}
$validLogs=0
$staleLogs=0
$catalogZones=New-Object 'System.Collections.Generic.List[object]'
$seenZonePaths=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($metaFile in $metaFiles){
    $log=$metaFile.FullName.Substring(0,$metaFile.FullName.Length-'.meta.json'.Length)
    $ok=$false
    $reason=''
    $zoneName=''
    try{
        $m=Get-Content -LiteralPath $metaFile.FullName -Raw|ConvertFrom-Json
        $path=[string]$m.file
        $zoneName=[IO.Path]::GetFileName($path)
        if(-not(Test-Path -LiteralPath $log -PathType Leaf) -or
           -not(Test-Path -LiteralPath $path -PathType Leaf)){
            $reason='SOURCE_FILE_OR_LOG_ABSENT'
        }else{
            $ff=Get-Item -LiteralPath $path
            $ok=([long]$ff.Length -eq [long]$m.bytes -and
                 $ff.LastWriteTimeUtc.ToString('o') -ceq [string]$m.modifiedUtc -and
                 [int]$m.exitCode -eq 0 -and
                 $seenZonePaths.Add($ff.FullName))
            if(-not $ok){$reason='SOURCE_FINGERPRINT_STALE_OR_DUPLICATE'}
        }
    }catch{
        $reason='UNREADABLE_CACHED_OAT_PROOF'
    }
    if(-not $ok){
        $staleLogs++
        $catalogZones.Add([pscustomobject]@{
            zone=$zoneName;source=[string]$metaFile.FullName;accepted=$false;reason=$reason
        })
        continue
    }
    $validLogs++
    $hits=0
    foreach($line in [IO.File]::ReadLines($log)){
        $txt=$line.TrimStart()
        if($txt.StartsWith('[',[StringComparison]::Ordinal)){
            $close=$txt.IndexOf(']')
            if($close -ge 0 -and $close+1 -lt $txt.Length){
                $txt=$txt.Substring($close+1).TrimStart()
            }
        }
        if($txt -notmatch '(?i)^(xmodel|fx|weapon)\s*,\s*(.*?)\s*$'){continue}
        $kind=([string]$Matches[1]).ToLowerInvariant()
        $listedName=([string]$Matches[2]).Trim().Trim('"')
        if([string]::IsNullOrWhiteSpace($listedName)){continue}
        $referenceOnly=$listedName.StartsWith(',',[StringComparison]::Ordinal)
        $name=if($referenceOnly){$listedName.TrimStart(',')}else{$listedName}
        $key=$kind+'|'+$name
        if(-not $targetByKey.ContainsKey($key)){continue}
        $hits++
        $targetByKey[$key].Add([pscustomobject]@{
            zone=$zoneName
            status=$(if($referenceOnly){'REFERENCE_ONLY_NAME'}else{'OAT_LISTED_OWNED_BODY_NAME'})
            bodyListed=(-not $referenceOnly)
            likelyPreloadedBaseZone=($zoneName.ToLowerInvariant() -in @('common.ff','code_post_gfx.ff','patch.ff'))
            actualRuntimeResidencyProven=$false
            oatList=$log
        })
    }
    $catalogZones.Add([pscustomobject]@{
        zone=$zoneName;source=[string]$m.file;accepted=$true
        recordsFor24MissingRoots=$hits;reason='OAT_LIST_CACHE_FINGERPRINT_ACCEPTED_NAME_ONLY'
    })
}
$completeCatalog=($validLogs -ge 67 -and $staleLogs -eq 0)
if($validLogs -eq 0){
    Write-Host '[AZTEC-PROVENANCE] No eligible 67-zone stock OAT listing cache. Marking provenance UNKNOWN, not stock absent.'
}
Write-Host "[AZTEC-PROVENANCE] Xbox OAT cached zones accepted=$validLogs; stale/invalid=$staleLogs; complete67Evidence=$completeCatalog"
Write-Host '[AZTEC-PROVENANCE-PROGRESS] [########------------] 40% | compare 24 names against individually OAT-listed retail zones'

$missingRows=New-Object 'System.Collections.Generic.List[object]'
foreach($item in $missing){
    $kind=([string]$item.category).ToLowerInvariant()
    $name=[string]$item.nativeRootName
    $key=$kind+'|'+$name
    $names=@($targetByKey[$key].ToArray())
    $bodies=@($names|Where-Object {$_.bodyListed})
    $refs=@($names|Where-Object {-not $_.bodyListed})
    $baseBodies=@($bodies|Where-Object {$_.likelyPreloadedBaseZone})
    $level=if($baseBodies.Count -gt 0){'OFFLINE_BASE_ZONE_OWNED_BODY_NAME_FOUND_RUNTIME_UNPROVEN'}
        elseif($bodies.Count -gt 0){'OFFLINE_OTHER_RETAIL_ZONE_OWNED_BODY_NAME_FOUND_NOT_PRELOAD_PROVEN'}
        elseif($refs.Count -gt 0){'RETAIL_REFERENCE_ONLY_LISTING_NO_BODY_FOUND'}
        elseif($completeCatalog){'NO_NAME_IN_67_VALID_RETAIL_OAT_ZONE_LISTS'}
        else{'UNRESOLVED_STOCK_CATALOG_INCOMPLETE'}
    $missingRows.Add([pscustomobject]@{
        type=$kind
        nativeRootOrdinal=[int]$item.nativeRootOrdinal
        name=$name
        originalPcOwnedSourceBodyProven=$false
        offlineRetailSourceNameClassification=$level
        baseZoneOwnedBodyNames=@($baseBodies|Select-Object -ExpandProperty zone -Unique)
        otherRetailOwnedBodyNames=@($bodies|Where-Object {-not $_.likelyPreloadedBaseZone}|Select-Object -ExpandProperty zone -Unique)
        referenceOnlyZoneNames=@($refs|Select-Object -ExpandProperty zone -Unique)
        allRetailZoneMatches=$names
        verifiedRuntimeResidency=$false
        automaticProductionFfAssetGraftAllowed=$false
    })
    Write-Host "[AZTEC-PROVENANCE] MISSING $kind #$($item.nativeRootOrdinal) $name : $level"
}

# Nothing resolves conflicting source definitions automatically. The
# actual PC map.ff vs mod.ff precedence is gameplay/script/load-order
# dependent; candidate hash & size differences are recorded for review.
$conflictRows=New-Object 'System.Collections.Generic.List[object]'
foreach($item in $conflict3){
    $candidates=@($item.candidates)
    $shaCount=@($candidates|Select-Object -ExpandProperty blobSha256 -Unique).Count
    $sizes=@($candidates|Select-Object -ExpandProperty blobBytes -Unique)
    $roles=@($candidates|Select-Object -ExpandProperty sourceRole -Unique)
    if($shaCount -lt 2 -or $candidates.Count -lt 2){throw "Claimed duplicate source conflict no longer differs: $($item.nativeRootName)"}
    $conflictRows.Add([pscustomobject]@{
        type=[string]$item.category
        nativeRootOrdinal=[int]$item.nativeRootOrdinal
        name=[string]$item.nativeRootName
        candidateCount=$candidates.Count
        sourceRoles=@($roles)
        sourceBodyByteSizes=@($sizes)
        differingSerializedBodySizes=($sizes.Count -gt 1)
        differingXboxBodyHashes=$shaCount
        candidates=$candidates
        state='ORIGINAL_PC_MULTIPLE_REAL_BODIES_NO_VALIDATED_LOAD_ORDER'
        selectedSource=$null
        productionFfInsertionAllowed=$false
    })
}
foreach($item in $conflictX){
    $candidates=@($item.candidates)
    $shaCount=@($candidates|Select-Object -ExpandProperty xboxBlobSha256 -Unique).Count
    $sizes=@($candidates|Select-Object -ExpandProperty blobBytes -Unique)
    $roles=@($candidates|Select-Object -ExpandProperty role -Unique)
    if($shaCount -lt 2 -or $candidates.Count -lt 2){throw "Claimed XAnim duplicate conflict no longer differs: $($item.nativeXAnimName)"}
    $conflictRows.Add([pscustomobject]@{
        type='xanim'
        nativeRootOrdinal=[int]$item.nativeRootIndex
        name=[string]$item.nativeXAnimName
        candidateCount=$candidates.Count
        sourceRoles=@($roles)
        sourceBodyByteSizes=@($sizes)
        differingSerializedBodySizes=($sizes.Count -gt 1)
        differingXboxBodyHashes=$shaCount
        candidates=$candidates
        state='ORIGINAL_PC_MULTIPLE_REAL_BODIES_NO_VALIDATED_LOAD_ORDER'
        selectedSource=$null
        productionFfInsertionAllowed=$false
    })
}
if($conflictRows.Count -ne 43 -or $missingRows.Count -ne 24){
    throw '43 conflicting bodies / 24 missing root partitions changed.'
}
New-Item -ItemType Directory -Force -Path $out|Out-Null
$csvRows=@()
foreach($row in $missingRows){
    $csvRows+=[pscustomobject]@{
        Type=$row.type;NativeRootOrdinal=$row.nativeRootOrdinal;Name=$row.name
        Decision='MISSING_ORIGINAL_PC_OWNED_SOURCE_BODY'
        RetailNameEvidence=$row.offlineRetailSourceNameClassification
        BodyCandidateRoles=''
        BodySizes=''
        NextRequirement='VERIFY_OWNERSHIP_NOT_RETAIL_DONOR_BYTES'
    }
}
foreach($row in $conflictRows){
    $csvRows+=[pscustomobject]@{
        Type=$row.type;NativeRootOrdinal=$row.nativeRootOrdinal;Name=$row.name
        Decision='SOURCE_OWNED_DIFFERENT_XBOX_BODY_HASHES'
        RetailNameEvidence='NOT_RELEVANT_TO_DOUBLE_PC_BODY_SOURCE'
        BodyCandidateRoles=($row.sourceRoles -join ';')
        BodySizes=($row.sourceBodyByteSizes -join ';')
        NextRequirement='PROVE_PC_FF_ASSET_OVERRIDE_SEMANTICS'
    }
}
$csvRows|Sort-Object Type,NativeRootOrdinal|
    Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8

$report=[ordered]@{
    schema='WaW360.AztecAsset43Conflict24MissingCachedStockProvenance.v1'
    status='AZTEC_43_DIFFERENT_PC_BODIES_24_MISSING_ROOTS_CATALOGED_NOT_RESOLVED'
    originalPcXenonSourceBodiesReadback=623
    nativeXModelFxWeaponRoots=586
    threeTypeUnambiguousSourceSelections=531
    threeTypeConflicts=31
    xanimConflicts=12
    totalDifferentSourceBodyConflicts=43
    nativeRootsWithoutOriginalPcOwnedBody=24
    stockCacheAcceptedZones=$validLogs
    stockCacheStaleZones=$staleLogs
    stock67ZoneCatalogComplete=$completeCatalog
    offlineStockBodyNameMatchesForMissing=@($missingRows|Where-Object {@($_.allRetailZoneMatches|Where-Object {$_.bodyListed}).Count -gt 0}).Count
    originalPcMissingBodyRoots=$missingRows.ToArray()
    realBodyConflicts=$conflictRows.ToArray()
    stockOatZoneEvidence=$catalogZones.ToArray()
    carriedForwardWorldMaterialSourceGraphCount=39
    materialNativeRootMissingSpecial39GraphProof=498
    originalPcLocalizeTypedNamesAndValues=177
    nativePackedLocalizeValuesRebased=$false
    XboxRuntimeStockAssetResidencyVerified=$false
    sourceDuplicateLoadOrderVerified=$false
    resolvedConflictingOriginalPcSourceBodies=0
    didNotRunOat=$true
    didNotReadRetailFfAssetBytes=$true
    didNotEmitOrModifyFastFile=$true
    XboxHardwareReady=$false
    gate11Closed=$false
    note='An offline owned OAT type/name listing is not guaranteed title-update stock residency, actual dependency graph availability, or permission to copy retail donor bytes. The 43 differing source bodies must remain unselected until PC FF override/zone precedence and semantic compatibility are independently proven.'
}
$report|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $json -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add('# Aztec: 43 competing PC source bodies + 24 exact missing native source roots')
$lines.Add('')
$lines.Add('**All 623 source-authentic Xbox proof blobs passed the preceding SHA checks; none are currently integrated.**')
$lines.Add('Consolidated result: 531 exact unambiguous XModel/FX/Weapon source roots; 31 conflicting Xbox source-model/FX/weapon bodies; 12 conflicting source animation bodies; 24 native names with no exact PC owned-body proof.')
$lines.Add("Cached Xbox retail OAT list entries fingerprint-accepted: $validLogs; invalid/stale=$staleLogs; 67-zone census complete=$completeCatalog. **Offline name listings never prove Xbox runtime residency.**")
$lines.Add('')
$lines.Add('## The 24 roots missing an original PC owned source body')
$lines.Add('')
$lines.Add('| Type | Root | Name | Cached Xbox retail body-name evidence |')
$lines.Add('|---|---:|---|---|')
foreach($r in $missingRows){
    $lines.Add("| $($r.type) | $($r.nativeRootOrdinal) | $($r.name) | $($r.offlineRetailSourceNameClassification) |")
}
$lines.Add('')
$lines.Add('## The 43 differing real PC source body hashes')
$lines.Add('')
$lines.Add('| Type | Root | Name | Candidate source roles | Different byte sizes |')
$lines.Add('|---|---:|---|---|---|')
foreach($r in $conflictRows){
    $lines.Add("| $($r.type) | $($r.nativeRootOrdinal) | $($r.name) | $(($r.sourceRoles) -join ', ') | $($r.differingSerializedBodySizes) |")
}
$lines.Add('')
$lines.Add('Source-owned duplicate bodies cannot be resolved by blindly preferring map.ff or mod.ff; prove engine override ordering and actual graph behavior first.')
$lines.Add('39 World Material graph codec checks pass but 498 other material roots remain without that special corpus; 177 localization source entries have unresolved native packed VALUE pointer rebasing.')
$lines.Add('**NO Xbox FF was generated, NO OAT was run, NO Xbox asset bytes were copied, and NO runtime compatibility is claimed.**')
[IO.File]::WriteAllLines($md,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PROVENANCE-PROGRESS] [##############------] 70% | 43 real body conflicts and missing names classified'
Write-Host "[AZTEC-PROVENANCE] Source-owned conflicts=$($conflictRows.Count), missing owned bodies=$($missingRows.Count); 0 conflicting bodies automatically resolved"
Write-Host "[AZTEC-PROVENANCE] 24 native missing source roots with owned-body names in cached retail zones: $($report.offlineStockBodyNameMatchesForMissing)"
Write-Host '[AZTEC-PROVENANCE-PROGRESS] [####################] 100% | precise source semantics + offline stock name risk report'
Write-Host "[AZTEC-PROVENANCE] STATUS=$($report.status)"
Write-Host "[AZTEC-PROVENANCE] Markdown: $md"
Write-Host "[AZTEC-PROVENANCE] JSON: $json"
Write-Host "[AZTEC-PROVENANCE] CSV: $csv"
Write-Host '[AZTEC-PROVENANCE] NO XBOX. Genuine FF-body ownership/load order and runtime stock residency remain unproven.'
