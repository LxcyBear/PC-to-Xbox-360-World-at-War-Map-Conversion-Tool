[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$constants=Join-Path $base 'pc_post_three_images_constant_table\aztec_pc_post_three_images_constant_table.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_first_material_state_next_header'
$json=Join-Path $out 'aztec_pc_first_material_state_next_header.json'
$buildLog=Join-Path $out 'build.log'
$runLog=Join-Path $out 'probe.log'
foreach($f in @($geometry,$constants,$cli)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){
        throw "Aztec first Material state/next-header prerequisite missing: $f"
    }
}
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometry -Raw)
$c=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $constants -Raw)
if([string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [bool]$g.XboxHardwareReady -or
   [string]$c.Schema -ne 'WaW360.AztecPcPostImagesConstants.v1' -or
   [string]$c.Status -ne 'AZTEC_PC_POST_IMAGES_ONE_6X32_CONSTANT_WINDOW_CANDIDATE' -or
   [int]$c.FullyPlausibleStarts -ne 1 -or
   [int]$c.BestPlausibleRecordCount -ne 6 -or
   @($c.SixOfSixCandidateOffsets).Count -ne 1 -or
   [string]$c.SixOfSixCandidateOffsets[0] -ne '0x057319E1' -or
   [bool]$c.OriginalMaterialOwnershipProven -or
   [bool]$c.All39SourceNamesProven -or
   [bool]$c.XboxMaterialBindingsProven -or
   [bool]$c.XboxHardwareReady){
    throw 'First material constant source proof or zero-hardware prerequisites changed.'
}
$pc=[string]$g.OriginalPCFastFile
if(-not(Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$g.OriginalPCFastFileSha256){
    throw 'Original PC Aztec fastfile SHA changed.'
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
Write-Host '[AZTEC-MAT-NEXT] ======================================================'
Write-Host '[AZTEC-MAT-NEXT] FIRST MATERIAL 4x8 STATEBITS + SECOND MATERIAL HEADER'
Write-Host '[AZTEC-MAT-NEXT] 6/6 source constant table SHA, 32B states, exact next 112B header.'
Write-Host '[AZTEC-MAT-NEXT] Source-only, no name ownership promotion, NO FF / NO XBOX.'
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
        foreach($e in $errs){Write-Host "[AZTEC-MAT-NEXT] BUILD ERROR: $($e.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-MAT-NEXT] BUILD: $_"}
    }
    throw "Aztec state/next-header .NET build failed: $buildLog"
}
Write-Host '[AZTEC-MAT-NEXT] Incremental .NET build PASS'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $argsList=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-first-material-state-next-header',$pc,$constants,$json)
    & dotnet @argsList 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($code -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 30 | ForEach-Object {Write-Host "[AZTEC-MAT-NEXT] TRACE: $_"}
    }
    throw "Aztec state/next-header probe failed exit=$code : $runLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
$allowed=@(
    'AZTEC_PC_FIRST_MATERIAL_STATEBITS_SECOND_HEADER_SHAPE_CANDIDATE',
    'AZTEC_PC_FIRST_MATERIAL_SECOND_HEADER_UNRESOLVED'
)
if([string]$r.Schema -ne 'WaW360.AztecPcFirstMaterialStateNextHeader.v1' -or
   [string]$r.Status -notin $allowed -or
   [string]$r.FirstConstantTableAt -ne '0x057319E1' -or
   [string]$r.FirstStateBitsTableAt -ne '0x05731AA1' -or
   [string]$r.SecondMaterialHeaderAt -ne '0x05731AC1' -or
   @($r.StateBits).Count -ne 4 -or
   [bool]$r.WorldMaterialMemoryOwnershipProven -or
   [bool]$r.All39MaterialNamesProven -or
   [bool]$r.XboxMaterialBindingsProven -or
   [bool]$r.XboxHardwareReady){
    throw 'Aztec statebits/second header report failed source/no-hardware safety gates.'
}
foreach($s in @($r.StateBits)){
    Write-Host "[AZTEC-MAT-NEXT] state#$($s.Index) at=$($s.Offset) word0=$($s.StateBits0) word1=$($s.StateBits1)"
}
Write-Host "[AZTEC-MAT-NEXT] secondHeader=$($r.SecondMaterialHeaderAt) shape=$($r.SecondHeaderShapePlausible) namePtr=$($r.SecondNamePointer)"
Write-Host "[AZTEC-MAT-NEXT] secondCounts texture=$($r.SecondTextureCount) constant=$($r.SecondConstantCount) state=$($r.SecondStateBitsCount) camera=$($r.SecondCameraRegion)"
Write-Host "[AZTEC-MAT-NEXT] nameCandidate=$($r.SecondUnverifiedName) at=$($r.SecondNamePhysicalStart) afterName=$($r.SecondAfterName)"
Write-Host "[AZTEC-MAT-NEXT] pointersCompatible=$($r.SecondPointerFieldsCompatible) countsPlausible=$($r.SecondCountsPlausible)"
Write-Host "[AZTEC-MAT-NEXT] tech=$($r.SecondTechniqueSetPointer) textures=$($r.SecondTextureTablePointer) constants=$($r.SecondConstantTablePointer) states=$($r.SecondStateBitsTablePointer)"
foreach($t in @($r.SecondTextureCandidates)){
    Write-Host "[AZTEC-MAT-NEXT] nextTextureTable=$($t.SourceOffset) plausible=$($t.PlausibleDescriptors)/$($t.TextureCount) inline=$($t.FollowingImagePointers) packed=$($t.PackedImagePointers)"
}
Write-Host '[AZTEC-MAT-NEXT] ================= RESULT ============================'
Write-Host "[AZTEC-MAT-NEXT] status=$($r.Status)"
Write-Host "[AZTEC-MAT-NEXT] report=$json"
Write-Host '[AZTEC-MAT-NEXT] Source candidates only. 39 identities / 735 Xbox bindings NOT proven.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
