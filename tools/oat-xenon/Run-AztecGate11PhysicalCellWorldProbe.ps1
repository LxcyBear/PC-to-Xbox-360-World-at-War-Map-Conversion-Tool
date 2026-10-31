[CmdletBinding()]
param([switch]$BuildDumper)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# Isolated PC-derived physical Gate11 GfxWorld cell/AABB FF probe.
# Never overwrite previously proven Gate10 map FF; no hardware deployment.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$g10Path=Join-Path $base 'pc_g10_1903\oat_loaded_1903_draw_readback\aztec_gate10_independent_1903_draw_readback.json'
$sourcePath=Join-Path $base 'pc_g10_1903\gate11_cell_topology_source\aztec_gate11_pc_source_cells_native_abi.json'
$work=Join-Path $base 'pc_g11_cells_740'
$report=Join-Path $work 'aztec_pc_world_core_integrated_oat_run.json'
$ff=Join-Path $work 'scratch_emit\nazi_zombie_aztec.ff'
$builder=Join-Path $PSScriptRoot 'Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1'
$verifier=Join-Path $PSScriptRoot 'Verify-AztecGate11OatLoadedCells.ps1'
$oatBuild=Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1'
$capPath=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
foreach($p in @($g10Path,$sourcePath,$builder,$verifier,$oatBuild)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing Gate11 prerequisite: $p"}
}
$g10=Get-Content -LiteralPath $g10Path -Raw|ConvertFrom-Json
$source=Get-Content -LiteralPath $sourcePath -Raw|ConvertFrom-Json
if([string]$g10.status -ne 'AZTEC_GATE10_1903_ALL_SOURCE_DRAW_FIELDS_OAT_READBACK_PASS_STOCK_UNVERIFIED' -or
   [int]$g10.actualNativeDrawCount -ne 1903 -or
   [string]$g10.stage4EEmittedFfSha256 -ine '0BB41398C4236EF33CB9D60F923E09152966D52AA623DAEFC33D731A42630564' -or
   [string]$source.status -ne 'AZTEC_GATE11_11_CELL_740_AABB_TYPED_SOURCE_NATIVE_ABI_PASS_NOT_EMITTED' -or
   -not [bool]$source.typedPcCellGraphSourcePass -or
   [int]$source.originalPcTypedAabbTreeCount -ne 740 -or [bool]$source.hardwareReady){
    throw 'Gate10 original FF or original-PC source/native Gate11 11-cell/740-tree proof differs.'
}
$priorFF=[string]$g10.stage4EEmittedFf
$sourceJson=[string]$source.originalPcTypedCellAabbGraph
foreach($p in @($priorFF,$sourceJson)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Gate10 FF/source cell JSON missing: $p"}
}
$originalHash=(Get-FileHash -LiteralPath $priorFF -Algorithm SHA256).Hash
if($originalHash -ine [string]$g10.stage4EEmittedFfSha256 -or
   (Get-FileHash -LiteralPath $sourceJson -Algorithm SHA256).Hash -ine
      [string]$source.originalPcTypedCellAabbGraphSha256){
    throw 'Gate10 FF or typed original-PC source AABB graph SHA changed.'
}
function Has-Witness{
    if(-not(Test-Path -LiteralPath $capPath -PathType Leaf)){return $false}
    $cap=Get-Content -LiteralPath $capPath -Raw|ConvertFrom-Json
    return ([string]$cap.format -eq 'WaW360.OatGraphDumperCapabilities.v1' -and
        [string]$cap.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($cap.capabilities) -contains 't4xenon-gfxworld-gate11-loaded-cell-aabb-typed-witness-v1')
}
if(-not(Has-Witness)){
    if(-not $BuildDumper){
        throw 'New independently OAT-loaded cell/AABB witness not installed. Re-run ONCE with -BuildDumper.'
    }
    Write-Host '[AZTEC-GATE11-FF] Installing new original-PC AABB loaded-field OAT readback witness.'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $oatBuild
    if($LASTEXITCODE -ne 0 -or -not(Has-Witness)){throw 'New pinned specialized OAT witness build FAILED.'}
}
Write-Host '[AZTEC-GATE11-FF] ========================================================'
Write-Host '[AZTEC-GATE11-FF] Source-owned 11 GfxCells + 740 nested AABBs + static-model indices.'
Write-Host '[AZTEC-GATE11-FF] New output pc_g11_cells_740; proven Gate10 SHA locked and unchanged.'
Write-Host '[AZTEC-GATE11-PROGRESS] [##------------------] 10% | original-PC typed graph and baseline checks'
$builderArgs=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$builder,
    '-IntegrateWorld','-ProbeLevel','Full','-DpvsStaticProbe',
    '-Gate8ChildProbe','-Gate8ChildLevel','All','-Gate8AllModelProbe',
    '-Gate10StaticDrawProbe','-Gate11CellProbe')
& powershell.exe @builderArgs
$code=[int]$LASTEXITCODE
$after=(Get-FileHash -LiteralPath $priorFF -Algorithm SHA256).Hash
if($originalHash -ine $after){throw 'CRITICAL: Passing Gate10 FF SHA changed; abort.'}
if($code -ne 0){
    throw "Gate11 isolated FF first OAT loader failed (exit=$code). Gate10 preserved. Inspect $work\integrated_emitter.log."
}
if(-not(Test-Path -LiteralPath $report -PathType Leaf) -or
   -not(Test-Path -LiteralPath $ff -PathType Leaf)){
    throw 'Gate11 FF or actual native OAT load report not produced.'
}
$r=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
if([string]$r.status -ne 'AZTEC_GATE11_11_CELL_740_AABB_DIAGNOSTIC_OAT_LOAD_PASS_REMAINDER_PENDING' -or
   -not [bool]$r.OatPass -or
   [int]$r.gate11SourceCellsInFf -ne 11 -or
   [int]$r.gate11AabbTreesInFf -ne 740 -or
   [int]$r.gate11SmodelIndicesInFf -ne [int]$source.originalPcTypedSmodelIndicesCount -or
   [int]$r.gate11ReflectionIndicesInFf -ne [int]$source.originalPcTypedCellReflectionProbeIndicesCount -or
   [bool]$r.gate11FullWorldClosureApproved -or [bool]$r.hardwareReady){
    throw 'Gate11 OAT load did not confirm all source-owned cell / AABB counts.'
}
Write-Host '[AZTEC-GATE11-PROGRESS] [################----] 80% | independently OAT loaded cell/tree scalar and index comparison'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $verifier
if($LASTEXITCODE -ne 0){throw 'Gate11 physical-cell independent OAT readback failed. Do not deploy.'}
$size=[long](Get-Item -LiteralPath $ff).Length
$hash=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
Write-Host "[AZTEC-GATE11-FF] New diagnostic FF=$size B SHA=$hash"
Write-Host '[AZTEC-GATE11-FF] 11/11 cells + 740/740 AABB + every typed model/reflection index OAT readback PASS.'
Write-Host '[AZTEC-GATE11-FF] Other GfxWorld children/lightmaps/runtime still pending; Xbox approval remains FALSE.'
Write-Host '[AZTEC-GATE11-FF] Original Gate10 FF preserved unchanged.'
