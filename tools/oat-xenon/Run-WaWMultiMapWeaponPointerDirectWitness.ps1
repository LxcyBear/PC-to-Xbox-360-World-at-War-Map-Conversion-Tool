[CmdletBinding()]
param(
    [string]$MapName='nazi_zombie_aztec_V2.2',
    [switch]$BuildDumper
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$runs=Join-Path $repo 'output\multi_map_matrix\runs'
$dumper=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$capPath=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$capName='t4-pc-weapon-virtual-pointer-direct-string-v1'
$audits=@(Get-ChildItem -LiteralPath $runs -Recurse -File -Filter 'weapon_name_provenance_*.json' | Sort-Object LastWriteTimeUtc -Descending)
$audit=$null
foreach($f in $audits){
    try {
        $obj=Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json
        if([string]$obj.schema -eq 'WaW360.WeaponNameAudit.v1' -and
           @($obj.maps | Where-Object {[string]$_.map -ieq $MapName}).Count -eq 1){
            $audit=$obj;break
        }
    }catch{}
}
if($null -eq $audit){throw 'No original PC WeaponDef source provenance audit. Run Run-WaWMultiMapWeaponNameAudit.ps1.'}
$map=@($audit.maps | Where-Object {[string]$_.map -ieq $MapName})[0]
if([int]$map.syntheticPackedNameCount -ne 10){throw 'Expected exactly ten packed WeaponDef names from Aztec proof corpus.'}
# Cross-check original PC FF SHA256 with the authoritative candidate's source
# archive, rather than merely verifying our copied temporary FF.
$matrixPath=[string]$audit.matrix
if(-not(Test-Path -LiteralPath $matrixPath -PathType Leaf)){throw "Name audit Candidate matrix no longer exists: $matrixPath"}
$matrix=Get-Content -LiteralPath $matrixPath -Raw | ConvertFrom-Json
$matrixMap=@($matrix.maps | Where-Object {[string]$_.map -ieq $MapName})
if($matrixMap.Count -ne 1){throw 'Weapon name audit matrix has no unique Aztec source map.'}
$archiveFile=Join-Path ([string]$matrixMap[0].conversionOutputFolder) (Join-Path $MapName (Join-Path 'linker_v2_source_archive' ($MapName+'_linker_v2_source_archive.json')))
if(-not(Test-Path -LiteralPath $archiveFile -PathType Leaf)){throw "Source-owned FF SHA ledger missing: $archiveFile"}
$sourceArchive=Get-Content -LiteralPath $archiveFile -Raw | ConvertFrom-Json
if(-not(Test-Path -LiteralPath $dumper -PathType Leaf) -or
   -not(Test-Path -LiteralPath $capPath -PathType Leaf) -or
   @((Get-Content -LiteralPath $capPath -Raw | ConvertFrom-Json).capabilities) -notcontains $capName){
    if(-not $BuildDumper){throw 'PC OAT pointer witness not installed. Run this script with -BuildDumper for the ONE-TIME native x86 OAT build.'}
    Write-Host '[WEAPON-VPTR] One-time pinned OAT build with direct virtual-pointer witness.'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1') -TraceWeaponNamePointers
    if($LASTEXITCODE -ne 0 -or -not $?){throw 'Specialized native OAT build failed.'}
}
$cap=Get-Content -LiteralPath $capPath -Raw | ConvertFrom-Json
if([string]$cap.oatRevision -ne '3beaa7c81af517f0c05f2b4612d477ed1541b676' -or
   @($cap.capabilities) -notcontains $capName){throw 'OAT direct pointer capability or revision does not match.'}
$rows=New-Object 'System.Collections.Generic.List[object]'
$total=0
$resolved=0
$run=Split-Path -Path ([string]$audit.matrix) -Parent
$stamp=Get-Date -Format 'yyyyMMdd_HHmmss'
Write-Host '[WEAPON-VPTR] ============================================================'
Write-Host '[WEAPON-VPTR] ORIGINAL PC packed WeaponDef VIRTUAL pointer target witness.'
Write-Host '[WEAPON-VPTR] Independently loaded virtual block, NOT name-order guesses.'
foreach($src in @($map.pcSourceOatListings)){
    $file=[string]$src.path
    $proof=[string]$src.expectedProofFile
    $selected=@($map.proofNames | Where-Object {$_.synthetic -and [string]$_.proof -ieq $proof})
    if($selected.Count -eq 0){continue}
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Original PC source FF missing: $file"}
    if(-not(Test-Path -LiteralPath $proof -PathType Leaf)){throw "Source weapon proof missing: $proof"}
    if([int]$src.exit -ne 0 -or -not [bool]$src.ordinalWitness.allKnownNamesAligned){throw "Original PC OAT name witness was not clean for $file"}
    $tokens=@($selected | ForEach-Object {
        $p=[uint64]$_.pointerRaw
        if($p -eq 0 -or $p -ge 4294967294L -or (($p-1) -shr 29) -ne 4){throw 'Invalid packed virtual pointer in proof.'}
        ('{0:X8}' -f $p)
    })
    if(@($tokens | Sort-Object -Unique).Count -ne $tokens.Count){throw "Duplicate raw pointers in $proof"}
    $total+=$tokens.Count
    $tmp=Join-Path ([IO.Path]::GetTempPath()) ('WAWVP_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    New-Item -Path $tmp -ItemType Directory -Force | Out-Null
    $log=Join-Path $run ('weapon_direct_vptr_'+$stamp+'_'+($src.sourceRole -replace '[^A-Za-z0-9_-]','_')+'.log')
    $text=''
    $exit=-1
    try{
        $copy=Join-Path $tmp ([IO.Path]::GetFileName($file))
        Copy-Item -LiteralPath $file -Destination $copy
        $sourceSha=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        $sourceRecords=@($sourceArchive.FastFiles | Where-Object {[string]$_.Path -ieq $file})
        if($sourceRecords.Count -ne 1 -or
           [string]$sourceRecords[0].GraphParticipation -notin @('PRIMARY_GRAPH_SOURCE','AUXILIARY_GRAPH_SOURCE') -or
           $sourceSha -ine [string]$sourceRecords[0].Sha256){
            throw "Original PC FF SHA or source-role mismatch with Linker V2 archive: $file"
        }
        if((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ine $sourceSha){throw "PC FF short-path copy changed: $file"}
        $out=Join-Path $tmp 'out.txt'
        $err=Join-Path $tmp 'err.txt'
        $prior=[Environment]::GetEnvironmentVariable('WAW360_WEAPON_POINTER_WITNESS','Process')
        try{
            [Environment]::SetEnvironmentVariable('WAW360_WEAPON_POINTER_WITNESS',($tokens -join ','),'Process')
            $args=(@('--list','--no-color',$copy) | ForEach-Object {'"'+([string]$_).Replace('"','\"')+'"'}) -join ' '
            $process=Start-Process -FilePath $dumper -ArgumentList $args -RedirectStandardOutput $out -RedirectStandardError $err -Wait -PassThru -NoNewWindow
            $exit=[int]$process.ExitCode
        }finally{
            [Environment]::SetEnvironmentVariable('WAW360_WEAPON_POINTER_WITNESS',$prior,'Process')
        }
        $text=[IO.File]::ReadAllText($out)+[Environment]::NewLine+[IO.File]::ReadAllText($err)
        [IO.File]::WriteAllText($log,$text,[Text.UTF8Encoding]::new($false))
    }finally{
        Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
    if($exit -ne 0){throw "PC OAT loading failed for $file. See $log"}
    $oatNames=@($text -split '[\r\n]+' | ForEach-Object {
        $m=[regex]::Match($_,'^\s*weapon\s*,\s*(\S(?:.*\S)?)\s*$',[Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if($m.Success){$m.Groups[1].Value.Trim()}
    })
    if($oatNames.Count -ne @($src.oatWeaponAssetNames).Count){throw "Original PC OAT asset count changed for $file; log=$log"}
    $known=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($n in $oatNames){if(-not $known.Add([string]$n)){throw "Duplicate PC weapon identity: $n"}}
    $trace=@($text -split '[\r\n]+' | Where-Object {$_ -match '^\[WAW360-WPTR\]'})
    foreach($weapon in $selected){
        $raw='{0:X8}' -f [uint64]$weapon.pointerRaw
        $hits=@($trace | Where-Object {$_ -match ('^\[WAW360-WPTR\] raw='+$raw+'\b')})
        $status='NO_TRACE'
        $name=$null
        if($hits.Count -eq 1){
            $hit=[regex]::Match($hits[0],'^\[WAW360-WPTR\] raw=([0-9a-fA-F]{8}) block=(\d+) offset=0x([0-9a-fA-F]+) status=([A-Z_]+)(?: name=([A-Za-z0-9_./-]+))?$')
            if($hit.Success){
                $status=[string]$hit.Groups[4].Value
                $value=[string]$hit.Groups[5].Value
                $offset=[Convert]::ToUInt64($hit.Groups[3].Value,16)
                if($status -eq 'STRING_AT_DIRECT_VIRTUAL_TARGET' -and
                   [int]$hit.Groups[2].Value -eq 4 -and
                   $offset -eq (([uint64]$weapon.pointerRaw-1) -band 0x1FFFFFFF) -and
                   $known.Contains($value)){
                    $name=$value
                    $status='DIRECT_POINTER_NAME_IN_OAT'
                    $resolved++
                }elseif($status -eq 'STRING_AT_DIRECT_VIRTUAL_TARGET'){$status='TARGET_NOT_IN_PC_WEAPON_NAMES'}
            }else{$status='MALFORMED_TRACE'}
        }elseif($hits.Count -gt 1){$status='AMBIGUOUS_NATIVE_TRACES'}
        Write-Host "[WEAPON-VPTR] $($src.sourceRole) $raw -> $status $name"
        $rows.Add([pscustomobject]@{sourceRole=$src.sourceRole;source=$file;sourceSha256=$sourceSha;raw=$raw;proof=$proof;proofOffset=$weapon.offset;status=$status;directOatLoadedName=$name;resolved=($status -eq 'DIRECT_POINTER_NAME_IN_OAT');ordinalInferenceUsed=$false;log=$log})
    }
}
if($total -ne 10){throw "Expected ten original packed pointers; found $total."}
$report=Join-Path $run ('weapon_direct_vptr_'+$stamp+'.json')
[ordered]@{
    schema='WaW360.WeaponDirectVirtualPointerWitness.v1'
    map=$MapName
    directMatches=$resolved
    requested=$total
    allDirectNamesResolved=($resolved -eq $total)
    sourceNameAudit=$audit.matrix
    namesAppliedToProduction=$false
    nativeXboxReleaseAuthorized=$false
    rows=@($rows.ToArray())
}|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $report -Encoding UTF8
Write-Host "[WEAPON-VPTR] DIRECT TARGETS MATCHED: $resolved/$total | $report"
Write-Host '[WEAPON-VPTR] NO candidate FF modification and NO Xbox deployment.'
