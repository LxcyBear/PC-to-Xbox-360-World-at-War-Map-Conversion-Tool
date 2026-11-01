[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$walk=Join-Path $base 'pc_material_stream_walker\aztec_pc_material_stream_walker.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$dir=Join-Path $base 'pc_material_stop_microscope'
$json=Join-Path $dir 'aztec_pc_material_stop_microscope.json'
$buildLog=Join-Path $dir 'build.log'
$runLog=Join-Path $dir 'probe.log'
foreach($f in @($geometry,$walk,$cli)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){
        throw "Aztec walker stop microscope prerequisite missing: $f"
    }
}
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometry -Raw)
$w=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $walk -Raw)
if([string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [bool]$g.XboxHardwareReady -or
   [string]$w.Schema -ne 'WaW360.AztecPcMaterialStreamWalker.v1' -or
   [string]$w.Status -ne 'AZTEC_PC_SOURCE_MATERIAL_WALK_BEYOND_TWO_CANDIDATES' -or
   [int]$w.DecodedMaterialCandidates -ne 15 -or
   [string]$w.StopReason -ne 'INLINE_GFXIMAGE_36_HEADER_SHAPE_UNRESOLVED' -or
   [string]$w.StopAt -ne '0x05733869' -or
   [string]$w.NextCursor -ne '0x057337C5' -or
   [bool]$w.GfxWorldMaterialMemoryOwnershipProven -or
   [bool]$w.All39SourceMaterialNamesProven -or
   [bool]$w.XboxMaterialBindingsProven -or
   [bool]$w.XboxHardwareReady){
    throw 'Original PC Aztec walker first stop report or no-hardware gates changed.'
}
$pc=[string]$g.OriginalPCFastFile
if(-not(Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$g.OriginalPCFastFileSha256){
    throw 'Original PC Aztec fastfile source SHA changed.'
}
New-Item -ItemType Directory -Path $dir -Force | Out-Null
Write-Host '[AZTEC-MAT-STOP] ======================================================'
Write-Host '[AZTEC-MAT-STOP] EXACT 15/39 WALKER STOP: IMAGE HEADER REJECTION'
Write-Host '[AZTEC-MAT-STOP] Pending Material 0x057337C5, rejected image 0x05733869.'
Write-Host '[AZTEC-MAT-STOP] Examine header gates, descriptor union types, nine fixed offsets.'
Write-Host '[AZTEC-MAT-STOP] Read-only source proof, NO FF / NO XBOX.'
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
        foreach($e in $errs){Write-Host "[AZTEC-MAT-STOP] BUILD ERROR: $($e.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-MAT-STOP] BUILD: $_"}
    }
    throw "Aztec walker stop microscope .NET build failed: $buildLog"
}
Write-Host '[AZTEC-MAT-STOP] Incremental .NET build PASS'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $arguments=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-material-stop-microscope',$pc,$walk,$json)
    & dotnet @arguments 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($code -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 35 | ForEach-Object {Write-Host "[AZTEC-MAT-STOP] TRACE: $_"}
    }
    throw "Aztec walker stop microscope failed exit=$code; log=$runLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
$allowed=@(
    'AZTEC_PC_MATERIAL_15TH_SUCCESSOR_IMAGE_REJECTION_EXPLAINED',
    'AZTEC_PC_MATERIAL_STOP_IMAGE_HEADER_GATE_MISMATCH'
)
if([string]$r.Schema -ne 'WaW360.AztecPcMaterialStopMicroscope.v1' -or
   [string]$r.Status -notin $allowed -or
   [string]$r.LastCompletedMaterialHeader -ne '0x057336C8' -or
   [string]$r.PendingMaterialHeader -ne '0x057337C5' -or
   [string]$r.StopImageAt -ne '0x05733869' -or
   @($r.ImageHeaderCandidates).Count -ne 9 -or
   @($r.TextureDescriptors).Count -ne [int]$r.TextureCount -or
   [bool]$r.GfxWorldMaterialMemoryOwnershipProven -or
   [bool]$r.All39SourceMaterialNamesProven -or
   [bool]$r.XboxMaterialBindingsProven -or
   [bool]$r.XboxHardwareReady){
    throw 'Aztec stop microscope report failed source/no-hardware gates.'
}
Write-Host "[AZTEC-MAT-STOP] pendingMaterial=$($r.PendingMaterialHeader) nameCandidate=$($r.PendingMaterialNameCandidate)"
Write-Host "[AZTEC-MAT-STOP] counts textures=$($r.TextureCount) constants=$($r.ConstantCount) states=$($r.StateBitsCount) camera=$($r.CameraRegion)"
foreach($t in @($r.TextureDescriptors)){
    Write-Host "[AZTEC-MAT-STOP] texture#$($t.Index) semantic=$($t.Semantic) water=$($t.IsWater) ptr=$($t.ImagePointer) following=$($t.IsFollowing) at=$($t.Offset)"
}
Write-Host "[AZTEC-MAT-STOP] EXACT REJECTION: $(@($r.ExactStopRejectionReasons) -join ', ')"
foreach($i in @($r.ImageHeaderCandidates)){
    Write-Host "[AZTEC-MAT-STOP] offset=$($i.HeaderOffset) delta=$($i.DeltaFromStop) map=$($i.MapType) texture=$($i.TexturePointer) name=$($i.NamePointer) size=$($i.Width)x$($i.Height)x$($i.Depth) category=$($i.Category) strict=$($i.StrictWalkerAccepts)"
    Write-Host "[AZTEC-MAT-STOP]   gates map=$($i.MapTypePlausible) texInsert=$($i.TexturePointerIsInsert) nameFollowing=$($i.NamePointerIsFollowing) dims=$($i.DimensionsPlausible) fields=$($i.OtherHeaderFieldsPlausible) nameCandidate=$($i.InlineNameCandidate)"
}
Write-Host "[AZTEC-MAT-STOP] 192-byte window SHA256=$($r.RawWindowSha256)"
Write-Host '[AZTEC-MAT-STOP] ================= RESULT ============================'
Write-Host "[AZTEC-MAT-STOP] status=$($r.Status)"
Write-Host "[AZTEC-MAT-STOP] report=$json"
Write-Host '[AZTEC-MAT-STOP] No automatic skip, no ownership proof; Gate 7 PENDING.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
