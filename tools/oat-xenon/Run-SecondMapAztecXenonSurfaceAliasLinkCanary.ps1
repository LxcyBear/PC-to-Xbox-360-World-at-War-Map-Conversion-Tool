[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$surfaces=Join-Path $base 'pc_xenon_76byte_gfxsurface_canary\aztec_735_gfxsurfaces_76byte_source_canary.json'
$worklist=Join-Path $base 'pc_verified_xbox_surface_material_relocation_worklist\aztec_verified_surface_material_relocation_worklist.json'
$native=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$layout=Join-Path $base 'pc_six_family_comworld_fullgraph_integration\scratch_emit\layout_recheck\nuclear_xenon_block_layout.json'
$materialBinding=Join-Path $base 'pc_two_xmodel_two_material_fullgraph_integration\aztec_two_pc_material_root_binding.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_xenon_world_surface_material_root_alias_link'
$json=Join-Path $out 'aztec_735_gfxsurfaces_material_root_alias_link_audit.json'
$bin=Join-Path $out 'aztec_735_gfxsurfaces_76byte_BE_ALIAS_LINK_ONLY_NOT_FOR_XBOX.bin'
$buildLog=Join-Path $out 'build.log'
$probeLog=Join-Path $out 'alias_link.log'
foreach($f in @($surfaces,$worklist,$native,$layout,$materialBinding,$cli)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){
        throw "Require passing 76B source canary + existing six-family layout/Material binding: $f"
    }
}
$c=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $surfaces -Raw)
$l=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $layout -Raw)
$mb=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $materialBinding -Raw)
if([string]$c.Status -ne 'AZTEC_PC_735_XENON_76B_GFXSURFACES_SOURCE_DERIVED_TOPMIP_READBACK_ONLY' -or
   [int]$c.SourceDerivedNativeExactTopMipSurfaces -ne 735 -or
   [int]$c.ZeroMaterialPointerFields -ne 735 -or
   [bool]$c.XboxHardwareReady -or
   -not [bool]$l.LayoutReady -or
   [int]$l.NativeRoots -ne 8509 -or
   @($l.RootInsertLayouts).Count -ne 8509 -or
   [int]$l.RealMaterialRoots -ne 2 -or
   [string]$mb.Format -ne 'WaW360.AztecPcTwoMaterialBinding.v1' -or
   @($mb.OwnedBindings).Count -ne 2 -or
   -not [bool]$mb.BindingPass -or
   -not [bool]$mb.ValidationOnly){
    throw 'Existing specific six-family scratch layout, exact native material coverage or original 76B source geometry changed.'
}
New-Item -ItemType Directory -Force -Path $out | Out-Null
Write-Host '[AZTEC-MAT-LINK] ====================================================='
Write-Host '[AZTEC-MAT-LINK] TRUE 76B XENON SURFACES -> PLANNED MATERIAL ROOT ALIASES'
Write-Host '[AZTEC-MAT-LINK] Verify 735/735 BE +44 pointers against actual Stage4D VIRTUAL insert slots.'
Write-Host '[AZTEC-MAT-LINK] Distinguish real serialized Material graphs from identity-shell roots.'
Write-Host '[AZTEC-MAT-LINK] This link copy is zone-LAYOUT-SPECIFIC. NO FF / NO XBOX.'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($exit -ne 0){
    $errs=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 16)
    if($errs.Count){foreach($e in $errs){Write-Host "[AZTEC-MAT-LINK] BUILD ERROR $($e.Line)"}}
    elseif(Test-Path -LiteralPath $buildLog){Get-Content -LiteralPath $buildLog -Tail 20 | ForEach-Object {Write-Host "[AZTEC-MAT-LINK] BUILD $_"}}
    throw "Xenon world surface link canary incremental build failed. Log=$buildLog"
}
Write-Host '[AZTEC-MAT-LINK] Incremental .NET build PASS'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $argsList=@('run','--no-build','-c','Release','--project',$cli,'--',
      'aztec-pc-xenon-surface-alias-link-canary',$surfaces,$worklist,$native,$layout,$materialBinding,$out)
    & dotnet @argsList 2>&1 | ForEach-Object {[string]$_} |
        Set-Content -LiteralPath $probeLog -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($exit -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $probeLog -PathType Leaf){
        Get-Content -LiteralPath $probeLog -TotalCount 16 | ForEach-Object {Write-Host "[AZTEC-MAT-LINK] TRACE $_"}
        Get-Content -LiteralPath $probeLog -Tail 10 | ForEach-Object {Write-Host "[AZTEC-MAT-LINK] TRACE $_"}
    }
    throw "World surface/root alias link preflight failed exit=$exit; log=$probeLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
