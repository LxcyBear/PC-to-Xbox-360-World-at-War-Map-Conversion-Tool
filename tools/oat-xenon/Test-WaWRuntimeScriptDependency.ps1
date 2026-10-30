[CmdletBinding()]
param(
    [string]$MapName='nazi_zombie_aztec_V2.2',
    [string]$Script='animscripts/dog_combat',
    [string]$Label='main',
    [string]$TestedFastFile='',
    [string]$MatrixJson=''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$runs=Join-Path $repo 'output\multi_map_matrix\runs'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
if(-not(Test-Path -LiteralPath $oat -PathType Leaf)){
    $oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
}
if(-not(Test-Path -LiteralPath $oat -PathType Leaf)){throw "Original OAT Unlinker missing: $oat"}
if($Script -notmatch '^[a-zA-Z0-9_./-]+$' -or $Label -notmatch '^[a-zA-Z0-9_]+$'){
    throw 'Script or label contains unexpected characters.'
}
$stem=$Script.Replace('\','/').TrimEnd('/')
if($stem.EndsWith('.gsc',[StringComparison]::OrdinalIgnoreCase)){$stem=$stem.Substring(0,$stem.Length-4)}
$fullScript=$stem+'.gsc'
if([string]::IsNullOrWhiteSpace($MatrixJson)){
    foreach($f in @(Get-ChildItem -LiteralPath $runs -File -Recurse -Filter 'multi_map_matrix.json' | Sort-Object LastWriteTimeUtc -Descending)){
        try{
            $candidate=Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json
            if([string]$candidate.mode -eq 'Candidate' -and
               [string]$candidate.schema -eq 'WaW360.MultiMapConversionMatrix.v1' -and
               @($candidate.maps | Where-Object {[string]$_.map -ieq $MapName}).Count -eq 1){
                $MatrixJson=$f.FullName
                break
            }
        }catch{}
    }
}
if([string]::IsNullOrWhiteSpace($MatrixJson)){throw "No Candidate matrix found for $MapName"}
$MatrixJson=(Resolve-Path -LiteralPath $MatrixJson).Path
$matrix=Get-Content -LiteralPath $MatrixJson -Raw | ConvertFrom-Json
$matching=@($matrix.maps | Where-Object {[string]$_.map -ieq $MapName})
if([string]$matrix.mode -ne 'Candidate' -or $matching.Count -ne 1){throw 'Matrix must have exactly one matching Candidate map.'}
$m=$matching[0]
$checks=New-Object 'System.Collections.Generic.List[object]'
$byPath=[System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

function Add-Check([string]$file,[string]$kind,[string]$variant,[string]$expectedSha){
    if([string]::IsNullOrWhiteSpace($file) -or -not(Test-Path -LiteralPath $file -PathType Leaf)){return}
    $full=[IO.Path]::GetFullPath($file)
    if(-not $byPath.Add($full)){return}
    $hash=(Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash
    if(-not [string]::IsNullOrWhiteSpace($expectedSha) -and $hash -ine $expectedSha){
        throw "FF source/candidate SHA mismatch: $full"
    }
    $checks.Add([pscustomobject]@{
        sourceKind=$kind;variant=$variant;file=$full;sha256=$hash
        bytes=[long](Get-Item -LiteralPath $full).Length
    })
}
foreach($ff in @($m.emittedCandidates)){
    if($null -eq $ff){continue}
    $parent=Split-Path ([string]$ff.path) -Parent
    $variant=Split-Path $parent -Leaf
    if($variant -notin @(
        'hardware_test_main_xanim_native_layout',
        'hardware_test_main_xanim_native_layout_script_closure',
        'hardware_test_main_xanim_native_layout_weapon_order_probe',
        'hardware_test_main_xanim_native_layout_tracked_shell')){continue}
    Add-Check ([string]$ff.path) 'CURRENT_DIAGNOSTIC_CANDIDATE' $variant ([string]$ff.sha256)
}
$archive=Join-Path ([string]$m.conversionOutputFolder) (
    Join-Path $MapName (Join-Path 'linker_v2_source_archive' ($MapName+'_linker_v2_source_archive.json')))
if(Test-Path -LiteralPath $archive -PathType Leaf){
    $sources=Get-Content -LiteralPath $archive -Raw | ConvertFrom-Json
    foreach($src in @($sources.FastFiles)){
        if($null -eq $src){continue}
        if([string]$src.GraphParticipation -in @('PRIMARY_GRAPH_SOURCE','AUXILIARY_GRAPH_SOURCE')){
            Add-Check ([string]$src.Path) 'ORIGINAL_PC_GRAPH_SOURCE' ([string]$src.Role) ([string]$src.Sha256)
        }
    }
}else{
    Write-Host '[SCRIPT-RUNTIME] NOTE: Linker V2 source archive missing; source closure cannot be fully audited.'
}
if(-not [string]::IsNullOrWhiteSpace($TestedFastFile)){
    Add-Check $TestedFastFile 'USER_CONFIRMED_TESTED_FF' 'exact-user-supplied-file' ''
    if(-not(Test-Path -LiteralPath $TestedFastFile -PathType Leaf)){
        throw "The exact FF tested on Xbox is missing: $TestedFastFile"
    }
}
if($checks.Count -eq 0){throw 'No FFs found for runtime script audit.'}
$run=Split-Path $MatrixJson -Parent
$stamp=Get-Date -Format 'yyyyMMdd_HHmmss'
$result=New-Object 'System.Collections.Generic.List[object]'
Write-Host '[SCRIPT-RUNTIME] ========================================================='
Write-Host "[SCRIPT-RUNTIME] Xbox script error: label '$Label' in '$stem'"
Write-Host "[SCRIPT-RUNTIME] Comparing $($checks.Count) unchanged PC source/candidate FF(s)."
Write-Host '[SCRIPT-RUNTIME] OAT --list sees asset NAMES, not executable GSC labels.'
foreach($item in $checks){
    $tmp=Join-Path ([IO.Path]::GetTempPath()) ('WAWGSC_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null
    $code=-1
    $all=''
    try{
        $short=Join-Path $tmp ([IO.Path]::GetFileName([string]$item.file))
        Copy-Item -LiteralPath $item.file -Destination $short
        if((Get-FileHash -LiteralPath $short -Algorithm SHA256).Hash -ine [string]$item.sha256){
            throw "Short-path OAT copy SHA mismatch: $($item.file)"
        }
        $out=Join-Path $tmp 'out.txt'; $err=Join-Path $tmp 'err.txt'
        $args=(@('--list','--no-color',$short) | ForEach-Object {'"'+([string]$_).Replace('"','\"')+'"'}) -join ' '
        $proc=Start-Process -FilePath $oat -ArgumentList $args -PassThru -Wait -NoNewWindow -RedirectStandardOutput $out -RedirectStandardError $err
        $code=[int]$proc.ExitCode
        $all=[IO.File]::ReadAllText($out)+[Environment]::NewLine+[IO.File]::ReadAllText($err)
    }finally{Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue}
    $errors=$all -match '(?im)^\s*(?:\[STDERR\]\s*)?(?:ERROR\s*:|FATAL\s*:)|^\s*Failed\s+with|Unhandled exception'
    $assetNames=@($all -split '[\r\n]+' | ForEach-Object {
        $match=[regex]::Match($_,'^\s*rawfile\s*,\s*(.+?)\s*$',[Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if($match.Success){$match.Groups[1].Value.Trim()}
    })
    $target=@($assetNames | Where-Object {
        $normalize=([string]$_).Replace('\','/').TrimStart('/')
        $normalize -ieq $fullScript -or $normalize -ieq $stem
    })
    $status=if($code -ne 0 -or $errors){'OAT_LIST_FAILED_OR_ERRORS'}elseif($target.Count -gt 0){'SCRIPT_NAME_LISTED__LABEL_UNPROVEN'}else{'SCRIPT_NAME_NOT_LISTED_IN_THIS_FF'}
    Write-Host "[SCRIPT-RUNTIME] $($item.sourceKind) / $($item.variant) | $status | FF=$($item.bytes)B"
    $result.Add([pscustomobject]@{
        sourceKind=$item.sourceKind;variant=$item.variant;file=$item.file
        sha256=$item.sha256;bytes=$item.bytes
        oatExit=$code;hasErrors=$errors;status=$status
        matchingRawFileNames=@($target)
        compiledScriptLabelVerified=$false
        scriptLabelToFind=$Label
    })
}
$report=Join-Path $run ('runtime_script_dependency_'+$stamp+'.json')
[ordered]@{
    schema='WaW360.RuntimeScriptDependencyEvidence.v1'
    map=$MapName;script=$stem;requestedEntryLabel=$Label
    testedFastFileExplicitlyProvided=(-not [string]::IsNullOrWhiteSpace($TestedFastFile))
    candidateReleaseAuthorized=$false
    readOnlyOriginalAndCandidateFFs=$true
    note='RawFile XAsset presence alone does NOT verify compiled GSC or its main label. Missing from a map FF does NOT prove missing from runtime global/common FFs; compare actual Xbox modules and FF mount order.'
    results=@($result.ToArray())
}|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $report -Encoding UTF8
Write-Host "[SCRIPT-RUNTIME] JSON=$report"
Write-Host '[SCRIPT-RUNTIME] No file rebuild or Xbox deployment. This report does not prove native release readiness.'
