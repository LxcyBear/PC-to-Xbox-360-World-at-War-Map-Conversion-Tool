[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$aliasPath=Join-Path $base 'pc_gfxworld_material_alias_trace\aztec_pc_gfxworld_material_alias_trace.json'
$geometryPath=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$work=Join-Path $base 'pc_gfxworld_material_memory_source_probe'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$json=Join-Path $work 'aztec_pc_gfxworld_material_memory_source_probe.json'
$txt=Join-Path $work 'aztec_pc_gfxworld_material_memory_source_probe.txt'
$buildLog=Join-Path $work 'dotnet_build.log'
$runLog=Join-Path $work 'probe.log'
foreach($p in @($aliasPath,$geometryPath,$cli)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec GfxWorld source MaterialMemory prerequisite missing: $p"
    }
}
$a=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $aliasPath -Raw)
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometryPath -Raw)
if([string]$a.Schema -ne 'WaW360.AztecPcGfxWorldMaterialAliasTrace.v1' -or
   [string]$a.Status -ne 'AZTEC_PC_GFXWORLD_39_POINTER_8BYTE_ALIAS_LATTICE_PASS' -or
   [int]$a.DistinctPackedWorldMaterialPointers -ne 39 -or
   [int]$a.HeaderMaterialMemoryCountCandidateAt0x18C -ne 39 -or
   [string]$a.HeaderFollowingMaterialMemoryPointerCandidateAt0x190 -ne '0xFFFFFFFF' -or
   [string]$a.SourcePointerBlock -ne 'Virtual' -or
   -not [bool]$a.SharedEightByteVirtualOffsetLattice -or
   [int]$a.VirtualSpanEightByteSlots -ne 39 -or
   [int]$a.UnreferencedSlotsWithinSpan -ne 0 -or
   [bool]$a.XboxHardwareReady -or
   [string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [int]$g.VertexCount -ne 32713 -or
   [int]$g.SurfaceCount -ne 735 -or
   [int]$g.IndexCount -ne 58653 -or
   [bool]$g.XboxHardwareReady){
    throw 'Original PC GfxWorld 39-slot material alias or Xenon three-buffer source proof changed.'
}
$pc=[string]$g.OriginalPCFastFile
if(-not (Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$g.OriginalPCFastFileSha256){
    throw 'Original PC Aztec map.ff SHA identity changed.'
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
Write-Host '[AZTEC-MAT-MEM] ======================================================='
Write-Host '[AZTEC-MAT-MEM] ORIGINAL PC GFXWORLD 39 x 8 BYTE MATERIALMEMORY ARRAY'
Write-Host '[AZTEC-MAT-MEM] Parent +0x18C = 39; +0x190 = FOLLOWING, search before verified vertices.'
Write-Host '[AZTEC-MAT-MEM] Bounded physical window, original-PC SHA pinned. NO FF / NO XBOX.'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0){
    $errors=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 15)
    if($errors.Count -gt 0){
        foreach($e in $errors){Write-Host "[AZTEC-MAT-MEM] BUILD ERROR: $($e.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-MAT-MEM] BUILD: $_"}
    }
    throw "Original PC GfxWorld MaterialMemory probe .NET build failed; see $buildLog"
}
Write-Host '[AZTEC-MAT-MEM] Incremental .NET build PASS'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $command=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-gfxworld-material-memory-probe',$pc,$aliasPath,$geometryPath,$json)
    & dotnet @command 2>&1 | ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0 -or -not (Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 30 | ForEach-Object {Write-Host "[AZTEC-MAT-MEM] PROBE: $_"}
    }
    throw "Original-PC GfxWorld 39-record MaterialMemory probe failed exit=$code; log=$runLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
$validStatus=@(
    'AZTEC_PC_GFXWORLD_39_MATERIAL_MEMORY_ARRAY_CANDIDATE_PASS',
    'AZTEC_PC_GFXWORLD_39_MATERIAL_MEMORY_ARRAY_MULTIPLE_CANDIDATES',
    'AZTEC_PC_GFXWORLD_39_MATERIAL_MEMORY_ARRAY_NOT_LOCATED'
)
if([string]$r.Schema -ne 'WaW360.AztecPcGfxWorldMaterialMemorySourceProbe.v1' -or
   [string]$r.Status -notin $validStatus -or
   [int]$r.MaterialMemoryCount -ne 39 -or
   [int]$r.MaterialMemoryArrayBytes -ne 312 -or
   [int]$r.MaximumScanBackBytes -ne (8*1024*1024) -or
   [int]$r.SourceVertexBufferOffset -ne 0x057359F9 -or
   [string]$r.SourceMaterialMemoryPointer -ne '0xFFFFFFFF' -or
   [bool]$r.ExactPhysicalOwnershipProven -or
   [bool]$r.PCMaterialNamesResolved -or
   [bool]$r.XboxMaterialBindingsProven -or
   [bool]$r.XboxHardwareReady){
    throw 'MaterialMemory source probe output or scratch-only hardware gate failed.'
}
Write-Host "[AZTEC-MAT-MEM] 3-row signatures=$($r.ThreeRecordSignatures), 39-row matches=$($r.Full39RecordSignatures)"
Write-Host "[AZTEC-MAT-MEM] Immediately preceding original vertices=$($r.ExactArrayImmediatelyBeforeVertexBuffer)"
foreach($candidate in @($r.Candidates | Select-Object -First 12)){
    Write-Host "[AZTEC-MAT-MEM] CANDIDATE $($candidate.SourceOffsetHex) beforeVertices=$($candidate.BytesBeforeVertexBuffer) following=$($candidate.FollowingMaterialFields) insert=$($candidate.InsertMaterialFields) offsets=$($candidate.PackedMaterialFields) distinct=$($candidate.DistinctMaterialPointerFields) positiveMemory=$($candidate.PositiveMemoryEntries)/39 totalMemoryBytes=$($candidate.TotalMemoryBytes)"
    Write-Host "[AZTEC-MAT-MEM]   material fields first=$($candidate.FirstMaterialField) mid=$($candidate.MiddleMaterialField) last=$($candidate.LastMaterialField) SHA=$($candidate.SourceArraySha256)"
}
@(
    "status=$($r.Status)",
    "Source PC GfxWorld parent MaterialMemory count=39, pointer=FOLLOWING",
    "39 GfxSurface Material references form independent 8-byte Virtual lattice, no gaps",
    "bounded search start=$($r.ScanStart) end=$($r.ScanEnd) bytesBeforeVertexMax=$($r.MaximumScanBackBytes)",
    "3-row signatures=$($r.ThreeRecordSignatures), full39 candidates=$($r.Full39RecordSignatures)",
    "immediately-before-vertex match=$($r.ExactArrayImmediatelyBeforeVertexBuffer)",
    'A valid-looking 39-record array is NOT proof that GfxSurface Material* points to MaterialMemory*. Material names unresolved.',
    'Gate 7 still pending. No Xbox FF or hardware authorization.',
    "Full JSON=$json"
) | Set-Content -LiteralPath $txt -Encoding UTF8
Write-Host '[AZTEC-MAT-MEM] ================= RESULT ============================='
Write-Host "[AZTEC-MAT-MEM] status=$($r.Status)"
Write-Host "[AZTEC-MAT-MEM] report=$txt"
Write-Host '[AZTEC-MAT-MEM] NO FF / NOT FOR XBOX / Gate 7 pending'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
