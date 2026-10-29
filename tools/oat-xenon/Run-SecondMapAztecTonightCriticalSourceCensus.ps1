[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$n=Join-Path $base 'native_material_mapping'
$baselinePath=Join-Path $base 'second_map_baseline.json'
$identityPath=Join-Path $n 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$sourcePath=Join-Path $n 'pc_fastfile_image_sources\pc_fastfile_image_sources.json'
$gatePath=Join-Path $n 'tonight_hardware_gate\aztec_tonight_hardware_gate.json'
$work=Join-Path $n 'tonight_critical_source_census'
$outJson=Join-Path $work 'aztec_tonight_critical_source_census.json'
$outTxt=Join-Path $work 'aztec_tonight_critical_source_census.txt'
foreach($p in @($baselinePath,$identityPath,$sourcePath,$gatePath)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec critical-source census prerequisite missing: $p"
    }
}
$baseline=Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
$native=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$source=Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json
$gate=Get-Content -LiteralPath $gatePath -Raw | ConvertFrom-Json
if([string]$baseline.format -ne 'WaW360.SecondMapBaseline.v1' -or
   [string]$baseline.mapName -ine 'nazi_zombie_aztec_V2.2' -or
   -not [bool]$native.TypeSequenceVerified -or
   @($native.Entries).Count -ne 8509 -or
   [string]$gate.schema -ne 'WaW360.AztecTonightHardwareGate.v1' -or
   [string]$gate.status -ne 'AZTEC_TONIGHT_HARDWARE_NO_GO_CURRENT_SCRATCH' -or
   [bool]$gate.safeToCopyToXbox){
    throw 'Native 8,509-root identity or current hardware NO-GO report changed.'
}
function Norm([string]$value){
    $value=$value.Trim().Trim('"').Replace('\','/').TrimStart('/')
    while($value.StartsWith(',',[StringComparison]::Ordinal)){$value=$value.Substring(1)}
    if($value.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){$value=$value.Substring(0,$value.Length-3)}
    return $value.ToLowerInvariant()
}
function Family([string]$value){
    return ($value.ToLowerInvariant() -replace '[^a-z0-9]','')
}
$targets=@('stringtable','gfxworld','clipmap','comworld','gameworldsp','gameworldmp','mapents','xmodel','material','image')
$sourcePins=@{}
foreach($row in @($source.sourceFastfiles)){
    $role=[string]$row.Role
    if($role -in @('map.ff','map_patch.ff','mod.ff')){
        if($sourcePins.ContainsKey($role)){throw "Duplicate original PC FF identity pin: $role"}
        $sourcePins[$role]=$row
    }
}
if($sourcePins.Count -ne 3){throw "Expected 3 original PC FF SHA pins, got $($sourcePins.Count)"}
$known=@{}
foreach($family in $targets){$known[$family]=@{}}
$sourceRoles=New-Object 'System.Collections.Generic.List[object]'
$ordinal=0
foreach($ff in @($baseline.fastfiles)){
    $ordinal++
    $role=[string]$ff.Role
    if(-not $sourcePins.ContainsKey($role)){continue}
    $pin=$sourcePins[$role]
    $path=[string]$ff.Path
    if(-not (Test-Path -LiteralPath $path -PathType Leaf) -or
       [long](Get-Item -LiteralPath $path).Length -ne [long]$ff.Bytes -or
       [string]$pin.SourceFastFile -ine $path -or
       (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine [string]$pin.Sha256){
        throw "Original PC source FF changed: $role"
    }
    $stem=([IO.Path]::GetFileNameWithoutExtension($path)) -replace '[^A-Za-z0-9_.-]','_'
    $log=Join-Path $base ('oat_asset_lists\{0:D2}_{1}.list.log' -f $ordinal,$stem)
    if(-not (Test-Path -LiteralPath $log -PathType Leaf)){
        throw "Previously parsed original-PC OAT list missing: $log"
    }
    $seen=0;$owners=0;$refs=0
    foreach($line in [IO.File]::ReadLines($log)){
        $s=$line.Trim()
        if($s.StartsWith('[',[StringComparison]::Ordinal)){
            $close=$s.IndexOf(']')
            if($close -ge 0 -and $close+1 -lt $s.Length){$s=$s.Substring($close+1).TrimStart()}
        }
        if($s -notmatch '^([A-Za-z_][A-Za-z0-9_]*)\s*,\s*(.*?)\s*$'){continue}
        $type=Family ([string]$Matches[1])
        if(-not $known.ContainsKey($type)){continue}
        $rawName=[string]$Matches[2]
        if([string]::IsNullOrWhiteSpace($rawName)){continue}
        $key=Norm $rawName
        if([string]::IsNullOrWhiteSpace($key)){continue}
        $isRef=$rawName.StartsWith(',',[StringComparison]::Ordinal)
        if(-not $known[$type].ContainsKey($key)){
            $known[$type][$key]=New-Object 'System.Collections.Generic.List[object]'
        }
        $known[$type][$key].Add([pscustomobject]@{
            role=$role
            rawName=$rawName
            pcOwned=(-not $isRef)
        })
        $seen++
        if($isRef){$refs++}else{$owners++}
    }
    $sourceRoles.Add([pscustomobject]@{
        role=$role
        sourceFastfile=$path
        sourceSha256=[string]$pin.Sha256
        oatListLog=$log
        targetFamilyRows=$seen
        pcOwnedNames=$owners
        pcReferenceNames=$refs
    })
}
if($sourceRoles.Count -ne 3){throw "Source lists covered $($sourceRoles.Count) / 3 original PC FF roles"}
$rows=New-Object 'System.Collections.Generic.List[object]'
$familySummary=New-Object 'System.Collections.Generic.List[object]'
foreach($family in $targets){
    $roots=@($native.Entries | Where-Object {(Family ([string]$_.TypeName)) -eq $family})
    $ownedRoots=@($roots | Where-Object {-not ([string]$_.Name).StartsWith(',')})
    $ownedMatches=0;$referencedMatches=0;$missing=0
    foreach($root in $ownedRoots){
        $key=Norm ([string]$root.Name)
        $hits=if($known[$family].ContainsKey($key)){@($known[$family][$key].ToArray())}else{@()}
        $ownerMatches=@($hits | Where-Object {[bool]$_.pcOwned})
        $referenceMatches=@($hits | Where-Object {-not [bool]$_.pcOwned})
        $kind='PC_NAME_ABSENT'
        if($ownerMatches.Count -gt 0){$kind='PC_OWNED_NAME_PRESENT';$ownedMatches++}
        elseif($referenceMatches.Count -gt 0){$kind='PC_REFERENCE_ONLY';$referencedMatches++}
        else{$missing++}
        $rows.Add([pscustomobject]@{
            nativeRoot=[int]$root.Index
            family=$family
            nativeName=[string]$root.Name
            classification=$kind
            pcOwnerRoles=@($ownerMatches | ForEach-Object {$_.role} | Sort-Object -Unique)
            pcReferenceRoles=@($referenceMatches | ForEach-Object {$_.role} | Sort-Object -Unique)
            originalPcBodyConverted=$false
            note='PC OAT list proves identity ownership only, never body completeness or Xbox runtime dependency.'
        })
    }
    $familySummary.Add([pscustomobject]@{
        family=$family
        allNativeRoots=$roots.Count
        ownedNativeRoots=$ownedRoots.Count
        pcOwnedNamePresent=$ownedMatches
        pcReferenceOnly=$referencedMatches
        pcNameAbsent=$missing
        originalPcPayloadConversionProvenByThisCensus=$false
    })
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
$status='AZTEC_TONIGHT_CRITICAL_PC_SOURCE_IDENTITY_CENSUS_PASS'
$report=[ordered]@{
    schema='WaW360.AztecTonightCriticalSourceCensus.v1'
    status=$status
    nativeRoots=8509
    pcSourceRoles=$sourceRoles.ToArray()
    familySummary=$familySummary.ToArray()
    nativeOwnedRootClassifications=$rows.ToArray()
    worldOrStringtablePcBodiesConverted=$false
    originalHardwareGate=$gatePath
    readyForXbox=$false
    notForHardware=$true
    next='Prioritize original PC owned world/StringTable bodies and source-critical dependencies, not blind conversion of all XModels/Materials/Images.'
    note='Census only; uses SHA-pinned original PC FFs and archived OAT lists. A matching name is not a proven body. Never promote reference-only roots or copy the current scratch FF to Xbox.'
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $outJson -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add("AZTEC TONIGHT CRITICAL ORIGINAL-PC SOURCE IDENTITY CENSUS: $status")
$lines.Add('All three original PC FastFiles SHA-256 verified; reused existing OAT --list metadata, no OAT/build needed.')
foreach($family in $familySummary.ToArray()){
    $lines.Add("FAMILY $($family.family): owned native=$($family.ownedNativeRoots); original-PC OWNED-NAME=$($family.pcOwnedNamePresent); PC REFERENCE-ONLY=$($family.pcReferenceOnly); PC NAME ABSENT=$($family.pcNameAbsent)")
}
$lines.Add('WORLD/STRINGTABLE ROOTS')
foreach($row in $rows.ToArray() | Where-Object {$_.family -in @('gfxworld','clipmap','comworld','gameworldsp','gameworldmp','mapents','stringtable')}){
    $lines.Add("ROOT $($row.nativeRoot) $($row.family) $($row.nativeName): $($row.classification); PC owners=$(($row.pcOwnerRoles) -join ','); refs=$(($row.pcReferenceRoles) -join ',')")
}
$lines.Add('Name matches are NOT payload conversion. No Xbox deployment approved.')
[IO.File]::WriteAllLines($outTxt,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-TONIGHT-SOURCES] ============================================'
Write-Host "[AZTEC-TONIGHT-SOURCES] status=$status"
foreach($family in $familySummary.ToArray()){
    Write-Host "[AZTEC-TONIGHT-SOURCES] $($family.family): native=$($family.ownedNativeRoots), PC owned-names=$($family.pcOwnedNamePresent), ref-only=$($family.pcReferenceOnly), missing=$($family.pcNameAbsent)"
}
Write-Host "[AZTEC-TONIGHT-SOURCES] report=$outTxt"
Write-Host '[AZTEC-TONIGHT-SOURCES] NO BUILD / NO FF / NO XBOX.'
