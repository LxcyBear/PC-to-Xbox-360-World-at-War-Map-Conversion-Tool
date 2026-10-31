[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# NO rebuild, NO native OAT call, NO FF modification.
# Inspect ALREADY FAILED Gate11 scratch FF and its original-PC source graph.
# Independently decompress the existing file, locate the 11*56-byte Xenon
# cell header array, and check its serialized AABB/ushort-index byte sequence.
# This detects an incorrect cell-child stream order before another costly build.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$work=Join-Path $base 'pc_g11_cells_740'
$ff=Join-Path $work 'scratch_emit\nazi_zombie_aztec.ff'
$sourceManifest=Join-Path $base 'pc_g10_1903\gate11_cell_topology_source\aztec_gate11_pc_source_cells_native_abi.json'
$failedManifest=Join-Path $work 'aztec_pc_world_core_integrated_oat_run.json'
$oatLog=Join-Path $work 'scratch_emit\oat_hard_gate\nazi_zombie_aztec_oat_xenon_list.log'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$report=Join-Path $work 'gate11_failed_ff_pointer_layout_audit.json'
foreach($p in @($ff,$sourceManifest,$failedManifest,$oatLog,$cli)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Missing existing failed Gate11 FF/proof: $p"
    }
}
$m=Get-Content -LiteralPath $failedManifest -Raw|ConvertFrom-Json
$source=Get-Content -LiteralPath $sourceManifest -Raw|ConvertFrom-Json
if([string]$m.status -ne 'AZTEC_GATE11_11_CELL_740_AABB_DIAGNOSTIC_OAT_LOAD_BLOCKED' -or
   [int]$m.gate11SourceCellsInFf -ne 11 -or
   [int]$m.gate11AabbTreesInFf -ne 740 -or
   [string]$source.status -ne 'AZTEC_GATE11_11_CELL_740_AABB_TYPED_SOURCE_NATIVE_ABI_PASS_NOT_EMITTED' -or
   -not [bool]$source.typedPcCellGraphSourcePass -or
   [bool]$m.hardwareReady){
    throw 'Do not diagnose an unpinned or hardware-approved build as the known Gate11 failed scratch FF.'
}
$typedFile=[string]$source.originalPcTypedCellAabbGraph
if(-not(Test-Path -LiteralPath $typedFile -PathType Leaf) -or
   (Get-FileHash -LiteralPath $typedFile -Algorithm SHA256).Hash -ine
       [string]$source.originalPcTypedCellAabbGraphSha256){
    throw 'Original-PC source-owned 11-cell typed graph is missing or SHA drifted.'
}
$pc=Get-Content -LiteralPath $typedFile -Raw|ConvertFrom-Json
$original=@($pc.cells)
if($original.Count -ne 11){throw 'Expected 11 original-PC cells.'}
$log=Get-Content -LiteralPath $oatLog -Raw
$match=[regex]::Match($log,'Zone referenced offset (?<n>\d+) of block (?<block>[A-Za-z0-9_:]+) which is larger than its size (?<size>\d+)')
if(-not $match.Success){throw 'The expected OAT out-of-block pointer message is missing or has changed.'}
$bad=[uint32]::Parse($match.Groups['n'].Value)
$maxBlock=[uint32]::Parse($match.Groups['size'].Value)
$badRaw=[uint32]($bad+1)
Write-Host '[AZTEC-GATE11-FORENSIC] ========================================================'
Write-Host "[AZTEC-GATE11-FORENSIC] Existing OAT rejection: block=$($match.Groups['block'].Value) decodedOffset=$bad (raw candidate=0x$($badRaw.ToString('X8'))) blockBytes=$maxBlock"
Write-Host '[AZTEC-GATE11-FORENSIC] Reusing FAILED FF, SHA-authenticated original PC cell graph.'
$ffHash=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('waw_g11failed_'+[Guid]::NewGuid().ToString('N')+'.zone')
try{
    Write-Host '[AZTEC-GATE11-FORENSIC] Decompressing existing FF only (no build, no OAT)'
    $save=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & dotnet run --no-build -c Release --project $cli -- decompress $ff $temporary 2>&1 |
            ForEach-Object {Write-Host "[AZTEC-GATE11-FORENSIC] DECOMP $_"}
        $exitCode=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$save}
    if($exitCode -ne 0 -or -not(Test-Path -LiteralPath $temporary -PathType Leaf)){
        throw "Existing .NET CLI decompressor failed (exit=$exitCode). The FF was NOT modified."
    }
    $raw=[IO.File]::ReadAllBytes($temporary)
    if($raw.Length -lt 900 -or $raw.Length -gt 100000000){
        throw 'Unexpected decompressed diagnostic zone byte length.'
    }
    if(-not ('WaWG11ByteSearch' -as [type])){
        Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
public static class WaWG11ByteSearch
{
    public static int[] Find(byte[] data,byte[] pattern,int limit)
    {
        var hits=new List<int>();
        for(int i=0;i<=data.Length-pattern.Length;i++)
        {
            if(data[i]!=pattern[0]) continue;
            int j=1;
            for(;j<pattern.Length;j++)
                if(data[i+j]!=pattern[j]) break;
            if(j!=pattern.Length)continue;
            hits.Add(i);
            if(hits.Count==limit)break;
        }
        return hits.ToArray();
    }
    public static uint Big32(byte[] d,int i)=>
        ((uint)d[i]<<24)|((uint)d[i+1]<<16)|((uint)d[i+2]<<8)|d[i+3];
    public static ushort Big16(byte[] d,int i)=>
        (ushort)((d[i]<<8)|d[i+1]);
    public static byte[] FloatBe(float value)
    {
        var b=BitConverter.GetBytes(value);
        if(BitConverter.IsLittleEndian)Array.Reverse(b);
        return b;
    }
}
'@
    }
    $signature=New-Object byte[] 24
    for($i=0;$i -lt 3;$i++){
        $a=[WaWG11ByteSearch]::FloatBe([single]($original[0].mins[$i]))
        $b=[WaWG11ByteSearch]::FloatBe([single]($original[0].maxs[$i]))
        [Array]::Copy($a,0,$signature,$i*4,4)
        [Array]::Copy($b,0,$signature,12+$i*4,4)
    }
    $cellPositions=@([WaWG11ByteSearch]::Find($raw,$signature,50))
    $ptrBytes=[byte[]]@(
        [byte](($badRaw -shr 24) -band 255),
        [byte](($badRaw -shr 16) -band 255),
        [byte](($badRaw -shr 8) -band 255),
        [byte]($badRaw -band 255)
    )
    $rawPointerHits=@([WaWG11ByteSearch]::Find($raw,$ptrBytes,30))
    $candidateDetails=New-Object 'System.Collections.Generic.List[object]'
    foreach($pos in $cellPositions){
        if(($pos + 616) -gt $raw.Length) {
            continue
        }
        $matches=0;$badCell=-1
        for($ci=0;$ci -lt 11;$ci++){
            $at=$pos+56*$ci
            if([WaWG11ByteSearch]::Big32($raw,$at+24) -ne [uint32]$original[$ci].aabbTreeCount){$badCell=$ci;break}
            $expectedPtr=if([int]$original[$ci].aabbTreeCount){[uint32]::MaxValue}else{[uint32]0}
            if([WaWG11ByteSearch]::Big32($raw,$at+28) -ne $expectedPtr){$badCell=$ci;break}
            if([int]$raw[$at+48] -ne [int]$original[$ci].reflectionProbeCount){$badCell=$ci;break}
            $matches++
        }
        $candidateDetails.Add([ordered]@{arrayStreamOffset=$pos;matchedCellHeaders=$matches;firstFailedCell=$badCell})
    }
    $best=@($candidateDetails.ToArray()|Where-Object {$_.matchedCellHeaders -eq 11}|Select-Object -First 1)
    $cursor=0
    $indexesMatched=0;$treeHeadersMatched=0;$probeIndexesMatched=0
    $firstMismatch=''
    if($best.Count -eq 1){
        $cursor=[int]$best[0].arrayStreamOffset+616
        :outer foreach($cell in $original){
            $trees=@($cell.aabbTrees)
            $bytesNeeded=$trees.Count*40
            if($cursor+$bytesNeeded -gt $raw.Length){$firstMismatch="Cell $($cell.ordinal) AABB headers exceed zone";break}
            $arrayAt=$cursor
            for($ti=0;$ti -lt $trees.Count;$ti++){
                $tree=$trees[$ti];$at=$arrayAt+$ti*40
                if([WaWG11ByteSearch]::Big16($raw,$at+30) -ne [uint16]$tree.smodelIndexCount -or
                   [WaWG11ByteSearch]::Big16($raw,$at+26) -ne [uint16]$tree.surfaceCount -or
                   [WaWG11ByteSearch]::Big16($raw,$at+28) -ne [uint16]$tree.startSurfIndex){
                    $firstMismatch="Cell $($cell.ordinal) AABB#$ti header mismatch at stream offset $at"
                    break outer
                }
                $want=if([int]$tree.smodelIndexCount){[uint32]::MaxValue}else{[uint32]0}
                if([WaWG11ByteSearch]::Big32($raw,$at+32) -ne $want){
                    $firstMismatch="Cell $($cell.ordinal) AABB#$ti pointer mismatch at stream offset $($at+32)"
                    break outer
                }
                $treeHeadersMatched++
            }
            $cursor+=$bytesNeeded
            foreach($tree in $trees){
                foreach($id in @($tree.smodelIndexes)){
                    if($cursor+2 -gt $raw.Length -or
                       [WaWG11ByteSearch]::Big16($raw,$cursor) -ne [uint16]$id){
                        $firstMismatch="Cell $($cell.ordinal) AABB#$($tree.ordinal) ushort index mismatch at stream offset $cursor"
                        break outer
                    }
                    $indexesMatched++
                    $cursor+=2
                }
            }
            foreach($id in @($cell.reflectionProbeIndices)){
                if($cursor -ge $raw.Length -or $raw[$cursor] -ne [byte]$id){
                    $firstMismatch="Cell $($cell.ordinal) reflection index mismatch at offset $cursor"
                    break outer
                }
                $probeIndexesMatched++
                $cursor++
            }
        }
    }else{
        $firstMismatch='Could not locate 11 consecutive source-owned 56B cell records after decompression.'
    }
    $result=[ordered]@{
        schema='WaW360.AztecGate11FailedPointerOfflineZoneForensic.v1'
        status='AZTEC_GATE11_FAILED_FF_OFFLINE_POINTER_LAYOUT_AUDIT_NOT_OAT'
        priorGate10FfUnchanged=$true
        failedDiagnosticFf=$ff
        failedDiagnosticFfSha256=$ffHash
        decompressedZoneBytes=$raw.Length
        observedOatErrorDecodedBlockOffset=$bad
        observedOatBlockSize=$maxBlock
        possiblePackedPointerRaw=('0x{0:X8}' -f $badRaw)
        pointerWordOccurrencesInRawZone=$rawPointerHits
        firstCellSignatureOccurrences=$cellPositions
        cellArrayCandidates=@($candidateDetails.ToArray())
        matchedAabbTreeHeaders=$treeHeadersMatched
        matchedStaticModelIndexEntries=$indexesMatched
        matchedReflectionProbeIndexEntries=$probeIndexesMatched
        firstSourceVsEmittedLayoutMismatch=$firstMismatch
        lastWalkedZoneOffset=$cursor
        diagnosticOnly=$true
        hardwareReady=$false
    }
    $result|ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $report -Encoding UTF8
    Write-Host "[AZTEC-GATE11-FORENSIC] zoneBytes=$($raw.Length), matchedCellArrays=$($best.Count), physicalErrorPackedWordHits=$($rawPointerHits.Count)"
    Write-Host "[AZTEC-GATE11-FORENSIC] Typed emitted byte stream: trees=$treeHeadersMatched/740, smodelIndexes=$indexesMatched/14974, probes=$probeIndexesMatched/11"
    Write-Host "[AZTEC-GATE11-FORENSIC] First mismatch=$firstMismatch"
    Write-Host "[AZTEC-GATE11-FORENSIC] Report=$report"
    Write-Host '[AZTEC-GATE11-FORENSIC] No new FF/OAT build, no output FF modification, no Xbox test; Gate11 still PENDING.'
}finally{
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
}
