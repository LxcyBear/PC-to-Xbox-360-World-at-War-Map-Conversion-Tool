[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Exact differential compressed-FF measurement: identical original-PC
# world/renderer and OAT setup, only the source-owned WeaponDef canary
# count changes 0 -> 63. Diagnostic FFs are NOT Xbox deployable.
# Never modify the SHA-pinned 4,510,695-byte protected good FF.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$sourceReport=Join-Path $base 'pc_g11_lightning_weapon_69_unified_native_source\aztec_69_weapon_unified_original_pc_xbox_body_selections.json'
$sizeReport=Join-Path $base 'pc_g11_native_ff_size_audit\aztec_native_ff_size_and_source_ledger.json'
$shared=Join-Path $repo '.deps\aztec-w63-ff-growth'
$archive=Join-Path $repo '.deps\aztec-w63-exact-ab'
$report=Join-Path $archive 'aztec_weapon63_exact_same_world_compressed_ff_delta.json'
$md=Join-Path $archive 'aztec_weapon63_exact_same_world_compressed_ff_delta.md'
$script=Join-Path $PSScriptRoot 'Run-AztecWeapon63FfGrowthCanary.ps1'
foreach($file in @($sourceReport,$sizeReport,$script)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){
        throw "A/B source evidence/runner is missing: $file"
    }
}
$p=Get-Content -LiteralPath $sourceReport -Raw|ConvertFrom-Json
$s=Get-Content -LiteralPath $sizeReport -Raw|ConvertFrom-Json
if([string]$p.schema -ne 'WaW360.AztecWeapon69UnifiedOriginalPcXboxBodySelection.v1' -or
   [string]$p.status -ne 'AZTEC_69_WEAPON_NATIVE_ROOTS_63_AUTHENTIC_PC_XBOX_BODIES_2_CONFLICT_4_STOCK_UNPROVEN_NOT_IN_FF' -or
   [int]$p.nativeWeaponDefRoots -ne 69 -or
   [int]$p.uniqueSourceOwnedXboxWeaponBodyMappings -ne 63 -or
   [long]$p.selectedUncompressedXboxOwnedPayloadBytes -ne 179775 -or
   [int]$p.conflictingDifferentPcSourceBodiesStillFenced -ne 2 -or
   [int]$p.offlineStockOnlyWeaponNamesNotRuntimeProven -ne 4 -or
   [bool]$p.xboxRuntimeReady -or
   [string]$s.schema -ne 'WaW360.AztecNativeFfSizeAndSourceCoverage.v1' -or
   [string]$s.converted.sha256 -ine 'F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6'){
    throw 'Exact original-PC WeaponDef 63/2/4 manifest or protected FF baseline changed.'
}
$protected=[string]$s.converted.path
if(-not(Test-Path -LiteralPath $protected -PathType Leaf)){
    throw "Protected OAT-approved FastFile missing: $protected"
}
$protectedBytes=[long](Get-Item -LiteralPath $protected).Length
$protectedHash=(Get-FileHash -LiteralPath $protected -Algorithm SHA256).Hash
if($protectedBytes -ne 4510695 -or $protectedHash -ine [string]$s.converted.sha256){
    throw "Protected good 4,510,695-byte FF SHA/length differs; refuse A/B."
}
$sourceHash=(Get-FileHash -LiteralPath $sourceReport -Algorithm SHA256).Hash
$sizeHash=(Get-FileHash -LiteralPath $sizeReport -Algorithm SHA256).Hash
# Code/content hashes matter in addition to HEAD: an uncommitted local edit
# between A/B passes would make the compressed delta misleading.
$codeInputs=@(
    'src\WaW360.Core\FastFiles\T4XenonAztecWeapon63ScalarCanary.cs',
    'src\WaW360.Core\FastFiles\T4XenonGraphBlockLayoutPlanner.cs',
    'src\WaW360.Core\FastFiles\T4XenonRendererGraphEmitter.cs',
    'src\WaW360.Core\FastFiles\T4WeaponVariableSerializerProof.cs',
    'tools\oat-xenon\Run-AztecWeapon63FfGrowthCanary.ps1',
    'tools\oat-xenon\Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1',
    'tools\oat-xenon\Run-AztecWeapon63ExactABSizeLightning.ps1'
)
$codeHashes=@{}
foreach($relative in $codeInputs){
    $file=Join-Path $repo $relative
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){
        throw "Cannot pin A/B code input: $file"
    }
    $codeHashes[$relative]=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
}
$revision=(& git -C $repo rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($revision)){
    throw 'Cannot pin current exact repository code revision.'
}
New-Item -ItemType Directory -Path $archive -Force|Out-Null

