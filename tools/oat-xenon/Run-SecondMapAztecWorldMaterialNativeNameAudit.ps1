[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$bridge=Join-Path $base 'pc_material_world_boundary_bridge\aztec_pc_material_world_boundary_bridge.json'
$native=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$atlas=Join-Path $base 'pc_gfxworld_material_binding_atlas\aztec_pc_gfxworld_material_binding_atlas.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_world_material_native_name_audit'
$json=Join-Path $out 'aztec_pc_world_material_native_name_audit.json'
$buildLog=Join-Path $out 'build.log'
$runLog=Join-Path $out 'audit.log'
foreach($p in @($bridge,$native,$atlas,$cli)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec native material-name reconciliation prerequisite missing: $p"
    }
}
$b=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $bridge -Raw)
$n=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $native -Raw)
$a=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $atlas -Raw)
if([string]$b.Schema -ne 'WaW360.AztecPcMaterialWorldBoundaryBridge.v1' -or
   [string]$b.Status -ne 'AZTEC_PC_39_MATERIALS_EXACT_MEMORY_TO_VERTICES_PHYSICAL_CHAIN' -or
   -not [bool]$b.ContiguousPhysicalChainValidated -or
   [int]$b.ConsecutiveMaterialCandidates -ne 39 -or
   [int]$b.VirtualAliasGroups -ne 39 -or
   [int]$b.SourceSurfaceCount -ne 735 -or
   [bool]$b.WorldMaterialMemoryOwnershipProven -or
   [bool]$b.VirtualAliasToPhysicalMaterialRelocationProven -or
   [bool]$b.XboxMaterialBindingsProven -or
   [bool]$b.XboxHardwareReady -or
   -not [bool]$n.TypeSequenceVerified -or
   @($n.Entries).Count -ne 8509 -or
   [string]$a.schema -ne 'WaW360.AztecPcGfxWorldMaterialBindingAtlas.v1' -or
   [string]$a.status -ne 'AZTEC_PC_GFXWORLD_39_MATERIAL_GROUP_DEPENDENCY_ATLAS_PASS' -or
   [int]$a.originalPcPointerGroups -ne 39 -or
   [int]$a.totalSurfaceReferences -ne 735 -or
   [int]$a.materialPointerGroupsBoundToXbox -ne 0 -or
   [bool]$a.readyForXbox){
    throw 'Require 39 PC physical source material chain + verified 8509 native identities + untouched 735-surface atlas.'
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
Write-Host '[AZTEC-MAT-NAMES] ====================================================='
Write-Host '[AZTEC-MAT-NAMES] 39 ORIGINAL-PC MATERIAL NAMES vs 8509 GOLDEN XBOX ROOTS'
Write-Host '[AZTEC-MAT-NAMES] Exact Material-type NAME matches only; independently SHA-verifies golden native FF.'
Write-Host '[AZTEC-MAT-NAMES] No ordinal virtual-slot identity promotion. NO BINDINGS / NO FF.'
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
        foreach($e in $errors){Write-Host "[AZTEC-MAT-NAMES] BUILD ERROR: $($e.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-MAT-NAMES] BUILD: $_"}
    }
    throw "Aztec Material-to-native-name audit build failed: $buildLog"
}
Write-Host '[AZTEC-MAT-NAMES] Incremental .NET build PASS'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $argsList=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-world-material-native-name-audit',$bridge,$native,$atlas,$json)
    & dotnet @argsList 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        # The first exception line identifies the root cause; the final
        # Program.cs stack frame by itself is not diagnostic.
        Write-Host '[AZTEC-MAT-NAMES] EXCEPTION HEAD:'
        Get-Content -LiteralPath $runLog -TotalCount 12 |
            ForEach-Object {Write-Host "[AZTEC-MAT-NAMES] TRACE: $_"}
        Write-Host '[AZTEC-MAT-NAMES] EXCEPTION TAIL:'
        Get-Content -LiteralPath $runLog -Tail 12 |
            ForEach-Object {Write-Host "[AZTEC-MAT-NAMES] TRACE: $_"}
    }
    throw "Aztec source-to-native-name audit failed exit=$code; log=$runLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
if([string]$r.Schema -ne 'WaW360.AztecPcWorldMaterialNativeNameReconciliation.v1' -or
   [string]$r.Status -ne 'AZTEC_PC_39_MATERIAL_NAMES_VS_GOLDEN_NATIVE_ROOTS_INVENTORIED' -or
   [int]$r.GoldenNativeRoots -ne 8509 -or
   [int]$r.SourceMaterialCandidates -ne 39 -or
   [int]$r.SourceSurfaces -ne 735 -or
   [int]$r.SourceTriangles -ne 19551 -or
   @($r.Rows).Count -ne 39 -or
   ([int]$r.UniqueExactNameCandidates + [int]$r.NoExactNameCandidates + [int]$r.AmbiguousExactNameCandidates) -ne 39 -or
   @($r.Rows | Where-Object { [bool]$_.PcSourceMaterialOwnershipProven -or [bool]$_.VirtualAliasToPcMaterialProven -or [bool]$_.XboxMaterialRootBindingProven }).Count -ne 0 -or
   [bool]$r.MaterialMemoryOwnershipProven -or
   [bool]$r.SourceVirtualToPhysicalMaterialRelocationProven -or
   [bool]$r.XboxMaterialBindingsProven -or
   [bool]$r.All735SurfaceMaterialRefsBound -or
   [bool]$r.XboxHardwareReady){
    throw 'Aztec PC/native material names report failed exact-inventory/no-binding safety gates.'
}
Write-Host "[AZTEC-MAT-NAMES] nativeRoots=$($r.GoldenNativeRoots) nativeMaterials=$($r.GoldenNativeMaterialRoots) PCSourceCandidates=$($r.SourceMaterialCandidates)"
Write-Host "[AZTEC-MAT-NAMES] UNIQUE_EXACT_NAME_ONLY=$($r.UniqueExactNameCandidates) absent=$($r.NoExactNameCandidates) ambiguous=$($r.AmbiguousExactNameCandidates)"
foreach($row in @($r.Rows)){
    $indices=@($row.NativeNameMatches | ForEach-Object { [string]$_.NativeRootIndex }) -join ','
    Write-Host "[AZTEC-MAT-NAMES] #$($row.Ordinal) $($row.UnverifiedPcMaterialName) -> $($row.MatchKind) nativeIndices=$indices"
}
Write-Host '[AZTEC-MAT-NAMES] ================= RESULT ==========================='
Write-Host "[AZTEC-MAT-NAMES] status=$($r.Status)"
Write-Host "[AZTEC-MAT-NAMES] report=$json"
Write-Host '[AZTEC-MAT-NAMES] NAME MATCHES ONLY. 0 virtual alias/PC material or Xbox root bindings proven. Gate 7 PENDING.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
