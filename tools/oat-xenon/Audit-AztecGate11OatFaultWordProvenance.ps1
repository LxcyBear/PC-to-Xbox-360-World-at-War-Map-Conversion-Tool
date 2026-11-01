[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# NO decompression, FF build, OAT rebuild, or map artifact mutation.
# Use the two independently saved reports to locate the REAL native OAT
# raw bad-pointer word in source-derived GfxCell/AABB serialized coordinates.
# Never assume the bad pointer's 0xC... top bits are a valid FF pointer.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$work=Join-Path $base 'pc_g11_cells_740'
$offlinePath=Join-Path $work 'gate11_failed_ff_pointer_layout_audit.json'
$nativePath=Join-Path $work 'oat_first_bad_pointer_trace\aztec_gate11_native_oat_first_bad_pointer.json'
$sourceManifestPath=Join-Path $base 'pc_g10_1903\gate11_cell_topology_source\aztec_gate11_pc_source_cells_native_abi.json'
$report=Join-Path $work 'oat_first_bad_pointer_trace\aztec_gate11_bad_pointer_source_field_provenance.json'
foreach($p in @($offlinePath,$nativePath,$sourceManifestPath)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Gate11 already-generated trace/source report missing: $p"}
}
$offline=Get-Content -LiteralPath $offlinePath -Raw|ConvertFrom-Json
$native=Get-Content -LiteralPath $nativePath -Raw|ConvertFrom-Json
$source=Get-Content -LiteralPath $sourceManifestPath -Raw|ConvertFrom-Json
if([string]$offline.schema -ne 'WaW360.AztecGate11FailedPointerOfflineZoneForensic.v1' -or
   [string]$offline.status -ne 'AZTEC_GATE11_FAILED_FF_OFFLINE_POINTER_LAYOUT_AUDIT_NOT_OAT' -or
   [int]$offline.matchedAabbTreeHeaders -ne 740 -or
   [int]$offline.matchedStaticModelIndexEntries -ne 14974 -or
   [int]$offline.matchedReflectionProbeIndexEntries -ne 11 -or
   -not [string]::IsNullOrWhiteSpace([string]$offline.firstSourceVsEmittedLayoutMismatch) -or
   [string]$native.schema -ne 'WaW360.AztecGate11OatFirstInvalidPointerTrace.v1' -or
   [string]$native.status -ne 'AZTEC_GATE11_FIRST_OAT_NATIVE_INVALID_POINTER_CONTEXT_CAPTURED_NOT_FIXED' -or
   [int]$native.nativeFaultingRootIndex -ne 7635 -or
   [int]$native.nativeFaultingTypeId -ne 18 -or
   [string]$native.nativeRawPointerAtInvalidOffset -notmatch '^0x[0-9A-Fa-f]{8}$' -or
   [string]$native.failedDiagnosticFastFileSha256 -ine [string]$offline.failedDiagnosticFfSha256 -or
   [string]$source.status -ne 'AZTEC_GATE11_11_CELL_740_AABB_TYPED_SOURCE_NATIVE_ABI_PASS_NOT_EMITTED' -or
   [int]$source.originalPcTypedAabbTreeCount -ne 740 -or
   [bool]$source.hardwareReady){
    throw 'Native first-fault, offline byte walk and original-PC source do not identify the same rejected 11-cell FF.'
}
$ffPath=[string]$offline.failedDiagnosticFf
$g10Path=Join-Path $base 'pc_g10_1903\oat_loaded_1903_draw_readback\aztec_gate10_independent_1903_draw_readback.json'
if(-not(Test-Path -LiteralPath $ffPath -PathType Leaf) -or
   -not(Test-Path -LiteralPath $g10Path -PathType Leaf) -or
   (Get-FileHash -LiteralPath $ffPath -Algorithm SHA256).Hash -ine
      [string]$native.failedDiagnosticFastFileSha256){
    throw 'Rejected FF provenance hash drifted.'
}
$g10=Get-Content -LiteralPath $g10Path -Raw|ConvertFrom-Json
$oldFF=[string]$g10.stage4EEmittedFf
if(-not(Test-Path -LiteralPath $oldFF -PathType Leaf) -or
   (Get-FileHash -LiteralPath $oldFF -Algorithm SHA256).Hash -ine
       [string]$g10.stage4EEmittedFfSha256 -or
   [string]$g10.stage4EEmittedFfSha256 -ine
       [string]$native.preservedGate10FastFileSha256){
    throw 'Preserved independently OAT-verified Gate10 FF changed.'
}
$typedPath=[string]$source.originalPcTypedCellAabbGraph
if(-not(Test-Path -LiteralPath $typedPath -PathType Leaf) -or
   (Get-FileHash -LiteralPath $typedPath -Algorithm SHA256).Hash -ine
       [string]$source.originalPcTypedCellAabbGraphSha256){
    throw 'PC-owned typed GfxCell/AABB source content hash drifted.'
}
$typed=Get-Content -LiteralPath $typedPath -Raw|ConvertFrom-Json
$cells=@($typed.cells)
$matches=@($offline.cellArrayCandidates | Where-Object {[int]$_.matchedCellHeaders -eq 11})
if($cells.Count -ne 11 -or $matches.Count -ne 1){
    throw 'Expected exactly one real 11x56B source-owned emitted GfxCell header array.'
}
$cellStart=[long]$matches[0].arrayStreamOffset
$cursor=$cellStart+616L
$rawWord=[Convert]::ToUInt32(([string]$native.nativeRawPointerAtInvalidOffset).Substring(2),16)
$float=[BitConverter]::ToSingle([BitConverter]::GetBytes($rawWord),0)
$streamPos=[long]$native.nativeStreamPositionAtInvalidPointer
$hits=New-Object 'System.Collections.Generic.List[object]'
function Check-Vector([object]$array,[long]$at,[string]$role,[int]$cellNo,[int]$treeNo){
    if(@($array).Count -ne 3){throw "Malformed PC-owned bounds $role"}
    for($axis=0;$axis -lt 3;$axis++){
        $coord=[single]$array[$axis]
        $bits=[BitConverter]::ToUInt32([BitConverter]::GetBytes($coord),0)
        if($bits -eq $rawWord){
            $absolute=$at+[long]$axis*4L
            $hits.Add([ordered]@{
                field=$role
                cellOrdinal=$cellNo
                treeOrdinal=$treeNo
                axis=$axis
                sourceFloat=$coord
                serializedZoneOffset=$absolute
                deltaFromOatStreamPosition=$absolute-$streamPos
            })
        }
    }
}
for($ci=0;$ci -lt 11;$ci++){
    $cell=$cells[$ci]
    if([int]$cell.ordinal -ne $ci){throw "Original-PC cell order mismatch at $ci"}
    $at=$cellStart+[long]$ci*56L
    Check-Vector $cell.mins $at 'GfxCell.mins' $ci -1
    Check-Vector $cell.maxs ($at+12L) 'GfxCell.maxs' $ci -1
}
$cellSections=New-Object 'System.Collections.Generic.List[object]'
$treeTotal=0;$indexTotal=0;$probeTotal=0
for($ci=0;$ci -lt 11;$ci++){
    $cell=$cells[$ci]
    $trees=@($cell.aabbTrees)
    $treeStart=$cursor
    for($ti=0;$ti -lt $trees.Count;$ti++){
        $tree=$trees[$ti]
        if([int]$tree.ordinal -ne $ti){throw "Original-PC tree order mismatch cell=$ci tree=$ti"}
        $at=$treeStart+[long]$ti*40L
        Check-Vector $tree.mins $at 'GfxAabbTree.mins' $ci $ti
        Check-Vector $tree.maxs ($at+12L) 'GfxAabbTree.maxs' $ci $ti
        $treeTotal++
    }
    $cursor += [long]$trees.Count*40L
    $indexStart=$cursor
    foreach($tree in $trees){
        $count=@($tree.smodelIndexes).Count
        if([int]$tree.smodelIndexCount -ne $count){throw "Tree $($tree.ordinal) index count changed"}
        $cursor += [long]$count*2L
        $indexTotal += $count
    }
    $probeStart=$cursor
    $probes=@($cell.reflectionProbeIndices).Count
    $cursor += $probes
    $probeTotal += $probes
    $cellSections.Add([ordered]@{
        cellOrdinal=$ci
        aabbTreeHeadersStart=$treeStart
        aabbTreeHeadersEndExclusive=$indexStart
        modelIndexBytesStart=$indexStart
        modelIndexBytesEndExclusive=$probeStart
        reflectionIndexBytesStart=$probeStart
        reflectionIndexBytesEndExclusive=$cursor
    })
}
if($treeTotal -ne 740 -or $indexTotal -ne 14974 -or $probeTotal -ne 11 -or
   $cursor -ne [long]$offline.lastWalkedZoneOffset){
    throw 'Offline byte-walk/typed source serialized offsets differ; do not infer bad-pointer provenance.'
}
$ordered=@($hits.ToArray()|Sort-Object @{Expression={[math]::Abs([double]$_.deltaFromOatStreamPosition)}})
$nearCurrent=@($cellSections.ToArray()|Where-Object {
    $streamPos -ge [long]$_.aabbTreeHeadersStart -and
    $streamPos -lt [long]$_.reflectionIndexBytesEndExclusive
})
$summary=[ordered]@{
    schema='WaW360.AztecGate11NativeBadPointerSourceFieldProvenance.v1'
    status='AZTEC_GATE11_BAD_POINTER_FLOAT_VS_LOADED_CELL_AABB_SOURCE_WITNESS_NOT_FIXED'
    nativeFirstFaultXAssetRoot=7635
    nativeFirstFaultTypeId=18
    nativeFirstFaultRawWord=('0x{0:X8}' -f $rawWord)
    rawWordAsBigEndianFloat=$float
    nativeStreamPosition=$streamPos
    serializedGfxCellHeaderStreamStart=$cellStart
    serializedGfxCellHeaderEndExclusive=$cellStart+616L
    serializedAabbChildrenEndExclusive=$cursor
    rawWordIsExactOwnedCellOrAabbCoordinate=$ordered.Count -gt 0
    occurrencesAsExactOriginalPcBounds=@($ordered)
    cellChildRegionAtOatStreamPosition=@($nearCurrent)
    cellChildSections=@($cellSections.ToArray())
    sourceFieldsProvenButOatRejectsFf=$true
    failedDiagnosticFfSha256=[string]$native.failedDiagnosticFastFileSha256
    preservedGate10FfSha256=[string]$native.preservedGate10FastFileSha256
    xboxHardwareApproved=$false
}|ConvertTo-Json -Depth 7
$summary|Set-Content -LiteralPath $report -Encoding UTF8
Write-Host '[AZTEC-GATE11-WORD] =============================================================='
Write-Host ('[AZTEC-GATE11-WORD] Real OAT raw={0} IEEE754BE={1} GfxWorld root=7635 streamPos={2}' -f ('0x{0:X8}' -f $rawWord),$float,$streamPos)
Write-Host "[AZTEC-GATE11-WORD] Emitted 11-cell headers [$cellStart,$($cellStart+616)); 740-tree region ends=$cursor"
Write-Host "[AZTEC-GATE11-WORD] Exact raw float in typed original-PC GfxCell/AABB bounds: $($ordered.Count) matches"
foreach($entry in @($ordered|Select-Object -First 12)){
    Write-Host ('[AZTEC-GATE11-WORD] match field={0}[cell={1},tree={2},axis={3}] zoneOffset={4} deltaToOatStream={5}' -f $entry.field,$entry.cellOrdinal,$entry.treeOrdinal,$entry.axis,$entry.serializedZoneOffset,$entry.deltaFromOatStreamPosition)
}
foreach($section in $nearCurrent){
    Write-Host "[AZTEC-GATE11-WORD] OAT stream position falls within emitted children of cell=$($section.cellOrdinal)"
}
Write-Host "[AZTEC-GATE11-WORD] Report=$report"
Write-Host '[AZTEC-GATE11-WORD] No OAT/FF build, no zone changes, no console test. The word is a CLUE, not a new pointer or hardware approval.'
