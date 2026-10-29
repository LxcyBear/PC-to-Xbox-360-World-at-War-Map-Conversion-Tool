[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# These are explicit milestone gates, NOT equal-work estimates.
# Source discovery is easy to verify; the last world/runtime gates
# require independent emitter/OAT/hardware evidence before a PASS.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$gates=New-Object 'System.Collections.Generic.List[object]'
function Read-Report([string]$relativePath){
    $path=Join-Path $base $relativePath
    if(-not (Test-Path -LiteralPath $path -PathType Leaf)){return $null}
    try{return (ConvertFrom-Json -InputObject (Get-Content -LiteralPath $path -Raw))}
    catch{return $null}
}
function Add-Gate([string]$name,[bool]$passed,[string]$evidence){
    $gates.Add([pscustomobject]@{Name=$name;Passed=$passed;Evidence=$evidence})
}
$six=Read-Report 'pc_six_family_comworld_fullgraph_integration\aztec_pc_six_family_comworld_graph.json'
$indices=Read-Report 'pc_gfxworld_index_source_proof\aztec_pc_gfxworld_index_source_proof.json'
$verts=Read-Report 'pc_gfxworld_vertex_source_probe\aztec_pc_gfxworld_vertex_source_probe.json'
$surfaces=Read-Report 'pc_gfxworld_surface_source_probe\aztec_pc_gfxworld_surface_source_probe.json'
$mesh=Read-Report 'pc_gfxworld_source_mesh_export\aztec_pc_gfxworld_obj_export.json'
$converted=Read-Report 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$pcMaterialWitness=Read-Report 'pc_oat_loaded_gfxworld_material_pointer_witness\pc_oat_gfxworld_material_pointer_reconciliation.json'
$pcSurface76=Read-Report 'pc_xenon_76byte_gfxsurface_canary\aztec_735_gfxsurfaces_76byte_source_canary.json'
$pcWorldMaterialCorpus=Read-Report 'pc_39_world_material_corpus_source\aztec_pc_39_world_material_corpus_summary.json'
$pcXenonMaterialCandidates=Read-Report 'pc_to_xenon_39_world_material_graph_candidates\aztec_39_pc_to_xenon_material_graph_candidates.json'
$pc39CodecPreflight=Read-Report 'pc_xenon_39_material_existing_graph_codec_preflight\aztec_39_material_existing_codec_preflight.json'
$pc39WorldFfScratch=Read-Report 'pc_39_world_material_real_ff_scratch_integration\aztec_39_source_world_material_scratch_ff_gate.json'
$pc735LiveWorldLink=Read-Report 'pc_xenon_735_world_surface_live_39_material_ff_link\aztec_735_world_surfaces_linked_to_39_ff_material_roots.json'
$pcGfxWorldRootAbi=Read-Report 'xenon_native_gfxworld_root_dpvs_abi\native_xenon_gfxworld_root_dpvs_abi.json'
$pcGfxWorldRootSeed=Read-Report 'pc_source_to_xenon_gfxworld_root_dpvs_scalar_seed\aztec_pc_gfxworld_800_to_xenon_900_root_dpvs_seed.json'
$pcIntegratedWorldCore=Read-Report 'pc_735_39_gfxworld_integrated_ff_oat_run\aztec_pc_world_core_integrated_oat_run.json'
$nativeTestReleaseReport=Join-Path $repo 'output\native_smoke_test_release\nazi_zombie_aztec\native_smoke_test_package_release.json'
$nativeTestRelease=$null
if(Test-Path -LiteralPath $nativeTestReleaseReport -PathType Leaf){
    try{$nativeTestRelease=Get-Content -LiteralPath $nativeTestReleaseReport -Raw | ConvertFrom-Json}
    catch{$nativeTestRelease=$null}
}
$nativeReady=$false
$realMainWorldOatReady=$false
$pairedFastFilesOatReady=$false
if($null -ne $nativeTestRelease -and
   [string]$nativeTestRelease.schema -eq 'WaW360.AztecNativeSmokeTestReleaseGate.v1' -and
   @($nativeTestRelease.finalFastfiles).Count -eq 2){
    $main=$nativeTestRelease.finalFastfiles | Where-Object {
        [string]$_.filename -eq 'nazi_zombie_aztec.ff'
    } | Select-Object -First 1
    $load=$nativeTestRelease.finalFastfiles | Where-Object {
        [string]$_.filename -eq 'nazi_zombie_aztec_load.ff'
    } | Select-Object -First 1
    function Test-FfReleaseProof($asset){
        if($null -eq $asset -or -not [bool]$asset.oatNativeLoadPass -or
           -not(Test-Path -LiteralPath ([string]$asset.path) -PathType Leaf)){
            return $false
        }
        return ((Get-FileHash -LiteralPath ([string]$asset.path) -Algorithm SHA256).Hash -ieq [string]$asset.sha256)
    }
    $mainFfVerified=Test-FfReleaseProof $main
    $loadFfVerified=Test-FfReleaseProof $load
    $realMainWorldOatReady=$mainFfVerified -and
        [bool]$nativeTestRelease.realMainGfxWorldAnd735MaterialReferencesOatPass
    $pairedFastFilesOatReady=$realMainWorldOatReady -and $loadFfVerified -and
        [bool]$nativeTestRelease.bothFinalFastfilesOatPass
    $nativeReady=$pairedFastFilesOatReady -and
        [string]$nativeTestRelease.status -eq 'HARDWARE_TEST_READY' -and
        [bool]$nativeTestRelease.completeCodxePackage -and
        [bool]$nativeTestRelease.readyForFirstControlledHardwareSmokeTest -and
        -not [bool]$nativeTestRelease.runtimeGameplayGuaranteed
}

$sixOk=$null -ne $six -and
    [string]$six.status -eq 'AZTEC_PC_SIX_FAMILY_COMWORLD_GRAPH_PASS' -and
    [int]$six.nativeRoots -eq 8509 -and
    -not [bool]$six.readyForXbox
Add-Gate 'Original PC + 8,509-root Xbox identity pinned' $sixOk 'Six-family source identity PASS'
Add-Gate 'Six original-PC asset families in OAT-valid scratch FF' (
    $sixOk -and [bool]$six.layoutReplayExact -and
    [bool]$six.oatZeroWarningsZeroErrors -and
    [int]$six.sourcePcComWorldByteReadback -eq 1 -and
    [int]$six.sourcePCMapEntsReadbacks -eq 1) 'Six-family OAT/layout/readback PASS'
Add-Gate '32,713 PC vertices + 58,653 PC indices recovered' (
    $null -ne $verts -and $null -ne $indices -and
    [string]$verts.Status -eq 'AZTEC_PC_GFXWORLD_32713_VERTEX_ARRAY_CANDIDATE_PASS' -and
    [string]$indices.status -eq 'AZTEC_PC_GFXWORLD_58653_INDEX_ARRAY_CANDIDATE_PASS' -and
    [int]$verts.SourcePCVertexCount -eq 32713 -and
    [int]$indices.originalPcIndexCount -eq 58653) 'Vertex and index source reports'
Add-Gate 'All 735 surfaces; 19,551 triangles and local index coverage' (
    $null -ne $surfaces -and
    [string]$surfaces.Status -eq 'AZTEC_PC_GFXWORLD_735_SURFACE_ARRAY_CANDIDATE_PASS' -and
    @($surfaces.TopCandidates | Where-Object {
        [int]$_.SourceOffset -eq 0x058E361E -and
        [int]$_.UncoveredIndexEntries -eq 0 -and
        [int]$_.IndexSpanOverlapEntries -eq 0 -and
        [int]$_.LocalIndicesInRange -eq 58653 -and
        [long]$_.TotalTriangleCount -eq 19551
    }).Count -eq 1) '52-byte GfxSurface full index-ownership PASS'
Add-Gate 'Full original-PC Aztec geometry exported to verified OBJ' (
    $null -ne $mesh -and
    [string]$mesh.Status -eq 'AZTEC_PC_GFXWORLD_19551_TRIANGLE_OBJ_EXPORT_PASS' -and
    [int]$mesh.TriangleCount -eq 19551 -and
    [int]$mesh.ReferencedVertexCount -eq 32713 -and
    [int]$mesh.DistinctPackedMaterialPointers -eq 39 -and
    [string]$mesh.ObjSha256 -eq 'D00AC821649A44BF552D7C61C173D7C25001F4F7DF2235CACDA97C382687A8EC') 'Source mesh file and manifest PASS'

# Gate 6 deliberately requires the successful PowerShell SHA/ledger
# audit marker, not merely the .NET JSON manifest. The first attempt
# produced valid bytes but failed the PS5.1 JSON-array gate.
$marker=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.txt'
$markerOk=Test-Path -LiteralPath $marker -PathType Leaf
if($markerOk){
    $markerOk=(Select-String -LiteralPath $marker -SimpleMatch 'status=AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -Quiet)
}
$convertedOk=$null -ne $converted -and
    [string]$converted.Status -eq 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -and
    [int]$converted.IndexCount -eq 58653 -and
    [int]$converted.VertexCount -eq 32713 -and
    [int]$converted.SurfaceCount -eq 735 -and
    [int]$converted.DistinctPcMaterialPointers -eq 39 -and
    [int]$converted.SurfacesRequiringMaterialBinding -eq 735 -and
    -not [bool]$converted.XenonGfxWorldRootEmitted -and
    -not [bool]$converted.OatGfxWorldBodyLoaded -and
    -not [bool]$converted.XboxHardwareReady
Add-Gate 'PC geometry -> endian-converted source-layout 3-buffer readback' (
    $convertedOk -and $markerOk) 'Three-buffer SHA/735-slot ledger runner PASS'

# Gate 7 is now a scratch-level proof: source-owned geometry, 735 material
# pointers to 39 actual Xenon Material bodies, and the real 900-byte world
# core all load under OAT with the exact 8,509-root sequence. This is NOT
# proof of complete DPVS runtime children or a hardware-testable FF.
$integratedCorePass=$null -ne $pcIntegratedWorldCore -and
    [string]$pcIntegratedWorldCore.schema -eq 'WaW360.AztecPcGfxWorldIntegratedFastFileOatRun.v1' -and
    [string]$pcIntegratedWorldCore.status -eq 'AZTEC_ORIGINAL_PC_GFXWORLD_CORE_IN_REAL_FF_OAT_LOAD_PASS_REMAINING_CHILDREN' -and
    [string]$pcIntegratedWorldCore.probeLevel -eq 'Full' -and
    [bool]$pcIntegratedWorldCore.realOriginalPcGfxWorldCoreEmitted -and
    [bool]$pcIntegratedWorldCore.finalMaterialReferencesRebasedDuringCurrentFfEmit -and
    [int]$pcIntegratedWorldCore.realPcGfxSurfaceCount -eq 735 -and
    [int]$pcIntegratedWorldCore.realPcGfxWorldMaterialRoots -eq 39 -and
    [bool]$pcIntegratedWorldCore.OatPass -and
    -not [bool]$pcIntegratedWorldCore.allOriginalPcGfxWorldDpvsChildrenComplete -and
    -not [bool]$pcIntegratedWorldCore.fullNativeWorldSourceClosurePass -and
    -not [bool]$pcIntegratedWorldCore.hardwareReady -and
    (Test-Path -LiteralPath ([string]$pcIntegratedWorldCore.outputFastFile) -PathType Leaf)
Add-Gate 'Bind all 735 GfxSurface material refs to Xbox roots (OAT scratch)' (
    $integratedCorePass -or $realMainWorldOatReady) 'Original-PC 735/39 real GfxWorld FF OAT core PASS (not full DPVS)'
# Keep the later gates fail-closed until full required world child closure,
# both native main/_load artifacts, and the independent release evidence.
Add-Gate 'Emit real GfxWorld root, DPVS and required children' $realMainWorldOatReady 'Full world/DPVS closure verified in final main FF'
Add-Gate 'Integrated FF OAT pass + required runtime dependencies' $pairedFastFilesOatReady 'Both final paired FFs individually passed T4Xenon OAT 0 warnings/errors'
Add-Gate 'Controlled Xbox smoke-test FF approved for deployment' $nativeReady 'Complete CoD Xe package plus two final OAT PASS FFs: START native Xbox test'

$passed=@($gates.ToArray() | Where-Object {$_.Passed}).Count
$total=$gates.Count
$percent=[int][Math]::Round(100.0*$passed/$total)
$segments=20
$filled=[int]($segments*$passed/$total)
$bar=('='*$filled)+('-'*($segments-$filled))
Write-Host '[AZTEC-FF-PROGRESS] ==================================================='
Write-Host "[AZTEC-FF-PROGRESS] FIRST CONTROLLED XBOX TEST: [$bar] $percent% ($passed/$total milestones)"
Write-Host '[AZTEC-FF-PROGRESS] Milestones count VERIFIED GATES, not time/effort or percentage of full conversion.'
for($i=0;$i -lt $gates.Count;$i++){
    $g=$gates[$i]
    $status=if($g.Passed){'PASS   '}else{'PENDING'}
    Write-Host ('[AZTEC-FF-PROGRESS] {0,2}. {1} | {2}' -f ($i+1),$status,$g.Name)
}
$pcWitnessPassed=$null -ne $pcMaterialWitness -and
    [string]$pcMaterialWitness.status -eq 'AZTEC_PC_OAT_LOADER_735_TYPED_MATERIAL_POINTER_IDENTITIES_MATCH_ORDINAL_CANDIDATES' -and
    [int]$pcMaterialWitness.loadedDirectPointerMatches -eq 735 -and
    [int]$pcMaterialWitness.independentlyVerifiedOrdinalAndNameSurfaces -eq 735 -and
    [int]$pcMaterialWitness.memoryRowsWithExactSourceCandidateName -eq 39 -and
    [int]$pcMaterialWitness.xboxRootBindingsProven -eq 0 -and
    -not [bool]$pcMaterialWitness.xboxHardwareReady
if($pcWitnessPassed){
    Write-Host '[AZTEC-FF-PROGRESS] PC MATERIAL IDENTITIES: 735/735 INDEPENDENT OAT TYPED POINTER PASS; standalone 76B source-derived surfaces PASS, but no valid serialized Xbox Material graph/FF relocations.'
}
$pc76Proof=$null -ne $pcSurface76 -and
    [string]$pcSurface76.Schema -eq 'WaW360.AztecPcXenonGfxSurface76Canary.v1' -and
    [int]$pcSurface76.Surfaces -eq 735 -and
    [int]$pcSurface76.SurfaceStride -eq 76 -and
    [int]$pcSurface76.MaterialOffset -eq 44 -and
    [int]$pcSurface76.WrittenMaterialPointerFields -eq 0 -and
    -not [bool]$pcSurface76.XenonGfxWorldSerialized -and
    -not [bool]$pcSurface76.OatIntegratedFastFileValidated -and
    -not [bool]$pcSurface76.XboxHardwareReady
if($pc76Proof){
    Write-Host "[AZTEC-FF-PROGRESS] REAL XBOX 76B SURFACE CANARY: 735 source-based structural records, topMip source-verified=$($pcSurface76.SourceDerivedNativeExactTopMipSurfaces)/735, null Material* fields=735; NOT A GFXWORLD FF."
}
$pc39CorpusPass=$null -ne $pcWorldMaterialCorpus -and
    [string]$pcWorldMaterialCorpus.status -eq 'AZTEC_PC_OAT_39_MATERIAL_HEADERS_AND_CHILD_TABLES_SOURCE_EXPORTED' -and
    [int]$pcWorldMaterialCorpus.typedWorldMaterialOwners -eq 39 -and
    [int]$pcWorldMaterialCorpus.sourceHeaders -eq 39 -and
    [int]$pcWorldMaterialCorpus.realXenonWorldMaterialGraphBodies -eq 0 -and
    -not [bool]$pcWorldMaterialCorpus.hardwareReady
if($pc39CorpusPass){
    Write-Host "[AZTEC-FF-PROGRESS] PC MATERIAL GRAPH SOURCE STAGE: 39/39 original PC headers/child tables extracted (historical source proof; actual FF integration shown below)."
}
$pc39XenonCandidatesPass=$null -ne $pcXenonMaterialCandidates -and
    [string]$pcXenonMaterialCandidates.Status -eq 'AZTEC_39_PC_MATERIALS_TO_XENON_96B_SOURCE_GRAPH_CANDIDATES_ONLY' -and
    [int]$pcXenonMaterialCandidates.SourceMaterialChildrenConverted -eq 39 -and
    [int]$pcXenonMaterialCandidates.CoveredWorldSurfaces -eq 735 -and
    [int]$pcXenonMaterialCandidates.TextureDescriptors -eq 87 -and
    [int]$pcXenonMaterialCandidates.ConstantRecords -eq 93 -and
    [int]$pcXenonMaterialCandidates.StateBitsRecords -eq 192 -and
    -not [bool]$pcXenonMaterialCandidates.RealXenonMaterialRootsEmitted -and
    -not [bool]$pcXenonMaterialCandidates.IntegratedOatFfValidated -and
    -not [bool]$pcXenonMaterialCandidates.XboxHardwareReady
if($pc39XenonCandidatesPass){
    Write-Host "[AZTEC-FF-PROGRESS] MATERIAL CONVERSION STAGE: 39/39 PC -> 96B Xenon candidates. Initial exact-name heuristic misses=$($pcXenonMaterialCandidates.MissingNativeDependencies); authoritative codec check appears below."
}
$pc39CodecPass=$null -ne $pc39CodecPreflight -and
    [string]$pc39CodecPreflight.Status -eq 'AZTEC_39_SOURCE_XENON_MATERIAL_GRAPHS_EXISTING_CODEC_LOAD_PASS_DEPENDENCY_AUDIT' -and
    [int]$pc39CodecPreflight.MaterialGraphsLoadedByExistingCodec -eq 39 -and
    -not [bool]$pc39CodecPreflight.RealXboxWorldMaterialBodiesEmitted -and
    -not [bool]$pc39CodecPreflight.IntegratedOatFastFileValidated -and
    -not [bool]$pc39CodecPreflight.XboxHardwareReady
if($pc39CodecPass){
    Write-Host "[AZTEC-FF-PROGRESS] SOURCE GRAPH WRITER CONTRACT: 39/39 loaded by existing Xenon Material codec, $($pc39CodecPreflight.MaterialGraphsPlannedByExistingCodec)/39 allocator plans; missing deps=$($pc39CodecPreflight.MissingNativeDependencyOccurrences), FF gates still PENDING."
}
$pc39WorldFfPass=$null -ne $pc39WorldFfScratch -and
    [string]$pc39WorldFfScratch.status -eq 'AZTEC_PC_39_REAL_WORLD_MATERIAL_ROOTS_IN_SCRATCH_XENON_FF_OAT_PASS' -and
    [int]$pc39WorldFfScratch.realSourceWorldMaterialRoots -eq 39 -and
    [int]$pc39WorldFfScratch.nativeWorldGfxSurfaceReferencesInFf -eq 0 -and
    [bool]$pc39WorldFfScratch.oatLoaderPass -and
    [bool]$pc39WorldFfScratch.oatZeroWarningsZeroErrors -and
    [bool]$pc39WorldFfScratch.layoutReplayExact -and
    [bool]$pc39WorldFfScratch.xboxNativeRootIdentityExact -and
    -not [bool]$pc39WorldFfScratch.xenonGfxWorldRootEmitted -and
    -not [bool]$pc39WorldFfScratch.hardwareReady
if($pc39WorldFfPass){
    Write-Host '[AZTEC-FF-PROGRESS] HISTORICAL 39-MATERIAL BASELINE: OAT PASS in scratch FF before direct world graft (the integrated-world result is reported separately below).'
}
$pc735LiveLinkPass=$null -ne $pc735LiveWorldLink -and
    [string]$pc735LiveWorldLink.Status -eq 'AZTEC_735_SURFACE_MATERIAL_REFS_TO_39_REAL_XBOX_FF_MATERIAL_ROOTS_REBASED_STANDALONE_PASS' -and
    [int]$pc735LiveWorldLink.SourceOwnedXboxSurfaceCount -eq 735 -and
    [int]$pc735LiveWorldLink.MaterialRootsWithRealFfGraphBody -eq 39 -and
    [int]$pc735LiveWorldLink.SurfacesLinkedToRealFfBodies -eq 735 -and
    [int]$pc735LiveWorldLink.SurfacesLinkedToIdentityShells -eq 0 -and
    -not [bool]$pc735LiveWorldLink.WorldGfxSurfaceArrayInsertedInFf -and
    -not [bool]$pc735LiveWorldLink.OatValidatedGfxWorldLoad -and
    -not [bool]$pc735LiveWorldLink.XboxHardwareReady
if($pc735LiveLinkPass){
    Write-Host '[AZTEC-FF-PROGRESS] HISTORICAL GFXSURFACE HANDOFF: standalone 735/735 +44 pointers to 39 material owners; direct world FF integration is reported below.'
}
$rootAbiPass=$null -ne $pcGfxWorldRootAbi -and
    [string]$pcGfxWorldRootAbi.status -eq 'AZTEC_NATIVE_XBOX_GFXWORLD_ROOT_AND_DPVS_ABI_EXACT_SOURCE_WITNESS' -and
    [int]$pcGfxWorldRootAbi.pcExpectedSurfaceCount -eq 735 -and
    [int]$pcGfxWorldRootAbi.pcExpectedMaterialMemoryCount -eq 39 -and
    -not [bool]$pcGfxWorldRootAbi.sourceGfxWorldRootSerialized -and
    -not [bool]$pcGfxWorldRootAbi.sourceDpvsStaticSerialized -and
    -not [bool]$pcGfxWorldRootAbi.hardwareReady
if($rootAbiPass){
    Write-Host "[AZTEC-FF-PROGRESS] XENON GFXWORLD ROOT+DPVS ABI WITNESS: native root $($pcGfxWorldRootAbi.nativeGfxWorldRootAbi.gfxWorldSize) bytes, dpvsStatic $($pcGfxWorldRootAbi.nativeGfxWorldRootAbi.dpvsStaticSize) bytes. PC world still not emitted."
}
$pcRootSeedPass=$null -ne $pcGfxWorldRootSeed -and
    [string]$pcGfxWorldRootSeed.Status -eq 'AZTEC_PC_GFXWORLD_800_TO_XBOX_900_ROOT_SCALAR_AND_DPVS_SEED_PASS_NOT_FF' -and
    [int]$pcGfxWorldRootSeed.XboxRootSize -eq 900 -and
    [int]$pcGfxWorldRootSeed.OriginalPcRootSize -eq 800 -and
    @($pcGfxWorldRootSeed.SourceVerifiedRootFields).Count -eq 3 -and
    @($pcGfxWorldRootSeed.SourceVerifiedDpvsFields).Count -eq 10 -and
    -not [bool]$pcGfxWorldRootSeed.FullWorldChildrenSerialized -and
    -not [bool]$pcGfxWorldRootSeed.OatGfxWorldLoadValidated -and
    -not [bool]$pcGfxWorldRootSeed.XboxHardwareReady
if($pcRootSeedPass){
    Write-Host '[AZTEC-FF-PROGRESS] HISTORICAL GFXWORLD ROOT SEED: 800B PC -> 900B Xbox scalars + 100B DPVS counters; full world child closure remains incomplete.'
}
if($null -ne $pcIntegratedWorldCore -and
   [string]$pcIntegratedWorldCore.schema -eq 'WaW360.AztecPcGfxWorldIntegratedFastFileOatRun.v1'){
    Write-Host "[AZTEC-FF-PROGRESS] FIRST REAL GFXWORLD FF EMIT: core=$($pcIntegratedWorldCore.realOriginalPcGfxWorldCoreEmitted), OAT_PASS=$($pcIntegratedWorldCore.OatPass), 735 source surfaces/39 Material roots; full DPVS=$($pcIntegratedWorldCore.allOriginalPcGfxWorldDpvsChildrenComplete), paired _load still pending."
    if($integratedCorePass){
        Write-Host '[AZTEC-FF-PROGRESS] VERIFIED: 735/735 source-owned Xbox GfxSurface material refs are now in the OAT-loaded 8509-root GfxWorld CORE. This advances milestone 7 ONLY. Required DPVS/cell/lightmap closure is NOT done.'
    }
}
Write-Host '[AZTEC-FF-PROGRESS] The existing 8,509-root FF remains diagnostic-only.'
Write-Host '[AZTEC-FF-PROGRESS] No Xbox FF release until all hardware-gating checks pass.'
Write-Host '[AZTEC-FF-PROGRESS] ==================================================='
Write-Progress -Activity 'Aztec first controlled Xbox FF milestone gates' -Completed
