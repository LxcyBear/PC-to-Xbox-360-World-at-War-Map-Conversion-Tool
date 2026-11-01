[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$surfaces=Join-Path $base 'pc_xenon_76byte_gfxsurface_canary\aztec_735_gfxsurfaces_76byte_source_canary.json'
$worklist=Join-Path $base 'pc_verified_xbox_surface_material_relocation_worklist\aztec_verified_surface_material_relocation_worklist.json'
$identity=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$scratch=Join-Path $base 'pc_39_world_material_real_ff_scratch_integration\aztec_39_source_world_material_scratch_ff_gate.json'
$out=Join-Path $base 'pc_xenon_735_world_surface_live_39_material_ff_link'
$report=Join-Path $out 'aztec_735_world_surfaces_linked_to_39_ff_material_roots.json'
$bin=Join-Path $out 'aztec_735_surfaces_76byte_BE_LINKED_TO_REAL_39_FF_LAYOUT_NOT_IN_FF.bin'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$buildLog=Join-Path $out 'build.log'
$runLog=Join-Path $out 'link.log'
foreach($p in @($surfaces,$worklist,$identity,$scratch,$cli)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Missing real 39-Material FF or 735 original PC source surface prerequisite: $p"
    }
}
$ffGate=Get-Content -LiteralPath $scratch -Raw | ConvertFrom-Json
if([string]$ffGate.status -ne 'AZTEC_PC_39_REAL_WORLD_MATERIAL_ROOTS_IN_SCRATCH_XENON_FF_OAT_PASS' -or
   [int]$ffGate.realSourceWorldMaterialRoots -ne 39 -or
   [int]$ffGate.nativeWorldGfxSurfaceReferencesInFf -ne 0 -or
   -not [bool]$ffGate.oatLoaderPass -or
   -not [bool]$ffGate.oatZeroWarningsZeroErrors -or
   -not [bool]$ffGate.layoutReplayExact -or
   -not [bool]$ffGate.xboxNativeRootIdentityExact -or
   [bool]$ffGate.hardwareReady){
    throw 'The actual native FF must have 39 source-derived real OAT-PASS Material bodies and no GfxWorld yet.'
}
New-Item -ItemType Directory -Force -Path $out | Out-Null
Write-Host '[AZTEC-735-LIVE-LINK] ======================================================='
Write-Host '[AZTEC-735-LIVE-LINK] REBASE 735 XBOX GFXSURFACE MATERIAL POINTERS -> REAL 39 FF MATERIAL BODIES'
Write-Host '[AZTEC-735-LIVE-LINK] Use current OAT-validated 8509-root FF, NOT old shell-only layout'
Write-Host '[AZTEC-735-LIVE-LINK] 76-byte BE GfxSurface: material field +44, source topMip 735/735'
Write-Host '[AZTEC-735-LIVE-LINK] Exact root INSERT slot -> packed VIRTUAL alias; verify every other byte unchanged'
Write-Host '[AZTEC-735-LIVE-LINK] STANDALONE LINKED CANARY, NOT INSERTED INTO FF, NOT FOR XBOX'
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
        foreach($error in $errors){Write-Host "[AZTEC-735-LIVE-LINK] BUILD ERROR $($error.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 20 |
            ForEach-Object {Write-Host "[AZTEC-735-LIVE-LINK] BUILD $_"}
    }
    throw "Aztec 735 source surface-to-39 real Material FF link build failed: $buildLog"
}
Write-Host '[AZTEC-735-LIVE-LINK] Incremental .NET build PASS'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $argsList=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-xenon-39-world-surface-live-layout-link',
        $surfaces,$worklist,$identity,$scratch,$out)
    & dotnet @argsList 2>&1 | ForEach-Object {[string]$_} |
        Set-Content -LiteralPath $runLog -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($exit -ne 0 -or -not(Test-Path -LiteralPath $report -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -TotalCount 14 |
            ForEach-Object {Write-Host "[AZTEC-735-LIVE-LINK] TRACE $_"}
        Get-Content -LiteralPath $runLog -Tail 10 |
            ForEach-Object {Write-Host "[AZTEC-735-LIVE-LINK] TRACE $_"}
    }
    throw "Aztec 735 GfxSurface true live 39 Material FF-root pointer rebase failed ($exit): $runLog"
}
$r=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if([string]$r.Schema -ne 'WaW360.AztecPcXenon39WorldSurfaceFinalLayoutLink.v1' -or
   [string]$r.Status -ne 'AZTEC_735_SURFACE_MATERIAL_REFS_TO_39_REAL_XBOX_FF_MATERIAL_ROOTS_REBASED_STANDALONE_PASS' -or
   [int]$r.NativeXboxRoots -ne 8509 -or
   [int]$r.RealFfMaterialBodies -ne 39 -or
   [int]$r.SourceOwnedXboxSurfaceCount -ne 735 -or
   [int]$r.TriangleCount -ne 19551 -or
   [int]$r.GfxSurfaceXboxStride -ne 76 -or
   [int]$r.GfxSurfaceMaterialFieldOffset -ne 44 -or
   [int]$r.LinkedSurfaceByteLength -ne 55860 -or
   [int]$r.MaterialRootsWithRealFfGraphBody -ne 39 -or
   [int]$r.MaterialIdentityShellTargets -ne 0 -or
   [int]$r.SurfacesLinkedToRealFfBodies -ne 735 -or
   [int]$r.SurfacesLinkedToIdentityShells -ne 0 -or
   @($r.MaterialRootTargets).Count -ne 39 -or
   @($r.Surfaces).Count -ne 735 -or
   [bool]$r.WorldGfxSurfaceArrayInsertedInFf -or
   [bool]$r.GfxWorldRootSerialized -or
   [bool]$r.OatValidatedGfxWorldLoad -or
   [bool]$r.XboxHardwareReady){
    throw '735/39 live material FF link violated material body, layout, or non-deployment constraints.'
}
if(-not(Test-Path -LiteralPath $bin -PathType Leaf) -or
   (Get-Item -LiteralPath $bin).Length -ne 55860 -or
   (Get-FileHash -LiteralPath $bin -Algorithm SHA256).Hash -ine
       [string]$r.StandaloneLinkedSurfaceArraySha256){
    throw 'Rebased Xbox source surface/material FF link bytes missing or SHA mismatch.'
}
if(-not(Test-Path -LiteralPath ([string]$r.ExactValidated39MaterialFastFile) -PathType Leaf) -or
   (Get-FileHash -LiteralPath ([string]$r.ExactValidated39MaterialFastFile) -Algorithm SHA256).Hash -ine
       [string]$r.ExactValidated39MaterialFastFileSha256){
    throw 'The exact OAT-validated FF changed AFTER this link, do not use stale Material root offsets.'
}
Write-Host "[AZTEC-735-LIVE-LINK] actualFF=OAT PASS 8509/8509 native root identities"
Write-Host "[AZTEC-735-LIVE-LINK] 76B surfaces=$($r.SourceOwnedXboxSurfaceCount)/735; verified native topMip 735/735"
Write-Host "[AZTEC-735-LIVE-LINK] 39 real emitted Material bodies=$($r.MaterialRootsWithRealFfGraphBody)/39 identityShellTargets=$($r.MaterialIdentityShellTargets)"
Write-Host "[AZTEC-735-LIVE-LINK] surfacePointerReadbacks=$($r.SurfacesLinkedToRealFfBodies)/735 at +$($r.GfxSurfaceMaterialFieldOffset) shellPointers=$($r.SurfacesLinkedToIdentityShells)"
Write-Host "[AZTEC-735-LIVE-LINK] standaloneSha256=$($r.StandaloneLinkedSurfaceArraySha256)"
foreach($m in @($r.MaterialRootTargets | Sort-Object ReferencingWorldSurfaces -Descending | Select-Object -First 5)){
    Write-Host "[AZTEC-735-LIVE-LINK] material#$($m.RootIndex) $($m.MaterialName) surfaces=$($m.ReferencingWorldSurfaces) pointer=$($m.FfSpecificPackedVirtualPointer)"
}
Write-Host '[AZTEC-735-LIVE-LINK] ================= RESULT ================================'
Write-Host "[AZTEC-735-LIVE-LINK] status=$($r.Status)"
Write-Host "[AZTEC-735-LIVE-LINK] report=$report"
Write-Host '[AZTEC-735-LIVE-LINK] WARNING: STANDALONE BE 76B surface array, not yet in FastFile. GfxWorld/DPVS/semantic validation pending. NOT FOR XBOX.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
