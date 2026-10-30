[CmdletBinding()]
param(
    [string[]]$SourceRoots=@(),
    [string[]]$IncludeMaps=@(),
    [string[]]$PriorityMaps=@('nazi_zombie_FRUmansion','nazi_zombie_tower'),
    [ValidateSet('Inventory','Analyze','Source','Candidate')]
    [string]$Mode='Analyze',
    [string]$OutputRoot='',
    [string[]]$RetailRoots=@(),
    [switch]$Force
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
trap {
    Write-Host "[MULTIMAP-ERROR] $($_.Exception.Message)"
    Write-Host "[MULTIMAP-ERROR] $($_.InvocationInfo.PositionMessage)"
    throw
}
# ONE build, all maps, isolated outputs. Candidate mode is a diagnostic
# build plus real OAT checks. NEVER deploy FFs to any Xbox folder.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if(-not $SourceRoots -or $SourceRoots.Count -eq 0){
    $SourceRoots=@(Join-Path (Split-Path -Parent $repo) 'PC Maps')
}
if([string]::IsNullOrWhiteSpace($OutputRoot)){
    $OutputRoot=Join-Path $repo 'output\multi_map_matrix'
}
$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
$run=Join-Path $OutputRoot ('runs\run_'+(Get-Date -Format 'yyyyMMdd_HHmmss')+'_'+[guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $run -Force | Out-Null
function HashText([string]$text){
    $sha=[Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text)))).Replace('-','') }
    finally { $sha.Dispose() }
}
function SafeName([string]$text){ return ($text -replace '[^A-Za-z0-9_-]','_').Trim('_') }
function ExecLog([string]$ex,[string[]]$arguments,[string]$log){
    # Start-Process returns ONE strongly typed ExitCode, avoiding PowerShell
    # 5.1 native-command pipeline/error-stream array coercion.
    # Each argument quoted separately to preserve paths with whitespace.
    $stdout=$log+'.stdout.tmp'
    $stderr=$log+'.stderr.tmp'
    foreach($tmp in @($stdout,$stderr)){
        if(Test-Path -LiteralPath $tmp){Remove-Item -LiteralPath $tmp -Force}
    }
    try{
        $argumentLine=(@($arguments | ForEach-Object {
            $a=[string]$_
            '"'+($a.Replace('"','\"'))+'"'
        }) -join ' ')
        $process=Start-Process -FilePath $ex -ArgumentList $argumentLine -RedirectStandardOutput $stdout -RedirectStandardError $stderr -NoNewWindow -PassThru -Wait -ErrorAction Stop
        $code=[int]$process.ExitCode
        $lines=New-Object 'System.Collections.Generic.List[string]'
        if(Test-Path -LiteralPath $stdout){
            foreach($line in [IO.File]::ReadAllLines($stdout)){$lines.Add($line)}
        }
        if(Test-Path -LiteralPath $stderr){
            foreach($line in [IO.File]::ReadAllLines($stderr)){$lines.Add('[STDERR] '+$line)}
        }
        [IO.File]::WriteAllLines($log,$lines.ToArray(),[Text.UTF8Encoding]::new($false))
        return $code
    }finally{
        foreach($tmp in @($stdout,$stderr)){
            Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
        }
    }
}
function Fault([string]$log){
    if(-not(Test-Path -LiteralPath $log)){return 'NO_LOG'}
    $txt=Get-Content -LiteralPath $log -Raw
    if($txt -match '(?i)outofmemory|memory pressure|allocate memory'){return 'MEMORY_PRESSURE_SUSPECT'}
    if($txt -match '(?i)invalid block|bad pointer|packed pointer'){return 'FF_POINTER_OR_BLOCK_LAYOUT'}
    if($txt -match '(?i)fxeffect|fx serializer'){return 'FX_SERIALIZER'}
    if($txt -match '(?i)xmodel|xsurface'){return 'XMODEL'}
    if($txt -match '(?i)xanim'){return 'XANIM'}
    if($txt -match '(?i)image|iwi|dds|material'){return 'IMAGE_MATERIAL'}
    if($txt -match '(?i)scriptstring|gsc|rawfile'){return 'SCRIPT_RAWFILE'}
    if($txt -match '(?i)missing|not found|could not find'){return 'MISSING_DEPENDENCY'}
    return 'OTHER'
}
$project=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
[xml]$xml=Get-Content -LiteralPath $project -Raw
$target=[string](@($xml.Project.PropertyGroup|ForEach-Object{$_.TargetFramework}|Where-Object{$_})|Select-Object -First 1)
if(-not $target){throw 'CLI target framework unknown.'}
$dll=Join-Path $repo ("src\WaW360.Cli\bin\Release\"+$target+"\WaW360.Cli.dll")
Write-Host "[MULTIMAP] Shared engine: compiling ONCE ($target). Mode=$Mode"
$buildLog=Join-Path $run 'build.log'
if((ExecLog -ex 'dotnet' -arguments @('build',$project,'-c','Release','--nologo') -log $buildLog) -ne 0){
    Get-Content -LiteralPath $buildLog -Tail 40
    throw "Shared build failed. Log=$buildLog"
}
if(-not(Test-Path -LiteralPath $dll)){throw "Built CLI missing: $dll"}
$rev=(& git -C $repo rev-parse HEAD).Trim()
# Historic Jake reports are advisory and never release/fit validators.
$rulesPath=Join-Path $PSScriptRoot 'multimap_conversion_rules.json'
$jakePath=Join-Path $PSScriptRoot 'jake_reference_map_reports.json'
$rules=Get-Content -LiteralPath $rulesPath -Raw | ConvertFrom-Json
$jake=Get-Content -LiteralPath $jakePath -Raw | ConvertFrom-Json
if([string]$rules.schema -ne 'WaW360.SharedConversionEvidenceRules.v1' -or
   [string]$jake.schema -ne 'WaW360.JakeReferenceMapOutcomes.v1' -or
   [int]$jake.reportCount -ne 39){
    throw 'Portable rules/Jake reference report schema changed.'
}
Write-Host "[MULTIMAP] Loaded $(@($rules.gates).Count) scoped evidence rules and $(@($jake.reports).Count) historic Jake report samples."
$found=New-Object 'System.Collections.Generic.List[object]'
foreach($r in $SourceRoots){
    $root=[IO.Path]::GetFullPath($r)
    if(-not(Test-Path -LiteralPath $root -PathType Container)){
        Write-Host "[MULTIMAP] Source root missing: $root"
        continue
    }
    $list=Join-Path $run ('discovery_'+(HashText $root).Substring(0,8)+'.json')
    if((ExecLog -ex 'dotnet' -arguments @($dll,'scan-maps-json',$root) -log $list) -ne 0){
        throw "Map discovery failed: $list"
    }
    $maps=@(Get-Content -LiteralPath $list -Raw | ConvertFrom-Json)
    foreach($m in $maps){
        if($IncludeMaps.Count -gt 0 -and @($IncludeMaps|Where-Object{[string]$m.name -like [string]$_}).Count -eq 0){continue}
        $found.Add([pscustomobject]@{
            id=(SafeName ([string]$m.name))+'_'+(HashText $root).Substring(0,8)
            name=[string]$m.name;root=$root;profile=[string]$m.profile
            main=[string]$m.mainFastFile;mod=[string]$m.modFastFile
            patch=[string]$m.patchFastFile;iwds=[int]$m.iwdCount
            sourceFolders=@($m.sourceFolders)
        })
    }
}
$maps=@($found.ToArray()|Sort-Object id -Unique)
if($maps.Count -eq 0){throw 'No maps found. Set -SourceRoots or check installed map folders.'}
# Work on requested priority maps FIRST, then every other discovered map.
# Never fabricate or overwrite source FFs if a priority name is absent.
$priority=New-Object 'System.Collections.Generic.List[object]'
$other=New-Object 'System.Collections.Generic.List[object]'
foreach($m in $maps){
    $match=$false
    foreach($pattern in $PriorityMaps){
        if($m.name -like $pattern){$match=$true;break}
    }
    if($match){$priority.Add($m)}else{$other.Add($m)}
}
$maps=@($priority.ToArray())+@($other.ToArray())
foreach($pattern in $PriorityMaps){
    if(@($maps|Where-Object{$_.name -like $pattern}).Count -eq 0){
        Write-Host "[MULTIMAP] PRIORITY MAP NOT DETECTED: $pattern (no source created, check folder/FF name)"
    }
}
Write-Host "[MULTIMAP] Found $($maps.Count) maps. First priorities: $($PriorityMaps -join ', ')"
Write-Host "[MULTIMAP] Compiled engine SHA/commit: $rev"
# Optional SHARED RETAIL FILE LOCATION index. This does not decode FF
# assets or assert any XAsset identity. Avoid redundant scans by saving it.
$retailIndex=''
if($RetailRoots.Count -gt 0){
    $retailIndex=Join-Path $OutputRoot 'retail_file_locations_only.json'
    if(-not(Test-Path -LiteralPath $retailIndex) -or $Force){
        $files=New-Object 'System.Collections.Generic.List[object]'
        foreach($r in $RetailRoots){
            if(-not(Test-Path -LiteralPath $r -PathType Container)){continue}
            foreach($f in (Get-ChildItem -LiteralPath $r -File -Recurse -ErrorAction SilentlyContinue|
                Where-Object {$_.Extension -in @('.ff','.iwd','.pak')})){
                $files.Add([ordered]@{
                    name=$f.Name;path=$f.FullName;bytes=[int64]$f.Length
                    assetIdentitiesVerified=$false
                })
            }
        }
        [ordered]@{
            schema='WaW360.SharedRetailFileLocations.v1'
            indexedAtUtc=[DateTime]::UtcNow.ToString('o')
            note='Physical file locations only. NOT decoded XAsset identity proofs.'
            files=@($files.ToArray())
        }|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $retailIndex -Encoding UTF8
    }
}
$failures=@{}
$results=New-Object 'System.Collections.Generic.List[object]'
$steps=if($Mode -eq 'Inventory'){@()}elseif($Mode -eq 'Analyze'){@('stage4-normal-analyze')}elseif($Mode -eq 'Source'){@('stage4-normal-analyze','linker-v2-source')}else{@('stage4-normal-analyze','linker-v2-source','linker-v2-main-structural')}
$i=0
foreach($m in $maps){
    $i++
    Write-Progress -Activity 'PC2XB3 multi-map conversion matrix' -Status "$i/$($maps.Count) $($m.name)" -PercentComplete ([int](100*($i-1)/$maps.Count))
    Write-Host "[MULTIMAP] [$i/$($maps.Count)] $($m.name) profile=$($m.profile)"
    $jakeMatches=@($jake.reports | Where-Object {
        [string]$_.map -ieq [string]$m.name
    })
    $jakeMatch=$null
    if($jakeMatches.Count -eq 1){
        $jakeMatch=$jakeMatches[0]
        Write-Host "[MULTIMAP]   Jake historical reference: $($jakeMatch.jakeReportedZoneMemoryMB) MB (Jake's reported unit) ESTIMATE, $($jakeMatch.jakeHeuristicLoadProbabilityPct)% HEURISTIC, NOT Xbox verification."
    }
    $folder=Join-Path $run $m.id
    $output=Join-Path $folder 'conversion'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $finger=@()
    foreach($inputFile in (@($m.main,$m.mod,$m.patch) | Where-Object{
        $_ -ne '' -and (Test-Path -LiteralPath $_ -PathType Leaf)
    })){
        $meta=Get-Item -LiteralPath $inputFile
        $finger+=("$($meta.FullName)|$($meta.Length)|$((Get-FileHash -LiteralPath $inputFile -Algorithm SHA256).Hash)")
    }
    # Hash actual FF/IWD inputs; stamp auxiliary source files so loose
    # scripts, images and sounds invalidate a previously PASSING run.
    foreach($sourceFolder in @($m.sourceFolders)){
        if(-not(Test-Path -LiteralPath $sourceFolder -PathType Container)){continue}
        foreach($file in (Get-ChildItem -LiteralPath $sourceFolder -File -Recurse -ErrorAction SilentlyContinue)){
            if($file.Extension -notin @('.ff','.iwd','.gsc','.csc','.iwi','.wav','.dds','.rmb','.json','.cfg')){continue}
            $stamp=if($file.Extension -in @('.ff','.iwd')){
                (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            }else{[string]$file.LastWriteTimeUtc.Ticks}
            $finger+=("$($file.FullName)|$($file.Length)|$stamp")
        }
    }
    $sourceHash=HashText (($finger | Sort-Object -Unique) -join [Environment]::NewLine)
    $cacheDir=Join-Path $OutputRoot 'pass_cache'
    $cacheFile=Join-Path $cacheDir ($m.id+'_'+$Mode+'.json')
    if(-not $Force -and (Test-Path -LiteralPath $cacheFile -PathType Leaf)){
        $cached=Get-Content -LiteralPath $cacheFile -Raw | ConvertFrom-Json
        $reuse=[string]$cached.schema -eq 'WaW360.MultiMapPassCache.v1' -and
            [string]$cached.gitCommit -eq $rev -and
            [string]$cached.sourceFingerprint -eq $sourceHash -and
            [string]$cached.mode -eq $Mode -and
            [string]$cached.mapId -eq $m.id -and
            (Test-Path -LiteralPath ([string]$cached.result.resultFolder) -PathType Container)
        if($reuse){
            foreach($artifact in @($cached.verifiedOatArtifacts | Where-Object { $null -ne $_ })){
                if(-not(Test-Path -LiteralPath ([string]$artifact.file) -PathType Leaf) -or
                   (Get-FileHash -LiteralPath ([string]$artifact.file) -Algorithm SHA256).Hash -ine [string]$artifact.sha256){
                    $reuse=$false
                    break
                }
            }
        }
        if($reuse){
            $results.Add($cached.result)
            Write-Host '[MULTIMAP]   PASS CACHE HIT: source files + Git revision + stage mode unchanged.'
            continue
        }
    }
    $status='INVENTORIED'
    $events=New-Object 'System.Collections.Generic.List[object]'
    $newFfs=New-Object 'System.Collections.Generic.List[string]'
    $oatPasses=0
    $oatBytes=[long]0
    if([string]::IsNullOrWhiteSpace($m.main)){
        $status='NO_PC_MAIN_FF'
    }else{
        foreach($step in $steps){
            $prior=@{}
            if($step -eq 'linker-v2-main-structural'){
                foreach($f in (Get-ChildItem -LiteralPath $output -File -Recurse -Filter '*.ff' -ErrorAction SilentlyContinue)){
                    $prior[$f.FullName]="$($f.Length)/$($f.LastWriteTimeUtc.Ticks)"
                }
            }
            $log=Join-Path $folder ($step+'.log')
            $exit=ExecLog -ex 'dotnet' -arguments @($dll,$step,$m.root,$m.name,$output) -log $log
            $ok=($exit -eq 0)
            $events.Add([ordered]@{step=$step;exit=$exit;pass=$ok;log=$log})
            Write-Host "[MULTIMAP]   $step => $(if($ok){'PASS'}else{'BLOCKED'}) ($exit)"
            if(-not $ok){
                $key=Fault $log
                $status="BLOCKED_$key"
                if(-not $failures.ContainsKey($key)){$failures[$key]=0}
                $failures[$key]++
                Get-Content -LiteralPath $log -Tail 6|ForEach-Object{Write-Host "[MULTIMAP]   $_"}
                break
            }
            $status="PASSED_$step"
            if($step -eq 'linker-v2-main-structural'){
                foreach($f in (Get-ChildItem -LiteralPath $output -File -Recurse -Filter '*.ff' -ErrorAction SilentlyContinue)){
                    $stamp="$($f.Length)/$($f.LastWriteTimeUtc.Ticks)"
                    if(-not $prior.ContainsKey($f.FullName) -or $prior[$f.FullName] -ne $stamp){$newFfs.Add($f.FullName)}
                }
                if($newFfs.Count -eq 0){$status='NO_NEW_FF_EMITTED';break}
                $oat=Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
                if(-not(Test-Path -LiteralPath $oat)){
                    $oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
                }
                if(-not(Test-Path -LiteralPath $oat)){ $status='OAT_NOT_INSTALLED';break }
                $passAll=$true
                $ord=0
                foreach($ff in $newFfs){
                    $ord++
                    $tmp=Join-Path ([IO.Path]::GetTempPath()) ('WawMatrix_'+[guid]::NewGuid().ToString('N').Substring(0,8))
                    New-Item -ItemType Directory -Path $tmp -Force|Out-Null
                    try{
                        $copy=Join-Path $tmp 'candidate.ff'
                        Copy-Item -LiteralPath $ff -Destination $copy
                        $sha=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
                        if((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ine $sha){throw "Short-path FF copy changed: $ff"}
                        $oatLog=Join-Path $folder ("oat_"+$ord+'.log')
                        $oatOut=Join-Path $tmp 'out'
                        New-Item -ItemType Directory -Path $oatOut -Force|Out-Null
                        $code=ExecLog -ex $oat -arguments @('--no-color','--output-folder',$oatOut,$copy) -log $oatLog
                        $tail=Get-Content -LiteralPath $oatLog -Raw
                        $pass=($code -eq 0 -and
                            $tail -match '(?i)Finished with\s+0\s+warnings?\s*,\s*0\s+errors?' -and
                            $tail -notmatch '(?im)^\s*(ERROR\s*:|FATAL\s*:|Unhandled exception)')
                        $events.Add([ordered]@{
                            step='OAT_DIAGNOSTIC';file=$ff;bytes=[int64](Get-Item $ff).Length
                            sha256=$sha;exit=$code;pass=$pass;log=$oatLog
                        })
                        Write-Host "[MULTIMAP]   OAT $ord/$($newFfs.Count): $(if($pass){'PASS - SCRATCH'}else{'FAIL'})"
                        if($pass){$oatPasses++;$oatBytes+=[int64](Get-Item $ff).Length}
                        else{$passAll=$false}
                    }finally{Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue}
                }
                $status=if($passAll){'OAT_PASS_DIAGNOSTIC_ONLY'}else{'OAT_REJECTED_NO_XBOX'}
            }
        }
    }
    $historyDir=Join-Path $OutputRoot 'verified_size_history'
    New-Item -ItemType Directory -Path $historyDir -Force | Out-Null
    $historyFile=Join-Path $historyDir ($m.id+'.json')
    $history=@()
    if(Test-Path -LiteralPath $historyFile -PathType Leaf){
        $histDoc=Get-Content -LiteralPath $historyFile -Raw | ConvertFrom-Json
        if([string]$histDoc.schema -ne 'WaW360.MultiMapOatCandidateHistory.v1' -or
           [string]$histDoc.mapId -ne [string]$m.id){
            throw "Map size-history schema or identity changed: $historyFile"
        }
        $history=@($histDoc.entries)
    }
    $priorVerifiedBytes=$null
    $deltaVerifiedBytes=$null
    $candidateArtifactHash=$null
    if($history.Count -gt 0){$priorVerifiedBytes=[long]$history[-1].bytes}
    if($status -eq 'OAT_PASS_DIAGNOSTIC_ONLY' -and $oatPasses -gt 0){
        $artifacts=@($events.ToArray()|Where-Object{$_.step -eq 'OAT_DIAGNOSTIC' -and $_.pass}|
            ForEach-Object{ [string]$_.sha256 }|Sort-Object)
        $candidateArtifactHash=HashText ($artifacts -join '|')
        if($null -eq $priorVerifiedBytes){$deltaVerifiedBytes=[long]$oatBytes}
        else{$deltaVerifiedBytes=[long]$oatBytes - [long]$priorVerifiedBytes}
        if($history.Count -eq 0 -or
           [string]$history[-1].artifactSetSha256 -ine $candidateArtifactHash){
            $history+=([pscustomobject]@{
                utc=[DateTime]::UtcNow.ToString('o')
                gitCommit=$rev;sourceFingerprintSha256=$sourceHash
                bytes=[long]$oatBytes;deltaBytes=$deltaVerifiedBytes
                artifactSetSha256=$candidateArtifactHash
                validatedOatFastFiles=$oatPasses;hardwareReady=$false
            })
            [ordered]@{
                schema='WaW360.MultiMapOatCandidateHistory.v1'
                mapId=$m.id;entries=@($history)
            }|ConvertTo-Json -Depth 9|Set-Content -LiteralPath $historyFile -Encoding UTF8
        }
        Write-Host "[MULTIMAP-SIZE] $($m.name): OAT-verified diagnostic=$oatBytes bytes; prior=$priorVerifiedBytes; delta=$deltaVerifiedBytes; NO Xbox approval"
    }
    $r=[ordered]@{
        map=$m.name;profile=$m.profile;sourceRoot=$m.root
        main=$m.main;mainBytes=if($m.main -and (Test-Path -LiteralPath $m.main -PathType Leaf)){[int64](Get-Item -LiteralPath $m.main).Length}else{0}
        iwdCount=$m.iwds;sourceFingerprint=$sourceHash
        status=$status;mode=$Mode;stages=@($events.ToArray())
        oatPassDiagnosticCount=$oatPasses;oatPassDiagnosticBytes=$oatBytes
        previousOatDiagnosticBytes=$priorVerifiedBytes;verifiedDeltaBytes=$deltaVerifiedBytes
        candidateArtifactSetSha256=$candidateArtifactHash;sizeHistory=$historyFile
        hardwareReady=$false;retailFileIndex=$retailIndex;resultFolder=$folder
        jakeHistoricEstimate=$jakeMatch
        jakeEstimateIsHardwareProof=$false
    }
    $results.Add([pscustomobject]$r)
    $r|ConvertTo-Json -Depth 9|Set-Content -LiteralPath (Join-Path $folder 'result.json') -Encoding UTF8
    if($status -like 'PASSED_*' -or $status -eq 'OAT_PASS_DIAGNOSTIC_ONLY' -or $status -eq 'INVENTORIED'){
        New-Item -ItemType Directory -Path $cacheDir -Force|Out-Null
        $oatVerified=@($events.ToArray()|Where-Object{$_.step -eq 'OAT_DIAGNOSTIC' -and $_.pass}|
            ForEach-Object{[ordered]@{file=$_.file;sha256=$_.sha256}})
        [ordered]@{
            schema='WaW360.MultiMapPassCache.v1'
            gitCommit=$rev;mode=$Mode;mapId=$m.id;sourceFingerprint=$sourceHash
            verifiedOatArtifacts=$oatVerified;result=$r
        }|ConvertTo-Json -Depth 11|Set-Content -LiteralPath $cacheFile -Encoding UTF8
    }
    Write-Host "[MULTIMAP]   => $status | OAT diagnostic FF bytes=$oatBytes"
}
Write-Progress -Activity 'PC2XB3 multi-map conversion matrix' -Completed
$summary=[ordered]@{
    schema='WaW360.MultiMapConversionMatrix.v1'
    gitCommit=$rev;dateUtc=[DateTime]::UtcNow.ToString('o')
    mode=$Mode;sourceRoots=$SourceRoots;mapCount=$results.Count
    sharedRulesLedger=$rulesPath
    jakeHistoricReportRegistry=$jakePath
    sharedRuleCount=@($rules.gates).Count
    note='Jake evidence is guidance, not universal validated conversion rules. No native donor bytes, no fake minimum-size padding, no auto Xbox deployment. OAT PASS != playable.'
    sharedFaults=@($failures.GetEnumerator()|Sort-Object Name|ForEach-Object{[ordered]@{category=$_.Key;maps=$_.Value}})
    maps=@($results.ToArray())
}
$json=Join-Path $run 'multi_map_matrix.json'
$csv=Join-Path $run 'multi_map_matrix.csv'
$summary|ConvertTo-Json -Depth 11|Set-Content -LiteralPath $json -Encoding UTF8
$results.ToArray()|Select-Object map,profile,status,mainBytes,iwdCount,oatPassDiagnosticCount,oatPassDiagnosticBytes,previousOatDiagnosticBytes,verifiedDeltaBytes,hardwareReady,resultFolder|
    Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8
Write-Host '[MULTIMAP] ==========================================================='
Write-Host "[MULTIMAP] $($results.Count) maps processed | NO Xbox release authorized."
foreach($r in $results){Write-Host ("[MULTIMAP] {0,-26} {1,-33} OAT bytes={2}" -f $r.map,$r.status,$r.oatPassDiagnosticBytes)}
foreach($e in $summary.sharedFaults){Write-Host "[MULTIMAP] Shared blocker $($e.category) across $($e.maps) map(s)."}
Write-Host "[MULTIMAP] JSON=$json"
Write-Host "[MULTIMAP] CSV=$csv"
