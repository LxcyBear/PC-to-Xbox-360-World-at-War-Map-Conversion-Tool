[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# SHA-256 compare all 614 authentic converted Xbox XAnim proof bodies
# against 602 native names. Select exact body ONLY if no duplicate conflict.
# No FF generation, OAT re-run, Xbox retail donor bytes, console deployment.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$last=Join-Path $base 'pc_g11_lightning_xanim_614_native_proof\aztec_614_pc_xanim_native_conversion_and_name_diff.json'
$native=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$inventory=Join-Path $base 'pc_fastfile_image_sources\pc_fastfile_image_sources.json'
$out=Join-Path $base 'pc_g11_lightning_xanim_602_root_selection'
$json=Join-Path $out 'aztec_602_xanim_source_body_selection.json'
$md=Join-Path $out 'aztec_602_xanim_source_body_selection.md'
$log=Join-Path $out 'aztec_602_xanim_source_body_selection_console.log'
$project=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
foreach($f in @($last,$native,$inventory,$project)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){
        throw "Missing previous native XAnim 614 proof/source identity: $f"
    }
}
$p=Get-Content -LiteralPath $last -Raw|ConvertFrom-Json
if([string]$p.schema -ne 'WaW360.AztecOriginalPcXAnimNativeTransform614.v1' -or
   [string]$p.status -ne 'AZTEC_614_PC_XANIM_TO_XENON_BLOBS_READBACK_PASS_ALL_602_NATIVE_NAMES_MATCH_NOT_IN_FF' -or
   [int]$p.convertedXboxXAnimBodiesReadbackVerified -ne 614 -or
   [int]$p.exactPcToXboxXAnimNameMatches -ne 602 -or
   [int]$p.originalPcDuplicateXAnimBodyOccurrences -ne 12 -or
   -not [bool]$p.allNativeXboxXAnimNameRootsCoveredByProof -or
   [bool]$p.integratedInto8509NativeXboxRootFastFile -or
   [bool]$p.hardwareReady){
    throw 'Prior XAnim native transform and 602-name mapping PASS proof is missing or changed.'
}
Write-Host '[AZTEC-XANIM-602] ============================================================'
Write-Host '[AZTEC-XANIM-602] 614 independently verified original-PC converted Xbox XAnim bodies'
Write-Host '[AZTEC-XANIM-602] Exact SHA comparisons -> 602 root selections; differing duplicates QUARANTINED'
Write-Host '[AZTEC-XANIM-602] NO map FF rebuild; no Xbox test.'
Write-Host '[AZTEC-XANIM-PROGRESS] [####----------------] 20% | source proof and original PC hash preflight'
New-Item -Path $out -ItemType Directory -Force|Out-Null
$argsList=@('run','--no-launch-profile','-c','Release','--project',$project,'--',
    'aztec-xanim-602-source-selection',$last,$native,$inventory,$out)
Write-Host '[AZTEC-XANIM-PROGRESS] [########------------] 40% | compare real 12-bucket Xbox blob hashes, validate every native root'
$pref=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet @argsList 2>&1|ForEach-Object {[string]$_}|Tee-Object -FilePath $log
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$pref}
if($code -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf) -or
   -not(Test-Path -LiteralPath $md -PathType Leaf)){
    if(Test-Path -LiteralPath $log -PathType Leaf){
        Get-Content -LiteralPath $log -Tail 45 | ForEach-Object {Write-Host "[AZTEC-XANIM-602] $_"}
    }
    throw "XAnim 602 root hash selection failed (exit=$code), native FF remains protected."
}
$r=Get-Content -LiteralPath $json -Raw|ConvertFrom-Json
$accepted=@('AZTEC_602_XANIM_NATIVE_BODY_SELECTION_FULLY_UNAMBIGUOUS_NOT_IN_FF',
            'AZTEC_602_XANIM_NATIVE_BODY_DUPLICATE_CONFLICTS_QUARANTINED')
if([string]$r.schema -ne 'WaW360.AztecXAnim602SourceBodyResolution.v1' -or
   [string]$r.status -notin $accepted -or
   [int]$r.sourceAuthenticXenonBodiesReadbackVerified -ne 614 -or
   [long]$r.sourceAuthenticXenonTotalBodyBytes -ne 8494093L -or
   [int]$r.nativeXboxXAnimRoots -ne 602 -or
   [int]$r.duplicatePcOccurrences -ne 12 -or
   @($r.nativeRootBodySelection).Count -ne 602 -or
   ([int]$r.nativeRootsWithSingleSource + [int]$r.nativeRootsWithIdenticalDuplicateBodies + [int]$r.nativeRootsWithConflictingDuplicateBodies) -ne 602 -or
   [bool]$r.nativeXAnimGraphGraftedInto8509RootFF -or
   [bool]$r.hardwareReady -or [bool]$r.runtimeAnimationPlaybackProven){
    throw 'Generated native Xbox root selection has invalid counts/bytes or claims false game readiness.'
}
Write-Host '[AZTEC-XANIM-PROGRESS] [################----] 80% | conflict classification'
Write-Host "[AZTEC-XANIM-602] 602 names: single=$($r.nativeRootsWithSingleSource), identical duplicates=$($r.nativeRootsWithIdenticalDuplicateBodies), conflicting duplicates=$($r.nativeRootsWithConflictingDuplicateBodies)"
Write-Host "[AZTEC-XANIM-602] Selected verified Xbox blob bytes=$($r.uniqueNativeRootBodiesSelectionBytes)"
foreach($name in @($r.namesOfConflictingDuplicateBodies|Select-Object -First 30)){
    Write-Host "[AZTEC-XANIM-602] DUPLICATE CONFLICT BLOCKED: $name"
}
Write-Host '[AZTEC-XANIM-PROGRESS] [####################] 100% | 602-root JSON + Markdown ready'
Write-Host "[AZTEC-XANIM-602] STATUS=$($r.status)"
Write-Host "[AZTEC-XANIM-602] Report: $md"
Write-Host "[AZTEC-XANIM-602] Source selection: $json"
Write-Host '[AZTEC-XANIM-602] Next native graft only after conflicting bodies resolved. NO FF / XBOX.'
