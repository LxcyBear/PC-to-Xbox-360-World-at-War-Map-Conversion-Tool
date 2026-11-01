[CmdletBinding()]
param(
    [string]$AudioSourceRoot='',
    [int]$MaxFiles=2500
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# No silent audio, synthetic samples, fake RIFF/XMA headers or generated FFs.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$out=Join-Path $base 'pc_g11_flame_real_source_sprint'
New-Item -ItemType Directory -Path $out -Force|Out-Null
$report=Join-Path $out 'aztec_real_xma_source_and_encoder_preflight.json'
if($MaxFiles -lt 1 -or $MaxFiles -gt 20000){throw 'MaxFiles must be 1..20000; no unlimited recursive scans.'}
$extensions=@('.wav','.mp3','.ogg','.flac','.wma','.xma','.xma2','.wem','.pak','.iwd','.sabs','.sabl')
$sampleExt=@('.wav','.mp3','.ogg','.flac','.wma','.xma','.xma2','.wem')
$rows=New-Object 'System.Collections.Generic.List[object]'
$filesFound=0;$payloadsVerified=0;$opaque=0;$xmaHeaders=0;$totalBytes=0L
$root=$null
if(-not [string]::IsNullOrWhiteSpace($AudioSourceRoot)){
    $root=[IO.Path]::GetFullPath($AudioSourceRoot)
    if(-not(Test-Path -LiteralPath $root -PathType Container)){
        throw "The original-PC sound source directory does not exist: $root"
    }
    $candidates=@(Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction Stop |
        Where-Object {$extensions -contains $_.Extension.ToLowerInvariant()} |
        Sort-Object FullName)
    if($candidates.Count -gt $MaxFiles){
        throw "Found $($candidates.Count) candidate audio/archive sources; narrow AudioSourceRoot or raise MaxFiles explicitly. No partial proof claimed."
    }
    foreach($file in $candidates){
        $filesFound++
        $totalBytes+=[long]$file.Length
        $ext=$file.Extension.ToLowerInvariant()
        $sha=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        $f=[IO.File]::Open($file.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        try{
            $head=New-Object byte[] 24
            $n=$f.Read($head,0,$head.Length)
        }finally{$f.Dispose()}
        $riff=$n -ge 12 -and [Text.Encoding]::ASCII.GetString($head,0,4) -eq 'RIFF' -and
            [Text.Encoding]::ASCII.GetString($head,8,4) -eq 'WAVE'
        $fmt=if($riff -and $n -ge 22 -and [Text.Encoding]::ASCII.GetString($head,12,4) -eq 'fmt '){
            [int]$head[20]+([int]$head[21] -shl 8)
        }else{$null}
        $format=if($fmt -in @(0x165,0x166)){'XMA_FORMAT_TAG_HEADER_DETECTED'}elseif($riff){
            'RIFF_WAVE_HEADER_NOT_XMA_VERIFIED'
        }elseif($ext -in @('.iwd','.pak','.sabs','.sabl')){
            'OPAQUE_AUDIO_OR_MAP_CONTAINER_NEEDS_TYPED_MEMBER_SOURCE_PROOF'
        }elseif($ext -in $sampleExt){
            'SOURCE_MEDIA_FILE_SHA_ONLY_CODEC_HEADER_NOT_VERIFIED'
        }else{'UNVERIFIED_MEDIA'}
        if($format -eq 'XMA_FORMAT_TAG_HEADER_DETECTED'){$xmaHeaders++}
        if($ext -in $sampleExt){$payloadsVerified++}else{$opaque++}
        $rows.Add([pscustomobject]@{
            sourcePath=$file.FullName
            sourceBytes=[long]$file.Length
            sourceSha256=$sha
            extension=$ext
            first24BytesHex=if($n -eq 0){''}else{(-join ($head[0..($n-1)]|ForEach-Object {$_.ToString('X2')}))}
            waveFormatTag=$fmt
            evidence=$format
            decodedToPcmAndCompared=$false
            verifiedXboxNativeXmaEncodedSample=$false
            validXboxSndAliasBinding=$false
        })
    }
}
# Codec executables are only capabilities; their mere existence cannot
# certify Xbox compatibility. Never invoke proprietary tools automatically.
$toolsFound=New-Object 'System.Collections.Generic.List[object]'
$toolPaths=@{}
foreach($name in @('xmaencode.exe','xma2encode.exe')){
    $command=Get-Command $name -ErrorAction SilentlyContinue
    if($null -ne $command -and -not [string]::IsNullOrWhiteSpace([string]$command.Source)){
        $toolPaths[[string]$command.Source]=$true
    }
}
if(-not [string]::IsNullOrWhiteSpace($env:XEDK)){
    foreach($rel in @('bin\xmaencode.exe','bin\win32\xmaencode.exe','bin\xma2encode.exe','bin\win32\xma2encode.exe')){
        $candidate=Join-Path $env:XEDK $rel
        if(Test-Path -LiteralPath $candidate -PathType Leaf){$toolPaths[[string]$candidate]=$true}
    }
}
foreach($path in $toolPaths.Keys){
    $toolsFound.Add([pscustomobject]@{
        executable=[IO.Path]::GetFileName($path)
        path=$path
        executableSha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        actualXboxXmaOutputAndRoundtripValidated=$false
    })
}
$state=if($null -eq $root){'SOURCE_AUDIO_ROOT_NOT_SUPPLIED_XMA_UNPROVEN'}else{
    'AUTHENTIC_AUDIO_FILES_CATALOGUED_NOT_YET_XMA_CONVERTED_OR_BOUND'
}
$result=[ordered]@{
    schema='WaW360.AztecRealXmaSourceCodecPreflight.v1'
    status=$state
    sourceAudioRoot=$root
    sourceFileCount=$filesFound
    authenticatedFileShas=$filesFound
    possibleIndividualMediaFiles=$payloadsVerified
    opaqueContainersNotCountedAsSamples=$opaque
    observedRiffXmaFormatTags=$xmaHeaders
    totalSourceFileBytes=$totalBytes
    locallyFoundXmaEncoderExecutables=$toolsFound.Count
    # PS 5.1 array-expression adapter can throw 'Argument types do not match'
    # when materializing generic List[object] inside an OrderedDictionary.
    # Explicit typed ToArray() is stable for both empty and populated lists.
    discoveredEncoderTools=$toolsFound.ToArray()
    genuineSourceFiles=$rows.ToArray()
    completeAudioStreamAssetAndXmaPayloadsVerified=$false
    xboxXmaEncodeRoundtripVerified=$false
    nativeSoundAliasSamplesBound=$false
    syntheticOrSilentSamplesProduced=$false
    nativeFastfileCreatedOrModified=$false
    xboxHardwareReady=$false
}
$result|ConvertTo-Json -Depth 9|Set-Content -LiteralPath $report -Encoding UTF8
Write-Host "[AZTEC-XMA-REAL] Original source files SHA catalogued=$filesFound, media-looking files=$payloadsVerified, opaque archives=$opaque"
Write-Host "[AZTEC-XMA-REAL] RIFF header-XMA tags=$xmaHeaders, local encoder executables (unverified)=$($toolsFound.Count)"
Write-Host "[AZTEC-XMA-REAL] STATUS=$state; samples/XMA/native references STILL BLOCKED; NO dummy audio"
Write-Host "[AZTEC-XMA-REAL] JSON=$report"
