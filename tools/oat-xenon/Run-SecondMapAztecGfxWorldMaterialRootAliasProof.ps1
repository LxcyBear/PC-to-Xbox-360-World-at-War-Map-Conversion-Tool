[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Exact original-PC GfxSurface Material pointer -> PC XAsset table check.
# Direct root pointer identity is strong evidence only if the 32-bit
# packed pointer is unique; OAT material list NAME order is not a root
# identity proof. Never bind by ordinal, guess, nearest match or donor.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$atlasPath=Join-Path $base 'pc_gfxworld_material_binding_atlas\aztec_pc_gfxworld_material_binding_atlas.json'
$geomPath=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$outDir=Join-Path $base 'pc_gfxworld_material_root_alias_proof'
$manifest=Join-Path $outDir 'original_pc_aztec_xasset_roots.json'
$runLog=Join-Path $outDir 'original_pc_manifest.log'
$json=Join-Path $outDir 'aztec_pc_gfxworld_material_root_alias_proof.json'
$txt=Join-Path $outDir 'aztec_pc_gfxworld_material_root_alias_proof.txt'
$csv=Join-Path $outDir 'aztec_39_pc_material_xasset_alias_cross_reference.csv'
foreach($file in @($atlasPath,$geomPath,$cli)){
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)){
        throw "Aztec GfxWorld PC Material root alias proof prerequisite missing: $file"
    }
}
$atlas=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $atlasPath -Raw)
$geom=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geomPath -Raw)
if([string]$atlas.schema -ne 'WaW360.AztecPcGfxWorldMaterialBindingAtlas.v1' -or
   [string]$atlas.status -ne 'AZTEC_PC_GFXWORLD_39_MATERIAL_GROUP_DEPENDENCY_ATLAS_PASS' -or
   [int]$atlas.originalPcPointerGroups -ne 39 -or
   [int]$atlas.totalSurfaceReferences -ne 735 -or
   [int]$atlas.totalTrianglesCovered -ne 19551 -or
   [int]$atlas.materialPointerGroupsBoundToXbox -ne 0 -or
   [bool]$atlas.readyForXbox -or
   [string]$geom.Schema -ne 'WaW360.AztecPcGfxWorldXenonGeometryComponents.v1' -or
   [string]$geom.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [int]$geom.DistinctPcMaterialPointers -ne 39 -or
   [bool]$geom.XboxHardwareReady){
    throw 'Original-PC GfxWorld 39-material group or three-buffer Xenon readback proof has drifted.'
}
$groups=@($atlas.materialReferenceGroups)
if($groups.Count -ne 39){throw "Atlas should contain exactly 39 original PC material groups, got $($groups.Count)"}
$pc=[string]$geom.OriginalPCFastFile
if(-not (Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$geom.OriginalPCFastFileSha256){
    throw 'Original PC Aztec map.ff compressed source SHA is not pinned.'
}
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
Write-Host '[AZTEC-MAT-ROOT] ======================================================'
Write-Host '[AZTEC-MAT-ROOT] 39 WORLD MATERIAL POINTERS VS ORIGINAL PC MATERIAL XASSET ROOTS'
Write-Host '[AZTEC-MAT-ROOT] Uses existing .NET manifest CLI; no build, no FF emit, no OAT dump.'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'run' '--no-build' '-c' 'Release' '--project' $cli '--' 'manifest' $pc $manifest 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $exitCode=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($exitCode -ne 0 -or -not (Test-Path -LiteralPath $manifest -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 18 | ForEach-Object {Write-Host "[AZTEC-MAT-ROOT] MANIFEST: $_"}
    }
    throw "Original PC XAsset table CLI manifest failed exit=$exitCode: $runLog"
}
$pcRoot=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $manifest -Raw)
if([string]$pcRoot.platform -ne 'PC' -or
   [string]$pcRoot.endianness -ne 'Little' -or
   [int]$pcRoot.xassetList.assetCount -ne @($pcRoot.assets).Count -or
   [int]$pcRoot.xassetList.assetCount -lt 4000 -or
   [string]$pcRoot.sourceFastFile -ine $pc){
    throw 'Original PC XAsset manifest platform/source/asset-count mismatch.'
}
$pcMats=@($pcRoot.assets | Where-Object {[string]$_.TypeName -ieq 'material'})
$aliasMats=@($pcMats | Where-Object {[string]$_.referenceKind -eq 'OffsetOrAlias'})
$inlineMats=@($pcMats | Where-Object {[string]$_.referenceKind -in @('InlineShared','Inline')})
$materialByPointer=@{}
$allByPointer=@{}
foreach($row in @($pcRoot.assets)){
    $key=([string]$row.rawHeader).ToUpperInvariant()
    if(-not $allByPointer.ContainsKey($key)){
        $allByPointer[$key]=New-Object 'System.Collections.Generic.List[object]'
    }
    $allByPointer[$key].Add($row)
    if([string]$row.TypeName -ieq 'material'){
        if(-not $materialByPointer.ContainsKey($key)){
            $materialByPointer[$key]=New-Object 'System.Collections.Generic.List[object]'
        }
        $materialByPointer[$key].Add($row)
    }
}
$rows=New-Object 'System.Collections.Generic.List[object]'
$totalTriangles=0
$totalSurfaces=0
$direct=0
$other=0
$ambiguous=0
foreach($group in $groups){
    $ptr=([string]$group.PcMaterialPointer).ToUpperInvariant()
    if($ptr -notmatch '^0X[0-9A-F]{8}$'){
        throw "Invalid source material packed pointer in atlas: $ptr"
    }
    $pcMaterialHits=if($materialByPointer.ContainsKey($ptr)){@($materialByPointer[$ptr].ToArray())}else{@()}
    $allHits=if($allByPointer.ContainsKey($ptr)){@($allByPointer[$ptr].ToArray())}else{@()}
    $status=if($pcMaterialHits.Count -eq 1){
        $direct++
        'EXACT_PC_MATERIAL_HEADER_POINTER'
    }elseif($pcMaterialHits.Count -gt 1){
        $ambiguous++
        'AMBIGUOUS_PC_MATERIAL_HEADERS'
    }elseif($allHits.Count -gt 0){
        $other++
        'MATCHED_NON_MATERIAL_XASSET_HEADER'
    }else{
        'NOT_A_TOP_LEVEL_XASSET_HEADER_POINTER'
    }
    $totalSurfaces += [int]$group.SurfaceCount
    $totalTriangles += [int]$group.TriangleCount
    $rows.Add([pscustomobject]@{
        PcMaterialPointer=$ptr
        PcVirtualBlock=[int]$group.SourceZoneBlock
        PcVirtualOffset=[string]$group.SourceBlockOffsetHex
        SurfaceCount=[int]$group.SurfaceCount
        TriangleCount=[int]$group.TriangleCount
        ExactMaterialRootHeaderMatches=$pcMaterialHits.Count
        AnyTypeRootHeaderMatches=$allHits.Count
        PcMaterialAssetIndex=$(if($pcMaterialHits.Count -eq 1){[int]$pcMaterialHits[0].Index}else{-1})
        MatchStatus=$status
        PcMaterialName=''
        XboxMaterialName=''
        XboxBound=$false
    })
}
if($rows.Count -ne 39 -or $totalSurfaces -ne 735 -or $totalTriangles -ne 19551){
    throw 'GfxWorld 39-pointer source topology changed since passing atlas.'
}
$ranked=@($rows.ToArray() | Sort-Object -Property @{Expression='TriangleCount';Descending=$true},PcMaterialPointer)
$ranked | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8
$report=[ordered]@{
    schema='WaW360.AztecPcGfxWorldMaterialRootAliasProof.v1'
    status='AZTEC_PC_GFXWORLD_39_POINTER_ROOT_CORRELATION_COMPLETE'
    originalPcFastFile=$pc
    originalPcFastFileSha256=[string]$geom.OriginalPCFastFileSha256
    pcAssetCount=[int]$pcRoot.xassetList.assetCount
    pcMaterialRootCount=$pcMats.Count
    pcMaterialInlineCount=$inlineMats.Count
    pcMaterialAliasCount=$aliasMats.Count
    originalPcGfxWorldMaterialPointers=39
    uniqueExactPcMaterialHeaderMatches=$direct
    ambiguousPcMaterialHeaderMatches=$ambiguous
    matchedOtherTypeRootHeaders=$other
    materialNamesProven=0
    xboxMaterialBindingsProven=0
    worldSurfaceReferences=735
    trianglesCovered=19551
    materialGroupRows=$ranked
    csv=$csv
    pcAssetManifest=$manifest
    gate7Complete=$false
    readyForXbox=$false
    note='Cross-references the 39 GfxSurface PC packed material pointers against ORIGINAL PC XAsset rawHeader values. Even unique exact pointer matches identify a PC root index, NOT its Material name/Xbox target; OAT list order is not proven equal to XAsset root order. This stage cannot complete the Xbox material binding gate.'
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $json -Encoding UTF8
@(
    "status=AZTEC_PC_GFXWORLD_39_POINTER_ROOT_CORRELATION_COMPLETE",
    "PC roots=$($report.pcAssetCount) Material roots=$($report.pcMaterialRootCount) inline=$($report.pcMaterialInlineCount) aliases=$($report.pcMaterialAliasCount)",
    "GfxWorld packed pointers=39. Exact PC Material root pointer matches=$direct, ambiguous=$ambiguous, other-type=$other, unmatched=$(39-$direct-$ambiguous-$other)",
    'Material names and Xbox identities remain unresolved until rooted by independent source evidence.',
    'Gate 7 remains PENDING. No Xbox FF.'
) | Set-Content -LiteralPath $txt -Encoding UTF8
Write-Host "[AZTEC-MAT-ROOT] Original PC XAssets=$($report.pcAssetCount), PC Material roots=$($pcMats.Count), inline=$($inlineMats.Count), alias=$($aliasMats.Count)"
Write-Host "[AZTEC-MAT-ROOT] Direct exact PC Material root pointer matches=$direct/39, ambiguous=$ambiguous, other-type=$other, no top-level header=$(39-$direct-$ambiguous-$other)"
foreach($row in @($ranked | Select-Object -First 12)){
    Write-Host "[AZTEC-MAT-ROOT] $($row.PcMaterialPointer) tris=$($row.TriangleCount) surfaces=$($row.SurfaceCount) $($row.MatchStatus) rootIndex=$($row.PcMaterialAssetIndex)"
}
Write-Host '[AZTEC-MAT-ROOT] ================= RESULT ============================'
Write-Host "[AZTEC-MAT-ROOT] status=$($report.status)"
Write-Host "[AZTEC-MAT-ROOT] report=$txt"
Write-Host '[AZTEC-MAT-ROOT] GATE 7 STILL PENDING. NO FF / NO XBOX.'
