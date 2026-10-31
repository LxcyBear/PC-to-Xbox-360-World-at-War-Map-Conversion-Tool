[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Read-only, exact-SHA proof of 82MB retail Xbox FF vs 4.51MB PC-sourced FF.
# Block capacities are NOT per-type payload bytes. No donor or console run.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$nativeProof=Join-Path $base 'native_graph_proof\native_graph_proof_result.json'
$identity=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$proof=Join-Path $base 'pc_g11_image_root_links_probe\oat_loaded_six_image_root_links\aztec_gate11_independent_six_native_name_root_links.json'
$manifest=Join-Path $base 'pc_g11_image_root_links_probe\aztec_pc_world_core_integrated_oat_run.json'
$project=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_g11_native_ff_size_audit'
$json=Join-Path $out 'aztec_native_ff_size_and_source_ledger.json'
$md=Join-Path $out 'aztec_native_ff_size_and_source_ledger.md'
$log=Join-Path $out 'aztec_native_ff_size_audit_cli.log'
foreach($file in @($nativeProof,$identity,$proof,$manifest,$project)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing native size audit input: $file"}
}
$n=Get-Content -LiteralPath $nativeProof -Raw|ConvertFrom-Json
$p=Get-Content -LiteralPath $proof -Raw|ConvertFrom-Json
$m=Get-Content -LiteralPath $manifest -Raw|ConvertFrom-Json
$retail=[string]$n.verifiedGoldenXboxFastFile
$candidate=[string]$p.newNativeFf
$emitter=[string]$m.emitterReport
$rsha='E0F3C244DD7B72D0FE71C05FAF014C2C9BFB05F770183954B3E7CDE625EBA98F'
$csha='F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6'
Write-Host '[AZTEC-SIZE] ============================================================'
Write-Host '[AZTEC-SIZE] READ-ONLY original Xbox and independently OAT-passing PC map zone audit'
Write-Host '[AZTEC-SIZE-PROGRESS] [####----------------] 20% | source proof and original FF SHA-256'

if([string]$n.status -ne 'IDENTITY_AND_TYPED_GRAPH_IMPORT_PASS' -or
   [string]$n.nativeSha256 -ine $rsha -or
   [string]$p.schema -ne 'WaW360.AztecGate11SixImageRootNameAliasReadback.v1' -or
   [string]$p.status -ne 'AZTEC_GATE11_SIX_GFXIMAGE_NAME_ONLY_ROOT_POINTERS_NATIVE_OAT_PASS_PIXEL_DATA_MISSING' -or
   [string]$p.newNativeFfSha256 -ine $csha -or
   [long]$p.newNativeFfBytes -ne 4510695L -or
   [int]$p.sourceNamedGfxImagePointersResolved -ne 6 -or
   -not [bool]$p.previous11Cells740Aabb14974IndexesStillMatch -or
   -not [bool]$p.previous4LightGridGroupsAnd1903ShadowRefsStillMatch -or
   [bool]$p.xboxHardwareReady -or
   [string]$m.schema -ne 'WaW360.AztecPcGfxWorldIntegratedFastFileOatRun.v1' -or
   [string]$m.status -ne 'AZTEC_GATE11_SIX_IMAGE_ROOT_NAME_SHELL_LINKS_OAT_PASS_PIXELS_UNPROVEN' -or
   -not [bool]$m.OatPass -or [bool]$m.hardwareReady -or
   [string]$m.outputFastFile -ine $candidate){
    throw 'Current 4510695B FF, native reference, or independent OAT status changed; no report generated.'
}
foreach($file in @($retail,$candidate,$emitter)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing pinned native FF or emitter manifest: $file"}
}
if((Get-FileHash -LiteralPath $retail -Algorithm SHA256).Hash -ine $rsha -or
   (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -ine $csha -or
   [long](Get-Item -LiteralPath $retail).Length -ne 82739469L -or
   [long](Get-Item -LiteralPath $candidate).Length -ne 4510695L){
    throw 'FF file hash or exact verified size changed; refuse misleading size comparison.'
}
New-Item -ItemType Directory -Path $out -Force|Out-Null
Write-Host '[AZTEC-SIZE-PROGRESS] [########------------] 40% | one incremental .NET build, decompress both FFs, exact XFile header and root audit'
$oldPref=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $argList=@('run','--no-launch-profile','--configuration','Release','--project',$project,'--',
        'aztec-native-ff-size-audit',$retail,$candidate,$identity,$emitter,$proof,$out)
    & dotnet @argList 2>&1 | ForEach-Object {[string]$_} | Tee-Object -FilePath $log
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$oldPref}
if($code -ne 0 -or
   -not(Test-Path -LiteralPath $json -PathType Leaf) -or
   -not(Test-Path -LiteralPath $md -PathType Leaf)){
    if(Test-Path -LiteralPath $log -PathType Leaf){
        Get-Content -LiteralPath $log -Tail 35 | ForEach-Object {Write-Host "[AZTEC-SIZE] $_"}
    }
    throw "Read-only Aztec size audit failed exit=$code. Neither original nor current FF modified."
}
$a=Get-Content -LiteralPath $json -Raw|ConvertFrom-Json
if([string]$a.schema -ne 'WaW360.AztecNativeFfSizeAndSourceCoverage.v1' -or
   [string]$a.status -ne 'AZTEC_NATIVE_FF_SIZE_AUDIT_PASS_NOT_HARDWARE_READY' -or
   [long]$a.retail.compressedBytes -ne 82739469L -or
   [long]$a.converted.compressedBytes -ne 4510695L -or
   [int]$a.rootCoverage.total -ne 8509 -or
   [int]$a.rootCoverage.originalPcSourceBodies -ne 185 -or
   [int]$a.rootCoverage.partiallyConvertedGfxWorldRoots -ne 1 -or
   [int]$a.sourceWorld.namedGfxImagePointersResolved -ne 6 -or
   [int]$a.sourceWorld.generatedImagePixelBodiesProven -ne 0 -or
   [bool]$a.hardwareReady -or [bool]$a.gate11Closed -or [bool]$a.retailPayloadCopied){
    throw 'Reported mass/source-root ledger failed independent completion/provenance checks.'
}
if((Get-FileHash -LiteralPath $retail -Algorithm SHA256).Hash -ine $rsha -or
   (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -ine $csha){
    throw 'An input FF changed during size audit.'
}
Write-Host '[AZTEC-SIZE-PROGRESS] [################----] 80% | list highest remaining name shells'
Write-Host "[AZTEC-SIZE] compressed: retail=$($a.retail.compressedBytes) B, candidate=$($a.converted.compressedBytes) B, gap=$($a.delta.compressedBytes) B"
Write-Host "[AZTEC-SIZE] decompressed: retail=$($a.retail.decompressedBytes) B, candidate=$($a.converted.decompressedBytes) B, gap=$($a.delta.decompressedBytes) B"
Write-Host "[AZTEC-SIZE] roots=8509: PC-source=$($a.rootCoverage.originalPcSourceBodies), renderer-replayed=$($a.rootCoverage.nativeRendererGraphReplays), partialWorld=$($a.rootCoverage.partiallyConvertedGfxWorldRoots), shells/unverified=$($a.rootCoverage.nameShellOrUnverifiedRoots)"
foreach($t in @($a.types|Sort-Object nameShellOrUnverified -Descending|Select-Object -First 12)){
    Write-Host "[AZTEC-SIZE] $($t.name) roots=$($t.nativeRoots) PC-source=$($t.originalPcSourceBodies) shell/unverified=$($t.nameShellOrUnverified)"
}
Write-Host '[AZTEC-SIZE-PROGRESS] [####################] 100% | exact JSON + readable Markdown ready'
Write-Host "[AZTEC-SIZE] Report: $md"
Write-Host "[AZTEC-SIZE] JSON: $json"
Write-Host '[AZTEC-SIZE] Block sizes are shared CAPACITIES, not missing bytes per asset type. NO XBOX.'
