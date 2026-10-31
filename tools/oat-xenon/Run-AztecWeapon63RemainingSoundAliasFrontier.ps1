[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Read-only speedrun gate: classify the ORIGINAL PC-owned 228 typed
# SndAliasCustom records into 62 already-grafted bounce entries,
# directly attachable WeaponDef slots, and nested/foreign children
# still needing parent graph closure. Never pad missing sound tables.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$source=Join-Path $base 'pc_g11_lightning_weapon63_bounce_sound\aztec_63_original_pc_bounce_sound_source_witness.json'
$work=Join-Path $repo '.deps\aztec-w63-bounce-ff-growth'
$growth=Join-Path $work 'aztec_weapon63_ff_growth_63.json'
$emit=Join-Path $work 'scratch_emit\nuclear_xenon_renderer_emit.json'
$ff=Join-Path $work 'scratch_emit\nazi_zombie_aztec.ff'
$out=Join-Path $work 'aztec_weapon63_remaining_real_sound_alias_frontier.json'
foreach($p in @($source,$growth,$emit,$ff)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Original-PC next sound frontier missing verified prerequisite: $p"}
}
Write-Host '[AZTEC-W63-SOUND-NEXT] ================================================================='
Write-Host '[AZTEC-W63-SOUND-NEXT] SOURCE-ONLY REMAINING 166 GENUINE INLINE SOUND ALIAS RECORD AUDIT'
Write-Host '[AZTEC-W63-SOUND-NEXT-PROGRESS] [####----------------] 20% | recheck source, native FF SHA and OAT manifest'
$s=Get-Content -LiteralPath $source -Raw|ConvertFrom-Json
$g=Get-Content -LiteralPath $growth -Raw|ConvertFrom-Json
$e=Get-Content -LiteralPath $emit -Raw|ConvertFrom-Json
$sha=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
$sourceSha=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
if([string]$s.schema -ne 'WaW360.Aztec63WeaponOriginalPcBounceSoundSourceWitness.v1' -or
   [string]$s.status -ne 'AZTEC_63_WEAPON_ORIGINAL_PC_BOUNCE_TABLE_INLINE_OWNERSHIP_PROVEN_NOT_IN_FF' -or
   [int]$s.originalPcWeaponRootsWithGenuineXbox7bOwnedProof -ne 63 -or
   [int]$s.inlineOriginalPcBounceTables -ne 2 -or
   [int]$s.nullBounceTableFields -ne 61 -or
   [int]$s.packedBounceTablesQuarantined -ne 0 -or
   [int]$s.originalPcInlineSoundAliasCustomRecordsAcrossAllOwnedWeaponFields -ne 228 -or
   [long]$s.originalPcOwnedInlineSoundAliasNameBytesAcrossAllOwnedWeaponFields -ne 895 -or
   [int]$s.originalPcBounceChildInlineAliasRecords -ne 62 -or
   [long]$s.originalPcBounceChildInlineNameBytes -ne 199 -or
   -not [bool]$s.sourcePcWeaponBodySourceProofHashesAllRevalidated -or
   -not [bool]$s.noRetailOrOracleDonorBytes -or
   [bool]$s.xboxRuntimeGameplayReady -or
   [string]$g.status -ne 'AZTEC_W63_TWO_REAL_PC_BOUNCE_TABLES_62_ALIAS_RECORDS_NATIVE_FF_OAT_PASS_NOT_FULL_AUDIO' -or
   [int]$g.nativeWeaponRootScalarXstringCanariesEmitted -ne 63 -or
   [int]$g.originalPcBounceTablesActuallyEmitted -ne 2 -or
   [int]$g.originalPcBounceAliasRecordsActuallyEmitted -ne 62 -or
   [int]$g.originalPcBounceAliasNamesActuallyEmitted -ne 9 -or
   [long]$g.originalPcBounceAliasNameBytesActuallyEmitted -ne 199 -or
   [string]$g.newScratchFastFileSha256 -ine $sha -or
   [long]$g.newScratchFastFileBytes -ne (Get-Item -LiteralPath $ff).Length -or
   [int]$e.NativeRoots -ne 8509 -or
   -not [bool]$e.ExactIdentitySequence -or
   -not [bool]$e.OatZeroWarningsZeroErrors -or
   -not [bool]$e.LayoutReplayExact -or
   -not [bool]$e.ValidationPass -or
   [bool]$e.ReadyForHardware){
    throw 'Authentic PC owned alias graph or prior OAT accepted 2-table FF changed; no next-child approval.'
}
$weapons=@($s.weapons)
if($weapons.Count -ne 63){throw '63 original PC weapon source roots are mandatory.'}
Write-Host '[AZTEC-W63-SOUND-NEXT-PROGRESS] [##########----------] 50% | typed origin label, pointer and nested-parent ownership classifications'
$directLabels=@('projExplosionSound','projDudSound','mortarShellSound',
    'tankShellSound','projIgnitionSound')
$totals=@{}
foreach($category in @('BOUNCE_ALREADY_GRAFTED','DIRECT_WEAPON_FIXED_SLOT',
                       'NESTED_FLAME_PARENT_NOT_GRAFTED',
                       'OTHER_FOREIGN_PARENT_FENCED')){
    $totals[$category]=[ordered]@{records=0;inlineNames=0;inlineNameBytes=0L}
}
$rows=@()
foreach($weapon in $weapons){
    $root=[string]$weapon.nativeWeaponRootName
    $entries=@($weapon.originalPcInlineSoundAliasRecords)
    $labels=@{}
    foreach($a in $entries){
        $label=[string]$a.label
        if([string]::IsNullOrWhiteSpace($label) -or $labels.ContainsKey($label)){
            throw "Source alias record label missing/duplicate: $root"
        }
        $labels[$label]=$true
        $offset=[int]$a.source7bSoundAliasCustomRecordOffset
        $bytes=[int]$a.originalPcOwnedInlineNameBytes
        $field=[string]$a.bigEndianFourByteSoundNamePointer
        if($offset -lt 0x9AC -or $bytes -lt 0 -or
           $field -notmatch '^0x[0-9A-Fa-f]{8}$'){
            throw "Source typed SndAliasCustom offset or pointer invalid: $root / $label"
        }
        if($bytes -gt 0 -and
           ([string]$a.namePointerClassification -ne 'ORIGINAL_PC_INLINE_SOUND_NAME' -or
            [string]::IsNullOrWhiteSpace([string]$a.originalPcOwnedInlineNameSha256))){
            throw "Original-PC inline sound name SHA/ownership missing: $root / $label"
        }
        $category='OTHER_FOREIGN_PARENT_FENCED'
        if($label -match '^bounceSound\[\d+\]$'){
            $category='BOUNCE_ALREADY_GRAFTED'
        }elseif($label -match '^sound@0x([0-9A-Fa-f]+)$'){
            $f=[Convert]::ToInt32($Matches[1],16)
            if($f -ge 388 -and $f -le 620 -and ($f % 4) -eq 0){
                $category='DIRECT_WEAPON_FIXED_SLOT'
            }
        }elseif($directLabels -contains $label){
            $category='DIRECT_WEAPON_FIXED_SLOT'
        }elseif($label -match '^flameTable'){
            $category='NESTED_FLAME_PARENT_NOT_GRAFTED'
        }
        $totals[$category].records=[int]$totals[$category].records+1
        if($bytes -gt 0){
            $totals[$category].inlineNames=[int]$totals[$category].inlineNames+1
            $totals[$category].inlineNameBytes=[long]$totals[$category].inlineNameBytes+$bytes
        }
        $rows+= [pscustomobject]@{
            nativeWeaponRootName=$root
            proofLabel=$label
            category=$category
            originalPcRecordOffset=$offset
            originalPcOwnedNameBytes=$bytes
            originalPcOwnedNameSha256=[string]$a.originalPcOwnedInlineNameSha256
            soundAssetPayloadOwned=$false
        }
    }
}
$allRecords=@($rows).Count
# Windows PowerShell 5.1 treats OrderedDictionary keys as items, not
# Measure-Object properties. Sum the verified typed counters explicitly.
# Use the key indexer to avoid PSObject property-adapter differences.
[long]$allNameBytes=0
[int]$allNames=0
[int]$allCategoryRecords=0
foreach($categoryName in @('BOUNCE_ALREADY_GRAFTED','DIRECT_WEAPON_FIXED_SLOT',
                            'NESTED_FLAME_PARENT_NOT_GRAFTED','OTHER_FOREIGN_PARENT_FENCED')){
    $categoryTotals=$totals[$categoryName]
    if($null -eq $categoryTotals){throw "Missing source ownership category $categoryName"}
    $allNameBytes+=[long]$categoryTotals['inlineNameBytes']
    $allNames+=[int]$categoryTotals['inlineNames']
    $allCategoryRecords+=[int]$categoryTotals['records']
}
if($allCategoryRecords -ne $allRecords){
    throw "Source alias category accounting mismatch: categories=$allCategoryRecords, records=$allRecords"
}
$bounce=$totals['BOUNCE_ALREADY_GRAFTED']
$direct=$totals['DIRECT_WEAPON_FIXED_SLOT']
$flame=$totals['NESTED_FLAME_PARENT_NOT_GRAFTED']
$other=$totals['OTHER_FOREIGN_PARENT_FENCED']
if($allRecords -ne 228 -or $allNameBytes -ne 895L -or
   [int]$bounce.records -ne 62 -or [long]$bounce.inlineNameBytes -ne 199L -or
   ([int]$direct.records+[int]$flame.records+[int]$other.records) -ne 166 -or
   ([long]$direct.inlineNameBytes+[long]$flame.inlineNameBytes+
       [long]$other.inlineNameBytes) -ne 696L){
    throw 'Original-PC 7b 63-weapon source alias classification/byte conservation failed.'
}
Write-Host '[AZTEC-W63-SOUND-NEXT-PROGRESS] [################----] 80% | exact 228 total original source records and 895 name bytes conserved'
Write-Host "[AZTEC-W63-SOUND-NEXT] VERIFIED SOURCE 7b ALIAS RECORDS TOTAL=$allRecords, name bytes=$allNameBytes"
Write-Host "[AZTEC-W63-SOUND-NEXT] Already OAT-pass bounce: records=$($bounce.records), names=$($bounce.inlineNames), bytes=$($bounce.inlineNameBytes)"
Write-Host "[AZTEC-W63-SOUND-NEXT] DIRECT next eligible fixed fields: records=$($direct.records), names=$($direct.inlineNames), bytes=$($direct.inlineNameBytes)"
Write-Host "[AZTEC-W63-SOUND-NEXT] Nested flame parent FENCED: records=$($flame.records), names=$($flame.inlineNames), bytes=$($flame.inlineNameBytes)"
Write-Host "[AZTEC-W63-SOUND-NEXT] Other foreign parent FENCED: records=$($other.records), names=$($other.inlineNames), bytes=$($other.inlineNameBytes)"
Write-Host '[AZTEC-W63-SOUND-NEXT] No original PC donor alias/XMA samples promoted. No new FF or Xbox install.'
$result=[ordered]@{
    schema='WaW360.AztecWeapon63RemainingSoundAliasFrontier.v1'
    status='AZTEC_63_WEAPON_SOURCE_7B_SOUND_ALIAS_ORIGIN_CLASSIFIED_NOT_YET_IN_FF'
    originalPc63WeaponBounceSourceWitnessSha256=$sourceSha
    latestOatGoodBounceFastfileSha256=$sha
    latestOatGoodBounceFastfileBytes=[long](Get-Item -LiteralPath $ff).Length
    nativeWeaponRootSourceProofs=63
    sourceOnlyInlineSoundAliasRecords=$allRecords
    sourceOnlyInlineSoundAliasNameStrings=$allNames
    sourceOnlyInlineSoundAliasNameBytes=$allNameBytes
    alreadyGraftedBounce=$bounce
    remainingDirectWeaponFixedSlots=$direct
    remainingNestedFlameParentBlocked=$flame
    remainingOtherForeignParentBlocked=$other
    all228RecordsConserved=$true
    noDonorAudioOrInventedTables=$true
    producedAnyNewNativeFf=$false
    sourceOnlyNoXboxHardware=$true
    perSourceOwnedRecord=$rows
}
$result|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $out -Encoding UTF8
Write-Host "[AZTEC-W63-SOUND-NEXT] JSON=$out"
Write-Host '[AZTEC-W63-SOUND-NEXT-PROGRESS] [####################] 100% | next safest original-PC sound-graph branch isolated; NO FF'
