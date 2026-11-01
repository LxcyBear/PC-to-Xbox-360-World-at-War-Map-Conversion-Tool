[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# Read-only, EXACT original-PC OAT loaded SndAliasCustom names versus
# cached OEM Xbox 360 retail OAT Sound XAsset names. A name match IS NOT
# a real XMA sample body, not a preloaded Xbox zone, not a runtime link.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$typed=Join-Path $base 'pc_g11_flame_loaded_virtual_target_proof\aztec_26_flame_packed_original_pc_typed_material_alias_probe_v2.json'
$stock=Join-Path $base 'pc_fastfile_image_sources\stock_identity_logs'
$out=Join-Path $base 'pc_g11_flame_loaded_virtual_target_proof\aztec_flame_sound_alias_real_xbox_retail_stock_names.json'
if(-not(Test-Path -LiteralPath $typed -PathType Leaf)){
    throw "No real original PC loaded SndAliasCustom v2 proof: $typed"
}
$t=Get-Content -LiteralPath $typed -Raw|ConvertFrom-Json
if([string]$t.schema -ne 'WaW360.AztecFlameOriginalPcOatLoadedTypedPointers.v2' -or
   [string]$t.status -ne 'AZTEC_ORIGINAL_PC_FLAME_TYPED_MATERIAL_NAME_SOUND_SOURCE_RESOLVED_PARTIAL_NO_XBOX_ASSETS' -or
   [int]$t.realOriginalPcFlameParents -ne 4 -or
   [int]$t.packedSourceSoundReferences -ne 6 -or
   [int]$t.packedSourceMaterialReferences -ne 17 -or
   [int]$t.packedSourceNameReferences -ne 3 -or
   [int]$t.genuineOriginalPcNullSoundSlotsPreserved -ne 4 -or
   [bool]$t.xboxHardwareReady -or
   @($t.typedSourceTargets).Count -ne [int]$t.uniqueSourcePackedPointerTargets){
    throw 'Real original-PC 26-reference typed-v2 FlameTable evidence not consistent. No stock material donor allowed.'
}
$targets=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
foreach($r in @($t.typedSourceTargets)){
    $roles=@($r.roles)
    if($roles -notcontains 'FlameSound' -or
       -not [bool]$r.pcLoadedFourByteSndAliasCustomNameCandidate){continue}
    if([bool]$r.differentAssetKindsSharePointer){continue}
    $name=[string]$r.sourceSndAliasName
    if([string]::IsNullOrWhiteSpace($name)){continue}
    if(-not $targets.ContainsKey($name)){
        $targets.Add($name,(New-Object 'System.Collections.Generic.List[object]'))
    }
    $targets[$name].Add([pscustomobject]@{
        originalPcPackedPtr=[string]$r.rawOriginalPcPackedPointer
        actualPcLoaderOatSoundRootNameMatch=[bool]$r.soundAliasNameIndependentlyListedAsOriginalPcOatAsset
        fields=@($r.originalSourcePointerUses)
    })
}
$matchesByName=@{}
foreach($name in $targets.Keys){$matchesByName[$name]=New-Object 'System.Collections.Generic.List[object]'}
$metaFiles=@()
if(Test-Path -LiteralPath $stock -PathType Container){
    $metaFiles=@(Get-ChildItem -LiteralPath $stock -File -Filter '*.meta.json')
}
$accepted=0;$stale=0
$seen=@{}
foreach($meta in $metaFiles){
    try{
        $m=Get-Content -LiteralPath $meta.FullName -Raw|ConvertFrom-Json
        $ffPath=[string]$m.file
        $log=$meta.FullName.Substring(0,$meta.FullName.Length-'.meta.json'.Length)
        if(-not(Test-Path -LiteralPath $ffPath -PathType Leaf) -or
           -not(Test-Path -LiteralPath $log -PathType Leaf) -or
           [int]$m.exitCode -ne 0){$stale++;continue}
        $file=Get-Item -LiteralPath $ffPath
        if($seen.ContainsKey($file.FullName) -or
           [long]$file.Length -ne [long]$m.bytes -or
           $file.LastWriteTimeUtc.ToString('o') -cne [string]$m.modifiedUtc){
            $stale++;continue
        }
        $seen[$file.FullName]=$true
        $accepted++
        foreach($line in [IO.File]::ReadLines($log)){
            $text=$line.TrimStart()
            if($text.StartsWith('[',[StringComparison]::Ordinal)){
                $end=$text.IndexOf(']')
                if($end -ge 0 -and $end+1 -lt $text.Length){
                    $text=$text.Substring($end+1).TrimStart()
                }
            }
            if($text -notmatch '(?i)^sound\s*,\s*(.*?)\s*$'){continue}
            $listed=[string]$Matches[1]
            $listed=$listed.Trim().Trim('"')
            if([string]::IsNullOrWhiteSpace($listed)){continue}
            $reference=$listed.StartsWith(',',[StringComparison]::Ordinal)
            $name=if($reference){$listed.TrimStart(',')}else{$listed}
            if(-not $targets.ContainsKey($name)){continue}
            $matchesByName[$name].Add([pscustomobject]@{
                xboxRetailZone=[IO.Path]::GetFileName($file.FullName)
                xboxRetailSource=$file.FullName
                sourceListingPath=$log
                offlineXAssetEvidence=if($reference){'REFERENCE_ONLY_NAME'}else{'OAT_OWNED_BODY_NAME'}
                likelyStockBaseZone=([IO.Path]::GetFileName($file.FullName).ToLowerInvariant() -in
                    @('common.ff','code_post_gfx.ff','patch.ff'))
                physicalXmaBytesConvertedOrExtracted=$false
                xboxRuntimeSoundZoneResidencyVerified=$false
            })
        }
    }catch{$stale++}
}
$rows=New-Object 'System.Collections.Generic.List[object]'
$ownerMatches=0;$baseMatches=0;$aliasOccurrencesWithOwnedCandidate=0
foreach($name in @($targets.Keys|Sort-Object)){
    $catalog=@($matchesByName[$name].ToArray())
    $owned=@($catalog|Where-Object {$_.offlineXAssetEvidence -eq 'OAT_OWNED_BODY_NAME'})
    $base=@($owned|Where-Object {$_.likelyStockBaseZone})
    $ref=@($catalog|Where-Object {$_.offlineXAssetEvidence -eq 'REFERENCE_ONLY_NAME'})
    if($owned.Count -gt 0){$ownerMatches++
        foreach($p in $targets[$name]){$aliasOccurrencesWithOwnedCandidate+=@($p.fields|Where-Object {$_.kind -eq 'FlameSound'}).Count}
    }
    if($base.Count -gt 0){$baseMatches++}
    $state=if($base.Count -gt 0){'OFFLINE_BASE_RETAIL_SOUND_NAME_EXISTS_PRELOAD_NOT_PROVEN'}
        elseif($owned.Count -gt 0){'OFFLINE_RETAIL_SOUND_OWNED_NAME_EXISTS_PRELOAD_NOT_PROVEN'}
        elseif($ref.Count -gt 0){'OFFLINE_RETAIL_REFERENCE_ONLY_NOT_AUDIO_BODY_PROOF'}
        elseif($accepted -ge 67 -and $stale -eq 0){'NO_EXACT_SOUND_NAME_FOUND_IN_67_OFFLINE_XBOX_OAT_LISTS'}
        else{'UNKNOWN_INCOMPLETE_OR_STALE_RETAIL_NAME_CATALOG'}
    $rows.Add([pscustomobject][ordered]@{
        originalPcLoadedSoundAliasName=$name
        sourceReferences=@($targets[$name].ToArray())
        offlineXboxSoundNameState=$state
        offlineOwnedRetailNameCount=$owned.Count
        offlineBaseOwnedNameCount=$base.Count
        offlineReferenceOnlyCount=$ref.Count
        matchingXboxOatRetailNameEvidence=@($catalog)
        actualXboxSoundSampleXmaBytesVerified=$false
        nativeXmaSoundAliasBindingApproved=$false
    })
    Write-Host "[AZTEC-XMA-STOCK] source alias=$name : $state (owned-retail-names=$($owned.Count))"
}
$report=[ordered]@{
    schema='WaW360.AztecFlameRealXboxSoundStockNameCrosswalk.v1'
    status='ORIGINAL_PC_SND_ALIAS_TO_OFFLINE_RETAIL_XBOX_SOUND_NAME_TRIAGE_ONLY_NO_XMA_PAYLOAD'
    realOriginalPcSoundAliasNames=$targets.Count
    uniqueSourceSoundNamesWithOfflineXboxOwnedNameCandidate=$ownerMatches
    uniqueSourceSoundNamesWithPossibleStockBaseNameCandidate=$baseMatches
    flamePackedSoundReferenceOccurrencesWithOfflineOwnedNameCandidate=$aliasOccurrencesWithOwnedCandidate
    offlineOriginalXboxRetailZonesAccepted=$accepted
    offlineOriginalXboxRetailZonesStaleOrInvalid=$stale
    retailCatalogComplete67NoStale=($accepted -ge 67 -and $stale -eq 0)
    audioSampleCodecXmaPayloadBytesVerified=0
    realXboxSoundAliasAssetPayloadGrafted=$false
    nativeXmaSourceAudioAndSampleGraphClosed=$false
    nativeXboxStockRuntimeResidencyProven=$false
    oldOatGoodNativeFfChanged=$false
    nullFlameSoundSlotsPreserved=4
    realPcFlameSoundNamesToXboxStockNameEvidence=@($rows.ToArray())
}
$report|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $out -Encoding UTF8
Write-Host "[AZTEC-XMA-STOCK] Genuine PC loaded alias names=$($targets.Count); offline Xbox owned-name candidates=$ownerMatches; 67-zone catalog accepted=$accepted stale=$stale"
Write-Host '[AZTEC-XMA-STOCK] Pure source name evidence. NO XMA sample bytes, codecs, runtime residency, FF or Xbox.'
Write-Host "[AZTEC-XMA-STOCK] JSON=$out"
