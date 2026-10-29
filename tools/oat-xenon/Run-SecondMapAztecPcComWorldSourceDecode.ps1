[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$anchorsFile=Join-Path $base 'pc_world_root_source_anchors\aztec_pc_world_root_source_anchors.json'
$forensicsFile=Join-Path $base 'pc_world_header_forensics\aztec_pc_world_header_forensics.json'
$censusFile=Join-Path $base 'tonight_critical_source_census\aztec_tonight_critical_source_census.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$work=Join-Path $base 'pc_comworld_source_decode'
$log=Join-Path $work 'decompress.log'
$json=Join-Path $work 'aztec_pc_comworld_source_decode.json'
$txt=Join-Path $work 'aztec_pc_comworld_source_decode.txt'
foreach($p in @($anchorsFile,$forensicsFile,$censusFile,$cli)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing ComWorld source provenance: $p"}
}
$anchors=Get-Content -LiteralPath $anchorsFile -Raw | ConvertFrom-Json
$forensics=Get-Content -LiteralPath $forensicsFile -Raw | ConvertFrom-Json
$census=Get-Content -LiteralPath $censusFile -Raw | ConvertFrom-Json
if([string]$anchors.schema -ne 'WaW360.AztecPcWorldRootSourceAnchors.v1' -or
   [string]$anchors.status -ne 'AZTEC_PC_WORLD_ROOT_SOURCE_ANCHOR_SCAN_COMPLETE' -or
   [string]$forensics.schema -ne 'WaW360.AztecPcWorldHeaderForensics.v1' -or
   [string]$forensics.status -ne 'AZTEC_PC_WORLD_HEADER_FORENSICS_COMPLETE' -or
   [string]$census.status -ne 'AZTEC_TONIGHT_CRITICAL_PC_SOURCE_IDENTITY_CENSUS_PASS' -or
   [int]$census.nativeRoots -ne 8509 -or
   @($forensics.inspectedWorldRoots).Count -ne 3){
    throw 'Original PC world-source chain no longer passes independent prerequisites.'
}
$sourceRoots=@($forensics.inspectedWorldRoots)
$com=@($sourceRoots | Where-Object {[string]$_.family -eq 'comworld'})
if($com.Count -ne 1 -or [int]$com[0].nativeRootIndex -ne 7178 -or
   [int]$com[0].sourcePCFixedHeaderSizeHypothesis -ne 64){
    throw 'ComWorld #7178 parent ABI source hypothesis no longer matches.'
}
$shared=@($sourceRoots | Select-Object -ExpandProperty originalPCNameByteOffset -Unique)
if($shared.Count -ne 1){
    throw "The previous world-name alias proof changed; distinct world offsets=$($shared.Count)."
}
$role=@($census.pcSourceRoles | Where-Object {[string]$_.role -eq 'map.ff'})
if($role.Count -ne 1 -or
   [string]$role[0].sourceFastfile -ine [string]$anchors.originalPCMapFastfile -or
   [string]$role[0].sourceSha256 -ine [string]$anchors.originalPCMapFastfileSha256){
    throw 'ComWorld original PC map.ff source identity mismatch.'
}
$pc=[string]$role[0].sourceFastfile
if(-not (Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$role[0].sourceSha256){
    throw 'ComWorld original-PC map.ff SHA-256 changed.'
}
function Uint([byte[]]$bytes,[int]$off){
    if($off -lt 0 -or $off -gt $bytes.Length-4){throw "Out of bounds 32-bit field: $off"}
    return [BitConverter]::ToUInt32($bytes,$off)
}
function Int([byte[]]$bytes,[int]$off){
    if($off -lt 0 -or $off -gt $bytes.Length-4){throw "Out of bounds 32-bit field: $off"}
    return [BitConverter]::ToInt32($bytes,$off)
}
function ShaSlice([byte[]]$bytes,[int]$off,[int]$count){
    $h=[Security.Cryptography.SHA256]::Create()
    try{return [BitConverter]::ToString($h.ComputeHash($bytes,$off,$count)).Replace('-','')}
    finally{$h.Dispose()}
}
function Marker([uint32]$word){
    return $word -eq [uint32]::MaxValue -or
           $word -eq [uint32]([uint32]::MaxValue - 1)
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
$temporary=''
$status='AZTEC_PC_COMWORLD_SOURCE_DECODE_BLOCKED'
$blocker=''
$parsed=$null
$childRows=@()
$bestScore=-1
$notes=New-Object 'System.Collections.Generic.List[string]'
Write-Host '[AZTEC-PC-COMWORLD] =================================================='
Write-Host '[AZTEC-PC-COMWORLD] SOURCE-PC COMWORLD PARENT ABI AND TWO LIGHTS PROBE'
Write-Host '[AZTEC-PC-COMWORLD] Three world names are SHARED. Only #7178 has a supported header candidate.'
Write-Host '[AZTEC-PC-COMWORLD] No FF emit, no build, no Xbox candidate.'
try{
    $temporary=Join-Path ([IO.Path]::GetTempPath()) ('wawAzCW_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    if($temporary.Length -gt 110){throw 'Decompression requires a short physical temporary path.'}
    New-Item -Path $temporary -ItemType Directory -Force | Out-Null
    $zoneFile=Join-Path $temporary 'original_pc_map.zone'
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & dotnet 'run' '--no-build' '-c' 'Release' '--project' $cli '--' 'decompress' $pc $zoneFile 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $log -Encoding UTF8
        $exit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    if($exit -ne 0 -or -not (Test-Path -LiteralPath $zoneFile -PathType Leaf)){
        throw "Original PC source decompression failed exit=$exit; see $log"
    }
    $bytes=[IO.File]::ReadAllBytes($zoneFile)
    $zoneSha=(Get-FileHash -LiteralPath $zoneFile -Algorithm SHA256).Hash
    if([long]$bytes.Length -ne 113701260L -or
       $zoneSha -ine [string]$anchors.decompressedPCZoneSha256 -or
       $zoneSha -ine [string]$forensics.originalPCZoneSha256){
        throw 'Original PC zone fingerprint drift; do not decode unverified bytes.'
    }
    $name=[string]$com[0].originalPCName
    $nameBytes=[Text.Encoding]::ASCII.GetBytes($name+[char]0)
    $nameOff=[int]$com[0].originalPCNameByteOffset
    $fixedOff=$nameOff-64
    if($fixedOff -ne [int]$com[0].hypothesizedFixedHeaderOffset -or
       $fixedOff -lt 0 -or $nameOff -gt $bytes.Length-$nameBytes.Length){
        throw 'ComWorld candidate byte offsets changed.'
    }
    for($i=0;$i -lt $nameBytes.Length;$i++){
        if($bytes[$nameOff+$i] -ne $nameBytes[$i]){throw 'Original PC ComWorld asset name bytes changed.'}
    }
    $fields=[ordered]@{
        namePointerHex=('0x{0:X8}' -f (Uint $bytes ($fixedOff+0)))
        isInUse=(Int $bytes ($fixedOff+4))
        primaryLightCount=(Uint $bytes ($fixedOff+8))
        primaryLightsPointerHex=('0x{0:X8}' -f (Uint $bytes ($fixedOff+12)))
        waterMinX=(Int $bytes ($fixedOff+16))
        waterMinY=(Int $bytes ($fixedOff+20))
        waterMaxX=(Int $bytes ($fixedOff+24))
        waterMaxY=(Int $bytes ($fixedOff+28))
        numWaterCells=(Uint $bytes ($fixedOff+32))
        waterCellsPointerHex=('0x{0:X8}' -f (Uint $bytes ($fixedOff+36)))
        burnableMinX=(Int $bytes ($fixedOff+40))
        burnableMinY=(Int $bytes ($fixedOff+44))
        burnableMaxX=(Int $bytes ($fixedOff+48))
        burnableMaxY=(Int $bytes ($fixedOff+52))
        numBurnableCells=(Uint $bytes ($fixedOff+56))
        burnableCellsPointerHex=('0x{0:X8}' -f (Uint $bytes ($fixedOff+60)))
    }
    $namePtr=Uint $bytes $fixedOff
    $lightsPtr=Uint $bytes ($fixedOff+12)
    $count=[long]$fields.primaryLightCount
    if(-not (Marker $namePtr) -or [int]$fields.isInUse -ne 1 -or
       $count -ne 2L -or -not (Marker $lightsPtr)){
        throw "ComWorld candidate signature disagrees with T4 ABI. name=$($fields.namePointerHex), use=$($fields.isInUse), lights=$count, lightsPtr=$($fields.primaryLightsPointerHex)"
    }
    # T4 ComPrimaryLight has 72 bytes on x86:
    # byte[4], short, 2 pad, 3x color, 3x dir, 3x origin,
    # 6 floats (radius/fov/limits), then 32-bit defName pointer.
    $afterName=$nameOff+$nameBytes.Length
    $candidates=New-Object 'System.Collections.Generic.List[object]'
    for($padding=0;$padding -le 16;$padding++){
        $start=$afterName+$padding
        if($start -lt 0 -or $start -gt $bytes.Length-144){continue}
        $score=0
        $lights=New-Object 'System.Collections.Generic.List[object]'
        for($k=0;$k -lt 2;$k++){
            $p=$start+($k*72)
            $type=[int]$bytes[$p]
            $shadow=[int]$bytes[$p+1]
            $exponent=[int]$bytes[$p+2]
            $priority=[int][sbyte]$bytes[$p+3]
            $cullDist=[BitConverter]::ToInt16($bytes,$p+4)
            $floatValues=New-Object 'System.Collections.Generic.List[single]'
            $finite=$true
            for($f=8;$f -le 64;$f+=4){
                $v=[BitConverter]::ToSingle($bytes,$p+$f)
                $floatValues.Add([single]$v)
                if([single]::IsNaN($v) -or [single]::IsInfinity($v) -or
                   [Math]::Abs([double]$v) -gt 1000000000.0){$finite=$false}
            }
            $defPtr=Uint $bytes ($p+68)
            $typeOk=$type -le 16
            $shadowOk=$shadow -le 2
            $radius=[single]$floatValues[9]
            $radiusOk=$finite -and $radius -ge 0 -and $radius -le 10000000
            $defNamePointerPlausible=$defPtr -eq 0 -or (Marker $defPtr)
            if($typeOk){$score+=2}
            if($shadowOk){$score+=2}
            if($finite){$score+=4}
            if($radiusOk){$score+=2}
            if($defNamePointerPlausible){$score+=2}
            $lights.Add([pscustomobject]@{
                byteOffset=$p;type=$type;canUseShadowMap=$shadow
                exponent=$exponent;priority=$priority;cullDist=$cullDist
                color=@($floatValues.ToArray()[0..2])
                dir=@($floatValues.ToArray()[3..5])
                origin=@($floatValues.ToArray()[6..8])
                radius=$radius
                defNamePointer=('0x{0:X8}' -f $defPtr)
                finiteFloats=$finite;typePlausible=$typeOk
                shadowPlausible=$shadowOk;radiusPlausible=$radiusOk
                defNamePointerPlausible=$defNamePointerPlausible
            })
        }
        $candidates.Add([pscustomobject]@{
            primaryLightArrayOffset=$start
            paddingAfterName=$padding
            candidateScore=$score
            candidateMaxScore=24
            lightFixedArraySha256=(ShaSlice $bytes $start 144)
            lights=$lights.ToArray()
            lightChildStringsRecovered=$false
        })
    }
    $best=@($candidates.ToArray() | Sort-Object -Property @{Expression='candidateScore';Descending=$true},@{Expression='paddingAfterName';Descending=$false})
    $childRows=@($best | Select-Object -First 4)
    $bestScore=[int]$childRows[0].candidateScore
    $parsed=[pscustomobject]@{
        nativeRootIndex=7178
        sourceComWorldHeaderOffset=$fixedOff
        sourceComWorldNameOffset=$nameOff
        sourceHeaderSize=64
        sourceParentSha256=(ShaSlice $bytes $fixedOff 64)
        fieldValues=[pscustomobject]$fields
        parentNameAndFirstFourFieldsConsistent=$true
        primaryLightFixedSizeHypothesis=72
        expectedPrimaryLightArrayBytes=144
        topLightArrayCandidates=$childRows
        primaryLightChildrenSourceVerified=$false
        waterAndBurnableChildrenSourceVerified=$false
        xboxComWorldSerialized=$false
    }
    Write-Host "[AZTEC-PC-COMWORLD] PARENT 0x$('{0:X8}' -f $fixedOff): name='$name' isInUse=$($fields.isInUse) primaryLights=$count ptr=$($fields.primaryLightsPointerHex)"
    Write-Host "[AZTEC-PC-COMWORLD] water: bounds=[$($fields.waterMinX),$($fields.waterMinY),$($fields.waterMaxX),$($fields.waterMaxY)] count=$($fields.numWaterCells) ptr=$($fields.waterCellsPointerHex)"
    Write-Host "[AZTEC-PC-COMWORLD] burnable: bounds=[$($fields.burnableMinX),$($fields.burnableMinY),$($fields.burnableMaxX),$($fields.burnableMaxY)] count=$($fields.numBurnableCells) ptr=$($fields.burnableCellsPointerHex)"
    foreach($cand in $childRows){
        Write-Host "[AZTEC-PC-COMWORLD] LIGHTS CANDIDATE offset=0x$('{0:X8}' -f $cand.primaryLightArrayOffset) pad=$($cand.paddingAfterName) plausibility=$($cand.candidateScore)/24"
        foreach($light in @($cand.lights)){
            Write-Host "[AZTEC-PC-COMWORLD]   light type=$($light.type) shadow=$($light.canUseShadowMap) radius=$($light.radius) defName=$($light.defNamePointer)"
        }
    }
    if($bestScore -ge 20){
        $status='AZTEC_PC_COMWORLD_PARENT_AND_LIGHTS_CANDIDATE_PASS'
        $notes.Add('Header fields and high-scoring 72-byte light fixed-array candidates are plausible; child names and exact stream ownership still require proof.')
    }else{
        $status='AZTEC_PC_COMWORLD_PARENT_ABI_PASS_CHILD_LAYOUT_UNRESOLVED'
        $notes.Add('64-byte parent signature is strong, but cannot infer 72-byte primary-light array location from name adjacency.')
    }
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-PC-COMWORLD] BLOCKER: $blocker"
    if(Test-Path -LiteralPath $log -PathType Leaf){
        foreach($line in @(Get-Content -LiteralPath $log -Tail 5)){
            Write-Host "[AZTEC-PC-COMWORLD] decompress: $line"
        }
    }
}finally{
    if(-not [string]::IsNullOrWhiteSpace($temporary) -and
       (Test-Path -LiteralPath $temporary -PathType Container)){
        Remove-Item -LiteralPath $temporary -Recurse -Force
    }
}
$report=[ordered]@{
    schema='WaW360.AztecOriginalPcComWorldSourceDecode.v1'
    status=$status
    sourceMapFastFile=$pc
    sourceMapFastFileSha256=[string]$role[0].sourceSha256
    sourcePCZoneSha256=[string]$anchors.decompressedPCZoneSha256
    sameNameByteOffsetForAllThreeWorlds=[int]$shared[0]
    sharedWorldNameDoesNotIdentifyGameWorldSpOrGfxWorld=$true
    comWorld=$parsed
    bestPrimaryLightsScore=$bestScore
    notes=$notes.ToArray()
    blocker=$blocker
    decodedXboxComWorldAssets=0
    fullWorldSourceProven=$false
    hardwareTestAuthorized=$false
    note='This inspects original PC 64-byte ComWorld parent fields and plausibility-ranked child arrays only. High candidate score does not establish parent-child stream ownership or rebase nested pointers. No FF. Not for Xbox.'
}
$report | ConvertTo-Json -Depth 13 | Set-Content -LiteralPath $json -Encoding UTF8
$summary=New-Object 'System.Collections.Generic.List[string]'
$summary.Add("Aztec original-PC ComWorld parent and lights probe: $status")
$summary.Add("Common world-name source byte offset=0x$('{0:X8}' -f $shared[0]); only one shared string, NOT three identified bodies")
if($null -ne $parsed){
    $summary.Add("PC ComWorld parent root #7178 at 0x$('{0:X8}' -f $parsed.sourceComWorldHeaderOffset)")
    $summary.Add("parent64 SHA256=$($parsed.sourceParentSha256), primaryLightCount=$($parsed.fieldValues.primaryLightCount)")
    foreach($c in $childRows){
        $summary.Add("LIGHT ARRAY CANDIDATE 0x$('{0:X8}' -f $c.primaryLightArrayOffset), pad=$($c.paddingAfterName), score=$($c.candidateScore)/24, SHA256=$($c.lightFixedArraySha256)")
    }
}
$summary.Add("blocker=$blocker")
$summary.Add('Source parent/header inspection ONLY. No ComWorld conversion. GfxWorld/GameWorldSP bodies NOT LOCATED. NOT FOR XBOX.')
[IO.File]::WriteAllLines($txt,[string[]]$summary.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-COMWORLD] ================= RESULT ========================'
Write-Host "[AZTEC-PC-COMWORLD] status=$status"
Write-Host "[AZTEC-PC-COMWORLD] report=$txt"
Write-Host '[AZTEC-PC-COMWORLD] NO BUILD / NO FF / NOT FOR XBOX.'
if($status -notin @('AZTEC_PC_COMWORLD_PARENT_AND_LIGHTS_CANDIDATE_PASS','AZTEC_PC_COMWORLD_PARENT_ABI_PASS_CHILD_LAYOUT_UNRESOLVED')){exit 4}
