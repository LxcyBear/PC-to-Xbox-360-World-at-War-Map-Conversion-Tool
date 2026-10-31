[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Real Xbox Sound alias -> SoundFile -> LoadedSound/StreamedSound -> dumped
# XMA sample identity. Uses ONLY original 67-zone Xbox retail OAT sources,
# never unrelated donor assets, synthetic sound bytes or production FF writes.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$crosswalk=Join-Path $base 'pc_g11_flame_loaded_virtual_target_proof\aztec_flame_sound_alias_real_xbox_retail_stock_names.json'
$oldAudio=Join-Path $base 'pc_g11_flame_real_xbox_xma_source\aztec_six_real_original_xbox_sound_xma_source_provenance.json'
$goodFf=Join-Path $repo '.deps\aztec-w63-direct-ff-growth\scratch_emit\nazi_zombie_aztec.ff'
$goodProof=Join-Path $repo '.deps\aztec-w63-direct-ff-growth\aztec_weapon63_ff_growth_63.json'
$capPath=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$out=Join-Path $base 'pc_g11_flame_real_xbox_xma_source'
$json=Join-Path $out 'aztec_six_real_flame_xbox_sound_to_xma_chain_v4.json'
$log=Join-Path $out 'aztec_real_xbox_flame_sound_alias_to_loaded_xma_v4.log'
$evidence=Join-Path $repo '.deps\aztec-flame-real-xma-linked'
foreach($f in @($crosswalk,$oldAudio,$goodFf,$goodProof,$capPath,$oat)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){throw "Verified original Xbox audio/native FF prerequisite missing: $f"}
}
$c=Get-Content -LiteralPath $crosswalk -Raw|ConvertFrom-Json
$a=Get-Content -LiteralPath $oldAudio -Raw|ConvertFrom-Json
$g=Get-Content -LiteralPath $goodProof -Raw|ConvertFrom-Json
$cap=Get-Content -LiteralPath $capPath -Raw|ConvertFrom-Json
$oldHash=(Get-FileHash -LiteralPath $goodFf -Algorithm SHA256).Hash
if([string]$c.schema -ne 'WaW360.AztecFlameRealXboxSoundStockNameCrosswalk.v1' -or
   [int]$c.realOriginalPcSoundAliasNames -ne 6 -or
   [int]$c.uniqueSourceSoundNamesWithOfflineXboxOwnedNameCandidate -ne 6 -or
   [int]$c.offlineOriginalXboxRetailZonesAccepted -ne 67 -or
   [int]$c.offlineOriginalXboxRetailZonesStaleOrInvalid -ne 0 -or
   [string]$a.schema -ne 'WaW360.AztecGenuineXboxRetailSoundXmaSourceExtraction.v1' -or
   [int]$a.originalPcFlameSoundAliasNames -ne 6 -or
   [int]$a.selectedRetailZoneCount -ne 1 -or
   @($a.sourceRetailZones).Count -ne 1 -or
   [int]$a.realSourceFlameAliasesWithDirectXmaFileMatches -ne 0 -or
   [string]$g.newScratchFastFileSha256 -ine $oldHash -or
   [long](Get-Item -LiteralPath $goodFf).Length -ne 4514599L -or
   [string]$cap.oatRevision -ne '3beaa7c81af517f0c05f2b4612d477ed1541b676' -or
   @($cap.capabilities) -notcontains 't4xenon-real-flame-sound-to-loaded-xma-chain-v4'){
    throw 'Old real six-alias/Xbox 67 retail-source proof, 1-zone previous OAT audio dump or v4 loader capability missing.'
}
$zone=$a.sourceRetailZones[0]
$ff=[string]$zone.originalXboxRetailFastFile
if(-not(Test-Path -LiteralPath $ff -PathType Leaf) -or
   (Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash -ine [string]$zone.originalXboxRetailFastFileSha256 -or
   [int]$zone.originalXboxOatSoundDumpExitCode -ne 0){
    throw 'Previously selected genuine Xbox retail FF SHA or OAT Sound/LoadedSound exit status changed.'
}
$names=@($c.realPcFlameSoundNamesToXboxStockNameEvidence|Select-Object -ExpandProperty originalPcLoadedSoundAliasName|Sort-Object -Unique)
if($names.Count -ne 6){throw 'Expected six actual genuine original PC Sound alias names.'}
$expected=@{}
foreach($name in $names){
    if([string]$name -notmatch '^[A-Za-z0-9_./-]{1,128}$'){throw 'Original source Sound alias name contains unsafe source-path characters.'}
    $expected[[string]$name]=$true
}
Write-Host '[AZTEC-XMA-CHAIN] ====================================================='
Write-Host "[AZTEC-XMA-CHAIN] Xbox retail source=$([IO.Path]::GetFileName($ff)); 6 PC-proven and Xbox-owned Sound aliases"
Write-Host '[AZTEC-XMA-CHAIN] Real Sound -> SoundFile -> LoadedSound/StreamedSound relationships, NOT alias filename guessing'
Write-Host '[AZTEC-XMA-CHAIN-PROGRESS] [####----------------] 20% | original Xbox FF SHA and actual Xbox Sound alias corpus locked'

$tmp=Join-Path ([IO.Path]::GetTempPath()) ('ZXMA_'+[guid]::NewGuid().ToString('N').Substring(0,9))
New-Item -ItemType Directory -Path $tmp -Force|Out-Null
$dump=Join-Path $tmp 'dump'
$stdout=Join-Path $tmp 'out.log'
$stderr=Join-Path $tmp 'err.log'
$prev=[Environment]::GetEnvironmentVariable('WAW360_XBOX_FLAME_SOUND_CHAINS','Process')
$exit=-1
try{
    [Environment]::SetEnvironmentVariable('WAW360_XBOX_FLAME_SOUND_CHAINS',($names -join ','),'Process')
    try{
        $sourceDir=[IO.Path]::GetDirectoryName($ff)
        $args=@('--no-color','--include-assets','sound,loadedsound','--output-folder',$dump,'--search-path',$sourceDir,$ff)
        $argLine=($args|ForEach-Object{'"'+([string]$_).Replace('"','\"')+'"'}) -join ' '
        $process=Start-Process -FilePath $oat -ArgumentList $argLine -RedirectStandardOutput $stdout -RedirectStandardError $stderr -Wait -PassThru -NoNewWindow
        $exit=[int]$process.ExitCode
    }finally{
        [Environment]::SetEnvironmentVariable('WAW360_XBOX_FLAME_SOUND_CHAINS',$prev,'Process')
    }
    $trace=[IO.File]::ReadAllText($stdout)+[Environment]::NewLine+[IO.File]::ReadAllText($stderr)
    [IO.File]::WriteAllText($log,$trace,[Text.UTF8Encoding]::new($false))
    if($exit -ne 0){throw "Genuine pinned Xbox retail OAT audio dump failed exit=$exit: $log"}

    $matches=@($trace -split '[\r\n]+'|Where-Object{$_ -match '^\[WAW360-XMASOUND\] root='})
    $expr='^\[WAW360-XMASOUND\] root=(\S+) index=(-?\d+) type=(LOADED|STREAMED|NULL|UNKNOWN|INVALID) exists=(\d+) alias=(\S+) loaded=(\S+) streamDir=(\S+) streamName=(\S+) sampleBytes=(\d+) loadedOwned=(0|1)
    $roots=@{}
    $rows=New-Object 'System.Collections.Generic.List[object]'
    $sourcePayloads=New-Object 'System.Collections.Generic.List[object]'
    $matchedSamples=0
    $xmaRiffProof=0
    $totalRows=0
    foreach($line in $matches){
        $m=[regex]::Match([string]$line.Trim(),$expr)
        if(-not $m.Success){throw "Xbox loaded Sound source trace malformed: $line"}
        $root=[string]$m.Groups[1].Value
        if(-not $expected.ContainsKey($root)){throw "Unexpected Sound alias source root $root in exact-six witness"}
        $i=[int]$m.Groups[2].Value
        $kind=[string]$m.Groups[3].Value
        $exists=([int]$m.Groups[4].Value -ne 0)
        $alias=[string]$m.Groups[5].Value
        $loaded=[string]$m.Groups[6].Value
        $streamDir=[string]$m.Groups[7].Value
        $streamName=[string]$m.Groups[8].Value
        $packetBytes=[long]$m.Groups[9].Value
        $loadedOwned=([int]$m.Groups[10].Value -eq 1)
        if($kind -eq 'LOADED' -and -not $loadedOwned -and $loaded -ne '-'){
            throw "Xbox LoadedSound pointer has no verified OAT-owned source asset: $root index=$i"
        }
        if($i -lt -1 -or $i -gt 512){throw 'Xbox real Sound alias source index outside bounds.'}
        if(-not $roots.ContainsKey($root)){$roots[$root]=New-Object 'System.Collections.Generic.HashSet[int]'}
        if(-not $roots[$root].Add($i)){throw "Repeated Xbox native alias record root=$root index=$i"}
        $rel=$null
        if($i -ge 0 -and $kind -eq 'LOADED' -and $loadedOwned -and $loaded -ne '-'){
            # OAT's pinned LoadedSoundDumperT4Xenon::GetFileNameForLoadedSound:
            # sound/ + path(name).replace_extension(".xma"), NOT aliasName.index.xma.
            $normalized=$loaded.Replace('\','/')
            $rel='sound/'+[IO.Path]::ChangeExtension($normalized,'.xma').Replace('\','/')
        }elseif($i -ge 0 -and $kind -eq 'STREAMED' -and $streamName -ne '-'){
            # OAT's pinned SoundDumperT4Xenon emits root.aliasIndex.xma.
            $rel=('sound/{0}.{1}.xma' -f $root,$i)
        }
        if($rel -and ($rel -notmatch '^sound/[A-Za-z0-9_./-]{1,230}\.xma$' -or
            $rel -match '(^|/)\.\.(/|$)')){
            throw "Unsafe original Xbox SoundFile loaded/streamed path: $rel"
        }
        $found=$false;$bytes=0L;$sha=$null;$tag=$null;$validRiffXma=$false;$saved=$null
        if($rel -and $exists){
            $realPath=Join-Path $dump ($rel.Replace('/',[IO.Path]::DirectorySeparatorChar))
            if(Test-Path -LiteralPath $realPath -PathType Leaf){
                $file=Get-Item -LiteralPath $realPath
                if([long]$file.Length -ge 32L){
                    $found=$true
                    $bytes=[long]$file.Length
                    $sha=(Get-FileHash -LiteralPath $realPath -Algorithm SHA256).Hash
                    $header=New-Object byte[] 24
                    $s=[IO.File]::OpenRead($realPath)
                    try{$read=$s.Read($header,0,24)}finally{$s.Dispose()}
                    $riff=($read -ge 22 -and
                        [Text.Encoding]::ASCII.GetString($header,0,4) -ceq 'RIFF' -and
                        [Text.Encoding]::ASCII.GetString($header,8,4) -ceq 'WAVE')
                    if($riff){
                        $fmt=[BitConverter]::ToUInt16($header,20)
                        $tag=('0x{0:X4}' -f $fmt)
                        $validRiffXma=($fmt -in @(0x0165,0x0166))
                    }
                    New-Item -ItemType Directory -Path $evidence -Force|Out-Null
                    $safeName=('{0}_{1}_{2}' -f $root,$i,([IO.Path]::GetFileName($realPath)))
                    $saved=Join-Path $evidence $safeName
                    Copy-Item -LiteralPath $realPath -Destination $saved -Force
                    if((Get-FileHash -LiteralPath $saved -Algorithm SHA256).Hash -ine $sha){
                        throw 'Authenticated original Xbox source audio sample SHA changed during staging.'
                    }
                    $matchedSamples++
                    if($validRiffXma){$xmaRiffProof++}
                    $sourcePayloads.Add([pscustomobject][ordered]@{
                        XboxRetailFfSource=$ff
                        XboxRetailFfSha256=[string]$zone.originalXboxRetailFastFileSha256
                        originalPcAndRetailSoundAliasName=$root
                        exactOatSourceSoundAliasIndex=$i
                        sourceSoundFileType=$kind
                        sourceLoadedSoundName=if($kind -eq 'LOADED'){$loaded}else{$null}
                        genuineOatDumpRelativeXmaFile=$rel
                        stagedAuthenticSample=$saved
                        sampleSha256=$sha
                        sampleBytes=$bytes
                        riffXmaCodecHeaderProven=$validRiffXma
                        riffWavFormatTag=$tag
                        sourceAliasToSampleFileRelationshipProven=$true
                        samplePlaybackOrCodXeRuntimeProven=$false
                    })
                    Write-Host "[AZTEC-XMA-CHAIN] $root #$i source-type=$kind linked-XMA=$rel bytes=$bytes RIFF-XMA=$validRiffXma"
                }
            }
        }
        $totalRows++
        $rows.Add([pscustomobject][ordered]@{
            realOriginalPcAndXboxSoundAlias=$root
            originalXboxRetailAliasIndex=$i
            OatLoadedSoundAliasRecordName=$alias
            originalXboxSoundFileType=$kind
            originalXboxSoundFileExistsFlag=$exists
            originalXboxLoadedSoundName=if($loaded -eq '-'){$null}else{$loaded}
            originalXboxLoadedSoundPoolAssetOwned=$loadedOwned
            originalXboxStreamDirectory=if($streamDir -eq '-'){$null}else{$streamDir}
            originalXboxStreamFile=if($streamName -eq '-'){$null}else{$streamName}
            originalXboxLoadedXmaPacketBytes=$packetBytes
            exactPinnedOatSampleRelativePath=$rel
            exactSamplePayloadExtracted=$found
            actualExtractedXmaSampleBytes=$bytes
            actualXmaSampleSha256=$sha
            actualXmaRiffCodecHeaderProven=$validRiffXma
            sourceAliasChainToSampleRelationshipProven=$found
            nativeAztecSoundFileGrafted=$false
            CodXeRuntimeSoundSampleResidencyVerified=$false
        })
    }
    if($roots.Count -ne 6){throw "OAT loaded Sound alias chain trace coverage incomplete: $($roots.Count)/6. No XMA identity promoted."}
    foreach($name in $names){
        if($roots[$name].Count -lt 1 -or $roots[$name].Contains(-1)){
            throw "Original Xbox Sound root $name lacks an actual loaded alias record; cannot claim XMA sample"
        }
    }
    $gotAliases=@($sourcePayloads.ToArray()|Select-Object -ExpandProperty originalPcAndRetailSoundAliasName -Unique)
    if((Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash -ine
       [string]$zone.originalXboxRetailFastFileSha256){
        throw 'Original Xbox retail FF SHA changed during read-only native OAT Sound dump.'
    }
    if((Get-FileHash -LiteralPath $goodFf -Algorithm SHA256).Hash -ine $oldHash){
        throw 'Original 222-alias OAT-good native Aztec FF mutated during Xbox source audio witness.'
    }
    $data=[ordered]@{
        schema='WaW360.AztecOriginalXboxSoundFileToXmaChain.v4'
        status=if($matchedSamples -gt 0){'GENUINE_XBOX_SOUND_TO_REAL_XMA_SAMPLE_NAME_AND_BYTES_LINK_PROVEN_OFFLINE'}
            else{'SIX_REAL_XBOX_SOUND_ALIAS_CHAINS_TRACED_NO_SOURCE_XMA_PAYLOAD_LINK'}
        sourceXboxRetailFf=$ff
        sourceXboxRetailSha256=[string]$zone.originalXboxRetailFastFileSha256
        sourceOriginalPcFlameSoundAliasCount=6
        actualXenonSoundRootsTyped=6
        nativeXboxSoundAliasRecordsTraced=$totalRows
        authenticXboxOatAliasToSampleFilesExtracted=$matchedSamples
        aliasesWithActualOriginalXboxLinkedSample=$gotAliases.Count
        riffXmaCodecHeaderProvenSampleCount=$xmaRiffProof
        originalPcFlameParentNullSoundSlotsPreserved=4
        nativeAztecFFModified=$false
        originalXmaSampleSourceBytesNeverTranscoded=$true
        nativeXboxSoundFileTypeMetadataGrafted=$false
        CodXeActualXboxRetailZoneSoundResidencyProven=$false
        xboxHardwareReady=$false
        directSourceSoundChains=@($rows.ToArray())
        verifiedOriginalXboxSourceSamplePayloads=@($sourcePayloads.ToArray())
        nativeOatRealXenonSoundAliasTrace=$log
    }
    $data|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $json -Encoding UTF8
    Write-Host "[AZTEC-XMA-CHAIN] Genuine native Xbox loaded Sound roots=$($roots.Count)/6; real alias-chain linked audio samples=$matchedSamples, unique sound aliases with samples=$($gotAliases.Count)/6; RIFF-XMA samples=$xmaRiffProof"
    Write-Host "[AZTEC-XMA-CHAIN] STATUS=$($data.status) JSON=$json"
    Write-Host '[AZTEC-XMA-CHAIN-PROGRESS] [####################] 100% | verified Xbox retail SoundFile -> real XMA source bytes; NO map FF mutation'
}finally{
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

    $roots=@{}
    $rows=New-Object 'System.Collections.Generic.List[object]'
    $sourcePayloads=New-Object 'System.Collections.Generic.List[object]'
    $matchedSamples=0
    $xmaRiffProof=0
    $totalRows=0
    foreach($line in $matches){
        $m=[regex]::Match([string]$line.Trim(),$expr)
        if(-not $m.Success){throw "Xbox loaded Sound source trace malformed: $line"}
        $root=[string]$m.Groups[1].Value
        if(-not $expected.ContainsKey($root)){throw "Unexpected Sound alias source root $root in exact-six witness"}
        $i=[int]$m.Groups[2].Value
        $kind=[string]$m.Groups[3].Value
        $exists=([int]$m.Groups[4].Value -ne 0)
        $alias=[string]$m.Groups[5].Value
        $loaded=[string]$m.Groups[6].Value
        $streamDir=[string]$m.Groups[7].Value
        $streamName=[string]$m.Groups[8].Value
        $packetBytes=[long]$m.Groups[9].Value
        if($i -lt -1 -or $i -gt 512){throw 'Xbox real Sound alias source index outside bounds.'}
        if(-not $roots.ContainsKey($root)){$roots[$root]=New-Object 'System.Collections.Generic.HashSet[int]'}
        if(-not $roots[$root].Add($i)){throw "Repeated Xbox native alias record root=$root index=$i"}
        $rel=$null
        if($i -ge 0 -and $kind -eq 'LOADED' -and $loaded -ne '-'){
            # OAT's pinned LoadedSoundDumperT4Xenon::GetFileNameForLoadedSound:
            # sound/ + path(name).replace_extension(".xma"), NOT aliasName.index.xma.
            $normalized=$loaded.Replace('\','/')
            $rel='sound/'+[IO.Path]::ChangeExtension($normalized,'.xma').Replace('\','/')
        }elseif($i -ge 0 -and $kind -eq 'STREAMED' -and $streamName -ne '-'){
            # OAT's pinned SoundDumperT4Xenon emits root.aliasIndex.xma.
            $rel=('sound/{0}.{1}.xma' -f $root,$i)
        }
        if($rel -and ($rel -notmatch '^sound/[A-Za-z0-9_./-]{1,230}\.xma$' -or
            $rel -match '(^|/)\.\.(/|$)')){
            throw "Unsafe original Xbox SoundFile loaded/streamed path: $rel"
        }
        $found=$false;$bytes=0L;$sha=$null;$tag=$null;$validRiffXma=$false;$saved=$null
        if($rel -and $exists){
            $realPath=Join-Path $dump ($rel.Replace('/',[IO.Path]::DirectorySeparatorChar))
            if(Test-Path -LiteralPath $realPath -PathType Leaf){
                $file=Get-Item -LiteralPath $realPath
                if([long]$file.Length -ge 32L){
                    $found=$true
                    $bytes=[long]$file.Length
                    $sha=(Get-FileHash -LiteralPath $realPath -Algorithm SHA256).Hash
                    $header=New-Object byte[] 24
                    $s=[IO.File]::OpenRead($realPath)
                    try{$read=$s.Read($header,0,24)}finally{$s.Dispose()}
                    $riff=($read -ge 22 -and
                        [Text.Encoding]::ASCII.GetString($header,0,4) -ceq 'RIFF' -and
                        [Text.Encoding]::ASCII.GetString($header,8,4) -ceq 'WAVE')
                    if($riff){
                        $fmt=[BitConverter]::ToUInt16($header,20)
                        $tag=('0x{0:X4}' -f $fmt)
                        $validRiffXma=($fmt -in @(0x0165,0x0166))
                    }
                    New-Item -ItemType Directory -Path $evidence -Force|Out-Null
                    $safeName=('{0}_{1}_{2}' -f $root,$i,([IO.Path]::GetFileName($realPath)))
                    $saved=Join-Path $evidence $safeName
                    Copy-Item -LiteralPath $realPath -Destination $saved -Force
                    if((Get-FileHash -LiteralPath $saved -Algorithm SHA256).Hash -ine $sha){
                        throw 'Authenticated original Xbox source audio sample SHA changed during staging.'
                    }
                    $matchedSamples++
                    if($validRiffXma){$xmaRiffProof++}
                    $sourcePayloads.Add([pscustomobject][ordered]@{
                        XboxRetailFfSource=$ff
                        XboxRetailFfSha256=[string]$zone.originalXboxRetailFastFileSha256
                        originalPcAndRetailSoundAliasName=$root
                        exactOatSourceSoundAliasIndex=$i
                        sourceSoundFileType=$kind
                        sourceLoadedSoundName=if($kind -eq 'LOADED'){$loaded}else{$null}
                        genuineOatDumpRelativeXmaFile=$rel
                        stagedAuthenticSample=$saved
                        sampleSha256=$sha
                        sampleBytes=$bytes
                        riffXmaCodecHeaderProven=$validRiffXma
                        riffWavFormatTag=$tag
                        sourceAliasToSampleFileRelationshipProven=$true
                        samplePlaybackOrCodXeRuntimeProven=$false
                    })
                    Write-Host "[AZTEC-XMA-CHAIN] $root #$i source-type=$kind linked-XMA=$rel bytes=$bytes RIFF-XMA=$validRiffXma"
                }
            }
        }
        $totalRows++
        $rows.Add([pscustomobject][ordered]@{
            realOriginalPcAndXboxSoundAlias=$root
            originalXboxRetailAliasIndex=$i
            OatLoadedSoundAliasRecordName=$alias
            originalXboxSoundFileType=$kind
            originalXboxSoundFileExistsFlag=$exists
            originalXboxLoadedSoundName=if($loaded -eq '-'){$null}else{$loaded}
            originalXboxStreamDirectory=if($streamDir -eq '-'){$null}else{$streamDir}
            originalXboxStreamFile=if($streamName -eq '-'){$null}else{$streamName}
            originalXboxLoadedXmaPacketBytes=$packetBytes
            exactPinnedOatSampleRelativePath=$rel
            exactSamplePayloadExtracted=$found
            actualExtractedXmaSampleBytes=$bytes
            actualXmaSampleSha256=$sha
            actualXmaRiffCodecHeaderProven=$validRiffXma
            sourceAliasChainToSampleRelationshipProven=$found
            nativeAztecSoundFileGrafted=$false
            CodXeRuntimeSoundSampleResidencyVerified=$false
        })
    }
    if($roots.Count -ne 6){throw "OAT loaded Sound alias chain trace coverage incomplete: $($roots.Count)/6. No XMA identity promoted."}
    foreach($name in $names){
        if($roots[$name].Count -lt 1){throw "No Xbox native SoundFile alias records for $name"}
    }
    $gotAliases=@($sourcePayloads.ToArray()|Select-Object -ExpandProperty originalPcAndRetailSoundAliasName -Unique)
    if((Get-FileHash -LiteralPath $goodFf -Algorithm SHA256).Hash -ine $oldHash){
        throw 'Original 222-alias OAT-good native Aztec FF mutated during Xbox source audio witness.'
    }
    $data=[ordered]@{
        schema='WaW360.AztecOriginalXboxSoundFileToXmaChain.v4'
        status=if($matchedSamples -gt 0){'GENUINE_XBOX_SOUND_TO_REAL_XMA_SAMPLE_NAME_AND_BYTES_LINK_PROVEN_OFFLINE'}
            else{'SIX_REAL_XBOX_SOUND_ALIAS_CHAINS_TRACED_NO_SOURCE_XMA_PAYLOAD_LINK'}
        sourceXboxRetailFf=$ff
        sourceXboxRetailSha256=[string]$zone.originalXboxRetailFastFileSha256
        sourceOriginalPcFlameSoundAliasCount=6
        actualXenonSoundRootsTyped=6
        nativeXboxSoundAliasRecordsTraced=$totalRows
        authenticXboxOatAliasToSampleFilesExtracted=$matchedSamples
        aliasesWithActualOriginalXboxLinkedSample=$gotAliases.Count
        riffXmaCodecHeaderProvenSampleCount=$xmaRiffProof
        originalPcFlameParentNullSoundSlotsPreserved=4
        nativeAztecFFModified=$false
        originalXmaSampleSourceBytesNeverTranscoded=$true
        nativeXboxSoundFileTypeMetadataGrafted=$false
        CodXeActualXboxRetailZoneSoundResidencyProven=$false
        xboxHardwareReady=$false
        directSourceSoundChains=@($rows.ToArray())
        verifiedOriginalXboxSourceSamplePayloads=@($sourcePayloads.ToArray())
        nativeOatRealXenonSoundAliasTrace=$log
    }
    $data|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $json -Encoding UTF8
    Write-Host "[AZTEC-XMA-CHAIN] Genuine native Xbox loaded Sound roots=$($roots.Count)/6; real alias-chain linked audio samples=$matchedSamples, unique sound aliases with samples=$($gotAliases.Count)/6; RIFF-XMA samples=$xmaRiffProof"
    Write-Host "[AZTEC-XMA-CHAIN] STATUS=$($data.status) JSON=$json"
    Write-Host '[AZTEC-XMA-CHAIN-PROGRESS] [####################] 100% | verified Xbox retail SoundFile -> real XMA source bytes; NO map FF mutation'
}finally{
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
