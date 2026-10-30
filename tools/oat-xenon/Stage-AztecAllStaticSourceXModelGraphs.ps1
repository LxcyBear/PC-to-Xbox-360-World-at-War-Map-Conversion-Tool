[CmdletBinding()]
param([switch]$SkipPcModelConversion)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# Source-owned real asset production work: convert actual PC-owned Aztec
# model TYPES into OAT-valid Xbox XModel bodies, then combine their verified
# graphs. A comma-prefixed XModel is an EXTERNAL REFERENCE, not a PC-owned
# mesh to fake/convert. Preserve all 27 source draw type identities.
# Two original verified XModels + N actually owned PC static model types.
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
# Historical filenames retained to avoid breaking local tooling.
# Payload count in the JSON is authoritative, NEVER the filename.
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
    if($exitCode -ne 0){throw "Aztec source-model wave script failed (exit=$exitCode). Check the FIRST error above, which may be PowerShell bookkeeping rather than a failed model conversion. Wave evidence: $wavePath. Existing five-model FF was not overwritten."}
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
   [string]$wave.status -notin @('AZTEC_GATE8_STATIC_MODEL_WAVE_STANDALONE_OAT_PASS',
       'AZTEC_GATE8_STATIC_MODEL_WAVE_UNRESOLVED_REFERENCE_IDENTITIES') -or
   [int]$wave.sourceStaticModels -ne 27 -or
   -not [bool]$wave.allSourceNamesAndDrawsAccounted -or
   [int]$wave.sourceNameTypesAccounted -ne 27 -or
   [int]$wave.modelOnlyOatProven -lt 3 -or
   ([int]$wave.modelOnlyOatProven + [int]$wave.externalNativeReferenceModels +
        [int]$wave.unresolvedReferenceModelTypes) -ne 27 -or
   ([int]$wave.modelOnlyOatRepresentedDraws + [int]$wave.externalNativeReferenceDraws +
        [int]$wave.unresolvedReferenceDraws) -ne 1903 -or
   @($wave.verifiedModels).Count -ne [int]$wave.modelOnlyOatProven -or
   @($wave.explicitlyVerifiedNativeReferences).Count -ne [int]$wave.externalNativeReferenceModels -or
   @($wave.unresolvedReferenceRows).Count -ne [int]$wave.unresolvedReferenceModelTypes -or
   [string]$five.status -ne 'AZTEC_GATE8_FIVE_SOURCE_XMODEL_GRAPHS_STAGED' -or
   [int]$five.modelCount -ne 5 -or
   @($bind5.ownedBindings).Count -ne 5 -or
   @($five.graphProofs).Count -ne 5 -or
   [string]$identity.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
   [int]$identity.assets -ne 8509 -or
   -not [bool]$identity.typeSequenceVerified -or
   [string]$caps.oatRevision -ne '3beaa7c81af517f0c05f2b4612d477ed1541b676' -or
   @($caps.capabilities) -notcontains 't4xenon-xmodel-relocatable-graph-v1'){
    throw 'All 27 original-PC static source model names must be classified as owned converted meshes or explicitly proven native external references.'
}
$expectedCorpusCount=2+[int]$wave.modelOnlyOatProven
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
        # Mirror the existing C# XModel graph resolver's exact-first
        # material identity tiers. Do NOT collapse owned '_wj' Materials
        # with distinct comma-prefixed external reference roots.
        $matWanted=([string]$mat).Replace('\','/')
        $exact=@($nativeMaterials|Where-Object {
            ([string]$_.Name).Replace('\','/') -ieq $matWanted
        })
        if($exact.Count -gt 0){$m=$exact}
        else{
            $unmarked=$matWanted.TrimStart(',')
            $near=@($nativeMaterials|Where-Object {
                ([string]$_.Name).TrimStart(',').Replace('\','/') -ieq $unmarked
            })
            if($near.Count -gt 0){$m=$near}
            else{$m=@($nativeMaterials|Where-Object {(Norm ([string]$_.Name)) -eq (Norm $matWanted)})}
        }
        if($m.Count -ne 1){
            throw "Real $name Material $mat has $($m.Count) strong native ownership candidates. Cannot link its graph."
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
    Write-Host "[AZTEC-29X] GRAFT SOURCE XMODEL $($ordinal+1)/$expectedCorpusCount : $normName root=$rootIndex"
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
for($j=3;$j -lt @($wave.verifiedModels).Count;$j++){
    $w=$wave.verifiedModels[$j]
    $key=Norm ([string]$w.model)
    if($seenName.Contains($key)){throw "Unexpected duplicate prior static XModel $key"}
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('AzG829_'+[Guid]::NewGuid().ToString('N').Substring(0,8))
    if($temp.Length -gt 115){throw "Native OAT temporary path too long: $temp"}
    New-Item -ItemType Directory -Path $temp -Force|Out-Null
    try{
        # PowerShell can hash 260+ character OneDrive paths that x86 OAT
        # cannot open. Give OAT a SHORT SHA-verified copy, never the cache.
        $cachedFF=[string]$w.ff
        $expectedSha=[string]$w.sha256
        if(-not(Test-Path -LiteralPath $cachedFF -PathType Leaf)){
            throw "Model source FF no longer exists: $cachedFF"
        }
        if((Get-FileHash -LiteralPath $cachedFF -Algorithm SHA256).Hash -ine $expectedSha){
            throw "Model source FF SHA256 mismatch: $cachedFF"
        }
        $shortFF=Join-Path $temp 'source.ff'
        Copy-Item -LiteralPath $cachedFF -Destination $shortFF -Force
        if($shortFF.Length -ge 240 -or
           (Get-FileHash -LiteralPath $shortFF -Algorithm SHA256).Hash -ine $expectedSha){
            throw "Short-path copied FF invalid for $key : $shortFF"
        }
        Write-Host "[AZTEC-29X] SHORT_OAT_INPUT model=$key cachedChars=$($cachedFF.Length) shortChars=$($shortFF.Length)"
        $log=Join-Path $work ('xmodel_{0:D3}_oat.log' -f ($j+2))
        $prior=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            & $oat '--no-color' '--include-assets' 'xmodel' '--model-format' 'gltf' '--output-folder' $temp $shortFF 2>&1 |
                ForEach-Object {[string]$_}|Set-Content -LiteralPath $log -Encoding UTF8
            $exit=[int]$LASTEXITCODE
        }finally{$ErrorActionPreference=$prior}
        # A standalone model FF has already passed OAT and is SHA-pinned.
        # This separate Unlinker call must EXIT 0 and physically emit exactly
        # one structurally valid, source-named relocatable Xenon graph.
        # OAT's console summary varies by build / stdout prefix. Never
        # classify a missing exact summary line as an XModel decoding failure.
        $logLines=@(Get-Content -LiteralPath $log)
        $errorLines=@($logLines | Where-Object {
            $_ -match '(?i)(?:^|\]\s*|\s)(?:ERROR\s*:|FATAL\s*:|fatal error\b|Unhandled exception\b)'
        })
        $summaries=@($logLines | Where-Object {
            $_ -match '(?i)Finished with\s+\d+\s+warnings?\s*,\s*\d+\s+errors?'
        })
        if($exit -ne 0 -or $errorLines.Count -gt 0){
            # Important: emit the REAL raw OAT error, not just errorLines=1.
            # Never silently ignore an OAT error when exit=0, and do NOT
            # rerun 26 already-proven PC model conversions to diagnose it.
            foreach($line in @($errorLines | Select-Object -First 10)){
                Write-Host "[AZTEC-29X] EXACT_OAT_ERROR model=$key : $line"
            }
            $graphProbe=Join-Path $temp 'xenon_xmodel_graphs'
            $graphCount=0
            if(Test-Path -LiteralPath $graphProbe -PathType Container){
                $graphCount=@(Get-ChildItem -LiteralPath $graphProbe -Filter '*.json' -File).Count
            }
            Write-Host "[AZTEC-29X] OAT_GRAPH_DIAGNOSTIC model=$key exit=$exit errors=$($errorLines.Count) actualRelocatableJsonGraphs=$graphCount"
            throw "Xenon XModel extraction has real OAT errors for $key; exit=$exit errorLines=$($errorLines.Count) physicalGraphs=$graphCount. See exact error above and $log"
        }
        if($summaries.Count -gt 1){
            throw "Ambiguous OAT terminal summary for $key; summaryCount=$($summaries.Count) log=$log"
        }
        if($summaries.Count -eq 1){
            if($summaries[0] -notmatch '(?i)Finished with\s+(\d+)\s+warnings?\s*,\s+(\d+)\s+errors?'){
                throw "Cannot parse terminal summary for $key. log=$log"
            }
            $warnings=[int]$Matches[1]
            $errors=[int]$Matches[2]
            if($warnings -ne 0 -or $errors -ne 0){
                throw "OAT returned $warnings warnings and $errors errors for $key despite exit=0. log=$log"
            }
        }else{
            Write-Host "[AZTEC-29X] OAT exit=0; no standard warning/error summary found for $key. Require actual native relocatable graph and validate its structure/ownership."
        }
        $graphFolder=Join-Path $temp 'xenon_xmodel_graphs'
        if(-not(Test-Path -LiteralPath $graphFolder -PathType Container)){
            throw "OAT exit=0 but did NOT emit Xenon XModel graph directory for $key. log=$log"
        }
        $g=@(Get-ChildItem -LiteralPath $graphFolder -Filter '*.json' -File)
        if($g.Count -ne 1){throw "OAT must emit exactly one source-owned Xenon XModel graph for $key; actual=$($g.Count); log=$log"}
        Stage-One ($j+2) $key ([int]$w.nativeRoot) $temp $g[0].FullName
    }finally{
        if(Test-Path -LiteralPath $temp -PathType Container){
            Remove-Item -LiteralPath $temp -Force -Recurse -ErrorAction SilentlyContinue
        }
    }
}
$realCount=2+[int]$wave.modelOnlyOatProven
if($rows.Count -ne $realCount -or $seenName.Count -ne $realCount -or $proofs.Count -ne $realCount){
    throw 'Source-owned verified XModel body count does not match the 27-name census minus explicitly verified native references.'
}
# Reference identities are retained in the 8509-root map by their native
# root indices, but NO fake owned XModel graph payload is generated for them.
# Do not claim they are runtime-resident: they may need stock zone resolution.
foreach($reference in @($wave.explicitlyVerifiedNativeReferences)){
    $name=[string]$reference.model
    $root=[int]$reference.nativeRoot
    # PC may spell a reference as ',viewmodel_default' while the native
    # 8509-root identity is the OWNED, UNPREFIXED 'viewmodel_default'.
    # This is valid only after we independently OAT-prove the same XModel
    # BODY in a specific Xbox stock base zone (currently common.ff).
    # Strict native-reference roots still require an EXACT comma name.
    $refClass=[string]$reference.classification
    $stockProven=$refClass -eq 'STOCK_XBOX_PRELOADED_REFERENCE_EXACT_NAME'
    $nativeProven=$refClass -eq 'XBOX_NATIVE_REFERENCE_NO_OWNED_MESH'
    $match=@($nativeModels|Where-Object {
        [int]$_.Index -eq $root -and
        ($stockProven -and (Norm ([string]$_.Name)) -eq (Norm $name) -or
         $nativeProven -and [string]$_.Name -ieq $name)
    })
    if(-not $name.StartsWith(',') -or
       (-not $nativeProven -and -not $stockProven) -or
       $match.Count -ne 1 -or
       $seenRoot.Contains($root) -or $seenName.Contains((Norm $name)) -or
       [bool]$reference.runtimeResidencyVerified){
        throw "XModel source reference $name (#$root) does not have a unique exact-native/stock-body identity proof."
    }
    if($stockProven){
        $zoneList=@($reference.stockBodyZones)
        $auditPath=[string]$reference.exactOatEvidenceManifest
        if(-not [bool]$reference.stockOatExactBodyMatch -or
           $zoneList.Count -eq 0 -or
           @($zoneList | Where-Object { $_ -notin @('common.ff','code_post_gfx.ff','patch.ff') }).Count -gt 0 -or
           -not(Test-Path -LiteralPath $auditPath -PathType Leaf)){
            throw "Stock-resolved source reference $name is missing its exact Xbox base-FF OAT proof."
        }
        # Re-check the *actual audit evidence*, not merely a boolean
        # copied into a wave report. Keep the missing patch.ff explicitly
        # unscanned; a verified common.ff body is enough for name identity.
        $auditProof=Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json
        $proofRow=@($auditProof.references | Where-Object {
            [string]$_.pcReferenceName -ieq $name -and
            @($_.nativeRootIndices).Count -eq 1 -and
            [int]$_.nativeRootIndices[0] -eq $root -and
            [bool]$_.exactNativeAliasMatch -and
            [bool]$_.exactExpectedStockBodyFound -and
            [bool]$_.foundInIndividuallyVerifiedStockBaseZone
        })
        if([string]$auditProof.schema -ne 'WaW360.AztecExpectedLoadedStockXModelReferenceAudit.v1' -or
           $proofRow.Count -ne 1){
            throw "The independently OAT-verified base-zone identity evidence for $name is absent/mismatched."
        }
        foreach($zone in $zoneList){
            $verified=@($auditProof.zones | Where-Object {
                [string]$_.zone -ieq [string]$zone -and
                [bool]$_.found -and [bool]$_.oatValid
            })
            if($verified.Count -ne 1 -or
               @($proofRow[0].expectedPreloadedStockBodyZones | Where-Object {
                   [string]$_ -ieq [string]$zone
               }).Count -ne 1){
                throw "Model reference $name has no OAT-listed real body in verified Xbox zone $zone."
            }
        }
        Write-Host "[AZTEC-29X] STOCK_REFERENCE_MATCH PC=$name native=$($match[0].Name) root=$root baseZone=$($zoneList -join ',') runtimeResidency=UNPROVEN"
    }
}
# Refused source-body synthesis: the PC FF's leading-comma reference
# may target a native root whose own identity is *unmarked* (owned) or
# different. Preserve that exact unresolved evidence with no resident
# mesh claim, while integrating ONLY actual converted source meshes.
foreach($unresolved in @($wave.unresolvedReferenceRows)){
    $name=[string]$unresolved.model
    $rootIndex=[int]$unresolved.nativeRoot
    $nativeMatch=@($nativeModels|Where-Object {[int]$_.Index -eq $rootIndex})
    if(-not $name.StartsWith(',') -or
       [string]$unresolved.classification -ne 'UNRESOLVED_PC_REFERENCE_NATIVE_IDENTITY' -or
       [bool]$unresolved.runtimeResidencyVerified -or
       $nativeMatch.Count -ne 1 -or
       (Norm ([string]$nativeMatch[0].Name)) -ne (Norm $name) -or
       $seenRoot.Contains($rootIndex) -or $seenName.Contains((Norm $name))){
        throw "Unresolved PC XModel reference '$name' native root #$rootIndex cannot be reconciled even by exact normalized identity. Do not guess at borrowed geometry."
    }
    Write-Host "[AZTEC-29X] UNRESOLVED PC reference '$name' -> Xbox native root '$($nativeMatch[0].Name)' ($rootIndex). Not converted/resident."
}
$binding=[ordered]@{
    format='WaW360.AztecGate8AllStaticXModelBinding.v3'
    bindingPass=$true
    nativeRoots=8509
    nativeXModelRoots=284
    sourceStaticModels=27
    realOwnedStaticModelTypes=[int]$wave.modelOnlyOatProven
    nativeExternalReferenceModelTypes=[int]$wave.externalNativeReferenceModels
    externalReferenceModels=@($wave.explicitlyVerifiedNativeReferences)
    unresolvedNativeReferenceModelTypes=[int]$wave.unresolvedReferenceModelTypes
    unresolvedNativeReferenceDraws=[int]$wave.unresolvedReferenceDraws
    unresolvedNativeReferenceModels=@($wave.unresolvedReferenceRows)
    sourceNamesAccounted=[int]$wave.sourceNameTypesAccounted
    staticDrawsRepresented=1903
    ownedModelDraws=[int]$wave.modelOnlyOatRepresentedDraws
    externallyReferencedDraws=[int]$wave.externalNativeReferenceDraws
    actualOwnedModelBodies=$realCount
    validationOnly=$true
    hardwareReady=$false
    ownedBindings=@($rows.ToArray())
    referenceBindings=@()
    note="Actual PC-owned XModel bodies: $realCount (2 existing plus $($wave.modelOnlyOatProven) static model types). Proven native external references: $($wave.externalNativeReferenceModels); unresolved source references: $($wave.unresolvedReferenceModelTypes), which remain native identity shells and are NOT classified or runtime-resident. All 27 names and 1903 draws accounted for, but NO world draw records are linked, no final Xbox hardware approval."
}
$binding|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $bindingPath -Encoding UTF8
$report=[ordered]@{
    schema='WaW360.AztecGate8AllStaticXModelStaging.v1'
    status=if([int]$wave.unresolvedReferenceModelTypes -gt 0){
        'AZTEC_GATE8_SOURCE_MODELS_STAGED_WITH_UNRESOLVED_REFERENCES'
    }else{
        'AZTEC_GATE8_ALL_27_PC_STATIC_XMODEL_IDENTITIES_CLASSIFIED_GRAPHS_STAGED'
    }
    sourceModels=27
    modelCount=$realCount
    realOwnedStaticModelTypes=[int]$wave.modelOnlyOatProven
    nativeExternalReferenceModelTypes=[int]$wave.externalNativeReferenceModels
    nativeExternalReferenceDraws=[int]$wave.externalNativeReferenceDraws
    nativeExternalReferences=@($wave.explicitlyVerifiedNativeReferences)
    unresolvedNativeReferenceModelTypes=[int]$wave.unresolvedReferenceModelTypes
    unresolvedNativeReferenceDraws=[int]$wave.unresolvedReferenceDraws
    unresolvedNativeReferenceModels=@($wave.unresolvedReferenceRows)
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
Write-Host "[AZTEC-29X] status=$($report.status) realOwnedModelGraphs=$realCount, nativeExternalReferences=$($wave.externalNativeReferenceModels), unresolvedReferences=$($wave.unresolvedReferenceModelTypes)"
Write-Host "[AZTEC-29X] typedSourcePlacements=1903/1903 (owned=$($wave.modelOnlyOatRepresentedDraws), nativeReference=$($wave.externalNativeReferenceDraws), unresolved=$($wave.unresolvedReferenceDraws))"
Write-Host "[AZTEC-29X] binding=$bindingPath"
Write-Host '[AZTEC-29X] Real PC model graph payloads staged; next integrate into native world FF. No Xbox claim.'