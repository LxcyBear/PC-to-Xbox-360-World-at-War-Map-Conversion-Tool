[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Original-PC source-owned Xbox 7b child graph: genuine inline WeaponDef
# accuracy Vec2 knot arrays via typed ZoneCode source traversal.
# READ-ONLY in respect to all original PC/retail sources and Xbox FFs.
# Does NOT authorize packing any pointer into an Xbox runtime FastFile.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$source=Join-Path $base 'pc_g11_lightning_weapon_69_unified_native_source\aztec_69_weapon_unified_original_pc_xbox_body_selections.json'
$size=Join-Path $base 'pc_g11_native_ff_size_audit\aztec_native_ff_size_and_source_ledger.json'
$project=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_g11_lightning_weapon63_accuracy_vec2'
$json=Join-Path $out 'aztec_63_original_pc_owned_accuracy_vec2_source_witness.json'
$log=Join-Path $out 'aztec_63_original_pc_owned_accuracy_vec2_source_witness.log'

foreach($file in @($source,$size,$project)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){
        throw "63-original-PC-source WeaponDef child witness prerequisite missing: $file"
    }
}
$s=Get-Content -LiteralPath $source -Raw|ConvertFrom-Json
$ledger=Get-Content -LiteralPath $size -Raw|ConvertFrom-Json
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
    throw 'Original PC 63/69 source manifests or protected good Xbox FF SHA changed. No child proof can be promoted.'
}
$good=[string]$ledger.converted.path
if(-not(Test-Path -LiteralPath $good -PathType Leaf) -or
   [long](Get-Item -LiteralPath $good).Length -ne 4510695 -or
   (Get-FileHash -LiteralPath $good -Algorithm SHA256).Hash -ine [string]$ledger.converted.sha256){
    throw 'Protected 4,510,695-byte OAT-good Xbox FF length/hash changed; original source witness fenced.'
}
New-Item -ItemType Directory -Force -Path $out|Out-Null
Write-Host '[AZTEC-W63-VEC2] ==========================================================='
Write-Host '[AZTEC-W63-VEC2] FIRST REAL NON-STRING ORIGINAL PC WEAPON OWNED GRAPH CHILD'
Write-Host '[AZTEC-W63-VEC2] 63 authentic Xbox 7b source blobs. 4 typed accuracy knot pointer slots each.'
Write-Host '[AZTEC-W63-VEC2] Source 7b walker resolves INLINE children, packed/outside children remain FENCED.'
Write-Host '[AZTEC-W63-VEC2] Read only proof; never modify native 4,513,376-byte passing FF or protected original.'
Write-Host '[AZTEC-W63-VEC2-PROGRESS] [####----------------] 20% | source SHA-256 and all 69 roots fenced'

$argsList=@('run','--no-launch-profile','-c','Release','--project',$project,'--',
    'aztec-weapon63-original-pc-accuracy-vec2-witness',$source,$size,$json)
Write-Host '[AZTEC-W63-VEC2-PROGRESS] [########------------] 40% | read original 7b owned byte streams / true 4-slot knot array traversal'
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
        Get-Content -LiteralPath $log -Tail 60|
            ForEach-Object {Write-Host "[AZTEC-W63-VEC2] $_"}
    }
    throw "Original PC WeaponDef Vec2 source traversal BLOCKED (exit=$code); no native FF was built."
}
$r=Get-Content -LiteralPath $json -Raw|ConvertFrom-Json
$statuses=@(
    'AZTEC_63_WEAPON_ORIGINAL_PC_INLINE_VEC2_CHILD_GRAPH_SOURCE_PROVEN_NOT_IN_FF',
    'AZTEC_63_WEAPON_ACCURACY_VEC2_ALL_SOURCE_ARRAYS_ABSENT_OR_PACKED_NO_INLINE_PROOF'
)
if([string]$r.schema -ne 'WaW360.Aztec63WeaponOriginalPcAccuracyVec2OwnedSourceWitness.v1' -or
   [string]$r.status -notin $statuses -or
   [int]$r.originalPcWeaponRootsWithGenuineXbox7bOwnedProof -ne 63 -or
   [int]$r.originalPcFixedAccuracyPointerFields -ne 252 -or
   @($r.weapons).Count -ne 63 -or
   [int]$r.provenInlineSourceOwnedVec2Knots * 8 -ne [long]$r.provenInlineBigEndianXenonOwnedBytes -or
   [int]$r.packedSourceArrayPointersQuarantined -lt 0 -or
   -not [bool]$r.sourcePcWeaponBodySourceProofHashesAllRevalidated -or
   -not [bool]$r.onlySourceOwnedInlineArraysQualifyForFutureRebase -or
   -not [bool]$r.noRetailOrOracleDonorBytes -or
   -not [bool]$r.noXboxFfGeneratedOrModified -or
   [bool]$r.xboxRuntimeGameplayReady -or
   [bool]$r.gate11Closed){
    throw 'Original PC WeaponDef Vec2 source proof is not an independently source-owned, non-runtime original-byte witness.'
}
Write-Host '[AZTEC-W63-VEC2-PROGRESS] [################----] 80% | original SHA-verified typed Vec2 arrays classified'
Write-Host "[AZTEC-W63-VEC2] REAL source INLINE Vec2 arrays=$($r.provenInlineSourceOwnedVec2ArrayRecords)"
Write-Host "[AZTEC-W63-VEC2] Source OWNED Vec2 knots=$($r.provenInlineSourceOwnedVec2Knots)"
Write-Host "[AZTEC-W63-VEC2] Source BIG-ENDIAN inline knot data=$($r.provenInlineBigEndianXenonOwnedBytes) B"
Write-Host "[AZTEC-W63-VEC2] Weapon roots with real source inline knots=$($r.weaponRootsWithRealInlineVec2Children)/63"
Write-Host "[AZTEC-W63-VEC2] Packed/external original PC pointer fields FENCED=$($r.packedSourceArrayPointersQuarantined)"
Write-Host "[AZTEC-W63-VEC2] NULL original PC array pointer fields=$($r.nullSourceArrayPointers)"
Write-Host "[AZTEC-W63-VEC2] STATUS=$($r.status)"
Write-Host "[AZTEC-W63-VEC2] JSON=$json"
Write-Host '[AZTEC-W63-VEC2-PROGRESS] [####################] 100% | eligible native arrays source-audited, NOT placed in any FF'
Write-Host '[AZTEC-W63-VEC2] Full 7b foreign-asset pointer graph + runtime child closure remain BLOCKED. NO Xbox.'
