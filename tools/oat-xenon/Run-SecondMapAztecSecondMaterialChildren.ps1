[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$prior=Join-Path $base 'pc_first_material_state_next_header\aztec_pc_first_material_state_next_header.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_second_material_children'
$json=Join-Path $out 'aztec_pc_second_material_children.json'
$buildLog=Join-Path $out 'build.log'
$runLog=Join-Path $out 'probe.log'
foreach($file in @($geometry,$prior,$cli)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){
        throw "Aztec second Material child cursor prerequisite missing: $file"
    }
}
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometry -Raw)
$p=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $prior -Raw)
if([string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [bool]$g.XboxHardwareReady -or
   [string]$p.Schema -ne 'WaW360.AztecPcFirstMaterialStateNextHeader.v1' -or
   [string]$p.Status -ne 'AZTEC_PC_FIRST_MATERIAL_STATEBITS_SECOND_HEADER_SHAPE_CANDIDATE' -or
   [string]$p.SecondMaterialHeaderAt -ne '0x05731AC1' -or
   [int]$p.SecondTextureCount -ne 6 -or
   [int]$p.SecondConstantCount -ne 6 -or
   [int]$p.SecondStateBitsCount -ne 8 -or
   [bool]$p.WorldMaterialMemoryOwnershipProven -or
   [bool]$p.All39MaterialNamesProven -or
   [bool]$p.XboxMaterialBindingsProven -or
   [bool]$p.XboxHardwareReady){
    throw 'Original-PC second Material source proof or no-hardware gates changed.'
}
$pc=[string]$g.OriginalPCFastFile
if(-not(Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$g.OriginalPCFastFileSha256){
    throw 'Original PC fastfile SHA identity changed.'
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
Write-Host '[AZTEC-MAT2] ========================================================='
Write-Host '[AZTEC-MAT2] SECOND ORIGINAL-PC MATERIAL SERIALIZED CHILD CURSOR'
Write-Host '[AZTEC-MAT2] Six texture descriptors at 0x05731B38; semantic 11 water fence.'
Write-Host '[AZTEC-MAT2] Up to six inline image/name/loadDef children; then six constants and eight states.'
Write-Host '[AZTEC-MAT2] Read-only, source-SHA checked. NO FF / NOT FOR XBOX.'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($code -ne 0){
    $errorsFound=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 15)
    if($errorsFound.Count -gt 0){
        foreach($e in $errorsFound){Write-Host "[AZTEC-MAT2] BUILD ERROR: $($e.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-MAT2] BUILD: $_"}
    }
    throw "Aztec second Material .NET build failed: $buildLog"
}
Write-Host '[AZTEC-MAT2] Incremental .NET build PASS'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $argsList=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-second-material-children',$pc,$prior,$json)
    & dotnet @argsList 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($code -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 30 | ForEach-Object {Write-Host "[AZTEC-MAT2] TRACE: $_"}
    }
    throw "Aztec second Material child cursor failed exit=$code; log=$runLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
$allowed=@(
    'AZTEC_PC_SECOND_MATERIAL_SIX_IMAGES_UNIQUE_CONSTANTS_CANDIDATE',
    'AZTEC_PC_SECOND_MATERIAL_SIX_IMAGES_SOURCE_CHAIN_CANDIDATE',
    'AZTEC_PC_SECOND_MATERIAL_PARTIAL_IMAGE_CHAIN_CANDIDATE',
    'AZTEC_PC_SECOND_MATERIAL_CHILD_CHAIN_UNRESOLVED'
)
if([string]$r.Schema -ne 'WaW360.AztecPcSecondMaterialChildren.v1' -or
   [string]$r.Status -notin $allowed -or
   [string]$r.SecondMaterialHeaderAt -ne '0x05731AC1' -or
   [string]$r.SecondTextureTableAt -ne '0x05731B38' -or
   @($r.Textures).Count -ne 6 -or
   @($r.InlineImageCandidates).Count -gt 6 -or
   [bool]$r.MaterialMemoryOwnershipProven -or
   [bool]$r.All39MaterialNamesProven -or
   [bool]$r.XboxMaterialBindingsProven -or
   [bool]$r.XboxHardwareReady){
    throw 'Second Material report failed source or hardware safety gates.'
}
foreach($t in @($r.Textures)){
    Write-Host "[AZTEC-MAT2] tex#$($t.Index) semantic=$($t.Semantic) water=$($t.IsWater) mature=$($t.IsMature) pointer=$($t.ImagePointer) plausible=$($t.DescriptorPlausible)"
}
Write-Host "[AZTEC-MAT2] allImageDescriptors=$($r.AllSixAreImageDescriptors)"
foreach($img in @($r.InlineImageCandidates)){
    Write-Host "[AZTEC-MAT2] image#$($img.Index) header=$($img.HeaderAt) plausible=$($img.HeaderPlausible) size=$($img.Width)x$($img.Height)x$($img.Depth) category=$($img.Category)"
    Write-Host "[AZTEC-MAT2]   name=$($img.NameCandidate) namePtr=$($img.NamePointer) loadDef=$($img.LoadDefAt)"
    Write-Host "[AZTEC-MAT2]   mip=$($img.MipLevelCount) format=$($img.FormatFourCC) resourceBytes=$($img.ResourceSize) dimensionsMatch=$($img.LoadDefDimensionsMatch) resourceFits=$($img.ResourceFits) after=$($img.AfterLoadDef)"
}
Write-Host "[AZTEC-MAT2] sixImageChain=$($r.SixInlineImageChainCandidate) postImages=$($r.PostSixImagesCursor)"
Write-Host "[AZTEC-MAT2] constantTrials=$($r.TestedConstantTableStarts) exactSixOfSix=$($r.FullyPlausibleConstantTableStarts)"
foreach($c in @($r.TopConstantTableCandidates | Select-Object -First 8)){
    Write-Host "[AZTEC-MAT2] constants=$($c.ConstantTableAt) delta=$($c.Delta) plausible=$($c.PlausibleConstants)/6 statebits=$($c.StateBitsAt) nextHeader=$($c.NextHeaderAt) following=$($c.NextNameFollowing) counts=$($c.NextCountsPlausible)"
    Write-Host "[AZTEC-MAT2]   names=$(@($c.CandidateNames) -join ' | ')"
}
Write-Host '[AZTEC-MAT2] ================= RESULT ================================'
Write-Host "[AZTEC-MAT2] status=$($r.Status)"
Write-Host "[AZTEC-MAT2] report=$json"
Write-Host '[AZTEC-MAT2] Structural candidates only. Gate 7 PENDING; no Xbox FF.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
