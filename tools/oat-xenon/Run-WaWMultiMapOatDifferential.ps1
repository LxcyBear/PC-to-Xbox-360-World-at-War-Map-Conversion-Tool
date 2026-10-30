[CmdletBinding()]
param(
    [string]$MatrixJson='',
    [string[]]$IncludeMaps=@('nazi_zombie_FRUmansion','nazi_zombie_tower','Backrooms','nazi_zombie_aztec_V2.2'),
    [string[]]$VariantNames=@('hardware_test_main_xanim_native_layout_tracked_shell'),
    [string]$OatExe=''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# Validate EXISTING candidates via OAT only. No .NET build, no new FF emission.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if([string]::IsNullOrWhiteSpace($MatrixJson)){
    $runs=Join-Path $repo 'output\multi_map_matrix\runs'
    if(-not(Test-Path -LiteralPath $runs -PathType Container)){throw "No matrix runs: $runs"}
    foreach($candidate in (Get-ChildItem -LiteralPath $runs -File -Recurse -Filter 'multi_map_matrix.json' | Sort-Object LastWriteTimeUtc -Descending)){
        try{
            $d=Get-Content -LiteralPath $candidate.FullName -Raw | ConvertFrom-Json
            if([string]$d.schema -eq 'WaW360.MultiMapConversionMatrix.v1' -and
                [string]$d.mode -eq 'Candidate'){
                $MatrixJson=$candidate.FullName
                break
            }
        }catch{Write-Host "[OAT-DIFF] Skipped unreadable report: $($candidate.FullName)"}
    }
    if([string]::IsNullOrWhiteSpace($MatrixJson)){throw "No Candidate report found under $runs"}
}
$MatrixJson=(Resolve-Path -LiteralPath $MatrixJson).Path
$matrix=Get-Content -LiteralPath $MatrixJson -Raw | ConvertFrom-Json
if([string]$matrix.schema -ne 'WaW360.MultiMapConversionMatrix.v1' -or [string]$matrix.mode -ne 'Candidate'){
    throw "Invalid Candidate matrix report: $MatrixJson"
}
if([string]::IsNullOrWhiteSpace($OatExe)){
    $OatExe=Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
    if(-not(Test-Path -LiteralPath $OatExe -PathType Leaf)){
        $OatExe=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
    }
}
if(-not(Test-Path -LiteralPath $OatExe -PathType Leaf)){throw "Pinned OAT Xenon executable missing: $OatExe"}
$OatExe=(Resolve-Path -LiteralPath $OatExe).Path
$results=New-Object 'System.Collections.Generic.List[object]'
$runRoot=Split-Path -Path $MatrixJson -Parent
$stamp=Get-Date -Format 'yyyyMMdd_HHmmss'
$report=Join-Path $runRoot ('oat_differential_'+$stamp+'.json')
Write-Host '[OAT-DIFF] ==========================================================='
Write-Host "[OAT-DIFF] No rebuild. Matrix: $MatrixJson"
Write-Host "[OAT-DIFF] Checking: $($IncludeMaps -join ', ')"
foreach($m in $matrix.maps){
    $selected=$false
    foreach($pattern in $IncludeMaps){
        if([string]$m.map -like $pattern){$selected=$true;break}
    }
    if(-not $selected){continue}
    $mapName=[string]$m.map
    $emitted=@($m.emittedCandidates | Where-Object{$null -ne $_})
    if($emitted.Count -eq 0){
        Write-Host "[OAT-DIFF] ${mapName}: no emitted FF to recheck ($($m.status))"
        continue
    }
    foreach($variant in $VariantNames){
        $matching=@($emitted | Where-Object {
            $parent=Split-Path -Path ([string]$_.path) -Parent
            (Split-Path -Path $parent -Leaf) -eq $variant
        })
        if($matching.Count -eq 0){
            Write-Host "[OAT-DIFF] $mapName / ${variant}: not emitted"
            continue
        }
        foreach($entry in $matching){
            $file=[string]$entry.path
            if(-not(Test-Path -LiteralPath $file -PathType Leaf)){
                Write-Host "[OAT-DIFF] $mapName / ${variant}: candidate TEMP FF missing: $file"
                continue
            }
            $hash=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
            $bytes=[long](Get-Item -LiteralPath $file).Length
            if($hash -ine [string]$entry.sha256 -or $bytes -ne [long]$entry.bytes){
                throw "Candidate changed since matrix run. REFUSE TO RECHECK: $file"
            }
            $tmp=Join-Path ([IO.Path]::GetTempPath()) ('WMMdiff_'+[guid]::NewGuid().ToString('N').Substring(0,8))
            New-Item -ItemType Directory -Path $tmp -Force | Out-Null
            $shortName=($mapName+'_'+$variant) -replace '[^a-zA-Z0-9_-]','_'
            $log=Join-Path $runRoot ('oat_diff_'+$stamp+'_'+$shortName+'.log')
            $code=-1
            $status='OAT_EXEC_EXCEPTION'
            try{
                $copy=Join-Path $tmp ([IO.Path]::GetFileName($file))
                Copy-Item -LiteralPath $file -Destination $copy -ErrorAction Stop
                if((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ine $hash){throw 'OAT short-path copy SHA mismatch'}
                $out=Join-Path $tmp 'out'
                New-Item -ItemType Directory -Path $out -Force | Out-Null
                $stdout=Join-Path $tmp 'stdout.log'
                $stderr=Join-Path $tmp 'stderr.log'
                $argsLine=(@('--no-color','--output-folder',$out,$copy) | ForEach-Object {
                    '"'+([string]$_).Replace('"','\"')+'"'
                }) -join ' '
                $proc=Start-Process -FilePath $OatExe -ArgumentList $argsLine -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -Wait -NoNewWindow -ErrorAction Stop
                $code=[int]$proc.ExitCode
                $std=if(Test-Path -LiteralPath $stdout){[IO.File]::ReadAllText($stdout)}else{''}
                $err=if(Test-Path -LiteralPath $stderr){[IO.File]::ReadAllText($stderr)}else{''}
                $all=$std+[Environment]::NewLine+$err
                [IO.File]::WriteAllText($log,$all,[Text.UTF8Encoding]::new($false))
                # "Dumped weapon" is informational output and is NOT a
                # missing-asset failure. Differentiate it from the actual
                # "[STDERR] ERROR: Failed to open file weapons/..." case.
                $zero=$all -match '(?im)^\s*(?:Finished|Succeeded|Completed|Success(?:fully)?)\s+(?:with\s+)?0\s+warnings?\s*[,;]|^\s*(?:Finished|Succeeded|Completed|Success(?:fully)?)\s+(?:with\s+)?0\s+warnings?\s+and\s+0\s+errors?'
                if($zero -and $all -notmatch '(?i)0\s+errors?'){
                    $zero=$false
                }
                $bad=$all -match '(?im)(^\s*(?:\[STDERR\]\s*)?(?:ERROR\s*:|FATAL\s*:)|^\s*Failed\s+with|Unhandled exception|Loading fastfile failed)'
                if($code -eq 0 -and $zero -and -not $bad){
                    $status='OAT_PASS_DIAGNOSTIC_ONLY'
                }elseif($code -eq 0 -and -not $bad){
                    $status='OAT_ZERO_EXIT_SUMMARY_UNCONFIRMED'
                }elseif($all -match 'Zone tried to reference invalid block 7'){
                    $status='INVALID_BLOCK_7'
                }elseif($all -match '(?i)ERROR\s*:\s*Failed to open file.*weapons/'){
                    $status='WEAPON_OUTPUT_PATH_ERROR'
                }else{$status='OAT_FAILURE_OTHER'}
                Write-Host ("[OAT-DIFF] {0,-24} {1,-53} {2,-31} exit={3} bytes={4}" -f $mapName,$variant,$status,$code,$bytes)
                $reportLines=@($all -split '[\r\n]+' | Where-Object {
                    $_ -match '(?i)(invalid block|Failed with|Finished with|Succeeded with|Completed with|Success|ERROR:|FATAL:|0 errors|Failed to open file)'
                })
                if($reportLines.Count -eq 0){
                    $reportLines=@($all -split '[\r\n]+' | Where-Object {$_ -match '\S'} | Select-Object -Last 7)
                }
                foreach($line in @($reportLines | Select-Object -Last 10)){
                    Write-Host "[OAT-DIFF]   $line"
                }
                if($all -match '(?i)Dumped weapon') {
                    Write-Host '[OAT-DIFF]   NOTE: Dumped weapon is informational; only ERROR/failed summaries indicate a failure.'
                }
            }catch{
                $msg=[string]$_
                [IO.File]::WriteAllText($log,$msg,[Text.UTF8Encoding]::new($false))
                Write-Host "[OAT-DIFF] $mapName / $variant ERROR: $msg"
            }finally{
                Remove-Item -LiteralPath $tmp -Force -Recurse -ErrorAction SilentlyContinue
            }
            $results.Add([pscustomobject]@{
                map=$mapName;variant=$variant;status=$status;bytes=$bytes
                sha256=$hash;exit=$code;log=$log;hardwareReady=$false
            })
        }
    }
}
if($results.Count -eq 0){
    Write-Host '[OAT-DIFF] No FF variants were testable; check selected map names, prior candidate output, and TEMP retention.'
}
[ordered]@{
    schema='WaW360.MultiMapOatDifferential.v1'
    matrix=$MatrixJson;gitCommit=[string]$matrix.gitCommit
    note='Read-only OAT recheck of already-emitted FFs. No Xbox release approval.'
    results=@($results.ToArray())
}|ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host '[OAT-DIFF] ==========================================================='
Write-Host "[OAT-DIFF] JSON=$report"
Write-Host '[OAT-DIFF] No original PC FFs or candidate FFs modified. NO Xbox release authorized.'
