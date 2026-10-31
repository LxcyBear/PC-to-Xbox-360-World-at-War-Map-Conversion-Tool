[CmdletBinding()]
param(
    [int]$MaxZones=2,
    [string]$XboxSoundPackRoot=''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# Read genuine original Xbox 360 retail FFs verified by offline OAT
# listings. The stock name crosswalk is NOT evidence of loaded data;
# this stage attempts an actual OAT native sound sample extraction.
# It never invents silent audio or rewrites a native map fastfile.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$source=Join-Path $base 'pc_g11_flame_loaded_virtual_target_proof\aztec_flame_sound_alias_real_xbox_retail_stock_names.json'
$nativeGood=Join-Path $repo '.deps\aztec-w63-direct-ff-growth\scratch_emit\nazi_zombie_aztec.ff'
$goodGrowth=Join-Path $repo '.deps\aztec-w63-direct-ff-growth\aztec_weapon63_ff_growth_63.json'
$unlinker=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$reportDir=Join-Path $base 'pc_g11_flame_real_xbox_xma_source'
$report=Join-Path $reportDir 'aztec_six_real_original_xbox_sound_xma_source_provenance.json'
$sampleDir=Join-Path $repo '.deps\aztec-real-xma-samples' # short path for Windows native MAX_PATH
if($MaxZones -lt 1 -or $MaxZones -gt 3){throw 'Select between 1 and 3 authentic Xbox retail source FFs.'}
foreach($f in @($source,$nativeGood,$goodGrowth,$unlinker)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){
        throw "Real Xbox XMA source extraction requires existing genuine source/proof: $f"
    }
}
$x=Get-Content -LiteralPath $source -Raw|ConvertFrom-Json
$g=Get-Content -LiteralPath $goodGrowth -Raw|ConvertFrom-Json
$priorGoodSha=(Get-FileHash -LiteralPath $nativeGood -Algorithm SHA256).Hash
if([string]$x.schema -ne 'WaW360.AztecFlameRealXboxSoundStockNameCrosswalk.v1' -or
   [int]$x.realOriginalPcSoundAliasNames -ne 6 -or
   [int]$x.uniqueSourceSoundNamesWithOfflineXboxOwnedNameCandidate -ne 6 -or
   [int]$x.offlineOriginalXboxRetailZonesAccepted -lt 67 -or
   [int]$x.offlineOriginalXboxRetailZonesStaleOrInvalid -ne 0 -or
   [bool]$x.nativeXmaSourceAudioAndSampleGraphClosed -or
   [bool]$x.nativeXboxStockRuntimeResidencyProven -or
   [string]$g.newScratchFastFileSha256 -ine $priorGoodSha -or
   [long](Get-Item -LiteralPath $nativeGood).Length -ne 4514599L){
    throw 'Original PC-to-Xbox name proof or protected native diagnostic FF changed.'
}
if(-not [string]::IsNullOrWhiteSpace($XboxSoundPackRoot)){
    $XboxSoundPackRoot=(Resolve-Path -LiteralPath $XboxSoundPackRoot -ErrorAction Stop).Path
    if(-not(Test-Path -LiteralPath $XboxSoundPackRoot -PathType Container)){
        throw "Real Xbox sound pack directory does not exist: $XboxSoundPackRoot"
    }
}
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
$targets=@($x.realPcFlameSoundNamesToXboxStockNameEvidence)
if($targets.Count -ne 6){throw 'Expected exactly six genuine loaded original PC sound identities.'}
$byName=@{}
$zones=@{}
foreach($item in $targets){
    $name=[string]$item.originalPcLoadedSoundAliasName
    if([string]::IsNullOrWhiteSpace($name) -or $byName.ContainsKey($name)){
        throw 'Original-PC sound identity duplicate/empty.'
    }
    $byName[$name]=$true
    foreach($c in @($item.matchingXboxOatRetailNameEvidence)){
        if([string]$c.offlineXAssetEvidence -ne 'OAT_OWNED_BODY_NAME'){continue}
        $path=[IO.Path]::GetFullPath([string]$c.xboxRetailSource)
        if(-not(Test-Path -LiteralPath $path -PathType Leaf)){continue}
        if(-not $zones.ContainsKey($path)){
            $zones[$path]=[pscustomobject]@{
                path=$path
                name=[IO.Path]::GetFileName($path)
                aliases=(New-Object 'System.Collections.Generic.List[string]')
                base=[bool]$c.likelyStockBaseZone
            }
        }
        if(-not $zones[$path].aliases.Contains($name)){
            $zones[$path].aliases.Add($name)
        }
    }
}
if($byName.Count -ne 6 -or $zones.Count -eq 0){
    throw 'No actual original Xbox retail Sound XAsset ownership names can be resolved to present FF paths.'
}
$choices=@($zones.Values|Sort-Object -Property @{Expression={$_.aliases.Count};Descending=$true},@{Expression={$_.base};Descending=$true},@{Expression={$_.name};Descending=$false})
$chosen=New-Object 'System.Collections.Generic.List[object]'
$remaining=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($name in $byName.Keys){$null=$remaining.Add($name)}
foreach($zone in $choices){
    if($chosen.Count -ge $MaxZones){break}
    $new=@($zone.aliases|Where-Object {$remaining.Contains($_)})
    if($new.Count -eq 0){continue}
    $chosen.Add($zone)
    foreach($name in $new){$null=$remaining.Remove($name)}
    if($remaining.Count -eq 0){break}
}
Write-Host '[AZTEC-XMA-REAL] ====================================================='
Write-Host '[AZTEC-XMA-REAL] Actual Xbox retail Sound/LoadedSound XMA sample extraction'
Write-Host '[AZTEC-XMA-REAL] Only original Xbox owned Sound XAssets with exact six original PC alias names. No dummy audio.'
Write-Host "[AZTEC-XMA-REAL] Selected retail FFs=$($chosen.Count), alias names potentially covered=$($byName.Count-$remaining.Count)/6"
Write-Host '[AZTEC-XMA-REAL-PROGRESS] [####----------------] 20% | 67 offline original Xbox FF name+ownership sources pinned'
$zoneResults=New-Object 'System.Collections.Generic.List[object]'
$allSamples=New-Object 'System.Collections.Generic.List[object]'
$allPotential=New-Object 'System.Collections.Generic.List[object]'
$copied=@{}
for($zi=0;$zi -lt $chosen.Count;$zi++){
    $zone=$chosen[$zi]
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('AZXMA_'+[guid]::NewGuid().ToString('N').Substring(0,10))
    New-Item -ItemType Directory -Force -Path $temp|Out-Null
    $log=Join-Path $reportDir ('oat_xbox_sound_xma_'+$zi.ToString('D2')+'.log')
    $dump=Join-Path $temp 'oat'
    $stdout=Join-Path $temp 'out.log'
    $stderr=Join-Path $temp 'err.log'
    $code=-1
    $warnings=@()
    try{
        $srcDir=[IO.Path]::GetDirectoryName([string]$zone.path)
        $search=@($srcDir)
        if($XboxSoundPackRoot -and $XboxSoundPackRoot -ine $srcDir){
            $search+=@($XboxSoundPackRoot)
        }
        $cmd=New-Object 'System.Collections.Generic.List[string]'
        foreach($arg in @('--no-color','--include-assets','sound,loadedsound','--output-folder',$dump,'--search-path',($search -join ';'),[string]$zone.path)){
            $cmd.Add('"' + ([string]$arg).Replace('"','\"') + '"')
        }
        $proc=Start-Process -FilePath $unlinker -ArgumentList ($cmd -join ' ') -RedirectStandardOutput $stdout -RedirectStandardError $stderr -Wait -PassThru -NoNewWindow
        $code=[int]$proc.ExitCode
        $msg=([IO.File]::ReadAllText($stdout)+[Environment]::NewLine+[IO.File]::ReadAllText($stderr))
        [IO.File]::WriteAllText($log,$msg,[Text.UTF8Encoding]::new($false))
        $warnings=@($msg -split '[\r\n]+' | Where-Object {$_ -match '(?i)warn|missing|failed|could not find' }| Select-Object -First 30)
        if($code -ne 0){
            Write-Host "[AZTEC-XMA-REAL] Retail sound dump returned exit=$code source=$($zone.name); see $log"
        }else{
            $candidateFiles=@(Get-ChildItem -LiteralPath $dump -Recurse -File -Filter '*.xma' -ErrorAction SilentlyContinue)
            if($candidateFiles.Count -gt 20000){
                throw 'Refusing excessive native XMA candidate file count from an unexpected retail dump.'
            }
            foreach($file in $candidateFiles){
                # Empty or too-short dumped files cannot be valid evidence for a real XMA sample.
                if([long]$file.Length -lt 32L){continue}
                $relative=$file.FullName.Substring($dump.Length).TrimStart('\','/').Replace('\','/')
                $aliasMatch=$null
                foreach($name in @($byName.Keys)){
                    if($relative -match ('(?i)^sound/'+[regex]::Escape($name)+'\.\d+\.xma$')){
                        $aliasMatch=$name;break
                    }
                }
                $header=New-Object byte[] 24
                $read=0
                $stream=[IO.File]::OpenRead($file.FullName)
                try{$read=$stream.Read($header,0,$header.Length)}finally{$stream.Dispose()}
                $riff=($read -ge 22 -and
                    [Text.Encoding]::ASCII.GetString($header,0,4) -ceq 'RIFF' -and
                    [Text.Encoding]::ASCII.GetString($header,8,4) -ceq 'WAVE')
                $fmt=if($riff){[BitConverter]::ToUInt16($header,20)}else{0}
                $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
                if($aliasMatch){
                    if(-not(Test-Path -LiteralPath $sampleDir -PathType Container)){
                        New-Item -ItemType Directory -Force -Path $sampleDir|Out-Null
                    }
                    $stem=('{0}_{1}' -f $zi,($relative.Replace('/','_')))
                    $dest=Join-Path $sampleDir $stem
                    if($copied.ContainsKey($dest)){
                        throw "Unexpected duplicate actual XMA extraction output $dest"
                    }
                    Copy-Item -LiteralPath $file.FullName -Destination $dest -ErrorAction Stop
                    if((Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash -ine $hash){
                        throw 'Genuine Xbox XMA copied sample SHA mismatch.'
                    }
                    $copied[$dest]=$true
                    $allSamples.Add([pscustomobject][ordered]@{
                        originalPcFlameSoundAliasName=$aliasMatch
                        originalXboxRetailZoneName=[string]$zone.name
                        originalXboxRetailFastFile=[string]$zone.path
                        originalXboxOatDumpRelativeSoundPath=$relative
                        extractedGenuineXmaFile=$dest
                        sampleFileSha256=$hash
                        actualSampleBytes=[long]$file.Length
                        riffWaveHeaderPresent=$riff
                        riffWaveFmtTag=if($riff){('0x{0:X4}' -f $fmt)}else{$null}
                        riffXmaCodecHeaderProven=($riff -and $fmt -in @(0x165,0x166))
                        soundAliasRuntimeSampleBound=$false
                        noTranscodingOrFakeSamples=$true
                    })
                    Write-Host "[AZTEC-XMA-REAL] Extracted genuine Xbox audio source $aliasMatch : bytes=$($file.Length) RIFF-XMA=$($riff -and $fmt -in @(0x165,0x166))"
                }else{
                    $allPotential.Add([pscustomobject]@{
                        originalXboxRetailZone=[string]$zone.name
                        dumpRelativeFile=$relative
                        SHA256=$hash
                        bytes=[long]$file.Length
                        directFlameAliasMatch=$false
                    })
                }
            }
            $zoneMatches=@($allSamples.ToArray()|Where-Object {$_.originalXboxRetailZoneName -eq $zone.name})
            Write-Host "[AZTEC-XMA-REAL] zone=$($zone.name) XMA sample candidates=$($candidateFiles.Count), exact flame-alias files=$($zoneMatches.Count)"
        }
    }finally{
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
    $zoneResults.Add([pscustomobject][ordered]@{
        originalXboxRetailFastFile=[string]$zone.path
        originalXboxRetailFastFileSha256=(Get-FileHash -LiteralPath $zone.path -Algorithm SHA256).Hash
        originalXboxOatOwnedAliasNames=$zone.aliases.ToArray()
        originalXboxNameEvidenceOnlyNotPreloadProven=$true
        originalXboxOatSoundDumpExitCode=$code
        originalXboxOatSoundDumpLog=$log
        loggedWarningsFirst30=$warnings
    })
}
if((Get-FileHash -LiteralPath $nativeGood -Algorithm SHA256).Hash -ine $priorGoodSha){
    throw 'Protected original OAT-good native Aztec map FF was modified. Refuse XMA source result.'
}
$distinct=@($allSamples.ToArray()|Select-Object -ExpandProperty originalPcFlameSoundAliasName -Unique)
$reportBody=[ordered]@{
    schema='WaW360.AztecGenuineXboxRetailSoundXmaSourceExtraction.v1'
    status=if($allSamples.Count -gt 0){
        'GENUINE_XBOX_XMA_SAMPLE_FILES_EXTRACTED_ALIAS_BINDING_AND_RUNTIME_UNVERIFIED'
    }else{'ORIGINAL_XBOX_OAT_SOUND_DUMP_COMPLETED_NO_MATCHING_XMA_BYTES_EXTRACTED'}
    selectedRetailZoneCount=$chosen.Count
    originalPcFlameSoundAliasNames=6
    aliasesCoveredBySelectedOfflineOriginalXboxSoundRoots=6-$remaining.Count
    realSourceXmaSampleFilesExtracted=$allSamples.Count
    realSourceFlameAliasesWithDirectXmaFileMatches=$distinct.Count
    additionalUnboundXboxLoadedSoundXmaFilesSeen=$allPotential.Count
    xboxRuntimeSoundResidencyVerified=$false
    authenticXmaSampleToOriginalFlameSoundAliasBindingProven=$false
    nativeAztecFastFileSoundFilePointersRewritten=$false
    originalXboxRetailSourceSha256Rechecked=$true
    diagnosticGoodFastFileUnmodified=$true
    noDummyAudioOrDonorFFGraft=$true
    noXboxHardwareRun=$true
    sourceRetailZones=@($zoneResults.ToArray())
    exactAliasXmaSampleFiles=@($allSamples.ToArray())
    unboundLoadedSoundXmaCandidateFiles=@($allPotential.ToArray()|Select-Object -First 200)
}
$reportBody|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $report -Encoding UTF8
Write-Host '[AZTEC-XMA-REAL-PROGRESS] [####################] 100% | source Xbox real Sound/LoadedSound XMA extraction attempt; old native FF preserved'
Write-Host "[AZTEC-XMA-REAL] Exact genuine flamethrower source .xma files=$($allSamples.Count); aliases with source samples=$($distinct.Count)/6"
Write-Host "[AZTEC-XMA-REAL] Other loaded XMA files (unbound)=$($allPotential.Count)"
Write-Host "[AZTEC-XMA-REAL] STATUS=$($reportBody.status) JSON=$report"
Write-Host '[AZTEC-XMA-REAL] Not a valid CodXe or runtime sample binding proof. No fake/silent audio.'
