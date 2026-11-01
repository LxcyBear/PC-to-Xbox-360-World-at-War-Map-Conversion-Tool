[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# SHA-verified original-PC converted Xbox XModel/FX/Weapon payload
# selection by all 586 precise native root names. Read-only diagnostic.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$five=Join-Path $base 'pc_g11_lightning_five_class_source_proof\aztec_five_class_lightning_source_proof.json'
$identity=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$xanim=Join-Path $base 'pc_g11_lightning_xanim_602_root_selection\aztec_602_xanim_source_body_selection.json'
$size=Join-Path $base 'pc_g11_native_ff_size_audit\aztec_native_ff_size_and_source_ledger.json'
$out=Join-Path $base 'pc_g11_lightning_three_class_native_root_resolution'
$json=Join-Path $out 'aztec_three_class_native_root_body_resolution.json'
$md=Join-Path $out 'aztec_three_class_native_root_body_resolution.md'
$log=Join-Path $out 'aztec_three_class_native_root_body_resolution_console.log'
$project=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
foreach($f in @($five,$identity,$xanim,$size,$project)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){
        throw "Aztec source-native proof baseline missing: $f"
    }
}
$f=Get-Content -LiteralPath $five -Raw|ConvertFrom-Json
$x=Get-Content -LiteralPath $xanim -Raw|ConvertFrom-Json
$s=Get-Content -LiteralPath $size -Raw|ConvertFrom-Json
if([string]$f.schema -ne 'WaW360.AztecFiveClassLightningSourceProof.v1' -or
   [string]$f.status -ne 'AZTEC_FIVE_CLASS_SOURCE_DIAGNOSTIC_STAGES_PASS_CONTENT_CLOSURE_OPEN' -or
   [int]$f.nativeXAssetRootCount -ne 8509 -or
   @($f.blockedProofs).Count -ne 0 -or
   -not [bool]$f.sourceShaVerified -or
   [int]$f.carriedForwardXAnimConflicts -ne 12 -or
   [string]$x.schema -ne 'WaW360.AztecXAnim602SourceBodyResolution.v1' -or
   [int]$x.nativeRootsWithSingleSource -ne 590 -or
   [int]$x.nativeRootsWithConflictingDuplicateBodies -ne 12 -or
   [bool]$x.hardwareReady -or
   [string]$s.schema -ne 'WaW360.AztecNativeFfSizeAndSourceCoverage.v1' -or
   [string]$s.converted.sha256 -ine 'F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6' -or
   [int]$s.rootCoverage.total -ne 8509){
    throw 'Source owner, native FF baseline or XAnim conflict proofs changed.'
}
$ff=[string]$s.converted.path
if(-not(Test-Path -LiteralPath $ff -PathType Leaf) -or
   (Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash -ine [string]$s.converted.sha256){
    throw "Protected 4,510,695-byte OAT-passing Xbox FF SHA changed: $ff"
}
Write-Host '[AZTEC-3ROOT] ==========================================================='
Write-Host '[AZTEC-3ROOT] Model + FX + Weapon original-PC Xbox native body hash selection'
Write-Host '[AZTEC-3ROOT] 284 + 233 + 69 = 586 native root identities; exact real converted body hashes'
Write-Host '[AZTEC-3ROOT] Material + Localize + 12 XAnim blockers retained. NO FF/OAT/XBOX.'
Write-Host '[AZTEC-3ROOT-PROGRESS] [####----------------] 20% | verified 4.51 MB FF and original-PC source proof'
New-Item -ItemType Directory -Force -Path $out|Out-Null
$argsList=@('run','--no-launch-profile','-c','Release','--project',$project,'--',
    'aztec-three-class-native-root-selection',$five,$identity,$xanim,$out)
Write-Host '[AZTEC-3ROOT-PROGRESS] [########------------] 40% | hash independently decoded Xbox source model/fx/weapon bodies'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet @argsList 2>&1 | ForEach-Object {[string]$_} | Tee-Object -FilePath $log
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($exit -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf) -or
   -not(Test-Path -LiteralPath $md -PathType Leaf)){
    if(Test-Path -LiteralPath $log -PathType Leaf){
        Get-Content -LiteralPath $log -Tail 35 |
            ForEach-Object {Write-Host "[AZTEC-3ROOT] $_"}
    }
    throw "XModel/FX/Weapon exact native root body selection failed (exit=$exit). No FF changed."
}
$r=Get-Content -LiteralPath $json -Raw|ConvertFrom-Json
if([string]$r.schema -ne 'WaW360.AztecThreeOwnedClassNativeRootSelection.v1' -or
   [string]$r.status -ne 'AZTEC_MODELS_FX_WEAPONS_EXACT_BODY_HASH_SELECTION_AND_GAPS_PASS_NOT_GRAFTED' -or
   [int]$r.nativeAllTypeRootCount -ne 8509 -or
   [int]$r.nativeThreeClassRoots -ne 586 -or
   [int]$r.sourceAuthenticXboxBlobRecords -ne 623 -or
   @($r.perTypeSummary).Count -ne 3 -or
   @($r.exactNativeRootSelection).Count -ne 586 -or
   [int]$r.originalPCWorldMaterialGraphsCodecLoaded -ne 39 -or
   [int]$r.localizedSourceStringsRecovered -ne 177 -or
   [int]$r.carriedForwardXAnimDuplicateConflicts -ne 12 -or
   -not [bool]$r.originalPcSourceProofContainersShaValidated -or
   [bool]$r.builtAnyFastFile -or
   [bool]$r.hardwareReady -or
   [bool]$r.gate11Passed){
    throw 'New exact root selection output claims unproven runtime readiness or lost source evidence.'
}
Write-Host '[AZTEC-3ROOT-PROGRESS] [################----] 80% | list native-root missing bodies and duplicate SHA conflicts'
foreach($c in @($r.perTypeSummary)){
    Write-Host ("[AZTEC-3ROOT] {0}: native={1}, single={2}, byte-identical duplicates={3}, conflicts={4}, missing exact source={5}, chosen Xbox source bytes={6}" -f
        $c.category,$c.nativeRoots,$c.uniqueNativeRootBodies,
        $c.identicalDuplicateNativeRootBodies,$c.conflictingDuplicateNativeRootBodies,
        $c.missingExactNameTypedSourceBodies,$c.selectedUncompressedXboxBlobBytes)
}
foreach($delta in @($r.missingOrConflictingNativeRoots)){
    Write-Host "[AZTEC-3ROOT] NEEDS SOURCE REVIEW: $($delta.category) #$($delta.nativeRootOrdinal) $($delta.nativeRootName) : $($delta.decision)"
}
Write-Host '[AZTEC-3ROOT-PROGRESS] [####################] 100% | verified root-identity and source-hash worklist'
Write-Host "[AZTEC-3ROOT] STATUS=$($r.status)"
Write-Host "[AZTEC-3ROOT] Markdown: $md"
Write-Host "[AZTEC-3ROOT] JSON: $json"
Write-Host "[AZTEC-3ROOT] Log: $log"
Write-Host '[AZTEC-3ROOT] No new native FastFile. No retail donor payload. No runtime approval. XAnim 12 conflicts stay fenced.'
