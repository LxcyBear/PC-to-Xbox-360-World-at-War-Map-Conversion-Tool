[CmdletBinding()]
param(
    [string]$PcMapsRoot,
    [string]$SourceMapFolderName='nazi_zombie_aztec_V2.2',
    [string]$PackageTemplateDirectory,
    [string]$ConvertedMapsRoot,
    [switch]$PublishApproved
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Minimal first hardware test, NOT polished gameplay:
# * Real 5-model / 735-GfxSurface / 39-Material GfxWorld main FF;
# * Original-PC SELECTED and individually OAT-passing load FF;
# * CoD Xe metadata and streamed sounds copied IF actually present;
# * Opt-in minimum native FastFile load-only approval with original
#   strict complete-package approval still available by default in gate.
#   Actual CoD Xe T4 SP fastfile.cpp uses usermaps/<map>/<name>.ff, not
#   map.json/preview.dds, to resolve the first FF open.
# Never copy an entire donor load FF, fake metadata, publish an unapproved FF,
# or overwrite prior OAT-passing world/model artifacts.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$map='nazi_zombie_aztec'
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$worldReport=Join-Path $base 'pc_gate8_5x_ff\aztec_pc_world_core_integrated_oat_run.json'
$approvalScript=Join-Path $PSScriptRoot 'Approve-AztecNativeSmokeTest.ps1'
$stagingRoot=Join-Path $repo 'output\native_smoke_test_staging'
$stage=Join-Path $stagingRoot $map
$reports=Join-Path $stagingRoot ($map+'_reports')
$releaseReport=Join-Path $reports 'native_smoke_test_package_release.json'
$reportPath=Join-Path $reports 'aztec_minimum_native_test_package.json'
$loadOutput=Join-Path $repo 'output\native_smoke_test_pc_load'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$oneDrive=if($env:OneDrive){$env:OneDrive}else{Join-Path $env:USERPROFILE 'OneDrive'}
$pcRoot=if($PcMapsRoot){[IO.Path]::GetFullPath($PcMapsRoot)}else{Join-Path $oneDrive 'Desktop\My Xbox\My Projects\World at War\PC Maps'}
$mainName=$map+'.ff'
$loadName=$map+'_load.ff'
$blockers=New-Object 'System.Collections.Generic.List[string]'
$mainSource='';$mainSha='';$loadSource='';$loadSha='';$template=''
$stockRuntimeAsset=$false
$releaseStatus='NOT_RUN'
New-Item -ItemType Directory -Force -Path $stage,$reports | Out-Null
function Say([string]$message){Write-Host "[AZTEC-MIN-TEST] $message"}
function Block([string]$message){$blockers.Add($message);Say "BLOCKER: $message"}
function ValidFastfile([string]$path){
    return -not [string]::IsNullOrWhiteSpace($path) -and
        (Test-Path -LiteralPath $path -PathType Leaf) -and
        (Get-Item -LiteralPath $path).Length -gt 64
}
function CopySha([string]$source,[string]$dest){
    Copy-Item -LiteralPath $source -Destination $dest -Force
    $hash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    if((Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash -ine $hash){
        throw "Copy checksum changed: $source"
    }
    return $hash
}
Say '=========================================================='
Say 'MINIMAL AZTEC NATIVE PACKAGE: KEEP FIVE-MODEL GFXWORLD PASS'
Say 'Approval requires BOTH final FFs OAT PASS and complete CoD Xe usermap.'
Say 'No 27-model/polished-lighting/script-success prerequisites.'
try{
    # First copy ONLY the OAT-verified original-PC source-derived world FF.
    if(-not(Test-Path -LiteralPath $worldReport -PathType Leaf)){
        Block "Known-good main world proof missing: $worldReport"
    }else{
        $w=Get-Content -LiteralPath $worldReport -Raw | ConvertFrom-Json
        if([string]$w.schema -ne 'WaW360.AztecPcGfxWorldIntegratedFastFileOatRun.v1' -or
           [string]$w.status -ne 'AZTEC_GATE8_FIVE_REAL_XMODELS_8509_ROOT_WORLD_OAT_PASS' -or
           -not [bool]$w.OatPass -or -not [bool]$w.fiveSourceOwnedXModelProbe -or
           [int]$w.realXModelRootsInCurrentFf -ne 5 -or
           [int]$w.realPcGfxSurfaceCount -ne 735 -or
           [int]$w.realPcGfxWorldMaterialRoots -ne 39){
            Block 'The pinned real GfxWorld main OAT proof no longer matches 5 XModels, 735 surfaces, and 39 Materials.'
        }else{
            $mainSource=[string]$w.outputFastFile
            if(-not (ValidFastfile $mainSource)){Block "Five-model main source FF missing: $mainSource"}
            else{
                $mainSha=CopySha $mainSource (Join-Path $stage $mainName)
                Say "STAGED real source GfxWorld main FF; sha256=$mainSha"
            }
        }
    }
    # Generate the selected original-PC _load using the existing V2
    # serializer. Never use arbitrary Xbox zone bytes or rename map.ff.
    if(-not(Test-Path -LiteralPath $cli -PathType Leaf) -or
       -not(Test-Path -LiteralPath $pcRoot -PathType Container)){
        Block "Cannot run original-PC load conversion: project=$cli pcRoot=$pcRoot"
    }else{
        $loadLog=Join-Path $reports 'selected_pc_load_emission.log'
        Say "SELECTED original-PC load conversion: source=$pcRoot folder=$SourceMapFolderName"
        $old=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            $out=@(& dotnet run --project $cli -c Release -- aztec-native-test-load-emit $pcRoot $SourceMapFolderName $loadOutput 2>&1)
            $exitCode=[int]$LASTEXITCODE
        }finally{$ErrorActionPreference=$old}
        $out|ForEach-Object{Say ([string]$_)}
        $out|ForEach-Object{[string]$_}|Set-Content -LiteralPath $loadLog -Encoding UTF8
        $loadReport=Join-Path $loadOutput ($SourceMapFolderName+'\linker_v2_load_emit\'+$SourceMapFolderName+'_linker_v2_load_emit.json')
        if($exitCode -ne 0){
            Block "Original-PC selected load conversion failed (exit=$exitCode); see $loadLog. No borrowed donor _load FF."
        }elseif(-not(Test-Path -LiteralPath $loadReport -PathType Leaf)){
            Block "PC selected load FastFile proof missing: $loadReport"
        }else{
            $l=Get-Content -LiteralPath $loadReport -Raw | ConvertFrom-Json
            $ff=[string]$l.OutputFastFile
            $loadSource=[string]$l.SelectedSourceFastFile
            $stockRuntimeAsset=[bool]$l.DefeatReferenceDonorUsed
            if(-not [bool]$l.Verified -or -not [bool]$l.HardwareReady -or
               -not [bool]$l.GraphOrderVerified -or -not [bool]$l.ZoneReadBackVerified -or
               -not [bool]$l.WrapperRoundTripVerified -or
               [string]::IsNullOrWhiteSpace($loadSource) -or
               -not(ValidFastfile $ff) -or
               [IO.Path]::GetFileName($ff) -ine $loadName){
                Block 'Converted original-PC _load FF is not hardware-candidate verified or is the wrong name.'
            }else{
                $loadSha=CopySha $ff (Join-Path $stage $loadName)
                if($loadSha -ine [string]$l.FastFileSha256){
                    throw 'Source-selected converted _load FF SHA does not match emitter manifest.'
                }
                Say "STAGED selected PC load FF sha256=$loadSha source=$loadSource"
                if($stockRuntimeAsset){
                    Say 'NOTE: V2 uses its documented stock defeat runtime asset; it does NOT borrow an entire Xbox _load FastFile.'
                }
            }
        }
    }

    # Preserve any original/user-owned CoD Xe metadata and audio.
    # For the first FF load-only test, CoD Xe resolves usermaps by .ff names,
    # and does NOT require map.json, preview.dds or sounds/ to open an FF.
    # Absence of those optional inputs is recorded, not represented as full
    # audio/gameplay readiness. Never invent missing metadata/audio.
    # Clear only our owned staging optional paths to prevent stale files
    # being mistaken for source data on a later run.
    foreach($name in @('map.json','preview.dds','description.txt','preview.png','mod.csv')){
        $optional=Join-Path $stage $name
        if(Test-Path -LiteralPath $optional -PathType Leaf){
            Remove-Item -LiteralPath $optional -Force
        }
    }
    foreach($name in @('sounds','highmip')){
        $optional=Join-Path $stage $name
        if(Test-Path -LiteralPath $optional -PathType Container){
            Remove-Item -LiteralPath $optional -Recurse -Force
        }
    }
    $candidates=New-Object 'System.Collections.Generic.List[string]'
    if($PackageTemplateDirectory){
        $candidates.Add([IO.Path]::GetFullPath($PackageTemplateDirectory))
    }else{
        $candidates.Add((Join-Path $pcRoot $SourceMapFolderName))
        $candidates.Add((Join-Path $pcRoot $map))
        $candidates.Add((Join-Path $repo ('Converted Maps\'+$map)))
        $candidates.Add((Join-Path $oneDrive ('Desktop\My Xbox\World at War\Converter\Converted Maps\'+$map)))
        $candidates.Add((Join-Path $oneDrive ('Desktop\My Xbox\My Projects\World at War\Converted Maps\'+$map)))
    }
    # Each optional file/folder can have a different original source.
    # Never require all three items to coexist in one directory.
    $copied=New-Object 'System.Collections.Generic.List[string]'
    foreach($candidate in $candidates){
        if(-not(Test-Path -LiteralPath $candidate -PathType Container)){continue}
        if(-not $template){$template=$candidate}
        foreach($name in @('map.json','preview.dds','description.txt','preview.png','mod.csv')){
            $src=Join-Path $candidate $name
            $dst=Join-Path $stage $name
            if((Test-Path -LiteralPath $src -PathType Leaf) -and
               -not(Test-Path -LiteralPath $dst -PathType Leaf)){
                $h=CopySha $src $dst
                $copied.Add($name+' <- '+$candidate)
            }
        }
        foreach($name in @('sounds','highmip')){
            $src=Join-Path $candidate $name
            $dst=Join-Path $stage $name
            if((Test-Path -LiteralPath $src -PathType Container) -and
               -not(Test-Path -LiteralPath $dst -PathType Container)){
                Copy-Item -LiteralPath $src -Destination $stage -Recurse
                $copied.Add($name+'/ <- '+$candidate)
            }
        }
    }
    Say "OPTIONAL package resources recovered=$($copied.Count)"
    foreach($item in $copied){Say "PRESERVED $item"}
    if($copied.Count -eq 0){
        Say 'No metadata/preview/sounds was found; these are OPTIONAL for CoD Xe initial FF open.'
        Say 'Missing external sounds may fail once playback starts; this remains a load-only smoke.'
    }
    if($blockers.Count -eq 0){
        Say 'RUNNING ACTUAL OAT APPROVAL: two FFs + full 735-surface GfxWorld; MINIMAL LOAD-ONLY permits missing optional assets.'
        $old=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            $auditLines=@(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $approvalScript -PackageDirectory $stage -MapName $map -ReportDirectory $reports -MinimumLoadSmokeTest 2>&1)
            $auditExit=[int]$LASTEXITCODE
        }finally{$ErrorActionPreference=$old}
        $auditLines|ForEach-Object {Say ([string]$_)}
        if($auditExit -ne 0 -or -not(Test-Path -LiteralPath $releaseReport -PathType Leaf)){
            Block "Strict native OAT/package approval failed; report=$releaseReport"
        }else{
            $release=Get-Content -LiteralPath $releaseReport -Raw|ConvertFrom-Json
            $releaseStatus=[string]$release.status
            if($releaseStatus -notin @('HARDWARE_TEST_READY','HARDWARE_TEST_READY_MINIMAL_LOAD_ONLY') -or
               -not [bool]$release.bothFinalFastfilesOatPass -or
               -not [bool]$release.realMainGfxWorldAnd735MaterialReferencesOatPass -or
               -not [bool]$release.minimalNativeFastFileLoadApproved -or
               -not [bool]$release.readyForFirstControlledHardwareSmokeTest){
                Block 'Two-FF/real GfxWorld OAT release did not authorize a minimum native loading smoke.'
            }
        }
    }
    if($PublishApproved -and $blockers.Count -eq 0){
        if(-not $ConvertedMapsRoot){
            Block 'Approved copy requires -ConvertedMapsRoot: do not guess your Windows Converted Maps location.'
        }else{
            $destRoot=[IO.Path]::GetFullPath($ConvertedMapsRoot)
            $dest=Join-Path $destRoot $map
            New-Item -ItemType Directory -Path $destRoot -Force | Out-Null
            if(Test-Path -LiteralPath $dest){
                throw "Approved Converted Maps destination exists; no overwriting: $dest"
            }
            Copy-Item -LiteralPath $stage -Destination $destRoot -Recurse
            foreach($name in @($mainName,$loadName,'map.json','preview.dds')){
                $sourceItem=Join-Path $stage $name
                $destItem=Join-Path $dest $name
                if(Test-Path -LiteralPath $sourceItem -PathType Leaf){
                    if(-not(Test-Path -LiteralPath $destItem -PathType Leaf) -or
                       (Get-FileHash -LiteralPath $sourceItem -Algorithm SHA256).Hash -ine
                       (Get-FileHash -LiteralPath $destItem -Algorithm SHA256).Hash){
                        throw "Published approved package checksum changed: $name"
                    }
                }
            }
            Say "PUBLISHED approved test package to Converted Maps: $dest"
        }
    }
}
catch{Block $_.Exception.Message}
finally{
    $approved=$blockers.Count -eq 0 -and
        $releaseStatus -in @('HARDWARE_TEST_READY','HARDWARE_TEST_READY_MINIMAL_LOAD_ONLY')
    $result=[ordered]@{
        schema='WaW360.AztecMinimumNativeSmokePackage.v1'
        status=if($approved){'AZTEC_FIRST_NATIVE_SMOKE_PACKAGE_APPROVED'}else{'AZTEC_FIRST_NATIVE_SMOKE_PACKAGE_BLOCKED'}
        mainSourceReport=$worldReport;mainSource=$mainSource;stagedMainSha256=$mainSha
        selectedPcLoadSource=$loadSource;stagedLoadSha256=$loadSha
        loadConverterUsesDocumentedStockDefeatRuntimeAsset=$stockRuntimeAsset
        metadataTemplate=$template;stage=$stage
        officialHardwareReleaseReport=$releaseReport;releaseStatus=$releaseStatus
        blockers=@($blockers.ToArray())
        fullGraphicsPolishRequired=$false
        hardwareReady=$approved;runtimeGameplayGuaranteed=$false
        minimumLoadOnlyApproval=($approved -and $releaseStatus -eq 'HARDWARE_TEST_READY_MINIMAL_LOAD_ONLY')
        fullPackageMetadataAndSoundPresent=([bool](Test-Path -LiteralPath (Join-Path $stage 'map.json') -PathType Leaf) -and
            [bool](Test-Path -LiteralPath (Join-Path $stage 'preview.dds') -PathType Leaf) -and
            [bool](Test-Path -LiteralPath (Join-Path $stage 'sounds') -PathType Container))
        note='Never borrowed an entire Xbox load.ff. Initial CoD Xe T4 SP fastfile lookup needs the two original-map-named FFs, not optional map.json/preview.dds/sounds. Both must individually OAT load with real 735-surface main GfxWorld. Missing optional resources limit audio/display but not initial FF-open test. This is not full asset, gameplay, or audio completion.'
    }
    $result|ConvertTo-Json -Depth 7|Set-Content -LiteralPath $reportPath -Encoding UTF8
    Say '=========================================================='
    Say "status=$($result.status) blockers=$($blockers.Count)"
    Say "report=$reportPath"
    if($approved){
        Say "FIRST CONTROLLED NATIVE XBOX LOAD-ONLY TEST APPROVED! Local package: $stage"
        Say 'OPTIONAL METADATA / SOUND MAY BE MISSING. Capture FIRST exception, do not expect full gameplay.'
    }else{
        foreach($b in $blockers){Say "BLOCKER: $b"}
        Say 'NO XBOX COPY. Fix the exact blocker; not cosmetic model/lighting work.'
    }
}
if($blockers.Count -ne 0){throw "Minimum Aztec native test package still blocked; details: $reportPath"}
