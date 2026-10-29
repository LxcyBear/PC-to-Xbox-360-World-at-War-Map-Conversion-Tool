[CmdletBinding()]
param(
    [switch]$BuildDumper
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$ledger=Join-Path $base 'pc_735_surface_material_candidate_ledger\aztec_pc_735_surface_material_candidate_ledger.json'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$dumper=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$capFile=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$out=Join-Path $base 'pc_oat_loaded_gfxworld_material_pointer_witness'
$dumpRoot=Join-Path $out 'oat_dump'
$runLog=Join-Path $out 'oat_unlinker.log'
$proofJson=Join-Path $out 'pc_oat_gfxworld_material_pointer_reconciliation.json'
foreach($f in @($ledger,$geometry)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){
        throw "Missing original-PC 735 surface/name candidate prerequisite: $f"
    }
}
$l=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $ledger -Raw)
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometry -Raw)
if([string]$l.Schema -ne 'WaW360.AztecPcWorldSurfaceMaterialCandidateLedger.v1' -or
   [string]$l.Status -ne 'AZTEC_PC_735_SURFACE_39_NAME_ORDINAL_CANDIDATE_LEDGER' -or
   [int]$l.SourceMaterialNameCandidates -ne 39 -or
   [int]$l.SourceSurfaces -ne 735 -or
   [int]$l.SourceTriangles -ne 19551 -or
   [int]$l.UniqueExactNativeNameCandidates -ne 39 -or
   [int]$l.HypotheticalSurfacesLabeled -ne 735 -or
   @($l.SurfaceCandidateRows).Count -ne 735 -or
   [bool]$l.VirtualAliasToMaterialNameProven -or
   [bool]$l.XboxMaterialBindingsProven -or
   [bool]$l.XboxHardwareReady -or
   [string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [int]$g.SurfaceCount -ne 735 -or
   [bool]$g.XboxHardwareReady){
    throw 'Original 39/39 names, 735/735 PC surfaces, and zero-binding inputs changed.'
}
$pc=[string]$g.OriginalPCFastFile
if(-not(Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$g.OriginalPCFastFileSha256){
    throw 'Original Aztec PC fastfile source SHA changed.'
}
$capability='t4-pc-gfxworld-loaded-material-pointer-witness-v1'
$installed=$false
if((Test-Path -LiteralPath $dumper -PathType Leaf) -and
   (Test-Path -LiteralPath $capFile -PathType Leaf)){
    $c=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $capFile -Raw)
    $installed=([string]$c.format -eq 'WaW360.OatGraphDumperCapabilities.v1' -and
        [string]$c.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($c.capabilities) -contains $capability)
}
if(-not $installed -and $BuildDumper){
    Write-Host '[AZTEC-OAT-POINTERS] Specialized graph dumper lacks the PC GfxWorld witness: one-time pinned OAT rebuild requested.'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1')
    if($LASTEXITCODE -ne 0 -or -not $?){
        throw 'One-time OAT PC GfxWorld witness build failed. Existing 735-surface source ledger remains unchanged.'
    }
    if(-not(Test-Path -LiteralPath $dumper -PathType Leaf) -or
       -not(Test-Path -LiteralPath $capFile -PathType Leaf)){
        throw 'Specialized graph Unlinker or capabilities were not produced.'
    }
    $c=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $capFile -Raw)
    $installed=[string]$c.format -eq 'WaW360.OatGraphDumperCapabilities.v1' -and
        @($c.capabilities) -contains $capability
}
if(-not $installed){
    throw "PC OAT loader witness not installed. Run this same script with -BuildDumper to build it once; capability=$capability"
}
New-Item -ItemType Directory -Path $dumpRoot -Force | Out-Null
Write-Host '[AZTEC-OAT-POINTERS] ======================================================'
Write-Host '[AZTEC-OAT-POINTERS] INDEPENDENT PC OAT-LOADER MATERIAL* POINTER WITNESS'
Write-Host '[AZTEC-OAT-POINTERS] Compare loaded GfxSurface.material against loaded MaterialMemory[].material.'
Write-Host '[AZTEC-OAT-POINTERS] Also test GfxSurface.material == ADDRESS of MaterialMemory row separately.'
Write-Host '[AZTEC-OAT-POINTERS] Read-only original PC FF. No Xenon surface rewrite / NO FF.'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & $dumper '--no-color' '--include-assets' 'gfxworld' '--output-folder' $dumpRoot $pc 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0){
    Write-Host '[AZTEC-OAT-POINTERS] OAT ERROR HEAD:'
    Get-Content -LiteralPath $runLog -TotalCount 12 | ForEach-Object {Write-Host "[AZTEC-OAT-POINTERS] TRACE $_"}
    Write-Host '[AZTEC-OAT-POINTERS] OAT ERROR TAIL:'
    Get-Content -LiteralPath $runLog -Tail 12 | ForEach-Object {Write-Host "[AZTEC-OAT-POINTERS] TRACE $_"}
    throw "PC original FF OAT material pointer witness dump failed exit=$code; log=$runLog"
}
$dir=Join-Path $dumpRoot 'pc_gfxworld_material_pointer_witness'
$files=@(Get-ChildItem -LiteralPath $dir -Filter '*.json' -File -ErrorAction SilentlyContinue)
if($files.Count -ne 1){
    $tail=Get-Content -LiteralPath $runLog -Tail 15
    $tail | ForEach-Object {Write-Host "[AZTEC-OAT-POINTERS] OAT: $_"}
    throw "Expected one loaded PC GfxWorld pointer witness JSON, found $($files.Count) in $dir"
}
$w=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $files[0].FullName -Raw)
if([string]$w.schema -ne 'WaW360.T4PcOatGfxWorldMaterialPointerWitness.v1'){
    throw 'OAT loaded GfxWorld witness version changed.'
}
$countsPass=[bool]$w.countsMatchAztec -and
    [int]$w.materialMemoryCount -eq 39 -and
    [int]$w.surfaceCount -eq 735
