[CmdletBinding()]
param(
    [string]$ProvenanceJson = '',
    [string[]]$IncludeMaps = @('nazi_zombie_aztec_V2.2')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$IncludeMaps = @($IncludeMaps | ForEach-Object { ([string]$_) -split ',' } |
    ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ -ne '' })
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path

# This tool is OFFLINE and does not launch OAT, copy PC files, compile,
# construct an FF or infer true packed-pointer string targets.
# It consumes one preexisting, same-run WeaponNameAudit report.
if([string]::IsNullOrWhiteSpace($ProvenanceJson)){
    $root=Join-Path $repo 'output\multi_map_matrix\runs'
    if(-not (Test-Path -LiteralPath $root -PathType Container)){throw "Missing prior matrix runs: $root"}
    foreach($file in (Get-ChildItem -LiteralPath $root -File -Recurse -Filter 'weapon_name_provenance_*.json' |
        Sort-Object LastWriteTimeUtc -Descending)){
        try{
            $candidate=Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
            if([string]$candidate.schema -ne 'WaW360.WeaponNameAudit.v1'){continue}
            $hasNames=$false
            foreach($map in @($candidate.maps)){
                if($null -eq $map){continue}
                foreach($source in @($map.pcSourceOatListings)){
                    if($null -ne $source -and
                        $null -ne $source.PSObject.Properties['oatWeaponAssetNames'] -and
                        $null -ne $source.PSObject.Properties['expectedProofFile']){
                        $hasNames=$true
                    }
                }
            }
            if($hasNames){$ProvenanceJson=$file.FullName;break}
        }catch{
            Write-Host "[WEAPON-ORDER] Skipping unreadable historical report: $($file.FullName)"
        }
    }
    if([string]::IsNullOrWhiteSpace($ProvenanceJson)){
        throw 'No WeaponNameAudit JSON with full original OAT weapon names was found.'
    }
}
$ProvenanceJson=(Resolve-Path -LiteralPath $ProvenanceJson).Path
$doc=Get-Content -LiteralPath $ProvenanceJson -Raw | ConvertFrom-Json
if([string]$doc.schema -ne 'WaW360.WeaponNameAudit.v1'){throw "Unexpected provenance schema: $ProvenanceJson"}
$out=Join-Path (Split-Path -Path $ProvenanceJson -Parent) ('weapon_order_witness_'+(Get-Date -Format 'yyyyMMdd_HHmmss')+'.json')
$results=New-Object 'System.Collections.Generic.List[object]'
Write-Host '[WEAPON-ORDER] ========================================================='
Write-Host '[WEAPON-ORDER] OFFLINE ONLY: compare saved OAT XAsset table order against physical WeaponDef proof order.'
Write-Host "[WEAPON-ORDER] Original evidence: $ProvenanceJson"

