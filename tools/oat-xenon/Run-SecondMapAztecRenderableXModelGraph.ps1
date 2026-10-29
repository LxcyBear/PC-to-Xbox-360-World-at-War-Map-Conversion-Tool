[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$work=Join-Path $base 'pc_xmodel_renderable_graph'
$canaryPath=Join-Path $base 'pc_xmodel_renderable_canary\aztec_pc_xmodel_renderable_canary.json'
$nativePath=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$mixedPath=Join-Path $base 'pc_iwd_rawfile_integration\aztec_pc_iwd_rawfile_integration.json'
$frontPath=Join-Path $base 'pc_model_world_source_frontier\aztec_pc_model_world_source_frontier.json'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$cap=Join-Path (Split-Path -Parent $oat) 'waw360-graph-dumper-capabilities.json'
foreach($p in @($canaryPath,$nativePath,$mixedPath,$frontPath,$oat,$cap)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing Aztec canary/graph input: $p"}
}
$canary=Get-Content -LiteralPath $canaryPath -Raw | ConvertFrom-Json
$native=Get-Content -LiteralPath $nativePath -Raw | ConvertFrom-Json
$mixed=Get-Content -LiteralPath $mixedPath -Raw | ConvertFrom-Json
$front=Get-Content -LiteralPath $frontPath -Raw | ConvertFrom-Json
$caps=Get-Content -LiteralPath $cap -Raw | ConvertFrom-Json
if([string]$canary.status -ne 'AZTEC_PC_XMODEL_RENDERABLE_MESH_PASS' -or
   [string]$canary.selectedModel -ine 'zombie_x2_icon' -or
   [int]$canary.verifiedRenderableVertices -lt 128 -or
   [int]$canary.verifiedTriangleIndices -lt 192 -or
   -not [bool]$canary.classificationComplete -or
   -not [bool]$canary.xenonOatRoundtripPass -or
   [string]$mixed.status -ne 'AZTEC_PC_IWD_RAWFILE_COMBINED_GRAPH_PASS' -or
   [int]$mixed.pixelShaExactReadbacks -ne 2 -or
   [int]$mixed.rawFileShaExactReadbacks -ne 114 -or
   [int]$front.nativeOwnedModelsWithPcName -ne 284 -or
   -not [bool]$native.TypeSequenceVerified -or
   @($native.Entries).Count -ne 8509 -or
   @($caps.capabilities) -notcontains 't4xenon-xmodel-relocatable-graph-v1'){
    throw 'The successful PC renderable XModel, combined PC FF or Xenon graph dumper is not verified.'
}
$bridgePath=[string]$canary.bridgeManifest
if(-not (Test-Path -LiteralPath $bridgePath -PathType Leaf)){throw "PC model bridge manifest missing: $bridgePath"}
$bridge=Get-Content -LiteralPath $bridgePath -Raw | ConvertFrom-Json
if([string]$bridge.format -ne 'WaW360.Nuclear4U.OatXModelBridge.v5' -or
   -not [bool]$bridge.bridgePass -or
   [string]$bridge.selectedModel -ine 'zombie_x2_icon' -or
   [int]$bridge.selectedMeshVertices -lt 128 -or
   [int]$bridge.selectedTriangleIndices -lt 192 -or
   -not (Test-Path -LiteralPath ([string]$bridge.linkedFastFile) -PathType Leaf) -or
   (Get-FileHash -LiteralPath ([string]$canary.sourceFastFile) -Algorithm SHA256).Hash -ine [string]$canary.sourceFastFileSha256){
    throw 'Original PC mesh source or independent Xenon FF has changed.'
}
function Normalize([string]$value){
    $v=$value.Trim().TrimStart(',').Replace('\','/')
    if($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(0,$v.Length-3)}
    return $v.ToLowerInvariant()
}
$rootMatches=@($native.Entries | Where-Object {
    [string]$_.TypeName -ieq 'xmodel' -and (Normalize ([string]$_.Name)) -eq 'zombie_x2_icon'
})
if($rootMatches.Count -ne 1){throw "Expected exactly one normalized native XModel root for zombie_x2_icon, got $($rootMatches.Count)"}
$root=$rootMatches[0]
$materialRoots=@($native.Entries | Where-Object {[string]$_.TypeName -ieq 'material'})
$targetGraph=Join-Path $work 'graph\dumps\000'
$log=Join-Path $work 'xmodel_graph_oat.log'
$report=Join-Path $work 'aztec_pc_xmodel_relocatable_graph.json'
$txt=Join-Path $work 'aztec_pc_xmodel_relocatable_graph.txt'
New-Item -Path $work -ItemType Directory -Force | Out-Null
$short=Join-Path ([IO.Path]::GetTempPath()) ('wawAzXG_'+[guid]::NewGuid().ToString('N').Substring(0,9))
if($short.Length -gt 115){throw 'OAT requires a short physical graph extraction path.'}
New-Item -Path $short -ItemType Directory -Force | Out-Null
if(Test-Path -LiteralPath $targetGraph -PathType Container){Remove-Item -LiteralPath $targetGraph -Recurse -Force}
New-Item -Path $targetGraph -ItemType Directory -Force | Out-Null
$status='AZTEC_PC_RENDERABLE_XMODEL_GRAPH_BLOCKED'
$blocker='';$exit=-1;$v=0;$tri=0;$surfs=0;$sha=''
$deps=New-Object 'System.Collections.Generic.List[object]'
function CheckBlob([string]$rel,[long]$length,[string]$kind){
    if($length -eq 0){return}
    if([string]::IsNullOrWhiteSpace($rel) -or $rel.Contains('..') -or [IO.Path]::IsPathRooted($rel)){
        throw "Invalid graph blob path for $kind"
    }
    $p=Join-Path $short ($rel.Replace('/','\'))
    if(-not (Test-Path -LiteralPath $p -PathType Leaf) -or
       [long](Get-Item -LiteralPath $p).Length -ne $length){
        throw "Missing or incorrect graph blob for $kind ($length bytes): $p"
    }
}
Write-Host '[AZTEC-XMODEL-GRAPH] PC zombie_x2_icon -> RELOCATABLE XENON MODEL GRAPH'
Write-Host '[AZTEC-XMODEL-GRAPH] Source-owned geometry, native identity only. NO full FF.'
try{
    $old=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        $args=@('--no-color','--include-assets','xmodel','--model-format','gltf','--output-folder',$short,[string]$bridge.linkedFastFile)
        & $oat @args 2>&1 | ForEach-Object {[string]$_} | Set-Content -LiteralPath $log -Encoding UTF8
        $exit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$old}
    if($exit -ne 0 -or
       @(Select-String -LiteralPath $log -Pattern '^\s*ERROR:').Count -gt 0 -or
       @(Select-String -LiteralPath $log -Pattern '(?i)^\s*Finished with\s+0\s+warnings?,\s+0\s+errors?\s*$').Count -eq 0){
        throw "Xenon graph Unlinker did not finish 0/0; exit=$exit log=$log"
    }
    $gFiles=@(Get-ChildItem -LiteralPath (Join-Path $short 'xenon_xmodel_graphs') -Filter '*.json' -File -ErrorAction Stop)
    if($gFiles.Count -ne 1){throw "Expected one relocatable graph, found $($gFiles.Count)"}
    $g=Get-Content -LiteralPath $gFiles[0].FullName -Raw | ConvertFrom-Json
    if([string]$g.format -ne 'WaW360.T4Xenon.XModelRelocatableGraph.v1' -or
       (Normalize ([string]$g.name)) -ne (Normalize ([string]$root.Name)) -or
       [int]$g.abi.xmodel -ne 212 -or
       [int]$g.unsupported.count -ne 0 -or
       [int]$g.reusable.aliasCount -ne 0){
        throw 'Original PC geometry graph has unexpected ABI/name/unsupported branches.'
    }
    $surfs=[int]$g.counts.numSurfs
    if($surfs -lt 1 -or @($g.surfaces).Count -ne $surfs -or
       @($g.materials).Count -ne $surfs){throw 'Xenon XModel surfaces/material counts do not match.'}
    CheckBlob ([string]$g.rootFile) 212 'root'
    CheckBlob ([string]$g.surfacesFile) ([long]$surfs*[int]$g.abi.xsurface) 'surface array'
    CheckBlob ([string]$g.partClassificationFile) ([long][int]$g.counts.numBones) 'bone classifications'
    foreach($s in @($g.surfaces)){
        $v+=[int]$s.vertCount
        $tri+=[int]$s.triCount
        CheckBlob ([string]$s.verts0File) ([long][int]$s.vertCount*[int]$g.abi.packedVertex) 'vertex data'
        CheckBlob ([string]$s.triIndicesFile) ([long][int]$s.triCount*[int]$g.abi.triIndex) 'triangle indices'
    }
    if($v -lt 128 -or $tri -lt 64){throw "Xenon packed geometry too small: vertices=$v triangles=$tri"}
    foreach($mat in @($g.materials)){
        if([string]::IsNullOrWhiteSpace([string]$mat)){continue}
        $norm=Normalize ([string]$mat)
        $matches=@($materialRoots | Where-Object { (Normalize ([string]$_.Name)) -eq $norm })
        $deps.Add([pscustomobject]@{Name=[string]$mat;NativeMaterialRootMatches=$matches.Count})
    }
    $copied=@(& robocopy.exe $short $targetGraph '/E' '/R:1' '/W:1' '/NP' '/NFL' '/NDL' '/NJH' '/NJS' 2>&1 | ForEach-Object {[string]$_})
    if([int]$LASTEXITCODE -ge 8){throw 'Graph blob robocopy failed.'}
    $copy=@(Get-ChildItem -LiteralPath (Join-Path $targetGraph 'xenon_xmodel_graphs') -Filter '*.json' -File -ErrorAction Stop)
    if($copy.Count -ne 1 -or
       (Get-FileHash -LiteralPath $copy[0].FullName -Algorithm SHA256).Hash -ine
       (Get-FileHash -LiteralPath $gFiles[0].FullName -Algorithm SHA256).Hash){throw 'Copied XModel graph failed SHA check'}
    $sha=(Get-FileHash -LiteralPath $copy[0].FullName -Algorithm SHA256).Hash
    $status='AZTEC_PC_RENDERABLE_XMODEL_GRAPH_PASS'
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-XMODEL-GRAPH] BLOCKER: $blocker"
    if(Test-Path -LiteralPath $log -PathType Leaf){
        foreach($line in @(Get-Content -LiteralPath $log -Tail 8)){Write-Host "[AZTEC-XMODEL-GRAPH] LOG: $line"}
    }
}finally{
    if(Test-Path -LiteralPath $short -PathType Container){Remove-Item -LiteralPath $short -Recurse -Force}
}
$doc=[ordered]@{
    schema='WaW360.AztecPcRenderableXModelGraph.v1'
    status=$status;model=[string]$canary.selectedModel
    nativeRootIndex=[int]$root.Index
    pcGlTfVertices=[int]$canary.verifiedRenderableVertices
    pcGlTfIndices=[int]$canary.verifiedTriangleIndices
    xenonPackedVertices=$v;xenonTriangles=$tri;xenonSurfaces=$surfs
    nativeMaterialDependencies=$deps.ToArray()
    sourcePCFastfile=[string]$canary.sourceFastFile
    originalPcSha256=[string]$canary.sourceFastFileSha256
    sourceModelOnlyFF=[string]$bridge.linkedFastFile
    graphRoot=(Join-Path $work 'graph');graphSha256=$sha
    oatExitCode=$exit;blocker=$blocker
    fullAztecXModelsIntegrated=0;hardwareReady=$false
    note='Native root is identity only. Packed Xenon geometry derives from original PC mod.ff. No full 8509-root FF was emitted.'
}
$doc | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add("Aztec original-PC renderable XModel graph: $status")
$lines.Add("root=$($root.Index) model=$($canary.selectedModel) glTF= $($canary.verifiedRenderableVertices) verts, $($canary.verifiedTriangleIndices) indices")
$lines.Add("xenon packed vertices=$v, triangles=$tri, surfaces=$surfs; graph SHA=$sha")
foreach($d in $deps.ToArray()){$lines.Add("material=$($d.Name), nativeMaterialRootMatches=$($d.NativeMaterialRootMatches)")}
$lines.Add('No native hardware readiness. No combined map FF emitted.')
[IO.File]::WriteAllLines($txt,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-XMODEL-GRAPH] ================= RESULT ==========================='
Write-Host "[AZTEC-XMODEL-GRAPH] status=$status"
Write-Host "[AZTEC-XMODEL-GRAPH] nativeRoot=$($root.Index) Xenon verts=$v triangles=$tri surfaces=$surfs"
Write-Host "[AZTEC-XMODEL-GRAPH] materialReferences=$($deps.Count); unresolved=$( @($deps.ToArray() | Where-Object {$_.NativeMaterialRootMatches -ne 1}).Count )"
Write-Host "[AZTEC-XMODEL-GRAPH] report=$txt"
Write-Host '[AZTEC-XMODEL-GRAPH] No full-map FF. NOT FOR XBOX.'
if($status -ne 'AZTEC_PC_RENDERABLE_XMODEL_GRAPH_PASS'){exit 4}
