[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$anchorFile=Join-Path $base 'pc_world_root_source_anchors\aztec_pc_world_root_source_anchors.json'
$censusFile=Join-Path $base 'tonight_critical_source_census\aztec_tonight_critical_source_census.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$work=Join-Path $base 'pc_world_header_forensics'
$log=Join-Path $work 'pc_map_decompress.log'
$reportFile=Join-Path $work 'aztec_pc_world_header_forensics.json'
$textFile=Join-Path $work 'aztec_pc_world_header_forensics.txt'
foreach($p in @($anchorFile,$censusFile,$cli)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing original-PC world forensics input: $p"}
}
$anchors=Get-Content -LiteralPath $anchorFile -Raw | ConvertFrom-Json
$census=Get-Content -LiteralPath $censusFile -Raw | ConvertFrom-Json
if([string]$anchors.schema -ne 'WaW360.AztecPcWorldRootSourceAnchors.v1' -or
   [string]$anchors.status -ne 'AZTEC_PC_WORLD_ROOT_SOURCE_ANCHOR_SCAN_COMPLETE' -or
   [long]$anchors.decompressedPCZoneBytes -ne 113701260L -or
   [string]$census.status -ne 'AZTEC_TONIGHT_CRITICAL_PC_SOURCE_IDENTITY_CENSUS_PASS' -or
   [int]$census.nativeRoots -ne 8509 -or
   @($anchors.worldRootObservations).Count -ne 3 -or
   [bool]$census.readyForXbox){
    throw 'World-name anchor or PC-source census provenance changed.'
}
$roles=@($census.pcSourceRoles | Where-Object {[string]$_.role -eq 'map.ff'})
if($roles.Count -ne 1 -or [string]$roles[0].sourceFastfile -ine [string]$anchors.originalPCMapFastfile -or
   [string]$roles[0].sourceSha256 -ine [string]$anchors.originalPCMapFastfileSha256){
    throw 'World header forensics requires original SHA-pinned PC map.ff only.'
}
$pc=[string]$roles[0].sourceFastfile
if(-not (Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$roles[0].sourceSha256){
    throw 'Original PC map.ff source changed since the passing world-name scan.'
}
$expected=@{
    comworld=@{name='ComWorld';root=7178;bytes=64}
    gameworldsp=@{name='GameWorldSp';root=7636;bytes=44}
    gfxworld=@{name='GfxWorld';root=7635;bytes=804}
}
function U32([byte[]]$b,[int]$p) {
    if($p -lt 0 -or $p -gt ($b.Length-4)){throw "Out-of-range U32 at $p"}
    return [BitConverter]::ToUInt32($b,$p)
}
function HexBytes([byte[]]$b,[int]$p,[int]$count){
    if($p -lt 0 -or $count -lt 0 -or $p -gt $b.Length-$count){throw 'Hex window out of range.'}
    return [BitConverter]::ToString($b,$p,$count).Replace('-',' ')
}
function ReadWordSummary([byte[]]$b,[int]$p,[int]$maxWords){
    $items=New-Object 'System.Collections.Generic.List[string]'
    for($i=0;$i -lt $maxWords;$i++){
        $addr=$p+$i*4
        if($addr -lt 0 -or $addr -gt ($b.Length-4)){break}
        $items.Add(('0x{0:X8}=0x{1:X8}' -f $addr,(U32 $b $addr)))
    }
    return $items.ToArray()
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
$status='AZTEC_PC_WORLD_HEADER_FORENSICS_BLOCKED'
$blocker=''
$rows=New-Object 'System.Collections.Generic.List[object]'
$lines=New-Object 'System.Collections.Generic.List[string]'
$temp=''
Write-Host '[AZTEC-WORLD-HEADER] ================================================='
Write-Host '[AZTEC-WORLD-HEADER] REAL PC WORLD ROOT NAME-ADJACENT POINTER/ABI FORENSICS'
Write-Host '[AZTEC-WORLD-HEADER] 3 unique names, cached offsets, source SHA check'
Write-Host '[AZTEC-WORLD-HEADER] No FF emit, no .NET build, no Xbox donor.'
try{
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('wawAzHdr_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    if($temp.Length -gt 110){throw 'OAT/CLI requires short physical TEMP path.'}
    New-Item -Path $temp -ItemType Directory -Force | Out-Null
    $zoneFile=Join-Path $temp 'original_pc_map.zone'
    $previous=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & dotnet 'run' '--no-build' '-c' 'Release' '--project' $cli '--' 'decompress' $pc $zoneFile 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $log -Encoding UTF8
        $exit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$previous}
    if($exit -ne 0 -or -not (Test-Path -LiteralPath $zoneFile -PathType Leaf)){
        throw "Original-PC map.ff decompressor failed exit=$exit. See $log"
    }
    $bytes=[IO.File]::ReadAllBytes($zoneFile)
    $sha=(Get-FileHash -LiteralPath $zoneFile -Algorithm SHA256).Hash
    if([long]$bytes.Length -ne [long]$anchors.decompressedPCZoneBytes -or
       $sha -ine [string]$anchors.decompressedPCZoneSha256){
        throw 'Original PC raw zone bytes differ from the previous PASS, refusing source forensics.'
    }
    $ptrFollowing=[uint32]::MaxValue
    $ptrInsert=[uint32]($ptrFollowing-1)
    $lines.Add("Aztec verified original PC zone: bytes=$($bytes.Length), sha256=$sha")
    foreach($row in @($anchors.worldRootObservations)){
        $family=[string]$row.family
        if(-not $expected.ContainsKey($family)){throw "Unknown world family $family"}
        $spec=$expected[$family]
        $name=[string]$row.exactNativeName
        $nativeIndex=[int]$row.nativeRoot
        if($nativeIndex -ne [int]$spec.root -or
           [int]$row.matchingNameStringOccurrences -ne 1 -or
           @($row.observedNameByteOffsets).Count -ne 1){
            throw "$family requires an exact unique original-PC world-name offset and expected root index."
        }
        $sourcePosition=[int]$row.observedNameByteOffsets[0]
        $needle=[Text.Encoding]::ASCII.GetBytes($name+[char]0)
        if($sourcePosition -lt 4096 -or $sourcePosition -gt ($bytes.Length - $needle.Length)){
            throw "World $family name is out of safe source bounds."
        }
        for($i=0;$i -lt $needle.Length;$i++){
            if($bytes[$sourcePosition+$i] -ne $needle[$i]){
                throw "$family original PC world name does not match previously pinned byte offset."
            }
        }
        $fixed=[int]$spec.bytes
        $predicted=$sourcePosition-$fixed
        $near=New-Object 'System.Collections.Generic.List[object]'
        # A candidate with a FOLLOWING/INSERT first word is only a *pointer-
        # shaped* header. We never promote a raw byte slice into a real world.
        for($dist=4;$dist -le 4096;$dist++){
            $start=$sourcePosition-$dist
            $v=U32 $bytes $start
            if($v -ne $ptrFollowing -and $v -ne $ptrInsert){continue}
            $words=@(ReadWordSummary $bytes $start 12)
            $near.Add([pscustomobject]@{
                startOffset=$start
                distanceToName=$dist
                expectedPCFixedBytes=$fixed
                signedHeaderGap=($dist-$fixed)
                offsetMod4=($start % 4)
                namePointerHex=('0x{0:X8}' -f $v)
                first48BytesHex=(HexBytes $bytes $start 48)
                first12Words=$words
                actualRootBodyVerified=$false
            })
        }
        $best=@($near.ToArray() | Sort-Object -Property @{Expression={[Math]::Abs([int]$_.signedHeaderGap)}},@{Expression={$_.distanceToName}} | Select-Object -First 16)
        $nearExpected=@($near.ToArray() | Where-Object {[Math]::Abs([int]$_.signedHeaderGap) -le 32})
        $adjacent=@($near.ToArray() | Where-Object {[int]$_.signedHeaderGap -eq 0})
        $windowStart=[Math]::Max(0,$sourcePosition-256)
        $nearHex=HexBytes $bytes $windowStart ($sourcePosition-$windowStart)
        $head=@(ReadWordSummary $bytes $predicted 12)
        $item=[pscustomobject]@{
            family=$family
            nativeRootIndex=$nativeIndex
            originalPCName=$name
            originalPCNameByteOffset=$sourcePosition
            sourcePCFixedHeaderSizeHypothesis=$fixed
            hypothesizedFixedHeaderOffset=$predicted
            hypothesizedHeaderOffsetMod4=($predicted % 4)
            hypothesizedHeaderFirst48BytesHex=(HexBytes $bytes $predicted 48)
            hypothesizedHeaderFirst12Words=$head
            bytesImmediatelyBeforeNameHex=$nearHex
            pointerMarkersWithin4KiBBeforeName=$near.Count
            exactAdjacentSizePointerMarkers=$adjacent.Count
            markersWithin32BytesOfHypothesizedHeader=$nearExpected.Count
            nearestSixteenPointerCandidates=$best
            recoveredPcWorldBody=$false
            recoveredNestedChildren=$false
            xboxConversionReady=$false
        }
        $rows.Add($item)
        Write-Host "[AZTEC-WORLD-HEADER] $family root=$nativeIndex source-name=0x$('{0:X8}' -f $sourcePosition) fixed=$fixed adjacent=$($adjacent.Count) near32=$($nearExpected.Count) markers4KiB=$($near.Count)"
        foreach($c in @($best | Select-Object -First 3)){
            Write-Host "[AZTEC-WORLD-HEADER]   marker=0x$('{0:X8}' -f $c.startOffset), name-gap=$($c.signedHeaderGap), alignment=$($c.offsetMod4), words=$($c.first12Words[0..3] -join ' ')"
        }
        $lines.Add("WORLD $family nativeRoot=$nativeIndex name='$name' sourceOffset=0x$('{0:X8}' -f $sourcePosition)")
        $lines.Add("  hypothetical header fixed=$fixed at offset=0x$('{0:X8}' -f $predicted) alignment=$($predicted%4), exact-adjacent markers=$($adjacent.Count), near32 markers=$($nearExpected.Count), 4KiB markers=$($near.Count)")
        $lines.Add("  hypothetically preceding header words: $($head -join ' ')")
        $lines.Add("  immediate 256-byte prefix HEX: $nearHex")
        foreach($c in @($best | Select-Object -First 10)){
            $lines.Add("  POINTER CANDIDATE offset=0x$('{0:X8}' -f $c.startOffset) gap=$($c.signedHeaderGap) mod4=$($c.offsetMod4), first12= $($c.first12Words -join ' ')")
        }
    }
    if($rows.Count -ne 3){throw "Expected exactly 3 source world forensic rows, found $($rows.Count)"}
    $status='AZTEC_PC_WORLD_HEADER_FORENSICS_COMPLETE'
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-WORLD-HEADER] BLOCKER: $blocker"
    if(Test-Path -LiteralPath $log -PathType Leaf){
        foreach($line in @(Get-Content -LiteralPath $log -Tail 6)){
            Write-Host "[AZTEC-WORLD-HEADER] decompress: $line"
        }
    }
}finally{
    if(-not [string]::IsNullOrWhiteSpace($temp) -and
       (Test-Path -LiteralPath $temp -PathType Container)){
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
$report=[ordered]@{
    schema='WaW360.AztecPcWorldHeaderForensics.v1'
    status=$status
    sourcePCMapFastFile=$pc
    sourcePCMapFastFileSha256=[string]$roles[0].sourceSha256
    originalPCZoneSha256=[string]$anchors.decompressedPCZoneSha256
    earlierNameAnchorReport=$anchorFile
    inspectedWorldRoots=$rows.ToArray()
    blocker=$blocker
    actualSourceWorldBodiesDecoded=0
    originalPcNestedWorldDependenciesRecovered=0
    safeToSerializeToXbox=$false
    readyForXbox=$false
    note='Only candidate pointer/header observations near a unique original PC asset name. Size hypotheses do not prove fixed size, pointer ownership, nested source data, zone block traversal, or Xbox serialization. Existing five-family Xbox FF remains untouched.'
}
$report | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $reportFile -Encoding UTF8
$lines.Add("status=$status")
$lines.Add("blocker=$blocker")
$lines.Add('CANDIDATE FORENSICS ONLY. No actual world bodies recovered. No new FF. NOT FOR XBOX.')
[IO.File]::WriteAllLines($textFile,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-WORLD-HEADER] ================= RESULT ========================'
Write-Host "[AZTEC-WORLD-HEADER] status=$status"
Write-Host "[AZTEC-WORLD-HEADER] report=$textFile"
Write-Host '[AZTEC-WORLD-HEADER] NO BUILD / NO FF / NO XBOX.'
if($status -ne 'AZTEC_PC_WORLD_HEADER_FORENSICS_COMPLETE'){exit 4}
