[CmdletBinding()]
param(
    [string]$MatrixJson='',
    [string[]]$IncludeMaps=@('Backrooms','nazi_zombie_aztec_V2.2'),
    [ValidateSet('TrackedShell','Primary')]
    [string]$Variant='TrackedShell'
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# powershell.exe -File can pass comma-separated -IncludeMaps as ONE string.
# Normalize both Windows argument forms without matching unrelated maps.
$IncludeMaps=@($IncludeMaps | ForEach-Object {
    ([string]$_) -split ','
} | ForEach-Object { ([string]$_).Trim() } | Where-Object { $_ -ne '' })
# OAT root prefix bisection of EXISTING FFs only; never rebuild maps.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if([string]::IsNullOrWhiteSpace($MatrixJson)){
    $runs=Join-Path $repo 'output\multi_map_matrix\runs'
    if(-not(Test-Path -LiteralPath $runs -PathType Container)){throw "No matrix runs: $runs"}
    foreach($f in (Get-ChildItem -LiteralPath $runs -Recurse -File -Filter 'multi_map_matrix.json' | Sort-Object LastWriteTimeUtc -Descending)){
        try{
            $data=Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json
            if([string]$data.schema -eq 'WaW360.MultiMapConversionMatrix.v1' -and [string]$data.mode -eq 'Candidate'){
                $MatrixJson=$f.FullName
                break
            }
        }catch{Write-Host "[OAT-BISECT] Ignored damaged report $($f.FullName)"}
    }
    if([string]::IsNullOrWhiteSpace($MatrixJson)){throw "No Candidate matrix report under $runs"}
}
$MatrixJson=(Resolve-Path -LiteralPath $MatrixJson).Path
$matrix=Get-Content -LiteralPath $MatrixJson -Raw | ConvertFrom-Json
if([string]$matrix.schema -ne 'WaW360.MultiMapConversionMatrix.v1' -or [string]$matrix.mode -ne 'Candidate'){
    throw "Not a Candidate matrix: $MatrixJson"
}
$wanted=if($Variant -eq 'TrackedShell'){'hardware_test_main_xanim_native_layout_tracked_shell'}else{'hardware_test_main_xanim_native_layout'}
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
if(-not(Test-Path -LiteralPath $oat -PathType Leaf)){$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'}
if(-not(Test-Path -LiteralPath $oat -PathType Leaf)){throw "Pinned OAT Xenon missing: $oat"}
$dll=Join-Path $repo 'src\WaW360.Cli\bin\Release\net8.0-windows\WaW360.Cli.dll'
if(-not(Test-Path -LiteralPath $dll -PathType Leaf)){throw "Built CLI missing: $dll"}
$root=Split-Path -Path $MatrixJson -Parent
$stamp=Get-Date -Format 'yyyyMMdd_HHmmss'
$working=Join-Path ([IO.Path]::GetTempPath()) ('WMMB_'+$stamp+'_'+[guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $working -Force | Out-Null
$results=New-Object 'System.Collections.Generic.List[object]'
Write-Host '[OAT-BISECT] ==========================================================='
Write-Host "[OAT-BISECT] Mode=NO REBUILD. Variant=$Variant. OAT uses --list, not extraction."
Write-Host "[OAT-BISECT] Matrix=$MatrixJson"
foreach($m in $matrix.maps){
    $yes=$false
    foreach($pattern in $IncludeMaps){if([string]$m.map -like $pattern){$yes=$true;break}}
    if(-not $yes){continue}
    $name=[string]$m.map
    $matched=@($m.emittedCandidates | Where-Object {
        if($null -eq $_){return $false}
        (Split-Path -Path (Split-Path -Path ([string]$_.path) -Parent) -Leaf) -eq $wanted
    })
    if($matched.Count -ne 1){
        Write-Host "[OAT-BISECT] $name expected one $wanted FF; found $($matched.Count)."
        continue
    }
    $e=$matched[0]
    $ff=[string]$e.path
    if(-not(Test-Path -LiteralPath $ff -PathType Leaf)){
        Write-Host "[OAT-BISECT] $name existing TEMP FF missing: $ff"
        continue
    }
    $hash=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
    if($hash -ine [string]$e.sha256 -or [long](Get-Item -LiteralPath $ff).Length -ne [long]$e.bytes){
        throw "Candidate FF changed since prior matrix; refusing to bisect: $ff"
    }
    $safe=($name -replace '[^A-Za-z0-9_-]','_')
    $out=Join-Path $working $safe
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    $log=Join-Path $root ('oat_bisect_'+$stamp+'_'+$safe+'_'+$Variant+'.log')
    Write-Host "[OAT-BISECT] START $name; $($e.bytes) B; sha256=$hash"
    $exit=-1
    $status='EXEC_EXCEPTION'
    try{
        $stdout=Join-Path $out 'stdout.log'
        $stderr=Join-Path $out 'stderr.log'
        $quoted=(@($dll,'oat-root-bisect',$ff,$oat,$out) | ForEach-Object {'"'+([string]$_).Replace('"','\"')+'"'}) -join ' '
        $p=Start-Process -FilePath 'dotnet' -ArgumentList $quoted -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -Wait -NoNewWindow -ErrorAction Stop
        $exit=[int]$p.ExitCode
        $body=''
        if(Test-Path -LiteralPath $stdout){$body+=[IO.File]::ReadAllText($stdout)}
        if(Test-Path -LiteralPath $stderr){$body+=[Environment]::NewLine+[IO.File]::ReadAllText($stderr)}
        [IO.File]::WriteAllText($log,$body,[Text.UTF8Encoding]::new($false))
        if($body -match 'ZERO-ROOT CONTROL FAILED'){$status='ZERO_ROOT_CONTROL_FAILED'}
        elseif($body -match 'first FAIL active roots'){$status='FIRST_BAD_ROOT_IDENTIFIED'}
        elseif($body -match 'Full candidate now passes OAT'){$status='FULL_FF_PASSED_OAT_LIST'}
        else{$status=if($exit -eq 0){'NO_FRONTIER_REPORTED'}else{'BISECT_FAILED'}}
        Write-Host "[OAT-BISECT] RESULT $name = $status (exit=$exit)"
        foreach($line in @($body -split '[\r\n]+' | Where-Object {
            $_ -match '(?i)(control active=|first FAIL active roots|failing XAsset index|^type\s+:|ZERO-ROOT|Full candidate|invalid block)'
        } | Select-Object -Last 15)){Write-Host "[OAT-BISECT]   $line"}
    }catch{
        $message=[string]$_
        Write-Host "[OAT-BISECT] ERROR $name = $message"
        [IO.File]::WriteAllText($log,$message,[Text.UTF8Encoding]::new($false))
    }
    $results.Add([pscustomobject]@{
        map=$name;variant=$Variant;status=$status;exit=$exit
        candidate=$ff;sha256=$hash;bytes=[long]$e.bytes
        log=$log;probeDirectory=$out;hardwareReady=$false
    })
}
$report=Join-Path $root ('oat_root_bisect_'+$stamp+'.json')
[ordered]@{
    schema='WaW360.MultiMapOatRootBisect.v1'
    sourceMatrix=$MatrixJson;variant=$Variant
    note='First failing root under OAT --list only. Zero-root control must pass. Not Xbox hardware readiness.'
    results=@($results.ToArray())
}|ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host '[OAT-BISECT] ==========================================================='
Write-Host "[OAT-BISECT] JSON=$report"
Write-Host '[OAT-BISECT] No rebuild. Original FFs and saved candidates remain unchanged.'
