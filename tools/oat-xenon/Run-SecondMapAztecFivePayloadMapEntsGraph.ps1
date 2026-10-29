[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$priorRoot=Join-Path $base 'pc_two_xmodel_two_material_fullgraph_integration'
$work=Join-Path $base 'pc_two_xmodel_two_material_mapents_fullgraph_integration'
$priorPath=Join-Path $priorRoot 'aztec_two_pc_xmodel_two_material_fullgraph_integration.json'
$canaryPath=Join-Path $base 'pc_mapents_xenon_canary\aztec_pc_mapents_xenon_canary.json'
$materialPath=Join-Path $base 'pc_two_material_xenon_canary\aztec_pc_two_material_xenon_canary.json'
$imagePath=Join-Path $base 'pc_iwd_image_roundtrip\aztec_pc_iwd_xenon_image_roundtrip.json'
$rawPath=Join-Path $base 'pc_rawfile_graft\pc_rawfile_graft_input.json'
$identityPath=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$proofPath=Join-Path $base 'native_graph_proof\native_graph_proof_result.json'
$bindingPath=Join-Path $base 'renderer_preflight\native_root_binding\nuclear_xenon_root_binding.json'
$imgGraph=Join-Path $base 'pc_iwd_rawfile_integration\pc_xenon_image_graph'
$modelGraph=Join-Path $priorRoot 'pc_two_xmodel_graph'
$modelBinding=Join-Path $priorRoot 'aztec_two_pc_xmodel_root_binding.json'
$matBinding=Join-Path $priorRoot 'aztec_two_pc_material_root_binding.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$required=@($priorPath,$canaryPath,$materialPath,$imagePath,$rawPath,
    $identityPath,$proofPath,$bindingPath,$modelBinding,$matBinding,$cli,$oat)
