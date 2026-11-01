[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$chain=Join-Path $base 'pc_first_material_image_chain\aztec_pc_first_material_image_chain.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_post_three_images_constant_table'
$json=Join-Path $out 'aztec_pc_post_three_images_constant_table.json'
$buildLog=Join-Path $out 'build.log'
$runLog=Join-Path $out 'probe.log'
foreach($f in @($geometry,$chain,$cli)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){
        throw "Aztec post-image constant-table prerequisite missing: $f"
    }
}
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometry -Raw)
$prior=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $chain -Raw)
if([string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [bool]$g.XboxHardwareReady -or
   [string]$prior.Schema -ne 'WaW360.AztecPcFirstMaterialImageChain.v1' -or
   [string]$prior.Status -ne 'AZTEC_PC_FIRST_MATERIAL_THREE_IMAGE_SERIALIZATION_CHAIN_CANDIDATE' -or
   -not [bool]$prior.ThreeImageSerializationChainCandidate -or
   [string]$prior.HypotheticalPostThirdImageCursor -ne '0x057319E1' -or
   [bool]$prior.MaterialMemoryOwnershipProven -or
   [bool]$prior.All39SourceNamesProven -or
   [bool]$prior.XboxMaterialBindingsProven -or
   [bool]$prior.XboxHardwareReady){
    throw 'Original-PC image chain evidence or no-hardware safety gates changed.'
}
$pc=[string]$g.OriginalPCFastFile
if(-not(Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$g.OriginalPCFastFileSha256){
    throw 'Original-PC source fastfile SHA identity changed.'
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
Write-Host '[AZTEC-MAT-CONST] ====================================================='
Write-Host '[AZTEC-MAT-CONST] POST-THREE-IMAGE CONSTANT TABLE BYTE-ALIGNMENT AUDIT'
Write-Host '[AZTEC-MAT-CONST] Exhaustively checks 512 physical byte starts after 0x057319E1.'
Write-Host '[AZTEC-MAT-CONST] Correct 6x32 MaterialConstantDef ABI; no absolute align assumption.'
Write-Host '[AZTEC-MAT-CONST] SHA-pinned, SOURCE ONLY, NO FF / NO XBOX.'
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
        foreach($e in $errs){Write-Host "[AZTEC-MAT-CONST] BUILD ERROR: $($e.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-MAT-CONST] BUILD: $_"}
    }
    throw "Aztec post-image constant audit .NET build failed: $buildLog"
}
Write-Host '[AZTEC-MAT-CONST] Incremental .NET build PASS'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $cmd=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-post-images-constants',$pc,$chain,$json)
    & dotnet @cmd 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($code -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 30 | ForEach-Object {Write-Host "[AZTEC-MAT-CONST] TRACE: $_"}
    }
    throw "Aztec post-image constant audit failed exit=$code; log=$runLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
$allowed=@(
    'AZTEC_PC_POST_IMAGES_ONE_6X32_CONSTANT_WINDOW_CANDIDATE',
    'AZTEC_PC_POST_IMAGES_MULTIPLE_6X32_CONSTANT_WINDOW_CANDIDATES',
    'AZTEC_PC_POST_IMAGES_6X32_CONSTANT_WINDOW_UNRESOLVED'
)
if([string]$r.Schema -ne 'WaW360.AztecPcPostImagesConstants.v1' -or
   [string]$r.Status -notin $allowed -or
   [string]$r.SourceAnchorAfterThirdImage -ne '0x057319E1' -or
   [int]$r.WindowBytes -ne 512 -or
   [int]$r.TestedPhysicalStarts -ne 512 -or
   [int]$r.ConstantCount -ne 6 -or
   [int]$r.ConstantStride -ne 32 -or
   [int]$r.StateBitsCount -ne 4 -or
   [int]$r.StateBitsStride -ne 8 -or
   [bool]$r.OriginalMaterialOwnershipProven -or
   [bool]$r.All39SourceNamesProven -or
   [bool]$r.XboxMaterialBindingsProven -or
   [bool]$r.XboxHardwareReady){
    throw 'Aztec post-image constant audit failed source or no-hardware gates.'
}
Write-Host "[AZTEC-MAT-CONST] tested=$($r.TestedPhysicalStarts) 6/6 candidates=$($r.FullyPlausibleStarts) best=$($r.BestPlausibleRecordCount)/6"
Write-Host "[AZTEC-MAT-CONST] fullCandidates=$(@($r.SixOfSixCandidateOffsets) -join ',')"
foreach($c in @($r.TopCandidateWindows | Select-Object -First 12)){
    Write-Host "[AZTEC-MAT-CONST] candidate=$($c.ConstantTableAt) delta=$($c.RelativeByteOffset) mod16=$($c.PhysicalOffsetModulo16) valid=$($c.PlausibleConstantRecords)/6 names=$($c.PrintableNames)/6 floats=$($c.FiniteFloatRecords)/6"
    Write-Host "[AZTEC-MAT-CONST]   nameCandidates=$(@($c.CandidateRecordNames) -join ' | ')"
    Write-Host "[AZTEC-MAT-CONST]   bits=$($c.HypotheticalStateBitsAt) nextMaterial=$($c.HypotheticalNextMaterialAt) nextNameFollowing=$($c.NextNameFollowing) countsPlausible=$($c.NextCountsPlausible)"
}
Write-Host '[AZTEC-MAT-CONST] ================= RESULT ==========================='
Write-Host "[AZTEC-MAT-CONST] status=$($r.Status)"
Write-Host "[AZTEC-MAT-CONST] report=$json"
Write-Host '[AZTEC-MAT-CONST] No ownership/binding/FF proven; Gate 7 remains PENDING.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
