[CmdletBinding()]
param([ValidateSet('0','1','6','63')][int]$Limit=63,
      [switch]$ExpandedInlineXStrings,
      [switch]$OwnedAccuracyVec2,
      [switch]$OwnedBounceSound,
      [switch]$OwnedDirectSoundAliases)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Real source PC scalar + original inline XString WeaponDef roots in ONE
# separate Xbox FF. The full 7b 179,775-byte owned graphs are source
# proofs and must NOT be blindly grafted until child-pointer rebase.
# NEVER deploy the diagnostic FF to the Xbox.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$sourceReport=Join-Path $base 'pc_g11_lightning_weapon_69_unified_native_source\aztec_69_weapon_unified_original_pc_xbox_body_selections.json'
$sizeReport=Join-Path $base 'pc_g11_native_ff_size_audit\aztec_native_ff_size_and_source_ledger.json'
if($OwnedAccuracyVec2 -and (-not $ExpandedInlineXStrings -or $Limit -ne 63)){
    throw 'Authentic original-PC owned accuracy Vec2 arrays require -Limit 63 -ExpandedInlineXStrings. No unrebased packed pointer may be promoted.'
}
if($OwnedBounceSound -and (-not $OwnedAccuracyVec2 -or -not $ExpandedInlineXStrings -or $Limit -ne 63)){
    throw 'Original-PC bounceSound native graft needs -Limit 63 -ExpandedInlineXStrings -OwnedAccuracyVec2; missing source tables cannot be synthesized.'
}
if($OwnedDirectSoundAliases -and (-not $OwnedBounceSound -or -not $OwnedAccuracyVec2 -or
    -not $ExpandedInlineXStrings -or $Limit -ne 63)){
    throw 'Only the 160 verified original-PC direct sound aliases may be promoted, and only with all prior typed 63-root Vec2/bounce/source witness prerequisites.'
}
$work=Join-Path $repo $(if($OwnedDirectSoundAliases){'.deps\aztec-w63-direct-ff-growth'}elseif($OwnedBounceSound){'.deps\aztec-w63-bounce-ff-growth'}elseif($OwnedAccuracyVec2){'.deps\aztec-w63-vec2-ff-growth'}elseif($ExpandedInlineXStrings){'.deps\aztec-w63-xstr-ff-growth'}else{'.deps\aztec-w63-ff-growth'})
$emitReport=Join-Path $work 'scratch_emit\nuclear_xenon_renderer_emit.json'
$ff=Join-Path $work 'scratch_emit\nazi_zombie_aztec.ff'
$summary=Join-Path $work ('aztec_weapon63_ff_growth_'+$Limit+'.json')
$prior=Join-Path $work ('aztec_weapon63_ff_growth_'+$Limit+'.previous.json')
foreach($f in @($sourceReport,$sizeReport)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){throw "Required original-PC native source SHA baseline absent: $f"}
}
$p=Get-Content -LiteralPath $sourceReport -Raw|ConvertFrom-Json
$s=Get-Content -LiteralPath $sizeReport -Raw|ConvertFrom-Json
if([string]$p.schema -ne 'WaW360.AztecWeapon69UnifiedOriginalPcXboxBodySelection.v1' -or
   [string]$p.status -ne 'AZTEC_69_WEAPON_NATIVE_ROOTS_63_AUTHENTIC_PC_XBOX_BODIES_2_CONFLICT_4_STOCK_UNPROVEN_NOT_IN_FF' -or
   [int]$p.nativeWeaponDefRoots -ne 69 -or
   [int]$p.uniqueSourceOwnedXboxWeaponBodyMappings -ne 63 -or
   [int]$p.selectedUncompressedXboxOwnedPayloadBytes -ne 179775 -or
   [int]$p.conflictingDifferentPcSourceBodiesStillFenced -ne 2 -or
   [int]$p.offlineStockOnlyWeaponNamesNotRuntimeProven -ne 4 -or
   [bool]$p.xboxRuntimeReady -or
   [string]$s.schema -ne 'WaW360.AztecNativeFfSizeAndSourceCoverage.v1' -or
   [string]$s.converted.sha256 -ine 'F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6'){
    throw 'Native FF or original-PC 69-root WeaponDef source identity evidence changed; no speculative graft permitted.'
}
$protected=[string]$s.converted.path
if(-not(Test-Path -LiteralPath $protected -PathType Leaf) -or
   (Get-FileHash -LiteralPath $protected -Algorithm SHA256).Hash -ine [string]$s.converted.sha256){
    throw "Original protected OAT-passing 4.51MB Xbox FF changed: $protected"
}
$baselineBytes=[long](Get-Item -LiteralPath $protected).Length
if($baselineBytes -ne 4510695){throw "Protected baseline bytes changed: $baselineBytes"}
New-Item -ItemType Directory -Path $work -Force|Out-Null
if(Test-Path -LiteralPath $summary -PathType Leaf){
    Copy-Item -LiteralPath $summary -Destination $prior -Force
}
# Avoid reporting an earlier scratch FF after a preflight/build failure.
# Only remove files under this tool's OWN dedicated .deps work directory.
foreach($stale in @($ff,$emitReport)){
    if(Test-Path -LiteralPath $stale -PathType Leaf){
        Remove-Item -LiteralPath $stale -Force
    }
}
Write-Host '[AZTEC-W63-GROWTH] ==========================================================='
Write-Host '[AZTEC-W63-GROWTH] ACTUAL isolated Xbox FF size experiment from original PC data'
Write-Host "[AZTEC-W63-GROWTH] Baseline protected OAT-good FF: $baselineBytes bytes"
Write-Host "[AZTEC-W63-GROWTH] Mode=$(if($Limit -eq 0){'SAME-SETTINGS NO-WEAPON A/B CONTROL'}else{'SOURCE-OWNED WEAPON CANARY'}) ; original-PC WeaponDef diagnostic roots=$Limit/63."
Write-Host '[AZTEC-W63-GROWTH] 179775B full 7b blobs remain standalone: NOT copied into FF.'
if($ExpandedInlineXStrings){
    Write-Host '[AZTEC-W63-XSTR] Opt-in: additional source-owned typed FIXED BODY inline XStrings; packed source/foreign child pointers fenced.'
}
if($OwnedAccuracyVec2){
    Write-Host '[AZTEC-W63-VEC2-FF] SOURCE-PROVEN nested child experiment: 96 real inline vec2 arrays, 612 knots / 4896 bytes, never packed aliases.'
    Write-Host '[AZTEC-W63-VEC2-FF] Exact original typed Alpha 7b source byte offsets, order, and child SHA mandatory. No full 7b graph, not Xbox-ready.'
}
if($OwnedBounceSound){
    Write-Host '[AZTEC-W63-BOUNCE-FF] SPEEDRUN: classify all 63 genuine PC sound-table fields; emit ONLY 2 real inline bounce tables / 62 alias records / 9 genuine source names.'
    Write-Host '[AZTEC-W63-BOUNCE-FF] Missing 61 native sound tables stay NULL. This is source-authentic 7b child graph, NOT actual audio/XMA.'
}
if($OwnedDirectSoundAliases){
    Write-Host '[AZTEC-W63-DIRECT-FF] SPEEDRUN: 160 genuine original-PC DIRECT WeaponDef SndAliasCustom children + 41 true inline name XStrings (696 B).'
    Write-Host '[AZTEC-W63-DIRECT-FF] Prior OAT-good 2-table FF remains untouched. Six nested FlameTable aliases and all XMA/audio samples stay FENCED.'
}
Write-Host '[AZTEC-W63-GROWTH] Remaining 2 conflicting + 4 stock-only weapon roots remain shells.'
Write-Host '[AZTEC-W63-GROWTH-PROGRESS] [####----------------] 20% | protected FF and all 69 source-root SHA identities pinned'
$runner=Join-Path $PSScriptRoot 'Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1'
$params=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$runner,
    '-IntegrateWorld','-ProbeLevel','Full','-DpvsStaticProbe',
    '-Gate8ChildProbe','-Gate8ChildLevel','All','-Gate8AllModelProbe',
    '-Gate10StaticDrawProbe','-Gate11CellProbe','-Gate11PrimitiveProbe',
    '-Gate11LightingProbe','-Gate11ImageHeadersProbe','-Gate11ImageRootLinksProbe',
    '-Weapon63SourceCanary','-WeaponCanaryLimit',[string]$Limit)
