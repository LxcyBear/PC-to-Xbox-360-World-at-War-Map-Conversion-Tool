[CmdletBinding()]
param(
    [string]$PcMapsRoot
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Read-only actual-byte audit of the original-PC Aztec, the native reference,
# the 5-real-XModel GfxWorld scratch FF and the PC-derived _load FF.
# NEVER infer completeness from packed .ff file size: compressed input formats
# differ between PC and Xbox, and the native reference is NOT a byte target.
# Distinguish physical bytes from native root identity shells and real payloads.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$map='nazi_zombie_aztec'
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$n=Join-Path $base 'native_material_mapping'
$worldReportPath=Join-Path $n 'pc_gate8_5x_ff\aztec_pc_world_core_integrated_oat_run.json'
$nativeProofPath=Join-Path $n 'native_graph_proof\native_graph_proof_result.json'
$baselinePath=Join-Path $base 'second_map_baseline.json'
$selectedLoadPath=Join-Path $repo 'output\native_smoke_test_pc_load\nazi_zombie_aztec_V2.2\linker_v2_load_emit\nazi_zombie_aztec_V2.2_linker_v2_load_emit.json'
$stage=Join-Path $repo 'output\native_smoke_test_staging\nazi_zombie_aztec'
$reportDir=Join-Path $repo 'output\native_smoke_test_staging\nazi_zombie_aztec_reports'
$jsonOut=Join-Path $reportDir 'aztec_real_payload_size_audit.json'
$txtOut=Join-Path $reportDir 'aztec_real_payload_size_audit.txt'
if(-not(Test-Path -LiteralPath $worldReportPath -PathType Leaf)){
    throw "Current five-XModel OAT world proof missing: $worldReportPath"
}
$w=Get-Content -LiteralPath $worldReportPath -Raw | ConvertFrom-Json
if([string]$w.schema -ne 'WaW360.AztecPcGfxWorldIntegratedFastFileOatRun.v1' -or
   [string]$w.status -ne 'AZTEC_GATE8_FIVE_REAL_XMODELS_8509_ROOT_WORLD_OAT_PASS' -or
   -not [bool]$w.OatPass -or
   -not [bool]$w.fiveSourceOwnedXModelProbe -or
   [int]$w.realXModelRootsInCurrentFf -ne 5 -or
   [int]$w.realPcGfxSurfaceCount -ne 735 -or
   [int]$w.realPcGfxWorldMaterialRoots -ne 39){
    throw 'Source five-model scratch proof drift. Refusing to label unproven FF as Aztec.'
}
$emitterManifestPath=[string]$w.emitterReport
if(-not(Test-Path -LiteralPath $emitterManifestPath -PathType Leaf)){
    throw "Xenon source emitter's exact zone/block manifest missing: $emitterManifestPath"
}
$e=Get-Content -LiteralPath $emitterManifestPath -Raw | ConvertFrom-Json
if([int]$e.NativeRoots -ne 8509 -or [int]$e.RealXModelRoots -ne 5 -or
   [int]$e.RealMaterialRoots -ne 39 -or [int]$e.RealImageRoots -ne 2 -or
   [int]$e.RealRawFileRoots -ne 114 -or
   -not [bool]$e.LayoutReplayExact -or
   -not [bool]$e.ExactIdentitySequence -or
   -not [bool]$e.OatZeroWarningsZeroErrors -or
   -not [bool]$e.ValidationOnly -or
   [bool]$e.ReadyForHardware){
    throw 'The source OAT-passing five-XModel graph no longer has its exact diagnostic shape.'
}
$rows=New-Object 'System.Collections.Generic.List[object]'
function Add-File([string]$label,[string]$role,[string]$path,[string]$expectSha=''){
    $ok=(-not [string]::IsNullOrWhiteSpace($path)) -and
        (Test-Path -LiteralPath $path -PathType Leaf)
    $bytes=if($ok){[long](Get-Item -LiteralPath $path).Length}else{0L}
    $sha=if($ok){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}else{''}
    $shaMatches=[string]::IsNullOrWhiteSpace($expectSha) -or ($sha -ieq $expectSha)
    $rows.Add([ordered]@{
        label=$label;role=$role;file=$path
        exists=$ok;bytes=$bytes;MiB=[math]::Round($bytes/1MB,3)
        sha256=$sha;shaMatch=$shaMatches
    })
}
$pcMain=''
$pcLoad=''
if(Test-Path -LiteralPath $baselinePath -PathType Leaf){
    $b=Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
    foreach($entry in @($b.fastfiles)){
        $role=[string]$entry.Role
        if($role -eq 'map.ff'){$pcMain=[string]$entry.Path}
        if($role -eq 'load.ff' -and
           [IO.Path]::GetFileName([string]$entry.Path) -ieq ($map+'_load.ff')){
            $pcLoad=[string]$entry.Path
        }
    }
}
if(-not $pcMain){
    $oneDrive=if($env:OneDrive){$env:OneDrive}else{Join-Path $env:USERPROFILE 'OneDrive'}
    $root=if($PcMapsRoot){[IO.Path]::GetFullPath($PcMapsRoot)}else{Join-Path $oneDrive 'Desktop\My Xbox\My Projects\World at War\PC Maps'}
    $pcMain=Join-Path $root 'nazi_zombie_aztec_V2.2\nazi_zombie_aztec.ff'
    $pcLoad=Join-Path $root 'nazi_zombie_aztec_V2.2\nazi_zombie_aztec_load.ff'
}
Add-File 'Original PC main FF (different compression/platform)' 'PC_ORIGINAL_MAIN' $pcMain
Add-File 'Original PC load FF (different compression/platform)' 'PC_ORIGINAL_LOAD' $pcLoad
# A PC custom map can be a multiple-container package: comparing ONLY
# map.ff ignores map_patch.ff/mod.ff and may understate source asset bytes.
if($null -ne (Get-Variable -Name b -ErrorAction SilentlyContinue)){
    foreach($entry in @($b.fastfiles)){
        $role=[string]$entry.Role
        if($role -in @('map_patch.ff','mod.ff')){
            Add-File ('Original PC '+$role) ('PC_SOURCE_'+($role -replace '[^a-zA-Z0-9]','_').ToUpperInvariant()) ([string]$entry.Path)
        }
    }
}
$golden=''
$goldenSha=''
if(Test-Path -LiteralPath $nativeProofPath -PathType Leaf){
    $nproof=Get-Content -LiteralPath $nativeProofPath -Raw | ConvertFrom-Json
    $golden=[string]$nproof.verifiedGoldenXboxFastFile
    $goldenSha=[string]$nproof.nativeSha256
}
Add-File 'Existing genuine native Xbox reference (not PC conversion)' 'NATIVE_REFERENCE_ONLY' $golden $goldenSha
$main=[string]$w.outputFastFile
Add-File 'Current original-PC five-real-XModel WORLD scratch FF' 'XBOX_PC_SOURCE_PARTIAL_WORLD' $main
Add-File 'Currently staged Xbox-format main FF' 'XBOX_STAGED_MAIN' (Join-Path $stage ($map+'.ff'))
$load=''
$loadSha=''
if(Test-Path -LiteralPath $selectedLoadPath -PathType Leaf){
    $loadReport=Get-Content -LiteralPath $selectedLoadPath -Raw | ConvertFrom-Json
    $load=[string]$loadReport.OutputFastFile
    $loadSha=[string]$loadReport.FastFileSha256
    # Some baselines inventory map.ff/patch/mod but exclude load.ff.
    # The verified Linker V2 selected-load proof is then authoritative.
    if(-not (Test-Path -LiteralPath $pcLoad -PathType Leaf) -and
       -not [string]::IsNullOrWhiteSpace([string]$loadReport.SelectedSourceFastFile)){
        $pcLoad=[string]$loadReport.SelectedSourceFastFile
        $originalLoadRow=@($rows.ToArray() | Where-Object {$_.role -eq 'PC_ORIGINAL_LOAD'})[0]
        if($null -ne $originalLoadRow){
            $originalLoadRow.file=$pcLoad
            $originalLoadRow.exists=Test-Path -LiteralPath $pcLoad -PathType Leaf
            if($originalLoadRow.exists){
                $originalLoadRow.bytes=[long](Get-Item -LiteralPath $pcLoad).Length
                $originalLoadRow.MiB=[math]::Round($originalLoadRow.bytes/1MB,3)
                $originalLoadRow.sha256=(Get-FileHash -LiteralPath $pcLoad -Algorithm SHA256).Hash
            }
        }
    }
}
Add-File 'PC-selected Xbox-format load FF' 'XBOX_SOURCE_SELECTED_LOAD' $load $loadSha
Add-File 'Currently staged Xbox-format load FF' 'XBOX_STAGED_LOAD' (Join-Path $stage ($map+'_load.ff'))
Add-File 'Emitter decompressed zone stream (do not confuse with compressed FF)' 'XBOX_EMITTED_ZONE' ([string]$e.OutputZoneFile)

# A 8509-root identity manifest is not a 8509-asset payload proof.
# These are the independent root bodies the current emitter actually claims.
$inventory=[ordered]@{
    nativeAssetRootIdentities=8509
    realSourceXModelBodies=[int]$e.RealXModelRoots
    sourceXModelRootIdentities=284
    nativeXModelRootIdentitiesWithoutSourceBody=(284-[int]$e.RealXModelRoots)
    distinctOriginalPcStaticModelNames=27
    distinctOriginalPcStaticModelBodiesIntegrated=3
    staticModelTypesWithBodiesNotIntegrated=24
    originalPcStaticDrawInstances=1903
    nativeStaticDrawInstanceRecordsActuallyLinked=0
    nativeStaticDrawPlacementRecordsRepresentedByExistingSourceModelBodies=1572
    realSourceMaterialBodies=[int]$e.RealMaterialRoots
    sourceWorldGfxSurfaces=735
    originalPcWorldVertexCount=32713
    originalPcWorldIndexCount=58653
    realImageBodies=[int]$e.RealImageRoots
    realRawFileBodies=[int]$e.RealRawFileRoots
    realMapEntsBodies=[int]$e.RealMapEntsRoots
    realComWorldBodies=[int]$e.RealComWorldRoots
    completeOriginalPcDpvsChildGraph=[bool]$e.OriginalPcGfxWorldFullDpvsGraphComplete
    sourceRuntimeContentClosureVerified=$false
    shellRootIdentitiesAreNotConvertedAssetBodies=$true
    note='Counts refer only to the source-owned bodies emitted in this particular diagnostic. Other native roots may be stock references or identity shells, not missing PC owner payloads to blindly copy.'
}
$blocks=[ordered]@{
    emitterReportedZoneBytes=[long]$e.ZoneBytes
    emitterReportedMainZoneBytes=[long]$e.MainZoneBytes
    emitterReportedLargeRuntimeBytes=[long]$e.LargeRuntimeBytes
    emitterReportedLargeBytes=[long]$e.LargeBytes
    emitterReportedTempBytes=[long]$e.TempBytes
    emitterReportedVirtualBytes=[long]$e.VirtualBytes
    emitterReportedPhysicalBytes=[long]$e.PhysicalBytes
}
$files=@($rows.ToArray())
$worldRow=@($files | Where-Object {$_.role -eq 'XBOX_PC_SOURCE_PARTIAL_WORLD'})[0]
$stagedRow=@($files | Where-Object {$_.role -eq 'XBOX_STAGED_MAIN'})[0]
$sourceCorrect=$worldRow.exists -and $stagedRow.exists -and
    $worldRow.sha256 -ieq $stagedRow.sha256 -and
    $worldRow.bytes -eq $stagedRow.bytes
$loadRow=@($files | Where-Object {$_.role -eq 'XBOX_SOURCE_SELECTED_LOAD'})[0]
$stagedLoadRow=@($files | Where-Object {$_.role -eq 'XBOX_STAGED_LOAD'})[0]
$loadCorrect=$loadRow.exists -and $stagedLoadRow.exists -and
    $loadRow.sha256 -ieq $stagedLoadRow.sha256 -and
    $loadRow.bytes -eq $stagedLoadRow.bytes -and $loadRow.shaMatch
$pcRow=@($files | Where-Object {$_.role -eq 'PC_ORIGINAL_MAIN'})[0]
$pcToXboxCompressedRatio=if($pcRow.exists -and $pcRow.bytes -gt 0){
    [math]::Round(100.0*$worldRow.bytes/[double]$pcRow.bytes,2)
}else{$null}
$originalValidated=[bool]$w.OatPass -and [bool]$e.ValidationPass -and [bool]$e.LayoutReplayExact
$result=[ordered]@{
    schema='WaW360.AztecNativeTestRealPayloadSizeAudit.v1'
    status='AZTEC_SIZE_AUDIT_PARTIAL_WORLD_CONFIRMED_NOT_FULL_MAP'
    originalPcSourceSizeComparableDirectlyToXenonCompressedFf=$false
    sourceMainShaMatchesStaging=$sourceCorrect
    sourceSelectedLoadShaMatchesStaging=$loadCorrect
    currentXenonCompressedMainAsPercentOfOriginalPcCompressedMain=$pcToXboxCompressedRatio
    actualSourceEmitterNativeOatPass=$originalValidated
    artifactFiles=$files
    emittedZoneAndMemoryBlockByteCounts=$blocks
    payloadInventory=$inventory
    fullProductionContentReady=$false
    safeToDescribeAsCompleteAztecMap=$false
    mayRunSeparateControlledFFLoaderOnlyTestAfterRealOatGfxWorldGate=$true
    hardwareGameplayValidated=$false
    note='Small .ff size is a real warning, not conclusive by itself: compression, native-vs-PC asset composition and separate loose files change sizes. The definitive incomplete-content evidence is 5 XModel bodies, 2 images, absent 1903 draw links, and incomplete world children. This audit changes NO FastFiles or package.'
}
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
$result | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $jsonOut -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add('AZTEC CURRENT FILE SIZE AND REAL PAYLOAD AUDIT')
$lines.Add('================================================================')
foreach($entry in $files){
    $lines.Add(('{0,-35} {1,12:N0} B {2,10:N3} MiB   shaOK={3}' -f
        $entry.role,$entry.bytes,$entry.MiB,$entry.shaMatch))
    $lines.Add(('    '+$entry.file))
}
$lines.Add('================================================================')
$lines.Add(('Current main FF/PC main FF compressed byte percentage: {0}%' -f $pcToXboxCompressedRatio))
$lines.Add(('Main and load staging SHA matches: {0} / {1}' -f $sourceCorrect,$loadCorrect))
$lines.Add(('Emitted decompressed zone: {0:N0} bytes; main zone: {1:N0}; virtual: {2:N0}; physical: {3:N0}' -f
    $blocks.emitterReportedZoneBytes,$blocks.emitterReportedMainZoneBytes,
    $blocks.emitterReportedVirtualBytes,$blocks.emitterReportedPhysicalBytes))
$lines.Add('PAYLOAD OWNERSHIP: actual XModels=5 of native 284 identities; only 3 of 27 static draw model types upgraded; 0/1903 draw records linked; 2 real images.')
$lines.Add('DPVS/world child graph complete: FALSE. Runtime full map NOT READY. Native OAT format proof is NOT production/full content proof.')
$lines.Add('Source FF bytes and staged package are NOT modified by this audit.')
$lines | Set-Content -LiteralPath $txtOut -Encoding UTF8
Write-Host '[AZTEC-SIZE-AUDIT] ================================================='
foreach($entry in $files){
    Write-Host ('[AZTEC-SIZE-AUDIT] {0,-31} {1,10:N3} MiB ({2:N0} bytes)' -f
        $entry.role,$entry.MiB,$entry.bytes)
}
Write-Host "[AZTEC-SIZE-AUDIT] compressedXboxMainVsPcMain=$pcToXboxCompressedRatio% (NOT a correctness score)"
Write-Host "[AZTEC-SIZE-AUDIT] stageMainShaMatches=$sourceCorrect stageLoadShaMatches=$loadCorrect"
Write-Host "[AZTEC-SIZE-AUDIT] REAL source XModels=5, image bodies=2, static draw links=0/1903, DPVS complete=False"
Write-Host '[AZTEC-SIZE-AUDIT] status=AZTEC_SIZE_AUDIT_PARTIAL_WORLD_CONFIRMED_NOT_FULL_MAP'
Write-Host "[AZTEC-SIZE-AUDIT] report=$txtOut"
Write-Host "[AZTEC-SIZE-AUDIT] json=$jsonOut"
Write-Host '[AZTEC-SIZE-AUDIT] READ-ONLY. DO NOT CONFLATE OAT LOADER PASS WITH FULL CONTENT.'
