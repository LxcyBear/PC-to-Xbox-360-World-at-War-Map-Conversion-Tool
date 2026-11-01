[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$second=Join-Path $base 'pc_second_material_children\aztec_pc_second_material_children.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_material_stream_walker'
$json=Join-Path $out 'aztec_pc_material_stream_walker.json'
$buildLog=Join-Path $out 'build.log'
$runLog=Join-Path $out 'probe.log'
foreach($p in @($geometry,$second,$cli)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec material stream walker prerequisite missing: $p"
    }
}
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometry -Raw)
$p=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $second -Raw)
if([string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [bool]$g.XboxHardwareReady -or
   [string]$p.Schema -ne 'WaW360.AztecPcSecondMaterialChildren.v1' -or
   [string]$p.Status -ne 'AZTEC_PC_SECOND_MATERIAL_SIX_IMAGES_UNIQUE_CONSTANTS_CANDIDATE' -or
   -not [bool]$p.SixInlineImageChainCandidate -or
   [string]$p.PostSixImagesCursor -ne '0x05731DA2' -or
   [int]$p.FullyPlausibleConstantTableStarts -ne 1 -or
   [bool]$p.MaterialMemoryOwnershipProven -or
   [bool]$p.All39MaterialNamesProven -or
   [bool]$p.XboxMaterialBindingsProven -or
   [bool]$p.XboxHardwareReady){
    throw 'Original PC two-Material evidence or no-hardware gate changed.'
}
$pc=[string]$g.OriginalPCFastFile
if(-not(Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$g.OriginalPCFastFileSha256){
    throw 'Original PC Aztec source FF SHA changed.'
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
Write-Host '[AZTEC-MAT-WALK] ======================================================'
Write-Host '[AZTEC-MAT-WALK] REUSABLE ORIGINAL-PC MATERIAL SERIALIZATION WALKER'
Write-Host '[AZTEC-MAT-WALK] Start 0x05731803; verify first two exact material boundaries.'
Write-Host '[AZTEC-MAT-WALK] Advance until first unsupported structure, 39 candidates or 16MB cap.'
Write-Host '[AZTEC-MAT-WALK] Source-only, NO FF, NO XBOX, NO 39-name ownership promotion.'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $exitCode=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($exitCode -ne 0){
    $errs=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 12)
    if($errs.Count -gt 0){
        foreach($e in $errs){ Write-Host "[AZTEC-MAT-WALK] BUILD ERROR: $($e.Line)" }
    }else{
        Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-MAT-WALK] BUILD: $_"}
    }
    throw "Aztec Material stream walker build failed: $buildLog"
}
Write-Host '[AZTEC-MAT-WALK] Incremental .NET build PASS'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $arguments=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-material-stream-walker',$pc,$second,$json)
    & dotnet @arguments 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $exitCode=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($exitCode -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 35 | ForEach-Object {Write-Host "[AZTEC-MAT-WALK] TRACE: $_"}
    }
    throw "Aztec Material stream walker failed exit=$exitCode; log=$runLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
$allowed=@(
    'AZTEC_PC_SOURCE_MATERIAL_WALK_39_STRUCTURAL_CANDIDATES',
    'AZTEC_PC_SOURCE_MATERIAL_WALK_BEYOND_TWO_CANDIDATES',
    'AZTEC_PC_SOURCE_MATERIAL_WALK_TWO_ANCHORS_THEN_STOP'
)
if([string]$r.Schema -ne 'WaW360.AztecPcMaterialStreamWalker.v1' -or
   [string]$r.Status -notin $allowed -or
   [string]$r.StartHeader -ne '0x05731803' -or
   [int]$r.MaterialCap -ne 39 -or
   [int]$r.ByteCap -ne 16777216 -or
   [int]$r.DecodedMaterialCandidates -lt 2 -or
   [int]$r.DecodedMaterialCandidates -gt 39 -or
   @($r.Materials).Count -ne [int]$r.DecodedMaterialCandidates -or
   [string]$r.Materials[0].HeaderAt -ne '0x05731803' -or
   [string]$r.Materials[0].NextCursor -ne '0x05731AC1' -or
   [string]$r.Materials[1].HeaderAt -ne '0x05731AC1' -or
   [string]$r.Materials[1].NextCursor -ne '0x05731EA2' -or
   [bool]$r.GfxWorldMaterialMemoryOwnershipProven -or
   [bool]$r.All39SourceMaterialNamesProven -or
   [bool]$r.XboxMaterialBindingsProven -or
   [bool]$r.XboxHardwareReady){
    throw 'Source-only Material walk did not replay both anchors or violated Xbox safety gates.'
}
Write-Host "[AZTEC-MAT-WALK] decoded=$($r.DecodedMaterialCandidates)/39 inlineImages=$($r.InlineImages) textures=$($r.TextureDescriptors) constants=$($r.ConstantRecords) states=$($r.StateBitsRecords)"
foreach($m in @($r.Materials)){
    Write-Host "[AZTEC-MAT-WALK] #$($m.Ordinal) material=$($m.HeaderAt) nameCandidate=$($m.CandidateName) tex=$($m.TextureCount) img=$($m.InlineImages) const=$($m.ConstantCount) state=$($m.StateBitsCount) next=$($m.NextCursor)"
    Write-Host "[AZTEC-MAT-WALK]    source tables texture=$($m.TextureTableAt) constant=$($m.ConstantTableAt) state=$($m.StateBitsTableAt)"
}
Write-Host "[AZTEC-MAT-WALK] stopReason=$($r.StopReason) stopAt=$($r.StopAt) cursor=$($r.NextCursor)"
Write-Host '[AZTEC-MAT-WALK] ================= RESULT ============================'
Write-Host "[AZTEC-MAT-WALK] status=$($r.Status)"
Write-Host "[AZTEC-MAT-WALK] report=$json"
Write-Host '[AZTEC-MAT-WALK] Not proof of 39 GfxWorld ownership entries; Gate 7 PENDING.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
