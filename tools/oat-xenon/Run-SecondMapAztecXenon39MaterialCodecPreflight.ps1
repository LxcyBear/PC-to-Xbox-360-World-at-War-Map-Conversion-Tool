[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$source=Join-Path $base 'pc_to_xenon_39_world_material_graph_candidates\aztec_39_pc_to_xenon_material_graph_candidates.json'
$native=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$out=Join-Path $base 'pc_xenon_39_material_existing_graph_codec_preflight'
$report=Join-Path $out 'aztec_39_material_existing_codec_preflight.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$buildLog=Join-Path $out 'build.log'
$runLog=Join-Path $out 'preflight.log'
foreach($f in @($source,$native,$cli)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){
        throw "Missing PC->Xenon 39-material source candidate/native identity prerequisite: $f"
    }
}
$s=Get-Content -LiteralPath $source -Raw | ConvertFrom-Json
$n=Get-Content -LiteralPath $native -Raw | ConvertFrom-Json
if([string]$s.Status -ne 'AZTEC_39_PC_MATERIALS_TO_XENON_96B_SOURCE_GRAPH_CANDIDATES_ONLY' -or
   [int]$s.SourceMaterialChildrenConverted -ne 39 -or
   [int]$s.CoveredWorldSurfaces -ne 735 -or
   [int]$s.TextureDescriptors -ne 87 -or
   [int]$s.ConstantRecords -ne 93 -or
   [int]$s.StateBitsRecords -ne 192 -or
   [bool]$s.RealXenonMaterialRootsEmitted -or
   [bool]$s.XboxHardwareReady -or
   [string]$n.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
   [int]$n.assets -ne 8509){
    throw 'Existing PC Material source 39-graph conversion and native Xbox identity gates changed.'
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
Write-Host '[AZTEC-39-CODEC] ======================================================='
Write-Host '[AZTEC-39-CODEC] 39 PC-OWNED XENON MATERIAL GRAPHS -> EXISTING FF CODEC'
Write-Host '[AZTEC-39-CODEC] Exact 8509 native roots, no donor blobs, strict 39 graph formats'
Write-Host '[AZTEC-39-CODEC] Test actual MaterialGraphCodec.Load + Plan & missing references'
Write-Host '[AZTEC-39-CODEC] Build source-only binding manifest, NOT a production FF'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($exit -ne 0){
    $errors=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 16)
    if($errors.Count -gt 0){foreach($e in $errors){Write-Host "[AZTEC-39-CODEC] BUILD ERROR $($e.Line)"}}
    else{Get-Content -LiteralPath $buildLog -Tail 25 | ForEach-Object {Write-Host "[AZTEC-39-CODEC] BUILD $_"}}
    throw "39-Material source-to-existing-codec build failed; log=$buildLog"
}
Write-Host '[AZTEC-39-CODEC] Incremental .NET build PASS'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $argsList=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-xenon-39-material-codec-preflight',
        $source,$native,$out)
    & dotnet @argsList 2>&1 | ForEach-Object {[string]$_} |
        Set-Content -LiteralPath $runLog -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($exit -ne 0 -or -not(Test-Path -LiteralPath $report -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -TotalCount 18 | ForEach-Object {Write-Host "[AZTEC-39-CODEC] TRACE $_"}
        Get-Content -LiteralPath $runLog -Tail 10 | ForEach-Object {Write-Host "[AZTEC-39-CODEC] TRACE $_"}
    }
    throw "Existing FF MaterialGraphCodec preflight failed exit=$exit; log=$runLog"
}
$r=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if([string]$r.Schema -ne 'WaW360.AztecPcXenonMaterial39CodecPreflight.v1' -or
   [string]$r.Status -ne 'AZTEC_39_SOURCE_XENON_MATERIAL_GRAPHS_EXISTING_CODEC_LOAD_PASS_DEPENDENCY_AUDIT' -or
   [int]$r.MaterialGraphsLoadedByExistingCodec -ne 39 -or
   [int]$r.OriginalPcWorldSurfaces -ne 735 -or
   [int]$r.OriginalPcWorldTriangles -ne 19551 -or
   ([int]$r.UniqueNativeDependencyOccurrences+[int]$r.MissingNativeDependencyOccurrences+
    [int]$r.AmbiguousNativeDependencyOccurrences) -ne
       ([int]$s.UniqueNameMatchesInNativeRoots+[int]$s.MissingNativeDependencies+[int]$s.AmbiguousNativeDependencies) -or
   [bool]$r.RealXboxWorldMaterialBodiesEmitted -or
   [bool]$r.AllMaterialSemanticDependenciesVerified -or
   [bool]$r.IntegratedOatFastFileValidated -or
   [bool]$r.XboxHardwareReady){
    throw 'Codec probe violated source-owned 39 Material / 735 world surfaces or non-deployable gates.'
}
Write-Host "[AZTEC-39-CODEC] actualCodecGraphLoad=$($r.MaterialGraphsLoadedByExistingCodec)/39"
Write-Host "[AZTEC-39-CODEC] actualCodecGraphPlan=$($r.MaterialGraphsPlannedByExistingCodec)/39 planFailures=$($r.ExistingCodecPlanFailures)"
Write-Host "[AZTEC-39-CODEC] uniqueNativeReferences=$($r.UniqueNativeDependencyOccurrences) missingOccurrences=$($r.MissingNativeDependencyOccurrences) ambiguousOccurrences=$($r.AmbiguousNativeDependencyOccurrences) uniqueUnresolvedNames=$($r.UniqueUnresolvedDependencyNames)"
Write-Host "[AZTEC-39-CODEC] unresolved PC stateBits[51..58] semantic mapping=$($r.MaterialsWithUnverifiedPcStateBitsTail)/39"
foreach($dep in @($r.UnresolvedReferences | Select-Object -First 16)){
    $hint=(@($dep.HumanReviewCandidates) -join ', ')
    Write-Host "[AZTEC-39-CODEC] UNRESOLVED $($dep.Type):$($dep.OriginalPcName) affectedMaterials=$($dep.AffectedMaterialCount) matches=$($dep.NativeRootMatches) suggestions=[$hint]"
}
foreach($err in @($r.PlanFailureDetails | Select-Object -First 8)){
    Write-Host "[AZTEC-39-CODEC] PLAN BLOCKER $($err.MaterialName): $($err.PlanError)"
}
Write-Host '[AZTEC-39-CODEC] ================= RESULT ================================'
Write-Host "[AZTEC-39-CODEC] status=$($r.Status)"
Write-Host "[AZTEC-39-CODEC] report=$report"
Write-Host "[AZTEC-39-CODEC] candidateRoot=$($r.CandidateGraphRoot)"
Write-Host '[AZTEC-39-CODEC] REAL Material FF roots / shader/image dependencies / GfxWorld/DPVS NOT YET emitted. Gate 7 pending.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
