[CmdletBinding()]
param([switch]$SkipPcModelConversion)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# Actual PC->Xenon body conversion and native world FF build.
# The earlier 5-model OAT PASS is never overwritten. The new FF is an
# isolated production-asset-development candidate, NOT a released map.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$stage=Join-Path $PSScriptRoot 'Stage-AztecAllStaticSourceXModelGraphs.ps1'
$builder=Join-Path $PSScriptRoot 'Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1'
$n=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$report=Join-Path $n 'pc_gate8_29x_ff\aztec_pc_world_core_integrated_oat_run.json'
$stagingProof=Join-Path $repo '.deps\aztec8-all-static-xmodels\aztec_gate8_29_xmodel_staging.json'
$old=$ErrorActionPreference
Write-Host '[AZTEC-REAL-FF] ====================================================='
Write-Host '[AZTEC-REAL-FF] CLASSIFY 27 SOURCE MODEL TYPES: CONVERT REAL OWNED BODIES, RETAIN PROVEN EXTERNAL REFERENCES'
Write-Host '[AZTEC-REAL-FF] Recompile REAL model graphs into 8509-root Xbox FF; OAT readback.'
Write-Host '[AZTEC-REAL-FF] Preserve existing PC source and passing 5-model baseline.'
Write-Host '[AZTEC-REAL-FF] NO ARTIFICIAL SIZE PADDING OR DONOR MODEL BYTES.'
try{
    $params=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$stage)
    if($SkipPcModelConversion){$params+='-SkipPcModelConversion'}
    $ErrorActionPreference='Continue'
    & powershell.exe @params 2>&1|ForEach-Object {Write-Host "[AZTEC-REAL-FF] $_"}
    $stageExit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($stageExit -ne 0){throw 'PC-owned model conversion or source/native-reference classification blocked. Previous five-XModel map unchanged.'}
$stageProof=Get-Content -LiteralPath $stagingProof -Raw|ConvertFrom-Json
if([string]$stageProof.status -ne 'AZTEC_GATE8_ALL_27_PC_STATIC_XMODEL_IDENTITIES_CLASSIFIED_GRAPHS_STAGED' -or
   [int]$stageProof.sourceModels -ne 27 -or
   [int]$stageProof.staticDrawsRepresented -ne 1903 -or
   [int]$stageProof.realOwnedStaticModelTypes + [int]$stageProof.nativeExternalReferenceModelTypes -ne 27 -or
   [int]$stageProof.modelCount -ne (2 + [int]$stageProof.realOwnedStaticModelTypes)){
    throw 'Source model ownership/classified native-reference stage is not complete.'
}
try{
    $ErrorActionPreference='Continue'
    $builderArgs=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$builder,
        '-IntegrateWorld','-ProbeLevel','Full','-DpvsStaticProbe',
        '-Gate8ChildProbe','-Gate8ChildLevel','All','-Gate8AllModelProbe')
    & powershell.exe @builderArgs 2>&1 |
        ForEach-Object {Write-Host "[AZTEC-REAL-FF] $_"}
    $emitExit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($emitExit -ne 0 -or -not(Test-Path -LiteralPath $report -PathType Leaf)){
    throw "Source-owned model FF did not emit/OAT load. See $report; previous five-model FF remains untouched."
}
$r=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
$ff=[string]$r.outputFastFile
if([string]$r.schema -ne 'WaW360.AztecPcGfxWorldIntegratedFastFileOatRun.v1' -or
   [string]$r.status -ne 'AZTEC_GATE8_ALL_CLASSIFIED_STATIC_XMODEL_TYPES_8509_ROOT_WORLD_OAT_PASS' -or
   [int]$r.realXModelRootsInCurrentFf -ne [int]$stageProof.modelCount -or
   [int]$r.sourceOwnedStaticModelTypes -ne [int]$stageProof.realOwnedStaticModelTypes -or
   [int]$r.verifiedNativeExternalModelReferences -ne [int]$stageProof.nativeExternalReferenceModelTypes -or
   [int]$r.realPcGfxSurfaceCount -ne 735 -or
   [int]$r.realPcGfxWorldMaterialRoots -ne 39 -or
   -not [bool]$r.OatPass -or
   -not [bool]$r.all27StaticSourceXModelProbe -or
   [bool]$r.hardwareReady -or
   [bool]$r.allOriginalPcGfxWorldDpvsChildrenComplete -or
   -not(Test-Path -LiteralPath $ff -PathType Leaf)){
    throw 'Original-PC source-owned model bodies and native references did not match real Xbox graph/OAT evidence.'
}
$sha=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
$length=(Get-Item -LiteralPath $ff).Length
Write-Host '[AZTEC-REAL-FF] ====================================================='
Write-Host "[AZTEC-REAL-FF] PASS: $($r.status)"
Write-Host "[AZTEC-REAL-FF] REAL Xbox source-derived XModel bodies=$($r.realXModelRootsInCurrentFf) (original two + $($stageProof.realOwnedStaticModelTypes) static types)."
Write-Host "[AZTEC-REAL-FF] EXTERNAL native reference model types=$($stageProof.nativeExternalReferenceModelTypes), source typed model names=27/27, world surfaces=735"
Write-Host '[AZTEC-REAL-FF] Source placement count 1903 is classified, NOT YET physically linked into GfxWorld.'
Write-Host "[AZTEC-REAL-FF] physical bytes=$length sha256=$sha"
Write-Host "[AZTEC-REAL-FF] FF=$ff"
Write-Host '[AZTEC-REAL-FF] NEXT real integration target: 1903 XModel draw links/complete world DPVS+required image/runtime assets.'
Write-Host '[AZTEC-REAL-FF] NO hardware ready claim; this is actual source-payload progress, not cosmetic polish.'