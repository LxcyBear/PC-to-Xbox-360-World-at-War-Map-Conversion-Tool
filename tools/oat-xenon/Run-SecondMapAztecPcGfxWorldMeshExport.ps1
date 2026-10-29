[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$proofPath=Join-Path $base 'pc_gfxworld_surface_source_probe\aztec_pc_gfxworld_surface_source_probe.json'
$sixPath=Join-Path $base 'pc_six_family_comworld_fullgraph_integration\aztec_pc_six_family_comworld_graph.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$work=Join-Path $base 'pc_gfxworld_source_mesh_export'
$buildLog=Join-Path $work 'dotnet_build.log'
$runLog=Join-Path $work 'obj_export.log'
$manifest=Join-Path $work 'aztec_pc_gfxworld_obj_export.json'
$report=Join-Path $work 'aztec_pc_gfxworld_obj_export.txt'
$obj=Join-Path $work 'nazi_zombie_aztec_PC_GfxWorld_19551_triangles.obj'

foreach($file in @($proofPath,$sixPath,$cli)){
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)){
        throw "Aztec PC GfxWorld source mesh export prerequisite missing: $file"
    }
}
$surf=Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json
$six=Get-Content -LiteralPath $sixPath -Raw | ConvertFrom-Json
if([string]$surf.Schema -ne 'WaW360.AztecPcGfxSurfaceSourceProbe.v1' -or
   [string]$surf.Status -ne 'AZTEC_PC_GFXWORLD_735_SURFACE_ARRAY_CANDIDATE_PASS' -or
   [int]$surf.SurfaceCount -ne 735 -or
   [int]$surf.SurfaceStride -ne 52 -or
   [bool]$surf.XboxHardwareReady -or
   [bool]$surf.FullGfxWorldSerialized -or
   [string]$six.schema -ne 'WaW360.AztecPcSixFamilyComWorldGraph.v1' -or
   [string]$six.status -ne 'AZTEC_PC_SIX_FAMILY_COMWORLD_GRAPH_PASS' -or
   [int]$six.nativeRoots -ne 8509 -or
   -not [bool]$six.layoutReplayExact -or
   -not [bool]$six.oatZeroWarningsZeroErrors -or
   [bool]$six.readyForXbox){
    throw 'Original PC GfxWorld surface topology or previous 8509-root six-family scratch baseline changed.'
}
$winner=@($surf.TopCandidates | Where-Object {
    [int]$_.SourceOffset -eq 0x058E361E -and
    [int]$_.ValidSurfaceRecords -eq 735 -and
    [int]$_.ValidBoundingBoxes -eq 735 -and
    [long]$_.TotalTriangleCount -eq 19551 -and
    [int]$_.UncoveredIndexEntries -eq 0 -and
    [int]$_.IndexSpanOverlapEntries -eq 0 -and
    [int]$_.LocalIndicesInRange -eq 58653
})
if($winner.Count -ne 1 -or
   [string]$winner[0].OriginalPCArraySha256 -ine
      '919FF1CDB1EC5FCD57ED4020F1908AE813C1FB570C8631C15135E07C5DF852E5'){
    throw 'Pinned 735/735 source GfxSurface with complete local triangle-index topology missing.'
}
$pc=[string]$surf.OriginalPCFastFile
if(-not (Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine
       [string]$surf.OriginalPCFastFileSha256){
    throw 'Original PC Aztec map.ff source identity has changed.'
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
Write-Host '[AZTEC-PC-MESH] ======================================================='
Write-Host '[AZTEC-PC-MESH] REAL ORIGINAL PC AZTEC GFXWORLD -> 19551 TRIANGLE OBJ'
Write-Host '[AZTEC-PC-MESH] Exactly 735 surfaces, 32713 vertices, 58653 indices.'
Write-Host '[AZTEC-PC-MESH] Geometry preview only. No FastFile / NO XBOX.'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0){
    if(Test-Path -LiteralPath $buildLog -PathType Leaf){
        $errors=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 15)
        if($errors.Count -gt 0){
            foreach($line in $errors){Write-Host "[AZTEC-PC-MESH] BUILD ERROR: $($line.Line)"}
        }else{
            Get-Content -LiteralPath $buildLog -Tail 30 | ForEach-Object {
                Write-Host "[AZTEC-PC-MESH] BUILD: $_"
            }
        }
    }
    throw "Original PC Aztec OBJ mesh exporter .NET incremental build failed: $buildLog"
}
Write-Host '[AZTEC-PC-MESH] Incremental .NET build PASS'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $runArgs=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-gfxworld-export-obj',$pc,$proofPath,$obj,$manifest)
    & dotnet @runArgs 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0 -or
   -not (Test-Path -LiteralPath $manifest -PathType Leaf) -or
   -not (Test-Path -LiteralPath $obj -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 25 | ForEach-Object {
            Write-Host "[AZTEC-PC-MESH] EXPORT: $_"
        }
    }
    throw "PC Aztec GfxWorld OBJ mesh export failed exit=$code; see $runLog"
}
$got=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
$expected='AZTEC_PC_GFXWORLD_19551_TRIANGLE_OBJ_EXPORT_PASS'
if([string]$got.Schema -ne 'WaW360.AztecPcGfxWorldMeshExport.v1' -or
   [string]$got.Status -ne $expected -or
   [int]$got.VertexCount -ne 32713 -or
   [int]$got.IndexCount -ne 58653 -or
   [int]$got.SurfaceCount -ne 735 -or
   [int]$got.TriangleCount -ne 19551 -or
   [int]$got.DistinctPackedMaterialPointers -lt 1 -or
   [string]$got.OriginalPCFastFileSha256 -ine [string]$surf.OriginalPCFastFileSha256 -or
   [string]$got.OriginalPCSurfaceSha256 -ine [string]$winner[0].OriginalPCArraySha256 -or
   [bool]$got.MaterialTextureBindingsProven -or
   [bool]$got.XenonGfxWorldSerialized -or
   [bool]$got.XboxHardwareReady -or
   [string]$got.ObjPath -ine [string]([IO.Path]::GetFullPath($obj)) -or
   [long]$got.ObjBytes -ne (Get-Item -LiteralPath $obj).Length -or
   [string]$got.ObjSha256 -ine (Get-FileHash -LiteralPath $obj -Algorithm SHA256).Hash){
    throw 'Original PC Aztec neutral OBJ file or SHA/count/no-hardware manifest validation failed.'
}
Write-Host "[AZTEC-PC-MESH] OBJ: triangles=$($got.TriangleCount)/19551 surfaces=$($got.SurfaceCount)/735 vertices=$($got.VertexCount)/32713"
Write-Host "[AZTEC-PC-MESH] referencedGlobalVertices=$($got.ReferencedVertexCount) materialPointerGroups=$($got.DistinctPackedMaterialPointers) degenerateTriples=$($got.DegenerateFaceIndexTriples)"
Write-Host "[AZTEC-PC-MESH] OBJ SHA-256=$($got.ObjSha256)"
Write-Host "[AZTEC-PC-MESH] file=$obj"
@(
"status=$expected",
"Source PC Aztec GfxWorld vertices=32713, indices=58653, surfaces=735, triangles=19551",
"Source zone SHA=$($got.OriginalPCZoneSha256)",
"Source index SHA=$($got.OriginalPCIndexSha256)",
"Source vertex SHA=$($got.OriginalPCVertexSha256)",
"Source surface SHA=$($got.OriginalPCSurfaceSha256)",
"OBJ path=$obj",
"OBJ sha256=$($got.ObjSha256)",
"Referenced global vertices=$($got.ReferencedVertexCount)",
"Material pointer groups=$($got.DistinctPackedMaterialPointers)",
"Degenerate triple count=$($got.DegenerateFaceIndexTriples)",
"Material pointer groups are PC references only, NOT resolved/textured.",
"NO FF was emitted. Six-family OAT baseline preserved. NOT for Xbox."
) | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host '[AZTEC-PC-MESH] ================= RESULT ============================='
Write-Host "[AZTEC-PC-MESH] status=$expected"
Write-Host "[AZTEC-PC-MESH] report=$report"
Write-Host '[AZTEC-PC-MESH] Real PC mesh exported for visual verification. No game-load claim.'
