[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Gate8 proof-only five-XModel corpus preparation; never mutates validated
# two-model graphs, original-PC source, reference FF or 8509-root world FF.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$prior=Join-Path $base 'pc_two_xmodel_two_material_fullgraph_integration'
$existing=Join-Path $prior 'pc_two_xmodel_graph'
$priorBind=Join-Path $prior 'aztec_two_pc_xmodel_root_binding.json'
$handoff=Join-Path $base 'pc_gate8_static_model_graph_handoff\aztec_gate8_three_static_xmodel_graph_handoff.json'
$wave=Join-Path $base 'pc_gate8_static_model_wave\aztec_gate8_static_model_wave.json'
$native=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$work=Join-Path $repo '.deps\aztec8-five-xmodels'
$graphs=Join-Path $work 'graph'
$bindingPath=Join-Path $work 'aztec_gate8_five_xmodel_binding.json'
$reportPath=Join-Path $work 'aztec_gate8_five_xmodel_staging.json'
foreach($p in @($priorBind,$handoff,$wave,$native)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Gate8 five-model graft prerequisite missing: $p"}
}
foreach($p in @($existing,(Join-Path $repo '.deps\aztec8-static-xmodel-graphs'))){
    if(-not(Test-Path -LiteralPath $p -PathType Container)){throw "Verified original-PC Xenon XModel graph root missing: $p"}
}
$two=Get-Content -LiteralPath $priorBind -Raw | ConvertFrom-Json
$three=Get-Content -LiteralPath $handoff -Raw | ConvertFrom-Json
$w=Get-Content -LiteralPath $wave -Raw | ConvertFrom-Json
$id=Get-Content -LiteralPath $native -Raw | ConvertFrom-Json
if([string]$two.format -ne 'WaW360.AztecPcTwoXModelBinding.v1' -or
   -not [bool]$two.bindingPass -or
   [int]$two.nativeRoots -ne 8509 -or
   [int]$two.nativeXModelRoots -ne 284 -or
   @($two.ownedBindings).Count -ne 2 -or
   @($two.referenceBindings).Count -ne 0 -or
   [string]$three.schema -ne 'WaW360.AztecGate8StaticXModelGraphHandoff.v1' -or
   [string]$three.status -ne 'AZTEC_GATE8_THREE_STATIC_XMODEL_RELOCATABLE_GRAPHS_PASS' -or
   [int]$three.graphsVerified -ne 3 -or
   [int]$three.standaloneRealModelsVerified -ne 3 -or
   [int]$three.correspondingDrawOccurrences -ne 1572 -or
   @($three.models).Count -ne 3 -or
   [bool]$three.hardwareReady -or
   [string]$w.status -ne 'AZTEC_GATE8_STATIC_MODEL_WAVE_STANDALONE_OAT_PASS' -or
   [int]$w.modelOnlyOatProven -ne 3 -or
   [int]$w.modelOnlyOatRepresentedDraws -ne 1572 -or
   [string]$id.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
   [int]$id.assets -ne 8509 -or
   -not [bool]$id.typeSequenceVerified){
    throw 'Original two-model or three-model OAT proof does not match exact Gate8 source corpus.'
}
$wanted=@(
    [pscustomobject]@{name='zombie_x2_icon';root=-1;draws=0},
    [pscustomobject]@{name='tag_origin';root=-1;draws=0},
    [pscustomobject]@{name='foliage_oki2_tallgrass';root=7502;draws=718},
    [pscustomobject]@{name='foliage_pacific_grass01_large';root=7634;draws=456},
    [pscustomobject]@{name='foliage_cod5_hedgerow04';root=7465;draws=398}
)
function Norm([string]$name){
    $n=$name.Trim().Replace('\','/').TrimStart(',')
    if($n.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){$n=$n.Substring(0,$n.Length-3)}
    return $n.ToLowerInvariant()
}
$sourceDirs=New-Object 'System.Collections.Generic.List[string]'
$rows=New-Object 'System.Collections.Generic.List[object]'
$seen=New-Object 'System.Collections.Generic.HashSet[int]'
for($i=0;$i -lt 5;$i++){
    $want=$wanted[$i]
    if($i -lt 2){
        $b=$two.ownedBindings[$i]
        $sourceDir=Join-Path $existing ('dumps\{0:D3}' -f $i)
        $rootIndex=[int]$b.NativeRootIndex
        if([int]$b.CorpusOrdinal -ne $i -or
           (Norm ([string]$b.CorpusName)) -ne $want.name -or
           (Norm ([string]$b.NativeName)) -ne $want.name){
            throw "Existing original-PC two-XModel root binding changed at ordinal $i."
        }
    }else{
        $m=$three.models[$i-2];$v=$w.verifiedModels[$i-2]
        if(-not [bool]$m.pass -or
           (Norm ([string]$m.name)) -ne $want.name -or
           [int]$m.ordinal -ne $i-2 -or
           [int]$m.nativeRootIndex -ne $want.root -or
           [int]$m.draws -ne $want.draws -or
           [int]$m.unresolvedMaterialIdentities -ne 0 -or
           [int]$m.ambiguousMaterialIdentities -ne 0 -or
           (Norm ([string]$v.model)) -ne $want.name -or
           [int]$v.nativeRoot -ne $want.root -or
           [string]$m.sourceStandaloneFfSha256 -ine [string]$v.sha256 -or
           -not(Test-Path -LiteralPath ([string]$m.graphFile) -PathType Leaf) -or
           (Get-FileHash -LiteralPath ([string]$m.graphFile) -Algorithm SHA256).Hash -ine
               [string]$m.graphSha256){
            throw "Previously passing source-owned foliage graph $($want.name) has changed."
        }
        $rootIndex=$want.root
        $sourceDir=[string]$m.graphRoot
    }
    $roots=@($id.entries | Where-Object {
        [int]$_.Index -eq $rootIndex -and
        [string]$_.OatTypeName -eq 'xmodel' -and
        (Norm ([string]$_.Name)) -eq $want.name
    })
    if($roots.Count -ne 1 -or -not $seen.Add($rootIndex)){
        throw "No unique native Xbox root identity for source-derived $($want.name)."
    }
    if(-not(Test-Path -LiteralPath $sourceDir -PathType Container)){
        throw "Missing verified $($want.name) graph folder: $sourceDir"
    }
    $graphsSource=@(Get-ChildItem -LiteralPath (Join-Path $sourceDir 'xenon_xmodel_graphs') -Filter '*.json' -File)
    if($graphsSource.Count -ne 1){throw "Expected exactly one source-owned graph JSON for $($want.name)."}
    $graph=Get-Content -LiteralPath $graphsSource[0].FullName -Raw | ConvertFrom-Json
    if([string]$graph.format -ne 'WaW360.T4Xenon.XModelRelocatableGraph.v1' -or
       (Norm ([string]$graph.name)) -ne $want.name -or
       [int]$graph.abi.xmodel -ne 212 -or
       [int]$graph.unsupported.count -ne 0 -or
       [int]$graph.reusable.aliasCount -ne 0){
        throw "Real Xenon graph ABI/identity/unsupported policy mismatch for $($want.name)."
    }
    $sourceDirs.Add($sourceDir)
    $rows.Add([ordered]@{
        NativeRootIndex=$rootIndex
        NativeName=[string]$roots[0].Name
        CorpusOrdinal=$i
        CorpusName=$want.name
    })
}
if(Test-Path -LiteralPath $graphs -PathType Container){
    # Only our NEW five-XModel staging scratch directory is replaced.
    Remove-Item -LiteralPath $graphs -Recurse -Force
}
New-Item -ItemType Directory -Force -Path (Join-Path $graphs 'dumps') | Out-Null
$proofs=New-Object 'System.Collections.Generic.List[object]'
for($i=0;$i -lt 5;$i++){
    $src=$sourceDirs[$i]
    $dst=Join-Path (Join-Path $graphs 'dumps') ('{0:D3}' -f $i)
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    & robocopy.exe $src $dst '/E' '/R:1' '/W:1' '/NP' '/NFL' '/NDL' '/NJH' '/NJS' | Out-Null
    if([int]$LASTEXITCODE -ge 8){throw "Staging source-owned XModel graph #$i failed."}
    $sgraph=@(Get-ChildItem -LiteralPath (Join-Path $src 'xenon_xmodel_graphs') -Filter '*.json' -File)
    $dgraph=@(Get-ChildItem -LiteralPath (Join-Path $dst 'xenon_xmodel_graphs') -Filter '*.json' -File)
    if($sgraph.Count -ne 1 -or $dgraph.Count -ne 1 -or
       (Get-FileHash -LiteralPath $sgraph[0].FullName -Algorithm SHA256).Hash -ine
       (Get-FileHash -LiteralPath $dgraph[0].FullName -Algorithm SHA256).Hash){
        throw "Staged real XModel graph #$i did not preserve original source graph SHA."
    }
    $proofs.Add([ordered]@{
        ordinal=$i;name=$wanted[$i].name;nativeRoot=[int]$rows[$i].NativeRootIndex
        stagedGraph=$dgraph[0].FullName
        sha256=(Get-FileHash -LiteralPath $dgraph[0].FullName -Algorithm SHA256).Hash
    })
    Write-Host "[AZTEC-GATE8-FIVE] STAGED real PC-derived XModel graph #$i $($wanted[$i].name)"
}
$binding=[ordered]@{
    format='WaW360.AztecGate8FiveSourceXModelBinding.v1'
    bindingPass=$true;nativeRoots=8509;nativeXModelRoots=284
    validationOnly=$true;hardwareReady=$false
    ownedBindings=@($rows.ToArray())
    referenceBindings=@()
    note='Five exact source-owned standalone OAT-verified XModel graphs: two previous plus three Gate8 static foliage models. No synthetic XModel bodies, no hardware approval.'
}
$binding | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $bindingPath -Encoding UTF8
$proof=[ordered]@{
    schema='WaW360.AztecGate8FiveXModelStaging.v1'
    status='AZTEC_GATE8_FIVE_SOURCE_XMODEL_GRAPHS_STAGED'
    modelCount=5;rootCount=8509;staticDrawsRepresented=1572
    graphRoot=$graphs;bindingManifest=$bindingPath
    graphProofs=@($proofs.ToArray())
    sourceThreeGraphReport=$handoff
    worldXModelRootsAlreadyUpgraded=2
    worldStaticDrawLinksUpgraded=0
    hardwareReady=$false
}
$proof | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Host "[AZTEC-GATE8-FIVE] status=$($proof.status), modelGraphs=5, staticDrawsRepresented=1572"
Write-Host "[AZTEC-GATE8-FIVE] graph=$graphs"
Write-Host "[AZTEC-GATE8-FIVE] binding=$bindingPath"
Write-Host '[AZTEC-GATE8-FIVE] STAGING ONLY. EXISTING WORLD FF AND TWO-MODEL GRAPH UNCHANGED.'
