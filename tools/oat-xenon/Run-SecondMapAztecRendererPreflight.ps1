[CmdletBinding()]
param(
    [string]$MapName='nazi_zombie_aztec_V2.2',
    [string]$OutputRoot='',
    [switch]$SkipCliBuild
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutputRoot) { $OutputRoot=Join-Path $repo 'output\second_map_baselines' }
$base=Join-Path $OutputRoot $MapName
$graphProofPath=Join-Path $base 'native_material_mapping\native_graph_proof\native_graph_proof_result.json'
$sourceProofPath=Join-Path $base 'native_material_mapping\native_material_mapping_result.json'
$pcClosurePath=Join-Path $base 'native_material_mapping\closure_reconciliation\closure_reconciliation_result.json'
$identityPath=Join-Path $base 'native_material_mapping\main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
foreach($f in @($graphProofPath,$sourceProofPath,$pcClosurePath,$identityPath)) {
    if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { throw "Previous verified evidence missing: $f" }
}
$graph=Get-Content -LiteralPath $graphProofPath -Raw | ConvertFrom-Json
$source=Get-Content -LiteralPath $sourceProofPath -Raw | ConvertFrom-Json
$pc=Get-Content -LiteralPath $pcClosurePath -Raw | ConvertFrom-Json
$identity=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$golden='e0f3c244dd7b72d0fe71c05faf014c2c9bfb05f770183954b3e7cde625eba98f'
$native=[string]$graph.verifiedGoldenXboxFastFile
if ([string]$graph.schema -ne 'WaW360.SecondMapAztecNativeGraphProof.v1' -or
    [string]$graph.mapFolder -ine $MapName -or
    [string]$graph.status -ne 'IDENTITY_AND_TYPED_GRAPH_IMPORT_PASS' -or
    -not [bool]$graph.nativeGraphIdentityPass -or
    -not [bool]$graph.typedGraphImporterPass -or
    [int]$graph.oatReportedErrors -ne 0 -or
    [string]$graph.nativeSha256 -ine $golden -or
    [int]$graph.nativeIdentityTechniqueSets -ne 70 -or
    [int]$graph.nativeIdentityPixelShaders -ne 436 -or
    [string]$source.status -ne 'GOLDEN_VERIFIED_NATIVE_MAPPING_RECORDED' -or
    [string]$source.verifiedXboxReference -ine $native -or
    [string]$pc.schema -ne 'WaW360.SecondMapNativeClosureReconciliation.v1' -or
    [string]$pc.verifiedGoldenXboxFastFile -ine $native -or
    [string]$identity.NativeFastFile -ine $native -or
    -not [bool]$identity.TypeSequenceVerified) {
    throw 'Aztec graph/native/PC identity evidence is not a verified matching set. Refusing preflight.'
}
if (-not (Test-Path -LiteralPath $native -PathType Leaf)) { throw "Golden Xbox FF missing: $native" }
if ([long](Get-Item -LiteralPath $native).Length -ne 82739469L -or
    (Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash -ine $golden) {
    throw 'Golden native Xbox FF changed: fingerprint mismatch.'
}
$dumpRoot=[string]$graph.dumpRoot
if (-not (Test-Path -LiteralPath $dumpRoot -PathType Container)) { throw "Verified graph dump missing: $dumpRoot" }
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) { throw "Converter CLI missing: $cli" }
$work=Join-Path $base 'native_material_mapping\renderer_preflight'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$planRoot=Join-Path $work 'serialization_plan'
$bindRoot=Join-Path $work 'native_root_binding'
$layoutRoot=Join-Path $work 'xbox_block_layout'
foreach ($folder in @($planRoot,$bindRoot,$layoutRoot)) {
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
}
$planJson=Join-Path $planRoot 'xenon_graph_serialization_plan.json'
$bindJson=Join-Path $bindRoot 'nuclear_xenon_root_binding.json'
$layoutJson=Join-Path $layoutRoot 'nuclear_xenon_block_layout.json'
$reportJson=Join-Path $work 'renderer_preflight_result.json'
$reportTxt=Join-Path $work 'renderer_preflight_result.txt'
$stages=New-Object 'System.Collections.Generic.List[object]'

