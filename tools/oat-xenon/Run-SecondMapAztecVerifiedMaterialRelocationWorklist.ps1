[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$witness=Join-Path $base 'pc_oat_loaded_gfxworld_material_pointer_witness\pc_oat_gfxworld_material_pointer_reconciliation.json'
$candidates=Join-Path $base 'pc_735_surface_material_candidate_ledger\aztec_pc_735_surface_material_candidate_ledger.json'
$names=Join-Path $base 'pc_world_material_native_name_audit\aztec_pc_world_material_native_name_audit.json'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_verified_xbox_surface_material_relocation_worklist'
$json=Join-Path $out 'aztec_verified_surface_material_relocation_worklist.json'
$buildLog=Join-Path $out 'build.log'
$runLog=Join-Path $out 'reconcile.log'
foreach($f in @($witness,$candidates,$names,$geometry,$cli)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){
        throw "Exact PC OAT typed pointer / native-name / Xenon geometry prerequisite missing: $f"
    }
}
$w=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $witness -Raw)
$n=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $names -Raw)
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometry -Raw)
if([string]$w.schema -ne 'WaW360.AztecPcOatLoadedGfxWorldMaterialWitnessReconciliation.v1' -or
   [string]$w.status -ne 'AZTEC_PC_OAT_LOADER_735_TYPED_MATERIAL_POINTER_IDENTITIES_MATCH_ORDINAL_CANDIDATES' -or
   [int]$w.loadedDirectPointerMatches -ne 735 -or
   [int]$w.independentlyVerifiedOrdinalAndNameSurfaces -ne 735 -or
   [int]$w.memoryRowsWithExactSourceCandidateName -ne 39 -or
   [int]$w.unresolvedOrAmbiguous -ne 0 -or
   [int]$w.directlyMatchedButDifferentOrdinal -ne 0 -or
   [int]$w.xboxRootBindingsProven -ne 0 -or
   [bool]$w.xboxHardwareReady -or
   [string]$n.Status -ne 'AZTEC_PC_39_MATERIAL_NAMES_VS_GOLDEN_NATIVE_ROOTS_INVENTORIED' -or
   [int]$n.UniqueExactNameCandidates -ne 39 -or
   [bool]$n.XboxMaterialBindingsProven -or
   [string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [int]$g.ZeroMaterialPointerPlaceholders -ne 735 -or
   [bool]$g.XboxHardwareReady){
    throw 'Original OAT loaded material identity, unique golden Xbox root name, or Xenon null buffer evidence changed.'
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
Write-Host '[AZTEC-MAT-FIXUPS] ==================================================='
Write-Host '[AZTEC-MAT-FIXUPS] 735 OAT-TYPED PC MATERIAL IDENTITIES -> XBOX ROOT NAMES'
Write-Host '[AZTEC-MAT-FIXUPS] 735 exact 4B Xenon GfxSurface relocation INTENT sites.'
Write-Host '[AZTEC-MAT-FIXUPS] Root INDICES are identities, NOT Xbox pointers. No byte writes.'
$prior=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$prior}
if($exit -ne 0){
    $errors=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 16)
    if($errors.Count -gt 0){
        foreach($item in $errors){Write-Host "[AZTEC-MAT-FIXUPS] BUILD ERROR: $($item.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 20 | ForEach-Object {Write-Host "[AZTEC-MAT-FIXUPS] BUILD: $_"}
    }
    throw "735 Xenon surface material relocation worklist build failed: $buildLog"
}
Write-Host '[AZTEC-MAT-FIXUPS] Incremental .NET build PASS'
$prior=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $argsList=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-verified-material-relocation-worklist',
        $witness,$candidates,$names,$geometry,$json)
    & dotnet @argsList 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$prior}
if($exit -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Write-Host '[AZTEC-MAT-FIXUPS] EXCEPTION HEAD:'
        Get-Content -LiteralPath $runLog -TotalCount 15 |
            ForEach-Object {Write-Host "[AZTEC-MAT-FIXUPS] TRACE: $_"}
        Write-Host '[AZTEC-MAT-FIXUPS] EXCEPTION TAIL:'
        Get-Content -LiteralPath $runLog -Tail 12 |
            ForEach-Object {Write-Host "[AZTEC-MAT-FIXUPS] TRACE: $_"}
    }
    throw "Original PC to Xbox Material relocation-intent proof failed exit=$exit; log=$runLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
if([string]$r.Schema -ne 'WaW360.AztecPcVerifiedSurfaceMaterialRelocationPlan.v1' -or
   [string]$r.Status -ne 'AZTEC_PC_735_TYPED_MATERIALS_VERIFIED_XBOX_ROOT_RELOCATION_WORKLIST_ONLY' -or
   [int]$r.VerifiedPcMaterialMemoryEntries -ne 39 -or
   [int]$r.VerifiedPcSurfaceReferences -ne 735 -or
   [int]$r.OriginalPcTriangles -ne 19551 -or
   [int]$r.UniqueGoldenXboxNativeMaterialRootNames -ne 39 -or
   [int]$r.ExactRelocationSites -ne 735 -or
   [int]$r.VerifiedTypedPcSurfaceIdentities -ne 735 -or
   -not [bool]$r.AllPcSourceMaterialIdentitiesVerified -or
   -not [bool]$r.AllXboxRootNamesIdentified -or
   @($r.RelocationIntents).Count -ne 735 -or
   @($r.RelocationIntents | Where-Object { $_.XenonMaterialPointerWritten -or $_.XboxRuntimeMaterialGraphBindingProven }).Count -ne 0 -or
   [int]$r.XenonSurfaceMaterialPointersWritten -ne 0 -or
   [int]$r.XboxRuntimeSurfaceMaterialBindings -ne 0 -or
   [bool]$r.XenonGfxWorldEmitted -or
   [bool]$r.OatXenonGfxWorldLoaded -or
   [bool]$r.XboxHardwareReady){
    throw 'Verified 735 surface material worklist failed non-writing/hardware safety contract.'
}
Write-Host "[AZTEC-MAT-FIXUPS] originalPcTypedPointers=$($r.VerifiedTypedPcSurfaceIdentities)/735 goldenXboxRootNames=$($r.UniqueGoldenXboxNativeMaterialRootNames)/39"
Write-Host "[AZTEC-MAT-FIXUPS] xenonMaterialFieldSites=$($r.ExactRelocationSites)/735 unmodifiedSurfaceSHA=$($r.XenonSurfaceBufferSha256)"
foreach($row in @($r.RelocationIntents | Select-Object -First 8)){
    Write-Host "[AZTEC-MAT-FIXUPS] site=+$($row.XenonSurfaceBufferByteOffset) surface#$($row.SurfaceIndex) pcName=$($row.VerifiedPcMaterialName) xboxRootIndex=$($row.UniqueMatchingXboxNativeRootIndex) pointerWritten=False"
}
Write-Host '[AZTEC-MAT-FIXUPS] ================= RESULT ==========================='
Write-Host "[AZTEC-MAT-FIXUPS] status=$($r.Status)"
Write-Host "[AZTEC-MAT-FIXUPS] report=$json"
Write-Host '[AZTEC-MAT-FIXUPS] Source binding IDENTIFIED. Xenon runtime material graph / FF pointer relocations PENDING. Gate 7 PENDING.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
