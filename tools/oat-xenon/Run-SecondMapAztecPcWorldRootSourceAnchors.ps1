[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$censusFile=Join-Path $base 'tonight_critical_source_census\aztec_tonight_critical_source_census.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$work=Join-Path $base 'pc_world_root_source_anchors'
$log=Join-Path $work 'pc_map_decompress.log'
$jsonFile=Join-Path $work 'aztec_pc_world_root_source_anchors.json'
$textFile=Join-Path $work 'aztec_pc_world_root_source_anchors.txt'
foreach($p in @($censusFile,$cli)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing source-anchor prerequisite: $p"}
}
$census=Get-Content -LiteralPath $censusFile -Raw | ConvertFrom-Json
if([string]$census.schema -ne 'WaW360.AztecTonightCriticalSourceCensus.v1' -or
   [string]$census.status -ne 'AZTEC_TONIGHT_CRITICAL_PC_SOURCE_IDENTITY_CENSUS_PASS' -or
   [int]$census.nativeRoots -ne 8509 -or [bool]$census.readyForXbox){
    throw 'Original-PC source census provenance is not passing.'
}
$pcRole=@($census.pcSourceRoles | Where-Object {[string]$_.role -eq 'map.ff'})
if($pcRole.Count -ne 1){throw 'Require exactly one original-PC map.ff source identity.'}
$pcPath=[string]$pcRole[0].sourceFastfile
if(-not (Test-Path -LiteralPath $pcPath -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pcPath -Algorithm SHA256).Hash -ine [string]$pcRole[0].sourceSha256){
    throw 'Original PC map.ff changed since 8,509-root identity census.'
}
$families=@('comworld','gameworldsp','gfxworld')
$sizes=@{
    comworld=@(64)
    gameworldsp=@(44)
    # 804 is the existing PC-side reduced GfxWorld fixed-size hypothesis.
    # 900 is our Xenon diagnostic shell size, NOT a verified PC struct size.
    gfxworld=@(804,900)
}
$roots=New-Object 'System.Collections.Generic.List[object]'
foreach($family in $families){
    $matches=@($census.nativeOwnedRootClassifications | Where-Object {
        [string]$_.family -eq $family -and
        [string]$_.classification -eq 'PC_OWNED_NAME_PRESENT'
    })
    if($matches.Count -ne 1 -or
       @($matches[0].pcOwnerRoles).Count -ne 1 -or
       [string]$matches[0].pcOwnerRoles[0] -ne 'map.ff'){
        throw "No unique PC map.ff owned-name evidence for $family."
    }
    $roots.Add([pscustomobject]@{
        family=$family
        nativeRoot=[int]$matches[0].nativeRoot
        sourceName=[string]$matches[0].nativeName
    })
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
$status='AZTEC_PC_WORLD_ROOT_SOURCE_ANCHOR_SCAN_BLOCKED'
$blocker=''
$zoneBytes=0L;$zoneSha=''
$rows=New-Object 'System.Collections.Generic.List[object]'
$short=''
Write-Host '[AZTEC-PC-WORLD] ======================================================='
Write-Host '[AZTEC-PC-WORLD] ORIGINAL PC map.ff WORLD ROOT SOURCE ANCHOR FINGERPRINT'
Write-Host '[AZTEC-PC-WORLD] 3 owned root identities; NO new linker/FF/.NET build.'
Write-Host '[AZTEC-PC-WORLD] Candidate fixed headers are diagnostic, NOT root-body proof.'
try{
    $short=Join-Path ([IO.Path]::GetTempPath()) ('wawAzWd_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    if($short.Length -gt 110){throw 'World probe needs a short physical temporary directory.'}
    New-Item -Path $short -ItemType Directory -Force | Out-Null
    $zoneFile=Join-Path $short 'pc_map.zone'
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & dotnet 'run' '--no-build' '-c' 'Release' '--project' $cli '--' 'decompress' $pcPath $zoneFile 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $log -Encoding UTF8
        $exit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    if($exit -ne 0 -or -not (Test-Path -LiteralPath $zoneFile -PathType Leaf)){
        throw "PC map.ff decompression failed exit=$exit, see $log"
    }
    $zone=[IO.File]::ReadAllBytes($zoneFile)
    $zoneBytes=[long]$zone.LongLength
    if($zoneBytes -lt 1000000L){throw "PC source zone unexpectedly small ($zoneBytes bytes)."}
    $hasher=[Security.Cryptography.SHA256]::Create()
    try{$zoneSha=([BitConverter]::ToString($hasher.ComputeHash($zone))).Replace('-','')}
    finally{$hasher.Dispose()}
    # ISO-8859-1 decodes exactly one char per byte, keeping the original
    # byte offsets of ASCII asset name strings even in binary zone data.
    $chars=[Text.Encoding]::GetEncoding(28591).GetString($zone)
    Write-Host "[AZTEC-PC-WORLD] Original PC decompressed zone=$zoneBytes bytes; SHA256=$zoneSha"
    foreach($root in $roots.ToArray()){
        $name=([string]$root.sourceName).Replace('\','/')
        if($name.Contains('..') -or $name -notmatch '^[A-Za-z0-9_./-]+$'){
            throw "Unexpected unsafe world name in verified native manifest: $name"
        }
        $needle=$name+[char]0
        $starts=New-Object 'System.Collections.Generic.List[int]'
        $search=0
        while($search -lt $chars.Length){
            $pos=$chars.IndexOf($needle,$search,[StringComparison]::OrdinalIgnoreCase)
            if($pos -lt 0){break}
            if($starts.Count -ge 256){break}
            $starts.Add([int]$pos)
            $search=$pos+$needle.Length
        }
        $bodyCandidates=New-Object 'System.Collections.Generic.List[object]'
        foreach($nameStart in $starts.ToArray()){
            foreach($fixedSize in @($sizes[[string]$root.family])){
                $start=[int]($nameStart-$fixedSize)
                if($start -lt 0 -or $fixedSize -gt $zone.Length-$start){continue}
                $namePointer=[BitConverter]::ToUInt32($zone,$start)
                # A T4 source inline name pointer is either FOLLOWING or
                # INSERT. A packed-alias pointer requires a separate decoder.
                if($namePointer -ne [uint32]0xFFFFFFFF -and
                   $namePointer -ne [uint32]0xFFFFFFFE){continue}
                $nonzero=0;$inlinePointers=0;$aliases=0
                for($off=0;$off -le $fixedSize-4;$off+=4){
                    $v=[BitConverter]::ToUInt32($zone,($start+$off))
                    if($v -ne 0){$nonzero++}
                    if($v -eq [uint32]0xFFFFFFFF -or $v -eq [uint32]0xFFFFFFFE){$inlinePointers++}
                    elseif($v -ne 0){$aliases++}
                }
                $hash=[Security.Cryptography.SHA256]::Create()
                try{
                    $digest=([BitConverter]::ToString($hash.ComputeHash($zone,$start,$fixedSize))).Replace('-','')
                }finally{$hash.Dispose()}
                $bodyCandidates.Add([pscustomobject]@{
                    nativeRoot=[int]$root.nativeRoot
                    family=[string]$root.family
                    sourceName=$name
                    possibleBodyOffset=$start
                    observedNameByteOffset=[int]$nameStart
                    testedFixedBytes=[int]$fixedSize
                    namePointerHex=('0x{0:X8}' -f $namePointer)
                    nonzeroWords=$nonzero
                    inlinePointers=$inlinePointers
                    otherNonzeroWords=$aliases
                    fixedCandidateSha256=$digest
                    candidateOnly=$true
                })
            }
        }
        $row=[pscustomobject]@{
            family=[string]$root.family
            nativeRoot=[int]$root.nativeRoot
            exactNativeName=$name
            originalPCMapRole='map.ff'
            matchingNameStringOccurrences=$starts.Count
            occurrencesCapped=($starts.Count -ge 256)
            observedNameByteOffsets=@($starts.ToArray() | Select-Object -First 16)
            plausibleFixedHeaderCandidates=$bodyCandidates.Count
            candidates=@($bodyCandidates.ToArray() | Select-Object -First 16)
            sourceBodyDecoded=$false
            fixedBodySizeVerified=$false
        }
        $rows.Add($row)
        Write-Host "[AZTEC-PC-WORLD] $($row.family) native #$($row.nativeRoot): source names=$($row.matchingNameStringOccurrences); plausible fixed-header candidates=$($row.plausibleFixedHeaderCandidates)"
        foreach($c in @($row.candidates | Select-Object -First 3)){
            Write-Host "[AZTEC-PC-WORLD]   candidate offset=0x$('{0:X8}' -f $c.possibleBodyOffset) fixedBytes=$($c.testedFixedBytes) pointers=$($c.inlinePointers) data SHA256=$($c.fixedCandidateSha256)"
        }
    }
    $status='AZTEC_PC_WORLD_ROOT_SOURCE_ANCHOR_SCAN_COMPLETE'
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-PC-WORLD] BLOCKER: $blocker"
    if(Test-Path -LiteralPath $log -PathType Leaf){
        foreach($line in @(Get-Content -LiteralPath $log -Tail 8)){
            Write-Host "[AZTEC-PC-WORLD] decompressor: $line"
        }
    }
}finally{
    if(-not [string]::IsNullOrWhiteSpace($short) -and
       (Test-Path -LiteralPath $short -PathType Container)){
        Remove-Item -LiteralPath $short -Recurse -Force
    }
}
$report=[ordered]@{
    schema='WaW360.AztecPcWorldRootSourceAnchors.v1'
    status=$status
    originalPCMapFastfile=$pcPath
    originalPCMapFastfileSha256=[string]$pcRole[0].sourceSha256
    decompressedPCZoneBytes=$zoneBytes
    decompressedPCZoneSha256=$zoneSha
    worldRootObservations=$rows.ToArray()
    blocker=$blocker
    worldBodiesConverted=0
    realSourceBodyRecovered=$false
    hardwareTestAuthorized=$false
    note='Read-only native-name string/possible-inline-header reconnaissance of SHA-pinned original PC map.ff. A candidate is NOT a decoded source world root and cannot authorize synthetic or real Xbox world bodies. Source body sizes/zone child order remain to be proven.'
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $jsonFile -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add("Aztec original PC world-root source anchor scan: $status")
$lines.Add("Original PC zone bytes=$zoneBytes, SHA256=$zoneSha")
foreach($row in $rows.ToArray()){
    $lines.Add("WORLD $($row.family), native root=$($row.nativeRoot), owned name='$($row.exactNativeName)', PC occurrences=$($row.matchingNameStringOccurrences), fixed-header candidates=$($row.plausibleFixedHeaderCandidates)")
    foreach($candidate in @($row.candidates | Select-Object -First 8)){
        $lines.Add("  CANDIDATE: fixed=0x$('{0:X}' -f $candidate.testedFixedBytes), source offset=0x$('{0:X8}' -f $candidate.possibleBodyOffset), pointer=$($candidate.namePointerHex), SHA256=$($candidate.fixedCandidateSha256)")
    }
}
$lines.Add("blocker=$blocker")
$lines.Add('No source world bodies decoded or emitted; NO BUILD; NO FF; NOT FOR XBOX.')
[IO.File]::WriteAllLines($textFile,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-WORLD] ================= RESULT ============================='
Write-Host "[AZTEC-PC-WORLD] status=$status"
Write-Host "[AZTEC-PC-WORLD] report=$textFile"
Write-Host '[AZTEC-PC-WORLD] Candidate scan ONLY / no world conversion / NOT FOR XBOX.'
if($status -ne 'AZTEC_PC_WORLD_ROOT_SOURCE_ANCHOR_SCAN_COMPLETE'){exit 4}
