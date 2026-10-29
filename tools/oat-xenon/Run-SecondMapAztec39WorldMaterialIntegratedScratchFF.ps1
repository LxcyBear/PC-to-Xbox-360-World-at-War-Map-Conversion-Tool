[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$priorRoot=Join-Path $base 'pc_two_xmodel_two_material_fullgraph_integration'
$source=Join-Path $base 'pc_to_xenon_39_world_material_graph_candidates\aztec_39_pc_to_xenon_material_graph_candidates.json'
$codec=Join-Path $base 'pc_xenon_39_material_existing_graph_codec_preflight\aztec_39_material_existing_codec_preflight.json'
$proof=Join-Path $base 'native_graph_proof\native_graph_proof_result.json'
$nativeIdentity=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$renderBinding=Join-Path $base 'renderer_preflight\native_root_binding\nuclear_xenon_root_binding.json'
$images=Join-Path $base 'pc_iwd_rawfile_integration\pc_xenon_image_graph'
$raw=Join-Path $base 'pc_rawfile_graft\pc_rawfile_graft_input.json'
$models=Join-Path $priorRoot 'pc_two_xmodel_graph'
$modelBinding=Join-Path $priorRoot 'aztec_two_pc_xmodel_root_binding.json'
$map=Join-Path $base 'pc_mapents_xenon_canary\aztec_pc_mapents_xenon_canary.json'
$com=Join-Path $base 'pc_comworld_burnable_source\aztec_pc_comworld_burnable_source.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$work=Join-Path $base 'pc_39_world_material_real_ff_scratch_integration'
$out=Join-Path $work 'scratch_emit'
$log=Join-Path $work 'integrated_emitter.log'
$buildLog=Join-Path $work 'build.log'
$report=Join-Path $work 'aztec_39_source_world_material_scratch_ff_gate.json'
$emitManifest=Join-Path $out 'nuclear_xenon_renderer_emit.json'

foreach($p in @($source,$codec,$proof,$nativeIdentity,$renderBinding,$raw,$modelBinding,$map,$com,$cli,$oat)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "39-source Material fullgraph scratch FF prerequisite missing: $p"
    }
}
foreach($p in @($images,$models)){
    if(-not(Test-Path -LiteralPath $p -PathType Container)){
        throw "39-source Material 6-family existing source graph folder missing: $p"
    }
}
$s=Get-Content -LiteralPath $source -Raw | ConvertFrom-Json
$c=Get-Content -LiteralPath $codec -Raw | ConvertFrom-Json
$pr=Get-Content -LiteralPath $proof -Raw | ConvertFrom-Json
$identity=Get-Content -LiteralPath $nativeIdentity -Raw | ConvertFrom-Json
if([string]$s.Status -ne 'AZTEC_39_PC_MATERIALS_TO_XENON_96B_SOURCE_GRAPH_CANDIDATES_ONLY' -or
   [int]$s.SourceMaterialChildrenConverted -ne 39 -or
   [int]$s.CoveredWorldSurfaces -ne 735 -or
   [int]$s.CoveredWorldTriangles -ne 19551 -or
   [int]$s.TextureDescriptors -ne 87 -or
   [int]$s.ConstantRecords -ne 93 -or
   [int]$s.StateBitsRecords -ne 192 -or
   [bool]$s.RealXenonMaterialRootsEmitted -or
   [bool]$s.XboxHardwareReady -or
   [string]$c.Status -ne 'AZTEC_39_SOURCE_XENON_MATERIAL_GRAPHS_EXISTING_CODEC_LOAD_PASS_DEPENDENCY_AUDIT' -or
   [int]$c.MaterialGraphsLoadedByExistingCodec -ne 39 -or
   [int]$c.MaterialGraphsPlannedByExistingCodec -ne 39 -or
   [int]$c.ExistingCodecPlanFailures -ne 0 -or
   [int]$c.MissingNativeDependencyOccurrences -ne 0 -or
   [int]$c.AmbiguousNativeDependencyOccurrences -ne 0 -or
   [int]$c.ExistingCodecDependencyErrorCount -ne 0 -or
   [bool]$c.RealXboxWorldMaterialBodiesEmitted -or
   [bool]$c.XboxHardwareReady -or
   [string]$identity.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
   [int]$identity.assets -ne 8509 -or
   [string]$pr.status -ne 'IDENTITY_AND_TYPED_GRAPH_IMPORT_PASS'){
    throw 'Only precisely 39 PC-sourced, writer-plan-verified Xenon Material candidate roots may be used in diagnostic scratch FF.'
}
$native=[string]$pr.verifiedGoldenXboxFastFile
if(-not(Test-Path -LiteralPath $native -PathType Leaf) -or
   (Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash -ine [string]$pr.nativeSha256){
    throw 'Original native Xbox reference FF SHA changed.'
}
$binding=[string]$c.SourceOnlyRootBindingManifest
if(-not(Test-Path -LiteralPath $binding -PathType Leaf)){
    throw "Source-only Material 39-root ownership binding missing: $binding"
}
$b=Get-Content -LiteralPath $binding -Raw | ConvertFrom-Json
if([string]$b.Format -ne 'WaW360.AztecPc39MaterialCodecIdentityCandidate.v1' -or
   -not [bool]$b.BindingPass -or
   -not [bool]$b.ValidationOnly -or
   [bool]$b.RuntimeMaterialBodiesWritten -or
   [bool]$b.CompleteDependencyClosure -or
   [int]$b.NativeRoots -ne 8509 -or
   @($b.OwnedBindings).Count -ne 39){
    throw 'Source-only 39 native world Material root binding has been altered.'
}
$matGraph=[string]$c.CandidateGraphRoot
if(-not(Test-Path -LiteralPath (Join-Path $matGraph 'xenon_material_graphs') -PathType Container)){
    throw "The 39 PC->Xenon candidate Material graphs no longer exist: $matGraph"
}
New-Item -ItemType Directory -Force -Path $work | Out-Null
Write-Host '[AZTEC-WORLD39-FF] ========================================================='
Write-Host '[AZTEC-WORLD39-FF] REAL 39 SOURCE-OWNED XENON MATERIAL BODIES IN 8509-ROOT FF'
Write-Host '[AZTEC-WORLD39-FF] Existing six-family renderer+2 images+114 RawFiles+2 XModels+MapEnts+ComWorld'
Write-Host '[AZTEC-WORLD39-FF] Unlike old canary: all 39 GfxWorld Material roots upgrade from identity shells'
Write-Host '[AZTEC-WORLD39-FF] NOT the final GfxWorld; GfxSurface array/DPVS are not yet inserted'
Write-Host '[AZTEC-WORLD39-FF] Material stateBits PC->Xbox semantic tail remains UNVERIFIED'
Write-Host '[AZTEC-WORLD39-FF] OAT FULL LOAD + 8509 NATIVE ROOT ORDER OR FAIL CLOSED. NO XBOX'
$save=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$save}
if($code -ne 0){
    $errors=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 20)
    if($errors.Count){foreach($e in $errors){Write-Host "[AZTEC-WORLD39-FF] BUILD ERROR $($e.Line)"}}
    else{Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-WORLD39-FF] BUILD $_"}}
    throw "Existing FF source 39-world Material integration build failed: $buildLog"
}
Write-Host '[AZTEC-WORLD39-FF] Incremental .NET build PASS'
if(Test-Path -LiteralPath $out -PathType Container){
    Remove-Item -LiteralPath $out -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $out | Out-Null
$save=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $argsList=@('run','--no-build','-c','Release','--project',$cli,'--',
        'xenon-graph-emit',[string]$pr.dumpRoot,$native,$nativeIdentity,$renderBinding,
        $out,'nazi_zombie_aztec',$oat,'70','436',
        '--aztec-pc-world-39-material-source-scratch-graft',
        $images,'2',$raw,$models,$modelBinding,$matGraph,$binding,$map,$com)
    & dotnet @argsList 2>&1 | ForEach-Object {[string]$_} |
        Set-Content -LiteralPath $log -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$save}
