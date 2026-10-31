[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Read-only real-asset gate. Original-PC source or verified Xbox stock ONLY.
# A 476-byte FlameTable and the six sound alias records are genuine, but
# neither material pointers nor flame names are permitted to become NULL
# in production simply because their target assets are not yet resolved.
# Never create an FF, OAT candidate, empty/filler material or fake image.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$flameFile=Join-Path $base 'pc_g11_lightning_weapon63_flame_parent\aztec_63_original_pc_flame_parent_source_witness.json'
$directWork=Join-Path $repo '.deps\aztec-w63-direct-ff-growth'
$directFf=Join-Path $directWork 'scratch_emit\nazi_zombie_aztec.ff'
$directReport=Join-Path $directWork 'aztec_weapon63_ff_growth_63.json'
$out=Join-Path $base 'pc_g11_lightning_weapon63_flame_parent\aztec_63_original_pc_flame_real_asset_dependency_audit.json'
foreach($file in @($flameFile,$directFf,$directReport)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){
        throw "Cannot audit source-owned FlameTable dependency without its original proof and previous OAT-good FF: $file"
    }
}
$f=Get-Content -LiteralPath $flameFile -Raw|ConvertFrom-Json
$d=Get-Content -LiteralPath $directReport -Raw|ConvertFrom-Json
$ffSha=(Get-FileHash -LiteralPath $directFf -Algorithm SHA256).Hash
if([string]$f.schema -ne 'WaW360.AztecWeapon63OriginalPcFlameParentSourceWitness.v1' -or
   [string]$f.status -ne 'AZTEC_63_WEAPON_FLAME_PARENT_ORIGINAL_PC_OWNERSHIP_PROVEN_NOT_IN_FF' -or
   [int]$f.originalPcWeaponBodiesRechecked -ne 63 -or
   [int]$f.inlineOriginalPcFlameParentTables -ne 4 -or
   [int]$f.nullOriginalPcFlameParentFields -ne 122 -or
   [int]$f.packedOriginalPcFlameParentFieldsFenced -ne 0 -or
   [long]$f.originalPcOwnedInlineFlameParentBytes -ne 1904L -or
   [int]$f.originalPcOwnedNestedFlameSoundAliasRecords -ne 6 -or
   [int]$f.originalPcOwnedNestedFlameSoundAliasNames -ne 0 -or
   -not [bool]$f.originalPcProofBodiesAllShaValidated -or
   -not [bool]$f.noDonorOrOracleBytes -or
   -not [bool]$f.parentSourcePointersMustNotBeCopiedRawIntoNativeFf -or
   -not [bool]$f.noNativeFastfileCreatedOrModified -or
   [bool]$f.xboxHardwareReady -or
   [string]$d.status -ne 'AZTEC_W63_160_REAL_PC_DIRECT_SOUND_ALIASES_NATIVE_FF_OAT_PASS_FLAME_AND_AUDIO_BLOCKED' -or
   [int]$d.nativeWeaponRootScalarXstringCanariesEmitted -ne 63 -or
   [int]$d.originalPcDirectSoundAliasesActuallyEmitted -ne 160 -or
   [int]$d.originalPcBounceAliasRecordsActuallyEmitted -ne 62 -or
   [long]$d.newScratchFastFileBytes -ne 4514599L -or
   [long](Get-Item -LiteralPath $directFf).Length -ne 4514599L -or
   [string]$d.newScratchFastFileSha256 -ine $ffSha){
    throw 'Original-PC real-asset FlameTable source witness or previously passing native FF has drifted. No fake replacement permitted.'
}

