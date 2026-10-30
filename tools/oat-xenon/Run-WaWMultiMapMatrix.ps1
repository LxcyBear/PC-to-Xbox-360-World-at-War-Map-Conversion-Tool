[CmdletBinding()]
param(
    [string[]]$SourceRoots=@(),
    [string[]]$IncludeMaps=@(),
    [ValidateSet('Inventory','Analyze','Source','Candidate')]
    [string]$Mode='Analyze',
    [string]$OutputRoot='',
    [string[]]$RetailRoots=@(),
    [switch]$Force
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
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
    $old=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        & $ex @arguments 2>&1 | ForEach-Object { [string]$_ } | Set-Content -LiteralPath $log -Encoding UTF8
        return [int]$LASTEXITCODE
    }finally{ $ErrorActionPreference=$old }
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
if((ExecLog 'dotnet' @('build',$project,'-c','Release','--nologo') $buildLog) -ne 0){
    Get-Content -LiteralPath $buildLog -Tail 40
    throw "Shared build failed. Log=$buildLog"
}
if(-not(Test-Path -LiteralPath $dll)){throw "Built CLI missing: $dll"}
$rev=(& git -C $repo rev-parse HEAD).Trim()
$found=New-Object 'System.Collections.Generic.List[object]'
foreach($r in $SourceRoots){
    $root=[IO.Path]::GetFullPath($r)
    if(-not(Test-Path -LiteralPath $root -PathType Container)){
        Write-Host "[MULTIMAP] Source root missing: $root"
        continue
    }
    $list=Join-Path $run ('discovery_'+(HashText $root).Substring(0,8)+'.json')
    if((ExecLog 'dotnet' @($dll,'scan-maps-json',$root) $list) -ne 0){
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
        })
    }
}
$maps=@($found.ToArray()|Sort-Object id -Unique)
if($maps.Count -eq 0){throw 'No maps found. Set -SourceRoots or check installed map folders.'}
Write-Host "[MULTIMAP] Found $($maps.Count) maps. Compiled engine SHA/commit: $rev"
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
    $folder=Join-Path $run $m.id
    $output=Join-Path $folder 'conversion'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
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
            $exit=ExecLog 'dotnet' @($dll,$step,$m.root,$m.name,$output) $log
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
                        $code=ExecLog $oat @('--no-color','--output-folder',$oatOut,$copy) $oatLog
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
    $finger=@()
    foreach($p in @($m.main,$m.mod,$m.patch)|Where-Object{$_ -ne '' -and (Test-Path -LiteralPath $_ -PathType Leaf)}){
        $item=Get-Item -LiteralPath $p
        $finger+=("$($item.FullName)|$($item.Length)|$((Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash)")
    }
    $r=[ordered]@{
        map=$m.name;profile=$m.profile;sourceRoot=$m.root
        main=$m.main;mainBytes=if($m.main -and(Test-Path $m.main)){[int64](Get-Item $m.main).Length}else{0}
        iwdCount=$m.iwds;sourceFingerprint=HashText ($finger -join [Environment]::NewLine)
        status=$status;mode=$Mode;stages=@($events.ToArray())
        oatPassDiagnosticCount=$oatPasses;oatPassDiagnosticBytes=$oatBytes
        hardwareReady=$false;retailFileIndex=$retailIndex;resultFolder=$folder
    }
    $results.Add([pscustomobject]$r)
    $r|ConvertTo-Json -Depth 9|Set-Content -LiteralPath (Join-Path $folder 'result.json') -Encoding UTF8
    Write-Host "[MULTIMAP]   => $status | OAT diagnostic FF bytes=$oatBytes"
}
Write-Progress -Activity 'PC2XB3 multi-map conversion matrix' -Completed
$summary=[ordered]@{
    schema='WaW360.MultiMapConversionMatrix.v1'
    gitCommit=$rev;dateUtc=[DateTime]::UtcNow.ToString('o')
    mode=$Mode;sourceRoots=$SourceRoots;mapCount=$results.Count
    note='Jake evidence is guidance, not universal validated conversion rules. No native donor bytes, no fake minimum-size padding, no auto Xbox deployment. OAT PASS != playable.'
    sharedFaults=@($failures.GetEnumerator()|Sort-Object Name|ForEach-Object{[ordered]@{category=$_.Key;maps=$_.Value}})
    maps=@($results.ToArray())
}
$json=Join-Path $run 'multi_map_matrix.json'
$csv=Join-Path $run 'multi_map_matrix.csv'
$summary|ConvertTo-Json -Depth 11|Set-Content -LiteralPath $json -Encoding UTF8
$results.ToArray()|Select-Object map,profile,status,mainBytes,iwdCount,oatPassDiagnosticCount,oatPassDiagnosticBytes,hardwareReady,resultFolder|
    Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8
Write-Host '[MULTIMAP] ==========================================================='
Write-Host "[MULTIMAP] $($results.Count) maps processed | NO Xbox release authorized."
foreach($r in $results){Write-Host ("[MULTIMAP] {0,-26} {1,-33} OAT bytes={2}" -f $r.map,$r.status,$r.oatPassDiagnosticBytes)}
foreach($e in $summary.sharedFaults){Write-Host "[MULTIMAP] Shared blocker $($e.category) across $($e.maps) map(s)."}
Write-Host "[MULTIMAP] JSON=$json"
Write-Host "[MULTIMAP] CSV=$csv"
