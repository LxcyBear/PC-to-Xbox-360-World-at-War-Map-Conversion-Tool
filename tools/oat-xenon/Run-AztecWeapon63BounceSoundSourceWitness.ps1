[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# SOURCE ONLY: original-PC T4 Alpha 7b WeaponDef bounceSound 31-pointer
# tables and truly inline SndAliasCustom name record children.
# Exact 63 source-bodies, original PC SHA and protected Xbox FF pinned.
# No native FF, no OAT, no Xbox, no retail audio body payload copied.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$source=Join-Path $base 'pc_g11_lightning_weapon_69_unified_native_source\aztec_69_weapon_unified_original_pc_xbox_body_selections.json'
$ledgerPath=Join-Path $base 'pc_g11_native_ff_size_audit\aztec_native_ff_size_and_source_ledger.json'
$project=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_g11_lightning_weapon63_bounce_sound'
$json=Join-Path $out 'aztec_63_original_pc_bounce_sound_source_witness.json'
$log=Join-Path $out 'aztec_63_original_pc_bounce_sound_source_witness.log'
foreach($file in @($source,$ledgerPath,$project)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){
        throw "63-weapon original PC bounce sound source witness missing prerequisite: $file"
    }
}
$s=Get-Content -LiteralPath $source -Raw|ConvertFrom-Json
$ledger=Get-Content -LiteralPath $ledgerPath -Raw|ConvertFrom-Json
if([string]$s.schema -ne 'WaW360.AztecWeapon69UnifiedOriginalPcXboxBodySelection.v1' -or
   [string]$s.status -ne 'AZTEC_69_WEAPON_NATIVE_ROOTS_63_AUTHENTIC_PC_XBOX_BODIES_2_CONFLICT_4_STOCK_UNPROVEN_NOT_IN_FF' -or
   [int]$s.nativeWeaponDefRoots -ne 69 -or
   [int]$s.uniqueSourceOwnedXboxWeaponBodyMappings -ne 63 -or
   [long]$s.selectedUncompressedXboxOwnedPayloadBytes -ne 179775 -or
   [int]$s.conflictingDifferentPcSourceBodiesStillFenced -ne 2 -or
   [int]$s.offlineStockOnlyWeaponNamesNotRuntimeProven -ne 4 -or
   [bool]$s.xboxRuntimeReady -or
   [string]$ledger.schema -ne 'WaW360.AztecNativeFfSizeAndSourceCoverage.v1' -or
   [string]$ledger.converted.sha256 -ine 'F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6'){
    throw 'Original-PC 63 weapon source identity manifest or protected FF ledger changed.'
}
$good=[string]$ledger.converted.path
if(-not(Test-Path -LiteralPath $good -PathType Leaf) -or
   [long](Get-Item -LiteralPath $good).Length -ne 4510695L -or
   (Get-FileHash -LiteralPath $good -Algorithm SHA256).Hash -ine [string]$ledger.converted.sha256){
    throw 'Protected original OAT-good native FF has an unexpected size/SHA; refusing sound source promotion.'
}
New-Item -Path $out -ItemType Directory -Force|Out-Null
Write-Host '[AZTEC-W63-BOUNCE] ================================================================='
Write-Host '[AZTEC-W63-BOUNCE] 63 ORIGINAL PC SOURCE-OWNED WEAPON 7b BOUNCE SOUND CENSUS'
Write-Host '[AZTEC-W63-BOUNCE] 31 SndAliasCustom entries/table; only genuine INLINE pointer tables may qualify.'
Write-Host '[AZTEC-W63-BOUNCE] Nested sound names are NOT Xbox audio samples, and pointer words are not safe native rebase values.'
Write-Host '[AZTEC-W63-BOUNCE] This is read-only source proof; all existing OAT-pass FastFiles remain untouched.'
Write-Host '[AZTEC-W63-BOUNCE-PROGRESS] [####----------------] 20% | source root/FF SHA baseline pinned'

# Tee the .NET stdout/stderr without losing the actual native process code.
$argsList=@('run','--no-launch-profile','-c','Release','--project',$project,'--',
    'aztec-weapon63-original-pc-bounce-sound-witness',$source,$ledgerPath,$json)
