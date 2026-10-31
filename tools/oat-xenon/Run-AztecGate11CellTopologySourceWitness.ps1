[CmdletBinding()]
param([switch]$BuildDumper)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Gate11: one source-only OAT ABI/topology witness for 11 actual GfxCells.
# No converting reference Xbox payload, no FF regeneration, no deployment.
# Rebuild specialized pinned OAT once to install new TWO-sided capability;
# after that runs reuse the installed binary.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$pcSummaryPath=Join-Path $base 'pc_39_world_material_corpus_source\aztec_pc_39_world_material_corpus_summary.json'
$nativeIdentityPath=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$gate10Path=Join-Path $base 'pc_g10_1903\oat_loaded_1903_draw_readback\aztec_gate10_independent_1903_draw_readback.json'
$out=Join-Path $base 'pc_g10_1903\gate11_cell_topology_source'
$report=Join-Path $out 'aztec_gate11_pc_source_cells_native_abi.json'
$dumper=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$capPath=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$pcCap='t4-pc-gfxworld-gate11-cell-topology-v1'
$xbCap='t4xenon-gfxworld-gate11-cell-abi-v1'
$pcGraphCap='t4-pc-gfxworld-gate11-cell-typed-aabb-graph-v1'
$nativeFieldCap='t4xenon-gfxworld-gate11-full-cell-aabb-abi-v1'
$build=Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1'
foreach($item in @($pcSummaryPath,$nativeIdentityPath,$gate10Path)){
    if(-not(Test-Path -LiteralPath $item -PathType Leaf)){
        throw "Missing verified original source or Gate10 manifest: $item"
    }
}
$pcSummary=Get-Content -LiteralPath $pcSummaryPath -Raw|ConvertFrom-Json
$nativeIdentity=Get-Content -LiteralPath $nativeIdentityPath -Raw|ConvertFrom-Json
$gate10=Get-Content -LiteralPath $gate10Path -Raw|ConvertFrom-Json
if([string]$pcSummary.status -ne 'AZTEC_PC_OAT_39_MATERIAL_HEADERS_AND_CHILD_TABLES_SOURCE_EXPORTED' -or
   [string]$nativeIdentity.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
   [int]$nativeIdentity.assets -ne 8509 -or
   -not [bool]$nativeIdentity.typeSequenceVerified -or
   [string]$gate10.status -ne 'AZTEC_GATE10_1903_ALL_SOURCE_DRAW_FIELDS_OAT_READBACK_PASS_STOCK_UNVERIFIED' -or
   [int]$gate10.actualNativeDrawCount -ne 1903 -or
   [bool]$gate10.XboxHardwareReady){
    throw 'Pinned PC-source, original native ABI, or actual Gate10 draw readback proof changed.'
}
$pcFF=[string]$pcSummary.originalPcFastFile
$xbFF=[string]$nativeIdentity.nativeFastFile
$gateFF=[string]$gate10.stage4EEmittedFf
$readWitness=[string]$gate10.independentOatLoadedWitness
foreach($item in @($pcFF,$xbFF,$gateFF,$readWitness)){
    if([string]::IsNullOrWhiteSpace($item) -or
       -not(Test-Path -LiteralPath $item -PathType Leaf)){
        throw "Missing original PC FF, read-only Xbox reference, or Gate10 FF/witness: $item"
    }
}
if((Get-FileHash -LiteralPath $pcFF -Algorithm SHA256).Hash -ine [string]$pcSummary.sourceFastFileSha256 -or
   (Get-FileHash -LiteralPath $xbFF -Algorithm SHA256).Hash -ine 'E0F3C244DD7B72D0FE71C05FAF014C2C9BFB05F770183954B3E7CDE625EBA98F' -or
   [long](Get-Item -LiteralPath $xbFF).Length -ne 82739469 -or
   (Get-FileHash -LiteralPath $gateFF -Algorithm SHA256).Hash -ine [string]$gate10.stage4EEmittedFfSha256 -or
   (Get-FileHash -LiteralPath $readWitness -Algorithm SHA256).Hash -ine [string]$gate10.independentOatLoadedWitnessSha256){
    throw 'OAT input FF/witness digest or pinned original Xbox identity changed.'
}
$existingGateFFsha=[string]$gate10.stage4EEmittedFfSha256
function Has-Capabilities {
    if(-not(Test-Path -LiteralPath $dumper -PathType Leaf) -or
       -not(Test-Path -LiteralPath $capPath -PathType Leaf)){return $false}
    $cap=Get-Content -LiteralPath $capPath -Raw|ConvertFrom-Json
    return ([string]$cap.format -eq 'WaW360.OatGraphDumperCapabilities.v1' -and
        [string]$cap.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($cap.capabilities) -contains $pcCap -and
        @($cap.capabilities) -contains $xbCap -and
        @($cap.capabilities) -contains $pcGraphCap -and
        @($cap.capabilities) -contains $nativeFieldCap)
}
if(-not (Has-Capabilities)){
    if(-not $BuildDumper){
        throw 'Gate11 typed 11-cell source/native ABI dumper not installed yet. Re-run ONCE with -BuildDumper. No map FF rebuild is needed.'
    }
    Write-Host '[AZTEC-GATE11-CELLS] Installing TWO new typed cell witness capabilities with one pinned OAT native build.'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $build
    if($LASTEXITCODE -ne 0 -or -not (Has-Capabilities)){
        throw 'One-time source/native OAT 11-cell witness installation did not complete; Gate10 FF preserved.'
    }
}
New-Item -ItemType Directory -Path $out -Force|Out-Null
$temp=Join-Path ([IO.Path]::GetTempPath()) ('wawg11_cells_'+[Guid]::NewGuid().ToString('N').Substring(0,10))
New-Item -ItemType Directory -Path $temp -Force|Out-Null
$pcOut=Join-Path $temp 'pc'
$xbOut=Join-Path $temp 'xbox'
New-Item -ItemType Directory -Path $pcOut,$xbOut -Force|Out-Null
function Dump-World([string]$inputFF,[string]$output,[string]$log){
    $returnCode=999
    $originalPreference=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $dumper '--no-color' '--include-assets' 'gfxworld' '--output-folder' $output $inputFF 2>&1|
            ForEach-Object { [string]$_ }|Set-Content -LiteralPath $log -Encoding UTF8
        $returnCode=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$originalPreference}
    if($returnCode -ne 0){
        Get-Content -LiteralPath $log -Tail 30|ForEach-Object {Write-Host "[AZTEC-GATE11-CELLS] OAT $_"}
        throw "Original-PC or native Xbox read-only GfxWorld OAT dump exit=$returnCode; Gate10 preserved."
    }
}
Write-Host '[AZTEC-GATE11-CELLS] ==================================================='
Write-Host '[AZTEC-GATE11-CELLS] ORIGINAL-PC 11 GfxCells: typed OAT topology and native ABI only.'
Write-Host '[AZTEC-GATE11-CELLS] No map FF build, no donor payload, no Xbox.'
Write-Host '[AZTEC-GATE11-PROGRESS] [####----------------] 20% | original-PC loaded cell topology'
Dump-World $pcFF $pcOut (Join-Path $out 'pc_gfxworld_cell_source.log')
Write-Host '[AZTEC-GATE11-PROGRESS] [###########---------] 55% | original-Xbox cell struct ABI offsets'
Dump-World $xbFF $xbOut (Join-Path $out 'native_xbox_cell_abi.log')
$pcFiles=@(Get-ChildItem -LiteralPath $pcOut -Filter 'manifest.json' -Recurse -File |
    Where-Object {$_.FullName -match 'pc_gfxworld_39_material_source_corpus'})
