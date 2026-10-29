[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$meshPath=Join-Path $base 'pc_gfxworld_source_mesh_export\aztec_pc_gfxworld_obj_export.json'
$surfPath=Join-Path $base 'pc_gfxworld_surface_source_probe\aztec_pc_gfxworld_surface_source_probe.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$outDir=Join-Path $base 'pc_gfxworld_xenon_geometry_components'
$buildLog=Join-Path $outDir 'dotnet_build.log'
$runLog=Join-Path $outDir 'xenon_geometry_components.log'
$manifest=Join-Path $outDir 'gfxworld_source_to_xenon_geometry_components.json'
$txt=Join-Path $outDir 'gfxworld_source_to_xenon_geometry_components.txt'

foreach($p in @($meshPath,$surfPath,$cli)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec original-PC Xenon GfxWorld geometry source proof missing: $p"
    }
}
$mesh=Get-Content -LiteralPath $meshPath -Raw | ConvertFrom-Json
$surf=Get-Content -LiteralPath $surfPath -Raw | ConvertFrom-Json
if([string]$mesh.Schema -ne 'WaW360.AztecPcGfxWorldMeshExport.v1' -or
   [string]$mesh.Status -ne 'AZTEC_PC_GFXWORLD_19551_TRIANGLE_OBJ_EXPORT_PASS' -or
   [int]$mesh.TriangleCount -ne 19551 -or
   [int]$mesh.SurfaceCount -ne 735 -or
   [int]$mesh.DistinctPackedMaterialPointers -ne 39 -or
   [bool]$mesh.XboxHardwareReady -or
   [string]$surf.Status -ne 'AZTEC_PC_GFXWORLD_735_SURFACE_ARRAY_CANDIDATE_PASS' -or
   [bool]$surf.XboxHardwareReady){
    throw 'Existing PC Aztec geometry and material-pointer proof changed.'
}
$pc=[string]$mesh.OriginalPCFastFile
if(-not (Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine
     [string]$mesh.OriginalPCFastFileSha256){
    throw 'Aztec original PC map.ff SHA256 drifted.'
}
New-Item -Path $outDir -ItemType Directory -Force | Out-Null
Write-Host '[AZTEC-GFX-XENON] ===================================================='
Write-Host '[AZTEC-GFX-XENON] SOURCE-PC AZTEC GEOMETRY -> XENON BIG-ENDIAN BUFFERS'
Write-Host '[AZTEC-GFX-XENON] 58653 indices, 32713 vertices, 735 surfaces, 39 material pointer groups.'
Write-Host '[AZTEC-GFX-XENON] Intermediate scratch binary components only; NO FF / NO XBOX.'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $exitCode=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($exitCode -ne 0){
    if(Test-Path -LiteralPath $buildLog -PathType Leaf){
        $errs=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 15)
        if($errs.Count -gt 0){
            foreach($e in $errs){Write-Host "[AZTEC-GFX-XENON] BUILD ERROR: $($e.Line)"}
        }else{
            Get-Content -LiteralPath $buildLog -Tail 30 | ForEach-Object {Write-Host "[AZTEC-GFX-XENON] BUILD: $_"}
        }
    }
    throw "Xenon GfxWorld component codec failed .NET build: $buildLog"
}
Write-Host '[AZTEC-GFX-XENON] Incremental .NET build PASS'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'run' '--no-build' '-c' 'Release' '--project' $cli '--' 'aztec-pc-gfxworld-xenon-components' $pc $meshPath $outDir 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $exitCode=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($exitCode -ne 0 -or -not (Test-Path -LiteralPath $manifest -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 35 | ForEach-Object {Write-Host "[AZTEC-GFX-XENON] RUN: $_"}
    }
    throw "Xenon GfxWorld source geometry endian conversion failed exit=$exitCode. Log: $runLog"
}
$got=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
if([string]$got.Schema -ne 'WaW360.AztecPcGfxWorldXenonGeometryComponents.v1' -or
   [string]$got.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [int]$got.IndexCount -ne 58653 -or
   [int]$got.VertexCount -ne 32713 -or
   [int]$got.SurfaceCount -ne 735 -or
   [int]$got.TriangleCount -ne 19551 -or
   [int]$got.DistinctPcMaterialPointers -ne 39 -or
   [int]$got.SurfacesRequiringMaterialBinding -ne 735 -or
   [int]$got.SurfaceLocalIndicesChecked -ne 58653 -or
   [int]$got.ZeroMaterialPointerPlaceholders -ne 735 -or
   [bool]$got.XenonGfxWorldRootEmitted -or
   [bool]$got.OatGfxWorldBodyLoaded -or
   [bool]$got.XboxHardwareReady){
    throw 'Aztec Xbox-endian GfxWorld component manifest failed original source and hardware gating.'
}
$components=@(
    [pscustomobject]@{Path=[string]$got.XenonIndicesFile;Sha=[string]$got.XenonIndicesSha256;Size=117306},
    [pscustomobject]@{Path=[string]$got.XenonVerticesFile;Sha=[string]$got.XenonVerticesSha256;Size=1439372},
    [pscustomobject]@{Path=[string]$got.XenonSurfacesFile;Sha=[string]$got.XenonSurfacesSha256;Size=38220}
)
foreach($component in $components){
    $p=$component.Path
    if(-not (Test-Path -LiteralPath $p -PathType Leaf) -or
       [long](Get-Item -LiteralPath $p).Length -ne [long]$component.Size -or
       (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -ine [string]$component.Sha){
        throw "Xbox-endian GfxWorld component bytes or SHA mismatch: $p"
    }
}
$ledger=[string]$got.MaterialRelocationLedger
if(-not (Test-Path -LiteralPath $ledger -PathType Leaf)){
    throw "Missing original-PC material-pointer relocation ledger: $ledger"
}
# Windows PowerShell 5.1 sometimes passes a JSON root array through the
# ConvertFrom-Json pipeline as ONE nested object[] instead of 735 records.
# Normalize the JSON root explicitly, then verify EVERY original-PC pointer
# and all 735 unbound slots; do not rely on @(... | ConvertFrom-Json).Count.
$decodedLedger=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $ledger -Raw)
$refs=New-Object 'System.Collections.Generic.List[object]'
foreach($record in $decodedLedger){
    if($record -is [System.Array]){
        foreach($entry in $record){$refs.Add($entry)}
    }else{
        $refs.Add($record)
    }
}
$bound=0
$badRows=0
$seenPointers=New-Object 'System.Collections.Generic.HashSet[string]'
$seenIndices=New-Object 'System.Collections.Generic.HashSet[int]'
foreach($record in $refs){
    $i=[int]$record.SurfaceIndex
    $ptr=[string]$record.OriginalPcPackedPointer
    if($i -lt 0 -or $i -ge 735 -or
       -not $seenIndices.Add($i) -or
       $ptr -notmatch '^0x[0-9A-Fa-f]{8}$' -or
       -not [string]::IsNullOrEmpty([string]$record.XboxMaterialRootName)){
        $badRows++
    }
    [void]$seenPointers.Add($ptr.ToUpperInvariant())
    if([bool]$record.BoundToXboxMaterialRoot){$bound++}
}
Write-Host "[AZTEC-GFX-XENON] Ledger audit: rows=$($refs.Count)/735, uniqueSurfaceIndices=$($seenIndices.Count)/735, distinctPCMaterialPtrs=$($seenPointers.Count)/39, boundXboxMaterials=$bound/0, malformed=$badRows"
if($refs.Count -ne 735 -or
   $seenIndices.Count -ne 735 -or
   $seenPointers.Count -ne 39 -or
   $bound -ne 0 -or $badRows -ne 0){
    throw "Original-PC GfxWorld material relocation ledger gate failed: rows=$($refs.Count), indices=$($seenIndices.Count), pointerGroups=$($seenPointers.Count), bound=$bound, malformed=$badRows. Ledger=$ledger"
}
@(
  "status=AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS",
  "PC index 58653 x 2 -> Xenon BE 117306 bytes SHA=$($got.XenonIndicesSha256)",
  "PC vertex 32713 x 44 -> Xenon BE 1439372 bytes SHA=$($got.XenonVerticesSha256)",
  "PC surface 735 x 52 -> Xenon BE 38220 bytes SHA=$($got.XenonSurfacesSha256)",
  "Original PC packed material groups=39; 735 slots intentionally UNBOUND (0 in output surfaces)",
  "Source buffer SHA fences preserved, full numeric endian reversal verified.",
  "Not a game FF. No GfxWorld root, no OAT world load, not for Xbox.",
  "Output=$outDir"
) | Set-Content -LiteralPath $txt -Encoding UTF8
Write-Host '[AZTEC-GFX-XENON] ================= RESULT ==========================='
Write-Host "[AZTEC-GFX-XENON] status=$($got.Status)"
Write-Host "[AZTEC-GFX-XENON] PC indices=$($got.IndexCount)/58653, vertices=$($got.VertexCount)/32713, surfaces=$($got.SurfaceCount)/735"
Write-Host "[AZTEC-GFX-XENON] Material groups=$($got.DistinctPcMaterialPointers)/39, UNBOUND slots=$($got.ZeroMaterialPointerPlaceholders)/735"
Write-Host "[AZTEC-GFX-XENON] report=$txt"
Write-Host '[AZTEC-GFX-XENON] Next: real GfxWorld root + DPVS + Material links + OAT. NOT FOR XBOX.'
