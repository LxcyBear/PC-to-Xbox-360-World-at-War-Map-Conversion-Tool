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
# * CoD Xe usermap metadata + streamed sounds preserved;
# * Strict existing Approve-AztecNativeSmokeTest.ps1 release audit.
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

    # Preserve original/user-owned CoD Xe metadata and audio. Never
    # invent map.json, DDS, XMA, or copy an opaque donor *.ff from template.
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
    foreach($candidate in $candidates){
        if((Test-Path -LiteralPath (Join-Path $candidate 'map.json') -PathType Leaf) -and
           (Test-Path -LiteralPath (Join-Path $candidate 'preview.dds') -PathType Leaf) -and
           (Test-Path -LiteralPath (Join-Path $candidate 'sounds') -PathType Container)){
            $template=$candidate;break
        }
    }
    if(-not $template){
        Block ('Aztec CoD Xe map.json+preview.dds+sounds source missing. Re-run with -PackageTemplateDirectory <your known original package>. Checked: '+($candidates -join ' | '))
    }else{
        foreach($name in @('map.json','preview.dds')){
            $h=CopySha (Join-Path $template $name) (Join-Path $stage $name)
        }
        foreach($name in @('sounds','highmip')){
            $sourcePath=Join-Path $template $name
            if(Test-Path -LiteralPath $sourcePath -PathType Container){
                $destination=Join-Path $stage $name
                if(Test-Path -LiteralPath $destination -PathType Container){
                    Remove-Item -LiteralPath $destination -Recurse -Force
                }
                Copy-Item -LiteralPath $sourcePath -Destination $stage -Recurse
            }
        }
        foreach($name in @('description.txt','preview.png','mod.csv')){
            $from=Join-Path $template $name
            if(Test-Path -LiteralPath $from -PathType Leaf){
                $h=CopySha $from (Join-Path $stage $name)
            }
        }
        Say "STAGED original map.json, preview.dds, sounds and optional metadata from $template"
    }
    if($blockers.Count -eq 0){
        Say 'RUNNING EXISTING STRICT HARDWARE APPROVAL (two FFs + real 735-surface world + package).'
        $old=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            $auditLines=@(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $approvalScript -PackageDirectory $stage -MapName $map -ReportDirectory $reports 2>&1)
            $auditExit=[int]$LASTEXITCODE
        }finally{$ErrorActionPreference=$old}
        $auditLines|ForEach-Object {Say ([string]$_)}
        if($auditExit -ne 0 -or -not(Test-Path -LiteralPath $releaseReport -PathType Leaf)){
            Block "Strict native OAT/package approval failed; report=$releaseReport"
        }else{
            $release=Get-Content -LiteralPath $releaseReport -Raw|ConvertFrom-Json
            $releaseStatus=[string]$release.status
            if($releaseStatus -ne 'HARDWARE_TEST_READY' -or
               -not [bool]$release.bothFinalFastfilesOatPass -or
               -not [bool]$release.realMainGfxWorldAnd735MaterialReferencesOatPass -or
               -not [bool]$release.completeCodxePackage -or
               -not [bool]$release.readyForFirstControlledHardwareSmokeTest){
                Block 'Release script did not authorize the exact two-FF native hardware test.'
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
                if((Get-FileHash -LiteralPath (Join-Path $stage $name) -Algorithm SHA256).Hash -ine
                   (Get-FileHash -LiteralPath (Join-Path $dest $name) -Algorithm SHA256).Hash){
                    throw "Published approved package checksum changed: $name"
                }
            }
            Say "PUBLISHED approved test package to Converted Maps: $dest"
        }
    }
}
catch{Block $_.Exception.Message}
finally{
    $approved=$blockers.Count -eq 0 -and $releaseStatus -eq 'HARDWARE_TEST_READY'
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
        note='No borrowed complete Xbox _load.ff; source-selected load converter may use known local stock runtime assets. Real GfxWorld + both FF OAT pass + package metadata/sounds are the ONLY required first-test gates.'
    }
    $result|ConvertTo-Json -Depth 7|Set-Content -LiteralPath $reportPath -Encoding UTF8
    Say '=========================================================='
    Say "status=$($result.status) blockers=$($blockers.Count)"
    Say "report=$reportPath"
    if($approved){
        Say "FIRST CONTROLLED NATIVE XBOX TEST APPROVED! Local package: $stage"
    }else{
        foreach($b in $blockers){Say "BLOCKER: $b"}
        Say 'NO XBOX COPY. Fix the exact blocker; not cosmetic model/lighting work.'
    }
}
if($blockers.Count -ne 0){throw "Minimum Aztec native test package still blocked; details: $reportPath"}
