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
$stockProofPath=Join-Path $n 'pc_gate8_runtime_stock_xmodel_reference_audit\aztec_runtime_stock_xmodel_reference_audit.json'
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
    # A previous run may have moved/preserved the OAT-proven body out of
    # %TEMP%; recover ONLY when the durable SHA matches the original proof.
    $durable=Join-Path (Split-Path -Parent $path) 'original_pc_xenon_model_standalone.ff'
    if(-not(Test-Path -LiteralPath ([string]$m.standaloneXenonFF) -PathType Leaf) -and
       (Test-Path -LiteralPath $durable -PathType Leaf)){
        if((Get-FileHash -LiteralPath $durable -Algorithm SHA256).Hash -ine
            [string]$m.standaloneXenonFFSha256){
            throw "Persisted source-owned XModel body hash mismatch: $durable"
        }
        $m.standaloneXenonFF=$durable
        $m | ConvertTo-Json -Depth 9 |
            Set-Content -LiteralPath $path -Encoding UTF8
    }
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
$unresolved=New-Object 'System.Collections.Generic.List[object]'
# Optional separately OAT-verified retail stock body evidence. The stock
# audit must be run against the actual Xbox base files; never infer runtime
# residency from 8509 root names or a matching unrelated campaign FF.
$stockProof=$null
if(Test-Path -LiteralPath $stockProofPath -PathType Leaf){
    $candidate=Get-Content -LiteralPath $stockProofPath -Raw | ConvertFrom-Json
    if([string]$candidate.schema -eq 'WaW360.AztecExpectedLoadedStockXModelReferenceAudit.v1' -and
       [bool]$candidate.allExpectedLoadedZonesOatListed -and
       @($candidate.references).Count -eq [int]$candidate.targetSourceReferences -and
       @($candidate.zones).Count -eq 3 -and
       @($candidate.zones | Where-Object {-not [bool]$_.oatValid}).Count -eq 0){
        $stockProof=$candidate
        Write-Host "[AZTEC-GATE8-XMODEL-WAVE] Independent retail stock reference audit loaded: $stockProofPath"
    }else{
        Write-Host '[AZTEC-GATE8-XMODEL-WAVE] Retail stock audit incomplete; refusing its reference fallback. Owned model conversion unaffected.'
    }
}
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
        if($null -ne $stockProof){
            $stockRows=@($stockProof.references | Where-Object {
                [string]$_.pcReferenceName -ieq $name -and
                [int]@($_.nativeRootIndices).Count -eq 1 -and
                [int]$_.nativeRootIndices[0] -eq $rootIndex -and
                [bool]$_.exactNativeAliasMatch -and
                [bool]$_.exactExpectedStockBodyFound
            })
            if($stockRows.Count -eq 1){
                $bodyZones=@($stockRows[0].expectedPreloadedStockBodyZones)
                if($bodyZones.Count -gt 0 -and
                   @($bodyZones | Where-Object { $_ -notin @('common.ff','code_post_gfx.ff','patch.ff') }).Count -eq 0){
                    Write-Host "[AZTEC-GATE8-XMODEL-WAVE] PROVEN OAT STOCK BASE XMODEL BODY: $name -> $($bodyZones -join ',')"
                    return [pscustomobject]@{
                        model=$name
                        draws=[int]$row.occurrences
                        nativeRoot=$rootIndex
                        classification='STOCK_XBOX_PRELOADED_REFERENCE_EXACT_NAME'
                        stockOatExactBodyMatch=$true
                        stockBodyZones=$bodyZones
                        exactOatEvidenceManifest=$stockProofPath
                        runtimeResidencyVerified=$false
                    }
                }
            }
        }
        # The PC world can name a comma-prefixed reference while the
        # corresponding native identity is an OWNED root, an unprefixed
        # name or a different alias. Do not infer stock runtime residency
        # and do not fabricate a model body. Record all original evidence,
        # continue converting the other 26 names, and keep release blocked.
        $actualRoots=@($identity.entries | Where-Object {
            [int]$_.Index -eq $rootIndex
        } | ForEach-Object {
            [pscustomobject]@{
                index=[int]$_.Index
                type=[string]$_.OatTypeName
                name=[string]$_.Name
            }
        })
        $actualBindings=@($front.nativeXModelIdentityBindings | Where-Object {
            [int]$_.NativeRootIndex -eq $rootIndex
        } | ForEach-Object {
            [pscustomobject]@{
                nativeName=[string]$_.NativeName
                nativeReference=[bool]$_.NativeReference
                matchingPcFastfileName=[bool]$_.MatchingPcFastfileName
                pcSourceRoles=@($_.PcSourceRoles)
            }
        })
        return [pscustomobject]@{
            model=$name
            draws=[int]$row.occurrences
            nativeRoot=$rootIndex
            classification='UNRESOLVED_PC_REFERENCE_NATIVE_IDENTITY'
            runtimeResidencyVerified=$false
            exactNativeReferenceRoots=$roots.Count
            exactNativeFrontierReferenceBindings=$bindings.Count
            actualRootRows=$actualRoots
            actualFrontierBindings=$actualBindings
        }
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
        if([string]$reference.classification -eq 'UNRESOLVED_PC_REFERENCE_NATIVE_IDENTITY'){
            $unresolved.Add($reference)
            Write-Host "[AZTEC-GATE8-XMODEL-WAVE] UNRESOLVED comma source reference '$($reference.model)' native root #$($reference.nativeRoot); continuing OTHER model conversions, no fake geometry."
            foreach($rootProof in @($reference.actualRootRows)){
                Write-Host "[AZTEC-GATE8-XMODEL-WAVE]   ACTUAL ROOT: index=$($rootProof.index) type='$($rootProof.type)' name='$($rootProof.name)'"
            }
            foreach($frontProof in @($reference.actualFrontierBindings)){
                Write-Host "[AZTEC-GATE8-XMODEL-WAVE]   FRONTIER: name='$($frontProof.nativeName)' isReference=$($frontProof.nativeReference) pcNameMatch=$($frontProof.matchingPcFastfileName)"
            }
        }else{
            $referenced.Add($reference)
            Write-Host "[AZTEC-GATE8-XMODEL-WAVE] VERIFIED native external XModel reference: $($reference.model), root=$($reference.nativeRoot), draws=$($reference.draws)"
        }
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
# Windows PowerShell 5.1 + StrictMode: Measure-Object on an empty
# collection can return a result without a .Sum member. Both external
# reference buckets are legitimately empty on a valid conversion wave.
# Count directly and deterministically, treating ONLY empty buckets as 0;
# malformed or negative draw counts must remain hard failures.
function Get-ExactDrawSum([object[]]$items,[string]$label){
    [long]$sum=0
    foreach($item in $items){
        if($null -eq $item){throw "$label includes a null model row"}
        $p=$item.PSObject.Properties['draws']
        if($null -eq $p -or $null -eq $p.Value){
            throw "$label model row is missing its draw-count property"
        }
        [int]$count=0
        if(-not [int]::TryParse([string]$p.Value,[ref]$count) -or $count -lt 0){
            throw "$label model row has an invalid draw count: $($p.Value)"
        }
        $sum += [long]$count
    }
    if($sum -gt 1903){
        throw "$label exceeds the pinned original-PC 1903 static draw instances ($sum)"
    }
    return [int]$sum
}
$draws=Get-ExactDrawSum -items $verified.ToArray() -label 'PC-owned XModel bodies'
$referenceDraws=Get-ExactDrawSum -items $referenced.ToArray() -label 'Verified native reference names'
$unresolvedDraws=Get-ExactDrawSum -items $unresolved.ToArray() -label 'Unresolved PC reference names'
$coveredTypes=$verified.Count+$referenced.Count
$accountedTypes=$coveredTypes+$unresolved.Count
$fullCensus=$coveredTypes -eq 27 -and $draws+$referenceDraws -eq 1903 -and $unresolved.Count -eq 0
$allNamesAccounted=$accountedTypes -eq 27 -and
    ($draws+$referenceDraws+$unresolvedDraws -eq 1903)
