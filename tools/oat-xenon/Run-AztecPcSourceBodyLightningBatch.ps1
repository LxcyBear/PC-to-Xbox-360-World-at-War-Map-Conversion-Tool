[CmdletBinding()]
param([switch]$FastHeadersOnly)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Independent original-PC source BODY ownership pointer census, optionally
# plus typed real SoundAliasList structure (no Xbox XMA or FF generated).
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$work=Join-Path $base 'native_material_mapping'
$lightning=Join-Path $work 'pc_g11_lightning_all_asset_verification\aztec_lightning_verification_summary.json'
$size=Join-Path $work 'pc_g11_native_ff_size_audit\aztec_native_ff_size_and_source_ledger.json'
$baseline=Join-Path $base 'second_map_baseline.json'
$inventory=Join-Path $work 'pc_fastfile_image_sources\pc_fastfile_image_sources.json'
$project=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $work 'pc_g11_lightning_source_body_batch'
$report=Join-Path $out 'aztec_pc_typed_sound_and_source_headers.json'
$csv=Join-Path $out 'aztec_pc_three_ff_type_pointer_census.csv'
$log=Join-Path $out 'source_body_batch_console.log'
foreach($f in @($lightning,$size,$baseline,$inventory,$project)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){throw "Missing original PC or current native evidence: $f"}
}
$l=Get-Content -LiteralPath $lightning -Raw|ConvertFrom-Json
$s=Get-Content -LiteralPath $size -Raw|ConvertFrom-Json
$b=Get-Content -LiteralPath $baseline -Raw|ConvertFrom-Json
$i=Get-Content -LiteralPath $inventory -Raw|ConvertFrom-Json
if([string]$l.schema -ne 'WaW360.AztecLightningAllRootSourceIdentityTriage.v1' -or
   [int]$l.nativeRootsEnumerated -ne 8509 -or
   [int]$l.exactPcOatTypeAndNameMatches -ne 6476 -or
   [int]$l.priorShellOrUnverifiedRootCount -ne 7817 -or
   [string]$l.nativeOatPassingCandidateSha256 -ine 'F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6' -or
   [string]$s.schema -ne 'WaW360.AztecNativeFfSizeAndSourceCoverage.v1' -or
   [int]$s.rootCoverage.total -ne 8509 -or
   [string]$b.format -ne 'WaW360.SecondMapBaseline.v1' -or
   [string]$b.mapName -ine 'nazi_zombie_aztec_V2.2' -or
   [string]$i.schema -ne 'WaW360.AztecPcFastfileImageSourceCensus.v1' -or
   @($i.sourceFastfiles).Count -ne 3){
    throw 'Lightning 8,509-root proof, original PC source SHA inventory or reference FF changed.'
}
Write-Host '[AZTEC-PC-PAYLOAD] ========================================================='
Write-Host '[AZTEC-PC-PAYLOAD] THREE PC SOURCE FFs: physical XAsset headers, aliases and typed sound alias lists'
Write-Host '[AZTEC-PC-PAYLOAD] Original files read-only, no new Xbox FF or audio payload.'
New-Item -ItemType Directory -Force -Path $out|Out-Null
Write-Host '[AZTEC-PC-PAYLOAD-PROGRESS] [####----------------] 20% | original PC FF SHA and 8,509-root proof gate'
$arguments=@(
    'run','--no-launch-profile','-c','Release','--project',$project,'--',
    'aztec-pc-source-body-batch',$baseline,$size,$inventory,$out
)
if($FastHeadersOnly){$arguments+='--skip-deep-sound'}
Write-Host '[AZTEC-PC-PAYLOAD-PROGRESS] [########------------] 40% | deserialize the three PC XAsset tables; typed sound structural proof optional'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet @arguments 2>&1 | ForEach-Object {[string]$_}|Tee-Object -FilePath $log
    $exitCode=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($exitCode -ne 0 -or -not(Test-Path -LiteralPath $report -PathType Leaf) -or
   -not(Test-Path -LiteralPath $csv -PathType Leaf)){
    if(Test-Path -LiteralPath $log -PathType Leaf){
        Get-Content -LiteralPath $log -Tail 40 |
            ForEach-Object {Write-Host "[AZTEC-PC-PAYLOAD] $_"}
    }
    throw "PC typed source body proof failed (exit=$exitCode); no Xbox files modified."
}
$r=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json
$okStatus=@(
    'AZTEC_PC_XASSET_POINTER_AND_SOUND_ALIAS_STRUCTURE_PASS_NO_XBOX_AUDIO',
    'AZTEC_PC_XASSET_POINTER_PASS_SOUND_TYPED_PROOF_INCOMPLETE',
    'AZTEC_PC_XASSET_POINTER_HEADER_CENSUS_PASS_SOUND_TYPED_NOT_RUN'
)
if([string]$r.schema -ne 'WaW360.AztecOriginalPcPhysicalBodiesQuickBatch.v1' -or
   [string]$r.status -notin $okStatus -or
   [int]$r.originalPcSourceFastfiles -ne 3 -or
   [int]$r.originalPcFastfilesSha256Verified -ne 3 -or
   -not [bool]$r.sourceZoneAssetHeaderClassificationReconciled -or
   @($r.sourceFiles).Count -ne 3 -or
   [int]$r.emittedToCurrentXboxAztecSoundBodies -ne 0 -or
   [int]$r.originalPcSoundBytesConvertedToXboxXma -ne 0 -or
   [bool]$r.XboxSoundResidencyProven -or
   [bool]$r.hardwareReady -or
   [bool]$r.fullGate11Passed){
    throw 'Typed PC XAsset source-body proof produced a misleading native Xbox readiness report.'
}
Write-Host '[AZTEC-PC-PAYLOAD-PROGRESS] [################----] 80% | source inline header counts and sound nodes'
Write-Host "[AZTEC-PC-PAYLOAD] Original PC physical Sound aliases: $($r.originalPcSoundPhysicalInlineHeaders); typed=$($r.typedPcSoundAliasListsVerified); typed-run failures=$($r.sourceSoundTypedFailedRuns)"
Write-Host "[AZTEC-PC-PAYLOAD] PC sound node categories: loaded=$($r.typedPcLoadedSoundNodes); streamed=$($r.typedPcStreamedSoundNodes); primed=$($r.typedPcPrimedSoundNodes)"
Write-Host "[AZTEC-PC-PAYLOAD] Original PC physical XAnim headers=$($r.sourcePhysicalXanimHeaders); those are not converted bodies."
foreach($x in @($r.sourceTypeHeaders | Sort-Object sourceXAssetCount -Descending | Select-Object -First 14)){
    Write-Host "[AZTEC-PC-PAYLOAD] $($x.role) $($x.assetType): total=$($x.sourceXAssetCount), inline=$($x.originalPcPhysicalInlineHeaders), aliases=$($x.originalPcOffsetOrAliasHeaders), null=$($x.originalPcNullHeaders)"
}
Write-Host '[AZTEC-PC-PAYLOAD-PROGRESS] [####################] 100% | PC source body census written'
Write-Host "[AZTEC-PC-PAYLOAD] STATUS=$($r.status)"
Write-Host "[AZTEC-PC-PAYLOAD] JSON: $report"
Write-Host "[AZTEC-PC-PAYLOAD] CSV: $csv"
Write-Host '[AZTEC-PC-PAYLOAD] No FF, OAT, XMA, donor bytes or Xbox hardware. Only PC SOURCE structural evidence.'
