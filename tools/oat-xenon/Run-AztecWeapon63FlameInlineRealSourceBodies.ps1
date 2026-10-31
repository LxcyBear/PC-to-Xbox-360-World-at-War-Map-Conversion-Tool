[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# Source forensic only: all genuinely inline PC bytes retain physical SHA,
# all non-inline source material/name references remain unresolved.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$manifest=Join-Path $base 'pc_g11_lightning_weapon_69_unified_native_source\aztec_69_weapon_unified_original_pc_xbox_body_selections.json'
$ledger=Join-Path $base 'pc_g11_native_ff_size_audit\aztec_native_ff_size_and_source_ledger.json'
$bounce=Join-Path $base 'pc_g11_lightning_weapon63_bounce_sound\aztec_63_original_pc_bounce_sound_source_witness.json'
$oldFile=Join-Path $base 'pc_g11_lightning_weapon63_flame_parent\aztec_63_original_pc_flame_parent_source_witness.json'
$auditFile=Join-Path $base 'pc_g11_lightning_weapon63_flame_parent\aztec_63_original_pc_flame_real_asset_dependency_audit.json'
$work=Join-Path $repo '.deps\aztec-w63-direct-ff-growth'
$ff=Join-Path $work 'scratch_emit\nazi_zombie_aztec.ff'
$priorFF=Join-Path $work 'aztec_weapon63_ff_growth_63.json'
$out=Join-Path $base 'pc_g11_lightning_weapon63_flame_inline_real_source'
$json=Join-Path $out 'aztec_63_original_pc_flame_real_inline_child_sha_witness.json'
$log=Join-Path $out 'aztec_63_original_pc_flame_real_inline_child_sha_witness.log'
$project=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
foreach($f in @($manifest,$ledger,$bounce,$oldFile,$auditFile,$ff,$priorFF,$project)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){throw "Required original PC source or prior OAT-good FF absent: $f"}
}
$old=Get-Content -LiteralPath $oldFile -Raw|ConvertFrom-Json
$a=Get-Content -LiteralPath $auditFile -Raw|ConvertFrom-Json
$d=Get-Content -LiteralPath $priorFF -Raw|ConvertFrom-Json
$sha=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
if([int]$old.inlineOriginalPcFlameParentTables -ne 4 -or
   [string]$a.sourceFlameParentWitnessSha256 -ine (Get-FileHash -LiteralPath $oldFile -Algorithm SHA256).Hash -or
   [int]$a.unresolvedNonNullMaterialReferencesRequiringTrueAssets -ne 20 -or
   [string]$d.status -ne 'AZTEC_W63_160_REAL_PC_DIRECT_SOUND_ALIASES_NATIVE_FF_OAT_PASS_FLAME_AND_AUDIO_BLOCKED' -or
   [string]$d.newScratchFastFileSha256 -ine $sha -or
   [long](Get-Item -LiteralPath $ff).Length -ne 4514599L){
    throw 'Original source 4-parent/20-material dependency evidence or OAT FF SHA changed.'
}
New-Item -ItemType Directory -Force -Path $out|Out-Null
Write-Host '[AZTEC-FLAME-REAL] Source-only 7b name/material child SHA witness; old FF protected'
Write-Host '[AZTEC-FLAME-REAL-PROGRESS] [####----------------] 20% | hash prerequisites'
$cmd=@('run','--no-launch-profile','-c','Release','--project',$project,'--',
    'aztec-weapon63-original-pc-flame-parent-witness',$manifest,$ledger,$bounce,$json)
$oldPref=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet @cmd 2>&1|ForEach-Object{[string]$_}|Tee-Object -FilePath $log
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$oldPref}
if($exit -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    throw "Typed original-PC 7b inline FlameTable child witness failed exit=$exit; no FF created."
}
$r=Get-Content -LiteralPath $json -Raw|ConvertFrom-Json
if([string]$r.schema -ne 'WaW360.AztecWeapon63OriginalPcFlameParentSourceWitness.v1' -or
   [int]$r.originalPcWeaponBodiesRechecked -ne 63 -or
   [int]$r.inlineOriginalPcFlameParentTables -ne 4 -or
   [int]$r.originalPcFlameNamePointersInlineOwned+[int]$r.originalPcFlameNamePointersPackedUnresolved -ne 4 -or
   [int]$r.originalPcFlameMaterialPointersInlineOwned+[int]$r.originalPcFlameMaterialPointersPackedUnresolved -ne 20 -or
   [int]$r.originalPcFlameMaterialPointersAuthenticallyNull -ne 12 -or
   -not [bool]$r.originalPcNameMaterialInlineSourceChildrenShaVerified){
    throw 'Original PC typed FlameTable name/material evidence failed exact source counts.'
}
$parents=@($r.weapons|ForEach-Object{@($_.originalPcOwnedFlameParents)}|Where-Object{$_.ownership -eq 'SOURCE_OWNED_476_BYTE_INLINE_PARENT'})
if($parents.Count -ne 4 -or
   @($parents|Where-Object{@($_.originalPcNameAndMaterialTypedChildren).Count -ne 9}).Count -ne 0){
    throw 'Original PC FlameTable parent/typed child source graph no longer exact.'
}
Write-Host '[AZTEC-FLAME-REAL-PROGRESS] [################----] 80% | 4 x 9 typed source child pointers'
Write-Host "[AZTEC-FLAME-REAL] Name INLINE=$($r.originalPcFlameNamePointersInlineOwned), SHA-owned bytes=$($r.originalPcGenuineInlineFlameNameBytes), PACKED unresolved=$($r.originalPcFlameNamePointersPackedUnresolved)"
Write-Host "[AZTEC-FLAME-REAL] Material INLINE=$($r.originalPcFlameMaterialPointersInlineOwned), SHA-owned bytes=$($r.originalPcGenuineInlineFlameMaterial7bSerializedBytes), PACKED unresolved=$($r.originalPcFlameMaterialPointersPackedUnresolved), source-NULL=12"
Write-Host '[AZTEC-FLAME-REAL] Sound samples and material native bindings NOT proven; NO FF/OAT/Xbox.'
Write-Host "[AZTEC-FLAME-REAL] JSON=$json"
Write-Host '[AZTEC-FLAME-REAL-PROGRESS] [####################] 100% | source-only'
