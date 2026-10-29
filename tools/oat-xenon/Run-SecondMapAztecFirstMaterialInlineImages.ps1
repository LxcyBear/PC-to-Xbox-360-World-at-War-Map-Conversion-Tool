[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$textureProof=Join-Path $base 'pc_gfxworld_first_material_children\aztec_pc_gfxworld_first_material_children.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$dir=Join-Path $base 'pc_first_material_inline_image_cursor'
$json=Join-Path $dir 'aztec_pc_first_material_inline_image_cursor.json'
$buildLog=Join-Path $dir 'build.log'
$runLog=Join-Path $dir 'probe.log'
foreach($p in @($geometry,$textureProof,$cli)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec first Material image-child prerequisite missing: $p"
    }
}
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometry -Raw)
$t=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $textureProof -Raw)
if([string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
    [bool]$g.XboxHardwareReady -or
    [string]$t.Schema -ne 'WaW360.AztecPcGfxWorldFirstMaterialChildren.v2' -or
    [string]$t.Status -ne 'AZTEC_PC_GFXWORLD_FIRST_MATERIAL_6X16_TEXTURE_TABLE_CANDIDATE' -or
    [int]$t.TextureDescriptorStride -ne 16 -or
    [int]$t.TextureImagePointerOffset -ne 12 -or
    [int]$t.BestValidTextureDescriptorCount -ne 6 -or
    @($t.FullyValidTextureTableSourceOffsets).Count -ne 1 -or
    [string]$t.FullyValidTextureTableSourceOffsets[0] -ne '0x0573187C' -or
    [bool]$t.OriginalMaterialOwnershipProven -or
    [bool]$t.All39SourceNamesProven -or
    [bool]$t.XboxMaterialBindingsProven -or
    [bool]$t.XboxHardwareReady){
    throw 'Source T4 112-byte first Material and 6x16 texture descriptor proof changed.'
}
$pc=[string]$g.OriginalPCFastFile
if(-not(Test-Path -LiteralPath $pc -PathType Leaf) -or
    (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$g.OriginalPCFastFileSha256){
    throw 'Original PC source Aztec fastfile SHA identity changed.'
}
New-Item -ItemType Directory -Path $dir -Force | Out-Null
Write-Host '[AZTEC-MAT-IMAGE] =================================================='
Write-Host '[AZTEC-MAT-IMAGE] FIRST MATERIAL INLINE IMAGE SOURCE CURSOR'
Write-Host '[AZTEC-MAT-IMAGE] 6 original PC textures: 3 FOLLOWING + 3 packed Virtual.'
Write-Host '[AZTEC-MAT-IMAGE] Exact 36-byte T4 GfxImage header, name/loadDef test.'
Write-Host '[AZTEC-MAT-IMAGE] No broad scans, no names/bindings assumed, NO FF.'
$previous=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$previous}
if($exit -ne 0){
    $errs=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 15)
    if($errs.Count -gt 0){
        foreach($e in $errs){Write-Host "[AZTEC-MAT-IMAGE] BUILD ERROR: $($e.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 20 | ForEach-Object {Write-Host "[AZTEC-MAT-IMAGE] BUILD: $_"}
    }
    throw "Aztec first Material image cursor .NET build failed; see $buildLog"
}
Write-Host '[AZTEC-MAT-IMAGE] Incremental .NET build PASS'
$previous=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $argsList=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-first-material-image-cursor',$pc,$textureProof,$json)
    & dotnet @argsList 2>&1 | ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$previous}
if($exit -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 30 | ForEach-Object {Write-Host "[AZTEC-MAT-IMAGE] TRACE: $_"}
    }
    throw "Aztec original PC first Material image source cursor failed exit=$exit; log=$runLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
$allowed=@(
    'AZTEC_PC_FIRST_MATERIAL_IMAGE_CHILD_INLINE_LOADDEF_CANDIDATE',
    'AZTEC_PC_FIRST_MATERIAL_IMAGE_CHILD_NAME_BOUNDARY_CANDIDATE',
    'AZTEC_PC_FIRST_MATERIAL_IMAGE_CHILD_HEADER_CANDIDATES',
    'AZTEC_PC_FIRST_MATERIAL_IMAGE_CHILD_BOUNDARY_UNRESOLVED'
)
if([string]$r.Schema -ne 'WaW360.AztecPcFirstMaterialInlineImageCursor.v1' -or
    [string]$r.Status -notin $allowed -or
    [int]$r.TexturesInMaterial -ne 6 -or
    [int]$r.FollowingImageReferences -ne 3 -or
    [int]$r.PackedImageReferences -ne 3 -or
    [string]$r.PhysicalCursorAfterTextureTable -ne '0x057318DC' -or
    @($r.TextureReferences).Count -ne 6 -or
    [bool]$r.ParentMaterialOwnedByGfxWorldProven -or
    [bool]$r.SourceMaterialNamesProven -or
    [bool]$r.XboxImageOrMaterialBindingProven -or
    [bool]$r.XboxHardwareReady){
    throw 'Aztec image cursor report or safety gates failed.'
}
foreach($t in @($r.TextureReferences)){
    Write-Host "[AZTEC-MAT-IMAGE] texture#$($t.Index) semantic=$($t.Semantic) image=$($t.SourceImagePointer) $($t.DecodedImagePointer)"
}
Write-Host "[AZTEC-MAT-IMAGE] plausibleHeader=$($r.PlausibleHeaderCandidates), inlineName=$($r.HeaderWithInlineNameCandidates), inlineLoadDef=$($r.HeaderWithInlineLoadDefCandidates)"
foreach($h in @($r.CandidateImageHeaders)){
    Write-Host "[AZTEC-MAT-IMAGE] header=$($h.PhysicalOffset) plausible=$($h.StructurePlausible) mapType=$($h.MapType) image=$($h.TexturePointer) size=$($h.Width)x$($h.Height)x$($h.Depth) semantic=$($h.Semantic) category=$($h.Category) name=$($h.NamePointer)"
    foreach($n in @($h.InlineNameCandidates)){
        Write-Host "[AZTEC-MAT-IMAGE]   NAME CANDIDATE $($n.PhysicalOffset)=$($n.UnverifiedName) (not identity proof)"
        foreach($d in @($n.LoadDefCandidates)){
            Write-Host "[AZTEC-MAT-IMAGE]   LOADDEF $($d.PhysicalOffset) fits=$($d.FitsImageDimensionsAndSourceBounds) size=$($d.ResourceSize) format=$($d.Format) end=$($d.EndOfPayload)"
        }
    }
}
Write-Host '[AZTEC-MAT-IMAGE] ================= RESULT ========================='
Write-Host "[AZTEC-MAT-IMAGE] status=$($r.Status)"
Write-Host "[AZTEC-MAT-IMAGE] report=$json"
Write-Host '[AZTEC-MAT-IMAGE] NO FF / NOT FOR XBOX / Gate 7 pending'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