if([string]$r.Schema -ne 'WaW360.AztecPcXenonSurfaceAliasLinkCanary.v1' -or
   [string]$r.Status -ne 'AZTEC_735_XENON_MATERIAL_ROOT_ALIAS_PACKED_POINTERS_DIAGNOSTIC_ONLY' -or
   [int]$r.TotalSurfaceMaterialReferences -ne 735 -or
   [int]$r.DistinctWorldMaterialRoots -ne 39 -or
   [int]$r.CoveredTriangles -ne 19551 -or
   [int]$r.ByteLength -ne 55860 -or
   [int]$r.XboxGfxSurfaceStride -ne 76 -or
   [int]$r.XboxMaterialOffset -ne 44 -or
   ([int]$r.WorldRootsWithRealMaterialGraph+[int]$r.WorldRootsWithIdentityShellOnly) -ne 39 -or
   ([int]$r.SurfaceReferencesToRealMaterialGraphs+[int]$r.SurfaceReferencesToIdentityShells) -ne 735 -or
   @($r.Targets).Count -ne 39 -or
   @($r.LinkedSurfaceSites).Count -ne 735 -or
   [bool]$r.RealXboxGfxWorldRootEmitted -or
   [bool]$r.All39WorldMaterialGraphsEmitted -or
   [bool]$r.IntegratedOatGfxWorldLoaded -or
   [bool]$r.XboxHardwareReady){
    throw 'The independent pointer alias link canary contradicted native root/graph ownership or false-promoted a hardware gate.'
}
if(-not(Test-Path -LiteralPath $bin -PathType Leaf) -or
   (Get-Item -LiteralPath $bin).Length -ne 55860 -or
   (Get-FileHash -LiteralPath $bin -Algorithm SHA256).Hash -ine [string]$r.PseudoLinkedBinarySha256){
    throw 'BE Xbox material alias surface diagnostic output missing or SHA mismatch.'
}
Write-Host "[AZTEC-MAT-LINK] packedPointers=$($r.TotalSurfaceMaterialReferences)/735 nativeMaterialRoots=$($r.DistinctWorldMaterialRoots)/39 XboxSurfaceABI=$($r.XboxGfxSurfaceStride)B field=+$($r.XboxMaterialOffset)"
Write-Host "[AZTEC-MAT-LINK] WORLD ROOTS with REAL graph bodies=$($r.WorldRootsWithRealMaterialGraph)/39; identitySHELLS=$($r.WorldRootsWithIdentityShellOnly)"
Write-Host "[AZTEC-MAT-LINK] Surface refs to real graphs=$($r.SurfaceReferencesToRealMaterialGraphs)/735; to shell-only targets=$($r.SurfaceReferencesToIdentityShells)"
Write-Host "[AZTEC-MAT-LINK] Preexisting real Material bodies elsewhere=$($r.RealMaterialRootsElsewhereInLayout)"
foreach($item in @($r.Targets | Sort-Object -Property SurfaceCount -Descending | Select-Object -First 7)){
    Write-Host "[AZTEC-MAT-LINK] root#$($item.NativeMaterialRootIndex) $($item.MaterialName) surfaces=$($item.SurfaceCount) pointer=$($item.Stage4DPackedPointer) realGraph=$($item.RealOwnedMaterialGraph)"
}
Write-Host '[AZTEC-MAT-LINK] ================= RESULT ==============================='
Write-Host "[AZTEC-MAT-LINK] status=$($r.Status)"
Write-Host "[AZTEC-MAT-LINK] report=$json"
Write-Host '[AZTEC-MAT-LINK] Root alias bytes are valid ONLY in this old scratch layout; the Material bodies and real GfxWorld are still required. Not for Xbox.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
