[CmdletBinding()]
param(
    [string]$MapName='nazi_zombie_aztec_V2.2',
    [string]$OutputRoot=''
)

Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if ($MapName -ine 'nazi_zombie_aztec_V2.2') { throw 'This fixed-oracle test is for Aztec V2.2 only.' }
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutputRoot) { $OutputRoot=Join-Path $repo 'output\second_map_baselines' }
$root=Join-Path $OutputRoot $MapName
$nativeWork=Join-Path $root 'native_material_mapping'
$graphProofPath=Join-Path $nativeWork 'native_graph_proof\native_graph_proof_result.json'
$preflightPath=Join-Path $nativeWork 'renderer_preflight\renderer_preflight_result.json'
$nativeIdentityPath=Join-Path $nativeWork 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$bindingPath=Join-Path $nativeWork 'renderer_preflight\native_root_binding\nuclear_xenon_root_binding.json'
$layoutPath=Join-Path $nativeWork 'renderer_preflight\xbox_block_layout\nuclear_xenon_block_layout.json'
foreach($f in @($graphProofPath,$preflightPath,$nativeIdentityPath,$bindingPath,$layoutPath)) {
    if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { throw "Previous proof missing: $f" }
}
$graph=Get-Content -LiteralPath $graphProofPath -Raw | ConvertFrom-Json
$preflight=Get-Content -LiteralPath $preflightPath -Raw | ConvertFrom-Json
$identity=Get-Content -LiteralPath $nativeIdentityPath -Raw | ConvertFrom-Json
$binding=Get-Content -LiteralPath $bindingPath -Raw | ConvertFrom-Json
$layout=Get-Content -LiteralPath $layoutPath -Raw | ConvertFrom-Json
$native=[string]$preflight.nativeGoldenFF
$graphRoot=[string]$graph.dumpRoot
$golden='e0f3c244dd7b72d0fe71c05faf014c2c9bfb05f770183954b3e7cde625eba98f'
if ([string]$preflight.schema -ne 'WaW360.AztecRendererPreflight.v1' -or
    [string]$preflight.mapFolder -ine $MapName -or
    [string]$preflight.status -ne 'NATIVE_RENDERER_LAYOUT_READY_ORACLE_ONLY' -or
    -not [bool]$preflight.typedSerializationPlanPass -or
    -not [bool]$preflight.exactNativeRootBindingPass -or
    -not [bool]$preflight.xboxBlockLayoutPass -or
    [string]$graph.status -ne 'IDENTITY_AND_TYPED_GRAPH_IMPORT_PASS' -or
    -not [bool]$graph.nativeGraphIdentityPass -or
    -not [bool]$graph.typedGraphImporterPass -or
    [int]$graph.oatReportedErrors -ne 0 -or
    [int]$graph.nativeIdentityTechniqueSets -ne 70 -or
    [int]$graph.nativeIdentityPixelShaders -ne 436 -or
    [string]$graph.verifiedGoldenXboxFastFile -ine $native -or
    [string]$graph.nativeSha256 -ine $golden -or
    -not [bool]$identity.TypeSequenceVerified -or
    [string]$identity.NativeFastFile -ine $native -or
    -not [bool]$binding.exactRootBindingReady -or
    [string]$binding.nativeFastFile -ine $native -or
    [int]$binding.matchedTechniqueSets -ne 70 -or
    [int]$binding.matchedPixelShaders -ne 436 -or
    -not [bool]$layout.LayoutReady -or
    [int]$layout.RendererTechniqueSets -ne 70 -or
    [int]$layout.RendererPixelShaders -ne 436) {
    throw 'Native 4B/4C/4D proof identity or cardinality mismatch. Stage 4E blocked.'
}
if (-not (Test-Path -LiteralPath $native -PathType Leaf)) { throw "Golden native FF missing: $native" }
if ([long](Get-Item -LiteralPath $native).Length -ne 82739469L -or
    (Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash -ine $golden) {
    throw 'Native golden FF fingerprint mismatch.'
}
if (-not (Test-Path -LiteralPath $graphRoot -PathType Container)) { throw "Verified graph dump missing: $graphRoot" }
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$unlinker=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) { throw "CLI missing: $cli" }
if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) { throw "OAT missing: $unlinker" }

