[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$walk=Join-Path $base 'pc_material_stream_walker_null_replay\aztec_pc_material_stream_walker_null_replay.json'
$memory=Join-Path $base 'pc_gfxworld_material_memory_source_probe\aztec_pc_gfxworld_material_memory_source_probe.json'
$alias=Join-Path $base 'pc_gfxworld_material_alias_trace\aztec_pc_gfxworld_material_alias_trace.json'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_material_world_boundary_bridge'
$json=Join-Path $out 'aztec_pc_material_world_boundary_bridge.json'
$buildLog=Join-Path $out 'build.log'
$runLog=Join-Path $out 'probe.log'
foreach($p in @($walk,$memory,$alias,$geometry,$cli)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec 39 Material physical-boundary prerequisite missing: $p"
    }
}
$w=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $walk -Raw)
$m=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $memory -Raw)
$a=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $alias -Raw)
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometry -Raw)
if([string]$w.Schema -ne 'WaW360.AztecPcMaterialStreamWalker.v1' -or
   [string]$w.Status -ne 'AZTEC_PC_SOURCE_MATERIAL_WALK_39_STRUCTURAL_CANDIDATES' -or
   [int]$w.DecodedMaterialCandidates -ne 39 -or
   [string]$w.NextCursor -ne '0x057359F9' -or
   [bool]$w.XboxMaterialBindingsProven -or
   [bool]$w.XboxHardwareReady -or
   [string]$m.Schema -ne 'WaW360.AztecPcGfxWorldMaterialMemorySourceProbe.v3' -or
   [string]$m.Status -ne 'AZTEC_PC_GFXWORLD_39_MATERIAL_MEMORY_TWO_OVERLAPPING_WINDOWS_40_RECORD_RUN' -or
   [bool]$m.ExactPhysicalOwnershipProven -or
   [bool]$m.XboxHardwareReady -or
   [string]$a.Status -ne 'AZTEC_PC_GFXWORLD_39_POINTER_8BYTE_ALIAS_LATTICE_PASS' -or
   [int]$a.DistinctPackedWorldMaterialPointers -ne 39 -or
   [int]$a.Surfaces -ne 735 -or
   [bool]$a.SourceMaterialNamesResolved -or
   [bool]$a.XboxMaterialBindingsProven -or
   [string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [bool]$g.XboxHardwareReady){
    throw 'Source-only 39-material walker / MaterialMemory / 735-surface prerequisites changed.'
}
$pc=[string]$g.OriginalPCFastFile
if(-not(Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$g.OriginalPCFastFileSha256){
    throw 'Original PC map.ff compressed SHA identity changed.'
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
Write-Host '[AZTEC-MAT-BRIDGE] ==================================================='
Write-Host '[AZTEC-MAT-BRIDGE] 39 MEMORY ENTRIES -> 39 MATERIALS -> VERTEX ARRAY'
Write-Host '[AZTEC-MAT-BRIDGE] Cross-check two overlapping windows, exact 39 material stream and pinned vertex.'
Write-Host '[AZTEC-MAT-BRIDGE] Independently cross-check 39 virtual alias groups and 735 surface fields.'
Write-Host '[AZTEC-MAT-BRIDGE] Ordinal table is hypothetical; NO GFXWORLD BINDINGS OR FF.'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0){
    $errs=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 15)
    if($errs.Count -gt 0){
        foreach($e in $errs){Write-Host "[AZTEC-MAT-BRIDGE] BUILD ERROR: $($e.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-MAT-BRIDGE] BUILD: $_"}
    }
    throw "Aztec Material world boundary .NET build failed: $buildLog"
}
Write-Host '[AZTEC-MAT-BRIDGE] Incremental .NET build PASS'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $argsList=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-material-world-boundary-bridge',
        $pc,$walk,$memory,$alias,$geometry,$json)
    & dotnet @argsList 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 30 | ForEach-Object {Write-Host "[AZTEC-MAT-BRIDGE] TRACE: $_"}
    }
    throw "Aztec Material world boundary source proof failed exit=$code; log=$runLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
