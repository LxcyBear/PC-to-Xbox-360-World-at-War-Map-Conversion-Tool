[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Gate8: extract THREE verified standalone PC-derived Xenon model FFs into
# relocatable XModel/XSurface graphs. This never alters the passing map FF.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$n=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$wavePath=Join-Path $n 'pc_gate8_static_model_wave\aztec_gate8_static_model_wave.json'
$nativePath=Join-Path $n 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$cap=Join-Path (Split-Path -Parent $oat) 'waw360-graph-dumper-capabilities.json'
$graphRoot=Join-Path $repo '.deps\aztec8-static-xmodel-graphs'
$out=Join-Path $n 'pc_gate8_static_model_graph_handoff'
$report=Join-Path $out 'aztec_gate8_three_static_xmodel_graph_handoff.json'
foreach($p in @($wavePath,$nativePath,$oat,$cap)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Gate8 XModel graph prerequisite missing: $p"}
}
$wave=Get-Content -LiteralPath $wavePath -Raw | ConvertFrom-Json
$native=Get-Content -LiteralPath $nativePath -Raw | ConvertFrom-Json
$caps=Get-Content -LiteralPath $cap -Raw | ConvertFrom-Json
if([string]$wave.schema -ne 'WaW360.AztecGate8StandaloneXModelWave.v1' -or
   [string]$wave.status -ne 'AZTEC_GATE8_STATIC_MODEL_WAVE_STANDALONE_OAT_PASS' -or
   [int]$wave.modelOnlyOatProven -ne 3 -or
   [int]$wave.modelOnlyOatRepresentedDraws -ne 1572 -or
   @($wave.verifiedModels).Count -ne 3 -or
   [int]$wave.realBodiesIntegratedInto8509RootWorldFf -ne 0 -or
   [bool]$wave.xboxReady -or
   [string]$native.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
   [int]$native.assets -ne 8509 -or
   -not [bool]$native.typeSequenceVerified -or
   @($caps.capabilities) -notcontains 't4xenon-xmodel-relocatable-graph-v1'){
    throw 'Three-model OAT source proof, exact native root table, or pinned Xenon graph dumper is not verified.'
}
function Norm([string]$value){
    $v=$value.Trim().Replace('\','/').TrimStart(',')
    if($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(0,$v.Length-3)}
    return $v.ToLowerInvariant()
}
$wanted=@(
    [pscustomobject]@{name='foliage_oki2_tallgrass';root=7502;draws=718},
    [pscustomobject]@{name='foliage_pacific_grass01_large';root=7634;draws=456},
    [pscustomobject]@{name='foliage_cod5_hedgerow04';root=7465;draws=398}
)
$nativeMaterials=@($native.entries | Where-Object {[string]$_.OatTypeName -eq 'material'})
$nativeModels=@($native.entries | Where-Object {[string]$_.OatTypeName -eq 'xmodel'})
if($nativeModels.Count -ne 284){throw 'Native Aztec XModel root count changed.'}
for($i=0;$i -lt 3;$i++){
    $w=$wave.verifiedModels[$i];$v=$wanted[$i]
    if((Norm ([string]$w.model)) -ne $v.name -or
       [int]$w.nativeRoot -ne $v.root -or
       [int]$w.draws -ne $v.draws -or
       -not(Test-Path -LiteralPath ([string]$w.ff) -PathType Leaf) -or
       (Get-FileHash -LiteralPath ([string]$w.ff) -Algorithm SHA256).Hash -ine [string]$w.sha256){
        throw "Verified standalone model $($v.name) FF missing/modified."
    }
    $roots=@($nativeModels | Where-Object {
        [int]$_.Index -eq $v.root -and (Norm ([string]$_.Name)) -eq $v.name
    })
    if($roots.Count -ne 1){throw "No exact native Xbox root identity for $($v.name)/$($v.root)."}
}
New-Item -ItemType Directory -Force -Path $out | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $graphRoot 'dumps') | Out-Null
$rows=New-Object 'System.Collections.Generic.List[object]'
$firstError=''
function Assert-Blob([string]$base,[string]$rel,[long]$length,[string]$label){
    if($length -eq 0){return}
    if($length -lt 0 -or [string]::IsNullOrWhiteSpace($rel) -or
       [IO.Path]::IsPathRooted($rel) -or
       ($rel -split '[\\/]') -contains '..'){
        throw "Invalid graph blob path/length for $label : $rel ($length bytes)"
    }
    $p=Join-Path $base ($rel.Replace('/','\'))
    if(-not(Test-Path -LiteralPath $p -PathType Leaf) -or
       [long](Get-Item -LiteralPath $p).Length -ne $length){
        throw "Graph blob missing or incorrect ($label, $length bytes): $p"
    }
}
Write-Host '[AZTEC-GATE8-GRAPHS] ================================================'
Write-Host '[AZTEC-GATE8-GRAPHS] THREE real PC foliage XModels -> relocatable Xenon graph'
Write-Host '[AZTEC-GATE8-GRAPHS] Source model FFs: already OAT PASS. Full map FF untouched.'
for($i=0;$i -lt 3;$i++){
    $w=$wave.verifiedModels[$i];$v=$wanted[$i]
    $dump=Join-Path (Join-Path $graphRoot 'dumps') ('{0:D3}' -f $i)
    $log=Join-Path $out ('xmodel_graph_{0:D3}.log' -f $i)
    $short=Join-Path ([IO.Path]::GetTempPath()) ('AzG8Graph_'+[Guid]::NewGuid().ToString('N').Substring(0,8))
    $pass=$false;$blocker='';$graphFile='';$graphSha='';$surfs=0;$vertices=0;$triangles=0;$missing=0;$ambiguous=0;$dependencies=@()
    try{
        if($short.Length -ge 105){throw "Native Win32 OAT graph scratch path too long: $short"}
        New-Item -ItemType Directory -Force -Path $short | Out-Null
        $prior=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            & $oat '--no-color' '--include-assets' 'xmodel' '--model-format' 'gltf' '--output-folder' $short ([string]$w.ff) 2>&1 |
                ForEach-Object {[string]$_} | Set-Content -LiteralPath $log -Encoding UTF8
            $exit=[int]$LASTEXITCODE
        }finally{$ErrorActionPreference=$prior}
        if($exit -ne 0 -or
           @(Select-String -LiteralPath $log -Pattern '^\s*ERROR:').Count -gt 0 -or
           @(Select-String -LiteralPath $log -Pattern '(?i)^\s*Finished with\s+0\s+warnings?,\s+0\s+errors?\s*$').Count -eq 0){
            throw "Original PC-derived Xenon XModel graph Unlinker did not pass 0/0; exit=$exit"
        }
        $files=@(Get-ChildItem -LiteralPath (Join-Path $short 'xenon_xmodel_graphs') -Filter '*.json' -File)
        if($files.Count -ne 1){throw "Expected one real XModel graph, found $($files.Count)"}
        $g=Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json
        if([string]$g.format -ne 'WaW360.T4Xenon.XModelRelocatableGraph.v1' -or
           (Norm ([string]$g.name)) -ne $v.name -or
           [int]$g.abi.xmodel -ne 212 -or
           [int]$g.unsupported.count -ne 0 -or
           [int]$g.reusable.aliasCount -ne 0){
            throw "Real XModel graph unsupported, aliased or wrong identity: name=$($g.name) unsupported=$($g.unsupported.count) aliases=$($g.reusable.aliasCount)"
        }
        $bones=[int]$g.counts.numBones
        $nonRoots=[int]$g.counts.nonRootBones
        $surfs=[int]$g.counts.numSurfs
        $highMips=[int]$g.counts.highMipBounds
        if($surfs -lt 1 -or $bones -lt 0 -or $nonRoots -lt 0 -or
           @($g.surfaces).Count -ne $surfs -or
           @($g.materials).Count -ne $surfs -or
           @($g.boneNames).Count -ne $bones){throw 'XModel child/header/material count mismatch.'}
        Assert-Blob $short ([string]$g.rootFile) 212 'xmodel.root'
        Assert-Blob $short ([string]$g.parentListFile) $nonRoots 'parentList'
        Assert-Blob $short ([string]$g.quatsFile) ([long]$nonRoots*[int]$g.abi.xmodelQuat) 'quats'
        Assert-Blob $short ([string]$g.transFile) ([long]$nonRoots*16) 'trans'
        Assert-Blob $short ([string]$g.partClassificationFile) $bones 'partClassification'
        Assert-Blob $short ([string]$g.baseMatFile) ([long]$bones*[int]$g.abi.dobjAnimMat) 'baseMat'
        Assert-Blob $short ([string]$g.surfacesFile) ([long]$surfs*[int]$g.abi.xsurface) 'surfaces'
        Assert-Blob $short ([string]$g.boneInfoFile) ([long]$bones*[int]$g.abi.boneInfo) 'boneInfo'
        Assert-Blob $short ([string]$g.highMipBoundsFile) ([long]$highMips*[int]$g.abi.highMipBounds) 'highMips'
        foreach($surface in @($g.surfaces)){
            $si=[int]$surface.index
            $vertices+=[int]$surface.vertCount
            $triangles+=[int]$surface.triCount
            Assert-Blob $short ([string]$surface.vertsBlendFile) ([long][int]$surface.vertsBlendCount*2) "surf$si.blend"
            Assert-Blob $short ([string]$surface.verts0File) ([long][int]$surface.vertCount*[int]$g.abi.packedVertex) "surf$si.verts"
            Assert-Blob $short ([string]$surface.vertListFile) ([long][int]$surface.vertListCount*[int]$g.abi.rigidVertList) "surf$si.rigid"
            Assert-Blob $short ([string]$surface.triIndicesFile) ([long][int]$surface.triCount*[int]$g.abi.triIndex) "surf$si.tris"
            foreach($tree in @($surface.collisionTrees)){
                Assert-Blob $short ([string]$tree.treeFile) ([long][int]$g.abi.collisionTree) "surf$si.tree"
                Assert-Blob $short ([string]$tree.nodesFile) ([long][int]$tree.nodeCount*[int]$g.abi.collisionNode) "surf$si.nodes"
                Assert-Blob $short ([string]$tree.leafsFile) ([long][int]$tree.leafCount*[int]$g.abi.collisionLeaf) "surf$si.leafs"
            }
        }
        if($vertices -lt 3 -or $triangles -lt 1){throw 'Graph has no valid renderable Xenon triangle mesh.'}
        $deps=New-Object 'System.Collections.Generic.List[object]'
        foreach($mat in @($g.materials)){
            $name=[string]$mat
            if([string]::IsNullOrWhiteSpace($name)){continue}
            $matches=@($nativeMaterials | Where-Object {(Norm ([string]$_.Name)) -eq (Norm $name)})
            if($matches.Count -eq 0){$missing++}
            if($matches.Count -gt 1){$ambiguous++}
            $deps.Add([pscustomobject]@{
                name=$name;nativeIdentityMatches=$matches.Count
                nativeRootIndices=@($matches | ForEach-Object {[int]$_.Index})
            })
        }
        $dependencies=@($deps.ToArray())
        if(Test-Path -LiteralPath $dump -PathType Container){Remove-Item -LiteralPath $dump -Recurse -Force}
        New-Item -ItemType Directory -Force -Path $dump | Out-Null
        $copy=@(& robocopy.exe $short $dump '/E' '/R:1' '/W:1' '/NP' '/NFL' '/NDL' '/NJH' '/NJS' 2>&1)
        if([int]$LASTEXITCODE -ge 8){throw 'Copying standalone XModel graph blobs failed.'}
        $graphFile=Join-Path (Join-Path $dump 'xenon_xmodel_graphs') $files[0].Name
        if(-not(Test-Path -LiteralPath $graphFile -PathType Leaf) -or
           (Get-FileHash -LiteralPath $graphFile -Algorithm SHA256).Hash -ine
           (Get-FileHash -LiteralPath $files[0].FullName -Algorithm SHA256).Hash){
            throw 'Copied model graph failed exact SHA check.'
        }
        Assert-Blob $dump ([string]$g.rootFile) 212 'copied.root'
        $graphSha=(Get-FileHash -LiteralPath $graphFile -Algorithm SHA256).Hash
        $pass=$true
    }catch{
        $blocker=$_.Exception.Message
    }finally{
        if(Test-Path -LiteralPath $short -PathType Container){
            Remove-Item -LiteralPath $short -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    $rows.Add([pscustomobject]@{
        ordinal=$i;name=$v.name;nativeRootIndex=$v.root;draws=$v.draws
        sourceStandaloneFf=[string]$w.ff;sourceStandaloneFfSha256=[string]$w.sha256
        graphFile=$graphFile;graphSha256=$graphSha;graphRoot=$dump
        surfaces=$surfs;packedVertices=$vertices;triangles=$triangles
        unresolvedMaterialIdentities=$missing;ambiguousMaterialIdentities=$ambiguous
        materialDependencies=$dependencies;pass=$pass;error=$blocker;log=$log
    })
    if($pass){
        Write-Host "[AZTEC-GATE8-GRAPHS] PASS $($v.name) :: surfaces=$surfs vertices=$vertices triangles=$triangles, missingMaterialIdentities=$missing ambiguous=$ambiguous"
    }else{
        Write-Host "[AZTEC-GATE8-GRAPHS] BLOCKED $($v.name): $blocker"
        if(Test-Path -LiteralPath $log -PathType Leaf){
            Get-Content -LiteralPath $log -Tail 12 | ForEach-Object {Write-Host "[AZTEC-GATE8-GRAPHS] OAT $_"}
        }
        $firstError=$blocker
        break
    }
}
$allPass=$rows.Count -eq 3 -and @($rows.ToArray() | Where-Object {-not $_.pass}).Count -eq 0
$result=[ordered]@{
    schema='WaW360.AztecGate8StaticXModelGraphHandoff.v1'
    status=if($allPass){'AZTEC_GATE8_THREE_STATIC_XMODEL_RELOCATABLE_GRAPHS_PASS'}else{'AZTEC_GATE8_STATIC_XMODEL_GRAPH_HANDOFF_BLOCKED'}
    nativeRootCount=8509;sourceStaticDraws=1903;sourceStaticModelNames=27
    standaloneRealModelsVerified=3;correspondingDrawOccurrences=1572
    graphsVerified=@($rows.ToArray() | Where-Object {$_.pass}).Count
    graphRoot=$graphRoot;models=@($rows.ToArray())
    firstBlocker=$firstError
    allStaticDrawRecordsIntegrated=$false
    realModelBodiesIntegratedIntoFullWorldFf=0
    hardwareReady=$false
    note='Original-PC source converted standalone XModel FFs were independently unloaded to full binary Xenon graph blobs with per-child sizes and SHA. This does not insert any model payloads or GfxStaticModelDrawInst into the 8509-root world FF.'
}
$result | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host '[AZTEC-GATE8-GRAPHS] ================================================'
Write-Host "[AZTEC-GATE8-GRAPHS] status=$($result.status) graphs=$($result.graphsVerified)/3"
Write-Host "[AZTEC-GATE8-GRAPHS] result=$report"
Write-Host '[AZTEC-GATE8-GRAPHS] GRAPH EXPORT/OWNERSHIP PROOF ONLY. NO FULL FF OR XBOX.'
if(-not $allPass){throw "Gate8 graph handoff blocked: $firstError"}
