[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Diagnose, do NOT apply, original-PC WeaponDef packed-name identities.
# Reuses real 6f/7b Xbox proof containers and fingerprint-pinned original-PC
# OAT --list logs. A matching ordinal is NEVER pointer-to-name proof.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$recovery=Join-Path $base 'pc_g11_lightning_six_weapon_real_source_recovery\aztec_six_real_pc_weapon_source_body_proof.json'
$previous=Join-Path $base 'pc_g11_lightning_six_weapons_43_override_sources\aztec_six_weapon_source_candidates_and_43_override_roles.json'
$size=Join-Path $base 'pc_g11_native_ff_size_audit\aztec_native_ff_size_and_source_ledger.json'
$out=Join-Path $base 'pc_g11_lightning_six_weapon_packed_name_witness'
$json=Join-Path $out 'aztec_six_weapon_packed_name_source_witness.json'
$md=Join-Path $out 'aztec_six_weapon_packed_name_source_witness.md'
foreach($f in @($recovery,$previous,$size)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){throw "Previous original PC source evidence missing: $f"}
}
$r=Get-Content -LiteralPath $recovery -Raw|ConvertFrom-Json
$p=Get-Content -LiteralPath $previous -Raw|ConvertFrom-Json
$sz=Get-Content -LiteralPath $size -Raw|ConvertFrom-Json
if([string]$r.schema -ne 'WaW360.AztecSixWeaponRealPcSourceOwnedXboxProof.v1' -or
   [int]$r.originalSixNativeWeaponRootNames -ne 6 -or
   [int]$r.sixWithProvenIdenticalOrSingleConvertedXboxBody -ne 0 -or
   [bool]$r.hardwareReady -or
   [string]$p.schema -ne 'WaW360.AztecSixWeapon43OverrideSourceTriage.v1' -or
   [int]$p.sixWeaponSourceHintsFound -ne 6 -or
   [int]$p.totalConflictingSourceBodies -ne 43 -or
   [string]$sz.schema -ne 'WaW360.AztecNativeFfSizeAndSourceCoverage.v1' -or
   [string]$sz.converted.sha256 -ine 'F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6'){
    throw 'Six-weapon proof, 43 conflicts, or OAT-passing baseline changed.'
}
if(-not(Test-Path -LiteralPath ([string]$sz.converted.path) -PathType Leaf) -or
   (Get-FileHash -LiteralPath ([string]$sz.converted.path) -Algorithm SHA256).Hash -ine [string]$sz.converted.sha256){
    throw 'Protected Aztec FF SHA changed: refuse stale source name inference.'
}
function Read-Be32([IO.BinaryReader]$reader){
    $bytes=$reader.ReadBytes(4)
    if($bytes.Length -ne 4){throw 'Truncated Xbox WeaponDef proof u32'}
    return [uint32](([uint64]$bytes[0] -shl 24) -bor
        ([uint64]$bytes[1] -shl 16) -bor
        ([uint64]$bytes[2] -shl 8) -bor [uint64]$bytes[3])
}
function Read-WeaponProof([string]$path){
    $stream=[IO.File]::OpenRead($path)
    $reader=New-Object IO.BinaryReader($stream)
    try{
        $magic=[Text.Encoding]::ASCII.GetString($reader.ReadBytes(8)).TrimEnd([char]0)
        if($magic -ne 'W360A7W'){throw "Unexpected WeaponDef proof magic: $magic"}
        $version=Read-Be32 $reader
        $count=Read-Be32 $reader
        $fixed=Read-Be32 $reader
        if($version -ne 8 -or $fixed -ne 0x9AC -or $count -gt 10000){
            throw "Invalid owned Xbox WeaponDef proof header: version=$version records=$count"
        }
        $rows=New-Object 'System.Collections.Generic.List[object]'
        for($i=0;$i -lt [int]$count;$i++){
            $offset=Read-Be32 $reader
            $length=Read-Be32 $reader
            $nameBytes=Read-Be32 $reader
            if($length -lt 0x9AC -or $nameBytes -gt 2048 -or
               $stream.Position+[int64]$nameBytes+[int64]$length -gt $stream.Length){
                throw "Invalid WeaponDef blob record #$i lengths in $path"
            }
            $name=[Text.Encoding]::UTF8.GetString($reader.ReadBytes([int]$nameBytes))
            $synthetic=$name.StartsWith('<packed-name@',[StringComparison]::OrdinalIgnoreCase)
            $ptrRaw=$null;$ptrBlock=$null;$ptrOffset=$null
            if($synthetic){
                $match=[regex]::Match($name,'^<packed-name@0x([0-9a-fA-F]+):0x([0-9a-fA-F]{8})>$')
                if(-not $match.Success){throw "Malformed synthetic name at WeaponDef proof #$i"}
                if([Convert]::ToUInt32($match.Groups[1].Value,16) -ne $offset){
                    throw "Synthetic label/source body offset mismatch at #$i"
                }
                $ptrRaw=[Convert]::ToUInt32($match.Groups[2].Value,16)
                $unbiased=[uint64]$ptrRaw-1
                $ptrBlock=[int]($unbiased -shr 29)
                $ptrOffset=[long]($unbiased -band 0x1FFFFFFF)
            }
            $rows.Add([pscustomobject]@{
                ordinal=$i
                originalPcBodyOffset=[long]$offset
                proofLabel=$name
                labelIsPackedSynthetic=$synthetic
                packedNamePointer=$ptrRaw
                packedNameBlock=$ptrBlock
                packedNameBlockOffset=$ptrOffset
                xboxSerializedBodyBytes=[long]$length
                pointerToNameResolved=$false
            })
            [void]$stream.Seek([long]$length,[IO.SeekOrigin]::Current)
        }
        if($stream.Position -ne $stream.Length){throw 'Trailing bytes in owned WeaponDef proof container'}
        return ,@($rows.ToArray())
    }finally{$reader.Dispose();$stream.Dispose()}
}
function Read-OriginalOatWeapons([string]$path){
    $owned=New-Object 'System.Collections.Generic.List[string]'
    $refOnly=New-Object 'System.Collections.Generic.List[string]'
    foreach($raw in [IO.File]::ReadLines($path)){
        $line=$raw.Trim()
        if($line.StartsWith('[',[StringComparison]::Ordinal)){
            $close=$line.IndexOf(']')
            if($close -ge 0){$line=$line.Substring($close+1).TrimStart()}
        }
        if($line -notmatch '(?i)^weapon\s*,\s*(.*?)\s*$'){continue}
        $name=([string]$Matches[1]).Trim().Trim('"')
        if($name.StartsWith(',',[StringComparison]::Ordinal)){
            $refOnly.Add($name.TrimStart(',').Trim())
        }elseif(-not [string]::IsNullOrWhiteSpace($name)){
            $owned.Add($name)
        }
    }
    return [pscustomobject]@{Owned=@($owned.ToArray());ReferenceOnly=@($refOnly.ToArray())}
}
Write-Host '[AZTEC-WEAPON-NAME] ========================================================'
Write-Host '[AZTEC-WEAPON-NAME] 71/71 Xbox 7b owned WeaponDefs, 0/6 name matches: determine packed-name cause'
Write-Host '[AZTEC-WEAPON-NAME] Use previous real proof hashes and cached original PC OAT names. NO rewrites.'
Write-Host '[AZTEC-WEAPON-PROGRESS] [####----------------] 20% | source proof and protected FF SHA locked'
$results=New-Object 'System.Collections.Generic.List[object]'
$totalProof=0;$totalSynthetic=0;$targetsWithOnlyOrdinal=0
$allTargets=@($p.sixWeaponRoots|ForEach-Object {[string]$_.name})
foreach($run in @($r.sourcePcFfRuns)){
    if([string]$run.status -ne 'SOURCE_XBOX_WEAPON_OWNED_PROOF_PASS'){
        throw "Previously passing 71-body source unexpectedly blocked: $($run.sourcePcFastFile)"
    }
    $original=[string]$run.sourcePcFastFile
    $proof=[string]$run.proofContainer
    if(-not(Test-Path -LiteralPath $original -PathType Leaf) -or
       -not(Test-Path -LiteralPath $proof -PathType Leaf) -or
       (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -ine [string]$run.sourceSha256 -or
       (Get-FileHash -LiteralPath $proof -Algorithm SHA256).Hash -ine [string]$run.verifiedOwnedContainerSha256){
        throw "Authentic WeaponDef original FF or Xbox payload proof SHA drift: $original"
    }
    $hints=@($p.sixWeaponRoots|ForEach-Object {
        @($_.potentialPcSourceFileNameOrOatCandidates)|Where-Object {
            [string]$_.sourceKind -eq 'OTHER_PC_FF_OAT_OWNED_WEAPON_NAME_ONLY' -and
            [string]$_.sourcePcFastFile -ieq $original
        }
    })
    $paths=@($hints|Select-Object -ExpandProperty sourceOatList -Unique)
    if($paths.Count -ne 1 -or -not(Test-Path -LiteralPath $paths[0] -PathType Leaf)){
        throw "Ambiguous saved original PC OAT source listing for FF: $original"
    }
    $index=Read-WeaponProof $proof
    $list=Read-OriginalOatWeapons ([string]$paths[0])
    $known=@($index|Where-Object {-not $_.labelIsPackedSynthetic})
    $synthetic=@($index|Where-Object {$_.labelIsPackedSynthetic})
    $totalProof+=$index.Count
    $totalSynthetic+=$synthetic.Count
    if($index.Count -ne [int]$run.convertedWeaponBodies -or
       $index.Count -ne [int]$run.sourcePhysicalWeaponHeaders){
        throw "Original PC proof container record coverage changed: $original"
    }
    $sameCount=($list.Owned.Count -eq $index.Count)
    $uniqueNames=@($list.Owned|Select-Object -Unique).Count -eq $list.Owned.Count
    $anchorConflicts=New-Object 'System.Collections.Generic.List[object]'
    $knownAnchors=0
    if($sameCount){
        for($i=0;$i -lt $index.Count;$i++){
            if($index[$i].labelIsPackedSynthetic){continue}
            $knownAnchors++
            if([string]$index[$i].proofLabel -ine [string]$list.Owned[$i]){
                $anchorConflicts.Add([pscustomobject]@{
                    ordinal=$i;proofName=$index[$i].proofLabel
                    oatName=$list.Owned[$i]
                })
            }
        }
    }
    $ordinalAligned=($sameCount -and $uniqueNames -and
                     $knownAnchors -gt 0 -and $anchorConflicts.Count -eq 0)
    $targetRows=New-Object 'System.Collections.Generic.List[object]'
    foreach($name in $allTargets){
        $ownedOrdinal=-1
        for($i=0;$i -lt $list.Owned.Count;$i++){
            if([string]$list.Owned[$i] -ieq $name){
                if($ownedOrdinal -ge 0){throw "OAT source repeats target weapon identity: $name"}
                $ownedOrdinal=$i
            }
        }
        if($ownedOrdinal -lt 0){continue}
        $candidate=if($sameCount){$index[$ownedOrdinal]}else{$null}
        $status=if($null -eq $candidate){'NO_SOURCE_PROOF_ORDINAL_LINK'}
            elseif(-not $candidate.labelIsPackedSynthetic){
                if([string]$candidate.proofLabel -ieq $name){'EXACT_PHYSICAL_PROOF_LABEL_NAME'}
                else{'KNOWN_NAME_ORDINAL_CONFLICT_NO_MAPPING'}
            }elseif($ordinalAligned){
                'PACKED_NAME_ORDER_ONLY_PLAUSIBLE_NOT_VERIFIED'
            }else{'PACKED_NAME_UNRESOLVED_ORDER_NOT_PROVEN'}
        if($status -eq 'PACKED_NAME_ORDER_ONLY_PLAUSIBLE_NOT_VERIFIED'){$targetsWithOnlyOrdinal++}
        $targetRows.Add([pscustomobject]@{
            target=$name;originalOatOwnedListOrdinal=$ownedOrdinal;classification=$status
            proofAtSameOrdinal=$candidate
            exactOriginalPackedPointerToNameProven=$false
            productionProofBodyNameCanBeReplaced=$false
        })
        Write-Host "[AZTEC-WEAPON-NAME] $name in $([IO.Path]::GetFileName($original)) : $status"
    }
    $results.Add([pscustomobject]@{
        originalPcSource=$original;role=[string]$run.role
        xboxWeaponProof=$proof
        proofRecords=$index.Count
        packedSyntheticNameProofs=$synthetic.Count
        knownLiteralNameProofs=$known.Count
        oatOwnedWeaponNames=@($list.Owned)
        oatReferenceOnlyNames=@($list.ReferenceOnly)
        sourceOwnedListAndPhysicalProofCountsEqual=$sameCount
        oatOwnedNamesUnique=$uniqueNames
        knownNameAlignedWitnesses=$knownAnchors
        knownNameOrdinalConflicts=@($anchorConflicts.ToArray())
        nameOrderEligibleForUnverifiedHypotheses=$ordinalAligned
        sixTargetSourceEvidence=@($targetRows.ToArray())
        allNativeProofLabels=@($index)
        packedNameVirtualBlockPointerResolvedToString=$false
    })
    Write-Host "[AZTEC-WEAPON-NAME] $([IO.Path]::GetFileName($original)): real Xbox weapon blobs=$($index.Count), packed synthetic=$($synthetic.Count), literal=$($known.Count), OAT owned=$($list.Owned.Count), order-aligned=$ordinalAligned"
}
if($totalProof -ne 71 -or $allTargets.Count -ne 6){
    throw "Expected 71 previous source bodies and 6 native targets; got $totalProof/$($allTargets.Count)"
}
New-Item -ItemType Directory -Path $out -Force|Out-Null
$report=[ordered]@{
    schema='WaW360.AztecSixWeaponPackedNameIdentityWitness.v1'
    status='AZTEC_SIX_WEAPON_PACKED_NAME_POINTER_TO_STRING_NOT_PROVEN'
    sourceAuthenticXboxWeaponProofBodies=$totalProof
    sourceWeaponBodiesWithSyntheticPackedInternalNames=$totalSynthetic
    sixNativeWeaponTargetNames=6
    targetsWithOrderOnlyPackedNameCandidates=$targetsWithOnlyOrdinal
    originalPcSources=@($results.ToArray())
    sourcePointerToOriginalWeaponNameMappingVerified=$false
    recoveredSixWeaponBodiesWithVerifiedNames=0
    original43PcSourceBodyConflictsResolved=0
    nativeFastFileModified=$false
    readyForXboxHardware=$false
    note='The OAT --list order is XAsset table order, while proof body order is physical offset order. Even if all known names align and the six targets have packed names at the same ordinal, this is ONLY a hypothesis; do not replace synthetic labels without decoding original packed virtual pointer target and verifying target string.'
}
$report|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $json -Encoding UTF8
$mdLines=New-Object 'System.Collections.Generic.List[string]'
$mdLines.Add('# Aztec six missing weapon names: original PC packed WeaponDef name identity witness')
$mdLines.Add('')
$mdLines.Add("71/71 source-authentic Xbox owned WeaponDef blobs remain verified. Synthetic packed PC source labels: $totalSynthetic/71. Six native names with order-only packed candidates: $targetsWithOnlyOrdinal/6.")
foreach($source in $results){
    $mdLines.Add('')
    $mdLines.Add("## "+[IO.Path]::GetFileName([string]$source.originalPcSource))
    $mdLines.Add("OAT owned=$(@($source.oatOwnedWeaponNames).Count), source physical proof=$($source.proofRecords), packed synthetic=$($source.packedSyntheticNameProofs), known anchors=$($source.knownNameAlignedWitnesses), anchor conflicts=$(@($source.knownNameOrdinalConflicts).Count), order hypothesis eligible=$($source.nameOrderEligibleForUnverifiedHypotheses)")
    foreach($row in @($source.sixTargetSourceEvidence)){
        $mdLines.Add("- "+$row.target+" : "+$row.classification)
    }
}
$mdLines.Add('')
$mdLines.Add('**NO target original-PC packed virtual pointer has been resolved to a verified name string by this diagnostic.** OAT list asset order alone is never sufficient to relabel source-native proof bodies. No FF, console, OAT rerun or native asset selection.')
[IO.File]::WriteAllLines($md,[string[]]$mdLines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-WEAPON-PROGRESS] [################----] 80% | classified 6 names vs 71 real body proof labels'
Write-Host "[AZTEC-WEAPON-NAME] Original PC 7b Xbox proof bodies=71; packed synthetic labels=$totalSynthetic; six targets with only ordinal hypotheses=$targetsWithOnlyOrdinal"
Write-Host '[AZTEC-WEAPON-PROGRESS] [####################] 100% | precise packed-name blockers documented'
Write-Host "[AZTEC-WEAPON-NAME] STATUS=$($report.status)"
Write-Host "[AZTEC-WEAPON-NAME] Report: $md"
Write-Host "[AZTEC-WEAPON-NAME] JSON: $json"
Write-Host '[AZTEC-WEAPON-NAME] Production body mapping remains fenced until original PC virtual-block pointer/name rebasing is proven. NO XBOX.'
