[CmdletBinding()]
param([switch]$SkipSourceHash)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Aztec fast source/asset triage. One root-level CSV for all 8,509 native
# identities + per-type shell/real-body ledger + optional PC OAT name
# and retail image identity evidence. No .NET build, OAT, FF emission,
# Xbox file graft or hardware step. Names are NOT ownership/residency.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$work=Join-Path $base 'native_material_mapping'
$identityPath=Join-Path $work 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$sizePath=Join-Path $work 'pc_g11_native_ff_size_audit\aztec_native_ff_size_and_source_ledger.json'
$proofPath=Join-Path $work 'pc_g11_image_root_links_probe\oat_loaded_six_image_root_links\aztec_gate11_independent_six_native_name_root_links.json'
$baselinePath=Join-Path $base 'second_map_baseline.json'
$imagePath=Join-Path $work 'pc_fastfile_image_sources\aztec_image_dependency_triage.json'
$pcImageInventoryPath=Join-Path $work 'pc_fastfile_image_sources\pc_fastfile_image_sources.json'
$imageSourcePath=Join-Path $work 'pc_fastfile_image_sources\aztec_external_pc_image_sources.json'
$imageExtendedPath=Join-Path $work 'pc_fastfile_image_sources\aztec_external_pc_image_sources_extended.json'
$out=Join-Path $work 'pc_g11_lightning_all_asset_verification'
$csv=Join-Path $out 'aztec_all_8509_roots_evidence.csv'
$summaryPath=Join-Path $out 'aztec_lightning_verification_summary.json'
$md=Join-Path $out 'aztec_lightning_verification_priorities.md'
foreach($f in @($identityPath,$sizePath,$proofPath,$baselinePath)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){throw "Missing required prior evidence: $f"}
}
$identity=Get-Content -LiteralPath $identityPath -Raw|ConvertFrom-Json
$size=Get-Content -LiteralPath $sizePath -Raw|ConvertFrom-Json
$proof=Get-Content -LiteralPath $proofPath -Raw|ConvertFrom-Json
$baseline=Get-Content -LiteralPath $baselinePath -Raw|ConvertFrom-Json
$rsha='E0F3C244DD7B72D0FE71C05FAF014C2C9BFB05F770183954B3E7CDE625EBA98F'
$csha='F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6'
if([string]$identity.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
   [int]$identity.assets -ne 8509 -or -not [bool]$identity.typeSequenceVerified -or
   @($identity.entries).Count -ne 8509 -or
   [string]$size.schema -ne 'WaW360.AztecNativeFfSizeAndSourceCoverage.v1' -or
   [string]$size.status -ne 'AZTEC_NATIVE_FF_SIZE_AUDIT_PASS_NOT_HARDWARE_READY' -or
   [int]$size.rootCoverage.total -ne 8509 -or
   [int]$size.rootCoverage.originalPcSourceBodies -ne 185 -or
   [int]$size.rootCoverage.nativeRendererGraphReplays -ne 506 -or
   [int]$size.rootCoverage.partiallyConvertedGfxWorldRoots -ne 1 -or
   [int]$size.rootCoverage.nameShellOrUnverifiedRoots -ne 7817 -or
   [string]$size.retail.sha256 -ine $rsha -or
   [string]$size.converted.sha256 -ine $csha -or
   [long]$size.retail.compressedBytes -ne 82739469L -or
   [long]$size.converted.compressedBytes -ne 4510695L -or
   [string]$proof.schema -ne 'WaW360.AztecGate11SixImageRootNameAliasReadback.v1' -or
   [string]$proof.newNativeFfSha256 -ine $csha -or
   [int]$proof.sourceNamedGfxImagePointersResolved -ne 6 -or
   -not [bool]$proof.previous11Cells740Aabb14974IndexesStillMatch -or
   -not [bool]$proof.previous4LightGridGroupsAnd1903ShadowRefsStillMatch -or
   [bool]$proof.xboxHardwareReady -or
   [string]$baseline.format -ne 'WaW360.SecondMapBaseline.v1' -or
   [string]$baseline.mapName -ine 'nazi_zombie_aztec_V2.2'){
    throw 'Root identity, exact source/size ledger, OAT proof, or original-PC source identity drifted. Refuse stale/mixed evidence.'
}
if(-not [bool]$size.rootCoverage.representsRootCountsNotByteContributions){
    throw 'Per-root body-provenance ledger did not preserve byte-mass limitation.'
}
Write-Host '[AZTEC-LIGHTNING] ======================================================'
Write-Host '[AZTEC-LIGHTNING] ALL 8509 ROOTS: cached exact PC names vs native identities'
Write-Host '[AZTEC-LIGHTNING] This is a SOURCE TRIAGE, not a magic 7817-body conversion.'
Write-Host '[AZTEC-LIGHTNING-PROGRESS] [####----------------] 20% | source and root manifests SHA-locked'
$ref=[string]$size.retail.path
$candidate=[string]$size.converted.path
if(-not(Test-Path -LiteralPath $ref -PathType Leaf) -or
   -not(Test-Path -LiteralPath $candidate -PathType Leaf) -or
   (Get-FileHash -LiteralPath $ref -Algorithm SHA256).Hash -ine $rsha -or
   (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -ine $csha){
    throw 'Original Xbox or current passing FF changed SHA. Never silently switch baselines.'
}

# Parse existing OAT --list evidence ONLY. Checking source file byte sizes and
# optionally SHA proves the path currently exists, but a historical OAT log
# is still not an authenticated fresh ownership/payload or Xbox residency test.
# Avoid claiming "all stock" based on a retail identity name.
$pcSourceHashes=New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
if(Test-Path -LiteralPath $pcImageInventoryPath -PathType Leaf){
    $sourceInventory=Get-Content -LiteralPath $pcImageInventoryPath -Raw|ConvertFrom-Json
    if([string]$sourceInventory.schema -ne 'WaW360.AztecPcFastfileImageSourceCensus.v1' -or
       [int]$sourceInventory.nativeImageRoots -ne 666 -or
       @($sourceInventory.sourceFastfiles).Count -ne 3){
        throw 'Original-PC FastFile image-source SHA inventory schema/count changed.'
    }
    foreach($x in @($sourceInventory.sourceFastfiles)){
        $pathKey=[IO.Path]::GetFullPath([string]$x.SourceFastFile)
        if($pcSourceHashes.ContainsKey($pathKey)){
            throw "Duplicate original PC source SHA inventory $pathKey"
        }
        $pcSourceHashes.Add($pathKey,[string]$x.Sha256)
    }
}
$pcRoles=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
$sourceRuns=New-Object 'System.Collections.Generic.List[object]'
$missingLogs=New-Object 'System.Collections.Generic.List[string]'
$ffOrdinal=0
foreach($ff in @($baseline.fastfiles)){
    $ffOrdinal++
    $role=[string]$ff.Role
    if($role -notin @('map.ff','map_patch.ff','mod.ff')){continue}
    $pcPath=[string]$ff.Path
    if(-not(Test-Path -LiteralPath $pcPath -PathType Leaf) -or
       [long](Get-Item -LiteralPath $pcPath).Length -ne [long]$ff.Bytes){
        throw "Original PC source file absent/changed length: $pcPath"
    }
    if([bool]$ff.Success -eq $false -or [int]$ff.ExitCode -ne 0){
        $missingLogs.Add("ORIGINAL_PC_OAT_BASELINE_NOT_PASSED: $role $pcPath")
        $sourceRuns.Add([pscustomobject]@{Role=$role;Source=$pcPath;OatList='';Rows=0;Status='BASELINE_OAT_NOT_PASSED'})
        continue
    }
    $pcFull=[IO.Path]::GetFullPath($pcPath)
    $shaStatus='HASH_SKIPPED'
    if(-not $SkipSourceHash){
        $pcSha=(Get-FileHash -LiteralPath $pcPath -Algorithm SHA256).Hash
        if($pcSourceHashes.ContainsKey($pcFull)){
            if($pcSha -ine $pcSourceHashes[$pcFull]){
                throw "Original-PC OAT evidence source content changed SHA: $pcFull"
            }
            $shaStatus='EXACT_PC_SOURCE_SHA_VERIFIED'
        }else{
            $shaStatus='PC_SOURCE_SHA_NOT_PINNED'
        }
    }
    $stem=([IO.Path]::GetFileNameWithoutExtension($pcPath)) -replace '[^A-Za-z0-9_.-]','_'
    $listPath=Join-Path $base ('oat_asset_lists\{0:D2}_{1}.list.log' -f $ffOrdinal,$stem)
    $metaPath=($listPath -replace '\.list\.log$','.meta.json')
    $metaGood=$false
    if(Test-Path -LiteralPath $metaPath -PathType Leaf){
        $meta=Get-Content -LiteralPath $metaPath -Raw|ConvertFrom-Json
        $pcFile=Get-Item -LiteralPath $pcPath
        $fp="$($pcFile.FullName)|$([long]$pcFile.Length)|$($pcFile.LastWriteTimeUtc.Ticks)"
        $metaGood=([string]$meta.Fingerprint -ceq $fp -and [int]$meta.ExitCode -eq 0)
    }
    if(-not(Test-Path -LiteralPath $listPath -PathType Leaf) -or -not $metaGood){
        [void]$missingLogs.Add("MISSING_OR_STALE_OAT_LIST_META: $listPath")
        $sourceRuns.Add([pscustomobject]@{Role=$role;Source=$pcPath;OatList=$listPath;Rows=0;Status='MISSING_OR_STALE_OAT_LIST_META';SourceHashStatus=$shaStatus})
        continue
    }
    $rows=0
    foreach($line in [IO.File]::ReadLines($listPath)){
        if($line -match '^\s*(?:\[[^\]]+\]\s*)?([A-Za-z][A-Za-z0-9_]*)\s*,\s*(.*?)\s*$'){
            $type=[string]$Matches[1].ToLowerInvariant()
            $name=[string]$Matches[2].Trim()
            if($type -eq 'techset'){$type='techniqueset'}
            if($name.Length -eq 0){continue}
            $key=$type+'|'+$name
            if(-not $pcRoles.ContainsKey($key)){
                $pcRoles.Add($key,(New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)))
            }
            [void]$pcRoles[$key].Add($role)
            $rows++
        }
    }
    $sourceRuns.Add([pscustomobject]@{
        Role=$role;Source=$pcPath;OatList=$listPath;Rows=$rows;SourceHashStatus=$shaStatus;Status=$(if($rows -gt 0){'PC_OAT_LIST_NAME_ROWS'}else{'EMPTY_OR_UNRECOGNIZED_LIST'})
    })
}
if(@($sourceRuns.ToArray()).Count -ne 3){throw 'Expected exactly three original PC FastFile roles.'}
Write-Host "[AZTEC-LIGHTNING] PC OAT cached source names indexed=$($pcRoles.Count); missing logs=$($missingLogs.Count)"
Write-Host '[AZTEC-LIGHTNING-PROGRESS] [########------------] 40% | 67-zone image candidate identities, source ownership still unproven'

