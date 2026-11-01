[CmdletBinding()]
param([switch]$BuildDumper)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$nativeManifest=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$pcProof=Join-Path $base 'pc_oat_loaded_gfxworld_material_pointer_witness\pc_oat_gfxworld_material_pointer_reconciliation.json'
$fixups=Join-Path $base 'pc_verified_xbox_surface_material_relocation_worklist\aztec_verified_surface_material_relocation_worklist.json'
$dumper=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$capFile=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$out=Join-Path $base 'xenon_native_gfxworld_76_byte_surface_abi'
$log=Join-Path $out 'native_oat_surface_abi.log'
$json=Join-Path $out 'native_xenon_gfxworld_surface_abi_reconciliation.json'
$dumpRoot=Join-Path ([System.IO.Path]::GetTempPath()) ('WaW360_XenonAbi_' + [guid]::NewGuid().ToString('N').Substring(0,12))
foreach($p in @($nativeManifest,$pcProof,$fixups)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing source/native material ABI prerequisite: $p"}
}
$n=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $nativeManifest -Raw)
$w=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $pcProof -Raw)
$f=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $fixups -Raw)
if([string]$n.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
   [int]$n.assets -ne 8509 -or
   -not [bool]$n.typeSequenceVerified -or
   [string]$w.status -ne 'AZTEC_PC_OAT_LOADER_735_TYPED_MATERIAL_POINTER_IDENTITIES_MATCH_ORDINAL_CANDIDATES' -or
   [int]$w.loadedDirectPointerMatches -ne 735 -or
   [int]$w.independentlyVerifiedOrdinalAndNameSurfaces -ne 735 -or
   [bool]$w.xboxHardwareReady -or
   [string]$f.Status -ne 'AZTEC_PC_735_TYPED_MATERIALS_VERIFIED_XBOX_ROOT_RELOCATION_WORKLIST_ONLY' -or
   [int]$f.ExactRelocationSites -ne 735 -or
   [int]$f.XenonSurfaceMaterialPointersWritten -ne 0 -or
   [bool]$f.XboxHardwareReady){
    throw 'Requires verified 8509-root golden Xbox identity, independent 735 PC typed pointers and no FF material patch.'
}
$native=[string]$n.nativeFastFile
if(-not(Test-Path -LiteralPath $native -PathType Leaf) -or
   (Get-Item -LiteralPath $native).Length -ne 82739469 -or
   (Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash -ine
       'E0F3C244DD7B72D0FE71C05FAF014C2C9BFB05F770183954B3E7CDE625EBA98F'){
    throw 'Golden Xbox native Aztec reference FF size/SHA changed; no native witness permitted.'
}

$capability='t4xenon-gfxworld-surface-abi-loaded-pointer-witness-v1'
function Test-Installed{
    if(-not(Test-Path -LiteralPath $dumper -PathType Leaf) -or
       -not(Test-Path -LiteralPath $capFile -PathType Leaf)){return $false}
    $c=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $capFile -Raw)
    return ([string]$c.format -eq 'WaW360.OatGraphDumperCapabilities.v1' -and
        [string]$c.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($c.capabilities) -contains $capability)
}
if(-not(Test-Installed) -and $BuildDumper){
    Write-Host '[AZTEC-XENON-ABI] Missing native Xenon GfxWorld capability. One-time OAT graph-dumper rebuild.'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1')
    if($LASTEXITCODE -ne 0 -or -not $?){throw 'OAT ABI witness native build failed.'}
}
if(-not(Test-Installed)){
    throw "Specialized OAT Unlinker missing $capability. Run the same script with -BuildDumper ONCE."
}
$worst=Join-Path (Join-Path $dumpRoot 'xenon_gfxworld_surface_abi_witness') (('A'*96)+'.json')
if($worst.Length -ge 240){throw "Native OAT output path too long ($($worst.Length)). Shorten TEMP."}
New-Item -ItemType Directory -Force -Path $dumpRoot | Out-Null
New-Item -ItemType Directory -Force -Path $out | Out-Null

