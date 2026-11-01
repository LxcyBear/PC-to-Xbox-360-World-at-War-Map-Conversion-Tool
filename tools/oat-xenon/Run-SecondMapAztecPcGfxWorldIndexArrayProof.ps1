[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$censusPath=Join-Path $base 'tonight_critical_source_census\aztec_tonight_critical_source_census.json'
$scanPath=Join-Path $base 'pc_gfxworld_alias_anchor\aztec_pc_gfxworld_alias_anchor.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$work=Join-Path $base 'pc_gfxworld_index_source_proof'
$log=Join-Path $work 'original_pc_decompress.log'
$out=Join-Path $work 'aztec_pc_gfxworld_index_source_proof.json'
$outText=Join-Path $work 'aztec_pc_gfxworld_index_source_proof.txt'
foreach($p in @($censusPath,$scanPath,$cli)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec GfxWorld index proof prerequisite missing: $p"
    }
}
$census=Get-Content -LiteralPath $censusPath -Raw | ConvertFrom-Json
$scan=Get-Content -LiteralPath $scanPath -Raw | ConvertFrom-Json
if([string]$census.status -ne 'AZTEC_TONIGHT_CRITICAL_PC_SOURCE_IDENTITY_CENSUS_PASS' -or
   [int]$census.nativeRoots -ne 8509 -or
   [string]$scan.schema -ne 'WaW360.AztecPcGfxWorldAliasAnchor.v1' -or
   [string]$scan.status -ne 'AZTEC_PC_GFXWORLD_ALIAS_ANCHOR_SCAN_COMPLETE' -or
   [bool]$scan.sourceGfxWorldBodyDecoded -or
   [bool]$scan.xboxHardwareAuthorized -or
   [int]$scan.sourcePcGfxWorldXAssetIndex -ne 4284){
    throw 'Original PC source-root identity or 20/20 GfxWorld scan provenance changed.'
}
$gfxMatches=@($scan.candidateHeaders | Where-Object {
    [string]$_.candidateOffsetHex -eq '0x049122C0'
})
if($gfxMatches.Count -ne 1){
    throw 'Exactly one original PC GfxWorld 0x049122C0 candidate must exist.'
}
$gfx=$gfxMatches[0]
if([int]$gfx.score -ne 20 -or [int]$gfx.maxScore -ne 20 -or
   [int]$gfx.planeCount -ne 3447 -or
   [int]$gfx.nodeCount -ne 24511 -or
   [int]$gfx.indexCount -ne 58653 -or
   [int]$gfx.surfaceCount -ne 735 -or
   [int]$gfx.vertexCount -ne 32713 -or
   [string]$gfx.namePointerHex -ine '0x84702AC9' -or
   [string]$gfx.baseNamePointerHex -ine '0xFFFFFFFF' -or
   [string]$gfx.fixed128ByteSourceSha256 -ine '56594EA2849676C6A6C603954DE3EAE8011DEA05D9823899E7C27621CFEE92CF'){
    throw 'GfxWorld 20/20 source geometry-count header signature changed.'
}
$roles=@($census.pcSourceRoles | Where-Object {[string]$_.role -eq 'map.ff'})
if($roles.Count -ne 1){throw 'Expected one original PC map.ff source ownership.'}
$pc=[string]$roles[0].sourceFastfile
if(-not (Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$roles[0].sourceSha256 -or
   [string]$scan.originalPcSourceFastFileSha256 -ine [string]$roles[0].sourceSha256){
    throw 'Original PC map.ff source bytes changed since 20/20 GfxWorld header scan.'
}
function U32([byte[]]$b,[int]$o){
    if($o -lt 0 -or $o -gt $b.Length-4){throw 'U32 out of range'}
    return [BitConverter]::ToUInt32($b,$o)
}
function Sha([byte[]]$b,[int]$o,[int]$count){
    if($o -lt 0 -or $o -gt $b.Length-$count){throw 'SHA range out of bounds'}
    $h=[Security.Cryptography.SHA256]::Create()
    try{return [BitConverter]::ToString($h.ComputeHash($b,$o,$count)).Replace('-','')}
    finally{$h.Dispose()}
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
$temp=''
$status='AZTEC_PC_GFXWORLD_INDEX_SOURCE_PROOF_BLOCKED'
$blocker=''
$names=New-Object 'System.Collections.Generic.List[object]'
$arrays=New-Object 'System.Collections.Generic.List[object]'
$zoneSha=''
Write-Host '[AZTEC-PC-GFX-INDEX] =================================================='
Write-Host '[AZTEC-PC-GFX-INDEX] REAL PC GFXWORLD HEADER + INLINE BASENAME + 58653 INDICES'
Write-Host '[AZTEC-PC-GFX-INDEX] Original PC GfxWorld lead 0x049122C0, source SHA pinned.'
Write-Host '[AZTEC-PC-GFX-INDEX] No .NET build / no new FF / no Xbox.'
try{
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('wawAzGI_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    if($temp.Length -gt 110){throw 'Require short physical temporary path.'}
    New-Item -Path $temp -ItemType Directory -Force | Out-Null
    $zone=Join-Path $temp 'pc_map.zone'
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & dotnet 'run' '--no-build' '-c' 'Release' '--project' $cli '--' 'decompress' $pc $zone 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $log -Encoding UTF8
        $code=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    if($code -ne 0 -or -not (Test-Path -LiteralPath $zone -PathType Leaf)){
        throw "Original PC source decompression failed (exit=$code): $log"
    }
    $z=[IO.File]::ReadAllBytes($zone)
    $zoneSha=(Get-FileHash -LiteralPath $zone -Algorithm SHA256).Hash
    if([long]$z.Length -ne 113701260L -or
       $zoneSha -ine [string]$scan.originalPcZoneSha256){
        throw 'Original-PC decompressed 113701260-byte zone SHA-256 changed.'
    }
    $root=0x049122C0
    if((Sha $z $root 128) -ine [string]$gfx.fixed128ByteSourceSha256 -or
       (U32 $z $root) -ne [uint32]::Parse('84702AC9',[Globalization.NumberStyles]::HexNumber) -or
       (U32 $z ($root+4)) -ne [uint32]::MaxValue -or
       [int](U32 $z ($root+8)) -ne 3447 -or
       [int](U32 $z ($root+12)) -ne 24511 -or
       [int](U32 $z ($root+16)) -ne 58653 -or
       [int](U32 $z ($root+24)) -ne 735 -or
       [int](U32 $z ($root+52)) -ne 32713){
        throw 'Pinned original-PC 128-byte GfxWorld header/count fields did not match.'
    }
    $indexPtr=U32 $z ($root+20)
    if($indexPtr -ne [uint32]::MaxValue -and
       $indexPtr -ne [uint32]([uint32]::MaxValue-1)){
        throw "GfxWorld indices pointer unexpectedly is not inline: 0x$('{0:X8}' -f $indexPtr)"
    }
    Write-Host '[AZTEC-PC-GFX-INDEX] GfxWorld count/header/source SHA: EXACT PASS'
    # The T4 GfxWorld loader processes name, baseName and indices first,
    # although the name here is a packed alias, not an inline string.
    # Search only 0x200..0x1200 after root for a null-terminated aztec
    # basename, not the one shared root-name occurrence elsewhere.
    $strings=@('nazi_zombie_aztec','maps/nazi_zombie_aztec',
               'maps/nazi_zombie_aztec.d3dbsp')
    $window=$root+0x200
    $windowLength=0x1000
    $latin=[Text.Encoding]::GetEncoding(28591).GetString($z,$window,$windowLength)
    foreach($target in $strings){
        $needle=$target+[char]0
        $cursor=0
        while($cursor -lt $latin.Length){
            $found=$latin.IndexOf($needle,$cursor,[StringComparison]::OrdinalIgnoreCase)
            if($found -lt 0){break}
            $position=$window+$found
            $pre=if($position -gt 0){[int]$z[$position-1]}else{0}
            # A match must begin a standalone string (not the middle
            # of another path) and be immediately NUL-terminated.
            if($pre -eq 0 -or $pre -lt 32 -or $pre -gt 126){
                $names.Add([pscustomobject]@{
                    name=$target
                    byteOffset=$position
                    nameStartDelta=($position-$root)
                    nulTerminated=$true
                    byteCount=[Text.Encoding]::ASCII.GetByteCount($needle)
                    mayBeSerializedBaseName=$true
                })
            }
            $cursor=$found+$needle.Length
        }
    }
    $namesRanked=@($names.ToArray() | Sort-Object -Property byteOffset -Unique)
    Write-Host "[AZTEC-PC-GFX-INDEX] Candidate inline Aztec baseName strings within root+0x200..0x1200: $($namesRanked.Count)"
    foreach($name in $namesRanked){
        Write-Host "[AZTEC-PC-GFX-INDEX]   name='$($name.name)' position=0x$('{0:X8}' -f $name.byteOffset), fixedSizeHypothesis=$($name.nameStartDelta)"
    }

    $vertexCount=32713
    $indexCount=58653
    $size=$indexCount*2
    foreach($name in $namesRanked){
        $after=[int]$name.byteOffset+[int]$name.byteCount
        for($pad=0;$pad -le 16;$pad++){
            $at=$after+$pad
            if($at -lt 0 -or $at -gt $z.Length-$size){continue}
            $below=0;$nonzero=0;$high=0
            $min=65535;$max=0
            $unique=New-Object 'System.Collections.Generic.HashSet[uint16]'
            $first=New-Object 'System.Collections.Generic.List[int]'
            # Check the ENTIRE PC 16-bit index array. A few plausible
            # sample indices alone would be an unsafe false positive.
            for($i=0;$i -lt $indexCount;$i++){
                $v=[int][BitConverter]::ToUInt16($z,$at+$i*2)
                if($v -lt $vertexCount){$below++}
                else{$high++}
                if($v -ne 0){$nonzero++}
                if($v -lt $min){$min=$v}
                if($v -gt $max){$max=$v}
                [void]$unique.Add([uint16]$v)
                if($i -lt 12){$first.Add($v)}
            }
            $arrays.Add([pscustomobject]@{
                candidateArrayOffset=$at
                paddingAfterName=$pad
                originalPCBaseName=$name.name
                basenameOffset=$name.byteOffset
                originalPcIndexCount=$indexCount
                sourceBytes=$size
                indicesWithin32713=$below
                indicesOutOfRange=$high
                nonzeroIndices=$nonzero
                minIndex=$min
                maxIndex=$max
                uniqueIndices=$unique.Count
                firstTwelveIndices=$first.ToArray()
                sourceIndexBytesSha256=(Sha $z $at $size)
                realArrayOwnershipProven=$false
                XboxIndexArrayConverted=$false
            })
        }
    }
    $best=@($arrays.ToArray() | Sort-Object -Property @{Expression='indicesWithin32713';Descending=$true},@{Expression='uniqueIndices';Descending=$true},@{Expression='paddingAfterName'})
    foreach($result in @($best | Select-Object -First 8)){
        Write-Host "[AZTEC-PC-GFX-INDEX] ARRAY at 0x$('{0:X8}' -f $result.candidateArrayOffset), pad=$($result.paddingAfterName) inside=$($result.indicesWithin32713)/58653 high=$($result.indicesOutOfRange) unique=$($result.uniqueIndices) nonzero=$($result.nonzeroIndices) min=$($result.minIndex) max=$($result.maxIndex) sha=$($result.sourceIndexBytesSha256)"
        Write-Host "[AZTEC-PC-GFX-INDEX]   first12=$($result.firstTwelveIndices -join ',')"
    }
    if($best.Count -gt 0 -and
       [int]$best[0].indicesOutOfRange -eq 0 -and
       [int]$best[0].uniqueIndices -ge 256 -and
       [int]$best[0].nonzeroIndices -ge 20000){
        $status='AZTEC_PC_GFXWORLD_58653_INDEX_ARRAY_CANDIDATE_PASS'
        Write-Host '[AZTEC-PC-GFX-INDEX] Complete 58653-index source-backed candidate PASSED bounds and diversity gates.'
    }else{
        $status='AZTEC_PC_GFXWORLD_INDEX_LAYOUT_UNRESOLVED'
        Write-Host '[AZTEC-PC-GFX-INDEX] No complete 58653-index array adjacent to the tested baseName strings.'
    }
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-PC-GFX-INDEX] BLOCKER: $blocker"
    if(Test-Path -LiteralPath $log -PathType Leaf){
        Get-Content -LiteralPath $log -Tail 5 | ForEach-Object {Write-Host "[AZTEC-PC-GFX-INDEX] decompress $_"}
    }
}finally{
    if(-not [string]::IsNullOrWhiteSpace($temp) -and
       (Test-Path -LiteralPath $temp -PathType Container)){
        Remove-Item -LiteralPath $temp -Force -Recurse
    }
}
$ranked=@($arrays.ToArray() | Sort-Object -Property @{Expression='indicesWithin32713';Descending=$true},@{Expression='uniqueIndices';Descending=$true},@{Expression='paddingAfterName'})
$report=[ordered]@{
    schema='WaW360.AztecPcGfxWorldIndexSourceProof.v1'
    status=$status
    originalPcFastFile=$pc
    originalPcFastFileSha256=[string]$roles[0].sourceSha256
    originalPcZoneSha256=$zoneSha
    gfxWorldHeaderOffset=0x049122C0
    gfxWorldHeader128Sha256=[string]$gfx.fixed128ByteSourceSha256
    originalPcIndexCount=58653
    originalPcVertexCount=32713
    candidateBasenameStrings=$names.ToArray()
    candidateIndexArrays=$ranked
    blocker=$blocker
    originalPcGfxWorldBodyFullyDecoded=$false
    xboxWorldGeometryReady=$false
    readyForXbox=$false
    note='All PC GfxWorld count and header bytes SHA-pinned. Index-array matches, even if 0 out of range, are candidates until loader child-stream ownership and nested vertex data are proven. This scans only immediately after possible baseName strings, no FF emitted.'
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $out -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add("Original-PC Aztec GfxWorld 58653 index source proof: $status")
$lines.Add("PC gfxworld root at 0x049122C0, SHA=$($gfx.fixed128ByteSourceSha256)")
$lines.Add("PC source zone SHA=$zoneSha")
foreach($name in @($names.ToArray())){$lines.Add("inline name $($name.name) at 0x$('{0:X8}' -f $name.byteOffset)")}
foreach($result in @($ranked | Select-Object -First 12)){
    $lines.Add("index candidate at 0x$('{0:X8}' -f $result.candidateArrayOffset): padding=$($result.paddingAfterName), inside=$($result.indicesWithin32713)/58653 out=$($result.indicesOutOfRange), distinct=$($result.uniqueIndices), max=$($result.maxIndex), sourceSha=$($result.sourceIndexBytesSha256)")
}
$lines.Add("blocker=$blocker")
$lines.Add('PC candidate verification only. No PC vertex array read yet; no Xbox GfxWorld or FF emitted.')
[IO.File]::WriteAllLines($outText,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-GFX-INDEX] ================= RESULT ========================'
Write-Host "[AZTEC-PC-GFX-INDEX] status=$status"
Write-Host "[AZTEC-PC-GFX-INDEX] report=$outText"
Write-Host '[AZTEC-PC-GFX-INDEX] NO FF / NOT FOR XBOX.'
if($status -eq 'AZTEC_PC_GFXWORLD_INDEX_SOURCE_PROOF_BLOCKED'){exit 4}