# ONE fixed, script-owned diagnostic folder. Never touches the release-only
# Converted Maps folder and never creates numbered FF build subfolders.
$work=Join-Path $nativeWork 'renderer_preflight\4e_validation'
New-Item -ItemType Directory -Path $work -Force | Out-Null
$emitManifest=Join-Path $work 'nuclear_xenon_renderer_emit.json'
$emitFF=Join-Path $work 'nazi_zombie_aztec.ff'
$emitZone=Join-Path $work 'nuclear_xenon_renderer.zone'
$log=Join-Path $work 'aztec_4e_console.log'
$reportPath=Join-Path $work 'aztec_4e_validation_result.txt'
$jsonPath=Join-Path $work 'aztec_4e_validation_result.json'
foreach($f in @($emitManifest,$emitFF,$emitZone,$reportPath,$jsonPath)) {
    if (Test-Path -LiteralPath $f -PathType Leaf) { Remove-Item -LiteralPath $f -Force }
}
Write-Host '[AZTEC-4E] ============================================================='
Write-Host '[AZTEC-4E] REAL 4E BYTE EMISSION — NATIVE ORACLE VALIDATION ONLY'
Write-Host '[AZTEC-4E] 4B=PASS 4C=PASS 4D=PASS; reusing verified 70/436 graph.'
Write-Host '[AZTEC-4E] Produces ONE scratch FF, NOT a real PC-converted Xbox map.'
Write-Host "[AZTEC-4E] Workspace: $work"
Write-Host '[AZTEC-4E] ============================================================='
$cliArgs=@('run','--no-build','-c','Release','--project',$cli,'--',
           'xenon-graph-emit',$graphRoot,$native,$nativeIdentityPath,$bindingPath,
           $work,'nazi_zombie_aztec',$unlinker,'70','436')
$prev=$ErrorActionPreference
try {
    $ErrorActionPreference='Continue'
    & dotnet @cliArgs 2>&1 | ForEach-Object { [string]$_ } | Set-Content -LiteralPath $log -Encoding UTF8
    $exitCode=[int]$LASTEXITCODE
} finally { $ErrorActionPreference=$prev }
Write-Host "[AZTEC-4E] CLI exit=$exitCode"
if (Test-Path -LiteralPath $log -PathType Leaf) {
    foreach ($line in @(Get-Content -LiteralPath $log -Tail 28)) { Write-Host "[AZTEC-4E]   $line" }
}

$emit=$null
if (Test-Path -LiteralPath $emitManifest -PathType Leaf) {
    $emit=Get-Content -LiteralPath $emitManifest -Raw | ConvertFrom-Json
}
$layoutReplay=($null -ne $emit -and [bool]$emit.LayoutReplayExact)
$identityMatch=($null -ne $emit -and [bool]$emit.ExactIdentitySequence)
$oatZero=($null -ne $emit -and [bool]$emit.OatZeroWarningsZeroErrors)
$validationPass=($null -ne $emit -and [bool]$emit.ValidationPass)
$validationOnly=($null -ne $emit -and [bool]$emit.ValidationOnly)
$ffExists=(Test-Path -LiteralPath $emitFF -PathType Leaf)
$ffBytes=if($ffExists){[long](Get-Item -LiteralPath $emitFF).Length}else{0L}
$nativeRootCount=if($null -ne $emit){[int]$emit.NativeRoots}else{0}
$realMaterials=if($null -ne $emit){[int]$emit.RealMaterialRoots}else{0}
$realImages=if($null -ne $emit){[int]$emit.RealImageRoots}else{0}
$realXmodels=if($null -ne $emit){[int]$emit.RealXModelRoots}else{0}
$realRawfiles=if($null -ne $emit){[int]$emit.RealRawFileRoots}else{0}
$structuralPass=($exitCode -eq 0 -and $layoutReplay -and $identityMatch -and
    $oatZero -and $validationPass -and $validationOnly -and $ffExists -and
    $ffBytes -gt 0 -and $nativeRootCount -eq @($identity.Entries).Count -and
    $realMaterials -eq 0 -and $realImages -eq 0 -and $realXmodels -eq 0 -and $realRawfiles -eq 0)
