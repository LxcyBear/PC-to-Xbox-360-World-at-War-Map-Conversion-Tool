[CmdletBinding()]
param(
    [switch]$SkipPcModelConversion,
    [string]$FinalOutputDirectory = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\Xbox Maps\Converter Current Test Map',
    [switch]$ExportDiagnostic
)
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
# Requested final hardware-test map destination, intentionally OUTSIDE
# all repo scratch/OAT outputs. Partial 28-XModel FF never lands here as
# the user's current Xbox test map without a real source-closure proof.
$finalDest=[IO.Path]::GetFullPath($FinalOutputDirectory)
$runLogs=Join-Path $n 'pc_gate8_29x_ff\build_logs'
New-Item -ItemType Directory -Path $runLogs -Force | Out-Null
$stageLog=Join-Path $runLogs 'latest_xmodel_stage.log'
$emitterLog=Join-Path $runLogs 'latest_integrated_emitter.log'
$sizeWriter=Join-Path $PSScriptRoot 'Record-AztecOatFastFileSize.ps1'
$milestoneView=Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1'
function Set-BuildProgress([int]$percent,[string]$phase){
    # This 0..100 number is progress through THIS build's actions.
    # It is deliberately unrelated to Xbox map/gameplay completeness.
    $p=[Math]::Max(0,[Math]::Min(100,$percent))
    $blocks=20
    $filled=[int][Math]::Floor(($p*$blocks)/100.0)
    $bar=('#' * $filled)+('-' * ($blocks-$filled))
    Write-Progress -Id 1 -Activity 'Aztec actual Xenon FastFile build (not map readiness)' -Status "$phase ($p%)" -PercentComplete $p
    Write-Host "[AZTEC-BUILD-PROGRESS] [$bar] $p% | $phase"
}
function Show-BuildError([string]$log,[string]$step,[int]$exitCode){
    Write-Host "[AZTEC-REAL-FF] ERROR: $step failed (exit=$exitCode); first failure details follow."
    if(Test-Path -LiteralPath $log -PathType Leaf){
        $all=@(Get-Content -LiteralPath $log)
        $matches=@($all | Where-Object {
            $_ -match '(?i)(?:^|\]\s*)(?:ERROR\s*:|FATAL\s*:|throw\s*:)' -or
            $_ -match '(?i)(?:InvalidDataException|ParserError|PropertyNotFound|at\s+.+\.ps1:\d+)' -or
            $_ -match '(?i)(?:Source\s+\S+\s+Xenon\s+graph|OAT.*(?:failed|blocked)|native root.+mismatch|reference-only XModel|source reference.*identity|STOCK_REFERENCE_MATCH)'
        })
        foreach($line in @($matches | Select-Object -First 12)){
            Write-Host "[AZTEC-REAL-FF] FIRST_FAILURE $line"
        }
        if($matches.Count -eq 0){
            Write-Host '[AZTEC-REAL-FF] No obvious error keyword; last 35 process lines:'
            foreach($line in @($all | Select-Object -Last 35)){
                Write-Host "[AZTEC-REAL-FF] TRACE $line"
            }
        }
    }
    throw "$step failed (exit=$exitCode). Full actual process log: $log"
}
$old=$ErrorActionPreference
Write-Host '[AZTEC-REAL-FF] ====================================================='
Write-Host '[AZTEC-REAL-FF] CLASSIFY 27 SOURCE MODEL TYPES: CONVERT REAL OWNED BODIES, RETAIN PROVEN EXTERNAL REFERENCES'
Write-Host '[AZTEC-REAL-FF] Recompile REAL model graphs into 8509-root Xbox FF; OAT readback.'
Write-Host '[AZTEC-REAL-FF] Preserve existing PC source and passing 5-model baseline.'
Write-Host '[AZTEC-REAL-FF] NO ARTIFICIAL SIZE PADDING OR DONOR MODEL BYTES.'
Write-Host '[AZTEC-SIZE] Last verified baseline: 3,615,230 bytes (3.45 MiB); future OAT-PASS builds will be compared and ledgered.'
Set-BuildProgress 2 'Starting source/native identity checks'
Write-Host "[AZTEC-REAL-FF] REQUESTED FINAL Xbox map destination: $finalDest"
Write-Host '[AZTEC-REAL-FF] Final root receives ONLY full source-closure/hardware-approved maps.'
if($ExportDiagnostic){
    Write-Host '[AZTEC-REAL-FF] Explicit diagnostic export: _diagnostic_only_not_for_xbox subfolder.'
}
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
Set-BuildProgress 12 'Retail reference check complete; source XModel corpus next'
try{
    $params=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$stage)
    if($SkipPcModelConversion){$params+='-SkipPcModelConversion'}
    $ErrorActionPreference='Continue'
    & powershell.exe @params 2>&1 |
        ForEach-Object { [string]$_ } |
        Tee-Object -FilePath $stageLog |
        ForEach-Object {
            $line=[string]$_
            Write-Host "[AZTEC-REAL-FF] $line"
            if($line -match 'GRAFT SOURCE XMODEL\s+(\d+)/(\d+)'){
                $completed=[int]$Matches[1]
                $target=[int]$Matches[2]
                if($target -gt 0 -and $completed -le $target){
                    $pct=18+[int][Math]::Round(42.0*$completed/$target)
                    Set-BuildProgress $pct "Verified real XModel graph $completed/$target"
                }
            }
        }
    $stageExit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($stageExit -ne 0){
    Show-BuildError -log $stageLog -step 'Original-PC model staging' -exitCode $stageExit
}
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
Set-BuildProgress 63 "All $($stageProof.modelCount) XModel graphs staged; emitting one native FF"
try{
    $ErrorActionPreference='Continue'
    $builderArgs=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$builder,
        '-IntegrateWorld','-ProbeLevel','Full','-DpvsStaticProbe',
        '-Gate8ChildProbe','-Gate8ChildLevel','All','-Gate8AllModelProbe')
    & powershell.exe @builderArgs 2>&1 |
        ForEach-Object { [string]$_ } |
        Tee-Object -FilePath $emitterLog |
        ForEach-Object {
            $line=[string]$_
            Write-Host "[AZTEC-REAL-FF] $line"
            if($line -match 'Incremental \.NET build PASS'){
                Set-BuildProgress 76 '.NET Xenon emitter compiled; writing FF and verifying'
            }elseif($line -match 'OAT PASS=True'){
                Set-BuildProgress 92 'Native OAT reports FF load success; checking actual manifest and SHA'
            }
        }
    $emitExit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($emitExit -ne 0){
    Show-BuildError -log $emitterLog -step 'Integrated Xbox FastFile emission/OAT readback' -exitCode $emitExit
}
if(-not(Test-Path -LiteralPath $report -PathType Leaf)){
    throw "OAT builder exited zero but produced no actual integrated FF report: $report; process log=$emitterLog"
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
Set-BuildProgress 96 'Native OAT and source graph gates passed; recording size history'
if(-not(Test-Path -LiteralPath $sizeWriter -PathType Leaf)){
    throw "Validated FF exists, but the size-history tool is missing: $sizeWriter"
}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $sizeWriter `
    -FastFile $ff -ExpectedSha256 $sha -OatStatus ([string]$r.status) `
    -HistoryDirectory $runLogs -RealXModelBodies ([int]$r.realXModelRootsInCurrentFf) `
    -RealGfxSurfaces ([int]$r.realPcGfxSurfaceCount) `
    -RealMaterials ([int]$r.realPcGfxWorldMaterialRoots) -DrawRecordsLinked 0
if($LASTEXITCODE -ne 0){
    throw "Real FF OAT passed, but recording its verified size history failed. Inspect $runLogs."
}
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
if(Test-Path -LiteralPath $milestoneView -PathType Leaf){
    Write-Host '[AZTEC-REAL-FF] UPDATED VERIFIED MAP MILESTONES (different from per-build progress):'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $milestoneView
    if($LASTEXITCODE -ne 0){
        Write-Host '[AZTEC-REAL-FF] WARNING: map milestone display failed; it does not invalidate the OAT-verified FF.'
    }
}
Set-BuildProgress 100 'OAT-verified diagnostic FF and size history complete'
Write-Progress -Id 1 -Activity 'Aztec actual Xenon FastFile build' -Completed
# The only currently possible PASS statuses above are explicitly
# validation-only: incomplete DPVS, absent 1903 static draw pointers and
# additional image/script/runtime dependency closure. Never overwrite the
# requested Xbox Maps test root with that scratch FF.
Write-Host "[AZTEC-REAL-FF] FINAL DESTINATION RESERVED (not overwritten): $finalDest"
Write-Host '[AZTEC-REAL-FF] Final FF publish: BLOCKED - this build is validation-only; full content/DPVS/hardware checks remain false.'
if($ExportDiagnostic){
    # Opt-in physical copy of genuine OAT-passing partial source content,
    # physically separated from the FINAL root by an unmistakable folder.
    # Do not copy a stale file: the actual validated emitted FF and its
    # manifest are SHA checked immediately before and after copying.
    $diag=Join-Path $finalDest '_diagnostic_only_not_for_xbox'
    New-Item -ItemType Directory -Path $diag -Force | Out-Null
    $dest=Join-Path $diag 'nazi_zombie_aztec.ff'
    $tmp=Join-Path $diag ('nazi_zombie_aztec.ff.tmp.'+[Guid]::NewGuid().ToString('N'))
    try{
        Copy-Item -LiteralPath $ff -Destination $tmp -Force
        if((Get-FileHash -LiteralPath $tmp -Algorithm SHA256).Hash -ine $sha){
            throw 'Opt-in diagnostic FF SHA changed during copy.'
        }
        Move-Item -LiteralPath $tmp -Destination $dest -Force
        if((Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash -ine $sha){
            throw 'Opt-in diagnostic FF SHA changed after publication.'
        }
        $diagnosticManifest=[ordered]@{
            schema='WaW360.AztecExplicitDiagnosticExport.v1'
            map='nazi_zombie_aztec'
            status='DIAGNOSTIC_ONLY_NOT_FOR_XBOX'
            physicalFf=$dest
            sourceFf=$ff
            sha256=$sha
            bytes=[long]$length
            oatStatus=[string]$r.status
            verifiedModelBodies=[int]$r.realXModelRootsInCurrentFf
            staticDrawRecordsLinked=0
            sourceFullWorldDpvsComplete=$false
            hardwareReady=$false
            allowedForFinalTestMap=$false
            note='Partial real PC-owned Xenon assets; this file is NOT a completed map or approved CoD Xe hardware test. The parent FinalOutputDirectory is reserved for validated complete assets.'
        }
        $diagnosticManifest|ConvertTo-Json -Depth 5|
            Set-Content -LiteralPath (Join-Path $diag 'DIAGNOSTIC_NOT_FOR_XBOX.json') -Encoding UTF8
        Write-Host "[AZTEC-REAL-FF] EXPORTED OPT-IN DIAGNOSTIC ONLY: $dest"
    }finally{
        if(Test-Path -LiteralPath $tmp -PathType Leaf){
            Remove-Item -LiteralPath $tmp -Force
        }
    }
}