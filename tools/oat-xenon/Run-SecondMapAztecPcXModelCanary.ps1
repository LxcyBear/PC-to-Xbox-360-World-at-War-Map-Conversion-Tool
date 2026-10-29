[CmdletBinding()]
param([string]$ModelName = '')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$n=Join-Path $base 'native_material_mapping'
$frontPath=Join-Path $n 'pc_model_world_source_frontier\aztec_pc_model_world_source_frontier.json'
$combinedPath=Join-Path $n 'pc_iwd_rawfile_integration\aztec_pc_iwd_rawfile_integration.json'
$baselinePath=Join-Path $base 'second_map_baseline.json'
$bridge=Join-Path $PSScriptRoot 'Run-FRUmansionOatXModelBridge.ps1'
$work=Join-Path $n 'pc_xmodel_canary'
foreach($p in @($frontPath,$combinedPath,$baselinePath,$bridge)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing proven Aztec XModel canary prerequisite: $p"}
}
$front=Get-Content -LiteralPath $frontPath -Raw | ConvertFrom-Json
$combined=Get-Content -LiteralPath $combinedPath -Raw | ConvertFrom-Json
$baseline=Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
if([string]$front.schema -ne 'WaW360.AztecPcModelWorldSourceFrontier.v1' -or
   [string]$front.status -ne 'AZTEC_PC_MODEL_WORLD_SOURCE_FRONTIER_PASS' -or
   [int]$front.nativeRoots -ne 8509 -or
   [int]$front.nativeXModelRoots -ne 284 -or
   [int]$front.nativeOwnedModelsWithPcName -ne 284 -or
   [int]$front.nativeOwnedModelsNotNamedInPcFF -ne 0 -or
   @($front.nativeXModelIdentityBindings).Count -ne 284 -or
   [string]$combined.status -ne 'AZTEC_PC_IWD_RAWFILE_COMBINED_GRAPH_PASS' -or
   [int]$combined.pixelShaExactReadbacks -ne 2 -or
   [int]$combined.rawFileShaExactReadbacks -ne 114 -or
   [string]$baseline.format -ne 'WaW360.SecondMapBaseline.v1' -or
   [string]$baseline.mapName -ine 'nazi_zombie_aztec_V2.2'){
    throw 'Prior exact 284/284 XModel identity or 2-image/114-RawFile proof has changed.'
}
$mods=@($baseline.fastfiles | Where-Object { [string]$_.Role -eq 'mod.ff' })
$modStats=@($front.sourceFastfiles | Where-Object { [string]$_.Role -eq 'mod.ff' })
if($mods.Count -ne 1 -or $modStats.Count -ne 1){
    throw 'Exactly one Aztec mod.ff source and OAT identity report are required.'
}
$modFile=[string]$mods[0].Path
if([string]$modStats[0].SourceFF -ine $modFile -or
   -not (Test-Path -LiteralPath $modFile -PathType Leaf) -or
   [long](Get-Item -LiteralPath $modFile).Length -ne [long]$mods[0].Bytes){
    throw 'Original PC Aztec mod.ff does not match its verified source baseline.'
}
function Norm([string]$value){
    $v=$value.Trim().Trim('"').Replace('\','/').TrimStart('/')
    if($v.StartsWith(',',[StringComparison]::Ordinal)){$v=$v.Substring(1)}
    if($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(0,$v.Length-3)}
    return $v.ToLowerInvariant()
}
$allOwned=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($model in @($front.nativeXModelIdentityBindings)){
    if(-not [bool]$model.NativeReference){
        [void]$allOwned.Add((Norm ([string]$model.NativeName)))
    }
}
if($allOwned.Count -ne 284){throw 'Native Aztec owned XModel names are not distinct.'}
$allow=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($name in @($modStats[0].XModelNames)){
    $key=Norm ([string]$name)
    if($allOwned.Contains($key)){[void]$allow.Add([string]$name)}
}
if($allow.Count -eq 0){throw 'No mod.ff PC XModels correspond to actual native Aztec owned roots.'}
if(-not [string]::IsNullOrWhiteSpace($ModelName) -and -not $allow.Contains($ModelName)){
    throw "Requested PC XModel '$ModelName' is not among the verified mod.ff native owned names. Use the automatic canary or a name from the model-world report."
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
$allowedFile=Join-Path $work 'mod_pc_native_owned_xmodels.txt'
[IO.File]::WriteAllLines($allowedFile,[string[]]@($allow | Sort-Object),[Text.UTF8Encoding]::new($false))
# Intentionally keep short physical %TEMP% bridge corpus after the run:
# the generic source-backed model bridge embeds absolute paths to GLTF,
# classification CSV, and generated Xenon FF in its verified manifest.
# Its output is NEVER placed in usermaps/ or Converted Maps.
$scratch=Join-Path ([IO.Path]::GetTempPath()) ('wawAz4U_'+[guid]::NewGuid().ToString('N').Substring(0,10))
if($scratch.Length -gt 115){throw "OAT XModel exporter needs a genuinely short physical path: $scratch"}
$log=Join-Path $work 'pc_xmodel_canary_bridge.log'
$manifestPath=Join-Path $scratch 'nuclear_4u_oat_xmodel_bridge.json'
$bridgeExit=-1
$bridgeResult=$null
$blocker=''
Write-Host '[AZTEC-PC-XMODEL] ==========================================================='
Write-Host '[AZTEC-PC-XMODEL] FIRST REAL AZTEC PC MODEL -> XENON XMODEL CANARY'
Write-Host '[AZTEC-PC-XMODEL] ==========================================================='
Write-Host "[AZTEC-PC-XMODEL] PC mod.ff names in native owned roots=$($allow.Count)"
Write-Host '[AZTEC-PC-XMODEL] Uses existing classification-fidelity and GLTF/Xenon bridge.'
Write-Host '[AZTEC-PC-XMODEL] Not a hardware test. No native Xbox model data as input.'
Write-Host "[AZTEC-PC-XMODEL] Short physical workspace: $scratch"
$originalSha=(Get-FileHash -LiteralPath $modFile -Algorithm SHA256).Hash
try{
    $args=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$bridge,
        '-PcFastFile',$modFile,'-OutputRoot',$scratch,
        '-AllowedModelNamesFile',$allowedFile)
    if(-not [string]::IsNullOrWhiteSpace($ModelName)){$args+=@('-ModelName',$ModelName)}
    $prior=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & powershell.exe @args 2>&1 |
            ForEach-Object {[string]$_} |
            Set-Content -LiteralPath $log -Encoding UTF8
        $bridgeExit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$prior}
    if(-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)){
        throw "XModel bridge did not write a final manifest (exit=$bridgeExit); read $log"
    }
    $bridgeResult=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if($bridgeExit -ne 0 -or
       [string]$bridgeResult.format -ne 'WaW360.Nuclear4U.OatXModelBridge.v5' -or
       -not [bool]$bridgeResult.bridgePass -or
       -not [bool]$bridgeResult.xenonRoundTripPass -or
       -not [bool]$bridgeResult.partClassificationComplete -or
       -not [bool]$bridgeResult.selectedModelListed -or
       [int]$bridgeResult.xenonReadbackErrors -ne 0 -or
       [int]$bridgeResult.xenonLinkExitCode -ne 0 -or
       [int]$bridgeResult.xenonReadbackExitCode -ne 0 -or
       [string]$bridgeResult.pcFastFile -ine $modFile -or
       -not $allow.Contains([string]$bridgeResult.selectedModel) -or
       [int]$bridgeResult.selectedLodCount -le 0 -or
       [long]$bridgeResult.selectedLodBytes -le 0 -or
       -not (Test-Path -LiteralPath ([string]$bridgeResult.linkedFastFile) -PathType Leaf)){
        throw "Aztec PC model was not classification-complete or failed Xenon readback (exit=$bridgeExit)."
    }
    Write-Host "[AZTEC-PC-XMODEL] Selected original PC model: $($bridgeResult.selectedModel)"
    Write-Host "[AZTEC-PC-XMODEL] Original PC GLTF LODs=$($bridgeResult.selectedLodCount); bytes=$($bridgeResult.selectedLodBytes)"
    Write-Host "[AZTEC-PC-XMODEL] PC partClassification: PASS, bones=$($bridgeResult.selectedPartClassificationBones)"
    Write-Host '[AZTEC-PC-XMODEL] Xbox model FF linked and independently loaded by OAT: PASS'
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-PC-XMODEL] BLOCKER: $blocker"
    if(Test-Path -LiteralPath $log -PathType Leaf){
        foreach($line in @(Get-Content -LiteralPath $log -Tail 14)){Write-Host "[AZTEC-PC-XMODEL] LOG: $line"}
    }
}
$status=if(-not $blocker -and $bridgeExit -eq 0 -and
    $null -ne $bridgeResult -and [bool]$bridgeResult.bridgePass){
    'AZTEC_PC_XMODEL_XENON_CANARY_PASS'
}else{'AZTEC_PC_XMODEL_XENON_CANARY_BLOCKED'}
$result=[ordered]@{
    schema='WaW360.AztecPcXModelXenonCanary.v1'
    status=$status
    sourceFastFile=$modFile
    sourceFastFileSha256=$originalSha
    nativeModelIdentities=284
    pcModelNamesMatchingNative=284
    modXModelsEligibleForCanary=$allow.Count
    selectedModel=$(if($null -eq $bridgeResult){''}else{[string]$bridgeResult.selectedModel})
    pcLods=$(if($null -eq $bridgeResult){0}else{[int]$bridgeResult.selectedLodCount})
    pcGltfBytes=$(if($null -eq $bridgeResult){0}else{[long]$bridgeResult.selectedLodBytes})
    classificationComplete=$(if($null -eq $bridgeResult){$false}else{[bool]$bridgeResult.partClassificationComplete})
    xenonOatRoundtripPass=$(if($null -eq $bridgeResult){$false}else{[bool]$bridgeResult.xenonRoundTripPass})
    originalPcXModelPayloadsPromotedIntoFullAztecGraph=0
    originalPcRawfileReadbacksInPriorGraph=114
    originalPcPixelReadbacksInPriorGraph=2
    bridgeExitCode=$bridgeExit;bridgeManifest=$manifestPath;bridgeLog=$log
    shortPhysicalWorkspace=$scratch
    blocker=$blocker
    readyForXbox=$false
    fullMapConverted=$false
    convertedMapsPromotionApproved=$false
    note='A single original-PC-mod source XModel is independently encoded/reloaded as a diagnostic Xbox fastfile; does not prove the complete Aztec world, other 283 models or material/runtime references.'
}
$reportPath=Join-Path $work 'aztec_pc_xmodel_canary.json'
$result | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Host '[AZTEC-PC-XMODEL] ================= RESULT ==========================='
Write-Host "[AZTEC-PC-XMODEL] status=$status"
Write-Host "[AZTEC-PC-XMODEL] Report: $reportPath"
Write-Host '[AZTEC-PC-XMODEL] Source-backed model-only proof. NOT FOR XBOX.'
if($status -ne 'AZTEC_PC_XMODEL_XENON_CANARY_PASS'){exit 4}
