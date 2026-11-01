[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Prepare one future FF build by proving EVERY source-owned PC primitive
# child in a single native-endian pass, then mapping root reference names.
# This never emits/rebuilds any .ff, runs OAT, or copies Xbox donor bytes.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$work=Join-Path $base 'pc_g11_cells_740\gate11_remaining_unified_source'
$source=Join-Path $work 'aztec_gate11_remaining_world_batch_source_gate.json'
$leafScript=Join-Path $PSScriptRoot 'Prepare-AztecGate11NativeLeafBatch.ps1'
$bindScript=Join-Path $PSScriptRoot 'Audit-AztecGate11WorldAssetBindings.ps1'
$leafReport=Join-Path $work 'native_leaf_batch\aztec_gate11_native_leaf_batch_conversion.json'
$bindingReport=Join-Path $work 'aztec_gate11_exact_asset_reference_audit.json'
$summary=Join-Path $work 'aztec_gate11_combined_no_ff_build_preflight.json'
foreach($p in @($source,$leafScript,$bindScript)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Gate11 combined preflight prerequisite missing: $p"}
}
$s=Get-Content -LiteralPath $source -Raw|ConvertFrom-Json
if([string]$s.status -ne 'AZTEC_GATE11_ALL_REMAINING_WORLD_SOURCE_CORPUS_CAPTURED_FF_BATCH_NOT_YET_APPROVED' -or
   [int]$s.sourceFamilyCount -ne 8 -or
   [long]$s.originalPcSourceLeafByteCount -ne 6930524L -or
   [bool]$s.singleCombinedFfReadyToEmit -or [bool]$s.actualMapFfBuildPerformed){
    throw 'The bundled all-family source evidence is missing or has a changed readiness status.'
}
Write-Host '[AZTEC-GATE11-ONEBUILD] ================================================================'
Write-Host '[AZTEC-GATE11-ONEBUILD] First source-only native endian conversion of ALL 13 families.'
Write-Host '[AZTEC-GATE11-ONEBUILD] No .NET build, no OAT build, NO MAP FF REBUILD, no original Xbox payload.'
Write-Host '[AZTEC-GATE11-PROGRESS] [####----------------] 20% | 13 PC leaf arrays -> original source SHA verification'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $leafScript
if($LASTEXITCODE -ne 0){throw 'Original PC source leaf native-endian round-trip preflight failed; no FF.'}
if(-not(Test-Path -LiteralPath $leafReport -PathType Leaf)){
    throw 'Original PC source leaf preflight did not produce its immutable hash report.'
}
$l=Get-Content -LiteralPath $leafReport -Raw|ConvertFrom-Json
if([string]$l.status -ne 'AZTEC_GATE11_13_SOURCE_LEAF_XENON_ENDIAN_CONVERSION_PASS_FULL_WORLD_UNPROVEN' -or
   [int]$l.families -ne 13 -or [long]$l.nativeCandidateByteCount -ne 6930524L -or
   [bool]$l.newNativeFfEmitted -or [bool]$l.gate11OverallPassed){
    throw 'Cannot advance without all 13 independently reversible endian source leaf conversions.'
}
Write-Host '[AZTEC-GATE11-PROGRESS] [############--------] 60% | exact Xbox native 8509-root image reference audit'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $bindScript
if($LASTEXITCODE -ne 0){throw 'Original-PC to native Xbox image root identity audit failed. No FF.'}
if(-not(Test-Path -LiteralPath $bindingReport -PathType Leaf)){
    throw 'Image binding reference audit did not save a readable report.'
}
$b=Get-Content -LiteralPath $bindingReport -Raw|ConvertFrom-Json
if([string]$b.status -ne 'AZTEC_GATE11_IMAGE_ROOT_IDENTITIES_CENSUSED_ONLY_NOT_NATIVE_PAYLOAD_PROOF' -or
   [string]$b.passingNativeWorldFfSha256 -ine
       [string]$l.protectedIndependentGate11FfSha256 -or
   [bool]$b.newNativeWorldFfEmitted -or [bool]$b.hardwareReady){
    throw 'Image root source manifest is not tied to the independently verified Gate11 baseline.'
}
$allIdentityNamesPresent=([int]$b.absentExactImageRootNames -eq 0 -and
    [int]$b.ambiguousExactImageRootNames -eq 0)
$plan=[ordered]@{
    schema='WaW360.AztecGate11CombinedNativeOneBuildPreparation.v1'
    status='AZTEC_GATE11_SOURCE_LEAF_13_NATIVE_ENDIAN_PASS_IMAGE_IDENTITIES_AUDITED_FULL_FF_BLOCKED'
    originalSourceFamiliesCollected=8
    originalSourceLeafArraysConverted=13
    sourceLeafBytes=6930524
    nativeEndianLeafBytes=[long]$l.nativeCandidateByteCount
    endiannessRoundTripProven=$true
    allSourceImageReferenceNamesHaveUnambiguousNativeCandidates=$allIdentityNamesPresent
    sourceImageReferenceCount=[int]$b.imageReferenceFields
    sourceImageNamesMissingNativeExactRoot=[int]$b.absentExactImageRootNames
    sourceImageNamesAmbiguousNativeRoot=[int]$b.ambiguousExactImageRootNames
    nativeCandidateNameIsNotPayloadEquivalence=$true
    nativeImageRootBindingsActuallyEmitted=$false
    nativeMaterialRootBindingsActuallyEmitted=$false
    xboxNativeStreamInfoReconstructed=$false
    nestedShadowAndLightRegionGraphConverted=$false
    previousPassingGate11Ff=[string]$s.protectedPreviousGate11Ff
    previousPassingGate11FfSha256=[string]$s.protectedPreviousGate11FfSha256
    nativeEndiannessLeafReport=$leafReport
    imageExactRootNameAudit=$bindingReport
    targetOneFutureIsolatedWorldFF='pc_g11_remaining_world_single_batch'
    currentFfEmissionPerformed=$false
    allWorldGraphComponentsComplete=$false
    fullGate11Passed=$false
    XboxHardwareReady=$false
}
$plan|ConvertTo-Json -Depth 7|Set-Content -LiteralPath $summary -Encoding UTF8
Write-Host '[AZTEC-GATE11-PROGRESS] [####################] 100% | native source arrays + root identities verified WITHOUT FF build'
Write-Host "[AZTEC-GATE11-ONEBUILD] PASS source endian leaf graph 13/13 ($($plan.nativeEndianLeafBytes) bytes)."
Write-Host "[AZTEC-GATE11-ONEBUILD] IMAGE identity candidates exact=$($b.uniqueExactImageRootNameCandidates) missing=$($b.absentExactImageRootNames) ambiguous=$($b.ambiguousExactImageRootNames) null=$($b.nullImageReferenceFields)"
Write-Host "[AZTEC-GATE11-ONEBUILD] Future single candidate FF must serialize 13 leaf families + resolved named lightmap/reflection roots + source shadow/region graph + Xenon streaming semantics."
Write-Host "[AZTEC-GATE11-ONEBUILD] Combined report=$summary"
Write-Host '[AZTEC-GATE11-ONEBUILD] 10/13 Gate11 overall PENDING. Previous real FF unchanged. Xbox hardware NOT approved.'
