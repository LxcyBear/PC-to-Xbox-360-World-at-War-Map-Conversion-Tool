[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$sourcePath=Join-Path $base 'pc_fastfile_image_sources\pc_fastfile_image_sources.json'
$corePath=Join-Path $base 'pc_fastfile_image_sources\pc_external_image_xbox_stock_closure.json'
$allPath=Join-Path $base 'pc_fastfile_image_sources\pc_external_image_xbox_stock_closure_all_zones.json'
$nativePath=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$rawPath=Join-Path $base 'pc_rawfile_graft\pc_rawfile_graft_result.json'
$iwdPath=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\nuclear_4g_iwi_image_corpus\nuclear_4g_iwi_image_corpus.json'
foreach($path in @($sourcePath,$corePath,$allPath,$nativePath,$rawPath,$iwdPath)){
    if(-not (Test-Path -LiteralPath $path -PathType Leaf)){throw "Required proven Aztec input missing: $path"}
}
$source=Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json
$core=Get-Content -LiteralPath $corePath -Raw | ConvertFrom-Json
$all=Get-Content -LiteralPath $allPath -Raw | ConvertFrom-Json
$native=Get-Content -LiteralPath $nativePath -Raw | ConvertFrom-Json
$raw=Get-Content -LiteralPath $rawPath -Raw | ConvertFrom-Json
$iwd=Get-Content -LiteralPath $iwdPath -Raw | ConvertFrom-Json

# Exact current corpus gates. Never silently classify different stock scopes
# or a failed OAT scan as proof of a missing/available dependency.
if([string]$source.schema -ne 'WaW360.AztecPcFastfileImageSourceCensus.v1' -or
   [string]$source.status -ne 'AZTEC_PC_FF_IMAGE_SOURCE_DUMP_PARTIAL_RECOVERED' -or
   [int]$source.pcFastfileNameMatchesNativeRoots -ne 647 -or
   [int]$source.extractedPcIwiFiles -ne 11 -or
   [string]$raw.status -ne 'AZTEC_PC_RAWFILE_GRAFT_OAT_READBACK_PASS' -or
   [int]$raw.xboxReadbacksExact -ne 114 -or
   -not [bool]$native.TypeSequenceVerified -or
   @($native.Entries).Count -ne 8509 -or
   [int]$iwd.NativeImages -ne 666 -or
   [int]$iwd.MatchedNativeImages -ne 2){
    throw 'RawFile, PC image-source or native identity evidence changed since proven Aztec gates.'
}
foreach($audit in @($core,$all)){
    if([string]$audit.schema -ne 'WaW360.AztecExternalImageRetailIdentity.v1' -or
       [string]$audit.status -ne 'AZTEC_EXTERNAL_IMAGE_RETAIL_IDENTITY_AUDIT_PASS' -or
       -not [bool]$audit.scanComplete -or
       [int]$audit.distinctFailedPcImageNames -ne 654 -or
       [int]$audit.missingPcImageOatErrorsAcrossRoles -ne 775 -or
       [int]@($audit.missingPcImageClassifications).Count -ne 654){
        throw 'One of the Xbox stock identity reports is incomplete or uses a different source corpus.'
    }
}
if([string]$core.scope -ne 'core-zombie-zones-only' -or
   [int]$core.zonesScanned -ne 8 -or
   [int]$core.failedNamesFoundInScannedXboxRetail -ne 302 -or
   [string]$all.scope -ne 'all-retail-zones' -or
   [int]$all.zonesScanned -ne 67 -or
   [int]$all.failedNamesFoundInScannedXboxRetail -ne 473 -or
   [int]$all.failedNamesNotFoundInScannedXboxRetail -ne 181){
    throw 'Expected 8-core-zone and 67-full-retail-zone Aztec audit PASS pair.'
}
foreach($zone in @($core.zones)+@($all.zones)){
    if(-not [bool]$zone.Loaded -or [int]$zone.OatExitCode -ne 0){
        throw "An OAT retail list scan was not successful: $($zone.Zone)"
    }
}
function Normalize-Image([string]$inputName){
    $v=$inputName.Trim().Trim('"').Replace('\','/').TrimStart('/')
    $p=$v.LastIndexOf('/images/',[StringComparison]::OrdinalIgnoreCase)
    if($p -ge 0){$v=$v.Substring($p+8)}
    elseif($v.StartsWith('images/',[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(7)}
    foreach($e in @('.iwi','.dds')){
        if($v.EndsWith($e,[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(0,$v.Length-$e.Length)}
    }
    if($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(0,$v.Length-3)}
    return $v.ToLowerInvariant()
}
$coreByKey=@{}
foreach($entry in @($core.missingPcImageClassifications)){
    $key=[string]$entry.Key
    if([string]::IsNullOrWhiteSpace($key) -or $coreByKey.ContainsKey($key)){
        throw "Invalid/duplicate image key in 8-zone audit: $key"
    }
    $coreByKey[$key]=$entry
}
$allByKey=@{}
foreach($entry in @($all.missingPcImageClassifications)){
    $key=[string]$entry.Key
    if([string]::IsNullOrWhiteSpace($key) -or $allByKey.ContainsKey($key)){
        throw "Invalid/duplicate image key in 67-zone audit: $key"
    }
    $allByKey[$key]=$entry
}
if($coreByKey.Count -ne 654 -or $allByKey.Count -ne 654){
    throw 'PC missing-image identity sets are not the same size.'
}
$pcIwi=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($record in @($source.extractedSourceImages)){
    [void]$pcIwi.Add((Normalize-Image ([string]$record.Name)))
}
$iwdIwi=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($record in @($iwd.LargestMatchedImages)){
    [void]$iwdIwi.Add((Normalize-Image ([string]$record.CanonicalName)))
}
$nativeNames=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($entry in @($native.Entries | Where-Object {$_.TypeName -ieq 'image'})){
    [void]$nativeNames.Add((Normalize-Image ([string]$entry.Name)))
}
if($nativeNames.Count -ne 666){throw 'Native image identity count not 666.'}

$zombieGlobalNames=@('common.ff','code_post_gfx.ff','ui.ff','nazi_zombie_prototype.ff','nazi_zombie_prototype_load.ff')
$mpNames=@('common_mp.ff','code_post_gfx_mp.ff','ui_mp.ff')
$rows=New-Object 'System.Collections.Generic.List[object]'
$coreTotal=0;$otherOnly=0;$notFound=0
$zombieGlobal=0;$mpOnly=0;$otherRetailOnly=0
$unresolvedNative=0;$unresolvedGenerated=0;$partialImageCandidates=0
foreach($key in @($allByKey.Keys | Sort-Object)){
    if(-not $coreByKey.ContainsKey($key)){
        throw "67-zone image $key absent from the 8-zone source audit."
    }
    $baseRow=$coreByKey[$key];$wideRow=$allByKey[$key]
    $a=[bool]$baseRow.FoundInScannedXboxRetail
    $b=[bool]$wideRow.FoundInScannedXboxRetail
    if($a -and -not $b){
        throw "Full retail scan regressed a previously found 8-zone identity: $key"
    }
    if([int]$baseRow.OatMissingOccurrences -ne [int]$wideRow.OatMissingOccurrences){
        throw "Image OAT error occurrence count drifted: $key"
    }
    $class='not-in-scanned-retail'
    if($a){$class='core-8-zone-identity';$coreTotal++}
    elseif($b){$class='additional-59-zone-identity';$otherOnly++}
    else{$notFound++}

    [string[]]$locations=@($wideRow.XboxRetailZones)
    $hasGlobal=$false;$hasMp=$false;$hasOther=$false
    foreach($zone in $locations){
        $zn=[string]$zone
        if($zn -in $zombieGlobalNames){$hasGlobal=$true}
        elseif($zn -in $mpNames){$hasMp=$true}
        else{$hasOther=$true}
    }
    # This is ONLY a NAME-LOCATION label, never a guarantee that a zone
    # is loaded, that the payloads are equivalent or references can resolve.
    $retailRisk='no-retail-identity'
    if($hasGlobal){$retailRisk='global-or-zombie-zone-identity';$zombieGlobal++}
    elseif($hasMp){$retailRisk='multiplayer-only-zone-identity';$mpOnly++}
    elseif($hasOther){$retailRisk='campaign-or-other-zone-identity';$otherRetailOnly++}
    $inNative=$nativeNames.Contains($key)
    $generated=$key.StartsWith('~',[StringComparison]::Ordinal)
    if(-not $b -and $inNative){$unresolvedNative++}
    if(-not $b -and $generated){$unresolvedGenerated++}
    $hasPcIwi=$pcIwi.Contains($key)
    if($hasPcIwi){$partialImageCandidates++}
    $rows.Add([pscustomobject]@{
        Key=$key
        Name=[string]$wideRow.Name
        AuditPartition=$class
        RetailZoneRisk=$retailRisk
        LooksGenerated=$generated
        IsNativeAztecImageRoot=$inNative
        OatMissingOccurrences=[int]$wideRow.OatMissingOccurrences
        SourceRoles=[string[]]@($wideRow.SourceRoles)
        InPCPartialIwiDump=$hasPcIwi
        InPcIwdImageCorpus=$iwdIwi.Contains($key)
        RetailZones=[string[]]$locations
    })
}
if($coreTotal -ne 302 -or $otherOnly -ne 171 -or $notFound -ne 181 -or
   ($zombieGlobal+$mpOnly+$otherRetailOnly+$notFound) -ne 654){
    throw "Source dependency classification partition mismatch: core=$coreTotal other=$otherOnly noRetail=$notFound"
}
$work=Join-Path $base 'pc_fastfile_image_sources'
$json=Join-Path $work 'aztec_image_dependency_triage.json'
$txt=Join-Path $work 'aztec_image_dependency_triage.txt'
$summary=[ordered]@{
    schema='WaW360.AztecImageDependencyTriage.v1'
    status='AZTEC_IMAGE_DEPENDENCY_TRIAGE_PASS'
    nativeImageRoots=666
    missingPcImageErrorOccurrences=775
    distinctMissingPcImageNames=654
    listedInCore8RetailZones=$coreTotal
    newlyListedInOther59Zones=$otherOnly
    missingFromAll67RetailZones=$notFound
    listedInGlobalOrZombieZone=$zombieGlobal
    listedOnlyInMpZone=$mpOnly
    listedOnlyInCampaignOrOtherRetailZone=$otherRetailOnly
    unresolvedGeneratedLookingNames=$unresolvedGenerated
    unresolvedNativeImageRootNames=$unresolvedNative
    failedImageNamesWithPCPartialIwi=$partialImageCandidates
    pcFastfileNamesMatchingNativeRoots=647
    rawfileShaReadbacks=114
    sourceProvenance='Native Xbox zone lists provide NAME evidence only; PC FF/IWD supplies source metadata. No pixel or retail game bytes copied.'
    hardwareReady=$false
    dependenciesResolvedForRuntime=$false
    productionImageGraftReady=$false
    convertedMapsPromotionApproved=$false
    limitations=@(
        'The 302 core-scope matches include multiplayer-only zones. Neither MP-only nor global/Zombies-zone identity proves runtime residency.',
        'The 171 added-zone matches require separate Xbox dependency/runtime residency evidence before any reference.',
        'The 181 no-retail names may be map-owned, generated composites, or other external content; this audit does not classify actual ownership.',
        '11 cached PC IWI files are diagnostic only; source reproducibility and native Xenon pixel conversion are not proven.'
    )
    rows=$rows.ToArray()
}
$summary | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $json -Encoding UTF8
$report=New-Object 'System.Collections.Generic.List[string]'
$report.Add('AZTEC: 67-ZONE IMAGE DEPENDENCY TRIAGE (READ ONLY)')
$report.Add('================================================')
$report.Add("status=$($summary.status) nativeImages=666 pcErrorOccurrences=775 distinctPcErrorNames=654")
$report.Add("Core eight-zone identities=$coreTotal; additional 59-zone identities=$otherOnly; no retail identity=$notFound")
$report.Add("Global/Zombies identity names=$zombieGlobal; MP-only identity names=$mpOnly; campaign/other-only identity names=$otherRetailOnly")
$report.Add("181 unmatched: native image roots=$unresolvedNative; generated-looking=$unresolvedGenerated")
$report.Add("PC dump image candidates among 654 failed names=$partialImageCandidates")
$report.Add('Neither stock identity nor source name establishes equivalent pixels, safe references, or loaded runtime assets.')
$report.Add('================================================')
$report.Add('NO RETAIL MATCH (FULL 181-ITEM AUDIT)')
foreach($r in @($rows.ToArray() | Where-Object {$_.AuditPartition -eq 'not-in-scanned-retail'})){
    $roles=([string[]]$r.SourceRoles) -join '+'
    $report.Add("UNRESOLVED $($r.Name) [native=$($r.IsNativeAztecImageRoot) generated=$($r.LooksGenerated) roles=$roles pcIwi=$($r.InPCPartialIwiDump)]")
}
$report.Add('================================================')
$report.Add('ADDITIONAL RETAIL ONLY (FULL 171-ITEM AUDIT)')
foreach($r in @($rows.ToArray() | Where-Object {$_.AuditPartition -eq 'additional-59-zone-identity'})){
    $report.Add("OTHER-RETAIL $($r.Name) [zoneRisk=$($r.RetailZoneRisk) zones=$(($r.RetailZones) -join ', ')]")
}
$report.Add('================================================')
$report.Add('CORE 8 RETAIL (FULL 302-ITEM AUDIT)')
foreach($r in @($rows.ToArray() | Where-Object {$_.AuditPartition -eq 'core-8-zone-identity'})){
    $report.Add("CORE-RETAIL $($r.Name) [zoneRisk=$($r.RetailZoneRisk) zones=$(($r.RetailZones) -join ', ')]")
}
$report.Add('NO FF emission, no native pixels, no promotion to Converted Maps.')
[IO.File]::WriteAllLines($txt,[string[]]$report.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-IMG-TRIAGE] ====================== RESULT ======================='
Write-Host "[AZTEC-IMG-TRIAGE] status=$($summary.status)"
Write-Host "[AZTEC-IMG-TRIAGE] 654 missing PC image names: core8=$coreTotal; additional59=$otherOnly; absent67=$notFound"
Write-Host "[AZTEC-IMG-TRIAGE] Retail locations: global/zombie=$zombieGlobal; MP-only=$mpOnly; campaign/other-only=$otherRetailOnly"
Write-Host "[AZTEC-IMG-TRIAGE] Unmatched: native=$unresolvedNative; generated=$unresolvedGenerated"
Write-Host "[AZTEC-IMG-TRIAGE] Report: $txt"
Write-Host '[AZTEC-IMG-TRIAGE] Source and identity classification ONLY. No OAT, no FF, no Xbox.'