if(-not $countsPass -or -not [bool]$w.materialMemoryLoaded -or
   -not [bool]$w.surfaceArrayLoaded){
    Write-Host "[AZTEC-OAT-POINTERS] Loader cannot inspect fixed-up PC GfxWorld: $($w.status)."
    Write-Host "[AZTEC-OAT-POINTERS] Raw graph preserved at $($files[0].FullName)"
    throw 'PC OAT loader world counts/pointers unavailable; do not promote ordinal bindings.'
}
if(@($w.memoryRows).Count -ne 39 -or @($w.surfaces).Count -ne 735 -or
   [int]$w.directUniqueMaterialPointerMatches +
   [int]$w.ambiguousSurfaces + [int]$w.unmatchedSurfaces -ne 735 -or
   [int]$w.totalTriangles -ne 19551){
    throw 'PC OAT loaded MaterialMemory/GfxSurface cardinalities changed from original source.'
}
$verifiedSameOrdinal=0
$directButDifferentOrdinal=0
$nonUnique=0
$surfaceRows=@()
for($i=0; $i -lt 735; $i++){
    $o=$w.surfaces[$i]
    $p=$l.SurfaceCandidateRows[$i]
    if([int]$o.index -ne $i -or [int]$p.SurfaceIndex -ne $i -or
       [int]$o.triangleCount -ne [int]$p.Triangles){
        throw "Loaded original-PC GfxSurface index/triangles mismatch at index $i"
    }
    $same=$false
    $direct=([int]$o.directMatchingMemoryRows -eq 1)
    if($direct){
        $loadedRow=[int]$o.directMaterialRow
        $name=[string]$o.materialNameOnDirectMatch
        if($loadedRow -lt 0 -or $loadedRow -ge 39 -or
           [string]$w.memoryRows[$loadedRow].materialName -cne $name){
            throw "Loaded Material pointer/owner name mismatch at PC surface #$i"
        }
        $same=$loadedRow -eq [int]$p.OrdinalSlotHypothesis -and
            $name -ceq [string]$p.ProposedPcMaterialName
        if($same){$verifiedSameOrdinal++} else {$directButDifferentOrdinal++}
    }else{$nonUnique++}
    $surfaceRows+=([pscustomobject]@{
        surfaceIndex=$i
        triCount=[int]$o.triangleCount
        sourcePackedPointer=[string]$p.SourcePackedMaterialPointer
        candidateMemoryRow=[int]$p.OrdinalSlotHypothesis
        loadedDirectPointerMemoryRow=[int]$o.directMaterialRow
        loadedMemoryPointerMatches=[int]$o.directMatchingMemoryRows
        loadedMemoryStructAddressRow=[int]$o.memoryStructAddressRow
        loadedMaterialName=[string]$o.materialNameOnDirectMatch
        sameOrdinalAndName=$same
        xboxRootBindingProven=$false
    })
}
$namesSameOrder=0
for($i=0; $i -lt 39; $i++){
    if([string]$w.memoryRows[$i].materialName -ceq
       [string]$l.CandidateGroups[$i].SourceMaterialNameCandidate){
        $namesSameOrder++
    }
}
$status=if($verifiedSameOrdinal -eq 735){
    'AZTEC_PC_OAT_LOADER_735_TYPED_MATERIAL_POINTER_IDENTITIES_MATCH_ORDINAL_CANDIDATES'
}elseif([int]$w.directUniqueMaterialPointerMatches -eq 735){
    'AZTEC_PC_OAT_LOADER_735_MATERIAL_POINTERS_RESOLVED_NONORDINAL'
}else{
    'AZTEC_PC_OAT_LOADER_735_MATERIAL_POINTER_RELATIONSHIP_PARTIAL_OR_DIFFERENT'
}
$result=[ordered]@{
    schema='WaW360.AztecPcOatLoadedGfxWorldMaterialWitnessReconciliation.v1'
    status=$status
    originalPcFastFile=$pc
    sourceSha256=[string]$g.OriginalPCFastFileSha256
    oatGraphFile=$files[0].FullName
    loadedSurfaceCount=735
    loadedMaterialMemoryCount=39
    loadedTriangles=19551
    memoryRowsWithExactSourceCandidateName=$namesSameOrder
    loadedDirectPointerMatches=[int]$w.directUniqueMaterialPointerMatches
    loadedSurfacePointerEqualsMemoryStructAddress=[int]$w.memoryRecordAddressMatches
    independentlyVerifiedOrdinalAndNameSurfaces=$verifiedSameOrdinal
    directlyMatchedButDifferentOrdinal=$directButDifferentOrdinal
    unresolvedOrAmbiguous=$nonUnique
    xboxRootBindingsProven=0
    xboxHardwareReady=$false
    perSurface=$surfaceRows
    note='Post-loader PC OAT typed pointer equality is independent of raw-source 39-slot ordinal guess. When a surface directly matches MaterialMemory[i].material and its loaded name, that is PC pointer identity evidence only, NOT Xenon serializability or runtime Xbox root linkage. No surface buffers mutated and no FF emitted.'
}
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $proofJson -Encoding UTF8
Write-Host "[AZTEC-OAT-POINTERS] Loaded memory names in source material order=$namesSameOrder/39"
Write-Host "[AZTEC-OAT-POINTERS] Typed Material* direct unique matches=$($w.directUniqueMaterialPointerMatches)/735"
Write-Host "[AZTEC-OAT-POINTERS] Surface pointers equal memory RECORD address=$($w.memoryRecordAddressMatches)/735"
Write-Host "[AZTEC-OAT-POINTERS] Independent SAME ordinal + NAME=$verifiedSameOrdinal/735; different=$directButDifferentOrdinal; unresolved=$nonUnique"
Write-Host '[AZTEC-OAT-POINTERS] ================= RESULT ============================='
Write-Host "[AZTEC-OAT-POINTERS] status=$status"
Write-Host "[AZTEC-OAT-POINTERS] report=$proofJson"
Write-Host '[AZTEC-OAT-POINTERS] Even perfect PC pointer mapping is NOT an Xbox Material root binding or deployable GfxWorld.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
