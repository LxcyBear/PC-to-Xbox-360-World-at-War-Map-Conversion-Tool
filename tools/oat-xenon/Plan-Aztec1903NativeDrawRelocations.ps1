[CmdletBinding()]
param(
    [string]$OutputDirectory=''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Production-frontier input: deterministic original-PC draw -> Xenon XModel
# root relocation intentions. No FF mutation; NOT a 60B draw serializer.
# The 28-body world contains 1902 source-owned model occurrences and ONE
# external stock model reference with UNPROVEN runtime residency.
# No donor Xbox draw/light/geometry bytes are used. Physical pointer
# encoding must happen in the Stage 4D/4E emitter after root allocation.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$sourcePath=Join-Path $base 'pc_gate8_static_draw_source\aztec_gate8_pc_static_draw_source_report.json'
$axesPath=Join-Path $base 'pc_gate8_static_draw_xenon_axis_preflight_28_model_world\aztec_1903_xenon_axis_pack_preflight.json'
$bridgePath=Join-Path $base 'pc_gate8_xenon_60b_draw_layout_bridge_28_model_world\aztec_1903_xenon_60b_draw_layout_bridge.json'
$worldPath=Join-Path $base 'pc_gate8_29x_ff\aztec_pc_world_core_integrated_oat_run.json'
$stagePath=Join-Path $repo '.deps\aztec8-all-static-xmodels\aztec_gate8_29_xmodel_staging.json'
$identityPath=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
if([string]::IsNullOrWhiteSpace($OutputDirectory)){
    $OutputDirectory=Join-Path $base 'pc_gate8_1903_draw_relocation_plan'
}
function Need([string]$path,[string]$label){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing $label file: $path"}
    return (Resolve-Path -LiteralPath $path).Path
}
function Sha([string]$path){return [string](Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
function Canon([string]$value){
    $s=$value.Trim().Replace('\','/').TrimStart(',').ToLowerInvariant()
    if($s.EndsWith('_wj',[StringComparison]::Ordinal)){$s=$s.Substring(0,$s.Length-3)}
    return $s
}
$source=Get-Content -LiteralPath (Need $sourcePath 'PC source proof') -Raw | ConvertFrom-Json
$axes=Get-Content -LiteralPath (Need $axesPath 'PC Xenon axes preflight') -Raw | ConvertFrom-Json
$bridge=Get-Content -LiteralPath (Need $bridgePath '60-byte ABI bridge') -Raw | ConvertFrom-Json
$world=Get-Content -LiteralPath (Need $worldPath 'OAT world proof') -Raw | ConvertFrom-Json
$stage=Get-Content -LiteralPath (Need $stagePath 'real XModel staging proof') -Raw | ConvertFrom-Json
$identity=Get-Content -LiteralPath (Need $identityPath 'native root identity') -Raw | ConvertFrom-Json
if([string]$source.status -ne 'AZTEC_GATE8_1903_PC_TYPED_STATIC_DRAWS_SOURCE_PASS' -or
   [int]$source.sourceDrawCount -ne 1903 -or
   [int]$source.verifiedSourceWorldModelBodies -ne 28 -or
   [string]$axes.status -ne 'AZTEC_GATE8_1903_PC_PLACEMENT_XENON_PACKED_AXES_PREFLIGHT_PASS' -or
   [string]$axes.verifiedWorldMode -ne '28-model' -or
   [int]$axes.placementOccurrencesWithRealModelBodies -ne 1902 -or
   [int]$axes.placementOccurrencesWithVerifiedExternalStockIdentity -ne 1 -or
   [int]$axes.placementOccurrencesWithOnlyNativeIdentity -ne 0 -or
   [string]$axes.originalPcDrawSourceSha256 -ine [string]$source.typedDrawSha256 -or
   [int]$axes.encodedT4XenonUnitAxisVectors -ne 5709 -or
   [string]$bridge.status -ne 'AZTEC_GATE8_1903_NATIVE_60B_GEOMETRY_SKELETON_SOURCE_ONLY_PASS' -or
   [int]$bridge.xenonHeaderDerivedDrawStride -ne 60 -or
   [int]$bridge.pcSourceDraws -ne 1903 -or
   [int]$bridge.sourceOwnedXModelDraws -ne 1902 -or
   [int]$bridge.nativeStockIdentityDraws -ne 1 -or
   [int]$bridge.xenonModelPointersRelocated -ne 0 -or
   [int]$bridge.drawRecordsLinkedIntoGfxWorld -ne 0 -or
   [string]$world.status -ne 'AZTEC_GATE8_ALL_CLASSIFIED_STATIC_XMODEL_TYPES_8509_ROOT_WORLD_OAT_PASS' -or
   -not [bool]$world.OatPass -or
   [bool]$world.hardwareReady -or
   [int]$world.realXModelRootsInCurrentFf -ne 28 -or
   [string]$stage.schema -ne 'WaW360.AztecGate8AllStaticXModelStaging.v1' -or
   [int]$stage.modelCount -ne 28 -or
   [int]$stage.realOwnedStaticModelTypes -ne 26 -or
   [int]$stage.nativeExternalReferenceModelTypes -ne 1 -or
   [int]$stage.nativeExternalReferenceDraws -ne 1 -or
   [int]$stage.unresolvedNativeReferenceDraws -ne 0 -or
   [string]$identity.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
   [int]$identity.assets -ne 8509 -or
   -not [bool]$identity.typeSequenceVerified){
    throw 'Original-PC typed draw / 60B ABI / 28-body Xenon world evidence changed.'
}
$srcDraws=Need ([string]$source.typedDrawFile) '1903 typed original-PC records'
if((Sha $srcDraws) -ine [string]$source.typedDrawSha256){throw 'Original-PC source draw SHA changed.'}
$axisFile=Need ([string]$axes.packedAxesReport) '1903 packed axes'
$geom=Need ([string]$bridge.geometryCandidateFile) '60B source geometry skeleton'
if([long](Get-Item -LiteralPath $geom).Length -ne 114180 -or
   (Sha $geom) -ine [string]$bridge.geometryCandidateSha256){
    throw 'Original-PC 1903 x 60-byte geometry payload altered or wrong size.'
}
$draws=Get-Content -LiteralPath $srcDraws -Raw|ConvertFrom-Json
$packed=Get-Content -LiteralPath $axisFile -Raw|ConvertFrom-Json
if([string]$draws.format -ne 'WaW360.T4PcGate8StaticDrawSource.v1' -or
   [int]$draws.count -ne 1903 -or
   [string]$packed.schema -ne 'WaW360.AztecGate8XenonPackedStaticDrawAxes.v1' -or
   @($draws.records).Count -ne 1903 -or
   @($packed.records).Count -ne 1903){throw 'Typed source or packed axes 1903-row ABI mismatch.'}

# The graph is source-owned. An XModel's root index is a stable identity,
# not a relocation word, until the emitter has made its final root layout.
$owned=@{}
foreach($graph in @($stage.graphProofs)){
    $key=Canon ([string]$graph.name)
    $index=[int]$graph.nativeRoot
    if($owned.ContainsKey($key)){throw "Duplicate physical XModel owner: $key"}
    $owned[$key]=$index
}
if($owned.Count -ne 28){throw "Expected 28 physically converted XModel roots; actual $($owned.Count)."}
$external=@{}
foreach($reference in @($stage.nativeExternalReferences)){
    $key=Canon ([string]$reference.model)
    if($external.ContainsKey($key) -or $owned.ContainsKey($key) -or
       -not [bool]$reference.stockOatExactBodyMatch -or
       [bool]$reference.runtimeResidencyVerified){
        throw "Invalid stock reference identity or unjustified residency for $key"
    }
    $external[$key]=[int]$reference.nativeRoot
}
if($external.Count -ne 1){throw "Expected exactly one non-owned stock reference, actual $($external.Count)."}
$identityByRoot=@{}
foreach($root in @($identity.entries)){
    if([string]$root.OatTypeName -ne 'xmodel'){continue}
    $idx=[int]$root.Index
    if($identityByRoot.ContainsKey($idx)){throw "Repeated native XModel root $idx"}
    $identityByRoot[$idx]=Canon ([string]$root.Name)
}
$plan=New-Object 'System.Collections.Generic.List[object]'
$realCount=0
$externalCount=0
$names=@{}
for($i=0;$i -lt 1903;$i++){
    $d=$draws.records[$i]
    $p=$packed.records[$i]
    $key=Canon ([string]$d.model)
    if([int]$d.ordinal -ne $i -or [int]$p.ordinal -ne $i -or
       $key -cne (Canon ([string]$p.model)) -or
       @($p.axisPackedWords).Count -ne 3){
        throw "Original-PC draw / Xenon-axis ordinal or model mismatch at row $i"
    }
    $root=[int]$p.nativeModelRoot
    if(-not $identityByRoot.ContainsKey($root) -or
       $identityByRoot[$root] -cne $key){
        throw "Original-PC draw $i points at incompatible native root identity #$root"
    }
    $owner='';$resolved=$false
    if([bool]$p.realModelBodyPresentInCurrentWorldFf -and
       -not [bool]$p.exactExternalStockIdentityOnly){
        if(-not $owned.ContainsKey($key) -or $owned[$key] -ne $root){
            throw "Missing physical PC-owned XModel body #$root at draw $i"
        }
        $owner='SOURCE_XMODEL_EMITTED';$resolved=$true;$realCount++
    }elseif([bool]$p.exactExternalStockIdentityOnly -and
            -not [bool]$p.realModelBodyPresentInCurrentWorldFf){
        if(-not $external.ContainsKey($key) -or $external[$key] -ne $root){
            throw "Unproven native stock XModel identity #$root at draw $i"
        }
        $owner='STOCK_REFERENCE_RESIDENCY_UNVERIFIED';$externalCount++
    }else{
        throw "Unknown model ownership status at draw $i"
    }
    if(-not $names.ContainsKey($key)){$names[$key]=0};$names[$key]++
    $plan.Add([pscustomobject]@{
        ordinal=$i
        pcModel=[string]$d.model
        canonicalModelName=$key
        nativeXModelRootIndex=$root
        bodyStatus=$owner
        sourceOwnedBodyReady=$resolved
        recordOffsetInFutureArray=(60*$i)
        modelPointerOffsetInFutureArray=(60*$i+32)
        pointerWord='EMITTER_STABLE_ROOT_POINTER_REQUIRED_NOT_WRITTEN'
        nativeLightingAndFlags='UNRESOLVED_NO_DONOR_BYTES'
    })
}
if($realCount -ne 1902 -or $externalCount -ne 1 -or $names.Count -ne 27){
    throw "Draw relocation coverage drift: owned=$realCount external=$externalCount distinct=$($names.Count)"
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$planPath=Join-Path $OutputDirectory 'aztec_1903_model_pointer_relocation_plan.json'
$resultPath=Join-Path $OutputDirectory 'aztec_1903_model_pointer_relocation_summary.json'
[ordered]@{
    schema='WaW360.Aztec1903ModelRootRelocations.v1'
    sourceTypedDrawSha256=[string]$source.typedDrawSha256
    sourceGeometrySha256=[string]$bridge.geometryCandidateSha256
    verifiedXModelWorldFastfile=[string]$world.outputFastFile
    drawCount=1903
    nativeRecordStride=60
    nativeModelPointerFieldOffset=32
    nativeRootIdentityCount=8509
    records=@($plan.ToArray())
    donorBytesUsed=$false
    nativeDrawBytesEmitted=$false
}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $planPath -Encoding UTF8
[ordered]@{
    schema='WaW360.Aztec1903ModelRootRelocationPlanSummary.v1'
    status='AZTEC_1903_MODEL_ROOT_RELOCATION_INTENT_PLANNED_NOT_EMITTED'
    planFile=$planPath
    planSha256=(Sha $planPath)
    sourceTypedDrawSha256=[string]$source.typedDrawSha256
    physical28BodyWorldProof=$worldPath
    sourceDrawCount=1903
    physicalXModelBodyDraws=$realCount
    externalStockBodyIdentityDraws=$externalCount
    externalStockResidencyProven=$false
    sourceOwnedRuntimeModelPointerWordsWritten=0
    xenonDrawStructsEmitted=0
    nativeWorldDrawPointersLinked=0
    remainingBlockingFields=@('stock XModel runtime residency','model pointer root packing at emission',
        'reflection/primary light indices','lightingHandle source semantics',
        'flags','19 reserved/padding bytes','GfxWorld DPVS static array pointer/length',
        'remaining world children')
    productionReady=$false
    note='Real 1903 per-draw source->native root relocation targets; NEVER interpret root indices as pointer words. No FF or reference asset bytes touched.'
}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath $resultPath -Encoding UTF8
Write-Host '[AZTEC-DRAW-RELOC] ==========================================================='
Write-Host "[AZTEC-DRAW-RELOC] Plan PASS: 1903/1903 exact PC names to native roots."
Write-Host "[AZTEC-DRAW-RELOC] Physical source XModel bodies=1902 draws; external stock reference=$externalCount draw (runtime residency UNVERIFIED)."
Write-Host '[AZTEC-DRAW-RELOC] Stage4D/4E pointer emission+lighting/flags and DPVS still pending; 0/1903 FF draw links.'
Write-Host "[AZTEC-DRAW-RELOC] JSON=$resultPath"
Write-Host '[AZTEC-DRAW-RELOC] No Xbox FF, no native donor, no production approval.'
