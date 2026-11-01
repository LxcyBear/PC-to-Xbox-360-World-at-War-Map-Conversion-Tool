[CmdletBinding()]
param([switch]$NamesOnly)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Real Xbox XAnim endian/quaternion/key proof from ORIGINAL PC FF inputs.
# No map FF, audio, Xbox donor payload, OAT rebuild, or console operation.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$native=Join-Path $base 'native_material_mapping'
$baseline=Join-Path $base 'second_map_baseline.json'
$inventory=Join-Path $native 'pc_fastfile_image_sources\pc_fastfile_image_sources.json'
$identity=Join-Path $native 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$sound=Join-Path $native 'pc_g11_lightning_source_body_batch\aztec_pc_typed_sound_and_source_headers.json'
$out=Join-Path $native 'pc_g11_lightning_xanim_614_native_proof'
$json=Join-Path $out 'aztec_614_pc_xanim_native_conversion_and_name_diff.json'
$md=Join-Path $out 'aztec_614_pc_xanim_native_conversion_and_name_diff.md'
$log=Join-Path $out 'aztec_614_xanim_source_native_console.log'
$project=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
foreach($f in @($baseline,$inventory,$identity,$sound,$project)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){throw "Missing Aztec original-PC XAnim source: $f"}
}
$b=Get-Content -LiteralPath $sound -Raw|ConvertFrom-Json
if([string]$b.schema -ne 'WaW360.AztecOriginalPcPhysicalBodiesQuickBatch.v1' -or
   [string]$b.status -ne 'AZTEC_PC_XASSET_POINTER_AND_SOUND_ALIAS_STRUCTURE_PASS_NO_XBOX_AUDIO' -or
   [int]$b.originalPcSourceFastfiles -ne 3 -or
   [int]$b.originalPcFastfilesSha256Verified -ne 3 -or
   [int]$b.sourcePhysicalXanimHeaders -ne 614 -or
   [int]$b.typedPcSoundAliasListsVerified -ne 3774 -or
   [int]$b.sourceSoundTypedFailedRuns -ne 0 -or
   [bool]$b.hardwareReady){
    throw 'Prior 614 PC XAnim physical headers or 3774/3774 typed sound aliases changed.'
}
Write-Host '[AZTEC-XANIM-614] ======================================================'
Write-Host '[AZTEC-XANIM-614] 614 original-PC bodies -> native Xbox 0x5C XAnim proof containers'
Write-Host '[AZTEC-XANIM-614] Compare EXACT PC XAnim names with 602 native Xbox XAnim roots'
Write-Host '[AZTEC-XANIM-614] Source-authentic quaternion, translation, ScriptString and blob readback; no FF.'
Write-Host "[AZTEC-XANIM-614] Names-only fast mode=$([bool]$NamesOnly)"
Write-Host '[AZTEC-XANIM-PROGRESS] [####----------------] 20% | prior SHA-locked source proof'
New-Item -ItemType Directory -Force -Path $out|Out-Null
$argsList=@('run','--no-launch-profile','--configuration','Release','--project',$project,'--',
    'aztec-xanim-614-source-native',$baseline,$inventory,$identity,$sound,$out)
if($NamesOnly){$argsList+='--names-only'}
Write-Host '[AZTEC-XANIM-PROGRESS] [########------------] 40% | typed source candidates and PC->Xenon transformation'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet @argsList 2>&1|ForEach-Object {[string]$_}|Tee-Object -FilePath $log
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf) -or
   -not(Test-Path -LiteralPath $md -PathType Leaf)){
    if(Test-Path -LiteralPath $log -PathType Leaf){
        Get-Content -LiteralPath $log -Tail 45|ForEach-Object {Write-Host "[AZTEC-XANIM-614] $_"}
    }
    throw "Aztec original PC XAnim source/native proof failed exit=$code. No Xbox FF modified."
}
$r=Get-Content -LiteralPath $json -Raw|ConvertFrom-Json
$states=@(
    'AZTEC_XANIM_PC_NAME_DIFFERENTIAL_ONLY_NO_CONVERSION',
    'AZTEC_614_PC_XANIM_TO_XENON_BLOBS_READBACK_PASS_ALL_602_NATIVE_NAMES_MATCH_NOT_IN_FF',
    'AZTEC_614_PC_XANIM_TO_XENON_BLOBS_READBACK_PASS_NATIVE_IDENTITY_GAPS',
    'AZTEC_PC_XANIM_TO_XENON_TRANSFORM_PARTIAL'
)
if([string]$r.schema -ne 'WaW360.AztecOriginalPcXAnimNativeTransform614.v1' -or
   [string]$r.status -notin $states -or
   [int]$r.originalPcFastfiles -ne 3 -or
   [int]$r.originalPcPhysicalXAnimHeaders -ne 614 -or
   [int]$r.nativeXboxXAnimRootCount -ne 602 -or
   [int]$r.priorOriginalPcSoundAliasListsVerified -ne 3774 -or
   [bool]$r.integratedInto8509NativeXboxRootFastFile -or
   -not [bool]$r.internalProofContainersOnly -or
   [bool]$r.hardwareReady -or [bool]$r.fullGate11Passed){
    throw 'Source XAnim proof report unexpectedly changed release/integration readiness.'
}
Write-Host '[AZTEC-XANIM-PROGRESS] [################----] 80% | native names, duplicate PC anims and readbacks classified'
Write-Host "[AZTEC-XANIM-614] PC physical=614, unique names=$($r.originalPcDistinctXAnimNames), duplicate occurrences=$($r.originalPcDuplicateXAnimBodyOccurrences)"
Write-Host "[AZTEC-XANIM-614] Xbox roots=602, exact same-name=$($r.exactPcToXboxXAnimNameMatches), PC-only=$(@($r.pcOnlyXAnimNames).Count), Xbox-only=$(@($r.nativeOnlyXAnimNames).Count)"
Write-Host "[AZTEC-XANIM-614] Actual native Xbox 12-bucket XAnim body transformations: $($r.convertedXboxXAnimBodiesReadbackVerified)/614"
Write-Host "[AZTEC-XANIM-614] Verified transformed internal Xbox XAnim bytes=$($r.nativeXboxSerializedAnimationBodyBytes)"
if(@($r.failures).Count -gt 0){
    foreach($issue in @($r.failures|Select-Object -First 8)){Write-Host "[AZTEC-XANIM-614] BLOCKER $issue"}
}
Write-Host '[AZTEC-XANIM-PROGRESS] [####################] 100% | PC->native proof report ready'
Write-Host "[AZTEC-XANIM-614] STATUS=$($r.status)"
Write-Host "[AZTEC-XANIM-614] Report: $md"
Write-Host "[AZTEC-XANIM-614] JSON: $json"
Write-Host '[AZTEC-XANIM-614] Standalone PC-source Xbox-layout proof only; no actual native FF graft/console runtime.'
