[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Regression from the Aztec 1902-pointer local PASS to the reusable .NET
# T4 Xenon static-draw pointer codec. No existing FF or prior proof modified.
# Generic codec must reproduce the old candidate exactly, byte-for-byte.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$oldReportPath=Join-Path $base 'pc_gate8_1903_draw_pointer_relocation_candidate\aztec_1903_draw_pointer_emission_report.json'
$planSummaryPath=Join-Path $base 'pc_gate8_1903_draw_relocation_plan\aztec_1903_model_pointer_relocation_summary.json'
$bridgePath=Join-Path $base 'pc_gate8_xenon_60b_draw_layout_bridge_28_model_world\aztec_1903_xenon_60b_draw_layout_bridge.json'
$layoutPath=Join-Path $base 'pc_gate8_29x_ff\scratch_emit\layout_recheck\nuclear_xenon_block_layout.json'
$stagePath=Join-Path $repo '.deps\aztec8-all-static-xmodels\aztec_gate8_29_xmodel_staging.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
function Need([string]$p,[string]$description){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "$description file missing: $p"}
    return (Resolve-Path -LiteralPath $p).Path
}
function Sha([string]$p){return [string](Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash}
function Canon([string]$n){
    $x=$n.Trim().Replace('\','/').TrimStart(',').ToLowerInvariant()
    if($x.EndsWith('_wj',[StringComparison]::Ordinal)){$x=$x.Substring(0,$x.Length-3)}
    return $x
}
$old=Get-Content -LiteralPath (Need $oldReportPath '1902 pointer candidate proof') -Raw|ConvertFrom-Json
$s=Get-Content -LiteralPath (Need $planSummaryPath '1903 relocation plan summary') -Raw|ConvertFrom-Json
$b=Get-Content -LiteralPath (Need $bridgePath '60B geometry bridge') -Raw|ConvertFrom-Json
$l=Get-Content -LiteralPath (Need $layoutPath 'source-world layout') -Raw|ConvertFrom-Json
$st=Get-Content -LiteralPath (Need $stagePath 'physical 28 XModel bodies proof') -Raw|ConvertFrom-Json
if([string]$old.schema -ne 'WaW360.AztecDrawPointerEmissionCandidate.v1' -or
   [string]$old.status -ne 'AZTEC_1902_OWNED_NATIVE_ROOT_POINTER_WORDS_ENCODED_EXTERNAL_DRAW_QUARANTINED' -or
   [int]$old.sourceOwnedXModelPointerWordsWritten -ne 1902 -or
   [int]$old.externalStockReferencePointerWordsWritten -ne 0 -or
   [int]$old.distinctRealXModelRootBodiesReferenced -ne 26 -or
   [bool]$old.physicalGfxWorldArrayLinked -or [bool]$old.productionApproved -or
   [int]$old.candidateBytes -ne 114180 -or
   [string]$s.schema -ne 'WaW360.Aztec1903ModelRootRelocationPlanSummary.v1' -or
   [int]$s.sourceDrawCount -ne 1903 -or
   [string]$b.status -ne 'AZTEC_GATE8_1903_NATIVE_60B_GEOMETRY_SKELETON_SOURCE_ONLY_PASS' -or
   [int]$b.xenonHeaderDerivedDrawStride -ne 60 -or
   [int]$l.NativeRoots -ne 8509 -or [int]$l.RealXModelRoots -ne 28 -or
   -not [bool]$l.LayoutReady -or
   @($l.RootInsertLayouts).Count -ne 8509 -or
   [int]$st.modelCount -ne 28){
    throw 'Source-world exact 1902/1903 pointer or native layout proof changed.'
}
$originalCandidate=Need ([string]$old.candidateDrawArray) 'historical source-derived pointer candidate'
if((Sha $originalCandidate) -ine [string]$old.candidateDrawArraySha256){
    throw 'Historical pointer candidate SHA changed since user-verified PASS.'
}
$planPath=Need ([string]$s.planFile) '1903 source draw plan'
if((Sha $planPath) -ine [string]$s.planSha256){throw 'Original-PC draw root plan SHA changed.'}
$plan=Get-Content -LiteralPath $planPath -Raw|ConvertFrom-Json
if([int]$plan.drawCount -ne 1903 -or @($plan.records).Count -ne 1903 -or
   [string]$plan.sourceGeometrySha256 -ine [string]$b.geometryCandidateSha256){
    throw 'Source draw plan record count/geometry SHA mismatch.'
}
$geom=Need ([string]$b.geometryCandidateFile) 'original PC geometry candidate'
if((Sha $geom) -ine [string]$b.geometryCandidateSha256 -or
   [long](Get-Item -LiteralPath $geom).Length -ne 114180){
    throw 'Original-PC geometry candidate changed.'
}
$rootMap=@{}
foreach($r in @($l.RootInsertLayouts)){
    $idx=[int]$r.RootIndex
    if($rootMap.ContainsKey($idx)){throw "Repeated Stage 4D root $idx"}
    $rootMap[$idx]=$r
}
$owned=@{}
foreach($p in @($st.graphProofs)){
    $idx=[int]$p.nativeRoot
    if($owned.ContainsKey($idx)){throw "Duplicate real model root $idx"}
    $owned[$idx]=Canon ([string]$p.name)
}
if($owned.Count -ne 28){throw 'Physical 28-body XModel proof changed.'}
$neededRoots=@{}
$sites=New-Object 'System.Collections.Generic.List[object]'
$ownedDraws=0;$stockDraws=0
for($i=0;$i -lt 1903;$i++){
    $p=$plan.records[$i]
    $idx=[int]$p.nativeXModelRootIndex
    if([int]$p.ordinal -ne $i -or -not $rootMap.ContainsKey($idx)){
        throw "Missing ordinal/root in the originally verified source plan at $i"
    }
    $r=$rootMap[$idx]
    $name=Canon ([string]$p.pcModel)
    if([string]$r.TypeName -ne 'xmodel' -or (Canon ([string]$r.Name)) -cne $name){
        throw "XModel source/native root name changed at ordinal $i"
    }
    $real=[string]$p.bodyStatus -eq 'SOURCE_XMODEL_EMITTED'
    $external=[string]$p.bodyStatus -eq 'STOCK_REFERENCE_RESIDENCY_UNVERIFIED'
    if($real -eq $external -or $real -ne [bool]$p.sourceOwnedBodyReady){
        throw "Source XModel ownership ambiguity at draw $i"
    }
    if($real){
        if(-not $owned.ContainsKey($idx) -or $owned[$idx] -cne $name){
            throw "Unbacked physical XModel at draw $i"
        }
        $ownedDraws++
    }else{
        if($owned.ContainsKey($idx)){throw "Stock draw $i aliases an owned model."}
        $stockDraws++
    }
    $sites.Add([ordered]@{
        ordinal=$i
        nativeRootIndex=$idx
        modelName=[string]$p.pcModel
        sourceOwnedBodyPresent=$real
    })
    if(-not $neededRoots.ContainsKey($idx)){
        $neededRoots[$idx]=[ordered]@{
            nativeRootIndex=$idx
            typeName=[string]$r.TypeName
            nativeName=[string]$r.Name
            stablePointer=[uint32]$r.StablePointer
            sourceOwnedBodyPresent=$real
        }
    }elseif([bool]$neededRoots[$idx].sourceOwnedBodyPresent -ne $real){
        throw "Mixed source ownership classification for model root $idx"
    }
}
if($ownedDraws -ne 1902 -or $stockDraws -ne 1 -or $neededRoots.Count -ne 27){
    throw "XModel draw census changed: owned=$ownedDraws stock=$stockDraws roots=$($neededRoots.Count)"
}
$timestamp=Get-Date -Format 'yyyyMMdd_HHmmss'
$out=Join-Path $base ('pc_gate8_shared_codec_regression\run_'+$timestamp+'_'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $out -Force | Out-Null
$neutral=Join-Path $out 'generic_t4_xenon_draw_input.json'
$bin=Join-Path $out 'generic_t4_xenon_draw_candidate.bin'
$report=Join-Path $out 'generic_t4_xenon_draw_result.json'
$proof=Join-Path $out 'aztec_shared_engine_exact_comparison.json'
[ordered]@{
    schema='WaW360.T4Xenon.StaticDrawPointerInput.v1'
    sourceGeometryFile=$geom
    sourceGeometrySha256=[string]$b.geometryCandidateSha256
    recordStride=60
    modelPointerOffset=32
    sourcePlanSha256=[string]$s.planSha256
    sourceWorldLayout=$layoutPath
    sourceOwnedXModelStagingProof=$stagePath
    draws=@($sites.ToArray())
    roots=@($neededRoots.GetEnumerator()|Sort-Object Name|ForEach-Object {$_.Value})
}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $neutral -Encoding UTF8
Write-Host '[AZTEC-SHARED-CODEC] ====================================================='
Write-Host '[AZTEC-SHARED-CODEC] Source plan=1903/1903; real owned=1902; stock dependency quarantined=1'
Write-Host '[AZTEC-SHARED-CODEC] Building reusable .NET 8 PC2XB3 CLI (incremental).'
& dotnet build $cli -c Release --nologo
if($LASTEXITCODE -ne 0){throw 'Shared PC2XB3 compiler build failed. No FF changes.'}
Write-Host '[AZTEC-SHARED-CODEC] Running generic non-Aztec 3 x 48B synthetic codec self-test.'
& dotnet run --project $cli -c Release --no-build -- xenon-static-draw-pointer-selftest
if($LASTEXITCODE -ne 0){throw 'Shared pointer codec generic regression FAILED.'}
Write-Host '[AZTEC-SHARED-CODEC] Running 1903 original-PC source-bound Xbox pointer regression.'
& dotnet run --project $cli -c Release --no-build -- xenon-static-draw-pointer-encode $neutral $bin $report
if($LASTEXITCODE -ne 0){throw 'Shared pointer codec rejected Aztec baseline. Old PASS file is unchanged.'}
if(-not(Test-Path -LiteralPath $bin -PathType Leaf) -or
   (Get-Item -LiteralPath $bin).Length -ne 114180 -or
   (Sha $bin) -ine [string]$old.candidateDrawArraySha256){
    throw "Shared Xenon codec candidate is NOT byte-for-byte identical to prior verified Aztec emission. Check $report"
}
$x=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
if([int]$x.sourceOwnedPointersWritten -ne 1902 -or
   [int]$x.externalPointersQuarantined -ne 1 -or
   [int]$x.distinctSourceOwnedModelRoots -ne 26 -or
   [bool]$x.productionReady){
    throw 'Map-independent native draw pointer codec proof has unexpected owner/stock counts.'
}
[ordered]@{
    schema='WaW360.AztecGenericDrawCodecRegression.v1'
    status='GENERIC_T4_XENON_CODEC_BYTE_EXACT_AGAINST_AZTEC_1902_POINTER_PROOF'
    timestampUtc=[DateTime]::UtcNow.ToString('o')
    genericManifest=$neutral
    originalAztecPointerCandidate=$originalCandidate
    originalAztecPointerCandidateSha256=[string]$old.candidateDrawArraySha256
    genericPointerCandidate=$bin
    genericCandidateSha256=(Sha $bin)
    byteForByteIdentical=$true
    genericArbitraryDrawStrideSelfTest=$true
    recordCount=1903
    sourceOwnedPointers=1902
    runtimeUnverifiedExternal=1
    physicallyLinkedIntoWorld=$false
    hardwareReady=$false
    sourceWorldFfModified=$false
}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath $proof -Encoding UTF8
Write-Host '[AZTEC-SHARED-CODEC] ================================================'
Write-Host '[AZTEC-SHARED-CODEC] PASS: generic T4Xenon static-draw encoder matches Aztec EXACT bytes.'
Write-Host '[AZTEC-SHARED-CODEC] 1902/1902 source-owned pointers, 1 stock quarantined, 0 FF draw links.'
Write-Host "[AZTEC-SHARED-CODEC] Report=$proof"
Write-Host '[AZTEC-SHARED-CODEC] No donor bytes, no native FF emission, no Xbox deployment.'
