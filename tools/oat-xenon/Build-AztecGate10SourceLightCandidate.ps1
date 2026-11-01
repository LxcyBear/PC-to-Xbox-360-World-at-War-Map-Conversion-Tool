[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# AZTEC Gate10 read-only/sidecar source conversion experiment.
# Test *every* original PC primary/probe field against independent native
# Xbox OAT scalars before constructing a SOURCE-DERIVED diagnostic candidate.
# NEVER use the native reference bytes as converted asset payload.
# Still NOT a valid Xbox draw array or FF; no hardware deployment.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$auditPath=Join-Path $base 'pc_gate8_static_draw_scalar_equivalence\aztec_1903_pc_to_native_draw_scalar_equivalence.json'
$sourceProofPath=Join-Path $base 'pc_gate8_static_draw_source\aztec_gate8_pc_static_draw_source_report.json'
$nativeProofPath=Join-Path $base 'xenon_native_gate8_static_draw_60b_abi_witness\aztec_native_1903_static_draw_abi_witness_report.json'
$nativeWrapPath=Join-Path $base 'xenon_native_gfxworld_76_byte_surface_abi\native_xenon_gfxworld_surface_abi_reconciliation.json'
$flagsParent=Join-Path $base 'pc_gate8_source_flags_codec_candidate'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
function Need([string]$p,[string]$what){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing $what : $p"}
    return (Resolve-Path -LiteralPath $p).Path
}
function Sha([string]$p){return [string](Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash}
$proof=Get-Content -LiteralPath (Need $sourceProofPath 'PC typed draw proof') -Raw|ConvertFrom-Json
$audit=Get-Content -LiteralPath (Need $auditPath 'updated lighting hypothesis audit') -Raw|ConvertFrom-Json
$nproof=Get-Content -LiteralPath (Need $nativeProofPath 'Xbox native 60B ABI proof') -Raw|ConvertFrom-Json
if([string]$proof.status -ne 'AZTEC_GATE8_1903_PC_TYPED_STATIC_DRAWS_SOURCE_PASS' -or
   [int]$proof.sourceDrawCount -ne 1903 -or
   [string]$audit.schema -ne 'WaW360.AztecPcXenonStaticDrawScalarEquivalence.v1' -or
   [string]$audit.status -ne 'AZTEC_1903_DRAW_PC_TO_XBOX_SCALAR_EQUIVALENCE_AUDITED_NOT_EMITTED' -or
   [string]$audit.originalPcTypedDrawSha256 -ine [string]$proof.typedDrawSha256 -or
   [string]$audit.originalXboxOatWitnessSha256 -ine [string]$nproof.originalXboxGfxWorldWitnessSha256 -or
   [int]$audit.independentNameOriginScaleCullAnchoredRows -ne 1903 -or
   -not [bool]$audit.all1903IdentityAnchoredWithoutRotation -or
   [int]$audit.sourcePackedAxisVsReferenceMismatchCount -ne 4 -or
   [int]$audit.originalPcDraws -ne 1903 -or
   [int]$audit.originalXboxDraws -ne 1903 -or
   [int]$nproof.nativeAbi.stride -ne 60 -or
   [int]$nproof.nativeAbi.modelAt -ne 32 -or
   [int]$nproof.nativeAbi.reflectionAt -ne 36 -or
   [int]$nproof.nativeAbi.primaryLightAt -ne 37 -or
   [int]$nproof.nativeAbi.lightingHandleAt -ne 38 -or
   [int]$nproof.nativeAbi.flagsAt -ne 40 -or
   [bool]$audit.productionReady -or [int]$audit.xboxDrawRecordsEmitted -ne 0){
    throw 'Source/native 1903 draw, flags, ABI or independent audit proof changed; candidate blocked.'
}
Write-Host '[AZTEC-GATE10-LIGHT] ========================================================'
Write-Host "[AZTEC-GATE10-LIGHT] HYPOTHESIS Xbox lightingHandle = 256 + PC primaryLightIndex: $($audit.xboxHandleEquals256PlusPcPrimaryCount)/1903"
Write-Host "[AZTEC-GATE10-LIGHT] HYPOTHESIS Xbox reflectionProbeIndex = PC reflectionProbeIndex - 1: $($audit.xboxReflectionEqualsPcReflectionMinusOneCount)/1903"
if([int]$audit.xboxHandleEquals256PlusPcPrimaryCount -ne 1903 -or
   [int]$audit.xboxReflectionEqualsPcReflectionMinusOneCount -ne 1903 -or
   -not [bool]$audit.xboxHandleEquals256PlusPcPrimaryForAllRows -or
   -not [bool]$audit.xboxReflectionEqualsPcReflectionMinusOneForAllRows -or
   [int]$audit.primaryIndexToHandleMappingConflicts -ne 0 -or
   [bool]$audit.originalPcPrimaryToXboxHandleConversionSemanticsIndependentlyProven -or
   [bool]$audit.originalPcReflectionToXboxIndexBaseSemanticsIndependentlyProven){
    Write-Host '[AZTEC-GATE10-LIGHT] NOT PROVEN: no candidate. Do NOT apply an inferred PC/Xbox light transform.'
    throw 'Observed light arithmetic is not 1903/1903. More source ABI/probe ownership analysis needed.'
}
$latest=@(Get-ChildItem -LiteralPath $flagsParent -Filter 'gate10_source_flags_candidate_summary.json' -File -Recurse -ErrorAction Stop |
    Sort-Object LastWriteTime -Descending)
if($latest.Count -eq 0){throw 'Run the previously proven source flags candidate script first.'}
$flags=Get-Content -LiteralPath $latest[0].FullName -Raw|ConvertFrom-Json
if([string]$flags.schema -ne 'WaW360.AztecGate10PcSourcedFlagsCandidate.v1' -or
   [string]$flags.status -ne 'AZTEC_GATE10_ALL_1903_SOURCE_FLAGS_PC_TO_XBOX_BYTE_ENCODING_PASS_NOT_FF' -or
   [int]$flags.nativeByteFlagsWritten -ne 1903 -or
   [int]$flags.xmodelPointersPreserved -ne 1902 -or
   [int]$flags.unprovenStockXModelDraws -ne 1 -or
   [int]$flags.packedRotationsStillNeedReview -ne 4 -or
   [bool]$flags.productionReady -or [bool]$flags.validCompleteXboxDrawArray -or
   [string]$flags.sourcePcTypedDrawSha256 -ine [string]$proof.typedDrawSha256){
    throw 'Last source-owned 1903 flag and 1902-pointer proof invalid.'
}
$priorCandidate=Need ([string]$flags.byteCandidateFile) '1903 PC flag and 1902 pointer candidate'
if((Sha $priorCandidate) -ine [string]$flags.byteCandidateSha256 -or
   (Get-Item -LiteralPath $priorCandidate).Length -ne 114180){
    throw 'Last original-PC flags candidate bytes altered.'
}
$pcPath=Need ([string]$proof.typedDrawFile) 'original PC 1903 typed rows'
if((Sha $pcPath) -ine [string]$proof.typedDrawSha256){throw 'PC typed draw SHA mismatch.'}
$pc=Get-Content -LiteralPath $pcPath -Raw|ConvertFrom-Json
$wrap=Get-Content -LiteralPath (Need $nativeWrapPath 'original Xbox reference OAT provenance') -Raw|ConvertFrom-Json
if([string]$wrap.status -ne 'AZTEC_NATIVE_XENON_76_BYTE_GFXSURFACE_ABI_LOADED_SOURCE_WITNESS' -or
   [string]$wrap.originalNativeSha256 -ine 'E0F3C244DD7B72D0FE71C05FAF014C2C9BFB05F770183954B3E7CDE625EBA98F'){
    throw 'Original native reference provenance changed.'
}
$nativeRaw=Need ([string]$wrap.rawNativeOatWitness) 'read-only native Xbox 1903 rows'
if((Sha $nativeRaw) -ine [string]$nproof.originalXboxGfxWorldWitnessSha256){
    throw 'Independent original Xbox reference witness changed.'
}
$native=Get-Content -LiteralPath $nativeRaw -Raw|ConvertFrom-Json
if(@($pc.records).Count -ne 1903 -or
   @($native.nativeStaticDraws).Count -ne 1903){
    throw 'Original PC/Xbox draw census changed.'
}
$sourceReflection=New-Object 'System.Collections.Generic.List[int]'
$sourcePrimary=New-Object 'System.Collections.Generic.List[int]'
$matchesHandle=0;$matchesProbe=0;$xboxPrimaryZeros=0
for($i=0;$i -lt 1903;$i++){
    $p=$pc.records[$i];$n=$native.nativeStaticDraws[$i]
    if([int]$p.ordinal -ne $i -or [int]$n.ordinal -ne $i){
        throw "Corrupt original source/native ordinal $i"
    }
    $reflection=[int]$p.reflectionProbeIndex
    $primary=[int]$p.primaryLightIndex
    if($reflection -lt 1 -or $reflection -gt 256 -or
       $primary -lt 0 -or $primary -gt 65535-256){
        throw "Original PC light/probe indices outside provable Xbox candidate arithmetic at $i."
    }
    if([int]$n.reflectionProbeIndex -eq $reflection-1){$matchesProbe++}
    if([int]$n.lightingHandle -eq $primary+256){$matchesHandle++}
    if([int]$n.primaryLightIndex -eq 0){$xboxPrimaryZeros++}
    $sourceReflection.Add($reflection)
    $sourcePrimary.Add($primary)
}
Write-Host "[AZTEC-GATE10-LIGHT] Independent per-row readback: probe=$matchesProbe/1903 handle=$matchesHandle/1903 originalXboxPrimaryZero=$xboxPrimaryZeros/1903"
if($matchesHandle -ne 1903 -or $matchesProbe -ne 1903){
    throw 'Independent per-row hypothesis failed. No Xbox or FF bytes produced.'
}
$out=Join-Path $base ('pc_gate8_source_light_codec_candidate\run_'+(Get-Date -Format 'yyyyMMdd_HHmmss')+'_'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Force -Path $out|Out-Null
$inputJson=Join-Path $out 'source_owned_native_light_arithmetic_input.json'
$binary=Join-Path $out '1903_source_probes_and_handles_FLAGS_MODEL_PTRS_STILL_NOT_FOR_FF.bin'
$codecReport=Join-Path $out 'generic_derived_scalar_codec_report.json'
$summary=Join-Path $out 'aztec_gate10_source_light_candidate_summary.json'
[ordered]@{
    schema='WaW360.T4Xenon.StaticDrawDerivedScalarSource.v1'
    sourceOnlyDiagnostic=$true
    productionApproved=$false
    sourceCandidate=$priorCandidate
    sourceCandidateSha256=[string]$flags.byteCandidateSha256
    sourceAuditSha256=(Sha $auditPath)
    originalXboxWitnessSha256=[string]$nproof.originalXboxGfxWorldWitnessSha256
    recordStride=60
    modelPointerByteOffset=32
    fields=@(
        [ordered]@{
            name='reflectionProbeIndex'
            nativeByteOffset=36
            nativeWidthBytes=1
            addend=-1
            pcSourceField='reflectionProbeIndex'
            originalPcValues=@($sourceReflection.ToArray())
            allRowsComparedToIndependentXboxWitness=$true
            nativeSemanticsIndependentlyProven=$false
        }
        [ordered]@{
            name='lightingHandle'
            nativeByteOffset=38
            nativeWidthBytes=2
            addend=256
            pcSourceField='primaryLightIndex'
            originalPcValues=@($sourcePrimary.ToArray())
            allRowsComparedToIndependentXboxWitness=$true
            nativeSemanticsIndependentlyProven=$false
        }
    )
    nativeReferenceBytesUsedAsDonor=$false
}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $inputJson -Encoding UTF8
Write-Host '[AZTEC-GATE10-LIGHT] Incremental shared PC2XB3 build, generic 52B selftest and source-derived BE16 fields.'
& dotnet build $cli -c Release --nologo
if($LASTEXITCODE -ne 0){throw 'Generic .NET source light field converter compilation failed.'}
& dotnet run --project $cli -c Release --no-build -- xenon-static-draw-derived-selftest
if($LASTEXITCODE -ne 0){throw 'Generic BE16 scalar codec selftest FAILED.'}
& dotnet run --project $cli -c Release --no-build -- xenon-static-draw-derived-encode $inputJson $binary $codecReport
if($LASTEXITCODE -ne 0){throw 'PC-source derived light field encoding FAILED.'}
$before=[IO.File]::ReadAllBytes($priorCandidate)
$after=[IO.File]::ReadAllBytes((Need $binary 'new source derived light buffer'))
if($after.Length -ne 114180 -or $before.Length -ne $after.Length){
    throw 'Produced draw binary is not 1903 x 60 bytes.'
}
for($i=0;$i -lt 1903;$i++){
    $start=$i*60
    if([int]$after[$start+36] -ne ($sourceReflection[$i]-1) -or
       $after[$start+38] -ne [byte](((($sourcePrimary[$i]+256) -shr 8)) -band 255) -or
       $after[$start+39] -ne [byte](($sourcePrimary[$i]+256) -band 255)){
        throw "Derived big-endian native light bytes do not match original PC values at draw $i"
    }
    for($j=0;$j -lt 60;$j++){
        if($j -notin @(36,38,39) -and $before[$start+$j] -ne $after[$start+$j]){
            throw "Model pointers, PC flags, geometry or non-proven bytes changed at draw $i +$j."
        }
    }
}
[ordered]@{
    schema='WaW360.AztecGate10SourceLightHypothesisCandidate.v1'
    status='AZTEC_GATE10_SOURCE_LIGHT_ARITHMETIC_MATCHES_ORIGINAL_XBOX_1903_NOT_PRODUCTION'
    originalPcStaticDrawSha256=[string]$proof.typedDrawSha256
    originalXboxStaticDrawWitnessSha256=[string]$nproof.originalXboxGfxWorldWitnessSha256
    inputPcOwnedFlagsAndModelPointersSha256=[string]$flags.byteCandidateSha256
    candidate=$binary
    candidateSha256=(Sha $binary)
    reflectionIndexBaseHypothesisExactRows=$matchesProbe
    lightingHandleAdd256HypothesisExactRows=$matchesHandle
    sourceFlagsRetained=1903
    convertedOwnedXModelPointersRetained=1902
    externallyResidentStockXModelUnproven=1
    packedAxesDifferFromNativeReferenceDraws=4
    nativePrimaryLightIndexSourceMappingProven=$false
    sourceLightFieldRuntimeSemanticCompatibilityProven=$false
    nativeReferencePayloadBytesCopied=$false
    sourceWorldFFModified=$false
    physicalDrawsLinkedToGfxWorld=0
    productionApproved=$false
}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath $summary -Encoding UTF8
Write-Host "[AZTEC-GATE10-LIGHT] SOURCE-ONLY PASS: $matchesProbe reflection base / $matchesHandle Xbox uint16 light handles independently matched."
Write-Host '[AZTEC-GATE10-LIGHT] 1903 source flags + 1902 native XModel pointers unchanged.'
Write-Host "[AZTEC-GATE10-LIGHT] Candidate: $binary"
Write-Host "[AZTEC-GATE10-LIGHT] Report: $summary"
Write-Host '[AZTEC-GATE10-LIGHT] UNPROVEN runtime light semantics, 4 rotation differences, one external stock model, primary light field, zero world links. No FF.'