foreach($map in @($doc.maps)){
    if($null -eq $map){continue}
    $wanted=$false
    foreach($pattern in $IncludeMaps){if([string]$map.map -like $pattern){$wanted=$true;break}}
    if(-not $wanted){continue}
    $mapName=[string]$map.map
    foreach($source in @($map.pcSourceOatListings)){
        if($null -eq $source){continue}
        if($null -eq $source.PSObject.Properties['expectedProofFile'] -or
           $null -eq $source.PSObject.Properties['oatWeaponAssetNames']){
            Write-Host "[WEAPON-ORDER] $mapName source lacks full-name provenance; skipping."
            continue
        }
        $path=[string]$source.path
        $role=[string]$source.sourceRole
        $expectedProof=[string]$source.expectedProofFile
        $proofs=@($map.proofNames | Where-Object {
            $null -ne $_ -and [string]$_.proof -ieq $expectedProof
        } | Sort-Object -Property @{Expression='record';Ascending=$true})
        $oatNames=@($source.oatWeaponAssetNames | Where-Object {$null -ne $_} | ForEach-Object {[string]$_})
        $anchorCount=0
        $conflicts=New-Object 'System.Collections.Generic.List[object]'
        $orderCandidates=New-Object 'System.Collections.Generic.List[object]'
        $equalCounts=([int]$source.exit -eq 0 -and $proofs.Count -eq $oatNames.Count)
        $uniqueNames=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $allNamesUnique=$true
        foreach($name in $oatNames){if(-not $uniqueNames.Add($name)){$allNamesUnique=$false}}
        if($equalCounts){
            for($i=0;$i -lt $proofs.Count;$i++){
                $p=$proofs[$i]
                if([bool]$p.synthetic){continue}
                $anchorCount++
                if(-not [string]::Equals([string]$p.name,[string]$oatNames[$i],
                    [StringComparison]::OrdinalIgnoreCase)){
                    $conflicts.Add([pscustomobject]@{
                        ordinal=$i;knownProofName=[string]$p.name;oatName=[string]$oatNames[$i]
                        sourceProofBodyOffset=[long]$p.offset
                    })
                }
            }
        }
        $allAnchorsMatch=($equalCounts -and $allNamesUnique -and
            $anchorCount -gt 0 -and $conflicts.Count -eq 0)
        if($allAnchorsMatch){
            for($i=0;$i -lt $proofs.Count;$i++){
                $p=$proofs[$i]
                if(-not [bool]$p.synthetic){continue}
                $orderCandidates.Add([pscustomobject]@{
                    status='UNVERIFIED_ORDINAL_ONLY'
                    sourceFF=$path
                    sourceRole=$role
                    ordinal=$i
                    proofRecord=[int]$p.record
                    proofNamePointer=[string]$p.name
                    pointerBlock=$p.pointerBlock
                    pointerOffset=$p.pointerOffset
                    oatListedNameAtSameOrdinal=[string]$oatNames[$i]
                    independentPackedPointerTargetProof=$false
                    allowedForProductionConversion=$false
                })
            }
        }
        $status=if(-not $equalCounts){'COUNT_OR_SOURCE_OAT_MISMATCH'}elseif(-not $allNamesUnique){
            'NONUNIQUE_OAT_NAMES'
        }elseif($anchorCount -eq 0){
            'NO_KNOWN_NAME_ANCHORS'
        }elseif($conflicts.Count -gt 0){
            'KNOWN_NAME_ORDER_CONFLICT'
        }else{
            'ALL_KNOWN_NAMES_MATCH_ORDER_ONLY'
        }
        Write-Host "[WEAPON-ORDER] $mapName $role : $status | OAT=$($oatNames.Count) proof=$($proofs.Count) knownAnchors=$anchorCount conflicts=$($conflicts.Count) unresolvedOrderCandidates=$($orderCandidates.Count)"
        foreach($conflict in @($conflicts.ToArray() | Select-Object -First 6)){
            Write-Host "[WEAPON-ORDER]   CONFLICT ordinal=$($conflict.ordinal) proof=$($conflict.knownProofName) OAT=$($conflict.oatName)"
        }
        foreach($item in @($orderCandidates.ToArray() | Select-Object -First 10)){
            Write-Host "[WEAPON-ORDER]   UNVERIFIED ordinal=$($item.ordinal) pointer=$($item.proofNamePointer) OAT-order-name=$($item.oatListedNameAtSameOrdinal)"
        }
        $results.Add([pscustomobject]@{
            map=$mapName;role=$role;sourceFF=$path;status=$status
            oatWeaponAssets=$oatNames.Count;physicalProofBodies=$proofs.Count
            knownNameAnchors=$anchorCount;knownNameOrderConflicts=@($conflicts.ToArray())
            unverifiedOrdinalCandidates=@($orderCandidates.ToArray())
            exactPointerStringResolutionEstablished=$false
            productionUseAllowed=$false
        })
    }
}
if($results.Count -eq 0){throw "No source FFs in selected maps; check -IncludeMaps and -ProvenanceJson"}
[ordered]@{
    schema='WaW360.WeaponOrdinalWitness.v1'
    sourceProvenanceJson=$ProvenanceJson
    readOnly=$true
    oATInvoked=$false
    xboxFFBuilt=$false
    note='A perfect ordering witness makes a mapping plausible, NOT independently verified. No conversion code should apply an ordinal-only candidate without packed-pointer target proof.'
    sources=@($results.ToArray())
}|ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $out -Encoding UTF8
Write-Host "[WEAPON-ORDER] JSON=$out"
Write-Host '[WEAPON-ORDER] No OAT calls, source files, new FFs, or inferred production weapon names.'
