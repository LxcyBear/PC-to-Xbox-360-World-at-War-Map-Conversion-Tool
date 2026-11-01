[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$source=Join-Path $base 'pc_39_world_material_corpus_source\aztec_pc_39_world_material_corpus_summary.json'
$worklist=Join-Path $base 'pc_verified_xbox_surface_material_relocation_worklist\aztec_verified_surface_material_relocation_worklist.json'
$native=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$out=Join-Path $base 'pc_to_xenon_39_world_material_graph_candidates'
$report=Join-Path $out 'aztec_39_pc_to_xenon_material_graph_candidates.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$buildLog=Join-Path $out 'build.log'
$runLog=Join-Path $out 'conversion.log'
foreach($p in @($source,$worklist,$native,$cli)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Required original-PC 39-Material extraction/verified binding/native identity missing: $p"
    }
}
$s=Get-Content -LiteralPath $source -Raw | ConvertFrom-Json
$w=Get-Content -LiteralPath $worklist -Raw | ConvertFrom-Json
$n=Get-Content -LiteralPath $native -Raw | ConvertFrom-Json
if([string]$s.status -ne 'AZTEC_PC_OAT_39_MATERIAL_HEADERS_AND_CHILD_TABLES_SOURCE_EXPORTED' -or
   [int]$s.sourceHeaders -ne 39 -or
   [int]$s.textures -ne 87 -or
   [int]$s.constants -ne 93 -or
   [int]$s.stateBits -ne 192 -or
   [int]$s.waterNestedGraphsPending -ne 0 -or
   [int]$s.realXenonWorldMaterialGraphBodies -ne 0 -or
   [bool]$s.hardwareReady -or
   [string]$w.Status -ne 'AZTEC_PC_735_TYPED_MATERIALS_VERIFIED_XBOX_ROOT_RELOCATION_WORKLIST_ONLY' -or
   [int]$w.VerifiedTypedPcSurfaceIdentities -ne 735 -or
   [int]$w.UniqueGoldenXboxNativeMaterialRootNames -ne 39 -or
   [string]$n.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
   [int]$n.assets -ne 8509){
    throw 'Pinned original-PC material graph and native root identity changed; refusing Xenon conversion.'
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
Write-Host '[AZTEC-MAT39-XENON] ======================================================'
Write-Host '[AZTEC-MAT39-XENON] 39 REAL PC MATERIAL HEADERS/TABLES -> XENON 96B CANDIDATES'
Write-Host '[AZTEC-MAT39-XENON] 112B PC header -> 96B Xenon, 87 textures, 93 constants, 192 stateBits'
Write-Host '[AZTEC-MAT39-XENON] Reuse existing T4Xenon MaterialRelocatableGraph format'
Write-Host '[AZTEC-MAT39-XENON] Resolve technique/image names; unresolved references remain BLOCKERS'
Write-Host '[AZTEC-MAT39-XENON] NO real FF root writes, NO GfxWorld yet, NOT FOR XBOX'
$before=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$before}
if($exit -ne 0){
    $errors=@(Select-String -LiteralPath $buildLog -Pattern '(?i)\berror\s+(?:CS|MSB)\d+|\berror:' | Select-Object -First 16)
    if($errors.Count -gt 0){foreach($e in $errors){Write-Host "[AZTEC-MAT39-XENON] BUILD ERROR: $($e.Line)"}}
    else{Get-Content -LiteralPath $buildLog -Tail 20 | ForEach-Object {Write-Host "[AZTEC-MAT39-XENON] BUILD: $_"}}
    throw "PC->Xbox 39 material graph .NET build failed; log=$buildLog"
}
Write-Host '[AZTEC-MAT39-XENON] Incremental .NET build PASS'
$before=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $argsList=@('run','--no-build','-c','Release','--project',$cli,'--',
        'aztec-pc-to-xenon-39-world-material-graphs',
        $source,$worklist,$native,$out)
    & dotnet @argsList 2>&1 | ForEach-Object {[string]$_} |
        Set-Content -LiteralPath $runLog -Encoding UTF8
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$before}
if($exit -ne 0 -or -not(Test-Path -LiteralPath $report -PathType Leaf)){
    if(Test-Path -LiteralPath $runLog -PathType Leaf){
        Get-Content -LiteralPath $runLog -TotalCount 18 | ForEach-Object {Write-Host "[AZTEC-MAT39-XENON] TRACE: $_"}
        Get-Content -LiteralPath $runLog -Tail 12 | ForEach-Object {Write-Host "[AZTEC-MAT39-XENON] TRACE: $_"}
    }
    throw "39 original PC to Xenon Material graph candidate conversion failed exit=$exit; log=$runLog"
}
$r=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if([string]$r.Schema -ne 'WaW360.AztecPcXenonMaterial39SourceBridge.v1' -or
   [string]$r.Status -ne 'AZTEC_39_PC_MATERIALS_TO_XENON_96B_SOURCE_GRAPH_CANDIDATES_ONLY' -or
   [int]$r.SourceMaterialHeaders -ne 39 -or
   [int]$r.SourceMaterialChildrenConverted -ne 39 -or
   [int]$r.CoveredWorldSurfaces -ne 735 -or
   [int]$r.CoveredWorldTriangles -ne 19551 -or
   [int]$r.TextureDescriptors -ne 87 -or
   [int]$r.ConstantRecords -ne 93 -or
   [int]$r.StateBitsRecords -ne 192 -or
   @($r.Materials).Count -ne 39 -or
   [bool]$r.AllDependenciesVerified -or
   [bool]$r.RealXenonMaterialRootsEmitted -or
   [bool]$r.IntegratedOatFfValidated -or
   [bool]$r.XboxHardwareReady){
    throw 'Source-to-Xenon 39-material candidate conversion claimed invalid corpus or unauthorized FF readiness.'
}
$realGraphFiles=@(Get-ChildItem -LiteralPath (Join-Path $out 'xenon_material_graphs') -Filter 'material_*.json' -File)
if($realGraphFiles.Count -ne 39){throw "Expected 39 individual Xenon Material candidate graph JSON, got $($realGraphFiles.Count)"}
$most=@($r.Materials | Sort-Object WorldSurfaceCount -Descending | Select-Object -First 7)
Write-Host "[AZTEC-MAT39-XENON] sourceOwnedXenon96BCandidates=$($r.SourceMaterialChildrenConverted)/39 originalPCSurfaces=$($r.CoveredWorldSurfaces)/735"
Write-Host "[AZTEC-MAT39-XENON] source child records textures=$($r.TextureDescriptors) constants=$($r.ConstantRecords) stateBits=$($r.StateBitsRecords)"
Write-Host "[AZTEC-MAT39-XENON] dependency references uniqueName=$($r.UniqueNameMatchesInNativeRoots) MISSING=$($r.MissingNativeDependencies) AMBIGUOUS=$($r.AmbiguousNativeDependencies) nullSourceTechniques=$($r.MaterialsWithNullOriginalTechnique)"
Write-Host "[AZTEC-MAT39-XENON] stateBitsMappingReview=$($r.MaterialsWithPotentiallyActiveTruncatedStateBitsEntries)/39 source materials"
foreach($m in $most){
    Write-Host "[AZTEC-MAT39-XENON] root#$($m.GoldenXboxRootIndex) $($m.VerifiedMaterialName) surfaces=$($m.WorldSurfaceCount) textures=$($m.TextureCount) technique=$($m.OriginalPcTechniqueSet)"
}
Write-Host '[AZTEC-MAT39-XENON] ================= RESULT =================================='
Write-Host "[AZTEC-MAT39-XENON] status=$($r.Status)"
Write-Host "[AZTEC-MAT39-XENON] report=$report"
Write-Host "[AZTEC-MAT39-XENON] graphRoot=$($r.CandidateGraphRoot)"
Write-Host '[AZTEC-MAT39-XENON] Next: resolve dependency names/shader semantics, promote 39 real graphs into planned FF, integrate GfxWorld/DPVS and OAT test. No Xbox FF yet.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