if($ExpandedInlineXStrings){$params+=@('-Weapon63ExtendedInlineStrings')}
if($OwnedAccuracyVec2){$params+=@('-Weapon63OwnedAccuracyVec2')}
if($OwnedBounceSound){$params+=@('-Weapon63OwnedBounceSound')}
if($OwnedDirectSoundAliases){$params+=@('-Weapon63OwnedDirectSoundAliases')}
Write-Host '[AZTEC-W63-GROWTH-PROGRESS] [########------------] 40% | existing world emitter + native Stage4D/4E weapon layout and OAT'
& powershell @params
$exit=[int]$LASTEXITCODE
$newBytes=$null
$sha=$null
if(Test-Path -LiteralPath $ff -PathType Leaf){
    $newBytes=[long](Get-Item -LiteralPath $ff).Length
    $sha=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
}
# A missing candidate is not a zero-byte FF. Never claim shrinkage
# when Stage 4D/4E failed before writing anything.
$delta=if($null -ne $newBytes){[long]$newBytes-$baselineBytes}else{$null}
$e=$null
if(Test-Path -LiteralPath $emitReport -PathType Leaf){
    try{$e=Get-Content -LiteralPath $emitReport -Raw|ConvertFrom-Json}catch{}
}
$emitted=if($null -ne $e){[int]$e.OriginalPcWeaponDiagnosticRoots}else{0}
$pass=($exit -eq 0 -and $null -ne $e -and $null -ne $newBytes -and
       $newBytes -gt 0 -and
       [int]$e.NativeRoots -eq 8509 -and
       $emitted -eq $Limit -and
       [bool]$e.LayoutReplayExact -and
       [bool]$e.ExactIdentitySequence -and
       [bool]$e.OatZeroWarningsZeroErrors -and
       [bool]$e.ValidationPass -and
       [bool]$e.ValidationOnly -and
       -not [bool]$e.ReadyForHardware -and
       -not [bool]$e.OriginalPcWeaponFullOwnedGraphsEmitted -and
       (-not $OwnedAccuracyVec2 -or
        ([int]$e.OriginalPcWeaponOwnedAccuracyVec2Arrays -eq 96 -and
         [int]$e.OriginalPcWeaponOwnedAccuracyVec2Knots -eq 612 -and
         [long]$e.OriginalPcWeaponOwnedAccuracyVec2Bytes -eq 4896)) -and
       (-not $OwnedBounceSound -or
        ([int]$e.OriginalPcWeaponOwnedBounceSoundTables -eq 2 -and
         [int]$e.OriginalPcWeaponOwnedBounceAliasRecords -eq 62 -and
         [int]$e.OriginalPcWeaponOwnedBounceAliasNames -eq 9 -and
         [long]$e.OriginalPcWeaponOwnedBounceAliasNameBytes -eq 199)) -and
       (-not $OwnedDirectSoundAliases -or
        ([int]$e.OriginalPcWeaponOwnedDirectSoundAliasRecords -eq 160 -and
         [int]$e.OriginalPcWeaponOwnedDirectSoundAliasNames -eq 41 -and
         [long]$e.OriginalPcWeaponOwnedDirectSoundAliasNameBytes -eq 696)))
