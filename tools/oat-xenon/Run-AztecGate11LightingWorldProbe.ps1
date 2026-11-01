[CmdletBinding()]
param([switch]$BuildDumper)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# One new FF, one native OAT capability rebuild IF needed, two independent
# OAT verifications. Previously OAT-proven candidate FFs never overwritten.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$work=Join-Path $base 'pc_g11_lighting_combined_probe'
$preflight=Join-Path $base 'pc_g11_cells_740\gate11_remaining_unified_source\aztec_gate11_combined_no_ff_build_preflight.json'
$plan=Join-Path $base 'pc_g11_two_wave_world_closure\aztec_gate11_two_wave_source_dependency_plan.json'
$primitiveProof=Join-Path $base 'pc_g11_remaining_world_primitive_probe\oat_loaded_primitive_source_digest\aztec_gate11_independent_loaded_primitive_source_digest.json'
$builder=Join-Path $PSScriptRoot 'Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1'
$verifier=Join-Path $PSScriptRoot 'Verify-AztecGate11OatLoadedCells.ps1'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$capFile=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$buildOat=Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1'
$emitReport=Join-Path $work 'aztec_pc_world_core_integrated_oat_run.json'
$cellReadback=Join-Path $work 'oat_loaded_11_cell_aabb_readback\aztec_gate11_independent_cells_aabb_readback.json'
$proof=Join-Path $work 'oat_loaded_lighting_shadow_region\aztec_gate11_independent_lighting_shadow_source_digest.json'
$primitiveSha='D46ABC8E5681E4F235ED82821016F6F0002505AD0DEF68D680D70FCCE75F1FCB'
$cellSha='05CEEC82AF5D58706BA5A6EE24995D09A3FB764F3E547A8B4758A9347ECF2862'
foreach($f in @($preflight,$plan,$primitiveProof,$builder,$verifier,$buildOat)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){throw "Missing Gate11 source/prior OAT proof: $f"}
}
$p=Get-Content -LiteralPath $preflight -Raw|ConvertFrom-Json
$t=Get-Content -LiteralPath $plan -Raw|ConvertFrom-Json
$d=Get-Content -LiteralPath $primitiveProof -Raw|ConvertFrom-Json
if([string]$p.previousPassingGate11FfSha256 -ine $cellSha -or
   [string]$t.protectedCurrentPrimitiveWorldSha256 -ine $primitiveSha -or
   [string]$t.status -ne 'AZTEC_GATE11_TWO_WAVE_ALL_LANES_SOURCE_AND_NATIVE_ABI_AUDITED_NEXT_FF_NOT_EMITTED' -or
   [string]$d.status -ne 'AZTEC_GATE11_SOURCE_OWNED_SKY_BRUSH_WATER_NATIVE_LOADED_DIGEST_PASS_REMAINDER_PENDING' -or
   [string]$d.actualNewNativeFfSha256 -ine $primitiveSha -or
   [int]$d.leafGroupsWithExactLoadedSourceDigests -ne 4 -or
   [long]$d.sourceUncompressedBytesCovered -ne 2107664L -or
   [bool]$t.fullGate11Passed -or [bool]$t.xboxHardwareReady){
    throw 'Two-wave joint source or prior original-PC FF proof not established.'
}
$old=[string]$d.actualNewNativeFf
$cell=[string]$p.previousPassingGate11Ff
if([IO.Path]::GetFullPath($old) -ieq [IO.Path]::GetFullPath($cell) -or
   (Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash -ine $primitiveSha -or
   (Get-FileHash -LiteralPath $cell -Algorithm SHA256).Hash -ine $cellSha -or
   [long](Get-Item -LiteralPath $old).Length -ne 3650170L){
    throw 'SHA/size of protected 3,650,170B primitive FF or 3,642,932B cell FF changed.'
}
Write-Host '[AZTEC-GATE11-LIGHTING] ==================================================================='
Write-Host '[AZTEC-GATE11-LIGHTING] ONE 8,509-root FF: 4.82MB light-grid + 1,903 shadow refs + 2 empty regions.'
Write-Host '[AZTEC-GATE11-LIGHTING] Reflection/lightmap GfxImage payloads + Xbox streamInfo remain HARD FENCED.'
Write-Host '[AZTEC-GATE11-PROGRESS] [##------------------] 10% | SHA-protect previous FFs and check OAT capability'
function Has-Capability {
    if(-not(Test-Path -LiteralPath $oat -PathType Leaf) -or
       -not(Test-Path -LiteralPath $capFile -PathType Leaf)){return $false}
    $cap=Get-Content -LiteralPath $capFile -Raw|ConvertFrom-Json
    return ([string]$cap.format -eq 'WaW360.OatGraphDumperCapabilities.v1' -and
       [string]$cap.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
       @($cap.capabilities) -contains 't4xenon-gfxworld-gate11-loaded-lighting-shadow-region-digest-v1')
}
if(-not(Has-Capability)){
    if(-not $BuildDumper){throw 'New lighting witness missing. Run same command once with -BuildDumper.'}
    Write-Host '[AZTEC-GATE11-LIGHTING] One specialized x86 OAT rebuild for native loaded grid/shadow verification.'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $buildOat
    if($LASTEXITCODE -ne 0 -or -not(Has-Capability)){throw 'Specialized OAT native lighting witness build failed.'}
}
Write-Host '[AZTEC-GATE11-PROGRESS] [######--------------] 30% | one combined source-owned FF build + native OAT first load'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $builder -IntegrateWorld -ProbeLevel Full -DpvsStaticProbe -Gate8ChildProbe -Gate8ChildLevel All -Gate8AllModelProbe -Gate10StaticDrawProbe -Gate11CellProbe -Gate11PrimitiveProbe -Gate11LightingProbe
if($LASTEXITCODE -ne 0){throw "Combined candidate failed; see $work\build.log and $work\integrated_emitter.log"}
if(-not(Test-Path -LiteralPath $emitReport -PathType Leaf)){throw "New source-owned FF manifest missing: $emitReport"}
$m=Get-Content -LiteralPath $emitReport -Raw|ConvertFrom-Json
if([string]$m.status -ne 'AZTEC_GATE11_LIGHT_GRID_SHADOW_REGION_OAT_LOAD_PASS_FIELDS_UNVERIFIED' -or
   -not [bool]$m.OatPass -or
   -not [bool]$m.gate11LightingCombinedProbe -or
   [long]$m.gate11LightingGridBytes -ne 4822860L -or
   [int]$m.gate11LightingShadowModelReferences -ne 1903 -or
   [int]$m.gate11LightingEmptyRegionHeaders -ne 2 -or
   [bool]$m.gate11LightingIndependentLoadedFieldReadback -or
   [bool]$m.gate11LightmapImagePayloadsResolved -or
   [bool]$m.gate11XboxStreamInfoReconstructed -or
   [int]$m.gate10DrawRecordsInEmittedFf -ne 1903 -or
   [int]$m.gate10StockModelDrawUnverified -ne 1 -or
   [int]$m.gate11SourceCellsInFf -ne 11 -or
   [int]$m.gate11AabbTreesInFf -ne 740 -or
   [bool]$m.hardwareReady){
    throw 'New FF first native OAT loader did not accept exact combined Gate11 source graph.'
}
$new=[string]$m.outputFastFile
$newSize=[long](Get-Item -LiteralPath $new).Length
$newSha=(Get-FileHash -LiteralPath $new -Algorithm SHA256).Hash
if([IO.Path]::GetFullPath($new) -ieq [IO.Path]::GetFullPath($old) -or
   $newSha -ieq $primitiveSha -or $newSize -le 3650170L){
    throw 'New combined FF size/identity not distinct from previously passing candidate.'
}
if((Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash -ine $primitiveSha){
    throw 'CRITICAL: previously proven primitive FF changed during combined build.'
}
Write-Host "[AZTEC-GATE11-SIZE] Prior=3650170 B, new=$newSize B, delta=$($newSize-3650170L) B"
Write-Host '[AZTEC-GATE11-PROGRESS] [###########---------] 55% | independent OAT re-load + original PC 11-cell/740-tree/14974-index source compare'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $verifier -LightingCandidate
if($LASTEXITCODE -ne 0 -or -not(Test-Path -LiteralPath $cellReadback -PathType Leaf)){
    throw 'Combined FF independent original-PC cell/AABB regression FAIL.'
}
$v=Get-Content -LiteralPath $cellReadback -Raw|ConvertFrom-Json
if([string]$v.actualGfxWorldFfSha256 -ine $newSha -or
   -not [bool]$v.sourceOwnedLightingCandidate -or
   [int]$v.exactLoadedCounts.cellOrdinals -ne 11 -or
   [int]$v.exactLoadedCounts.treeOrdinals -ne 740 -or
   [int]$v.exactLoadedCounts.treeModelIndexes -ne 14974){
    throw 'Independent OAT cell regression readback was not bound to exact new combined FF.'
}

# Compare OAT-loaded native x86 little-endian scalar memory to SHA-locked
# ORIGINAL-PC little-endian typed leaf files; never use Xbox donor payload.
Write-Host '[AZTEC-GATE11-PROGRESS] [##############------] 70% | independently digest all new light-grid, shadow and region source fields'
$l=Get-Content -LiteralPath ([string]$p.nativeEndiannessLeafReport) -Raw|ConvertFrom-Json
$s=Get-Content -LiteralPath ([string]$l.sourceReadinessReport) -Raw|ConvertFrom-Json
$worldFile=[string]$s.sourceTypedRemainderGraph
if((Get-FileHash -LiteralPath $worldFile -Algorithm SHA256).Hash -ine [string]$s.sourceTypedRemainderGraphSha256){
    throw 'Original PC normalized shadow/region source SHA drifted.'
}
$world=Get-Content -LiteralPath $worldFile -Raw|ConvertFrom-Json
if(-not ('AztecGate11LightingFnv64' -as [type])){
    Add-Type -TypeDefinition @'
using System;
using System.Globalization;
public static class AztecGate11LightingFnv64
{
    public static string Digest(byte[] data)
    {
        unchecked
        {
            ulong h=14695981039346656037UL;
            foreach(byte b in data){h^=b;h*=1099511628211UL;}
            return h.ToString("x16",CultureInfo.InvariantCulture);
        }
    }
    public static string DigestU16(ushort[] data)
    {
        unchecked
        {
            ulong h=14695981039346656037UL;
            foreach(ushort v in data)
            {
                h^=(byte)v;h*=1099511628211UL;
                h^=(byte)(v>>8);h*=1099511628211UL;
            }
            return h.ToString("x16",CultureInfo.InvariantCulture);
        }
    }
}
'@
}
$gridExpected=[ordered]@{}
foreach($item in @(
    @{name='gridRowDataStart';count=322;stride=2},
    @{name='gridRawRowData';count=10320;stride=1},
    @{name='gridEntries';count=1036024;stride=4},
    @{name='gridColors';count=3975;stride=168}
)){
    $name=[string]$item.name
    $e=$l.leaves.$name
    $file=[string]$e.sourceFile
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing PC source light-grid blob: $name"}
    $bytes=[IO.File]::ReadAllBytes($file)
    if([long]$bytes.Length -ne ([long]$item.count*[long]$item.stride) -or
       [long]$e.sourceRecordCount -ne [long]$item.count -or
       [int]$e.sourceAndNativeStructBytes -ne [int]$item.stride -or
       -not [bool]$e.sourceOwned -or -not [bool]$e.endianRoundTripExact -or
       [bool]$e.pointerFieldsPresent -or
       (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine [string]$e.sourceSha256){
        throw "Original PC grid SHA/count/endian report changed: $name"
    }
    $gridExpected[$name]=[AztecGate11LightingFnv64]::Digest($bytes)
}
$shadows=@($world.shadowGeometry)
$regions=@($world.lightRegions)
if($shadows.Count -ne 2 -or $regions.Count -ne 2){throw 'PC primary light shadow/region counts changed.'}
$shadowDigests=@()
$totalRefs=0
for($i=0;$i -lt 2;$i++){
    $row=$shadows[$i]
    $vec=New-Object 'System.Collections.Generic.List[UInt16]'
    foreach($id in @($row.smodelIndex)){
        if([int]$id -lt 0 -or [int]$id -ge 1903){throw "PC shadow model index out of range: $id"}
        $vec.Add([UInt16]$id)
    }
    if([int]$row.index -ne $i -or [int]$row.smodelCount -ne $vec.Count -or
       [int]$row.surfaceCount -ne 0 -or [int]$regions[$i].hullCount -ne 0){
        throw "Original PC light shadow/region source changed at ordinal $i."
    }
    $shadowDigests+=([AztecGate11LightingFnv64]::DigestU16($vec.ToArray()))
    $totalRefs+=$vec.Count
}
if($totalRefs -ne 1903){throw "Source shadow models total $totalRefs expected 1903."}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('waw_g11light_'+[Guid]::NewGuid().ToString('N').Substring(0,10))
$out=Split-Path -Parent $proof
$log=Join-Path $out 'independent_oat_loaded_lighting.log'
New-Item -ItemType Directory -Path $temp,$out -Force|Out-Null
try{
    $save=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $oat '--no-color' '--include-assets' 'gfxworld' '--output-folder' $temp $new 2>&1|
            ForEach-Object {[string]$_}|Set-Content -LiteralPath $log -Encoding UTF8
        $exitCode=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$save}
    if($exitCode -ne 0){
        Get-Content -LiteralPath $log -Tail 35|ForEach-Object {Write-Host "[AZTEC-GATE11-LIGHTING] OAT $_"}
        throw "Third independent source lighting OAT load failed ($exitCode)"
    }
    $hits=@(Get-ChildItem -LiteralPath $temp -Recurse -File -Filter '*.json'|
        Where-Object {$_.FullName -match 'xenon_gfxworld_surface_abi_witness'})
    if($hits.Count -ne 1){throw "Expected one loaded GfxWorld typed witness, got $($hits.Count)"}
    $loaded=Get-Content -LiteralPath $hits[0].FullName -Raw|ConvertFrom-Json
    $g=$loaded.nativeLightingGraph
    $pr=$loaded.nativePrimitiveGraph
    if([string]$g.schema -ne 'WaW360.T4XenonLoadedGate11LightingShadowRegionDigest.v1' -or
       -not [bool]$g.gridArraysReady -or -not [bool]$g.shadowRegionHeadersReady -or
       [int]$g.rowCount -ne 322 -or [int]$g.rawRowDataSize -ne 10320 -or
       [int]$g.entryCount -ne 1036024 -or [int]$g.colorCount -ne 3975 -or
       [int]$g.primaryLights -ne 2 -or @($g.shadows).Count -ne 2 -or
       [bool]$g.imagePayloadBindingsProven -or [bool]$g.xenonStreamingGraphReconstructed -or
       [bool]$g.hardwareRuntimeSemanticsProven -or -not [bool]$pr.arraysAvailable){
        throw 'Loaded GfxWorld light-grid/shadow/primitive pointers or release fences failed.'
    }
    $shape=$world.sourceGridShape
    if([int]$g.rowAxis -ne [int]$shape.rowAxis -or
       [int]$g.colAxis -ne [int]$shape.colAxis -or
       [int]$g.sunPrimaryLightIndex -ne [int]$shape.sunPrimaryLightIndex -or
       [bool]$g.hasLightRegions -ne [bool]$shape.hasLightRegions -or
       (@($g.mins) -join ',') -cne (@($shape.mins) -join ',') -or
       (@($g.maxs) -join ',') -cne (@($shape.maxs) -join ',')){
        throw 'Loaded native embedded light-grid scalar geometry differs from source PC.'
    }
    $gridLoaded=[ordered]@{
        gridRowDataStart=[string]$g.gridRowDigest
        gridRawRowData=[string]$g.gridRawDigest
        gridEntries=[string]$g.gridEntryDigest
        gridColors=[string]$g.gridColorDigest
    }
    foreach($name in @($gridLoaded.Keys)){
        if([string]$gridLoaded[$name] -ine [string]$gridExpected[$name]){
            throw "Original PC $name and loaded native lighting digest differ. Expected $($gridExpected[$name]), found $($gridLoaded[$name])"
        }
        Write-Host "[AZTEC-GATE11-LIGHTING] MATCH grid $name FNV64=$($gridLoaded[$name])"
    }
    $refs=0
    for($i=0;$i -lt 2;$i++){
        $z=$g.shadows[$i]
        if([int]$z.ordinal -ne $i -or [int]$z.surfaceCount -ne 0 -or
           [int]$z.smodelCount -ne [int]$shadows[$i].smodelCount -or
           [string]$z.smodelDigest -ine [string]$shadowDigests[$i] -or
           [int]$z.hullCount -ne 0 -or -not [bool]$z.regionHullPointerNull){
            throw "Original PC shadow light $i or empty region differs in native OAT memory."
        }
        $refs+=[int]$z.smodelCount
        Write-Host "[AZTEC-GATE11-LIGHTING] MATCH shadow primaryLight=$i modelRefs=$($z.smodelCount), region hulls=0"
    }
    if($refs -ne 1903){throw 'Native shadow index count not exactly 1903.'}
    $prior=[ordered]@{
        skyStartSurfs=[string]$pr.skyDigest
        brushModels=[string]$pr.brushDigest
        waterBuffer0=[string]$pr.water0Digest
        waterBuffer1=[string]$pr.water1Digest
    }
    foreach($name in @($prior.Keys)){
        if([string]$prior[$name] -ine [string]$d.sourceLittleEndianDigests.$name){
            throw "Previously proven primitive field $name regressed in combined FF"
        }
    }
    if((Get-FileHash -LiteralPath $cell -Algorithm SHA256).Hash -ine $cellSha -or
       (Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash -ine $primitiveSha -or
       (Get-FileHash -LiteralPath $new -Algorithm SHA256).Hash -ine $newSha){
        throw 'Protected native cell/primitive FF changed during independent readback.'
    }
    $result=[ordered]@{
        schema='WaW360.AztecGate11IndependentOatLoadedLightingAndShadowSourceProof.v1'
        status='AZTEC_GATE11_SOURCE_LIGHTGRID_SHADOW_EMPTY_REGIONS_NATIVE_LOADED_PASS_IMAGE_STREAM_STILL_BLOCKED'
        newNativeFf=$new
        newNativeFfSha256=$newSha
        newNativeFfBytes=$newSize
        priorCellFfSha256=$cellSha
        priorPrimitiveFfSha256=$primitiveSha
        nativeLightGridBytesVerified=4822860L
        gridOriginalPcDigests=$gridExpected
        gridNativeLoadedDigests=$gridLoaded
        sourceShadowModelReferences=$totalRefs
        loadedShadowModelReferences=$refs
        loadedRegionHeaders=2
        preexistingCellsAabbAndModelIndicesStillExact=$true
        preexistingFourPrimitiveSourceDigestsStillExact=$true
        reflectionLightmapImagePayloadsProven=$false
        xboxStreamInfoReconstructed=$false
        fullGate11Passed=$false
        hardwareReady=$false
    }
    $result|ConvertTo-Json -Depth 9|Set-Content -LiteralPath $proof -Encoding UTF8
    Write-Host '[AZTEC-GATE11-PROGRESS] [####################] 100% | new grid + shadow + region and previous source regressions independent OAT PASS'
    Write-Host "[AZTEC-GATE11-SIZE] Prior=3650170 B new=$newSize B delta=$($newSize-3650170L) B"
    Write-Host "[AZTEC-GATE11-LIGHTING] LIGHTGRID 4,822,860 bytes; SHADOW refs 1,903; REGIONS 2; cells 11; trees 740. Source OAT PASS."
    Write-Host "[AZTEC-GATE11-LIGHTING] Report=$proof"
    Write-Host '[AZTEC-GATE11-LIGHTING] GATE11=10/13; 6 image name matches NOT pixel proof, Xbox stream info + stock XModel unresolved. NO XBOX.'
}finally{
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