if ($structuralPass) { $status='NATIVE_4E_BYTE_EMISSION_VALIDATION_PASS' }
elseif ($null -eq $emit) { $status='4E_EMITTER_ABORTED_BEFORE_MANIFEST' }
else { $status='4E_EMISSION_VALIDATION_BLOCKED' }
$report=[ordered]@{
    schema='WaW360.AztecStage4ENativeProof.v1'
    mapFolder=$MapName
    status=$status
    goldenNativeSha256=$golden
    stage4eCliExitCode=$exitCode
    outputFF=$emitFF
    outputFFBytes=$ffBytes
    nativeRoots=$nativeRootCount
    layoutReplayExact=$layoutReplay
    nativeRootSequenceExact=$identityMatch
    oatZeroWarningsZeroErrors=$oatZero
    validationPass=$validationPass
    validationOnly=$validationOnly
    pcOwnedMaterialRoots=$realMaterials
    pcOwnedImageRoots=$realImages
    pcOwnedXModelRoots=$realXmodels
    pcOwnedRawFileRoots=$realRawfiles
    pcSourceOwnedShaderGraphComplete=$false
    pcStringTablePayloadComplete=$false
    hardwareReady=$false
    diagnosticOnly=$true
    emitterManifest=$emitManifest
    cliLog=$log
    notes=@(
        'This FF is a native-shader-oracle and name-shell structural diagnostic, NOT a PC-to-Xbox converted map.',
        'The Xbox shaders come from a user-supplied native reference, not independent PC shader translation.',
        'Other assets remain name/empty shells; StringTable rows and values are not serialized.',
        'An OAT identity pass is not gameplay or graphics proof and must not cause release promotion.',
        'Converted Maps is reserved for working Xbox hardware-verified packages only.'
    )
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add('AZTEC STAGE 4E NATIVE-REFERENCE VALIDATION ONLY — NOT A PC CONVERSION')
$lines.Add('=====================================================================')
$lines.Add("status=$status cliExit=$exitCode")
$lines.Add("layoutReplay=$layoutReplay nativeIdentity=$identityMatch oatZero=$oatZero validationPass=$validationPass validationOnly=$validationOnly")
$lines.Add("nativeRoots=$nativeRootCount scratchFFbytes=$ffBytes")
$lines.Add("PC-owned payloads: Material=$realMaterials Image=$realImages XModel=$realXmodels RawFile=$realRawfiles")
$lines.Add("emitterManifest=$emitManifest")
$lines.Add("consoleLog=$log")
$lines.Add('HARD FENCE: DO NOT copy this diagnostic FF to Xbox or Converted Maps.')
[IO.File]::WriteAllLines($reportPath,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-4E] ================= RESULT ====================================='
Write-Host "[AZTEC-4E] status=$status"
Write-Host "[AZTEC-4E] layoutReplay=$layoutReplay identity=$identityMatch oatZero=$oatZero FFbytes=$ffBytes"
Write-Host '[AZTEC-4E] NOT a PC conversion or hardware test candidate.'
Write-Host "[AZTEC-4E] Report: $reportPath"
Write-Host "[AZTEC-4E] JSON: $jsonPath"
if (-not $structuralPass) { exit 4 }
