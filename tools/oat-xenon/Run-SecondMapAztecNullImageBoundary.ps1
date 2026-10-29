[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$microscope=Join-Path $base 'pc_material_stop_microscope\aztec_pc_material_stop_microscope.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$dir=Join-Path $base 'pc_null_image_boundary'
$json=Join-Path $dir 'aztec_pc_null_image_boundary.json'
$buildLog=Join-Path $dir 'build.log'
$runLog=Join-Path $dir 'probe.log'
foreach($f in @($geometry,$microscope,$cli)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){
        throw "Aztec null image boundary prerequisite missing: $f"
    }
}
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometry -Raw)
$p=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $microscope -Raw)
if([string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [bool]$g.XboxHardwareReady -or
   [string]$p.Schema -ne 'WaW360.AztecPcMaterialStopMicroscope.v1' -or
   [string]$p.Status -ne 'AZTEC_PC_MATERIAL_15TH_SUCCESSOR_IMAGE_REJECTION_EXPLAINED' -or
   [string]$p.PendingMaterialHeader -ne '0x057337C5' -or
   [string]$p.StopImageAt -ne '0x05733869' -or
   [bool]$p.GfxWorldMaterialMemoryOwnershipProven -or
   [bool]$p.All39SourceMaterialNamesProven -or
   [bool]$p.XboxMaterialBindingsProven -or
   [bool]$p.XboxHardwareReady){
    throw 'Original PC 15/39 stop microscope evidence or no-hardware gates changed.'
}
$pc=[string]$g.OriginalPCFastFile
if(-not(Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$g.OriginalPCFastFileSha256){
    throw 'Original-PC Aztec source fastfile SHA identity changed.'
}
New-Item -ItemType Directory -Path $dir -Force | Out-Null
Write-Host '[AZTEC-MAT-NULL] ======================================================='
Write-Host '[AZTEC-MAT-NULL] ORIGINAL PC NULL-TEXTURE IMAGE / NAME-ONLY CHILD'
Write-Host '[AZTEC-MAT-NULL] MAPTYPE_NONE + null texture + zero dims + FOLLOWING name.'
Write-Host '[AZTEC-MAT-NULL] Test NO LoadDef: inline name -> two constants -> eight states -> next header.'
Write-Host '[AZTEC-MAT-NULL] SHA-pinned source-only, no walker mutation, NO FF / NO XBOX.'
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
        foreach($e in $errs){Write-Host "[AZTEC-MAT-NULL] BUILD ERROR: $($e.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-MAT-NULL] BUILD: $_"}
    }
    throw "Aztec null-image boundary .NET build failed: $buildLog"
}
Write-Host '[AZTEC-MAT-NULL] Incremental .NET build PASS'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $arguments=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-null-image-boundary',$pc,$microscope,$json)
    & dotnet @arguments 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($code -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 35 | ForEach-Object {Write-Host "[AZTEC-MAT-NULL] TRACE: $_"}
    }
    throw "Aztec null-image boundary diagnostic failed exit=$code; log=$runLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
$allowed=@(
    'AZTEC_PC_NULL_IMAGE_NAME_ONLY_EXACT_MATERIAL_CONTINUATION_CANDIDATE',
    'AZTEC_PC_NULL_IMAGE_NAME_ONLY_CHILD_BOUNDARY_UNRESOLVED'
)
if([string]$r.Schema -ne 'WaW360.AztecPcNullImageBoundary.v1' -or
   [string]$r.Status -notin $allowed -or
   [string]$r.PendingMaterialHeaderAt -ne '0x057337C5' -or
   [string]$r.NullImageHeaderAt -ne '0x05733869' -or
   [string]$r.NullImageNameAt -ne '0x0573388D' -or
   [string]$r.NullImageName -ne ',global_black_c' -or
   [string]$r.CandidateAfterInlineName -ne '0x0573389D' -or
   [bool]$r.NestedLoadDefProven -or
   @($r.Candidates).Count -lt 1 -or
   @($r.Candidates).Count -gt 10 -or
   [bool]$r.GfxWorldMaterialMemoryOwnershipProven -or
   [bool]$r.All39SourceMaterialNamesProven -or
   [bool]$r.XboxMaterialBindingsProven -or
   [bool]$r.XboxHardwareReady){
    throw 'Aztec null-image boundary report failed SHA-pin/source-only safety gates.'
}
Write-Host "[AZTEC-MAT-NULL] Material $($r.PendingMaterialHeaderAt) nullImage=$($r.NullImageHeaderAt)"
Write-Host "[AZTEC-MAT-NULL] nameCandidate=$($r.NullImageName) at=$($r.NullImageNameAt) afterName=$($r.CandidateAfterInlineName)"
Write-Host "[AZTEC-MAT-NULL] Tested=$($r.TestedConstantStarts) positions; LoadDef was NOT consumed."
foreach($c in @($r.Candidates)){
    Write-Host "[AZTEC-MAT-NULL] constants=$($c.ConstantTableAt) delta=$($c.DeltaAfterName) plausible=$($c.PlausibleConstants)/2"
    Write-Host "[AZTEC-MAT-NULL]   candidateNames=$(@($c.ConstantNames) -join ' | ')"
    Write-Host "[AZTEC-MAT-NULL]   stateBits=$($c.StateBitsAt) nextHeader=$($c.NextHeaderAt) nameFollowing=$($c.NextNameFollowingAndReadable) nameCandidate=$($c.NextNameCandidate) counts=$($c.NextTextureCount)/$($c.NextConstantCount)/$($c.NextStateBitsCount) plausible=$($c.NextCountsPlausible)"
}
Write-Host '[AZTEC-MAT-NULL] ================= RESULT ============================'
Write-Host "[AZTEC-MAT-NULL] exactPostNameContinuation=$($r.ExactPostNameMaterialContinuationPlausible)"
Write-Host "[AZTEC-MAT-NULL] status=$($r.Status)"
Write-Host "[AZTEC-MAT-NULL] report=$json"
Write-Host '[AZTEC-MAT-NULL] No walker change or Xbox FF; Gate 7 remains PENDING.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
