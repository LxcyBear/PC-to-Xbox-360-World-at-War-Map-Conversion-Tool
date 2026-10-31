[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# One NEW isolated 8509-root diagnostic. Link 6 original-PC
# GfxWorld GfxImage* name references to existing GfxImage ROOT shells.
# This does NOT convert any pixels, texture headers, highmips or
# prove generated-image hardware semantics. NO Xbox deployment.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$priorPath=Join-Path $base 'pc_g11_image_headers_probe\oat_loaded_generated_image_headers\aztec_gate11_independent_image_headers_and_zero_stream.json'
$lightingPath=Join-Path $base 'pc_g11_lighting_combined_probe\oat_loaded_lighting_shadow_region\aztec_gate11_independent_lighting_shadow_source_digest.json'
$sourcePath=Join-Path $base 'pc_g11_image_stream_closure\aztec_gate11_6_images_and_native_stream_shape.json'
$builder=Join-Path $PSScriptRoot 'Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1'
$verifier=Join-Path $PSScriptRoot 'Verify-AztecGate11OatLoadedCells.ps1'
$order=Join-Path $PSScriptRoot 'Test-AztecGate11ImageHeaderNativeOrder.ps1'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$work=Join-Path $base 'pc_g11_image_root_links_probe'
$emitReport=Join-Path $work 'aztec_pc_world_core_integrated_oat_run.json'
$cellReport=Join-Path $work 'oat_loaded_11_cell_aabb_readback\aztec_gate11_independent_cells_aabb_readback.json'
$out=Join-Path $work 'oat_loaded_six_image_root_links'
$report=Join-Path $out 'aztec_gate11_independent_six_native_name_root_links.json'
$oldSha='D0120CE57CD558D680AF805A63752B19F97500B555295F7F548D4A2F4B1B9A09'
$lightingSha='B832EDCE7EECFBBF2B0A141FF2C3D946595B317D7E7C7ACA0D21991277522883'
foreach($path in @($priorPath,$lightingPath,$sourcePath,$builder,$verifier,$order,$oat)){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing source/native OAT image proof: $path"}
}
$p=Get-Content -LiteralPath $priorPath -Raw|ConvertFrom-Json
$l=Get-Content -LiteralPath $lightingPath -Raw|ConvertFrom-Json
$s=Get-Content -LiteralPath $sourcePath -Raw|ConvertFrom-Json
if([string]$p.schema -ne 'WaW360.AztecGate11IndependentSourceImageHeaders.v1' -or
   [string]$p.status -ne 'AZTEC_GATE11_PC_REFLECTION_LIGHTMAP_HEADER_OAT_PASS_SIX_IMAGE_PAYLOADS_UNPROVEN' -or
   [string]$p.newNativeFfSha256 -ine $oldSha -or [long]$p.newNativeFfBytes -ne 4510670L -or
   [string]$p.priorLightingWorldFfSha256 -ine $lightingSha -or
   [int]$p.sourceReflectionOriginsExact -ne 2 -or
   [int]$p.sourceLightmapHeadersLoaded -ne 2 -or
   [int]$p.sourceGeneratedImagePointersIntentionallyNull -ne 6 -or
   -not [bool]$p.previousSourceLightGridShadowAndSkyBrushWaterIntact -or
   [bool]$p.realXboxRuntimeTextureBindingReady -or [bool]$p.xboxHardwareReady -or
   [string]$l.newNativeFfSha256 -ine $lightingSha -or
   [string]$s.status -ne 'AZTEC_GATE11_SIX_IMAGE_NAMES_NATIVE_REFERENCE_MATCH_AND_NATIVE_STREAM_EMPTY_STRUCT_PROVEN_PAYLOAD_PENDING' -or
   -not [bool]$s.originalAndCurrentNativeEmptyStreamStructsMatch -or
   -not [bool]$s.originalAndNativeReflectionLightmapNamesMatch -or
   [int]$s.nativeReferenceImageNameMatches -ne 6 -or
   [int]$s.nativeReferenceImagePointersPresent -ne 6 -or
   [int]$s.imageFieldsWithoutAvailableOwnedPixelProof -ne 6 -or
   [bool]$s.fullGate11Passed -or [bool]$s.xboxHardwareReady){
    throw 'Exact previous 4,510,670-byte source/asset names, empty stream or hardware fences changed.'
}
$old=[string]$p.newNativeFf
$oldLighting=[string]$l.newNativeFf
foreach($pair in @(@($old,$oldSha),@($oldLighting,$lightingSha))){
    if(-not(Test-Path -LiteralPath ([string]$pair[0]) -PathType Leaf) -or
       (Get-FileHash -LiteralPath ([string]$pair[0]) -Algorithm SHA256).Hash -ine [string]$pair[1]){
        throw "Proven prior native world FF no longer SHA matches: $($pair[0])"
    }
}
$names=@(
    '*reflection_probe0','*reflection_probe1',
    '*lightmap0_primary','*lightmap0_secondary',
    '*lightmap1_primary','*lightmap1_secondary'
)
$fields=@(
    'GfxWorld.reflectionProbes[0].reflectionImage',
    'GfxWorld.reflectionProbes[1].reflectionImage',
    'GfxWorld.lightmaps[0].primary',
    'GfxWorld.lightmaps[0].secondary',
    'GfxWorld.lightmaps[1].primary',
    'GfxWorld.lightmaps[1].secondary'
)
$ids=New-Object 'System.Collections.Generic.HashSet[int]'
for($i=0;$i -lt 6;$i++){
    $rows=@($s.imageFields|Where-Object {[string]$_.worldPointerField -ceq $fields[$i]})
    if($rows.Count -ne 1 -or
       [string]$rows[0].originalPcImageName -cne $names[$i] -or
       [string]$rows[0].classification -ne 'NO_SOURCE_PIXEL_PROOF' -or
       [bool]$rows[0].verifiedPcOwnedXenonImageBody -or
       -not $ids.Add([int]$rows[0].nativeImageRootIdentityIndex)){
        throw "Not a unique original PC name-only GfxImage identity at $($fields[$i])"
    }
}
Write-Host '[AZTEC-GATE11-IMG-ROOT] ================================================================='
Write-Host '[AZTEC-GATE11-IMG-ROOT] Six GfxWorld root aliases; all six image pixel bodies remain UNPROVEN.'
Write-Host '[AZTEC-GATE11-PROGRESS] [##------------------] 10% | source native child order'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $order
if($LASTEXITCODE -ne 0){throw 'Pinned T4 Xenon image/GfxWorld source order test failed.'}
Write-Host '[AZTEC-GATE11-PROGRESS] [######--------------] 30% | one new isolated FF and real OAT first load'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $builder -IntegrateWorld -ProbeLevel Full -DpvsStaticProbe -Gate8ChildProbe -Gate8ChildLevel All -Gate8AllModelProbe -Gate10StaticDrawProbe -Gate11CellProbe -Gate11PrimitiveProbe -Gate11LightingProbe -Gate11ImageHeadersProbe -Gate11ImageRootLinksProbe
if($LASTEXITCODE -ne 0){throw "New root alias FF build/OAT failed; inspect $work\integrated_emitter.log"}
if(-not(Test-Path -LiteralPath $emitReport -PathType Leaf)){throw 'Native root alias FF emission manifest missing.'}
$m=Get-Content -LiteralPath $emitReport -Raw|ConvertFrom-Json
if([string]$m.status -ne 'AZTEC_GATE11_SIX_IMAGE_ROOT_NAME_SHELL_LINKS_OAT_PASS_PIXELS_UNPROVEN' -or
   -not [bool]$m.OatPass -or
   -not [bool]$m.gate11ImageRootLinksProbe -or
   [int]$m.gate11ImageRootNamesLinked -ne 6 -or
   [int]$m.gate11ImageShellPixelPayloadsVerified -ne 0 -or
   [int]$m.gate11SourceReflectionHeaderCount -ne 2 -or
   [int]$m.gate11SourceLightmapHeaderCount -ne 2 -or
   [int]$m.gate10StockModelDrawUnverified -ne 1 -or
   [int]$m.gate11SourceCellsInFf -ne 11 -or
   [int]$m.gate11AabbTreesInFf -ne 740 -or
   [bool]$m.gate11ImageRootLinksIndependentReadbackProven -or
   [bool]$m.gate11LightmapImagePayloadsResolved -or
   [bool]$m.hardwareReady){
    throw 'First OAT did not load exact name-shell alias candidate under release fences.'
}
$new=[string]$m.outputFastFile
$newSha=(Get-FileHash -LiteralPath $new -Algorithm SHA256).Hash
$newSize=[long](Get-Item -LiteralPath $new).Length
if($newSha -ieq $oldSha -or
   [IO.Path]::GetFullPath($new) -ieq [IO.Path]::GetFullPath($old)){
    throw 'New reference FF is not an isolated, distinct pointer candidate.'
}
Write-Host "[AZTEC-GATE11-SIZE] previous=4510670B new=$newSize B delta=$($newSize-4510670L) B"
Write-Host '[AZTEC-GATE11-PROGRESS] [###########---------] 55% | independent original PC 11-cell/740-tree/14974-index comparison'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $verifier -ImageRootLinksCandidate
if($LASTEXITCODE -ne 0 -or -not(Test-Path -LiteralPath $cellReport -PathType Leaf)){
    throw 'Second independent GfxCell/AABB native OAT readback rejected source pointer FF.'
}
$c=Get-Content -LiteralPath $cellReport -Raw|ConvertFrom-Json
if([string]$c.actualGfxWorldFfSha256 -ine $newSha -or
   -not [bool]$c.sourceOwnedImageRootLinksCandidate -or
   [int]$c.exactLoadedCounts.cellOrdinals -ne 11 -or
   [int]$c.exactLoadedCounts.treeOrdinals -ne 740 -or
   [int]$c.exactLoadedCounts.treeModelIndexes -ne 14974){
    throw 'Second OAT 11-cell/740-tree proof does not belong to exact new pointer FF.'
}
Write-Host '[AZTEC-GATE11-PROGRESS] [##############------] 70% | third independent native OAT resolution of SIX GfxImage root names'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('g11_imgroots_'+[Guid]::NewGuid().ToString('N').Substring(0,10))
New-Item -ItemType Directory -Force -Path $temp,$out|Out-Null
$log=Join-Path $out 'independent_image_name_root_alias_oat.log'
try{
    $save=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $oat '--no-color' '--include-assets' 'gfxworld' '--output-folder' $temp $new 2>&1|
            ForEach-Object {[string]$_}|Set-Content -LiteralPath $log -Encoding UTF8
        $code=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$save}
    if($code -ne 0){
        Get-Content -LiteralPath $log -Tail 35 | ForEach-Object {Write-Host "[AZTEC-GATE11-IMG-ROOT] OAT $_"}
        throw 'Third OAT source GfxWorld six-name pointer test FAILED.'
    }
    $hits=@(Get-ChildItem -LiteralPath $temp -Recurse -File -Filter '*.json'|
        Where-Object {$_.FullName -match 'xenon_gfxworld_surface_abi_witness'})
    if($hits.Count -ne 1){throw "Expected 1 loaded native image pointer witness, got $($hits.Count)"}
    $w=Get-Content -LiteralPath $hits[0].FullName -Raw|ConvertFrom-Json
    $im=$w.gate11WorldImageSlots
    $stream=$w.gate11StreamShape
    $grid=$w.nativeLightingGraph
    $pr=$w.nativePrimitiveGraph
    if([string]$im.schema -ne 'WaW360.T4XenonGate11NativeWorldImageNameReferences.v1' -or
       -not [bool]$im.slotHeadersReady -or
       @($im.reflectionProbeReferences).Count -ne 2 -or
       @($im.lightmapReferences).Count -ne 2 -or
       [bool]$im.imagePixelsReadOrCopied -or
       [bool]$im.sourceOwnedImagePayloadsVerified -or
       [bool]$im.hardwareRuntimeSemanticsProven){
        throw 'New FF loaded GfxWorld image alias witness incorrectly reports missing/verified pixel data.'
    }
    for($j=0;$j -lt 2;$j++){
        $probe=$im.reflectionProbeReferences[$j]
        $lm=$im.lightmapReferences[$j]
        if([int]$probe.ordinal -ne $j -or -not [bool]$probe.imagePointerPresent -or
           [string]$probe.imageName -cne $names[$j] -or
           [int]$lm.ordinal -ne $j -or
           -not [bool]$lm.primaryPointerPresent -or
           -not [bool]$lm.secondaryPointerPresent -or
           [string]$lm.primaryName -cne $names[2+2*$j] -or
           [string]$lm.secondaryName -cne $names[3+2*$j]){
            throw "Loaded GfxWorld image ROOT ALIASES did not resolve to exact 3 names at pair $j"
        }
        Write-Host "[AZTEC-GATE11-IMG-ROOT] MATCH reflection $($probe.imageName); lightmap $($lm.primaryName), $($lm.secondaryName)"
    }
    if([int]$stream.treeCount -ne 0 -or
       [int]$stream.leafRefCount -ne 0 -or
       [bool]$stream.treePointerPresent -or [bool]$stream.leafPointerPresent -or
       -not [bool]$stream.boundedCountsAndPointers){
        throw 'New pointer FF lost measured 0/0 NULL Xbox stream structure.'
    }
    $origGrid=$l.gridNativeLoadedDigests
    if(-not [bool]$grid.gridArraysReady -or
       -not [bool]$grid.shadowRegionHeadersReady -or
       [string]$grid.gridRowDigest -ine [string]$origGrid.gridRowDataStart -or
       [string]$grid.gridRawDigest -ine [string]$origGrid.gridRawRowData -or
       [string]$grid.gridEntryDigest -ine [string]$origGrid.gridEntries -or
       [string]$grid.gridColorDigest -ine [string]$origGrid.gridColors -or
       [int]$grid.primaryLights -ne 2 -or
       ([int]$grid.shadows[0].smodelCount+[int]$grid.shadows[1].smodelCount) -ne 1903 -or
       -not [bool]$pr.arraysAvailable){
        throw 'New pointer FF regressed earlier source-owned light-grid, shadows or sky/brush/water.'
    }
    if((Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash -ine $oldSha -or
       (Get-FileHash -LiteralPath $oldLighting -Algorithm SHA256).Hash -ine $lightingSha -or
       (Get-FileHash -LiteralPath $new -Algorithm SHA256).Hash -ine $newSha){
        throw 'New native FF or protected OAT-passing source baselines changed.'
    }
    $result=[ordered]@{
        schema='WaW360.AztecGate11SixImageRootNameAliasReadback.v1'
        status='AZTEC_GATE11_SIX_GFXIMAGE_NAME_ONLY_ROOT_POINTERS_NATIVE_OAT_PASS_PIXEL_DATA_MISSING'
        newNativeFf=$new
        newNativeFfSha256=$newSha
        newNativeFfBytes=$newSize
        protectedHeaderFfSha256=$oldSha
        protectedLightingFfSha256=$lightingSha
        sourceNamedGfxImagePointersResolved=6
        reflectionPointersResolved=2
        lightmapPointersResolved=4
        previous11Cells740Aabb14974IndexesStillMatch=$true
        previous4LightGridGroupsAnd1903ShadowRefsStillMatch=$true
        nativeEmptyStreamPointersStillMatch=$true
        realOriginalPcGeneratedImagePixelsProven=0
        actualGfxImageXenosTexturePayloadBodiesAvailable=$false
        rootsAreNameOnlyShells=$true
        fullGate11Passed=$false
        xboxHardwareReady=$false
    }
    $result|ConvertTo-Json -Depth 9|Set-Content -LiteralPath $report -Encoding UTF8
    Write-Host '[AZTEC-GATE11-PROGRESS] [####################] 100% | six name-only image root POINTERS native OAT PASS, all original-PC source arrays unchanged'
    Write-Host "[AZTEC-GATE11-SIZE] previous=4510670B new=$newSize B delta=$($newSize-4510670L) B"
    Write-Host "[AZTEC-GATE11-IMG-ROOT] Report=$report"
    Write-Host '[AZTEC-GATE11-IMG-ROOT] SIX actual image pixels/texture bodies still unproven; stock XModel/material stateBits unresolved. Gate11 10/13 NO XBOX.'
}finally{
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
