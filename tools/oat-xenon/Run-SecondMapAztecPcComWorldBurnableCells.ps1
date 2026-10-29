[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$proofPath=Join-Path $base 'pc_comworld_source_decode\aztec_pc_comworld_source_decode.json'
$censusPath=Join-Path $base 'tonight_critical_source_census\aztec_tonight_critical_source_census.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$work=Join-Path $base 'pc_comworld_burnable_source'
$log=Join-Path $work 'original_pc_decompress.log'
$outJson=Join-Path $work 'aztec_pc_comworld_burnable_source.json'
$outTxt=Join-Path $work 'aztec_pc_comworld_burnable_source.txt'
foreach($file in @($proofPath,$censusPath,$cli)){
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)){throw "ComWorld burnable source prerequisite missing: $file"}
}
$proof=Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json
$census=Get-Content -LiteralPath $censusPath -Raw | ConvertFrom-Json
if([string]$proof.schema -ne 'WaW360.AztecOriginalPcComWorldSourceDecode.v1' -or
   [string]$proof.status -notin @('AZTEC_PC_COMWORLD_PARENT_AND_LIGHTS_CANDIDATE_PASS',
                                  'AZTEC_PC_COMWORLD_PARENT_ABI_PASS_CHILD_LAYOUT_UNRESOLVED') -or
   [int]$proof.comWorld.nativeRootIndex -ne 7178 -or
   [int]$proof.comWorld.sourceHeaderSize -ne 64 -or
   [long]$proof.comWorld.fieldValues.primaryLightCount -ne 2 -or
   [long]$proof.comWorld.fieldValues.numBurnableCells -ne 88 -or
   [int]$proof.bestPrimaryLightsScore -ne 24 -or
   [string]$census.status -ne 'AZTEC_TONIGHT_CRITICAL_PC_SOURCE_IDENTITY_CENSUS_PASS' -or
   [int]$census.nativeRoots -ne 8509 -or [bool]$proof.fullWorldSourceProven){
    throw 'Previous original-PC ComWorld 64-byte/2-light candidate proof changed or is incomplete.'
}
$roles=@($census.pcSourceRoles | Where-Object {[string]$_.role -eq 'map.ff'})
if($roles.Count -ne 1 -or
   [string]$roles[0].sourceSha256 -ine [string]$proof.sourceMapFastFileSha256 -or
   [string]$roles[0].sourceFastfile -ine [string]$proof.sourceMapFastFile){
    throw 'Original PC map.ff ownership or SHA manifest disagrees with ComWorld source proof.'
}
$pc=[string]$roles[0].sourceFastfile
if(-not (Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$roles[0].sourceSha256){
    throw 'Original PC map.ff SHA no longer matches earlier verified conversion input.'
}
$lights=@($proof.comWorld.topLightArrayCandidates | Where-Object {
    [int]$_.candidateScore -eq 24 -and [int]$_.paddingAfterName -eq 0
})
if($lights.Count -ne 1){throw 'Require unique 24/24 contiguous 144-byte ComPrimaryLight candidate.'}
function U32([byte[]]$bytes,[int]$o){
    if($o -lt 0 -or $o -gt $bytes.Length-4){throw 'U32 out of range'}
    return [BitConverter]::ToUInt32($bytes,$o)
}
function I32([byte[]]$bytes,[int]$o){
    if($o -lt 0 -or $o -gt $bytes.Length-4){throw 'I32 out of range'}
    return [BitConverter]::ToInt32($bytes,$o)
}
function Sha([byte[]]$bytes,[int]$o,[int]$count){
    if($o -lt 0 -or $count -lt 0 -or $o -gt $bytes.Length-$count){throw 'Hash range invalid'}
    $h=[Security.Cryptography.SHA256]::Create()
    try{return [BitConverter]::ToString($h.ComputeHash($bytes,$o,$count)).Replace('-','')}
    finally{$h.Dispose()}
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
$temp=''
$status='AZTEC_PC_COMWORLD_BURNABLE_SOURCE_BLOCKED'
$blocker=''
$selected=$null
$observed=@()
$zoneSha=''
Write-Host '[AZTEC-PC-BURNABLE] ======================================================'
Write-Host '[AZTEC-PC-BURNABLE] REAL PC COMWORLD 88 BURNABLE CELL PARENT/CHILD SCAN'
Write-Host '[AZTEC-PC-BURNABLE] No build / no FF / preserve previous five-family PASS.'
try{
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('wawAzBC_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    if($temp.Length -gt 110){throw 'PC zone temp path too long.'}
    New-Item -Path $temp -ItemType Directory -Force | Out-Null
    $zonePath=Join-Path $temp 'source_pc_map.zone'
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & dotnet 'run' '--no-build' '-c' 'Release' '--project' $cli '--' 'decompress' $pc $zonePath 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $log -Encoding UTF8
        $exit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    if($exit -ne 0 -or -not (Test-Path -LiteralPath $zonePath -PathType Leaf)){
        throw "Original PC map.ff decompression failed ($exit), see $log"
    }
    $zoneSha=(Get-FileHash -LiteralPath $zonePath -Algorithm SHA256).Hash
    $bytes=[IO.File]::ReadAllBytes($zonePath)
    if([long]$bytes.Length -ne 113701260L -or
       $zoneSha -ine [string]$proof.sourcePCZoneSha256){
        throw 'Original 113701260-byte PC zone SHA changed since 24/24 ComWorld source probe.'
    }
    $ptrFollowing=[uint32]::MaxValue
    $ptrInsert=[uint32]($ptrFollowing-1)
    $parent=[int]$proof.comWorld.sourceComWorldHeaderOffset
    $parentSha=Sha $bytes $parent 64
    if($parentSha -ine [string]$proof.comWorld.sourceParentSha256 -or
       (U32 $bytes $parent) -ne $ptrFollowing -or
       (I32 $bytes ($parent+4)) -ne 1 -or
       (U32 $bytes ($parent+8)) -ne 2 -or
       (U32 $bytes ($parent+12)) -ne $ptrFollowing -or
       (U32 $bytes ($parent+56)) -ne 88 -or
       (U32 $bytes ($parent+60)) -ne $ptrFollowing){
        throw 'Original-PC 64-byte ComWorld parent no longer proves 2 lights and 88 source-owned burnable cells.'
    }
    $start=[int]$lights[0].primaryLightArrayOffset
    if((Sha $bytes $start 144) -ine [string]$lights[0].lightFixedArraySha256){
        throw 'The original PC 144-byte two-light array SHA changed.'
    }
    $endLights=$start+144
    $candidateRows=New-Object 'System.Collections.Generic.List[object]'
    # Generated T4 loader streams a count-array of 88 ComBurnableCell
    # structs (12 bytes each), then separately traverses 32-byte data
    # buffers per non-null cell pointer. Try bounded insertion/alignment
    # offsets; only accept realistic coordinate/pointer distributions.
    for($pad=0;$pad -le 32;$pad++){
        $begin=$endLights+$pad
        $length=88*12
        if($begin -lt 0 -or $begin -gt $bytes.Length-$length){continue}
        $validCoords=0;$validPtrs=0;$inline=0;$nulls=0
        $unique=New-Object 'System.Collections.Generic.HashSet[string]'
        $preview=New-Object 'System.Collections.Generic.List[object]'
        $coords=New-Object 'System.Collections.Generic.List[object]'
        for($i=0;$i -lt 88;$i++){
            $off=$begin+$i*12
            $x=I32 $bytes $off
            $y=I32 $bytes ($off+4)
            $ptr=U32 $bytes ($off+8)
            $okay=($x -ge -6 -and $x -le 22 -and $y -ge -19 -and $y -le 5)
            if($okay){$validCoords++}
            $okPtr=($ptr -eq 0 -or $ptr -eq $ptrFollowing -or $ptr -eq $ptrInsert)
            if($okPtr){$validPtrs++}
            if($ptr -eq 0){$nulls++}
            if($ptr -eq $ptrFollowing -or $ptr -eq $ptrInsert){$inline++}
            [void]$unique.Add("$x,$y")
            $entry=[pscustomobject]@{index=$i;x=$x;y=$y;pointerHex=('0x{0:X8}' -f $ptr)}
            $coords.Add($entry)
            if($i -lt 8){$preview.Add($entry)}
        }
        $score=$validCoords+$validPtrs+$unique.Count
        $candidateRows.Add([pscustomobject]@{
            serializedCellArrayOffset=$begin
            bytePaddingAfterLights=$pad
            cellRecords=88
            cellRecordBytes=12
            boundsMatchingCoordinates=$validCoords
            recognizedPointers=$validPtrs
            uniqueCoordinates=$unique.Count
            inlinePointers=$inline
            nullPointers=$nulls
            score=$score
            scoreMax=264
            cellArraySha256=(Sha $bytes $begin $length)
            firstEightCells=$preview.ToArray()
            allCellCoordinates=$coords.ToArray()
        })
    }
    $ranked=@($candidateRows.ToArray() | Sort-Object -Property @{Expression='score';Descending=$true},@{Expression='bytePaddingAfterLights';Descending=$false})
    $observed=@($ranked | Select-Object -First 5)
    $selected=$observed[0]
    $topScore=[int]$selected.score
    $strong=([int]$selected.boundsMatchingCoordinates -eq 88 -and
             [int]$selected.recognizedPointers -eq 88 -and
             [int]$selected.uniqueCoordinates -eq 88)
    Write-Host "[AZTEC-PC-BURNABLE] parent=0x$('{0:X8}' -f $parent) 2 lights=144 bytes; burnableCount=88 bounds=[-6,-19,22,5]"
    foreach($item in $observed){
        Write-Host "[AZTEC-PC-BURNABLE] cells offset=0x$('{0:X8}' -f $item.serializedCellArrayOffset) pad=$($item.bytePaddingAfterLights) coordinates=$($item.boundsMatchingCoordinates)/88 ptrs=$($item.recognizedPointers)/88 unique=$($item.uniqueCoordinates) inline=$($item.inlinePointers) score=$($item.score)/264"
        foreach($entry in @($item.firstEightCells | Select-Object -First 3)){
            Write-Host "[AZTEC-PC-BURNABLE]   cell#$($entry.index) x=$($entry.x) y=$($entry.y) ptr=$($entry.pointerHex)"
        }
    }
    if($strong){
        # No assertion about child buffer ownership until T4 serialization
        # confirms 32-byte data for all 88 records in wire order.
        $status='AZTEC_PC_COMWORLD_88_BURNABLE_CELL_HEADERS_PASS'
        Write-Host '[AZTEC-PC-BURNABLE] 88/88 bounded coordinates, pointers, unique positions: HEADER PASS'
    }else{
        $status='AZTEC_PC_COMWORLD_BURNABLE_LAYOUT_UNRESOLVED'
        Write-Host '[AZTEC-PC-BURNABLE] No exact 88/88 source cell header sequence; data remains blocked.'
    }
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-PC-BURNABLE] BLOCKER: $blocker"
    if(Test-Path -LiteralPath $log -PathType Leaf){
        foreach($line in @(Get-Content -LiteralPath $log -Tail 5)){
            Write-Host "[AZTEC-PC-BURNABLE] decompress $line"
        }
    }
}finally{
    if(-not [string]::IsNullOrWhiteSpace($temp) -and (Test-Path -LiteralPath $temp -PathType Container)){
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
$report=[ordered]@{
    schema='WaW360.AztecPcComWorldBurnableSource.v1'
    status=$status
    originalPCMapFastFile=$pc
    originalPCMapSha256=[string]$roleS=[string]$roles[0].sourceSha256
    originalPCZoneSha256=$zoneSha
    comWorldHeaderSha256=[string]$proof.comWorld.sourceParentSha256
    twoLights144ByteSha256=[string]$lights[0].lightFixedArraySha256
    nativeRootIndex=7178
    expectedBurnableCellCount=88
    bestCellArray=$selected
    topCandidates=$observed
    child32ByteSamplesAuthenticated=$false
    exactSourceWorldSerializationProven=$false
    xboxWorldConverted=$false
    hardwareTestAuthorized=$false
    blocker=$blocker
    note='Diagnostic only. 88 PC ComBurnableCell coordinate/pointer records, if present, do not prove child 32-byte samples or full ComWorld nested serialization. The previous 8509-root FF is unchanged and not for Xbox.'
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $outJson -Encoding UTF8
$summary=New-Object 'System.Collections.Generic.List[string]'
$summary.Add("Original-PC Aztec ComWorld burnable cells: $status")
$summary.Add("PC zone SHA256=$zoneSha; root #7178; lights original SHA=$([string]$lights[0].lightFixedArraySha256)")
foreach($c in $observed){
    $summary.Add("candidate offset=0x$('{0:X8}' -f $c.serializedCellArrayOffset) pad=$($c.bytePaddingAfterLights) bounds=$($c.boundsMatchingCoordinates)/88 ptrs=$($c.recognizedPointers)/88 unique=$($c.uniqueCoordinates)/88 score=$($c.score)/264 sha=$($c.cellArraySha256)")
}
$summary.Add("blocker=$blocker")
$summary.Add('No sample buffers read or world FF emitted. NOT FOR XBOX.')
[IO.File]::WriteAllLines($outTxt,[string[]]$summary.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-BURNABLE] ================= RESULT =========================='
Write-Host "[AZTEC-PC-BURNABLE] status=$status"
Write-Host "[AZTEC-PC-BURNABLE] report=$outTxt"
Write-Host '[AZTEC-PC-BURNABLE] NO BUILD / NO FF / NOT FOR XBOX.'
if($status -notin @('AZTEC_PC_COMWORLD_88_BURNABLE_CELL_HEADERS_PASS','AZTEC_PC_COMWORLD_BURNABLE_LAYOUT_UNRESOLVED')){exit 4}
