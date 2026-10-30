[CmdletBinding()]
param(
    [string]$MatrixJson = '',
    [string[]]$IncludeMaps = @()
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$IncludeMaps=@($IncludeMaps | ForEach-Object {([string]$_) -split ','} |
    ForEach-Object {([string]$_).Trim()} | Where-Object {$_ -ne ''})
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$runs=Join-Path $repo 'output\multi_map_matrix\runs'
if([string]::IsNullOrWhiteSpace($MatrixJson)){
    if(-not(Test-Path -LiteralPath $runs -PathType Container)){throw "No matrix run folder: $runs"}
    foreach($candidate in (Get-ChildItem -LiteralPath $runs -Recurse -File -Filter 'multi_map_matrix.json' |
        Sort-Object LastWriteTimeUtc -Descending)){
        try {
            $probe=Get-Content -LiteralPath $candidate.FullName -Raw | ConvertFrom-Json
            if([string]$probe.schema -eq 'WaW360.MultiMapConversionMatrix.v1' -and
                [string]$probe.mode -eq 'Candidate'){
                $MatrixJson=$candidate.FullName
                break
            }
        }catch{}
    }
}
if([string]::IsNullOrWhiteSpace($MatrixJson)){throw 'No Candidate matrix report found'}
$MatrixJson=(Resolve-Path -LiteralPath $MatrixJson).Path
$matrix=Get-Content -LiteralPath $MatrixJson -Raw | ConvertFrom-Json
if([string]$matrix.schema -ne 'WaW360.MultiMapConversionMatrix.v1' -or
    [string]$matrix.mode -ne 'Candidate'){throw "Not a Candidate matrix report: $MatrixJson"}
$run=Split-Path -Path $MatrixJson -Parent
$stamp=Get-Date -Format 'yyyyMMdd_HHmmss'
$progressFile=Join-Path $run ('native_readiness_'+$stamp+'.json')
$historyRoot=Join-Path $repo 'output\multi_map_matrix\native_readiness_history'
New-Item -ItemType Directory -Path $historyRoot -Force | Out-Null

function Get-ShaMatch([object]$artifact) {
    if($null -eq $artifact){return $false}
    $path=[string]$artifact.path
    if([string]::IsNullOrWhiteSpace($path) -or
       -not (Test-Path -LiteralPath $path -PathType Leaf)){return $false}
    $current=Get-Item -LiteralPath $path
    if([long]$artifact.bytes -ne [long]$current.Length){return $false}
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ieq [string]$artifact.sha256
}
function Find-Variant([object[]]$artifacts,[string]$variant){
    foreach($entry in $artifacts){
        if($null -eq $entry){continue}
        $parent=Split-Path -Path ([string]$entry.path) -Parent
        if((Split-Path -Path $parent -Leaf) -eq $variant){return $entry}
    }
    return $null
}
function Is-OatVerified([object[]]$diffs,[string]$mapName,
    [string]$variant,[string]$mode,[object]$artifact){
    if($null -eq $artifact -or -not(Get-ShaMatch $artifact)){return $false}
    $status=if($mode -eq 'List'){'OAT_LIST_CLEAN_DIAGNOSTIC'}else{'OAT_EXTRACT_CLEAN_DIAGNOSTIC'}
    foreach($diff in $diffs){
        foreach($entry in @($diff.results)){
            if($null -eq $entry){continue}
            if([string]$entry.map -ieq $mapName -and
               [string]$entry.variant -eq $variant -and
               [string]$entry.oatMode -eq $mode -and
               [string]$entry.status -eq $status -and
               [string]$entry.sha256 -ieq [string]$artifact.sha256 -and
               [long]$entry.bytes -eq [long]$artifact.bytes -and
               [int]$entry.exit -eq 0){return $true}
        }
    }
    return $false
}
function New-Gate([string]$id,[string]$title,[string]$status,[string]$reason) {
    return [pscustomobject]@{
        id=$id;title=$title;status=$status;reason=$reason
        # Only PASS contributes to the count, and only a separate,
        # independently proved production signoff could authorize Xbox testing.
        countedPass=($status -eq 'PASS')
    }
}
$diffs=New-Object 'System.Collections.Generic.List[object]'
foreach($file in (Get-ChildItem -LiteralPath $run -File -Filter 'oat_differential_*.json' |
    Sort-Object LastWriteTimeUtc -Descending)){
    try{
        $d=Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        if([string]$d.schema -eq 'WaW360.MultiMapOatDifferential.v1' -and
           [string]$d.matrix -ieq $MatrixJson -and
           [string]$d.gitCommit -eq [string]$matrix.gitCommit){
            $diffs.Add($d)
        }
    }catch{Write-Host "[READINESS] Ignoring invalid OAT report: $($file.FullName)"}
}
$reports=New-Object 'System.Collections.Generic.List[object]'
Write-Host '[READINESS] ========================================================='
Write-Host "[READINESS] Native Xbox test-preflight evidence. Candidate matrix: $MatrixJson"
Write-Host '[READINESS] Gate tally is NOT a percentage or an authorization to deploy.'

foreach($m in @($matrix.maps)){
    if($null -eq $m){continue}
    $selected=$IncludeMaps.Count -eq 0
    foreach($pattern in $IncludeMaps){if([string]$m.map -like $pattern){$selected=$true;break}}
    if(-not $selected){continue}
    $mapName=[string]$m.map
    $gates=New-Object 'System.Collections.Generic.List[object]'
    $artifacts=@($m.emittedCandidates | Where-Object {$null -ne $_})
    $scriptVariant='hardware_test_main_xanim_native_layout_script_closure'
    $mainVariant='hardware_test_main_xanim_native_layout'
    $script=Find-Variant $artifacts $scriptVariant
    $primary=Find-Variant $artifacts $mainVariant
    $scriptValid=Get-ShaMatch $script
    $primaryValid=Get-ShaMatch $primary
    $sourceIndex=Join-Path ([string]$m.conversionOutputFolder) (
        Join-Path $mapName (Join-Path 'recipe_source_precedence' ($mapName+'_recipe_iwd_precedence.json')))
    $sourceOk=$false
    $indexed=0
    if(Test-Path -LiteralPath $sourceIndex -PathType Leaf){
        try{
            $src=Get-Content -LiteralPath $sourceIndex -Raw | ConvertFrom-Json
            $sourceOk=[bool]$src.PrecedenceIndexed -and @($src.Errors).Count -eq 0
            $indexed=@($src.Entries).Count
        }catch{}
    }
    $gates.Add((New-Gate 'SOURCE' 'Recipe IWD/source precedence indexed' $(
        if($sourceOk){'PASS'}else{'PENDING'}) $(
        if($sourceOk){"Source index clean, $indexed IWD entries"}else{'Current Candidate source-index proof missing or invalid'})))

    $emitterLog=Join-Path ([string]$m.resultFolder) 'linker-v2-main-structural.log'
    $logText=if(Test-Path -LiteralPath $emitterLog -PathType Leaf){
        Get-Content -LiteralPath $emitterLog -Raw
    }else{''}
    $scriptOverridden=0
    if($logText -match 'RECIPE IWD script-closure overrides=([0-9,]+)'){
        $scriptOverridden=[int](($Matches[1]) -replace ',','')
    }
    $closureOk=$scriptValid -and
        $logText -match 'scriptClosureReady=True;\s*residentMenusRemoved=[0-9,]+;\s*rawFilesPromoted=[0-9,]+;\s*readBackErrors=0;\s*wrapperRoundTrip=True' -and
        $scriptOverridden -gt 0
    $gates.Add((New-Gate 'SCRIPT' 'Real IWD script overrides + FF internal readback' $(
        if($closureOk){'PASS'}else{'PENDING'}) $(
        if($closureOk){"$scriptOverridden overridden RawFile roots; internal readback 0; FF $($script.bytes) B"}else{'No verified, altered script-closure FF in this run'})))

    $scriptListOk=Is-OatVerified @($diffs.ToArray()) $mapName $scriptVariant 'List' $script
    $gates.Add((New-Gate 'SCRIPT_LIST' 'Script-closure independent OAT listing' $(
        if($scriptListOk){'PASS'}else{'PENDING'}) $(
        if($scriptListOk){"OAT --list 0 warnings/errors; FF SHA $([string]$script.sha256)"}else{'No same-run, SHA-matched clean OAT listing result'})))

    $scriptExtractOk=Is-OatVerified @($diffs.ToArray()) $mapName $scriptVariant 'Extract' $script
    $gates.Add((New-Gate 'SCRIPT_EXTRACT' 'Script-closure full OAT extraction' $(
        if($scriptExtractOk){'PASS'}else{'PENDING'}) $(
        if($scriptExtractOk){'Same SHA fully extracted without warnings/errors'}else{'Full OAT extraction not yet proven for script-closure FF'})))

    $mainExtractOk=Is-OatVerified @($diffs.ToArray()) $mapName $mainVariant 'Extract' $primary
    if(-not $mainExtractOk -and $primaryValid){
        foreach($stage in @($m.stages)){
            if($null -ne $stage -and [string]$stage.step -eq 'OAT_DIAGNOSTIC' -and
               [bool]$stage.pass -and [string]$stage.file -ieq [string]$primary.path -and
               [string]$stage.sha256 -ieq [string]$primary.sha256){
                $mainExtractOk=$true
            }
        }
    }
    $gates.Add((New-Gate 'MAIN_EXTRACT' 'Production-shape primary FF full OAT extraction' $(
        if($mainExtractOk){'PASS'}else{'BLOCKED'}) $(
        if($mainExtractOk){'Same-hash primary FF passed OAT full extraction'}else{"Primary OAT full-extraction gate not passed; matrix=$($m.status)"})))

    # A matched name at the same XAsset ordinal is NOT the same as a
    # virtual-block pointer-to-string proof; fail closed on both.
    $weaponState='PENDING'
    $weaponReason='No verified packed pointer-to-name resolver; ordinal witnesses are not enough'
    $latestAudit=$null
    foreach($auditFile in (Get-ChildItem -LiteralPath $runs -File -Recurse -Filter 'weapon_name_provenance_*.json' |
        Sort-Object LastWriteTimeUtc -Descending)){
        try{
            $a=Get-Content -LiteralPath $auditFile.FullName -Raw | ConvertFrom-Json
            if([string]$a.schema -ne 'WaW360.WeaponNameAudit.v1'){continue}
            $matches=@($a.maps | Where-Object {$null -ne $_ -and [string]$_.map -ieq $mapName})
            if($matches.Count -gt 0){$latestAudit=$matches[0];break}
        }catch{}
    }
    if($null -ne $latestAudit){
        $missing=[int]$latestAudit.syntheticPackedNameCount
        if($missing -gt 0){
            $weaponState='BLOCKED'
            $weaponReason="$missing synthetic packed-name proof labels in latest historical audit; ordinal names are not pointer proofs"
        }
    }
    $gates.Add((New-Gate 'WEAPON_NAMES' 'All real WeaponDef identities proven' $weaponState $weaponReason))
    $gates.Add((New-Gate 'ASSETS' 'Typed renderer, FX, audio, collision assets complete' 'PENDING'
        'Structural/placeholder roots remain; no complete production graph manifest'))
    $gates.Add((New-Gate 'DEPENDENCIES' 'Required script, world and gameplay dependency closure' 'PENDING'
        'No independently verified production runtime-reachability closure'))
    $gates.Add((New-Gate 'MEMORY' 'Xbox 360 memory and asset-pool limits verified' 'PENDING'
        'No production-sized Xbox arena/pool admission result'))
    $gates.Add((New-Gate 'PACKAGE' 'Native FF, load FF, metadata, textures/sound dependencies validated' 'PENDING'
        'No independently verified complete CodXe native release package'))

    $passed=@($gates.ToArray() | Where-Object {$_.status -eq 'PASS'}).Count
    $total=$gates.Count
    $bar=('=' * $passed)+('-' * ($total-$passed))
    $key=($mapName -replace '[^a-zA-Z0-9_-]','_')+'_'+(
        [IO.Path]::GetFileName([string]$m.resultFolder) -replace '[^a-zA-Z0-9_-]','_')
    $historical=Join-Path $historyRoot ($key+'.json')
    $oldCount=$null
    $history=@()
    if(Test-Path -LiteralPath $historical -PathType Leaf){
        try{
            $old=Get-Content -LiteralPath $historical -Raw | ConvertFrom-Json
            if([string]$old.schema -eq 'WaW360.NativeTestReadinessHistory.v1'){
                $history=@($old.entries | Where-Object {$null -ne $_})
                if($history.Count -gt 0){$oldCount=[int]$history[-1].passed}
            }
        }catch{Write-Host "[READINESS] Ignoring invalid prior readiness history: $historical"}
    }
    $delta=if($null -eq $oldCount){'first evidence baseline'}else{
        $d=$passed-$oldCount
        if($d -gt 0){"+$d gates since previous check"}elseif($d -lt 0){
            "$d gates since previous check"
        }else{'no gate-count change since previous check'}
    }
    Write-Host "[READINESS] $mapName [$bar] $passed/$total independently evidenced gates | $delta"
    Write-Progress -Activity 'PC2XB3 native test readiness' -Status "$mapName $passed/$total evidence gates" -PercentComplete ([int](100*$passed/$total))
    foreach($gate in $gates){
        Write-Host "[READINESS]   $($gate.status.PadRight(7)) $($gate.id.PadRight(16)) $($gate.reason)"
    }
    Write-Host '[READINESS]   VERDICT: HOLD_DIAGNOSTIC_ONLY. No automated Xbox deployment or release approval.'
    $stateSignature=(@($gates.ToArray() | ForEach-Object {$_.id+'='+$_.status}) -join ';')+
        '|'+[string]$matrix.gitCommit+'|'+[string]$m.sourceFingerprint+
        '|scriptSha='+$(if($null -ne $script){[string]$script.sha256}else{'none'})
    if($history.Count -eq 0 -or [string]$history[-1].signature -ne $stateSignature){
        $history+=([pscustomobject]@{
            utc=[DateTime]::UtcNow.ToString('o')
            gitCommit=[string]$matrix.gitCommit
            matrix=$MatrixJson;signature=$stateSignature
            passed=$passed;total=$total
            state='HOLD_DIAGNOSTIC_ONLY'
        })
        [ordered]@{
            schema='WaW360.NativeTestReadinessHistory.v1'
            map=$mapName;entries=@($history)
        }|ConvertTo-Json -Depth 9|Set-Content -LiteralPath $historical -Encoding UTF8
    }
    $reports.Add([pscustomobject]@{
        map=$mapName;matrix=$MatrixJson;sourceFingerprint=[string]$m.sourceFingerprint
        gitCommit=[string]$matrix.gitCommit
        passedEvidenceGates=$passed;totalEvidenceGates=$total
        scriptOverrides=$scriptOverridden
        scriptClosureFFBytes=if($scriptValid){[long]$script.bytes}else{$null}
        primaryFFBytes=if($primaryValid){[long]$primary.bytes}else{$null}
        sourceIndex=$sourceIndex
        gates=@($gates.ToArray())
        verdict='HOLD_DIAGNOSTIC_ONLY'
        xboxReleaseAuthorized=$false
        history=$historical
    })
}
Write-Progress -Activity 'PC2XB3 native test readiness' -Completed
if($reports.Count -eq 0){throw 'No maps matched. Specify -IncludeMaps or -MatrixJson.'}
[ordered]@{
    schema='WaW360.NativeTestReadiness.v1'
    generatedUtc=[DateTime]::UtcNow.ToString('o')
    candidateMatrix=$MatrixJson
    allMapsAuthorizedForXboxTest=$false
    note='Evidence counts are discrete preflight gates, NOT percent completion; production release requires full native asset closure and separate human-controlled hardware approval.'
    maps=@($reports.ToArray())
}|ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $progressFile -Encoding UTF8
Write-Host '[READINESS] ========================================================='
Write-Host "[READINESS] JSON=$progressFile"
Write-Host '[READINESS] A diagnostic FF is never a safe native Xbox release merely because these checks pass.'
