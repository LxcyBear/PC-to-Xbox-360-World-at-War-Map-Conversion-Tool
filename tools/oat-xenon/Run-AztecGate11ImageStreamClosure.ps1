[CmdletBinding()]
param([switch]$BuildDumper)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# One READ-ONLY proof pass for BOTH remaining native GfxWorld dependencies:
#   6 reflection/lightmap GfxImage pointer slots and true native streamInfo.
# Compare named original-PC source image candidates to the TWO genuine
# independently verified PC-owned images. Inspect the currently proven
# 4.51MB FF and retail native FF only for loader-typed STRUCTURAL
# streaming count/bounds invariants, NEVER Xbox streaming payload bytes.
# No FF rebuild, no Xbox source donor, no fake texture name equivalence.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$root=Join-Path $base 'pc_g11_lighting_combined_probe'
$proofPath=Join-Path $root 'oat_loaded_lighting_shadow_region\aztec_gate11_independent_lighting_shadow_source_digest.json'
$twoWavePath=Join-Path $base 'pc_g11_two_wave_world_closure\aztec_gate11_two_wave_source_dependency_plan.json'
$bindingPath=Join-Path $base 'pc_g11_cells_740\gate11_remaining_unified_source\aztec_gate11_exact_asset_reference_audit.json'
$pcFastfileImages=Join-Path $base 'pc_fastfile_image_sources\pc_fastfile_image_sources.json'
$pcImageRoundTrip=Join-Path $base 'pc_iwd_image_roundtrip\aztec_pc_iwd_xenon_image_roundtrip.json'
$pcImageGraph=Join-Path $base 'pc_iwd_rawfile_integration\aztec_pc_iwd_rawfile_integration.json'
$reviewPath=Join-Path $base 'pc_fastfile_image_sources\aztec_pc_image_candidate_provenance.json'
$nativeIdentity=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$nativeGraph=Join-Path $base 'native_graph_proof\native_graph_proof_result.json'
$capPath=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$builder=Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1'
$out=Join-Path $base 'pc_g11_image_stream_closure'
$report=Join-Path $out 'aztec_gate11_6_images_and_native_stream_shape.json'
$expectedFf='B832EDCE7EECFBBF2B0A141FF2C3D946595B317D7E7C7ACA0D21991277522883'
$expectedRetail='E0F3C244DD7B72D0FE71C05FAF014C2C9BFB05F770183954B3E7CDE625EBA98F'
foreach($path in @($proofPath,$twoWavePath,$bindingPath,$pcFastfileImages,
                 $pcImageRoundTrip,$pcImageGraph,$nativeIdentity,$nativeGraph,$builder)){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Gate11 image+stream joint frontier missing prior source proof: $path"
    }
}
$p=Get-Content -LiteralPath $proofPath -Raw|ConvertFrom-Json
$t=Get-Content -LiteralPath $twoWavePath -Raw|ConvertFrom-Json
$b=Get-Content -LiteralPath $bindingPath -Raw|ConvertFrom-Json
$pc=Get-Content -LiteralPath $pcFastfileImages -Raw|ConvertFrom-Json
$ip=Get-Content -LiteralPath $pcImageRoundTrip -Raw|ConvertFrom-Json
$mix=Get-Content -LiteralPath $pcImageGraph -Raw|ConvertFrom-Json
$n=Get-Content -LiteralPath $nativeIdentity -Raw|ConvertFrom-Json
$nr=Get-Content -LiteralPath $nativeGraph -Raw|ConvertFrom-Json
if([string]$p.schema -ne 'WaW360.AztecGate11IndependentOatLoadedLightingAndShadowSourceProof.v1' -or
   [string]$p.status -ne 'AZTEC_GATE11_SOURCE_LIGHTGRID_SHADOW_EMPTY_REGIONS_NATIVE_LOADED_PASS_IMAGE_STREAM_STILL_BLOCKED' -or
   [string]$p.newNativeFfSha256 -ine $expectedFf -or
   [long]$p.newNativeFfBytes -ne 4510629L -or
   [long]$p.nativeLightGridBytesVerified -ne 4822860L -or
   [long]$p.loadedShadowModelReferences -ne 1903L -or
   -not [bool]$p.preexistingCellsAabbAndModelIndicesStillExact -or
   -not [bool]$p.preexistingFourPrimitiveSourceDigestsStillExact -or
   [bool]$p.reflectionLightmapImagePayloadsProven -or
   [bool]$p.xboxStreamInfoReconstructed -or [bool]$p.hardwareReady -or
   [string]$t.schema -ne 'WaW360.AztecGate11TwoWaveJointLightingWorldClosure.v1' -or
   [string]$t.protectedCurrentPrimitiveWorldSha256 -ine [string]$p.priorPrimitiveFfSha256 -or
   [string]$b.schema -ne 'WaW360.AztecGate11OriginalPcWorldAssetExactNameBindingAudit.v1' -or
   [string]$b.status -ne 'AZTEC_GATE11_IMAGE_ROOT_IDENTITIES_CENSUSED_ONLY_NOT_NATIVE_PAYLOAD_PROOF' -or
   [int]$b.nativeImageRootCount -ne 666 -or [bool]$b.hardwareReady -or
   [string]$pc.schema -ne 'WaW360.AztecPcFastfileImageSourceCensus.v1' -or
   [string]$pc.status -ne 'AZTEC_PC_FF_IMAGE_SOURCE_DUMP_PARTIAL_RECOVERED' -or
   [int]$pc.extractedPcIwiFiles -ne 11 -or
   [bool]$pc.cachedIwiProvenanceFullyRevalidated -or
   [string]$ip.schema -ne 'WaW360.AztecPcIwdXenonImageRoundTrip.v1' -or
   [string]$ip.status -ne 'AZTEC_PC_IWD_XENON_IMAGE_ROUNDTRIP_PASS' -or
   [int]$ip.successfulXenonPixelReadbacks -ne 2 -or
   [string]$mix.schema -ne 'WaW360.AztecPcIwdRawfileCombinedGraph.v1' -or
   [string]$mix.status -ne 'AZTEC_PC_IWD_RAWFILE_COMBINED_GRAPH_PASS' -or
   [int]$mix.realPcImageRoots -ne 2 -or
   [int]$mix.pixelShaExactReadbacks -ne 2 -or
   [bool]$mix.hardwareReady -or
   [int]$n.assets -ne 8509 -or -not [bool]$n.typeSequenceVerified -or
   @($n.Entries).Count -ne 8509 -or
   [string]$nr.status -ne 'IDENTITY_AND_TYPED_GRAPH_IMPORT_PASS' -or
   [string]$nr.nativeSha256 -ine $expectedRetail){
    throw 'Prior combined FF, original-PC PC-IWD and source-image manifests or native identity proofs drifted.'
}
$ff=[string]$p.newNativeFf
$retail=[string]$nr.verifiedGoldenXboxFastFile
if([IO.Path]::GetFullPath($ff) -ieq [IO.Path]::GetFullPath($retail) -or
   -not(Test-Path -LiteralPath $ff -PathType Leaf) -or
   -not(Test-Path -LiteralPath $retail -PathType Leaf) -or
   (Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash -ine $expectedFf -or
   (Get-FileHash -LiteralPath $retail -Algorithm SHA256).Hash -ine $expectedRetail -or
   [long](Get-Item -LiteralPath $ff).Length -ne 4510629L -or
   [long](Get-Item -LiteralPath $retail).Length -ne 82739469L){
    throw 'Current combined source-only FF or retail native STRUCT oracle SHA changed.'
}

function Normalize-Name([string]$name){
    if([string]::IsNullOrWhiteSpace($name)){return ''}
    $v=$name.Trim().Trim('"').Replace('\','/')
    $part='/images/'
    $at=$v.LastIndexOf($part,[StringComparison]::OrdinalIgnoreCase)
    if($at -ge 0){$v=$v.Substring($at+$part.Length)}
    elseif($v.StartsWith('images/',[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(7)}
    foreach($ext in @('.iwi','.dds')){
        if($v.EndsWith($ext,[StringComparison]::OrdinalIgnoreCase)){
            $v=$v.Substring(0,$v.Length-$ext.Length)
        }
    }
    return $v.ToLowerInvariant()
}

Write-Host '[AZTEC-GATE11-IMG-STREAM] ==============================================================='
Write-Host '[AZTEC-GATE11-IMG-STREAM] ONE read-only source/payload provenance + Xbox streaming STRUCT architecture scan'
Write-Host '[AZTEC-GATE11-IMG-STREAM] Previous 4510629B FF SHA-locked. NO FF/PC pixel conversion, NO Xbox donor bytes.'
Write-Host '[AZTEC-GATE11-PROGRESS] [####----------------] 20% | verify six original PC image root names against actual converted images'
$refRoles=@($b.imageReferenceRows|Where-Object {
    [string]$_.sourceField -match '^GfxWorld\.(reflectionProbes\[|lightmaps\[)'
})
if($refRoles.Count -ne 6 -or
   @($refRoles|Where-Object {[string]$_.resolution -ne 'UNIQUE_NATIVE_IMAGE_ROOT_IDENTITY_CANDIDATE_ONLY'}).Count -ne 0){
    throw 'Exactly six unambiguous reflection/lightmap NAME identities are required; no payload conclusions.'
}
$nativeImages=@($n.Entries|Where-Object {[string]$_.TypeName -ieq 'image'})
if($nativeImages.Count -ne 666){throw 'Native image identity count changed.'}
$owned=@{}
foreach($row in @($ip.individualImages)){
    if(-not [bool]$row.HeaderExact -or -not [bool]$row.PixelPayloadExact){
        throw "Original PC-owned source image readback changed: $($row.Name)"
    }
    $key=Normalize-Name ([string]$row.Name)
    if($owned.ContainsKey($key)){throw "Duplicate actual PC IWD identity: $key"}
    $owned[$key]=$row
}
if($owned.Count -ne 2){throw 'Expected two PC-owned image payloads with successful Xenon roundtrip.'}
$graphByName=@{}
foreach($gr in @($mix.imageGraphInfo)){
    $key=Normalize-Name ([string]$gr.Name)
    if($graphByName.ContainsKey($key)){throw "Duplicate real source-owned graph: $key"}
    $graphByName[$key]=$gr
}
if($graphByName.Count -ne 2){throw 'Expected two real source image graphs inside the 8509 root candidate.'}
$cached=@{}
foreach($row in @($pc.extractedSourceImages)){
    $key=Normalize-Name ([string]$row.Name)
    if(-not $cached.ContainsKey($key)){$cached[$key]=New-Object 'System.Collections.Generic.List[object]'}
    $cached[$key].Add($row)
}
$review=$null
if(Test-Path -LiteralPath $reviewPath -PathType Leaf){
    $review=Get-Content -LiteralPath $reviewPath -Raw|ConvertFrom-Json
    if([string]$review.schema -ne 'WaW360.AztecPcImageCandidateReview.v1' -or
       [string]$review.status -ne 'AZTEC_PC_IMAGE_CANDIDATE_PROVENANCE_PASS' -or
       [bool]$review.hardwareReady){
        throw 'Optional external PC library candidate report drifted.'
    }
}
$reviewByName=@{}
if($null -ne $review){
    foreach($r in @($review.rows)){
        $key=Normalize-Name ([string]$r.ImageName)
        if(-not $reviewByName.ContainsKey($key)){$reviewByName[$key]=$r}
    }
}
$classified=New-Object 'System.Collections.Generic.List[object]'
$ownedCount=0
$cachedCount=0
$externalCount=0
$unresolved=0
foreach($img in $refRoles){
    $name=[string]$img.originalPcImageName
    $key=Normalize-Name $name
    if([string]::IsNullOrWhiteSpace($key)){throw 'Source image name missing for an exact native identity field.'}
    $rootIndex=[int]$img.nativeImageRootIndexes[0]
    $target=@($nativeImages|Where-Object {[int]$_.Index -eq $rootIndex})
    if($target.Count -ne 1 -or (Normalize-Name ([string]$target[0].Name)) -cne $key){
        throw "Six-slot native root identity not exact: $name index=$rootIndex"
    }
    $kind='NO_SOURCE_PIXEL_PROOF'
    $sourceCandidateEvidence=@()
    $pcRoundtrip=$owned.ContainsKey($key) -and $graphByName.ContainsKey($key)
    if($pcRoundtrip){
        if([string]$owned[$key].InputPayloadSha256 -ine
           [string]$owned[$key].XboxReadbackPayloadSha256){
            throw "PC IWD image payload digest no longer exact: $name"
        }
        $kind='PC_OWNED_IWD_XENON_IMAGE_ROUNDTRIP_PASS'
        $ownedCount++
        $sourceCandidateEvidence=@([string]$owned[$key].SourcePcIwiSha256)
    }elseif($cached.ContainsKey($key)){
        $kind='DIAGNOSTIC_PC_FASTFILE_IWI_CACHE_NOT_REPRODUCIBLE'
        $cachedCount++
        $sourceCandidateEvidence=@($cached[$key]|ForEach-Object {[string]$_.Sha256})
    }elseif($reviewByName.ContainsKey($key) -and
           [string]$reviewByName[$key].Classification -ne 'NO_PC_SOURCE_CANDIDATE'){
        $kind='EXTERNAL_PC_NAME_CANDIDATE_NOT_AZTEC_OWNERSHIP'
        $externalCount++
        $sourceCandidateEvidence=@($reviewByName[$key].Candidates|ForEach-Object {[string]$_.Sha256})
    }else{$unresolved++}
    $classified.Add([ordered]@{
        worldPointerField=[string]$img.sourceField
        originalPcImageName=$name
        nativeImageRootIdentityIndex=$rootIndex
        classification=$kind
        sourceCandidateSha256s=@($sourceCandidateEvidence)
        verifiedPcOwnedXenonImageBody=$pcRoundtrip
        proofOfXboxRuntimeResidency=$false
        fieldActuallyRebasedInGate11Ff=$false
        copiedXboxReferencePixels=$false
    })
    Write-Host "[AZTEC-GATE11-IMG-STREAM] IMAGE $($img.sourceField): $name => $kind"
}
if($classified.Count -ne 6){throw 'Six image slots not fully classified.'}
Write-Host "[AZTEC-GATE11-IMG-STREAM] 6 image slots: PC owned=$ownedCount partial-cache=$cachedCount external-name-only=$externalCount unresolved=$unresolved"

# Use the independently source-compared 11-cell/740-AABB graph as the
# TOPOLOGY input for future true Xbox streaming generation. This is not
# itself a GfxStreamingAabbTree: native trees use different item/child
# semantics, which are deliberately not guessed or copied from retail.
$topoPath=Join-Path $base 'pc_g10_1903\gate11_cell_topology_source\aztec_gate11_pc_source_cells_native_abi.json'
$topoReadback=Join-Path $root 'oat_loaded_11_cell_aabb_readback\aztec_gate11_independent_cells_aabb_readback.json'
foreach($file in @($topoPath,$topoReadback)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Previously loaded source cell topology missing: $file"}
}
$tr=Get-Content -LiteralPath $topoReadback -Raw|ConvertFrom-Json
$topo=Get-Content -LiteralPath $topoPath -Raw|ConvertFrom-Json
$pcCellFile=[string]$topo.originalPcTypedCellAabbGraph
if([string]$tr.actualGfxWorldFfSha256 -ine $expectedFf -or
   [int]$tr.exactLoadedCounts.cellOrdinals -ne 11 -or
   [int]$tr.exactLoadedCounts.treeOrdinals -ne 740 -or
   [int]$tr.exactLoadedCounts.treeModelIndexes -ne 14974 -or
   [string]$tr.originalPcSourceTypedGraphSha256 -ine [string]$topo.originalPcTypedCellAabbGraphSha256 -or
   -not(Test-Path -LiteralPath $pcCellFile -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pcCellFile -Algorithm SHA256).Hash -ine
        [string]$topo.originalPcTypedCellAabbGraphSha256){
    throw '11-cell/740-AABB source-only topology cannot be trusted for stream reconstruction.'
}
$pcCells=Get-Content -LiteralPath $pcCellFile -Raw|ConvertFrom-Json
if(@($pcCells.cells).Count -ne 11){throw 'Original PC 11-cell source graph length changed'}
$treeCounter=0;$sourceSmodelLinks=0
$sourceBoundsMin=@([double]::PositiveInfinity,[double]::PositiveInfinity,[double]::PositiveInfinity)
$sourceBoundsMax=@([double]::NegativeInfinity,[double]::NegativeInfinity,[double]::NegativeInfinity)
foreach($cellRow in @($pcCells.cells)){
    $trees=@($cellRow.aabbTrees)
    if([int]$cellRow.aabbTreeCount -ne $trees.Count){
        throw 'Original PC cell tree count and validated source index graph diverged.'
    }
    $treeCounter += $trees.Count
    for($axis=0;$axis -lt 3;$axis++){
        $low=[double]$cellRow.mins[$axis]
        $high=[double]$cellRow.maxs[$axis]
        if([double]::IsNaN($low) -or [double]::IsNaN($high) -or
           [double]::IsInfinity($low) -or [double]::IsInfinity($high) -or
           $low -gt $high){
            throw 'Nonfinite/invalid source cell bounds in a source-backed stream topology.'
        }
        $sourceBoundsMin[$axis]=[math]::Min($sourceBoundsMin[$axis],$low)
        $sourceBoundsMax[$axis]=[math]::Max($sourceBoundsMax[$axis],$high)
    }
    foreach($tree in $trees){
        $sourceSmodelLinks+=@($tree.smodelIndexes).Count
    }
}
if($treeCounter -ne 740 -or $sourceSmodelLinks -ne 14974){
    throw "Source cell/tree model-indices changed: trees=$treeCounter, refs=$sourceSmodelLinks"
}
$sourceTopology=[ordered]@{
    originalPcSourceCells=11
    originalPcSourceCellAabbTrees=$treeCounter
    originalPcCellAabbModelIndexLinks=$sourceSmodelLinks
    sourceBoundsMins=$sourceBoundsMin
    sourceBoundsMaxs=$sourceBoundsMax
    sourceGraphSha256=[string]$topo.originalPcTypedCellAabbGraphSha256
    sameAsNativeStreamingTreeSemanticLayout=$false
    reconstructedNativeStreamingTreeBody=$false
}
Write-Host "[AZTEC-GATE11-IMG-STREAM] Original-PC topology: 11 cells, 740 source AABB trees, 14974 model refs; native stream tree is DISTINCT."

function Has-Capability {
    if(-not(Test-Path -LiteralPath $oat -PathType Leaf) -or
       -not(Test-Path -LiteralPath $capPath -PathType Leaf)){return $false}
    $cap=Get-Content -LiteralPath $capPath -Raw|ConvertFrom-Json
    return ([string]$cap.format -eq 'WaW360.OatGraphDumperCapabilities.v1' -and
        [string]$cap.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($cap.capabilities) -contains 't4xenon-gfxworld-gate11-stream-structural-shape-v1')
}
Write-Host '[AZTEC-GATE11-PROGRESS] [########------------] 40% | native stream structural-only OAT capability'
if(-not(Has-Capability)){
    if(-not $BuildDumper){throw 'Loaded Xbox stream structural witness not installed; run once with -BuildDumper.'}
    Write-Host '[AZTEC-GATE11-IMG-STREAM] Building specialized pinned x86 OAT stream-STRUCT witness once.'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $builder
    if($LASTEXITCODE -ne 0 -or -not(Has-Capability)){
        throw 'New OAT stream structural capability build failed. No source FF touched.'
    }
}
New-Item -ItemType Directory -Force -Path $out|Out-Null
$temp=Join-Path ([IO.Path]::GetTempPath()) ('waw_g11stream_'+[Guid]::NewGuid().ToString('N').Substring(0,10))
New-Item -ItemType Directory -Force -Path $temp|Out-Null

function Read-NativeStreamShape([string]$file,[string]$part,[string]$tempRoot,[string]$workDir){
    $dir=Join-Path $tempRoot $part
    New-Item -ItemType Directory -Force -Path $dir|Out-Null
    $log=Join-Path $workDir ($part+'_gfxworld_struct_only_oat.log')
    $save=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $oat '--no-color' '--include-assets' 'gfxworld' '--output-folder' $dir $file 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $log -Encoding UTF8
        $code=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$save}
    if($code -ne 0){
        Get-Content -LiteralPath $log -Tail 30 |
            ForEach-Object {Write-Host "[AZTEC-GATE11-IMG-STREAM] $part OAT $_"}
        throw "Native GfxWorld $part loaded-struct shape OAT failed exit=$code"
    }
    $json=@(Get-ChildItem -LiteralPath $dir -Recurse -File -Filter '*.json' |
        Where-Object {$_.FullName -match 'xenon_gfxworld_surface_abi_witness'})
    if($json.Count -ne 1){throw "$part expected one OAT native GfxWorld struct, got $($json.Count)"}
    $doc=Get-Content -LiteralPath $json[0].FullName -Raw|ConvertFrom-Json
    $v=$doc.gate11StreamShape
    if([string]$doc.schema -ne 'WaW360.T4XenonGfxWorldSurfaceAbiWitness.v1' -or
       [string]$v.schema -ne 'WaW360.T4XenonGate11LoadedStreamStructuralShape.v1' -or
       [int]$v.streamStructBytes -ne 16 -or
       [int]$v.treeStructBytes -ne 32 -or
       -not [bool]$v.boundedCountsAndPointers -or
       [bool]$v.containsXboxReferenceStreamPayload -or
       [bool]$v.sourceOwnedStreamingReconstructionProven -or
       [bool]$v.runtimeHardwareValidated){
        throw "Native $part stream struct fields absent, unsafe or unexpectedly claimed complete."
    }
    return $v
}

try{
    Write-Host '[AZTEC-GATE11-PROGRESS] [############--------] 60% | read CURRENT 4.51MB world native stream metadata shape'
    $present=Read-NativeStreamShape $ff 'new_pc_world' $temp $out
    Write-Host "[AZTEC-GATE11-IMG-STREAM] Converted world: stream trees=$($present.treeCount), leafRefs=$($present.leafRefCount), image fields reflection=$($present.reflectionProbeCount) lightmaps=$($present.lightmapCount)"
    Write-Host '[AZTEC-GATE11-PROGRESS] [################----] 80% | read ORIGINAL Xbox native ABI stream shape (AGGREGATES ONLY)'
    $native=Read-NativeStreamShape $retail 'retail_abi_only' $temp $out
    Write-Host "[AZTEC-GATE11-IMG-STREAM] Xbox ABI oracle: stream trees=$($native.treeCount) leafRefs=$($native.leafRefCount) childSpansBad=$($native.outOfRangeChildSpans) itemSpansBad=$($native.outOfRangeItemSpans)"
    if((Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash -ine $expectedFf -or
       (Get-FileHash -LiteralPath $retail -Algorithm SHA256).Hash -ine $expectedRetail){
        throw 'CRITICAL: loaded native FF changed during read-only image/stream frontier audit.'
    }
    $hasValidNativeTreeModel=([int]$native.treeCount -gt 0 -and [int]$native.leafRefCount -ge 0 -and
        [int]$native.outOfRangeItemSpans -eq 0 -and
        [int]$native.outOfRangeChildSpans -eq 0 -and [int]$native.invalidBoundsAxes -eq 0)
    $status=if($hasValidNativeTreeModel){
        'AZTEC_GATE11_IMAGES_CLASSIFIED_STREAM_ABI_TOPOLOGY_METRICS_PASS_ORIGINAL_PC_BUILD_PENDING'
    }else{
        'AZTEC_GATE11_IMAGES_CLASSIFIED_STREAM_NATIVE_REFERENCE_TOPOLOGY_REQUIRES_REVIEW'
    }
    $result=[ordered]@{
        schema='WaW360.AztecGate11JointImageSourceAndStreamStructuralFrontier.v1'
        status=$status
        previouslyLoadedSourceOwnedWorldSha256=$expectedFf
        previousWorldBytes=4510629
        previousLightingAndShadowIndependentReadback=$proofPath
        nativeReferenceFfSha256=$expectedRetail
        imageFields=$classified.ToArray()
        imageFieldsTotal=6
        pcOwnedXenonImageRoundtripFields=$ownedCount
        diagnosticCachedPcIwiFields=$cachedCount
        externalPcOnlyNameCandidates=$externalCount
        imageFieldsWithoutAvailableOwnedPixelProof=6-$ownedCount
        nativeLoadedCurrentWorldStreamShape=$present
        referenceOriginalXboxAbiOnlyAggregateStreamShape=$native
        nativeReferenceStreamStructureValid=$hasValidNativeTreeModel
        sourceBackedValidatedCellTopology=$sourceTopology
        currentSourceCells=11
        currentSourceAabbTrees=740
        currentSourceStaticModelDraws=1903
        sourceStreamingTreeGeometrySerializationCompleted=$false
        useRetailStreamIndexPayloadAsSource=$false
        useRetailImagePixelsAsSource=$false
        copyNativeRootPointerPayloads=$false
        sourceImagePayloadGraftCompleted=$false
        sourceNativeStreamingReconstructionCompleted=$false
        fullGate11Passed=$false
        xboxHardwareReady=$false
        nextStep='Derive streaming topology from original-PC source-owned cell/AABB/geometry; convert genuinely owned reflection/lightmap images and independently OAT-read back all new native FF fields'
    }
    $result|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $report -Encoding UTF8
    Write-Host '[AZTEC-GATE11-PROGRESS] [####################] 100% | six image source classes + loaded native stream shape measured, no new FF'
    Write-Host "[AZTEC-GATE11-IMG-STREAM] Report=$report"
    Write-Host '[AZTEC-GATE11-IMG-STREAM] 10/13 Gate11 still PENDING. No donor payloads, no Xbox hardware.'
}finally{
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