Write-Host '[AZTEC-XENON-ABI] ==========================================================='
Write-Host '[AZTEC-XENON-ABI] INDEPENDENT ORIGINAL XBOX AZTEC GFXWORLD 76-BYTE ABI WITNESS'
Write-Host '[AZTEC-XENON-ABI] Source PC: 735 typed material identities already proven'
Write-Host '[AZTEC-XENON-ABI] Native Xbox: inspect sizeof/offsetof, topMip floats, 39 material owners'
Write-Host "[AZTEC-XENON-ABI] nativeOutput=$dumpRoot worstCaseLength=$($worst.Length)"
Write-Host '[AZTEC-XENON-ABI] Source-only read; no generated FF and no surface modification.'

$captured=New-Object 'System.Collections.Generic.List[string]'
$exit=$null
$invocationError=$null
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $global:LASTEXITCODE=$null
    try{
        & $dumper '--no-color' '--include-assets' 'gfxworld' '--output-folder' $dumpRoot $native 2>&1 |
            ForEach-Object {$captured.Add([string]$_)}
        if($null -ne $LASTEXITCODE){$exit=[int]$LASTEXITCODE}
    }catch{
        $invocationError=[string]$_
        $captured.Add("Native invocation error: $invocationError")
    }
}finally{$ErrorActionPreference=$saved}
if($null -eq $exit){$exit=-1}
$hex='0x{0:X8}' -f ([long]$exit -band 4294967295L)
$logLines=@("ExitCodeDecimal=$exit","ExitCodeHex=$hex","SourceNativeFF=$native",
    "OutputDirectory=$dumpRoot","Lines=$($captured.Count)")
$logLines+=@($captured)
if($captured.Count -eq 0){$logLines+='[NO STDOUT/STDERR FROM OAT]'}
[IO.File]::WriteAllLines($log,[string[]]$logLines,[Text.UTF8Encoding]::new($false))
Write-Host "[AZTEC-XENON-ABI] OAT exit=$exit ($hex) outputLines=$($captured.Count)"
if($exit -ne 0){
    Get-Content -LiteralPath $log -TotalCount 18 | ForEach-Object {Write-Host "[AZTEC-XENON-ABI] TRACE $_"}
    throw "Native GfxWorld ABI witness failed. See $log"
}
$witnessDir=Join-Path $dumpRoot 'xenon_gfxworld_surface_abi_witness'
$files=@(Get-ChildItem -LiteralPath $witnessDir -Filter '*.json' -File -ErrorAction SilentlyContinue)
if($files.Count -ne 1){
    throw "Native OAT exited 0 but produced $($files.Count) GfxWorld ABI witnesses, expected 1 in $witnessDir"
}
$a=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $files[0].FullName -Raw)
if([string]$a.schema -ne 'WaW360.T4XenonGfxWorldSurfaceAbiWitness.v1' -or
   [string]$a.status -ne 'NATIVE_XENON_GFXWORLD_SURFACE_ABI_LOADER_WITNESS' -or
   [string]$a.platform -ne 'T4Xenon' -or
   [int]$a.abi.gfxSurfaceSize -ne 76 -or
   [int]$a.abi.trianglesSize -ne 40 -or
   [int]$a.abi.worldVertexSize -ne 44 -or
   [int]$a.abi.topMipMinAt -ne 16 -or
   [int]$a.abi.topMipMaxAt -ne 28 -or
   [int]$a.abi.materialFieldAt -ne 44 -or
   [int]$a.abi.boundsAt -ne 52 -or
   -not [bool]$a.loadedArraysAvailable){
    throw "Native Xbox OAT loaded world disagrees with pinned 76B ABI or fields; see $($files[0].FullName)"
}
$nativeCount=[int]$a.surfaceCount
$nativeMemCount=[int]$a.materialMemoryCount
if(@($a.surfaces).Count -ne $nativeCount -or
   @($a.materialMemory).Count -ne $nativeMemCount){
    throw 'Native loaded GfxWorld arrays disagree with reported ABI counts.'
}
$nativeMatchesPcShape=$nativeCount -eq 735 -and $nativeMemCount -eq 39 -and
    [int]$a.vertexCount -eq 32713 -and [int]$a.indexCount -eq 58653
