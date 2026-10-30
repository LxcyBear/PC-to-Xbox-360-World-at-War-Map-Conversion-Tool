[CmdletBinding()]
param(
    [string]$MatrixJson='',
    [string]$MainVariant='hardware_test_main_xanim_native_layout_weapon_real_scalars',
    [string]$PcMapsRoot='',
    [string]$SourceMapFolderName='nazi_zombie_aztec_V2.2',
    [switch]$Stage
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# Separate from the STRICT RELEASE gate. Staging a first-exception canary
# is not a claim of an Xbox-ready map. No native reference FF is eligible.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$map='nazi_zombie_aztec'
$sourceMap='nazi_zombie_aztec_V2.2'
if($MainVariant -notin @('hardware_test_main_xanim_native_layout_weapon_real_scalars','hardware_test_main_xanim_native_layout_weapon_direct_proven')){
    throw 'Only previously SHA-verified OAT-extracted Aztec diagnostics can be staged.'
}
function Hash([string]$p){return [string](Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash}
function RequireFile([string]$p,[string]$label){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "$label missing: $p"}
    return (Resolve-Path -LiteralPath $p).Path
}
function CopyVerified([string]$from,[string]$to,[string]$expected){
    if(Test-Path -LiteralPath $to){throw "Refusing to overwrite staged file: $to"}
    Copy-Item -LiteralPath $from -Destination $to -ErrorAction Stop
    if((Hash $to) -ine $expected -or (Hash $from) -ine $expected){
        throw "Stage/source SHA mismatch: $from -> $to"
    }
}
if([string]::IsNullOrWhiteSpace($MatrixJson)){
    $runs=Join-Path $repo 'output\multi_map_matrix\runs'
    foreach($file in @(Get-ChildItem -LiteralPath $runs -Filter 'multi_map_matrix.json' -File -Recurse | Sort-Object LastWriteTimeUtc -Descending)){
        try{
            $probe=Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
            if([string]$probe.schema -ne 'WaW360.MultiMapConversionMatrix.v1' -or [string]$probe.mode -ne 'Candidate'){continue}
            $withMap=@($probe.maps | Where-Object {[string]$_.map -ieq $sourceMap})
            if($withMap.Count -ne 1){continue}
            $withVariant=@($withMap[0].emittedCandidates | Where-Object {
                (Split-Path -Path (Split-Path -Path ([string]$_.path) -Parent) -Leaf) -eq $MainVariant
            })
            if($withVariant.Count -eq 1){$MatrixJson=$file.FullName;break}
        }catch{}
    }
}
if([string]::IsNullOrWhiteSpace($MatrixJson)){throw 'No Aztec Candidate matrix. Pass -MatrixJson with the confirmed 451050-byte Aztec run.'}
$MatrixJson=RequireFile $MatrixJson 'Candidate matrix'
$matrix=Get-Content -LiteralPath $MatrixJson -Raw | ConvertFrom-Json
if([string]$matrix.schema -ne 'WaW360.MultiMapConversionMatrix.v1' -or [string]$matrix.mode -ne 'Candidate'){
    throw 'Expected a generated Candidate matrix, never a native reference.'
}
$selected=@($matrix.maps | Where-Object {[string]$_.map -ieq $sourceMap})
if($selected.Count -ne 1){throw 'Candidate matrix Aztec map identity mismatch.'}
$m=$selected[0]
$items=@($m.emittedCandidates | Where-Object {
    (Split-Path -Path (Split-Path -Path ([string]$_.path) -Parent) -Leaf) -eq $MainVariant
})
if($items.Count -ne 1){throw 'Selected Candidate variant not unique.'}
$entry=$items[0]
$main=RequireFile ([string]$entry.path) 'Genuine PC2XB3 generated main FF'
if([IO.Path]::GetFileName($main) -ine ($map+'.ff')){throw 'Generated main FF basename mismatch.'}
$mainSha=Hash $main
$mainSize=[long](Get-Item -LiteralPath $main).Length
if($mainSha -ine [string]$entry.sha256 -or $mainSize -ne [long]$entry.bytes){
    throw 'Candidate FF differs from its original matrix SHA-256/size.'
}
if($main -match '(?i)codxe_beta_stage|reference|working map references'){
    throw 'Reference-stage main FF is forbidden in this generated candidate.'
}
$runRoot=Split-Path -Path $MatrixJson -Parent
$evidence=@{}
foreach($f in @(Get-ChildItem -LiteralPath $runRoot -Filter 'oat_differential_*.json' -File)){
    try{
        $d=Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json
        if([string]$d.schema -ne 'WaW360.MultiMapOatDifferential.v1' -or
            [string]$d.matrix -ine $MatrixJson -or [string]$d.gitCommit -ne [string]$matrix.gitCommit){continue}
        foreach($o in @($d.results | Where-Object {$null -ne $_})){
            if([string]$o.map -ine $sourceMap -or [string]$o.variant -ne $MainVariant -or
                [string]$o.sha256 -ine $mainSha -or [long]$o.bytes -ne $mainSize -or
                [int]$o.exit -ne 0){continue}
            if([string]$o.oatMode -eq 'List' -and [string]$o.status -eq 'OAT_LIST_CLEAN_DIAGNOSTIC'){
                $evidence['List']=$f.FullName
            }
            if([string]$o.oatMode -eq 'Extract' -and [string]$o.status -eq 'OAT_EXTRACT_CLEAN_DIAGNOSTIC'){
                $evidence['Extract']=$f.FullName
            }
        }
    }catch{Write-Host "[AZTEC-FIRST-EXCEPTION] Ignoring unreadable OAT report: $($f.FullName)"}
}
if(-not $evidence.ContainsKey('List') -or -not $evidence.ContainsKey('Extract')){
    throw 'Main FF requires same-Candidate, same-SHA OAT List AND Extract PASS evidence before staging.'
}
if([string]::IsNullOrWhiteSpace($PcMapsRoot)){
    $PcMapsRoot=Join-Path (Split-Path -Path $repo -Parent) 'PC Maps'
}
$PcMapsRoot=(Resolve-Path -LiteralPath $PcMapsRoot -ErrorAction Stop).Path
$pcMap=Join-Path $PcMapsRoot $SourceMapFolderName
if(-not(Test-Path -LiteralPath $pcMap -PathType Container)){
    throw "Original PC map folder not found: $pcMap"
}
Write-Host '[AZTEC-FIRST-EXCEPTION] ====================================================='
Write-Host '[AZTEC-FIRST-EXCEPTION] FIRST-EXCEPTION EXPERIMENT ONLY. NOT A PRODUCTION RELEASE.'
Write-Host "[AZTEC-FIRST-EXCEPTION] Variant=$MainVariant"
Write-Host "[AZTEC-FIRST-EXCEPTION] Main=$main"
Write-Host "[AZTEC-FIRST-EXCEPTION] Main bytes=$mainSize SHA256=$mainSha"
Write-Host '[AZTEC-FIRST-EXCEPTION] Same-SHA OAT List and Extract: PASS.'
if(-not $Stage){
    Write-Host '[AZTEC-FIRST-EXCEPTION] DRY RUN: no files copied. Add -Stage to make a new generated main + converted load debug package.'
    return
}

$runId=(Get-Date -Format 'yyyyMMdd_HHmmss')+'_'+([guid]::NewGuid().ToString('N').Substring(0,8))
$run=Join-Path $repo ('output\native_debug_stage\aztec_first_exception_'+$runId)
$loadOut=Join-Path $run 'pc_selected_load_conversion'
$target=Join-Path $run ('_codxe\usermaps\'+$map)
if(Test-Path -LiteralPath $run){throw "Refusing to overwrite existing run: $run"}
New-Item -ItemType Directory -Path $loadOut -Force | Out-Null
try{
    $dll=Join-Path $repo 'src\WaW360.Cli\bin\Release\net8.0-windows\WaW360.Cli.dll'
    if(-not(Test-Path -LiteralPath $dll -PathType Leaf)){
        throw 'WaW360 CLI missing. Run dotnet build .\src\WaW360.Cli\WaW360.Cli.csproj -c Release once.'
    }
    $stdout=Join-Path $run 'pc_selected_load.stdout.log'
    $stderr=Join-Path $run 'pc_selected_load.stderr.log'
    $arguments=@($dll,'aztec-native-test-load-emit',$PcMapsRoot,$SourceMapFolderName,$loadOut)
    $argsLine=(@($arguments | ForEach-Object {'"'+([string]$_).Replace('"','\"')+'"'})) -join ' '
    Write-Host '[AZTEC-FIRST-EXCEPTION] Converting PC-selected native load fastfile. No full reference FF is copied.'
    $p=Start-Process -FilePath 'dotnet' -ArgumentList $argsLine -RedirectStandardOutput $stdout -RedirectStandardError $stderr -NoNewWindow -PassThru -Wait -ErrorAction Stop
    if([int]$p.ExitCode -ne 0){
        Get-Content -LiteralPath $stdout -Tail 20 -ErrorAction SilentlyContinue | ForEach-Object {Write-Host $_}
        Get-Content -LiteralPath $stderr -Tail 20 -ErrorAction SilentlyContinue | ForEach-Object {Write-Host $_}
        throw "Load converter failed exit=$($p.ExitCode). Logs: $stdout"
    }
    $loadReport=Join-Path $loadOut (Join-Path $SourceMapFolderName (Join-Path 'linker_v2_load_emit' ($SourceMapFolderName+'_linker_v2_load_emit.json')))
    $loadReport=RequireFile $loadReport 'PC-derived load emitter report'
    $l=Get-Content -LiteralPath $loadReport -Raw | ConvertFrom-Json
    if(-not [bool]$l.Verified -or -not [bool]$l.HardwareReady -or
        -not [bool]$l.GraphOrderVerified -or -not [bool]$l.ZoneReadBackVerified -or
        -not [bool]$l.WrapperRoundTripVerified){
        throw 'Original-PC load converter proof failed; no native test pair staged.'
    }
    $load=RequireFile ([string]$l.OutputFastFile) 'Converter-generated native load FF'
    $pcLoad=RequireFile ([string]$l.SelectedSourceFastFile) 'Original PC selected load FF'
    if(-not $pcLoad.StartsWith($PcMapsRoot.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
        throw 'Canonical selected load source is not within the original PC source root.'
    }
    if([IO.Path]::GetFileName($load) -ine ($map+'_load.ff')){throw 'Load FF basename wrong.'}
    $loadSha=Hash $load
    if($loadSha -ine [string]$l.FastFileSha256){throw 'Converted load FF SHA mismatch against emitter report.'}

    # Independently validate the SOURCE-CONVERTED load with Xenon OAT (both
    # modes). This is evidence only; even 0/0 parsing never proves gameplay.
    $oat=Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
    if(-not(Test-Path -LiteralPath $oat -PathType Leaf)){
        $oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
    }
    if(-not(Test-Path -LiteralPath $oat -PathType Leaf)){
        throw 'Pinned native Xenon OAT Unlinker unavailable; load OAT validation cannot be skipped.'
    }
    $oatTmp=Join-Path ([IO.Path]::GetTempPath()) ('AztecNativeLoad_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    New-Item -Path $oatTmp -ItemType Directory -Force | Out-Null
    try{
        $shortLoad=Join-Path $oatTmp ($map+'_load.ff')
        CopyVerified $load $shortLoad $loadSha
        foreach($mode in @('List','Extract')){
            $oatStdout=Join-Path $run ('pc_load_oat_'+$mode+'.stdout.log')
            $oatStderr=Join-Path $run ('pc_load_oat_'+$mode+'.stderr.log')
            $argParts=if($mode -eq 'List'){
                @('--list','--no-color',$shortLoad)
            }else{
                $oatExtract=Join-Path $oatTmp 'extracted'
                New-Item -Path $oatExtract -ItemType Directory -Force | Out-Null
                @('--no-color','--output-folder',$oatExtract,$shortLoad)
            }
            $oatArgs=(@($argParts | ForEach-Object {'"'+([string]$_).Replace('"','\"')+'"'})) -join ' '
            $oatProcess=Start-Process -FilePath $oat -ArgumentList $oatArgs -RedirectStandardOutput $oatStdout -RedirectStandardError $oatStderr -NoNewWindow -PassThru -Wait
            $outText=if(Test-Path -LiteralPath $oatStdout){[IO.File]::ReadAllText($oatStdout)}else{''}
            $errText=if(Test-Path -LiteralPath $oatStderr){[IO.File]::ReadAllText($oatStderr)}else{''}
            $oatText=$outText+[Environment]::NewLine+$errText
            $zero=($oatText -match '(?im)Finished with\s+0\s+warnings?\s*,\s*0\s+errors?')
            $errorLine=($oatText -match '(?im)^\s*(?:ERROR\s*:|FATAL\s*:|Unhandled exception)')
            if([int]$oatProcess.ExitCode -ne 0 -or -not $zero -or $errorLine){
                throw "PC-derived LOAD FF OAT $mode BLOCKED, exit=$($oatProcess.ExitCode); log=$oatStdout stderr=$oatStderr"
            }
            Write-Host "[AZTEC-FIRST-EXCEPTION] SOURCE-CONVERTED LOAD OAT $mode PASS 0 warnings 0 errors SHA256=$loadSha"
        }
    }finally{
        Remove-Item -LiteralPath $oatTmp -Recurse -Force -ErrorAction SilentlyContinue
    }

    New-Item -ItemType Directory -Path $target -Force | Out-Null
    $stagedMain=Join-Path $target ($map+'.ff')
    $stagedLoad=Join-Path $target ($map+'_load.ff')
    CopyVerified $main $stagedMain $mainSha
    CopyVerified $load $stagedLoad $loadSha

    # Original-PC text metadata is optional; no retail/reference FFs, PAKs,
    # highmips, DDS or audio files are borrowed or fabricated.
    $metadata=New-Object 'System.Collections.Generic.List[object]'
    foreach($name in @('map.arena','mod.arena','map.json')){
        $src=Join-Path $pcMap $name
        if(Test-Path -LiteralPath $src -PathType Leaf){
            $dest=Join-Path $target $name
            $h=Hash $src
            CopyVerified $src $dest $h
            $metadata.Add([pscustomobject]@{file=$name;source=$src;sha256=$h})
        }
    }
    $manifest=Join-Path $run 'PROVENANCE_NATIVE_DEBUG_NOT_RELEASE.json'
    [ordered]@{
        schema='WaW360.AztecFirstExceptionCanary.v1'
        status='EXPERIMENTAL_FIRST_EXCEPTION_NOT_PRODUCTION'
        createdUtc=[DateTime]::UtcNow.ToString('o')
        variant=$MainVariant
        candidateMatrix=$MatrixJson
        matrixGitCommit=[string]$matrix.gitCommit
        stagedMapDirectory=$target
        main=[ordered]@{source=$main;staged=$stagedMain;sha256=$mainSha;bytes=$mainSize;oatList=$evidence['List'];oatExtract=$evidence['Extract']}
        load=[ordered]@{originalPcSource=$pcLoad;generated=$load;staged=$stagedLoad;sha256=$loadSha;bytes=[long](Get-Item $load).Length;defeatReferenceDonorUsed=[bool]$l.DefeatReferenceDonorUsed;retailStockOriginProven=[bool]$l.RetailStockOriginProven}
        optionalOriginalPcMetadata=@($metadata.ToArray())
        intentionallyOmitted=@('preview.dds','highmip','sounds','_patch.ff','.pak','_load.bik')
        copiedNativeReferenceFastFiles=0
        playableConversionProven=$false
        nativeReleaseAuthorized=$false
    }|ConvertTo-Json -Depth 9|Set-Content -LiteralPath $manifest -Encoding UTF8
    @(
        'EXPERIMENTAL XBOX FIRST-EXCEPTION CANARY. NOT A PLAYABLE MAP OR RELEASE.'
        "Main SHA-256: $mainSha"
        "Load SHA-256: $loadSha"
        'Back up the current Xbox map before testing. Do not overwrite the only working map.'
        'Stage only these two generated fastfiles under _codxe/usermaps/nazi_zombie_aztec.'
        'Expect possible menu return/crash. Capture WV first exception, Watson, CodXe log and reached loading stage.'
        'Never claim converter-gameplay success without matching on-console and manifest SHA-256.'
        "Manifest: $manifest"
    )|Set-Content -LiteralPath (Join-Path $run 'READ_THIS_FIRST_NATIVE_DEBUG.txt') -Encoding UTF8
    Write-Host '[AZTEC-FIRST-EXCEPTION] ====================================================='
    Write-Host "[AZTEC-FIRST-EXCEPTION] Generated main+load both staged and SHA-verified."
    Write-Host "[AZTEC-FIRST-EXCEPTION] MAIN: $mainSize B SHA256=$mainSha"
    Write-Host "[AZTEC-FIRST-EXCEPTION] LOAD: $((Get-Item $load).Length) B SHA256=$loadSha"
    Write-Host "[AZTEC-FIRST-EXCEPTION] Original selected PC load: $pcLoad"
    Write-Host "[AZTEC-FIRST-EXCEPTION] Xbox-copy candidate folder: $target"
    Write-Host "[AZTEC-FIRST-EXCEPTION] Manifest: $manifest"
    Write-Host '[AZTEC-FIRST-EXCEPTION] No Xbox installation altered. Not a production release.'
}catch{
    Write-Host "[AZTEC-FIRST-EXCEPTION] BLOCKED: $($_.Exception.Message)"
    Write-Host "[AZTEC-FIRST-EXCEPTION] Incomplete debug run, if any, MUST NOT be installed: $run"
    throw
}
