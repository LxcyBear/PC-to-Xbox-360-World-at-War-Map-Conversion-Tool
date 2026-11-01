[CmdletBinding()]
param([switch]$EvidenceOnly)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# One-command FIVE-class original-PC typed/real Xbox proof batch:
# Materials / XModels / Effects / Localization / Weapons.
# Reuses original PC FF SHA and previous 602-XAnim selection.
# Does NOT rebuild Xbox FastFile, OAT, or run on hardware.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$work=Join-Path $base 'native_material_mapping'
$baseline=Join-Path $base 'second_map_baseline.json'
$inventory=Join-Path $work 'pc_fastfile_image_sources\pc_fastfile_image_sources.json'
$identity=Join-Path $work 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$xanim614=Join-Path $work 'pc_g11_lightning_xanim_614_native_proof\aztec_614_pc_xanim_native_conversion_and_name_diff.json'
$xanim602=Join-Path $work 'pc_g11_lightning_xanim_602_root_selection\aztec_602_xanim_source_body_selection.json'
$material=Join-Path $work 'pc_to_xenon_39_world_material_graph_candidates\aztec_39_pc_to_xenon_material_graph_candidates.json'
$project=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$out=Join-Path $work 'pc_g11_lightning_five_class_source_proof'
$json=Join-Path $out 'aztec_five_class_lightning_source_proof.json'
$md=Join-Path $out 'aztec_five_class_lightning_source_proof.md'
$log=Join-Path $out 'aztec_five_class_lightning_console.log'
foreach($f in @($baseline,$inventory,$identity,$xanim614,$xanim602,$material,$project)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){
        throw "Five-class lightning missing proven original-PC or native evidence: $f"
    }
}
$a=Get-Content -LiteralPath $xanim602 -Raw|ConvertFrom-Json
$b=Get-Content -LiteralPath $xanim614 -Raw|ConvertFrom-Json
$m=Get-Content -LiteralPath $material -Raw|ConvertFrom-Json
if([string]$a.schema -ne 'WaW360.AztecXAnim602SourceBodyResolution.v1' -or
   [string]$a.status -ne 'AZTEC_602_XANIM_NATIVE_BODY_DUPLICATE_CONFLICTS_QUARANTINED' -or
   [int]$a.nativeXboxXAnimRoots -ne 602 -or
   [int]$a.nativeRootsWithSingleSource -ne 590 -or
   [int]$a.nativeRootsWithConflictingDuplicateBodies -ne 12 -or
   [long]$a.sourceAuthenticXenonTotalBodyBytes -ne 8494093L -or
   [bool]$a.hardwareReady -or
   [string]$b.schema -ne 'WaW360.AztecOriginalPcXAnimNativeTransform614.v1' -or
   [int]$b.convertedXboxXAnimBodiesReadbackVerified -ne 614 -or
   [string]$m.Schema -ne 'WaW360.AztecPcXenonMaterial39SourceBridge.v1' -or
   [int]$m.SourceMaterialHeaders -ne 39 -or
   [int]$m.SourceMaterialChildrenConverted -ne 39 -or
   [bool]$m.XboxHardwareReady){
    throw 'Prior XAnim 590/12 source selection or 39 material source-owned graph evidence changed.'
}
Write-Host '[AZTEC-5LIGHTNING] =============================================================='
Write-Host '[AZTEC-5LIGHTNING] MATERIALS + XMODELS + EFFECTS + LOCALIZATION + WEAPONS'
Write-Host '[AZTEC-5LIGHTNING] 3 SHA-locked original PC FFs; real typed source->Xbox proofs where supported.'
Write-Host "[AZTEC-5LIGHTNING] Evidence-only (no native serializer proofs)=$([bool]$EvidenceOnly)"
Write-Host '[AZTEC-5LIGHTNING] No FF build, no new OAT, no donor Xbox assets, no hardware.'
Write-Host '[AZTEC-5LIGHTNING-PROGRESS] [####----------------] 20% | pin source hashes and native/XAnim identity'
New-Item -ItemType Directory -Force -Path $out|Out-Null
$arguments=@('run','--no-launch-profile','--configuration','Release',
    '--project',$project,'--','aztec-five-class-lightning',
    $baseline,$inventory,$identity,$xanim614,$xanim602,$material,$out)
