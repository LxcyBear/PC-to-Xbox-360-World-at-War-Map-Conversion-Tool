[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# SOURCE ONLY. Prove actual 476-byte typed original-PC owned parent
# FlameTable bodies BEFORE attempting six nested SndAliasCustom records.
# Existing 4,514,599B 160-direct-alias FF and old OAT-good FF untouched.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$manifest=Join-Path $base 'pc_g11_lightning_weapon_69_unified_native_source\aztec_69_weapon_unified_original_pc_xbox_body_selections.json'
$ledger=Join-Path $base 'pc_g11_native_ff_size_audit\aztec_native_ff_size_and_source_ledger.json'
$bounce=Join-Path $base 'pc_g11_lightning_weapon63_bounce_sound\aztec_63_original_pc_bounce_sound_source_witness.json'
$directWork=Join-Path $repo '.deps\aztec-w63-direct-ff-growth'
$directFf=Join-Path $directWork 'scratch_emit\nazi_zombie_aztec.ff'
$directReport=Join-Path $directWork 'aztec_weapon63_ff_growth_63.json'
$directEmit=Join-Path $directWork 'scratch_emit\nuclear_xenon_renderer_emit.json'
$project=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_g11_lightning_weapon63_flame_parent'
$json=Join-Path $out 'aztec_63_original_pc_flame_parent_source_witness.json'
$log=Join-Path $out 'aztec_63_original_pc_flame_parent_source_witness.log'
foreach($path in @($manifest,$ledger,$bounce,$directFf,$directReport,$directEmit,$project)){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Original-PC FlameTable source witness missing independently verified input: $path"
    }
}
$d=Get-Content -LiteralPath $directReport -Raw|ConvertFrom-Json
$e=Get-Content -LiteralPath $directEmit -Raw|ConvertFrom-Json
$sha=(Get-FileHash -LiteralPath $directFf -Algorithm SHA256).Hash
if([string]$d.status -ne 'AZTEC_W63_160_REAL_PC_DIRECT_SOUND_ALIASES_NATIVE_FF_OAT_PASS_FLAME_AND_AUDIO_BLOCKED' -or
   [int]$d.nativeWeaponRootScalarXstringCanariesEmitted -ne 63 -or
   [int]$d.originalPcBounceTablesActuallyEmitted -ne 2 -or
   [int]$d.originalPcBounceAliasRecordsActuallyEmitted -ne 62 -or
   [int]$d.originalPcDirectSoundAliasesActuallyEmitted -ne 160 -or
   [int]$d.originalPcDirectSoundAliasNamesActuallyEmitted -ne 41 -or
   [long]$d.originalPcDirectSoundAliasNameBytesActuallyEmitted -ne 696 -or
   [long]$d.newScratchFastFileBytes -ne 4514599L -or
   [string]$d.newScratchFastFileSha256 -ine $sha -or
   [long](Get-Item -LiteralPath $directFf).Length -ne 4514599L -or
   [int]$e.NativeRoots -ne 8509 -or
   -not [bool]$e.ExactIdentitySequence -or
   -not [bool]$e.LayoutReplayExact -or
   -not [bool]$e.OatZeroWarningsZeroErrors -or
   -not [bool]$e.ValidationPass -or
   [bool]$e.ReadyForHardware){
    throw 'Last 4,514,599 byte 160-direct-alias OAT-good FF SHA/identity or source child evidence changed. No FlameTable promotion.'
}
New-Item -Path $out -ItemType Directory -Force|Out-Null
Write-Host '[AZTEC-W63-FLAME] ==============================================================='
Write-Host '[AZTEC-W63-FLAME] SPEEDRUN NEXT: PROVE REAL PC FlameTable PARENTS BEFORE SIX SOUND CHILDREN'
Write-Host '[AZTEC-W63-FLAME] Six nested FlameTable sound records are SOURCE-OWNED, but no parent has been promoted to native FF.'
Write-Host '[AZTEC-W63-FLAME-PROGRESS] [####----------------] 20% | 222/228 previous OAT-good FF SHA and 8509 identities pinned'
$argsList=@('run','--no-launch-profile','-c','Release','--project',$project,'--',
    'aztec-weapon63-original-pc-flame-parent-witness',
    $manifest,$ledger,$bounce,$json)
