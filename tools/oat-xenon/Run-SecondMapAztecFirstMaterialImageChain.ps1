[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$first=Join-Path $base 'pc_first_image_loaddef_boundary\aztec_pc_first_image_loaddef_boundary.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_first_material_image_chain'
$json=Join-Path $out 'aztec_pc_first_material_image_chain.json'
$buildLog=Join-Path $out 'build.log'
$runLog=Join-Path $out 'probe.log'
foreach($p in @($geometry,$first,$cli)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec first Material image chain prerequisite missing: $p"
    }
}
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometry -Raw)
$prior=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $first -Raw)
if([string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [bool]$g.XboxHardwareReady -or
   [string]$prior.Schema -ne 'WaW360.AztecPcFirstImageLoadDefBoundary.v1' -or
   [string]$prior.Status -ne 'AZTEC_PC_FIRST_IMAGE_DXT5_ZERO_PAYLOAD_BOUNDARY_CANDIDATE' -or
   [string]$prior.CandidatePhysicalCursorAfterLoadDef -ne '0x05731937' -or
   [bool]$prior.PhysicalChildOwnershipProven -or
   [bool]$prior.All39MaterialNamesProven -or
   [bool]$prior.XboxBindingsProven -or
   [bool]$prior.XboxHardwareReady){
    throw 'Original PC LoadDef boundary proof changed or hardware readiness unexpectedly asserted.'
}
$pc=[string]$g.OriginalPCFastFile
if(-not(Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$g.OriginalPCFastFileSha256){
    throw 'Original PC Aztec fastfile identity changed.'
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
Write-Host '[AZTEC-MAT-CHAIN] ======================================================'
Write-Host '[AZTEC-MAT-CHAIN] FIRST PC MATERIAL: IMAGE #2/#3 EXACT CHILD CURSOR'
Write-Host '[AZTEC-MAT-CHAIN] Follow second image -> inline name -> LoadDef -> possible third image.'
Write-Host '[AZTEC-MAT-CHAIN] After coherent 3-image chain, test nearby 16-aligned six-constant layouts.'
Write-Host '[AZTEC-MAT-CHAIN] SHA-pinned, source-only, NO FF / NO XBOX.'
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
        foreach($e in $errorsFound){Write-Host "[AZTEC-MAT-CHAIN] BUILD ERROR: $($e.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-MAT-CHAIN] BUILD: $_"}
    }
    throw "Aztec image chain diagnostic build failed: $buildLog"
}
Write-Host '[AZTEC-MAT-CHAIN] Incremental .NET build PASS'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $argsList=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-first-material-image-chain',$pc,$first,$json)
    & dotnet @argsList 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($code -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 30 | ForEach-Object {Write-Host "[AZTEC-MAT-CHAIN] TRACE: $_"}
    }
    throw "Aztec image chain diagnostic failed exit=$code : $runLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
$allowed=@(
    'AZTEC_PC_FIRST_MATERIAL_THREE_IMAGE_SERIALIZATION_CHAIN_CANDIDATE',
    'AZTEC_PC_FIRST_MATERIAL_SECOND_IMAGE_HEADER_CANDIDATE',
    'AZTEC_PC_FIRST_MATERIAL_NEXT_IMAGE_CHAIN_UNRESOLVED'
)
if([string]$r.Schema -ne 'WaW360.AztecPcFirstMaterialImageChain.v1' -or
   [string]$r.Status -notin $allowed -or
   [string]$r.FirstImageHeaderAt -ne '0x057318DC' -or
   [string]$r.SecondImageHeaderAt -ne '0x05731937' -or
   [int]$r.ExpectedFollowingImageReferences -ne 3 -or
   @($r.AdditionalImageCandidates).Count -lt 1 -or
   @($r.AdditionalImageCandidates).Count -gt 2 -or
   [bool]$r.MaterialMemoryOwnershipProven -or
   [bool]$r.All39SourceNamesProven -or
   [bool]$r.XboxMaterialBindingsProven -or
   [bool]$r.XboxHardwareReady){
    throw 'Aztec three-image diagnostic report failed fail-closed source/hardware gate.'
}
foreach($i in @($r.AdditionalImageCandidates)){
    Write-Host "[AZTEC-MAT-CHAIN] IMAGE#$($i.ImageIndex) header=$($i.HeaderAt) plausible=$($i.ImageHeaderPlausible) size=$($i.Width)x$($i.Height)x$($i.Depth) type=$($i.MapType) category=$($i.Category)"
    Write-Host "[AZTEC-MAT-CHAIN]   texture=$($i.TexturePointer) namePtr=$($i.NamePointer) nameCandidate=$($i.CandidateName) nameAt=$($i.NameAt)"
    Write-Host "[AZTEC-MAT-CHAIN]   loadDef=$($i.CandidateLoadDefAt) mip=$($i.MipLevelCount) format=$($i.FormatFourCC) resourceBytes=$($i.LoadDefResourceBytes)"
    Write-Host "[AZTEC-MAT-CHAIN]   dimensionsMatch=$($i.LoadDefPrefixDimensionsMatch) resourceFits=$($i.ResourceFitsSource) recognizedDXT=$($i.FormatIsDXT1DXT3OrDXT5) nextCursor=$($i.CandidateAfterLoadDef)"
}
Write-Host "[AZTEC-MAT-CHAIN] threeImageChainCandidate=$($r.ThreeImageSerializationChainCandidate) afterThird=$($r.HypotheticalPostThirdImageCursor)"
foreach($c in @($r.NearbyAlignedConstantTableCandidates)){
    Write-Host "[AZTEC-MAT-CHAIN] CONSTANTS $($c.ConstantTableAt) plausible=$($c.PlausibleConstants)/6 statebits=$($c.StateBitsTableAt) nextHeader=$($c.HypotheticalNextMaterialAt) nextNameFollowing=$($c.NextNameFollowing) nextCounts=$($c.NextCountsPlausible)"
}
Write-Host '[AZTEC-MAT-CHAIN] ================= RESULT ============================'
Write-Host "[AZTEC-MAT-CHAIN] status=$($r.Status)"
Write-Host "[AZTEC-MAT-CHAIN] report=$json"
Write-Host '[AZTEC-MAT-CHAIN] No material identity or physical ownership proven. Gate 7 PENDING.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