$sameGeometryCount=0
$sameMaterialNameCount=0
$sameOrdinalCount=0
$rawPc=Join-Path $base 'pc_735_surface_material_candidate_ledger\aztec_pc_735_surface_material_candidate_ledger.json'
$pc=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $rawPc -Raw)
for($i=0; $i -lt [Math]::Min(735,$nativeCount); $i++){
    $x=$a.surfaces[$i];$p=$pc.SurfaceCandidateRows[$i]
    if([int]$x.triangleCount -eq [int]$p.Triangles){$sameGeometryCount++}
    if([string]$x.loadedMaterialName -ceq [string]$p.ProposedPcMaterialName){$sameMaterialNameCount++}
    if([int]$x.materialMemoryOrdinal -eq [int]$p.OrdinalSlotHypothesis){$sameOrdinalCount++}
}
$report=[ordered]@{
    schema='WaW360.AztecNativeXenonGfxWorldSurfaceAbiReconciliation.v1'
    status='AZTEC_NATIVE_XENON_76_BYTE_GFXSURFACE_ABI_LOADED_SOURCE_WITNESS'
    originalNativeFastFile=$native
    originalNativeSha256='E0F3C244DD7B72D0FE71C05FAF014C2C9BFB05F770183954B3E7CDE625EBA98F'
    rawNativeOatWitness=$files[0].FullName
    sourcePcPointerReconciliation=$pcProof
    existing735RelocationWorklist=$fixups
    nativeGfxSurfaceStride=76
    nativeGfxSurfaceMaterialFieldOffset=44
    nativeGfxSurfaceTopMipFloatStart=16
    nativeGfxSurfaceTopMipFloatEndExclusive=40
    nativeWorldVertices=[int]$a.vertexCount
    nativeWorldIndices=[int]$a.indexCount
    nativeWorldSurfaces=$nativeCount
    nativeMaterialMemoryEntries=$nativeMemCount
    nativeUniqueDirectMaterialMatches=[int]$a.directMatches
    nativeUnmatchedMaterialPointers=[int]$a.unmatched
    nativeAmbiguousMaterialPointers=[int]$a.ambiguous
    nativeGeometryAndMaterialCountsSameAsPc=$nativeMatchesPcShape
    surfaceTriangleCountsMatchingByOrdinal=$sameGeometryCount
    surfaceMaterialNamesMatchingByOrdinal=$sameMaterialNameCount
    surfaceMaterialMemorySlotsMatchingByOrdinal=$sameOrdinalCount
    sourcePcTypedMaterialBindingsProven=735
    convertedXenonSurfaceMaterialFieldsWritten=0
    completeXenonTopMipDerivationProven=$false
    hardwareReady=$false
    note='Native original-Xbox OAT-loaded GfxWorld ABI calibration only. The previous 52B BE surfaces are endian-readback of a PC layout, NOT a 76B Xenon GfxSurface. Native topMip float values are observed but their derivation from PC geometry is not yet proven. No FF modified.'
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $json -Encoding UTF8
Write-Host "[AZTEC-XENON-ABI] Native surface stride=$($report.nativeGfxSurfaceStride) materialAt=+$($report.nativeGfxSurfaceMaterialFieldOffset) topMipBytes=+16..+39"
Write-Host "[AZTEC-XENON-ABI] nativeSurfaces=$nativeCount nativeMaterialMemory=$nativeMemCount nativeVerts=$($a.vertexCount) nativeIndices=$($a.indexCount)"
Write-Host "[AZTEC-XENON-ABI] nativeDirectMaterialMatches=$($a.directMatches) unmatched=$($a.unmatched) ambiguous=$($a.ambiguous)"
Write-Host "[AZTEC-XENON-ABI] PC vs native same source shape=$nativeMatchesPcShape triOrdinalMatches=$sameGeometryCount nameOrdinalMatches=$sameMaterialNameCount pointerOrdinalMatches=$sameOrdinalCount"
Write-Host "[AZTEC-XENON-ABI] report=$json"
Write-Host '[AZTEC-XENON-ABI] Xbox 76B layout required; no material pointers patched; Gate 7 pending.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