foreach($file in $required){
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)){
        throw "Aztec 5-payload input missing: $file"
    }
}
foreach($folder in @((Join-Path $imgGraph 'xenon_image_graphs'),
                      (Join-Path $modelGraph 'dumps'))){
    if(-not (Test-Path -LiteralPath $folder -PathType Container)){
        throw "Previously passing PC source graph missing: $folder"
    }
}
$prior=Get-Content -LiteralPath $priorPath -Raw | ConvertFrom-Json
$canary=Get-Content -LiteralPath $canaryPath -Raw | ConvertFrom-Json
$material=Get-Content -LiteralPath $materialPath -Raw | ConvertFrom-Json
$images=Get-Content -LiteralPath $imagePath -Raw | ConvertFrom-Json
$raw=Get-Content -LiteralPath $rawPath -Raw | ConvertFrom-Json
$identity=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$proof=Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json
if([string]$prior.schema -ne 'WaW360.AztecPcTwoXModelTwoMaterialMixedGraph.v1' -or
   [string]$prior.status -ne 'AZTEC_PC_TWO_XMODEL_TWO_MATERIAL_FULLGRAPH_PASS' -or
   [int]$prior.nativeRoots -ne 8509 -or
   [int]$prior.realXModelRoots -ne 2 -or [int]$prior.realImageRoots -ne 2 -or
   [int]$prior.realRawFileRoots -ne 114 -or
   [int]$prior.independentMaterialNamesListed -ne 2 -or
   [int]$prior.independentModelNamesListed -ne 2 -or
   [int]$prior.independentPcPixelShaExactReadbacks -ne 2 -or
   [int]$prior.independentPcRawFileShaExactReadbacks -ne 114 -or
   -not [bool]$prior.layoutReplayExact -or -not [bool]$prior.oatZeroWarningsZeroErrors -or
   [bool]$prior.readyForXbox -or
   [string]$canary.schema -ne 'WaW360.AztecPcMapEntsXenonCanary.v1' -or
   [string]$canary.status -ne 'AZTEC_PC_MAPENTS_XENON_CANARY_PASS' -or
   [long]$canary.originalPcMapEntsBytes -ne 571011L -or
   [int]$canary.xboxFormatOatRootListed -ne 1 -or
   [string]$canary.xenonMapEntsSha256 -ine [string]$canary.originalPcMapEntsSha256 -or
   [bool]$canary.readyForXbox -or
   [string]$material.status -ne 'AZTEC_PC_TWO_MATERIAL_XENON_CANARY_PASS' -or
   [int]$material.xenonMaterialGraphCount -ne 2 -or
   [string]$images.status -ne 'AZTEC_PC_IWD_XENON_IMAGE_ROUNDTRIP_PASS' -or
   @($images.individualImages).Count -ne 2 -or
   [string]$raw.format -ne 'WaW360.AztecPcRawFileGraftInput.v1' -or
   @($raw.entries).Count -ne 114 -or
   -not [bool]$identity.TypeSequenceVerified -or
   @($identity.Entries).Count -ne 8509 -or
   [string]$proof.status -ne 'IDENTITY_AND_TYPED_GRAPH_IMPORT_PASS'){
    throw 'Source-backed 4-payload fullgraph, MapEnts canary, and remaining source proofs must all pass.'
}
# Resolve by explicit native index and separately compare the exact
# identity. Do not rely on a compound PowerShell pipeline equality/cast;
# the standalone canary already pins this index through the PC census.
$expectedIndex=[int]$canary.nativeRootIndex
$expectedName=([string]$canary.asset).Replace('\','/')
# Native manifest uses the exact wire name 'map_ents' while OAT's
# Linker/Unlinker and our census call the family 'mapents'.
# Normalize ONLY the family identifier; never change a root name/index.
$allMapEnts=@($identity.Entries | Where-Object {
    ([string]$_.TypeName).Replace('_','').ToLowerInvariant() -eq 'mapents'
})
Write-Host "[AZTEC-PC-5PAYLOAD] MapEnts gate: canary root=$expectedIndex identity=$expectedName, native mapents roots=$($allMapEnts.Count)"
foreach($item in $allMapEnts){
    Write-Host "[AZTEC-PC-5PAYLOAD] native MapEnts root=$([int]$item.Index) identity=$([string]$item.Name)"
}
if($expectedIndex -lt 0 -or $expectedIndex -ge @($identity.Entries).Count){
    throw "MapEnts canary native root index out of range: $expectedIndex."
}
$nativeAtIndex=$identity.Entries[$expectedIndex]
$identityMatches=[string]::Equals(
    ([string]$nativeAtIndex.Name).Replace('\','/'),
    $expectedName,[StringComparison]::OrdinalIgnoreCase)
$typeMatches=[string]::Equals(
    ([string]$nativeAtIndex.TypeName).Replace('_',''),
    'mapents',[StringComparison]::OrdinalIgnoreCase)
$rootIndexMatches=([int]$nativeAtIndex.Index -eq $expectedIndex)
if($allMapEnts.Count -ne 1 -or -not $typeMatches -or
   -not $identityMatches -or -not $rootIndexMatches){
    throw "MapEnts identity mismatch: canary index=$expectedIndex name='$expectedName'; native[$expectedIndex] index=$([int]$nativeAtIndex.Index) type='$([string]$nativeAtIndex.TypeName)' name='$([string]$nativeAtIndex.Name)' (mapents count=$($allMapEnts.Count))."
}
Write-Host '[AZTEC-PC-5PAYLOAD] MapEnts exact native index/name PASS'
$native=[string]$proof.verifiedGoldenXboxFastFile
if(-not (Test-Path -LiteralPath $native -PathType Leaf) -or
   (Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash -ine [string]$proof.nativeSha256){
    throw 'Golden native Xbox FF structural identity/provenance changed.'
}
$matRoot=[string]$material.xenonGraphRoot
if(-not (Test-Path -LiteralPath (Join-Path $matRoot 'xenon_material_graphs') -PathType Container)){
    throw 'Original PC material graphs no longer available.'
}
$entitySource=Join-Path $base 'tonight_pc_critical_body_export_probe\source_exports\mapents\map_ff\maps\nazi_zombie_aztec.d3dbsp.ents'
if(-not (Test-Path -LiteralPath $entitySource -PathType Leaf) -or
   [long](Get-Item -LiteralPath $entitySource).Length -ne 571011L -or
   (Get-FileHash -LiteralPath $entitySource -Algorithm SHA256).Hash -ine
    [string]$canary.originalPcMapEntsSha256){
    throw 'Original-PC 571011-byte MapEnts file changed after standalone Xenon PASS.'
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
$logDir=Join-Path $work 'logs'
$emitRoot=Join-Path $work 'scratch_emit'
New-Item -Path $logDir -ItemType Directory -Force | Out-Null
$buildLog=Join-Path $logDir 'dotnet_build.log'
$emitLog=Join-Path $logDir 'five_payload_emit.log'
$listLog=Join-Path $logDir 'oat_list.log'
$mapLog=Join-Path $logDir 'mapents_readback.log'
$rawLog=Join-Path $logDir 'rawfile_readback.log'
$imageLog=Join-Path $logDir 'image_readback.log'
$reportPath=Join-Path $work 'aztec_pc_five_payload_mapents_graph.json'
$reportText=Join-Path $work 'aztec_pc_five_payload_mapents_graph.txt'
$status='AZTEC_PC_FIVE_PAYLOAD_MAPENTS_GRAPH_BLOCKED'
$blocker=''
$mapExact=0;$modelListed=0;$materialListed=0;$rawExact=0;$imgExact=0
$emit=$null;$outputFF='';$emitFile=''
Write-Host '[AZTEC-PC-5PAYLOAD] ==================================================='
Write-Host '[AZTEC-PC-5PAYLOAD] MAPENTS + 2 XMODELS + 2 MATERIALS + 2 IMAGES + 114 RAWFILES'
Write-Host '[AZTEC-PC-5PAYLOAD] ALL ORIGINAL-PC BODIES (ORIGINAL RENDERER ORACLE STRUCTURE)'
Write-Host '[AZTEC-PC-5PAYLOAD] 8,509 native roots. SCRATCH ONLY / NEVER DEPLOY ON XBOX'
try{
    if(Test-Path -LiteralPath $emitRoot -PathType Container){
        Remove-Item -LiteralPath $emitRoot -Force -Recurse
    }
    New-Item -Path $emitRoot -ItemType Directory -Force | Out-Null
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
        $code=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    if($code -ne 0){throw "Incremental .NET build failed: $buildLog"}
    Write-Host '[AZTEC-PC-5PAYLOAD] .NET incremental build PASS'
    $args=@('run','--no-build','-c','Release','--project',$cli,'--',
        'xenon-graph-emit',[string]$proof.dumpRoot,$native,$identityPath,$bindingPath,
        $emitRoot,'nazi_zombie_aztec',$oat,'70','436',
        '--aztec-pc-two-model-two-material-mapents-image-rawfile-graft',
        $imgGraph,'2',$rawPath,$modelGraph,$modelBinding,$matRoot,$matBinding,$canaryPath)
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & dotnet @args 2>&1 | ForEach-Object {[string]$_} |
            Set-Content -LiteralPath $emitLog -Encoding UTF8
        $code=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    $emitFile=Join-Path $emitRoot 'nuclear_xenon_renderer_emit.json'
    if(-not (Test-Path -LiteralPath $emitFile -PathType Leaf)){
        throw "Five-payload emitter produced no report (exit=$code): $emitLog"
    }
    $emit=Get-Content -LiteralPath $emitFile -Raw | ConvertFrom-Json
    if($code -ne 0 -or
       -not [bool]$emit.ValidationPass -or -not [bool]$emit.LayoutReplayExact -or
       -not [bool]$emit.ExactIdentitySequence -or -not [bool]$emit.OatZeroWarningsZeroErrors -or
       -not [bool]$emit.ValidationOnly -or [bool]$emit.ReadyForHardware -or
       [int]$emit.NativeRoots -ne 8509 -or
       [int]$emit.RealMapEntsRoots -ne 1 -or
       [int]$emit.RealXModelRoots -ne 2 -or [int]$emit.RealMaterialRoots -ne 2 -or
       [int]$emit.RealImageRoots -ne 2 -or [int]$emit.RealRawFileRoots -ne 114){
        throw "Combined 8,509-root 5-payload layout/OAT gate failed (exit=$code): $emitLog"
    }
    $outputFF=[string]$emit.OutputFastFile
    if(-not (Test-Path -LiteralPath $outputFF -PathType Leaf) -or
       -not ([IO.Path]::GetFullPath($outputFF)).StartsWith(
           ([IO.Path]::GetFullPath($emitRoot)).TrimEnd([char]'\')+'\',
           [StringComparison]::OrdinalIgnoreCase)){
        throw 'Five-payload scratch FF missing or outside controlled output root.'
    }
    Write-Host '[AZTEC-PC-5PAYLOAD] 8,509-root Xenon OAT/layout PASS (MapEnts=1)'
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('wawAz5R_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    if($temp.Length -gt 115){throw 'OAT readback requires short physical temp path.'}
    $mapTemp=Join-Path $temp 'map'
    $rawTemp=Join-Path $temp 'raw'
    $imgTemp=Join-Path $temp 'img'
    New-Item -Path $mapTemp,$rawTemp,$imgTemp -ItemType Directory -Force | Out-Null
    try{
        $saved=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            & $oat '--no-color' '--list' $outputFF 2>&1 |
                ForEach-Object {[string]$_} | Set-Content -LiteralPath $listLog -Encoding UTF8
            $code=[int]$LASTEXITCODE
        }finally{$ErrorActionPreference=$saved}
        if($code -ne 0 -or
           @(Select-String -LiteralPath $listLog -Pattern '(?i)^\s*(ERROR:|WARNING:)').Count -ne 0){
            throw "OAT list failed: $listLog"
        }
        $list=@(Get-Content -LiteralPath $listLog)
        foreach($model in @('zombie_x2_icon','tag_origin')){
            $pattern='(?i)(?:^|[\]\s])xmodel\s*,\s*'+[regex]::Escape($model)+'\s*$'
            if(@($list | Where-Object {[string]$_ -match $pattern}).Count -ne 1){
                throw "XModel $model missing from five-payload OAT list."
            }
            $modelListed++
        }
        foreach($materialName in @('mc/lambert1','mc/mtl_x2icon_gold')){
            $pattern='(?i)(?:^|[\]\s])material\s*,\s*'+[regex]::Escape($materialName)+'\s*$'
            if(@($list | Where-Object {[string]$_ -match $pattern}).Count -ne 1){
                throw "Material $materialName missing from five-payload OAT list."
            }
            $materialListed++
        }
        $pattern='(?i)(?:^|[\]\s])mapents\s*,\s*'+[regex]::Escape([string]$canary.asset)+'\s*$'
        if(@($list | Where-Object {[string]$_ -match $pattern}).Count -ne 1){
            throw 'Source-backed MapEnts missing/ambiguous from five-payload OAT listing.'
        }
        $saved=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            & $oat '--no-color' '--include-assets' 'mapents' '--output-folder' $mapTemp $outputFF 2>&1 |
                ForEach-Object {[string]$_} | Set-Content -LiteralPath $mapLog -Encoding UTF8
            $code=[int]$LASTEXITCODE
        }finally{$ErrorActionPreference=$saved}
        if($code -ne 0 -or
           @(Select-String -LiteralPath $mapLog -Pattern '(?i)^\s*(ERROR:|WARNING:)').Count -ne 0 -or
           @(Select-String -LiteralPath $mapLog -Pattern '(?i)^\s*Finished with\s+0\s+warnings?,\s+0\s+errors?\s*$').Count -eq 0){
            throw "Combined MapEnts Xenon dump failed: $mapLog"
        }
        $mapRead=Join-Path $mapTemp 'maps\nazi_zombie_aztec.d3dbsp.ents'
        if(-not (Test-Path -LiteralPath $mapRead -PathType Leaf) -or
           [long](Get-Item -LiteralPath $mapRead).Length -ne 571011L -or
           (Get-FileHash -LiteralPath $mapRead -Algorithm SHA256).Hash -ine [string]$canary.originalPcMapEntsSha256){
            throw 'Combined MapEnts 571011-byte PC-source SHA-256 readback changed!'
        }
        $mapExact=1
        Write-Host '[AZTEC-PC-5PAYLOAD] MapEnts: 571011 bytes PC SHA-256 EXACT PASS'
        $saved=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            & $oat '--no-color' '--include-assets' 'rawfile' '--output-folder' $rawTemp $outputFF 2>&1 |
                ForEach-Object {[string]$_} | Set-Content -LiteralPath $rawLog -Encoding UTF8
            $code=[int]$LASTEXITCODE
        }finally{$ErrorActionPreference=$saved}
        if($code -ne 0 -or
           @(Select-String -LiteralPath $rawLog -Pattern '(?i)^\s*(ERROR:|WARNING:)').Count -ne 0){
            throw "Combined RawFile dump failed: $rawLog"
        }
        foreach($e in @($raw.entries)){
            $file=Join-Path $rawTemp (([string]$e.name).Replace('/','\'))
            if(-not (Test-Path -LiteralPath $file -PathType Leaf) -or
               [long](Get-Item -LiteralPath $file).Length -ne [long]$e.bytes -or
               (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine [string]$e.sha256){
                throw "PC RawFile SHA/size changed with MapEnts graft: $($e.name)"
            }
            $rawExact++
        }
        $saved=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            & $oat '--no-color' '--include-assets' 'image' '--image-format' 'IWI' '--output-folder' $imgTemp $outputFF 2>&1 |
                ForEach-Object {[string]$_} | Set-Content -LiteralPath $imageLog -Encoding UTF8
            $code=[int]$LASTEXITCODE
        }finally{$ErrorActionPreference=$saved}
        if($code -ne 0 -or
           @(Select-String -LiteralPath $imageLog -Pattern '(?i)^\s*(ERROR:|WARNING:)').Count -ne 0){
            throw "Combined original PC image dump failed: $imageLog"
        }
        $sources=@{}
        foreach($i in @($images.individualImages)){
            $sources[([string]$i.Name).ToLowerInvariant()]=$i
        }
        $imageFiles=@(Get-ChildItem -LiteralPath $imgTemp -Filter '*.iwi' -File -Recurse)
        if($imageFiles.Count -ne 2){throw "Expected exact 2 real PC image IWI readbacks, got $($imageFiles.Count)."}
        foreach($file in $imageFiles){
            $relative=$file.FullName.Substring($imgTemp.Length).TrimStart([char]'\').Replace('\','/')
            if($relative.StartsWith('images/',[StringComparison]::OrdinalIgnoreCase)){
                $relative=$relative.Substring(7)
            }
            $name=$relative.Substring(0,$relative.Length-4).ToLowerInvariant()
            if(-not $sources.ContainsKey($name)){throw "Unexpected PC image identity in 5-payload FF: $name"}
            $data=[IO.File]::ReadAllBytes($file.FullName)
            if($data.Length -lt 29 -or $data[0] -ne 73 -or $data[1] -ne 87 -or
               $data[2] -ne 105 -or $data[3] -ne 6 -or $data[4] -ne 13){
                throw "Original PC IWI v6 BC3 image header changed: $name"
            }
            $hasher=[Security.Cryptography.SHA256]::Create()
            try{$sha=([BitConverter]::ToString($hasher.ComputeHash($data,28,$data.Length-28))).Replace('-','')}
            finally{$hasher.Dispose()}
            if($sha -ine [string]$sources[$name].InputPayloadSha256){
                throw "Original PC image pixel SHA changed in MapEnts combined graph: $name"
            }
            $imgExact++
        }
        if($mapExact -ne 1 -or $rawExact -ne 114 -or $imgExact -ne 2 -or
           $modelListed -ne 2 -or $materialListed -ne 2){
            throw 'Combined five-payload exact readback counts not passing.'
        }
    }finally{
        if(Test-Path -LiteralPath $temp -PathType Container){Remove-Item -LiteralPath $temp -Recurse -Force}
    }
    $status='AZTEC_PC_FIVE_PAYLOAD_MAPENTS_GRAPH_PASS'
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-PC-5PAYLOAD] BLOCKER: $blocker"
    foreach($l in @($buildLog,$emitLog,$listLog,$mapLog,$rawLog,$imageLog)){
        if(Test-Path -LiteralPath $l -PathType Leaf){
            Write-Host "[AZTEC-PC-5PAYLOAD] Last $l"
            foreach($line in @(Get-Content -LiteralPath $l -Tail 7)){
                Write-Host "[AZTEC-PC-5PAYLOAD]   $line"
            }
        }
    }
}
$report=[ordered]@{
    schema='WaW360.AztecPcFivePayloadMapEntsGraph.v1'
    status=$status
    nativeRoots=8509
    originalPcMapEntsBytes=$([long]$canary.originalPcMapEntsBytes)
    originalPcMapEntsSha256=[string]$canary.originalPcMapEntsSha256
    sourcePCMapEntsReadbacks=$mapExact
    pcXModelsListed=$modelListed
    pcMaterialsListed=$materialListed
    pcIwiPixelExactReadbacks=$imgExact
    pcRawFileExactReadbacks=$rawExact
    mapEntsCanary=$canaryPath
    earlierFourPayloadProof=$priorPath
    emitterReport=$emitFile
    diagnosticScratchFF=$outputFF
    blocker=$blocker
    oatZeroWarningsZeroErrors=$(if($null -eq $emit){$false}else{[bool]$emit.OatZeroWarningsZeroErrors})
    layoutReplayExact=$(if($null -eq $emit){$false}else{[bool]$emit.LayoutReplayExact})
    readyForXbox=$false
    fullMapPlayable=$false
    missingWorldAndStringTablePayloads=$true
    note='Source-backed MapEnts graft into 8509-root source-mixed Xenon FF. OAT + four earlier exact source readback gates preserved. Renderer shaders still oracle, many other roots shells, NOT FOR XBOX.'
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportPath -Encoding UTF8
[IO.File]::WriteAllLines($reportText,[string[]]@(
    "Aztec original PC five-payload fullgraph: $status"
    "MapEnts readback=$mapExact/1, bytes=571011, original PC SHA=$($canary.originalPcMapEntsSha256)"
    "Models listed=$modelListed/2 Materials listed=$materialListed/2 PC pixel SHAs=$imgExact/2 RawFile SHAs=$rawExact/114"
    "blocker=$blocker"
    "SCRATCH ONLY. Missing world/StringTable/custom asset bodies. NEVER DEPLOY ON XBOX."
),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-5PAYLOAD] ================= RESULT ========================='
Write-Host "[AZTEC-PC-5PAYLOAD] status=$status"
Write-Host "[AZTEC-PC-5PAYLOAD] MapEnts=$mapExact/1, XModels=$modelListed/2, Materials=$materialListed/2, images=$imgExact/2, RawFiles=$rawExact/114"
Write-Host "[AZTEC-PC-5PAYLOAD] report=$reportText"
Write-Host '[AZTEC-PC-5PAYLOAD] SCRATCH ONLY / NOT FOR XBOX'
if($status -ne 'AZTEC_PC_FIVE_PAYLOAD_MAPENTS_GRAPH_PASS'){exit 4}
