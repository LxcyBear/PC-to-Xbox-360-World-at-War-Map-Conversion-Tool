[CmdletBinding()]
param([switch]$BuildDumper)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$out=Join-Path $base 'pc_g11_flame_loaded_virtual_target_proof'
$capFile=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$bin=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$capability='t4xenon-real-flame-sound-to-loaded-xma-chain-v4'
$report=Join-Path $out 'aztec_flame_v4_original_pc_real_material_memory_and_xbox_real_sound_source_sprint.json'
$materialReport=Join-Path $out 'aztec_five_original_pc_real_material_memory_words.json'
$audioReport=Join-Path $base 'pc_g11_flame_real_xbox_xma_source\aztec_six_real_flame_xbox_sound_to_xma_chain_v4.json'
Write-Host '[AZTEC-FLAME-V4] ====================================================='
Write-Host '[AZTEC-FLAME-V4] REAL PC FIVE MATERIAL SOURCE MEMORY TARGETS + XBOX SOUND -> LOADED XMA'
Write-Host '[AZTEC-FLAME-V4] Previous original-PC names=3/3, sound aliases=6/6, retail sound root names=6/6'
Write-Host '[AZTEC-FLAME-V4] No synthetic material, no silent XMA, no original-Xbox donor FF graft.'
$installed=$false
if((Test-Path -LiteralPath $bin -PathType Leaf) -and
   (Test-Path -LiteralPath $capFile -PathType Leaf)){
    $c=Get-Content -LiteralPath $capFile -Raw|ConvertFrom-Json
    $installed=([string]$c.format -eq 'WaW360.OatGraphDumperCapabilities.v1' -and
        [string]$c.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($c.capabilities) -contains $capability)
}
if(-not $installed -and $BuildDumper){
    Write-Host '[AZTEC-FLAME-V4] ONE pinned original Xbox x86 OAT SoundFile -> LoadedSound witness build.'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1') -TraceWeaponNamePointers
    if($LASTEXITCODE -ne 0){throw 'Native Xbox SoundFile->LoadedSound OAT build failed.'}
    if(-not(Test-Path -LiteralPath $capFile -PathType Leaf)){throw 'No v4 Xbox original XMA OAT capability manifest after build.'}
    $c=Get-Content -LiteralPath $capFile -Raw|ConvertFrom-Json
    $installed=([string]$c.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($c.capabilities) -contains $capability)
}
if(-not $installed){throw 'Actual Xbox loaded SoundFile->XMA v4 capability absent. Use -BuildDumper once.'}
Write-Host '[AZTEC-FLAME-V4-PROGRESS] [####----------------] 20% | pinned genuine original Xbox alias chain OAT x86 dumper installed'
Write-Host '[AZTEC-FLAME-V4] Reuse existing SHA-pinned v3 real original-PC 5-material source proof, zero extra PC OAT build.'
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Run-AztecFlameFiveMaterialCellEvidence.ps1')
if($LASTEXITCODE -ne 0){throw 'Original-PC material target 32B memory word evidence blocked.'}
Write-Host '[AZTEC-FLAME-V4-PROGRESS] [########------------] 40% | all five real original-PC Material* target first32 bytes classified without fabricated names'
Write-Host '[AZTEC-FLAME-V4] Read original Xbox Sound/LoadedSound relationship for all six original-PC-proven flamethrower alias names.'
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Run-AztecFlameXboxLoadedSampleChain.ps1')
if($LASTEXITCODE -ne 0){throw 'Xbox original Sound -> SoundFile -> real LoadedSound XMA relationship scan blocked.'}
Write-Host '[AZTEC-FLAME-V4-PROGRESS] [################----] 80% | six loaded Xbox sound alias records and their real source samples mapped'
if(-not(Test-Path -LiteralPath $audioReport -PathType Leaf) -or
   -not(Test-Path -LiteralPath $materialReport -PathType Leaf)){
    throw 'Authentic original PC Material memory or Xbox SoundFile->XMA provenance reports missing.'
}
$m=Get-Content -LiteralPath $materialReport -Raw|ConvertFrom-Json
$a=Get-Content -LiteralPath $audioReport -Raw|ConvertFrom-Json
if([string]$m.schema -ne 'WaW360.AztecFiveRealPcFlameMaterialMemoryWordEvidence.v1' -or
   [int]$m.genuineOriginalPcMaterialPackedReferenceFields -ne 17 -or
   [int]$m.distinctOriginalPcLoadedPackedMaterialTargetValues -ne 5 -or
   [bool]$m.actualXboxFfModified -or
   [string]$a.schema -ne 'WaW360.AztecOriginalXboxSoundFileToXmaChain.v4' -or
   [int]$a.sourceOriginalPcFlameSoundAliasCount -ne 6 -or
   [int]$a.actualXenonSoundRootsTyped -ne 6 -or
   [bool]$a.nativeAztecFFModified -or
   [bool]$a.xboxHardwareReady){
    throw 'Original-PC real material source or Xbox SoundFile/XMA provenance was unexpectedly changed.'
}
$body=[ordered]@{
    schema='WaW360.AztecFlameV4MaterialMemoryAndXboxOriginalXmaSoundChain.v1'
    status='GENUINE_ORIGINAL_PC_5_MATERIAL_SOURCE_BYTES_AND_6_XBOX_SOUND_ALIAS_CHAINS_AUDITED_NO_PRODUCTION_GRAFT'
    originalPCFlameTableParentCount=4
    packedFlameTableNameReferenceOccurrencesRecovered=3
    actualOriginalPcSoundAliasNamesMatchedInXboxRetail=6
    unresolvedRealOriginalPcMaterialReferences=17
    uniquePackedFlameMaterialSourceMemoryTargetsExamined=5
    genuineXboxAliasSourceRoots=6
    originalXboxSoundFileRecordsObserved=[int]$a.nativeXboxSoundAliasRecordsTraced
    actualXboxSourceAliasToSampleFilesExtracted=[int]$a.authenticXboxOatAliasToSampleFilesExtracted
    xboxAliasNamesWithActuallyLinkedSourceSample=[int]$a.aliasesWithActualOriginalXboxLinkedSample
    realXmaRiffHeaderVerifiedSampleCount=[int]$a.riffXmaCodecHeaderProvenSampleCount
    materialSourceMemoryProof=$materialReport
    xboxSoundToSampleSourceProof=$audioReport
    nativeXboxFlameMaterialImageTechniqueGraphGrafted=$false
    nativeXboxRealSoundFileGraphGrafted=$false
    XboxCodXeAudioRuntimeVerified=$false
    oldOatGoodNativeFFChanged=$false
    xboxHardwareReady=$false
    noDummyOrSilentContent=$true
}
$body|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $report -Encoding UTF8
Write-Host "[AZTEC-FLAME-V4] Genuine original Xbox Sound-alias-linked samples=$($body.actualXboxSourceAliasToSampleFilesExtracted); aliases covered=$($body.xboxAliasNamesWithActuallyLinkedSourceSample)/6; RIFF-XMA packet header-verified=$($body.realXmaRiffHeaderVerifiedSampleCount)"
Write-Host '[AZTEC-FLAME-V4] Material cells still need independent original-PC material body identity; no guessed names or Xbox texture/techset graft.'
Write-Host "[AZTEC-FLAME-V4] STATUS=$($body.status)"
Write-Host "[AZTEC-FLAME-V4] JSON=$report"
Write-Host '[AZTEC-FLAME-V4-PROGRESS] [####################] 100% | material bytes & original Xbox loaded audio chain source-only; existing FF protected'
