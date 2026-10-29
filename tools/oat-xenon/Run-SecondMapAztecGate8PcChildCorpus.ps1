[CmdletBinding()]
param([switch]$BuildDumper)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Typed original-PC GfxWorld source evidence. NO Xenon FF emission.
$repo=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$seedPath=Join-Path $base 'pc_source_to_xenon_gfxworld_root_dpvs_scalar_seed\aztec_pc_gfxworld_800_to_xenon_900_root_dpvs_seed.json'
$gatePath=Join-Path $base 'pc_735_39_gfxworld_integrated_ff_oat_run_probe_dpvs_static\aztec_pc_world_core_integrated_oat_run.json'
$abiPath=Join-Path $base 'xenon_native_gfxworld_root_dpvs_abi\native_xenon_gfxworld_root_dpvs_abi.json'
$dumper=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$capPath=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$cap='t4-pc-gfxworld-gate8-child-source-v1'
$out=Join-Path $base 'pc_gfxworld_gate8_child_source_corpus'
$report=Join-Path $out 'aztec_pc_gate8_child_source_corpus.json'
foreach($p in @($seedPath,$gatePath,$abiPath)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing source gate prerequisite: $p"}
}
$seed=Get-Content -LiteralPath $seedPath -Raw | ConvertFrom-Json
$gate=Get-Content -LiteralPath $gatePath -Raw | ConvertFrom-Json
$abi=Get-Content -LiteralPath $abiPath -Raw | ConvertFrom-Json
$shape=$abi.originalPcLoadedWorldChildShape
if([string]$seed.Status -ne 'AZTEC_PC_GFXWORLD_800_TO_XBOX_900_ROOT_SCALAR_AND_DPVS_SEED_PASS_NOT_FF' -or
   [string]$gate.status -ne 'AZTEC_GFXWORLD_SOURCE_STATIC_DPVS_CHILD_PROBE_OAT_PASS' -or
   -not [bool]$gate.OatPass -or [bool]$gate.hardwareReady -or
   [string]$abi.status -ne 'AZTEC_NATIVE_XBOX_GFXWORLD_ROOT_AND_DPVS_ABI_EXACT_SOURCE_WITNESS' -or
   [int]$shape.planeCount -ne 3447 -or [int]$shape.nodeCount -ne 24511 -or
   [int]$shape.smodelCount -ne 1903 -or [int]$shape.cellCount -ne 11 -or
   [int]$shape.staticSurfaceCount -ne 613){
    throw 'Requires exact original-PC 3447/24511/1903 world OAT witness and proven static DPVS integration.'
}
$pc=[string]$seed.OriginalPcFastFile
if(-not(Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$abi.originalPcFastFileSha256){
    throw 'Original PC map FF SHA differs from the prior source witness.'
}
function Test-Dumper {
    if(-not(Test-Path -LiteralPath $dumper -PathType Leaf) -or -not(Test-Path -LiteralPath $capPath -PathType Leaf)){return $false}
    $c=Get-Content -LiteralPath $capPath -Raw | ConvertFrom-Json
    return ([string]$c.format -eq 'WaW360.OatGraphDumperCapabilities.v1' -and
        [string]$c.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($c.capabilities) -contains $cap)
}
if(-not(Test-Dumper) -and $BuildDumper){
    Write-Host '[AZTEC-GATE8-CORPUS] One-time pinned OAT rebuild for PC child-source exporter.'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1')
    if($LASTEXITCODE -ne 0){throw 'Specialized PC OAT exporter build failed.'}
}
if(-not(Test-Dumper)){throw "Missing OAT capability $cap; run once with -BuildDumper."}
New-Item -ItemType Directory -Force -Path $out | Out-Null
$temp=Join-Path ([IO.Path]::GetTempPath()) ('AztecGate8_' + [Guid]::NewGuid().ToString('N').Substring(0,10))
New-Item -ItemType Directory -Force -Path $temp | Out-Null
try{
    Write-Host '[AZTEC-GATE8-CORPUS] PC OAT typed planes/nodes/vertex layer + static model names'
    $lines=New-Object 'System.Collections.Generic.List[string]'
    $save=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $dumper '--no-color' '--include-assets' 'gfxworld' '--output-folder' $temp $pc 2>&1 | ForEach-Object {$lines.Add([string]$_)}
        $code=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$save}
    $log=Join-Path $out 'pc_oat_gate8_child_source.log'
    @("Exit=$code","OriginalPcFF=$pc")+@($lines.ToArray()) | Set-Content -LiteralPath $log -Encoding UTF8
    if($code -ne 0){
        Get-Content -LiteralPath $log -Tail 20 | ForEach-Object {Write-Host "[AZTEC-GATE8-CORPUS] OAT $_"}
        throw "Original PC OAT typed GfxWorld source dump failed: $log"
    }
    $corp=Join-Path $temp 'pc_gfxworld_39_material_source_corpus'
    $manifests=@(Get-ChildItem -LiteralPath $corp -Filter manifest.json -Recurse -File -ErrorAction SilentlyContinue)
    if($manifests.Count -ne 1){throw "Expected one source GfxWorld material manifest, found $($manifests.Count)."}
    $m=Get-Content -LiteralPath $manifests[0].FullName -Raw | ConvertFrom-Json
    $g=$m.gate8ChildSource
    if([string]$m.format -ne 'WaW360.T4PcGfxWorld39MaterialSourceCorpus.v1' -or
       [string]$g.status -ne 'T4_PC_GATE8_TYPED_CHILD_BLOBS_EXPORTED' -or
       [int]$g.planeCount -ne 3447 -or [int]$g.nodeCount -ne 24511 -or
       [int]$g.smodelCount -ne 1903 -or [int]$g.cellCount -ne 11 -or
       [int]$g.planeStride -ne 20 -or [int]$g.nodeStride -ne 2 -or
       [int]$g.vertexLayerDataSize -le 0 -or [int]$g.vertexLayerDataSize -gt 33554432 -or
       @($g.staticModelDrawNames).Count -ne 1903 -or
       -not [bool]$g.planesPresent -or -not [bool]$g.nodesPresent -or
       -not [bool]$g.layerPresent -or -not [bool]$g.staticModelDrawInstsPresent){
        throw 'Typed original-PC Gate8 source arrays or OAT ABI failed strict requirements.'
    }
    $specs=@(
        @{key='planes'; rel=[string]$g.planesPath; expected=3447*20; name='planes_pc_le.bin'},
        @{key='nodes'; rel=[string]$g.nodesPath; expected=24511*2; name='nodes_pc_le.bin'},
        @{key='vertexLayer'; rel=[string]$g.vertexLayerPath; expected=[int]$g.vertexLayerDataSize; name='vertex_layer_pc_raw.bin'}
    )
    $blobs=[ordered]@{}
    foreach($spec in $specs){
        if($spec.rel -notmatch '^[A-Za-z0-9_./-]+$' -or
           $spec.rel.Contains('..') -or [IO.Path]::IsPathRooted($spec.rel)){
            throw "Unsafe OAT blob path: $($spec.rel)"
        }
        $src=[IO.Path]::GetFullPath((Join-Path $temp ($spec.rel -replace '/','\')))
        $prefix=[IO.Path]::GetFullPath($temp).TrimEnd([char]'\')+'\'
        if(-not $src.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -or
           -not(Test-Path -LiteralPath $src -PathType Leaf)){
            throw "Missing PC OAT child blob: $src"
        }
        $bytes=(Get-Item -LiteralPath $src).Length
        if($bytes -ne [long]$spec.expected){throw "PC child blob length mismatch $($spec.key): $bytes"}
        $dest=Join-Path $out ([string]$spec.name)
        Copy-Item -LiteralPath $src -Destination $dest -Force
        $blobs[[string]$spec.key]=[ordered]@{
            path=$dest; bytes=$bytes
            sha256=(Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash
        }
    }
    $pbytes=[IO.File]::ReadAllBytes([string]$blobs.planes.path)
    for($i=0;$i -lt 3447;$i++){
        for($k=0;$k -lt 4;$k++){
            $n=[BitConverter]::ToSingle($pbytes,$i*20+$k*4)
            if([Single]::IsNaN($n) -or [Single]::IsInfinity($n) -or [Math]::Abs($n) -gt 100000000){
                throw "Original PC plane[$i] invalid float #$k"
            }
        }
    }
    $missing=@($g.staticModelDrawNames | Where-Object {$null -eq $_ -or $_ -eq ''}).Count
    $unique=@($g.staticModelDrawNames | Where-Object {$_} | Sort-Object -Unique).Count
    $result=[ordered]@{
        schema='WaW360.AztecGate8PcTypedChildCorpus.v1'
        status='AZTEC_PC_GATE8_TYPED_PLANES_NODES_LAYER_SOURCE_PASS'
        originalPcFF=$pc
        originalPcFFSha256=[string]$abi.originalPcFastFileSha256
        verifiedStaticDpvsOatPass=$gatePath
        planeCount=3447; nodeCount=24511; staticModelDrawCount=1903
        vertexLayerBytes=[int]$g.vertexLayerDataSize
        distinctStaticModelNames=$unique; missingStaticModelNames=$missing
        staticModelDrawNames=@($g.staticModelDrawNames)
        sourceBlobs=$blobs
        xboxFfEmitted=$false; fullDpvsComplete=$false; xboxHardwareReady=$false
        note='OAT-fixed-up original-PC plane/node/layer blobs and typed XModel names only. No original Xbox world bytes, no Xenon fastfile, no deployment.'
    }
    $result | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $report -Encoding UTF8
    Write-Host "[AZTEC-GATE8-CORPUS] STATUS $($result.status)"
    Write-Host "[AZTEC-GATE8-CORPUS] planes=3447 nodes=24511 vertexLayerBytes=$($g.vertexLayerDataSize)"
    Write-Host "[AZTEC-GATE8-CORPUS] staticModelDraws=1903 distinctNames=$unique missing=$missing"
    Write-Host "[AZTEC-GATE8-CORPUS] report=$report"
    Write-Host '[AZTEC-GATE8-CORPUS] PC SOURCE ONLY. NO FF / NO XBOX.'
}finally{
    if(Test-Path -LiteralPath $temp){Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue}
}
