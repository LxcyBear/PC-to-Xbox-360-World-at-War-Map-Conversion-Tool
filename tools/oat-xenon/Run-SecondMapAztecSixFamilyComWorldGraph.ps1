[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$priorRoot=Join-Path $base 'pc_two_xmodel_two_material_fullgraph_integration'
$work=Join-Path $base 'pc_six_family_comworld_fullgraph_integration'
$priorPath=Join-Path $priorRoot 'aztec_two_pc_xmodel_two_material_fullgraph_integration.json'
$canaryPath=Join-Path $base 'pc_mapents_xenon_canary\aztec_pc_mapents_xenon_canary.json'
$comPath=Join-Path $base 'pc_comworld_burnable_source\aztec_pc_comworld_burnable_source.json'
$fivePath=Join-Path $base 'pc_two_xmodel_two_material_mapents_fullgraph_integration\aztec_pc_five_payload_mapents_graph.json'
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
$required=@($priorPath,$canaryPath,$comPath,$fivePath,$materialPath,$imagePath,$rawPath,
    $identityPath,$proofPath,$bindingPath,$modelBinding,$matBinding,$cli,$oat)
foreach($file in $required){
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)){
        throw "Aztec 6-family input missing: $file"
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
$com=Get-Content -LiteralPath $comPath -Raw | ConvertFrom-Json
$five=Get-Content -LiteralPath $fivePath -Raw | ConvertFrom-Json
$material=Get-Content -LiteralPath $materialPath -Raw | ConvertFrom-Json
$images=Get-Content -LiteralPath $imagePath -Raw | ConvertFrom-Json
$raw=Get-Content -LiteralPath $rawPath -Raw | ConvertFrom-Json
$identity=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$proof=Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json
if([string]$five.schema -ne 'WaW360.AztecPcFivePayloadMapEntsGraph.v1' -or
   [string]$five.status -ne 'AZTEC_PC_FIVE_PAYLOAD_MAPENTS_GRAPH_PASS' -or
   [int]$five.nativeRoots -ne 8509 -or
   [int]$five.sourcePCMapEntsReadbacks -ne 1 -or
   [int]$five.pcIwiPixelExactReadbacks -ne 2 -or
   [int]$five.pcRawFileExactReadbacks -ne 114 -or
   [int]$five.pcXModelsListed -ne 2 -or [int]$five.pcMaterialsListed -ne 2 -or
   -not [bool]$five.layoutReplayExact -or -not [bool]$five.oatZeroWarningsZeroErrors -or
   [bool]$five.readyForXbox -or
   [string]$com.schema -ne 'WaW360.AztecPcComWorldBurnableSource.v1' -or
   [string]$com.status -ne 'AZTEC_PC_COMWORLD_88_BURNABLE_CELL_HEADERS_PASS' -or
   [int]$com.nativeRootIndex -ne 7178 -or
   [int]$com.expectedBurnableCellCount -ne 88 -or
   [int]$com.bestCellArray.bytePaddingAfterLights -ne 0 -or
   [int]$com.bestCellArray.inlinePointers -ne 88 -or
   [int]$com.bestCellArray.nullPointers -ne 0 -or
   [int]$com.bestCellArray.score -ne 264 -or
   @($com.bestCellArray.adjacent32ByteSampleCandidates).Count -lt 1 -or
   [int]$com.bestCellArray.adjacent32ByteSampleCandidates[0].paddingAfterCellArray -ne 0 -or
   [int]$com.bestCellArray.adjacent32ByteSampleCandidates[0].sampleBytes -ne 2816 -or
   [int]$com.bestCellArray.adjacent32ByteSampleCandidates[0].nonzeroSampleBytes -ne 1300 -or
   [bool]$com.exactSourceWorldSerializationProven -or
   [bool]$com.hardwareTestAuthorized -or
   [string]$prior.schema -ne 'WaW360.AztecPcTwoXModelTwoMaterialMixedGraph.v1' -or
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
$comRoot=$identity.Entries[7178]
if([int]$comRoot.Index -ne 7178 -or
   ([string]$comRoot.TypeName).Replace('_','').ToLowerInvariant() -ne 'comworld' -or
   [string]$comRoot.Name -ine 'maps/nazi_zombie_aztec.d3dbsp'){
    throw "Original-PC ComWorld native root #7178 identity changed: type=$($comRoot.TypeName), name=$($comRoot.Name)."
}
$expectedIndex=[int]$canary.nativeRootIndex
$expectedName=([string]$canary.asset).Replace('\','/')
# Native manifest uses the exact wire name 'map_ents' while OAT's
# Linker/Unlinker and our census call the family 'mapents'.
# Normalize ONLY the family identifier; never change a root name/index.
$allMapEnts=@($identity.Entries | Where-Object {
    ([string]$_.TypeName).Replace('_','').ToLowerInvariant() -eq 'mapents'
})
Write-Host "[AZTEC-PC-COMWORLD] MapEnts gate: canary root=$expectedIndex identity=$expectedName, native mapents roots=$($allMapEnts.Count)"
foreach($item in $allMapEnts){
    Write-Host "[AZTEC-PC-COMWORLD] native MapEnts root=$([int]$item.Index) identity=$([string]$item.Name)"
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
Write-Host '[AZTEC-PC-COMWORLD] MapEnts exact native index/name PASS'
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
$emitLog=Join-Path $logDir 'comworld_emit.log'
$listLog=Join-Path $logDir 'oat_list.log'
$mapLog=Join-Path $logDir 'mapents_readback.log'
$rawLog=Join-Path $logDir 'rawfile_readback.log'
$imageLog=Join-Path $logDir 'image_readback.log'
$reportPath=Join-Path $work 'aztec_pc_six_family_comworld_graph.json'
$reportText=Join-Path $work 'aztec_pc_six_family_comworld_graph.txt'
$status='AZTEC_PC_SIX_FAMILY_COMWORLD_GRAPH_BLOCKED'
$blocker=''
$mapExact=0;$modelListed=0;$materialListed=0;$rawExact=0;$imgExact=0;$comListed=0;$comExact=0
$emit=$null;$outputFF='';$emitFile=''
Write-Host '[AZTEC-PC-COMWORLD] ==================================================='
Write-Host '[AZTEC-PC-COMWORLD] COMWORLD + MAPENTS + 2 XMODELS + 2 MATERIALS + 2 IMAGES + 114 RAWFILES'
Write-Host '[AZTEC-PC-COMWORLD] SIX ORIGINAL-PC ASSET FAMILIES, RENDERER ORACLE STRUCTURE'
Write-Host '[AZTEC-PC-COMWORLD] 8,509 native roots. SCRATCH ONLY / NEVER DEPLOY ON XBOX'
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
    Write-Host '[AZTEC-PC-COMWORLD] .NET incremental build PASS'
    $args=@('run','--no-build','-c','Release','--project',$cli,'--',
        'xenon-graph-emit',[string]$proof.dumpRoot,$native,$identityPath,$bindingPath,
        $emitRoot,'nazi_zombie_aztec',$oat,'70','436',
        '--aztec-pc-six-family-comworld-scratch-graft',
        $imgGraph,'2',$rawPath,$modelGraph,$modelBinding,$matRoot,$matBinding,$canaryPath,$comPath)
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & dotnet @args 2>&1 | ForEach-Object {[string]$_} |
            Set-Content -LiteralPath $emitLog -Encoding UTF8
        $code=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    $emitFile=Join-Path $emitRoot 'nuclear_xenon_renderer_emit.json'
    if(-not (Test-Path -LiteralPath $emitFile -PathType Leaf)){
        throw "Six-family emitter produced no report (exit=$code): $emitLog"
    }
    $emit=Get-Content -LiteralPath $emitFile -Raw | ConvertFrom-Json
    if($code -ne 0 -or
       -not [bool]$emit.ValidationPass -or -not [bool]$emit.LayoutReplayExact -or
       -not [bool]$emit.ExactIdentitySequence -or -not [bool]$emit.OatZeroWarningsZeroErrors -or
       -not [bool]$emit.ValidationOnly -or [bool]$emit.ReadyForHardware -or
       [int]$emit.NativeRoots -ne 8509 -or
       [int]$emit.RealMapEntsRoots -ne 1 -or [int]$emit.RealComWorldRoots -ne 1 -or
       [int]$emit.RealXModelRoots -ne 2 -or [int]$emit.RealMaterialRoots -ne 2 -or
       [int]$emit.RealImageRoots -ne 2 -or [int]$emit.RealRawFileRoots -ne 114){
        throw "Combined 8,509-root 6-family layout/OAT gate failed (exit=$code): $emitLog"
    }
    $outputFF=[string]$emit.OutputFastFile
    if(-not (Test-Path -LiteralPath $outputFF -PathType Leaf) -or
       -not ([IO.Path]::GetFullPath($outputFF)).StartsWith(
           ([IO.Path]::GetFullPath($emitRoot)).TrimEnd([char]'\')+'\',
           [StringComparison]::OrdinalIgnoreCase)){
        throw 'Six-family scratch FF missing or outside controlled output root.'
    }
    Write-Host '[AZTEC-PC-COMWORLD] 8,509-root Xenon OAT/layout PASS (MapEnts=1)'
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('wawAz6R_'+[guid]::NewGuid().ToString('N').Substring(0,8))
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
        $comPattern='(?i)(?:^|[\]\s])comworld\s*,\s*maps/nazi_zombie_aztec\.d3dbsp\s*$'
        $comListed=@($list | Where-Object {[string]$_ -match $comPattern}).Count
        if($comListed -ne 1){
            throw "OAT combined ComWorld #7178 source name missing or ambiguous, count=$comListed."
        }
        foreach($model in @('zombie_x2_icon','tag_origin')){
            $pattern='(?i)(?:^|[\]\s])xmodel\s*,\s*'+[regex]::Escape($model)+'\s*$'
            if(@($list | Where-Object {[string]$_ -match $pattern}).Count -ne 1){
                throw "XModel $model missing from six-family OAT list."
            }
            $modelListed++
        }
        foreach($materialName in @('mc/lambert1','mc/mtl_x2icon_gold')){
            $pattern='(?i)(?:^|[\]\s])material\s*,\s*'+[regex]::Escape($materialName)+'\s*$'
            if(@($list | Where-Object {[string]$_ -match $pattern}).Count -ne 1){
                throw "Material $materialName missing from six-family OAT list."
            }
            $materialListed++
        }
        $pattern='(?i)(?:^|[\]\s])mapents\s*,\s*'+[regex]::Escape([string]$canary.asset)+'\s*$'
        if(@($list | Where-Object {[string]$_ -match $pattern}).Count -ne 1){
            throw 'Source-backed MapEnts missing/ambiguous from six-family OAT listing.'
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
        # Original PC ComWorld is NOT supported by the OAT T4 body
        # dumper, so verify the actual emitted uncompressed Xbox zone
        # byte sections independently, while OAT validates the FF load.
        $fullZone=[string]$emit.OutputZoneFile
        $allowedZoneRoot=([IO.Path]::GetFullPath($emitRoot)).TrimEnd([char]'\')+'\'
        if(-not (Test-Path -LiteralPath $fullZone -PathType Leaf) -or
           -not ([IO.Path]::GetFullPath($fullZone)).StartsWith($allowedZoneRoot,[StringComparison]::OrdinalIgnoreCase)){
            throw 'New ComWorld raw-zone diagnostic is missing or outside scratch emitter root.'
        }
        $uncompressed=[IO.File]::ReadAllBytes($fullZone)
        $comAt=[int64]$emit.ComWorldZoneOffset
        $nameBytes=[Text.Encoding]::UTF8.GetBytes('maps/nazi_zombie_aztec.d3dbsp'+[char]0)
        $comBlockLength=64+$nameBytes.Length+144+1056+2816
        if($comAt -lt 0 -or $comAt -gt $uncompressed.LongLength-$comBlockLength){
            throw "Emitted ComWorld zone offset 0x$('{0:X}' -f $comAt) invalid."
        }
        $parentAt=[int]$comAt
        $nameAt=$parentAt+64
        $lightsAt=$nameAt+$nameBytes.Length
        $cellsAt=$lightsAt+144
        $samplesAt=$cellsAt+1056
        for($i=0;$i -lt $nameBytes.Length;$i++){
            if($uncompressed[$nameAt+$i] -ne $nameBytes[$i]){
                throw "Emitted ComWorld name differs at byte $i."
            }
        }
        function ShaRange([byte[]]$data,[int]$start,[int]$count){
            $hash=[Security.Cryptography.SHA256]::Create()
            try{return [BitConverter]::ToString($hash.ComputeHash($data,$start,$count)).Replace('-','')}
            finally{$hash.Dispose()}
        }
        $shaParent=ShaRange $uncompressed $parentAt 64
        $shaLights=ShaRange $uncompressed $lightsAt 144
        $shaCells=ShaRange $uncompressed $cellsAt 1056
        $shaSamples=ShaRange $uncompressed $samplesAt 2816
        if($shaParent -ine [string]$emit.ComWorldParentXenonSha256 -or
           $shaLights -ine [string]$emit.ComWorldLightsXenonSha256 -or
           $shaCells -ine [string]$emit.ComWorldCellsXenonSha256 -or
           $shaSamples -ine [string]$emit.ComWorldSamplesSourceSha256 -or
           $shaSamples -ine [string]$com.bestCellArray.adjacent32ByteSampleCandidates[0].sourceSampleSha256){
            throw 'ComWorld Xenon parent/light/cell or original-PC 2816 sample SHAs changed in emitted FF zone.'
        }
        # Check critical 32-bit values at their actual Xenon positions,
        # not just a self-declared hash from the emitter.
        function ReadBE([byte[]]$data,[int]$at){
            # PowerShell 5.1 signed-bitwise casts can turn FFFFFFFF
            # into -1 and throw when converted back to UInt32.
            $val=[uint64]$data[$at]*16777216L +
                 [uint64]$data[$at+1]*65536L +
                 [uint64]$data[$at+2]*256L +
                 [uint64]$data[$at+3]
            return [uint32]$val
        }
        if((ReadBE $uncompressed $parentAt) -ne [uint32]::MaxValue -or
           (ReadBE $uncompressed ($parentAt+4)) -ne 1 -or
           (ReadBE $uncompressed ($parentAt+8)) -ne 2 -or
           (ReadBE $uncompressed ($parentAt+12)) -ne [uint32]::MaxValue -or
           (ReadBE $uncompressed ($parentAt+56)) -ne 88 -or
           (ReadBE $uncompressed ($parentAt+60)) -ne [uint32]::MaxValue){
            throw 'ComWorld 64-byte big-endian header has invalid source-owned T4 count/pointer values.'
        }
        for($i=0;$i -lt 88;$i++){
            $ptr=ReadBE $uncompressed ($cellsAt+$i*12+8)
            if($ptr -ne [uint32]::MaxValue){
                throw "ComWorld Xenon burnable cell #$i lost its inline source pointer."
            }
        }
        $nonzero=0
        for($i=0;$i -lt 2816;$i++){
            if($uncompressed[$samplesAt+$i] -ne 0){$nonzero++}
        }
        if($nonzero -ne 1300){throw "ComWorld original-PC sample nonzero bytes=$nonzero expected 1300."}
        $comExact=1
        Write-Host '[AZTEC-PC-COMWORLD] PC ComWorld source endian/header/2 lights/88 cells/2816 sample bytes EXACT PASS'
        $mapRead=Join-Path $mapTemp 'maps\nazi_zombie_aztec.d3dbsp.ents'
        if(-not (Test-Path -LiteralPath $mapRead -PathType Leaf) -or
           [long](Get-Item -LiteralPath $mapRead).Length -ne 571011L -or
           (Get-FileHash -LiteralPath $mapRead -Algorithm SHA256).Hash -ine [string]$canary.originalPcMapEntsSha256){
            throw 'Combined MapEnts 571011-byte PC-source SHA-256 readback changed!'
        }
        $mapExact=1
        Write-Host '[AZTEC-PC-COMWORLD] MapEnts: 571011 bytes PC SHA-256 EXACT PASS'
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
            if(-not $sources.ContainsKey($name)){throw "Unexpected PC image identity in 6-family FF: $name"}
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
        if($mapExact -ne 1 -or $comExact -ne 1 -or $comListed -ne 1 -or $rawExact -ne 114 -or $imgExact -ne 2 -or
           $modelListed -ne 2 -or $materialListed -ne 2){
            throw 'Combined six-family exact readback counts not passing.'
        }
    }finally{
        if(Test-Path -LiteralPath $temp -PathType Container){Remove-Item -LiteralPath $temp -Recurse -Force}
    }
    $status='AZTEC_PC_SIX_FAMILY_COMWORLD_GRAPH_PASS'
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-PC-COMWORLD] BLOCKER: $blocker"
    foreach($l in @($buildLog,$emitLog,$listLog,$mapLog,$rawLog,$imageLog)){
        if(Test-Path -LiteralPath $l -PathType Leaf){
            Write-Host "[AZTEC-PC-COMWORLD] Last $l"
            foreach($line in @(Get-Content -LiteralPath $l -Tail 7)){
                Write-Host "[AZTEC-PC-COMWORLD]   $line"
            }
        }
    }
}
$report=[ordered]@{
    schema='WaW360.AztecPcSixFamilyComWorldGraph.v1'
    status=$status
    nativeRoots=8509
    originalPcMapEntsBytes=$([long]$canary.originalPcMapEntsBytes)
    originalPcMapEntsSha256=[string]$canary.originalPcMapEntsSha256
    sourcePCMapEntsReadbacks=$mapExact
    sourcePcComWorldOatListed=$comListed
    sourcePcComWorldByteReadback=$comExact
    originalPcComWorldSampleSha256=[string]$com.bestCellArray.adjacent32ByteSampleCandidates[0].sourceSampleSha256
    originalPCComWorldSourceProof=$comPath
    earlierFiveFamilyProof=$fivePath
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
    missingGfxWorldAndGameWorldSp=$true
    note='Original PC ComWorld parent and 2 lights and 88 burnable cells plus 2816 sample bytes grafted into 8509-root mixed Xenon FF; MapEnts, models, materials, images and RawFiles still independently checked. OAT T4 ComWorld has no body dumper; byte section readback uses output raw zone plus source SHA. Renderer shaders still oracle and many roots are shells, NOT FOR XBOX.'
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $reportPath -Encoding UTF8
[IO.File]::WriteAllLines($reportText,[string[]]@(
    "Aztec original PC six-family fullgraph: $status"
    "ComWorld readback=$comExact/1, OAT=$comListed/1, MapEnts readback=$mapExact/1, bytes=571011, original PC SHA=$($canary.originalPcMapEntsSha256)"
    "Models listed=$modelListed/2 Materials listed=$materialListed/2 PC pixel SHAs=$imgExact/2 RawFile SHAs=$rawExact/114"
    "blocker=$blocker"
    "SCRATCH ONLY. Missing world/StringTable/custom asset bodies. NEVER DEPLOY ON XBOX."
),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-COMWORLD] ================= RESULT ========================='
Write-Host "[AZTEC-PC-COMWORLD] status=$status"
Write-Host "[AZTEC-PC-COMWORLD] ComWorld=$comExact/1, MapEnts=$mapExact/1, XModels=$modelListed/2, Materials=$materialListed/2, images=$imgExact/2, RawFiles=$rawExact/114"
Write-Host "[AZTEC-PC-COMWORLD] report=$reportText"
Write-Host '[AZTEC-PC-COMWORLD] SCRATCH ONLY / NOT FOR XBOX'
if($status -ne 'AZTEC_PC_SIX_FAMILY_COMWORLD_GRAPH_PASS'){exit 4}