$xbFiles=@(Get-ChildItem -LiteralPath $xbOut -Filter '*.json' -Recurse -File |
    Where-Object {$_.FullName -match 'xenon_gfxworld_surface_abi_witness'})
if($pcFiles.Count -ne 1 -or $xbFiles.Count -ne 1){
    throw "OAT did not output exactly one original-PC cell witness and one Xbox ABI witness; PC=$($pcFiles.Count) XB=$($xbFiles.Count)."
}
$pcDoc=Get-Content -LiteralPath $pcFiles[0].FullName -Raw|ConvertFrom-Json
$xbDoc=Get-Content -LiteralPath $xbFiles[0].FullName -Raw|ConvertFrom-Json
$pcShape=$pcDoc.gate11CellTopology
$xbAbi=$xbDoc.gfxWorldRootAbi

$typedMeta=$pcDoc.gate11CellSourceGraph
if([string]$typedMeta.schema -ne 'WaW360.T4PcGate11TypedGfxCellAabbSourcePointerFree.v1' -or
   [int]$typedMeta.exportedTrees -ne 740 -or
   -not [bool]$typedMeta.typedGraphExportValid -or
   [bool]$typedMeta.nativeFfChildrenEmitted){
    throw 'Specialized OAT did not export a complete original-PC pointer-free 740-tree graph.'
}
$rel=[string]$typedMeta.path
if($rel -notmatch '^[A-Za-z0-9_./-]+$' -or
   $rel.Contains('..') -or [IO.Path]::IsPathRooted($rel)){
    throw "Unsafe OAT cell source path: $rel"
}
$typedFile=Join-Path $pcOut ($rel -replace '/','\')
if(-not(Test-Path -LiteralPath $typedFile -PathType Leaf)){
    throw "Missing typed 11-cell/740-AABB PC graph: $typedFile"
}
$typed=Get-Content -LiteralPath $typedFile -Raw|ConvertFrom-Json
if([string]$typed.schema -ne 'WaW360.T4PcGate11TypedGfxCellAabbSource.v1' -or
   [int]$typed.cellCount -ne 11 -or
   [int]$typed.actualExportedTreeCount -ne 740 -or
   -not [bool]$typed.completeAndValidated -or
   [bool]$typed.originalPcSourcePointerBytesExported -or
   [bool]$typed.xboxReferencePayloadUsed -or
   [bool]$typed.xboxGfxCellsPhysicallyEmitted -or
   [bool]$typed.xboxHardwareReady -or
   @($typed.cells).Count -ne 11){
    throw 'Typed GfxCell/AABB source child data lacks PC-owned, pointer-free validity.'
}
$treeCount=0
$modelIndexes=0
$probeIndexes=0
for($ci=0;$ci -lt 11;$ci++){
    $cell=$typed.cells[$ci]
    if([int]$cell.ordinal -ne $ci -or
       [int]$cell.portalCount -ne 0 -or
       [int]$cell.cullGroupCount -ne 0 -or
       [int]$cell.aabbTreeCount -ne @($cell.aabbTrees).Count -or
       [int]$cell.reflectionProbeCount -ne @($cell.reflectionProbeIndices).Count -or
       @($cell.mins).Count -ne 3 -or @($cell.maxs).Count -ne 3){
        throw "Cell $ci has invalid source-owned typed child counts."
    }
    $probeIndexes+=@($cell.reflectionProbeIndices).Count
    for($ti=0;$ti -lt @($cell.aabbTrees).Count;$ti++){
        $t=$cell.aabbTrees[$ti]
        if([int]$t.ordinal -ne $ti -or
           [int]$t.smodelIndexCount -ne @($t.smodelIndexes).Count -or
           @($t.mins).Count -ne 3 -or
           @($t.maxs).Count -ne 3){
            throw "Cell $ci AABB $ti has invalid model-index child array."
        }
        foreach($index in @($t.smodelIndexes)){
            if([int]$index -lt 0 -or [int]$index -ge 1903){
                throw "Cell $ci AABB $ti indexes an out-of-range static model."
            }
            $modelIndexes++
        }
        $treeCount++
    }
}
if($treeCount -ne 740 -or
   $modelIndexes -ne [int]$typed.actualExportedSmodelIndexes -or
   $probeIndexes -ne [int]$typed.actualExportedCellReflectionIndexes){
    throw 'OAT typed cell model/probe index child totals do not reconcile.'
}

if([string]$pcDoc.format -ne 'WaW360.T4PcGfxWorld39MaterialSourceCorpus.v1' -or
   [string]$pcShape.schema -ne 'WaW360.T4PcGate11CellTopology.v1' -or
   [int]$pcShape.cellCount -ne 11 -or
   -not [bool]$pcShape.cellsPresent -or
   @($pcShape.cells).Count -ne 11 -or
   [string]$xbDoc.schema -ne 'WaW360.T4XenonGfxWorldSurfaceAbiWitness.v1' -or
   [int]$xbAbi.gfxWorldSize -ne 900 -or
   [int]$xbDoc.loadedWorldRootShape.cellCount -ne 11 -or
   [int]$pcShape.pcCellSize -le 0 -or
   [int]$xbAbi.gfxCellSize -le 0 -or
   [int]$pcShape.pcPortalSize -le 0 -or
   [int]$xbAbi.gfxPortalSize -le 0 -or
   [int]$pcShape.pcAabbTreeSize -le 0 -or
   [int]$xbAbi.gfxAabbTreeSize -le 0){
    throw 'Pinned original-PC source GfxCell and native T4Xenon cell topology/ABI not captured.'
}
$countsOk=[bool]$pcShape.cellTopologyConsistent
$totalTrees=0;$totalPortals=0;$totalCull=0;$unresolved=0
foreach($cell in @($pcShape.cells)){
    $idx=[int]$cell.ordinal
    if($idx -lt 0 -or $idx -gt 10 -or -not [bool]$cell.pointerAndCountShapeValid){$countsOk=$false}
    $totalTrees+=[int]$cell.aabbTreeCount
    $totalPortals+=[int]$cell.portalCount
    $totalCull+=[int]$cell.cullGroupCount
    if([bool]$cell.pointerAndCountShapeValid -and
       @($cell.portals).Count -ne [int]$cell.portalCount){$countsOk=$false}
    foreach($portal in @($cell.portals)){
        if([int]$portal.targetCellIndex -lt 0 -or [int]$portal.targetCellIndex -ge 11){
            $unresolved++
        }
    }
}
if($totalTrees -ne [int]$pcShape.totalAabbTrees -or
   $totalPortals -ne [int]$pcShape.totalPortals -or
   $totalCull -ne [int]$pcShape.totalCellCullGroupReferences -or
   $unresolved -ne [int]$pcShape.unresolvedCrossCellPortalTargets){
    throw 'Source cell graph total counts/portal-reference ordinals disagree internally.'
}
$nativeFieldOffsetsProven=(
    [int]$xbAbi.cellMinsAt -eq 0 -and
    [int]$xbAbi.cellMaxsAt -eq 12 -and
    [int]$xbAbi.cellAabbTreeCountAt -eq 24 -and
    [int]$xbAbi.cellAabbTreeAt -eq 28 -and
    [int]$xbAbi.cellPortalCountAt -eq 32 -and
    [int]$xbAbi.cellPortalsAt -eq 36 -and
    [int]$xbAbi.cellCullGroupCountAt -eq 40 -and
    [int]$xbAbi.cellCullGroupsAt -eq 44 -and
    [int]$xbAbi.cellReflectionProbeCountAt -eq 48 -and
    [int]$xbAbi.cellReflectionProbesAt -eq 52 -and
    [int]$xbAbi.aabbMinsAt -eq 0 -and
    [int]$xbAbi.aabbMaxsAt -eq 12 -and
    [int]$xbAbi.aabbChildCountAt -eq 24 -and
    [int]$xbAbi.aabbSurfaceCountAt -eq 26 -and
    [int]$xbAbi.aabbStartSurfIndexAt -eq 28 -and
    [int]$xbAbi.aabbSmodelIndexCountAt -eq 30 -and
    [int]$xbAbi.aabbSmodelIndexesAt -eq 32 -and
    [int]$xbAbi.aabbChildrenOffsetAt -eq 36
)
$nativeAbiMeasured=([int]$pcShape.pcCellSize -eq [int]$xbAbi.gfxCellSize -and
    [int]$pcShape.pcPortalSize -eq [int]$xbAbi.gfxPortalSize -and
    [int]$pcShape.pcAabbTreeSize -eq [int]$xbAbi.gfxAabbTreeSize -and
    [int]$pcShape.pcCellAabbTreePointerAt -eq [int]$xbAbi.cellAabbTreeAt -and
    [int]$pcShape.pcCellPortalsPointerAt -eq [int]$xbAbi.cellPortalsAt -and
    [int]$pcShape.pcPortalCellPointerAt -eq [int]$xbAbi.portalCellAt)
$pcCopy=Join-Path $out 'original_pc_oat_loaded_11_cells.json'
$xbCopy=Join-Path $out 'original_xbox_oat_cell_abi.json'
$typedCopy=Join-Path $out 'original_pc_oat_loaded_11_cells_740_aabb_typed.json'
Copy-Item -LiteralPath $pcFiles[0].FullName -Destination $pcCopy -Force
Copy-Item -LiteralPath $xbFiles[0].FullName -Destination $xbCopy -Force
Copy-Item -LiteralPath $typedFile -Destination $typedCopy -Force
$verifiedTopology=$countsOk -and $nativeAbiMeasured -and $nativeFieldOffsetsProven -and
    $unresolved -eq 0 -and $treeCount -eq 740
$summary=[ordered]@{
    schema='WaW360.AztecGate11PcCellTopologyNativeAbiWitness.v1'
    status=if($verifiedTopology){
        'AZTEC_GATE11_11_CELL_740_AABB_TYPED_SOURCE_NATIVE_ABI_PASS_NOT_EMITTED'
    }else{
        'AZTEC_GATE11_CELL_TOPOLOGY_OR_NATIVE_ABI_NEEDS_SOURCE_REVIEW_NOT_EMITTED'
    }
    pcOriginalFastFileSha256=[string]$pcSummary.sourceFastFileSha256
    nativeReferenceFastFileSha256='E0F3C244DD7B72D0FE71C05FAF014C2C9BFB05F770183954B3E7CDE625EBA98F'
    existingPassingGate10FfSha256=$existingGateFFsha
    sourceWorldCellCount=11
    originalPcCellBytes=[int]$pcShape.pcCellSize
    nativeXboxCellBytes=[int]$xbAbi.gfxCellSize
    originalPcPortalBytes=[int]$pcShape.pcPortalSize
    nativeXboxPortalBytes=[int]$xbAbi.gfxPortalSize
    originalPcAabbTreeBytes=[int]$pcShape.pcAabbTreeSize
    nativeXboxAabbTreeBytes=[int]$xbAbi.gfxAabbTreeSize
    originalPcWorldCellsOffset=[int]$pcShape.worldCellsPointerAt
    nativeXboxWorldCellsOffset=[int]$xbAbi.cellsAt
    originalPcAabbTrees=$totalTrees
    originalPcTypedAabbTreeCount=$treeCount
    originalPcTypedSmodelIndicesCount=$modelIndexes
    originalPcTypedCellReflectionProbeIndicesCount=$probeIndexes
    originalPcTypedCellAabbGraph=$typedCopy
    originalPcTypedCellAabbGraphSha256=(Get-FileHash -LiteralPath $typedCopy -Algorithm SHA256).Hash
    typedPcCellGraphSourcePass=$verifiedTopology
    nativeCellAabbOffsetsProven=$nativeFieldOffsetsProven
    originalPcPortals=$totalPortals
    originalPcCellCullGroupRefs=$totalCull
    unresolvedCrossCellPortalDestinations=$unresolved
    originalPcTopologyPointerCountsConsistent=$countsOk
    nativeCellPortalAabbSizesAndCriticalOffsetsMatchPc=$nativeAbiMeasured
    originalPcOatSourceManifest=$pcCopy
    originalPcOatSourceManifestSha256=(Get-FileHash -LiteralPath $pcCopy -Algorithm SHA256).Hash
    originalXboxReferenceAbi=$xbCopy
    originalXboxReferenceAbiSha256=(Get-FileHash -LiteralPath $xbCopy -Algorithm SHA256).Hash
    sourcePointerValuesCopied=$false
    xboxOraclePayloadBytesCopied=$false
    portableGfxCellChildDataEmitted=$false
    gate11ProductionPassed=$false
    hardwareReady=$false
    note='Full PC-owned pointer-free 11 GfxCells/740 AABB typed source records, static-model and reflection-probe index arrays, plus independently measured native Xbox 56B/40B field offsets. No child FF emission or hardware approval.'
}
$summary|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $report -Encoding UTF8
# The full material/asset extraction was only a disposable source witness.
# Keep compact hashes and JSON copies; avoid accumulating duplicate OAT dumps.
Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host '[AZTEC-GATE11-PROGRESS] [####################] 100% | typed original-PC cell topology & Xbox native ABI measured'
Write-Host "[AZTEC-GATE11-CELLS] World cells=$($summary.sourceWorldCellCount) AABB trees=$totalTrees portals=$totalPortals cell cull refs=$totalCull"
Write-Host "[AZTEC-GATE11-CELLS] TYPED PC GRAPH: $treeCount AABB trees; $modelIndexes smodel-index children; $probeIndexes reflection-probe indices"
Write-Host "[AZTEC-GATE11-CELLS] TYPED GRAPH=$typedCopy"
Write-Host "[AZTEC-GATE11-CELLS] Original-PC/Xbox sizes: GfxCell $($summary.originalPcCellBytes)/$($summary.nativeXboxCellBytes), Portal $($summary.originalPcPortalBytes)/$($summary.nativeXboxPortalBytes), AABB $($summary.originalPcAabbTreeBytes)/$($summary.nativeXboxAabbTreeBytes)"
Write-Host "[AZTEC-GATE11-CELLS] Cross-cell target ordinals unresolved=$unresolved; native ABI matches=$nativeAbiMeasured"
Write-Host "[AZTEC-GATE11-CELLS] Report=$report"
Write-Host '[AZTEC-GATE11-CELLS] NO production FF emission or hardware deployment. Gate11 pending; 10/13 preserved.'
if(-not $verifiedTopology){
    throw 'Typed source topology or Xbox cell ABI differs; inspect saved per-cell report before a serializer is attempted.'
}
