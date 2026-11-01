[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$v3Path=Join-Path $base 'pc_gfxworld_material_memory_source_probe\aztec_pc_gfxworld_material_memory_source_probe.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $base 'pc_gfxworld_material_inline_boundary'
$json=Join-Path $out 'aztec_pc_gfxworld_material_inline_boundary.json'
$buildLog=Join-Path $out 'build.log'
$runLog=Join-Path $out 'probe.log'
foreach($p in @($geometry,$v3Path,$cli)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec exact inline Material boundary prerequisite missing: $p"
    }
}
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometry -Raw)
$v3=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $v3Path -Raw)
if([string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
    [bool]$g.XboxHardwareReady -or
    [string]$v3.Schema -ne 'WaW360.AztecPcGfxWorldMaterialMemorySourceProbe.v3' -or
    [string]$v3.Status -ne 'AZTEC_PC_GFXWORLD_39_MATERIAL_MEMORY_TWO_OVERLAPPING_WINDOWS_40_RECORD_RUN' -or
    -not [bool]$v3.TwoFullWindowsOverlapIntoFortyRecordRun -or
    [int]$v3.Full39RecordSignatures -ne 2 -or
    [int]$v3.Isolated39RecordSignatures -ne 0 -or
    [bool]$v3.ExactPhysicalOwnershipProven -or
    [bool]$v3.XboxHardwareReady){
    throw 'Require the exact original PC 39-row/40-FOLLOWING v3 source proof; no hardware FF.'
}
$pc=[string]$g.OriginalPCFastFile
if(-not (Test-Path -LiteralPath $pc -PathType Leaf) -or
    (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$g.OriginalPCFastFileSha256){
    throw 'Original PC Aztec fastfile SHA changed.'
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
Write-Host '[AZTEC-MAT-BOUNDARY] ================================================='
Write-Host '[AZTEC-MAT-BOUNDARY] EXACT TWO-WINDOW SOURCE MATERIAL HEADER BOUNDARY'
Write-Host '[AZTEC-MAT-BOUNDARY] 39 FOLLOWING entries + possible inline Material header.'
Write-Host '[AZTEC-MAT-BOUNDARY] ONLY 0x057316CB / 0x057316D3 and nearby Material bodies.'
Write-Host '[AZTEC-MAT-BOUNDARY] No broad scan / no FF / no Xbox.'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0){
    $errs=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 15)
    if($errs.Count -gt 0){
        foreach($e in $errs){Write-Host "[AZTEC-MAT-BOUNDARY] BUILD ERROR: $($e.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 20 | ForEach-Object {Write-Host "[AZTEC-MAT-BOUNDARY] BUILD: $_"}
    }
    throw "Source Material inline boundary .NET build failed; log=$buildLog"
}
Write-Host '[AZTEC-MAT-BOUNDARY] Incremental .NET build PASS'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $command=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-gfxworld-material-inline-boundary',$pc,$v3Path,$json)
    & dotnet @command 2>&1 | ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 30 |
            ForEach-Object {Write-Host "[AZTEC-MAT-BOUNDARY] TRACE: $_"}
    }
    throw "Source Material boundary probe failed exit=$code; log=$runLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
$goodStatuses=@(
    'AZTEC_PC_GFXWORLD_FIRST_INLINE_MATERIAL_112_ABI_STRUCTURAL_CANDIDATE',
    'AZTEC_PC_GFXWORLD_FIRST_INLINE_MATERIAL_ABI_UNRESOLVED'
)
if([string]$r.Schema -ne 'WaW360.AztecPcGfxWorldMaterialInlineBoundary.v2' -or
    [string]$r.Status -notin $goodStatuses -or
    [int]$r.MaterialMemoryCount -ne 39 -or
    [int]$r.CandidateMaterialMemoryBytes -ne 312 -or
    @($r.Windows).Count -ne 2 -or
    [bool]$r.WorldMaterialMemoryArrayOwnershipProven -or
    [bool]$r.SourceMaterialNamesProven -or
    [bool]$r.XboxMaterialBindingsProven -or
    [bool]$r.XboxHardwareReady){
    throw '39-row inline Material boundary report failed source and no-hardware gates.'
}
Write-Host '[AZTEC-MAT-BOUNDARY] ================= RESULT ========================'
foreach($w in @($r.Windows)){
    Write-Host "[AZTEC-MAT-BOUNDARY] WINDOW=$($w.WindowStart) follows=$($w.FollowingPointerFields)/39 nextWord=$($w.FollowingHeaderCandidateFirstWord) preValid=$($w.ValidPrecedingEightByteRecord) postValid=$($w.ValidFollowingEightByteRecord)"
    Write-Host "[AZTEC-MAT-BOUNDARY]   memory min=$($w.SmallestMemoryField) max=$($w.LargestMemoryField) total=$($w.SumMemoryFields)"
    foreach($shape in @($w.PossibleMaterialHeaders)){
        Write-Host "[AZTEC-MAT-BOUNDARY]   MaterialLayout=$($shape.CandidateMaterialBytes)B plausibleCounts=$($shape.PlausibleMaterialCountFields) textures=$($shape.TextureCount) constants=$($shape.ConstantCount) states=$($shape.StateBitsCount) camera=$($shape.CameraRegion) technique=$($shape.TechniqueSetPointer)"
        Write-Host "[AZTEC-MAT-BOUNDARY]     name=$($shape.MaterialNamePointer), following=$($shape.NamePointerIsFollowing), techniqueOK=$($shape.TechniqueSetPointerCompatible), textureOK=$($shape.TextureTablePointerCompatible), constantOK=$($shape.ConstantTablePointerCompatible), statebitsOK=$($shape.StateBitsTablePointerCompatible), allPtrsOK=$($shape.AllDependencyPointerFieldsCompatible), nameDirectlyAfterHeader=$($shape.ExactPostHeaderReadableString)"
        Write-Host "[AZTEC-MAT-BOUNDARY]     rawPtrs texture=$($shape.TextureTablePointer) constant=$($shape.ConstantTablePointer) stateBits=$($shape.StateBitsTablePointer)"
        foreach($name in @($shape.NameCandidates)){
            Write-Host "[AZTEC-MAT-BOUNDARY]     NAME-CANDIDATE $($name.SourceOffset)=$($name.UnverifiedName) (unproven)"
        }
    }
}
Write-Host "[AZTEC-MAT-BOUNDARY] status=$($r.Status)"
Write-Host "[AZTEC-MAT-BOUNDARY] report=$json"
Write-Host '[AZTEC-MAT-BOUNDARY] Candidate names are NOT identity proof. NO FF / NOT FOR XBOX.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
