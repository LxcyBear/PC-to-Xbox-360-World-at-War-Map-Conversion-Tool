[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$atlasPath=Join-Path $base 'pc_gfxworld_material_binding_atlas\aztec_pc_gfxworld_material_binding_atlas.json'
$correlation=Join-Path $base 'pc_gfxworld_material_root_alias_proof\aztec_pc_gfxworld_material_root_alias_proof.json'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$work=Join-Path $base 'pc_gfxworld_material_alias_trace'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$json=Join-Path $work 'aztec_pc_gfxworld_material_alias_trace.json'
$txt=Join-Path $work 'aztec_pc_gfxworld_material_alias_trace.txt'
$buildLog=Join-Path $work 'dotnet_build.log'
$runLog=Join-Path $work 'alias_trace.log'
foreach($path in @($atlasPath,$correlation,$geometry,$cli)){
    if(-not (Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Aztec original PC material alias tracing missing source prerequisite: $path"
    }
}
$source=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometry -Raw)
$c=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $correlation -Raw)
$a=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $atlasPath -Raw)
if([string]$source.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [string]$c.status -ne 'AZTEC_PC_GFXWORLD_39_POINTER_ROOT_CORRELATION_COMPLETE' -or
   [int]$c.pcMaterialRootCount -ne 1 -or [int]$c.pcAssetCount -ne 4874 -or
   [int]$c.uniqueExactPcMaterialHeaderMatches -ne 0 -or
   [int]$c.ambiguousPcMaterialHeaderMatches -ne 0 -or
   [int]$c.matchedOtherTypeRootHeaders -ne 0 -or
   [int]$a.originalPcPointerGroups -ne 39 -or
   [int]$a.totalSurfaceReferences -ne 735 -or
   [int]$a.totalTrianglesCovered -ne 19551 -or
   [bool]$c.gate7Complete -or [bool]$source.XboxHardwareReady){
    throw 'Source GfxWorld material atlas or root-header nonmatch proof changed.'
}
$pc=[string]$source.OriginalPCFastFile
if(-not (Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$source.OriginalPCFastFileSha256){
    throw 'Original PC Aztec map.ff compressed SHA changed.'
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
Write-Host '[AZTEC-MAT-ALIAS] ===================================================='
Write-Host '[AZTEC-MAT-ALIAS] ORIGINAL PC 39 GFXSURFACE MATERIAL POINTER INDIRECTION'
Write-Host '[AZTEC-MAT-ALIAS] Investigating 8-byte virtual aliases and references outside 735 surfaces.'
Write-Host '[AZTEC-MAT-ALIAS] No invented Material names, Xbox bindings or FF emission.'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0){
    $errs=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 12)
    if($errs.Count -gt 0){
        foreach($e in $errs){Write-Host "[AZTEC-MAT-ALIAS] BUILD ERROR: $($e.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-MAT-ALIAS] BUILD: $_"}
    }
    throw "Original PC GfxWorld material alias trace .NET build failed. Log=$buildLog"
}
Write-Host '[AZTEC-MAT-ALIAS] Incremental .NET build PASS'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $runArgs=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-gfxworld-material-alias-trace',$pc,$atlasPath,$correlation,$json)
    & dotnet @runArgs 2>&1 | ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0 -or -not (Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 30 | ForEach-Object {Write-Host "[AZTEC-MAT-ALIAS] TRACE: $_"}
    }
    throw "Original PC GfxWorld Material alias trace failed exit=$code; log=$runLog"
}
$got=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
if([string]$got.Schema -ne 'WaW360.AztecPcGfxWorldMaterialAliasTrace.v1' -or
   [string]$got.Status -notin @(
       'AZTEC_PC_GFXWORLD_39_POINTER_8BYTE_ALIAS_LATTICE_PASS',
       'AZTEC_PC_GFXWORLD_39_POINTER_ALIAS_GEOMETRY_UNRESOLVED') -or
   [int]$got.OriginalPcAssetCount -ne 4874 -or
   [int]$got.OriginalPcTopLevelMaterialRoots -ne 1 -or
   [int]$got.DistinctPackedWorldMaterialPointers -ne 39 -or
   [int]$got.Surfaces -ne 735 -or [int]$got.Triangles -ne 19551 -or
   @($got.Groups).Count -ne 39 -or
   [bool]$got.SourceMaterialNamesResolved -or
   [bool]$got.XboxMaterialBindingsProven -or
   [bool]$got.XboxHardwareReady){
    throw 'GfxWorld material alias trace output failed source identity/diagnostic no-hardware gate.'
}
Write-Host "[AZTEC-MAT-ALIAS] block=$($got.SourcePointerBlock), eightByteLattice=$($got.SharedEightByteVirtualOffsetLattice), spanSlots=$($got.VirtualSpanEightByteSlots), unreferencedSlots=$($got.UnreferencedSlotsWithinSpan)"
Write-Host "[AZTEC-MAT-ALIAS] Other serialized pointer appearances outside 735 surfaces=$($got.AdditionalNonSurfaceSerializedPointerHits)"
Write-Host "[AZTEC-MAT-ALIAS] Fixed parent header count=39 candidate offsets: $(@($got.FixedWorldHeader39CountCandidateOffsets) -join ', ')"
foreach($distance in @($got.PointerDistances)){
    Write-Host "[AZTEC-MAT-ALIAS] Adjacent sorted virtual offset delta=$($distance.OffsetDelta) occurrences=$($distance.PairCount)"
}
foreach($g in @($got.Groups | Where-Object {[int]$_.OtherSerializedReferenceCount -gt 0} | Sort-Object OtherSerializedReferenceCount -Descending | Select-Object -First 12)){
    Write-Host "[AZTEC-MAT-ALIAS] $($g.PcMaterialPointer) surfaces=$($g.SurfaceCount) triangles=$($g.TriangleCount) outsideHits=$($g.OtherSerializedReferenceCount) at=$($g.OtherSerializedReferenceOffsets -join ',')"
}
@(
    "status=$($got.Status)",
    "PC GfxWorld 39 source Material pointer references, 735 surfaces, 19551 triangles",
    "Original PC XAsset roots=4874, top-level PC Material roots=1, direct header pointer matches=0",
    "Source pointer block=$($got.SourcePointerBlock), lowest=$($got.LowestVirtualOffset), highest=$($got.HighestVirtualOffset)",
    "Common 8-byte virtual lattice=$($got.SharedEightByteVirtualOffsetLattice); spanSlots=$($got.VirtualSpanEightByteSlots); unreferencedSlots=$($got.UnreferencedSlotsWithinSpan)",
    "Non-surface raw pointer occurrences=$($got.AdditionalNonSurfaceSerializedPointerHits)",
    "39-count parent GfxWorld header candidate offsets: $(@($got.FixedWorldHeader39CountCandidateOffsets) -join ', ')",
    "NO PROVEN MATERIAL NAMES OR XBOX ROOT BINDINGS; THIS DOES NOT MARK GATE 7 PASS.",
    "Full JSON=$json"
) | Set-Content -LiteralPath $txt -Encoding UTF8
Write-Host '[AZTEC-MAT-ALIAS] ================= RESULT ==========================='
Write-Host "[AZTEC-MAT-ALIAS] status=$($got.Status)"
Write-Host "[AZTEC-MAT-ALIAS] report=$txt"
Write-Host '[AZTEC-MAT-ALIAS] NO FF / NOT FOR XBOX / Gate 7 pending'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
