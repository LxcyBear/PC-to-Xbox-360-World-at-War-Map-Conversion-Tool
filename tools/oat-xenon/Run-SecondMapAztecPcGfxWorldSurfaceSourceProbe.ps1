[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$vertexProof=Join-Path $base 'pc_gfxworld_vertex_source_probe\aztec_pc_gfxworld_vertex_source_probe.json'
$sixProof=Join-Path $base 'pc_six_family_comworld_fullgraph_integration\aztec_pc_six_family_comworld_graph.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$work=Join-Path $base 'pc_gfxworld_surface_source_probe'
$buildLog=Join-Path $work 'dotnet_build.log'
$runLog=Join-Path $work 'surface_source_scan.log'
$json=Join-Path $work 'aztec_pc_gfxworld_surface_source_probe.json'
$txt=Join-Path $work 'aztec_pc_gfxworld_surface_source_probe.txt'
foreach($file in @($vertexProof,$sixProof,$cli)){
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)){
        throw "Aztec 735-surface source proof missing prerequisite: $file"
    }
}
$vertex=Get-Content -LiteralPath $vertexProof -Raw | ConvertFrom-Json
$six=Get-Content -LiteralPath $sixProof -Raw | ConvertFrom-Json
if([string]$vertex.Schema -ne 'WaW360.AztecPcGfxWorldVertexSourceProbe.v1' -or
   [string]$vertex.Status -ne 'AZTEC_PC_GFXWORLD_32713_VERTEX_ARRAY_CANDIDATE_PASS' -or
   [int]$vertex.SourcePCVertexCount -ne 32713 -or
   [int]$vertex.SourcePCIndexCount -ne 58653 -or
   [int]$vertex.VertexStride -ne 44 -or
   [bool]$vertex.HardwareReady -or
   [string]$six.schema -ne 'WaW360.AztecPcSixFamilyComWorldGraph.v1' -or
   [string]$six.status -ne 'AZTEC_PC_SIX_FAMILY_COMWORLD_GRAPH_PASS' -or
   [int]$six.nativeRoots -ne 8509 -or
   [int]$six.sourcePcComWorldByteReadback -ne 1 -or
   [int]$six.sourcePCMapEntsReadbacks -ne 1 -or
   -not [bool]$six.layoutReplayExact -or
   -not [bool]$six.oatZeroWarningsZeroErrors -or
   [bool]$six.readyForXbox){
    throw 'Existing PC 32713-vertex and six-family Xenon OAT source proofs changed.'
}
$winning=@($vertex.TopCandidates | Where-Object {
    [int]$_.SourceOffset -eq 0x057359F9 -and
    [int]$_.ValidBinormalSigns -eq 32713 -and
    [int]$_.ValidPositions -eq 32713 -and
    [int]$_.ValidTexCoords -eq 32713
})
if($winning.Count -ne 1 -or
   [string]$winning[0].OriginalPCArraySha256 -ine
     '6303269A8170F6F06233DCCE0BF1230EC6A659C4D0EE78CFDF7C14EE242D8041'){
    throw 'The 32713 PC vertex array at 0x057359F9 is no longer pinned to passing SHA.'
}
$pc=[string]$vertex.OriginalPCFastFile
if(-not (Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$vertex.OriginalPCFastFileSha256){
    throw 'Aztec original PC map.ff source hash changed.'
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
Write-Host '[AZTEC-PC-SURFACE] ===================================================='
Write-Host '[AZTEC-PC-SURFACE] 735 PC GFXSURFACE RECORDS FROM REAL AZTEC MAP GEOMETRY (52-BYTE ABI)'
Write-Host '[AZTEC-PC-SURFACE] SHA-pinned 32713 vertices + 58653 indices; 52-byte T4 structures.'
Write-Host '[AZTEC-PC-SURFACE] No Xbox FF emission, previous 8509-root six-family PASS unchanged.'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0){
    Write-Host '[AZTEC-PC-SURFACE] FIRST BUILD ERRORS:'
    if(Test-Path -LiteralPath $buildLog -PathType Leaf){
        $errors=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 15)
        if($errors.Count -gt 0){
            foreach($errorLine in $errors){
                Write-Host "[AZTEC-PC-SURFACE] ERROR: $($errorLine.Line)"
            }
        }else{
            Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {
                Write-Host "[AZTEC-PC-SURFACE] BUILD: $_"
            }
        }
    }
    throw "Aztec GfxSurface source scanner failed incremental .NET build: $buildLog"
}
Write-Host '[AZTEC-PC-SURFACE] Incremental .NET build PASS'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $args=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-gfxworld-surfaces',$pc,$vertexProof,$json)
    & dotnet @args 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0 -or -not (Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 25 | ForEach-Object {
            Write-Host "[AZTEC-PC-SURFACE] SCAN: $_"
        }
    }
    throw "Aztec GfxSurface source scanner failed, exit=$code, report log: $runLog"
}
$result=Get-Content -LiteralPath $json -Raw | ConvertFrom-Json
if([string]$result.Schema -ne 'WaW360.AztecPcGfxSurfaceSourceProbe.v1' -or
   [string]$result.Status -notin @(
       'AZTEC_PC_GFXWORLD_735_SURFACE_ARRAY_CANDIDATE_PASS',
       'AZTEC_PC_GFXWORLD_SURFACE_LAYOUT_UNRESOLVED') -or
   [int]$result.SurfaceCount -ne 735 -or
   [int]$result.SurfaceStride -ne 52 -or
   [bool]$result.FullGfxWorldSerialized -or
   [bool]$result.SurfaceMaterialOwnershipProven -or
   [bool]$result.XboxHardwareReady){
    throw 'PC 735-surface scan returned inconsistent or non-diagnostic result.'
}
Write-Host "[AZTEC-PC-SURFACE] 3-record signatures=$($result.ThreeRecordGates), 6-record signatures=$($result.SixRecordGates)"
foreach($c in @($result.TopCandidates | Select-Object -First 8)){
    Write-Host "[AZTEC-PC-SURFACE] CANDIDATE $($c.SourceOffsetHex) surfaces=$($c.ValidSurfaceRecords)/735 bounds=$($c.ValidBoundingBoxes)/735 materialPtrs=$($c.ValidMaterialPointers)/735 tris=$($c.TotalTriangleCount)/19551 maxIndexEnd=$($c.MaximumIndexEnd)/58653 monotonic=$($c.MonotonicIndexBasePairs)/734 missing=$($c.UncoveredIndexEntries) overlapping=$($c.IndexSpanOverlapEntries) local=$($c.LocalIndicesInRange)/58653 global=$($c.GlobalIndicesInRange)/58653 sourceBlockMaterials=$($c.SourceBlockMaterialPointers)/735"
    Write-Host "[AZTEC-PC-SURFACE]   source SHA=$($c.OriginalPCArraySha256)"
    foreach($first in @($c.FirstEightRecords | Select-Object -First 2)){
        Write-Host "[AZTEC-PC-SURFACE]   srf#$($first.Index) firstVertex=$($first.FirstVertex) vertices=$($first.VertexCount) tris=$($first.TriangleCount) baseIndex=$($first.BaseIndex) material=$($first.MaterialPointer)"
    }
}
$summary=New-Object 'System.Collections.Generic.List[string]'
$summary.Add("Original-PC Aztec 735 GfxSurface source scan: $($result.Status)")
$summary.Add("Vertex 32713 x 44, original PC SHA 6303269A8170F6F06233DCCE0BF1230EC6A659C4D0EE78CFDF7C14EE242D8041")
$summary.Add("Indices 58653 x 2, original PC SHA E89621777CB47C46BB47FE0CA948645D6813151E02A706C2A2325C01BDEFD32D")
$summary.Add("3-record gates=$($result.ThreeRecordGates), 6-record gates=$($result.SixRecordGates)")
foreach($c in @($result.TopCandidates | Select-Object -First 20)){
    $summary.Add("candidate $($c.SourceOffsetHex) valid=$($c.ValidSurfaceRecords)/735 bounds=$($c.ValidBoundingBoxes) triangles=$($c.TotalTriangleCount) maxEnd=$($c.MaximumIndexEnd) SHA=$($c.OriginalPCArraySha256)")
}
$summary.Add('Source candidate only. GfxWorld Xenon full geometry/render dependencies not yet serialized, NOT FOR XBOX.')
[IO.File]::WriteAllLines($txt,[string[]]$summary.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-SURFACE] ================= RESULT ==========================='
Write-Host "[AZTEC-PC-SURFACE] status=$($result.Status)"
Write-Host "[AZTEC-PC-SURFACE] report=$txt"
Write-Host '[AZTEC-PC-SURFACE] NO FF / NO XBOX'
