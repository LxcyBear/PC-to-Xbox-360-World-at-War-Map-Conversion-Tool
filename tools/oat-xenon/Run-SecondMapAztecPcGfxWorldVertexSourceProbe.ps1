[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$indexPath=Join-Path $base 'pc_gfxworld_index_source_proof\aztec_pc_gfxworld_index_source_proof.json'
$sixPath=Join-Path $base 'pc_six_family_comworld_fullgraph_integration\aztec_pc_six_family_comworld_graph.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$work=Join-Path $base 'pc_gfxworld_vertex_source_probe'
$buildLog=Join-Path $work 'dotnet_build.log'
$runLog=Join-Path $work 'gfxworld_vertex_source_scan.log'
$json=Join-Path $work 'aztec_pc_gfxworld_vertex_source_probe.json'
$txt=Join-Path $work 'aztec_pc_gfxworld_vertex_source_probe.txt'

foreach($p in @($indexPath,$sixPath,$cli)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec source vertex test prerequisite missing: $p"
    }
}
$index=Get-Content -LiteralPath $indexPath -Raw | ConvertFrom-Json
$six=Get-Content -LiteralPath $sixPath -Raw | ConvertFrom-Json
if([string]$index.schema -ne 'WaW360.AztecPcGfxWorldIndexSourceProof.v1' -or
   [string]$index.status -ne 'AZTEC_PC_GFXWORLD_58653_INDEX_ARRAY_CANDIDATE_PASS' -or
   [int]$index.gfxWorldHeaderOffset -ne 0x049122C0 -or
   [int]$index.originalPcIndexCount -ne 58653 -or
   [int]$index.originalPcVertexCount -ne 32713 -or
   [bool]$index.readyForXbox -or
   [string]$six.schema -ne 'WaW360.AztecPcSixFamilyComWorldGraph.v1' -or
   [string]$six.status -ne 'AZTEC_PC_SIX_FAMILY_COMWORLD_GRAPH_PASS' -or
   [int]$six.nativeRoots -ne 8509 -or
   [int]$six.sourcePcComWorldByteReadback -ne 1 -or
   [int]$six.sourcePCMapEntsReadbacks -ne 1 -or
   -not [bool]$six.oatZeroWarningsZeroErrors -or
   -not [bool]$six.layoutReplayExact -or
   [bool]$six.readyForXbox){
    throw 'The verified 8509-root six-family FF or original PC GfxWorld indices source gate changed.'
}
$matches=@($index.candidateIndexArrays | Where-Object {
    [int]$_.candidateArrayOffset -eq 0x049125F2 -and
    [int]$_.paddingAfterName -eq 0 -and
    [int]$_.indicesOutOfRange -eq 0
})
if($matches.Count -ne 1 -or
   [string]$matches[0].sourceIndexBytesSha256 -ine
     'E89621777CB47C46BB47FE0CA948645D6813151E02A706C2A2325C01BDEFD32D'){
    throw 'Require the precise source-owned zero-padding 58653-index lead, not a shifted array candidate.'
}
$pc=[string]$index.originalPcFastFile
if(-not (Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$index.originalPcFastFileSha256){
    throw 'Original Aztec PC map.ff SHA-256 provenance changed.'
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
Write-Host '[AZTEC-PC-GFX-VERTEX] ================================================='
Write-Host '[AZTEC-PC-GFX-VERTEX] DEMO-CRITICAL: 32713 PC GFXWORLD VERTEX ARRAY SOURCE'
Write-Host '[AZTEC-PC-GFX-VERTEX] 58653 original PC indices verified; search 44-byte vertex ABI.'
Write-Host '[AZTEC-PC-GFX-VERTEX] No FastFile generation; six-family PASS remains untouched.'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0){
    Write-Host '[AZTEC-PC-GFX-VERTEX] FIRST COMPILER ERRORS:'
    if(Test-Path -LiteralPath $buildLog -PathType Leaf){
        $found=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 12)
        if($found.Count -gt 0){
            foreach($item in $found){
                Write-Host "[AZTEC-PC-GFX-VERTEX] ERROR: $($item.Line)"
            }
        }else{
            Get-Content -LiteralPath $buildLog -Tail 40 | ForEach-Object {Write-Host "[AZTEC-PC-GFX-VERTEX] BUILD: $_"}
        }
    }
    throw "Incremental .NET GfxWorld source-probe build failed: $buildLog"
}
Write-Host '[AZTEC-PC-GFX-VERTEX] Incremental .NET build PASS'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $runArgs=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-gfxworld-vertices',$pc,$indexPath,$json)
    & dotnet @runArgs 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0 -or -not (Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 20 | ForEach-Object {Write-Host "[AZTEC-PC-GFX-VERTEX] SCANNER: $_"}
    }
    throw "Original-PC 32713-vertex array source scan failed exit=$code; see $runLog"
}
$result=Get-Content -LiteralPath $json -Raw | ConvertFrom-Json
if([string]$result.Schema -ne 'WaW360.AztecPcGfxWorldVertexSourceProbe.v1' -or
   [string]$result.Status -notin @(
     'AZTEC_PC_GFXWORLD_32713_VERTEX_ARRAY_CANDIDATE_PASS',
     'AZTEC_PC_GFXWORLD_VERTEX_LAYOUT_UNRESOLVED') -or
   [int]$result.SourcePCVertexCount -ne 32713 -or
   [int]$result.SourcePCIndexCount -ne 58653 -or
   [int]$result.VertexStride -ne 44 -or
   [int]$result.VertexArrayBytes -ne 1439372 -or
   [bool]$result.FullGfxWorldNestedGraphProven -or
   [bool]$result.FullXenonWorldEmitted -or
   [bool]$result.HardwareReady){
    throw 'Original PC GfxWorld vertex report failed exact source/model/diagnostic-only gates.'
}
Write-Host "[AZTEC-PC-GFX-VERTEX] source candidates coarse=$($result.CoarseCandidates) deep=$($result.DeepCandidates)"
foreach($c in @($result.TopCandidates | Select-Object -First 8)){
    Write-Host "[AZTEC-PC-GFX-VERTEX] VERTICES $($c.SourceOffsetHex) sign=$($c.ValidBinormalSigns)/32713 xyz=$($c.ValidPositions)/32713 uv=$($c.ValidTexCoords)/32713 uniqueSampleXYZ=$($c.UniqueSampledPositions) SHA=$($c.OriginalPCArraySha256)"
    Write-Host "[AZTEC-PC-GFX-VERTEX]   bounds=($($c.MinX),$($c.MinY),$($c.MinZ))..($($c.MaxX),$($c.MaxY),$($c.MaxZ))"
}
$summary=New-Object 'System.Collections.Generic.List[string]'
$summary.Add("Original PC Aztec GfxWorld 32713 vertex scan: $($result.Status)")
$summary.Add("Previous 58653 indexed triangle stream: SHA-pinned and unmodified")
$summary.Add("Source zone SHA: $($result.OriginalPCZoneSha256)")
$summary.Add("Candidates coarse=$($result.CoarseCandidates), deeply sampled=$($result.DeepCandidates)")
foreach($c in @($result.TopCandidates | Select-Object -First 20)){
    $summary.Add("candidate $($c.SourceOffsetHex) sign=$($c.ValidBinormalSigns)/32713 positions=$($c.ValidPositions)/32713 UV=$($c.ValidTexCoords)/32713 uniqueXYZ=$($c.UniqueSampledPositions) SHA=$($c.OriginalPCArraySha256)")
}
$summary.Add('Candidates only. No GfxSurface/DPVS/materials built. No Xbox FF; six-family PASS untouched.')
[IO.File]::WriteAllLines($txt,[string[]]$summary.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-GFX-VERTEX] ================= RESULT ========================'
Write-Host "[AZTEC-PC-GFX-VERTEX] status=$($result.Status)"
Write-Host "[AZTEC-PC-GFX-VERTEX] report=$txt"
Write-Host '[AZTEC-PC-GFX-VERTEX] NO FF / NOT FOR XBOX.'
