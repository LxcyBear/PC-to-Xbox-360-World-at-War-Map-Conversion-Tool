[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Gate10 native FF infrastructure regression, not a FF build.
# Validate source-owned draw pointer rebasing with the baseline 8509-root
# Stage4D layout plus synthetic shifted layout. No Xbox donor bytes.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$lightParent=Join-Path $base 'pc_gate8_source_light_codec_candidate'
$plan=Join-Path $base 'pc_gate8_1903_draw_relocation_plan\aztec_1903_model_pointer_relocation_summary.json'
$layout=Join-Path $base 'pc_gate8_29x_ff\scratch_emit\layout_recheck\nuclear_xenon_block_layout.json'
$testProject=Join-Path $repo 'tests\WaW360.Gate10DrawProbe\WaW360.Gate10DrawProbe.csproj'

foreach($p in @($lightParent,$plan,$layout,$testProject)){
    if(-not(Test-Path -LiteralPath $p)){throw "Gate10 original source / root proof missing: $p"}
}
$proofs=@(Get-ChildItem -LiteralPath $lightParent -Filter 'aztec_gate10_source_light_candidate_summary.json' -File -Recurse |
    Sort-Object LastWriteTime -Descending)
if($proofs.Count -lt 1){throw 'Source-derived 1903-draw light candidate not yet created.'}
$light=$proofs[0].FullName
$manifest=Get-Content -LiteralPath $light -Raw|ConvertFrom-Json
if([string]$manifest.status -ne 'AZTEC_GATE10_SOURCE_LIGHT_ARITHMETIC_MATCHES_ORIGINAL_XBOX_1903_NOT_PRODUCTION' -or
   [int]$manifest.reflectionIndexBaseHypothesisExactRows -ne 1903 -or
   [int]$manifest.lightingHandleAdd256HypothesisExactRows -ne 1903 -or
   [int]$manifest.convertedOwnedXModelPointersRetained -ne 1902 -or
   [int]$manifest.externallyResidentStockXModelUnproven -ne 1 -or
   [bool]$manifest.productionApproved){
    throw 'Last PC/native source-owned draw proof not the already validated Gate10 input.'
}
Write-Host '[AZTEC-GATE10-REBASE] =============================================='
Write-Host '[AZTEC-GATE10-REBASE] Verifying shared PC2XB3 per-build XModel pointer rebasing.'
Write-Host '[AZTEC-GATE10-REBASE] Incremental .NET compilation; NO world FF build/OAT/Xbox.'
& dotnet run --project $testProject -c Release -- $light $plan $layout
if($LASTEXITCODE -ne 0){
    throw 'Gate10 baseline rebase and synthetic shifted-layout test failed.'
}
Write-Host '[AZTEC-GATE10-REBASE] PASS, only if every source byte and model pointer exactly matches the known valid baseline.'
Write-Host '[AZTEC-GATE10-REBASE] Remaining: actual source draw array emitter wiring, stock reference, full DPVS/runtime light semantics.'
