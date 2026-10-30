[CmdletBinding()]
param(
    [string]$ModelName='foliage_oki2_tallgrass',
    [int]$MinimumMeshVertices=3,
    [int]$MinimumTriangleIndices=3
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# Source-owned PC static draw -> isolated Xenon model-only FF.
# Never overwrite the passing 8509-root full-world scratch OAT candidate.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$n=Join-Path $base 'native_material_mapping'
$dependencyPath=Join-Path $n 'pc_gate8_static_model_dependency_audit\aztec_gate8_static_draw_xmodel_dependencies.json'
$frontPath=Join-Path $n 'pc_model_world_source_frontier\aztec_pc_model_world_source_frontier.json'
$baselinePath=Join-Path $base 'second_map_baseline.json'
$bridge=Join-Path $PSScriptRoot 'Run-FRUmansionOatXModelBridge.ps1'
$cap=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
# Preserve the first 718-draw proof at its existing path. Every additional
# source model gets a distinct, resumable diagnostic report/log and never
# overwrites the proven tallgrass canary.
$staticSlug=($ModelName.Trim().ToLowerInvariant() -replace '[^a-z0-9_-]','_')
if([string]::IsNullOrWhiteSpace($staticSlug) -or $staticSlug.Length -gt 90){
    throw 'Invalid static model name for isolated PC XModel evidence output.'
}
$work=Join-Path $n $(if($staticSlug -eq 'foliage_oki2_tallgrass'){
    'pc_gate8_first_static_xmodel'
}else{
    'pc_gate8_static_xmodel_'+$staticSlug
})
$report=Join-Path $work 'aztec_first_static_draw_xmodel_canary.json'
$log=Join-Path $work 'static_draw_xmodel_bridge.log'
foreach($p in @($dependencyPath,$frontPath,$baselinePath,$bridge,$cap)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing PC static model source proof: $p"}
}
$dep=Get-Content -LiteralPath $dependencyPath -Raw | ConvertFrom-Json
$front=Get-Content -LiteralPath $frontPath -Raw | ConvertFrom-Json
$baseline=Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
$caps=Get-Content -LiteralPath $cap -Raw | ConvertFrom-Json
if([string]$dep.schema -ne 'WaW360.AztecGate8StaticDrawModelDependencyAudit.v1' -or
   [string]$dep.status -ne 'AZTEC_GATE8_1903_STATIC_DRAWS_27_NAMES_NATIVE_ROOT_RECONCILIATION_COMPLETE_DIAGNOSTIC' -or
   [int]$dep.sourceDrawInstances -ne 1903 -or
   [int]$dep.distinctSourceStaticModelNames -ne 27 -or
   [bool]$dep.hardwareReady -or
   [string]$front.schema -ne 'WaW360.AztecPcModelWorldSourceFrontier.v1' -or
   [string]$front.status -ne 'AZTEC_PC_MODEL_WORLD_SOURCE_FRONTIER_PASS' -or
   [int]$front.nativeRoots -ne 8509 -or
   [int]$front.nativeXModelRoots -ne 284 -or
   [int]$front.nativeOwnedModelsWithPcName -ne 284 -or
   [string]$baseline.format -ne 'WaW360.SecondMapBaseline.v1' -or
   [string]$baseline.mapName -ine 'nazi_zombie_aztec_V2.2' -or
   [string]$caps.format -ne 'WaW360.OatGraphDumperCapabilities.v1' -or
   [string]$caps.oatRevision -ne '3beaa7c81af517f0c05f2b4612d477ed1541b676' -or
   @($caps.capabilities) -notcontains 't4-xmodel-classification-v1'){
    throw 'Gate8 static model source, native XModel identity or classification tooling proof changed.'
}
if($MinimumMeshVertices -lt 3 -or $MinimumTriangleIndices -lt 3){
    throw 'Require at least one real PC triangle for renderable XModel bridge.'
}
function Norm([string]$name){
    $v=$name.Trim().Replace('\','/').TrimStart(',')
    if($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){
        $v=$v.Substring(0,$v.Length-3)
    }
    return $v.ToLowerInvariant()
}
$wanted=Norm $ModelName
$selectedRows=@($dep.modelDependencyRows | Where-Object {(Norm ([string]$_.pcModelName)) -eq $wanted})
if($selectedRows.Count -ne 1 -or
   [string]$selectedRows[0].status -ne 'NATIVE_IDENTITY_ONLY_NO_REAL_GRAPH' -or
   @($selectedRows[0].nativeRootIndices).Count -ne 1){
    throw "Static draw model $ModelName is not a unique pending real-XModel root."
}
$row=$selectedRows[0]
$binds=@($front.nativeXModelIdentityBindings | Where-Object {
    [int]$_.NativeRootIndex -eq [int]$row.nativeRootIndices[0] -and
    (Norm ([string]$_.NativeName)) -eq $wanted -and
    -not [bool]$_.NativeReference -and
    [bool]$_.MatchingPcFastfileName
})
if($binds.Count -ne 1){throw "No unique source-backed PC XModel identity for $ModelName"}
$binding=$binds[0]
$roleChosen='';$sourceName='';$pc=''
foreach($role in @('map.ff','map_patch.ff','mod.ff')){
    if(@($binding.PcSourceRoles) -notcontains $role){continue}
    $stats=@($front.sourceFastfiles | Where-Object {[string]$_.Role -eq $role})
    $files=@($baseline.fastfiles | Where-Object {[string]$_.Role -eq $role})
    if($stats.Count -ne 1 -or $files.Count -ne 1){continue}
    $names=@($stats[0].XModelNames | Where-Object {(Norm ([string]$_)) -eq $wanted})
    if($names.Count -ne 1){continue}
    $path=[string]$files[0].Path
    if([string]$stats[0].SourceFF -ine $path -or
       -not(Test-Path -LiteralPath $path -PathType Leaf) -or
       [long](Get-Item -LiteralPath $path).Length -ne [long]$files[0].Bytes){
        throw "Pinned original-PC $role FF missing or changed."
    }
    $roleChosen=$role;$sourceName=[string]$names[0];$pc=$path
    break
}
if(-not $pc){throw "No verified PC fastfile role carries original static model $ModelName."}
$originalSha=(Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash
New-Item -ItemType Directory -Force -Path $work | Out-Null
$allowed=Join-Path $work 'allowed_static_draw_xmodel.txt'
[IO.File]::WriteAllLines($allowed,[string[]]@($sourceName),[Text.UTF8Encoding]::new($false))
# Keep a fresh short-path scratch workspace for the native Win32 OAT programs.
# Do not delete its successful GLTF, linked FF, or per-model classification.
$scratch=Join-Path ([IO.Path]::GetTempPath()) ('AzG8M_'+[Guid]::NewGuid().ToString('N').Substring(0,8))
if($scratch.Length -ge 105){throw "Native Win32 OAT model output path would be too long: $scratch"}
$manifest=Join-Path $scratch 'nuclear_4u_oat_xmodel_bridge.json'
Write-Host '[AZTEC-GATE8-XMODEL] =================================================='
Write-Host "[AZTEC-GATE8-XMODEL] selected=$sourceName drawInstances=$($row.occurrences) nativeRoot=$($row.nativeRootIndices[0])"
Write-Host "[AZTEC-GATE8-XMODEL] sourceRole=$roleChosen scratch=$scratch"
Write-Host '[AZTEC-GATE8-XMODEL] Running existing original-PC -> Xenon XModel OAT round-trip bridge.'
$arguments=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$bridge,
    '-PcFastFile',$pc,'-ModelName',$sourceName,'-OutputRoot',$scratch,
    '-AllowedModelNamesFile',$allowed,
    '-MinimumMeshVertices',[string]$MinimumMeshVertices,
    '-MinimumTriangleIndices',[string]$MinimumTriangleIndices)
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & powershell.exe @arguments 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $log -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($exit -ne 0 -or -not(Test-Path -LiteralPath $manifest -PathType Leaf)){
    if(Test-Path -LiteralPath $log){
        Get-Content -LiteralPath $log -Tail 30 | ForEach-Object {Write-Host "[AZTEC-GATE8-XMODEL] TRACE $_"}
    }
    throw "PC static draw model OAT bridge failed; exit=$exit log=$log"
}
$b=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
$ff=[string]$b.linkedFastFile
if([string]$b.format -ne 'WaW360.Nuclear4U.OatXModelBridge.v5' -or
   -not [bool]$b.bridgePass -or -not [bool]$b.xenonRoundTripPass -or
   -not [bool]$b.partClassificationComplete -or
   -not [bool]$b.selectedModelListed -or
   [int]$b.xenonReadbackErrors -ne 0 -or
   [int]$b.xenonLinkExitCode -ne 0 -or
   [int]$b.xenonReadbackExitCode -ne 0 -or
   (Norm ([string]$b.selectedModel)) -ne $wanted -or
   [string]$b.pcFastFile -ine $pc -or
   [int]$b.selectedMeshVertices -lt $MinimumMeshVertices -or
   [int]$b.selectedTriangleIndices -lt $MinimumTriangleIndices -or
   [int]$b.selectedTrianglePrimitives -lt 1 -or
   [int]$b.selectedLodCount -lt 1 -or
   -not(Test-Path -LiteralPath $ff -PathType Leaf) -or
   $ff.Length -ge 260){
    throw 'Standalone XModel bridge lacked a real PC triangle-mesh, model-specific classification, or Xenon OAT readback.'
}
$ffSha=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
$result=[ordered]@{
    schema='WaW360.AztecGate8StaticDrawXModelCanary.v1'
    status='AZTEC_GATE8_STATIC_DRAW_PC_XMODEL_TO_XENON_OAT_PASS_STANDALONE'
    sourceModel=$sourceName
    originalPcSourceRole=$roleChosen
    originalPcFastFile=$pc
    originalPcFastFileSha256=$originalSha
    drawInstances=[int]$row.occurrences
    nativeXModelRootIndex=[int]$row.nativeRootIndices[0]
    sourceMeshVertices=[int]$b.selectedMeshVertices
    sourceTriangleIndices=[int]$b.selectedTriangleIndices
    standaloneXenonFF=$ff
    standaloneXenonFFSha256=$ffSha
    bridgeManifest=$manifest
    bridgeLog=$log
    realModelBodyIntegratedInto8509RootWorldFF=$false
    all1903StaticDrawInstancesSerialized=$false
    xboxReady=$false
    note='Original PC XModel source reconstructed into a model-only Xenon FF and independently OAT loaded. 8509-root world FF and prior known-good canaries remain unchanged. No original Xbox model bytes are copied.'
}
$result | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host '[AZTEC-GATE8-XMODEL] =================================================='
Write-Host "[AZTEC-GATE8-XMODEL] PASS: $($result.status)"
Write-Host "[AZTEC-GATE8-XMODEL] model=$sourceName instances=$($row.occurrences) nativeRoot=$($row.nativeRootIndices[0])"
Write-Host "[AZTEC-GATE8-XMODEL] PC GLTF vertices=$($b.selectedMeshVertices) indices=$($b.selectedTriangleIndices) LODs=$($b.selectedLodCount)"
Write-Host "[AZTEC-GATE8-XMODEL] linkedFF=$ff"
Write-Host "[AZTEC-GATE8-XMODEL] report=$report"
Write-Host '[AZTEC-GATE8-XMODEL] MODEL-ONLY OAT PASS, NOT A FULL WORLD OR XBOX RELEASE.'
