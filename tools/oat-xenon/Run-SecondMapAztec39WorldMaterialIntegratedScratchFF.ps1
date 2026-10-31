[CmdletBinding()]
param([switch]$IntegrateWorld,
      [ValidateSet('Root','Indices','Surfaces','MaterialMemory','Full')]
      [string]$ProbeLevel='Full',
      [switch]$DpvsStaticProbe,
      [switch]$Gate8ChildProbe,
      [switch]$Gate8FiveModelProbe,
      [switch]$Gate8AllModelProbe,
      [switch]$Gate10StaticDrawProbe,
      [switch]$Gate11CellProbe,
      [switch]$Gate11PrimitiveProbe,
      [switch]$Gate11LightingProbe,
      [switch]$Gate11ImageHeadersProbe,
      [switch]$Gate11ImageRootLinksProbe,
      [ValidateSet('Planes','Nodes','All')]
      [string]$Gate8ChildLevel='All')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(-not $IntegrateWorld -and $ProbeLevel -ne 'Full'){
    throw 'World child probe levels require -IntegrateWorld.'
}
if($DpvsStaticProbe -and (-not $IntegrateWorld -or $ProbeLevel -ne 'Full')){
    throw 'Source-backed static DPVS probe requires -IntegrateWorld -ProbeLevel Full.'
}
if($Gate8ChildProbe -and (-not $DpvsStaticProbe -or -not $IntegrateWorld -or $ProbeLevel -ne 'Full')){
    throw 'Gate 8 typed PC plane/node/layer probe requires -IntegrateWorld -ProbeLevel Full -DpvsStaticProbe.'
}
if(-not $Gate8ChildProbe -and $Gate8ChildLevel -ne 'All'){
    throw '-Gate8ChildLevel applies only to -Gate8ChildProbe.'
}
if($Gate8FiveModelProbe -and
   (-not $IntegrateWorld -or $ProbeLevel -ne 'Full' -or
    -not $DpvsStaticProbe -or -not $Gate8ChildProbe -or
    $Gate8ChildLevel -ne 'All')){
    throw '-Gate8FiveModelProbe requires -IntegrateWorld -ProbeLevel Full -DpvsStaticProbe -Gate8ChildProbe -Gate8ChildLevel All.'
}
if($Gate8AllModelProbe -and $Gate8FiveModelProbe){
    throw 'Gate8 five-model and all-static-model graph probes must use separate outputs.'
}
if($Gate8AllModelProbe -and
   (-not $IntegrateWorld -or $ProbeLevel -ne 'Full' -or
    -not $DpvsStaticProbe -or -not $Gate8ChildProbe -or
    $Gate8ChildLevel -ne 'All')){
    throw '-Gate8AllModelProbe requires -IntegrateWorld -ProbeLevel Full -DpvsStaticProbe -Gate8ChildProbe -Gate8ChildLevel All.'
}
if($Gate10StaticDrawProbe -and
   (-not $Gate8AllModelProbe -or -not $IntegrateWorld -or
    -not $DpvsStaticProbe -or -not $Gate8ChildProbe -or
    $ProbeLevel -ne 'Full' -or $Gate8ChildLevel -ne 'All')){
    throw '-Gate10StaticDrawProbe requires the full source DPVS, Gate8 all-model/plane/node and current original-PC world diagnostic.'
}
if($Gate11CellProbe -and -not $Gate10StaticDrawProbe){
    throw '-Gate11CellProbe requires -Gate10StaticDrawProbe and the full verified Gate8 original-PC children.'
}
if($Gate11PrimitiveProbe -and -not $Gate11CellProbe){
    throw '-Gate11PrimitiveProbe requires -Gate11CellProbe; the source-owned GfxCell/AABB baseline must be present.'
}
if($Gate11LightingProbe -and -not $Gate11PrimitiveProbe){
    throw '-Gate11LightingProbe requires the entire proven Gate11 primitive/cell/1903-draw graph.'
}
if($Gate11ImageHeadersProbe -and -not $Gate11LightingProbe){
    throw '-Gate11ImageHeadersProbe requires source-owned lighting/shadow, primitive, cell and draw children.'
}
if($Gate11ImageRootLinksProbe -and -not $Gate11ImageHeadersProbe){
    throw '-Gate11ImageRootLinksProbe requires source-owned reflection/lightmap headers and all prior Gate11 children.'
}
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
if($Gate8FiveModelProbe){
    # Exact 5-model proof is prepared only by Stage-AztecGate8FiveSourceXModelGraphs.
    # It is a separate graph root/binding and cannot mutate the original 2-model
    # canary or previously proven 8,509-root Gate8 BSP candidate.
    $staging=Join-Path $repo '.deps\aztec8-five-xmodels\aztec_gate8_five_xmodel_staging.json'
    if(-not(Test-Path -LiteralPath $staging -PathType Leaf)){
        throw "Five original-PC XModel staging proof missing: $staging"
    }
    $stage=Get-Content -LiteralPath $staging -Raw | ConvertFrom-Json
    if([string]$stage.schema -ne 'WaW360.AztecGate8FiveXModelStaging.v1' -or
       [string]$stage.status -ne 'AZTEC_GATE8_FIVE_SOURCE_XMODEL_GRAPHS_STAGED' -or
       [int]$stage.modelCount -ne 5 -or
       [int]$stage.rootCount -ne 8509 -or
       [int]$stage.staticDrawsRepresented -ne 1572 -or
       [bool]$stage.hardwareReady){
        throw 'Gate8 source-owned five-XModel graph staging manifest is not verified.'
    }
    $models=[string]$stage.graphRoot
    $modelBinding=[string]$stage.bindingManifest
    foreach($item in @($stage.graphProofs)){
        if(-not(Test-Path -LiteralPath ([string]$item.stagedGraph) -PathType Leaf) -or
           (Get-FileHash -LiteralPath ([string]$item.stagedGraph) -Algorithm SHA256).Hash -ine
               [string]$item.sha256){
            throw "Five-XModel graph staging SHA changed at $($item.ordinal): $($item.name)"
        }
    }
    if(@($stage.graphProofs).Count -ne 5){
        throw 'Five-XModel graph staging requires exactly five SHA-authenticated graph rows.'
    }
}
if($Gate8AllModelProbe){
    $allStaging=Join-Path $repo '.deps\aztec8-all-static-xmodels\aztec_gate8_29_xmodel_staging.json'
    if(-not(Test-Path -LiteralPath $allStaging -PathType Leaf)){
        throw "All 27 PC model graphs have not been staged: $allStaging"
    }
    $allStage=Get-Content -LiteralPath $allStaging -Raw | ConvertFrom-Json
    if([string]$allStage.schema -ne 'WaW360.AztecGate8AllStaticXModelStaging.v1' -or
       [string]$allStage.status -notin @(
            'AZTEC_GATE8_ALL_27_PC_STATIC_XMODEL_IDENTITIES_CLASSIFIED_GRAPHS_STAGED',
            'AZTEC_GATE8_SOURCE_MODELS_STAGED_WITH_UNRESOLVED_REFERENCES') -or
       [int]$allStage.sourceModels -ne 27 -or
       [int]$allStage.staticDrawsRepresented -ne 1903 -or
       ([int]$allStage.realOwnedStaticModelTypes + [int]$allStage.nativeExternalReferenceModelTypes +
            [int]$allStage.unresolvedNativeReferenceModelTypes) -ne 27 -or
       [int]$allStage.modelCount -ne (2 + [int]$allStage.realOwnedStaticModelTypes) -or
       [int]$allStage.rootCount -ne 8509 -or
       [bool]$allStage.hardwareReady -or
       @($allStage.graphProofs).Count -ne [int]$allStage.modelCount -or
       @($allStage.nativeExternalReferences).Count -ne [int]$allStage.nativeExternalReferenceModelTypes -or
       @($allStage.unresolvedNativeReferenceModels).Count -ne [int]$allStage.unresolvedNativeReferenceModelTypes){

        throw 'All 27 PC static model identities are not classified with complete actual converted XModel graphs and native external-reference provenance.'
    }
    foreach($graph in @($allStage.graphProofs)){
        if(-not(Test-Path -LiteralPath ([string]$graph.stagedGraph) -PathType Leaf) -or
           (Get-FileHash -LiteralPath ([string]$graph.stagedGraph) -Algorithm SHA256).Hash -ine
               [string]$graph.sha256){
            throw "Source XModel graph has changed: #$($graph.ordinal) $($graph.name)"
        }
    }
    $models=[string]$allStage.graphRoot
    $modelBinding=[string]$allStage.bindingManifest
}
$map=Join-Path $base 'pc_mapents_xenon_canary\aztec_pc_mapents_xenon_canary.json'
$com=Join-Path $base 'pc_comworld_burnable_source\aztec_pc_comworld_burnable_source.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$worldSeed=Join-Path $base 'pc_source_to_xenon_gfxworld_root_dpvs_scalar_seed\aztec_pc_gfxworld_800_to_xenon_900_root_dpvs_seed.json'
$worldSurface76=Join-Path $base 'pc_xenon_76byte_gfxsurface_canary\aztec_735_gfxsurfaces_76byte_source_canary.json'
$gate8Source=Join-Path $base 'pc_gfxworld_gate8_child_source_corpus\aztec_pc_gate8_child_source_corpus.json'
if($Gate8ChildProbe -and -not(Test-Path -LiteralPath $gate8Source -PathType Leaf)){
    throw "Missing typed original-PC Gate8 source corpus. Run Run-SecondMapAztecGate8PcChildCorpus.ps1 first: $gate8Source"
}
$gate10Light=''
$gate10Plan=''
$gate11Summary=''
$gate11Primitive=''
$gate11Lighting=''
$gate11ImageHeaders=''
if($Gate10StaticDrawProbe){
    $sourceLights=Join-Path $base 'pc_gate8_source_light_codec_candidate'
    $latest=@(Get-ChildItem -LiteralPath $sourceLights -Recurse -File -Filter 'aztec_gate10_source_light_candidate_summary.json' -ErrorAction Stop |
        Sort-Object LastWriteTime -Descending)
    if($latest.Count -eq 0){throw "Missing source-owned Gate10 light sidecar under $sourceLights"}
    $gate10Light=$latest[0].FullName
    $gate10Plan=Join-Path $base 'pc_gate8_1903_draw_relocation_plan\aztec_1903_model_pointer_relocation_summary.json'
    if(-not(Test-Path -LiteralPath $gate10Plan -PathType Leaf)){
        throw "Gate10 1903 draw -> native XModel root plan missing: $gate10Plan"
    }
    Write-Host '[AZTEC-GATE10-FF] Isolated OAT-only candidate: 1903 original-PC draws, one stock XModel still UNVERIFIED.'
}
if($Gate11CellProbe){
    $gate11Summary=Join-Path $base 'pc_g10_1903\gate11_cell_topology_source\aztec_gate11_pc_source_cells_native_abi.json'
    if(-not(Test-Path -LiteralPath $gate11Summary -PathType Leaf)){
        throw "Source-owned typed 11-cell/740-AABB SHA report is missing: $gate11Summary"
    }
}
if($Gate11PrimitiveProbe){
    $gate11Primitive=Join-Path $base 'pc_g11_cells_740\gate11_remaining_unified_source\aztec_gate11_combined_no_ff_build_preflight.json'
    if(-not(Test-Path -LiteralPath $gate11Primitive -PathType Leaf)){
        throw "The SHA-verified 13-family Gate11 preflight must run first: $gate11Primitive"
    }
}
if($Gate11LightingProbe){
    $gate11Lighting=Join-Path $base 'pc_g11_two_wave_world_closure\aztec_gate11_two_wave_source_dependency_plan.json'
    if(-not(Test-Path -LiteralPath $gate11Lighting -PathType Leaf)){
        throw "Joint source-grid/reflection/shadow/region preflight must pass first: $gate11Lighting"
    }
}
if($Gate11ImageHeadersProbe){
    $gate11ImageHeaders=Join-Path $base 'pc_g11_image_stream_closure\aztec_gate11_6_images_and_native_stream_shape.json'
    if(-not(Test-Path -LiteralPath $gate11ImageHeaders -PathType Leaf)){
        throw "Six native image-name and empty-stream structural witnesses must run first: $gate11ImageHeaders"
    }
}
$work=Join-Path $base $(if($IntegrateWorld){
    if($Gate8ChildProbe){
        if($Gate11ImageRootLinksProbe){'pc_g11_image_root_links_probe'}
        elseif($Gate11ImageHeadersProbe){'pc_g11_image_headers_probe'}
        elseif($Gate11LightingProbe){'pc_g11_lighting_combined_probe'}
        elseif($Gate11PrimitiveProbe){'pc_g11_remaining_world_primitive_probe'}
        elseif($Gate11CellProbe){'pc_g11_cells_740'}
        elseif($Gate10StaticDrawProbe){'pc_g10_1903'}
        elseif($Gate8AllModelProbe){'pc_gate8_29x_ff'}
        elseif($Gate8FiveModelProbe){'pc_gate8_5x_ff'}
        elseif($Gate8ChildLevel -eq 'All'){'pc_gate8_planes_nodes_layer'}
        else{'pc_735_39_gfxworld_integrated_ff_oat_run_probe_gate8_'+$Gate8ChildLevel.ToLowerInvariant()}
    }
    elseif($DpvsStaticProbe){'pc_735_39_gfxworld_integrated_ff_oat_run_probe_dpvs_static'}
    elseif($ProbeLevel -eq 'Full'){'pc_735_39_gfxworld_integrated_ff_oat_run'}
    else{'pc_735_39_gfxworld_integrated_ff_oat_run_probe_'+$ProbeLevel.ToLowerInvariant()}
}else{'pc_39_world_material_real_ff_scratch_integration'})
$out=Join-Path $work 'scratch_emit'
$log=Join-Path $work 'integrated_emitter.log'
$buildLog=Join-Path $work 'build.log'
$report=Join-Path $work $(if($IntegrateWorld){'aztec_pc_world_core_integrated_oat_run.json'}else{'aztec_39_source_world_material_scratch_ff_gate.json'})
$emitManifest=Join-Path $out 'nuclear_xenon_renderer_emit.json'
# The pinned native Win32 Unlinker uses legacy MAX_PATH APIs. The original
# ALL probe's 266-character FF path could be written by modern .NET but
# failed native OAT with "Could not find file". Fail before the expensive
# .NET build if an output path cannot be opened by that legacy consumer.
$plannedFf=Join-Path $out 'nazi_zombie_aztec.ff'
Write-Host "[AZTEC-WORLD39-FF] OAT candidate FF pathLength=$($plannedFf.Length) output=$plannedFf"
if($plannedFf.Length -ge 260){
    throw "Native OAT MAX_PATH risk: candidate FF path is $($plannedFf.Length) characters. Shorten the scratch output directory before running OAT: $plannedFf"
}


