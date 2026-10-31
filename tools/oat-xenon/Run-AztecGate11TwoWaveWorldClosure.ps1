[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# ONE SHA-locked, source-owned work plan across BOTH upcoming GfxWorld waves:
# A: light-grid + reflection/lightmap asset bindings + shadow geometry.
# B: light-region hull/axis graph + reconstructed T4 Xenon stream metadata.
# Only validated ORIGINAL PC source and measured Xbox STRUCT ABI are inputs.
# This preflight does not build an FF, change a baseline, copy retail payload,
# approve named image matches as payloads, or claim Gate11 closure.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$sourceDir=Join-Path $base 'pc_g11_cells_740\gate11_remaining_unified_source'
$primitiveDir=Join-Path $base 'pc_g11_remaining_world_primitive_probe'
$preflight=Join-Path $sourceDir 'aztec_gate11_combined_no_ff_build_preflight.json'
$primitive=Join-Path $primitiveDir 'oat_loaded_primitive_source_digest\aztec_gate11_independent_loaded_primitive_source_digest.json'
$outDir=Join-Path $base 'pc_g11_two_wave_world_closure'
$report=Join-Path $outDir 'aztec_gate11_two_wave_source_dependency_plan.json'
$expectedCellSha='05CEEC82AF5D58706BA5A6EE24995D09A3FB764F3E547A8B4758A9347ECF2862'
$expectedPrimitiveSha='D46ABC8E5681E4F235ED82821016F6F0002505AD0DEF68D680D70FCCE75F1FCB'
foreach($path in @($preflight,$primitive)){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Two-wave Gate11 requires existing passing source preflight and typed OAT primitive readback: $path"
    }
}
$p=Get-Content -LiteralPath $preflight -Raw | ConvertFrom-Json
$d=Get-Content -LiteralPath $primitive -Raw | ConvertFrom-Json
if([string]$p.schema -ne 'WaW360.AztecGate11CombinedNativeOneBuildPreparation.v1' -or
   [string]$p.status -ne 'AZTEC_GATE11_SOURCE_LEAF_13_NATIVE_ENDIAN_PASS_IMAGE_IDENTITIES_AUDITED_FULL_FF_BLOCKED' -or
   [int]$p.originalSourceLeafArraysConverted -ne 13 -or
   [long]$p.nativeEndianLeafBytes -ne 6930524L -or
   [string]$p.previousPassingGate11FfSha256 -ine $expectedCellSha -or
   -not [bool]$p.endiannessRoundTripProven -or
   [bool]$p.fullGate11Passed -or [bool]$p.XboxHardwareReady -or
   [string]$d.schema -ne 'WaW360.AztecGate11IndependentLoadedPrimitiveSourceDigest.v1' -or
   [string]$d.status -ne 'AZTEC_GATE11_SOURCE_OWNED_SKY_BRUSH_WATER_NATIVE_LOADED_DIGEST_PASS_REMAINDER_PENDING' -or
   [string]$d.actualNewNativeFfSha256 -ine $expectedPrimitiveSha -or
   [string]$d.untouchedPriorGate11FfSha256 -ine $expectedCellSha -or
   [int]$d.leafGroupsWithExactLoadedSourceDigests -ne 4 -or
   [long]$d.sourceUncompressedBytesCovered -ne 2107664L -or
   [bool]$d.fullGate11Passed -or [bool]$d.xboxHardwareReady){
    throw 'Two-wave plan refuses unproven FFs, non-source leaf graphs, or changed release fences.'
}
$baseline=[string]$p.previousPassingGate11Ff
$proven=[string]$d.actualNewNativeFf
foreach($file in @($baseline,$proven)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Protected FF missing: $file"}
}
if([IO.Path]::GetFullPath($proven) -ieq [IO.Path]::GetFullPath($baseline) -or
   (Get-FileHash -LiteralPath $baseline -Algorithm SHA256).Hash -ine $expectedCellSha -or
   (Get-FileHash -LiteralPath $proven -Algorithm SHA256).Hash -ine $expectedPrimitiveSha -or
   [long](Get-Item -LiteralPath $proven).Length -ne 3650170L){
    throw 'The protected native 11-cell or source-owned primitive FF changed. Do not plan a new candidate.'
}
Write-Host '[AZTEC-GATE11-TWO-WAVE] ===================================================================='
Write-Host '[AZTEC-GATE11-TWO-WAVE] Wave A: light-grid + reflection/lightmap + shadows, in ONE future FF.'
Write-Host '[AZTEC-GATE11-TWO-WAVE] Wave B: light-region graphs + Xenon stream metadata, same prepared root.'
Write-Host '[AZTEC-GATE11-TWO-WAVE] No builds/FF emission, Xbox donor payloads, or hardware approval.'
Write-Host '[AZTEC-GATE11-PROGRESS] [####----------------] 20% | protect both OAT-proven source FFs'

