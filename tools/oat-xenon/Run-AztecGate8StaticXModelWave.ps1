[CmdletBinding()]
param([ValidateRange(1,26)][int]$MaxAdditionalModels=2)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# Gate 8 priority wave: model-only OAT canaries. Never touches world FF.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$n=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$auditPath=Join-Path $n 'pc_gate8_static_model_dependency_audit\aztec_gate8_static_draw_xmodel_dependencies.json'
$firstPath=Join-Path $n 'pc_gate8_first_static_xmodel\aztec_first_static_draw_xmodel_canary.json'
$script=Join-Path $PSScriptRoot 'Run-AztecGate8FirstStaticDrawXModelCanary.ps1'
$out=Join-Path $n 'pc_gate8_static_model_wave'
$report=Join-Path $out 'aztec_gate8_static_model_wave.json'
foreach($p in @($auditPath,$firstPath,$script)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing Gate8 priority-model prerequisite: $p"}
}
$audit=Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json
if([string]$audit.schema -ne 'WaW360.AztecGate8StaticDrawModelDependencyAudit.v1' -or
   [string]$audit.status -ne 'AZTEC_GATE8_1903_STATIC_DRAWS_27_NAMES_NATIVE_ROOT_RECONCILIATION_COMPLETE_DIAGNOSTIC' -or
   [int]$audit.sourceDrawInstances -ne 1903 -or
   [int]$audit.distinctSourceStaticModelNames -ne 27 -or
   [int]$audit.distinctModelsWithoutProvenRealGraph -ne 27 -or
   [bool]$audit.hardwareReady){
    throw 'Static draw source-name/XModel dependency audit changed.'
}
$rows=@($audit.modelDependencyRows | Sort-Object occurrences -Descending)
if($rows.Count -ne 27 -or
   [string]$rows[0].pcModelName -ne 'foliage_oki2_tallgrass' -or
   [int]$rows[0].occurrences -ne 718 -or
   [string]$rows[1].pcModelName -ne 'foliage_pacific_grass01_large' -or
   [int]$rows[1].occurrences -ne 456 -or
   [string]$rows[2].pcModelName -ne 'foliage_cod5_hedgerow04' -or
   [int]$rows[2].occurrences -ne 398){
    throw 'Prioritized static draw census has changed; no batch conversion until re-audited.'
}
function Norm([string]$name){
    $v=$name.Trim().Replace('\','/').TrimStart(',')
    if($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){
        $v=$v.Substring(0,$v.Length-3)
    }
    return $v.ToLowerInvariant()
}
function Report-Path([string]$name){
    $slug=(Norm $name) -replace '[^a-z0-9_-]','_'
    if($slug -eq 'foliage_oki2_tallgrass'){return $firstPath}
    return Join-Path (Join-Path $n ('pc_gate8_static_xmodel_'+$slug)) 'aztec_first_static_draw_xmodel_canary.json'
}
function Read-Pass($row,[string]$path){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){return $null}
    $m=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if([string]$m.schema -ne 'WaW360.AztecGate8StaticDrawXModelCanary.v1' -or
       [string]$m.status -ne 'AZTEC_GATE8_STATIC_DRAW_PC_XMODEL_TO_XENON_OAT_PASS_STANDALONE' -or
       (Norm ([string]$m.sourceModel)) -ne (Norm ([string]$row.pcModelName)) -or
       [int]$m.drawInstances -ne [int]$row.occurrences -or
       [int]$m.nativeXModelRootIndex -ne [int]$row.nativeRootIndices[0] -or
       [bool]$m.realModelBodyIntegratedInto8509RootWorldFF -or
       [bool]$m.all1903StaticDrawInstancesSerialized -or
       [bool]$m.xboxReady -or
       [int]$m.sourceMeshVertices -lt 3 -or [int]$m.sourceTriangleIndices -lt 3 -or
       -not(Test-Path -LiteralPath ([string]$m.standaloneXenonFF) -PathType Leaf) -or
       (Get-FileHash -LiteralPath ([string]$m.standaloneXenonFF) -Algorithm SHA256).Hash -ine
           [string]$m.standaloneXenonFFSha256){
        throw "Previously passing source-owned XModel FF changed: $path"
    }
    return $m
}
New-Item -ItemType Directory -Force -Path $out | Out-Null
$verified=New-Object 'System.Collections.Generic.List[object]'
$first=Read-Pass $rows[0] $firstPath
if($null -eq $first){throw 'First tallgrass real-body OAT PASS record missing. Preserve original proof.'}
$verified.Add([pscustomobject]@{
    model=[string]$rows[0].pcModelName
    draws=[int]$rows[0].occurrences
    ff=[string]$first.standaloneXenonFF
    sha256=[string]$first.standaloneXenonFFSha256
    nativeRoot=[int]$rows[0].nativeRootIndices[0]
})
Write-Host '[AZTEC-GATE8-XMODEL-WAVE] =========================================='
Write-Host '[AZTEC-GATE8-XMODEL-WAVE] 718-draw tallgrass standalone Xenon FF: VERIFIED and PRESERVED'
Write-Host '[AZTEC-GATE8-XMODEL-WAVE] Next: 456 grass -> 398 hedgerow. Model-only FFs.'
$converted=0
$blocked=''
foreach($row in @($rows | Select-Object -Skip 1)){
    if($converted -ge $MaxAdditionalModels){break}
    if([string]$row.status -ne 'NATIVE_IDENTITY_ONLY_NO_REAL_GRAPH' -or
       @($row.nativeRootIndices).Count -ne 1){
        throw "Source/native XModel mapping drifted for $($row.pcModelName)."
    }
    $path=Report-Path ([string]$row.pcModelName)
    $m=Read-Pass $row $path
    if($null -eq $m){
        Write-Host ("[AZTEC-GATE8-XMODEL-WAVE] CONVERT {0} ({1} draws)" -f $row.pcModelName,$row.occurrences)
        $old=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script -ModelName ([string]$row.pcModelName) 2>&1 |
                ForEach-Object {Write-Host ([string]$_)}
            $exit=[int]$LASTEXITCODE
        }finally{$ErrorActionPreference=$old}
        if($exit -ne 0){
            $blocked="First blocked model: $($row.pcModelName), exit=$exit"
            break
        }
        $m=Read-Pass $row $path
        if($null -eq $m){
            $blocked="Expected complete independent OAT source-model PASS manifest for $($row.pcModelName)"
            break
        }
    }else{
        Write-Host "[AZTEC-GATE8-XMODEL-WAVE] REUSE independent OAT PASS: $($row.pcModelName)"
    }
    $verified.Add([pscustomobject]@{
        model=[string]$row.pcModelName
        draws=[int]$row.occurrences
        ff=[string]$m.standaloneXenonFF
        sha256=[string]$m.standaloneXenonFFSha256
        nativeRoot=[int]$row.nativeRootIndices[0]
    })
    $converted++
}
$draws=[int](($verified.ToArray() | Measure-Object -Property draws -Sum).Sum)
$result=[ordered]@{
    schema='WaW360.AztecGate8StandaloneXModelWave.v1'
    status=if($blocked){'AZTEC_GATE8_STATIC_MODEL_WAVE_STOPPED_ON_BLOCKER'}else{'AZTEC_GATE8_STATIC_MODEL_WAVE_STANDALONE_OAT_PASS'}
    sourceStaticModels=27
    sourceStaticDraws=1903
    modelOnlyOatProven=$verified.Count
    modelOnlyOatRepresentedDraws=$draws
    remainingModelNames=27-$verified.Count
    remainingRepresentedDraws=1903-$draws
    verifiedModels=@($verified.ToArray())
    blocker=$blocked
    realBodiesIntegratedInto8509RootWorldFf=0
    staticDrawGraphComplete=$false
    xboxReady=$false
    note='Each independently passing FF contains one PC-owned converted XModel. Zero new GfxStaticModelDrawInst pointers have been added to the full 8509-root map FF. No Xbox deployment.'
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host '[AZTEC-GATE8-XMODEL-WAVE] =========================================='
Write-Host "[AZTEC-GATE8-XMODEL-WAVE] status=$($result.status)"
Write-Host "[AZTEC-GATE8-XMODEL-WAVE] standalone models=$($verified.Count)/27, represented placements=$draws/1903"
Write-Host "[AZTEC-GATE8-XMODEL-WAVE] report=$report"
Write-Host '[AZTEC-GATE8-XMODEL-WAVE] Independent model-only OAT evidence, not full FF integration.'
if($blocked){throw $blocked}
