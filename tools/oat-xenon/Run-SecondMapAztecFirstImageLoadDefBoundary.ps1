[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$sourceReport=Join-Path $base 'pc_first_material_inline_image_cursor\aztec_pc_first_material_inline_image_cursor.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$dir=Join-Path $base 'pc_first_image_loaddef_boundary'
$json=Join-Path $dir 'aztec_pc_first_image_loaddef_boundary.json'
$buildLog=Join-Path $dir 'build.log'
$runLog=Join-Path $dir 'probe.log'
foreach($p in @($geometry,$sourceReport,$cli)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec image LoadDef boundary prerequisite missing: $p"
    }
}
$g=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $geometry -Raw)
$prior=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $sourceReport -Raw)
if([string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS' -or
   [bool]$g.XboxHardwareReady -or
   [string]$prior.Schema -ne 'WaW360.AztecPcFirstMaterialInlineImageCursor.v1' -or
   [string]$prior.Status -ne 'AZTEC_PC_FIRST_MATERIAL_IMAGE_CHILD_NAME_BOUNDARY_CANDIDATE' -or
   [bool]$prior.ParentMaterialOwnedByGfxWorldProven -or
   [bool]$prior.SourceMaterialNamesProven -or
   [bool]$prior.XboxImageOrMaterialBindingProven -or
   [bool]$prior.XboxHardwareReady){
    throw 'Original PC source material/image proof or no-hardware gates changed.'
}
$pc=[string]$g.OriginalPCFastFile
if(-not(Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$g.OriginalPCFastFileSha256){
    throw 'Original PC fastfile SHA source identity changed.'
}
New-Item -ItemType Directory -Path $dir -Force | Out-Null
Write-Host '[AZTEC-LOADDEF] ======================================================='
Write-Host '[AZTEC-LOADDEF] FIRST PC DXT5 ZERO-PAYLOAD LOADDEF BOUNDARY DIAGNOSTIC'
Write-Host '[AZTEC-LOADDEF] Source-only, next-header checks bounded, NO FF/XBOX.'
$previous=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$previous}
if($exit -ne 0){
    $errorsFound=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 15)
    if($errorsFound.Count -gt 0){
        foreach($e in $errorsFound){Write-Host "[AZTEC-LOADDEF] BUILD ERROR: $($e.Line)"}
    }else{
        Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-LOADDEF] BUILD: $_"}
    }
    throw "Aztec LoadDef boundary diagnostic build failed: $buildLog"
}
Write-Host '[AZTEC-LOADDEF] Incremental .NET build PASS'
$previous=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'run' '--no-build' '-c' 'Release' '--project' $cli '--' `
        'aztec-pc-first-image-loaddef-boundary' $pc $sourceReport $json 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $runLog -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$previous}
if($exit -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-LOADDEF] TRACE: $_"}
    }
    throw "Aztec LoadDef boundary diagnostic failed exit=$exit; log=$runLog"
}
$r=ConvertFrom-Json -InputObject (Get-Content -LiteralPath $json -Raw)
if([string]$r.Schema -ne 'WaW360.AztecPcFirstImageLoadDefBoundary.v1' -or
   [string]$r.Status -ne 'AZTEC_PC_FIRST_IMAGE_DXT5_ZERO_PAYLOAD_BOUNDARY_CANDIDATE' -or
   [int]$r.MipLevelCount -ne 0 -or
   [uint32]$r.ResourceSize -ne 0 -or
   [string]$r.FormatFourCC -ne '0x35545844' -or
   [string]$r.CandidatePhysicalCursorAfterLoadDef -ne '0x05731937' -or
   -not [bool]$r.ZeroPayload -or
   [bool]$r.PhysicalChildOwnershipProven -or
   [bool]$r.All39MaterialNamesProven -or
   [bool]$r.XboxBindingsProven -or
   [bool]$r.XboxHardwareReady){
    throw 'Aztec zero-payload LoadDef report failed source or no-hardware safety gate.'
}
Write-Host "[AZTEC-LOADDEF] LoadDef at $($r.LoadDefPrefixAt): levels=$($r.MipLevelCount), flags=$($r.Flags), dimensions=$($r.Width)x$($r.Height)x$($r.Depth)"
Write-Host "[AZTEC-LOADDEF] format=$($r.FormatFourCC) (DXT5), resourceBytes=$($r.ResourceSize)"
Write-Host "[AZTEC-LOADDEF] HYPOTHETICAL post-LoadDef cursor=$($r.CandidatePhysicalCursorAfterLoadDef)"
Write-Host "[AZTEC-LOADDEF] Lookahead 96 bytes SHA256=$($r.PostCursor96BytesSha256)"
foreach($c in @($r.BoundedNextImageCandidates)){
    Write-Host "[AZTEC-LOADDEF] nextHeader=$($c.Offset) plausible=$($c.StructurallyPlausible) mapType=$($c.MapType) dimensions=$($c.Width)x$($c.Height)x$($c.Depth) category=$($c.Category) texture=$($c.TexturePointer) name=$($c.NamePointer)"
}
Write-Host '[AZTEC-LOADDEF] ================= RESULT =============================='
Write-Host "[AZTEC-LOADDEF] status=$($r.Status)"
Write-Host "[AZTEC-LOADDEF] report=$json"
Write-Host '[AZTEC-LOADDEF] This does NOT prove a LoadDef, 39 materials, or Xbox readiness.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
