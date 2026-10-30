[CmdletBinding()]
param([switch]$SkipPcModelConversion)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# Source-owned real asset production work: convert all original-PC Aztec
# static model TYPES to independently OAT-valid Xbox XModel bodies, then
# aggregate those actual binary graphs into one relocatable native corpus.
# FIRST TWO existing PC-owned XModels + 27 PC-owned static draw XModels=29.
# Does not claim final GfxWorld DPVS, 1903 draw links, images or runtime
# source dependency closure. Never modifies the existing five-model PASS.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$n=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$waveScript=Join-Path $PSScriptRoot 'Run-AztecGate8StaticXModelWave.ps1'
$wavePath=Join-Path $n 'pc_gate8_static_model_wave\aztec_gate8_static_model_wave.json'
$fivePath=Join-Path $repo '.deps\aztec8-five-xmodels\aztec_gate8_five_xmodel_staging.json'
$fiveBindingPath=Join-Path $repo '.deps\aztec8-five-xmodels\aztec_gate8_five_xmodel_binding.json'
$identityPath=Join-Path $n 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$cap=Join-Path (Split-Path -Parent $oat) 'waw360-graph-dumper-capabilities.json'
$work=Join-Path $repo '.deps\aztec8-all-static-xmodels'
$graphs=Join-Path $work 'graph'
$bindingPath=Join-Path $work 'aztec_gate8_29_xmodel_binding.json'
$reportPath=Join-Path $work 'aztec_gate8_29_xmodel_staging.json'
New-Item -ItemType Directory -Path $work -Force | Out-Null
function Norm([string]$name){
    $v=$name.Trim().Replace('\','/').TrimStart(',')
    if($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(0,$v.Length-3)}
    return $v.ToLowerInvariant()
}
if(-not $SkipPcModelConversion){
    Write-Host '[AZTEC-29X] CONVERT remaining PC XModels via existing OAT source-owned bridge, preserving all proven canaries.'
    $prior=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $waveScript -MaxAdditionalModels 26 2>&1 |
            ForEach-Object {Write-Host "[AZTEC-29X] $_"}
        $exitCode=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$prior}
    if($exitCode -ne 0){throw "Original-PC XModel conversion wave stopped at a real conversion blocker. See $wavePath; no main FF was overwritten."}
}
foreach($file in @($wavePath,$fivePath,$fiveBindingPath,$identityPath,$oat,$cap)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){
        throw "Required source-owned model graph evidence missing: $file"
    }
}
$wave=Get-Content -LiteralPath $wavePath -Raw|ConvertFrom-Json
$five=Get-Content -LiteralPath $fivePath -Raw|ConvertFrom-Json
$bind5=Get-Content -LiteralPath $fiveBindingPath -Raw|ConvertFrom-Json
$identity=Get-Content -LiteralPath $identityPath -Raw|ConvertFrom-Json
$caps=Get-Content -LiteralPath $cap -Raw|ConvertFrom-Json
if([string]$wave.schema -ne 'WaW360.AztecGate8StandaloneXModelWave.v1' -or
   [string]$wave.status -ne 'AZTEC_GATE8_STATIC_MODEL_WAVE_STANDALONE_OAT_PASS' -or
   [int]$wave.sourceStaticModels -ne 27 -or
   [int]$wave.modelOnlyOatProven -ne 27 -or
   [int]$wave.modelOnlyOatRepresentedDraws -ne 1903 -or
   @($wave.verifiedModels).Count -ne 27 -or
   [string]$five.status -ne 'AZTEC_GATE8_FIVE_SOURCE_XMODEL_GRAPHS_STAGED' -or
   [int]$five.modelCount -ne 5 -or
   @($bind5.ownedBindings).Count -ne 5 -or
   @($five.graphProofs).Count -ne 5 -or
   [string]$identity.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
   [int]$identity.assets -ne 8509 -or
   -not [bool]$identity.typeSequenceVerified -or
   [string]$caps.oatRevision -ne '3beaa7c81af517f0c05f2b4612d477ed1541b676' -or
   @($caps.capabilities) -notcontains 't4xenon-xmodel-relocatable-graph-v1'){
    throw 'All 27 converted source XModels and five historical graphs must pass exact native ownership gates.'
}
$byName=@{}
foreach($row in @($wave.verifiedModels)){
    $key=Norm ([string]$row.model)
    if($byName.ContainsKey($key) -or -not(Test-Path -LiteralPath ([string]$row.ff) -PathType Leaf) -or
       (Get-FileHash -LiteralPath ([string]$row.ff) -Algorithm SHA256).Hash -ine [string]$row.sha256){
        throw "Source-only XModel FF SHA/unique name failed: $key"
    }
    $byName[$key]=$row
}
$nativeModels=@($identity.entries | Where-Object {[string]$_.OatTypeName -eq 'xmodel'})
$nativeMaterials=@($identity.entries | Where-Object {[string]$_.OatTypeName -eq 'material'})
if($nativeModels.Count -ne 284){throw 'Native 284 XModel root identities changed.'}
$rows=New-Object 'System.Collections.Generic.List[object]'
$proofs=New-Object 'System.Collections.Generic.List[object]'
$seenRoot=New-Object 'System.Collections.Generic.HashSet[int]'
$seenName=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
function Stage-One([int]$ordinal,[string]$name,[int]$rootIndex,[string]$sourceFolder,[string]$sourceGraph){
    $normName=Norm $name
    $matches=@($nativeModels|Where-Object {
        [int]$_.Index -eq $rootIndex -and (Norm ([string]$_.Name)) -eq $normName
    })
    if($matches.Count -ne 1 -or -not $seenRoot.Add($rootIndex) -or
       -not $seenName.Add($normName)){throw "Duplicate/missing native model root #$rootIndex $name"}
    if(-not(Test-Path -LiteralPath $sourceGraph -PathType Leaf)){
        throw "Actual relocatable Xenon graph missing for $name : $sourceGraph"
    }
    $graph=Get-Content -LiteralPath $sourceGraph -Raw|ConvertFrom-Json
    if([string]$graph.format -ne 'WaW360.T4Xenon.XModelRelocatableGraph.v1' -or
       (Norm ([string]$graph.name)) -ne $normName -or
       [int]$graph.abi.xmodel -ne 212 -or
       [int]$graph.unsupported.count -ne 0 -or
       [int]$graph.reusable.aliasCount -ne 0){
        throw "Unsupported/identity-drifted real Xbox model graph: $name"
    }
    foreach($mat in @($graph.materials)){
        if([string]::IsNullOrWhiteSpace([string]$mat)){continue}
        $m=@($nativeMaterials|Where-Object {(Norm ([string]$_.Name)) -eq (Norm ([string]$mat))})
        if($m.Count -ne 1){
            throw "Real $name Material $mat has $($m.Count) native ownership candidates. Cannot link its graph."
        }
    }
    $dst=Join-Path (Join-Path $graphs 'dumps') ('{0:D3}' -f $ordinal)
    New-Item -ItemType Directory -Path $dst -Force|Out-Null
    & robocopy.exe $sourceFolder $dst '/E' '/R:1' '/W:1' '/NP' '/NFL' '/NDL' '/NJH' '/NJS' | Out-Null
    if([int]$LASTEXITCODE -ge 8){throw "Copy of actual model graph/children for $name failed."}
    $copied=@(Get-ChildItem -LiteralPath (Join-Path $dst 'xenon_xmodel_graphs') -Filter '*.json' -File)
    if($copied.Count -ne 1 -or
       (Get-FileHash -LiteralPath $sourceGraph -Algorithm SHA256).Hash -ine
       (Get-FileHash -LiteralPath $copied[0].FullName -Algorithm SHA256).Hash){
        throw "Actual $name graph SHA changed during stage."
    }
    $rows.Add([ordered]@{
        NativeRootIndex=$rootIndex
        NativeName=[string]$matches[0].Name
        CorpusOrdinal=$ordinal
        CorpusName=$normName
    })
    $proofs.Add([ordered]@{
        ordinal=$ordinal;name=$normName;nativeRoot=$rootIndex
        stagedGraph=$copied[0].FullName
        sha256=(Get-FileHash -LiteralPath $copied[0].FullName -Algorithm SHA256).Hash
    })
    Write-Host "[AZTEC-29X] GRAFT SOURCE XMODEL $ordinal/28 : $normName root=$rootIndex"
}
# The 5-model reference is the exact preexisting verified source graph.
# No modification to .deps/aztec8-five-xmodels is performed.
if(Test-Path -LiteralPath $graphs -PathType Container){Remove-Item -LiteralPath $graphs -Recurse -Force}
New-Item -ItemType Directory -Path (Join-Path $graphs 'dumps') -Force|Out-Null
for($i=0;$i -lt 5;$i++){
    $b=$bind5.ownedBindings[$i]
    $p=$five.graphProofs[$i]
    $src=Join-Path ([string]$five.graphRoot) ('dumps\{0:D3}' -f $i)
    if([int]$b.CorpusOrdinal -ne $i -or
       [int]$p.ordinal -ne $i -or
       (Get-FileHash -LiteralPath ([string]$p.stagedGraph) -Algorithm SHA256).Hash -ine [string]$p.sha256){
        throw "Original OAT-passing five-model source graph proof changed at ordinal $i."
    }
    Stage-One $i ([string]$b.CorpusName) ([int]$b.NativeRootIndex) $src ([string]$p.stagedGraph)
}
# Wave is sorted by source draw frequency: first 3 are known foliage.
for($j=3;$j -lt 27;$j++){
    $w=$wave.verifiedModels[$j]
    $key=Norm ([string]$w.model)
    if($seenName.Contains($key)){throw "Unexpected duplicate prior static XModel $key"}
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('AzG829_'+[Guid]::NewGuid().ToString('N').Substring(0,8))
    if($temp.Length -gt 115){throw "Native OAT temporary path too long: $temp"}
    New-Item -ItemType Directory -Path $temp -Force|Out-Null
    try{
        $log=Join-Path $work ('xmodel_{0:D3}_oat.log' -f ($j+2))
        $prior=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            & $oat '--no-color' '--include-assets' 'xmodel' '--model-format' 'gltf' '--output-folder' $temp ([string]$w.ff) 2>&1 |
                ForEach-Object {[string]$_}|Set-Content -LiteralPath $log -Encoding UTF8
            $exit=[int]$LASTEXITCODE
        }finally{$ErrorActionPreference=$prior}
        if($exit -ne 0 -or
           @(Select-String -LiteralPath $log -Pattern '^\s*ERROR:').Count -gt 0 -or
           @(Select-String -LiteralPath $log -Pattern '(?i)^\s*Finished with\s+0\s+warnings?,\s+0\s+errors?\s*$').Count -ne 1){
            throw "Source $key Xenon graph OAT load failed, exit=$exit. Log=$log"
        }
        $g=@(Get-ChildItem -LiteralPath (Join-Path $temp 'xenon_xmodel_graphs') -Filter '*.json' -File)
        if($g.Count -ne 1){throw "One source-derived model must produce exactly one Xenon relocatable graph: $key"}
        Stage-One ($j+2) $key ([int]$w.nativeRoot) $temp $g[0].FullName
    }finally{
        if(Test-Path -LiteralPath $temp -PathType Container){
            Remove-Item -LiteralPath $temp -Force -Recurse -ErrorAction SilentlyContinue
        }
    }
}
if($rows.Count -ne 29 -or $seenName.Count -ne 29 -or $proofs.Count -ne 29){
    throw 'Complete 27 original-PC static models plus original 2 source-owned XModels was not staged.'
}
$binding=[ordered]@{
    format='WaW360.AztecGate8AllStaticXModelBinding.v1'
    bindingPass=$true
    nativeRoots=8509
    nativeXModelRoots=284
    sourceStaticModels=27
    staticDrawsRepresented=1903
    validationOnly=$true
    hardwareReady=$false
    ownedBindings=@($rows.ToArray())
    referenceBindings=@()
    note='29 real source-owned XModels, original 2 plus all 27 PC static-draw model types. Full model GRAPH ownership only; does not certify GfxWorld draw linkage, other assets or hardware.'
}
$binding|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $bindingPath -Encoding UTF8
$report=[ordered]@{
    schema='WaW360.AztecGate8AllStaticXModelStaging.v1'
    status='AZTEC_GATE8_ALL_27_PC_STATIC_XMODEL_GRAPHS_STAGED'
    sourceModels=27
    modelCount=29
    rootCount=8509
    staticDrawsRepresented=1903
    graphRoot=$graphs
    bindingManifest=$bindingPath
    graphProofs=@($proofs.ToArray())
    sourceWaveProof=$wavePath
    actualWorldDrawRecordsLinked=0
    hardwareReady=$false
}
$report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Host "[AZTEC-29X] status=$($report.status) modelGraphs=29/29 sourceDrawOccurrences=1903/1903"
Write-Host "[AZTEC-29X] binding=$bindingPath"
Write-Host '[AZTEC-29X] Real PC model graph payloads staged; next integrate into native world FF. No Xbox claim.'