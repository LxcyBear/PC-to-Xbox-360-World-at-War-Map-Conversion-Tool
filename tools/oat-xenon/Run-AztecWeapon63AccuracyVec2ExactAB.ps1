[CmdletBinding()]
param([switch]$ReuseArchivedPasses)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Exact source-revision OAT A/B for the NEXT genuinely inline non-string
# WeaponDef children. A: 63 source scalar + real fixed inline XStrings.
# B: identical world and 63 weapons, plus 96 original-PC accuracy vec2
# arrays (612 knots, 4896 B). Never graft packed aliases/foreign pointers.
# Does not mutate the SHA-protected OAT-good baseline or release files.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$source=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping\pc_g11_lightning_weapon_69_unified_native_source\aztec_69_weapon_unified_original_pc_xbox_body_selections.json'
$size=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping\pc_g11_native_ff_size_audit\aztec_native_ff_size_and_source_ledger.json'
$vec2Proof=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping\pc_g11_lightning_weapon63_accuracy_vec2\aztec_63_original_pc_owned_accuracy_vec2_source_witness.json'
$runner=Join-Path $PSScriptRoot 'Run-AztecWeapon63FfGrowthCanary.ps1'
$archive=Join-Path $repo '.deps\aztec-w63-vec2-exact-ab'
$report=Join-Path $archive 'aztec_weapon63_accuracy_vec2_exact_ab.json'
$baseWork=Join-Path $repo '.deps\aztec-w63-xstr-ff-growth'
$extraWork=Join-Path $repo '.deps\aztec-w63-vec2-ff-growth'

