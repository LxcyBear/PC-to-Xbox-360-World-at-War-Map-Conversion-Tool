[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$second=Join-Path $base 'pc_second_material_children\aztec_pc_second_material_children.json'
$nullProof=Join-Path $base 'pc_null_image_boundary\aztec_pc_null_image_boundary.json'
$oldWalk=Join-Path $base 'pc_material_stream_walker\aztec_pc_material_stream_walker.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_material_stream_walker_null_replay'
$json=Join-Path $out 'aztec_pc_material_stream_walker_null_replay.json'
$buildLog=Join-Path $out 'build.log'
$runLog=Join-Path $out 'probe.log'
foreach($p in @($geometry,$second,$nullProof,$oldWalk,$cli)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec material stream walker prerequisite missing: $p"
    }
}
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometry -Raw)
$p=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $second -Raw)
$n=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $nullProof -Raw)
$oldW=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $oldWalk -Raw)
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
if([string]$n.Schema -ne 'WaW360.AztecPcNullImageBoundary.v1' -or
   [string]$n.Status -ne 'AZTEC_PC_NULL_IMAGE_NAME_ONLY_EXACT_MATERIAL_CONTINUATION_CANDIDATE' -or
   [string]$n.PendingMaterialHeaderAt -ne '0x057337C5' -or
   [string]$n.NullImageHeaderAt -ne '0x05733869' -or
   [string]$n.NullImageName -ne ',global_black_c' -or
   [string]$n.CandidateAfterInlineName -ne '0x0573389D' -or
   -not [bool]$n.ExactPostNameMaterialContinuationPlausible -or
   [bool]$n.NestedLoadDefProven -or
   [bool]$n.GfxWorldMaterialMemoryOwnershipProven -or
   [bool]$n.All39SourceMaterialNamesProven -or
   [bool]$n.XboxMaterialBindingsProven -or
   [bool]$n.XboxHardwareReady -or
   [int]$oldW.DecodedMaterialCandidates -ne 15 -or
   [string]$oldW.StopReason -ne 'INLINE_GFXIMAGE_36_HEADER_SHAPE_UNRESOLVED' -or
   [string]$oldW.StopAt -ne '0x05733869' -or
   [string]$oldW.NextCursor -ne '0x057337C5'){
    throw 'Null image exact continuation or historical 15-material stop evidence changed.'
}
$exact=@($n.Candidates | Where-Object { [int]$_.DeltaAfterName -eq 0 })
if($exact.Count -ne 1 -or
   [int]$exact[0].PlausibleConstants -ne 2 -or
   [string]$exact[0].ConstantTableAt -ne '0x0573389D' -or
   [string]$exact[0].StateBitsAt -ne '0x057338DD' -or
   [string]$exact[0].NextHeaderAt -ne '0x0573391D' -or
   [string]$exact[0].NextNameCandidate -ne 'wc/goal' -or
   -not [bool]$exact[0].NextNameFollowingAndReadable -or
   -not [bool]$exact[0].NextCountsPlausible){
    throw 'Exact 2/2 constants + 8 states + next material witness no longer valid.'
}
$pc=[string]$g.OriginalPCFastFile
if(-not(Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$g.OriginalPCFastFileSha256){
    throw 'Original PC Aztec source FF SHA changed.'
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
Write-Host '[AZTEC-MAT-REPLAY] ======================================================'
Write-Host '[AZTEC-MAT-REPLAY] GUARDED NULL-IMAGE REPLAY OF ORIGINAL-PC MATERIAL WALKER'
Write-Host '[AZTEC-MAT-REPLAY] Replay first 15 unchanged, accept exactly one witnessed name-only image at 0x05733869.'
Write-Host '[AZTEC-MAT-REPLAY] Require material #15 to end 0x0573391D; then stop at next unknown or 39 cap.'
Write-Host '[AZTEC-MAT-REPLAY] Source-only, NO FF, NO XBOX, NO 39-name ownership promotion.'
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
        foreach($e in $errs){ Write-Host "[AZTEC-MAT-REPLAY] BUILD ERROR: $($e.Line)" }
    }else{
        Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-MAT-REPLAY] BUILD: $_"}
    }
    throw "Aztec Material stream walker build failed: $buildLog"
}
Write-Host '[AZTEC-MAT-REPLAY] Incremental .NET build PASS'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $arguments=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-material-stream-walker-null-replay',$pc,$second,$json,$nullProof)
    & dotnet @arguments 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $exitCode=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($exitCode -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 35 | ForEach-Object {Write-Host "[AZTEC-MAT-REPLAY] TRACE: $_"}
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
   [int]$r.DecodedMaterialCandidates -lt 16 -or
   [int]$r.DecodedMaterialCandidates -gt 39 -or
   @($r.Materials).Count -ne [int]$r.DecodedMaterialCandidates -or
   [string]$r.Materials[0].HeaderAt -ne '0x05731803' -or
   [string]$r.Materials[0].NextCursor -ne '0x05731AC1' -or
   [string]$r.Materials[1].HeaderAt -ne '0x05731AC1' -or
   [string]$r.Materials[1].NextCursor -ne '0x05731EA2' -or
   [string]$r.Materials[15].HeaderAt -ne '0x057337C5' -or
   [string]$r.Materials[15].NextCursor -ne '0x0573391D' -or
   [string]$r.Materials[15].CandidateName -ne 'wc/global_invisible' -or
   [bool]$r.GfxWorldMaterialMemoryOwnershipProven -or
   [bool]$r.All39SourceMaterialNamesProven -or
   [bool]$r.XboxMaterialBindingsProven -or
   [bool]$r.XboxHardwareReady){
    throw 'Source-only Material walk did not replay both anchors or violated Xbox safety gates.'
}
for($i=0; $i -lt 15; $i++){
    $original=$oldW.Materials[$i]
    $new=$r.Materials[$i]
    foreach($field in @('HeaderAt','NextCursor','CandidateName','InlineImages',
        'HeaderSha256','TextureTableSha256','ConstantTableSha256','StateBitsSha256')){
        if([string]$new.$field -ne [string]$original.$field){
            throw "Historical 15-material SHA replay differs at material $i field $field"
        }
    }
}
Write-Host '[AZTEC-MAT-REPLAY] Historical 15 material boundaries, identities and SHA hashes UNCHANGED'
$nullImages=@($r.Materials | ForEach-Object { $_.Images } | Where-Object { $_.NameOnlyNullImageCandidate })
if($nullImages.Count -ne 1 -or
   [string]$nullImages[0].HeaderAt -ne '0x05733869' -or
   [string]$nullImages[0].CandidateName -ne ',global_black_c' -or
   [string]$nullImages[0].LoadDefAt -ne 'NOT_PRESENT' -or
   [string]$nullImages[0].After -ne '0x0573389D'){
    throw 'Guarded replay did not preserve its unique null-image source witness.'
}
Write-Host "[AZTEC-MAT-REPLAY] nullNameOnlyImages=$($nullImages.Count) image=$($nullImages[0].HeaderAt) next=$($nullImages[0].After) LoadDef=NOT_PRESENT"
Write-Host "[AZTEC-MAT-REPLAY] decoded=$($r.DecodedMaterialCandidates)/39 inlineImages=$($r.InlineImages) textures=$($r.TextureDescriptors) constants=$($r.ConstantRecords) states=$($r.StateBitsRecords)"
foreach($m in @($r.Materials)){
    Write-Host "[AZTEC-MAT-REPLAY] #$($m.Ordinal) material=$($m.HeaderAt) nameCandidate=$($m.CandidateName) tex=$($m.TextureCount) img=$($m.InlineImages) const=$($m.ConstantCount) state=$($m.StateBitsCount) next=$($m.NextCursor)"
    Write-Host "[AZTEC-MAT-REPLAY]    source tables texture=$($m.TextureTableAt) constant=$($m.ConstantTableAt) state=$($m.StateBitsTableAt)"
}
Write-Host "[AZTEC-MAT-REPLAY] stopReason=$($r.StopReason) stopAt=$($r.StopAt) cursor=$($r.NextCursor)"
Write-Host '[AZTEC-MAT-REPLAY] ================= RESULT ============================'
Write-Host "[AZTEC-MAT-REPLAY] status=$($r.Status)"
Write-Host "[AZTEC-MAT-REPLAY] report=$json"
Write-Host '[AZTEC-MAT-REPLAY] Source-only structure; not proof of 39 GfxWorld owners, Xbox bindings or FF. Gate 7 PENDING.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