function ImageKey([string]$value){
    $v=$value.Trim().ToLowerInvariant().Replace('\','/')
    $j=$v.LastIndexOf('/images/',[StringComparison]::Ordinal)
    if($j -ge 0){$v=$v.Substring($j+8)}
    elseif($v.StartsWith('images/')){$v=$v.Substring(7)}
    foreach($suffix in @('.iwi','.dds','_wj')){
        if($v.EndsWith($suffix)){$v=$v.Substring(0,$v.Length-$suffix.Length)}
    }
    return $v
}
$imageRows=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
$imageTriageStatus='NOT_PRESENT'
$imageNumbers=$null
if(Test-Path -LiteralPath $imagePath -PathType Leaf){
    $it=Get-Content -LiteralPath $imagePath -Raw|ConvertFrom-Json
    if([string]$it.schema -ne 'WaW360.AztecImageDependencyTriage.v1' -or
       [string]$it.status -ne 'AZTEC_IMAGE_DEPENDENCY_TRIAGE_PASS' -or
       [int]$it.nativeImageRoots -ne 666 -or
       @($it.rows).Count -ne 654 -or
       [int]$it.listedInCore8RetailZones -ne 302 -or
       [int]$it.newlyListedInOther59Zones -ne 171 -or
       [int]$it.missingFromAll67RetailZones -ne 181 -or
       [bool]$it.productionImageGraftReady){
        throw 'Previously verified 67-zone image name-only triage changed. Do not accept guessed residency.'
    }
    foreach($entry in @($it.rows)){
        $key=ImageKey ([string]$entry.Key)
        if($imageRows.ContainsKey($key)){throw "Nonunique image triage identity: $key"}
        $imageRows.Add($key,$entry)
    }
    $imageTriageStatus='67_ZONE_NAME_ONLY_TRIAGE_AVAILABLE'
    $imageNumbers=[ordered]@{
        missingNameRecords=654
        listedInCore8ZoneNames=302
        listedInOther59ZoneNames=171
        notListedIn67ZoneNames=181
        provenRuntimeResident=0
    }
}
$pcSourceAudit='NOT_PRESENT'
foreach($target in @($imageExtendedPath,$imageSourcePath)){
    if(Test-Path -LiteralPath $target -PathType Leaf){
        $sourceEvidence=Get-Content -LiteralPath $target -Raw|ConvertFrom-Json
        if([string]$sourceEvidence.schema -ne 'WaW360.AztecExternalPcImageSourceAudit.v1'){
            throw 'External source candidate file has unfamiliar schema.'
        }
        $pcSourceAudit=[string]$sourceEvidence.status
        break
    }
}

# Asset names are safe identity keys. A name in PC list only proves the
# source parser/list observed it, NOT that converted/native body is complete.
# An Xbox retail file name match is NOT Xbox Title Update runtime residency.
$generated=@(
    '*reflection_probe0','*reflection_probe1',
    '*lightmap0_primary','*lightmap0_secondary',
    '*lightmap1_primary','*lightmap1_secondary'
)
$generatedSet=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($name in $generated){[void]$generatedSet.Add($name)}
$rows=New-Object 'System.Collections.Generic.List[object]'
$byType=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
$seenIndices=New-Object 'System.Collections.Generic.HashSet[int]'
$pcMatched=0;$retailListed=0;$generatedSeen=0
foreach($entry in @($identity.entries)){
    $index=[int]$entry.Index
    $type=([string]$entry.OatTypeName).ToLowerInvariant()
    $name=[string]$entry.Name
    if($index -lt 0 -or $index -ge 8509 -or -not $seenIndices.Add($index) -or
       [string]::IsNullOrWhiteSpace($type)){
        throw "Invalid, duplicated, or untyped root ordinal $index"
    }
    $key=$type+'|'+$name
    $roles=''
    $fromPc=$pcRoles.ContainsKey($key)
    if($fromPc){
        $roles=(@($pcRoles[$key] | ForEach-Object {[string]$_}) | Sort-Object) -join ';'
        $pcMatched++
    }
    $imgRetail=''
    $imgRole=''
    if($type -eq 'image'){
        $ik=ImageKey $name
        if($imageRows.ContainsKey($ik)){
            $item=$imageRows[$ik]
            $imgRole=[string]$item.AuditPartition
            $zones=@($item.RetailZones)
            $imgRetail=if($zones.Count -gt 0){'NAME_IN_RETAIL_ZONE_LIST'}else{'NO_NAME_IN_67_RETAIL_ZONES'}
            if($zones.Count -gt 0){$retailListed++}
        }else{
            $imgRetail='NOT_IN_654_FAILURE_NAME_SCOPE'
        }
    }
    $gfxLink=($type -eq 'image' -and $generatedSet.Contains($name))
    if($gfxLink){$generatedSeen++}
    $row=[pscustomobject]@{
        Index=$index
        Type=$type
        Name=$name
        ExactNameSeenInPcOatList=$fromPc
        PcMapFastFileRoles=$roles
        PcNameEvidenceOnly=$(if($fromPc){'NAME_MATCH_NOT_BODY_OWNERSHIP'}else{'NO_MATCH_OBSERVED_OR_LOG_ABSENT'})
        RetailImageNameEvidence=$imgRetail
        ImageNameAuditPartition=$imgRole
        SixGfxWorldNameRootPointerOatVerified=$gfxLink
        IndividualBodyProvenByThisSweep=$false
        StockRuntimeResidencyProven=$false
        XboxHardwareReady=$false
        NeededValidation=$(switch($type){
            'sound' {'PC_ALIAS_GRAPH_AND_XBOX_SOUND_SEMANTICS'}
            'loadedsound' {'ORIGINAL_PC_AUDIO_BYTES_AND_XENON_STREAMING_OR_EXTERNAL_XMA'}
            'image' {'SOURCE_PIXEL_LOADDEF_OR_AUTHENTIC_ENGINE_GENERATION'}
            'xanim' {'PC_XANIM_TRANSFORM_GRAPH_AND_NATIVE_READBACK'}
            'xmodel' {'SOURCE_MESH_OR_STOCK_RUNTIME_RESIDENCY'}
            'material' {'MATERIAL_CHILD_GRAPH_AND_STATEBITS_RUNTIME_PROOF'}
            'fx' {'SOURCE_FX_NESTED_POINTER_GRAPH'}
            'weapon' {'SOURCE_WEAPONDEF_GRAPH_NOT_NAME_SHELL'}
            'rawfile' {'PER_ROOT_SCRIPT_BODY_AND_DEPENDENCIES'}
            'gfxworld' {'WORLD_DPVS_MATERIAL_AND_RUNTIME_SEMANTICS'}
            'techniqueset' {'RENDERER_GRAPH_RUNTIME_SEMANTICS'}
            'pixelshader' {'SHADER_BLOB_RUNTIME_SEMANTICS'}
            default {'ORIGINAL_PC_BODY_OR_PROVEN_RUNTIME_EXTERNAL'}
        })
    }
    [void]$rows.Add($row)
    if(-not $byType.ContainsKey($type)){
        $byType.Add($type,([pscustomobject]@{Type=$type;NativeRoots=0;PcNameMatches=0;ImageRetailListMatches=0;SixWorldLinkedImageNames=0}))
    }
    $g=$byType[$type]
    $g.NativeRoots++
    if($fromPc){$g.PcNameMatches++}
    if($imgRetail -eq 'NAME_IN_RETAIL_ZONE_LIST'){$g.ImageRetailListMatches++}
    if($gfxLink){$g.SixWorldLinkedImageNames++}
}
if($rows.Count -ne 8509 -or $seenIndices.Count -ne 8509 -or $generatedSeen -ne 6){
    throw 'Full 8509 identity sequence or exact six GfxWorld source names changed.'
}
foreach($typeRow in @($size.types)){
    $type=[string]$typeRow.name
    if(-not $byType.ContainsKey($type) -or
       [int]$byType[$type].NativeRoots -ne [int]$typeRow.nativeRoots){
        throw "Exact original Xbox native identity type count diverged: $type"
    }
}
Write-Host '[AZTEC-LIGHTNING-PROGRESS] [##############------] 70% | 8509 per-root CSV and per-type priorities'
New-Item -ItemType Directory -Force -Path $out|Out-Null
$rows|Sort-Object Index|Export-Csv -LiteralPath $csv -Encoding UTF8 -NoTypeInformation
$priorities=@($size.types|Sort-Object nameShellOrUnverified -Descending)
$typeStats=@(
    foreach($t in $priorities){
        $key=[string]$t.name
        $new=$byType[$key]
        [pscustomobject]@{
            type=$key
            totalNativeRoots=[int]$t.nativeRoots
            sourceEmittedBodiesAggregate=[int]$t.originalPcSourceBodies
            rendererGraphReplayAggregate=[int]$t.nativeRendererGraphReplays
            partialWorldAggregate=[int]$t.partiallyConverted
            shellOrUnverifiedAggregate=[int]$t.nameShellOrUnverified
            exactPcOatNameMatches=[int]$new.PcNameMatches
            xboxRetailImageNameMatchesOnly=[int]$new.ImageRetailListMatches
            verifiedRuntimeResidentRoots=0
            individuallySourceBodyProvenByThisSweep=0
        }
    }
)
$completePcLogs=($missingLogs.Count -eq 0 -and @($sourceRuns.ToArray()|Where-Object {[int]$_.Rows -le 0}).Count -eq 0)
$summary=[ordered]@{
    schema='WaW360.AztecLightningAllRootSourceIdentityTriage.v1'
    status=$(if($completePcLogs){'AZTEC_ALL_8509_ROOTS_TRIAGED_CONTENT_CLOSURE_BLOCKED'}else{'AZTEC_ALL_8509_ROOTS_TRIAGED_PC_OAT_LIST_EVIDENCE_PARTIAL'})
    nativeRootsEnumerated=8509
    nativeOatPassingCandidateSha256=$csha
    workingXboxReferenceSha256=$rsha
    originalPcSourceFastFiles=3
    existingPcOatListLogsComplete=$completePcLogs
    originalPcFileHashesComparedToPinnedSourceInventory=([bool](-not $SkipSourceHash -and $pcSourceHashes.Count -eq 3))
    rawSourceFingerprintsAreHistoricalCacheOnly=$true
    sourceListRuns=$sourceRuns.ToArray()
    missingPcOatListFiles=$missingLogs.ToArray()
    exactPcOatTypeAndNameMatches=$pcMatched
    exactPcOatMatchesAreOnlyNameEvidence=$true
    priorOriginalPcEmittedBodyRootCount=185
    priorNativeRendererReplayRootCount=506
    priorPartialSourceWorldRootCount=1
    priorShellOrUnverifiedRootCount=7817
    bodyOwnershipProvenPerIndividualRootByThisSweep=0
    stockRuntimeResidentRootsIndependentlyProvenByThisSweep=0
    nativeSixGeneratedImageNamePointersOatVerified=$generatedSeen
    nativeSixGeneratedImageRealPixelBodiesProven=0
    imageRetailIdentityTriage=$imageTriageStatus
    imageRetailTriage=$imageNumbers
    optionalExternalPcImageCandidateStatus=$pcSourceAudit
    categoryCoverage=$typeStats
    compressedSizeGapBytes=[long]$size.delta.compressedBytes
    decompressedSizeGapBytes=[long]$size.delta.decompressedBytes
    rootCsv=$csv
    reportMarkdown=$md
    limitations=@(
        'Every 8509 root/type/name is native OAT-verified, but a correct root ID does not mean an owned body is valid.',
        'Cached original-PC OAT list rows prove type/name observations, not source-owned payload, pointer topology, per-root converted bytes or runtime eligibility.',
        '67 retail-zone image identity matches are NOT title-update stock residency or permission to copy donor image data.',
        '185 PC source body and 506 replay totals are aggregate emitter measurements and cannot be assigned to a particular root without exact typed per-root graft evidence.',
        'Unverified roots are not a count of missing file data; some may be legitimate shared-zone dependencies or audio streamed from the usermap sounds folder.',
        'The six loaded source world image aliases have no verified texture pixels; one stock model and material stateBits still require closure.',
        'Neither FF changed; no .NET build, OAT rerun, Xbox deployment, or synthetic asset graft was attempted.'
    )
    verifiedAllAssetBodies=$false
    verifiedStockRuntimeResidency=$false
    fullGate11Passed=$false
    hardwareReady=$false
}
$summary|ConvertTo-Json -Depth 9|Set-Content -LiteralPath $summaryPath -Encoding UTF8
$report=New-Object 'System.Collections.Generic.List[string]'
[void]$report.Add('# AZTEC - LIGHTNING ALL-ASSET EVIDENCE TRIAGE')
[void]$report.Add('')
[void]$report.Add('Read-only cached-evidence sweep across 8,509 native OAT root identities. It is **NOT** a full body/runtime verification.')
[void]$report.Add('')
[void]$report.Add('Root identities=8,509; source-owned emitted bodies=185 aggregate, renderer graph replays=506, partially source-owned GfxWorld=1, unverified/shells=7,817.')
[void]$report.Add("Exact type/name entries seen in original PC OAT lists=$pcMatched; historical PC list evidence complete=$completePcLogs. **Name matches are never body proofs.**")
[void]$report.Add('Runtime resident stock roots independently proven=0 by this sweep. Six source world generated image root pointers pass OAT but all six image pixel bodies remain unresolved.')
[void]$report.Add('')
[void]$report.Add('## Native roots by largest unverified types')
[void]$report.Add('')
[void]$report.Add('| Type | Native roots | Source body count (aggregate) | Shell/unverified | PC OAT same-name matches | Retail image names only |')
[void]$report.Add('|---|---:|---:|---:|---:|---:|')
foreach($stat in @($typeStats|Select-Object -First 18)){
    [void]$report.Add("| $($stat.type) | $($stat.totalNativeRoots) | $($stat.sourceEmittedBodiesAggregate) | $($stat.shellOrUnverifiedAggregate) | $($stat.exactPcOatNameMatches) | $($stat.xboxRetailImageNameMatchesOnly) |")
}
[void]$report.Add('')
[void]$report.Add('## Work plan ranked by root count and blocking dependency')
[void]$report.Add('')
[void]$report.Add('1. Sound + LoadedSound (5,306 shells): compare original-PC typed sound aliases/loaded-vs-streamed source metadata against legitimate Xbox runtime/required external XMA; do NOT create silent stubs as production.')
[void]$report.Add('2. Images (664 shells): use existing PC source IWI census and 67-zone name triage to recover actual texture bodies; identify generated star images as runtime-generated or source-owned by independent semantic evidence.')
[void]$report.Add('3. Animations (602 shells): reuse the original PC XAnim Xenon quaternion/bone conversion and native readback; join exact root-level source proofs.')
[void]$report.Add('4. Materials / XModels / FX: preserve 39 material and 28 model source proofs; build remaining original PC body graphs and verify material runtime stateBits and stock XModel residency.')
[void]$report.Add('5. Weapons / scripts / localize / stringtables: no asset removal without original-PC graph or verified external runtime reference.')
[void]$report.Add('')
[void]$report.Add("Root-level CSV: $csv")
[void]$report.Add("Summary JSON: $summaryPath")
[void]$report.Add('')
[void]$report.Add('**Exact per-asset byte mass is not measured.** Native block header sizes represent shared capacity, not the amount of sound or texture data.')
[void]$report.Add('')
[void]$report.Add('**No Xbox authorization.** Per-type and source OAT-list names can prioritize conversion, but cannot verify a missing body or the stock game residency on their own.')
[IO.File]::WriteAllLines($md,[string[]]$report.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-LIGHTNING-PROGRESS] [####################] 100% | 8509 root rows and ranked closure priorities written'
Write-Host "[AZTEC-LIGHTNING] original-PC OAT same-name rows=$pcMatched; real bodies emitted (aggregate)=185; renderer replays=506; missing/unverified roots=7817"
Write-Host "[AZTEC-LIGHTNING] image 67-zone list-identity matches among unresolved sample=$retailListed; runtime-resident proven=0"
foreach($p in @($typeStats|Select-Object -First 10)){
    Write-Host "[AZTEC-LIGHTNING] $($p.type): shells=$($p.shellOrUnverifiedAggregate) PC-name-hits=$($p.exactPcOatNameMatches)"
}
Write-Host "[AZTEC-LIGHTNING] CSV: $csv"
Write-Host "[AZTEC-LIGHTNING] Report: $md"
Write-Host "[AZTEC-LIGHTNING] JSON: $summaryPath"
Write-Host '[AZTEC-LIGHTNING] RESULT: SOURCE/EVIDENCE INVENTORY ONLY; actual 7817 unverified bodies still need conversion/proof. NO XBOX.'
