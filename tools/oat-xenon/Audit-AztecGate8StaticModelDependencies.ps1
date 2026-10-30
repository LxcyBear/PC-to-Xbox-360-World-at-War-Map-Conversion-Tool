[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Gate 8 static draw dependency census. Read-only.
# A native name / identity shell does NOT equal a real XModel body.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$sourcePath=Join-Path $base 'pc_gfxworld_gate8_child_source_corpus\aztec_pc_gate8_child_source_corpus.json'
$oatPath=Join-Path $base 'pc_gate8_planes_nodes_layer\aztec_pc_world_core_integrated_oat_run.json'
$nativePath=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$bindingPath=Join-Path $base 'pc_two_xmodel_two_material_fullgraph_integration\aztec_two_pc_xmodel_root_binding.json'
$out=Join-Path $base 'pc_gate8_static_model_dependency_audit'
$reportPath=Join-Path $out 'aztec_gate8_static_draw_xmodel_dependencies.json'

foreach($path in @($sourcePath,$oatPath,$nativePath,$bindingPath)){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Missing Gate 8 dependency source: $path"
    }
}
$source=Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json
$oat=Get-Content -LiteralPath $oatPath -Raw | ConvertFrom-Json
$native=Get-Content -LiteralPath $nativePath -Raw | ConvertFrom-Json
$binding=Get-Content -LiteralPath $bindingPath -Raw | ConvertFrom-Json

