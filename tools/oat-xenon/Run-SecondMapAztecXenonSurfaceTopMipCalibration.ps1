[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$native=Join-Path $base 'xenon_native_gfxworld_76_byte_surface_abi\native_xenon_gfxworld_surface_abi_reconciliation.json'
$fixups=Join-Path $base 'pc_verified_xbox_surface_material_relocation_worklist\aztec_verified_surface_material_relocation_worklist.json'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_to_xenon_surface_topmip_calibration'
$json=Join-Path $out 'aztec_pc_to_xenon_735_surface_topmip_calibration.json'
$build=Join-Path $out 'build.log'
$run=Join-Path $out 'probe.log'

foreach($p in @($native,$fixups,$geometry,$cli)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Aztec Xbox 76-byte topMip calibration prerequisite missing: $p"}
}
$n=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $native -Raw)
$f=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $fixups -Raw)
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometry -Raw)
if([string]$n.status -ne 'AZTEC_NATIVE_XENON_76_BYTE_GFXSURFACE_ABI_LOADED_SOURCE_WITNESS' -or
   [int]$n.nativeGfxSurfaceStride -ne 76 -or
   [int]$n.nativeGfxSurfaceMaterialFieldOffset -ne 44 -or
   [int]$n.nativeUniqueDirectMaterialMatches -ne 735 -or
   [int]$n.surfaceMaterialNamesMatchingByOrdinal -ne 735 -or
   [int]$n.surfaceMaterialMemorySlotsMatchingByOrdinal -ne 735 -or
   [bool]$n.hardwareReady -or
   [string]$f.Status -ne 'AZTEC_PC_735_TYPED_MATERIALS_VERIFIED_XBOX_ROOT_RELOCATION_WORKLIST_ONLY' -or
   [int]$f.VerifiedTypedPcSurfaceIdentities -ne 735 -or
   [int]$f.XenonSurfaceMaterialPointersWritten -ne 0 -or
   [string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [bool]$g.XboxHardwareReady){
    throw 'Requires independently loaded native Xbox GfxWorld ABI and untouched 735 typed PC material evidence.'
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
Write-Host '[AZTEC-TOPMIP] ==============================================================='
Write-Host '[AZTEC-TOPMIP] 735 PC GFXSURFACES vs NATIVE XBOX topMipMins/Maxs SOURCE PROOF'
Write-Host '[AZTEC-TOPMIP] Checks all native/PC firstVertex, vertexCount, baseIndex, layer and bounds.'
Write-Host '[AZTEC-TOPMIP] Tests exact PC bounds, PC vertex-span AABB and triangle-only AABB.'
Write-Host '[AZTEC-TOPMIP] NO invented topMip, NO 76B output yet, NO material pointer patches, NO FF.'
$prior=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $build -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$prior}
if($exit -ne 0){
    $errors=@(Select-String -LiteralPath $build -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 16)
    if($errors.Count -gt 0){foreach($item in $errors){Write-Host "[AZTEC-TOPMIP] BUILD ERROR: $($item.Line)"}}
    else{Get-Content -LiteralPath $build -Tail 20 | ForEach-Object {Write-Host "[AZTEC-TOPMIP] BUILD: $_"}}
    throw "Xbox topMip source .NET build failed. See $build"
}
Write-Host '[AZTEC-TOPMIP] Incremental .NET build PASS'
$prior=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $argsList=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-xenon-surface-topmip-calibration',
        $native,$fixups,$geometry,$json)
    & dotnet @argsList 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $run -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$prior}
if($exit -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $run -PathType Leaf){
        Get-Content -LiteralPath $run -TotalCount 14 | ForEach-Object {Write-Host "[AZTEC-TOPMIP] TRACE: $_"}
        Get-Content -LiteralPath $run -Tail 10 | ForEach-Object {Write-Host "[AZTEC-TOPMIP] TRACE: $_"}
    }
    throw "Aztec Xbox topMip source audit failed exit=$exit; log=$run"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
if([string]$r.Schema -ne 'WaW360.AztecPcXenonSurfaceTopMipCalibration.v1' -or
   [string]$r.Status -ne 'AZTEC_PC_VS_NATIVE_XENON_735_SURFACE_TOPMIP_SOURCE_HYPOTHESIS_AUDIT' -or
   [int]$r.SurfacesCompared -ne 735 -or
   [int]$r.NativeXenonSurfaceStride -ne 76 -or
   [int]$r.OriginalPcSurfaceStride -ne 52 -or
   [int]$r.NativeMaterialFieldAt -ne 44 -or
   [bool]$r.XenonGfxSurfaceMaterialPointersWritten -or
   [bool]$r.XboxHardwareReady){
    throw 'TopMip calibration violated original-PC/native ABI and zero-FF safety gates.'
}
Write-Host "[AZTEC-TOPMIP] PC/native firstVertex matches=$($r.FirstVertexExactMatches)/735 vertexCount=$($r.VertexCountExactMatches)/735 baseIndex=$($r.BaseIndexExactMatches)/735 vertexLayerData=$($r.VertexLayerDataExactMatches)/735"
Write-Host "[AZTEC-TOPMIP] PC/native bounds EXACT=$($r.BoundsFloat32ExactMatches)/735 mismatchedGeometry=$($r.NativeGeometryFieldDisagreements)"
Write-Host "[AZTEC-TOPMIP] native finite topMip=$($r.FiniteNativeTopMipSurfaces)/735"
Write-Host "[AZTEC-TOPMIP] topMip EXACT: sourceBounds=$($r.NativeTopMipEqualsPcSurfaceBoundsExact)/735 vertexSpanAabb=$($r.NativeTopMipEqualsPcVertexSpanAabbExact)/735 triangleOnlyAabb=$($r.NativeTopMipEqualsPcTriangleVertexAabbExact)/735"
Write-Host "[AZTEC-TOPMIP] topMip CLOSE: sourceBounds=$($r.NativeTopMipCloseToPcSurfaceBounds)/735 vertexSpanAabb=$($r.NativeTopMipCloseToPcVertexSpanAabb)/735 triangleOnlyAabb=$($r.NativeTopMipCloseToPcTriangleVertexAabb)/735"
Write-Host "[AZTEC-TOPMIP] completeSourceDerivation=$($r.CompleteVerifiedTopMipDerivation) 76BSourceProofReady=$($r.SourceOnlyTopMipCandidateReadyFor76ByteSerialization)"
foreach($sample in @($r.Samples | Select-Object -First 4)){
    Write-Host "[AZTEC-TOPMIP] sample#$($sample.Surface) nativeMin=$($sample.NativeTopMipMins -join ',') nativeMax=$($sample.NativeTopMipMaxs -join ',') pcBoundsMin=$($sample.PcSurfaceBoundsMins -join ',') pcBoundsMax=$($sample.PcSurfaceBoundsMaxs -join ',')"
}
Write-Host '[AZTEC-TOPMIP] ================= RESULT ==================================='
Write-Host "[AZTEC-TOPMIP] status=$($r.Status)"
Write-Host "[AZTEC-TOPMIP] report=$json"
Write-Host '[AZTEC-TOPMIP] A complete exact algorithm is needed BEFORE serializing 76B Xbox surfaces. Gate 7 PENDING.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