# Preserve the user's previous 63/63 OAT-good scratch FF before
# the 0-canary run legitimately replaces its shared diagnostic output.
$oldSummary=Join-Path $shared 'aztec_weapon63_ff_growth_63.json'
$oldFf=Join-Path $shared 'scratch_emit\nazi_zombie_aztec.ff'
if((Test-Path -LiteralPath $oldSummary -PathType Leaf) -and
   (Test-Path -LiteralPath $oldFf -PathType Leaf)){
    $last=Get-Content -LiteralPath $oldSummary -Raw|ConvertFrom-Json
    $lastHash=(Get-FileHash -LiteralPath $oldFf -Algorithm SHA256).Hash
    if([string]$last.status -eq 'AZTEC_W63_SOURCE_SCALAR_XSTRINGS_IN_NATIVE_FF_OAT_LOAD_PASS_FULL_WEAPON_GRAPH_BLOCKED' -and
       [int]$last.nativeWeaponRootScalarXstringCanariesEmitted -eq 63 -and
       $lastHash -ieq [string]$last.newScratchFastFileSha256){
        $previousDir=Join-Path $archive 'prior_OAT_passed_63'
        New-Item -ItemType Directory -Path $previousDir -Force|Out-Null
        Copy-Item -LiteralPath $oldFf -Destination (Join-Path $previousDir 'nazi_zombie_aztec.ff') -Force
        Copy-Item -LiteralPath $oldSummary -Destination (Join-Path $previousDir 'prior_63_pass.json') -Force
        Write-Host '[AZTEC-W63-AB] Archived existing 63/63 OAT-passing scratch FF before rebuilding control.'
    }
}
Write-Host '[AZTEC-W63-AB] ========================================================='
Write-Host '[AZTEC-W63-AB] TRUE compressed FF growth: same full Gate11 world, 0 vs 63 PC WeaponDef canaries'
Write-Host "[AZTEC-W63-AB] Preserved 4,510,695-byte protected FF SHA: $protectedHash"
Write-Host '[AZTEC-W63-AB] Both builds require 8509 exact identities and OAT 0/0, with NO Xbox deployment.'
Write-Host '[AZTEC-W63-AB] Full 7b child graphs (179,775 source-proof bytes) are NOT copied to either FF.'
Write-Host '[AZTEC-W63-AB-PROGRESS] [###-----------------] 15% | original source evidence, good FF and Git revision locked'