if([string]$r.Schema -ne 'WaW360.AztecPcMaterialWorldBoundaryBridge.v1' -or
   [string]$r.Status -ne 'AZTEC_PC_39_MATERIALS_EXACT_MEMORY_TO_VERTICES_PHYSICAL_CHAIN' -or
   -not [bool]$r.ContiguousPhysicalChainValidated -or
   -not [bool]$r.PreferredWindowDistinguishedByFirstHeaderAdjacency -or
   [int]$r.MaterialMemoryEntries -ne 39 -or
   [int]$r.ConsecutiveMaterialCandidates -ne 39 -or
   [int]$r.VirtualAliasGroups -ne 39 -or
   [int]$r.SourceSurfaceCount -ne 735 -or
   [int]$r.SourceTriangleCount -ne 19551 -or
   [string]$r.PreferredMemoryWindowAt -ne '0x057316CB' -or
   [string]$r.PreferredMemoryWindowEnd -ne '0x05731803' -or
   [string]$r.FirstMaterialHeaderAt -ne '0x05731803' -or
   [string]$r.VertexBufferAt -ne '0x057359F9' -or
   @($r.OrdinalPairingHypotheses).Count -ne 39 -or
   @($r.OrdinalPairingHypotheses | Where-Object {[bool]$_.VirtualAliasToMaterialBindingProven}).Count -ne 0 -or
   [bool]$r.WorldMaterialMemoryOwnershipProven -or
   [bool]$r.VirtualAliasToPhysicalMaterialRelocationProven -or
   [bool]$r.XboxMaterialBindingsProven -or
   [bool]$r.XboxHardwareReady){
    throw 'Aztec physical-source bridge failed count/contiguity/no-binding safety gates.'
}
Write-Host "[AZTEC-MAT-BRIDGE] preferredMemory=$($r.PreferredMemoryWindowAt) length=$($r.MemoryWindowBytes) end=$($r.PreferredMemoryWindowEnd)"
Write-Host "[AZTEC-MAT-BRIDGE] shiftedWindow=$($r.AlternativeShiftedMemoryWindowAt) end=$($r.ShiftedMemoryWindowEnd) (NOT first Material)"
Write-Host "[AZTEC-MAT-BRIDGE] firstMaterial=$($r.FirstMaterialHeaderAt) 39-material end=$($r.VertexBufferAt)"
Write-Host "[AZTEC-MAT-BRIDGE] contiguous=$($r.ContiguousPhysicalChainValidated) candidateMaterials=$($r.ConsecutiveMaterialCandidates) aliasSlots=$($r.VirtualAliasGroups) surfaces=$($r.SourceSurfaceCount) triangles=$($r.SourceTriangleCount)"
Write-Host "[AZTEC-MAT-BRIDGE] source serialized material stream bytes=$($r.SerializedMaterialStreamBytes); memory values sum=$($r.SumMemoryValues)"
Write-Host "[AZTEC-MAT-BRIDGE] memory SHA=$($r.SelectedMemoryWindowSha256)"
Write-Host "[AZTEC-MAT-BRIDGE] material stream SHA=$($r.SerializedMaterialStreamSha256)"
foreach($pair in @($r.OrdinalPairingHypotheses | Select-Object -First 5)){
    Write-Host "[AZTEC-MAT-BRIDGE] ORDINAL-HYPOTHESIS #$($pair.Ordinal) $($pair.UnverifiedMaterialName) <- virtual=$($pair.VirtualAliasPointerHypothesis) surfaces=$($pair.AliasSurfaceCount) triangles=$($pair.AliasTriangleCount) BINDING_PROVEN=False"
}
Write-Host '[AZTEC-MAT-BRIDGE] ================= RESULT ==========================='
Write-Host "[AZTEC-MAT-BRIDGE] status=$($r.Status)"
Write-Host "[AZTEC-MAT-BRIDGE] report=$json"
Write-Host '[AZTEC-MAT-BRIDGE] Exact PHYSICAL sequence is not a virtual relocation or Xbox root mapping. Gate 7 PENDING.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