Write-Host '[AZTEC-W63-BOUNCE-PROGRESS] [########------------] 40% | 7b typed table and nested sound alias traversal'
$priorError=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet @argsList 2>&1 |
        ForEach-Object {[string]$_} |
        Tee-Object -FilePath $log
    $code=[int]$LASTEXITCODE
}finally{
    $ErrorActionPreference=$priorError
}
if($code -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $log -PathType Leaf){
        Get-Content -LiteralPath $log -Tail 60 |
            ForEach-Object {Write-Host "[AZTEC-W63-BOUNCE] $_"}
    }
    throw "Original-PC bounceSound source witness failed (exit=$code); NO FF was built."
}
$r=Get-Content -LiteralPath $json -Raw|ConvertFrom-Json
$statuses=@(
    'AZTEC_63_WEAPON_ORIGINAL_PC_BOUNCE_TABLE_INLINE_OWNERSHIP_PROVEN_NOT_IN_FF',
    'AZTEC_63_WEAPON_BOUNCE_TABLES_ABSENT_OR_PACKED_NO_INLINE_PROMOTION'
)
if([string]$r.schema -ne 'WaW360.Aztec63WeaponOriginalPcBounceSoundSourceWitness.v1' -or
   [string]$r.status -notin $statuses -or
   [int]$r.originalPcWeaponRootsWithGenuineXbox7bOwnedProof -ne 63 -or
   [int]$r.nativeWeaponRoots -ne 69 -or
   [int]$r.fixedBouncePointerFieldOffset -ne 624 -or
   [int]$r.bounceTableEntryCount -ne 31 -or
   [int]$r.inlineOriginalPcBounceTables + [int]$r.packedBounceTablesQuarantined + [int]$r.nullBounceTableFields -ne 63 -or
   [long]$r.originalPcOwnedInlineBouncePointerTableBytes -ne ([long]$r.inlineOriginalPcBounceTables * 124L) -or
   [int]$r.inlineBounceEntryAliasRecords + [int]$r.packedBounceEntryAliasPointersQuarantined + [int]$r.nullBounceEntryAliasPointers -ne ([int]$r.inlineOriginalPcBounceTables * 31) -or
   @($r.weapons).Count -ne 63 -or
   -not [bool]$r.sourcePcWeaponBodySourceProofHashesAllRevalidated -or
   -not [bool]$r.noRetailOrOracleDonorBytes -or
   -not [bool]$r.source7bTypedWalkerProvidesPhysicalOffsets -or
   -not [bool]$r.sourceOwnedPointerBytesAreNotValidNativeFfRebases -or
   -not [bool]$r.noNativeFastFileGeneratedOrModified -or
   -not [bool]$r.soundAliasGraphAndRuntimeAudioStillBlocked -or
   [bool]$r.xboxRuntimeGameplayReady -or
   [bool]$r.gate11Closed){
    throw 'The genuine 63-WeaponDef bounceSound source manifest failed strict source-only classification.'
}
Write-Host '[AZTEC-W63-BOUNCE-PROGRESS] [################----] 80% | 31 pointer table and nested source name records classified'
Write-Host "[AZTEC-W63-BOUNCE] INLINE real original-PC bounce tables=$($r.inlineOriginalPcBounceTables)/63"
Write-Host "[AZTEC-W63-BOUNCE] SOURCE-OWNED inline table pointer bytes=$($r.originalPcOwnedInlineBouncePointerTableBytes)"
Write-Host "[AZTEC-W63-BOUNCE] INLINE bounce child SndAliasCustom records=$($r.originalPcBounceChildInlineAliasRecords)"
Write-Host "[AZTEC-W63-BOUNCE] INLINE bounce child sound name strings=$($r.originalPcBounceChildInlineNameStrings)"
Write-Host "[AZTEC-W63-BOUNCE] INLINE bounce child sound name bytes=$($r.originalPcBounceChildInlineNameBytes)"
Write-Host "[AZTEC-W63-BOUNCE] ALL source-owned inline sound alias records=$($r.originalPcInlineSoundAliasCustomRecordsAcrossAllOwnedWeaponFields)"
Write-Host "[AZTEC-W63-BOUNCE] ALL source-owned inline name bytes=$($r.originalPcOwnedInlineSoundAliasNameBytesAcrossAllOwnedWeaponFields)"
Write-Host "[AZTEC-W63-BOUNCE] EXTERNAL bounce pointer tables fenced=$($r.packedBounceTablesQuarantined)"
Write-Host "[AZTEC-W63-BOUNCE] STATUS=$($r.status)"
Write-Host "[AZTEC-W63-BOUNCE] JSON=$json"
Write-Host '[AZTEC-W63-BOUNCE-PROGRESS] [####################] 100% | SHA-verified original-PC table/name byte ownership; NO map FF'
Write-Host '[AZTEC-W63-BOUNCE] NEXT: typed pointer rebase+child sound-name closure BEFORE optional scratch FF/OAT. NO Xbox.'
