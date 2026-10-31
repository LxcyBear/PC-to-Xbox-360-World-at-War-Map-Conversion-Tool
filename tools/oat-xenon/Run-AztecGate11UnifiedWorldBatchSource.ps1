[CmdletBinding()]
param([switch]$BuildDumper)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# ALL remaining source-owned GfxWorld branches audited together. This is
# an input/staging step for ONE future integrated FF emission, not another
# independently rebuilt FF, no Xbox payload donors, no hardware deployment.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$gate11Readback=Join-Path $base 'pc_g11_cells_740\oat_loaded_11_cell_aabb_readback\aztec_gate11_independent_cells_aabb_readback.json'
$pcSummaryPath=Join-Path $base 'pc_39_world_material_corpus_source\aztec_pc_39_world_material_corpus_summary.json'
$nativeIdentityPath=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$out=Join-Path $base 'pc_g11_cells_740\gate11_remaining_unified_source'
$report=Join-Path $out 'aztec_gate11_remaining_world_batch_source_gate.json'
$dumper=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$capPath=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$builder=Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1'
foreach($p in @($gate11Readback,$pcSummaryPath,$nativeIdentityPath,$builder)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Gate11 unified world source requires existing verified FF and PC-native source: $p"
    }
}
$read=Get-Content -LiteralPath $gate11Readback -Raw|ConvertFrom-Json
$pc=Get-Content -LiteralPath $pcSummaryPath -Raw|ConvertFrom-Json
$native=Get-Content -LiteralPath $nativeIdentityPath -Raw|ConvertFrom-Json
if([string]$read.schema -ne 'WaW360.AztecGate11IndependentLoadedCellAabbReadback.v1' -or
   [string]$read.status -ne 'AZTEC_GATE11_PHYSICAL_11_CELL_740_AABB_SOURCE_FIELDS_INDEPENDENT_OAT_PASS_REMAINDER_PENDING' -or
   [string]$read.actualGfxWorldFfSha256 -ine
       '05CEEC82AF5D58706BA5A6EE24995D09A3FB764F3E547A8B4758A9347ECF2862' -or
   [string]$pc.status -ne 'AZTEC_PC_OAT_39_MATERIAL_HEADERS_AND_CHILD_TABLES_SOURCE_EXPORTED' -or
   [string]$native.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
   [int]$native.assets -ne 8509 -or -not [bool]$native.typeSequenceVerified -or
   [string]$read.preservedGate10FfSha256 -ine
       '0BB41398C4236EF33CB9D60F923E09152966D52AA623DAEFC33D731A42630564' -or
   [bool]$read.hardwareReady){
    throw 'Source/Gate10/Gate11 proven native FF lineage changed. Refuse unverified world audit.'
}
$gate11Ff=[string]$read.actualGfxWorldFf
$pcFF=[string]$pc.originalPcFastFile
$xbFF=[string]$native.nativeFastFile
foreach($p in @($gate11Ff,$pcFF,$xbFF)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Missing protected passing FF or original PC/retail ABI source: $p"
    }
}
$protectedGate11=(Get-FileHash -LiteralPath $gate11Ff -Algorithm SHA256).Hash
if($protectedGate11 -ine [string]$read.actualGfxWorldFfSha256 -or
   (Get-FileHash -LiteralPath $pcFF -Algorithm SHA256).Hash -ine
      [string]$pc.sourceFastFileSha256 -or
   (Get-FileHash -LiteralPath $xbFF -Algorithm SHA256).Hash -ine
      'E0F3C244DD7B72D0FE71C05FAF014C2C9BFB05F770183954B3E7CDE625EBA98F' -or
   [long](Get-Item -LiteralPath $xbFF).Length -ne 82739469L){
    throw 'Gate11 FF or original PC/native Xbox reference SHA drifted; no OAT allowed.'
}
$capPC='t4-pc-gfxworld-gate11-all-remaining-source-census-v1'
$capXB='t4xenon-gfxworld-gate11-all-remaining-field-abi-v1'
function Has-Capabilities{
    if(-not(Test-Path -LiteralPath $dumper -PathType Leaf) -or
       -not(Test-Path -LiteralPath $capPath -PathType Leaf)){return $false}
    $cap=Get-Content -LiteralPath $capPath -Raw|ConvertFrom-Json
    return ([string]$cap.format -eq 'WaW360.OatGraphDumperCapabilities.v1' -and
        [string]$cap.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($cap.capabilities) -contains $capPC -and
        @($cap.capabilities) -contains $capXB)
}
if(-not(Has-Capabilities)){
    if(-not $BuildDumper){
        throw 'New ALL-family PC + Xbox source/ABI census not installed. Re-run ONCE with -BuildDumper. DO NOT rebuild FF.'
    }
    Write-Host '[AZTEC-GATE11-BATCH] One specialized OAT rebuild adds ALL remaining world family source dumps and native ABI.'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $builder
    if($LASTEXITCODE -ne 0 -or -not(Has-Capabilities)){
        throw 'One-time specialized world-wide OAT source census installation failed. Passing FF unchanged.'
    }
}
New-Item -ItemType Directory -Path $out -Force|Out-Null
$temp=Join-Path ([IO.Path]::GetTempPath()) ('waw_g11_batch_'+[Guid]::NewGuid().ToString('N').Substring(0,10))
$pcOut=Join-Path $temp 'pc'
$xbOut=Join-Path $temp 'xbox'
New-Item -ItemType Directory -Path $pcOut,$xbOut -Force|Out-Null
function Read-World([string]$ff,[string]$output,[string]$log){
    $save=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $dumper '--no-color' '--include-assets' 'gfxworld' '--output-folder' $output $ff 2>&1 |
            ForEach-Object {[string]$_} |
            Set-Content -LiteralPath $log -Encoding UTF8
        $code=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$save}
    if($code -ne 0){
        Get-Content -LiteralPath $log -Tail 35 |
            ForEach-Object {Write-Host "[AZTEC-GATE11-BATCH] OAT $_"}
        throw "Native OAT GfxWorld source/ABI read failed ($code). No new FF build."
    }
}
Write-Host '[AZTEC-GATE11-BATCH] ================================================================'
Write-Host '[AZTEC-GATE11-BATCH] ONE combined source capture for ALL remaining GfxWorld dependencies.'
Write-Host '[AZTEC-GATE11-BATCH] 11 cells / 740 trees already independently OAT PASS; their FF SHA is protected.'
Write-Host '[AZTEC-GATE11-BATCH] ZERO new map FF or .NET builds. Xbox reference ABI only, NEVER byte donor.'
Write-Host '[AZTEC-GATE11-PROGRESS] [####----------------] 20% | source-owned PC world all-family corpus'
try{
    Read-World $pcFF $pcOut (Join-Path $out 'original_pc_all_world_children_oat.log')
    Write-Host '[AZTEC-GATE11-PROGRESS] [##########----------] 50% | Xbox 360 native ABI for every remaining group'
    Read-World $xbFF $xbOut (Join-Path $out 'original_xbox_world_abi_only_oat.log')
    $pcs=@(Get-ChildItem -LiteralPath $pcOut -Recurse -File -Filter 'manifest.json' |
        Where-Object {$_.FullName -match 'pc_gfxworld_39_material_source_corpus'})
    $xbs=@(Get-ChildItem -LiteralPath $xbOut -Recurse -File -Filter '*.json' |
        Where-Object {$_.FullName -match 'xenon_gfxworld_surface_abi_witness'})
    if($pcs.Count -ne 1 -or $xbs.Count -ne 1){
        throw "OAT produced PC world source=$($pcs.Count) and Xbox native ABI=$($xbs.Count), expected exactly 1 each."
    }
    $manifest=Get-Content -LiteralPath $pcs[0].FullName -Raw|ConvertFrom-Json
    $nativeDoc=Get-Content -LiteralPath $xbs[0].FullName -Raw|ConvertFrom-Json
    $meta=$manifest.gate11RemainingWorldCorpus
    if([string]$manifest.format -ne 'WaW360.T4PcGfxWorld39MaterialSourceCorpus.v1' -or
       [string]$meta.schema -ne 'WaW360.T4PcGate11RemainingWorldSourceCorpus.v1' -or
       -not [bool]$meta.pointerFreeSourceGraphWritten -or
       [bool]$meta.nativeFfBuildPerformed -or
       [string]$nativeDoc.schema -ne 'WaW360.T4XenonGfxWorldSurfaceAbiWitness.v1' -or
       [string]$nativeDoc.gate11RemainingNativeAbi.schema -ne
           'WaW360.T4XenonGate11RemainingWorldNativeAbi.v1' -or
       [bool]$nativeDoc.gate11RemainingNativeAbi.containsOriginalXboxPayload -or
       [int]$nativeDoc.gate11RemainingNativeAbi.gfxWorldBytes -ne 900){
        throw 'The bundled source graph/native field ABI was absent or not pointer-safe.'
    }
    $rel=[string]$meta.path
    if($rel -notmatch '^[A-Za-z0-9_./-]+$' -or
       $rel.Contains('..') -or [IO.Path]::IsPathRooted($rel)){
        throw "Unsafe OAT original-PC source file path: $rel"
    }
    $input=Join-Path $pcOut ($rel -replace '/','\')
    if(-not(Test-Path -LiteralPath $input -PathType Leaf)){
        throw "OAT original-PC all-family source graph file not found: $input"
    }
    $world=Get-Content -LiteralPath $input -Raw | ConvertFrom-Json
    if([string]$world.schema -ne 'WaW360.T4PcGate11WorldRemainderCensus.v1' -or
       -not [bool]$world.sourceOnly -or
       [bool]$world.originalPcProcessPointerValuesExported -or
       [bool]$world.nativeXboxPayloadBytesUsed -or
       [bool]$world.hardwareReady -or
       [bool]$world.xboxSerializedWorldChildren -or
       -not [bool]$world.validCountAndPointerShape -or
       -not [bool]$world.sourceOnlyComplete){
        throw 'Source-only GfxWorld remaining child corpus incomplete, oversized, or unsafe. No build.'
    }
    $sourceCopy=Join-Path $out 'original_pc_gate11_all_remaining_world_typed.json'
    Copy-Item -LiteralPath $input -Destination $sourceCopy -Force
    $sourceSha=(Get-FileHash -LiteralPath $sourceCopy -Algorithm SHA256).Hash
    $nativeOnlyPath=Join-Path $out 'measured_xbox360_remaining_world_abi_only.json'
    $nativeDoc.gate11RemainingNativeAbi|ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $nativeOnlyPath -Encoding UTF8
    $nativeAbiSha=(Get-FileHash -LiteralPath $nativeOnlyPath -Algorithm SHA256).Hash
    Write-Host '[AZTEC-GATE11-PROGRESS] [##############------] 70% | verify every original-PC little-endian leaf graph blob'
    $fields=@('skyStartSurfs','coronas','brushModels','cullGroups',
       'worldLodChains','worldLodInfos','worldLodSurfaces',
       'gridRowDataStart','gridRawRowData','gridEntries','gridColors',
       'waterBuffer0','waterBuffer1')
    $blobs=[ordered]@{}
    $totalBytes=0L
    foreach($name in $fields){
        $entry=$world.littleEndianSourceLeafBlobs.$name
        $fileRel=[string]$entry.path
        if(-not [bool]$entry.exported -or
           [bool]$entry.containsProcessPointers -or
           -not [bool]$entry.sourceLittleEndian -or
           $fileRel -notmatch '^[A-Za-z0-9_./-]+$' -or
           $fileRel.Contains('..') -or [IO.Path]::IsPathRooted($fileRel)){
            throw "Source primitive array $name lacks safe original-PC provenance."
        }
        $file=Join-Path $pcOut ($fileRel -replace '/','\')
        if(-not(Test-Path -LiteralPath $file -PathType Leaf)){
            throw "Missing original-PC source array $name at $file"
        }
        $length=[long](Get-Item -LiteralPath $file).Length
        $expected=[long]$entry.count*[long]$entry.elementBytes
        if([long]$entry.count -lt 0 -or [int]$entry.elementBytes -le 0 -or
           $length -ne $expected){
            throw "Original-PC typed $name length=$length but expected $expected bytes."
        }
        $copy=Join-Path $out ('original_pc_'+$name+'_LE.bin')
        Copy-Item -LiteralPath $file -Destination $copy -Force
        $totalBytes += $length
        $blobs[$name]=[ordered]@{
            file=$copy
            sha256=(Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash
            count=[long]$entry.count
            elementBytes=[int]$entry.elementBytes
            byteLength=$length
            endian='ORIGINAL_PC_LITTLE_ENDIAN_REQUIRES_NATIVE_CONVERSION'
            rawPointersCopied=$false
        }
    }
    $c=$world.counts
    if(@($world.reflectionProbeRefs).Count -ne [int]$c.reflectionProbes -or
       @($world.lightmapRefs).Count -ne [int]$c.lightmaps -or
       (@($world.shadowGeometry).Count -ne 0 -and
           @($world.shadowGeometry).Count -ne [int]$c.primaryLights) -or
       (@($world.lightRegions).Count -ne 0 -and
           @($world.lightRegions).Count -ne [int]$c.primaryLights)){
        throw 'Source graph nested reflection/lightmap/shadow/region arrays do not match original PC scalar counts.'
    }
    # These are source-OWNED, NOT completed Xenon asset dependencies.
    # One bundled FF build is intended only after every required serializer
    # and original-PC image/material/root identity relocation passes.
    $families=[ordered]@{
        sky=[ordered]@{
            sourceCount=[int]$c.skySurfaces
            hasSkyImage=-not [string]::IsNullOrWhiteSpace([string]$world.sourceImageAndMaterialReferences.skyImage)
            nextRule='bind PC-owned sky image and skyBox identity to proven native asset roots'
            xenonEmitted=$false
        }
        reflection=[ordered]@{
            sourceCount=[int]$c.reflectionProbes
            referenceRows=@($world.reflectionProbeRefs).Count
            nextRule='rebase every reflection probe image identity and create typed Xenon probe array'
            xenonEmitted=$false
        }
        coronas=[ordered]@{
            sourceCount=[int]$c.coronas
            nextRule='BE-convert 28B native/source GfxLightCorona float records using pinned ABI'
            xenonEmitted=$false
        }
        lightmaps=[ordered]@{
            sourceCount=[int]$c.lightmaps
            referenceRows=@($world.lightmapRefs).Count
            nextRule='resolve every primary and secondary lightmap GfxImage root'
            xenonEmitted=$false
        }
        lightGrid=[ordered]@{
            rows=[int]$c.lightGridRows
            rawBytes=[long]$c.lightGridRawBytes
            entries=[long]$c.lightGridEntries
            colors=[long]$c.lightGridColors
            nextRule='BE-convert row starts/entry indices and native root grid scalars; retain encoded color bytes'
            xenonEmitted=$false
        }
        brushAndCull=[ordered]@{
            brushes=[int]$c.brushModels
            cullGroups=[int]$c.cullGroups
            nextRule='BE-convert source-owned primitive root records, retaining source material and surface counts'
            xenonEmitted=$false
        }
        shadowsAndRegions=[ordered]@{
            primaryLights=[int]$c.primaryLights
            shadowRootPresent=[bool]$c.shadowGeomRootPresent
            regionRootPresent=[bool]$c.lightRegionRootPresent
            nextRule='recreate nested Xenon shadow geometry and light-region hull/axis pointer graphs'
            xenonEmitted=$false
        }
        lodAndWater=[ordered]@{
            chains=[int]$c.worldLodChains
            infos=[int]$c.worldLodInfos
            surfaces=[int]$c.worldLodSurfaces
            water0Bytes=[long]$c.waterBuffer0Bytes
            water1Bytes=[long]$c.waterBuffer1Bytes
            nextRule='convert LOD scalar graph and native water buffer float arrays'
            xenonEmitted=$false
        }
    }
    $needsRebind=[bool]$world.requiresImageAssetRelocation
    $needsStream=[bool]$world.requiresXenonStreamingMetadataReconstruction
    $needsMaterial=[bool]$world.requiresMaterialStateBitsRuntimeSemanticProof
    $summary=[ordered]@{
        schema='WaW360.AztecGate11SingleBatchRemainingWorldSource.v1'
        status='AZTEC_GATE11_ALL_REMAINING_WORLD_SOURCE_CORPUS_CAPTURED_FF_BATCH_NOT_YET_APPROVED'
        priorIndependentGfxCells=11
        priorIndependentAabbTrees=740
        priorIndependentModelIndices=14974
        protectedPreviousGate11Ff=$gate11Ff
        protectedPreviousGate11FfSha256=$protectedGate11
        sourcePcFastFileSha256=[string]$pc.sourceFastFileSha256
        sourceTypedRemainderGraph=$sourceCopy
        sourceTypedRemainderGraphSha256=$sourceSha
        nativeXboxAbiOnlyFile=$nativeOnlyPath
        nativeXboxAbiOnlySha256=$nativeAbiSha
        originalPcSourceLeafByteCount=$totalBytes
        originalPcLeafFiles=$blobs
        originalPcWorldCounts=$c
        sourceFamiliesCollectedTogether=$families
        sourceFamilyCount=8
        nativeFFsNeededForNextBatch=1
        runtimeOnlyBuffersCopyAllowed=$false
        imageAndMaterialRootRelocationStillNeeded=$needsRebind
        xenonOnlyWorldStreamingMetadataStillNeeded=$needsStream
        materialStateBitsSemanticsStillNeeded=$needsMaterial
        targetNextSingleFfBuild='pc_g11_remaining_world_single_batch'
        actualMapFfBuildPerformed=$false
        singleCombinedFfReadyToEmit=$false
        fullWorldClosureProven=$false
        gate11OverallPass=$false
        hardwareReady=$false
    }
    $summary|ConvertTo-Json -Depth 12|
        Set-Content -LiteralPath $report -Encoding UTF8
    Write-Host "[AZTEC-GATE11-BATCH] Source families collected=8/8 (SERIALIZER + REBINDING not yet built)."
    Write-Host "[AZTEC-GATE11-BATCH] Sky=$($c.skySurfaces) Reflection=$($c.reflectionProbes) Coronas=$($c.coronas) Lightmaps=$($c.lightmaps)"
    Write-Host "[AZTEC-GATE11-BATCH] Grid rows=$($c.lightGridRows) rawBytes=$($c.lightGridRawBytes) entries=$($c.lightGridEntries) colors=$($c.lightGridColors)"
    Write-Host "[AZTEC-GATE11-BATCH] Brush=$($c.brushModels) cull=$($c.cullGroups) primaryLights=$($c.primaryLights) shadowRoot=$($c.shadowGeomRootPresent) regionRoot=$($c.lightRegionRootPresent)"
    Write-Host "[AZTEC-GATE11-BATCH] LOD=$($c.worldLodChains)/$($c.worldLodInfos)/$($c.worldLodSurfaces); water=$($c.waterBuffer0Bytes)/$($c.waterBuffer1Bytes) bytes"
    Write-Host "[AZTEC-GATE11-BATCH] PC leaf arrays: $($fields.Count) families, $totalBytes source bytes; all SHA-locked."
    Write-Host "[AZTEC-GATE11-BATCH] Report=$report"
    Write-Host '[AZTEC-GATE11-PROGRESS] [####################] 100% | source-only combined census complete; no FF rebuild'
    Write-Host '[AZTEC-GATE11-BATCH] Next ONE FF build must bind images/materials, reconstruct Xenon streamInfo and convert typed source arrays.'
    Write-Host '[AZTEC-GATE11-BATCH] GATE11 REMAINS PENDING. Hardware NOT approved.'
}finally{
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
if((Get-FileHash -LiteralPath $gate11Ff -Algorithm SHA256).Hash -ine $protectedGate11){
    throw 'CRITICAL: independently verified Gate11 FF changed unexpectedly during source-only census.'
}
