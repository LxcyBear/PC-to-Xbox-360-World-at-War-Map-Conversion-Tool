[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$geometryPath=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$boundaryPath=Join-Path $base 'pc_gfxworld_material_inline_boundary\aztec_pc_gfxworld_material_inline_boundary.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_gfxworld_first_material_children'
$json=Join-Path $out 'aztec_pc_gfxworld_first_material_children.json'
$buildLog=Join-Path $out 'build.log'
$runLog=Join-Path $out 'probe.log'
foreach($f in @($geometryPath,$boundaryPath,$cli)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){
        throw "Aztec source first-Material child probe prerequisite missing: $f"
    }
}
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometryPath -Raw)
$boundary=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $boundaryPath -Raw)
if([string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
    [bool]$g.XboxHardwareReady -or
    [string]$boundary.Schema -ne 'WaW360.AztecPcGfxWorldMaterialInlineBoundary.v2' -or
    [string]$boundary.Status -ne 'AZTEC_PC_GFXWORLD_FIRST_INLINE_MATERIAL_112_ABI_STRUCTURAL_CANDIDATE' -or
    [bool]$boundary.WorldMaterialMemoryArrayOwnershipProven -or
    [bool]$boundary.SourceMaterialNamesProven -or
    [bool]$boundary.XboxMaterialBindingsProven -or
    [bool]$boundary.XboxHardwareReady){
    throw 'Aztec original PC 112-byte first Material structural proof changed.'
}
$pc=[string]$g.OriginalPCFastFile
if(-not (Test-Path -LiteralPath $pc -PathType Leaf) -or
    (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$g.OriginalPCFastFileSha256){
    throw 'Original PC source fastfile SHA identity changed.'
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
Write-Host '[AZTEC-MAT-CHILD] ====================================================='
Write-Host '[AZTEC-MAT-CHILD] ORIGINAL PC FIRST MATERIAL EXACT SOURCE CHILD LAYOUT'
Write-Host '[AZTEC-MAT-CHILD] Correct T4 ABI: 6 x 16B textures (image pointer +12), 6 x 32B constants (16-align), 4 x 8B statebits.'
Write-Host '[AZTEC-MAT-CHILD] Small alignment choices, next-header check. NO FF/XBOX.'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($code -ne 0){
    $errs=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 15)
    if($errs.Count -gt 0){
        foreach($e in $errs){Write-Host "[AZTEC-MAT-CHILD] BUILD ERROR: $($e.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-MAT-CHILD] BUILD: $_"}
    }
    throw "Aztec original PC first-Material child layout .NET build failed: $buildLog"
}
Write-Host '[AZTEC-MAT-CHILD] Incremental .NET build PASS'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $cmd=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-gfxworld-first-material-children',$pc,$boundaryPath,$json)
    & dotnet @cmd 2>&1 | ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($code -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-MAT-CHILD] TRACE: $_"}
    }
    throw "Aztec original PC first-Material child layout failed exit=$code; log=$runLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
$statuses=@(
    'AZTEC_PC_GFXWORLD_FIRST_MATERIAL_6X16_TEXTURE_TABLE_CANDIDATE',
    'AZTEC_PC_GFXWORLD_FIRST_MATERIAL_CHILD_LAYOUT_ONE_DIRECT_CANDIDATE',
    'AZTEC_PC_GFXWORLD_FIRST_MATERIAL_CHILD_LAYOUT_MULTIPLE_CANDIDATES',
    'AZTEC_PC_GFXWORLD_FIRST_MATERIAL_CHILD_LAYOUT_NOT_PROVEN'
)
if([string]$r.Schema -ne 'WaW360.AztecPcGfxWorldFirstMaterialChildren.v2' -or
    [string]$r.Status -notin $statuses -or
    [int]$r.MaterialHeaderBytes -ne 112 -or
    [int]$r.TextureCount -ne 6 -or
    [int]$r.ConstantCount -ne 6 -or
    [int]$r.StateBitsCount -ne 4 -or
    [int]$r.TextureDescriptorStride -ne 16 -or
    [int]$r.TextureImagePointerOffset -ne 12 -or
    [bool]$r.OriginalMaterialOwnershipProven -or
    [bool]$r.All39SourceNamesProven -or
    [bool]$r.XboxMaterialBindingsProven -or
    [bool]$r.XboxHardwareReady){
    throw 'Aztec first Material child report failed source or no-hardware safety gates.'
}
Write-Host "[AZTEC-MAT-CHILD] candidate header=$($r.FirstMaterialHeaderAt), ASCII=$($r.UnverifiedName) (UNPROVEN), afterName=$($r.ExactPostNameOffset)"
Write-Host "[AZTEC-MAT-CHILD] checkedAlignedLayouts=$($r.TestedAlignedChildLayouts), completeDirectCandidates=$($r.FullyCompatibleDirectLayouts)"
Write-Host "[AZTEC-MAT-CHILD] bestTextureDescriptors=$($r.BestValidTextureDescriptorCount)/6, valid6TextureTableStarts=$(@($r.FullyValidTextureTableSourceOffsets) -join ',')"
foreach($c in @($r.BestCandidates | Select-Object -First 8)){
    Write-Host "[AZTEC-MAT-CHILD] child texture=$($c.TextureTableAt), constant=$($c.ConstantTableAt), statebits=$($c.StateBitsTableAt), nextHeader=$($c.NextExpectedMaterialHeaderAt)"
    Write-Host "[AZTEC-MAT-CHILD]   validTexture=$($c.ValidTextureDescriptors)/6, plausibleConstants=$($c.PlausibleConstantRecords)/6, nextNameFollowing=$($c.NextMaterialHeaderNameFollowing), nextCounts=$($c.NextMaterialHeaderCountsPlausible)"
    foreach($t in @($c.TextureSamples)){
        Write-Host "[AZTEC-MAT-CHILD]   texture#$($t.Index) semantic=$($t.Semantic) mature=$($t.IsMatureContent) image=$($t.ImagePointer) plausible=$($t.Plausible)"
    }
}
Write-Host '[AZTEC-MAT-CHILD] ================= RESULT ==========================='
Write-Host "[AZTEC-MAT-CHILD] status=$($r.Status)"
Write-Host "[AZTEC-MAT-CHILD] report=$json"
Write-Host '[AZTEC-MAT-CHILD] NO FF / NOT FOR XBOX / Gate 7 pending.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
