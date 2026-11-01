[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Authentically decode original PC FFs identified by successful OAT listings.
# Convert their actual owned WeaponDef bodies with the established Xenon
# 6f/7b serializers, only as isolated diagnostic proof blobs.
# NO Xbox retail donor bytes, NO OAT, NO new FastFile or Xbox deployment.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$work=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$prior=Join-Path $work 'pc_g11_lightning_six_weapons_43_override_sources\aztec_six_weapon_source_candidates_and_43_override_roles.json'
$native=Join-Path $work 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$size=Join-Path $work 'pc_g11_native_ff_size_audit\aztec_native_ff_size_and_source_ledger.json'
$out=Join-Path $work 'pc_g11_lightning_six_weapon_real_source_recovery'
$json=Join-Path $out 'aztec_six_real_pc_weapon_source_body_proof.json'
$md=Join-Path $out 'aztec_six_real_pc_weapon_source_body_proof.md'
$log=Join-Path $out 'aztec_six_real_pc_weapon_source_body_console.log'
$project=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
foreach($f in @($prior,$native,$size,$project)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){throw "Missing authenticated source/old Xbox map FF proof: $f"}
}
$p=Get-Content -LiteralPath $prior -Raw|ConvertFrom-Json
$s=Get-Content -LiteralPath $size -Raw|ConvertFrom-Json
if([string]$p.schema -ne 'WaW360.AztecSixWeapon43OverrideSourceTriage.v1' -or
   [string]$p.status -ne 'AZTEC_SIX_WEAPONS_SOURCE_SEARCH_AND_43_PC_BODY_CONFLICT_ROLES_TRIAGED_NO_SELECTION' -or
   [int]$p.sixWeaponSourceHintsFound -ne 6 -or
   [int]$p.totalConflictingSourceBodies -ne 43 -or
   [int]$p.sourceOwnedSixWeaponXboxSerializationProven -ne 0 -or
   [bool]$p.xboxHardwareReady -or
   [string]$s.schema -ne 'WaW360.AztecNativeFfSizeAndSourceCoverage.v1' -or
   [string]$s.converted.sha256 -ine 'F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6'){
    throw 'Original six PC WeaponDef source candidates or protected Aztec size/hash evidence changed.'
}
$ff=[string]$s.converted.path
if(-not(Test-Path -LiteralPath $ff -PathType Leaf) -or
   (Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash -ine [string]$s.converted.sha256){
    throw "Original OAT-passing Aztec FF changed. Refuse stale source-body conversion."
}
Write-Host '[AZTEC-SIX-RECOVERY] ======================================================='
Write-Host '[AZTEC-SIX-RECOVERY] Six target WeaponDefs: REAL PC source-owned body -> Xbox 6f/7b'
Write-Host '[AZTEC-SIX-RECOVERY] Cached OAT owned names only; no reference stubs or IWD filename substitutions.'
Write-Host '[AZTEC-SIX-RECOVERY] Other PC map sources are FORENSIC only, no production donors.'
Write-Host '[AZTEC-SIX-RECOVERY] NO stock retail bytes, NO native FastFile build/OAT, NO Xbox.'
Write-Host '[AZTEC-SIX-PROGRESS] [####----------------] 20% | six candidate names and protected FF SHA locked'
New-Item -ItemType Directory -Force -Path $out|Out-Null
$arguments=@('run','--no-launch-profile','-c','Release','--project',$project,'--',
    'aztec-six-weapon-real-source-recovery',$prior,$native,$out)
Write-Host '[AZTEC-SIX-PROGRESS] [########------------] 40% | per PC source: fixed WeaponDef + full owned Xbox child serializer'
$old=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & dotnet @arguments 2>&1|ForEach-Object {[string]$_}|Tee-Object -FilePath $log
    $exit=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$old}
if($exit -ne 0 -or -not(Test-Path -LiteralPath $json -PathType Leaf) -or
   -not(Test-Path -LiteralPath $md -PathType Leaf)){
    if(Test-Path -LiteralPath $log -PathType Leaf){
        Get-Content -LiteralPath $log -Tail 40|
            ForEach-Object {Write-Host "[AZTEC-SIX-RECOVERY] $_"}
    }
    throw "Original-PC six-weapon source-body conversion failed (exit=$exit). Map FF not touched."
}
$r=Get-Content -LiteralPath $json -Raw|ConvertFrom-Json
$states=@(
    'AZTEC_SIX_REAL_NATIVE_WEAPON_SOURCE_BODIES_UNAMBIGUOUS_NOT_GRAFTED',
    'AZTEC_SIX_PC_WEAPON_SOURCE_PROOF_PARTIAL_OR_CONFLICTING'
)
if([string]$r.schema -ne 'WaW360.AztecSixWeaponRealPcSourceOwnedXboxProof.v1' -or
   [string]$r.status -notin $states -or
   [int]$r.originalSixNativeWeaponRootNames -ne 6 -or
   @($r.actualXboxWeaponDefSourceBodyProof).Count -ne 6 -or
   [int]$r.sixWithProvenIdenticalOrSingleConvertedXboxBody -gt 6 -or
   [int]$r.sixWithProvenIdenticalOrSingleConvertedXboxBody -lt 0 -or
   -not [bool]$r.noXboxRetailDonorBodiesCopied -or
   -not [bool]$r.noAztecFastFileBuiltOrModified -or
   -not [bool]$r.existing43SourceBodyConflictsNotResolved -or
   [bool]$r.hardwareReady -or [bool]$r.gate11Closed){
    throw 'Six-weapon proof claims false source or native Xbox runtime readiness.'
}
Write-Host '[AZTEC-SIX-PROGRESS] [################----] 80% | compare real named Xbox WeaponDef blobs and conflicts'
Write-Host "[AZTEC-SIX-RECOVERY] Six weapons with one unambiguous genuine source Xbox proof=$($r.sixWithProvenIdenticalOrSingleConvertedXboxBody)/6"
Write-Host "[AZTEC-SIX-RECOVERY] Different genuine Xbox source bodies=$($r.sixWithConflictingDifferentConvertedWeaponBodies)"
foreach($weapon in @($r.actualXboxWeaponDefSourceBodyProof)){
    Write-Host "[AZTEC-SIX-RECOVERY] $($weapon.nativeWeaponName): typed-owned-Xbox-bodies=$($weapon.exactTypedSourceNativeXboxWeaponDefBodies), different-hashes=$($weapon.differentConvertedXboxOwnedWeaponDefBodySha256), $($weapon.decision)"
}
foreach($issue in @($r.sourceProofFailures|Select-Object -First 20)){
    Write-Host "[AZTEC-SIX-RECOVERY] BLOCKER: $issue"
}
Write-Host '[AZTEC-SIX-PROGRESS] [####################] 100% | proof containers + per-root source details'
Write-Host "[AZTEC-SIX-RECOVERY] STATUS=$($r.status)"
Write-Host "[AZTEC-SIX-RECOVERY] JSON: $json"
Write-Host "[AZTEC-SIX-RECOVERY] Markdown: $md"
Write-Host "[AZTEC-SIX-RECOVERY] Console log: $log"
Write-Host '[AZTEC-SIX-RECOVERY] NO map FF graft. Original-PC other-map source proof requires legitimate Aztec source ownership before production use.'
