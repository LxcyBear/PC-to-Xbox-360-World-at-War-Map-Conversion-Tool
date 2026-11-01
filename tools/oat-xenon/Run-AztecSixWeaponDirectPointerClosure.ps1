[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Exact source-PC pointer -> loaded Virtual-block name crosswalk.
# Reuses existing specialist OAT DIRECT pointer evidence; NEVER ranks by
# OAT order and NEVER relabels/modifies 7b Xbox WeaponDef proof containers.
# Completely source-only: NO OAT invocation, .NET, FF build or Xbox.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$names=Join-Path $base 'pc_g11_lightning_six_weapon_packed_name_witness\aztec_six_weapon_packed_name_source_witness.json'
$recovery=Join-Path $base 'pc_g11_lightning_six_weapon_real_source_recovery\aztec_six_real_pc_weapon_source_body_proof.json'
$size=Join-Path $base 'pc_g11_native_ff_size_audit\aztec_native_ff_size_and_source_ledger.json'
$directRoot=Join-Path $repo 'output\multi_map_matrix\runs'
$out=Join-Path $base 'pc_g11_lightning_six_weapon_direct_pointer_closure'
$json=Join-Path $out 'aztec_six_weapon_direct_pointer_body_closure.json'
$md=Join-Path $out 'aztec_six_weapon_direct_pointer_body_closure.md'
foreach($file in @($names,$recovery,$size)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Required existing 71-body original-PC source proof missing: $file"}
}
$n=Get-Content -LiteralPath $names -Raw|ConvertFrom-Json
$r=Get-Content -LiteralPath $recovery -Raw|ConvertFrom-Json
$s=Get-Content -LiteralPath $size -Raw|ConvertFrom-Json
if([string]$n.schema -ne 'WaW360.AztecSixWeaponPackedNameIdentityWitness.v1' -or
   [int]$n.sourceAuthenticXboxWeaponProofBodies -ne 71 -or
   [int]$n.sourceWeaponBodiesWithSyntheticPackedInternalNames -ne 10 -or
   [int]$n.targetsWithOrderOnlyPackedNameCandidates -ne 6 -or
   [bool]$n.sourcePointerToOriginalWeaponNameMappingVerified -or
   [string]$r.schema -ne 'WaW360.AztecSixWeaponRealPcSourceOwnedXboxProof.v1' -or
   [int]$r.sixWithProvenIdenticalOrSingleConvertedXboxBody -ne 0 -or
   [int]$r.originalSixNativeWeaponRootNames -ne 6 -or
   [string]$s.schema -ne 'WaW360.AztecNativeFfSizeAndSourceCoverage.v1' -or
   [string]$s.converted.sha256 -ine 'F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6'){
    throw 'Source name witness, 71 real WeaponDef blobs or protected OAT FF SHA changed.'
}
$protected=[string]$s.converted.path
if(-not(Test-Path -LiteralPath $protected -PathType Leaf) -or
   (Get-FileHash -LiteralPath $protected -Algorithm SHA256).Hash -ine [string]$s.converted.sha256){
    throw 'Protected OAT-passing FF hash differs. No stale pointer/source assignment.'
}
Write-Host '[AZTEC-DIRECT-SIX] ==============================================================='
Write-Host '[AZTEC-DIRECT-SIX] Cross-reference ORIGINAL PC loaded Virtual pointer/string proofs'
Write-Host '[AZTEC-DIRECT-SIX] against 71 verified Xbox owned WeaponDef bodies and 6 exact targets'
Write-Host '[AZTEC-DIRECT-SIX] No ordering inference, no OAT/dotnet/FF/Xbox.'
Write-Host '[AZTEC-DIRECT-PROGRESS] [####----------------] 20% | real Xbox WeaponDef proof/source SHA pinned'

# Revalidate current (not historical) proof files and source hashes. Build a
# source+bodyOffset+rawPointer table with no source-name guesses.
$sourceByPath=@{}
$packedByKey=@{}
$totalPacked=0
$sourceRows=@($n.originalPcSources)
if($sourceRows.Count -ne 2){throw "Expected two exact original PC WeaponDef source files, got $($sourceRows.Count)."}
foreach($src in $sourceRows){
    $source=[IO.Path]::GetFullPath([string]$src.originalPcSource)
    $run=@($r.sourcePcFfRuns|Where-Object {[string]$_.sourcePcFastFile -ieq $source})
    if($run.Count -ne 1 -or [string]$run[0].status -ne 'SOURCE_XBOX_WEAPON_OWNED_PROOF_PASS'){
        throw "Existing original-PC source 7b owned proof no longer PASS: $source"
    }
    $proof=[IO.Path]::GetFullPath([string]$src.xboxWeaponProof)
    if($proof -ine [string]$run[0].proofContainer -or
       -not(Test-Path -LiteralPath $source -PathType Leaf) -or
       -not(Test-Path -LiteralPath $proof -PathType Leaf) -or
       (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ine [string]$run[0].sourceSha256 -or
       (Get-FileHash -LiteralPath $proof -Algorithm SHA256).Hash -ine [string]$run[0].verifiedOwnedContainerSha256){
        throw "Authentic source PC FF or 7b Xbox owned-payload proof SHA changed: $source"
    }
    $all=@($src.allNativeProofLabels)
    if($all.Count -ne [int]$run[0].convertedWeaponBodies -or
       $all.Count -ne [int]$run[0].sourcePhysicalWeaponHeaders){
        throw "Original PC physical weapon and Xbox proof record counts drifted: $source"
    }
    $sourceByPath[$source]=[pscustomobject]@{
        source=$source
        sourceSha256=[string]$run[0].sourceSha256
        sourceRole=[string]$run[0].role
        proof=$proof
        proofSha256=[string]$run[0].verifiedOwnedContainerSha256
        records=$all
        ownedOatWeaponNames=@($src.oatOwnedWeaponNames)
        expectedOriginalPcOwnedWeaponCount=$all.Count
    }
    foreach($rec in $all){
        if(-not [bool]$rec.labelIsPackedSynthetic){continue}
        $ptr=[uint64]$rec.packedNamePointer
        if($ptr -eq 0 -or $ptr -ge 4294967294L -or (($ptr-1) -shr 29) -ne 4){
            throw 'Prior packed synthetic WeaponDef pointer is not a valid original PC Virtual pointer.'
        }
        $raw='{0:X8}' -f $ptr
        $off=[long]$rec.originalPcBodyOffset
        $key=$source+'|'+$off.ToString('X')+'|'+$raw
        if($packedByKey.ContainsKey($key)){throw "Duplicate source physical WeaponDef pointer identity: $key"}
        $packedByKey[$key]=[pscustomobject]@{
            key=$key;source=$source;sourceSha256=[string]$run[0].sourceSha256
            role=[string]$run[0].role;bodyOffset=$off
            raw=$raw;decodedVirtualBlockOffset=[long](($ptr-1) -band 0x1FFFFFFF)
            proof=$proof;proofSha256=[string]$run[0].verifiedOwnedContainerSha256
            proofOrdinal=[int]$rec.ordinal;xboxBlobBytes=[long]$rec.xboxSerializedBodyBytes
            syntheticLabel=[string]$rec.proofLabel
        }
        $totalPacked++
    }
}
if($sourceByPath.Count -ne 2 -or $totalPacked -ne 10){
    throw "Expected 10 real source PC packed-name WeaponDefs across 2 FFs, found $totalPacked."
}
Write-Host '[AZTEC-DIRECT-PROGRESS] [########------------] 40% | verify independent OAT loaded-virtual-pointer traces'

# The project already supports an independently loaded PC Virtual-block
# pointer witness (Run-WaWMultiMapWeaponPointerDirectWitness.ps1). Its
# source SHA/body offset/raw name pointer must match THIS current 7b
# serializer; stale reports are deliberately refused.
$files=@()
if(Test-Path -LiteralPath $directRoot -PathType Container){
    $files=@(Get-ChildItem -LiteralPath $directRoot -File -Recurse -Filter 'weapon_direct_vptr_*.json' |
        Sort-Object LastWriteTimeUtc -Descending)
}
$winner=$null;$winnerFile=$null;$validated=@()
$reportsSeen=0
foreach($file in $files){
    $doc=$null
    try{$doc=Get-Content -LiteralPath $file.FullName -Raw|ConvertFrom-Json}catch{continue}
    if([string]$doc.schema -ne 'WaW360.WeaponDirectVirtualPointerWitness.v1' -or
       [string]$doc.map -ine 'nazi_zombie_aztec_V2.2' -or
       [int]$doc.requested -ne 10 -or [int]$doc.directMatches -ne 10 -or
       -not [bool]$doc.allDirectNamesResolved -or
       [bool]$doc.namesAppliedToProduction -or
       [bool]$doc.nativeXboxReleaseAuthorized -or
       @($doc.rows).Count -ne 10){continue}
    $reportsSeen++
    $used=@{}
    $mapped=New-Object 'System.Collections.Generic.List[object]'
    $valid=$true
    $logs=@{}
    foreach($row in @($doc.rows)){
        $source=[IO.Path]::GetFullPath([string]$row.source)
        $raw=([string]$row.raw).ToUpperInvariant()
        $offset=[long]$row.proofOffset
        $name=[string]$row.directOatLoadedName
        $key=$source+'|'+$offset.ToString('X')+'|'+$raw
        if(-not $packedByKey.ContainsKey($key) -or $used.ContainsKey($key) -or
           [string]$row.status -ne 'DIRECT_POINTER_NAME_IN_OAT' -or
           -not [bool]$row.resolved -or [bool]$row.ordinalInferenceUsed){
            $valid=$false;break
        }
        $actual=$packedByKey[$key]
        if([string]$row.sourceSha256 -ine [string]$actual.sourceSha256 -or
           [string]$row.sourceRole -ine [string]$actual.role -or
           @($sourceByPath[$source].ownedOatWeaponNames|Where-Object {$_ -ieq $name}).Count -ne 1){
            $valid=$false;break
        }
        $path=[string]$row.log
        if(-not(Test-Path -LiteralPath $path -PathType Leaf)){$valid=$false;break}
        if(-not $logs.ContainsKey($path)){$logs[$path]=[IO.File]::ReadAllText($path)}
        $ptr=[Convert]::ToUInt64($raw,16)
        $expectedOffset=[long](($ptr-1) -band 0x1FFFFFFF)
        $pattern='(?m)^\[WAW360-WPTR\] raw='+[regex]::Escape($raw)+
            ' block=4 offset=0x([0-9A-Fa-f]+) status=STRING_AT_DIRECT_VIRTUAL_TARGET name='+
            [regex]::Escape($name)+'\r?$'
        $hits=[regex]::Matches([string]$logs[$path],$pattern)
        if($hits.Count -ne 1 -or
           [Convert]::ToUInt64($hits[0].Groups[1].Value,16) -ne $expectedOffset -or
           $expectedOffset -ne [long]$actual.decodedVirtualBlockOffset){
            $valid=$false;break
        }
        $used[$key]=$true
        $mapped.Add([pscustomobject]@{
            resolvedName=$name;sourcePcFastFile=$actual.source
            sourceRole=$actual.role;sourcePcSha256=$actual.sourceSha256
            originalPcWeaponDefBodyOffset=$actual.bodyOffset
            originalPackedPointer=$actual.raw
            originalPackedVirtualOffset=$actual.decodedVirtualBlockOffset
            xbox7bProofFile=$actual.proof;xbox7bProofSha256=$actual.proofSha256
            xbox7bRecordOrdinal=$actual.proofOrdinal
            realXboxOwnedWeaponBlobBytes=$actual.xboxBlobBytes
            loadedVirtualPointerTraceFile=$path
            identityProof='DIRECT_OAT_PC_LOADED_VIRTUAL_POINTER_TO_NAME_AND_SAME_ORIGINAL_PC_SOURCE_BODY'
            inferredFromOatAssetOrder=$false
            productionXAssetGraftPerformed=$false
        })
    }
    if($valid -and $used.Count -eq 10 -and $mapped.Count -eq 10){
        $winner=$doc;$winnerFile=$file.FullName;$validated=@($mapped.ToArray());break
    }
}
if($null -eq $winner){
    throw "No matching FULL 10/10 original-PC DIRECT Virtual-pointer witness found for current 71 verified Xbox bodies (candidate witness reports checked=$reportsSeen). Run powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapWeaponPointerDirectWitness.ps1' once, then rerun this source-only join. No names or bytes were changed."
}
$targets=@(
    'zombie_perk_bottle_doubletap','zombie_perk_bottle_jugg',
    'zombie_perk_bottle_revive','zombie_perk_bottle_sleight',
    'm7_launcher_zombie','m7_launcher_zombie_upgraded'
)
$targetRows=New-Object 'System.Collections.Generic.List[object]'
foreach($name in $targets){
    $candidate=@($validated|Where-Object {[string]$_.resolvedName -ieq $name})
    if($candidate.Count -ne 1){throw "Original PC direct loaded pointer cannot uniquely map six-target weapon $name."}
    $targetRows.Add($candidate[0])
    Write-Host "[AZTEC-DIRECT-SIX] DIRECT PROVEN $name : $($candidate[0].sourceRole) proof-ordinal=$($candidate[0].xbox7bRecordOrdinal) packed=0x$($candidate[0].originalPackedPointer) bytes=$($candidate[0].realXboxOwnedWeaponBlobBytes)"
}
if($targetRows.Count -ne 6){throw 'Direct six target count missing.'}
New-Item -ItemType Directory -Path $out -Force|Out-Null
$report=[ordered]@{
    schema='WaW360.AztecSixWeaponDirectPointerSourceBodyClosure.v1'
    status='AZTEC_SIX_REAL_PC_NAME_DIRECT_POINTER_AND_VERIFIED_XBOX_OWNED_BODIES_PROVEN_NOT_IN_FF'
    sourceOriginalPcFFs=2
    originalPcSourceOwnedXboxWeaponDefBodiesReadback=71
    pcPackedWeaponNamesDirectLoadedPointerProven=10
    sixNativeWeaponIdentityMatchedExactDirectPointer=6
    pointerWitnessSource=$winnerFile
    pointerWitnessSha256=(Get-FileHash -LiteralPath $winnerFile -Algorithm SHA256).Hash
    allOriginalPcPackedBodyMappings=$validated
    sixProvenNativeNameToSourceXboxBodies=@($targetRows.ToArray())
    previousCrossPcFFSourceConflictsResolved=0
    noXboxRetailDonorBodyUsed=$true
    noAztecXboxFastFileCreatedOrModified=$true
    xboxRuntimeWeaponDependenciesVerified=$false
    readyForXboxHardware=$false
    gate11Closed=$false
    note='Independent loaded-PC Virtual-block direct-pointer name identity is paired to exact source compressed FF SHA/body offset/raw pointer and real already-readback-verified Xbox 7b owned-payload blob. No asset names were inferred from ordinal; no production FF graft, shader/audio/renderer closure or runtime proof.'
}
$report|ConvertTo-Json -Depth 14|Set-Content -LiteralPath $json -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add('# Aztec: six original-PC packed WeaponDef names DIRECTLY proven to Xbox source blobs')
$lines.Add('')
$lines.Add('**Six source identities directly confirmed by loaded Virtual-pointer tracing**; the source-owned Xbox-format WeaponDef bodies were already converted/read back. No FF emitted.')
$lines.Add('Source original PC FFs: 2; verified Xbox WeaponDef bodies: 71; packed names direct-proven: 10/10; selected six matching native-root names: 6/6.')
$lines.Add("Witness: $winnerFile")
$lines.Add('')
$lines.Add('| Real weapon name | Original PC FF role | Packed internal pointer | Original body offset | Xbox proof record | Xbox owned payload bytes |')
$lines.Add('|---|---|---|---|---:|---:|')
foreach($row in $targetRows){
    $lines.Add("| $($row.resolvedName) | $($row.sourceRole) | 0x$($row.originalPackedPointer) | 0x$('{0:X}' -f $row.originalPcWeaponDefBodyOffset) | $($row.xbox7bRecordOrdinal) | $($row.realXboxOwnedWeaponBlobBytes) |")
}
$lines.Add('')
$lines.Add('Every name-to-body association uses original-PC compressed FF SHA256 + physical WeaponDef body offset + exact packed pointer + independent OAT loaded Virtual-block target string, NOT ordinal name inference.')
$lines.Add('These are original-PC-source Xbox owned-payload proofs; no new native root has been inserted. All previous 43 competing PC source bodies, 498 materials, image, shader, audio and Xbox runtime closure remain unresolved.')
[IO.File]::WriteAllLines($md,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-DIRECT-PROGRESS] [################----] 80% | exact 10 source pointer identities + six native WeaponDef roots'
Write-Host '[AZTEC-DIRECT-SIX] Packed WeaponDef Virtual pointers directly proven=10/10; six native target names=6/6.'
Write-Host '[AZTEC-DIRECT-PROGRESS] [####################] 100% | source-owned Xbox body/name join complete; NO FF'
Write-Host "[AZTEC-DIRECT-SIX] STATUS=$($report.status)"
Write-Host "[AZTEC-DIRECT-SIX] JSON: $json"
Write-Host "[AZTEC-DIRECT-SIX] Markdown: $md"
Write-Host '[AZTEC-DIRECT-SIX] All earlier map and source proofs remain unmodified; native Xbox hardware readiness unchanged.'