function Invoke-Gate([string]$label,[string[]]$cliArgs,[string]$manifest,[string]$log) {
    if (Test-Path -LiteralPath $manifest -PathType Leaf) { Remove-Item -LiteralPath $manifest -Force }
    $saved=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        $output=@(& dotnet 'run' '--no-build' '-c' 'Release' '--project' $cli '--' @cliArgs 2>&1 | ForEach-Object { [string]$_ })
        $code=[int]$LASTEXITCODE
    } finally { $ErrorActionPreference=$saved }
    [IO.File]::WriteAllLines($log,[string[]]$output,[Text.UTF8Encoding]::new($false))
    Write-Host "[AZTEC-RENDER] $label exit=$code"
    foreach ($line in @($output | Select-Object -Last 9)) { Write-Host "[AZTEC-RENDER]   $line" }
    $item=[pscustomobject]@{Stage=$label;ExitCode=$code;Manifest=$manifest;Log=$log;ReportCreated=(Test-Path -LiteralPath $manifest -PathType Leaf)}
    $script:stages.Add($item)
    if ($code -ne 0 -or -not $item.ReportCreated) { return $null }
    return (Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json)
}
Write-Host '[AZTEC-RENDER] =========================================================='
Write-Host '[AZTEC-RENDER] NATIVE GRAPH -> EXACT ROOT BINDING -> XBOX BLOCK LAYOUT'
Write-Host '[AZTEC-RENDER] 70 TechniqueSets + 436 PixelShaders; no candidate FF emitted.'
Write-Host '[AZTEC-RENDER] Reusing successful graph corpus. No OAT graph redump/build.'
Write-Host '[AZTEC-RENDER] =========================================================='
if (-not $SkipCliBuild) {
    # The preceding StringTable ABI repair changed C# source. The 4B/4C/4D
    # commands use --no-build; compile the CLI incrementally ONCE first.
    # This does NOT rebuild OAT or generate fastfiles.
    $buildLog=Join-Path $work 'cli_incremental_build.log'
    $saved=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        $buildOutput=@(& dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 | ForEach-Object { [string]$_ })
        $buildCode=[int]$LASTEXITCODE
    } finally { $ErrorActionPreference=$saved }
    [IO.File]::WriteAllLines($buildLog,[string[]]$buildOutput,[Text.UTF8Encoding]::new($false))
    Write-Host "[AZTEC-RENDER] Incremental CLI build exit=$buildCode; log=$buildLog"
    if ($buildCode -ne 0) {
        foreach ($line in @($buildOutput | Select-Object -Last 25)) { Write-Host $line }
        throw 'CLI incremental compile failed. Do not run the preflight with stale binaries.'
    }
} else {
    Write-Host '[AZTEC-RENDER] -SkipCliBuild selected; caller must guarantee current Release binaries.'
}
$plan=Invoke-Gate '4B typed serialization plan' @('xenon-graph-plan',$dumpRoot,$planRoot,'70','436') $planJson (Join-Path $work 'serialization_plan.console.log')
$bind=$null
$layout=$null
if ($null -ne $plan -and [bool]$plan.ReadyForSerialization) {
    $bind=Invoke-Gate '4C exact native root binding' @('xenon-graph-bind',$dumpRoot,$native,$bindRoot,'70','436') $bindJson (Join-Path $work 'root_binding.console.log')
} else { Write-Host '[AZTEC-RENDER] Root binding fenced until serializer plan passes.' }
if ($null -ne $bind -and [bool]$bind.exactRootBindingReady) {
    $layout=Invoke-Gate '4D Xbox block layout' @('xenon-graph-layout',$dumpRoot,$native,$identityPath,$bindJson,$layoutRoot,'nazi_zombie_aztec','70','436') $layoutJson (Join-Path $work 'block_layout.console.log')
} else { Write-Host '[AZTEC-RENDER] Block layout fenced until native binding passes.' }
$boundNames=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
if ($null -ne $bind) {
    foreach($b in @($bind.techniqueSetBindings)) { [void]$boundNames.Add([string]$b.Name) }
}
$classCsv=[string]$pc.classificationCsv
if (-not (Test-Path -LiteralPath $classCsv -PathType Leaf)) { throw "PC root classification CSV missing: $classCsv" }
$pcRows=@(Import-Csv -LiteralPath $classCsv)
$mapped=0;$exact=0;$unresolved=0;$crossSource=0;$covered=0
$pending=New-Object 'System.Collections.Generic.List[object]'
foreach($row in $pcRows) {
    if ([string]$row.ObservedByAnyExportedMaterial -ine 'True') { continue }
    $classification=[string]$row.Classification
    $target=[string]$row.XboxTechniqueSet
    if ($classification -in @('LOCAL_MATERIAL_BRIDGE_NATIVE_ROOT_MATCH','CROSS_SOURCE_MATERIAL_BRIDGE_NATIVE_ROOT_MATCH')) {
        $mapped++
        if ($classification -eq 'CROSS_SOURCE_MATERIAL_BRIDGE_NATIVE_ROOT_MATCH') { $crossSource++ }
    } elseif ($classification -eq 'EXACT_NATIVE_NAME_ONLY_NEEDS_MATERIAL_PROOF') { $exact++ }
    else { $unresolved++ }
    if ($target -and $boundNames.Contains($target)) { $covered++ }
    else { $pending.Add([pscustomobject]@{Role=$row.Role;PcTechniqueSet=$row.PcTechniqueSet;Classification=$classification;XboxTarget=$target}) }
}
$planPass=($null -ne $plan -and [bool]$plan.ReadyForSerialization)
$bindPass=($null -ne $bind -and [bool]$bind.exactRootBindingReady -and [int]$bind.matchedTechniqueSets -eq 70 -and [int]$bind.matchedPixelShaders -eq 436)
$layoutPass=($null -ne $layout -and [bool]$layout.LayoutReady -and [int]$layout.RendererTechniqueSets -eq 70 -and [int]$layout.RendererPixelShaders -eq 436)
if ($planPass -and $bindPass -and $layoutPass) { $status='NATIVE_RENDERER_LAYOUT_READY_ORACLE_ONLY' }
elseif (-not $planPass) { $status='SERIALIZATION_PLAN_BLOCKED' }
elseif (-not $bindPass) { $status='ROOT_BINDING_BLOCKED' }
else { $status='XBOX_BLOCK_LAYOUT_BLOCKED' }
$summary=[ordered]@{
    schema='WaW360.AztecRendererPreflight.v1'
    mapFolder=$MapName
    nativeGoldenFF=$native
    nativeSha256=$golden
    graphDump=$dumpRoot
    status=$status
    typedSerializationPlanPass=$planPass
    exactNativeRootBindingPass=$bindPass
    xboxBlockLayoutPass=$layoutPass
    serializedTechniqueSetRoots=$(if($null -ne $bind){[int]$bind.matchedTechniqueSets}else{0})
    serializedPixelShaderRoots=$(if($null -ne $bind){[int]$bind.matchedPixelShaders}else{0})
    layoutTempBytes=$(if($null -ne $layout){[long]$layout.TempBytes}else{$null})
    layoutVirtualBytes=$(if($null -ne $layout){[long]$layout.VirtualBytes}else{$null})
    layoutPhysicalBytes=$(if($null -ne $layout){[long]$layout.PhysicalBytes}else{$null})
    materialReferencedPcRootEntries=($mapped+$exact+$unresolved)
    nativeTargetsMaterialEvidence=$mapped
    ofWhichCrossSource=$crossSource
    nativeTargetsExactNameOnly=$exact
    materialReferencedUnresolved=$unresolved
    pcMaterialRootsWithBoundNativeGraphTarget=$covered
    pcMaterialRootsWithoutBoundGraphTarget=[object[]]$pending.ToArray()
    stages=[object[]]$stages.ToArray()
    pcSourceOwnedShaderGraphReady=$false
    pcSourceOwnedStringTablePayloadReady=$false
    pcSourceOwnedRendererSerializationReady=$false
    actualConvertedXboxFFProduced=$false
    readyForHardware=$false
    policy=@(
      'The input is a verified NATIVE ORACLE graph; renderer layout PASS is not PC source shader conversion.',
      'Native cached/physical shader binaries are reference-only, not distributable PC-source-derived compiler outputs.',
      'Resolve source ownership, exact-only technique identities and missing references before generating a real Xbox candidate.',
      'Nothing written to Converted Maps. No map FF built or released.'
    )
}
$summary | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $reportJson -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add('AZTEC XENON RENDERER PREFLIGHT: GRAPH SERIALIZATION / ROOT BINDING / LAYOUT')
$lines.Add('=========================================================================')
$lines.Add("Status: $status")
$lines.Add('Native reference graph ONLY; no PC shader conversion.')
$lines.Add("4B Serialization plan=$planPass; 4C native binding=$bindPass; 4D Xbox layout=$layoutPass")
$lines.Add("TechniqueSets bound=$($summary.serializedTechniqueSetRoots)/70; PixelShaders=$($summary.serializedPixelShaderRoots)/436")
$lines.Add("PC material-referenced roots: material evidence=$mapped (cross-source=$crossSource), exact-name-only=$exact, unresolved=$unresolved")
$lines.Add("PC roots with existing bound native graph targets: $covered / $($mapped+$exact+$unresolved)")
foreach($p in $pending) { $lines.Add("  NO TARGET $($p.Role): $($p.PcTechniqueSet) [$($p.Classification)]") }
$lines.Add("Block bytes: TEMP=$($summary.layoutTempBytes) VIRTUAL=$($summary.layoutVirtualBytes) PHYSICAL=$($summary.layoutPhysicalBytes)")
foreach($st in $stages) { $lines.Add("  $($st.Stage): exit=$($st.ExitCode); report=$($st.Manifest); log=$($st.Log)") }
$lines.Add('StringTable note: 4D supports verified 16-byte shell ABI, but its real PC rows/values are NOT yet serialized.')
$lines.Add('NEXT: source-derived shader graph and dependency ownership, two unresolved identities, StringTable contents, validator/hardware candidate.')
$lines.Add('NO FF EMITTED. Converted Maps directory untouched.')
[IO.File]::WriteAllLines($reportTxt,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-RENDER] ================= RESULT =================================='
Write-Host "[AZTEC-RENDER] status=$status"
Write-Host "[AZTEC-RENDER] 4B=$planPass 4C=$bindPass 4D=$layoutPass"
Write-Host "[AZTEC-RENDER] PC graph targets=$covered/$($mapped+$exact+$unresolved); unresolved=$unresolved"
Write-Host "[AZTEC-RENDER] Report: $reportTxt"
Write-Host "[AZTEC-RENDER] JSON: $reportJson"
Write-Host '[AZTEC-RENDER] Native oracle pre-emission verification only; NO Xbox hardware FF built.'
