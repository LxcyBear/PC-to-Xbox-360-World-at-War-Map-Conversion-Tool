[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# ONE isolated Stage 4D/4E OAT candidate. This does NOT mark Gate11 PASS:
# only sky starts, 183 brush models and two source-owned water float buffers
# have been added to the previously independently OAT-verified 11-cell world.
# No image/material/shadow/streamInfo oracle bytes and NO XBOX deployment.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$preflight=Join-Path $base 'pc_g11_cells_740\gate11_remaining_unified_source\aztec_gate11_combined_no_ff_build_preflight.json'
$builder=Join-Path $PSScriptRoot 'Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1'
$work=Join-Path $base 'pc_g11_remaining_world_primitive_probe'
$report=Join-Path $work 'aztec_pc_world_core_integrated_oat_run.json'
$expectedBaseline='05CEEC82AF5D58706BA5A6EE24995D09A3FB764F3E547A8B4758A9347ECF2862'
foreach($p in @($preflight,$builder)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Gate11 source-preflight prerequisite missing: $p"
    }
}
$p=Get-Content -LiteralPath $preflight -Raw | ConvertFrom-Json
if([string]$p.schema -ne 'WaW360.AztecGate11CombinedNativeOneBuildPreparation.v1' -or
   [string]$p.status -ne 'AZTEC_GATE11_SOURCE_LEAF_13_NATIVE_ENDIAN_PASS_IMAGE_IDENTITIES_AUDITED_FULL_FF_BLOCKED' -or
   [int]$p.originalSourceLeafArraysConverted -ne 13 -or
   [long]$p.nativeEndianLeafBytes -ne 6930524L -or
   -not [bool]$p.endiannessRoundTripProven -or
   [string]$p.previousPassingGate11FfSha256 -ine $expectedBaseline -or
   [bool]$p.currentFfEmissionPerformed -or [bool]$p.fullGate11Passed -or
   [bool]$p.XboxHardwareReady){
    throw 'Gate11 source graph, typed endian conversions, or release fences have changed. Abort.'
}
$old=[string]$p.previousPassingGate11Ff
if(-not(Test-Path -LiteralPath $old -PathType Leaf)){
    throw "Previously OAT-verified 3,642,932-byte Gate11 FF missing: $old"
}
$originalHash=(Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash
if($originalHash -ine $expectedBaseline){
    throw 'Independently OAT-loaded 11-cell/740-AABB FF SHA changed; no experimental build allowed.'
}
$originalBytes=[long](Get-Item -LiteralPath $old).Length
if($originalBytes -ne 3642932L){
    throw "Protected Gate11 FF size drifted: $originalBytes"
}
Write-Host '[AZTEC-GATE11-PRIMITIVE] ==================================================================='
Write-Host '[AZTEC-GATE11-PRIMITIVE] NEW ISOLATED NATIVE FF: sky starts (2) + brush models (183) + water (2 buffers).'
Write-Host '[AZTEC-GATE11-PRIMITIVE] Original PC big-endian leaf source only. Prior OAT FF SHA locked.'
Write-Host '[AZTEC-GATE11-PRIMITIVE] Other 9 leaf arrays, image/lightgrid/reflection/shadow/streaming graphs remain gated.'
Write-Host '[AZTEC-GATE11-PROGRESS] [####----------------] 20% | protect passing 11-cell world and verify source leaf manifest'
$builderArgs=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$builder,
    '-IntegrateWorld','-ProbeLevel','Full','-DpvsStaticProbe',
    '-Gate8ChildProbe','-Gate8ChildLevel','All','-Gate8AllModelProbe',
    '-Gate10StaticDrawProbe','-Gate11CellProbe','-Gate11PrimitiveProbe')
& powershell.exe @builderArgs
$code=[int]$LASTEXITCODE
if((Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash -ine $originalHash){
    throw 'CRITICAL: independently OAT-proven Gate11 source FF was modified.'
}
if($code -ne 0){
    Write-Host "[AZTEC-GATE11-PRIMITIVE] New isolated build/OAT error; examine $work\build.log and $work\integrated_emitter.log"
    throw "Gate11 primitive candidate build/OAT failed ($code). Original passing FF unchanged."
}
if(-not(Test-Path -LiteralPath $report -PathType Leaf)){
    throw "New Stage 4D/4E output report missing: $report"
}
$r=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if([string]$r.schema -ne 'WaW360.AztecPcGfxWorldIntegratedFastFileOatRun.v1' -or
   [string]$r.status -ne 'AZTEC_GATE11_PRIMITIVE_SKY_BRUSH_WATER_OAT_LOAD_PASS_REMAINDER_PENDING' -or
   -not [bool]$r.OatPass -or -not [bool]$r.gate11PhysicalCellDiagnostic -or
   -not [bool]$r.gate11PrimitiveProbe -or
   [int]$r.gate11PrimitiveSkySurfaces -ne 2 -or
   [int]$r.gate11PrimitiveBrushModels -ne 183 -or
   [long]$r.gate11PrimitiveWaterBytes -ne 2097408L -or
   [bool]$r.gate11PrimitiveIndependentReadbackProven -or
   [int]$r.gate11SourceCellsInFf -ne 11 -or
   [int]$r.gate11AabbTreesInFf -ne 740 -or
   [bool]$r.gate11FullWorldClosureApproved -or
   [bool]$r.hardwareReady){
    throw 'Isolated FF was not independently accepted by the first OAT loader. Gate11 overall PENDING.'
}
$new=[string]$r.outputFastFile
if(-not(Test-Path -LiteralPath $new -PathType Leaf) -or
   [IO.Path]::GetFullPath($new) -ieq [IO.Path]::GetFullPath($old)){
    throw 'Isolated candidate FF absent or dangerously targets protected old FF.'
}
$newBytes=[long](Get-Item -LiteralPath $new).Length
$newHash=(Get-FileHash -LiteralPath $new -Algorithm SHA256).Hash
if($newBytes -le $originalBytes -or $newHash -ieq $originalHash){
    throw "Candidate size/hash not distinct from protected cell FF: $newBytes / $newHash"
}
Write-Host '[AZTEC-GATE11-PROGRESS] [####################] 100% | first native OAT load passed; independent field readback PENDING'
Write-Host "[AZTEC-GATE11-PRIMITIVE] Prior FF=$originalBytes B SHA=$originalHash (UNCHANGED)"
Write-Host "[AZTEC-GATE11-PRIMITIVE] New FF=$newBytes B SHA=$newHash"
Write-Host '[AZTEC-GATE11-PRIMITIVE] Sky=2; brush models=183; two source-owned water float arrays=2,097,408 uncompressed bytes.'
Write-Host '[AZTEC-GATE11-PRIMITIVE] OAT first load PASS. New groups still require independent per-field OAT readback.'
Write-Host '[AZTEC-GATE11-PRIMITIVE] Gate 11 overall=10/13 PENDING; other GfxWorld branches and Xbox testing NOT approved.'
