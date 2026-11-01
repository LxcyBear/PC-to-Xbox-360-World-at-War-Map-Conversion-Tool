[CmdletBinding()]
param([string]$MatrixJson = '', [string[]]$IncludeMaps = @('nazi_zombie_aztec_V2.2'))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$IncludeMaps=@($IncludeMaps | ForEach-Object {([string]$_) -split ','} | ForEach-Object {$_.Trim()} | Where-Object {$_ -ne ''})
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$runs=Join-Path $repo 'output\multi_map_matrix\runs'
if([string]::IsNullOrWhiteSpace($MatrixJson)){
    foreach($f in @(Get-ChildItem -LiteralPath $runs -Recurse -File -Filter 'multi_map_matrix.json' | Sort-Object LastWriteTimeUtc -Descending)){
        try{
            $d=Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json
            if([string]$d.schema -ne 'WaW360.MultiMapConversionMatrix.v1' -or [string]$d.mode -ne 'Candidate'){continue}
            $names=@($d.maps | ForEach-Object {[string]$_.map})
            $wanted=@($names | Where-Object {$n=$_;@($IncludeMaps | Where-Object {$n -like $_}).Count -gt 0})
            if($wanted.Count -gt 0){$MatrixJson=$f.FullName;break}
        }catch{}
    }
}
if([string]::IsNullOrWhiteSpace($MatrixJson)){throw 'No matching Candidate matrix found'}
$MatrixJson=(Resolve-Path -LiteralPath $MatrixJson).Path
$matrix=Get-Content -LiteralPath $MatrixJson -Raw | ConvertFrom-Json
if([string]$matrix.schema -ne 'WaW360.MultiMapConversionMatrix.v1' -or [string]$matrix.mode -ne 'Candidate'){throw 'Not a Candidate matrix'}
$run=Split-Path -Path $MatrixJson -Parent
$json=Join-Path $run ('weapon_scalar_witness_'+(Get-Date -Format 'yyyyMMdd_HHmmss')+'.json')
# Scalar locations validated by the original T4 WeaponDef alpha-6f proof scanner.
# No asset pointers, inline strings, ScriptString indices or names are fingerprinted.
$offsets=@(320,324,328,332,336,340,344,348,352,368,372,376,688,988,996,1000,1316,1320,1556,1568,1668,1704,1996,1244,1248,1252,1268,1280,1484,1488,1492,1496,1500,1512,1516,1520,1524,1528,1532,1536,1540,1544,1560,1572,1576,1580,1608,1612,1676,1700,1708,1712,1716,1720,1724,2200,2204,2208,2212) | Sort-Object -Unique
if($offsets.Count -lt 45 -or @($offsets | Where-Object {$_ -lt 0 -or $_ -gt 0x9A8}).Count -gt 0){throw 'Scalar offset list invalid'}
function Read-BE32([IO.BinaryReader]$r){
    $a=$r.ReadBytes(4)
    if($a.Length -ne 4){throw 'Truncated proof integer'}
    return [uint32](([uint64]$a[0]*16777216)+([uint64]$a[1]*65536)+([uint64]$a[2]*256)+[uint64]$a[3])
}
function Read-WeaponProof([string]$file){
    $rows=New-Object 'System.Collections.Generic.List[object]'
    $stream=[IO.File]::OpenRead($file)
    try{
        $reader=[IO.BinaryReader]::new($stream,[Text.Encoding]::UTF8,$true)
        $magic=[Text.Encoding]::ASCII.GetString($reader.ReadBytes(8)).TrimEnd([char]0)
        $version=Read-BE32 $reader; $count=Read-BE32 $reader; $fixed=Read-BE32 $reader
        if($magic -ne 'W360A7W' -or $version -ne 8 -or $fixed -ne 0x9AC -or $count -gt 10000){throw "Unexpected weapon proof schema in $file"}
        $sha=[Security.Cryptography.SHA256]::Create()
        try{
            for($i=0;$i -lt $count;$i++){
                $sourceOffset=Read-BE32 $reader
                $length=Read-BE32 $reader
                $nameLength=Read-BE32 $reader
                if($length -lt 0x9AC -or $length -gt 134217728 -or $nameLength -gt 4096 -or $stream.Position+[long]$length+[long]$nameLength -gt $stream.Length){throw "Invalid proof record #$i"}
                $name=[Text.Encoding]::UTF8.GetString($reader.ReadBytes([int]$nameLength))
                $blobStart=$stream.Position
                $fixedBytes=$reader.ReadBytes(0x9AC)
                if($fixedBytes.Length -ne 0x9AC){throw "Truncated weapon body #$i"}
                $signature=New-Object byte[] ($offsets.Count*4)
                for($j=0;$j -lt $offsets.Count;$j++){[Array]::Copy($fixedBytes,[int]$offsets[$j],$signature,$j*4,4)}
                $fingerprint=[BitConverter]::ToString($sha.ComputeHash($signature)).Replace('-','')
                $packed=$name.StartsWith('<packed-name@',[StringComparison]::OrdinalIgnoreCase)
                if($packed -and -not [regex]::IsMatch($name,'^<packed-name@0x[0-9a-fA-F]+:0x[0-9a-fA-F]{8}>$')){throw "Unrecognized packed-name label: $name"}
                $rows.Add([pscustomobject]@{proof=[IO.Path]::GetFileName($file);record=$i;sourceOffset=[long]$sourceOffset;name=$name;packedName=$packed;scalarFingerprint=$fingerprint})
                $null=$stream.Seek($blobStart+[long]$length,[IO.SeekOrigin]::Begin)
            }
        }finally{$sha.Dispose()}
        if($stream.Position -ne $stream.Length){throw "Trailing proof data in $file"}
    }finally{$stream.Dispose()}
    return $rows.ToArray()
}
$results=New-Object 'System.Collections.Generic.List[object]'
Write-Host '[WEAPON-SCALARS] ======================================================='
Write-Host '[WEAPON-SCALARS] READ ONLY: independent source scalar-signature uniqueness feasibility.'
Write-Host '[WEAPON-SCALARS] Names and source pointers NOT resolved; no FF or OAT operation.'
foreach($m in @($matrix.maps)){
    $mapName=[string]$m.map
    $match=@($IncludeMaps | Where-Object {$mapName -like $_})
    if($match.Count -eq 0){continue}
    $folder=Join-Path ([string]$m.conversionOutputFolder) (Join-Path $mapName 'stage4_alpha7b_weapon_owned')
    if(-not(Test-Path -LiteralPath $folder -PathType Container)){throw "Missing weapon proof directory $folder"}
    $files=@(Get-ChildItem -LiteralPath $folder -File -Filter '*.xbox_weapon_owned_proof.bin')
    if($files.Count -eq 0){throw "Missing weapon proof files in $folder"}
    $all=New-Object 'System.Collections.Generic.List[object]'
    foreach($file in $files){foreach($row in @(Read-WeaponProof $file.FullName)){$all.Add($row)}}
    $allRows=@($all.ToArray())
    $packedRows=@($allRows | Where-Object {$_.packedName})
    $duplicateGroups=@($allRows | Group-Object scalarFingerprint | Where-Object {$_.Count -gt 1})
    $cases=New-Object 'System.Collections.Generic.List[object]'
    foreach($row in $packedRows){
        $same=@($allRows | Where-Object {$_.scalarFingerprint -eq $row.scalarFingerprint})
        $cases.Add([pscustomobject]@{
            packedProofLabel=$row.name; proof=$row.proof;sourceOffset=$row.sourceOffset
            scalarFingerprintSha256=$row.scalarFingerprint
            uniqueAgainstAllPhysicalProofBodies=($same.Count -eq 1)
            collidingProofNames=@($same | ForEach-Object {$_.name})
            independentOatLoadedScalarWitnessCollected=$false
            genuinePackedPointerTargetProven=$false
            realWeaponIdentityResolved=$false
        })
    }
    $uniquePacked=@($cases.ToArray() | Where-Object {$_.uniqueAgainstAllPhysicalProofBodies}).Count
    Write-Host "[WEAPON-SCALARS] $($mapName): total=$($all.Count) packed=$($packedRows.Count) unique-packed-fingerprints=$uniquePacked/$($packedRows.Count) duplicate-groups=$($duplicateGroups.Count)"
    foreach($case in $cases){
        $status=if($case.uniqueAgainstAllPhysicalProofBodies){'UNIQUE-FINGERPRINT'}else{'COLLISION'}
        Write-Host "[WEAPON-SCALARS]   $status $($case.packedProofLabel)"
    }
    $results.Add([pscustomobject]@{map=$mapName;proofCount=$files.Count;physicalBodies=$all.Count;syntheticPackedNames=$packedRows.Count;uniquePackedFingerprints=$uniquePacked;duplicateGroups=$duplicateGroups.Count;cases=@($cases.ToArray());proofRows=$allRows;resolvedPackedPointers=0;xboxTestAuthorized=$false})
}
if($results.Count -eq 0){throw 'No maps matched'}
[ordered]@{schema='WaW360.WeaponScalarIdentityFeasibility.v1';matrix=$MatrixJson;readOnly=$true;scalarOffsets=$offsets;note='Source scalar uniqueness alone cannot associate a real name or verify packed-pointer targets. OAT-loaded typed scalar crosswalk remains necessary.';maps=@($results.ToArray())}|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $json -Encoding UTF8
Write-Host "[WEAPON-SCALARS] JSON=$json"
Write-Host '[WEAPON-SCALARS] Xbox readiness UNCHANGED. No FF/OAT build, pointer substitution or deployment.'
