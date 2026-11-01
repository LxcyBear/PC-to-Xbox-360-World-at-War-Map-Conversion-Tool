[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Material identity dependency frontier, NOT a Material graft.
# Correlates all 735 original-PC GfxSurface material pointers to actual
# BE surface triangle data, and inventories the authoritative native
# Xbox Material root names without asserting unproven identity matches.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$geometryPath=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$geometryMarker=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.txt'
$identityPath=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$sixPath=Join-Path $base 'pc_six_family_comworld_fullgraph_integration\aztec_pc_six_family_comworld_graph.json'
$outDir=Join-Path $base 'pc_gfxworld_material_binding_atlas'
$reportPath=Join-Path $outDir 'aztec_pc_gfxworld_material_binding_atlas.json'
$groupCsv=Join-Path $outDir 'aztec_39_pc_world_material_pointer_groups.csv'
$nativeCsv=Join-Path $outDir 'aztec_native_xbox_material_root_catalog.csv'
$summaryTxt=Join-Path $outDir 'aztec_pc_gfxworld_material_binding_atlas.txt'

foreach($file in @($geometryPath,$geometryMarker,$identityPath,$sixPath)){
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)){
        throw "Aztec source-to-Xenon material atlas prerequisite missing: $file"
    }
}
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometryPath -Raw)
$id=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $identityPath -Raw)
$six=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $sixPath -Raw)
if([string]$g.Schema -ne 'WaW360.AztecPcGfxWorldXenonGeometryComponents.v1' -or
   [string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [int]$g.IndexCount -ne 58653 -or
   [int]$g.SurfaceCount -ne 735 -or
   [int]$g.DistinctPcMaterialPointers -ne 39 -or
   [int]$g.ZeroMaterialPointerPlaceholders -ne 735 -or
   [bool]$g.XboxHardwareReady -or
   [bool]$g.XenonGfxWorldRootEmitted -or
   [string]$six.status -ne 'AZTEC_PC_SIX_FAMILY_COMWORLD_GRAPH_PASS' -or
   [int]$six.nativeRoots -ne 8509 -or
   [bool]$six.readyForXbox -or
   -not [bool]$id.TypeSequenceVerified -or
   @($id.Entries).Count -ne 8509 -or
   -not (Select-String -LiteralPath $geometryMarker -SimpleMatch 'status=AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -Quiet)){
    throw 'Require original-PC geometry SHA/readback and verified 8509 native-root identity before atlas.'
}
$ledger=[string]$g.MaterialRelocationLedger
$surfaceFile=[string]$g.XenonSurfacesFile
foreach($file in @($ledger,$surfaceFile)){
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)){
        throw "Required material pointer ledger or BE surfaces missing: $file"
    }
}
$b=[IO.File]::ReadAllBytes($surfaceFile)
if($b.Length -ne 735*52 -or
   (Get-FileHash -LiteralPath $surfaceFile -Algorithm SHA256).Hash -ine [string]$g.XenonSurfacesSha256){
    throw 'Original-PC material atlas BE surface buffer SHA or exact length changed.'
}
$decoded=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $ledger -Raw)
$slots=New-Object 'System.Collections.Generic.List[object]'
foreach($record in $decoded){
    if($record -is [System.Array]){
        foreach($nested in $record){$slots.Add($nested)}
    }else{$slots.Add($record)}
}
if($slots.Count -ne 735){throw "Source pointer relocation ledger has $($slots.Count) rather than 735 entries."}
function U16BE([byte[]]$bytes,[int]$off){
    return (([int]$bytes[$off] -shl 8) -bor [int]$bytes[$off+1])
}
function U32BE([byte[]]$bytes,[int]$off){
    return (([long]$bytes[$off] -shl 24) -bor
        ([long]$bytes[$off+1] -shl 16) -bor
        ([long]$bytes[$off+2] -shl 8) -bor [long]$bytes[$off+3])
}
$byPointer=@{}
$seen=New-Object 'System.Collections.Generic.HashSet[int]'
$totalTris=0
foreach($slot in $slots){
    $index=[int]$slot.SurfaceIndex
    $ptr=[string]$slot.OriginalPcPackedPointer
    if($index -lt 0 -or $index -ge 735 -or
       -not $seen.Add($index) -or
       $ptr -notmatch '^0x[0-9A-Fa-f]{8}$' -or
       [bool]$slot.BoundToXboxMaterialRoot -or
       -not [string]::IsNullOrWhiteSpace([string]$slot.XboxMaterialRootName)){
        throw "Invalid or prematurely bound original-PC GfxSurface material relocation record index=$index pointer=$ptr"
    }
    $ptr=$ptr.ToUpperInvariant()
    $base=$index*52
    if((U32BE $b ($base+20)) -ne 0){throw "Unexpected non-null Xenon material pointer in GfxSurface #$index"}
    $tris=U16BE $b ($base+10)
    if($tris -le 0 -or $tris -gt 19551){throw "Invalid triangles in GfxSurface #$index"}
    $totalTris+=$tris
    if(-not $byPointer.ContainsKey($ptr)){
        $raw=[Convert]::ToUInt32($ptr.Substring(2),16)
        if($raw -eq 0 -or $raw -ge [uint32]::Parse('FFFFFFFE',[Globalization.NumberStyles]::HexNumber)){
            throw "Original-PC Material pointer is a null/following sentinel, not a packed identity: $ptr"
        }
        $minus=[long]$raw - 1L
        $block=[int]($minus -shr 29)
        $offset=[long]($minus -band 0x1FFFFFFFL)
        if($block -gt 6){throw "Original-PC packed material pointer block outside T4 range: $ptr"}
        $byPointer[$ptr]=[pscustomobject]@{
            PcMaterialPointer=$ptr
            SourceZoneBlock=$block
            SourceBlockOffsetHex=('0x{0:X8}' -f $offset)
            SurfaceCount=0
            TriangleCount=0
            PCMaterialName=''
            XboxMaterialRootName=''
            XboxNativeRootIndex=-1
            IdentityProven=$false
            BoundToXboxMaterialRoot=$false
        }
    }
    $byPointer[$ptr].SurfaceCount++
    $byPointer[$ptr].TriangleCount+=$tris
}
if($seen.Count -ne 735 -or $totalTris -ne 19551 -or $byPointer.Count -ne 39){
    throw "Source GfxWorld material topology drift: surfaces=$($seen.Count) groups=$($byPointer.Count) tris=$totalTris"
}
$nativeRoots=@($id.Entries | Where-Object {
    ([string]$_.TypeName).Replace('_','').Replace('-','').ToLowerInvariant() -eq 'material'
})
if($nativeRoots.Count -lt 39){throw "Fewer than 39 native Material roots: $($nativeRoots.Count)"}
$nativeRows=@($nativeRoots | ForEach-Object {
    [pscustomobject]@{
        NativeRootIndex=[int]$_.Index
        Name=[string]$_.Name
        Owned=(-not ([string]$_.Name).StartsWith(','))
        # A root name alone is NOT evidence that any PC virtual
        # pointer refers to that Xbox Material identity.
        PCWorldPointerMapped=$false
    }
})
$groups=@($byPointer.Values | Sort-Object -Property @{Expression='TriangleCount';Descending=$true},PcMaterialPointer)
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$groups | Export-Csv -LiteralPath $groupCsv -NoTypeInformation -Encoding UTF8
$nativeRows | Export-Csv -LiteralPath $nativeCsv -NoTypeInformation -Encoding UTF8
$report=[ordered]@{
    schema='WaW360.AztecPcGfxWorldMaterialBindingAtlas.v1'
    status='AZTEC_PC_GFXWORLD_39_MATERIAL_GROUP_DEPENDENCY_ATLAS_PASS'
    originalPcPointerGroups=39
    totalSurfaceReferences=735
    totalTrianglesCovered=19551
    nativeXboxMaterialRootCount=$nativeRows.Count
    nativeXboxOwnedMaterialCount=@($nativeRows | Where-Object {$_.Owned}).Count
    sourcePcMaterialNamesResolved=0
    materialPointerGroupsBoundToXbox=0
    materialSurfaceSlotsBoundToXbox=0
    materialReferenceGroups=$groups
    materialGroupCsv=$groupCsv
    nativeRootCsv=$nativeCsv
    fullMaterialClosure=$false
    readyForXbox=$false
    note='39 source packed pointers are decoded to PC block offsets and 735 source surface usages. Native Xbox Material names are listed separately; no source pointer-to-name identity has been proven, so this report grants NO material bindings and does NOT complete milestone 7.'
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath -Encoding UTF8
@(
    'Aztec original PC GfxWorld 39-material dependency atlas PASS',
    "Source material groups=$($groups.Count), GfxSurface references=$($seen.Count), triangles=$totalTris",
    "Native Xbox material roots=$($nativeRows.Count), owned=$($report.nativeXboxOwnedMaterialCount)",
    'PC pointer-to-material NAME mapping remains UNRESOLVED. All 735 Xbox surface material pointers remain 0.',
    'This is a source-first binding worksheet for further exact identity proof; NOT a GfxWorld FF and NOT milestone 7.',
    "Groups CSV=$groupCsv",
    "Native root catalog=$nativeCsv"
) | Set-Content -LiteralPath $summaryTxt -Encoding UTF8
Write-Host '[AZTEC-MAT-ATLAS] ====================================================='
Write-Host '[AZTEC-MAT-ATLAS] ALL 735 SURFACES / 39 SOURCE PC GFXWORLD MATERIAL POINTERS'
Write-Host "[AZTEC-MAT-ATLAS] original triangles=$totalTris; distinct groups=$($groups.Count); Xbox material root identities=$($nativeRows.Count)"
foreach($group in @($groups | Select-Object -First 10)){
    Write-Host "[AZTEC-MAT-ATLAS] group $($group.PcMaterialPointer) block=$($group.SourceZoneBlock) virtual=$($group.SourceBlockOffsetHex) surfaces=$($group.SurfaceCount) triangles=$($group.TriangleCount)"
}
Write-Host '[AZTEC-MAT-ATLAS] SOURCE MATERIAL NAMES UNRESOLVED (not assigned automatically).'
Write-Host '[AZTEC-MAT-ATLAS] ================= RESULT ============================'
Write-Host '[AZTEC-MAT-ATLAS] status=AZTEC_PC_GFXWORLD_39_MATERIAL_GROUP_DEPENDENCY_ATLAS_PASS'
Write-Host "[AZTEC-MAT-ATLAS] report=$summaryTxt"
Write-Host '[AZTEC-MAT-ATLAS] No FF. No material identity invented. NOT FOR XBOX.'
