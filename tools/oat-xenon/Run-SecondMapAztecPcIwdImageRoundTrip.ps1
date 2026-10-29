[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$work=Join-Path $base 'native_material_mapping\pc_iwd_image_roundtrip'
$baselinePath=Join-Path $base 'second_map_baseline.json'
$nativePath=Join-Path $base 'native_material_mapping\main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$sourceProofPath=Join-Path $base 'native_material_mapping\pc_image_source_census\pc_image_source_census_result.json'
$rawProofPath=Join-Path $base 'native_material_mapping\pc_rawfile_graft\pc_rawfile_graft_result.json'
$linker=Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Linker.exe'
$unlinker=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
foreach($p in @($baselinePath,$nativePath,$sourceProofPath,$rawProofPath,$linker,$unlinker)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){throw "Aztec source-image prerequisite missing: $p"}
}
$baseline=Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
$native=Get-Content -LiteralPath $nativePath -Raw | ConvertFrom-Json
$sourceProof=Get-Content -LiteralPath $sourceProofPath -Raw | ConvertFrom-Json
$rawProof=Get-Content -LiteralPath $rawProofPath -Raw | ConvertFrom-Json
if([string]$baseline.format -ne 'WaW360.SecondMapBaseline.v1' -or
   [string]$baseline.mapName -ine 'nazi_zombie_aztec_V2.2' -or
   -not [bool]$native.TypeSequenceVerified -or @($native.Entries).Count -ne 8509 -or
   [string]$sourceProof.status -ne 'AZTEC_PC_IWD_IMAGE_CENSUS_PASS' -or
   [int]$sourceProof.nativeImageRoots -ne 666 -or
   [int]$sourceProof.sourceIwiEntries -ne 2 -or
   [int]$sourceProof.matchedNativeImageIdentities -ne 2 -or
   [string]$rawProof.status -ne 'AZTEC_PC_RAWFILE_GRAFT_OAT_READBACK_PASS' -or
   [int]$rawProof.xboxReadbacksExact -ne 114){
    throw 'Aztec source IWD proof, native identity or 114/114 PC RawFile readback drifted.'
}
function Normalize-IwiName([string]$name){
    if([string]::IsNullOrWhiteSpace($name)){return ''}
    $v=$name.Replace('\','/').TrimStart('/')
    $pos=$v.LastIndexOf('/images/',[StringComparison]::OrdinalIgnoreCase)
    if($pos -ge 0){$v=$v.Substring($pos+8)}
    elseif($v.StartsWith('images/',[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(7)}
    if($v.EndsWith('.iwi',[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(0,$v.Length-4)}
    return $v.ToLowerInvariant()
}
function Read-Iwi([string]$path) {
    $bytes=[IO.File]::ReadAllBytes($path)
    if($bytes.Length -lt 29 -or $bytes[0] -ne 73 -or
       $bytes[1] -ne 87 -or $bytes[2] -ne 105) {
        throw "Invalid or truncated PC IWI header: $path"
    }
    if([int]$bytes[3] -ne 6 -or [int]$bytes[4] -ne 13){
        throw ("The previously proved PC Aztec source IWI v6 format 0x0D changed: v{0} 0x{1:X2} {2}" -f $bytes[3],$bytes[4],$path)
    }
    $width=[BitConverter]::ToUInt16($bytes,6)
    $height=[BitConverter]::ToUInt16($bytes,8)
    $depth=[BitConverter]::ToUInt16($bytes,10)
    if($width -lt 1 -or $height -lt 1 -or $depth -lt 1){throw "Invalid source image dimensions: $path"}
    $hasher=[Security.Cryptography.SHA256]::Create()
    try{
        $hash=([BitConverter]::ToString($hasher.ComputeHash($bytes,28,$bytes.Length-28))).Replace('-','').ToLowerInvariant()
    }finally{$hasher.Dispose()}
    return [pscustomobject]@{
        Width=[int]$width;Height=[int]$height;Depth=[int]$depth
        Version=[int]$bytes[3];Format=[int]$bytes[4];Flags=[int]$bytes[5]
        TotalBytes=[long]$bytes.Length;PayloadSha256=$hash
    }
}
$nativeNames=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($x in @($native.Entries | Where-Object { [string]$_.TypeName -ieq 'image' })){
    [void]$nativeNames.Add((Normalize-IwiName ([string]$x.Name)))
}
if($nativeNames.Count -ne 666){throw "Aztec native image root identity count drift: $($nativeNames.Count)"}
$sourceRoot=Join-Path ([string]$baseline.sourceRoot) 'nazi_zombie_aztec_V2.2'
if(-not (Test-Path -LiteralPath $sourceRoot -PathType Container)){throw "Aztec original PC map package missing: $sourceRoot"}
$archives=@(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.iwd' -File -Recurse -ErrorAction Stop)
if($archives.Count -ne 1){throw "Expected the single previously enumerated Aztec PC IWD, got $($archives.Count)."}
$originalIwd=$archives[0].FullName
$originalIwdSha=(Get-FileHash -LiteralPath $originalIwd -Algorithm SHA256).Hash
$existing=@(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.iwi' -File -Recurse -ErrorAction SilentlyContinue)
if($existing.Count -ne 0){throw "New loose Aztec IWIs appeared ($($existing.Count)); rerun PC source audit before accepting ownership."}

# All OAT input/output writes occur in a truly short physical temporary path.
# Never use native/oracle image pixels as the source, and never alter the
# successful 8,509-root or 114/114 RawFile validation FFs.
$temp=Join-Path ([IO.Path]::GetTempPath()) ('wawAzPx_'+[guid]::NewGuid().ToString('N').Substring(0,10))
if($temp.Length -gt 115){throw "OAT short-path workspace constraint failed: $temp"}
$shortRaw=Join-Path $temp 'raw\images'
$shortZone=Join-Path $temp 'zone_source'
$shortLinked=Join-Path $temp 'linked'
$shortReadback=Join-Path $temp 'readback'
$project='aztec_pc_iwd_2img'
$sourceCache=Join-Path $work 'original_pc_iwi'
$diagnosticFF=Join-Path $work 'linked_image_only'
$logRoot=Join-Path $work 'oat_logs'
New-Item -ItemType Directory -Path $shortRaw,$shortZone,$shortLinked,$shortReadback,$sourceCache,$diagnosticFF,$logRoot -Force | Out-Null
$items=New-Object 'System.Collections.Generic.List[object]'
$status='AZTEC_PC_IWD_XENON_IMAGE_ROUNDTRIP_BLOCKED'
$linkExit=-1;$unlinkExit=-1;$linkErrors=0;$unlinkErrors=0
$payloadExact=0;$headerExact=0
$results=New-Object 'System.Collections.Generic.List[object]'
$logLink=Join-Path $logRoot 'linker.log'
$logUnlink=Join-Path $logRoot 'unlinker.log'
try{
    $zip=[IO.Compression.ZipFile]::OpenRead($originalIwd)
    try{
        $entries=@($zip.Entries | Where-Object { [IO.Path]::GetExtension([string]$_.FullName) -ieq '.iwi' })
        if($entries.Count -ne 2){throw "The Aztec PC IWD source count changed; expected 2, got $($entries.Count)"}
        $names=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach($entry in $entries){
            $name=Normalize-IwiName ([string]$entry.FullName)
            if(-not $name -or $name.Contains('..') -or $name.Contains(':') -or
               -not $names.Add($name) -or -not $nativeNames.Contains($name)){
                throw "PC IWD IWI entry name does not match a unique native Aztec root: $($entry.FullName)"
            }
            $relative=$name.Replace('/','\')+'.iwi'
            $shortFile=Join-Path $shortRaw $relative
            $shortDir=Split-Path -Parent $shortFile
            New-Item -Path $shortDir -ItemType Directory -Force | Out-Null
            $stream=$entry.Open()
            $outStream=[IO.File]::Create($shortFile)
            try{$stream.CopyTo($outStream)}finally{$outStream.Dispose();$stream.Dispose()}
            $header=Read-Iwi $shortFile
            $cacheFile=Join-Path $sourceCache $relative
            New-Item -Path (Split-Path -Parent $cacheFile) -ItemType Directory -Force | Out-Null
            Copy-Item -LiteralPath $shortFile -Destination $cacheFile -Force
            $hash=(Get-FileHash -LiteralPath $shortFile -Algorithm SHA256).Hash
            if((Get-FileHash -LiteralPath $cacheFile -Algorithm SHA256).Hash -ne $hash){
                throw "PC image source copy SHA mismatch: $name"
            }
            $items.Add([pscustomobject]@{
                Name=$name;OriginalIwdEntry=[string]$entry.FullName
                OriginalIwdSha256=$originalIwdSha;PcSourceIwiSha256=$hash
                FileBytes=[long]$entry.Length;Header=$header
            })
        }
    }finally{$zip.Dispose()}
    if($items.Count -ne 2){throw "Expected two distinct legitimate PC Aztec IWD images, found $($items.Count)"}
    $zoneLines=New-Object 'System.Collections.Generic.List[string]'
    foreach($str in @('>game,T4','>platform,xbox360',('>name,'+$project),'')){$zoneLines.Add($str)}
    foreach($image in @($items.ToArray() | Sort-Object Name)){$zoneLines.Add('image,'+$image.Name)}
    [IO.File]::WriteAllLines((Join-Path $shortZone ($project+'.zone')),[string[]]$zoneLines.ToArray(),[Text.UTF8Encoding]::new($false))
    Write-Host '[AZTEC-PC-PIXEL] ===================================================='
    Write-Host '[AZTEC-PC-PIXEL] TWO REAL AZTEC PC IWIs -> T4 XENON ENCODE -> OAT READBACK'
    Write-Host "[AZTEC-PC-PIXEL] Legitimate PC IWD: $originalIwd"
    Write-Host '[AZTEC-PC-PIXEL] No oracle pixels. Image-only diagnostic FF. NOT FOR XBOX.'
    Write-Host '[AZTEC-PC-PIXEL] ===================================================='
    $old=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        Push-Location $temp
        try{
            $linkArgs=@('--verbose','--no-color','--base-folder',$temp,
                '--source-search-path',$shortZone,'--asset-search-path',(Join-Path $temp 'raw'),
                '--output-folder',$shortLinked,$project)
            & $linker @linkArgs 2>&1 | ForEach-Object {[string]$_} | Set-Content -LiteralPath $logLink -Encoding UTF8
            $linkExit=[int]$LASTEXITCODE
        }finally{Pop-Location}
    }finally{$ErrorActionPreference=$old}
    $linkErrors=@(Select-String -LiteralPath $logLink -Pattern '(?i)^\s*ERROR:').Count
    Write-Host "[AZTEC-PC-PIXEL] OAT Linker exit=$linkExit errors=$linkErrors"
    if($linkExit -ne 0 -or $linkErrors -gt 0){throw "PC-source Xenon image-only link failed; see $logLink"}
    $linked=@(Get-ChildItem -LiteralPath $shortLinked -Filter '*.ff' -File -Recurse -ErrorAction Stop)
    if($linked.Count -ne 1){throw "Expected exactly one new 2-image diagnostic FF, got $($linked.Count)"}
    $linkedFile=$linked[0].FullName
    $linkedCopy=Join-Path $diagnosticFF $linked[0].Name
    Copy-Item -LiteralPath $linkedFile -Destination $linkedCopy -Force
    if((Get-FileHash -LiteralPath $linkedCopy -Algorithm SHA256).Hash -ne
       (Get-FileHash -LiteralPath $linkedFile -Algorithm SHA256).Hash){
        throw 'Diagnostic image-only fastfile copy SHA mismatch.'
    }
    $old=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        Push-Location (Split-Path -Parent $linkedFile)
        try{
            $unlinkArgs=@('--verbose','--no-color','--include-assets','image','--image-format','IWI',
                '--output-folder',$shortReadback,$linkedFile)
            & $unlinker @unlinkArgs 2>&1 | ForEach-Object {[string]$_} | Set-Content -LiteralPath $logUnlink -Encoding UTF8
            $unlinkExit=[int]$LASTEXITCODE
        }finally{Pop-Location}
    }finally{$ErrorActionPreference=$old}
    $unlinkErrors=@(Select-String -LiteralPath $logUnlink -Pattern '(?i)^\s*ERROR:').Count
    Write-Host "[AZTEC-PC-PIXEL] OAT Unlinker exit=$unlinkExit errors=$unlinkErrors"
    if($unlinkExit -ne 0 -or $unlinkErrors -gt 0){throw "Independent Xbox image IWI readback failed; see $logUnlink"}
    $readBack=@(Get-ChildItem -LiteralPath $shortReadback -Filter '*.iwi' -File -Recurse -ErrorAction Stop)
    if($readBack.Count -ne 2){throw "Expected two Xbox-exported IWI files, saw $($readBack.Count)"}
    $readByName=@{}
    foreach($file in $readBack){
        $relative=$file.FullName.Substring($shortReadback.Length).TrimStart([char]'\')
        $name=Normalize-IwiName $relative
        if($readByName.ContainsKey($name)){throw "Duplicate IWI image output identity: $name"}
        $readByName[$name]=$file
    }
    foreach($image in $items.ToArray()){
        if(-not $readByName.ContainsKey($image.Name)){throw "Missing image readback: $($image.Name)"}
        $back=Read-Iwi $readByName[$image.Name].FullName
        $sameHeader=($back.Version -eq $image.Header.Version -and $back.Format -eq $image.Header.Format -and
            $back.Width -eq $image.Header.Width -and $back.Height -eq $image.Header.Height -and $back.Depth -eq $image.Header.Depth)
        $samePixels=($back.PayloadSha256 -eq $image.Header.PayloadSha256)
        if($sameHeader){$headerExact++}
        if($samePixels){$payloadExact++}
        $results.Add([pscustomobject]@{
            Name=$image.Name;OriginalIwdEntry=$image.OriginalIwdEntry
            SourcePcIwiSha256=$image.PcSourceIwiSha256
            InputPayloadSha256=$image.Header.PayloadSha256
            XboxReadbackPayloadSha256=$back.PayloadSha256
            HeaderExact=$sameHeader;PixelPayloadExact=$samePixels
            InputBytes=$image.Header.TotalBytes;XboxReadbackBytes=$back.TotalBytes
        })
        Write-Host "[AZTEC-PC-PIXEL] $($image.Name): headerExact=$sameHeader pixelPayloadExact=$samePixels"
    }
    if($headerExact -eq 2 -and $payloadExact -eq 2){
        $status='AZTEC_PC_IWD_XENON_IMAGE_ROUNDTRIP_PASS'
    }else{
        $status='AZTEC_PC_IWD_XENON_IMAGE_ROUNDTRIP_MISMATCH'
    }
}catch{
    Write-Host "[AZTEC-PC-PIXEL] BLOCKER: $($_.Exception.Message)"
    if(Test-Path -LiteralPath $logLink -PathType Leaf){
        foreach($line in @(Get-Content -LiteralPath $logLink -Tail 6)){Write-Host "[AZTEC-PC-PIXEL] LINK: $line"}
    }
    if(Test-Path -LiteralPath $logUnlink -PathType Leaf){
        foreach($line in @(Get-Content -LiteralPath $logUnlink -Tail 6)){Write-Host "[AZTEC-PC-PIXEL] UNLINK: $line"}
    }
}finally{
    if(Test-Path -LiteralPath $temp -PathType Container){Remove-Item -LiteralPath $temp -Recurse -Force}
}
$result=[ordered]@{
    schema='WaW360.AztecPcIwdXenonImageRoundTrip.v1'
    status=$status
    originalPcIwd=$originalIwd
    originalPcIwdSha256=$originalIwdSha
    pcImagesFound=$items.Count
    successfulXenonHeaderReadbacks=$headerExact
    successfulXenonPixelReadbacks=$payloadExact
    linkerExitCode=$linkExit;unlinkerExitCode=$unlinkExit
    linkerErrorLines=$linkErrors;unlinkerErrorLines=$unlinkErrors
    individualImages=$results.ToArray()
    sourcePcIwiCorpus=$sourceCache
    xboxImageOnlyDiagnosticFolder=$diagnosticFF
    fullAztecMapConverted=$false
    integratedInto8509RootGraph=$false
    nonImageShellsRemain=$true
    convertedMapsPromotionApproved=$false
    hardwareReady=$false
    notes=@(
        'Actual unmodified PC IWD source texture bytes were fed to OAT Xenon image-only Linker. Native Xbox fastfile supplied identity evidence only.',
        'Both the IWI header fields and payload SHA-256 must survive independent OAT Xbox readback for a PASS.',
        'The diagnostic image-only FF does not include Aztec materials, geometry, scripts, or complete map dependencies.',
        'No native Xbox donor image pixels, no stock retail game bytes, no full-map FF and no hardware-ready claim.'
    )
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
$json=Join-Path $work 'aztec_pc_iwd_xenon_image_roundtrip.json'
$txt=Join-Path $work 'aztec_pc_iwd_xenon_image_roundtrip.txt'
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $json -Encoding UTF8
$summary=New-Object 'System.Collections.Generic.List[string]'
$summary.Add("AZTEC PC IWD -> XBOX IMAGE-ONLY ROUNDTRIP: $status")
$summary.Add("PC source IWD=$originalIwd")
$summary.Add("PC source IWD SHA=$originalIwdSha")
$summary.Add("PC images=$($items.Count) headerExact=$headerExact payloadExact=$payloadExact")
$summary.Add("OAT Linker exit=$linkExit errors=$linkErrors; OAT Unlinker exit=$unlinkExit errors=$unlinkErrors")
foreach($v in $results.ToArray()){$summary.Add("$($v.Name): header=$($v.HeaderExact) payload=$($v.PixelPayloadExact) sha=$($v.InputPayloadSha256)")}
$summary.Add('Image-only diagnostic FF, not complete Aztec; do not deploy to Xbox or Converted Maps.')
[IO.File]::WriteAllLines($txt,[string[]]$summary.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-PIXEL] ===================== RESULT ========================='
Write-Host "[AZTEC-PC-PIXEL] status=$status"
Write-Host "[AZTEC-PC-PIXEL] Actual PC image payloads readback=$payloadExact/2; headers=$headerExact/2"
Write-Host "[AZTEC-PC-PIXEL] Report: $txt"
Write-Host '[AZTEC-PC-PIXEL] Source-backed image-only FF. NOT FOR XBOX.'
if($status -ne 'AZTEC_PC_IWD_XENON_IMAGE_ROUNDTRIP_PASS'){exit 4}