foreach($path in @($source,$size,$vec2Proof,$runner)){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Required original-PC source proof or runner missing: $path"
    }
}
$m=Get-Content -LiteralPath $source -Raw|ConvertFrom-Json
$s=Get-Content -LiteralPath $size -Raw|ConvertFrom-Json
$v=Get-Content -LiteralPath $vec2Proof -Raw|ConvertFrom-Json
if([string]$m.schema -ne 'WaW360.AztecWeapon69UnifiedOriginalPcXboxBodySelection.v1' -or
   [string]$m.status -ne 'AZTEC_69_WEAPON_NATIVE_ROOTS_63_AUTHENTIC_PC_XBOX_BODIES_2_CONFLICT_4_STOCK_UNPROVEN_NOT_IN_FF' -or
   [int]$m.nativeWeaponDefRoots -ne 69 -or
   [int]$m.uniqueSourceOwnedXboxWeaponBodyMappings -ne 63 -or
   [long]$m.selectedUncompressedXboxOwnedPayloadBytes -ne 179775 -or
   [int]$m.conflictingDifferentPcSourceBodiesStillFenced -ne 2 -or
   [int]$m.offlineStockOnlyWeaponNamesNotRuntimeProven -ne 4 -or
   [bool]$m.xboxRuntimeReady -or
   [string]$s.schema -ne 'WaW360.AztecNativeFfSizeAndSourceCoverage.v1' -or
   [string]$s.converted.sha256 -ine 'F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6' -or
   [string]$v.schema -ne 'WaW360.Aztec63WeaponOriginalPcAccuracyVec2OwnedSourceWitness.v1' -or
   [string]$v.status -ne 'AZTEC_63_WEAPON_ORIGINAL_PC_INLINE_VEC2_CHILD_GRAPH_SOURCE_PROVEN_NOT_IN_FF' -or
   [int]$v.provenInlineSourceOwnedVec2ArrayRecords -ne 96 -or
   [int]$v.provenInlineSourceOwnedVec2Knots -ne 612 -or
   [long]$v.provenInlineBigEndianXenonOwnedBytes -ne 4896 -or
   [int]$v.weaponRootsWithRealInlineVec2Children -ne 49 -or
   [int]$v.packedSourceArrayPointersQuarantined -ne 96 -or
   [int]$v.nullSourceArrayPointers -ne 60 -or
   -not [bool]$v.sourcePcWeaponBodySourceProofHashesAllRevalidated -or
   -not [bool]$v.noRetailOrOracleDonorBytes -or
   [bool]$v.xboxRuntimeGameplayReady){
    throw 'WeaponDef source identity, genuine inline vec2 bytes or protected baseline proof drifted.'
}
$protected=[string]$s.converted.path
if(-not(Test-Path -LiteralPath $protected -PathType Leaf)){
    throw "Protected OAT-good native FF missing: $protected"
}
$protectedBytes=[long](Get-Item -LiteralPath $protected).Length
$protectedHash=(Get-FileHash -LiteralPath $protected -Algorithm SHA256).Hash
if($protectedBytes -ne 4510695L -or $protectedHash -ine [string]$s.converted.sha256){
    throw 'Protected original native FF changed; refusing A/B.'
}
$inputPaths=@($source,$size,$vec2Proof)
$inputHashes=@{}
foreach($path in $inputPaths){
    $inputHashes[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
$codePaths=@(
    'src\WaW360.Core\FastFiles\T4WeaponVariableSerializerProof.cs',
    'src\WaW360.Core\FastFiles\T4XenonAztecWeapon63ScalarCanary.cs',
    'src\WaW360.Core\FastFiles\T4XenonGraphBlockLayoutPlanner.cs',
    'src\WaW360.Core\FastFiles\T4XenonRendererGraphEmitter.cs',
    'tools\oat-xenon\Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1',
    'tools\oat-xenon\Run-AztecWeapon63FfGrowthCanary.ps1',
    'tools\oat-xenon\Run-AztecWeapon63AccuracyVec2ExactAB.ps1'
)
$codeHashes=@{}
foreach($relative in $codePaths){
    $path=Join-Path $repo $relative
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Exact A/B tracked writer source missing: $path"
    }
    $codeHashes[$relative]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
$revision=(& git -C $repo rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($revision)){
    throw 'Git revision unavailable; refusing uncontrolled original-PC FF A/B.'
}
New-Item -Path $archive -ItemType Directory -Force|Out-Null
Write-Host '[AZTEC-W63-VEC2-AB] ============================================================='
Write-Host '[AZTEC-W63-VEC2-AB] SAME-WORLD, SAME-REVISION ORIGINAL-PC INLINE VEC2 FF A/B'
Write-Host "[AZTEC-W63-VEC2-AB] Source revision=$revision"
Write-Host "[AZTEC-W63-VEC2-AB] Protected OAT-good FF SHA=$protectedHash"
Write-Host "[AZTEC-W63-VEC2-AB] Mode=$(if($ReuseArchivedPasses){'READ ARCHIVES'}else{'FRESH TWO OAT BUILDS'})"
Write-Host '[AZTEC-W63-VEC2-AB-PROGRESS] [####----------------] 20% | immutable source/FF/code SHA preflight'
Write-Host '[AZTEC-W63-VEC2-AB] Diagnostic scratch only. Never install these FFs on Xbox.'

function Assert-Unchanged {
    if(-not(Test-Path -LiteralPath $protected -PathType Leaf) -or
       [long](Get-Item -LiteralPath $protected).Length -ne $protectedBytes -or
       (Get-FileHash -LiteralPath $protected -Algorithm SHA256).Hash -ine $protectedHash){
        throw 'Protected original FF changed during experiment.'
    }
    foreach($path in $inputPaths){
        if(-not(Test-Path -LiteralPath $path -PathType Leaf) -or
           (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine [string]$inputHashes[$path]){
            throw "A/B source witness or ledger changed: $path"
        }
    }
    $current=(& git -C $repo rev-parse HEAD).Trim()
    if($LASTEXITCODE -ne 0 -or $current -ine $revision){
        throw 'Git HEAD changed during native FF A/B.'
    }
    foreach($relative in $codePaths){
        $path=Join-Path $repo $relative
        if(-not(Test-Path -LiteralPath $path -PathType Leaf) -or
           (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine [string]$codeHashes[$relative]){
            throw "Native FF writer changed during A/B: $relative"
        }
    }
}
function Preserve-ExistingScratch([string]$work,[string]$label,[string]$expected){
    $ff=Join-Path $work 'scratch_emit\nazi_zombie_aztec.ff'
    $json=Join-Path $work 'aztec_weapon63_ff_growth_63.json'
    $emit=Join-Path $work 'scratch_emit\nuclear_xenon_renderer_emit.json'
    $exists=@(Test-Path -LiteralPath $ff -PathType Leaf,Test-Path -LiteralPath $json -PathType Leaf)
    # Missing pair is not necessarily an error; the runner has its own
    # dedicated scratch location. An incomplete pair is an error.
    $hasFf=Test-Path -LiteralPath $ff -PathType Leaf
    $hasJson=Test-Path -LiteralPath $json -PathType Leaf
    if(-not $hasFf -and -not $hasJson){return}
    if(-not $hasFf -or -not $hasJson -or -not(Test-Path -LiteralPath $emit -PathType Leaf)){
        throw "Existing $label scratch has incomplete provenance; refusing to overwrite."
    }
    $r=Get-Content -LiteralPath $json -Raw|ConvertFrom-Json
    $e=Get-Content -LiteralPath $emit -Raw|ConvertFrom-Json
    $sha=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
    if([string]$r.status -ne $expected -or [string]$r.newScratchFastFileSha256 -ine $sha -or
       -not [bool]$e.ValidationPass -or -not [bool]$e.OatZeroWarningsZeroErrors -or
       [int]$e.NativeRoots -ne 8509){
        throw "Existing $label scratch is not proven OAT-good. Refusing overwrite."
    }
    $where=Join-Path $archive ('previous_'+$label+'_'+$sha.Substring(0,12))
    New-Item -Path $where -ItemType Directory -Force|Out-Null
    foreach($pair in @(
        @{from=$ff;to='nazi_zombie_aztec.ff'},
        @{from=$json;to='prior_growth_report.json'},
        @{from=$emit;to='prior_emit_report.json'})){
        $destination=Join-Path $where $pair.to
        if(Test-Path -LiteralPath $destination -PathType Leaf){
            if((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ine
               (Get-FileHash -LiteralPath $pair.from -Algorithm SHA256).Hash){
                throw "Prior A/B archive unexpectedly differs: $destination"
            }
        }else{
            Copy-Item -LiteralPath $pair.from -Destination $destination
        }
    }
    Write-Host "[AZTEC-W63-VEC2-AB] Preserved previous $label SHA=$sha"
}
if(-not $ReuseArchivedPasses){
    Preserve-ExistingScratch $baseWork 'strings_63' 'AZTEC_W63_EXTENDED_ORIGINAL_PC_INLINE_XSTRINGS_NATIVE_FF_OAT_PASS_NOT_FULL_WEAPON_GRAPH'
    Preserve-ExistingScratch $extraWork 'strings_and_vec2_63' 'AZTEC_W63_96_ORIGINAL_PC_ACCURACY_VEC2_OWNED_CHILDREN_NATIVE_FF_OAT_PASS_NOT_FULL_GRAPH'
}
function Invoke-Case([string]$label,[bool]$includeVec2,[string]$work){
    Assert-Unchanged
    $dir=Join-Path $archive $label
    if($ReuseArchivedPasses){
        $ff=Join-Path $dir 'nazi_zombie_aztec.ff'
        $json=Join-Path $dir 'case_growth_report.json'
        $emit=Join-Path $dir 'nuclear_xenon_renderer_emit.json'
    }else{
        Write-Host "[AZTEC-W63-VEC2-AB] BUILD $label - original PC inline accuracy arrays=$includeVec2"
        $args=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$runner,
                '-Limit','63','-ExpandedInlineXStrings')
        if($includeVec2){$args+='-OwnedAccuracyVec2'}
        & powershell.exe @args | Out-Host
        if([int]$LASTEXITCODE -ne 0){
            throw "Native FF source/OAT build $label failed; archived previous successes remain preserved."
        }
        $ff=Join-Path $work 'scratch_emit\nazi_zombie_aztec.ff'
        $json=Join-Path $work 'aztec_weapon63_ff_growth_63.json'
        $emit=Join-Path $work 'scratch_emit\nuclear_xenon_renderer_emit.json'
    }
    foreach($path in @($ff,$json,$emit)){
        if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
            throw "Case $label did not produce a complete OAT+SHA archive: $path"
        }
    }
    $r=Get-Content -LiteralPath $json -Raw|ConvertFrom-Json
    $e=Get-Content -LiteralPath $emit -Raw|ConvertFrom-Json
    $hash=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
    $bytes=[long](Get-Item -LiteralPath $ff).Length
    $expected=if($includeVec2){
        'AZTEC_W63_96_ORIGINAL_PC_ACCURACY_VEC2_OWNED_CHILDREN_NATIVE_FF_OAT_PASS_NOT_FULL_GRAPH'
    }else{
        'AZTEC_W63_EXTENDED_ORIGINAL_PC_INLINE_XSTRINGS_NATIVE_FF_OAT_PASS_NOT_FULL_WEAPON_GRAPH'
    }
    $expectedArrays=if($includeVec2){96}else{0}
    $expectedKnots=if($includeVec2){612}else{0}
    $expectedPayload=if($includeVec2){4896L}else{0L}
    if([string]$r.schema -ne 'WaW360.AztecWeapon63RealSourceScalarXstringFfGrowthCanary.v1' -or
       [string]$r.status -ne $expected -or [int]$r.parentRunnerExitCode -ne 0 -or
       [int]$r.requestedCanaries -ne 63 -or
       [int]$r.nativeWeaponRootScalarXstringCanariesEmitted -ne 63 -or
       -not [bool]$r.extendedOriginalPcFixedInlineXStringsMode -or
       [int]$r.extendedSourceFixedInlineStringFieldsEmitted -ne 207 -or
       [bool]$r.originalPcAccuracyVec2OwnedChildMode -ne $includeVec2 -or
       [int]$r.originalPcAccuracyVec2OwnedArraysEmitted -ne $expectedArrays -or
       [int]$r.originalPcAccuracyVec2OwnedKnotsEmitted -ne $expectedKnots -or
       [long]$r.originalPcAccuracyVec2OwnedPayloadBytesEmitted -ne $expectedPayload -or
       [int]$e.OriginalPcWeaponDiagnosticRoots -ne 63 -or
       [int]$e.OriginalPcWeaponExpandedInlineXStringFields -ne 207 -or
       [int]$e.OriginalPcWeaponOwnedAccuracyVec2Arrays -ne $expectedArrays -or
       [int]$e.OriginalPcWeaponOwnedAccuracyVec2Knots -ne $expectedKnots -or
       [long]$e.OriginalPcWeaponOwnedAccuracyVec2Bytes -ne $expectedPayload -or
       [long]$e.OriginalPcWeaponSourceProofBytes -ne 179775 -or
       [bool]$e.OriginalPcWeaponFullOwnedGraphsEmitted -or
       [bool]$r.fullWeaponDefChildrenAndDependenciesEmitted -or
       [bool]$r.outputNativeXboxHardwareReady -or
       [bool]$r.userMapDeployable -or [bool]$r.gate11Closed -or
       [int]$e.NativeRoots -ne 8509 -or
       -not [bool]$e.LayoutReplayExact -or
       -not [bool]$e.ExactIdentitySequence -or
       -not [bool]$e.OatZeroWarningsZeroErrors -or
       -not [bool]$e.ValidationPass -or
       -not [bool]$e.ValidationOnly -or
       [bool]$e.ReadyForHardware -or
       [long]$r.newScratchFastFileBytes -ne $bytes -or
       [string]$r.newScratchFastFileSha256 -ine $hash -or
       [string]$r.protectedPriorSha256 -ine $protectedHash -or
       [long]$r.protectedPriorNativeFastFileBytes -ne $protectedBytes){
        throw "Case $label failed original-PC source/OAT/root/hash checks; no size attribution allowed."
    }
    if(-not $ReuseArchivedPasses){
        New-Item -Path $dir -ItemType Directory -Force|Out-Null
        foreach($pair in @(
            @{from=$ff;to='nazi_zombie_aztec.ff'},
            @{from=$json;to='case_growth_report.json'},
            @{from=$emit;to='nuclear_xenon_renderer_emit.json'})){
            $destination=Join-Path $dir $pair.to
            if(Test-Path -LiteralPath $destination -PathType Leaf){
                if((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ine
                   (Get-FileHash -LiteralPath $pair.from -Algorithm SHA256).Hash){
                    throw "Previous A/B archive differs for $label. Refusing overwrite."
                }
            }else{Copy-Item -LiteralPath $pair.from -Destination $destination}
        }
    }
    $saved=Join-Path $dir 'nazi_zombie_aztec.ff'
    if((Get-FileHash -LiteralPath $saved -Algorithm SHA256).Hash -ine $hash){
        throw "Saved native FF $label hash differs from OAT-validated scratch."
    }
    Write-Host "[AZTEC-W63-VEC2-AB] $label OAT PASS, 8509 roots, 0/0 errors; FF=$bytes B, SHA=$hash"
    return [pscustomobject]@{
        label=$label
        genuineInlineVec2Enabled=$includeVec2
        sourceOwnedWeaponRoots=63
        extraInlineXStringFields=207
        sourceOwnedAccuracyVec2Arrays=$expectedArrays
        sourceOwnedAccuracyVec2Knots=$expectedKnots
        sourceOwnedAccuracyVec2Bytes=$expectedPayload
        compressedFfBytes=$bytes
        compressedFfSha256=$hash
        archivedFf=$saved
        diagnosticWeaponSerializedBytes=[long]$e.OriginalPcWeaponDiagnosticSerializedBytes
        wholeZoneBytes=[long]$e.ZoneBytes
        realMaterialRoots=[int]$e.RealMaterialRoots
        realXModelRoots=[int]$e.RealXModelRoots
        realImageRoots=[int]$e.RealImageRoots
        realRawFileRoots=[int]$e.RealRawFileRoots
        gate10Draws=[int]$e.OriginalPcGfxWorldGate10DrawRecords
        gate11Cells=[int]$e.OriginalPcGfxWorldGate11Cells
        gate11Trees=[int]$e.OriginalPcGfxWorldGate11AabbTrees
        gate11Indexes=[int]$e.OriginalPcGfxWorldGate11SmodelIndexes
        physicalZoneBytes=[long]$e.PhysicalBytes
        readyForXboxHardware=$false
    }
}
$base=Invoke-Case 'same_world_strings_63' $false $baseWork
Write-Host '[AZTEC-W63-VEC2-AB-PROGRESS] [##########----------] 50% | strings-only source FF passed OAT and archived'
$extra=Invoke-Case 'same_world_strings_plus_vec2_63' $true $extraWork
Write-Host '[AZTEC-W63-VEC2-AB-PROGRESS] [#################---] 85% | 96 Vec2 arrays source FF passed OAT and archived'
Assert-Unchanged
if($null -eq $base -or $null -eq $extra -or
   $base -isnot [pscustomobject] -or $extra -isnot [pscustomobject]){
    throw 'A/B output stream not a single case object each; rejecting corrupted result.'
}
foreach($key in @('sourceOwnedWeaponRoots','extraInlineXStringFields','realMaterialRoots',
                  'realXModelRoots','realImageRoots','realRawFileRoots','gate10Draws',
                  'gate11Cells','gate11Trees','gate11Indexes','physicalZoneBytes')){
    if($base.PSObject.Properties[$key].Value -ne $extra.PSObject.Properties[$key].Value){
        throw "A/B non-Vec2 map property '$key' changed; cannot attribute compressed bytes."
    }
}
$actualOwnedBytes=[long]$extra.diagnosticWeaponSerializedBytes-$base.diagnosticWeaponSerializedBytes
$zoneDelta=[long]$extra.wholeZoneBytes-$base.wholeZoneBytes
$compressedDelta=[long]$extra.compressedFfBytes-$base.compressedFfBytes
if($actualOwnedBytes -ne 4896L -or $zoneDelta -lt 4896L){
    throw "Source child byte count not exact (weapon=$actualOwnedBytes, native zone=$zoneDelta). No A/B size claim."
}
$status=if($compressedDelta -gt 0){
    'AZTEC_W63_96_PC_ACCURACY_VEC2_EXACT_FF_GROWTH_OAT_PASS'
}elseif($compressedDelta -eq 0){
    'AZTEC_W63_96_PC_ACCURACY_VEC2_EXACT_FF_EQUAL_SIZE_OAT_PASS'
}else{
    'AZTEC_W63_96_PC_ACCURACY_VEC2_EXACT_FF_SHRINK_OAT_PASS'
}
$result=[ordered]@{
    schema='WaW360.AztecWeapon63AccuracyVec2SameWorldExactAB.v1'
    status=$status
    freshTwoCasesBuiltUnderSameRevision=(-not $ReuseArchivedPasses)
    archivedReadbackOnly=[bool]$ReuseArchivedPasses
    sourceRevisionForCurrentRun=$revision
    readbackCannotProveOriginalArchivedBuildRevision=[bool]$ReuseArchivedPasses
    trackedWriterFileSha256=$codeHashes
    inputSourceSha256=$inputHashes
    originalProtectedOatGoodFfSha256=$protectedHash
    originalProtectedOatGoodFfBytes=$protectedBytes
    stringsOnlySourceFF=$base
    stringsPlusOriginalPcOwnedVec2FF=$extra
    originalPcInlineAccuracyArrays=96
    originalPcInlineAccuracyKnots=612
    exactOriginalPcOwnedVec2SerializedBytes=$actualOwnedBytes
    nativeUncompressedZoneDifference=$zoneDelta
    nativeCompressedFastFileDifferenceBytes=$compressedDelta
    allNonVec2WorldCountsSame=$true
    bothOatLoaded8509RootsZeroErrorsWarnings=$true
    originalPcPackedAccuracyPointersRemainQuarantined=96
    originalPcNullAccuracyPointerFieldsRemainNull=60
    twoCompetingOriginalPcWeaponBodiesStillFenced=$true
    fourStockOnlyWeaponRootsNotRuntimeProven=$true
    missingSixImagePixelBodiesStillBlocked=$true
    full179775ByteWeaponChildGraphNotInRuntimeFf=$true
    xboxHardwareTestingApproved=$false
    gate11Closed=$false
    caveat=if($ReuseArchivedPasses){
        'Archived SHA/OAT evidence checked, but original Git build revision is not cryptographically attested by archived records. Only fresh two-case runs prove same-revision exact size attribution.'
    }else{
        'Two native scratch FFs freshly emitted with pinned original-PC source and exact same tracked writer bytes. Only 96 authentic inline accuracy vec2 child arrays differ. 7b foreign child graphs, images, lighting semantics, stock model and runtime are incomplete.'
    }
}
$result|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $report -Encoding UTF8
Write-Host '[AZTEC-W63-VEC2-AB-PROGRESS] [####################] 100% | both native FFs exact SHA/OAT checked; original-PC child-byte growth isolated'
Write-Host '[AZTEC-W63-VEC2-AB] ============================================================='
Write-Host "[AZTEC-W63-VEC2-AB] STRINGS-ONLY 63 FF = $($base.compressedFfBytes) B"
Write-Host "[AZTEC-W63-VEC2-AB] STRINGS + 96 ORIGINAL-PC VEC2 FF = $($extra.compressedFfBytes) B"
Write-Host "[AZTEC-W63-VEC2-AB] EXACT NEW ORIGINAL-PC OWNED VEC2 BYTES = $actualOwnedBytes B"
Write-Host "[AZTEC-W63-VEC2-AB] NATIVE ZONE DELTA = $zoneDelta B"
Write-Host "[AZTEC-W63-VEC2-AB] COMPRESSED FF DELTA = $compressedDelta B"
Write-Host "[AZTEC-W63-VEC2-AB] STATUS=$status"
Write-Host "[AZTEC-W63-VEC2-AB] JSON=$report"
Write-Host '[AZTEC-W63-VEC2-AB] OAT-good diagnostics only; full WeaponDef/world/image/runtime closure remains blocked.'
