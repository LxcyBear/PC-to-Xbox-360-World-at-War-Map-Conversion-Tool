[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$nativeRoot=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$combinedPath=Join-Path $nativeRoot 'pc_two_xmodel_two_material_fullgraph_integration\aztec_two_pc_xmodel_two_material_fullgraph_integration.json'
$canaryPath=Join-Path $nativeRoot 'pc_two_material_xenon_canary\aztec_pc_two_material_xenon_canary.json'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$work=Join-Path $nativeRoot 'pc_two_model_material_combined_readback'
$logPath=Join-Path $work 'combined_xenon_material_graph_dump.log'
$reportPath=Join-Path $work 'combined_pc_material_readback.json'
$summaryPath=Join-Path $work 'combined_pc_material_readback.txt'
foreach($f in @($combinedPath,$canaryPath,$oat)){
    if(-not (Test-Path -LiteralPath $f -PathType Leaf)){throw "Missing combined material readback prerequisite: $f"}
}
$full=Get-Content -LiteralPath $combinedPath -Raw | ConvertFrom-Json
$canary=Get-Content -LiteralPath $canaryPath -Raw | ConvertFrom-Json
if([string]$full.schema -ne 'WaW360.AztecPcTwoXModelTwoMaterialMixedGraph.v1' -or
   [string]$full.status -ne 'AZTEC_PC_TWO_XMODEL_TWO_MATERIAL_FULLGRAPH_PASS' -or
   [int]$full.nativeRoots -ne 8509 -or
   [int]$full.realXModelRoots -ne 2 -or
   [int]$full.realImageRoots -ne 2 -or
   [int]$full.realRawFileRoots -ne 114 -or
   [int]$full.independentModelNamesListed -ne 2 -or
   [int]$full.independentMaterialNamesListed -ne 2 -or
   [int]$full.independentPcPixelShaExactReadbacks -ne 2 -or
   [int]$full.independentPcRawFileShaExactReadbacks -ne 114 -or
   -not [bool]$full.layoutReplayExact -or
   -not [bool]$full.oatZeroWarningsZeroErrors -or
   [bool]$full.readyForXbox -or
   [string]$canary.schema -ne 'WaW360.AztecPcTwoMaterialXenonCanary.v1' -or
   [string]$canary.status -ne 'AZTEC_PC_TWO_MATERIAL_XENON_CANARY_PASS' -or
   [int]$canary.xenonMaterialGraphCount -ne 2 -or
   [bool]$canary.readyForXbox -or
   [string]$full.materialCanaryProof -ine $canaryPath){
    throw 'Mixed fullgraph or source-PC standalone material provenance no longer passes exact gates.'
}
$ff=[string]$full.diagnosticScratchFF
if(-not (Test-Path -LiteralPath $ff -PathType Leaf)){throw "Full-graph scratch Xenon FF missing: $ff"}
$allowedDir=Join-Path $nativeRoot 'pc_two_xmodel_two_material_fullgraph_integration\scratch_emit'
$allowedRoot=([IO.Path]::GetFullPath($allowedDir)).TrimEnd([char]'\')+'\'
if(-not ([IO.Path]::GetFullPath($ff)).StartsWith($allowedRoot,[StringComparison]::OrdinalIgnoreCase)){
    throw 'Combined FF path is outside the known isolated scratch emitter output.'
}
function Norm([string]$s){
    $s=$s.Trim().Trim('"').Replace('\','/')
    while($s.StartsWith(',',[StringComparison]::Ordinal)){$s=$s.Substring(1)}
    if($s.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){$s=$s.Substring(0,$s.Length-3)}
    return $s.ToLowerInvariant()
}
function JsonVal([object]$x){
    return ConvertTo-Json -InputObject $x -Depth 15 -Compress
}
function Compare-Section([object]$a,[object]$b,[string]$label){
    if((JsonVal $a) -cne (JsonVal $b)){throw "Combined Material graph $label differs from original standalone PC Xenon material canary."}
}
function Blob-SHA([object]$g,[string]$root,[string]$property,[long]$expected){
    $relative=[string]$g.$property
    if([string]::IsNullOrWhiteSpace($relative)){
        if($expected -ne 0){throw "Graph $($g.name) missing required blob $property ($expected bytes)."}
        return ''
    }
    if([IO.Path]::IsPathRooted($relative) -or $relative.Contains('..') -or $relative.Contains(':')){
        throw "Unsafe graph blob path in $property : $relative"
    }
    $rootFull=([IO.Path]::GetFullPath($root)).TrimEnd([char]'\')+'\'
    $fullName=[IO.Path]::GetFullPath((Join-Path $root ($relative.Replace('/','\'))))
    if(-not $fullName.StartsWith($rootFull,[StringComparison]::OrdinalIgnoreCase)){
        throw "Material graph blob escapes dump root: $relative"
    }
    if(-not (Test-Path -LiteralPath $fullName -PathType Leaf) -or
       [long](Get-Item -LiteralPath $fullName).Length -ne $expected){
        throw "Missing/incorrect Material graph blob $property expectedBytes=$expected : $fullName"
    }
    return (Get-FileHash -LiteralPath $fullName -Algorithm SHA256).Hash
}
function Texture-Signature([object]$g){
    $rows=New-Object 'System.Collections.Generic.List[object]'
    foreach($t in @($g.textures)){
        $rows.Add([pscustomobject]@{
            index=[int]$t.index
            semantic=[int]$t.semantic
            image=(Norm ([string]$t.image))
            hasWater=$($null -ne $t.water -and [string]$t.water -notin @('','null'))
        })
    }
    return $rows.ToArray()
}
$baselineRoot=[string]$canary.xenonGraphRoot
$sourceGraphDir=Join-Path $baselineRoot 'xenon_material_graphs'
if(-not (Test-Path -LiteralPath $sourceGraphDir -PathType Container)){
    throw 'Original standalone 2 PC Xenon material graph directory missing.'
}
$sourceFiles=@(Get-ChildItem -LiteralPath $sourceGraphDir -Filter '*.json' -File -Recurse)
if($sourceFiles.Count -ne 2){throw "Expected exact two original PC Material graphs, got $($sourceFiles.Count)."}
$sourceGraphs=@{}
foreach($f in $sourceFiles){
    $g=Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json
    $key=Norm ([string]$g.name)
    $known=@($canary.xenonMaterialGraphs | Where-Object {(Norm ([string]$_.name)) -eq $key})
    if($known.Count -ne 1 -or
       (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash -ine [string]$known[0].jsonSha256 -or
       $sourceGraphs.ContainsKey($key)){
        throw "Source Material graph $key JSON SHA or provenance changed."
    }
    $sourceGraphs[$key]=$g
}
$expectedNames=@('mc/lambert1','mc/mtl_x2icon_gold')
if($sourceGraphs.Count -ne 2 -or @($expectedNames | Where-Object {-not $sourceGraphs.ContainsKey($_)}).Count -ne 0){
    throw 'Original standalone material canary identities drifted.'
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
$short=''
$status='AZTEC_PC_TWO_MATERIAL_FULLGRAPH_READBACK_BLOCKED'
$blocker=''
$verified=New-Object 'System.Collections.Generic.List[object]'
$numberOfGraphs=0
Write-Host '[AZTEC-MAT-READBACK] ==================================================='
Write-Host '[AZTEC-MAT-READBACK] INDEPENDENT MATERIAL BODY READBACK FROM 8509 ROOT FF'
Write-Host '[AZTEC-MAT-READBACK] Compare source-owned TechniqueSets, textures, constants, state bits.'
Write-Host '[AZTEC-MAT-READBACK] Scratch only; NEVER DEPLOY ON XBOX.'
try{
    $short=Join-Path ([IO.Path]::GetTempPath()) ('wawAzMatRb_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    if($short.Length -gt 115){throw 'OAT graph dumper needs a short physical TEMP folder.'}
    New-Item -Path $short -ItemType Directory -Force | Out-Null
    $prev=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $oat '--no-color' '--include-assets' 'material' '--output-folder' $short $ff 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $logPath -Encoding UTF8
        $exitCode=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$prev}
    $errors=@(Select-String -LiteralPath $logPath -Pattern '(?i)^\s*(ERROR:|WARNING:)')
    $zero=@(Select-String -LiteralPath $logPath -Pattern '(?i)^\s*Finished with\s+0\s+warnings?,\s+0\s+errors?\s*$')
    if($exitCode -ne 0 -or $errors.Count -ne 0 -or $zero.Count -eq 0){
        throw "Combined FF independent OAT Material graph dump failed exit=$exitCode errors=$($errors.Count); see $logPath"
    }
    $dumpDir=Join-Path $short 'xenon_material_graphs'
    if(-not (Test-Path -LiteralPath $dumpDir -PathType Container)){
        throw 'Independent OAT dump produced no Xenon material graphs.'
    }
    $dumped=@(Get-ChildItem -LiteralPath $dumpDir -Filter '*.json' -File -Recurse)
    $numberOfGraphs=$dumped.Count
    foreach($name in $expectedNames){
        $candidates=New-Object 'System.Collections.Generic.List[object]'
        foreach($f in $dumped){
            $g=Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json
            if((Norm ([string]$g.name)) -eq $name){
                $candidates.Add([pscustomobject]@{File=$f.FullName;Graph=$g})
            }
        }
        if($candidates.Count -ne 1){throw "Combined FF has $($candidates.Count) independent Material graph(s) for $name (expected one)."}
        $g=$candidates[0].Graph
        $src=$sourceGraphs[$name]
        if([string]$g.format -ne 'WaW360.T4Xenon.MaterialRelocatableGraph.v1' -or
           [int]$g.abi.material -ne 96 -or [int]$g.reusable.aliasCount -ne 0 -or
           [int]$g.inventory.waterTextures -ne 0 -or
           [int]$g.inventory.waterSamples -ne 0 -or
           [int]$g.inventory.invalidWaterDimensions -ne 0){
            throw "Material $name has unsupported Xenon ABI/alias/water graph payload."
        }
        Compare-Section $src.abi $g.abi "$name ABI"
        Compare-Section $src.alignments $g.alignments "$name alignments"
        Compare-Section $src.offsets $g.offsets "$name offsets"
        Compare-Section $src.counts $g.counts "$name counts"
        Compare-Section $src.presence $g.presence "$name pointer presence"
        if((Norm ([string]$src.techniqueSet)) -ne (Norm ([string]$g.techniqueSet))){
            throw "Material $name lost its original-PC TechniqueSet identity."
        }
        Compare-Section (Texture-Signature $src) (Texture-Signature $g) "$name texture slots/images"
        $blobRows=New-Object 'System.Collections.Generic.List[object]'
        foreach($tuple in @(
            @('constantTableFile',[long]$g.counts.constants * [long]$g.abi.constantDef),
            @('stateBitsTableFile',[long]$g.counts.stateBits * [long]$g.abi.stateBits)
        )){
            $field=[string]$tuple[0];$bytes=[long]$tuple[1]
            $oldSha=Blob-SHA $src $baselineRoot $field $bytes
            $newSha=Blob-SHA $g $short $field $bytes
            if($oldSha -ine $newSha){
                throw "PC Material $name $field byte payload changed during 8509-root integration."
            }
            $blobRows.Add([pscustomobject]@{field=$field;bytes=$bytes;sha256=$newSha})
        }
        $verified.Add([pscustomobject]@{
            name=$name;originalPcMaterialSource=$true
            techniqueSet=[string]$g.techniqueSet
            textureSlots=[int]$g.counts.textures
            textureImages=@($g.textures | ForEach-Object {[string]$_.image})
            constants=[int]$g.counts.constants
            stateBits=[int]$g.counts.stateBits
            byteReadbacks=$blobRows.ToArray()
        })
        Write-Host "[AZTEC-MAT-READBACK] $name PASS: textures=$($g.counts.textures), constants=$($g.counts.constants), stateBits=$($g.counts.stateBits)"
    }
    if($verified.Count -ne 2){throw 'Exact two original-PC material body readbacks required.'}
    $status='AZTEC_PC_TWO_MATERIAL_FULLGRAPH_READBACK_PASS'
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-MAT-READBACK] BLOCKER: $blocker"
    if(Test-Path -LiteralPath $logPath -PathType Leaf){
        foreach($line in @(Get-Content -LiteralPath $logPath -Tail 8)){Write-Host "[AZTEC-MAT-READBACK] OAT $line"}
    }
}finally{
    if(-not [string]::IsNullOrWhiteSpace($short) -and
       (Test-Path -LiteralPath $short -PathType Container)){
        Remove-Item -LiteralPath $short -Recurse -Force
    }
}
$result=[ordered]@{
    schema='WaW360.AztecPcTwoMaterialIntegratedReadback.v1'
    status=$status
    originalFullgraphProof=$combinedPath
    originalSourceMaterialProof=$canaryPath
    nativeRoots=8509
    originalPcMaterialBodyReadbacks=$verified.Count
    totalMaterialGraphsObserved=$numberOfGraphs
    verifiedMaterials=$verified.ToArray()
    blocker=$blocker
    xenonHardwareTestAuthorized=$false
    fullMapPlayableProven=$false
    sourceOwnedWorldsComplete=$false
    note='Independent fullgraph Material OAT export/readback, semantic graph equivalence and constant/stateBits SHA checks only. Original FF remains untouched; runtime references not proven. NOT FOR XBOX.'
}
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath -Encoding UTF8
[IO.File]::WriteAllLines($summaryPath,[string[]]@(
    "AZTEC ORIGINAL-PC MATERIAL FULLGRAPH READBACK: $status"
    "original PC material graphs read back=$($verified.Count)/2"
    "all OAT-exported material graph JSON files=$numberOfGraphs"
    "blocker=$blocker"
    'Standalone PC-source TechniqueSets/images are references, not source-owned runtime shader/pixel closure.'
    'No new FF created. NOT FOR XBOX.'
),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-MAT-READBACK] ================= RESULT ========================'
Write-Host "[AZTEC-MAT-READBACK] status=$status"
Write-Host "[AZTEC-MAT-READBACK] independentOriginalPcMaterialReadbacks=$($verified.Count)/2"
Write-Host "[AZTEC-MAT-READBACK] report=$summaryPath"
Write-Host '[AZTEC-MAT-READBACK] READ-ONLY / NEVER DEPLOY ON XBOX'
if($status -ne 'AZTEC_PC_TWO_MATERIAL_FULLGRAPH_READBACK_PASS'){exit 4}
