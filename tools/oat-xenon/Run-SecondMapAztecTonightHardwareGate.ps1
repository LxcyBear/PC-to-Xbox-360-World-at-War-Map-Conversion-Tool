[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$fullPath=Join-Path $base 'pc_two_xmodel_two_material_fullgraph_integration\aztec_two_pc_xmodel_two_material_fullgraph_integration.json'
$identityPath=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$readbackPath=Join-Path $base 'pc_two_model_material_combined_readback\combined_pc_material_readback.json'
$emitPath=Join-Path $base 'pc_two_xmodel_two_material_fullgraph_integration\scratch_emit\nuclear_xenon_renderer_emit.json'
$outDir=Join-Path $base 'tonight_hardware_gate'
$outJson=Join-Path $outDir 'aztec_tonight_hardware_gate.json'
$outTxt=Join-Path $outDir 'aztec_tonight_hardware_gate.txt'

foreach($p in @($fullPath,$identityPath,$emitPath)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec first-hardware-test audit prerequisite missing: $p"
    }
}
$full=Get-Content -LiteralPath $fullPath -Raw | ConvertFrom-Json
$identity=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$emit=Get-Content -LiteralPath $emitPath -Raw | ConvertFrom-Json
if([string]$full.schema -ne 'WaW360.AztecPcTwoXModelTwoMaterialMixedGraph.v1' -or
   [string]$full.status -ne 'AZTEC_PC_TWO_XMODEL_TWO_MATERIAL_FULLGRAPH_PASS' -or
   [int]$full.nativeRoots -ne 8509 -or
   [int]$full.realXModelRoots -ne 2 -or
   [int]$full.realImageRoots -ne 2 -or
   [int]$full.realRawFileRoots -ne 114 -or
   [int]$full.independentModelNamesListed -ne 2 -or
   [int]$full.independentMaterialNamesListed -ne 2 -or
   [int]$full.independentPcPixelShaExactReadbacks -ne 2 -or
   [int]$full.independentPcRawFileShaExactReadbacks -ne 114 -or
   -not [bool]$full.layoutReplayExact -or
   -not [bool]$full.oatZeroWarningsZeroErrors -or
   -not [bool]$identity.TypeSequenceVerified -or
   @($identity.Entries).Count -ne 8509 -or
   [int]$emit.NativeRoots -ne 8509 -or
   [int]$emit.RealXModelRoots -ne 2 -or
   [int]$emit.RealMaterialRoots -ne 2 -or
   [int]$emit.RealImageRoots -ne 2 -or
   [int]$emit.RealRawFileRoots -ne 114){
    throw 'Original-PC 2-model 2-material fullgraph or exact native 8509-root provenance changed. No hardware status may be inferred.'
}
$ff=[string]$full.diagnosticScratchFF
if([string]$emit.OutputFastFile -ine $ff -or
   -not (Test-Path -LiteralPath $ff -PathType Leaf)){
    throw 'Current mixed FF is not the passing emitter scratch FF.'
}
$approvedScratch=([IO.Path]::GetFullPath((Join-Path $base 'pc_two_xmodel_two_material_fullgraph_integration\scratch_emit'))).TrimEnd([char]'\')+'\'
if(-not ([IO.Path]::GetFullPath($ff)).StartsWith($approvedScratch,[StringComparison]::OrdinalIgnoreCase)){
    throw 'Current FF is outside isolated scratch folder; never resolve or deploy ambiguous FF paths.'
}
function Family([string]$v){return (($v.ToLowerInvariant()) -replace '[^a-z0-9]','')}
$entries=@($identity.Entries)
$families=New-Object 'System.Collections.Generic.List[object]'
$targetFamilies=@('xmodel','material','image','stringtable','gfxworld','clipmap','comworld','gameworldsp','gameworldmp','mapents')
foreach($family in $targetFamilies){
    $rows=@($entries | Where-Object {(Family ([string]$_.TypeName)) -eq $family})
    $owned=@($rows | Where-Object {-not ([string]$_.Name).StartsWith(',')})
    $real=0
    switch($family){
        'xmodel' {$real=[int]$emit.RealXModelRoots}
        'material' {$real=[int]$emit.RealMaterialRoots}
        'image' {$real=[int]$emit.RealImageRoots}
        default {$real=0}
    }
    $families.Add([pscustomobject]@{
        family=$family
        rootCount=$rows.Count
        ownedRootCount=$owned.Count
        serializedOriginalPcBodies=$real
        # Remaining owned roots use the emitter's BuildShell fallback unless
        # another explicit serializer branch is proven for that family.
        ownedRootsWithoutPromotedPcBodies=[math]::Max(0,$owned.Count-$real)
        exampleOwned=@($owned | Select-Object -First 4 | ForEach-Object {[string]$_.Name})
    })
}

$readbackStatus='NOT_RUN'
$readbackCount=0
if(Test-Path -LiteralPath $readbackPath -PathType Leaf){
    $readback=Get-Content -LiteralPath $readbackPath -Raw | ConvertFrom-Json
    $readbackStatus=[string]$readback.status
    if($readback.PSObject.Properties.Name -contains 'originalPcMaterialBodyReadbacks'){
        $readbackCount=[int]$readback.originalPcMaterialBodyReadbacks
    }
}
$blockers=New-Object 'System.Collections.Generic.List[string]'
if($readbackStatus -ne 'AZTEC_PC_TWO_MATERIAL_FULLGRAPH_READBACK_PASS' -or $readbackCount -ne 2){
    $blockers.Add("Integrated material body readback not proven: status=$readbackStatus verified=$readbackCount/2.")
}
if(-not [bool]$emit.ValidationPass -or
   -not [bool]$emit.OatZeroWarningsZeroErrors -or
   -not [bool]$emit.LayoutReplayExact -or
   -not [bool]$emit.ExactIdentitySequence){
    $blockers.Add('Combined FF does not pass the complete structural OAT, replay, and root-identity gate.')
}
if([bool]$emit.ValidationOnly -or -not [bool]$emit.ReadyForHardware){
    $blockers.Add('The actual Xenon emitter marks this fullgraph ValidationOnly=True and ReadyForHardware=False.')
}
$stringCount=@($families.ToArray() | Where-Object {$_.family -eq 'stringtable'} | Select-Object -ExpandProperty ownedRootCount)
if($stringCount.Count -eq 1 -and [int]$stringCount[0] -gt 0){
    $blockers.Add("StringTable roots=$($stringCount[0]) are emitted as empty shells (no PC-owned row/column payload).")
}
$critical=@('gfxworld','clipmap','comworld','gameworldsp','gameworldmp','mapents')
foreach($row in @($families.ToArray() | Where-Object {$_.family -in $critical -and $_.ownedRootCount -gt 0})){
    $blockers.Add("Runtime-critical $($row.family) roots=$($row.ownedRootCount) use placeholder shells, not source-owned game/world bodies.")
}
foreach($family in @('xmodel','material','image')){
    $row=@($families.ToArray() | Where-Object {$_.family -eq $family})[0]
    if($row.ownedRootsWithoutPromotedPcBodies -gt 0){
        $blockers.Add("${family}: $($row.ownedRootsWithoutPromotedPcBodies) owned native root(s) have no promoted original-PC payload; determine whether each is required before a loading test.")
    }
}
$canReview=($blockers.Count -eq 0 -and [bool]$emit.ReadyForHardware)
$status=if($canReview){'AZTEC_SMOKE_CANDIDATE_READY_FOR_MANUAL_REVIEW'}else{'AZTEC_TONIGHT_HARDWARE_NO_GO_CURRENT_SCRATCH'}
New-Item -Path $outDir -ItemType Directory -Force | Out-Null
$report=[ordered]@{
    schema='WaW360.AztecTonightHardwareGate.v1'
    status=$status
    mode='READ_ONLY_AUDIT'
    structuralOatPass=[bool]$emit.ValidationPass
    independentMaterialReadback=$readbackStatus
    nativeRoots=8509
    sourceBackedXModels=[int]$emit.RealXModelRoots
    sourceBackedMaterials=[int]$emit.RealMaterialRoots
    sourceBackedImages=[int]$emit.RealImageRoots
    sourceBackedRawFiles=[int]$emit.RealRawFileRoots
    rendererTechniqueSets=[int]$emit.RendererTechniqueSets
    rendererPixelShaders=[int]$emit.RendererPixelShaders
    nativeRootFamilyCounts=$families.ToArray()
    blockers=$blockers.ToArray()
    canReviewForControlledHardwareLoad=$canReview
    safeToCopyToXbox=$false
    sourceFastfile=$ff
    note='No new FF; no deployment approved. Emission remains validation-only and incomplete critical content must be proven before any real-world loader-test candidate.'
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $outJson -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add("Aztec first Xbox 360 controlled-load GO/NO-GO: $status")
$lines.Add("OAT structure=$([bool]$emit.ValidationPass); integrated Material readback=$readbackStatus")
$lines.Add("Native roots=8509; real PC XModels=$([int]$emit.RealXModelRoots); Materials=$([int]$emit.RealMaterialRoots); Images=$([int]$emit.RealImageRoots); RawFiles=$([int]$emit.RealRawFileRoots)")
foreach($row in $families.ToArray()){
    $lines.Add("asset family $($row.family): owned=$($row.ownedRootCount), PC bodies promoted=$($row.serializedOriginalPcBodies)")
}
foreach($problem in $blockers.ToArray()){$lines.Add("BLOCKER: $problem")}
$lines.Add('Current FF is structural-validation only. No deploy/copy operations performed.')
[IO.File]::WriteAllLines($outTxt,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-TONIGHT] =========================================='
Write-Host "[AZTEC-TONIGHT] status=$status"
Write-Host "[AZTEC-TONIGHT] Structural OAT PASS=$([bool]$emit.ValidationPass) | Material readback=$readbackStatus"
Write-Host "[AZTEC-TONIGHT] Blockers=$($blockers.Count)"
foreach($problem in $blockers.ToArray()){Write-Host "[AZTEC-TONIGHT] BLOCKER: $problem"}
Write-Host "[AZTEC-TONIGHT] report=$outTxt"
Write-Host '[AZTEC-TONIGHT] READ-ONLY. NO XBOX COPY. CURRENT FF NOT APPROVED.'
