[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$sourcePath=Join-Path $base 'tonight_critical_source_census\aztec_tonight_critical_source_census.json'
$huntPath=Join-Path $base 'pc_gameworldsp_path_source_hunt\aztec_pc_gameworldsp_header_hunt.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$work=Join-Path $base 'pc_gameworldsp_node_source_proof'
$log=Join-Path $work 'original_pc_decompress.log'
$json=Join-Path $work 'aztec_pc_gameworldsp_node_array_proof.json'
$txt=Join-Path $work 'aztec_pc_gameworldsp_node_array_proof.txt'
foreach($p in @($sourcePath,$huntPath,$cli)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec pathnode source proof requires existing prerequisite: $p"
    }
}
$source=Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json
$hunt=Get-Content -LiteralPath $huntPath -Raw | ConvertFrom-Json
if([string]$source.status -ne 'AZTEC_TONIGHT_CRITICAL_PC_SOURCE_IDENTITY_CENSUS_PASS' -or
   [int]$source.nativeRoots -ne 8509 -or
   [string]$hunt.Schema -ne 'WaW360.AztecPcGameWorldSpHeaderHunt.v1' -or
   [string]$hunt.Status -ne 'AZTEC_PC_GAMEWORLDSP_HEADER_HUNT_COMPLETE' -or
   [long]$hunt.OriginalPcZoneBytes -ne 113701260L -or
   [bool]$hunt.ActualGameWorldSpSourceRootLocated -or
   [bool]$hunt.ReadyForXbox){
    throw 'GameWorldSP hunt or original PC source provenance changed.'
}
$pcRole=@($source.pcSourceRoles | Where-Object {[string]$_.role -eq 'map.ff'})
if($pcRole.Count -ne 1){throw 'Require exactly one SHA-pinned original-PC map.ff role.'}
$pc=[string]$pcRole[0].sourceFastfile
if(-not (Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$pcRole[0].sourceSha256 -or
   [string]$hunt.OriginalPcFastFileSha256 -ine [string]$pcRole[0].sourceSha256){
    throw 'Original-PC Aztec map.ff SHA-256 differs from previous passed source scan.'
}
$roots=@($hunt.PcWorldAssetRoots | Where-Object {
    ([string]$_.PcTypeName).Replace('_','').ToLowerInvariant() -eq 'gameworldsp'
})
if($roots.Count -ne 1 -or [string]$roots[0].RawHeader -ne '0xFFFFFFFF'){
    throw 'Actual PC GameWorldSP XAsset must remain a unique InlineShared root.'
}
$candidate=@($hunt.TopCandidates | Where-Object {[string]$_.OffsetHex -eq '0x05B17945'})
if($candidate.Count -ne 1 -or
   [uint32]$candidate[0].NodeCount -ne 7630 -or
   [uint32]$candidate[0].NodeTreeCount -ne 3619 -or
   [uint32]$candidate[0].VisBytes -ne 7276159 -or
   [string]$candidate[0].NamePtr -ine '0x84702AC9' -or
   [string]$candidate[0].Fixed44ByteSha256 -ine 'F4C9B7B7A1128B7BED87C23352A8FB3795ADEAC99C54595543B5A70A2CA550AA'){
    throw 'Unique high-value PC GameWorldSP 7630-node candidate changed. Refusing ambiguous scan.'
}
function ShaSpan([byte[]]$bytes,[int]$start,[int]$count){
    $sha=[Security.Cryptography.SHA256]::Create()
    try{return [BitConverter]::ToString($sha.ComputeHash($bytes,$start,$count)).Replace('-','')}
    finally{$sha.Dispose()}
}
function StatNode([byte[]]$b,[int]$o){
    $type=[BitConverter]::ToInt32($b,$o)
    $x=[BitConverter]::ToSingle($b,$o+20)
    $y=[BitConverter]::ToSingle($b,$o+24)
    $z=[BitConverter]::ToSingle($b,$o+28)
    $radius=[BitConverter]::ToSingle($b,$o+44)
    $links=[BitConverter]::ToUInt16($b,$o+62)
    $ptr=[BitConverter]::ToUInt32($b,$o+64)
    $finite= -not ([single]::IsNaN($x) -or [single]::IsInfinity($x) -or
       [single]::IsNaN($y) -or [single]::IsInfinity($y) -or
       [single]::IsNaN($z) -or [single]::IsInfinity($z) -or
       [single]::IsNaN($radius) -or [single]::IsInfinity($radius))
    $xyzOk=$finite -and [Math]::Abs([double]$x) -le 1000000.0 -and
       [Math]::Abs([double]$y) -le 1000000.0 -and
       [Math]::Abs([double]$z) -le 1000000.0
    $radiusOk=$finite -and $radius -ge 0 -and $radius -le 1000000
    $linksOk=$links -le 1024 -and
       ($ptr -eq 0 -or $ptr -eq [uint32]::MaxValue -or $ptr -eq [uint32]([uint32]::MaxValue-1))
    return [pscustomobject]@{
        sourceOffset=$o
        type=$type
        typeValid=($type -ge 0 -and $type -le 20)
        x=$x;y=$y;z=$z
        xyzPlausible=$xyzOk
        radius=$radius;radiusPlausible=$radiusOk
        totalLinkCount=$links
        linksPointerHex=('0x{0:X8}' -f $ptr)
        linksPointerPlausible=$linksOk
    }
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
$status='AZTEC_PC_GAMEWORLDSP_NODE_SOURCE_SCAN_BLOCKED'
$blocker=''
$temp=''
$summaries=New-Object 'System.Collections.Generic.List[object]'
$best=$null
Write-Host '[AZTEC-PC-NODES] ======================================================'
Write-Host '[AZTEC-PC-NODES] TARGETED 7630 SOURCE PATHNODE CHECK AT 0x05B17945'
Write-Host '[AZTEC-PC-NODES] Exact PC source hash + 44-byte header SHA pinned.'
Write-Host '[AZTEC-PC-NODES] NO BUILD / NO FF / NO XBOX.'
try{
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('wawAzPN_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    if($temp.Length -gt 110){throw 'Need short physical TEMP path.'}
    New-Item -Path $temp -ItemType Directory -Force | Out-Null
    $zone=Join-Path $temp 'original_pc_map.zone'
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & dotnet 'run' '--no-build' '-c' 'Release' '--project' $cli '--' 'decompress' $pc $zone 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $log -Encoding UTF8
        $code=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    if($code -ne 0 -or -not (Test-Path -LiteralPath $zone -PathType Leaf)){
        throw "PC zone decompress failed, exit=$code; $log"
    }
    $z=[IO.File]::ReadAllBytes($zone)
    $zoneHash=(Get-FileHash -LiteralPath $zone -Algorithm SHA256).Hash
    if([long]$z.Length -ne 113701260L -or
       $zoneHash -ine [string]$hunt.OriginalPcZoneSha256){
        throw 'Original-PC 113701260-byte zone SHA-256 changed.'
    }
    $header=0x05B17945
    if((ShaSpan $z $header 44) -ine [string]$candidate[0].Fixed44ByteSha256){
        throw 'Original PC 44-byte candidate SHA-256 drifted.'
    }
    $nodeCount=[int]$candidate[0].NodeCount
    $nodeFixedBytes=$nodeCount*128
    $afterHeader=$header+44
    for($pad=0;$pad -le 16;$pad++){
        $nodeStart=$afterHeader+$pad
        if($nodeStart -lt 0 -or $nodeStart -gt $z.Length-$nodeFixedBytes){continue}
        $validTypes=0;$validXYZ=0;$validRadius=0;$validLinks=0;$nonzeroTypes=0;$nonzeroPos=0
        $preview=New-Object 'System.Collections.Generic.List[object]'
        for($i=0;$i -lt $nodeCount;$i++){
            $node=StatNode $z ($nodeStart+$i*128)
            if($node.typeValid){$validTypes++}
            if($node.xyzPlausible){$validXYZ++}
            if($node.radiusPlausible){$validRadius++}
            if($node.linksPointerPlausible){$validLinks++}
            if($node.type -ne 0 -and $node.typeValid){$nonzeroTypes++}
            if($node.xyzPlausible -and
               ([Math]::Abs([double]$node.x)+[Math]::Abs([double]$node.y)+[Math]::Abs([double]$node.z)) -gt 0.001){$nonzeroPos++}
            if($i -lt 6){$preview.Add($node)}
        }
        $score=$validTypes+$validXYZ+$validRadius+$validLinks
        $record=[pscustomobject]@{
            sourceNodeArrayOffset=$nodeStart
            padAfterHeader=$pad
            pathNodeCount=$nodeCount
            hypothesizedPcPathNodeBytes=128
            sourceBytes=$nodeFixedBytes
            typeCountValid=$validTypes
            xyzCountPlausible=$validXYZ
            radiusCountPlausible=$validRadius
            linksPointerCountPlausible=$validLinks
            nonzeroRecognizedTypes=$nonzeroTypes
            nonzeroPlausiblePositions=$nonzeroPos
            score=$score
            maxScore=($nodeCount*4)
            sourceFixedNodeArraySha256=(ShaSpan $z $nodeStart $nodeFixedBytes)
            firstSixNodes=$preview.ToArray()
            actualSourceOwnershipProven=$false
        }
        $summaries.Add($record)
    }
    $ranked=@($summaries.ToArray() | Sort-Object -Property @{Expression='score';Descending=$true},@{Expression='padAfterHeader'})
    if($ranked.Count -eq 0){throw 'No bounded pathnode-array samples found.'}
    $best=$ranked[0]
    Write-Host "[AZTEC-PC-NODES] GameWorldSP candidate=0x05B17945: 7630 nodes, 3619 trees, 7276159 visibility bytes"
    foreach($r in @($ranked | Select-Object -First 5)){
        Write-Host "[AZTEC-PC-NODES] pad=$($r.padAfterHeader), nodeStart=0x$('{0:X8}' -f $r.sourceNodeArrayOffset), types=$($r.typeCountValid)/7630, XYZ=$($r.xyzCountPlausible)/7630, radius=$($r.radiusCountPlausible)/7630, linkPtrs=$($r.linksPointerCountPlausible)/7630, nonzeroPositions=$($r.nonzeroPlausiblePositions), score=$($r.score)/30520"
        foreach($first in @($r.firstSixNodes | Select-Object -First 2)){
            Write-Host "[AZTEC-PC-NODES]   node type=$($first.type) xyz=($($first.x),$($first.y),$($first.z)) links=$($first.totalLinkCount) ptr=$($first.linksPointerHex)"
        }
    }
    # Significantly different byte distributions across all 7630 records
    # help distinguish genuine fixed structs from accidental pointer words.
    # Do not auto-promote the array to a converted world body.
    if([int]$best.typeCountValid -ge 7000 -and
       [int]$best.xyzCountPlausible -ge 7000 -and
       [int]$best.radiusCountPlausible -ge 7000 -and
       [int]$best.linksPointerCountPlausible -ge 7000 -and
       [int]$best.nonzeroPlausiblePositions -ge 500){
        $status='AZTEC_PC_GAMEWORLDSP_7630_NODE_SOURCE_EVIDENCE_PASS'
    }else{
        $status='AZTEC_PC_GAMEWORLDSP_NODE_LAYOUT_INCONCLUSIVE'
    }
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-PC-NODES] BLOCKER: $blocker"
    if(Test-Path -LiteralPath $log -PathType Leaf){
        Get-Content -LiteralPath $log -Tail 5 | ForEach-Object {Write-Host "[AZTEC-PC-NODES] decompress: $_"}
    }
}finally{
    if(-not [string]::IsNullOrWhiteSpace($temp) -and (Test-Path -LiteralPath $temp -PathType Container)){
        Remove-Item -LiteralPath $temp -Force -Recurse
    }
}
$report=[ordered]@{
    schema='WaW360.AztecPcGameWorldSpNodeArrayEvidence.v1'
    status=$status
    sourcePcFastFile=$pc
    originalPcFastFileSha256=[string]$pcRole[0].sourceSha256
    decompressedZoneSha256=[string]$hunt.OriginalPcZoneSha256
    targetHeaderOffset=0x05B17945
    targetHeaderSha256=[string]$candidate[0].Fixed44ByteSha256
    candidatesRanked=@($summaries.ToArray() | Sort-Object -Property @{Expression='score';Descending=$true},@{Expression='padAfterHeader'})
    blocker=$blocker
    directRootHeaderOwnershipProven=$false
    pathNodeChildGraphsDecoded=$false
    xboxGameWorldSpBuilt=$false
    hardwareTestAuthorized=$false
    note='Source-owned PC GameWorldSP 44-byte candidate and 128-byte node array pattern evidence only. 7630 node types/positions are not proof of nested link ownership. No FF generated; no Xbox test candidate.'
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $json -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add("Aztec targeted 7630 pathnode array evidence: $status")
$lines.Add("Source zone SHA=$($hunt.OriginalPcZoneSha256)")
foreach($r in @($report.candidatesRanked | Select-Object -First 5)){
    $lines.Add("pad=$($r.padAfterHeader) start=0x$('{0:X8}' -f $r.sourceNodeArrayOffset) types=$($r.typeCountValid)/7630 XYZ=$($r.xyzCountPlausible)/7630 radius=$($r.radiusCountPlausible)/7630 linkPtrs=$($r.linksPointerCountPlausible)/7630 nonzeroPositions=$($r.nonzeroPlausiblePositions), sourceSha=$($r.sourceFixedNodeArraySha256)")
}
$lines.Add("blocker=$blocker")
$lines.Add('Candidate only. NO WORLD BODY CONVERTED. NOT FOR XBOX.')
[IO.File]::WriteAllLines($txt,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-NODES] ================= RESULT ============================='
Write-Host "[AZTEC-PC-NODES] status=$status"
Write-Host "[AZTEC-PC-NODES] report=$txt"
Write-Host '[AZTEC-PC-NODES] NO BUILD / NO FF / NOT FOR XBOX.'
if($status -eq 'AZTEC_PC_GAMEWORLDSP_NODE_SOURCE_SCAN_BLOCKED'){exit 4}