function Classify-SourcePointer([string]$raw){
    if($raw -notmatch '^0x[0-9a-fA-F]{8}$'){throw "Invalid original PC Xbox-endian source pointer: $raw"}
    switch($raw.ToUpperInvariant()){
        '0X00000000' {return 'SOURCE_NULL'}
        '0XFFFFFFFF' {return 'SOURCE_INLINE_OWNED_REQUIRES_CHILD_PROOF'}
        '0XFFFFFFFE' {return 'SOURCE_INLINE_OWNED_REQUIRES_CHILD_PROOF'}
        default {return 'SOURCE_PACKED_REQUIRES_EXACT_ASSET_IDENTITY'}
    }
}
Write-Host '[AZTEC-W63-FLAME-REAL] ======================================================='
Write-Host '[AZTEC-W63-FLAME-REAL] REAL PC ASSETS ONLY: NEVER SUBSTITUTE FILLER FOR FLAMETABLE DEPENDENCIES'
Write-Host '[AZTEC-W63-FLAME-REAL-PROGRESS] [####----------------] 20% | source-parent and previous OAT FF hashes pinned'
$rows=@()
[long]$ownedParentBytes=0
[int]$inlineParents=0
[int]$nullParents=0
[int]$nameNonNull=0
[int]$materialNonNull=0
[int]$materialNull=0
[int]$soundInline=0
[int]$soundNull=0
[int]$soundPacked=0
$seenRoots=@{}
foreach($weapon in @($f.weapons)){
    $weaponName=[string]$weapon.nativeWeaponRootName
    $rootIndex=[int]$weapon.nativeRootOrdinal
    if([string]::IsNullOrWhiteSpace($weaponName) -or $seenRoots.ContainsKey($weaponName)){
        throw "Original-PC FlameTable root identity repeated or absent: $weaponName"
    }
    $seenRoots[$weaponName]=$true
    $parents=@($weapon.originalPcOwnedFlameParents)
    if($parents.Count -ne 2){throw "Original-PC FlameTable has no exact first/third parent slots: $weaponName"}
    $seenSlots=@{}
    foreach($parent in $parents){
        $slot=[int]$parent.fixedParentSlot
        $label=[string]$parent.name
        if($slot -notin @(2460,2464) -or $seenSlots.ContainsKey([string]$slot) -or
           $label -ne $(if($slot -eq 2460){'flameTableFirstPersonPtr'}else{'flameTableThirdPersonPtr'})){
            throw "Original-PC FlameTable fixed parent slot or label changed: $weaponName / $label"
        }
        $seenSlots[[string]$slot]=$true
        $sourceKind=Classify-SourcePointer ([string]$parent.originalPcRawParentPointer)
        $ownership=[string]$parent.ownership
        if($sourceKind -eq 'SOURCE_NULL'){
            $nullParents++
            if($ownership -ne 'NULL' -or [int]$parent.source7bOwnedParentBytes -ne 0 -or
               @($parent.originalPcMaterialPointerCells).Count -ne 0 -or
               @($parent.nestedSounds).Count -ne 0){
                throw "Naturally NULL FlameTable gained fake source children: $weaponName / $label"
            }
            continue
        }
        if($sourceKind -notlike 'SOURCE_INLINE*' -or
           $ownership -ne 'SOURCE_OWNED_476_BYTE_INLINE_PARENT' -or
           [int]$parent.source7bOwnedParentBytes -ne 476 -or
           [int]$parent.source7bOwnedParentByteOffset -lt 0x9AC -or
           [string]$parent.source7bOwnedParentSha256 -notmatch '^[0-9A-Fa-f]{64}$'){
            throw "Unverified, packed or fabricated FlameTable parent cannot enter source graph: $weaponName / $label"
        }
        $inlineParents++
        $ownedParentBytes+=476L
        $nameRaw=[string]$parent.originalPcNamePointer
        $nameKind=Classify-SourcePointer $nameRaw
        if($nameKind -ne 'SOURCE_NULL'){$nameNonNull++}
        $materials=@($parent.originalPcMaterialPointerCells)
        $sounds=@($parent.nestedSounds)
        if($materials.Count -ne 8 -or $sounds.Count -ne 4){
            throw "Original-PC 476-byte FlameTable ABI must expose 8 Material* and 4 SoundAlias* slots: $weaponName / $label"
        }
        $materialRows=@()
        $soundRows=@()
        for($i=0;$i -lt 8;$i++){
            $raw=[string]$materials[$i]
            $kind=Classify-SourcePointer $raw
            if($kind -eq 'SOURCE_NULL'){$materialNull++}else{$materialNonNull++}
            $materialRows+= [ordered]@{
                fixedParentOffset=428+4*$i
                originalPcRawPointer=$raw
                originClass=$kind
                exactMaterialAssetIdentity=$null
                nativeRealMaterialBodyVerified=$false
                action=if($kind -eq 'SOURCE_NULL'){'PRESERVE_REAL_SOURCE_NULL'}else{'FENCE_UNTIL_REAL_PC_MATERIAL_OR_VERIFIED_XBOX_STOCK_ASSET_RESOLVED'}
            }
        }
        for($i=0;$i -lt 4;$i++){
            $child=$sounds[$i]
            $raw=[string]$child.rawPointer
            $kind=Classify-SourcePointer $raw
            if([string]$child.label -ne "$label.sound[$i]" -or
               [string]$child.verdict -ne $(if($kind -eq 'SOURCE_NULL'){'NULL'}elseif($kind -like 'SOURCE_INLINE*'){'GENUINE_OWNED_INLINE_ALIAS'}else{'PACKED_EXTERNAL_ALIAS_FENCED'})){
                throw "Original-PC nested FlameTable sound pointer and typed 7b verdict disagree: $weaponName / $label / $i"
            }
            if($kind -eq 'SOURCE_NULL'){
                $soundNull++
                if($null -ne $child.source7bAliasRecordOffset){throw "NULL nested alias has a fake record: $weaponName / $label / $i"}
            }elseif($kind -like 'SOURCE_INLINE*'){
                $soundInline++
                if([int]$child.source7bAliasRecordOffset -lt 0x9AC){
                    throw "Original-PC flame sound alias lacks an owned source record offset: $weaponName / $label / $i"
                }
            }else{
                $soundPacked++
            }
            $soundRows+= [ordered]@{
                sourceLabel=[string]$child.label
                fixedParentOffset=460+4*$i
                originalPcRawPointer=$raw
                originClass=$kind
                source7bAliasRecordOffset=$child.source7bAliasRecordOffset
                actualSoundAssetSamplesVerified=$false
                action=if($kind -eq 'SOURCE_NULL'){'PRESERVE_REAL_SOURCE_NULL'}else{'REQUIRE_REAL_SOUND_ASSET_CLOSURE_BEFORE_GAMEPLAY'}
            }
        }
        $rows+= [ordered]@{
            nativeWeaponRootName=$weaponName
            nativeRootOrdinal=$rootIndex
            originalPcParentField=$label
            originalPcParentOffset=[int]$parent.source7bOwnedParentByteOffset
            originalPcParentSourceSha256=[string]$parent.source7bOwnedParentSha256
            originalPcParentSourceBytes=476
            originalPcFlameName=[ordered]@{
                rawPointer=$nameRaw
                originClass=$nameKind
                sourceOwnedExactStringSha256=$null
                action=if($nameKind -eq 'SOURCE_NULL'){'PRESERVE_REAL_SOURCE_NULL'}else{'REQUIRE_GENUINE_PC_SOURCE_STRING_BYTES_OR_AUTHENTIC_STOCK_IDENTITY'}
            }
            materialReferences=$materialRows
            soundReferences=$soundRows
            nativeParentReadyWithoutSubstitutions=($nameKind -eq 'SOURCE_NULL' -and @($materialRows | Where-Object {$_.originClass -ne 'SOURCE_NULL'}).Count -eq 0 -and @($soundRows | Where-Object {$_.originClass -notlike 'SOURCE_INLINE*' -and $_.originClass -ne 'SOURCE_NULL'}).Count -eq 0)
        }
    }
}
if($seenRoots.Count -ne 63 -or $inlineParents -ne 4 -or $nullParents -ne 122 -or
   $ownedParentBytes -ne 1904L -or
   $materialNull+$materialNonNull -ne 32 -or
   $soundInline -ne 6 -or $soundInline+$soundNull+$soundPacked -ne 16){
    throw "Real PC FlameTable parent/name/material/sound source counts invalid; roots=$($seenRoots.Count) parents=$inlineParents material=$($materialNull+$materialNonNull) sound=$($soundInline+$soundNull+$soundPacked)."
}
Write-Host '[AZTEC-W63-FLAME-REAL-PROGRESS] [##############------] 70% | 4 genuine parent bodies and 32 material/16 sound source pointer cells classified'
Write-Host "[AZTEC-W63-FLAME-REAL] SOURCE-OWNED FlameTable parents=$inlineParents, real source bytes=$ownedParentBytes, 122 parent NULL fields preserved"
Write-Host "[AZTEC-W63-FLAME-REAL] Parent names non-NULL source pointers=$nameNonNull / $inlineParents (actual source string byte closure pending)"
Write-Host "[AZTEC-W63-FLAME-REAL] Material* slots NULL=$materialNull / 32; non-NULL requiring actual asset provenance=$materialNonNull"
Write-Host "[AZTEC-W63-FLAME-REAL] SoundAlias slots: inline-real=$soundInline, NULL=$soundNull, packed=$soundPacked"
Write-Host '[AZTEC-W63-FLAME-REAL] 6 sound alias children have proven source identity, NOT sound sample/XMA audio asset closure.'
Write-Host '[AZTEC-W63-FLAME-REAL] No placeholder materials, fake images, shell-only asset substitutions or silent pointer NULLing allowed for production.'
$report=[ordered]@{
    schema='WaW360.Aztec63WeaponFlameRealAssetDependencyAudit.v1'
    status='AZTEC_REAL_PC_FLAMETABLE_PARENT_DEPENDENCIES_CLASSIFIED_NOT_NATIVE_ASSET_CLOSED'
    sourceFlameParentWitnessSha256=(Get-FileHash -LiteralPath $flameFile -Algorithm SHA256).Hash
    priorSourceOnly222RecordOatFastfileSha256=$ffSha
    priorOatFastfileBytes=4514599L
    originalPcWeaponRootSourceProofs=63
    genuineInlineFlameParentTables=$inlineParents
    naturallyNullFlameParentPointerFields=$nullParents
    authenticOriginalPcFlameParentBytes=$ownedParentBytes
    flameParentNamePointersNonNull=$nameNonNull
    realMaterialPointerSlots=32
    sourceNullMaterialPointerSlots=$materialNull
    unresolvedNonNullMaterialReferencesRequiringTrueAssets=$materialNonNull
    nestedSoundPointerSlots=16
    genuineInlineNestedSoundAliasRecords=$soundInline
    genuinelyNullNestedSoundSlots=$soundNull
    packedExternalNestedSoundPointers=$soundPacked
    allNonNullMaterialsNeedAssetIdentityAndRealBodyVerification=$true
    originalPcFlameNameStringsRequireRealSourceOwnershipProof=$true
    soundSamplesAndXmaPayloadsVerified=$false
    sourceOnlyNoNativeFastfileBuilt=$true
    productionWithoutPlaceholderAssetsAllowed=$false
    xboxHardwareReady=$false
    originalPcSourcesNotInvented=$true
    realInlineParentGraphs=$rows
}
$report|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $out -Encoding UTF8
Write-Host "[AZTEC-W63-FLAME-REAL] STATUS=$($report.status)"
Write-Host "[AZTEC-W63-FLAME-REAL] JSON=$out"
Write-Host '[AZTEC-W63-FLAME-REAL-PROGRESS] [####################] 100% | parent dependency graph audited; real asset closure still required; NO FF'
