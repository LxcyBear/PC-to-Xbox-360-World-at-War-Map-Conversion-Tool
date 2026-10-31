[CmdletBinding()]
param(
    [string]$OutputDirectory=''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Gate8G: original-PC vs original-Xbox static draw scalar ABI audit.
# NO FF writes, NO copy of native body/light data, NO bytes staged for Xbox.
# The Xbox reference is a read-only semantic/orientation oracle ONLY.
# If row identities/geometry differ, do not assert ordinal correspondence.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$pcProofPath=Join-Path $base 'pc_gate8_static_draw_source\aztec_gate8_pc_static_draw_source_report.json'
$axisProofPath=Join-Path $base 'pc_gate8_static_draw_xenon_axis_preflight_28_model_world\aztec_1903_xenon_axis_pack_preflight.json'
$nativeProofPath=Join-Path $base 'xenon_native_gate8_static_draw_60b_abi_witness\aztec_native_1903_static_draw_abi_witness_report.json'
$ptrProofPath=Join-Path $base 'pc_gate8_shared_codec_regression'
$ptrPlanPath=Join-Path $base 'pc_gate8_1903_draw_relocation_plan\aztec_1903_model_pointer_relocation_summary.json'
if([string]::IsNullOrWhiteSpace($OutputDirectory)){
    $OutputDirectory=Join-Path $base 'pc_gate8_static_draw_scalar_equivalence'
}
function Need([string]$p,[string]$name){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Missing $name : $p"
    }
    return (Resolve-Path -LiteralPath $p).Path
}
function Sha([string]$p){return [string](Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash}
function Canon([string]$s){
    $s=$s.Trim().Replace('\','/').TrimStart(',').ToLowerInvariant()
    if($s.EndsWith('_wj',[StringComparison]::Ordinal)){$s=$s.Substring(0,$s.Length-3)}
    return $s
}
function EqualNum($a,$b,[double]$tol){
    $x=[double]$a
    $y=[double]$b
    return (-not [double]::IsNaN($x) -and -not [double]::IsInfinity($x) -and
            -not [double]::IsNaN($y) -and -not [double]::IsInfinity($y) -and
            [math]::Abs($x-$y) -le $tol)
}
function Field([object]$obj,[string]$name){
    if($null -eq $obj.PSObject.Properties[$name]){
        throw "Required original-PC/Xbox loaded scalar '$name' is unavailable. Need an OAT exporter update, not a guess."
    }
    return $obj.PSObject.Properties[$name].Value
}
$pcProof=Get-Content -LiteralPath (Need $pcProofPath 'typed original PC 1903 draws') -Raw|ConvertFrom-Json
$axProof=Get-Content -LiteralPath (Need $axisProofPath 'verified PC Xenon packed axes') -Raw|ConvertFrom-Json
$nativeProof=Get-Content -LiteralPath (Need $nativeProofPath 'original Xbox 60B ABI witness') -Raw|ConvertFrom-Json
$pointerPlan=Get-Content -LiteralPath (Need $ptrPlanPath '1903 per-draw root provenance') -Raw|ConvertFrom-Json
if([string]$pcProof.status -ne 'AZTEC_GATE8_1903_PC_TYPED_STATIC_DRAWS_SOURCE_PASS' -or
   [int]$pcProof.sourceDrawCount -ne 1903 -or
   [int]$pcProof.verifiedSourceWorldModelBodies -ne 28 -or
   [string]$axProof.status -ne 'AZTEC_GATE8_1903_PC_PLACEMENT_XENON_PACKED_AXES_PREFLIGHT_PASS' -or
   [int]$axProof.encodedT4XenonUnitAxisVectors -ne 5709 -or
   [string]$axProof.verifiedWorldMode -ne '28-model' -or
   [string]$axProof.originalPcDrawSourceSha256 -ine [string]$pcProof.typedDrawSha256 -or
   [string]$nativeProof.schema -ne 'WaW360.AztecGate8NativeStaticDraw60ByteWitness.v1' -or
   [string]$nativeProof.status -ne 'AZTEC_GATE8_ORIGINAL_XBOX_1903_NATIVE_STATIC_DRAW_ABI_LOADED_PASS' -or
   [int]$nativeProof.sourceDraws -ne 1903 -or
   [int]$nativeProof.independentlyOatLoadedNativeDraws -ne 1903 -or
   [int]$nativeProof.nativeAbi.stride -ne 60 -or
   [int]$nativeProof.nativeAbi.modelAt -ne 32 -or
   [int]$nativeProof.nativeAbi.reflectionAt -ne 36 -or
   [int]$nativeProof.nativeAbi.primaryLightAt -ne 37 -or
   [int]$nativeProof.nativeAbi.lightingHandleAt -ne 38 -or
   [int]$nativeProof.nativeAbi.flagsAt -ne 40 -or
   [int]$nativeProof.nativeAbi.padAt -ne 41 -or
   [string]$nativeProof.originalPcTypedDrawSourceSha256 -ine [string]$pcProof.typedDrawSha256 -or
   [int]$pointerPlan.physicalXModelBodyDraws -ne 1902 -or
   [int]$pointerPlan.externalStockBodyIdentityDraws -ne 1){
    throw 'Historical original-PC/native-Xbox draw source, geometry ABI or native witness changed.'
}
$pcPath=Need ([string]$pcProof.typedDrawFile) 'original PC per-draw sidecar'
$axPath=Need ([string]$axProof.packedAxesReport) 'PC packed T4 axes sidecar'
$nativeRaw=Need (Join-Path $base 'xenon_native_gfxworld_76_byte_surface_abi\native_xenon_gfxworld_surface_abi_reconciliation.json') 'native GfxWorld OAT witness provenance'
$prior=Get-Content -LiteralPath $nativeRaw -Raw|ConvertFrom-Json
if([string]$prior.status -ne 'AZTEC_NATIVE_XENON_76_BYTE_GFXSURFACE_ABI_LOADED_SOURCE_WITNESS' -or
   [string]$prior.originalNativeSha256 -ine 'E0F3C244DD7B72D0FE71C05FAF014C2C9BFB05F770183954B3E7CDE625EBA98F'){
    throw 'Original Xbox GfxWorld source provenance changed.'
}
$nativePath=Need ([string]$prior.rawNativeOatWitness) 'original Xbox OAT-loaded static draw rows'
if((Sha $nativePath) -ine [string]$nativeProof.originalXboxGfxWorldWitnessSha256 -or
   (Sha $pcPath) -ine [string]$pcProof.typedDrawSha256){
    throw 'Original-PC/native Xbox fixed-up row data SHA changed.'
}
$pc=Get-Content -LiteralPath $pcPath -Raw|ConvertFrom-Json
$axis=Get-Content -LiteralPath $axPath -Raw|ConvertFrom-Json
$native=Get-Content -LiteralPath $nativePath -Raw|ConvertFrom-Json
if([string]$pc.format -ne 'WaW360.T4PcGate8StaticDrawSource.v1' -or
   [string]$axis.schema -ne 'WaW360.AztecGate8XenonPackedStaticDrawAxes.v1' -or
   [string]$native.schema -ne 'WaW360.T4XenonGfxWorldSurfaceAbiWitness.v1' -or
   [string]$native.status -ne 'NATIVE_XENON_GFXWORLD_SURFACE_ABI_LOADER_WITNESS' -or
   -not [bool]$native.nativeStaticDrawArrayAvailable -or
   @($pc.records).Count -ne 1903 -or
   @($axis.records).Count -ne 1903 -or
   @($native.nativeStaticDraws).Count -ne 1903){
    throw 'Loaded 1903 PC/native typed draw corpus is missing.'
}
$names=0;$axesExact=0;$origins=0;$scales=0;$culls=0
$rowsFullyAligned=0;$identityAnchoredRows=0
$mismatches=New-Object 'System.Collections.Generic.List[object]'
$axisDetails=New-Object 'System.Collections.Generic.List[object]'
$fields=@('reflectionProbeIndex','primaryLightIndex','flags')
$allCounters=@{}
$alignedCounters=@{}
$identityCounters=@{}
$mappingCounts=@{}
$nonMatchingExamples=@{}
foreach($name in $fields){
    $allCounters[$name]=0
    $alignedCounters[$name]=0
    $identityCounters[$name]=0
    $mappingCounts[$name]=@{}
    $nonMatchingExamples[$name]=New-Object 'System.Collections.Generic.List[object]'
}
$cachedEq=0
$cachedAlignedEq=0
$pcFlagOutOfByte=0
$padRowsNonZero=0
$alignment=@()
$sourceReflectionRangeOk=$true
$sourcePrimaryRangeOk=$true
$sourceFlagsRangeOk=$true
for($i=0;$i -lt 1903;$i++){
    $p=$pc.records[$i];$x=$axis.records[$i];$n=$native.nativeStaticDraws[$i]
    if([int]$p.ordinal -ne $i -or [int]$x.ordinal -ne $i -or [int]$n.ordinal -ne $i -or
       (Canon ([string]$p.model)) -cne (Canon ([string]$x.model)) -or
       @($x.axisPackedWords).Count -ne 3 -or @($p.origin).Count -ne 3 -or
       @($n.origin).Count -ne 3 -or @($n.packedAxisWords).Count -ne 3){
        throw "Malformed original-PC/native Xbox row shape at ordinal $i."
    }
    $matchName=(Canon ([string]$p.model)) -ceq (Canon ([string]$n.model))
    $matchAxis=$true;$matchOrigin=$true
    $axisWords=New-Object 'System.Collections.Generic.List[object]'
    for($j=0;$j -lt 3;$j++){
        $v=[Convert]::ToUInt32(([string]$x.axisPackedWords[$j]).Substring(2),16)
        [uint32]$nativeAxisWord=[uint32]$n.packedAxisWords[$j]
        if($nativeAxisWord -ne $v){
            $matchAxis=$false
            $axisWords.Add([ordered]@{
                axisOrdinal=$j
                sourceOwnedPcPackedWord=('0x{0:X8}' -f $v)
                originalXboxReferencePackedWord=('0x{0:X8}' -f $nativeAxisWord)
                xorMask=('0x{0:X8}' -f ($v -bxor $nativeAxisWord))
                originalPcUnpackedAxis=@($p.axis[$j])
            })
        }
        if(-not(EqualNum $p.origin[$j] $n.origin[$j] 0.00001)){$matchOrigin=$false}
    }
    $matchScale=EqualNum $p.scale $n.scale 0.00001
    $matchCull=EqualNum $p.cullDist $n.cullDist 0.0001
    if($matchName){$names++}
    if($matchAxis){$axesExact++}
    if($matchOrigin){$origins++}
    if($matchScale){$scales++}
    if($matchCull){$culls++}
    # A name+position+scale+cull row anchor can independently establish
    # correspondence even when its 10-bit packed rotation differs.
    # This is weaker than a full draw geometry match; track both.
    $identityAnchored=$matchName -and $matchOrigin -and $matchScale -and $matchCull
    if($identityAnchored){$identityAnchoredRows++}
    $aligned=$identityAnchored -and $matchAxis
    if($aligned){$rowsFullyAligned++}
    if(-not $matchAxis){
        $axisDetails.Add([ordered]@{
            ordinal=$i
            originalPcModel=[string]$p.model
            originalXboxModel=[string]$n.model
            identityAnchored=$identityAnchored
            originalPcFlags=[int64](Field $p 'flags')
            originalXboxReferenceFlags=[int64](Field $n 'flags')
            differentAxisWords=@($axisWords.ToArray())
        })
    }
    elseif($mismatches.Count -lt 15){
        $mismatches.Add([ordered]@{
            ordinal=$i;pcModel=[string]$p.model;xboxModel=[string]$n.model
            modelNameEqual=$matchName;packedAxisEqual=$matchAxis
            originEqual=$matchOrigin;scaleEqual=$matchScale;cullEqual=$matchCull
        })
    }
    foreach($name in $fields){
        $pv=[int64](Field $p $name)
        $nv=[int64](Field $n $name)
        $pair=('{0}->{1}' -f $pv,$nv)
        if(-not $mappingCounts[$name].ContainsKey($pair)){$mappingCounts[$name][$pair]=0}
        $mappingCounts[$name][$pair]++
        if($pv -eq $nv){
            $allCounters[$name]++
            if($aligned){$alignedCounters[$name]++}
            if($identityAnchored){$identityCounters[$name]++}
        }elseif($nonMatchingExamples[$name].Count -lt 10){
            $nonMatchingExamples[$name].Add([ordered]@{
                ordinal=$i;pc=$pv;xbox=$nv;geometryIdentityAligned=$aligned
            })
        }
    }
    $pcReflection=[int64](Field $p 'reflectionProbeIndex')
    $pcPrimary=[int64](Field $p 'primaryLightIndex')
    $pcFlags=[int64](Field $p 'flags')
    if($pcReflection -lt 0 -or $pcReflection -gt 255){$sourceReflectionRangeOk=$false}
    if($pcPrimary -lt 0 -or $pcPrimary -gt 255){$sourcePrimaryRangeOk=$false}
    if($pcFlags -lt 0 -or $pcFlags -gt 255){$sourceFlagsRangeOk=$false;$pcFlagOutOfByte++}
    if([int](Field $p 'cachedLightSettingIndex') -eq [int](Field $n 'lightingHandle')){
        $cachedEq++;if($aligned){$cachedAlignedEq++}
    }
    if([int](Field $n 'paddingNonZeroBytes') -gt 0){$padRowsNonZero++}
}
if($padRowsNonZero -ne [int]$nativeProof.nativeRowsWithNonzeroPadding -or
   $names -ne [int]$nativeProof.ordinalModelNamesEqual -or
   $axesExact -ne [int]$nativeProof.ordinalPackedAxesExact -or
   $origins -ne [int]$nativeProof.ordinalOriginNear -or
   $scales -ne [int]$nativeProof.ordinalScaleNear -or
   $culls -ne [int]$nativeProof.ordinalCullDistNear){
    throw 'Recomputed original PC/Xbox geometry identity does not match the independent earlier witness.'
}
$fullAligned=($rowsFullyAligned -eq 1903)
$proofs=New-Object 'System.Collections.Generic.List[object]'
foreach($name in $fields){
    $rangeOk=switch($name){
        reflectionProbeIndex{$sourceReflectionRangeOk}
        primaryLightIndex{$sourcePrimaryRangeOk}
        flags{$sourceFlagsRangeOk}
    }
    $fullGeometryPass=$fullAligned -and $rangeOk -and [int]$allCounters[$name] -eq 1903
    $identityOnlyCandidate=($identityAnchoredRows -eq 1903 -and
        $rangeOk -and [int]$allCounters[$name] -eq 1903)
    $proofs.Add([ordered]@{
        field=$name
        originalPcMatchesOriginalXboxOrdinalValues=[int]$allCounters[$name]
        valuesEqualOnCorrespondingGeometry=[int]$alignedCounters[$name]
        valuesEqualOnNameOriginScaleCullAnchors=[int]$identityCounters[$name]
        nativeTypeUnsignedByteRangeProven=[bool]$rangeOk
        candidateExactIdentitySemanticsProven=[bool]$fullGeometryPass
        sourceFieldOnlyDiagnosticCandidateSupported=[bool]$identityOnlyCandidate
        sourceOwnedFieldSerializationPermittedByThisAudit=[bool]$fullGeometryPass
        sourceToXboxObservedPairCounts=@(
            $mappingCounts[$name].GetEnumerator() |
            Sort-Object Name |
            ForEach-Object { [ordered]@{sourceToReferencePair=[string]$_.Key;count=[int]$_.Value} }
        )
        differences=@($nonMatchingExamples[$name].ToArray())
    })
}
$lightingHypothesis=$fullAligned -and $cachedEq -eq 1903
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$report=Join-Path $OutputDirectory 'aztec_1903_pc_to_native_draw_scalar_equivalence.json'
[ordered]@{
    schema='WaW360.AztecPcXenonStaticDrawScalarEquivalence.v1'
    status='AZTEC_1903_DRAW_PC_TO_XBOX_SCALAR_EQUIVALENCE_AUDITED_NOT_EMITTED'
    originalPcTypedDrawSha256=[string]$pcProof.typedDrawSha256
    originalXboxOatWitnessSha256=[string]$nativeProof.originalXboxGfxWorldWitnessSha256
    originalPcDraws=1903
    originalXboxDraws=1903
    ordinalModelNameMatches=$names
    ordinalPackedAxisMatches=$axesExact
    ordinalOriginMatches=$origins
    ordinalScaleMatches=$scales
    ordinalCullMatches=$culls
    fullyEquivalentGeometryAndIdentityRows=$rowsFullyAligned
    directOrdinalPCToXboxCorrespondenceProven=$fullAligned
    independentNameOriginScaleCullAnchoredRows=$identityAnchoredRows
    all1903IdentityAnchoredWithoutRotation=$identityAnchoredRows -eq 1903
    sourcePackedAxisVsReferenceMismatchCount=$axisDetails.Count
    packedAxisMismatchDetails=@($axisDetails.ToArray())
    packedAxisMismatchRequiresIndependentCodecReview=($axisDetails.Count -gt 0)
    fields=@($proofs.ToArray())
    cachedLightSettingIndexEqualsXboxLightingHandleCount=$cachedEq
    cachedLightEqualsHandleOnAlignedGeometryRows=$cachedAlignedEq
    exactCachedLightSettingIndexToXboxLightingHandleHypothesis=$lightingHypothesis
    exactLightingHandleSemanticMappingProven=$false
    nativePaddingRowsWithNonzeroBytes=$padRowsNonZero
    nativePaddingValuesImported=$false
    originalPcFlagValuesOutsideByteRange=$pcFlagOutOfByte
    firstUnalignedGeometryRows=@($mismatches.ToArray())
    xboxDrawRecordsEmitted=0
    nativeFFModified=$false
    productionReady=$false
    note='Read-only original-PC versus original Xbox reference. A full geometry match requires identical packed axes. A source-field-only diagnostic candidate may be supported by all 1903 name/origin/scale/cull anchors plus exact field-value matches even when 4 packed rotations differ. This is not permission to graft any game FF and cannot establish unknown light semantics. Reference values are evidence only; no donor bytes are copied.'
}|ConvertTo-Json -Depth 9|Set-Content -LiteralPath $report -Encoding UTF8
Write-Host '[AZTEC-DRAW-SCALAR] ====================================================='
Write-Host "[AZTEC-DRAW-SCALAR] PC/native corresponding complete geometry rows: $rowsFullyAligned/1903"
Write-Host "[AZTEC-DRAW-SCALAR] Identity anchors model+origin+scale+cull: $identityAnchoredRows/1903; packed axis mismatches=$($axisDetails.Count)"
foreach($item in @($axisDetails.ToArray())){
    foreach($word in @($item.differentAxisWords)){
        Write-Host "[AZTEC-DRAW-SCALAR] AXIS-DIFF draw=$($item.ordinal) axis=$($word.axisOrdinal) PC=$($word.sourceOwnedPcPackedWord) Xbox=$($word.originalXboxReferencePackedWord) XOR=$($word.xorMask)"
    }
}
Write-Host "[AZTEC-DRAW-SCALAR] Ordinal model=$names/1903 axis=$axesExact/1903 origin=$origins/1903 scale=$scales/1903 cull=$culls/1903"
foreach($f in @($proofs.ToArray())){
    $state=if([bool]$f.candidateExactIdentitySemanticsProven){'FULL-GEOMETRY-CANDIDATE'}
        elseif([bool]$f.sourceFieldOnlyDiagnosticCandidateSupported){'SOURCE-FIELD-DIAGNOSTIC-ONLY'}
        else{'BLOCKED'}
    Write-Host "[AZTEC-DRAW-SCALAR] $($f.field) equal=$($f.originalPcMatchesOriginalXboxOrdinalValues)/1903 checkedGeometry=$($f.valuesEqualOnCorrespondingGeometry)/$rowsFullyAligned identityAnchors=$($f.valuesEqualOnNameOriginScaleCullAnchors)/$identityAnchoredRows -> $state"
}
Write-Host "[AZTEC-DRAW-SCALAR] cachedLightSettingIndex == Xbox lightingHandle: $cachedEq/1903 (exact mapping proven=$lightingHypothesis; field SEMANTICS not yet independently proven)"
Write-Host "[AZTEC-DRAW-SCALAR] Native padding nonzero rows=$padRowsNonZero. NO native pad bytes are imported."
Write-Host "[AZTEC-DRAW-SCALAR] REPORT=$report"
Write-Host '[AZTEC-DRAW-SCALAR] ZERO draw bytes emitted; 0/1903 FF draw links. NO Xbox test.'
