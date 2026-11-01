[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$bridge=Join-Path $base 'pc_material_world_boundary_bridge\aztec_pc_material_world_boundary_bridge.json'
$names=Join-Path $base 'pc_world_material_native_name_audit\aztec_pc_world_material_native_name_audit.json'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_735_surface_material_candidate_ledger'
$json=Join-Path $out 'aztec_pc_735_surface_material_candidate_ledger.json'
$buildLog=Join-Path $out 'build.log'
$runLog=Join-Path $out 'probe.log'
foreach($p in @($bridge,$names,$geometry,$cli)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec 735-surface ordinal candidate ledger prerequisite missing: $p"
    }
}
$b=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $bridge -Raw)
$n=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $names -Raw)
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometry -Raw)
if([string]$b.Schema -ne 'WaW360.AztecPcMaterialWorldBoundaryBridge.v1' -or
   [string]$b.Status -ne 'AZTEC_PC_39_MATERIALS_EXACT_MEMORY_TO_VERTICES_PHYSICAL_CHAIN' -or
   -not [bool]$b.ContiguousPhysicalChainValidated -or
   [int]$b.ConsecutiveMaterialCandidates -ne 39 -or
   [int]$b.VirtualAliasGroups -ne 39 -or
   [int]$b.SourceSurfaceCount -ne 735 -or
   [bool]$b.VirtualAliasToPhysicalMaterialRelocationProven -or
   [bool]$b.XboxMaterialBindingsProven -or
   [bool]$b.XboxHardwareReady -or
   [string]$n.Schema -ne 'WaW360.AztecPcWorldMaterialNativeNameReconciliation.v1' -or
   [string]$n.Status -ne 'AZTEC_PC_39_MATERIAL_NAMES_VS_GOLDEN_NATIVE_ROOTS_INVENTORIED' -or
   [int]$n.UniqueExactNameCandidates -ne 39 -or
   [int]$n.NoExactNameCandidates -ne 0 -or
   [int]$n.AmbiguousExactNameCandidates -ne 0 -or
   [bool]$n.SourceVirtualToPhysicalMaterialRelocationProven -or
   [bool]$n.XboxMaterialBindingsProven -or
   [bool]$n.XboxHardwareReady -or
   [string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [int]$g.SurfaceCount -ne 735 -or
   [bool]$g.XboxHardwareReady){
    throw 'Requires historical 39 physical source candidates, 39 unique native name matches, 735 source surfaces and ZERO bindings.'
}
$pc=[string]$g.OriginalPCFastFile
if(-not(Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$g.OriginalPCFastFileSha256){
    throw 'Original PC Aztec map.ff compressed SHA identity changed.'
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
Write-Host '[AZTEC-735-CANDIDATES] =================================================='
Write-Host '[AZTEC-735-CANDIDATES] EXACT 39 NAME MATCHES -> HYPOTHETICAL 735-SURFACE LEDGER'
Write-Host '[AZTEC-735-CANDIDATES] Slot i -> physical memory row i -> material i -> exact Xbox name.'
Write-Host '[AZTEC-735-CANDIDATES] ORDINAL ASSUMPTION UNPROVEN. This is NOT an Xbox material binding.'
Write-Host '[AZTEC-735-CANDIDATES] Source SHA checked; no surface bytes rewritten. NO FF / NO XBOX.'
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
        foreach($e in $errors){Write-Host "[AZTEC-735-CANDIDATES] BUILD ERROR: $($e.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-735-CANDIDATES] BUILD: $_"}
    }
    throw "Aztec 735 surface ledger .NET build failed: $buildLog"
}
Write-Host '[AZTEC-735-CANDIDATES] Incremental .NET build PASS'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $argsList=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-world-735-surface-material-candidates',$pc,$bridge,$names,$json)
    & dotnet @argsList 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Write-Host '[AZTEC-735-CANDIDATES] EXCEPTION HEAD:'
        Get-Content -LiteralPath $runLog -TotalCount 12 |
            ForEach-Object {Write-Host "[AZTEC-735-CANDIDATES] TRACE: $_"}
        Write-Host '[AZTEC-735-CANDIDATES] EXCEPTION TAIL:'
        Get-Content -LiteralPath $runLog -Tail 12 |
            ForEach-Object {Write-Host "[AZTEC-735-CANDIDATES] TRACE: $_"}
    }
    throw "Aztec 735 surface material candidate ledger failed exit=$code; log=$runLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
if([string]$r.Schema -ne 'WaW360.AztecPcWorldSurfaceMaterialCandidateLedger.v1' -or
   [string]$r.Status -ne 'AZTEC_PC_735_SURFACE_39_NAME_ORDINAL_CANDIDATE_LEDGER' -or
   [int]$r.SourceMaterialNameCandidates -ne 39 -or
   [int]$r.UniqueExactNativeNameCandidates -ne 39 -or
   [int]$r.DistinctVirtualAliasSlots -ne 39 -or
   [int]$r.SourceSurfaces -ne 735 -or
   [int]$r.SourceTriangles -ne 19551 -or
   [int]$r.HypotheticalSurfacesLabeled -ne 735 -or
   [int]$r.HypotheticalTrianglesLabeled -ne 19551 -or
   @($r.CandidateGroups).Count -ne 39 -or
   @($r.SurfaceCandidateRows).Count -ne 735 -or
   @($r.CandidateGroups | Where-Object { [bool]$_.SourceVirtualToPhysicalBindingProven -or [bool]$_.XboxMaterialBindingProven }).Count -ne 0 -or
   @($r.SurfaceCandidateRows | Where-Object { [bool]$_.SourcePointerToMaterialProven -or [bool]$_.XboxMaterialBindingProven }).Count -ne 0 -or
   [bool]$r.PhysicalMemoryArrayOwnershipProven -or
   [bool]$r.VirtualAliasToMaterialNameProven -or
   [bool]$r.XboxMaterialBindingsProven -or
   [bool]$r.All735XboxSurfacesBound -or
   [bool]$r.XboxHardwareReady){
    throw 'Source candidate 735-surface ledger violated exact cardinality or zero Xbox binding gates.'
}
Write-Host "[AZTEC-735-CANDIDATES] exactNames=$($r.UniqueExactNativeNameCandidates)/39 virtualSlots=$($r.DistinctVirtualAliasSlots)/39 hypotheticalSurfaces=$($r.HypotheticalSurfacesLabeled)/735"
Write-Host "[AZTEC-735-CANDIDATES] triangles=$($r.HypotheticalTrianglesLabeled)/19551; source-only, zero bindings proven"
foreach($group in @($r.CandidateGroups | Select-Object -First 10)){
    Write-Host "[AZTEC-735-CANDIDATES] CANDIDATE #$($group.Ordinal) pointer=$($group.SourceVirtualAliasPointer) name=$($group.SourceMaterialNameCandidate) XboxRootIndex=$($group.UniqueExactNativeMaterialNameRootIndex) surfaces=$($group.RecountedSourceSurfaceCount) triangles=$($group.RecountedSourceTriangleCount) BINDING_PROVEN=False"
}
Write-Host '[AZTEC-735-CANDIDATES] ================= RESULT ============================='
Write-Host "[AZTEC-735-CANDIDATES] status=$($r.Status)"
Write-Host "[AZTEC-735-CANDIDATES] report=$json"
Write-Host '[AZTEC-735-CANDIDATES] Next: independent native-GfxWorld/OAT pointer relocation validation.'
Write-Host '[AZTEC-735-CANDIDATES] No GfxSurface material pointers written, Gate 7 PENDING.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