$leafPath=[string]$p.nativeEndiannessLeafReport
$bindingPath=[string]$p.imageExactRootNameAudit
foreach($file in @($leafPath,$bindingPath)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Previous source/reference report missing: $file"}
}
$l=Get-Content -LiteralPath $leafPath -Raw|ConvertFrom-Json
$b=Get-Content -LiteralPath $bindingPath -Raw|ConvertFrom-Json
$sourcePath=[string]$l.sourceReadinessReport
if(-not(Test-Path -LiteralPath $sourcePath -PathType Leaf)){throw "Original PC source manifest missing: $sourcePath"}
$s=Get-Content -LiteralPath $sourcePath -Raw|ConvertFrom-Json
$worldPath=[string]$s.sourceTypedRemainderGraph
$abiPath=[string]$s.nativeXboxAbiOnlyFile
foreach($file in @($worldPath,$abiPath)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Original-PC world or measured Xbox ABI file missing: $file"}
}
if([string]$l.status -ne 'AZTEC_GATE11_13_SOURCE_LEAF_XENON_ENDIAN_CONVERSION_PASS_FULL_WORLD_UNPROVEN' -or
   [int]$l.families -ne 13 -or
   [string]$l.protectedIndependentGate11FfSha256 -ine $expectedCellSha -or
   [bool]$l.gate11OverallPassed -or
   [string]$s.schema -ne 'WaW360.AztecGate11SingleBatchRemainingWorldSource.v1' -or
   [string]$s.status -ne 'AZTEC_GATE11_ALL_REMAINING_WORLD_SOURCE_CORPUS_CAPTURED_FF_BATCH_NOT_YET_APPROVED' -or
   [long]$s.originalPcSourceLeafByteCount -ne 6930524L -or
   [string]$s.protectedPreviousGate11FfSha256 -ine $expectedCellSha -or
   [bool]$s.singleCombinedFfReadyToEmit -or [bool]$s.hardwareReady -or
   [string]$b.schema -ne 'WaW360.AztecGate11OriginalPcWorldAssetExactNameBindingAudit.v1' -or
   [string]$b.status -ne 'AZTEC_GATE11_IMAGE_ROOT_IDENTITIES_CENSUSED_ONLY_NOT_NATIVE_PAYLOAD_PROOF' -or
   [int]$b.nativeImageRootCount -ne 666 -or
   -not [bool]$b.rootIdentityIsNotPayloadEquivalence -or
   [bool]$b.rootReferencesRebasedInNewNativeFf -or [bool]$b.hardwareReady -or
   (Get-FileHash -LiteralPath $worldPath -Algorithm SHA256).Hash -ine
      [string]$s.sourceTypedRemainderGraphSha256 -or
   (Get-FileHash -LiteralPath $abiPath -Algorithm SHA256).Hash -ine
      [string]$s.nativeXboxAbiOnlySha256){
    throw 'Original-PC source, binary leaf provenance, Xbox-only struct ABI, or identity audit drifted.'
}
$w=Get-Content -LiteralPath $worldPath -Raw|ConvertFrom-Json
$a=Get-Content -LiteralPath $abiPath -Raw|ConvertFrom-Json
$c=$w.counts
if([string]$w.schema -ne 'WaW360.T4PcGate11WorldRemainderCensus.v1' -or
   -not [bool]$w.sourceOnlyComplete -or
   [bool]$w.nativeXboxPayloadBytesUsed -or
   [bool]$w.originalPcProcessPointerValuesExported -or
   [string]$a.schema -ne 'WaW360.T4XenonGate11RemainingWorldNativeAbi.v1' -or
   [int]$a.gfxWorldBytes -ne 900 -or
   [int]$a.reflectionProbeBytes -ne 16 -or
   [int]$a.lightmapArrayBytes -ne 8 -or
   [int]$a.gridEntryBytes -ne 4 -or
   [int]$a.gridColorsBytes -ne 168 -or
   [bool]$a.containsOriginalXboxPayload){
    throw 'Original PC typed source or Xbox measured GfxWorld STRUCT-only layout changed.'
}
Write-Host '[AZTEC-GATE11-PROGRESS] [########------------] 40% | four light-grid leaves and measured native header layout'
# SHA validate both representations, byte sizes, counts and type conversion
# for ALL FOUR light-grid families, not just a source manifest boolean.
$gridNames=@('gridRowDataStart','gridRawRowData','gridEntries','gridColors')
$gridCounts=@{
    gridRowDataStart=[long]$c.lightGridRows
    gridRawRowData=[long]$c.lightGridRawBytes
    gridEntries=[long]$c.lightGridEntries
    gridColors=[long]$c.lightGridColors
}
$gridSizes=@{gridRowDataStart=2;gridRawRowData=1;gridEntries=4;gridColors=168}
$gridBytes=0L
$grid=@()
foreach($name in $gridNames){
    $entry=$l.leaves.$name
    $pc=$s.originalPcLeafFiles.$name
    $file=[string]$entry.xenonNativeEndianFile
    $pcFile=[string]$entry.sourceFile
    foreach($f in @($file,$pcFile)){
        if(-not(Test-Path -LiteralPath $f -PathType Leaf)){throw "Grid leaf source missing: $name : $f"}
    }
    $bytes=[long](Get-Item -LiteralPath $file).Length
    $pcBytes=[long](Get-Item -LiteralPath $pcFile).Length
    $want=[long]$gridCounts[$name]*[long]$gridSizes[$name]
    if($want -lt 0 -or $want -gt 300000000L -or $bytes -ne $want -or
       $pcBytes -ne $want -or
       [long]$entry.sourceRecordCount -ne [long]$gridCounts[$name] -or
       [int]$entry.sourceAndNativeStructBytes -ne [int]$gridSizes[$name] -or
       -not [bool]$entry.endianRoundTripExact -or
       -not [bool]$entry.sourceOwned -or
       [bool]$entry.pointerFieldsPresent -or
       [bool]$entry.containedXboxOraclePayload -or
       [bool]$entry.hardwareReady -or
       (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine
          [string]$entry.xenonNativeEndianSha256 -or
       (Get-FileHash -LiteralPath $pcFile -Algorithm SHA256).Hash -ine
          [string]$entry.sourceSha256 -or
       [string]$pc.sha256 -ine [string]$entry.sourceSha256){
        throw "Original-PC and native Xenon grid type/source SHA mismatch: $name"
    }
    $gridBytes += $bytes
    $grid += [ordered]@{
        leaf=$name; records=[long]$gridCounts[$name]
        recordSize=[int]$gridSizes[$name]; endianVerified=$true
        sourceFileSha256=[string]$entry.sourceSha256
        nativeFileSha256=[string]$entry.xenonNativeEndianSha256
        nativeBytes=$bytes; loadedNativeValueReadbackProven=$false
    }
    Write-Host "[AZTEC-GATE11-TWO-WAVE] GRID $name records=$($gridCounts[$name]) bytes=$bytes PC->Xenon typed source verified"
}
# T4Xenon x86 pointer layout is measured, not derived from PC offsets.
if([int]$a.lightGridBytes -ne 56 -or
   [int]$a.gridRowDataStartAt -ne 28 -or
   [int]$a.gridRawRowDataAt -ne 36 -or
   [int]$a.gridEntriesAt -ne 44 -or
   [int]$a.gridColorsAt -ne 52 -or
   [int]$a.lightGridAt -lt 0 -or [int]$a.lightGridAt -gt 844){
    throw 'Native 56B embedded GfxLightGrid ABI layout changed; do not guess scalar/child pointer offsets.'
}
$shape=$w.sourceGridShape
if([int]$shape.rowAxis -lt 0 -or [int]$shape.rowAxis -gt 2 -or
   [int]$shape.colAxis -lt 0 -or [int]$shape.colAxis -gt 2 -or
   [int]$shape.colAxis -eq [int]$shape.rowAxis -or
   @($shape.mins).Count -ne 3 -or @($shape.maxs).Count -ne 3){
    throw 'Original PC GfxLightGrid embedded scalar shape not valid.'
}
$axis=[int]$shape.rowAxis
if(([int]$shape.maxs[$axis]-[int]$shape.mins[$axis]+1) -ne [int]$c.lightGridRows){
    throw 'Original PC grid row count does not match embedded row-axis bounds.'
}

Write-Host '[AZTEC-GATE11-PROGRESS] [############--------] 60% | reflection/lightmap bindings and primary-light shadow child graph'
$rows=@($b.imageReferenceRows)
$imgRoles=@($rows|Where-Object {
    [string]$_.sourceField -match '^GfxWorld\.(reflectionProbes\[|lightmaps\[)'
})
$refRows=@($w.reflectionProbeRefs)
$lmRows=@($w.lightmapRefs)
if($refRows.Count -ne [int]$c.reflectionProbes -or
   $lmRows.Count -ne [int]$c.lightmaps -or
   $imgRoles.Count -ne ($refRows.Count+2*$lmRows.Count)){
    throw 'Original PC reflection/lightmap typed references or image audit cardinality changed.'
}
$missing=@($imgRoles|Where-Object {
    [string]$_.resolution -eq 'NO_NATIVE_IMAGE_ROOT_EXACT_NAME' -or
    [string]$_.resolution -eq 'AMBIGUOUS_NATIVE_IMAGE_NAME'
})
$named=@($imgRoles|Where-Object {
    [string]$_.resolution -eq 'UNIQUE_NATIVE_IMAGE_ROOT_IDENTITY_CANDIDATE_ONLY'
})
$nulls=@($imgRoles|Where-Object {[string]$_.resolution -eq 'SOURCE_NULL'})
if(($missing.Count+$named.Count+$nulls.Count) -ne $imgRoles.Count -or
   @($imgRoles|Where-Object {$_.payloadSemanticsVerified -or $_.xboxImagePayloadDonorCopied}).Count -ne 0){
    throw 'Image naming provenance has unknown resolution or falsely claims pixel payload equivalence.'
}
for($i=0;$i -lt $refRows.Count;$i++){
    if([int]$refRows[$i].index -ne $i){throw "Original PC reflection probe ordinal $i drifted"}
}
for($i=0;$i -lt $lmRows.Count;$i++){
    if([int]$lmRows[$i].index -ne $i){throw "Original PC lightmap ordinal $i drifted"}
}
Write-Host "[AZTEC-GATE11-TWO-WAVE] REFLECTION/LIGHTMAP image fields=$($imgRoles.Count), exact-name-only=$($named.Count), null=$($nulls.Count), missing/ambiguous=$($missing.Count)"
Write-Host '[AZTEC-GATE11-TWO-WAVE] A highmip/mip file is texture-storage data, NOT proof of a GfxImage lightmap binding or native pixel equivalence.'

$lights=[int]$c.primaryLights
$shadows=@($w.shadowGeometry)
$shadowRoot=[bool]$c.shadowGeomRootPresent
if($lights -lt 0 -or $lights -gt 4096 -or
   ($shadowRoot -and $shadows.Count -ne $lights) -or
   (-not $shadowRoot -and $shadows.Count -ne 0) -or
   [int]$a.shadowGeometryBytes -ne 12 -or
   [int]$a.shadowSortedAt -ne 4 -or
   [int]$a.shadowModelsAt -ne 8 -or
   [int]$a.shadowGeomAt -lt 0 -or [int]$a.shadowGeomAt -ge 900){
    throw 'Native 12B shadow structs or original PC primary-light geometry root changed.'
}
$shadowSurfaceIndices=0L;$shadowModelIndices=0L
for($i=0;$i -lt $shadows.Count;$i++){
    $v=$shadows[$i]
    if([int]$v.index -ne $i -or
       [long]$v.surfaceCount -ne @($v.sortedSurfIndex).Count -or
       [long]$v.smodelCount -ne @($v.smodelIndex).Count -or
       [long]$v.surfaceCount -gt 735 -or
       [long]$v.smodelCount -gt 1903){
        throw "Original-PC shadow geometry light $i count/ordinal drift"
    }
    foreach($id in @($v.sortedSurfIndex)){
        if([long]$id -lt 0 -or [long]$id -ge 735){throw "Light $i invalid shadow surface index $id"}
    }
    foreach($id in @($v.smodelIndex)){
        if([long]$id -lt 0 -or [long]$id -ge 1903){throw "Light $i invalid shadow model index $id"}
    }
    $shadowSurfaceIndices += [long]$v.surfaceCount
    $shadowModelIndices += [long]$v.smodelCount
}
Write-Host "[AZTEC-GATE11-TWO-WAVE] SHADOW primaryLights=$lights, headers=$($shadows.Count), surfaceRefs=$shadowSurfaceIndices, modelRefs=$shadowModelIndices"

Write-Host '[AZTEC-GATE11-PROGRESS] [################----] 80% | nested light-region graph and Xbox-only stream ABI, same scan'
$regions=@($w.lightRegions)
$regionRoot=[bool]$c.regionRootPresent
if(($regionRoot -and $regions.Count -ne $lights) -or
   (-not $regionRoot -and $regions.Count -ne 0) -or
   [int]$a.lightRegionBytes -ne 8 -or
   [int]$a.lightRegionAxisBytes -ne 20 -or
   [int]$a.lightRegionHullBytes -ne 80 -or
   [int]$a.streamInfoBytes -ne 16 -or
   [int]$a.worldStreamInfoAt -lt 0 -or
   [int]$a.worldStreamInfoAt -gt 884){
    throw 'Native nested light region / streamInfo ABI changed; no reconstruction permitted.'
}
$hulls=0L;$axes=0L
for($i=0;$i -lt $regions.Count;$i++){
    $r=$regions[$i]
    $rh=@($r.hulls)
    if([int]$r.index -ne $i -or
       [long]$r.hullCount -ne $rh.Count -or $rh.Count -gt 4096){
        throw "Light-region primaryLight=$i hull graph mismatch"
    }
    foreach($h in $rh){
        $ar=@($h.axis)
        if([long]$h.axisCount -ne $ar.Count -or $ar.Count -gt 128 -or
           @($h.kdopMidPoint).Count -ne 9 -or @($h.kdopHalfSize).Count -ne 9){
            throw "Light-region primaryLight=$i hull nested axis graph mismatch"
        }
        $hulls++;$axes += $ar.Count
    }
}
$structStream=[ordered]@{
    nativeGfxWorldRootBytes=900
    nativeStreamInfoBytes=[int]$a.streamInfoBytes
    nativeStreamInfoOffset=[int]$a.worldStreamInfoAt
    nativeOnlySourceRequired=$true
    nextRule='Reconstruct AABB streaming tree metadata and leaf references from converted PC geometry/topology, verify typed native OAT readback'
    originalXboxReferenceBytesUsed=$false
    readyToEmit=$false
}
$waveA=[ordered]@{
    name='WAVE_A_LIGHT_GRID_REFLECTION_LIGHTMAP_SHADOW'
    sourceTypedGridLeafFamilies=4
    originalPcGridNativeEndianBytes=$gridBytes
    lightGrid=$grid
    originalPcGridRowAxis=[int]$shape.rowAxis
    originalPcGridColAxis=[int]$shape.colAxis
    originalPcGridMins=@($shape.mins)
    originalPcGridMaxs=@($shape.maxs)
    nativeEmbeddedGfxLightGridBytes=[int]$a.lightGridBytes
    nativeEmbeddedGridRootOffset=[int]$a.lightGridAt
    reflectionProbeRecords=$refRows.Count
    lightmapRecords=$lmRows.Count
    lightmapAndReflectionImageReferenceFields=$imgRoles.Count
    sourceNullImageFields=$nulls.Count
    exactNameIdentityOnlyImageFields=$named.Count
    missingOrAmbiguousImageNames=$missing.Count
    imagePayloadProvenanceOrEquivalentOwnedGfxImageRootsReady=$false
    imageBindingsMayBeRebasedInOutputFf=$false
    shadowRootPresent=$shadowRoot
    primaryLights=$lights
    typedShadowHeaders=$shadows.Count
    originalPcShadowSurfaceIndices=$shadowSurfaceIndices
    originalPcShadowModelIndices=$shadowModelIndices
    allSourceAndNativeLayoutEvidenceComplete=$true
    dependenciesForNewFf=@('exact GfxImage content/identity provenance per non-null reflection and lightmap field',
      'source-owned typed grid scalar/child serialization with correct native block order',
      'source-owned nested shadow root/indices and per-light streaming order',
      'independent OAT loaded-value witness of all new fields')
    emittedOrIndependentlyOatVerified=$false
}
$waveB=[ordered]@{
    name='WAVE_B_LIGHT_REGIONS_AND_XENON_STREAM_INFO'
    primaryLights=$lights
    lightRegionRootPresent=$regionRoot
    lightRegionHeaders=$regions.Count
    lightRegionHulls=$hulls
    lightRegionAxes=$axes
    xenonStreamInfo=$structStream
    dependenciesForNewFf=@('rebuild all source-owned hull/axis nested pointer fields',
       'derive Xbox-only stream aabbTrees/leafRefs from proven map geometry rather than copy donor bytes',
       'independent OAT loaded-value witness for regions and reconstructed stream counts')
    emittedOrIndependentlyOatVerified=$false
}
$summary=[ordered]@{
    schema='WaW360.AztecGate11TwoWaveJointLightingWorldClosure.v1'
    status='AZTEC_GATE11_TWO_WAVE_ALL_LANES_SOURCE_AND_NATIVE_ABI_AUDITED_NEXT_FF_NOT_EMITTED'
    sourceOfTruth='original PC typed GfxWorld source + SHA-verified native-endian leaves + measured T4Xenon struct offsets'
    previousCellWorldSha256=$expectedCellSha
    protectedCurrentPrimitiveWorldSha256=$expectedPrimitiveSha
    protectedCurrentPrimitiveWorldBytes=3650170
    nativeRefImageRootIdentitiesOnly=$true
    waveA=$waveA
    waveB=$waveB
    proposedSingleCombinedFfCandidate='pc_g11_lighting_reflection_shadow_region_stream_batch'
    maxNewFfBuildsWhenAllPrerequisitesSatisfied=1
    sourceProofAndPointerLayoutMayProceedTogether=$true
    permitPartialBranchesIfMissingImagePayload=$false
    worldSourceOwnedAssetsOnly=$true
    xboxReferenceUsedForNativeAbiOnly=$true
    nativeImageRootBindingsVerified=$false
    nativeStreamingMetadataReconstructed=$false
    newMapFastFileProduced=$false
    fullGate11Passed=$false
    xboxHardwareReady=$false
}
New-Item -ItemType Directory -Path $outDir -Force|Out-Null
$summary|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $report -Encoding UTF8
if((Get-FileHash -LiteralPath $baseline -Algorithm SHA256).Hash -ine $expectedCellSha -or
   (Get-FileHash -LiteralPath $proven -Algorithm SHA256).Hash -ine $expectedPrimitiveSha){
    throw 'CRITICAL: protected cell/primitive FF changed during source-only two-wave planning.'
}
Write-Host '[AZTEC-GATE11-PROGRESS] [####################] 100% | BOTH wave source/layout proofs examined together; no FF rebuild'
Write-Host "[AZTEC-GATE11-TWO-WAVE] Wave A grid native bytes=$gridBytes; reflection=$($waveA.reflectionProbeRecords), lightmaps=$($waveA.lightmapRecords), shadow headers=$($waveA.typedShadowHeaders)"
Write-Host "[AZTEC-GATE11-TWO-WAVE] Wave B regions=$($waveB.lightRegionHeaders), hulls=$hulls, axes=$axes, stream header=$($a.streamInfoBytes) native ABI bytes"
Write-Host "[AZTEC-GATE11-TWO-WAVE] Report=$report"
Write-Host '[AZTEC-GATE11-TWO-WAVE] Source layout ready for joint engineering, but image payload and Xenon streaming reconstruction still block native FF emission.'
Write-Host '[AZTEC-GATE11-TWO-WAVE] Gate11 overall=10/13 PENDING; no new FF, no Xbox testing.'
