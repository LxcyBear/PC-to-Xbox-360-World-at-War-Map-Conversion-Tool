[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$sourceProof=Join-Path $base 'tonight_critical_source_census\aztec_tonight_critical_source_census.json'
$priorSix=Join-Path $base 'pc_six_family_comworld_fullgraph_integration\aztec_pc_six_family_comworld_graph.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$work=Join-Path $base 'pc_gameworldsp_path_source_hunt'
$buildLog=Join-Path $work 'dotnet_build.log'
$runLog=Join-Path $work 'gameworldsp_header_hunt.log'
$reportPath=Join-Path $work 'aztec_pc_gameworldsp_header_hunt.json'
$textPath=Join-Path $work 'aztec_pc_gameworldsp_header_hunt.txt'
foreach($f in @($sourceProof,$priorSix,$cli)){
    if(-not (Test-Path -LiteralPath $f -PathType Leaf)){
        throw "Aztec source-root header hunt prerequisite missing: $f"
    }
}
$source=Get-Content -LiteralPath $sourceProof -Raw | ConvertFrom-Json
$six=Get-Content -LiteralPath $priorSix -Raw | ConvertFrom-Json
if([string]$source.schema -ne 'WaW360.AztecTonightCriticalSourceCensus.v1' -or
   [string]$source.status -ne 'AZTEC_TONIGHT_CRITICAL_PC_SOURCE_IDENTITY_CENSUS_PASS' -or
   [int]$source.nativeRoots -ne 8509 -or
   [bool]$source.readyForXbox -or
   [string]$six.schema -ne 'WaW360.AztecPcSixFamilyComWorldGraph.v1' -or
   [string]$six.status -ne 'AZTEC_PC_SIX_FAMILY_COMWORLD_GRAPH_PASS' -or
   [int]$six.nativeRoots -ne 8509 -or
   [int]$six.sourcePcComWorldByteReadback -ne 1 -or
   [int]$six.sourcePCMapEntsReadbacks -ne 1 -or
   [int]$six.pcXModelsListed -ne 2 -or
   [int]$six.pcMaterialsListed -ne 2 -or
   [int]$six.pcIwiPixelExactReadbacks -ne 2 -or
   [int]$six.pcRawFileExactReadbacks -ne 114 -or
   -not [bool]$six.oatZeroWarningsZeroErrors -or
   -not [bool]$six.layoutReplayExact -or
   [bool]$six.readyForXbox){
    throw 'Requires unchanged six-family 8509-root Xenon OAT and exact original-PC source PASS.'
}
$roles=@($source.pcSourceRoles | Where-Object {[string]$_.role -eq 'map.ff'})
if($roles.Count -ne 1){throw 'Expected one SHA-pinned original PC map.ff role.'}
$pc=[string]$roles[0].sourceFastfile
$sourceSha=[string]$roles[0].sourceSha256
if(-not (Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine $sourceSha){
    throw 'Original PC map.ff SHA-256 differs from previously verified source census.'
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
Write-Host '[AZTEC-PC-PATH] ======================================================='
Write-Host '[AZTEC-PC-PATH] GAMEWORLDSP ORIGINAL PC XASSET TABLE + PATHDATA HUNT'
Write-Host '[AZTEC-PC-PATH] Looking for 44-byte source body, NOT the shared map name.'
Write-Host '[AZTEC-PC-PATH] Six-family FF stays unchanged; NO NEW XBOX FF EMITTED.'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0){
    Write-Host '[AZTEC-PC-PATH] Build log tail:'
    Get-Content -LiteralPath $buildLog -Tail 15 | ForEach-Object {Write-Host "[AZTEC-PC-PATH] $_"}
    throw "Incremental GameWorldSP source-scanner build failed. See $buildLog"
}
Write-Host '[AZTEC-PC-PATH] .NET incremental build PASS'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $runArgs=@('run','--no-build','-c','Release','--project',$cli,'--',
      'aztec-pc-gameworldsp-hunt',$pc,$reportPath)
    & dotnet @runArgs 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0 -or -not (Test-Path -LiteralPath $reportPath -PathType Leaf)){
    Get-Content -LiteralPath $runLog -Tail 16 | ForEach-Object {Write-Host "[AZTEC-PC-PATH] $_"}
    throw "PC GameWorldSP header hunt failed exit=$code; see $runLog"
}
$hunt=Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
if([string]$hunt.Schema -ne 'WaW360.AztecPcGameWorldSpHeaderHunt.v1' -or
   [string]$hunt.Status -ne 'AZTEC_PC_GAMEWORLDSP_HEADER_HUNT_COMPLETE' -or
   [string]$hunt.OriginalPcFastFileSha256 -ine $sourceSha -or
   [int64]$hunt.OriginalPcZoneBytes -ne 113701260L -or
   [int]$hunt.PcAssetCount -lt 1 -or
   [bool]$hunt.ActualGameWorldSpSourceRootLocated -or
   [bool]$hunt.NestedPathDataDecoded -or
   [bool]$hunt.ReadyForXbox){
    throw 'GameWorldSP source-structure scan report identity or diagnostic-only gates failed.'
}
$roots=@($hunt.PcWorldAssetRoots)
$game=@($roots | Where-Object {
    ([string]$_.PcTypeName).Replace('_','').ToLowerInvariant() -eq 'gameworldsp'
})
$gfx=@($roots | Where-Object {
    ([string]$_.PcTypeName).Replace('_','').ToLowerInvariant() -eq 'gfxworld'
})
$com=@($roots | Where-Object {
    ([string]$_.PcTypeName).Replace('_','').ToLowerInvariant() -eq 'comworld'
})
if($game.Count -lt 1 -or $gfx.Count -lt 1 -or $com.Count -lt 1){
    throw "Original PC XAsset table world types unexpectedly absent: game=$($game.Count) gfx=$($gfx.Count) com=$($com.Count)"
}
Write-Host "[AZTEC-PC-PATH] PC XAsset roots: ComWorld=$($com.Count), GameWorldSP=$($game.Count), GfxWorld=$($gfx.Count)"
foreach($row in $roots){
    Write-Host "[AZTEC-PC-PATH] PC asset #$($row.PcAssetIndex) $($row.PcTypeName) top-level header=$($row.RawHeader) reference=$($row.ReferenceKind)"
}
Write-Host "[AZTEC-PC-PATH] 44-byte signature candidates=$($hunt.StructurallyPlausibleHeaderHits)"
foreach($item in @($hunt.TopCandidates | Select-Object -First 12)){
    Write-Host "[AZTEC-PC-PATH] CANDIDATE: $($item.OffsetHex) score=$($item.Score) nodes=$($item.NodeCount) chains=$($item.ChainNodeCount) vis=$($item.VisBytes) trees=$($item.NodeTreeCount) namePtr=$($item.NamePtr) sourceSha=$($item.Fixed44ByteSha256)"
}
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add("Aztec PC GameWorldSP 44-byte source header hunt: $($hunt.Status)")
$lines.Add("SHA-pinned original PC map.ff; decompressed zone=$($hunt.OriginalPcZoneBytes) bytes")
$lines.Add("PC XAsset roots: ComWorld=$($com.Count), GameWorldSP=$($game.Count), GfxWorld=$($gfx.Count)")
foreach($row in $roots){
    $lines.Add("PC ROOT #$($row.PcAssetIndex) family=$($row.PcTypeName) rootHeader=$($row.RawHeader) form=$($row.ReferenceKind)")
}
$lines.Add("T4 GameWorldSp PathData header candidates=$($hunt.StructurallyPlausibleHeaderHits), raw node array markers=$($hunt.RawNodesArrayPointerMarkerHits)")
foreach($item in @($hunt.TopCandidates | Select-Object -First 24)){
    $lines.Add("CANDIDATE $($item.OffsetHex) score=$($item.Score) nodes=$($item.NodeCount) chains=$($item.ChainNodeCount) visBytes=$($item.VisBytes) treeCount=$($item.NodeTreeCount) namePtr=$($item.NamePtr) nodesPtr=$($item.NodesPtr) baseNodesPtr=$($item.BaseNodesPtr) sha=$($item.Fixed44ByteSha256)")
}
$lines.Add('NO decoded GameWorldSP or GfxWorld body. No FastFile mutation or hardware test. ALL SIX-FAMILY PASS ARTIFACTS PRESERVED.')
[IO.File]::WriteAllLines($textPath,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-PATH] ================= RESULT ============================='
Write-Host "[AZTEC-PC-PATH] status=$($hunt.Status)"
Write-Host "[AZTEC-PC-PATH] report=$textPath"
Write-Host '[AZTEC-PC-PATH] NO FF / NO XBOX. Source header investigation only.'
