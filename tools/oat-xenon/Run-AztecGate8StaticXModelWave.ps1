[CmdletBinding()]
param([ValidateRange(1,26)][int]$MaxAdditionalModels=2)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# Gate 8 priority wave: model-only OAT canaries. Never touches world FF.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$n=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$auditPath=Join-Path $n 'pc_gate8_static_model_dependency_audit\aztec_gate8_static_draw_xmodel_dependencies.json'
$firstPath=Join-Path $n 'pc_gate8_first_static_xmodel\aztec_first_static_draw_xmodel_canary.json'
$identityPath=Join-Path $n 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$frontPath=Join-Path $n 'pc_model_world_source_frontier\aztec_pc_model_world_source_frontier.json'
$script=Join-Path $PSScriptRoot 'Run-AztecGate8FirstStaticDrawXModelCanary.ps1'
$out=Join-Path $n 'pc_gate8_static_model_wave'
$report=Join-Path $out 'aztec_gate8_static_model_wave.json'
foreach($p in @($auditPath,$firstPath,$identityPath,$frontPath,$script)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing Gate8 priority-model prerequisite: $p"}
}
$audit=Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json
$identity=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$front=Get-Content -LiteralPath $frontPath -Raw | ConvertFrom-Json
if([string]$audit.schema -ne 'WaW360.AztecGate8StaticDrawModelDependencyAudit.v1' -or
   [string]$audit.status -ne 'AZTEC_GATE8_1903_STATIC_DRAWS_27_NAMES_NATIVE_ROOT_RECONCILIATION_COMPLETE_DIAGNOSTIC' -or
   [int]$audit.sourceDrawInstances -ne 1903 -or
   [int]$audit.distinctSourceStaticModelNames -ne 27 -or
   [int]$audit.distinctModelsWithoutProvenRealGraph -ne 27 -or
   [bool]$audit.hardwareReady){
    throw 'Static draw source-name/XModel dependency audit changed.'
}
if([string]$identity.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
   [int]$identity.assets -ne 8509 -or -not [bool]$identity.typeSequenceVerified -or
   [string]$front.schema -ne 'WaW360.AztecPcModelWorldSourceFrontier.v1' -or
   [string]$front.status -ne 'AZTEC_PC_MODEL_WORLD_SOURCE_FRONTIER_PASS' -or
   [int]$front.nativeXModelRoots -ne 284){
    throw 'Original-PC 8509-root model-reference/native identity evidence changed.'
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
    # Permanently cache the already OAT-proven model FF in the per-model
    # output directory. Older wave results used %TEMP% and could disappear
    # between Windows sessions even though the conversion had PASSED.
    $source=[string]$m.standaloneXenonFF
    $durable=Join-Path (Split-Path -Parent $path) 'original_pc_xenon_model_standalone.ff'
    if([IO.Path]::GetFullPath($source) -ine [IO.Path]::GetFullPath($durable)){
        if(Test-Path -LiteralPath $durable -PathType Leaf){
            if((Get-FileHash -LiteralPath $durable -Algorithm SHA256).Hash -ine
                [string]$m.standaloneXenonFFSha256){
                throw "Cached OAT model FF SHA changed: $durable"
            }
        }else{
            Copy-Item -LiteralPath $source -Destination $durable -Force
            if((Get-FileHash -LiteralPath $durable -Algorithm SHA256).Hash -ine
                [string]$m.standaloneXenonFFSha256){
                throw "Caching source-derived OAT model FF changed its bytes: $durable"
            }
        }
        $m.standaloneXenonFF=$durable
        $m | ConvertTo-Json -Depth 9 |
            Set-Content -LiteralPath $path -Encoding UTF8
        Write-Host "[AZTEC-GATE8-XMODEL-WAVE] PRESERVED actual source-owned OAT FF at $durable"
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
$referenced=New-Object 'System.Collections.Generic.List[object]'
function Resolve-ExplicitReference($row){
    $name=[string]$row.pcModelName
    if(-not $name.StartsWith(',',[StringComparison]::Ordinal)){return $null}
    if(@($row.nativeRootIndices).Count -ne 1){
        throw "Comma-prefixed XModel must have exactly one matching native root: $name"
    }
    $rootIndex=[int]$row.nativeRootIndices[0]
    $roots=@($identity.entries | Where-Object {
        [int]$_.Index -eq $rootIndex -and
        [string]$_.OatTypeName -ieq 'xmodel' -and
        [string]$_.Name -ieq $name
    })
    $bindings=@($front.nativeXModelIdentityBindings | Where-Object {
        [int]$_.NativeRootIndex -eq $rootIndex -and
        [string]$_.NativeName -ieq $name -and
        [bool]$_.NativeReference
    })
    if($roots.Count -ne 1 -or $bindings.Count -ne 1){
        throw "XModel $name looks like an external reference, but native identity/provenance does NOT prove one. Refuse to convert or silently skip it."
    }
    return [pscustomobject]@{
        model=$name
        draws=[int]$row.occurrences
        nativeRoot=$rootIndex
        classification='XBOX_NATIVE_REFERENCE_NO_OWNED_MESH'
        runtimeResidencyVerified=$false
    }
}
foreach($row in @($rows | Select-Object -Skip 1)){
    if($converted -ge $MaxAdditionalModels){break}
    if([string]$row.status -ne 'NATIVE_IDENTITY_ONLY_NO_REAL_GRAPH' -or
       @($row.nativeRootIndices).Count -ne 1){
        throw "Source/native XModel mapping drifted for $($row.pcModelName)."
    }
    $reference=Resolve-ExplicitReference $row
    if($null -ne $reference){
        $referenced.Add($reference)
        Write-Host "[AZTEC-GATE8-XMODEL-WAVE] EXTERNAL XMODEL REFERENCE (not a PC-owned mesh): $($reference.model), root=$($reference.nativeRoot), draws=$($reference.draws)"
        continue
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
$referenceDraws=[int](($referenced.ToArray() | Measure-Object -Property draws -Sum).Sum)
$coveredTypes=$verified.Count+$referenced.Count
$fullCensus=$coveredTypes -eq 27 -and $draws+$referenceDraws -eq 1903
$result=[ordered]@{
    schema='WaW360.AztecGate8StandaloneXModelWave.v1'
    status=if($blocked){'AZTEC_GATE8_STATIC_MODEL_WAVE_STOPPED_ON_BLOCKER'}
        else{'AZTEC_GATE8_STATIC_MODEL_WAVE_STANDALONE_OAT_PASS'}
    sourceStaticModels=27
    sourceStaticDraws=1903
    modelOnlyOatProven=$verified.Count
    modelOnlyOatRepresentedDraws=$draws
    externalNativeReferenceModels=$referenced.Count
    externalNativeReferenceDraws=$referenceDraws
    explicitlyVerifiedNativeReferences=@($referenced.ToArray())
    sourceNameTypesClassified=$coveredTypes
    allStaticModelTypeIdentitiesClassified=$fullCensus
    remainingModelNames=27-$coveredTypes
    remainingRepresentedDraws=1903-$draws-$referenceDraws
    verifiedModels=@($verified.ToArray())
    blocker=$blocked
    realBodiesIntegratedInto8509RootWorldFf=0
    staticDrawGraphComplete=$false
    xboxReady=$false
    note='Each verifiedModels row owns and OAT-proves actual source PC model geometry. Comma-prefixed explicitlyVerifiedNativeReferences rows are native reference identities, NOT owned/converted mesh bodies; Xbox runtime residency is unproven. Exact full 27-name/1903-draw source census is tracked separately. Zero static draw pointers added to main map FF. No Xbox release.'
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host '[AZTEC-GATE8-XMODEL-WAVE] =========================================='
Write-Host "[AZTEC-GATE8-XMODEL-WAVE] status=$($result.status)"
Write-Host "[AZTEC-GATE8-XMODEL-WAVE] OWNED real model bodies=$($verified.Count); externally referenced model names=$($referenced.Count)"
Write-Host "[AZTEC-GATE8-XMODEL-WAVE] original-PC placements classified=$($draws+$referenceDraws)/1903 (mesh=$draws, reference=$referenceDraws) distinct names=$coveredTypes/27"
Write-Host "[AZTEC-GATE8-XMODEL-WAVE] report=$report"
Write-Host '[AZTEC-GATE8-XMODEL-WAVE] Independent model-only OAT evidence, not full FF integration.'
if($blocked){throw $blocked}
