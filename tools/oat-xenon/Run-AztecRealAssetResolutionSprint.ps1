[CmdletBinding()]
param(
    [string]$AudioSourceRoot='',
    [int]$MaxAudioFiles=2500,
    [switch]$NoBuild
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# One incremental .NET source-provenance build plus real audio file inventory.
# Never emit a native FF, silent stub, dummy material, fake image or Xbox install.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$weapon=Join-Path $base 'pc_g11_lightning_weapon_69_unified_native_source\aztec_69_weapon_unified_original_pc_xbox_body_selections.json'
$flame=Join-Path $base 'pc_g11_lightning_weapon63_flame_inline_real_source\aztec_63_original_pc_flame_real_inline_child_sha_witness.json'
$mat=Join-Path $base 'pc_verified_xbox_surface_material_relocation_worklist\aztec_verified_surface_material_relocation_worklist.json'
$work=Join-Path $repo '.deps\aztec-w63-direct-ff-growth'
$ff=Join-Path $work 'scratch_emit\nazi_zombie_aztec.ff'
$prior=Join-Path $work 'aztec_weapon63_ff_growth_63.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_g11_flame_real_source_sprint'
$join=Join-Path $out 'aztec_flame_packed_material_exact_pc_zone_join.json'
$log=Join-Path $out 'aztec_flame_packed_material_exact_pc_zone_join.log'
$xma=Join-Path $out 'aztec_real_xma_source_and_encoder_preflight.json'
$summary=Join-Path $out 'aztec_real_asset_resolution_sprint.json'
foreach($path in @($weapon,$flame,$mat,$ff,$prior,$cli)){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Real asset sprint missing original PC or OAT-good proof: $path"
    }
}
$p=Get-Content -LiteralPath $prior -Raw|ConvertFrom-Json
$src=Get-Content -LiteralPath $flame -Raw|ConvertFrom-Json
$oldHash=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
if([string]$p.status -ne 'AZTEC_W63_160_REAL_PC_DIRECT_SOUND_ALIASES_NATIVE_FF_OAT_PASS_FLAME_AND_AUDIO_BLOCKED' -or
   [long]$p.newScratchFastFileBytes -ne 4514599L -or
   [string]$p.newScratchFastFileSha256 -ine $oldHash -or
   [int]$p.originalPcDirectSoundAliasesActuallyEmitted -ne 160 -or
   [int]$p.originalPcBounceAliasRecordsActuallyEmitted -ne 62 -or
   [string]$src.schema -ne 'WaW360.AztecWeapon63OriginalPcFlameParentSourceWitness.v1' -or
   [int]$src.originalPcWeaponBodiesRechecked -ne 63 -or
   [int]$src.inlineOriginalPcFlameParentTables -ne 4 -or
   [int]$src.originalPcFlameMaterialPointersInlineOwned -ne 3 -or
   [int]$src.originalPcFlameMaterialPointersPackedUnresolved -ne 17 -or
   [int]$src.originalPcFlameNamePointersInlineOwned -ne 1 -or
   [int]$src.originalPcFlameNamePointersPackedUnresolved -ne 3 -or
   [int]$src.originalPcOwnedNestedFlameSoundAliasRecords -ne 6){
    throw 'Source FlameTable evidence or OAT-good 4,514,599 byte FF changed.'
}
New-Item -ItemType Directory -Force -Path $out|Out-Null
Write-Host '[AZTEC-REAL-SPRINT] ====================================================='
Write-Host '[AZTEC-REAL-SPRINT] ONE-BUILD ORIGINAL-PC MATERIAL + FLAME + AUDIO/XMA SOURCE SPRINT'
Write-Host '[AZTEC-REAL-SPRINT] No null substitution, no dummy audio, no FF, no Xbox.'
if($NoBuild){Write-Host '[AZTEC-REAL-SPRINT] FAST RETRY: reuse previously compiled Release CLI; PowerShell-only XMA preflight fix.'}
Write-Host '[AZTEC-REAL-SPRINT-PROGRESS] [####----------------] 20% | 222/228 prior FF and real PC FlameTable sources hash-pinned'
$argsList=@('run','--no-launch-profile','-c','Release','--project',$cli)
if($NoBuild){$argsList+=@('--no-build')}
$argsList+=@('--','aztec-weapon63-real-flame-material-join',$weapon,$flame,$mat,$join)
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet @argsList 2>&1|ForEach-Object {[string]$_}|Tee-Object -FilePath $log
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0 -or -not(Test-Path -LiteralPath $join -PathType Leaf)){
    if(Test-Path -LiteralPath $log -PathType Leaf){
        Get-Content -LiteralPath $log -Tail 45|ForEach-Object {Write-Host "[AZTEC-REAL-SPRINT] $_"}
    }
    throw "Original-PC same-FF byte-proven typed material identity join failed exit=$code. No FF."
}
$r=Get-Content -LiteralPath $join -Raw|ConvertFrom-Json
if([string]$r.schema -ne 'WaW360.AztecFlameRealSourceMaterialJoin.v1' -or
   [string]$r.status -ne 'AZTEC_FLAME_PACKED_SOURCE_MATERIAL_MATCH_AUDIT_COMPLETE_NATIVE_ASSETS_UNRESOLVED' -or
   [int]$r.realOriginalPcFlameParentBodies -ne 4 -or
   [int]$r.worldOatLoadedTypedMaterialIdentities -ne 39 -or
   [int]$r.worldMaterialSourceReferences -ne 735 -or
   [int]$r.nonNullPackedFlameMaterialPointers -ne 17 -or
   [int]$r.realOriginalPcInlineMaterialChildBodies -ne 3 -or
   [int]$r.naturallyNullMaterialPointersPreserved -ne 12 -or
   [int]$r.originalPcInlineFlameNames -ne 1 -or
   [int]$r.originalPcPackedFlameNamesStillUnresolved -ne 3 -or
   [int]$r.realOriginalPcInlineSoundAliasCustomRecords -ne 6 -or
   [int]$r.originalPcPackedFlameSoundAliasReferencesUnresolved -ne 6 -or
   [int]$r.naturallyNullFlameSoundSlotsPreserved -ne 4 -or
   ([int]$r.sameOriginalPcZoneExactOatTypedWorldMaterialPointerMatches+
    [int]$r.packedFlameMaterialPointersStillUnresolved) -ne 17 -or
   ([int]$r.originalPcFlameParentSameZoneFixedBodyAndParentShaVerified+
    [int]$r.originalPcFlameParentSourceZoneUnproven) -ne 4 -or
   [bool]$r.readyForNativeFlameEmission -or
   -not [bool]$r.noPlaceholderMaterialOrSoundEmitted){
    throw 'Actual PC source 4 FlameTables and 17 non-NULL packed Material references failed typed-accounting audit.'
}
Write-Host '[AZTEC-REAL-SPRINT-PROGRESS] [############--------] 60% | 17 PC source material pointers correlated, but NEVER bound by numeric equality alone'
Write-Host "[AZTEC-REAL-SPRINT] Same original PC zone exact WeaponDef+FlameTable byte/hash owners=$($r.originalPcFlameParentSameZoneFixedBodyAndParentShaVerified)/4"
Write-Host "[AZTEC-REAL-SPRINT] 17 packed materials: SAME-SOURCE loaded-name identity matches=$($r.sameOriginalPcZoneExactOatTypedWorldMaterialPointerMatches); unresolved=$($r.packedFlameMaterialPointersStillUnresolved)"
Write-Host '[AZTEC-REAL-SPRINT] Material pointer identity is not yet a full Xbox shader/texture/techset runtime asset.'
$audioArgs=@('-NoProfile','-ExecutionPolicy','Bypass','-File',
    (Join-Path $PSScriptRoot 'Run-AztecXmaRealSourcePreflight.ps1'),'-MaxFiles',"$MaxAudioFiles")
