[CmdletBinding()]
param([switch]$PrimitiveCandidate,[switch]$LightingCandidate,[switch]$ImageHeadersCandidate)
if(@($PrimitiveCandidate,$LightingCandidate,$ImageHeadersCandidate|Where-Object {$_}).Count -gt 1){
    throw 'Choose only one isolated FF candidate.'
}
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$work=Join-Path $base $(if($ImageHeadersCandidate){'pc_g11_image_headers_probe'}elseif($LightingCandidate){'pc_g11_lighting_combined_probe'}elseif($PrimitiveCandidate){'pc_g11_remaining_world_primitive_probe'}else{'pc_g11_cells_740'})
$manifestPath=Join-Path $work 'aztec_pc_world_core_integrated_oat_run.json'
$gate10Path=Join-Path $base 'pc_g10_1903\oat_loaded_1903_draw_readback\aztec_gate10_independent_1903_draw_readback.json'
$sourcePath=Join-Path $base 'pc_g10_1903\gate11_cell_topology_source\aztec_gate11_pc_source_cells_native_abi.json'
$out=Join-Path $work 'oat_loaded_11_cell_aabb_readback'
$report=Join-Path $out 'aztec_gate11_independent_cells_aabb_readback.json'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$capFile=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
foreach($p in @($manifestPath,$gate10Path,$sourcePath,$oat,$capFile)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing independent cell readback prerequisite: $p"}
}
$cap=Get-Content -LiteralPath $capFile -Raw|ConvertFrom-Json
if([string]$cap.format -ne 'WaW360.OatGraphDumperCapabilities.v1' -or
   [string]$cap.oatRevision -ne '3beaa7c81af517f0c05f2b4612d477ed1541b676' -or
   @($cap.capabilities) -notcontains 't4xenon-gfxworld-gate11-loaded-cell-aabb-typed-witness-v1'){
    throw 'Specialized pinned Xenon OAT lacks independent loaded-GfxCell/AABB typed witness capability.'
}
$m=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
$g=Get-Content -LiteralPath $gate10Path -Raw|ConvertFrom-Json
$s=Get-Content -LiteralPath $sourcePath -Raw|ConvertFrom-Json
$expectedStatus=if($ImageHeadersCandidate){
    'AZTEC_GATE11_SOURCE_IMAGE_HEADERS_OAT_PASS_PIXELS_UNPROVEN'
}elseif($LightingCandidate){
    'AZTEC_GATE11_LIGHT_GRID_SHADOW_REGION_OAT_LOAD_PASS_FIELDS_UNVERIFIED'
}elseif($PrimitiveCandidate){
    'AZTEC_GATE11_PRIMITIVE_SKY_BRUSH_WATER_OAT_LOAD_PASS_REMAINDER_PENDING'
}else{
    'AZTEC_GATE11_11_CELL_740_AABB_DIAGNOSTIC_OAT_LOAD_PASS_REMAINDER_PENDING'
}
if([string]$m.schema -ne 'WaW360.AztecPcGfxWorldIntegratedFastFileOatRun.v1' -or
   [string]$m.status -ne $expectedStatus -or
   -not [bool]$m.OatPass -or
   -not [bool]$m.gate11PhysicalCellDiagnostic -or
   (($PrimitiveCandidate -or $LightingCandidate -or $ImageHeadersCandidate) -and (-not [bool]$m.gate11PrimitiveProbe -or
      [int]$m.gate11PrimitiveSkySurfaces -ne 2 -or
      [int]$m.gate11PrimitiveBrushModels -ne 183 -or
      [long]$m.gate11PrimitiveWaterBytes -ne 2097408L -or
      [bool]$m.gate11PrimitiveIndependentReadbackProven)) -or
   (($LightingCandidate -or $ImageHeadersCandidate) -and (-not [bool]$m.gate11LightingCombinedProbe -or
      [long]$m.gate11LightingGridBytes -ne 4822860L -or
      [int]$m.gate11LightingShadowModelReferences -ne 1903 -or
      [int]$m.gate11LightingEmptyRegionHeaders -ne 2 -or
      [bool]$m.gate11LightingIndependentLoadedFieldReadback -or
      [bool]$m.gate11LightmapImagePayloadsResolved -or
      [bool]$m.gate11XboxStreamInfoReconstructed)) -or
   ($ImageHeadersCandidate -and (-not [bool]$m.gate11ImageHeadersProbe -or
      [int]$m.gate11SourceReflectionHeaderCount -ne 2 -or
      [int]$m.gate11SourceLightmapHeaderCount -ne 2 -or
      [int]$m.gate11UnboundGeneratedImagePointers -ne 6 -or
      -not [bool]$m.gate11NativeEmptyStreamStructRequiredByReference -or
      [bool]$m.gate11ImageHeaderIndependentLoadedReadbackProven)) -or
   [int]$m.gate10DrawRecordsInEmittedFf -ne 1903 -or
   [int]$m.gate10StockModelDrawUnverified -ne 1 -or
   [int]$m.gate11SourceCellsInFf -ne 11 -or
   [int]$m.gate11AabbTreesInFf -ne 740 -or
   [int]$m.gate11SmodelIndicesInFf -ne [int]$s.originalPcTypedSmodelIndicesCount -or
   [int]$m.gate11ReflectionIndicesInFf -ne [int]$s.originalPcTypedCellReflectionProbeIndicesCount -or
   [bool]$m.gate11FullWorldClosureApproved -or [bool]$m.hardwareReady -or
   [string]$g.stage4EEmittedFfSha256 -ine '0BB41398C4236EF33CB9D60F923E09152966D52AA623DAEFC33D731A42630564' -or
   [string]$s.status -ne 'AZTEC_GATE11_11_CELL_740_AABB_TYPED_SOURCE_NATIVE_ABI_PASS_NOT_EMITTED' -or
   -not [bool]$s.typedPcCellGraphSourcePass -or
   -not [bool]$s.nativeCellAabbOffsetsProven -or
   -not [bool]$s.nativeCellPortalAabbSizesAndCriticalOffsetsMatchPc){
    throw 'Gate11 source/FF evidence not a real isolated OAT-loaded 11-cell and 740-AABB diagnostic.'
}
$ff=[string]$m.outputFastFile
$old=[string]$g.stage4EEmittedFf
$source=[string]$s.originalPcTypedCellAabbGraph
foreach($p in @($ff,$old,$source)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing FF or PC source graph: $p"}
}
$baselineHash=(Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash
$sourceHash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
if($baselineHash -ine [string]$g.stage4EEmittedFfSha256 -or
   $sourceHash -ine [string]$s.originalPcTypedCellAabbGraphSha256 -or
   [IO.Path]::GetFullPath($ff) -ieq [IO.Path]::GetFullPath($old)){
    throw 'Source/Gate10 SHA drift or Gate11 illegally reuses passing Gate10 FF output.'
}
$sourceDoc=Get-Content -LiteralPath $source -Raw|ConvertFrom-Json
$expected=@($sourceDoc.cells)
$ffSha=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
$temp=Join-Path ([IO.Path]::GetTempPath()) ('wawg11read_'+[Guid]::NewGuid().ToString('N').Substring(0,10))
New-Item -ItemType Directory -Path $out,$temp -Force|Out-Null
$log=Join-Path $out 'oat_loaded_cells.log'
Write-Host '[AZTEC-GATE11-READBACK] ======================================================='
Write-Host '[AZTEC-GATE11-READBACK] OAT load independent NEW 11-cell FF; baseline SHA-locked.'
Write-Host '[AZTEC-GATE11-PROGRESS] [#####---------------] 25% | OAT loaded native GfxCell graph'
$exitCode=-1
$prior=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & $oat '--no-color' '--include-assets' 'gfxworld' '--output-folder' $temp $ff 2>&1|
        ForEach-Object {[string]$_}|Set-Content -LiteralPath $log -Encoding UTF8
    $exitCode=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$prior}