Write-Host '[AZTEC-W63-FLAME-PROGRESS] [########------------] 40% | typed 63 x 2 FlameTable fixed parent pointer fields and 7b source offsets'
$oldPref=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet @argsList 2>&1 |
        ForEach-Object {[string]$_} |
        Tee-Object -FilePath $log
    $code=[int]$LASTEXITCODE
}finally{
    $ErrorActionPreference=$oldPref
}
if($code -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $log -PathType Leaf){
        Get-Content -LiteralPath $log -Tail 50 | ForEach-Object{Write-Host "[AZTEC-W63-FLAME] $_"}
    }
    throw "Original-PC source FlameTable typed parent provenance BLOCKED (exit=$code). No FF or Xbox."
}
$r=Get-Content -LiteralPath $json -Raw|ConvertFrom-Json
if([string]$r.schema -ne 'WaW360.AztecWeapon63OriginalPcFlameParentSourceWitness.v1' -or
   [string]$r.status -ne 'AZTEC_63_WEAPON_FLAME_PARENT_ORIGINAL_PC_OWNERSHIP_PROVEN_NOT_IN_FF' -or
   [int]$r.originalPcWeaponBodiesRechecked -ne 63 -or
   [int]$r.twoFlameParentPointerSlotsPerWeapon -ne 2 -or
   [int]$r.inlineOriginalPcFlameParentTables +
       [int]$r.nullOriginalPcFlameParentFields +
       [int]$r.packedOriginalPcFlameParentFieldsFenced -ne 126 -or
   [long]$r.originalPcOwnedInlineFlameParentBytes -ne
       (476L*[long]$r.inlineOriginalPcFlameParentTables) -or
   [int]$r.originalPcOwnedNestedFlameSoundAliasRecords -ne 6 -or
   [int]$r.originalPcOwnedNestedFlameSoundAliasNames -ne 0 -or
   [long]$r.originalPcOwnedNestedFlameSoundAliasNameBytes -ne 0 -or
   @($r.weapons).Count -ne 63 -or
   -not [bool]$r.originalPcProofBodiesAllShaValidated -or
   -not [bool]$r.noDonorOrOracleBytes -or
   -not [bool]$r.parentSourcePointersMustNotBeCopiedRawIntoNativeFf -or
   -not [bool]$r.noNativeFastfileCreatedOrModified -or
   -not [bool]$r.noAudioXmaOrRuntimeClosure -or
   [bool]$r.xboxHardwareReady -or [bool]$r.gate11Closed){
    throw 'Original-PC FlameTable source-owned parent proof or six known nested sound children no longer conserves valid byte counts.'
}
Write-Host '[AZTEC-W63-FLAME-PROGRESS] [################----] 80% | source exact 476B parent pointers and six sound records verified'
Write-Host "[AZTEC-W63-FLAME] AUTHENTIC inline original-PC FlameTable parents=$($r.inlineOriginalPcFlameParentTables)"
Write-Host "[AZTEC-W63-FLAME] OWNED source bytes=$($r.originalPcOwnedInlineFlameParentBytes)"
Write-Host "[AZTEC-W63-FLAME] NULL parent fields=$($r.nullOriginalPcFlameParentFields)"
Write-Host "[AZTEC-W63-FLAME] PACKED/external FlameTable parents FENCED=$($r.packedOriginalPcFlameParentFieldsFenced)"
Write-Host "[AZTEC-W63-FLAME] Nested genuine inline aliases=6, nested inline sound names=0"
Write-Host '[AZTEC-W63-FLAME] CAUTION: material and name pointers in FlameTable parent still require separate relocation proof. No FF/OAT/Xbox.'
Write-Host "[AZTEC-W63-FLAME] JSON=$json"
Write-Host "[AZTEC-W63-FLAME] STATUS=$($r.status)"
Write-Host '[AZTEC-W63-FLAME-PROGRESS] [####################] 100% | actual source parent ownership certified; native FF unchanged'
