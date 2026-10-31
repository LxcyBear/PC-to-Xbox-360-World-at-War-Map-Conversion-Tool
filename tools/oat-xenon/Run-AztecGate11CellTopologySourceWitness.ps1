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
        @($cap.capabilities) -contains $xbCap)
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
$nativeAbiMeasured=([int]$pcShape.pcCellSize -eq [int]$xbAbi.gfxCellSize -and
    [int]$pcShape.pcPortalSize -eq [int]$xbAbi.gfxPortalSize -and
    [int]$pcShape.pcAabbTreeSize -eq [int]$xbAbi.gfxAabbTreeSize -and
    [int]$pcShape.pcCellAabbTreePointerAt -eq [int]$xbAbi.cellAabbTreeAt -and
    [int]$pcShape.pcCellPortalsPointerAt -eq [int]$xbAbi.cellPortalsAt -and
    [int]$pcShape.pcPortalCellPointerAt -eq [int]$xbAbi.portalCellAt)
$pcCopy=Join-Path $out 'original_pc_oat_loaded_11_cells.json'
$xbCopy=Join-Path $out 'original_xbox_oat_cell_abi.json'
Copy-Item -LiteralPath $pcFiles[0].FullName -Destination $pcCopy -Force
Copy-Item -LiteralPath $xbFiles[0].FullName -Destination $xbCopy -Force
$verifiedTopology=$countsOk -and $nativeAbiMeasured -and $unresolved -eq 0
$summary=[ordered]@{
    schema='WaW360.AztecGate11PcCellTopologyNativeAbiWitness.v1'
    status=if($verifiedTopology){
        'AZTEC_GATE11_11_CELL_POINTER_FREE_TOPOLOGY_NATIVE_ABI_MATCH_SOURCE_PASS_NOT_EMITTED'
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
    note='First TWO-sided PC source GfxCell topology / pinned Xbox native struct ABI only. This is not 11 complete portal/AABB typed payloads, not a source-child serializer, not hardware-authorized; preserve the verified 10/13 milestone.'
}
$summary|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $report -Encoding UTF8
# The full material/asset extraction was only a disposable source witness.
# Keep compact hashes and JSON copies; avoid accumulating duplicate OAT dumps.
Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
Write-Host '[AZTEC-GATE11-PROGRESS] [####################] 100% | typed original-PC cell topology & Xbox native ABI measured'
Write-Host "[AZTEC-GATE11-CELLS] World cells=$($summary.sourceWorldCellCount) AABB trees=$totalTrees portals=$totalPortals cell cull refs=$totalCull"
Write-Host "[AZTEC-GATE11-CELLS] Original-PC/Xbox sizes: GfxCell $($summary.originalPcCellBytes)/$($summary.nativeXboxCellBytes), Portal $($summary.originalPcPortalBytes)/$($summary.nativeXboxPortalBytes), AABB $($summary.originalPcAabbTreeBytes)/$($summary.nativeXboxAabbTreeBytes)"
Write-Host "[AZTEC-GATE11-CELLS] Cross-cell target ordinals unresolved=$unresolved; native ABI matches=$nativeAbiMeasured"
Write-Host "[AZTEC-GATE11-CELLS] Report=$report"
Write-Host '[AZTEC-GATE11-CELLS] NO production FF emission or hardware deployment. Gate11 pending; 10/13 preserved.'
if(-not $verifiedTopology){
    throw 'Typed source topology or Xbox cell ABI differs; inspect saved per-cell report before a serializer is attempted.'
}
