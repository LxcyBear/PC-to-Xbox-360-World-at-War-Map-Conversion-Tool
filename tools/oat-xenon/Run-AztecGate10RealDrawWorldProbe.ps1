[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Actual isolated Stage4D/4E 1903-draw GfxWorld child + OAT test.
# The prior OAT-verified Gate8 source FF must never be overwritten.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$baseline=Join-Path $base 'pc_gate8_29x_ff\scratch_emit\nazi_zombie_aztec.ff'
$builder=Join-Path $PSScriptRoot 'Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1'
$work=Join-Path $base 'pc_g10_1903'
$report=Join-Path $work 'aztec_pc_world_core_integrated_oat_run.json'
$ff=Join-Path $work 'scratch_emit\nazi_zombie_aztec.ff'
if(-not(Test-Path -LiteralPath $baseline -PathType Leaf)){
    throw "Previously verified 28-model Xbox world FF missing: $baseline"
}
if(-not(Test-Path -LiteralPath $builder -PathType Leaf)){
    throw "Integrated world builder missing: $builder"
}
$priorHash=(Get-FileHash -LiteralPath $baseline -Algorithm SHA256).Hash
$priorLen=[long](Get-Item -LiteralPath $baseline).Length
Write-Host '[AZTEC-GATE10-FF] ==========================================================='
Write-Host '[AZTEC-GATE10-PROGRESS] [##------------------] 10% | baseline and source checks'
Write-Host '[AZTEC-GATE10-FF] REAL Stage4D/4E GfxWorld child array + OAT loader test.'
Write-Host '[AZTEC-GATE10-FF] New isolated diagnostic output: pc_g10_1903.'
Write-Host '[AZTEC-GATE10-FF] No donor bytes. One stock model remains unverified.'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $builder -IntegrateWorld -ProbeLevel Full -DpvsStaticProbe -Gate8ChildProbe -Gate8ChildLevel All -Gate8AllModelProbe -Gate10StaticDrawProbe
$exit=[int]$LASTEXITCODE
$afterHash=(Get-FileHash -LiteralPath $baseline -Algorithm SHA256).Hash
if($afterHash -ine $priorHash){
    throw 'CRITICAL: baseline OAT FF unexpectedly changed. Investigate before any more tests.'
}
if($exit -ne 0){
    Write-Host "[AZTEC-GATE10-FF] New-world diagnostic OAT/FF exit=$exit"
    Write-Host '[AZTEC-GATE10-FF] Passing 28-model FF SHA UNCHANGED.'
    Write-Host "[AZTEC-GATE10-FF] Source emission log: $(Join-Path $work 'integrated_emitter.log')"
    throw 'Physical draw-array OAT canary failed. Do not deploy or rebuild again without investigating the loader evidence.'
}
if(-not(Test-Path -LiteralPath $report -PathType Leaf) -or
   -not(Test-Path -LiteralPath $ff -PathType Leaf)){
    throw 'Gate10 attempted OAT but no physical FF and final gate report were produced.'
}
$gate=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
if([string]$gate.schema -ne 'WaW360.AztecPcGfxWorldIntegratedFastFileOatRun.v1' -or
   [string]$gate.status -ne 'AZTEC_GATE10_1903_STATIC_DRAW_DIAGNOSTIC_OAT_PASS_STOCK_UNVERIFIED' -or
   -not [bool]$gate.OatPass -or
   -not [bool]$gate.gate10SourceDrawDiagnostic -or
   [int]$gate.gate10DrawRecordsInEmittedFf -ne 1903 -or
   [int]$gate.gate10StockModelDrawUnverified -ne 1 -or
   [bool]$gate.gate10ProductionApproved -or
   [bool]$gate.hardwareReady -or
   [bool]$gate.fullNativeWorldSourceClosurePass -or
   [bool]$gate.bothMainAndLoadFfsCompleted){
    throw 'OAT/readback report is not the verified 1903-draw DIAGNOSTIC only; original FF intact.'
}
$actualSha=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
$newLen=[long](Get-Item -LiteralPath $ff).Length
Write-Host '[AZTEC-GATE10-PROGRESS] [####################] 100% | diagnostic FF emitted and OAT list PASS'
Write-Host '[AZTEC-GATE10-FF] SOURCE-ONLY DIAGNOSTIC OAT PASS: 1903 draw records serialized in GfxWorld.'
Write-Host '[AZTEC-GATE10-FF] PC-backed model pointers=1902; stock hardware residency UNPROVEN=1.'
Write-Host "[AZTEC-GATE10-SIZE] Previous verified FF=$priorLen B, new FF=$newLen B, delta=$($newLen-$priorLen) B"
Write-Host "[AZTEC-GATE10-FF] New FF SHA256=$actualSha"
Write-Host "[AZTEC-GATE10-FF] Report=$report"
Write-Host '[AZTEC-GATE10-FF] Next: independent per-record OAT witness and source-light/stock closure.'
Write-Host '[AZTEC-GATE10-FF] NOT complete Gate10, not a playable map, not Xbox deployable.'