if([string]$source.schema -ne 'WaW360.AztecGate8PcTypedChildCorpus.v1' -or
   [string]$source.status -ne 'AZTEC_PC_GATE8_TYPED_PLANES_NODES_LAYER_SOURCE_PASS' -or
   [int]$source.planeCount -ne 3447 -or
   [int]$source.nodeCount -ne 24511 -or
   [int]$source.staticModelDrawCount -ne 1903 -or
   [int]$source.distinctStaticModelNames -ne 27 -or
   [int]$source.missingStaticModelNames -ne 0 -or
   [bool]$source.xboxFfEmitted -or [bool]$source.xboxHardwareReady -or
   [string]$oat.status -ne 'AZTEC_GFXWORLD_PC_GATE8_PLANES_NODES_LAYER_OAT_PASS' -or
   -not [bool]$oat.OatPass -or
   -not [bool]$oat.sourceBackedGate8PlaneNodeLayerProbe -or
   [string]$oat.sourceBackedGate8ProbeLevel -ne 'All' -or
   [bool]$oat.allOriginalPcGfxWorldDpvsChildrenComplete -or
   [bool]$oat.hardwareReady -or
   -not(Test-Path -LiteralPath ([string]$oat.outputFastFile) -PathType Leaf) -or
   [string]$native.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
   [int]$native.assets -ne 8509 -or
   -not [bool]$native.typeSequenceVerified -or
   [string]$binding.format -ne 'WaW360.AztecPcTwoXModelBinding.v1' -or
   -not [bool]$binding.bindingPass -or
   [int]$binding.nativeRoots -ne 8509 -or
   [int]$binding.nativeXModelRoots -ne 284){
    throw 'Gate 8 XModel ownership audit requires typed PC corpus, full three-child OAT PASS and exact native two-XModel canary.'
}
$drawNames=@($source.staticModelDrawNames)
if($drawNames.Count -ne 1903 -or @($drawNames | Where-Object {[string]::IsNullOrWhiteSpace([string]$_)}).Count -ne 0){
    throw 'Original PC static draw model identity array is missing or has null names.'
}
$nativeModels=@($native.entries | Where-Object {[string]$_.OatTypeName -eq 'xmodel'})
if($nativeModels.Count -ne 284 -or @($binding.ownedBindings).Count -ne 2 -or
   @($binding.referenceBindings).Count -ne 0){
    throw "Unexpected native XModel census or real model binding: $($nativeModels.Count)/284 identities."
}
$owned=@($binding.ownedBindings)
$expectedReal=@('zombie_x2_icon','tag_origin')
for($i=0;$i -lt 2;$i++){
    $b=$owned[$i]
    if([string]$b.CorpusName -cne $expectedReal[$i] -or
       [int]$b.CorpusOrdinal -ne $i -or
       @($nativeModels | Where-Object {[int]$_.Index -eq [int]$b.NativeRootIndex}).Count -ne 1){
        throw 'Two source-backed real XModel bodies differ from the prior pinned canary.'
    }
}
function Normalize-Model([string]$name){
    $s=$name.Replace('\','/').TrimStart(',')
    if($s.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){
        $s=$s.Substring(0,$s.Length-3)
    }
    return $s.ToLowerInvariant()
}
$counts=@($drawNames | Group-Object | Sort-Object -Property Count -Descending)
if($counts.Count -ne 27 -or ($counts | Measure-Object -Property Count -Sum).Sum -ne 1903){
    throw 'Original-PC static model distinct-name/instance counts changed.'
}
$rows=New-Object 'System.Collections.Generic.List[object]'
foreach($g in $counts){
    $name=[string]$g.Name
    $normalized=Normalize-Model $name
    $exact=@($nativeModels | Where-Object {[string]$_.Name -ceq $name})
    $logical=@($nativeModels | Where-Object {(Normalize-Model ([string]$_.Name)) -eq $normalized})
    $reals=@($owned | Where-Object {(Normalize-Model ([string]$_.CorpusName)) -eq $normalized})
    $status=if($reals.Count -eq 1 -and $logical.Count -eq 1){
        'REAL_XMODEL_GRAPH_BOUND'
    }elseif($logical.Count -eq 0){
        'NO_NATIVE_XMODEL_IDENTITY'
    }elseif($logical.Count -gt 1){
        'AMBIGUOUS_NATIVE_XMODEL_IDENTITY'
    }else{
        'NATIVE_IDENTITY_ONLY_NO_REAL_GRAPH'
    }
    $rows.Add([pscustomobject]@{
        pcModelName=$name
        occurrences=[int]$g.Count
        normalizedName=$normalized
        nativeExactMatches=$exact.Count
        nativeLogicalMatches=$logical.Count
        nativeRootIndices=@($logical | ForEach-Object {[int]$_.Index})
        nativeRootNames=@($logical | ForEach-Object {[string]$_.Name})
        currentRealXModelBodyMatches=$reals.Count
        realBodyRootIndices=@($reals | ForEach-Object {[int]$_.NativeRootIndex})
        status=$status
    })
}
$countsByStatus=@($rows.ToArray() | Group-Object status |
    ForEach-Object {[pscustomobject]@{
        status=[string]$_.Name
        distinctModels=[int]$_.Count
        totalDrawInstances=(@($_.Group | Measure-Object occurrences -Sum)[0].Sum)
    }})
$missing=@($rows.ToArray() | Where-Object {$_.status -ne 'REAL_XMODEL_GRAPH_BOUND'})
New-Item -ItemType Directory -Force -Path $out | Out-Null
$result=[ordered]@{
    schema='WaW360.AztecGate8StaticDrawModelDependencyAudit.v1'
    status='AZTEC_GATE8_1903_STATIC_DRAWS_27_NAMES_NATIVE_ROOT_RECONCILIATION_COMPLETE_DIAGNOSTIC'
    originalPcTypedCorpus=$sourcePath
    verifiedWorldBspOatPass=$oatPath
    nativeIdentityManifest=$nativePath
    xmodelBindingManifest=$bindingPath
    sourceDrawInstances=1903
    distinctSourceStaticModelNames=27
    nativeXModelIdentityRoots=284
    currentSourceBackedRealXModelBodies=2
    modelDependencyRows=$rows.ToArray()
    summary=$countsByStatus
    distinctModelsWithoutProvenRealGraph=$missing.Count
    staticDrawInstancesWithoutProvenRealGraph=(@($missing | Measure-Object occurrences -Sum)[0].Sum)
    fullStaticModelDrawGraphComplete=$false
    hardwareReady=$false
    note='A native Xbox asset name/root is NOT evidence of a real source-owned XModel graph. This audit emits no FF and does not serialize GfxStaticModelDrawInst. 27 draw-model identities and real XModel bodies must be reconciled before runtime approval.'
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Host '[AZTEC-GATE8-MODELS] =================================================='
Write-Host '[AZTEC-GATE8-MODELS] SOURCE: 1903 static draw instances, 27 model names'
Write-Host '[AZTEC-GATE8-MODELS] NATIVE: 284 XModel root identities'
Write-Host '[AZTEC-GATE8-MODELS] REAL SOURCE-BACKED XMODEL BODIES IN CURRENT FF: 2'
foreach($part in $countsByStatus){
    Write-Host ('[AZTEC-GATE8-MODELS] {0}: names={1} draws={2}' -f $part.status,$part.distinctModels,$part.totalDrawInstances)
}
Write-Host ('[AZTEC-GATE8-MODELS] PENDING REAL GRAPH: names={0}/27 draws={1}/1903' -f $missing.Count,$result.staticDrawInstancesWithoutProvenRealGraph)
Write-Host '[AZTEC-GATE8-MODELS] TOP SOURCE STATIC DRAW DEPENDENCIES:'
foreach($row in @($missing | Select-Object -First 12)){
    Write-Host ('[AZTEC-GATE8-MODELS] {0,4}x {1}  [{2}]' -f $row.occurrences,$row.pcModelName,$row.status)
}
Write-Host "[AZTEC-GATE8-MODELS] report=$reportPath"
Write-Host '[AZTEC-GATE8-MODELS] READ-ONLY. NO OAT BUILD / NO FF / NO XBOX.'
Write-Host '[AZTEC-GATE8-MODELS] =================================================='
