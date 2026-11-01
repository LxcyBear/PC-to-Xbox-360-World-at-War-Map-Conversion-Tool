[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Gate10 next source-owned candidate: write 1903 byte flags from the ORIGINAL
# PC typed exporter into the already proven 1902 Xbox XModel pointer array.
# Original Xbox native values are READ-ONLY comparison witnesses.
# NO copied reference bytes; NO light fields; NO FF mutation or Xbox release.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$pcProofPath=Join-Path $base 'pc_gate8_static_draw_source\aztec_gate8_pc_static_draw_source_report.json'
$auditPath=Join-Path $base 'pc_gate8_static_draw_scalar_equivalence\aztec_1903_pc_to_native_draw_scalar_equivalence.json'
$pointerReportPath=Join-Path $base 'pc_gate8_1903_draw_pointer_relocation_candidate\aztec_1903_draw_pointer_emission_report.json'
$nativeProofPath=Join-Path $base 'xenon_native_gate8_static_draw_60b_abi_witness\aztec_native_1903_static_draw_abi_witness_report.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
function Need([string]$p,[string]$name){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing $name : $p"}
    return (Resolve-Path -LiteralPath $p).Path
}
function Sha([string]$p){return [string](Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash}
$pcProof=Get-Content -LiteralPath (Need $pcProofPath 'original-PC source proof') -Raw|ConvertFrom-Json
$audit=Get-Content -LiteralPath (Need $auditPath 'Gate8G PC/Xbox field equivalence audit') -Raw|ConvertFrom-Json
$old=Get-Content -LiteralPath (Need $pointerReportPath 'prior 1902-pointer candidate') -Raw|ConvertFrom-Json
$nativeProof=Get-Content -LiteralPath (Need $nativeProofPath 'native Xbox 60B ABI witness') -Raw|ConvertFrom-Json
if([string]$pcProof.status -ne 'AZTEC_GATE8_1903_PC_TYPED_STATIC_DRAWS_SOURCE_PASS' -or
   [int]$pcProof.sourceDrawCount -ne 1903 -or
   [string]$audit.schema -ne 'WaW360.AztecPcXenonStaticDrawScalarEquivalence.v1' -or
   [string]$audit.status -ne 'AZTEC_1903_DRAW_PC_TO_XBOX_SCALAR_EQUIVALENCE_AUDITED_NOT_EMITTED' -or
   [string]$audit.originalPcTypedDrawSha256 -ine [string]$pcProof.typedDrawSha256 -or
   [string]$audit.originalXboxOatWitnessSha256 -ine [string]$nativeProof.originalXboxGfxWorldWitnessSha256 -or
   [int]$audit.originalPcDraws -ne 1903 -or [int]$audit.originalXboxDraws -ne 1903 -or
   -not [bool]$audit.all1903IdentityAnchoredWithoutRotation -or
   [int]$audit.independentNameOriginScaleCullAnchoredRows -ne 1903 -or
   [int]$audit.ordinalModelNameMatches -ne 1903 -or
   [int]$audit.sourcePackedAxisVsReferenceMismatchCount -ne 4 -or
   [int]$audit.fullyEquivalentGeometryAndIdentityRows -ne 1899 -or
   [bool]$audit.directOrdinalPCToXboxCorrespondenceProven -or
   [bool]$audit.productionReady -or [int]$audit.xboxDrawRecordsEmitted -ne 0 -or
   [string]$old.schema -ne 'WaW360.AztecDrawPointerEmissionCandidate.v1' -or
   [int]$old.sourceOwnedXModelPointerWordsWritten -ne 1902 -or
   [int]$old.externalStockReferencePointerWordsWritten -ne 0 -or
   [int]$old.candidateBytes -ne 114180 -or [bool]$old.productionApproved -or
   [int]$nativeProof.nativeAbi.stride -ne 60 -or
   [int]$nativeProof.nativeAbi.modelAt -ne 32 -or
   [int]$nativeProof.nativeAbi.flagsAt -ne 40){
    throw 'Aztec original-PC/60B Xbox flags and 1902-pointer provenance changed; NO candidate output.'
}
$flags=@($audit.fields|Where-Object {[string]$_.field -ceq 'flags'})
if($flags.Count -ne 1 -or
   [int]$flags[0].originalPcMatchesOriginalXboxOrdinalValues -ne 1903 -or
   [int]$flags[0].valuesEqualOnNameOriginScaleCullAnchors -ne 1903 -or
   -not [bool]$flags[0].nativeTypeUnsignedByteRangeProven -or
   -not [bool]$flags[0].sourceFieldOnlyDiagnosticCandidateSupported -or
   [bool]$flags[0].sourceOwnedFieldSerializationPermittedByThisAudit){
    throw 'Flags are not 1903/1903 source-owned byte-equality CANDIDATES; do not silently approve full geometry.'
}
$src=Need ([string]$pcProof.typedDrawFile) 'original PC 1903 typed static draw rows'
if((Sha $src) -ine [string]$pcProof.typedDrawSha256){throw 'Original PC typed draw SHA drifted.'}
$oldCandidate=Need ([string]$old.candidateDrawArray) '1902 Xbox pointer bytes'
if((Sha $oldCandidate) -ine [string]$old.candidateDrawArraySha256){
    throw 'Historical user-verified XModel pointer candidate SHA drifted.'
}
$typed=Get-Content -LiteralPath $src -Raw|ConvertFrom-Json
if([string]$typed.format -ne 'WaW360.T4PcGate8StaticDrawSource.v1' -or
   [int]$typed.count -ne 1903 -or @($typed.records).Count -ne 1903){
    throw 'Original PC rows no longer form the exact loaded 1903-draw source.'
}
# Independently compare the original Xbox scalar values, but NEVER copy
# Xbox native reference bytes. Only the original PC flags are emitted.
$priorNative=Get-Content -LiteralPath (Need (Join-Path $base 'xenon_native_gfxworld_76_byte_surface_abi\native_xenon_gfxworld_surface_abi_reconciliation.json') 'native loader provenance') -Raw|ConvertFrom-Json
$nativeRaw=Need ([string]$priorNative.rawNativeOatWitness) 'read-only original Xbox OAT draw rows'
if((Sha $nativeRaw) -ine [string]$nativeProof.originalXboxGfxWorldWitnessSha256){
    throw 'Native Xbox original OAT witness SHA drifted after scalar comparison.'
}
$native=Get-Content -LiteralPath $nativeRaw -Raw|ConvertFrom-Json
if(@($native.nativeStaticDraws).Count -ne 1903){
    throw 'Original Xbox independent loaded draw row count changed.'
}
$sourceValues=New-Object 'System.Collections.Generic.List[int]'
for($i=0;$i -lt 1903;$i++){
    $p=$typed.records[$i];$n=$native.nativeStaticDraws[$i]
    if([int]$p.ordinal -ne $i -or [int]$n.ordinal -ne $i){
        throw "Original-PC and Xbox ordinal proof mismatch at $i"
    }
    $v=[int]$p.flags
    if($v -lt 0 -or $v -gt 255 -or $v -ne [int]$n.flags){
        throw "Source flags are not verified as an exact byte at ordinal $i."
    }
    $sourceValues.Add($v)
}
$out=Join-Path $base ('pc_gate8_source_flags_codec_candidate\run_'+(Get-Date -Format 'yyyyMMdd_HHmmss')+'_'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $out -Force|Out-Null
$neutral=Join-Path $out 'source_owned_flags_input.json'
$binary=Join-Path $out '1903_pc_owned_flags_and_1902_xmodel_ptrs_INCOMPLETE_NOT_FOR_FF.bin'
$codecReport=Join-Path $out 'generic_scalar_codec_report.json'
$summary=Join-Path $out 'gate10_source_flags_candidate_summary.json'
[ordered]@{
    schema='WaW360.T4Xenon.StaticDrawScalarSource.v1'
    sourceCandidate=$oldCandidate
    sourceCandidateSha256=[string]$old.candidateDrawArraySha256
    recordStride=60
    modelPointerOffset=32
    independentReferenceAuditSha256=(Sha $auditPath)
    sourceFields=@([ordered]@{
        name='flags'
        byteOffset=40
        auditedExactPerOrdinalMatch=$true
        sourceOwner='ORIGINAL_PC_OAT_LOADED_TYPED_FIELDS'
        values=@($sourceValues.ToArray())
    })
    sourceOnly=$true
    originalXboxBytesUsedAsOutput=$false
    xboxHardwareReady=$false
}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $neutral -Encoding UTF8
Write-Host '[AZTEC-GATE10-FLAGS] ================================================'
Write-Host '[AZTEC-GATE10-FLAGS] PC flags 1903/1903 equal original Xbox reference; 4 rotations still differ.'
Write-Host '[AZTEC-GATE10-FLAGS] Incremental generic PC2XB3 .NET 8 build and independent non-Aztec scalar test.'
& dotnet build $cli -c Release --nologo
if($LASTEXITCODE -ne 0){throw 'Reusable PC2XB3 CLI build failed; no FF changed.'}
& dotnet run --project $cli -c Release --no-build -- xenon-static-draw-scalar-selftest
if($LASTEXITCODE -ne 0){throw 'Generic non-Aztec scalar codec selftest failed.'}
& dotnet run --project $cli -c Release --no-build -- xenon-static-draw-scalar-encode $neutral $binary $codecReport
if($LASTEXITCODE -ne 0){throw 'PC-sourced Xbox flags codec candidate failed.'}
if(-not(Test-Path -LiteralPath $binary -PathType Leaf) -or
   (Get-Item -LiteralPath $binary).Length -ne 114180){
    throw 'Expected 1903 x 60B byte-scalar candidate was not emitted.'
}
$before=[IO.File]::ReadAllBytes($oldCandidate)
$after=[IO.File]::ReadAllBytes($binary)
$flagChangeCount=0
for($i=0;$i -lt 1903;$i++){
    $flagAt=$i*60+40
    if([int]$after[$flagAt] -ne $sourceValues[$i]){
        throw "Written Xbox flags were not sourced from PC typed draw ordinal $i."
    }
    if($after[$flagAt] -ne $before[$flagAt]){$flagChangeCount++}
    for($j=0;$j -lt 60;$j++){
        if($j -ne 40 -and $after[$i*60+$j] -ne $before[$i*60+$j]){
            throw "Original 1902-pointer model/geometry/lighting byte changed at draw $i relative field $j."
        }
    }
}
[ordered]@{
    schema='WaW360.AztecGate10PcSourcedFlagsCandidate.v1'
    status='AZTEC_GATE10_ALL_1903_SOURCE_FLAGS_PC_TO_XBOX_BYTE_ENCODING_PASS_NOT_FF'
    sourcePcTypedDrawSha256=[string]$pcProof.typedDrawSha256
    originalXboxScalarAuditSha256=(Sha $auditPath)
    preexistingXModelPointerSha256=[string]$old.candidateDrawArraySha256
    sourceFlagsFromOriginalPc=1903
    sourceFlagValuesMatchReadOnlyXboxReference=1903
    nativeByteFlagsWritten=1903
    nativeByteFlagsActuallyNonzeroOrChanged=$flagChangeCount
    xmodelPointersPreserved=1902
    unprovenStockXModelDraws=1
    packedRotationsStillNeedReview=4
    nativeReflectionProbeIndicesConverted=0
    nativePrimaryLightIndicesConverted=0
    nativeLightingHandlesConverted=0
    staticDrawRecordsPhysicallyLinkedIntoFF=0
    byteCandidateFile=$binary
    byteCandidateSha256=(Sha $binary)
    usedOriginalXboxReferencePayloadAsDonor=$false
    validCompleteXboxDrawArray=$false
    sourceWorldFfModified=$false
    productionReady=$false
}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath $summary -Encoding UTF8
Write-Host '[AZTEC-GATE10-FLAGS] PASS: 1903/1903 real PC flags bytes, 1902/1902 real owned model pointers preserved.'
Write-Host "[AZTEC-GATE10-FLAGS] 114180B candidate SHA256=$(Sha $binary)"
Write-Host '[AZTEC-GATE10-FLAGS] 4 rotation differences, all lighting fields, stock model and world links remain pending.'
Write-Host "[AZTEC-GATE10-FLAGS] REPORT=$summary"
Write-Host '[AZTEC-GATE10-FLAGS] NO FF rebuilt, NO Xbox package, NO donor data.'
