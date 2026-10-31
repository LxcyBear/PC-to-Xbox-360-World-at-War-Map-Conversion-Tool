[CmdletBinding()]
param([string]$OutputDirectory='')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Real Stage4D stable-root-pointer relocation for 1902 source-owned Aztec
# static draws. One stock XModel draw remains quarantined until Xbox runtime
# residency is proven. Lighting/flags/draw tail unresolved: NOT A GAME ARRAY.
# Never alter FFs or graft reference/stock oracle bytes into the output.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$planSummary=Join-Path $base 'pc_gate8_1903_draw_relocation_plan\aztec_1903_model_pointer_relocation_summary.json'
$bridgePath=Join-Path $base 'pc_gate8_xenon_60b_draw_layout_bridge_28_model_world\aztec_1903_xenon_60b_draw_layout_bridge.json'
$worldPath=Join-Path $base 'pc_gate8_29x_ff\aztec_pc_world_core_integrated_oat_run.json'
$layoutPath=Join-Path $base 'pc_gate8_29x_ff\scratch_emit\layout_recheck\nuclear_xenon_block_layout.json'
$stagePath=Join-Path $repo '.deps\aztec8-all-static-xmodels\aztec_gate8_29_xmodel_staging.json'
if([string]::IsNullOrWhiteSpace($OutputDirectory)){
    $OutputDirectory=Join-Path $base 'pc_gate8_1903_draw_pointer_relocation_candidate'
}
function Need([string]$p,[string]$name){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing $name : $p"}
    return (Resolve-Path -LiteralPath $p).Path
}
function Sha([string]$p){return [string](Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash}
function Canon([string]$name){
    $v=$name.Trim().Replace('\','/').TrimStart(',').ToLowerInvariant()
    if($v.EndsWith('_wj',[StringComparison]::Ordinal)){$v=$v.Substring(0,$v.Length-3)}
    return $v
}
function Put32BE([byte[]]$b,[int]$at,[uint32]$v){
    $b[$at]=[byte](($v -shr 24) -band 255)
    $b[$at+1]=[byte](($v -shr 16) -band 255)
    $b[$at+2]=[byte](($v -shr 8) -band 255)
    $b[$at+3]=[byte]($v -band 255)
}
function Get32BE([byte[]]$b,[int]$at){
    [uint32]$v=([uint32]$b[$at] -shl 24)
    $v=$v -bor ([uint32]$b[$at+1] -shl 16)
    $v=$v -bor ([uint32]$b[$at+2] -shl 8)
    $v=$v -bor [uint32]$b[$at+3]
    return $v
}
$s=Get-Content -LiteralPath (Need $planSummary '1903 source relocation summary') -Raw|ConvertFrom-Json
$b=Get-Content -LiteralPath (Need $bridgePath '60B geometry bridge proof') -Raw|ConvertFrom-Json
$w=Get-Content -LiteralPath (Need $worldPath '28-model OAT world proof') -Raw|ConvertFrom-Json
$l=Get-Content -LiteralPath (Need $layoutPath 'actual Stage4D final stable root layout') -Raw|ConvertFrom-Json
$st=Get-Content -LiteralPath (Need $stagePath '28 model staging provenance') -Raw|ConvertFrom-Json
if([string]$s.schema -ne 'WaW360.Aztec1903ModelRootRelocationPlanSummary.v1' -or
   [string]$s.status -ne 'AZTEC_1903_MODEL_ROOT_RELOCATION_INTENT_PLANNED_NOT_EMITTED' -or
   [int]$s.sourceDrawCount -ne 1903 -or
   [int]$s.physicalXModelBodyDraws -ne 1902 -or
   [int]$s.externalStockBodyIdentityDraws -ne 1 -or
   [bool]$s.externalStockResidencyProven -or
   [string]$b.status -ne 'AZTEC_GATE8_1903_NATIVE_60B_GEOMETRY_SKELETON_SOURCE_ONLY_PASS' -or
   [int]$b.xenonHeaderDerivedDrawStride -ne 60 -or
   [int]$b.geometryCandidateBytes -ne 114180 -or
   [string]$w.status -ne 'AZTEC_GATE8_ALL_CLASSIFIED_STATIC_XMODEL_TYPES_8509_ROOT_WORLD_OAT_PASS' -or
   -not [bool]$w.OatPass -or [bool]$w.hardwareReady -or
   [int]$w.realXModelRootsInCurrentFf -ne 28 -or
   [string]$st.schema -ne 'WaW360.AztecGate8AllStaticXModelStaging.v1' -or
   [int]$st.modelCount -ne 28 -or
   [int]$st.nativeExternalReferenceDraws -ne 1 -or
   [int]$l.NativeRoots -ne 8509 -or
   [int]$l.RealXModelRoots -ne 28 -or
   -not [bool]$l.LayoutReady -or
   @($l.RootInsertLayouts).Count -ne 8509){
    throw '1903 typed draw / real XModel FF / exact 4D layout evidence mismatch.'
}
$planPath=Need ([string]$s.planFile) 'source draw-to-root binding list'
if((Sha $planPath) -ine [string]$s.planSha256){throw 'Draw relocation source SHA changed.'}
$p=Get-Content -LiteralPath $planPath -Raw|ConvertFrom-Json
if([string]$p.schema -ne 'WaW360.Aztec1903ModelRootRelocations.v1' -or
   [int]$p.drawCount -ne 1903 -or
   [int]$p.nativeRootIdentityCount -ne 8509 -or
   @($p.records).Count -ne 1903 -or
   [string]$p.sourceTypedDrawSha256 -ine [string]$s.sourceTypedDrawSha256 -or
   [string]$p.sourceGeometrySha256 -ine [string]$b.geometryCandidateSha256){
    throw 'SHA-authenticated source-to-root plan changed.'
}
$emitter=Get-Content -LiteralPath (Need ([string]$w.emitterReport) 'native emitter manifest') -Raw|ConvertFrom-Json
if(-not [bool]$emitter.LayoutReplayExact -or
   -not [bool]$emitter.ValidationPass -or
   -not [bool]$emitter.OatZeroWarningsZeroErrors -or
   [int]$emitter.RealXModelRoots -ne 28 -or
   [bool]$emitter.ReadyForHardware){
    throw 'Stage 4D root offsets lack verified 4E replay or actual native OAT proof.'
}
$ff=Need ([string]$w.outputFastFile) '28 source-model main FF'
if([long](Get-Item -LiteralPath $ff).Length -ne 3615230 -or
   (Sha $ff) -ine '0CBD019ABB5D702429B9EECA9B04A843311FF118DDEDBD5CF012C757F757CF82'){
    throw 'Historical exact-world FF has changed; pointer layout provenance no longer valid.'
}
$geom=Need ([string]$b.geometryCandidateFile) 'exact source-owned geometry'
if((Sha $geom) -ine [string]$b.geometryCandidateSha256 -or
   [long](Get-Item -LiteralPath $geom).Length -ne 114180){
    throw 'GfxWorld 60B source geometry changed.'
}
$original=[IO.File]::ReadAllBytes($geom)
$bytes=[byte[]]$original.Clone()
$layouts=@{}
foreach($row in @($l.RootInsertLayouts)){
    $idx=[int]$row.RootIndex
    if($layouts.ContainsKey($idx)){throw "Duplicate 4D root layout $idx"}
    $layouts[$idx]=$row
}
$ownedModelRoots=@{}
foreach($model in @($st.graphProofs)){
    $idx=[int]$model.nativeRoot
    $name=Canon ([string]$model.name)
    if($ownedModelRoots.ContainsKey($idx) -or [string]::IsNullOrWhiteSpace($name)){
        throw "Duplicate 28-body source XModel at #$idx"
    }
    $ownedModelRoots[$idx]=$name
}
if($ownedModelRoots.Count -ne 28){throw 'Not all 28 converted model bodies present in staging proof.'}
$written=0
$quarantined=0
$ownedRootsUsed=New-Object 'System.Collections.Generic.HashSet[int]'
$forensics=New-Object 'System.Collections.Generic.List[object]'
for($i=0;$i -lt 1903;$i++){
    $r=$p.records[$i]
    $nativeRoot=[int]$r.nativeXModelRootIndex
    $at=$i*60+32
    if([int]$r.ordinal -ne $i -or [int]$r.recordOffsetInFutureArray -ne ($i*60) -or
       [int]$r.modelPointerOffsetInFutureArray -ne $at -or
       -not $layouts.ContainsKey($nativeRoot)){
        throw "Source draw pointer relocation offset/root mismatch at $i."
    }
    $layout=$layouts[$nativeRoot]
    $key=Canon ([string]$r.pcModel)
    if([string]$layout.TypeName -ne 'xmodel' -or
       (Canon ([string]$layout.Name)) -cne $key -or
       $at+4 -gt $bytes.Length){
        throw "XModel 4D stable pointer identity mismatch at draw #$i (root #$nativeRoot)"
    }
    if((Get32BE $bytes $at) -ne 0){
        throw "Candidate draw #$i unexpectedly has a preexisting pointer word."
    }
    if([string]$r.bodyStatus -eq 'SOURCE_XMODEL_EMITTED' -and
       [bool]$r.sourceOwnedBodyReady){
        if(-not $ownedModelRoots.ContainsKey($nativeRoot) -or
           $ownedModelRoots[$nativeRoot] -cne $key){
            throw "Draw #$i is not backed by a real, correctly named source-owned XModel body."
        }
        [uint32]$pointer=[Convert]::ToUInt32($layout.StablePointer)
        if($pointer -eq 0){throw "Null/invalid native root pointer for owned draw #$i."}
        Put32BE $bytes $at $pointer
        if((Get32BE $bytes $at) -ne $pointer){
            throw "Big-endian pointer roundtrip failed at $i."
        }
        [void]$ownedRootsUsed.Add($nativeRoot)
        $written++
    }elseif([string]$r.bodyStatus -eq 'STOCK_REFERENCE_RESIDENCY_UNVERIFIED' -and
           -not [bool]$r.sourceOwnedBodyReady){
        if($ownedModelRoots.ContainsKey($nativeRoot)){
            throw "External draw #$i overlaps a physically converted owned model."
        }
        $quarantined++
        $forensics.Add([ordered]@{
            ordinal=$i;model=$key;nativeRootIndex=$nativeRoot
            reason='Stock XModel body found in a different base-zone, runtime residency not yet proven'
            stableRootPointerCandidate=[string]$layout.StablePointer
            writtenIntoOutput=$false
        })
    }else{
        throw "Unresolved draw #$i ownership status; refusing pointer emission."
    }
}
if($written -ne 1902 -or $quarantined -ne 1 -or $ownedRootsUsed.Count -ne 26){
    throw "Source XModel reference relocation coverage invalid: owned=$written ownedTypes=$($ownedRootsUsed.Count) external=$quarantined."
}
# Prove no geometrical transform, lightingHandle, flags or reserved bytes
# were silently invented or modified. Only 4-pointer bytes in 1902 rows.
for($i=0;$i -lt 1903;$i++){
    $off=$i*60
    for($j=0;$j -lt 60;$j++){
        $at=$off+$j
        if($j -ge 32 -and $j -lt 36){continue}
        if($bytes[$at] -ne $original[$at]){
            throw "Source geometry or unproven lighting/flags/padding mutated at draw #$i offset +$j."
        }
    }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$out=Join-Path $OutputDirectory 'aztec_1903_draw_60b_1902_owned_pointers_ONLY_NOT_FOR_FF.bin'
$report=Join-Path $OutputDirectory 'aztec_1903_draw_pointer_emission_report.json'
[IO.File]::WriteAllBytes($out,$bytes)
if((Get-Item -LiteralPath $out).Length -ne 114180){throw 'Written 1903 x 60B size changed.'}
[ordered]@{
    schema='WaW360.AztecDrawPointerEmissionCandidate.v1'
    status='AZTEC_1902_OWNED_NATIVE_ROOT_POINTER_WORDS_ENCODED_EXTERNAL_DRAW_QUARANTINED'
    actualStage4dStableRootLayout=$layoutPath
    sourcePointerRelocationPlan=$planPath
    sourcePlanSha256=(Sha $planPath)
    sourceWorldFf=$ff
    sourceWorldFfSha256=(Sha $ff)
    sourceGeometrySha256=[string]$b.geometryCandidateSha256
    candidateDrawArray=$out
    candidateDrawArraySha256=(Sha $out)
    candidateBytes=$bytes.Length
    sourceOwnedXModelPointerWordsWritten=1902
    distinctRealXModelRootBodiesReferenced=$ownedRootsUsed.Count
    externalStockReferencePointerWordsWritten=0
    externalStockDrawsQuarantined=@($forensics.ToArray())
    lightingHandleMappingProven=$false
    nativeFlagsAndPaddingSemanticsProven=$false
    physicalGfxWorldArrayLinked=$false
    finalNativeDrawArrayComplete=$false
    fastfileModified=$false
    xboxHardwareReady=$false
    productionApproved=$false
    note='Only 1902 real PC->native 4D root pointer words emitted, with endian readback and geometry/tail immutability. 1 stock model runtime residency not verified; lighting/flags absent. DO NOT GRAFT INTO FF.'
}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $report -Encoding UTF8
Write-Host '[AZTEC-DRAW-PTR] ============================================================'
Write-Host "[AZTEC-DRAW-PTR] VERIFIED SOURCE-OWNED POINTER WORDS: $written/1903"
Write-Host "[AZTEC-DRAW-PTR] Real converted XModel source types referenced=$($ownedRootsUsed.Count) /26"
Write-Host "[AZTEC-DRAW-PTR] 1 external stock XModel draw QUARANTINED; runtime residency not proven."
Write-Host '[AZTEC-DRAW-PTR] All source geometry, missing lighting/flags/padding byte fields unchanged.'
Write-Host "[AZTEC-DRAW-PTR] 1903 x 60B candidate: $out"
Write-Host "[AZTEC-DRAW-PTR] Report: $report"
Write-Host '[AZTEC-DRAW-PTR] NOT AN XBOX ARRAY. 0/1903 linked to FF; NO NATIVE DEPLOYMENT.'
