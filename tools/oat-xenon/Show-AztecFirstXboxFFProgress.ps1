[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# These are explicit milestone gates, NOT equal-work estimates.
# Source discovery is easy to verify; the last world/runtime gates
# require independent emitter/OAT/hardware evidence before a PASS.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$gates=New-Object 'System.Collections.Generic.List[object]'
function Read-Report([string]$relativePath){
    $path=Join-Path $base $relativePath
    if(-not (Test-Path -LiteralPath $path -PathType Leaf)){return $null}
    try{return (ConvertFrom-Json -InputObject (Get-Content -LiteralPath $path -Raw))}
    catch{return $null}
}
function Add-Gate([string]$name,[bool]$passed,[string]$evidence){
    $gates.Add([pscustomobject]@{Name=$name;Passed=$passed;Evidence=$evidence})
}
$six=Read-Report 'pc_six_family_comworld_fullgraph_integration\aztec_pc_six_family_comworld_graph.json'
$indices=Read-Report 'pc_gfxworld_index_source_proof\aztec_pc_gfxworld_index_source_proof.json'
$verts=Read-Report 'pc_gfxworld_vertex_source_probe\aztec_pc_gfxworld_vertex_source_probe.json'
$surfaces=Read-Report 'pc_gfxworld_surface_source_probe\aztec_pc_gfxworld_surface_source_probe.json'
$mesh=Read-Report 'pc_gfxworld_source_mesh_export\aztec_pc_gfxworld_obj_export.json'
$converted=Read-Report 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$pcMaterialWitness=Read-Report 'pc_oat_loaded_gfxworld_material_pointer_witness\pc_oat_gfxworld_material_pointer_reconciliation.json'

$sixOk=$null -ne $six -and
    [string]$six.status -eq 'AZTEC_PC_SIX_FAMILY_COMWORLD_GRAPH_PASS' -and
    [int]$six.nativeRoots -eq 8509 -and
    -not [bool]$six.readyForXbox
Add-Gate 'Original PC + 8,509-root Xbox identity pinned' $sixOk 'Six-family source identity PASS'
Add-Gate 'Six original-PC asset families in OAT-valid scratch FF' (
    $sixOk -and [bool]$six.layoutReplayExact -and
    [bool]$six.oatZeroWarningsZeroErrors -and
    [int]$six.sourcePcComWorldByteReadback -eq 1 -and
    [int]$six.sourcePCMapEntsReadbacks -eq 1) 'Six-family OAT/layout/readback PASS'
Add-Gate '32,713 PC vertices + 58,653 PC indices recovered' (
    $null -ne $verts -and $null -ne $indices -and
    [string]$verts.Status -eq 'AZTEC_PC_GFXWORLD_32713_VERTEX_ARRAY_CANDIDATE_PASS' -and
    [string]$indices.status -eq 'AZTEC_PC_GFXWORLD_58653_INDEX_ARRAY_CANDIDATE_PASS' -and
    [int]$verts.SourcePCVertexCount -eq 32713 -and
    [int]$indices.originalPcIndexCount -eq 58653) 'Vertex and index source reports'
Add-Gate 'All 735 surfaces; 19,551 triangles and local index coverage' (
    $null -ne $surfaces -and
    [string]$surfaces.Status -eq 'AZTEC_PC_GFXWORLD_735_SURFACE_ARRAY_CANDIDATE_PASS' -and
    @($surfaces.TopCandidates | Where-Object {
        [int]$_.SourceOffset -eq 0x058E361E -and
        [int]$_.UncoveredIndexEntries -eq 0 -and
        [int]$_.IndexSpanOverlapEntries -eq 0 -and
        [int]$_.LocalIndicesInRange -eq 58653 -and
        [long]$_.TotalTriangleCount -eq 19551
    }).Count -eq 1) '52-byte GfxSurface full index-ownership PASS'
Add-Gate 'Full original-PC Aztec geometry exported to verified OBJ' (
    $null -ne $mesh -and
    [string]$mesh.Status -eq 'AZTEC_PC_GFXWORLD_19551_TRIANGLE_OBJ_EXPORT_PASS' -and
    [int]$mesh.TriangleCount -eq 19551 -and
    [int]$mesh.ReferencedVertexCount -eq 32713 -and
    [int]$mesh.DistinctPackedMaterialPointers -eq 39 -and
    [string]$mesh.ObjSha256 -eq 'D00AC821649A44BF552D7C61C173D7C25001F4F7DF2235CACDA97C382687A8EC') 'Source mesh file and manifest PASS'

# Gate 6 deliberately requires the successful PowerShell SHA/ledger
# audit marker, not merely the .NET JSON manifest. The first attempt
# produced valid bytes but failed the PS5.1 JSON-array gate.
$marker=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.txt'
$markerOk=Test-Path -LiteralPath $marker -PathType Leaf
if($markerOk){
    $markerOk=(Select-String -LiteralPath $marker -SimpleMatch 'status=AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -Quiet)
}
$convertedOk=$null -ne $converted -and
    [string]$converted.Status -eq 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -and
    [int]$converted.IndexCount -eq 58653 -and
    [int]$converted.VertexCount -eq 32713 -and
    [int]$converted.SurfaceCount -eq 735 -and
    [int]$converted.DistinctPcMaterialPointers -eq 39 -and
    [int]$converted.SurfacesRequiringMaterialBinding -eq 735 -and
    -not [bool]$converted.XenonGfxWorldRootEmitted -and
    -not [bool]$converted.OatGfxWorldBodyLoaded -and
    -not [bool]$converted.XboxHardwareReady
Add-Gate 'PC GfxWorld geometry -> Xenon-endian 3-buffer readback' (
    $convertedOk -and $markerOk) 'Three-buffer SHA/735-slot ledger runner PASS'

# Do not silently treat OAT identity or an empty GfxWorld shell as a
# converted runtime world. These later gates are fail-closed until a
# documented new proof/report is explicitly wired into this tracker.
Add-Gate 'Bind all 735 GfxSurface material refs to Xbox roots' $false 'PENDING actual Xenon root relocations and runtime graph closure'
Add-Gate 'Emit real GfxWorld root, DPVS and required children' $false 'PENDING source-backed Xbox world layout'
Add-Gate 'Integrated FF OAT pass + required runtime dependencies' $false 'PENDING first seven-family OAT validation'
Add-Gate 'Controlled Xbox smoke-test FF approved for deployment' $false 'PENDING safe-to-copy audit and load plan'

$passed=@($gates.ToArray() | Where-Object {$_.Passed}).Count
$total=$gates.Count
$percent=[int][Math]::Round(100.0*$passed/$total)
$segments=20
$filled=[int]($segments*$passed/$total)
$bar=('='*$filled)+('-'*($segments-$filled))
Write-Host '[AZTEC-FF-PROGRESS] ==================================================='
Write-Host "[AZTEC-FF-PROGRESS] FIRST CONTROLLED XBOX TEST: [$bar] $percent% ($passed/$total milestones)"
Write-Host '[AZTEC-FF-PROGRESS] Milestones count VERIFIED GATES, not time/effort or percentage of full conversion.'
for($i=0;$i -lt $gates.Count;$i++){
    $g=$gates[$i]
    $status=if($g.Passed){'PASS   '}else{'PENDING'}
    Write-Host ('[AZTEC-FF-PROGRESS] {0,2}. {1} | {2}' -f ($i+1),$status,$g.Name)
}
$pcWitnessPassed=$null -ne $pcMaterialWitness -and
    [string]$pcMaterialWitness.status -eq 'AZTEC_PC_OAT_LOADER_735_TYPED_MATERIAL_POINTER_IDENTITIES_MATCH_ORDINAL_CANDIDATES' -and
    [int]$pcMaterialWitness.loadedDirectPointerMatches -eq 735 -and
    [int]$pcMaterialWitness.independentlyVerifiedOrdinalAndNameSurfaces -eq 735 -and
    [int]$pcMaterialWitness.memoryRowsWithExactSourceCandidateName -eq 39 -and
    [int]$pcMaterialWitness.xboxRootBindingsProven -eq 0 -and
    -not [bool]$pcMaterialWitness.xboxHardwareReady
if($pcWitnessPassed){
    Write-Host '[AZTEC-FF-PROGRESS] PC MATERIAL IDENTITIES: 735/735 INDEPENDENT OAT TYPED POINTER PASS; Xbox Material root relocations still NOT written.'
}
Write-Host '[AZTEC-FF-PROGRESS] The existing 8,509-root FF remains diagnostic-only.'
Write-Host '[AZTEC-FF-PROGRESS] No Xbox FF release until all hardware-gating checks pass.'
Write-Host '[AZTEC-FF-PROGRESS] ==================================================='
Write-Progress -Activity 'Aztec first controlled Xbox FF milestone gates' -Completed
