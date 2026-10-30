[CmdletBinding()]
param(
    [switch]$BuildDumper,
    [switch]$Use28ModelWorld
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Gate8C PC-source placement witness. Source PC x86 OAT has fixed-up typed
# draw structures; NEVER re-export raw in-process XModel* pointer bytes.
# NOT a Xenon draw serializer. Does not mutate either the prior five-
# model baseline OR the current 28-XModel/8509-root OAT-verified FF.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$n=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$original=Join-Path $n 'pc_gfxworld_gate8_child_source_corpus\aztec_pc_gate8_child_source_corpus.json'
$worldProof=Join-Path $n $(if($Use28ModelWorld){
    'pc_gate8_29x_ff\aztec_pc_world_core_integrated_oat_run.json'
}else{
    'pc_gate8_5x_ff\aztec_pc_world_core_integrated_oat_run.json'
})
$models=Join-Path $n 'pc_gate8_static_model_dependency_audit\aztec_gate8_static_draw_xmodel_dependencies.json'
$build=Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$cap=Join-Path (Split-Path -Parent $oat) 'waw360-graph-dumper-capabilities.json'
$work=Join-Path $n 'pc_gate8_static_draw_source'
$log=Join-Path $work 'original_pc_oat_static_draw_source.log'
$recordsOut=Join-Path $work 'aztec_1903_original_pc_static_draws.json'
$report=Join-Path $work 'aztec_gate8_pc_static_draw_source_report.json'
foreach($p in @($original,$worldProof,$models,$build)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Gate8C verified source prerequisite missing: $p"}
}
$c=Get-Content -LiteralPath $original -Raw | ConvertFrom-Json
$f=Get-Content -LiteralPath $worldProof -Raw | ConvertFrom-Json
$a=Get-Content -LiteralPath $models -Raw | ConvertFrom-Json
$expectedWorldStatus=if($Use28ModelWorld){
    'AZTEC_GATE8_ALL_CLASSIFIED_STATIC_XMODEL_TYPES_8509_ROOT_WORLD_OAT_PASS'
}else{
    'AZTEC_GATE8_FIVE_REAL_XMODELS_8509_ROOT_WORLD_OAT_PASS'
}
$expectedBodies=if($Use28ModelWorld){28}else{5}
$worldModeVerified=if($Use28ModelWorld){
    [bool]$f.all27StaticSourceXModelProbe -and
    [int]$f.sourceOwnedStaticModelTypes -eq 26 -and
    [int]$f.verifiedNativeExternalModelReferences -eq 1 -and
    [int]$f.unresolvedNativeSourceModelReferences -eq 0 -and
    [int]$f.realPcGfxSurfaceCount -eq 735
}else{
    [bool]$f.fiveSourceOwnedXModelProbe
}
if([string]$c.schema -ne 'WaW360.AztecGate8PcTypedChildCorpus.v1' -or
   [string]$c.status -ne 'AZTEC_PC_GATE8_TYPED_PLANES_NODES_LAYER_SOURCE_PASS' -or
   [int]$c.staticModelDrawCount -ne 1903 -or
   @($c.staticModelDrawNames).Count -ne 1903 -or
   [int]$c.distinctStaticModelNames -ne 27 -or
   [int]$c.missingStaticModelNames -ne 0 -or
   [string]$f.schema -ne 'WaW360.AztecPcGfxWorldIntegratedFastFileOatRun.v1' -or
   [string]$f.status -ne $expectedWorldStatus -or
   -not [bool]$f.OatPass -or
   [int]$f.realXModelRootsInCurrentFf -ne $expectedBodies -or
   -not $worldModeVerified -or
   [bool]$f.hardwareReady -or
   -not(Test-Path -LiteralPath ([string]$f.outputFastFile) -PathType Leaf) -or
   [string]$a.schema -ne 'WaW360.AztecGate8StaticDrawModelDependencyAudit.v1' -or
   [int]$a.sourceDrawInstances -ne 1903 -or
   [int]$a.distinctSourceStaticModelNames -ne 27){
    throw "Gate8C requires original-PC 1903 typed draw names and real OAT-validated $expectedBodies-XModel 8509-root world; source-only, no FF mutation."
}
$pc=[string]$c.originalPcFF
if(-not(Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$c.originalPcFFSha256){
    throw 'Original-PC map.ff has changed relative to authoritative typed Gate8 source corpus.'
}
if($BuildDumper){
    Write-Host '[AZTEC-GATE8-DRAW] Rebuilding specialized PC OAT once for new typed placement exporter.'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $build
    if($LASTEXITCODE -ne 0){throw 'Pinned OAT typed static-draw dumper build failed.'}
}
if(-not(Test-Path -LiteralPath $cap -PathType Leaf) -or
   -not(Test-Path -LiteralPath $oat -PathType Leaf)){
    throw 'Specialized PC OAT Unlinker/capabilities missing; retry with -BuildDumper.'
}
$caps=Get-Content -LiteralPath $cap -Raw | ConvertFrom-Json
if([string]$caps.format -ne 'WaW360.OatGraphDumperCapabilities.v1' -or
   [string]$caps.oatRevision -ne '3beaa7c81af517f0c05f2b4612d477ed1541b676' -or
   @($caps.capabilities) -notcontains 't4-pc-gfxworld-gate8-static-draw-source-v1'){
    throw 'Pinned OAT needs new typed static-draw capability. Retry ONCE with -BuildDumper.'
}
New-Item -ItemType Directory -Force -Path $work | Out-Null
$short=Join-Path ([IO.Path]::GetTempPath()) ('AzG8Draw_'+[Guid]::NewGuid().ToString('N').Substring(0,8))
if($short.Length -ge 105){throw "Native Win32 OAT scratch path too long: $short"}
New-Item -ItemType Directory -Force -Path $short | Out-Null
try{
    Write-Host '[AZTEC-GATE8-DRAW] ================================================'
    Write-Host '[AZTEC-GATE8-DRAW] ORIGINAL PC 1903 STATIC DRAW TRANSFORM/METADATA SOURCE'
    Write-Host '[AZTEC-GATE8-DRAW] Export exact typed fields; never copy PC host pointers.'
    Write-Host "[AZTEC-GATE8-DRAW] Verified current Xbox world identity: OAT PASS with $expectedBodies real XModel bodies; main FF untouched."
    $prior=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $oat '--no-color' '--include-assets' 'gfxworld' '--output-folder' $short $pc 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $log -Encoding UTF8
        $exit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$prior}
    if($exit -ne 0 -or
       @(Select-String -LiteralPath $log -Pattern '^\s*ERROR:').Count -gt 0 -or
       @(Select-String -LiteralPath $log -Pattern '(?i)^\s*Finished with\s+0\s+warnings?,\s+0\s+errors?\s*$').Count -eq 0){
        Get-Content -LiteralPath $log -Tail 20 | ForEach-Object {Write-Host "[AZTEC-GATE8-DRAW] OAT $_"}
        throw "Pinned PC OAT source exporter did not complete 0 warnings/0 errors. Read $log"
    }
    $corpus=Join-Path $short 'pc_gfxworld_39_material_source_corpus'
    $manifests=@(Get-ChildItem -LiteralPath $corpus -Recurse -Filter manifest.json -File)
    if($manifests.Count -ne 1){throw "Expected one loaded original-PC GfxWorld source manifest, found $($manifests.Count)."}
    $m=Get-Content -LiteralPath $manifests[0].FullName -Raw | ConvertFrom-Json
    $g=$m.gate8StaticDrawSource
    if([string]$m.format -ne 'WaW360.T4PcGfxWorld39MaterialSourceCorpus.v1' -or
       [string]$g.status -ne 'T4_PC_GATE8_1903_TYPED_DRAWS_SOURCE_EXPORTED' -or
       [int]$g.count -ne 1903 -or
       [int]$g.pcDrawSize -ne 92 -or
       [int]$g.pcPlacementSize -ne 52 -or
       [int]$g.modelPointerOffset -ne 56 -or
       [bool]$g.pointerBytesExported -or
       [bool]$g.xboxDrawRecordsWritten){
        throw 'PC typed static draw export ABI, record count or pointer-sanitization status blocked.'
    }
    $rel=[string]$g.recordsPath
    if($rel -notmatch '^[A-Za-z0-9_./-]+$' -or
       $rel.Contains('..') -or [IO.Path]::IsPathRooted($rel)){
        throw "OAT static draw sidecar path invalid: $rel"
    }
    $src=[IO.Path]::GetFullPath((Join-Path $short ($rel.Replace('/','\'))))
    $allowed=[IO.Path]::GetFullPath($short).TrimEnd([char]'\')+'\'
    if(-not $src.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or
       -not(Test-Path -LiteralPath $src -PathType Leaf)){
        throw "Actual 1903-draw typed PC sidecar missing: $src"
    }
    $d=Get-Content -LiteralPath $src -Raw | ConvertFrom-Json
    if([string]$d.format -ne 'WaW360.T4PcGate8StaticDrawSource.v1' -or
       [int]$d.count -ne 1903 -or
       [int]$d.pcDrawSize -ne 92 -or [int]$d.pcPlacementSize -ne 52 -or
       [int]$d.modelPointerOffset -ne 56 -or
       @($d.records).Count -ne 1903){
        throw 'Loaded original-PC typed placement count, format or x86 struct ABI mismatch.'
    }
    $counts=@{}
    $topModels=@('foliage_oki2_tallgrass','foliage_pacific_grass01_large','foliage_cod5_hedgerow04')
    $top=0
    for($i=0;$i -lt 1903;$i++){
        $r=$d.records[$i]
        $name=[string]$r.model
        if([int]$r.ordinal -ne $i -or
           [string]::IsNullOrWhiteSpace($name) -or
           $name -cne [string]$c.staticModelDrawNames[$i] -or
           @($r.origin).Count -ne 3 -or @($r.axis).Count -ne 3 -or
           @($r.smodelCacheIndex).Count -ne 4){
            throw "Original PC typed static draw source identity/shape drift at ordinal $i."
        }
        $values=New-Object 'System.Collections.Generic.List[double]'
        foreach($val in @($r.cullDist,$r.scale) + @($r.origin)){
            $values.Add([double]$val)
        }
        foreach($axis in @($r.axis)){
            if(@($axis).Count -ne 3){throw "Original PC rotation axis is not 3x3 at $i."}
            foreach($val in @($axis)){$values.Add([double]$val)}
        }
        foreach($val in $values){
            if([double]::IsNaN($val) -or [double]::IsInfinity($val)){
                throw "Nonfinite source static draw transform at ordinal $i."
            }
        }
        if(-not $counts.ContainsKey($name)){$counts[$name]=0}
        $counts[$name]++
        if($topModels -ccontains $name){$top++}
    }
    if($counts.Count -ne 27 -or $top -ne 1572){
        throw "Original-PC 1903 draw records do not match source 27-model / 1572 foliage dependency census."
    }
    foreach($row in @($a.modelDependencyRows)){
        if(-not $counts.ContainsKey([string]$row.pcModelName) -or
           [int]$counts[[string]$row.pcModelName] -ne [int]$row.occurrences){
            throw "Exact typed source draw histogram differs from independent Gate8 identity audit: $($row.pcModelName)"
        }
    }
    Copy-Item -LiteralPath $src -Destination $recordsOut -Force
    $sha=(Get-FileHash -LiteralPath $recordsOut -Algorithm SHA256).Hash
    $result=[ordered]@{
        schema='WaW360.AztecGate8PcStaticDrawSourceProof.v1'
        status='AZTEC_GATE8_1903_PC_TYPED_STATIC_DRAWS_SOURCE_PASS'
        originalPcFastFile=$pc
        sourceFFSha256=[string]$c.originalPcFFSha256
        typedDrawFile=$recordsOut
        typedDrawSha256=$sha
        sourceDrawCount=1903
        distinctSourceModels=27
        verifiedFiveWorldFfReport=if($Use28ModelWorld){''}else{$worldProof}
        verifiedSourceWorldFfReport=$worldProof
        verifiedSourceWorldModelBodies=$expectedBodies
        verifiedSourceWorldHasAll27StaticModelIdentities=[bool]$Use28ModelWorld
        sourceDrawOccurrencesCoveredByRealConvertedModelTypes=if($Use28ModelWorld){1902}else{1572}
        fiveRealModelBodyDrawsRepresented=1572
        convertedXenonDrawRecords=0
        pcModelPointerBytesCopied=$false
        pcToXenonPackedAxisConversionVerified=$false
        originalPcLightingToXenonMappingVerified=$false
        hardwareReady=$false
        note='Source-only original-PC x86 GfxStaticModelDrawInst fields: cullDist, float placement origin/axis/scale, model names, lodFade, cached light metadata, flags and cache indices. No Xbox PackedUnitVec axes, lightingHandle, padding or runtime pointers are invented.'
    }
    $result | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $report -Encoding UTF8
    Write-Host "[AZTEC-GATE8-DRAW] PASS: $($result.status)"
    Write-Host '[AZTEC-GATE8-DRAW] originalPCDraws=1903 distinctModels=27'
    Write-Host "[AZTEC-GATE8-DRAW] PC model names cross-checked against $expectedBodies-body OAT-valid world; real source types currently represent $(if($Use28ModelWorld){1902}else{1572}) source draw occurrences."
    Write-Host '[AZTEC-GATE8-DRAW] source fields: full PC float axes, placement, lighting metadata, flags, LOD'
    Write-Host "[AZTEC-GATE8-DRAW] SHA256=$sha"
    Write-Host "[AZTEC-GATE8-DRAW] report=$report"
    Write-Host '[AZTEC-GATE8-DRAW] SOURCE ONLY; NO DRAW RELOCATION/NO FF MUTATION/NO XBOX.'
}
finally{
    if(Test-Path -LiteralPath $short -PathType Container){
        Remove-Item -LiteralPath $short -Recurse -Force -ErrorAction SilentlyContinue
    }
}
