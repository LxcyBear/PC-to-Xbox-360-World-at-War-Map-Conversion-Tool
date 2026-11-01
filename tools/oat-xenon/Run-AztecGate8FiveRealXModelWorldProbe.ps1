[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# Five source XModel graph bodies -> opt-in 8509-root GfxWorld diagnostic.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$stageScript=Join-Path $PSScriptRoot 'Stage-AztecGate8FiveSourceXModelGraphs.ps1'
$worldScript=Join-Path $PSScriptRoot 'Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1'
$report=Join-Path $base 'pc_gate8_5x_ff\aztec_pc_world_core_integrated_oat_run.json'
$stage=Join-Path $repo '.deps\aztec8-five-xmodels\aztec_gate8_five_xmodel_staging.json'
$target='AZTEC_GATE8_FIVE_REAL_XMODELS_8509_ROOT_WORLD_OAT_PASS'
foreach($p in @($stageScript,$worldScript)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing Gate8 native graph script: $p"}
}
Write-Host '[AZTEC-GATE8-5X] ======================================================'
Write-Host '[AZTEC-GATE8-5X] FIVE REAL ORIGINAL-PC XMODELS -> 8509-ROOT XENON WORLD FF'
Write-Host '[AZTEC-GATE8-5X] Existing two-model and world Gate8 OAT-PASS folders PRESERVED'
Write-Host '[AZTEC-GATE8-5X] No 1903-draw linkage, no main/_load approval, no Xbox'
Write-Host '[AZTEC-GATE8-5X] STAGING source-owned relocatable XModel graphs...'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $stageScript
if($LASTEXITCODE -ne 0){
    throw "Source-owned 5-XModel graph staging failed. See $stage"
}
if(-not(Test-Path -LiteralPath $stage -PathType Leaf)){
    throw "Five-XModel staging script did not write a report: $stage"
}
Write-Host '[AZTEC-GATE8-5X] RUNNING isolated full 8509-root FF/OAT diagnostic...'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $worldScript -IntegrateWorld -ProbeLevel Full -DpvsStaticProbe -Gate8ChildProbe -Gate8ChildLevel All -Gate8FiveModelProbe
if($LASTEXITCODE -ne 0){
    throw "Five-XModel 8509-root OAT run failed. Inspect $base\pc_gate8_5x_ff"
}
if(-not(Test-Path -LiteralPath $report -PathType Leaf)){
    throw "Five-XModel world FF OAT report absent: $report"
}
$r=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if([string]$r.schema -ne 'WaW360.AztecPcGfxWorldIntegratedFastFileOatRun.v1' -or
   [string]$r.status -ne $target -or
   -not [bool]$r.OatPass -or
   -not [bool]$r.fiveSourceOwnedXModelProbe -or
   [int]$r.realXModelRootsInCurrentFf -ne 5 -or
   -not [bool]$r.sourceBackedGate8PlaneNodeLayerProbe -or
   [bool]$r.allOriginalPcGfxWorldDpvsChildrenComplete -or
   [bool]$r.hardwareReady -or
   [bool]$r.bothMainAndLoadFfsCompleted -or
   -not(Test-Path -LiteralPath ([string]$r.outputFastFile) -PathType Leaf)){
    throw "Five-real-XModel world scratch proof incomplete or not safe for claim: $report"
}
Write-Host '[AZTEC-GATE8-5X] ======================================================'
Write-Host "[AZTEC-GATE8-5X] PASS: $target"
Write-Host '[AZTEC-GATE8-5X] Real source-owned XModel bodies upgraded in 8509-root FF: 5'
Write-Host '[AZTEC-GATE8-5X] New foliage bodies: 3, representing 1572 / 1903 static-draw model occurrences'
Write-Host '[AZTEC-GATE8-5X] Actual static-draw records bound into world: 0 / 1903'
Write-Host "[AZTEC-GATE8-5X] scratchFF=$($r.outputFastFile)"
Write-Host "[AZTEC-GATE8-5X] report=$report"
Write-Host '[AZTEC-GATE8-5X] SCRATCH/OAT ONLY. GATE8 STILL PENDING; NO XBOX TEST.'
