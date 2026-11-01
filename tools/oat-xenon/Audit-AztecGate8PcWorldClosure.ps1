[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Read-only: inspect the pinned original-PC GfxWorld root/DPVS and the
# successful Xbox-format static-DPVS scratch OAT gate. Do not emit a FF.
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base = Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$seedPath = Join-Path $base 'pc_source_to_xenon_gfxworld_root_dpvs_scalar_seed\aztec_pc_gfxworld_800_to_xenon_900_root_dpvs_seed.json'
$passPath = Join-Path $base 'pc_735_39_gfxworld_integrated_ff_oat_run_probe_dpvs_static\aztec_pc_world_core_integrated_oat_run.json'
$out = Join-Path $base 'pc_gfxworld_gate8_source_closure_audit'
$reportPath = Join-Path $out 'aztec_gate8_pc_world_child_pointer_audit.json'

foreach($path in @($seedPath,$passPath)){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Gate 8 source audit prerequisite missing: $path"
    }
}
$seed = Get-Content -LiteralPath $seedPath -Raw | ConvertFrom-Json
$gate = Get-Content -LiteralPath $passPath -Raw | ConvertFrom-Json
if([string]$seed.Status -ne 'AZTEC_PC_GFXWORLD_800_TO_XBOX_900_ROOT_SCALAR_AND_DPVS_SEED_PASS_NOT_FF' -or
   [int]$seed.OriginalPcRootSize -ne 800 -or
   [int]$seed.DpvsSize -ne 100 -or
   [string]$gate.status -ne 'AZTEC_GFXWORLD_SOURCE_STATIC_DPVS_CHILD_PROBE_OAT_PASS' -or
   -not [bool]$gate.OatPass -or
   -not [bool]$gate.sourceBackedStaticDpvsProbe -or
   -not [bool]$gate.finalMaterialReferencesRebasedDuringCurrentFfEmit -or
   [bool]$gate.hardwareReady -or
   -not(Test-Path -LiteralPath ([string]$gate.outputFastFile) -PathType Leaf)){
    throw 'Gate 8 read-only audit requires the actual source-static-DPVS OAT PASS and pinned original-PC 800/100B seed.'
}
$witnessPath = [string]$seed.DualSourceNativeAbiWitness
$pcRootPath = [string]$seed.OriginalPcRoot800File
$pcDpvsPath = [string]$seed.OriginalPcDpvs100File
foreach($path in @($witnessPath,$pcRootPath,$pcDpvsPath)){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing original-PC source witness: $path"}
}
$witness = Get-Content -LiteralPath $witnessPath -Raw | ConvertFrom-Json
if([string]$witness.status -ne 'AZTEC_NATIVE_XBOX_GFXWORLD_ROOT_AND_DPVS_ABI_EXACT_SOURCE_WITNESS'){
    throw 'Original-PC vs Xenon ABI witness status changed.'
}
$pcAbi = $witness.originalPcGfxWorldRootAbi
$shape = $witness.originalPcLoadedWorldChildShape
if([int]$pcAbi.gfxWorldSize -ne 800 -or
   [int]$pcAbi.dpvsStaticSize -ne 100 -or
   [int]$shape.surfaceCount -ne 735 -or
   [int]$shape.staticSurfaceCount -ne 613 -or
   [int]$shape.materialMemoryCount -ne 39 -or
   [int]$shape.vertexCount -ne 32713 -or
   [int]$shape.indexCount -ne 58653){
    throw 'PC GfxWorld child census no longer matches the exact prior witness.'
}
$root = [IO.File]::ReadAllBytes($pcRootPath)
$dpvs = [IO.File]::ReadAllBytes($pcDpvsPath)
if($root.Length -ne 800 -or $dpvs.Length -ne 100 -or
   (Get-FileHash -LiteralPath $pcRootPath -Algorithm SHA256).Hash -ine [string]$seed.OriginalPcRootSha256 -or
   (Get-FileHash -LiteralPath $pcDpvsPath -Algorithm SHA256).Hash -ine [string]$seed.OriginalPcDpvsSha256){
    throw 'The original-PC GfxWorld root or embedded DPVS source SHA changed.'
}
if(-not [BitConverter]::IsLittleEndian){throw 'This audit requires a little-endian host.'}
function Read-U32([byte[]]$buffer,[int]$at){
    if($at -lt 0 -or $at -gt ($buffer.Length-4)){throw "Pointer field offset outside buffer: $at"}
    return [BitConverter]::ToUInt32($buffer,$at)
}
function Decode-Pointer([uint32]$raw){
    if($raw -eq 0){return 'NULL'}
    if($raw -eq 4294967295){return 'FOLLOWING'}
    if($raw -eq 4294967294){return 'INSERT'}
    $unbiased = [uint64]$raw-1
    $block = [int][Math]::Floor($unbiased / 536870912.0)
    $offset = $unbiased % [uint64]536870912
    return ('PACKED_BLOCK_{0}_0x{1:X8}' -f $block,$offset)
}
$rows = New-Object 'System.Collections.Generic.List[object]'
function Add-Pointer([string]$group,[string]$name,[byte[]]$buffer,[int]$offset,[string]$status){
    $raw = [uint32](Read-U32 $buffer $offset)
    $rows.Add([pscustomobject]@{
        group=$group
        field=$name
        byteOffset=$offset
        rawPointer=('0x{0:X8}' -f $raw)
        interpretation=(Decode-Pointer $raw)
        conversionStatus=$status
    })
}
$pcDpvsAt = [int]$pcAbi.dpvsStaticAt
$pcPlanesAt = [int]$pcAbi.dpvsPlanesAt
if($pcDpvsAt -lt 0 -or $pcDpvsAt+100 -gt $root.Length -or
   $pcPlanesAt -lt 0 -or $pcPlanesAt+16 -gt $root.Length){
    throw 'PC original GfxWorld nested DPVS offsets invalid.'
}
if([int](Read-U32 $root $pcPlanesAt) -ne [int]$shape.cellCount -or
   [int](Read-U32 $dpvs 0) -ne [int]$shape.smodelCount -or
   [int](Read-U32 $dpvs 4) -ne 613){
    throw 'Original PC root/DPVS nested counters disagree with independently loaded OAT witness.'
}
$names = @(
    @('name','nameAt','CORE_STRING_OAT_PASS'),
    @('baseName','baseNameAt','CORE_STRING_OAT_PASS'),
    @('indices','indicesAt','CORE_INDICES_OAT_PASS'),
    @('vd.vertices','vertexDataAt','CORE_VERTICES_OAT_PASS'),
    @('vld.data','vertexLayerDataAt','UNRESOLVED_NEEDS_SOURCE'),
    @('materialMemory','materialMemoryAt','CORE_MATERIALS_OAT_PASS')
)
foreach($entry in $names){
    $property=[string]$entry[1]
    if($null -eq $pcAbi.PSObject.Properties[$property]){
        throw "Missing measured PC root offset $property"
    }
    Add-Pointer 'GfxWorld' ([string]$entry[0]) $root ([int]$pcAbi.$property) ([string]$entry[2])
}
foreach($entry in @(
    @('dpvsPlanes.planes',4,'UNRESOLVED_NEEDS_SOURCE'),
    @('dpvsPlanes.nodes',8,'UNRESOLVED_NEEDS_SOURCE'),
    @('dpvsPlanes.sceneEntCellBits',12,'RUNTIME_BLOCK_REVIEW')
)){
    Add-Pointer 'GfxWorldDpvsPlanes' ([string]$entry[0]) $root ($pcPlanesAt+[int]$entry[1]) ([string]$entry[2])
}
foreach($entry in @(
    @('smodelVisData[0]',40,'RUNTIME_BLOCK_REVIEW'),
    @('smodelVisData[1]',44,'RUNTIME_BLOCK_REVIEW'),
    @('smodelVisData[2]',48,'RUNTIME_BLOCK_REVIEW'),
    @('surfaceVisData[0]',52,'RUNTIME_BLOCK_REVIEW'),
    @('surfaceVisData[1]',56,'RUNTIME_BLOCK_REVIEW'),
    @('surfaceVisData[2]',60,'RUNTIME_BLOCK_REVIEW'),
    @('lodData',64,'RUNTIME_BLOCK_REVIEW'),
    @('sortedSurfIndex',68,'STATIC_DPVS_OAT_PASS'),
    @('smodelInsts',72,'STATIC_DPVS_OAT_PASS'),
    @('surfaces',76,'CORE_SURFACES_OAT_PASS'),
    @('cullGroups',80,'UNRESOLVED_NEEDS_SOURCE'),
    @('smodelDrawInsts',84,'UNRESOLVED_NEEDS_SOURCE'),
    @('surfaceMaterials',88,'RUNTIME_BLOCK_REVIEW'),
    @('surfaceCastsSunShadow',92,'RUNTIME_BLOCK_REVIEW')
)){
    Add-Pointer 'GfxWorldDpvsStatic' ([string]$entry[0]) $dpvs ([int]$entry[1]) ([string]$entry[2])
}
# GfxWorldDpvsDynamic immediately follows the 100-byte static DPVS in
# the fixed PC struct; the native runtime block semantics still need
# independent Xenon proof, so these are read-only observations.
$dynamicAt=$pcDpvsAt+100
if($dynamicAt+48 -gt $root.Length){throw 'PC DPVS Dynamic 48-byte header outside root.'}
$dynCounts=[ordered]@{
    dynEntClientWordCount0=(Read-U32 $root ($dynamicAt+0))
    dynEntClientWordCount1=(Read-U32 $root ($dynamicAt+4))
    dynEntClientCount0=(Read-U32 $root ($dynamicAt+8))
    dynEntClientCount1=(Read-U32 $root ($dynamicAt+12))
}
for($i=0;$i -lt 2;$i++){
    Add-Pointer 'GfxWorldDpvsDynamic' ("dynEntCellBits[$i]") $root ($dynamicAt+16+$i*4) 'RUNTIME_BLOCK_REVIEW'
}
for($i=0;$i -lt 6;$i++){
    $client=[int][Math]::Floor($i/3)
    $vis=$i%3
    Add-Pointer 'GfxWorldDpvsDynamic' ("dynEntVisData[$client][$vis]") $root ($dynamicAt+24+$i*4) 'RUNTIME_BLOCK_REVIEW'
}
$unresolved=@($rows.ToArray() | Where-Object {
    $_.conversionStatus -eq 'UNRESOLVED_NEEDS_SOURCE' -and $_.interpretation -ne 'NULL'
})
$runtime=@($rows.ToArray() | Where-Object {
    $_.conversionStatus -eq 'RUNTIME_BLOCK_REVIEW' -and $_.interpretation -ne 'NULL'
})
New-Item -ItemType Directory -Force -Path $out | Out-Null
$report=[ordered]@{
    schema='WaW360.AztecGfxWorldGate8SourceClosureAudit.v1'
    status='AZTEC_PC_GFXWORLD_GATE8_SOURCE_POINTER_CENSUS_PASS_DIAGNOSTIC'
    sourceOatStaticDpvsPassReport=$passPath
    originalPcRootSha256=[string]$seed.OriginalPcRootSha256
    originalPcDpvsSha256=[string]$seed.OriginalPcDpvsSha256
    observedCounts=[ordered]@{
        planeCount=[int]$shape.planeCount
        nodeCount=[int]$shape.nodeCount
        cellCount=[int]$shape.cellCount
        smodelCount=[int]$shape.smodelCount
        staticSurfaceCount=613
        surfaceCount=735
        materialMemoryCount=39
        smodelVisDataCount=[int]$shape.smodelVisDataCount
        surfaceVisDataCount=[int]$shape.surfaceVisDataCount
    }
    dynamicCounts=$dynCounts
    pointerFields=$rows.ToArray()
    unresolvedNonNullPointerFields=$unresolved.Count
    runtimeNonNullPointerFields=$runtime.Count
    completeWorldDpvs=$false
    readyForXbox=$false
    note='Read-only pointer-state census from SHA-pinned original PC 800B GfxWorld and 100B DPVS. Packed offsets are not physical source byte positions. Source pointers or zero values alone do not establish Xenon child completeness. No FF emitted and no gate advanced.'
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Host '[AZTEC-GATE8-AUDIT] ======================================================='
Write-Host '[AZTEC-GATE8-AUDIT] PREVIOUS SOURCE-STATIC-DPVS OAT PASS VERIFIED'
Write-Host ('[AZTEC-GATE8-AUDIT] planes={0} nodes={1} cells={2} smodels={3} sortedSurfaces=613' -f $shape.planeCount,$shape.nodeCount,$shape.cellCount,$shape.smodelCount)
Write-Host ('[AZTEC-GATE8-AUDIT] non-null unresolved source pointers={0}; non-null runtime-class pointers={1}' -f $unresolved.Count,$runtime.Count)
foreach($item in $unresolved){
    Write-Host ('[AZTEC-GATE8-AUDIT] NEXT SOURCE {0}: {1}' -f $item.field,$item.interpretation)
}
Write-Host "[AZTEC-GATE8-AUDIT] report=$reportPath"
Write-Host '[AZTEC-GATE8-AUDIT] SOURCE CENSUS ONLY. NO OAT REBUILD, NO FF, NO XBOX.'
Write-Host '[AZTEC-GATE8-AUDIT] ======================================================='