function Assert-InputsStillPinned {
    $hash=(Get-FileHash -LiteralPath $protected -Algorithm SHA256).Hash
    $len=[long](Get-Item -LiteralPath $protected).Length
    if($hash -ine $protectedHash -or $len -ne $protectedBytes){
        throw 'Protected native FF SHA/bytes changed between A/B runs.'
    }
    if((Get-FileHash -LiteralPath $sourceReport -Algorithm SHA256).Hash -ine $sourceHash -or
       (Get-FileHash -LiteralPath $sizeReport -Algorithm SHA256).Hash -ine $sizeHash){
        throw 'Original-PC source manifest changed DURING A/B test.'
    }
    $now=(& git -C $repo rev-parse HEAD).Trim()
    if($LASTEXITCODE -ne 0 -or $now -ine $revision){
        throw 'Git source revision changed DURING A/B test; results not comparable.'
    }
    foreach($relative in $codeInputs){
        $path=Join-Path $repo $relative
        if(-not(Test-Path -LiteralPath $path -PathType Leaf) -or
           (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine [string]$codeHashes[$relative]){
            throw "A/B native FF tool source code changed between control and 63-case: $relative"
        }
    }
}
function Invoke-ExactCase([int]$count,[string]$label){
    Assert-InputsStillPinned
    Write-Host "[AZTEC-W63-AB] BUILD $label : original PC weapon diagnostic canaries=$count"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $script -Limit $count
    $code=[int]$LASTEXITCODE
    if($code -ne 0){
        throw "A/B case $label failed (exit=$code). No exact weapon-only FF delta is claimed. Inspect .deps\aztec-w63-ff-growth logs."
    }
    $srcSummary=Join-Path $shared ("aztec_weapon63_ff_growth_"+$count+".json")
    $srcManifest=Join-Path $shared 'scratch_emit\nuclear_xenon_renderer_emit.json'
    $srcFf=Join-Path $shared 'scratch_emit\nazi_zombie_aztec.ff'
    foreach($required in @($srcSummary,$srcManifest,$srcFf)){
        if(-not(Test-Path -LiteralPath $required -PathType Leaf)){
            throw "A/B case $label PASS flag but output missing: $required"
        }
    }
    $r=Get-Content -LiteralPath $srcSummary -Raw|ConvertFrom-Json
    $e=Get-Content -LiteralPath $srcManifest -Raw|ConvertFrom-Json
    $expectedStatus=if($count -eq 0){'AZTEC_W63_NO_WEAPON_SAME_SETTINGS_CONTROL_OAT_PASS'}
        else{'AZTEC_W63_SOURCE_SCALAR_XSTRINGS_IN_NATIVE_FF_OAT_LOAD_PASS_FULL_WEAPON_GRAPH_BLOCKED'}
    $length=[long](Get-Item -LiteralPath $srcFf).Length
    $sha=(Get-FileHash -LiteralPath $srcFf -Algorithm SHA256).Hash
    if([string]$r.schema -ne 'WaW360.AztecWeapon63RealSourceScalarXstringFfGrowthCanary.v1' -or
       [string]$r.status -ne $expectedStatus -or
       [int]$r.parentRunnerExitCode -ne 0 -or
       [int]$r.requestedCanaries -ne $count -or
       [int]$r.nativeWeaponRootScalarXstringCanariesEmitted -ne $count -or
       [int]$e.OriginalPcWeaponDiagnosticRoots -ne $count -or
       [bool]$e.OriginalPcWeaponFullOwnedGraphsEmitted -or
       [bool]$r.fullWeaponDefChildrenAndDependenciesEmitted -or
       [bool]$r.outputNativeXboxHardwareReady -or
       [bool]$r.userMapDeployable -or
       [bool]$r.gate11Closed -or
       [int]$e.NativeRoots -ne 8509 -or
       -not [bool]$e.LayoutReplayExact -or
       -not [bool]$e.ExactIdentitySequence -or
       -not [bool]$e.OatZeroWarningsZeroErrors -or
       -not [bool]$e.ValidationPass -or
       -not [bool]$e.ValidationOnly -or [bool]$e.ReadyForHardware -or
       [long]$r.newScratchFastFileBytes -ne $length -or
       [string]$r.newScratchFastFileSha256 -ine $sha -or
       [string]$r.protectedPriorSha256 -ine $protectedHash -or
       [long]$r.protectedPriorNativeFastFileBytes -ne $protectedBytes){
        throw "A/B case $label failed exact original PC native WeaponDef, OAT, source or physical FF SHA verification."
    }
    $caseDir=Join-Path $archive $label
    New-Item -ItemType Directory -Path $caseDir -Force|Out-Null
    $copied=Join-Path $caseDir 'nazi_zombie_aztec.ff'
    Copy-Item -LiteralPath $srcFf -Destination $copied -Force
    Copy-Item -LiteralPath $srcSummary -Destination (Join-Path $caseDir 'case_size_and_oat.json') -Force
    Copy-Item -LiteralPath $srcManifest -Destination (Join-Path $caseDir 'nuclear_xenon_renderer_emit.json') -Force
    foreach($log in @('integrated_emitter.log','aztec_pc_world_core_integrated_oat_run.json')){
        $path=Join-Path $shared $log
        if(Test-Path -LiteralPath $path -PathType Leaf){
            Copy-Item -LiteralPath $path -Destination (Join-Path $caseDir $log) -Force
        }
    }
    if((Get-FileHash -LiteralPath $copied -Algorithm SHA256).Hash -ine $sha){
        throw "A/B case $label archive FF SHA differs from fresh OAT-passed build."
    }
    Write-Host "[AZTEC-W63-AB] $label OAT PASS; roots=8509; compressed FF bytes=$length; SHA=$sha"
    return [pscustomobject]@{
        label=$label
        weaponCanaries=$count
        compressedFfBytes=$length
        compressedFfSha256=$sha
        archivedFastFile=$copied
        evidenceManifest=Join-Path $caseDir 'nuclear_xenon_renderer_emit.json'
        tempMaxBytes=[long]$e.TempBytes
        virtualBytes=[long]$e.VirtualBytes
        physicalBytes=[long]$e.PhysicalBytes
        mainZoneBytes=[long]$e.MainZoneBytes
        zoneBytes=[long]$e.ZoneBytes
        diagnosticSourceOwnedWeaponBytesEmitted=[long]$e.OriginalPcWeaponDiagnosticSerializedBytes
        sourceOwnedFull7bProofBytesRepresented=[long]$e.OriginalPcWeaponSourceProofBytes
        realMaterialRoots=[int]$e.RealMaterialRoots
        realXModelRoots=[int]$e.RealXModelRoots
        realImageRoots=[int]$e.RealImageRoots
        realRawFileRoots=[int]$e.RealRawFileRoots
        gate10Draws=[int]$e.OriginalPcGfxWorldGate10DrawRecords
        gate11Cells=[int]$e.OriginalPcGfxWorldGate11Cells
        gate11Trees=[int]$e.OriginalPcGfxWorldGate11AabbTrees
        gate11ModelIndexes=[int]$e.OriginalPcGfxWorldGate11SmodelIndexes
        oatLoadedNativeIdentities=8509
        oatZeroWarningsZeroErrors=$true
        layoutReplayExact=$true
        validationOnly=$true
        fullWeaponGraphsEmitted=$false
        xboxHardwareReady=$false
    }
}
$control=Invoke-ExactCase 0 'control_00'
Write-Host '[AZTEC-W63-AB-PROGRESS] [##########----------] 50% | exact same-settings zero-weapon control FF saved + OAT-validated'
$weapons=Invoke-ExactCase 63 'weapons_63'
Write-Host '[AZTEC-W63-AB-PROGRESS] [#################---] 85% | exact same-settings 63 original PC weapons FF saved + OAT-validated'
Assert-InputsStillPinned
foreach($key in @('realMaterialRoots','realXModelRoots','realImageRoots','realRawFileRoots','gate10Draws','gate11Cells','gate11Trees','gate11ModelIndexes','physicalBytes')){
    if($control.$key -ne $weapons.$key){
        throw "A/B non-weapon world content changed ($key): $($control.$key) vs $($weapons.$key). Cannot attribute byte delta to WeaponDef canaries."
    }
}
if($control.weaponCanaries -ne 0 -or $control.diagnosticSourceOwnedWeaponBytesEmitted -ne 0 -or
   $weapons.weaponCanaries -ne 63 -or
   $weapons.diagnosticSourceOwnedWeaponBytesEmitted -le 0 -or
   $weapons.sourceOwnedFull7bProofBytesRepresented -ne 179775){
    throw 'A/B source diagnostic byte count is not a clean 0 -> 63 identical-world comparison.'
}
$weaponDelta=$weapons.compressedFfBytes-$control.compressedFfBytes
$zoneDelta=$weapons.zoneBytes-$control.zoneBytes
$protectedToControl=$control.compressedFfBytes-$protectedBytes
$protectedToWeapon=$weapons.compressedFfBytes-$protectedBytes
$status=if($weaponDelta -gt 0){
    'AZTEC_W63_EXACT_SAME_WORLD_0_VS_63_COMPRESSED_FF_GROWTH_OAT_PASS'
}elseif($weaponDelta -eq 0){
    'AZTEC_W63_EXACT_SAME_WORLD_0_VS_63_COMPRESSED_SIZE_UNCHANGED_OAT_PASS'
}else{
    'AZTEC_W63_EXACT_SAME_WORLD_0_VS_63_COMPRESSED_SIZE_DECREASE_OAT_PASS'
}
$result=[ordered]@{
    schema='WaW360.Aztec63WeaponExactSameSettingsAbFfSize.v1'
    status=$status
    revision=$revision
    inputOriginalPcWeapon69ManifestSha256=$sourceHash
    inputProtectedSizeLedgerSha256=$sizeHash
    exactNativeFfEmitterAndRunnerCodeSha256=$codeHashes
    originalSourceOwnedWeaponRootsWithRealXboxSourceProof=63
    originalSourceOnlyFull7bWeaponGraphBytes=179775
    protectedOatPassingFfBytes=$protectedBytes
    protectedOatPassingFfSha256=$protectedHash
    control=$control
    with63Weapons=$weapons
    exactWeaponOnlyCompressedSizeDeltaBytes=$weaponDelta
    exactWeaponOnlyUncompressedZoneDeltaBytes=$zoneDelta
    differentialZonePayloadFromVerifiedScalarsAndInlineXStringsBytes=$weapons.diagnosticSourceOwnedWeaponBytesEmitted
    controlVsProtectedFfBytes=$protectedToControl
    weaponCandidateVsProtectedFfBytes=$protectedToWeapon
    compressedWeaponPercentOfControl=[Math]::Round(100.0*$weaponDelta/$control.compressedFfBytes,4)
    exactSameSourceManifestsAndGitRevisionForBothRuns=$true
    allOtherTrackedWorldRootAndChildCountsEqual=$true
    bothCasesOatPass8509IdentitiesZeroWarningsErrors=$true
    noUnverifiedSourcePointersCopiedToCandidate=$true
    unresolvedTwoWeaponPcSourceConflictsStillQuarantined=$true
    stockOnlyFourWeaponNamesRemainUnprovenAtRuntime=$true
    allFullOwnedWeaponDefChildrenAndDependenciesLoaded=$false
    gamePlayableOnXbox=$false
    gate11Closed=$false
    noProtectedFfReplaced=$true
    caveat='Two Stage4D+4E diagnostic full Gate11 builds use the same source manifests, code revision and settings; only WeaponDef canary count differs 0 vs 63. Exact compressed delta is genuine integration size difference for the 63 PARTIAL scalar/inline-XString bodies, not the original 179775 bytes of full Xbox source-owned weapon 7b graphs. Both OAT loads are NOT Xbox hardware/gameplay proof.'
}
$result|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $report -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add('# Aztec same-world WeaponDef 0-vs-63 native FF compressed size and OAT A/B')
$lines.Add('')
$lines.Add("**Status:** $status")
$lines.Add('')
$lines.Add('| Case | Diagnostic PC weapon roots | Real compressed FF bytes | OAT exact 8,509 / 0 warning/error |')
$lines.Add('|---|---:|---:|---|')
$lines.Add("| Control 0 | 0 | $($control.compressedFfBytes) | PASS |")
$lines.Add("| Actual 63 | 63 | $($weapons.compressedFfBytes) | PASS |")
$lines.Add('')
$lines.Add("**Actual 63-weapon-only compressed delta: $weaponDelta bytes** (whole uncompressed zone delta: $zoneDelta bytes).")
$lines.Add("Historical separately protected FF: $protectedBytes bytes; control minus historical: $protectedToControl; 63 minus historical: $protectedToWeapon.")
$lines.Add("63 original-PC-source scalar+inline-XString diagnostic bytes: $($weapons.diagnosticSourceOwnedWeaponBytesEmitted); 63 authentic full standalone 7b proofs: 179775 bytes, NOT emitted as full graphs.")
$lines.Add('')
$lines.Add('Both archived FFs are scratch-only and not deployable. Outstanding: 2 conflicting versions, 4 stock-dependent names, nested WeaponDef child graph relinking, full world image/material/audio and runtime gates.')
[IO.File]::WriteAllLines($md,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-W63-AB-PROGRESS] [####################] 100% | 0 and 63 weapon FFs SHA archived, OAT both PASS, exact size measured'
Write-Host '[AZTEC-W63-AB] ========================================================='
Write-Host "[AZTEC-W63-AB] PROTECTED HISTORICAL GOOD FF: $protectedBytes B"
Write-Host "[AZTEC-W63-AB] CONTROL same-world 0-weapon FF: $($control.compressedFfBytes) B"
Write-Host "[AZTEC-W63-AB] WEAPONS same-world 63-weapon FF: $($weapons.compressedFfBytes) B"
Write-Host "[AZTEC-W63-AB] EXACT WEAPON-ONLY COMPRESSED DELTA: $weaponDelta B"
Write-Host "[AZTEC-W63-AB] EXACT ZONE BYTES DELTA: $zoneDelta B"
Write-Host "[AZTEC-W63-AB] ORIGINAL SOURCE SCALAR+INLINE XSTRING BYTES: $($weapons.diagnosticSourceOwnedWeaponBytesEmitted)"
Write-Host '[AZTEC-W63-AB] BOTH FULL GATE11 SCRATCH FFs OAT PASS 8509/8509, 0 warnings/errors; NO Xbox approval.'
Write-Host "[AZTEC-W63-AB] STATUS=$status"
Write-Host "[AZTEC-W63-AB] JSON: $report"
Write-Host "[AZTEC-W63-AB] Markdown: $md"
Write-Host '[AZTEC-W63-AB] Both exact source variants archived separately; original protected good FF unchanged.'
