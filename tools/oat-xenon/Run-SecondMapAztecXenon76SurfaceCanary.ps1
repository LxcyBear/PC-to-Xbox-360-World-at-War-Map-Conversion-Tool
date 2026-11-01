[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$top=Join-Path $base 'pc_to_xenon_surface_topmip_calibration\aztec_pc_to_xenon_735_surface_topmip_calibration.json'
$material=Join-Path $base 'pc_verified_xbox_surface_material_relocation_worklist\aztec_verified_surface_material_relocation_worklist.json'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$out=Join-Path $base 'pc_xenon_76byte_gfxsurface_canary'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$report=Join-Path $out 'aztec_735_gfxsurfaces_76byte_source_canary.json'
$bin=Join-Path $out 'aztec_735_gfxsurfaces_76byte_be_NON_DEPLOYABLE.bin'
$buildLog=Join-Path $out 'build.log'
$probeLog=Join-Path $out 'canary.log'

foreach($f in @($top,$material,$geometry,$cli)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){throw "Missing original PC/Xenon 76B surface source audit prerequisite: $f"}
}
$t=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $top -Raw)
$m=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $material -Raw)
if([string]$t.Status -ne 'AZTEC_PC_VS_NATIVE_XENON_735_SURFACE_TOPMIP_SOURCE_HYPOTHESIS_AUDIT' -or
   [int]$t.SurfacesCompared -ne 735 -or
   [int]$t.NativeXenonSurfaceStride -ne 76 -or
   [int]$t.NativeMaterialFieldAt -ne 44 -or
   [bool]$t.XenonGfxSurfaceMaterialPointersWritten -or
   [bool]$t.XboxHardwareReady -or
   [string]$m.Status -ne 'AZTEC_PC_735_TYPED_MATERIALS_VERIFIED_XBOX_ROOT_RELOCATION_WORKLIST_ONLY' -or
   [int]$m.VerifiedTypedPcSurfaceIdentities -ne 735 -or
   [int]$m.XenonSurfaceMaterialPointersWritten -ne 0){
    throw 'Requires exact 735 source PC Material* identities, 76B native Xbox ABI and source topMip calibration.'
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
Write-Host '[AZTEC-76B] ======================================================'
Write-Host '[AZTEC-76B] ORIGINAL PC -> TRUE 76B XENON GFXSURFACE ABI CANARY'
Write-Host '[AZTEC-76B] All 735 structural records, BE readback, true +44 material offset.'
Write-Host '[AZTEC-76B] Unknown Xbox-only topMip stays ZERO-PLACEHOLDER and BLOCKED.'
Write-Host '[AZTEC-76B] NO GfxWorld FF, NO material pointer patch, NOT FOR XBOX.'
Write-Host "[AZTEC-76B] previous topMip derivation=$($t.CompleteVerifiedTopMipDerivation) sourceReady=$($t.SourceOnlyTopMipCandidateReadyFor76ByteSerialization)"
Write-Host "[AZTEC-76B] topMip candidates EXACT: bounds=$($t.NativeTopMipEqualsPcSurfaceBoundsExact)/735 vertexSpan=$($t.NativeTopMipEqualsPcVertexSpanAabbExact)/735 triangleVertices=$($t.NativeTopMipEqualsPcTriangleVertexAabbExact)/735"
Write-Host "[AZTEC-76B] PC/native geometry: firstVertex=$($t.FirstVertexExactMatches)/735 vertexCount=$($t.VertexCountExactMatches)/735 baseIndex=$($t.BaseIndexExactMatches)/735 bounds=$($t.BoundsFloat32ExactMatches)/735"

$prior=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$prior}
if($exit -ne 0){
    $e=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 15)
    if($e.Count -gt 0){foreach($x in $e){Write-Host "[AZTEC-76B] BUILD ERROR: $($x.Line)"}}
    else{Get-Content -LiteralPath $buildLog -Tail 22 | ForEach-Object {Write-Host "[AZTEC-76B] BUILD: $_"}}
    throw "Incremental 76B surface canary .NET build failed; log=$buildLog"
}
Write-Host '[AZTEC-76B] Incremental .NET build PASS'
$prior=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $argsList=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-xenon-76byte-surface-canary',
        $top,$material,$geometry,$out)
    & dotnet @argsList 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $probeLog -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$prior}
if($exit -ne 0 -or -not(Test-Path -LiteralPath $report -PathType Leaf)){
    if(Test-Path -LiteralPath $probeLog -PathType Leaf){
        Get-Content -LiteralPath $probeLog -TotalCount 15 | ForEach-Object {Write-Host "[AZTEC-76B] TRACE $_"}
        Get-Content -LiteralPath $probeLog -Tail 10 | ForEach-Object {Write-Host "[AZTEC-76B] TRACE $_"}
    }
    throw "76-byte Xenon source surface canary failed exit=$exit; log=$probeLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $report -Raw)
if([string]$r.Schema -ne 'WaW360.AztecPcXenonGfxSurface76Canary.v1' -or
   [int]$r.Surfaces -ne 735 -or
   [int]$r.SurfaceStride -ne 76 -or
   [int]$r.ByteLength -ne 55860 -or
   [int]$r.MaterialOffset -ne 44 -or
   [int]$r.DistinctNamedXboxMaterialRoots -ne 39 -or
   [int]$r.Triangles -ne 19551 -or
   [int]$r.ZeroMaterialPointerFields -ne 735 -or
   [int]$r.WrittenMaterialPointerFields -ne 0 -or
   @($r.MaterialRelocationSites).Count -ne 735 -or
   @($r.MaterialRelocationSites | Where-Object {$_.MaterialPointerWritten}).Count -ne 0 -or
   [bool]$r.XenonGfxWorldSerialized -or
   [bool]$r.OatIntegratedFastFileValidated -or
   [bool]$r.XboxHardwareReady){
    throw 'Xenon 76B source canary violated source geometry or no-FF safety contract.'
}
if(-not(Test-Path -LiteralPath $bin -PathType Leaf) -or
   (Get-Item -LiteralPath $bin).Length -ne 55860 -or
   (Get-FileHash -LiteralPath $bin -Algorithm SHA256).Hash -ine [string]$r.BinarySha256){
    throw 'Xenon 76-byte canary file missing or did not pass standalone SHA.'
}
if([bool]$r.TopMipFullySourceProven -ne [bool]$t.SourceOnlyTopMipCandidateReadyFor76ByteSerialization -or
   [int]$r.UnresolvedTopMipSurfaces -ne (735-[int]$r.SourceDerivedNativeExactTopMipSurfaces)){
    throw '76-byte canary topMip source proof count inconsistent with calibration.'
}
Write-Host "[AZTEC-76B] status=$($r.Status)"
Write-Host "[AZTEC-76B] 76ByteSurfaces=$($r.Surfaces)/735 bytes=$($r.ByteLength) materialFieldOffset=$($r.MaterialOffset)"
Write-Host "[AZTEC-76B] sourceProvenTopMip=$($r.SourceDerivedNativeExactTopMipSurfaces)/735 UNRESOLVED_TOPMIP=$($r.UnresolvedTopMipSurfaces)"
Write-Host "[AZTEC-76B] exactMaterialSites=$(@($r.MaterialRelocationSites).Count)/735 uniqueXboxMaterialNames=$($r.DistinctNamedXboxMaterialRoots)"
Write-Host "[AZTEC-76B] SHA256=$($r.BinarySha256)"
Write-Host "[AZTEC-76B] report=$report"
Write-Host '[AZTEC-76B] NOT FOR XBOX; no GfxWorld root/DPVS or FF. Gate 7/8/9 remain PENDING.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