foreach($p in @($source,$codec,$proof,$nativeIdentity,$renderBinding,$raw,$modelBinding,$map,$com,$cli,$oat)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "39-source Material fullgraph scratch FF prerequisite missing: $p"
    }
}
if($IntegrateWorld){
    foreach($p in @($worldSeed,$worldSurface76)){
        if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
            throw "Integrated GfxWorld requires already-proven original-PC world seed and 735 Xbox GfxSurface source: $p"
        }
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
if($IntegrateWorld){
    Write-Host "[AZTEC-WORLD39-FF] REAL SOURCE GFXWORLD CORE: 735 surfaces, 39 Materials, vertex/index buffers (probe=$ProbeLevel)"
    if($DpvsStaticProbe){
        Write-Host '[AZTEC-WORLD39-FF] STATIC DPVS PROBE: source 613 sorted surface indices + original PC static model instances, OAT scratch gate only'
    }
    if($Gate8ChildProbe){
        Write-Host "[AZTEC-WORLD39-FF] GATE8 SOURCE CHILD PROBE: level=$Gate8ChildLevel (Planes -> Nodes -> All)"
    }
    if($Gate8FiveModelProbe){
        Write-Host '[AZTEC-WORLD39-FF] OPT-IN Gate8 FIVE source-owned XModel graph bodies: two prior + three real foliage'
        Write-Host '[AZTEC-WORLD39-FF] This does NOT yet serialize all 1,903 static draw records.'
    }
    if($Gate8AllModelProbe){
        Write-Host "[AZTEC-WORLD39-FF] ALL PC static model identities: $($allStage.modelCount) real source bodies, $($allStage.nativeExternalReferenceModelTypes) native references."
        if($Gate10StaticDrawProbe){
            Write-Host '[AZTEC-GATE10-FF] Attempting 1903 source-owned GfxStaticModelDrawInst links during THIS isolated build. OAT still pending.'
        }else{
            Write-Host '[AZTEC-WORLD39-FF] IMPORTANT: still 0/1903 GfxStaticModelDrawInst links; not a finished native map.'
        }
    }
    Write-Host '[AZTEC-WORLD39-FF] Only partial DPVS scalar+surface graph: other DPVS/cell/lightmap/runtime children remain incomplete'
}else{
    Write-Host '[AZTEC-WORLD39-FF] BASELINE: no FF-resident GfxWorld or 735-surface world linkage in this mode'
}
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
    if($IntegrateWorld){$argsList+=@($worldSeed,$worldSurface76,$source)}
    $priorProbe=$env:WAW360_AZTEC_WORLD_CHILD_PROBE
    $priorDpvs=$env:WAW360_AZTEC_WORLD_DPVS_STATIC_PROBE
    $priorGate8=$env:WAW360_AZTEC_GATE8_CHILD_CORPUS
    $priorGate8Level=$env:WAW360_AZTEC_GATE8_CHILD_LEVEL
    $priorGate10Light=$env:WAW360_AZTEC_GATE10_SOURCE_LIGHT_REPORT
    $priorGate10Plan=$env:WAW360_AZTEC_GATE10_ROOT_RELOCATION_SUMMARY
    $priorGate11=$env:WAW360_AZTEC_GATE11_CELL_GRAPH_REPORT
    $priorGate11Primitive=$env:WAW360_AZTEC_GATE11_PRIMITIVE_BATCH
    $priorGate11Lighting=$env:WAW360_AZTEC_GATE11_LIGHTING_BATCH
    $priorGate11Images=$env:WAW360_AZTEC_GATE11_IMAGE_HEADERS_BATCH
    $priorGate11Links=$env:WAW360_AZTEC_GATE11_IMAGE_REFERENCE_REBASE
    try{
        $env:WAW360_AZTEC_WORLD_CHILD_PROBE=$ProbeLevel
        if($DpvsStaticProbe){
            $env:WAW360_AZTEC_WORLD_DPVS_STATIC_PROBE='sorted-smodel'
        }else{
            Remove-Item Env:\WAW360_AZTEC_WORLD_DPVS_STATIC_PROBE -ErrorAction SilentlyContinue
        }
        if($Gate8ChildProbe){
            $env:WAW360_AZTEC_GATE8_CHILD_CORPUS=$gate8Source
            $env:WAW360_AZTEC_GATE8_CHILD_LEVEL=$Gate8ChildLevel
        }else{
            Remove-Item Env:\WAW360_AZTEC_GATE8_CHILD_CORPUS -ErrorAction SilentlyContinue
            Remove-Item Env:\WAW360_AZTEC_GATE8_CHILD_LEVEL -ErrorAction SilentlyContinue
        }
        if($Gate10StaticDrawProbe){
            $env:WAW360_AZTEC_GATE10_SOURCE_LIGHT_REPORT=$gate10Light
            $env:WAW360_AZTEC_GATE10_ROOT_RELOCATION_SUMMARY=$gate10Plan
            Write-Host '[AZTEC-GATE10-PROGRESS] [########------------] 40% | re-plan and emit GfxWorld draws with new XModel pointers'
        }else{
            Remove-Item Env:\WAW360_AZTEC_GATE10_SOURCE_LIGHT_REPORT -ErrorAction SilentlyContinue
            Remove-Item Env:\WAW360_AZTEC_GATE10_ROOT_RELOCATION_SUMMARY -ErrorAction SilentlyContinue
        }
        if($Gate11CellProbe){
            $env:WAW360_AZTEC_GATE11_CELL_GRAPH_REPORT=$gate11Summary
            Write-Host '[AZTEC-GATE11-PROGRESS] [########------------] 40% | emit source-owned 11 GfxCells and 740 AABB trees in isolated new FF'
        }else{
            Remove-Item Env:\WAW360_AZTEC_GATE11_CELL_GRAPH_REPORT -ErrorAction SilentlyContinue
        }
        if($Gate11PrimitiveProbe){
            $env:WAW360_AZTEC_GATE11_PRIMITIVE_BATCH=$gate11Primitive
            Write-Host '[AZTEC-GATE11-PRIMITIVE] Emit original-PC skyStartSurfs + 183 brush models + 2 water buffers, isolated FF.'
        }else{
            Remove-Item Env:\WAW360_AZTEC_GATE11_PRIMITIVE_BATCH -ErrorAction SilentlyContinue
        }
        if($Gate11LightingProbe){
            $env:WAW360_AZTEC_GATE11_LIGHTING_BATCH=$gate11Lighting
            Write-Host '[AZTEC-GATE11-LIGHTING] ONE combined native FF: 4,822,860B light-grid, 1903 shadow model refs, two empty light regions.'
            Write-Host '[AZTEC-GATE11-LIGHTING] Reflection/lightmap GfxImage payload bindings and Xbox streamInfo remain BLOCKED.'
        }else{
            Remove-Item Env:\WAW360_AZTEC_GATE11_LIGHTING_BATCH -ErrorAction SilentlyContinue
        }
        if($Gate11ImageHeadersProbe){
            $env:WAW360_AZTEC_GATE11_IMAGE_HEADERS_BATCH=$gate11ImageHeaders
            Write-Host '[AZTEC-GATE11-IMG-HEADERS] Two source-owned 16B reflection heads, two 8B lightmap heads; all SIX missing GfxImage* stay NULL.'
            Write-Host '[AZTEC-GATE11-IMG-HEADERS] streamInfo structurally 0/0/null per original Xbox ABI; no invented stream arrays.'
        }else{
            Remove-Item Env:\WAW360_AZTEC_GATE11_IMAGE_HEADERS_BATCH -ErrorAction SilentlyContinue
        }
        if($Gate11ImageRootLinksProbe){
            $env:WAW360_AZTEC_GATE11_IMAGE_REFERENCE_REBASE='1'
            Write-Host '[AZTEC-GATE11-IMG-LINKS] Six original-PC star-image pointers rebased to existing native GfxImage ROOT INSERT identities.'
            Write-Host '[AZTEC-GATE11-IMG-LINKS] The six target images are still NAME-ONLY shells, NO pixel bodies; NO hardware.'
        }else{
            Remove-Item Env:\WAW360_AZTEC_GATE11_IMAGE_REFERENCE_REBASE -ErrorAction SilentlyContinue
        }
        & dotnet @argsList 2>&1 | ForEach-Object {[string]$_} |
            Set-Content -LiteralPath $log -Encoding UTF8
        $code=[int]$LASTEXITCODE
    }finally{
        if($null -eq $priorProbe){
            Remove-Item Env:\WAW360_AZTEC_WORLD_CHILD_PROBE -ErrorAction SilentlyContinue
        }else{
            $env:WAW360_AZTEC_WORLD_CHILD_PROBE=$priorProbe
        }
        if($null -eq $priorDpvs){
            Remove-Item Env:\WAW360_AZTEC_WORLD_DPVS_STATIC_PROBE -ErrorAction SilentlyContinue
        }else{
            $env:WAW360_AZTEC_WORLD_DPVS_STATIC_PROBE=$priorDpvs
        }
        if($null -eq $priorGate8){
            Remove-Item Env:\WAW360_AZTEC_GATE8_CHILD_CORPUS -ErrorAction SilentlyContinue
        }else{
            $env:WAW360_AZTEC_GATE8_CHILD_CORPUS=$priorGate8
        }
        if($null -eq $priorGate8Level){
            Remove-Item Env:\WAW360_AZTEC_GATE8_CHILD_LEVEL -ErrorAction SilentlyContinue
        }else{
            $env:WAW360_AZTEC_GATE8_CHILD_LEVEL=$priorGate8Level
        }
        if($null -eq $priorGate10Light){
            Remove-Item Env:\WAW360_AZTEC_GATE10_SOURCE_LIGHT_REPORT -ErrorAction SilentlyContinue
        }else{
            $env:WAW360_AZTEC_GATE10_SOURCE_LIGHT_REPORT=$priorGate10Light
        }
        if($null -eq $priorGate11){
            Remove-Item Env:\WAW360_AZTEC_GATE11_CELL_GRAPH_REPORT -ErrorAction SilentlyContinue
        }else{
            $env:WAW360_AZTEC_GATE11_CELL_GRAPH_REPORT=$priorGate11
        }
        if($null -eq $priorGate11Primitive){
            Remove-Item Env:\WAW360_AZTEC_GATE11_PRIMITIVE_BATCH -ErrorAction SilentlyContinue
        }else{
            $env:WAW360_AZTEC_GATE11_PRIMITIVE_BATCH=$priorGate11Primitive
        }
        if($null -eq $priorGate11Lighting){
            Remove-Item Env:\WAW360_AZTEC_GATE11_LIGHTING_BATCH -ErrorAction SilentlyContinue
        }else{
            $env:WAW360_AZTEC_GATE11_LIGHTING_BATCH=$priorGate11Lighting
        }
        if($null -eq $priorGate11Images){
            Remove-Item Env:\WAW360_AZTEC_GATE11_IMAGE_HEADERS_BATCH -ErrorAction SilentlyContinue
        }else{
            $env:WAW360_AZTEC_GATE11_IMAGE_HEADERS_BATCH=$priorGate11Images
        }
        if($null -eq $priorGate11Links){
            Remove-Item Env:\WAW360_AZTEC_GATE11_IMAGE_REFERENCE_REBASE -ErrorAction SilentlyContinue
        }else{
            $env:WAW360_AZTEC_GATE11_IMAGE_REFERENCE_REBASE=$priorGate11Links
        }
        if($null -eq $priorGate10Plan){
            Remove-Item Env:\WAW360_AZTEC_GATE10_ROOT_RELOCATION_SUMMARY -ErrorAction SilentlyContinue
        }else{
            $env:WAW360_AZTEC_GATE10_ROOT_RELOCATION_SUMMARY=$priorGate10Plan
        }
    }
}finally{$ErrorActionPreference=$save}
if(-not(Test-Path -LiteralPath $emitManifest -PathType Leaf)){
    Get-Content -LiteralPath $log -Tail 30 | ForEach-Object {Write-Host "[AZTEC-WORLD39-FF] EMIT $_"}
    throw "Full OAT scratch FF emitter did not produce its status manifest (exit=$code): $log"
}
$e=Get-Content -LiteralPath $emitManifest -Raw | ConvertFrom-Json
if($IntegrateWorld){
    $core=([bool]$e.OriginalPcGfxWorldCoreEmitted) -and
        [int]$e.RealMaterialRoots -eq 39 -and
        [int]$e.NativeRoots -eq 8509
    $diagnosticProbe=($ProbeLevel -ne 'Full') -or $DpvsStaticProbe -or $Gate8ChildProbe
    $candidateOatPass=($code -eq 0) -and
        ([bool]$e.ValidationPass) -and
        [bool]$e.OatZeroWarningsZeroErrors -and
        [bool]$e.ExactIdentitySequence -and
        [bool]$e.LayoutReplayExact
    if($Gate8FiveModelProbe){
        $candidateOatPass=$candidateOatPass -and
            [bool]$e.ValidationOnly -and
            -not [bool]$e.ReadyForHardware -and
            [int]$e.NativeRoots -eq 8509 -and
            [int]$e.RealXModelRoots -eq 5 -and
            [int]$e.RealMaterialRoots -eq 39 -and
            [int]$e.RealImageRoots -eq 2 -and
            [int]$e.RealRawFileRoots -eq 114 -and
            [int]$e.RealMapEntsRoots -eq 1 -and
            [int]$e.RealComWorldRoots -eq 1
    }
    if($Gate10StaticDrawProbe){
        $candidateOatPass=$candidateOatPass -and
            [int]$e.OriginalPcGfxWorldGate10DrawRecords -eq 1903 -and
            [int]$e.OriginalPcGfxWorldGate10UnverifiedStockDraws -eq 1 -and
            [bool]$e.ValidationOnly -and -not [bool]$e.ReadyForHardware
    }
    if($Gate11CellProbe){
        $typed=Get-Content -LiteralPath $gate11Summary -Raw | ConvertFrom-Json
        $candidateOatPass=$candidateOatPass -and
            [int]$e.OriginalPcGfxWorldGate11Cells -eq 11 -and
            [int]$e.OriginalPcGfxWorldGate11AabbTrees -eq 740 -and
            [int]$e.OriginalPcGfxWorldGate11SmodelIndexes -eq [int]$typed.originalPcTypedSmodelIndicesCount -and
            [int]$e.OriginalPcGfxWorldGate11ReflectionIndexes -eq [int]$typed.originalPcTypedCellReflectionProbeIndicesCount -and
            [bool]$e.ValidationOnly -and -not [bool]$e.ReadyForHardware
    }
    if($Gate8AllModelProbe){
        $candidateOatPass=$candidateOatPass -and
            [bool]$e.ValidationOnly -and
            -not [bool]$e.ReadyForHardware -and
            [int]$e.NativeRoots -eq 8509 -and
            [int]$e.RealXModelRoots -eq [int]$allStage.modelCount -and
            [int]$e.RealMaterialRoots -eq 39 -and
            [int]$e.RealImageRoots -eq 2 -and
            [int]$e.RealRawFileRoots -eq 114 -and
            [int]$e.RealMapEntsRoots -eq 1 -and
            [int]$e.RealComWorldRoots -eq 1
    }
    $result=[ordered]@{
        schema='WaW360.AztecPcGfxWorldIntegratedFastFileOatRun.v1'
        status=if($Gate11ImageRootLinksProbe){
            if($core -and $candidateOatPass){
                'AZTEC_GATE11_SIX_IMAGE_ROOT_NAME_SHELL_LINKS_OAT_PASS_PIXELS_UNPROVEN'
            }else{
                'AZTEC_GATE11_SIX_IMAGE_ROOT_NAME_SHELL_LINKS_OAT_BLOCKED'
            }
        }elseif($Gate11ImageHeadersProbe){
            if($core -and $candidateOatPass){
                'AZTEC_GATE11_SOURCE_IMAGE_HEADERS_OAT_PASS_PIXELS_UNPROVEN'
            }else{
                'AZTEC_GATE11_SOURCE_IMAGE_HEADERS_OAT_BLOCKED'
            }
        }elseif($Gate11LightingProbe){
            if($core -and $candidateOatPass){
                'AZTEC_GATE11_LIGHT_GRID_SHADOW_REGION_OAT_LOAD_PASS_FIELDS_UNVERIFIED'
            }else{
                'AZTEC_GATE11_LIGHT_GRID_SHADOW_REGION_OAT_LOAD_BLOCKED'
            }
        }elseif($Gate11PrimitiveProbe){
            if($core -and $candidateOatPass){
                'AZTEC_GATE11_PRIMITIVE_SKY_BRUSH_WATER_OAT_LOAD_PASS_REMAINDER_PENDING'
            }else{
                'AZTEC_GATE11_PRIMITIVE_SKY_BRUSH_WATER_OAT_LOAD_BLOCKED'
            }
        }elseif($Gate11CellProbe){
            if($core -and $candidateOatPass){
                'AZTEC_GATE11_11_CELL_740_AABB_DIAGNOSTIC_OAT_LOAD_PASS_REMAINDER_PENDING'
            }else{
                'AZTEC_GATE11_11_CELL_740_AABB_DIAGNOSTIC_OAT_LOAD_BLOCKED'
            }
        }elseif($Gate10StaticDrawProbe){
            if($core -and $candidateOatPass){
                'AZTEC_GATE10_1903_STATIC_DRAW_DIAGNOSTIC_OAT_PASS_STOCK_UNVERIFIED'
            }else{
                'AZTEC_GATE10_1903_STATIC_DRAW_DIAGNOSTIC_OAT_BLOCKED'
            }
        }elseif($Gate8AllModelProbe){
            if($core -and $candidateOatPass){
                if([int]$allStage.unresolvedNativeReferenceModelTypes -gt 0){
                    'AZTEC_GATE8_REAL_OWNED_XMODEL_FF_OAT_PASS_REFERENCES_UNRESOLVED'
                }else{
                    'AZTEC_GATE8_ALL_CLASSIFIED_STATIC_XMODEL_TYPES_8509_ROOT_WORLD_OAT_PASS'
                }
            }
            else{'AZTEC_GATE8_ALL_STATIC_XMODEL_WORLD_OAT_BLOCKED'}
        }elseif($Gate8FiveModelProbe){
            if($core -and $candidateOatPass){'AZTEC_GATE8_FIVE_REAL_XMODELS_8509_ROOT_WORLD_OAT_PASS'}
            else{'AZTEC_GATE8_FIVE_REAL_XMODELS_WORLD_OAT_BLOCKED'}
        }elseif($Gate8ChildProbe -and $Gate8ChildLevel -ne 'All'){
            if($core -and $candidateOatPass){'AZTEC_GFXWORLD_PC_GATE8_PARTIAL_CHILD_BISECTION_OAT_PASS'}
            else{'AZTEC_GFXWORLD_PC_GATE8_PARTIAL_CHILD_BISECTION_OAT_BLOCKED'}
        }elseif($Gate8ChildProbe){
            if($core -and $candidateOatPass){'AZTEC_GFXWORLD_PC_GATE8_PLANES_NODES_LAYER_OAT_PASS'}
            else{'AZTEC_GFXWORLD_PC_GATE8_PLANES_NODES_LAYER_OAT_BLOCKED'}
        }elseif($DpvsStaticProbe){
            if($core -and $candidateOatPass){'AZTEC_GFXWORLD_SOURCE_STATIC_DPVS_CHILD_PROBE_OAT_PASS'}
            else{'AZTEC_GFXWORLD_SOURCE_STATIC_DPVS_CHILD_PROBE_OAT_BLOCKED'}
        }elseif($diagnosticProbe){
            if($core -and $candidateOatPass){'AZTEC_GFXWORLD_CHILD_PROBE_OAT_LOAD_PASS'}
            else{'AZTEC_GFXWORLD_CHILD_PROBE_OAT_BLOCKED'}
        }elseif($core -and $candidateOatPass){
            'AZTEC_ORIGINAL_PC_GFXWORLD_CORE_IN_REAL_FF_OAT_LOAD_PASS_REMAINING_CHILDREN'
        }else{'AZTEC_ORIGINAL_PC_GFXWORLD_INTEGRATION_OAT_BLOCKED'}
        probeLevel=$ProbeLevel
        sourceBackedStaticDpvsProbe=[bool]$DpvsStaticProbe
        sourceBackedGate8PlaneNodeLayerProbe=($Gate8ChildProbe -and $Gate8ChildLevel -eq 'All')
        sourceBackedGate8PartialChildBisect=($Gate8ChildProbe -and $Gate8ChildLevel -ne 'All')
        sourceBackedGate8ProbeLevel=if($Gate8ChildProbe){$Gate8ChildLevel}else{''}
        fiveSourceOwnedXModelProbe=[bool]$Gate8FiveModelProbe
        all27StaticSourceXModelProbe=[bool]$Gate8AllModelProbe
        gate10SourceDrawDiagnostic=[bool]$Gate10StaticDrawProbe
        gate10DrawRecordsInEmittedFf=[int]$e.OriginalPcGfxWorldGate10DrawRecords
        gate10StockModelDrawUnverified=[int]$e.OriginalPcGfxWorldGate10UnverifiedStockDraws
        gate10ProductionApproved=$false
        gate11PhysicalCellDiagnostic=[bool]$Gate11CellProbe
        gate11PrimitiveProbe=[bool]$Gate11PrimitiveProbe
        gate11LightingCombinedProbe=[bool]$Gate11LightingProbe
        gate11ImageHeadersProbe=[bool]$Gate11ImageHeadersProbe
        gate11ImageRootLinksProbe=[bool]$Gate11ImageRootLinksProbe
        gate11ImageRootNamesLinked=if($Gate11ImageRootLinksProbe){6}else{0}
        gate11ImageShellPixelPayloadsVerified=0
        gate11ImageRootLinksIndependentReadbackProven=$false
        gate11SourceReflectionHeaderCount=if($Gate11ImageHeadersProbe){2}else{0}
        gate11SourceLightmapHeaderCount=if($Gate11ImageHeadersProbe){2}else{0}
        gate11UnboundGeneratedImagePointers=if($Gate11ImageHeadersProbe){6}else{0}
        gate11NativeEmptyStreamStructRequiredByReference=[bool]$Gate11ImageHeadersProbe
        gate11ImageHeaderIndependentLoadedReadbackProven=$false
        gate11LightingGridBytes=if($Gate11LightingProbe){4822860L}else{0L}
        gate11LightingShadowModelReferences=if($Gate11LightingProbe){1903}else{0}
        gate11LightingEmptyRegionHeaders=if($Gate11LightingProbe){2}else{0}
        gate11LightingIndependentLoadedFieldReadback=$false
        gate11LightmapImagePayloadsResolved=$false
        gate11XboxStreamInfoReconstructed=$false
        gate11PrimitiveSkySurfaces=if($Gate11PrimitiveProbe){2}else{0}
        gate11PrimitiveBrushModels=if($Gate11PrimitiveProbe){183}else{0}
        gate11PrimitiveWaterBytes=if($Gate11PrimitiveProbe){2097408L}else{0L}
        gate11PrimitiveIndependentReadbackProven=$false
        gate11SourceCellsInFf=[int]$e.OriginalPcGfxWorldGate11Cells
        gate11AabbTreesInFf=[int]$e.OriginalPcGfxWorldGate11AabbTrees
        gate11SmodelIndicesInFf=[int]$e.OriginalPcGfxWorldGate11SmodelIndexes
        gate11ReflectionIndicesInFf=[int]$e.OriginalPcGfxWorldGate11ReflectionIndexes
        gate11FullWorldClosureApproved=$false
        all27SourceModelStagingManifest=if($Gate8AllModelProbe){$allStaging}else{''}
        unresolvedNativeSourceModelReferences=if($Gate8AllModelProbe){[int]$allStage.unresolvedNativeReferenceModelTypes}else{0}
        unresolvedNativeSourceModelDraws=if($Gate8AllModelProbe){[int]$allStage.unresolvedNativeReferenceDraws}else{0}
        sourceStaticModelTypesClassified=if($Gate8AllModelProbe){27}else{0}
        sourceOwnedStaticModelTypes=if($Gate8AllModelProbe){[int]$allStage.realOwnedStaticModelTypes}else{0}
        verifiedNativeExternalModelReferences=if($Gate8AllModelProbe){[int]$allStage.nativeExternalReferenceModelTypes}else{0}
        nativeExternalModelResidencyVerified=$false
        realXModelRootsInCurrentFf=[int]$e.RealXModelRoots
        fiveSourceModelStagingManifest=if($Gate8FiveModelProbe){$staging}else{''}
        realOriginalPcGfxWorldCoreEmitted=($core -and -not $diagnosticProbe)
        allOriginalPcGfxWorldDpvsChildrenComplete=[bool]$e.OriginalPcGfxWorldFullDpvsGraphComplete
        realPcGfxSurfaceCount=if($ProbeLevel -in @('Surfaces','MaterialMemory','Full')){735}else{0}
        realPcGfxWorldMaterialRoots=39
        finalMaterialReferencesRebasedDuringCurrentFfEmit=($core -and $ProbeLevel -in @('Surfaces','MaterialMemory','Full'))
        OatPass=$candidateOatPass
        OatError=[string]$e.OatGateError
        onlyRealGameDataUsed=$true
        fullNativeWorldSourceClosurePass=$false
        bothMainAndLoadFfsCompleted=$false
        hardwareReady=$false
        outputFastFile=[string]$e.OutputFastFile
        emitterReport=$emitManifest
        emitterLog=$log
        note=if($Gate11ImageRootLinksProbe){
            'ISOLATED name-only structural pointer probe: 2 reflection and 2 lightmap source headers; six PC-named image references now point to their exact native 8509-root GfxImage SHELL aliases, but all six remain unverified dummy shells without genuine pixel/texture payload. OAT load is not runtime/texture verification, no hardware.'
        }elseif($Gate11ImageHeadersProbe){
            'ONE ISOLATED PC-source candidate with two original-PC GfxReflectionProbe 16B heads and two GfxLightmapArray 8B heads, 6 unresolved generated-image GfxImage* pointers explicitly NULL. Native measured empty stream structure; source-owned grid/shadow/cell/draw regressions. All image pixels and material runtime semantics unresolved, no hardware deployment.'
        }elseif($Gate11LightingProbe){
            'SINGLE new isolated candidate: 4.82MB original-PC typed lightgrid and 1903 shadow model indexes and two original-PC empty light-region headers. OAT FF load alone is NOT a byte-level lighting proof. Six GfxImage identity-name matches remain payload UNVERIFIED; runtime Xbox streaming metadata still missing. No hardware admission.'
        }elseif($Gate11CellProbe){
            'Gate11 experimental source-only 11-cell/740-AABB tree array plus nested original-PC model/reflection indices physically emitted in ISOLATED scratch GfxWorld and OAT list checked. Original 1903 Gate10 draws remain unmodified in old FF. Full GfxWorld lightmap/sky/dynamic DPVS/stock/light semantics are still incomplete. NOT deployable.'
        }elseif($Gate10StaticDrawProbe){
            'Diagnostic-only source-owned 1903 x 60B smodelDrawInsts child physically serialized with CURRENT Stage4D XModel pointer rebases. One stock model NULL; lighting handle hypotheses and four packed rotations still need semantic proof. Complete world children, runtime assets and Xbox test blocked.'
        }elseif($Gate8AllModelProbe){
            'SCRATCH-ONLY: source 27 model NAMES are accounted by actual converted source mesh bodies, proven native refs, and explicitly UNRESOLVED PC reference/native-root mismatches. Only real source bodies are serialized; unresolved models remain identity shells. OAT must accept real FF. No 1903 GfxWorld static draws, external residency, complete DPVS, images, runtime dependency closure or Xbox hardware approval.'
        }elseif($Gate8FiveModelProbe){
            'SCRATCH-ONLY five source-owned Xenon XModel graph root upgrades, with original-PC Gate8 BSP plane/node/layer world. Exactly 3 static draw models added to the 2 earlier canaries, but all 1903 static draw entries remain unbound, and other DPVS/cell/runtime branches are incomplete. No main/_load pair or Xbox hardware.'
        }elseif($Gate8ChildProbe){
            'SCRATCH-ONLY Gate8 child probe: OAT typed original-PC 3447 BSP planes + 24511 nodes + vld bytes + static DPVS + original-PC world core. This is NOT full DPVS, not full runtime dependencies, not a native main/_load pair, and not hardware approved.'
        }elseif($DpvsStaticProbe){
            'SCRATCH-ONLY STATIC DPVS PROBE: 613 sortedSurface indices and original-PC static model instances are source-derived and placed before the 735 surfaces. Even with OAT pass, this does NOT complete other static/dynamic DPVS, cell/lightmap, full runtime dependencies or hardware release.'
        }elseif($diagnosticProbe){
            'SCRATCH-ONLY CHILD PROBE: requested child level is a deliberately incomplete world graph for OAT loader bisection. Even if OAT passes, it is not the complete core, not a deployable Xbox FF, and not evidence of correct runtime materials.'
        }else{
            'Actual original-PC GfxWorld root, all 735 real Xbox GfxSurface material refs, 39 material owners, 32713 vertices, 58653 indices emitted by Stage4E after final Stage4D allocation. Other PC-derived DPVS/cell/scene/lightmap children are still incomplete in the initial integrated core path. Do not package for native hardware until loader, full required world and both FFs pass.'
        }
    }
    $result|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $report -Encoding UTF8
    if($Gate11CellProbe){
        $ffPath=[string]$e.OutputFastFile
        $newLength=if(Test-Path -LiteralPath $ffPath -PathType Leaf){[long](Get-Item -LiteralPath $ffPath).Length}else{0L}
        Write-Host "[AZTEC-GATE11-SIZE] new FF=$newLength B, cells=$($e.OriginalPcGfxWorldGate11Cells) trees=$($e.OriginalPcGfxWorldGate11AabbTrees), modelIndexes=$($e.OriginalPcGfxWorldGate11SmodelIndexes)"
        Write-Host "[AZTEC-GATE11-PROGRESS] [####################] 100% | OAT list=$candidateOatPass; independent per-cell readback still required. NOT Xbox."
    }
    if($Gate10StaticDrawProbe -and -not $Gate11CellProbe){
        $ffPath=[string]$e.OutputFastFile
        if(Test-Path -LiteralPath $ffPath -PathType Leaf){
            $newLength=[long](Get-Item -LiteralPath $ffPath).Length
            $priorFf=Join-Path $base 'pc_gate8_29x_ff\scratch_emit\nazi_zombie_aztec.ff'
            if(Test-Path -LiteralPath $priorFf -PathType Leaf){
                $previousLength=[long](Get-Item -LiteralPath $priorFf).Length
                Write-Host "[AZTEC-GATE10-SIZE] baseline=$previousLength B new=$newLength B delta=$($newLength-$previousLength) B"
            }else{
                Write-Host "[AZTEC-GATE10-SIZE] new OAT diagnostic FF=$newLength B; previous baseline missing"
            }
        }
        Write-Host "[AZTEC-GATE10-PROGRESS] [####################] 100% | diagnostic FF emitted; OAT pass=$candidateOatPass; NOT Xbox ready"
    }
    Write-Host "[AZTEC-WORLD-INTEGRATED] probeLevel=$ProbeLevel sourceDpvsProbe=$DpvsStaticProbe gate8ChildProbe=$Gate8ChildProbe gate8Level=$Gate8ChildLevel fiveModelProbe=$Gate8FiveModelProbe allStaticModelProbe=$Gate8AllModelProbe realXModels=$($e.RealXModelRoots) fullCore=$($core -and -not $diagnosticProbe) realMaterials=$($e.RealMaterialRoots)/39"
    Write-Host "[AZTEC-WORLD-INTEGRATED] OAT PASS=$candidateOatPass originalPcWorldFF=$($e.OutputFastFile)"
    Write-Host "[AZTEC-WORLD-INTEGRATED] status=$($result.status)"
    Write-Host "[AZTEC-WORLD-INTEGRATED] report=$report"
    if(-not $core -or -not $candidateOatPass){
        # Show the actual OAT loader's stdout/stderr, not just the
        # downstream 0/8509 root-identity reconciliation summary.
        $oatList=Join-Path $out 'oat_hard_gate\nazi_zombie_aztec_oat_xenon_list.log'
        if(Test-Path -LiteralPath $oatList -PathType Leaf){
            Write-Host "[AZTEC-WORLD-INTEGRATED] OAT REAL LOADER LOG: $oatList"
            Get-Content -LiteralPath $oatList -Tail 45 |
                ForEach-Object {Write-Host "[AZTEC-WORLD-INTEGRATED] OAT $_"}
        }else{
            Write-Host "[AZTEC-WORLD-INTEGRATED] OAT list log not found at $oatList"
        }
        Get-Content -LiteralPath $log -Tail 28 | ForEach-Object {Write-Host "[AZTEC-WORLD-INTEGRATED] TRACE $_"}
        throw "Direct integrated world OAT failure; fix this real FF loader evidence next: $log"
    }
    if($diagnosticProbe){
        Write-Host "[AZTEC-WORLD-INTEGRATED] DIAGNOSTIC PROBE PASS ${ProbeLevel} DPVS=$DpvsStaticProbe; partial world OAT loaded. NO XBOX."
        return
    }
    Write-Host '[AZTEC-WORLD-INTEGRATED] Actual FF core world loaded; complete required original PC DPVS children next, then main/_load paired FF and native test.'
    & (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
    return
}
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
