[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$dual=Join-Path $base 'xenon_native_gfxworld_root_dpvs_abi\native_xenon_gfxworld_root_dpvs_abi.json'
$materialLink=Join-Path $base 'pc_xenon_735_world_surface_live_39_material_ff_link\aztec_735_world_surfaces_linked_to_39_ff_material_roots.json'
$out=Join-Path $base 'pc_source_to_xenon_gfxworld_root_dpvs_scalar_seed'
$report=Join-Path $out 'aztec_pc_gfxworld_800_to_xenon_900_root_dpvs_seed.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$buildLog=Join-Path $out 'build.log'
$runLog=Join-Path $out 'source_conversion.log'
foreach($f in @($dual,$materialLink,$cli)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){
        throw "GfxWorld source ownership / Xbox ABI witness missing: $f"
    }
}
$a=Get-Content -LiteralPath $dual -Raw | ConvertFrom-Json
$l=Get-Content -LiteralPath $materialLink -Raw | ConvertFrom-Json
if([string]$a.status -ne 'AZTEC_NATIVE_XBOX_GFXWORLD_ROOT_AND_DPVS_ABI_EXACT_SOURCE_WITNESS' -or
   [int]$a.originalPcGfxWorldRootAbi.gfxWorldSize -ne 800 -or
   [int]$a.nativeGfxWorldRootAbi.gfxWorldSize -ne 900 -or
   [int]$a.originalPcGfxWorldRootAbi.dpvsStaticSize -ne 100 -or
   [int]$a.nativeGfxWorldRootAbi.dpvsStaticSize -ne 100 -or
   [string]$l.Status -ne 'AZTEC_735_SURFACE_MATERIAL_REFS_TO_39_REAL_XBOX_FF_MATERIAL_ROOTS_REBASED_STANDALONE_PASS' -or
   [int]$l.SurfacesLinkedToRealFfBodies -ne 735 -or
   [int]$l.RealFfMaterialBodies -ne 39 -or
   [bool]$l.WorldGfxSurfaceArrayInsertedInFf -or
   [bool]$l.XboxHardwareReady){
    throw 'Requires original PC and Xbox ABI PASS, plus 735 linked surfaces and 39 OAT-loaded material roots.'
}
New-Item -ItemType Directory -Force -Path $out | Out-Null
Write-Host '[AZTEC-WORLD-SEED] ============================================================='
Write-Host '[AZTEC-WORLD-SEED] ORIGINAL PC 800B GFXWORLD ROOT -> XBOX 900B ABI SEED'
Write-Host '[AZTEC-WORLD-SEED] Original-PC FF root; independently loaded PC/Xbox ABI counts'
Write-Host '[AZTEC-WORLD-SEED] 100B DPVS scalar prefix, measured Xbox root offsets'
Write-Host '[AZTEC-WORLD-SEED] Missing child pointers stay null; NOT a FastFile/Xbox build'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($exit -ne 0){
    $errors=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 15)
    if($errors.Count){
        foreach($e in $errors){Write-Host "[AZTEC-WORLD-SEED] BUILD ERROR $($e.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 24 | ForEach-Object {Write-Host "[AZTEC-WORLD-SEED] BUILD $_"}
    }
    throw "PC->Xbox GfxWorld/DPVS root seed build failed: $buildLog"
}
Write-Host '[AZTEC-WORLD-SEED] Incremental .NET build PASS'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $arguments=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-gfxworld-root-dpvs-scalar-seed',$dual,$materialLink,$out)
    & dotnet @arguments 2>&1 | ForEach-Object {[string]$_} |
        Set-Content -LiteralPath $runLog -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($exit -ne 0 -or -not(Test-Path -LiteralPath $report -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 24 | ForEach-Object {Write-Host "[AZTEC-WORLD-SEED] TRACE $_"}
    }
    throw "Original PC GfxWorld/DPVS scalar source readback failed (exit=$exit): $runLog"
}
$r=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if([string]$r.Schema -ne 'WaW360.AztecPcGfxWorldRootDpvsSeed.v1' -or
   [string]$r.Status -ne 'AZTEC_PC_GFXWORLD_800_TO_XBOX_900_ROOT_SCALAR_AND_DPVS_SEED_PASS_NOT_FF' -or
   [int]$r.OriginalPcRootSize -ne 800 -or
   [int]$r.XboxRootSize -ne 900 -or
   [int]$r.DpvsSize -ne 100 -or
   [int]$r.WorldSurfaces -ne 735 -or
   [int]$r.WorldMaterials -ne 39 -or
   [int]$r.WorldVertices -ne 32713 -or
   [int]$r.WorldIndices -ne 58653 -or
   [int]$r.StaticSurfaces -ne 613 -or
   [int]$r.Cells -ne 11 -or
   @($r.SourceVerifiedRootFields).Count -ne 3 -or
   @($r.SourceVerifiedDpvsFields).Count -ne 10 -or
   [bool]$r.FullRootAllFieldsMapped -or
   [bool]$r.FullWorldChildrenSerialized -or
   [bool]$r.OatGfxWorldLoadValidated -or
   [bool]$r.XboxHardwareReady){
    throw 'Root/DPVS scalar readback violated original source counts or non-deployable gates.'
}
$root=[string]$r.XboxScalarOnlyRoot900File
if(-not(Test-Path -LiteralPath $root -PathType Leaf) -or
   (Get-Item -LiteralPath $root).Length -ne 900 -or
   (Get-FileHash -LiteralPath $root -Algorithm SHA256).Hash -ine
        [string]$r.XboxScalarOnlyRootSha256){
    throw "Source-owned 900B Xbox root seed SHA/length changed: $root"
}
Write-Host '[AZTEC-WORLD-SEED] Original PC 800B -> Xenon 900B scalar-only root'
Write-Host "[AZTEC-WORLD-SEED] root fields=$(@($r.SourceVerifiedRootFields).Count)/3 DPVS fields=$(@($r.SourceVerifiedDpvsFields).Count)/10"
Write-Host "[AZTEC-WORLD-SEED] source geometry: surfaces=$($r.WorldSurfaces) materials=$($r.WorldMaterials) vertices=$($r.WorldVertices) indices=$($r.WorldIndices)"
Write-Host "[AZTEC-WORLD-SEED] xenonScalarRootSha256=$($r.XboxScalarOnlyRootSha256)"
Write-Host '[AZTEC-WORLD-SEED] No Xbox hardware FF: GfxWorld child pointers and arrays NOT emitted.'
Write-Host "[AZTEC-WORLD-SEED] status=$($r.Status)"
Write-Host "[AZTEC-WORLD-SEED] report=$report"
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