$result=[ordered]@{
    schema='WaW360.AztecGate8StandaloneXModelWave.v1'
    status=if($blocked){'AZTEC_GATE8_STATIC_MODEL_WAVE_STOPPED_ON_BLOCKER'}
        elseif($unresolved.Count -gt 0){'AZTEC_GATE8_STATIC_MODEL_WAVE_UNRESOLVED_REFERENCE_IDENTITIES'}
        elseif(-not $fullCensus){'AZTEC_GATE8_STATIC_MODEL_WAVE_INCOMPLETE_CENSUS'}
        else{'AZTEC_GATE8_STATIC_MODEL_WAVE_STANDALONE_OAT_PASS'}
    sourceStaticModels=27
    sourceStaticDraws=1903
    modelOnlyOatProven=$verified.Count
    modelOnlyOatRepresentedDraws=$draws
    externalNativeReferenceModels=$referenced.Count
    externalNativeReferenceDraws=$referenceDraws
    explicitlyVerifiedNativeReferences=@($referenced.ToArray())
    unresolvedReferenceModelTypes=$unresolved.Count
    unresolvedReferenceDraws=$unresolvedDraws
    unresolvedReferenceRows=@($unresolved.ToArray())
    sourceNameTypesClassified=$coveredTypes
    sourceNameTypesAccounted=$accountedTypes
    allSourceNamesAndDrawsAccounted=$allNamesAccounted
    allStaticModelTypeIdentitiesClassified=$fullCensus
    remainingModelNames=27-$accountedTypes
    remainingRepresentedDraws=1903-$draws-$referenceDraws-$unresolvedDraws
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
Write-Host "[AZTEC-GATE8-XMODEL-WAVE] OWNED real model bodies=$($verified.Count); verified external refs=$($referenced.Count); unresolved reference names=$($unresolved.Count)"
Write-Host "[AZTEC-GATE8-XMODEL-WAVE] original-PC placements accounted=$($draws+$referenceDraws+$unresolvedDraws)/1903 (mesh=$draws, verified reference=$referenceDraws, unresolved=$unresolvedDraws) names=$accountedTypes/27"
if($unresolved.Count -gt 0){
    Write-Host '[AZTEC-GATE8-XMODEL-WAVE] UNRESOLVED references are NOT real XModel bodies, NOT native residency proofs, and NOT Xbox release approvals.'
}
Write-Host "[AZTEC-GATE8-XMODEL-WAVE] report=$report"
Write-Host '[AZTEC-GATE8-XMODEL-WAVE] Independent model-only OAT evidence, not full FF integration.'
if($blocked){throw $blocked}