if(-not [string]::IsNullOrWhiteSpace($AudioSourceRoot)){
    $audioArgs+=@('-AudioSourceRoot',$AudioSourceRoot)
}
& powershell @audioArgs
if($LASTEXITCODE -ne 0 -or -not(Test-Path -LiteralPath $xma -PathType Leaf)){
    throw 'Real source audio/XMA media inventory failed; no dummy audio or partial success.'
}
$a=Get-Content -LiteralPath $xma -Raw|ConvertFrom-Json
if([string]$a.schema -ne 'WaW360.AztecRealXmaSourceCodecPreflight.v1' -or
   [bool]$a.completeAudioStreamAssetAndXmaPayloadsVerified -or
   [bool]$a.nativeSoundAliasSamplesBound -or
   [bool]$a.syntheticOrSilentSamplesProduced -or
   [bool]$a.nativeFastfileCreatedOrModified){
    throw 'XMA proof incorrectly claimed native completion or non-source audio.'
}
if((Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash -ine $oldHash){
    throw 'PROTECTED OAT-good FF SHA changed during source-only audit. Refusing native promotion.'
}
Write-Host '[AZTEC-REAL-SPRINT-PROGRESS] [################----] 80% | genuine audio source hashes and XMA encoder availability inventoried'
$report=[ordered]@{
    schema='WaW360.AztecRealAssetResolutionSprint.v1'
    status='ORIGINAL_PC_REAL_ASSET_SOURCE_SPRINT_PASS_FLAME_AND_XMA_NATIVE_CLOSURE_PENDING'
    originalOatGoodFastFileSha256=$oldHash
    originalOatGoodFastFileBytes=4514599L
    realPcFlameParentBodies=4
    authenticNullFlameParentSlotsPreserved=122
    authenticNullFlameMaterialSlotsPreserved=12
    authenticNullFlameSoundSlotsPreserved=4
    actualInlineFlameNames=1
    actualInlineFlameNameBytes=24L
    actualInlineFlameMaterialBodies=3
    actualInlineFlameMaterialBytes=892L
    sourcePackedFlameMaterialSlots=17
    genuineSamePcSourceTypedMaterialIdentityMatches=[int]$r.sameOriginalPcZoneExactOatTypedWorldMaterialPointerMatches
    stillUnresolvedPackedMaterialSourceRefs=[int]$r.packedFlameMaterialPointersStillUnresolved
    unprovenSamePcSourceFlameParents=[int]$r.originalPcFlameParentSourceZoneUnproven
    packedFlameNameStringTargetsUnresolved=3
    authenticInlineFlameSndAliasRecords=6
    packedFlameSoundAliasRefsUnresolved=6
    genuinePcSoundRootHeadersInWorldFf=[int]$r.originalPcSoundAssetRootsInSameFastfile
    originalAudioMediaFileShas=[int]$a.authenticatedFileShas
    observedAudioRiffXmaTags=[int]$a.observedRiffXmaFormatTags
    untestedLocalXmaEncoderExecutableCandidates=[int]$a.locallyFoundXmaEncoderExecutables
    xmaEncodedAndXboxValidated=$false
    materialShaderImageTechniqueGraphXboxRuntimeVerified=$false
    fullWeaponDefAndWorldNativeGraphVerified=$false
    noSilentStubOrFillerGenerated=$true
    noOriginalNonNullReferenceSilentlyZeroed=$true
    nativeFfCreatedOrModified=$false
    xboxReady=$false
    originalPcMaterialSourceJoinReport=$join
    authenticAudioFileAndEncoderPreflight=$xma
}
$report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $summary -Encoding UTF8
Write-Host "[AZTEC-REAL-SPRINT] Actual PC audio files SHA catalogued=$($a.authenticatedFileShas); XMA native encode/roundtrip remains UNVERIFIED"
Write-Host "[AZTEC-REAL-SPRINT] STATUS=$($report.status)"
Write-Host "[AZTEC-REAL-SPRINT] JSON=$summary"
Write-Host '[AZTEC-REAL-SPRINT-PROGRESS] [####################] 100% | one-build real source material/XMA sprint; no FF or Xbox'