if($exitCode -ne 0){
    Get-Content -LiteralPath $log -Tail 35|ForEach-Object {Write-Host "[AZTEC-GATE11-READBACK] OAT $_"}
    throw "Independent OAT loaded GfxWorld FF exit=$exitCode. Gate11 NOT proven."
}
$loadedFiles=@(Get-ChildItem -LiteralPath $temp -Filter '*.json' -Recurse -File |
    Where-Object {$_.FullName -match 'xenon_gfxworld_surface_abi_witness'})
if($loadedFiles.Count -ne 1){throw "Expected one actual loaded GfxWorld typed witness, found $($loadedFiles.Count)"}
$loaded=Get-Content -LiteralPath $loadedFiles[0].FullName -Raw|ConvertFrom-Json
$graph=$loaded.nativeCellGraph
if([string]$loaded.schema -ne 'WaW360.T4XenonGfxWorldSurfaceAbiWitness.v1' -or
   [string]$graph.schema -ne 'WaW360.T4XenonLoadedGfxCellAabbWitness.v1' -or
   [int]$graph.cellCount -ne 11 -or
   -not [bool]$graph.cellsPresent -or
   -not [bool]$graph.loadedCellArraysValid -or
   @($graph.cells).Count -ne 11 -or
   [int]$graph.loadedTreeCount -ne 740 -or
   [int]$graph.loadedSmodelIndexCount -ne [int]$s.originalPcTypedSmodelIndicesCount -or
   [int]$graph.loadedReflectionProbeIndexCount -ne [int]$s.originalPcTypedCellReflectionProbeIndicesCount -or
   [bool]$graph.hardwareRuntimeSemanticsProven){
    throw 'New Gate11 FF did not load 11 valid GfxCells and all 740 nested AABB tree fields in pinned OAT.'
}
Write-Host '[AZTEC-GATE11-PROGRESS] [##############------] 70% | field-by-field 740 AABB and 14974 model-index source comparison'
$counts=[ordered]@{
    cellOrdinals=0;cellBounds=0;cellChildCounts=0;cellReflectionIndices=0
    treeOrdinals=0;treeBounds=0;treeMetadata=0;treeModelIndexes=0
}
function VecSame($a,$b,[string]$note){
    if(@($a).Count -ne 3 -or @($b).Count -ne 3){throw "${note}: vector length differs"}
    for($k=0;$k -lt 3;$k++){
        $x=[double]$a[$k];$y=[double]$b[$k]
        if([math]::Abs($x-$y) -gt 0.0001){throw "${note}: value mismatch on axis $k source=$x loaded=$y"}
    }
}
for($ci=0;$ci -lt 11;$ci++){
    $a=$expected[$ci]
    $b=$graph.cells[$ci]
    if([int]$a.ordinal -ne $ci -or [int]$b.ordinal -ne $ci){
        throw "Cell ordinal mismatch at $ci"
    }
    $counts.cellOrdinals++
    VecSame $a.mins $b.mins "Cell $ci mins"
    VecSame $a.maxs $b.maxs "Cell $ci maxs"
    $counts.cellBounds++
    foreach($field in @('aabbTreeCount','portalCount','cullGroupCount','reflectionProbeCount')){
        if([int]$a.$field -ne [int]$b.$field){throw "Cell $ci field $field mismatched"}
    }
    $counts.cellChildCounts++
    if((@($a.reflectionProbeIndices) -join ',') -cne (@($b.reflectionProbeIndices) -join ',')){
        throw "Cell $ci reflectionProbeIndices mismatch"
    }
    $counts.cellReflectionIndices+=@($a.reflectionProbeIndices).Count
    $at=@($a.aabbTrees);$bt=@($b.aabbTrees)
    if($at.Count -ne $bt.Count){throw "Cell $ci loaded AABB tree count mismatch"}
    for($ti=0;$ti -lt $at.Count;$ti++){
        $p=$at[$ti];$q=$bt[$ti]
        if([int]$p.ordinal -ne $ti -or [int]$q.ordinal -ne $ti){throw "AABB cell=$ci tree=$ti ordinal mismatch"}
        $counts.treeOrdinals++
        VecSame $p.mins $q.mins "Cell $ci tree $ti mins"
        VecSame $p.maxs $q.maxs "Cell $ci tree $ti maxs"
        $counts.treeBounds++
        foreach($field in @('childCount','surfaceCount','startSurfIndex','smodelIndexCount','childrenOffset')){
            if([int]$p.$field -ne [int]$q.$field){throw "AABB cell=$ci tree=$ti field $field mismatch"}
        }
        $counts.treeMetadata++
        if((@($p.smodelIndexes) -join ',') -cne (@($q.smodelIndexes) -join ',')){
            throw "AABB cell=$ci tree=$ti smodelIndexes differ from original-PC source"
        }
        $counts.treeModelIndexes+=@($p.smodelIndexes).Count
    }
}
if($counts.cellOrdinals -ne 11 -or $counts.cellBounds -ne 11 -or
   $counts.cellChildCounts -ne 11 -or
   $counts.cellReflectionIndices -ne [int]$s.originalPcTypedCellReflectionProbeIndicesCount -or
   $counts.treeOrdinals -ne 740 -or $counts.treeBounds -ne 740 -or
   $counts.treeMetadata -ne 740 -or
   $counts.treeModelIndexes -ne [int]$s.originalPcTypedSmodelIndicesCount){
    throw 'Independent loaded Gate11 GfxCell/AABB source field coverage incomplete.'
}
$witnessCopy=Join-Path $out 'oat_loaded_11_cells_740_aabb.json'
Copy-Item -LiteralPath $loadedFiles[0].FullName -Destination $witnessCopy -Force
$witnessSha=(Get-FileHash -LiteralPath $witnessCopy -Algorithm SHA256).Hash
$answer=[ordered]@{
    schema='WaW360.AztecGate11IndependentLoadedCellAabbReadback.v1'
    status='AZTEC_GATE11_PHYSICAL_11_CELL_740_AABB_SOURCE_FIELDS_INDEPENDENT_OAT_PASS_REMAINDER_PENDING'
    actualGfxWorldFf=$ff
    actualGfxWorldFfSha256=$ffSha
    preservedGate10FfSha256=$baselineHash
    sourceOwnedPrimitiveCandidate=[bool]($PrimitiveCandidate -or $LightingCandidate -or $ImageHeadersCandidate)
    sourceOwnedLightingCandidate=[bool]($LightingCandidate -or $ImageHeadersCandidate)
    sourceOwnedImageHeadersCandidate=[bool]$ImageHeadersCandidate
    primitiveSkyBrushWaterTypedReadbackProven=$false
    originalPcSourceTypedGraphSha256=$sourceHash
    independentOatWitness=$witnessCopy
    independentOatWitnessSha256=$witnessSha
    exactLoadedCounts=$counts
    runtimeLightsProven=$false
    externalStockModelResidencyProven=$false
    allGfxWorldChildrenComplete=$false
    hardwareReady=$false
}|ConvertTo-Json -Depth 8
$answer|Set-Content -LiteralPath $report -Encoding UTF8
Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "[AZTEC-GATE11-READBACK] 11/11 cells; 740/740 trees; $($counts.treeModelIndexes) model indices and $($counts.cellReflectionIndices) probes match ORIGINAL PC"
Write-Host "[AZTEC-GATE11-READBACK] New FF SHA=$ffSha"
Write-Host "[AZTEC-GATE11-READBACK] Report=$report"
if($PrimitiveCandidate -or $LightingCandidate -or $ImageHeadersCandidate){
    Write-Host '[AZTEC-GATE11-READBACK] New primitive FF preserves all source-owned cell/AABB fields in SECOND independent OAT readback.'
    Write-Host '[AZTEC-GATE11-READBACK] Sky, brush and water FIELD readback is still PENDING. No Gate11 overall PASS.'
}else{
    Write-Host '[AZTEC-GATE11-READBACK] Independently OAT-loaded typed FF children PASS. Other GfxWorld dependencies and hardware approval STILL PENDING.'
}
