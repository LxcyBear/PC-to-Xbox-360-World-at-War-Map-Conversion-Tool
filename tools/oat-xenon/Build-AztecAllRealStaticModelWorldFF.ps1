[CmdletBinding()]
param([switch]$SkipPcModelConversion)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# Actual PC->Xenon body conversion and native world FF build.
# The earlier 5-model OAT PASS is never overwritten. The new FF is an
# isolated production-asset-development candidate, NOT a released map.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$stage=Join-Path $PSScriptRoot 'Stage-AztecAllStaticSourceXModelGraphs.ps1'
$stockAudit=Join-Path $PSScriptRoot 'Audit-AztecRuntimeLoadedStockXModelReferences.ps1'
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
# Resolve source comma references from the REAL Xbox expected-preloaded
# common/code_post_gfx/patch zones before classifying model owners.
# A missing Xbox retail folder is NOT permission to fabricate stock proof:
# the model wave will keep those names explicitly UNRESOLVED instead.
if(Test-Path -LiteralPath $stockAudit -PathType Leaf){
    Write-Host '[AZTEC-REAL-FF] Auditing expected preloaded Xbox stock XModel bodies (three FFs only; cached when available).'
    try{
        $ErrorActionPreference='Continue'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $stockAudit 2>&1 |
            ForEach-Object {Write-Host "[AZTEC-REAL-FF] $_"}
        $stockExit=[int]$LASTEXITCODE
    }catch{
        $stockExit=4
        Write-Host "[AZTEC-REAL-FF] WARNING: retail stock audit was unavailable: $_"
    }finally{$ErrorActionPreference=$old}
    if($stockExit -ne 0){
        Write-Host '[AZTEC-REAL-FF] WARNING: stock residency unproven. Continuing PC-owned conversion, retaining explicit unresolved references.'
    }
}
try{
    $params=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$stage)
    if($SkipPcModelConversion){$params+='-SkipPcModelConversion'}
    $ErrorActionPreference='Continue'
    & powershell.exe @params 2>&1|ForEach-Object {Write-Host "[AZTEC-REAL-FF] $_"}
    $stageExit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($stageExit -ne 0){throw 'PC-owned model conversion or source/native-reference classification blocked. Previous five-XModel map unchanged.'}
$stageProof=Get-Content -LiteralPath $stagingProof -Raw|ConvertFrom-Json
if([string]$stageProof.status -notin @('AZTEC_GATE8_ALL_27_PC_STATIC_XMODEL_IDENTITIES_CLASSIFIED_GRAPHS_STAGED',
       'AZTEC_GATE8_SOURCE_MODELS_STAGED_WITH_UNRESOLVED_REFERENCES') -or
   [int]$stageProof.sourceModels -ne 27 -or
   [int]$stageProof.staticDrawsRepresented -ne 1903 -or
   ([int]$stageProof.realOwnedStaticModelTypes + [int]$stageProof.nativeExternalReferenceModelTypes +
       [int]$stageProof.unresolvedNativeReferenceModelTypes) -ne 27 -or
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
   [string]$r.status -notin @('AZTEC_GATE8_ALL_CLASSIFIED_STATIC_XMODEL_TYPES_8509_ROOT_WORLD_OAT_PASS',
       'AZTEC_GATE8_REAL_OWNED_XMODEL_FF_OAT_PASS_REFERENCES_UNRESOLVED') -or
   [int]$r.realXModelRootsInCurrentFf -ne [int]$stageProof.modelCount -or
   [int]$r.sourceOwnedStaticModelTypes -ne [int]$stageProof.realOwnedStaticModelTypes -or
   [int]$r.verifiedNativeExternalModelReferences -ne [int]$stageProof.nativeExternalReferenceModelTypes -or
   [int]$r.unresolvedNativeSourceModelReferences -ne [int]$stageProof.unresolvedNativeReferenceModelTypes -or
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
Write-Host "[AZTEC-REAL-FF] SOURCE NAMES: real model types=$($stageProof.realOwnedStaticModelTypes), verified native refs=$($stageProof.nativeExternalReferenceModelTypes), UNRESOLVED refs=$($stageProof.unresolvedNativeReferenceModelTypes) -- 27/27 accounted"
Write-Host '[AZTEC-REAL-FF] World geometry: 735 actual source-derived GfxSurfaces.'
Write-Host '[AZTEC-REAL-FF] Source placement count 1903 is ACCOUNTED, NOT ALL VERIFIED, and NOT physically linked into GfxWorld.'
if([int]$stageProof.unresolvedNativeReferenceModelTypes -gt 0){
    Write-Host '[AZTEC-REAL-FF] WARNING: unresolved source XModel references remain identity-only; runtime use UNKNOWN. No Xbox approval.'
}
Write-Host "[AZTEC-REAL-FF] physical bytes=$length sha256=$sha"
Write-Host "[AZTEC-REAL-FF] FF=$ff"
Write-Host '[AZTEC-REAL-FF] NEXT real integration target: 1903 XModel draw links/complete world DPVS+required image/runtime assets.'
Write-Host '[AZTEC-REAL-FF] NO hardware ready claim; this is actual source-payload progress, not cosmetic polish.'