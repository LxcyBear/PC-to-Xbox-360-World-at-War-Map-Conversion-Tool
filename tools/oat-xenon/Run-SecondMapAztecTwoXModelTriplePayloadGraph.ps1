[CmdletBinding()]
param([switch]$RebuildModelGraph)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$work=Join-Path $base 'pc_two_xmodel_image_rawfile_integration'
$graphRunner=Join-Path $PSScriptRoot 'Run-SecondMapAztecRenderableXModelGraph.ps1'
$graphDocPath=Join-Path $base 'pc_xmodel_renderable_graph\aztec_pc_xmodel_relocatable_graph.json'
$canaryPath=Join-Path $base 'pc_xmodel_renderable_canary\aztec_pc_xmodel_renderable_canary.json'
$tagCanaryPath=Join-Path $base 'pc_xmodel_canary\aztec_pc_xmodel_canary.json'
$tripleProofPath=Join-Path $base 'pc_xmodel_image_rawfile_integration\aztec_pc_xmodel_image_rawfile_integration.json'
$mixedPath=Join-Path $base 'pc_iwd_rawfile_integration\aztec_pc_iwd_rawfile_integration.json'
$imgProofPath=Join-Path $base 'pc_iwd_image_roundtrip\aztec_pc_iwd_xenon_image_roundtrip.json'
$rawProofPath=Join-Path $base 'pc_rawfile_graft\pc_rawfile_graft_result.json'
$rawInputPath=Join-Path $base 'pc_rawfile_graft\pc_rawfile_graft_input.json'
$identityPath=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$nativeProofPath=Join-Path $base 'native_graph_proof\native_graph_proof_result.json'
$bindingPath=Join-Path $base 'renderer_preflight\native_root_binding\nuclear_xenon_root_binding.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
foreach($file in @($graphRunner,$canaryPath,$tagCanaryPath,$tripleProofPath,$mixedPath,$imgProofPath,$rawProofPath,
    $rawInputPath,$identityPath,$nativeProofPath,$bindingPath,$cli,$oat)){
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)){
        throw "Aztec 1-XModel+2-images+114-RawFiles input missing: $file"
    }
}
$canary=Get-Content -LiteralPath $canaryPath -Raw | ConvertFrom-Json
$tagCanary=Get-Content -LiteralPath $tagCanaryPath -Raw | ConvertFrom-Json
$tripleProof=Get-Content -LiteralPath $tripleProofPath -Raw | ConvertFrom-Json
$mixed=Get-Content -LiteralPath $mixedPath -Raw | ConvertFrom-Json
$ip=Get-Content -LiteralPath $imgProofPath -Raw | ConvertFrom-Json
$rawProof=Get-Content -LiteralPath $rawProofPath -Raw | ConvertFrom-Json
$rawInput=Get-Content -LiteralPath $rawInputPath -Raw | ConvertFrom-Json
$identity=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$proof=Get-Content -LiteralPath $nativeProofPath -Raw | ConvertFrom-Json
if([string]$canary.schema -ne 'WaW360.AztecPcRenderableXModelCanary.v1' -or
   [string]$canary.status -ne 'AZTEC_PC_XMODEL_RENDERABLE_MESH_PASS' -or
   [string]$canary.selectedModel -ine 'zombie_x2_icon' -or
   [int]$canary.verifiedRenderableVertices -lt 128 -or
   [int]$canary.verifiedTriangleIndices -lt 192 -or
   -not [bool]$canary.classificationComplete -or
   -not [bool]$canary.xenonOatRoundtripPass -or
   [string]$mixed.status -ne 'AZTEC_PC_IWD_RAWFILE_COMBINED_GRAPH_PASS' -or
   [int]$mixed.nativeRoots -ne 8509 -or
   [int]$mixed.pixelShaExactReadbacks -ne 2 -or
   [int]$mixed.rawFileShaExactReadbacks -ne 114 -or
   [string]$ip.status -ne 'AZTEC_PC_IWD_XENON_IMAGE_ROUNDTRIP_PASS' -or
   @($ip.individualImages).Count -ne 2 -or
   [string]$rawProof.status -ne 'AZTEC_PC_RAWFILE_GRAFT_OAT_READBACK_PASS' -or
   [int]$rawProof.xboxReadbacksExact -ne 114 -or
   [string]$rawInput.format -ne 'WaW360.AztecPcRawFileGraftInput.v1' -or
   @($rawInput.entries).Count -ne 114 -or
   -not [bool]$identity.TypeSequenceVerified -or
   @($identity.Entries).Count -ne 8509 -or
   [string]$proof.status -ne 'IDENTITY_AND_TYPED_GRAPH_IMPORT_PASS' -or
   [string]$proof.nativeSha256 -ine 'e0f3c244dd7b72d0fe71c05faf014c2c9bfb05f770183954b3e7cde625eba98f'){
    throw 'Aztec PC model, prior combined source payloads or 8,509-root native identity proof changed.'
}
if([string]$tagCanary.schema -ne 'WaW360.AztecPcXModelXenonCanary.v1' -or
   [string]$tagCanary.status -ne 'AZTEC_PC_XMODEL_XENON_CANARY_PASS' -or
   [string]$tagCanary.selectedModel -ine 'tag_origin' -or
   -not [bool]$tagCanary.classificationComplete -or
   -not [bool]$tagCanary.xenonOatRoundtripPass -or
   [string]$tagCanary.sourceFastFileSha256 -ine [string]$canary.sourceFastFileSha256 -or
   [string]$tripleProof.status -ne 'AZTEC_PC_XMODEL_IMAGE_RAWFILE_COMBINED_GRAPH_PASS' -or
   [int]$tripleProof.realXModelRoots -ne 1 -or
   [int]$tripleProof.independentPcPixelShaExactReadbacks -ne 2 -or
   [int]$tripleProof.independentPcRawFileShaExactReadbacks -ne 114){
    throw 'Both PC XModel canaries and original 3-way SHA readback must pass before expansion.'
}
if(-not (Test-Path -LiteralPath ([string]$tagCanary.sourceFastFile) -PathType Leaf) -or
   (Get-FileHash -LiteralPath ([string]$tagCanary.sourceFastFile) -Algorithm SHA256).Hash -ine
    [string]$tagCanary.sourceFastFileSha256 -or
   -not (Test-Path -LiteralPath ([string]$tagCanary.bridgeManifest) -PathType Leaf)){
    throw 'PC tag_origin source FF or bridge manifest unavailable/changed.'
}
$tagBridge=Get-Content -LiteralPath ([string]$tagCanary.bridgeManifest) -Raw | ConvertFrom-Json
if([string]$tagBridge.format -ne 'WaW360.Nuclear4U.OatXModelBridge.v5' -or
   [string]$tagBridge.selectedModel -ine 'tag_origin' -or
   -not [bool]$tagBridge.bridgePass -or -not [bool]$tagBridge.xenonRoundTripPass -or
   -not [bool]$tagBridge.partClassificationComplete -or
   -not (Test-Path -LiteralPath ([string]$tagBridge.linkedFastFile) -PathType Leaf)){
    throw 'PC tag_origin standalone Xenon bridge evidence not intact.'
}
$native=[string]$proof.verifiedGoldenXboxFastFile
if(-not (Test-Path -LiteralPath $native -PathType Leaf) -or
   [long](Get-Item -LiteralPath $native).Length -ne 82739469L -or
   (Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash -ine [string]$proof.nativeSha256){
    throw 'Original native Aztec structural reference changed.'
}
if(-not (Test-Path -LiteralPath ([string]$canary.sourceFastFile) -PathType Leaf) -or
   (Get-FileHash -LiteralPath ([string]$canary.sourceFastFile) -Algorithm SHA256).Hash -ine
   [string]$canary.sourceFastFileSha256){
    throw 'Original PC mod.ff XModel source has changed.'
}
$nativeX=@($identity.Entries | Where-Object {
    [string]$_.TypeName -ieq 'xmodel' -and [string]$_.Name -ieq 'zombie_x2_icon'
})
if($nativeX.Count -ne 1){throw "Single Aztec native XModel identity missing or ambiguous: $($nativeX.Count)"}
$modelRoot=$nativeX[0]
function NormModel([string]$v){
    $v=$v.Trim().TrimStart(',').Replace('\','/')
    if($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(0,$v.Length-3)}
    return $v.ToLowerInvariant()
}
$tagRoots=@($identity.Entries | Where-Object {
    [string]$_.TypeName -ieq 'xmodel' -and
    -not ([string]$_.Name).StartsWith(',') -and
    (NormModel ([string]$_.Name)) -eq 'tag_origin'
})
if($tagRoots.Count -ne 1 -or [int]$tagRoots[0].Index -eq [int]$modelRoot.Index){
    throw "Distinct native tag_origin owned XModel root required; found $($tagRoots.Count)"
}
$tagRoot=$tagRoots[0]
$imgGraphRoot=Join-Path $base 'pc_iwd_rawfile_integration\pc_xenon_image_graph'
if(-not (Test-Path -LiteralPath (Join-Path $imgGraphRoot 'xenon_image_graphs') -PathType Container)){
    throw "The already passing original PC 2-image graph cache is absent: $imgGraphRoot"
}
$rawEntries=@($rawInput.entries)
$rawNames=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($e in $rawEntries){
    if(-not $rawNames.Add([string]$e.name) -or
       -not (Test-Path -LiteralPath ([string]$e.sourcePath) -PathType Leaf) -or
       [long](Get-Item -LiteralPath ([string]$e.sourcePath)).Length -ne [long]$e.bytes -or
       (Get-FileHash -LiteralPath ([string]$e.sourcePath) -Algorithm SHA256).Hash -ine [string]$e.sha256){
        throw "Original PC RawFile corpus was modified: $($e.name)"
    }
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
$logRoot=Join-Path $work 'logs'
$emitRoot=Join-Path $work 'scratch_emit'
New-Item -Path $logRoot -ItemType Directory -Force | Out-Null
$graphLog=Join-Path $logRoot 'model_graph.log'
$buildLog=Join-Path $logRoot 'dotnet_build.log'
$emitLog=Join-Path $logRoot 'mixed_emit.log'
$listLog=Join-Path $logRoot 'oat_list.log'
$rawLog=Join-Path $logRoot 'oat_rawfile_readback.log'
$imgLog=Join-Path $logRoot 'oat_image_readback.log'
$bindingFile=Join-Path $work 'aztec_two_pc_xmodel_root_binding.json'
$reportFile=Join-Path $work 'aztec_two_pc_xmodel_image_rawfile_integration.json'
$reportTxt=Join-Path $work 'aztec_two_pc_xmodel_image_rawfile_integration.txt'
$status='AZTEC_PC_TWO_XMODEL_IMAGE_RAWFILE_BLOCKED'
$blocker='';$graphExit=-1;$emitterExit=-1;$rawExact=0;$imgExact=0;$listPass=$false;$modelNamesListed=0
$emit=$null;$modelGraph=$null;$tagGraphSha=''
Write-Host '[AZTEC-PC-2MODEL] ============================================================='
Write-Host '[AZTEC-PC-2MODEL] 2 REAL PC XMODELS (MESH+HELPER) + 2 REAL IMAGES + 114 RAWFILES'
Write-Host '[AZTEC-PC-2MODEL] 8,509 ROOTS; OAT LAYOUT + MODEL/IMAGE/RAWFILE READBACK'
Write-Host '[AZTEC-PC-2MODEL] Scratch only, NEVER DEPLOY ON XBOX.'
Write-Host '[AZTEC-PC-2MODEL] ============================================================='
try{
    if($RebuildModelGraph -or -not (Test-Path -LiteralPath $graphDocPath -PathType Leaf)){
        $prior=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            & powershell.exe '-NoProfile' '-ExecutionPolicy' 'Bypass' '-File' $graphRunner 2>&1 |
                ForEach-Object {[string]$_} | Set-Content -LiteralPath $graphLog -Encoding UTF8
            $graphExit=[int]$LASTEXITCODE
        }finally{$ErrorActionPreference=$prior}
        if($graphExit -ne 0){throw "Aztec PC Xenon relocatable XModel graph extraction failed; see $graphLog"}
    }
    if(-not (Test-Path -LiteralPath $graphDocPath -PathType Leaf)){
        throw "Aztec PC XModel relocatable graph manifest absent: $graphDocPath"
    }
    $modelGraph=Get-Content -LiteralPath $graphDocPath -Raw | ConvertFrom-Json
    if([string]$modelGraph.schema -ne 'WaW360.AztecPcRenderableXModelGraph.v1' -or
       [string]$modelGraph.status -ne 'AZTEC_PC_RENDERABLE_XMODEL_GRAPH_PASS' -or
       [string]$modelGraph.model -ine 'zombie_x2_icon' -or
       [int]$modelGraph.nativeRootIndex -ne [int]$modelRoot.Index -or
       [int]$modelGraph.xenonPackedVertices -lt 128 -or
       [int]$modelGraph.xenonTriangles -lt 64 -or
       [int]$modelGraph.xenonSurfaces -lt 1 -or
       [string]$modelGraph.originalPcSha256 -ine [string]$canary.sourceFastFileSha256){
        throw 'PC-source Aztec XModel graph is not proven/fidelity-complete for zombie_x2_icon.'
    }
    $xmodelRoot=[string]$modelGraph.graphRoot
    $modelDump=Join-Path $xmodelRoot 'dumps\000'
    $graphs=@(Get-ChildItem -LiteralPath (Join-Path $modelDump 'xenon_xmodel_graphs') -Filter '*.json' -File -ErrorAction Stop)
    if($graphs.Count -ne 1 -or
       (Get-FileHash -LiteralPath $graphs[0].FullName -Algorithm SHA256).Hash -ine [string]$modelGraph.graphSha256){
        throw 'Original PC-source relocatable XModel JSON SHA does not match provenance manifest.'
    }
    if(Test-Path -LiteralPath $emitRoot -PathType Container){Remove-Item -LiteralPath $emitRoot -Recurse -Force}
    New-Item -Path $emitRoot -ItemType Directory -Force | Out-Null
    $binding=[ordered]@{
        format='WaW360.AztecPcSingleXModelBinding.v1'
        bindingPass=$true
        nativeRoots=8509
        nativeXModelRoots=284
        model='zombie_x2_icon'
        sourcePCFastFile=[string]$canary.sourceFastFile
        sourcePCFastFileSha256=[string]$canary.sourceFastFileSha256
        graphFileSha256=[string]$modelGraph.graphSha256
        ownedBindings=@([ordered]@{
            NativeRootIndex=[int]$modelRoot.Index
            NativeName=[string]$modelRoot.Name
            CorpusOrdinal=0
            CorpusName='zombie_x2_icon'
        })
        referenceBindings=@()
        note='This one-model diagnostic binds a single source-derived original PC XModel in the full Aztec root sequence. The other 283 XModels remain unconverted placeholders.'
    }
    $binding | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $bindingFile -Encoding UTF8
    Write-Host "[AZTEC-PC-2MODEL] PC XModel graph PASS: vertices=$($modelGraph.xenonPackedVertices) triangles=$($modelGraph.xenonTriangles) surfaces=$($modelGraph.xenonSurfaces)"
    $prior=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
        $buildExit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$prior}
    if($buildExit -ne 0){throw "Incremental .NET XModel graft build failed: $buildLog"}
    Write-Host '[AZTEC-PC-2MODEL] .NET build PASS'
    $args=@('run','--no-build','-c','Release','--project',$cli,'--',
        'xenon-graph-emit',[string]$proof.dumpRoot,$native,$identityPath,
        $bindingPath,$emitRoot,'nazi_zombie_aztec',$oat,'70','436',
        '--aztec-pc-one-xmodel-image-rawfile-graft',$imgGraphRoot,'2',
        $rawInputPath,$xmodelRoot,$bindingFile)
    $prior=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & dotnet @args 2>&1 | ForEach-Object {[string]$_} | Set-Content -LiteralPath $emitLog -Encoding UTF8
        $emitterExit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$prior}
    $emitFile=Join-Path $emitRoot 'nuclear_xenon_renderer_emit.json'
    if(-not (Test-Path -LiteralPath $emitFile -PathType Leaf)){
        throw "Triple payload Xenon emitter did not create its report (exit=$emitterExit); see $emitLog"
    }
    $emit=Get-Content -LiteralPath $emitFile -Raw | ConvertFrom-Json
    if($emitterExit -ne 0 -or -not [bool]$emit.ValidationPass -or
       -not [bool]$emit.LayoutReplayExact -or
       -not [bool]$emit.ExactIdentitySequence -or
       -not [bool]$emit.OatZeroWarningsZeroErrors -or
       -not [bool]$emit.ValidationOnly -or
       [bool]$emit.ReadyForHardware -or
       [int]$emit.NativeRoots -ne 8509 -or
       [int]$emit.RealImageRoots -ne 2 -or
       [int]$emit.RealRawFileRoots -ne 114 -or
       [int]$emit.RealXModelRoots -ne 1 -or
       [int]$emit.RealMaterialRoots -ne 0){
        throw "8,509-root combined 1-XModel+2-image+114-RawFile OAT/layout identity gate failed (exit=$emitterExit)."
    }
    $ff=[string]$emit.OutputFastFile
    if(-not (Test-Path -LiteralPath $ff -PathType Leaf) -or
       -not ([IO.Path]::GetFullPath($ff)).StartsWith(
           ([IO.Path]::GetFullPath($emitRoot)).TrimEnd([char]'\')+'\',
           [StringComparison]::OrdinalIgnoreCase)){
        throw 'Combined FF is absent or not in isolated scratch output directory.'
    }
    Write-Host '[AZTEC-PC-2MODEL] 8509-root Xenon layout/OAT PASS: 1 real XModel, 2 images, 114 RawFiles'
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('wawAz3W_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    if($temp.Length -gt 115){throw 'OAT readback needs short physical temp path.'}
    $rawTemp=Join-Path $temp 'raw'
    $imgTemp=Join-Path $temp 'img'
    New-Item -Path $rawTemp,$imgTemp -ItemType Directory -Force | Out-Null
    try{
        $prior=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            & $oat '--no-color' '--list' $ff 2>&1 |
                ForEach-Object {[string]$_} | Set-Content -LiteralPath $listLog -Encoding UTF8
            $listExit=[int]$LASTEXITCODE
        }finally{$ErrorActionPreference=$prior}
        if($listExit -ne 0 -or
           @(Select-String -LiteralPath $listLog -Pattern '^\s*ERROR:').Count -ne 0){
            throw "OAT asset-list readback failed for combined model FF: $listLog"
        }
        $listed=@(Get-Content -LiteralPath $listLog | Where-Object {
            [string]$_ -match '(?i)(?:^|[\]\s])xmodel\s*,\s*zombie_x2_icon\s*$'
        })
        if($listed.Count -ne 1){throw "Original PC zombie_x2_icon XModel missing/ambiguous in combined OAT list: $($listed.Count)"}
        $listPass=$true
        $prior=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            & $oat '--no-color' '--include-assets' 'rawfile' '--output-folder' $rawTemp $ff 2>&1 |
                ForEach-Object {[string]$_} | Set-Content -LiteralPath $rawLog -Encoding UTF8
            $rawExit=[int]$LASTEXITCODE
        }finally{$ErrorActionPreference=$prior}
        if($rawExit -ne 0 -or @(Select-String -LiteralPath $rawLog -Pattern '^\s*ERROR:').Count -ne 0){
            throw "OAT RawFile readback from mixed model FF failed: $rawLog"
        }
        foreach($e in $rawEntries){
            $file=Join-Path $rawTemp (([string]$e.name).Replace('/','\'))
            if(-not (Test-Path -LiteralPath $file -PathType Leaf) -or
               [long](Get-Item -LiteralPath $file).Length -ne [long]$e.bytes -or
               (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine [string]$e.sha256){
                throw "PC RawFile changed after XModel integration: $($e.name)"
            }
            $rawExact++
        }
        Write-Host "[AZTEC-PC-2MODEL] PC RawFiles: $rawExact/114 exact source SHA readbacks"
        $prior=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            & $oat '--no-color' '--include-assets' 'image' '--image-format' 'IWI' '--output-folder' $imgTemp $ff 2>&1 |
                ForEach-Object {[string]$_} | Set-Content -LiteralPath $imgLog -Encoding UTF8
            $imgExit=[int]$LASTEXITCODE
        }finally{$ErrorActionPreference=$prior}
        if($imgExit -ne 0 -or @(Select-String -LiteralPath $imgLog -Pattern '^\s*ERROR:').Count -ne 0){
            throw "OAT pixel readback from mixed model FF failed: $imgLog"
        }
        $imgSources=@{}
        foreach($r in @($ip.individualImages)){$imgSources[([string]$r.Name).ToLowerInvariant()]=$r}
        $readImages=@(Get-ChildItem -LiteralPath $imgTemp -Filter '*.iwi' -File -Recurse -ErrorAction Stop)
        if($readImages.Count -ne 2){throw "Combined model FF contained $($readImages.Count) real images, expected two."}
        foreach($file in $readImages){
            $relative=$file.FullName.Substring($imgTemp.Length).TrimStart([char]'\').Replace('\','/')
            if($relative.StartsWith('images/',[StringComparison]::OrdinalIgnoreCase)){$relative=$relative.Substring(7)}
            $name=$relative.Substring(0,$relative.Length-4).ToLowerInvariant()
            if(-not $imgSources.ContainsKey($name)){throw "Unexpected extracted PC image: $name"}
            $data=[IO.File]::ReadAllBytes($file.FullName)
            if($data.Length -lt 29 -or $data[0] -ne 73 -or $data[1] -ne 87 -or
               $data[2] -ne 105 -or $data[3] -ne 6 -or $data[4] -ne 13){
                throw "Invalid original PC IWI header in model FF: $name"
            }
            $sha=[Security.Cryptography.SHA256]::Create()
            try{$hash=([BitConverter]::ToString($sha.ComputeHash($data,28,$data.Length-28))).Replace('-','')}
            finally{$sha.Dispose()}
            if($hash -ine [string]$imgSources[$name].InputPayloadSha256){
                throw "PC texture payload SHA mismatch after adding model: $name"
            }
            $imgExact++
        }
        if($imgExact -ne 2 -or $rawExact -ne 114){throw 'Triple-payload exact readback count failed.'}
    }finally{
        if(Test-Path -LiteralPath $temp -PathType Container){Remove-Item -LiteralPath $temp -Recurse -Force}
    }
    $status='AZTEC_PC_XMODEL_IMAGE_RAWFILE_COMBINED_GRAPH_PASS'
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-PC-2MODEL] BLOCKER: $blocker"
    foreach($l in @($graphLog,$buildLog,$emitLog,$listLog,$rawLog,$imgLog)){
        if(Test-Path -LiteralPath $l -PathType Leaf){
            Write-Host "[AZTEC-PC-2MODEL] Last $l"
            foreach($line in @(Get-Content -LiteralPath $l -Tail 8)){Write-Host "[AZTEC-PC-2MODEL]   $line"}
        }
    }
}
$report=[ordered]@{
    schema='WaW360.AztecPcXModelImageRawFileCombinedGraph.v1'
    status=$status;nativeRoots=8509
    pcXModelSource='zombie_x2_icon'
    sourcePcModelRootIndex=[int]$modelRoot.Index
    sourcePcXModelPackedVertices=$(if($null -eq $modelGraph){0}else{[int]$modelGraph.xenonPackedVertices})
    sourcePcXModelTriangles=$(if($null -eq $modelGraph){0}else{[int]$modelGraph.xenonTriangles})
    realXModelRoots=$(if($null -eq $emit){0}else{[int]$emit.RealXModelRoots})
    realImageRoots=$(if($null -eq $emit){0}else{[int]$emit.RealImageRoots})
    realRawFileRoots=$(if($null -eq $emit){0}else{[int]$emit.RealRawFileRoots})
    independentModelIdentityReadback=$listPass
    independentPcPixelShaExactReadbacks=$imgExact
    independentPcRawFileShaExactReadbacks=$rawExact
    layoutReplayExact=$(if($null -eq $emit){$false}else{[bool]$emit.LayoutReplayExact})
    oatZeroWarningsZeroErrors=$(if($null -eq $emit){$false}else{[bool]$emit.OatZeroWarningsZeroErrors})
    modelGraphProof=$graphDocPath
    modelBindingManifest=$bindingFile
    sourceTwoPcImageProof=$imgProofPath
    source114RawFileProof=$rawProofPath
    diagnosticScratchFF=$(if($null -eq $emit){''}else{[string]$emit.OutputFastFile})
    blocker=$blocker
    readyForXbox=$false;convertedMapsPromotionApproved=$false
    fullAztecMapConverted=$false;remainingXModelPayloadsIncomplete=283
    sourceOwnedMaterialsComplete=$false;sourceOwnedWorldGeometryComplete=$false
    sourceOwnedImageGraphComplete=$false
    note='One source PC zombie_x2_icon Xenon mesh, two source PC image graphs and 114 source PC RawFiles in an otherwise incomplete 8509-root diagnostic graph. Native Xbox reference used for structural identity/renderer only. Not safe for Xbox.'
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportFile -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add("Aztec original PC 1 XModel + 2 Images + 114 RawFiles combined: $status")
$lines.Add("OAT identity model=$listPass; PC image SHA=$imgExact/2; RawFile SHA=$rawExact/114")
$lines.Add("Xenon model packed vertices=$($report.sourcePcXModelPackedVertices) triangles=$($report.sourcePcXModelTriangles)")
$lines.Add("Emitter actual native roots=$($report.nativeRoots), model roots=$($report.realXModelRoots), images=$($report.realImageRoots), RawFiles=$($report.realRawFileRoots)")
$lines.Add("Blocker=$blocker")
$lines.Add('The other 283 models, world, materials, images and production renderer source remain incomplete. NOT FOR XBOX.')
[IO.File]::WriteAllLines($reportTxt,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-2MODEL] ================= RESULT ==============================='
Write-Host "[AZTEC-PC-2MODEL] status=$status"
Write-Host "[AZTEC-PC-2MODEL] modelOatListed=$listPass, PC image pixels=$imgExact/2, PC RawFiles=$rawExact/114"
Write-Host "[AZTEC-PC-2MODEL] report=$reportTxt"
Write-Host '[AZTEC-PC-2MODEL] SCRATCH ONLY. NOT FOR XBOX.'
if($status -ne 'AZTEC_PC_XMODEL_IMAGE_RAWFILE_COMBINED_GRAPH_PASS'){exit 4}