$summaryObj=[ordered]@{
    schema='WaW360.AztecWeapon63RealSourceScalarXstringFfGrowthCanary.v1'
    status=if($pass){
        if($OwnedDirectSoundAliases){'AZTEC_W63_160_REAL_PC_DIRECT_SOUND_ALIASES_NATIVE_FF_OAT_PASS_FLAME_AND_AUDIO_BLOCKED'}
        elseif($OwnedBounceSound){'AZTEC_W63_TWO_REAL_PC_BOUNCE_TABLES_62_ALIAS_RECORDS_NATIVE_FF_OAT_PASS_NOT_FULL_AUDIO'}
        elseif($OwnedAccuracyVec2){'AZTEC_W63_96_ORIGINAL_PC_ACCURACY_VEC2_OWNED_CHILDREN_NATIVE_FF_OAT_PASS_NOT_FULL_GRAPH'}
        elseif($ExpandedInlineXStrings){'AZTEC_W63_EXTENDED_ORIGINAL_PC_INLINE_XSTRINGS_NATIVE_FF_OAT_PASS_NOT_FULL_WEAPON_GRAPH'}
        elseif($Limit -eq 0){'AZTEC_W63_NO_WEAPON_SAME_SETTINGS_CONTROL_OAT_PASS'}
        else{'AZTEC_W63_SOURCE_SCALAR_XSTRINGS_IN_NATIVE_FF_OAT_LOAD_PASS_FULL_WEAPON_GRAPH_BLOCKED'}
    }else{'AZTEC_W63_ISOLATED_NATIVE_FF_OAT_OR_LAYOUT_BLOCKED'}
    originalPcAuthenticXboxWeaponDefBodiesSourceProven=63
    originalPcNativeOwnedProofBytesAvailable=179775
    nativeWeaponRootScalarXstringCanariesEmitted=$emitted
    extendedOriginalPcFixedInlineXStringsMode=[bool]$ExpandedInlineXStrings
    extendedSourceFixedInlineStringFieldsEmitted=if($null -ne $e){[int]$e.OriginalPcWeaponExpandedInlineXStringFields}else{0}
    originalPcAccuracyVec2OwnedChildMode=[bool]$OwnedAccuracyVec2
    originalPcOwnedBounceSoundMode=[bool]$OwnedBounceSound
    originalPcOwnedDirectSoundAliasesMode=[bool]$OwnedDirectSoundAliases
    originalPcDirectSoundAliasesActuallyEmitted=if($null -ne $e){[int]$e.OriginalPcWeaponOwnedDirectSoundAliasRecords}else{0}
    originalPcDirectSoundAliasNamesActuallyEmitted=if($null -ne $e){[int]$e.OriginalPcWeaponOwnedDirectSoundAliasNames}else{0}
    originalPcDirectSoundAliasNameBytesActuallyEmitted=if($null -ne $e){[long]$e.OriginalPcWeaponOwnedDirectSoundAliasNameBytes}else{0L}
    originalPcBounceTablesActuallyEmitted=if($null -ne $e){[int]$e.OriginalPcWeaponOwnedBounceSoundTables}else{0}
    originalPcBounceAliasRecordsActuallyEmitted=if($null -ne $e){[int]$e.OriginalPcWeaponOwnedBounceAliasRecords}else{0}
    originalPcBounceAliasNamesActuallyEmitted=if($null -ne $e){[int]$e.OriginalPcWeaponOwnedBounceAliasNames}else{0}
    originalPcBounceAliasNameBytesActuallyEmitted=if($null -ne $e){[long]$e.OriginalPcWeaponOwnedBounceAliasNameBytes}else{0L}
    originalPcAccuracyVec2OwnedArraysEmitted=if($null -ne $e){[int]$e.OriginalPcWeaponOwnedAccuracyVec2Arrays}else{0}
    originalPcAccuracyVec2OwnedKnotsEmitted=if($null -ne $e){[int]$e.OriginalPcWeaponOwnedAccuracyVec2Knots}else{0}
    originalPcAccuracyVec2OwnedPayloadBytesEmitted=if($null -ne $e){[long]$e.OriginalPcWeaponOwnedAccuracyVec2Bytes}else{0L}
    fullWeaponDefChildrenAndDependenciesEmitted=$false
    requestedCanaries=$Limit
    originalPcSourceWeaponBodyUsedForEachCanary=($Limit -gt 0)
    fixedBodyNativeScalarsOnly=($Limit -gt 0)
    originalInlineXstringsOnly=($Limit -gt 0)
    protectedPriorNativeFastFile=$protected
    protectedPriorNativeFastFileBytes=$baselineBytes
    protectedPriorSha256=[string]$s.converted.sha256
    newScratchFastFile=$ff
    newScratchFastFileBytes=$newBytes
    newScratchFastFileSha256=$sha
    deltaBytesComparedToProtectedBaseline=$delta
    growthPercentComparedToProtectedBaseline=if($null -ne $delta){[Math]::Round(100.0*$delta/$baselineBytes,3)}else{$null}
    oatLoaded8509NativeAssetIdentities=if($null -ne $e){[bool]$e.ExactIdentitySequence}else{$false}
    oatZeroWarningsErrors=if($null -ne $e){[bool]$e.OatZeroWarningsZeroErrors}else{$false}
    nativeLayoutReplayExact=if($null -ne $e){[bool]$e.LayoutReplayExact}else{$false}
    wrapperRoundTripVerified=if($null -ne $e){[bool]$e.ValidationPass}else{$false}
    sourceOwnedScalarXstringSerializedBytes=if($null -ne $e){[long]$e.OriginalPcWeaponDiagnosticSerializedBytes}else{0L}
    sourceOwnedFullProofBytesForSelectedCanaries=if($null -ne $e){[long]$e.OriginalPcWeaponSourceProofBytes}else{0L}
    previous43ConflictsResolved=0
    sixPcWeaponOriginalNamesStillDirectPointerProven=$true
    outputNativeXboxHardwareReady=$false
    fullWeaponAssetsGameplayCompatible=$false
    userMapDeployable=$false
    gate11Closed=$false
    parentRunnerExitCode=$exit
    emitterManifest=$emitReport
    note=if($OwnedDirectSoundAliases){
        'ISOLATED PC-source authentic 63-weapon FF: previous verified 2 bounce tables / 62 alias records / 9 inline names plus 160 more ORIGINAL PC typed direct WeaponDef fixed-slot alias pointer records and 41 exact source-owned inline names (696B), all by original 7b physical offset and SHA, no copied foreign pointers, six FlameTable aliases gated and all actual Xbox sound/XMA sample payloads absent. 8509 native identity / Stage4D layout / OAT acceptance is NOT runtime gameplay support.'
    }elseif($OwnedBounceSound){
        'Isolated diagnostic FF: all 63 authentic original PC WeaponDef bounce pointer fields source-censused; exactly 2 real 31-entry original-PC pointer tables, their 62 inline SndAliasCustom record children and 9 true inline name XStrings are independently SHA verified and sanitized to Xbox FOLLOWING/NULL references. The other 61 fields are legitimately NULL, not synthesized. This is NOT 63 real sound tables, not full sound asset payloads, and NOT Xbox runtime compatible.'
    }elseif($OwnedAccuracyVec2){
        'Proof-gated partial WeaponDef: 63 original PC Xbox 7b SHA-owned scalars + authentic original fixed inline XStrings + 96 genuine source-owned big-endian 2-float knot arrays (612 knots, 4896 B). Exact typed original ZoneCode order and native layout replay. All original packed aliases and foreign Model/FX/Material/XAnim/Audio child graphs remain NULL; OAT acceptance only, never Xbox-ready.'
    }elseif($ExpandedInlineXStrings){
        'Experimental isolated FF: original-PC source typed 7b owned walk identifies direct fixed-body inline XStrings in correct ZoneCode order; only ASCII-safe inline fields rebased, all unverified packed source strings and XModel/FX/Material/XAnim/audio pointers stay NULL. Full 7b graphs NOT integrated, not playable, OAT list does not establish runtime correctness.'
    }else{
        'This isolated FF includes real original-PC numeric WeaponDef scalars and original inline WeaponDef display/overlay strings, but nulls all other WeaponDef pointers. It does not contain 63 complete 7b source-owned weapon graphs, nor make the map playable. Size alone never certifies source closure.'
    }
}
$summaryObj|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $summary -Encoding UTF8
Write-Host '[AZTEC-W63-GROWTH-PROGRESS] [################----] 80% | measured new actual compressed FF size, OAT result and source-derived child bytes'
Write-Host "[AZTEC-W63-GROWTH] PROTECTED BASELINE FF = $baselineBytes bytes ($('{0:N2}' -f ($baselineBytes/1MB)) MiB)"
if($null -ne $newBytes){
    Write-Host "[AZTEC-W63-GROWTH] NEW SCRATCH FF = $newBytes bytes ($('{0:N2}' -f ($newBytes/1MB)) MiB)"
    Write-Host "[AZTEC-W63-GROWTH] MEASURED DELTA vs prior protected candidate = $delta bytes"
}else{
    Write-Host '[AZTEC-W63-GROWTH] NEW SCRATCH FF = NO CANDIDATE GENERATED'
    Write-Host '[AZTEC-W63-GROWTH] MEASURED DELTA = N/A (build failed before writing FF; no size change proven)'
}
Write-Host "[AZTEC-W63-GROWTH] Native original-PC scalar+inline XString WeaponDef roots=$emitted/$Limit; full 7b graph in FF=0"
if($OwnedAccuracyVec2){
    Write-Host "[AZTEC-W63-VEC2-FF] GENUINE original-PC inline accuracy Vec2 arrays actually emitted=$($summaryObj.originalPcAccuracyVec2OwnedArraysEmitted)"
    Write-Host "[AZTEC-W63-VEC2-FF] ORIGINAL SOURCE big-endian knots=$($summaryObj.originalPcAccuracyVec2OwnedKnotsEmitted); owned bytes=$($summaryObj.originalPcAccuracyVec2OwnedPayloadBytesEmitted)"
    $priorExtended=Join-Path $repo '.deps\aztec-w63-inline-ab\same_world_extended_63\nazi_zombie_aztec.ff'
    if(Test-Path -LiteralPath $priorExtended -PathType Leaf){
        $baseSha=(Get-FileHash -LiteralPath $priorExtended -Algorithm SHA256).Hash
        if($baseSha -ieq 'A3009316D64FE8EB61E69ABF79F33ECFC2F6ECE10A96F62AFCD17CE5C92FF226' -and $null -ne $newBytes){
            $sizeOld=[long](Get-Item -LiteralPath $priorExtended).Length
            Write-Host "[AZTEC-W63-VEC2-FF] Prior OAT-good extended-string FF=$sizeOld B; new Vec2 FF=$newBytes B; archive-relative delta=$($newBytes-$sizeOld) B"
            Write-Host '[AZTEC-W63-VEC2-FF] Different revisions: NOT an exact controlled same-source A/B until two identical-world fresh builds are paired.'
        }
    }
}
if($ExpandedInlineXStrings){
    Write-Host "[AZTEC-W63-XSTR] NEW typed source-owned fixed WeaponDef inline XString fields=$($summaryObj.extendedSourceFixedInlineStringFieldsEmitted)"
    $old63=Join-Path $repo '.deps\aztec-w63-exact-ab\weapons_63\nazi_zombie_aztec.ff'
    $oldProof=Join-Path $repo '.deps\aztec-w63-exact-ab\weapons_63\case_size_and_oat.json'
    if((Test-Path -LiteralPath $old63 -PathType Leaf) -and
       (Test-Path -LiteralPath $oldProof -PathType Leaf)){
        $old=Get-Content -LiteralPath $oldProof -Raw|ConvertFrom-Json
        $oldSha=(Get-FileHash -LiteralPath $old63 -Algorithm SHA256).Hash
        if([string]$old.status -eq 'AZTEC_W63_SOURCE_SCALAR_XSTRINGS_IN_NATIVE_FF_OAT_LOAD_PASS_FULL_WEAPON_GRAPH_BLOCKED' -and
           [int]$old.nativeWeaponRootScalarXstringCanariesEmitted -eq 63 -and
           $oldSha -ieq [string]$old.newScratchFastFileSha256 -and
           $oldSha -ieq '3DBA6CB661691065192B57D4A11A1E4DE5FA0E5FCF6937C8B86E70A57C9152D7' -and
           $null -ne $newBytes){
            $oldBytes=[long](Get-Item -LiteralPath $old63).Length
            Write-Host "[AZTEC-W63-XSTR] Historic OAT-good 63-canary FF: $oldBytes B; expanded: $newBytes B; archival-relative compressed delta: $($newBytes-$oldBytes) B"
            Write-Host '[AZTEC-W63-XSTR] Different Git revisions: archive-relative delta is not an exact same-revision A/B proof.'
        }
    }
}
if($OwnedBounceSound){
    Write-Host "[AZTEC-W63-BOUNCE-FF] SOURCE-CENSUSED ORIGINAL-PC BOUNCE POINTERS=63/63"
    Write-Host "[AZTEC-W63-BOUNCE-FF] REAL table children actually emitted=$($summaryObj.originalPcBounceTablesActuallyEmitted)"
    Write-Host "[AZTEC-W63-BOUNCE-FF] Real nested alias records=$($summaryObj.originalPcBounceAliasRecordsActuallyEmitted); name strings=$($summaryObj.originalPcBounceAliasNamesActuallyEmitted); name bytes=$($summaryObj.originalPcBounceAliasNameBytesActuallyEmitted)"
    Write-Host '[AZTEC-W63-BOUNCE-FF] Historical Vec2 FF=4514237 B; compare only as archival relative, NOT same-revision exact A/B.'
}
if($OwnedDirectSoundAliases){
    Write-Host "[AZTEC-W63-DIRECT-FF] REAL ORIGINAL-PC direct SndAliasCustom records in FF=$($summaryObj.originalPcDirectSoundAliasesActuallyEmitted)"
    Write-Host "[AZTEC-W63-DIRECT-FF] INLINE names=$($summaryObj.originalPcDirectSoundAliasNamesActuallyEmitted); source bytes=$($summaryObj.originalPcDirectSoundAliasNameBytesActuallyEmitted)"
    Write-Host '[AZTEC-W63-DIRECT-FF] Total original-PC owned alias records emitted=222/228; remaining six FlameTable nested aliases fenced.'
    $previousBounce=Join-Path $repo '.deps\aztec-w63-bounce-ff-growth\scratch_emit\nazi_zombie_aztec.ff'
    if((Test-Path -LiteralPath $previousBounce -PathType Leaf) -and $null -ne $newBytes){
        $oldSize=[long](Get-Item -LiteralPath $previousBounce).Length
        Write-Host "[AZTEC-W63-DIRECT-FF] Historical bounce FF=$oldSize B; direct-child FF=$newBytes B; archive-relative delta=$($newBytes-$oldSize) B, NOT SAME-REVISION EXACT A/B."
    }
}
Write-Host "[AZTEC-W63-GROWTH] Stage4D layout exact=$($summaryObj.nativeLayoutReplayExact); OAT 8509 identity=$($summaryObj.oatLoaded8509NativeAssetIdentities); OAT 0/0=$($summaryObj.oatZeroWarningsErrors)"
Write-Host "[AZTEC-W63-GROWTH] STATUS=$($summaryObj.status)"
Write-Host "[AZTEC-W63-GROWTH] JSON=$summary"
Write-Host "[AZTEC-W63-GROWTH] Scratch FF=$ff"
Write-Host '[AZTEC-W63-GROWTH-PROGRESS] [####################] 100% | diagnostic size and validation complete; NOT Xbox-ready'
if(-not $pass){
    throw "Weapon63 isolated FF validation BLOCKED; inspect emitter/OAT log under $work. Retry -Limit 1 to bisect a single real WeaponDef source root, not fake FF growth."
}
Write-Host '[AZTEC-W63-GROWTH] OAT scratch candidate PASS. Full WeaponDef pointer/child graphs remain the next stage.'
