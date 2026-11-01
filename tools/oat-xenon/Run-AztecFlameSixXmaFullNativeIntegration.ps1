[CmdletBinding()]
param([switch]$BuildDumper)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$meta=Join-Path $base 'pc_g11_flame_real_xbox_xma_source\aztec_flame_six_genuine_xbox_sound_184b_loaded_160b_ff_metadata_v6.json'
$work=Join-Path $repo '.deps\aztec-flame-xma-ff'
$ff=Join-Path $work 'scratch_emit\nazi_zombie_aztec.ff'
$emit=Join-Path $work 'scratch_emit\nuclear_xenon_renderer_emit.json'
$zone=Join-Path $work 'scratch_emit\nuclear_xenon_renderer.zone'
$report=Join-Path $work 'six_original_xbox_sound_loaded_xma_native_ff_integration.json'
$oldGood=Join-Path $repo '.deps\aztec-w63-direct-ff-growth\scratch_emit\nazi_zombie_aztec.ff'
$oldProof=Join-Path $repo '.deps\aztec-w63-direct-ff-growth\aztec_weapon63_ff_growth_63.json'
foreach($f in @($oldGood,$oldProof)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){throw "Last genuine original-PC sound canary accepted FF/proof missing: $f"}
}
$oldHash=(Get-FileHash -LiteralPath $oldGood -Algorithm SHA256).Hash
$p=Get-Content -LiteralPath $oldProof -Raw|ConvertFrom-Json
if([string]$p.newScratchFastFileSha256 -ine $oldHash){
    throw 'Last passing genuine original-PC Weapon63 FF source changed.'
}
Write-Host '[AZTEC-SIX-XMA-FF] ===================================================='
Write-Host '[AZTEC-SIX-XMA-FF] ACTUAL SIX RETAIL XBOX 360 XMA SOUND METADATA + ENCODED PACKET NATIVE FF INTEGRATION'
Write-Host '[AZTEC-SIX-XMA-FF] Original PC owned Gate11 world + 63 WeaponDef inline/source proof + 160 direct aliases + six real Xbox sound roots.'
Write-Host '[AZTEC-SIX-XMA-FF] Native 8509 root identities held fixed. OAT decides native schema acceptance; no fake XMA/aliases.'
Write-Host '[AZTEC-SIX-XMA-FF-PROGRESS] [##------------------] 10% | original 63 WeaponDef source FF preserved'
$cap=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$have=$false
if(Test-Path -LiteralPath $cap -PathType Leaf){
    $c=Get-Content -LiteralPath $cap -Raw|ConvertFrom-Json
    $have=@($c.capabilities) -contains 't4xenon-flame-sound-alias-184b-loaded-160b-original-retail-metadata-v6'
}
if(-not $have -and -not $BuildDumper){
    throw 'One new original-Xbox audio metadata OAT build required. Run this same command with -BuildDumper once.'
}
$prep=@('-NoProfile','-ExecutionPolicy','Bypass','-File',
    (Join-Path $PSScriptRoot 'Run-AztecFlameNativeAudioMetadataCapture.ps1'))
if(-not $have){$prep+=@('-BuildDumper')}
& powershell @prep
if($LASTEXITCODE -ne 0){throw 'Actual original Xbox six-Sound metadata capture/XMA source validation failed.'}
$m=Get-Content -LiteralPath $meta -Raw|ConvertFrom-Json
if([string]$m.schema -ne 'WaW360.AztecOriginalXboxFlameSixSoundAliasLoadedMetadata.v6' -or
   @($m.exactOriginalXboxSoundAliases).Count -ne 6 -or
   [int]$m.verifiedActualOriginalXboxLoadedSoundMetadataCount -ne 6){
    throw 'Original Xbox source six-alias metadata not complete.'
}
Write-Host '[AZTEC-SIX-XMA-FF-PROGRESS] [######--------------] 30% | SIX owned Xbox alias 184B + LoadedSound 160B metadata and actual XMA stream payloads ready'
# Single actual isolated native FF build reuses ALL Gate11/weapon
# data and opts in six source Xbox Sound + LoadedSound graph bodies.
$runner=Join-Path $PSScriptRoot 'Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1'
$flags=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$runner,
    '-IntegrateWorld','-ProbeLevel','Full','-DpvsStaticProbe',
    '-Gate8ChildProbe','-Gate8ChildLevel','All','-Gate8AllModelProbe',
    '-Gate10StaticDrawProbe','-Gate11CellProbe','-Gate11PrimitiveProbe',
    '-Gate11LightingProbe','-Gate11ImageHeadersProbe','-Gate11ImageRootLinksProbe',
    '-Weapon63SourceCanary','-WeaponCanaryLimit','63',
    '-Weapon63ExtendedInlineStrings',
    '-Weapon63OwnedAccuracyVec2',
    '-Weapon63OwnedBounceSound',
    '-Weapon63OwnedDirectSoundAliases',
    '-FlameSound6NativeGraft')
