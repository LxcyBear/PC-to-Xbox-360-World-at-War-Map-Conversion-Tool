[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# No OAT, no .NET/map FF build. Strict original-PC GfxWorld image-root
# census matched against Xbox retail 8,509 root NAMES/TYPE identities only.
# NEVER infer image payload equivalence from matching names, and NEVER
# copy original Xbox reference FF payload bytes into a converter output.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$sourceDir=Join-Path $base 'pc_g11_cells_740\gate11_remaining_unified_source'
$sourcePath=Join-Path $sourceDir 'aztec_gate11_remaining_world_batch_source_gate.json'
$identityPath=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$nativeLeafPath=Join-Path $sourceDir 'native_leaf_batch\aztec_gate11_native_leaf_batch_conversion.json'
$out=Join-Path $sourceDir 'aztec_gate11_exact_asset_reference_audit.json'
$expectedFFSha='05CEEC82AF5D58706BA5A6EE24995D09A3FB764F3E547A8B4758A9347ECF2862'
foreach($path in @($sourcePath,$identityPath,$nativeLeafPath)){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Gate11 asset reference audit needs the existing PC source/native roots/leaf batch: $path"
    }
}
$s=Get-Content -LiteralPath $sourcePath -Raw|ConvertFrom-Json
$n=Get-Content -LiteralPath $identityPath -Raw|ConvertFrom-Json
$leaf=Get-Content -LiteralPath $nativeLeafPath -Raw|ConvertFrom-Json
if([string]$s.status -ne 'AZTEC_GATE11_ALL_REMAINING_WORLD_SOURCE_CORPUS_CAPTURED_FF_BATCH_NOT_YET_APPROVED' -or
   [string]$leaf.status -ne 'AZTEC_GATE11_13_SOURCE_LEAF_XENON_ENDIAN_CONVERSION_PASS_FULL_WORLD_UNPROVEN' -or
   [string]$s.protectedPreviousGate11FfSha256 -ine $expectedFFSha -or
   [string]$leaf.protectedIndependentGate11FfSha256 -ine $expectedFFSha -or
   [int]$n.Assets -ne 8509 -or
   -not [bool]$n.TypeSequenceVerified -or
   @($n.Entries).Count -ne 8509 -or
   [bool]$leaf.newNativeFfEmitted -or [bool]$leaf.xboxHardwareApproved){
    throw 'Gate11 source/leaf/native root identity lineage not independently established.'
}
if((Get-FileHash -LiteralPath ([string]$s.protectedPreviousGate11Ff) -Algorithm SHA256).Hash -ine
    $expectedFFSha -or
   (Get-FileHash -LiteralPath ([string]$s.sourceTypedRemainderGraph) -Algorithm SHA256).Hash -ine
    [string]$s.sourceTypedRemainderGraphSha256 -or
   [int]$leaf.families -ne 13 -or
   [long]$leaf.nativeCandidateByteCount -ne 6930524L){
    throw 'Protected previous Gate11 FF or original source endian batch changed.'
}
$sourceWorld=Get-Content -LiteralPath ([string]$s.sourceTypedRemainderGraph) -Raw|ConvertFrom-Json
$counts=$sourceWorld.counts
if([string]$sourceWorld.schema -ne 'WaW360.T4PcGate11WorldRemainderCensus.v1' -or
   -not [bool]$sourceWorld.sourceOnlyComplete -or
   [int]$counts.reflectionProbes -ne @($sourceWorld.reflectionProbeRefs).Count -or
   [int]$counts.lightmaps -ne @($sourceWorld.lightmapRefs).Count){
    throw 'The existing original-PC world image reference typed source changed.'
}
$images=@($n.Entries|Where-Object { [string]$_.TypeName -ieq 'image' })
$materials=@($n.Entries|Where-Object {[string]$_.TypeName -ieq 'material'})
if($images.Count -ne 666 -or $materials.Count -eq 0){
    throw "Expected 666 proven native Xbox image root identities; got $($images.Count)."
}
$byImage=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
foreach($e in $images){
    $key=[string]$e.Name
    if([string]::IsNullOrWhiteSpace($key)){throw 'Native image identity without an exact name.'}
    if(-not $byImage.ContainsKey($key)){
        $byImage.Add($key,(New-Object 'System.Collections.Generic.List[int]'))
    }
    $byImage[$key].Add([int]$e.Index)
}
$rows=New-Object 'System.Collections.Generic.List[object]'
function Add-Image([string]$role,[object]$value){
    $name=if($null -eq $value){''}else{[string]$value}
    $candidate=@()
    if($name.Length -gt 0 -and $byImage.ContainsKey($name)){
        $candidate=@($byImage[$name].ToArray())
    }
    $resolution=if($name.Length -eq 0){'SOURCE_NULL'}
        elseif($candidate.Count -eq 1){'UNIQUE_NATIVE_IMAGE_ROOT_IDENTITY_CANDIDATE_ONLY'}
        elseif($candidate.Count -gt 1){'AMBIGUOUS_NATIVE_IMAGE_NAME'}
        else{'NO_NATIVE_IMAGE_ROOT_EXACT_NAME'}
    $rows.Add([ordered]@{
        sourceField=$role
        originalPcImageName=$name
        resolution=$resolution
        nativeImageRootIndexes=$candidate
        nativeCandidateCount=$candidate.Count
        xboxImagePayloadDonorCopied=$false
        payloadSemanticsVerified=$false
        xboxHardwareApproved=$false
    })
}
$refs=$sourceWorld.sourceImageAndMaterialReferences
Add-Image 'GfxWorld.skyImage' $refs.skyImage
Add-Image 'GfxWorld.outdoorImage' $refs.outdoorImage
for($i=0;$i -lt [int]$counts.reflectionProbes;$i++){
    if([int]$sourceWorld.reflectionProbeRefs[$i].index -ne $i){
        throw "Original PC reflection probe ordinal $i changed."
    }
    Add-Image "GfxWorld.reflectionProbes[$i].reflectionImage" $sourceWorld.reflectionProbeRefs[$i].image
}
for($i=0;$i -lt [int]$counts.lightmaps;$i++){
    if([int]$sourceWorld.lightmapRefs[$i].index -ne $i){
        throw "Original PC lightmap ordinal $i changed."
    }
    Add-Image "GfxWorld.lightmaps[$i].primary" $sourceWorld.lightmapRefs[$i].primary
    Add-Image "GfxWorld.lightmaps[$i].secondary" $sourceWorld.lightmapRefs[$i].secondary
}
$array=@($rows.ToArray())
$nulls=@($array|Where-Object {$_.resolution -eq 'SOURCE_NULL'})
$unique=@($array|Where-Object {$_.resolution -eq 'UNIQUE_NATIVE_IMAGE_ROOT_IDENTITY_CANDIDATE_ONLY'})
$missing=@($array|Where-Object {$_.resolution -eq 'NO_NATIVE_IMAGE_ROOT_EXACT_NAME'})
$ambiguous=@($array|Where-Object {$_.resolution -eq 'AMBIGUOUS_NATIVE_IMAGE_NAME'})
$materialsNeeded=[ordered]@{
    sunflareSpriteMaterialNameNotYetExported=[bool]$refs.sunflareSpriteMaterialPresent
    sunflareFlareMaterialNameNotYetExported=[bool]$refs.sunflareFlareMaterialPresent
    waterMaterialNameNotYetExported=[bool]$refs.waterMaterialPresent
}
$result=[ordered]@{
    schema='WaW360.AztecGate11OriginalPcWorldAssetExactNameBindingAudit.v1'
    status='AZTEC_GATE11_IMAGE_ROOT_IDENTITIES_CENSUSED_ONLY_NOT_NATIVE_PAYLOAD_PROOF'
    sourceOnlyTypedGraph=[string]$s.sourceTypedRemainderGraph
    sourceOnlyTypedGraphSha256=[string]$s.sourceTypedRemainderGraphSha256
    passingNativeWorldFfSha256=$expectedFFSha
    nativeImageRootCount=$images.Count
    nativeMaterialRootCount=$materials.Count
    imageReferenceFields=$array.Count
    nullImageReferenceFields=$nulls.Count
    uniqueExactImageRootNameCandidates=$unique.Count
    absentExactImageRootNames=$missing.Count
    ambiguousExactImageRootNames=$ambiguous.Count
    imageReferenceRows=$array
    sourceMaterialNameDependencies=$materialsNeeded
    allImageReferenceNamesUniqueOrNull=($missing.Count -eq 0 -and $ambiguous.Count -eq 0)
    rootIdentityIsNotPayloadEquivalence=$true
    pixelDonorCopied=$false
    xboxStreamInfoReconstructed=$false
    rootReferencesRebasedInNewNativeFf=$false
    newNativeWorldFfEmitted=$false
    gate11OverallPassed=$false
    hardwareReady=$false
}
$result|ConvertTo-Json -Depth 9|Set-Content -LiteralPath $out -Encoding UTF8
Write-Host '[AZTEC-GATE11-BIND] ================================================================'
Write-Host "[AZTEC-GATE11-BIND] Original-PC world image pointer fields=$($array.Count): NULL=$($nulls.Count), exact native NAME candidates=$($unique.Count), absent=$($missing.Count), ambiguous=$($ambiguous.Count)"
foreach($row in $array){
    $ids=@($row.nativeImageRootIndexes) -join ','
    Write-Host "[AZTEC-GATE11-BIND] $($row.sourceField): $($row.originalPcImageName) => $($row.resolution) indexes=$ids"
}
Write-Host "[AZTEC-GATE11-BIND] Additional source material names still need proof: sprite=$([bool]$refs.sunflareSpriteMaterialPresent) flare=$([bool]$refs.sunflareFlareMaterialPresent) water=$([bool]$refs.waterMaterialPresent)"
Write-Host "[AZTEC-GATE11-BIND] Report=$out"
Write-Host '[AZTEC-GATE11-BIND] Identity candidates are NOT proven image payload equivalents. NO FF/OAT build and no Xbox approval.'
