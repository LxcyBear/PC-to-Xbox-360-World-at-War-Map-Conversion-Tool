[CmdletBinding()]
param([ValidateSet('1','6','63')][int]$Limit=63)
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
$work=Join-Path $repo '.deps\aztec-w63-ff-growth'
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
Write-Host "[AZTEC-W63-GROWTH] Try $Limit / 63 real source-owned WeaponDef scalar/inline XString canaries."
Write-Host '[AZTEC-W63-GROWTH] 179775B full 7b blobs remain standalone: NOT copied into FF.'
Write-Host '[AZTEC-W63-GROWTH] Remaining 2 conflicting + 4 stock-only weapon roots remain shells.'
Write-Host '[AZTEC-W63-GROWTH-PROGRESS] [####----------------] 20% | protected FF and all 69 source-root SHA identities pinned'
$runner=Join-Path $PSScriptRoot 'Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1'
$params=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$runner,
    '-IntegrateWorld','-ProbeLevel','Full','-DpvsStaticProbe',
    '-Gate8ChildProbe','-Gate8ChildLevel','All','-Gate8AllModelProbe',
    '-Gate10StaticDrawProbe','-Gate11CellProbe','-Gate11PrimitiveProbe',
    '-Gate11LightingProbe','-Gate11ImageHeadersProbe','-Gate11ImageRootLinksProbe',
    '-Weapon63SourceCanary','-WeaponCanaryLimit',[string]$Limit)
Write-Host '[AZTEC-W63-GROWTH-PROGRESS] [########------------] 40% | existing world emitter + native Stage4D/4E weapon layout and OAT'
& powershell @params
$exit=[int]$LASTEXITCODE
$newBytes=0L
$sha=$null
if(Test-Path -LiteralPath $ff -PathType Leaf){
    $newBytes=[long](Get-Item -LiteralPath $ff).Length
    $sha=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
}
$delta=$newBytes-$baselineBytes
$e=$null
if(Test-Path -LiteralPath $emitReport -PathType Leaf){
    try{$e=Get-Content -LiteralPath $emitReport -Raw|ConvertFrom-Json}catch{}
}
$emitted=if($null -ne $e){[int]$e.OriginalPcWeaponDiagnosticRoots}else{0}
$pass=($exit -eq 0 -and $null -ne $e -and
       [int]$e.NativeRoots -eq 8509 -and
       $emitted -eq $Limit -and
       [bool]$e.LayoutReplayExact -and
       [bool]$e.ExactIdentitySequence -and
       [bool]$e.OatZeroWarningsZeroErrors -and
       [bool]$e.ValidationPass -and
       [bool]$e.ValidationOnly -and
       -not [bool]$e.ReadyForHardware -and
       -not [bool]$e.OriginalPcWeaponFullOwnedGraphsEmitted)
$summaryObj=[ordered]@{
    schema='WaW360.AztecWeapon63RealSourceScalarXstringFfGrowthCanary.v1'
    status=if($pass){'AZTEC_W63_SOURCE_SCALAR_XSTRINGS_IN_NATIVE_FF_OAT_LOAD_PASS_FULL_WEAPON_GRAPH_BLOCKED'}
        else{'AZTEC_W63_ISOLATED_NATIVE_FF_OAT_OR_LAYOUT_BLOCKED'}
    originalPcAuthenticXboxWeaponDefBodiesSourceProven=63
    originalPcNativeOwnedProofBytesAvailable=179775
    nativeWeaponRootScalarXstringCanariesEmitted=$emitted
    fullWeaponDefChildrenAndDependenciesEmitted=$false
    requestedCanaries=$Limit
    originalPcSourceWeaponBodyUsedForEachCanary=$true
    fixedBodyNativeScalarsOnly=$true
    originalInlineXstringsOnly=$true
    protectedPriorNativeFastFile=$protected
    protectedPriorNativeFastFileBytes=$baselineBytes
    protectedPriorSha256=[string]$s.converted.sha256
    newScratchFastFile=$ff
    newScratchFastFileBytes=$newBytes
    newScratchFastFileSha256=$sha
    deltaBytesComparedToProtectedBaseline=$delta
    growthPercentComparedToProtectedBaseline=if($newBytes -gt 0){[Math]::Round(100.0*$delta/$baselineBytes,3)}else{$null}
    oatLoaded8509NativeAssetIdentities=if($null -ne $e){[bool]$e.ExactIdentitySequence}else{$false}
    oatZeroWarningsErrors=if($null -ne $e){[bool]$e.OatZeroWarningsZeroErrors}else{$false}
    nativeLayoutReplayExact=if($null -ne $e){[bool]$e.LayoutReplayExact}else{$false}
    wrapperRoundTripVerified=if($null -ne $e){[bool]$e.LayoutReplayExact}else{$false}
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
    note='This isolated FF includes real original-PC numeric WeaponDef scalars and original inline WeaponDef display/overlay strings, but nulls all other WeaponDef pointers. It does not contain 63 complete 7b source-owned weapon graphs, nor make the map playable. Size alone never certifies source closure.'
}
$summaryObj|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $summary -Encoding UTF8
Write-Host '[AZTEC-W63-GROWTH-PROGRESS] [################----] 80% | measured new actual compressed FF size, OAT result and source-derived child bytes'
Write-Host "[AZTEC-W63-GROWTH] PROTECTED BASELINE FF = $baselineBytes bytes ($('{0:N2}' -f ($baselineBytes/1MB)) MiB)"
Write-Host "[AZTEC-W63-GROWTH] NEW SCRATCH FF = $newBytes bytes ($('{0:N2}' -f ($newBytes/1MB)) MiB)"
Write-Host "[AZTEC-W63-GROWTH] MEASURED DELTA vs prior protected candidate = $delta bytes"
Write-Host "[AZTEC-W63-GROWTH] Native original-PC scalar+inline XString WeaponDef roots=$emitted/$Limit; full 7b graph in FF=0"
Write-Host "[AZTEC-W63-GROWTH] Stage4D layout exact=$($summaryObj.nativeLayoutReplayExact); OAT 8509 identity=$($summaryObj.oatLoaded8509NativeAssetIdentities); OAT 0/0=$($summaryObj.oatZeroWarningsErrors)"
Write-Host "[AZTEC-W63-GROWTH] STATUS=$($summaryObj.status)"
Write-Host "[AZTEC-W63-GROWTH] JSON=$summary"
Write-Host "[AZTEC-W63-GROWTH] Scratch FF=$ff"
Write-Host '[AZTEC-W63-GROWTH-PROGRESS] [####################] 100% | diagnostic size and validation complete; NOT Xbox-ready'
if(-not $pass){
    throw "Weapon63 isolated FF validation BLOCKED; inspect emitter/OAT log under $work. Retry -Limit 1 to bisect a single real WeaponDef source root, not fake FF growth."
}
Write-Host '[AZTEC-W63-GROWTH] OAT scratch candidate PASS. Full WeaponDef pointer/child graphs remain the next stage.'
