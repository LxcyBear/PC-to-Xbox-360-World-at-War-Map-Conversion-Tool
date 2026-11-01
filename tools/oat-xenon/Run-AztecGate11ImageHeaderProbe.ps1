[CmdletBinding()]
param([switch]$BuildDumper)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Gate11 opt-in next isolated candidate. Existing 4.51MB lighting FF
# is SHA protected. FIRST prove generated image NAMES and null-stream
# from original PC typed GfxWorld AND actual retail Xbox native refs;
# THEN serialize exactly 2 x 16B original-PC GfxReflectionProbe headers
# + 2 x 8B GfxLightmapArray headers. Six GfxImage* explicitly remain
# NULL (NO imaginary pixel donor, no promised hardware compatibility).
# 2 independent OAT readbacks + full 11-cell/740-AABB regression.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$sourceFrontier=Join-Path $PSScriptRoot 'Run-AztecGate11ImageStreamClosure.ps1'
$orderCheck=Join-Path $PSScriptRoot 'Test-AztecGate11ImageHeaderNativeOrder.ps1'
$builder=Join-Path $PSScriptRoot 'Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1'
$cellVerifier=Join-Path $PSScriptRoot 'Verify-AztecGate11OatLoadedCells.ps1'
$prior=Join-Path $base 'pc_g11_lighting_combined_probe\oat_loaded_lighting_shadow_region\aztec_gate11_independent_lighting_shadow_source_digest.json'
$sourceReport=Join-Path $base 'pc_g11_image_stream_closure\aztec_gate11_6_images_and_native_stream_shape.json'
$binding=Join-Path $base 'pc_g11_cells_740\gate11_remaining_unified_source\aztec_gate11_exact_asset_reference_audit.json'
$work=Join-Path $base 'pc_g11_image_headers_probe'
$ffReport=Join-Path $work 'aztec_pc_world_core_integrated_oat_run.json'
$cellReport=Join-Path $work 'oat_loaded_11_cell_aabb_readback\aztec_gate11_independent_cells_aabb_readback.json'
$out=Join-Path $work 'oat_loaded_generated_image_headers'
$resultPath=Join-Path $out 'aztec_gate11_independent_image_headers_and_zero_stream.json'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$oldSha='B832EDCE7EECFBBF2B0A141FF2C3D946595B317D7E7C7ACA0D21991277522883'
foreach($file in @($prior,$sourceFrontier,$orderCheck,$builder,$cellVerifier,$binding)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){
        throw "Missing original-PC Gate11 prior witness: $file"
    }
}
$p=Get-Content -LiteralPath $prior -Raw|ConvertFrom-Json
if([string]$p.newNativeFfSha256 -ine $oldSha -or
   [long]$p.newNativeFfBytes -ne 4510629L -or
   [long]$p.nativeLightGridBytesVerified -ne 4822860L -or
   [int]$p.loadedShadowModelReferences -ne 1903 -or
   [bool]$p.reflectionLightmapImagePayloadsProven -or
   [bool]$p.hardwareReady -or
   (Get-FileHash -LiteralPath ([string]$p.newNativeFf) -Algorithm SHA256).Hash -ine $oldSha){
    throw 'Source-owned 4,510,629B combined lighting FF has changed. No new FF permitted.'
}
Write-Host '[AZTEC-GATE11-IMAGE-HEADERS] ============================================================'
Write-Host '[AZTEC-GATE11-IMAGE-HEADERS] Combined source reflection/lightmap HEADER candidate, not image pixels.'
Write-Host '[AZTEC-GATE11-IMAGE-HEADERS] 4.51MB verified FF SHA locked; six generated GfxImage* remain NULL.'
Write-Host '[AZTEC-GATE11-PROGRESS] [#-------------------] 5% | native GfxWorld reflection/Dpvs/cells/lightmap order static fail-fast test'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $orderCheck
if($LASTEXITCODE -ne 0){throw 'T4 Xenon source GfxWorld child order regression detected. No OAT rebuild or new FF created.'}
Write-Host '[AZTEC-GATE11-PROGRESS] [##------------------] 10% | native Xbox image-name and zero-stream structure evidence'
$argsList=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$sourceFrontier)
if($BuildDumper){$argsList+='-BuildDumper'}
& powershell.exe @argsList
if($LASTEXITCODE -ne 0){throw 'Native generated-image and empty-stream source frontier failed. No FF was built.'}
if(-not(Test-Path -LiteralPath $sourceReport -PathType Leaf)){
    throw 'Native name/empty-stream frontier did not write its report.'
}
$r=Get-Content -LiteralPath $sourceReport -Raw|ConvertFrom-Json
if([string]$r.status -ne 'AZTEC_GATE11_SIX_IMAGE_NAMES_NATIVE_REFERENCE_MATCH_AND_NATIVE_STREAM_EMPTY_STRUCT_PROVEN_PAYLOAD_PENDING' -or
   -not [bool]$r.originalAndCurrentNativeEmptyStreamStructsMatch -or
   -not [bool]$r.originalAndNativeReflectionLightmapNamesMatch -or
   [int]$r.nativeReferenceImageNameMatches -ne 6 -or
   [int]$r.nativeReferenceImagePointersPresent -ne 6 -or
   [int]$r.imageFieldsWithoutAvailableOwnedPixelProof -ne 6 -or
   [bool]$r.fullGate11Passed -or [bool]$r.xboxHardwareReady){
    Write-Host "[AZTEC-GATE11-IMAGE-HEADERS] REFUSED: status=$($r.status), streamNullMatch=$($r.originalAndCurrentNativeEmptyStreamStructsMatch), imageNames=$($r.nativeReferenceImageNameMatches)/6."
    throw "Native image reference/empty-stream prerequisites aren't exact. Inspect $sourceReport; no FF built."
}
$srcMeta=Get-Content -LiteralPath $binding -Raw|ConvertFrom-Json
$sourceWorldFile=[string]$srcMeta.sourceOnlyTypedGraph
if((Get-FileHash -LiteralPath $sourceWorldFile -Algorithm SHA256).Hash -ine [string]$srcMeta.sourceOnlyTypedGraphSha256){
    throw 'Original PC typed world image source changed.'
}
$original=Get-Content -LiteralPath $sourceWorldFile -Raw|ConvertFrom-Json
Write-Host '[AZTEC-GATE11-PROGRESS] [######--------------] 30% | one new isolated 8509-root source-header FF plus native OAT load'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $builder -IntegrateWorld -ProbeLevel Full -DpvsStaticProbe -Gate8ChildProbe -Gate8ChildLevel All -Gate8AllModelProbe -Gate10StaticDrawProbe -Gate11CellProbe -Gate11PrimitiveProbe -Gate11LightingProbe -Gate11ImageHeadersProbe
if($LASTEXITCODE -ne 0){throw "New source header FF emitter/OAT failed; see $work\build.log and $work\integrated_emitter.log"}
if(-not(Test-Path -LiteralPath $ffReport -PathType Leaf)){throw "FF emitter manifest missing: $ffReport"}
$emit=Get-Content -LiteralPath $ffReport -Raw|ConvertFrom-Json
if([string]$emit.status -ne 'AZTEC_GATE11_SOURCE_IMAGE_HEADERS_OAT_PASS_PIXELS_UNPROVEN' -or
   -not [bool]$emit.OatPass -or
   -not [bool]$emit.gate11LightingCombinedProbe -or
   -not [bool]$emit.gate11ImageHeadersProbe -or
   [int]$emit.gate11SourceReflectionHeaderCount -ne 2 -or
   [int]$emit.gate11SourceLightmapHeaderCount -ne 2 -or
   [int]$emit.gate11UnboundGeneratedImagePointers -ne 6 -or
   -not [bool]$emit.gate11NativeEmptyStreamStructRequiredByReference -or
   [bool]$emit.gate11ImageHeaderIndependentLoadedReadbackProven -or
   [bool]$emit.gate11LightmapImagePayloadsResolved -or
   [bool]$emit.gate11XboxStreamInfoReconstructed -or
   [int]$emit.gate11SourceCellsInFf -ne 11 -or
   [int]$emit.gate11AabbTreesInFf -ne 740 -or
   [int]$emit.gate10DrawRecordsInEmittedFf -ne 1903 -or
   [bool]$emit.hardwareReady){
    throw 'Source header FF did not pass original-PC root + native OAT fail-closed manifest.'
}
$newFf=[string]$emit.outputFastFile
$newHash=(Get-FileHash -LiteralPath $newFf -Algorithm SHA256).Hash
$newSize=[long](Get-Item -LiteralPath $newFf).Length
if([IO.Path]::GetFullPath($newFf) -ieq [IO.Path]::GetFullPath([string]$p.newNativeFf) -or
   $newHash -ieq $oldSha -or $newSize -le 4510629L -or
   (Get-FileHash -LiteralPath ([string]$p.newNativeFf) -Algorithm SHA256).Hash -ine $oldSha){
    throw 'Combined new source-image FF invalid or protected 4.51MB candidate modified.'
}
Write-Host "[AZTEC-GATE11-SIZE] OLD=4510629 B, new=$newSize B, growth=$($newSize-4510629L) B"
Write-Host '[AZTEC-GATE11-PROGRESS] [##########----------] 50% | independent source 11-cell + 740 AABB model reference regression'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $cellVerifier -ImageHeadersCandidate
if($LASTEXITCODE -ne 0 -or -not(Test-Path -LiteralPath $cellReport -PathType Leaf)){
    throw 'Source-image FF second native OAT cell/AABB regression failed.'
}
$cell=Get-Content -LiteralPath $cellReport -Raw|ConvertFrom-Json
if([string]$cell.actualGfxWorldFfSha256 -ine $newHash -or
   -not [bool]$cell.sourceOwnedImageHeadersCandidate -or
   [int]$cell.exactLoadedCounts.cellOrdinals -ne 11 -or
   [int]$cell.exactLoadedCounts.treeOrdinals -ne 740 -or
   [int]$cell.exactLoadedCounts.treeModelIndexes -ne 14974){
    throw 'New FF second independent OAT was not same 11/740/14974 original-PC source graph.'
}
Write-Host '[AZTEC-GATE11-PROGRESS] [##############------] 70% | THIRD native OAT: both new source headers, NULL generated images, prior lighting'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('waw_imgheads_'+[Guid]::NewGuid().ToString('N').Substring(0,10))
New-Item -ItemType Directory -Force -Path $temp,$out|Out-Null
$log=Join-Path $out 'independent_native_oat.log'
try{
    $save=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $oat '--no-color' '--include-assets' 'gfxworld' '--output-folder' $temp $newFf 2>&1 |
           ForEach-Object {[string]$_}|Set-Content -LiteralPath $log -Encoding UTF8
        $exit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$save}
    if($exit -ne 0){
        Get-Content -LiteralPath $log -Tail 35|
            ForEach-Object {Write-Host "[AZTEC-GATE11-IMAGE-HEADERS] OAT $_"}
        throw 'Third native OAT source image headers load failed.'
    }
    $files=@(Get-ChildItem -LiteralPath $temp -Recurse -File -Filter '*.json'|
        Where-Object {$_.FullName -match 'xenon_gfxworld_surface_abi_witness'})
    if($files.Count -ne 1){throw "One typed GfxWorld OAT witness required, got $($files.Count)"}
    $w=Get-Content -LiteralPath $files[0].FullName -Raw|ConvertFrom-Json
    $im=$w.gate11WorldImageSlots
    $stream=$w.gate11StreamShape
    $grid=$w.nativeLightingGraph
    $pr=$w.nativePrimitiveGraph
    if([string]$im.schema -ne 'WaW360.T4XenonGate11NativeWorldImageNameReferences.v1' -or
       -not [bool]$im.slotHeadersReady -or
       [int]$im.reflectionProbeCount -ne 2 -or [int]$im.lightmapCount -ne 2 -or
       @($im.reflectionProbeReferences).Count -ne 2 -or
       @($im.lightmapReferences).Count -ne 2 -or
       [bool]$im.imagePixelsReadOrCopied -or
       [bool]$im.sourceOwnedImagePayloadsVerified -or
       [bool]$im.hardwareRuntimeSemanticsProven){
        throw 'Third OAT reflection/lightmap header structure wrong or fabricated image payload.'
    }
    for($i=0;$i -lt 2;$i++){
        $ref=$im.reflectionProbeReferences[$i]
        $source=$original.reflectionProbeRefs[$i]
        if([int]$ref.ordinal -ne $i -or [bool]$ref.imagePointerPresent -or
           -not [string]::IsNullOrEmpty([string]$ref.imageName) -or
           [int]$source.index -ne $i){
            throw "Source reflection probe $i must have actual XYZ and NULL unproven GfxImage*"
        }
        for($k=0;$k -lt 3;$k++){
            if([math]::Abs([double]$ref.origin[$k]-[double]$source.origin[$k]) -gt 0.0001){
                throw "Source reflection $i native OAT origin axis $k mismatch."
            }
        }
        $lm=$im.lightmapReferences[$i]
        if([int]$lm.ordinal -ne $i -or
           [bool]$lm.primaryPointerPresent -or
           [bool]$lm.secondaryPointerPresent -or
           -not [string]::IsNullOrEmpty([string]$lm.primaryName) -or
           -not [string]::IsNullOrEmpty([string]$lm.secondaryName)){
            throw "Source lightmap $i header has an unproven image body pointer."
        }
        Write-Host "[AZTEC-GATE11-IMAGE-HEADERS] MATCH original-PC reflection $i XYZ and lightmap $i two intentionally unresolved NULL pixel links."
    }
    if([int]$stream.treeCount -ne 0 -or [int]$stream.leafRefCount -ne 0 -or
       [bool]$stream.treePointerPresent -or [bool]$stream.leafPointerPresent -or
       -not [bool]$stream.boundedCountsAndPointers){
        throw 'New source-image FF changed measured original-Xbox empty stream layout.'
    }
    $priorGrid=$p.gridNativeLoadedDigests
    if(-not [bool]$grid.gridArraysReady -or -not [bool]$grid.shadowRegionHeadersReady -or
       [string]$grid.gridRowDigest -ine [string]$priorGrid.gridRowDataStart -or
       [string]$grid.gridRawDigest -ine [string]$priorGrid.gridRawRowData -or
       [string]$grid.gridEntryDigest -ine [string]$priorGrid.gridEntries -or
       [string]$grid.gridColorDigest -ine [string]$priorGrid.gridColors -or
       [int]$grid.primaryLights -ne 2 -or
       ([int]$grid.shadows[0].smodelCount+[int]$grid.shadows[1].smodelCount) -ne 1903 -or
       -not [bool]$pr.arraysAvailable){
        throw 'Previously exact light-grid/shadow/source primitive fields regressed in new image header FF.'
    }
    if((Get-FileHash -LiteralPath ([string]$p.newNativeFf) -Algorithm SHA256).Hash -ine $oldSha -or
       (Get-FileHash -LiteralPath $newFf -Algorithm SHA256).Hash -ine $newHash){
        throw 'Source image header validation unexpectedly changed old or new FF.'
    }
    $rpt=[ordered]@{
        schema='WaW360.AztecGate11IndependentSourceImageHeaders.v1'
        status='AZTEC_GATE11_PC_REFLECTION_LIGHTMAP_HEADER_OAT_PASS_SIX_IMAGE_PAYLOADS_UNPROVEN'
        newNativeFf=$newFf
        newNativeFfSha256=$newHash
        newNativeFfBytes=$newSize
        priorLightingWorldFfSha256=$oldSha
        independentCellAabbRegression=$cellReport
        sourceReflectionHeadersLoaded=2
        sourceReflectionOriginsExact=2
        sourceLightmapHeadersLoaded=2
        sourceGeneratedImagePointersIntentionallyNull=6
        originalXboxAndCurrentEmptyStreamStructMatches=$true
        previousSourceLightGridShadowAndSkyBrushWaterIntact=$true
        sourceOwnedPixelBodiesProven=0
        realXboxRuntimeTextureBindingReady=$false
        missingStockModelBindingStillOpen=$true
        stateBitsRuntimeSemanticsUnverified=$true
        fullGate11Passed=$false
        xboxHardwareReady=$false
    }
    $rpt|ConvertTo-Json -Depth 9|Set-Content -LiteralPath $resultPath -Encoding UTF8
    Write-Host '[AZTEC-GATE11-PROGRESS] [####################] 100% | TWO reflection + TWO lightmap source headers independently OAT PASS, all prior data unchanged'
    Write-Host "[AZTEC-GATE11-SIZE] Prior=4510629 B new=$newSize B added=$($newSize-4510629L) B"
    Write-Host "[AZTEC-GATE11-IMAGE-HEADERS] Report=$resultPath"
    Write-Host '[AZTEC-GATE11-IMAGE-HEADERS] NO pixel payloads. Xbox runtime semantics/STOCK XMODEL/material stateBits and Gate11 STILL PENDING. No Xbox.'
}finally{
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}