[CmdletBinding()]
param([switch]$BuildDumper)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$nativeManifest=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$linked=Join-Path $base 'pc_xenon_735_world_surface_live_39_material_ff_link\aztec_735_world_surfaces_linked_to_39_ff_material_roots.json'
$dumper=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$capabilities=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$capability='t4xenon-gfxworld-root-dpvs-layout-witness-v1'
$out=Join-Path $base 'xenon_native_gfxworld_root_dpvs_abi'
$log=Join-Path $out 'native_oat_gfxworld_root_dpvs.log'
$report=Join-Path $out 'native_xenon_gfxworld_root_dpvs_abi.json'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('WaW360_XenonWorldRoot_' + [Guid]::NewGuid().ToString('N').Substring(0,12))
foreach($p in @($nativeManifest,$linked)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "GfxWorld root/DPVS ABI source or 735 real-Material link proof missing: $p"
    }
}
$n=Get-Content -LiteralPath $nativeManifest -Raw | ConvertFrom-Json
$l=Get-Content -LiteralPath $linked -Raw | ConvertFrom-Json
if([string]$n.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
   [int]$n.assets -ne 8509 -or
   -not [bool]$n.typeSequenceVerified -or
   [string]$l.Status -ne 'AZTEC_735_SURFACE_MATERIAL_REFS_TO_39_REAL_XBOX_FF_MATERIAL_ROOTS_REBASED_STANDALONE_PASS' -or
   [int]$l.SourceOwnedXboxSurfaceCount -ne 735 -or
   [int]$l.SurfacesLinkedToRealFfBodies -ne 735 -or
   [int]$l.MaterialRootsWithRealFfGraphBody -ne 39 -or
   [int]$l.MaterialIdentityShellTargets -ne 0 -or
   [int]$l.TriangleCount -ne 19551 -or
   [bool]$l.WorldGfxSurfaceArrayInsertedInFf -or
   [bool]$l.GfxWorldRootSerialized -or
   [bool]$l.XboxHardwareReady){
    throw 'Requires exact previous 735 GfxSurfaces linked to 39 REAL Xbox FF material owners, but no GfxWorld FF promoted.'
}
$native=[string]$n.nativeFastFile
if(-not(Test-Path -LiteralPath $native -PathType Leaf) -or
   (Get-Item -LiteralPath $native).Length -ne 82739469 -or
   (Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash -ine
       'E0F3C244DD7B72D0FE71C05FAF014C2C9BFB05F770183954B3E7CDE625EBA98F'){
    throw 'Original-Xbox reference identity changed; cannot establish Xbox root ABI.'
}
function Test-Installed {
    if(-not(Test-Path -LiteralPath $dumper -PathType Leaf) -or
       -not(Test-Path -LiteralPath $capabilities -PathType Leaf)){return $false}
    $c=Get-Content -LiteralPath $capabilities -Raw | ConvertFrom-Json
    return ([string]$c.format -eq 'WaW360.OatGraphDumperCapabilities.v1' -and
        [string]$c.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($c.capabilities) -contains $capability)
}
if(-not(Test-Installed) -and $BuildDumper){
    Write-Host '[AZTEC-WORLD-ABI] One-time pinned OAT build for original Xbox GfxWorld root/DPVS ABI.'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1')
    if($LASTEXITCODE -ne 0 -or -not $?){throw 'Native pinned OAT GfxWorld root ABI build failed.'}
}
if(-not(Test-Installed)){
    throw ('Missing ' + $capability + '; run this script ONCE with -BuildDumper.')
}
$worst=Join-Path $temp ('xenon_gfxworld_surface_abi_witness\' + ('A'*96) + '.json')
if($worst.Length -ge 240){throw "OAT output path too long: $($worst.Length) >= 240."}
New-Item -ItemType Directory -Force -Path $out,$temp | Out-Null
Write-Host '[AZTEC-WORLD-ABI] ============================================================='
Write-Host '[AZTEC-WORLD-ABI] ORIGINAL XBOX GFXWORLD ROOT / DPVS STATIC EXACT NATIVE ABI'
Write-Host '[AZTEC-WORLD-ABI] Read only pinned OAT-loaded Xbox reference, NOT donor payload'
Write-Host '[AZTEC-WORLD-ABI] Native offsetof index/vertex/materialMemory/DPVS child fields'
Write-Host '[AZTEC-WORLD-ABI] Compare source-owned PC 735 surfaces, 39 materials, 32713 vertices, 58653 indices'
Write-Host '[AZTEC-WORLD-ABI] NO FF or child arrays emitted, NOT FOR XBOX'
$captured=New-Object 'System.Collections.Generic.List[string]'
$exit=$null
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $global:LASTEXITCODE=$null
    try{
        & $dumper '--no-color' '--include-assets' 'gfxworld' '--output-folder' $temp $native 2>&1 |
            ForEach-Object {$captured.Add([string]$_)}
        if($null -ne $LASTEXITCODE){$exit=[int]$LASTEXITCODE}
    }catch{
        $captured.Add("Native tool failed: $_")
    }
}finally{$ErrorActionPreference=$old}
if($null -eq $exit){$exit=-1}
$hex='0x{0:X8}' -f ([long]$exit -band 4294967295L)
$logLines=@("Exit=$exit","Hex=$hex","OriginalXboxFF=$native","NativeWitnessRoot=$temp")
$logLines+=@($captured)
[IO.File]::WriteAllLines($log,[string[]]$logLines,[Text.UTF8Encoding]::new($false))
Write-Host "[AZTEC-WORLD-ABI] Native OAT exit=$exit ($hex) outputLines=$($captured.Count)"
if($exit -ne 0){
    Get-Content -LiteralPath $log -Tail 20 | ForEach-Object {Write-Host "[AZTEC-WORLD-ABI] TRACE $_"}
    throw "Pinned original-Xbox GfxWorld/DPVS ABI witness failed: $log"
}
$witnessRoot=Join-Path $temp 'xenon_gfxworld_surface_abi_witness'
$files=@(Get-ChildItem -LiteralPath $witnessRoot -Filter '*.json' -File -ErrorAction SilentlyContinue)
if($files.Count -ne 1){throw "Expected 1 loaded native Xbox GfxWorld ABI witness, found $($files.Count)."}
$w=Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json
if([string]$w.schema -ne 'WaW360.T4XenonGfxWorldSurfaceAbiWitness.v1' -or
   [string]$w.status -ne 'NATIVE_XENON_GFXWORLD_SURFACE_ABI_LOADER_WITNESS' -or
   [string]$w.platform -ne 'T4Xenon' -or
   -not [bool]$w.loadedArraysAvailable -or
   [int]$w.surfaceCount -ne 735 -or
   [int]$w.materialMemoryCount -ne 39 -or
   [int]$w.vertexCount -ne 32713 -or
   [int]$w.indexCount -ne 58653 -or
   [int]$w.directMatches -ne 735 -or
   [int]$w.unmatched -ne 0 -or
   [int]$w.ambiguous -ne 0 -or
   [int]$w.abi.gfxSurfaceSize -ne 76 -or
   [int]$w.abi.materialFieldAt -ne 44 -or
   [int]$w.abi.topMipMinAt -ne 16 -or
   [int]$w.abi.topMipMaxAt -ne 28 -or
   @($w.surfaces).Count -ne 735){
    throw 'Native Xbox loaded GfxWorld source ABI/735 material identities disagree with pinned reference.'
}
$a=$w.gfxWorldRootAbi
$g=$w.loadedWorldRootShape
if($null -eq $a -or $null -eq $g -or
   [int]$a.gfxWorldSize -lt 400 -or
   [int]$a.gfxWorldSize -gt 4096 -or
   [int]$a.dpvsStaticSize -lt 80 -or
   [int]$a.dpvsStaticSize -gt 256 -or
   [int]$a.materialMemorySize -ne 8 -or
   [int]$a.nameAt -ne 0 -or
   [int]$a.baseNameAt -ne 4 -or
   [int]$a.dpvsStaticAt -le 0 -or
   [int]$a.dpvsStaticAt -ge [int]$a.gfxWorldSize -or
   [int]$a.dpvsStaticSurfacesAt -le 0 -or
   [int]$a.dpvsStaticSurfacesAt -ge [int]$a.dpvsStaticSize -or
   [int]$a.dpvsStaticSurfaceCountAt -ne 4 -or
   [int]$a.materialMemoryAt -ge [int]$a.dpvsStaticAt -or
   [int]$a.indicesAt -le [int]$a.indexCountAt -or
   [int]$a.vertexCountAt -ge [int]$a.vertexDataAt -or
   [int]$g.surfaceCount -ne 735 -or
   [int]$g.vertexCount -ne 32713 -or
   [int]$g.indexCount -ne 58653 -or
   [int]$g.materialMemoryCount -ne 39 -or
   [int]$g.staticSurfaceCount -le 0 -or
   [int]$g.staticSurfaceCount -gt 735 -or
   -not [bool]$g.surfacesPresent -or
   -not [bool]$g.materialMemoryPresent -or
   -not [bool]$g.indicesPresent -or
   -not [bool]$g.verticesPresent){
    throw "Pinned Xbox loader root/DPVS ABI/child-shape invariant not proven. Review raw witness $($files[0].FullName)"
}
$payload=[ordered]@{
    schema='WaW360.AztecXenonNativeGfxWorldRootDpvsAbiWitness.v1'
    status='AZTEC_NATIVE_XBOX_GFXWORLD_ROOT_AND_DPVS_ABI_EXACT_SOURCE_WITNESS'
    originalNativeFastFile=$native
    originalNativeFastFileSha256='E0F3C244DD7B72D0FE71C05FAF014C2C9BFB05F770183954B3E7CDE625EBA98F'
    generated39RealMaterialsScratchFf=[string]$l.ExactValidated39MaterialFastFile
    standalone735RealFfMaterialLinkProof=$linked
    nativeGfxWorldRootAbi=$a
    loadedNativeWorldChildShape=$g
    pcExpectedVertexCount=32713
    pcExpectedIndexCount=58653
    pcExpectedSurfaceCount=735
    pcExpectedMaterialMemoryCount=39
    pcVsNativeCountParity=$true
    sourceGfxWorldRootSerialized=$false
    sourceDpvsStaticSerialized=$false
    sourceWorldSurfacesInsertedInFf=$false
    hardwareReady=$false
    note='Original Xbox OAT witness gives T4Xenon sizeof/offsetof layout and reference-child shape only. NEVER donor-copy world body. Next independently read actual PC-owned world root/DPVS and re-plan the FF allocation. Not hardware ready.'
}
$payload | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host "[AZTEC-WORLD-ABI] nativeGfxWorldSize=$($a.gfxWorldSize)B dpvsStaticSize=$($a.dpvsStaticSize)B materialMemorySize=$($a.materialMemorySize)B"
Write-Host "[AZTEC-WORLD-ABI] offsets dpvsRoot=$($a.dpvsStaticAt) surfacesInDpvs=+$($a.dpvsStaticSurfacesAt) => absolute $([int]$a.dpvsStaticAt + [int]$a.dpvsStaticSurfacesAt)"
Write-Host "[AZTEC-WORLD-ABI] offsets vertexData=$($a.vertexDataAt) indexPtr=$($a.indicesAt) materialMemory=$($a.materialMemoryAt)"
Write-Host "[AZTEC-WORLD-ABI] nativeLoadedCounts indices=$($g.indexCount) vertices=$($g.vertexCount) surfaces=$($g.surfaceCount) materialOwners=$($g.materialMemoryCount) staticSurfaces=$($g.staticSurfaceCount)"
Write-Host "[AZTEC-WORLD-ABI] DPVS child visCounts surface=$($g.surfaceVisDataCount) smodel=$($g.smodelVisDataCount) cells=$($g.cellCount)"
Write-Host "[AZTEC-WORLD-ABI] report=$report"
Write-Host '[AZTEC-WORLD-ABI] Native ABI proven only; original PC GfxWorld children not yet inserted. Gate 8 PENDING.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