Write-Host '[AZTEC-SIX-XMA-FF-PROGRESS] [########------------] 40% | Stage4D layout + Stage4E genuine 6 Sound/LoadedSound/XMA packet emission + OAT'
& powershell @flags
$code=[int]$LASTEXITCODE
if($code -ne 0){throw "New standalone native Sound+XMA FF/OAT experiment BLOCKED (exit=$code). Inspect $work\integrated_emitter.log and build.log; previous FF untouched."}
if(-not(Test-Path -LiteralPath $emit -PathType Leaf) -or
   -not(Test-Path -LiteralPath $ff -PathType Leaf) -or
   -not(Test-Path -LiteralPath $zone -PathType Leaf)){
    throw 'No complete original Xbox six-audio FF/zone/OAT proof generated.'
}
$e=Get-Content -LiteralPath $emit -Raw|ConvertFrom-Json
if([int]$e.NativeRoots -ne 8509 -or
   [int]$e.OriginalXboxFlameNativeSoundRoots -ne 6 -or
   ([int]$e.OriginalXboxFlameNativeLoadedRoots +
      [int]$e.OriginalXboxFlameNativeInlineLoadedBodies) -ne 6 -or
   [long]$e.OriginalXboxFlameNativeXmaPacketBytes -le 0 -or
   [int]$e.OriginalXboxFlameNativeBitExactXmaPacketsReadBack -ne 6 -or
   -not [bool]$e.LayoutReplayExact -or
   -not [bool]$e.ValidationPass -or
   -not [bool]$e.ExactIdentitySequence -or
   -not [bool]$e.OatZeroWarningsZeroErrors){
    throw "Real Xbox six-Sound/XMA Stage4D/4E/OAT independent native gate rejected: $emit"
}
$sha=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
if((Get-FileHash -LiteralPath $oldGood -Algorithm SHA256).Hash -ine $oldHash){
    throw 'Last passing verified 63-WeaponDef source Xbox FF changed during new sound FF test.'
}
$body=[ordered]@{
    schema='WaW360.AztecSixRealOriginalXboxSoundNativeFFGraft.v1'
    status='SIX_SOURCE_AUTHENTIC_XBOX_SOUND_AND_LOADED_XMA_ROOT_GRAPHS_EMITTED_OAT_ACCEPTED_NOT_YET_RUNTIME_PLAYBACK'
    previousGoodNativeWeaponFf=$oldGood
    previousGoodNativeWeaponFfSha256=$oldHash
    newSourceAudioNativeFF=$ff
    newSourceAudioNativeFFSha256=$sha
    newSourceAudioNativeFFBytes=[long](Get-Item -LiteralPath $ff).Length
    originalXboxFlameSoundRootsInFF=[int]$e.OriginalXboxFlameNativeSoundRoots
    originalXboxFlameOwnedTopLevelLoadedRoots=[int]$e.OriginalXboxFlameNativeLoadedRoots
    originalXboxFlameInlineOwnedLoadedBodies=[int]$e.OriginalXboxFlameNativeInlineLoadedBodies
    actualOriginalXboxXmaEncodedPacketBytes=[long]$e.OriginalXboxFlameNativeXmaPacketBytes
    actualOriginalXboxXmaPacketBuffersReadBackBitExact=[int]$e.OriginalXboxFlameNativeBitExactXmaPacketsReadBack
    sourceOriginalXboxSoundMetadataProof=$meta
    nativeStage4d4eExactLayout=[bool]$e.LayoutReplayExact
    oatLoadedAll8509RootIdentitiesExact=[bool]$e.ExactIdentitySequence
    oatZeroWarningsZeroErrors=[bool]$e.OatZeroWarningsZeroErrors
    noFakeSampleBytes=$true
    weapon63FullGraphGrafted=$false
    fullMapAudioAll5306AliasesFinished=$false
    xboxHardwareReady=$false
}
$body|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $report -Encoding UTF8
Write-Host '[AZTEC-SIX-XMA-FF-PROGRESS] [####################] 100% | genuine Xbox 6 Sound + XMA embedded and native OAT accepted'
Write-Host "[AZTEC-SIX-XMA-FF] 6 source Xbox Sound roots + $($body.originalXboxFlameOwnedTopLevelLoadedRoots) top-level LoadedSound + $($body.originalXboxFlameInlineOwnedLoadedBodies) inline LoadedSound; exact source XMA packet bytes=$($body.actualOriginalXboxXmaEncodedPacketBytes)"
Write-Host "[AZTEC-SIX-XMA-FF] FfSize=$($body.newSourceAudioNativeFFBytes) SHA=$sha OAT 0/0=$($body.oatZeroWarningsZeroErrors)"
Write-Host "[AZTEC-SIX-XMA-FF] FF=$ff"
Write-Host '[AZTEC-SIX-XMA-FF] Source and prior known-good FF untouched; still requires Xbox runtime/audio test and broad map sound closure.'
