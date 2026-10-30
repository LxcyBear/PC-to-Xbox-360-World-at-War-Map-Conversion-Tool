[CmdletBinding()]
param(
    [string]$MatrixJson = '',
    [string[]]$IncludeMaps = @('nazi_zombie_FRUmansion','nazi_zombie_tower','Backrooms','nazi_zombie_aztec_V2.2')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$IncludeMaps = @($IncludeMaps | ForEach-Object { ([string]$_) -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$runs=Join-Path $repo 'output\multi_map_matrix\runs'

# Select the largest existing Candidate matrix to cover all four maps.
if([string]::IsNullOrWhiteSpace($MatrixJson)){
    $candidates=New-Object 'System.Collections.Generic.List[object]'
    foreach($f in (Get-ChildItem -LiteralPath $runs -Filter 'multi_map_matrix.json' -Recurse -File)){
        try {
            $doc=Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json
            if([string]$doc.mode -eq 'Candidate' -and [string]$doc.schema -eq 'WaW360.MultiMapConversionMatrix.v1'){
                $candidates.Add([pscustomobject]@{Path=$f.FullName;Count=@($doc.maps).Count;Modified=$f.LastWriteTimeUtc})
            }
        }catch{}
    }
    if($candidates.Count -eq 0){throw "No valid Candidate matrix under $runs"}
    $MatrixJson=[string]($candidates.ToArray() | Sort-Object @{Expression='Count';Descending=$true},@{Expression='Modified';Descending=$true} | Select-Object -First 1).Path
}
$MatrixJson=(Resolve-Path -LiteralPath $MatrixJson).Path
$matrix=Get-Content -LiteralPath $MatrixJson -Raw | ConvertFrom-Json
if([string]$matrix.mode -ne 'Candidate'){throw "Not a Candidate matrix: $MatrixJson"}
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
if(-not(Test-Path -LiteralPath $oat -PathType Leaf)){
    $oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
}
if(-not(Test-Path -LiteralPath $oat -PathType Leaf)){throw "OAT Xenon Unlinker missing: $oat"}

function Read-Be32([IO.BinaryReader]$reader){
    $b=$reader.ReadBytes(4)
    if($b.Length -ne 4){throw 'Truncated weapon proof integer'}
    return [uint32](([uint64]$b[0]*16777216)+([uint64]$b[1]*65536)+([uint64]$b[2]*256)+[uint64]$b[3])
}
function Read-WeaponNames([string]$path){
    $list=New-Object 'System.Collections.Generic.List[object]'
    $fs=[IO.File]::OpenRead($path)
    try{
        $reader=[IO.BinaryReader]::new($fs,[Text.Encoding]::UTF8,$true)
        $magic=[Text.Encoding]::ASCII.GetString($reader.ReadBytes(8)).TrimEnd([char]0)
        if($magic -ne 'W360A7W'){throw "Unexpected proof magic in $path"}
        $version=Read-Be32 $reader
        $count=Read-Be32 $reader
        $fixed=Read-Be32 $reader
        if($version -ne 8 -or $fixed -ne 0x9AC -or $count -gt 250000){throw "Unexpected proof schema in $path"}
        for($i=0;$i -lt $count;$i++){
            $offset=Read-Be32 $reader
            $size=Read-Be32 $reader
            $nameSize=Read-Be32 $reader
            if($nameSize -gt 4096 -or $size -gt 134217728 -or $fs.Position+$nameSize+$size -gt $fs.Length){
                throw "Bad record #$i in $path"
            }
            $name=[Text.Encoding]::UTF8.GetString($reader.ReadBytes([int]$nameSize))
            $null=$fs.Seek([long]$size,[IO.SeekOrigin]::Current)
            $list.Add([pscustomobject]@{record=$i;offset=[long]$offset;name=$name;synthetic=$name.StartsWith('<packed-name@',[StringComparison]::OrdinalIgnoreCase)})
        }
        if($fs.Position -ne $fs.Length){throw "Trailing data in $path"}
    }finally{$fs.Dispose()}
    return $list.ToArray()
}

$run=Split-Path -Path $MatrixJson -Parent
$stamp=Get-Date -Format 'yyyyMMdd_HHmmss'
$rows=New-Object 'System.Collections.Generic.List[object]'
Write-Host '[WEAPON-NAMES] ======================================================'
Write-Host "[WEAPON-NAMES] Read-only original-PC + typed-proof audit. Matrix=$MatrixJson"
foreach($m in $matrix.maps){
    $selected=$false
    foreach($pattern in $IncludeMaps){if([string]$m.map -like $pattern){$selected=$true;break}}
    if(-not $selected){continue}
    $mapName=[string]$m.map
    $proofFolder=Join-Path ([string]$m.conversionOutputFolder) (Join-Path $mapName 'stage4_alpha7b_weapon_owned')
    $proofFiles=@()
    if(Test-Path -LiteralPath $proofFolder -PathType Container){
        $proofFiles=@(Get-ChildItem -LiteralPath $proofFolder -File -Filter '*.xbox_weapon_owned_proof.bin')
    }
    $names=New-Object 'System.Collections.Generic.List[object]'
    foreach($p in $proofFiles){
        foreach($n in @(Read-WeaponNames $p.FullName)){
            $names.Add([pscustomobject]@{proof=$p.FullName;record=$n.record;offset=$n.offset;name=$n.name;synthetic=$n.synthetic})
        }
    }
    $unresolved=@($names.ToArray() | Where-Object {$_.synthetic})
    Write-Host "[WEAPON-NAMES] $mapName proof records=$($names.Count) | packed-name synthetic=$($unresolved.Count) | proof files=$($proofFiles.Count)"
    foreach($n in @($unresolved | Select-Object -First 4)){Write-Host "[WEAPON-NAMES]   unresolved: $($n.name) at PC source offset=$($n.offset)"}

    $inputs=New-Object 'System.Collections.Generic.List[string]'
    foreach($p in @($m.main,$m.mod,$m.patch)){
        if(-not [string]::IsNullOrWhiteSpace([string]$p) -and (Test-Path -LiteralPath $p -PathType Leaf)){
            if(-not $inputs.Contains([string]$p)){$inputs.Add([string]$p)}
        }
    }
    $oatSources=New-Object 'System.Collections.Generic.List[object]'
    $ix=0
    foreach($path in $inputs){
        $ix++
        $temp=Join-Path ([IO.Path]::GetTempPath()) ('WNAME_'+[guid]::NewGuid().ToString('N').Substring(0,8))
        New-Item -ItemType Directory -Path $temp -Force | Out-Null
        $log=Join-Path $run ('weapon_pc_oat_'+$stamp+'_'+($mapName -replace '[^A-Za-z0-9_-]','_')+'_'+$ix+'.log')
        $exit=-1
        $sample=@()
        try {
            $copy=Join-Path $temp ([IO.Path]::GetFileName([string]$path))
            Copy-Item -LiteralPath $path -Destination $copy
            if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash){
                throw 'Source FF short copy SHA mismatch'
            }
            $stdout=Join-Path $temp 'stdout.txt'
            $stderr=Join-Path $temp 'stderr.txt'
            $arguments=(@('--list','--no-color',$copy) | ForEach-Object {'"'+([string]$_).Replace('"','\"')+'"'}) -join ' '
            $process=Start-Process -FilePath $oat -ArgumentList $arguments -RedirectStandardOutput $stdout -RedirectStandardError $stderr -Wait -PassThru -NoNewWindow -ErrorAction Stop
            $exit=[int]$process.ExitCode
            $all=[IO.File]::ReadAllText($stdout)+[Environment]::NewLine+[IO.File]::ReadAllText($stderr)
            [IO.File]::WriteAllText($log,$all,[Text.UTF8Encoding]::new($false))
            $sample=@($all -split '[\r\n]+' | Where-Object {$_ -match '(?i)\bweapon\b|weapons[/\\]'} | Select-Object -First 12)
            Write-Host "[WEAPON-NAMES]   OAT original PC FF $ix : exit=$exit; weapon-related lines=$($sample.Count); log=$log"
            foreach($line in @($sample | Select-Object -First 3)){Write-Host "[WEAPON-NAMES]     $line"}
        }catch{
            [IO.File]::WriteAllText($log,[string]$_,[Text.UTF8Encoding]::new($false))
            Write-Host "[WEAPON-NAMES]   OAT original PC FF $ix ERROR: $([string]$_)"
        }finally{
            Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
        }
        $oatSources.Add([pscustomobject]@{path=$path;exit=$exit;log=$log;sample=@($sample)})
    }
    $rows.Add([pscustomobject]@{
        map=$mapName
        weaponProofRecords=$names.Count
        syntheticPackedNameCount=$unresolved.Count
        proofNames=@($names.ToArray())
        pcSourceOatListings=@($oatSources.ToArray())
        identitiesResolved=$false
    })
}
if($rows.Count -eq 0){throw 'No maps matched -IncludeMaps'}
$report=Join-Path $run ('weapon_name_provenance_'+$stamp+'.json')
[ordered]@{
    schema='WaW360.WeaponNameAudit.v1'
    matrix=$MatrixJson;readOnly=$true
    note='Synthetic <packed-name@...> weapon proof labels are NOT authentic weapon identities. OAT PC listings are evidence, not unverified one-to-one name matches.'
    maps=@($rows.ToArray())
}|ConvertTo-Json -Depth 11 | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host "[WEAPON-NAMES] JSON=$report"
Write-Host '[WEAPON-NAMES] No original PC FFs, proof FFs, or Xbox candidate FFs modified.'