if($EvidenceOnly){$arguments+='--evidence-only'}
Write-Host '[AZTEC-5LIGHTNING-PROGRESS] [########------------] 40% | source-owned proofs across five classes'
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet @arguments 2>&1 |
        ForEach-Object {[string]$_} | Tee-Object -FilePath $log
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$saved}
if($code -ne 0 -or
   -not(Test-Path -LiteralPath $json -PathType Leaf) -or
   -not(Test-Path -LiteralPath $md -PathType Leaf)){
    if(Test-Path -LiteralPath $log -PathType Leaf){
        Get-Content -LiteralPath $log -Tail 45 |
            ForEach-Object {Write-Host "[AZTEC-5LIGHTNING] $_"}
    }
    throw "Five-class source proof execution failed (exit=$code); protected Aztec FF unchanged."
}
$r=Get-Content -LiteralPath $json -Raw|ConvertFrom-Json
$statuses=@(
    'AZTEC_FIVE_CLASS_EVIDENCE_ONLY_NO_NEW_XBOX_BODY_PROOF',
    'AZTEC_FIVE_CLASS_SOURCE_DIAGNOSTIC_STAGES_PASS_CONTENT_CLOSURE_OPEN',
    'AZTEC_FIVE_CLASS_SOURCE_DIAGNOSTICS_PARTIAL_BLOCKERS_REPORTED'
)
if([string]$r.schema -ne 'WaW360.AztecFiveClassLightningSourceProof.v1' -or
   [string]$r.status -notin $statuses -or
   [int]$r.originalPcSourceFastfiles -ne 3 -or
   -not [bool]$r.sourceShaVerified -or
   [int]$r.nativeXAssetRootCount -ne 8509 -or
   @($r.categories).Count -ne 5 -or
   [int]$r.carriedForwardXAnimConflicts -ne 12 -or
   @($r.carriedForwardConflictingXAnimNames).Count -ne 12 -or
   -not [bool]$r.noAztecFastFileCreatedOrModified -or
   -not [bool]$r.noXboxRetailAssetBodiesCopied -or
   -not [bool]$r.noOatRerun -or
   [bool]$r.hardwareReady -or
   [bool]$r.gate11Closed){
    throw 'Five-class source proof integrity/runtimereadiness assertion failed.'
}
Write-Host '[AZTEC-5LIGHTNING-PROGRESS] [################----] 80% | compare real proof names against native roots'
foreach($c in @($r.categories)){
    Write-Host ("[AZTEC-5LIGHTNING] {0}: Xbox roots={1}, PC source headers={2}, exact native proof names={3}, no typed same-name body proof={4}, Xbox payload blobs={5}" -f
        $c.type,$c.nativeRootCount,$c.originalPcTopLevelAssetHeaders,
        $c.nativeRootNamesWithMatchingSourceBodyProof,
        $c.nativeRootsNotCoveredByExactTypedSourceNames,
        $c.standaloneXboxSourceBlobBodies)
    foreach($stage in @($c.proofRuns)){
        Write-Host "[AZTEC-5LIGHTNING]   $($stage.Stage) [$($stage.SourceRole)] $($stage.Covered)/$($stage.Declared): $($stage.Status)"
    }
}
foreach($failure in @($r.blockedProofs|Select-Object -First 15)){
    Write-Host "[AZTEC-5LIGHTNING] BLOCKED: $failure"
}
Write-Host '[AZTEC-5LIGHTNING-PROGRESS] [####################] 100% | category proof and actionable remaining blockers'
Write-Host "[AZTEC-5LIGHTNING] STATUS=$($r.status)"
Write-Host "[AZTEC-5LIGHTNING] Carried XAnim duplicate content conflicts=$($r.carriedForwardXAnimConflicts)"
Write-Host "[AZTEC-5LIGHTNING] Markdown: $md"
Write-Host "[AZTEC-5LIGHTNING] JSON: $json"
Write-Host "[AZTEC-5LIGHTNING] Log: $log"
Write-Host '[AZTEC-5LIGHTNING] IMPORTANT: source Xbox proof blobs are NOT in the protected native FF, gate 11 open. NO XBOX.'
