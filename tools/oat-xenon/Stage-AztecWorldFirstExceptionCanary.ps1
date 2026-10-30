[CmdletBinding()]
param(
    [string]$WorldReportJson='',
    [string]$LoadProvenanceJson='',
    [string]$UnlinkerExe='',
    [switch]$Stage
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Manual FIRST-EXCEPTION world-load experiment. Not a release gate,
# not a production-ready world, and never a native-reference FF transplant.
# Neither the original world scratch FF nor the original load-stage files
# are modified. No console files are installed by this script.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$map='nazi_zombie_aztec'
$expectedWorldSha='0CBD019ABB5D702429B9EECA9B04A843311FF118DDEDBD5CF012C757F757CF82'
$expectedWorldBytes=3615230L
if([string]::IsNullOrWhiteSpace($WorldReportJson)){
    $WorldReportJson=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping\pc_gate8_29x_ff\aztec_pc_world_core_integrated_oat_run.json'
}
if([string]::IsNullOrWhiteSpace($LoadProvenanceJson)){
    $root=Join-Path $repo 'output\native_debug_stage'
    if(-not(Test-Path -LiteralPath $root -PathType Container)){
        throw 'No prior PC-selected-load debug staging found. Run Stage-AztecFirstExceptionCanary.ps1 -Stage first.'
    }
    foreach($m in @(Get-ChildItem -LiteralPath $root -File -Recurse -Filter PROVENANCE_NATIVE_DEBUG_NOT_RELEASE.json | Sort-Object LastWriteTimeUtc -Descending)){
        try{
            $d=Get-Content -LiteralPath $m.FullName -Raw | ConvertFrom-Json
            if([string]$d.schema -eq 'WaW360.AztecFirstExceptionCanary.v1' -and
               [string]$d.status -eq 'EXPERIMENTAL_FIRST_EXCEPTION_NOT_PRODUCTION' -and
               [string]$d.load.sha256 -match '^[0-9A-Fa-f]{64}$' -and
               -not [bool]$d.nativeReleaseAuthorized){
                $LoadProvenanceJson=$m.FullName
                break
            }
        }catch{}
    }
}
function RequireFile([string]$path,[string]$purpose){
    if([string]::IsNullOrWhiteSpace($path) -or -not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Missing $purpose : $path"
    }
    return (Resolve-Path -LiteralPath $path).Path
}
function Sha([string]$file){return [string](Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash}
function CopyExact([string]$src,[string]$dst,[string]$expectedSha){
    if(Test-Path -LiteralPath $dst){throw "Never overwrite existing diagnostic file: $dst"}
    Copy-Item -LiteralPath $src -Destination $dst -ErrorAction Stop
    if((Sha $dst) -ine $expectedSha -or (Sha $src) -ine $expectedSha){
        throw "Copy changed source/destination SHA-256: $dst"
    }
}
$WorldReportJson=RequireFile $WorldReportJson 'actual PC-derived world FF report'
$w=Get-Content -LiteralPath $WorldReportJson -Raw | ConvertFrom-Json
if([string]$w.schema -ne 'WaW360.AztecPcGfxWorldIntegratedFastFileOatRun.v1' -or
   [string]$w.status -ne 'AZTEC_GATE8_ALL_CLASSIFIED_STATIC_XMODEL_TYPES_8509_ROOT_WORLD_OAT_PASS' -or
   -not [bool]$w.OatPass -or
   [int]$w.realXModelRootsInCurrentFf -ne 28 -or
   [int]$w.realPcGfxSurfaceCount -ne 735 -or
   [int]$w.realPcGfxWorldMaterialRoots -ne 39 -or
   [bool]$w.allOriginalPcGfxWorldDpvsChildrenComplete -or
   [bool]$w.hardwareReady -or [bool]$w.fullNativeWorldSourceClosurePass){
    throw 'Source-world report does not match the expected OAT-verified partial 28-model/735-surface diagnostic.'
}
$world=RequireFile ([string]$w.outputFastFile) 'original PC-derived world FF'
$emitReport=RequireFile ([string]$w.emitterReport) 'native source emitter JSON'
$e=Get-Content -LiteralPath $emitReport -Raw | ConvertFrom-Json
if([string]$e.OutputFastFile -ine [string]$w.outputFastFile -or
   -not [bool]$e.ValidationPass -or
   -not [bool]$e.OatZeroWarningsZeroErrors -or
   -not [bool]$e.ValidationOnly -or [bool]$e.ReadyForHardware -or
   -not [bool]$e.ExactIdentitySequence -or
   [int]$e.NativeRoots -ne 8509 -or
   [int]$e.RealXModelRoots -ne 28 -or
   [int]$e.RealMaterialRoots -ne 39 -or
   [int]$e.RealImageRoots -ne 2 -or
   [int]$e.RealRawFileRoots -ne 114){
    throw 'Real-source scratch FF and original emitter verification manifest disagree.'
}
if([IO.Path]::GetFileName($world) -ine ($map+'.ff')){
    throw 'Source-world main FF filename is not nazi_zombie_aztec.ff.'
}
if($world -match '(?i)codxe_beta_stage|working.map.references|native.reference'){
    throw 'Native reference FF path forbidden.'
}
$worldLen=[long](Get-Item -LiteralPath $world).Length
$worldHash=Sha $world
if($worldLen -ne $expectedWorldBytes -or $worldHash -ine $expectedWorldSha){
    throw "World artifact differs from recorded October 30 validated 3,615,230-byte FF: bytes=$worldLen SHA=$worldHash. Refuse to stage a changed file."
}

$LoadProvenanceJson=RequireFile $LoadProvenanceJson 'prior converter-generated load provenance'
$prov=Get-Content -LiteralPath $LoadProvenanceJson -Raw | ConvertFrom-Json
if([string]$prov.schema -ne 'WaW360.AztecFirstExceptionCanary.v1' -or
   [string]$prov.status -ne 'EXPERIMENTAL_FIRST_EXCEPTION_NOT_PRODUCTION' -or
   [bool]$prov.nativeReleaseAuthorized -or
   [string]$prov.load.sha256 -notmatch '^[0-9A-Fa-f]{64}$'){
    throw 'Previous native-debug load manifest is not an authentic converter staging manifest.'
}
$load=RequireFile ([string]$prov.load.generated) 'genuine PC-selected generated load FF'
$oldStagedLoad=RequireFile ([string]$prov.load.staged) 'SHA-verified prior generated load stage'
$loadHash=Sha $load
if($loadHash -ine [string]$prov.load.sha256 -or
   (Sha $oldStagedLoad) -ine $loadHash -or
   [long](Get-Item -LiteralPath $load).Length -ne [long]$prov.load.bytes -or
   [IO.Path]::GetFileName($load) -ine ($map+'_load.ff')){
    throw 'PC-selected _load.ff source/stage SHA, size or basename mismatch.'
}
if($load -match '(?i)codxe_beta_stage|working.map.references|native.reference'){
    throw 'Native reference load FF forbidden.'
}
$pcSource=RequireFile ([string]$prov.load.originalPcSource) 'original PC _load.ff provenance'
if([IO.Path]::GetFileName($pcSource) -ine ($map+'_load.ff')){
    throw 'Original PC load source filename disagrees with selected source.'
}
Write-Host '[AZTEC-WORLD-NATIVE] =========================================================='
Write-Host '[AZTEC-WORLD-NATIVE] SOURCE-DERIVED 8,509-ROOT WORLD FIRST-EXCEPTION EXPERIMENT.'
Write-Host '[AZTEC-WORLD-NATIVE] Strict production gate and all 4 pending milestones remain BLOCKED.'
Write-Host "[AZTEC-WORLD-NATIVE] MAIN=$worldLen bytes (28 real XModels / 735 surfaces / 39 Materials)"
Write-Host "[AZTEC-WORLD-NATIVE] MAIN SHA256=$worldHash"
Write-Host "[AZTEC-WORLD-NATIVE] LOAD SHA256=$loadHash (selected original PC _load.ff)"
Write-Host '[AZTEC-WORLD-NATIVE] 0/1903 static-draw links, incomplete DPVS/images/runtime. Crashes/menu-return expected.'
if(-not $Stage){
    Write-Host '[AZTEC-WORLD-NATIVE] DRY RUN ONLY. Add -Stage to recheck BOTH native FFs with OAT and create fresh folder.'
    return
}

if([string]::IsNullOrWhiteSpace($UnlinkerExe)){
    $UnlinkerExe=Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
    if(-not(Test-Path -LiteralPath $UnlinkerExe -PathType Leaf)){
        $UnlinkerExe=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
    }
}
$UnlinkerExe=RequireFile $UnlinkerExe 'native Xenon OpenAssetTools Unlinker.exe'
$tag=(Get-Date -Format 'yyyyMMdd_HHmmss')+'_'+([guid]::NewGuid().ToString('N').Substring(0,8))
$run=Join-Path $repo ('output\native_world_debug_stage\aztec_world_first_exception_'+$tag)
$target=Join-Path $run ('_codxe\usermaps\'+$map)
if(Test-Path -LiteralPath $run){throw "Refusing diagnostic output collision: $run"}
New-Item -ItemType Directory -Path $run -Force | Out-Null
function OatCheck([string]$fastfile,[string]$baseName,[string]$expectedSha){
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('AZworldOAT_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    try{
        $short=Join-Path $temp $baseName
        CopyExact $fastfile $short $expectedSha
        foreach($mode in @('List','Extract')){
            $stdout=Join-Path $run ($baseName+'_'+$mode+'.stdout.log')
            $stderr=Join-Path $run ($baseName+'_'+$mode+'.stderr.log')
            if($mode -eq 'List'){
                $oatArguments=@('--list','--no-color',$short)
            }else{
                $oatFolder=Join-Path $temp 'extracted'
                New-Item -ItemType Directory -Path $oatFolder -Force | Out-Null
                $oatArguments=@('--no-color','--output-folder',$oatFolder,$short)
            }
            $quoted=(@($oatArguments | ForEach-Object {'"'+([string]$_).Replace('"','\"')+'"'})) -join ' '
            $proc=Start-Process -FilePath $UnlinkerExe -ArgumentList $quoted -RedirectStandardOutput $stdout -RedirectStandardError $stderr -NoNewWindow -PassThru -Wait -ErrorAction Stop
            $outText=if(Test-Path -LiteralPath $stdout){[IO.File]::ReadAllText($stdout)}else{''}
            $errText=if(Test-Path -LiteralPath $stderr){[IO.File]::ReadAllText($stderr)}else{''}
            $combined=$outText+[Environment]::NewLine+$errText
            $passed=($proc.ExitCode -eq 0 -and
                $combined -match '(?im)Finished with\s+0\s+warnings?\s*,\s*0\s+errors?' -and
                $combined -notmatch '(?im)^\s*(?:ERROR\s*:|FATAL\s*:|Unhandled exception|Loading fastfile failed)')
            if(-not $passed){
                throw "Native OAT $baseName $mode FAILED, exit=$($proc.ExitCode), stdout=$stdout stderr=$stderr"
            }
            Write-Host "[AZTEC-WORLD-NATIVE] $baseName OAT $mode PASS: 0 warnings, 0 errors"
        }
    }finally{
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
try{
    OatCheck $world ($map+'.ff') $worldHash
    OatCheck $load ($map+'_load.ff') $loadHash
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    CopyExact $world (Join-Path $target ($map+'.ff')) $worldHash
    CopyExact $load (Join-Path $target ($map+'_load.ff')) $loadHash

    $meta=New-Object 'System.Collections.Generic.List[object]'
    $oldMapDir=[string]$prov.stagedMapDirectory
    if(-not [string]::IsNullOrWhiteSpace($oldMapDir) -and
        (Test-Path -LiteralPath $oldMapDir -PathType Container)){
        foreach($name in @('map.arena','mod.arena','map.json')){
            $file=Join-Path $oldMapDir $name
            if(Test-Path -LiteralPath $file -PathType Leaf){
                $sha=Sha $file
                CopyExact $file (Join-Path $target $name) $sha
                $meta.Add([pscustomobject]@{file=$name;origin='prior generated diagnostic metadata only';sha256=$sha;source=$file})
            }
        }
    }

    $manifest=Join-Path $run 'PROVENANCE_SOURCE_WORLD_NOT_RELEASE.json'
    [ordered]@{
        schema='WaW360.AztecWorldFirstExceptionCanary.v1'
        status='SOURCE_WORLD_FIRST_EXCEPTION_DEBUG_ONLY'
        createdUtc=[DateTime]::UtcNow.ToString('o')
        report=$WorldReportJson
        emitterReport=$emitReport
        stagedMapDirectory=$target
        sourceWorld=[ordered]@{generated=$world;sha256=$worldHash;bytes=$worldLen;realXModels=28;surfaces=735;materials=39;staticDrawLinks=0;fullDpvs=$false}
        sourceLoad=[ordered]@{generated=$load;sha256=$loadHash;bytes=[long](Get-Item -LiteralPath $load).Length;originalPcSource=$pcSource;previousProvenance=$LoadProvenanceJson;defeatReferenceDonorUsed=[bool]$prov.load.defeatReferenceDonorUsed}
        metadata=@($meta.ToArray())
        absentUnconverted=@('_patch.ff','.pak','_load.bik','highmip/','sounds/','preview.dds')
        copiedNativeReferenceMainOrLoadFfs=0
        nativeReleaseAuthorized=$false
        fullOriginalPcGraphReady=$false
        purpose='Xbox first exception / CodXe zone load diagnostic. Not guaranteed to spawn or render.'
    }|ConvertTo-Json -Depth 9|Set-Content -LiteralPath $manifest -Encoding UTF8

    @(
        'AZTEC PC-SOURCE WORLD FIRST-EXCEPTION PROBE. NOT A RELEASE.'
        "Generated main FF: $world"
        "Main SHA-256: $worldHash"
        "Generated load FF: $load"
        "Load SHA-256: $loadHash"
        "Map folder: $target"
        'This world FF has 28 real source XModels and GfxWorld geometry but incomplete DPVS and no 1903 live static draw links.'
        'Do NOT combine with native reference map FFs. Back up the current Xbox Aztec map outside the active usermaps folder.'
        'On Xbox copy exactly the contents of the isolated map folder, then compare hashes of files copied back.'
        'Start WV FIRST exception recording and xbWatson before launch; capture first fault, CodXe logs, and furthest load checkpoint.'
        'Menu return/crash is an EXPECTED diagnostic, not a failure to collect information.'
        "Manifest=$manifest"
    )|Set-Content -LiteralPath (Join-Path $run 'READ_THIS_FIRST_SOURCE_WORLD_TEST.txt') -Encoding UTF8
    Write-Host '[AZTEC-WORLD-NATIVE] =========================================================='
    Write-Host "[AZTEC-WORLD-NATIVE] TWO GENUINE PC2XB3 FFS STAGED (world + load), SHA-verified."
    Write-Host "[AZTEC-WORLD-NATIVE] Xbox-copy folder: $target"
    Write-Host "[AZTEC-WORLD-NATIVE] Manifest: $manifest"
    Write-Host '[AZTEC-WORLD-NATIVE] NO Xbox install or production release performed.'
}catch{
    Write-Host "[AZTEC-WORLD-NATIVE] BLOCKED: $($_.Exception.Message)"
    Write-Host "[AZTEC-WORLD-NATIVE] DO NOT INSTALL incomplete output folder: $run"
    throw
}