if(-not(Test-Path -LiteralPath $emitManifest -PathType Leaf)){
    Get-Content -LiteralPath $log -Tail 30 | ForEach-Object {Write-Host "[AZTEC-WORLD39-FF] EMIT $_"}
    throw "Full OAT scratch FF emitter did not produce its status manifest (exit=$code): $log"
}
$e=Get-Content -LiteralPath $emitManifest -Raw | ConvertFrom-Json
$pass=$code -eq 0 -and
    [bool]$e.ValidationPass -and [bool]$e.LayoutReplayExact -and
    [bool]$e.ExactIdentitySequence -and [bool]$e.OatZeroWarningsZeroErrors -and
    [bool]$e.ValidationOnly -and -not [bool]$e.ReadyForHardware -and
    [int]$e.NativeRoots -eq 8509 -and
    [int]$e.RealMaterialRoots -eq 39 -and
    [int]$e.RealXModelRoots -eq 2 -and
    [int]$e.RealImageRoots -eq 2 -and
    [int]$e.RealRawFileRoots -eq 114 -and
    [int]$e.RealMapEntsRoots -eq 1 -and
    [int]$e.RealComWorldRoots -eq 1
$ff=[string]$e.OutputFastFile
if($pass){
    $allowed=([IO.Path]::GetFullPath($out)).TrimEnd([char]'\')+'\'
    if(-not(Test-Path -LiteralPath $ff -PathType Leaf) -or
        -not ([IO.Path]::GetFullPath($ff)).StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){
        throw 'OAT PASS result FF is not inside the verified NON-DEPLOYABLE scratch output.'
    }
}
$result=[ordered]@{
    schema='WaW360.AztecPc39WorldMaterialScratchFfIntegration.v1'
    status=if($pass){'AZTEC_PC_39_REAL_WORLD_MATERIAL_ROOTS_IN_SCRATCH_XENON_FF_OAT_PASS'}else{'AZTEC_PC_39_WORLD_MATERIAL_ROOT_SCRATCH_FF_INTEGRATION_BLOCKED'}
    originalPcMaterialGraphCandidateReport=$source
    realSourceWorldMaterialRoots=if($pass){39}else{[int]$e.RealMaterialRoots}
    nativeWorldGfxSurfaceReferencesInFf=0
    xenonGfxWorldRootEmitted=$false
    realGfxWorldDpvsLinked=$false
    materialHeaderStateBitsSemanticsVerified=$false
    oatLoaderPass=[bool]$e.ValidationPass
    oatZeroWarningsZeroErrors=[bool]$e.OatZeroWarningsZeroErrors
    layoutReplayExact=[bool]$e.LayoutReplayExact
    xboxNativeRootIdentityExact=[bool]$e.ExactIdentitySequence
    originalXboxIdentityRoots=[int]$e.NativeRoots
    smokeTestFfPath=if($pass){$ff}else{''}
    diagnosticOnly=$true
    hardwareReady=$false
    emitterReport=$emitManifest
    emitterLog=$log
    note='39 original-PC Material 96-byte Xenon-format graph bodies integrated into a controlled scratch FastFile, not the final 735-surface GfxWorld. PC stateBitsEntry[51..58] semantic mapping and required dependencies are not certified for Xbox runtime. NO deployable FF.'
}
$result|ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host "[AZTEC-WORLD39-FF] .NET emitter exit=$code sourceWorldMaterialRoots=$($e.RealMaterialRoots)/39"
Write-Host "[AZTEC-WORLD39-FF] FF native roots=$($e.NativeRoots)/8509 OAT PASS=$($e.ValidationPass) 0 warnings/errors=$($e.OatZeroWarningsZeroErrors)"
Write-Host "[AZTEC-WORLD39-FF] nativeIdentityExact=$($e.ExactIdentitySequence) layoutReplayExact=$($e.LayoutReplayExact)"
Write-Host "[AZTEC-WORLD39-FF] status=$($result.status)"
Write-Host "[AZTEC-WORLD39-FF] report=$report"
if(-not $pass){
    Get-Content -LiteralPath $log -Tail 22 | ForEach-Object {Write-Host "[AZTEC-WORLD39-FF] TRACE $_"}
    throw "39-world-Material existing FF emission/OAT gate blocked (exit=$code): $log"
}
Write-Host "[AZTEC-WORLD39-FF] SCRATCH ONLY FF=$ff"
Write-Host '[AZTEC-WORLD39-FF] This is real Material body integration, NOT 735 GfxSurface bindings or a GfxWorld. NO XBOX.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
