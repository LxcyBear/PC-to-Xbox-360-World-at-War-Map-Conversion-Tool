[CmdletBinding()]
param(
    [ValidateRange(0.00001,0.02)]
    [double]$MaxAllowedReferenceAxisComponentDifference=0.003
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# READ-ONLY original-PC-derived OAT-loaded FF versus original Xbox
# reference. The reference is a measured ABI/calibration oracle only;
# no Xbox-native bytes are used as a converted body or placed in an FF.
# This is NOT a map readiness gate and NEVER approves runtime lights.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$readbackPath=Join-Path $base 'pc_g10_1903\oat_loaded_1903_draw_readback\aztec_gate10_independent_1903_draw_readback.json'
$sourcePath=Join-Path $base 'pc_gate8_static_draw_source\aztec_gate8_pc_static_draw_source_report.json'
$originalXboxPath=Join-Path $base 'xenon_native_gate8_static_draw_60b_abi_witness\aztec_native_1903_static_draw_abi_witness_report.json'
$planPath=Join-Path $base 'pc_gate8_1903_draw_relocation_plan\aztec_1903_model_pointer_relocation_summary.json'
$stockAuditPath=Join-Path $base 'pc_gate8_runtime_stock_xmodel_reference_audit\aztec_runtime_stock_xmodel_reference_audit.json'
$out=Join-Path $base 'pc_g10_1903\gate10_reference_axis_and_stock_closure.json'
function Need([string]$path,[string]$name){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Missing $name at $path"
    }
    return (Resolve-Path -LiteralPath $path).Path
}
function Sha([string]$p){
    return [string](Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash
}
$read=Get-Content -LiteralPath (Need $readbackPath 'independent converted-FF typed OAT readback') -Raw|ConvertFrom-Json
$sourceProof=Get-Content -LiteralPath (Need $sourcePath 'original-PC typed source proof') -Raw|ConvertFrom-Json
$originalProof=Get-Content -LiteralPath (Need $originalXboxPath 'original Xbox 60B ABI witness') -Raw|ConvertFrom-Json
$plan=Get-Content -LiteralPath (Need $planPath 'source-to-XModel-root mapping') -Raw|ConvertFrom-Json
if([string]$read.schema -ne 'WaW360.AztecGate10IndependentOat1903DrawReadback.v1' -or
   [string]$read.status -ne 'AZTEC_GATE10_1903_ALL_SOURCE_DRAW_FIELDS_OAT_READBACK_PASS_STOCK_UNVERIFIED' -or
   [string]$read.stage4EEmittedFfSha256 -ine '0BB41398C4236EF33CB9D60F923E09152966D52AA623DAEFC33D731A42630564' -or
   [int]$read.actualNativeDrawCount -ne 1903 -or
   [int]$read.sourceOwnedXModelRows -ne 1902 -or
   [int]$read.unresolvedStockModelRows -ne 1 -or
   [bool]$read.XboxHardwareReady -or [bool]$read.gate10ProductionComplete -or
   [string]$sourceProof.status -ne 'AZTEC_GATE8_1903_PC_TYPED_STATIC_DRAWS_SOURCE_PASS' -or
   [int]$sourceProof.sourceDrawCount -ne 1903 -or
   [string]$sourceProof.typedDrawSha256 -ine [string]$read.originalPcDrawSourceSha256 -or
   [string]$originalProof.status -ne 'AZTEC_GATE8_ORIGINAL_XBOX_1903_NATIVE_STATIC_DRAW_ABI_LOADED_PASS' -or
   [int]$originalProof.independentlyOatLoadedNativeDraws -ne 1903 -or
   [string]$plan.schema -ne 'WaW360.Aztec1903ModelRootRelocationPlanSummary.v1' -or
   [int]$plan.sourceDrawCount -ne 1903 -or
   [int]$plan.physicalXModelBodyDraws -ne 1902 -or
   [int]$plan.externalStockBodyIdentityDraws -ne 1 -or
   [bool]$plan.externalStockResidencyProven){
    throw 'Gate10 source/independent output/retail reference lineage has changed. Do not compare stale data.'
}
$convertedPath=Need ([string]$read.independentOatLoadedWitness) 'converted FF actual loaded draw rows'
$nativeWrapper=Join-Path $base 'xenon_native_gfxworld_76_byte_surface_abi\native_xenon_gfxworld_surface_abi_reconciliation.json'
$wrap=Get-Content -LiteralPath (Need $nativeWrapper 'retail Xbox OAT witness wrapper') -Raw|ConvertFrom-Json
$nativePath=Need ([string]$wrap.rawNativeOatWitness) 'original Xbox read-only loaded 1903 rows'
$planFile=Need ([string]$plan.planFile) '1903 source model root plan'
if((Sha $convertedPath) -ine [string]$read.independentOatLoadedWitnessSha256 -or
   (Sha $nativePath) -ine [string]$originalProof.originalXboxGfxWorldWitnessSha256 -or
   (Sha $planFile) -ine [string]$plan.planSha256){
    throw 'The original Xbox/converted FF OAT witness or source root-mapping hash changed.'
}
$converted=Get-Content -LiteralPath $convertedPath -Raw|ConvertFrom-Json
$native=Get-Content -LiteralPath $nativePath -Raw|ConvertFrom-Json
$modelRows=Get-Content -LiteralPath $planFile -Raw|ConvertFrom-Json
if(@($converted.nativeStaticDraws).Count -ne 1903 -or
   @($native.nativeStaticDraws).Count -ne 1903 -or
   @($modelRows.records).Count -ne 1903){
    throw 'Independent Xbox/converted FF source row counts are not 1903.'
}

# EXACT pinned T4Xenon ThirdBased unit vector unpack (OAT revision
# 3beaa7c81af517f0c05f2b4612d477ed1541b676).
# This decoder measures, never patches original Xbox reference words.
Add-Type -TypeDefinition @'
using System;
public static class Gate10XenonUnitAxisUnpack
{
    public static float Component(uint bits10)
    {
        uint bits = bits10 & 0x3ffu;
        bits = unchecked(bits - (2u * (bits & 0x200u)) + 0x40400000u);
        float f = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        return (f - 3.0f) * 8208.0312f;
    }
    public static float[] Axis(uint packed)
    {
        return new float[] {
            Component(packed),
            Component(packed >> 10),
            Component(packed >> 20)
        };
    }
}
'@
$wordDiffs=New-Object 'System.Collections.Generic.List[object]'
$modelStock=New-Object 'System.Collections.Generic.List[object]'
$mismatchOrdinals=New-Object 'System.Collections.Generic.HashSet[int]'
$maxComponentDiff=0.0
$worst=$null
for($i=0;$i -lt 1903;$i++){
    $p=$converted.nativeStaticDraws[$i]
    $n=$native.nativeStaticDraws[$i]
    $m=$modelRows.records[$i]
    if([int]$p.ordinal -ne $i -or [int]$n.ordinal -ne $i -or
       [int]$m.ordinal -ne $i -or
       @($p.packedAxisWords).Count -ne 3 -or
       @($n.packedAxisWords).Count -ne 3){
        throw "Original-Xbox reference and converted draw ordinal/shape mismatch at $i"
    }
    if([string]$m.bodyStatus -eq 'STOCK_REFERENCE_RESIDENCY_UNVERIFIED'){
        $modelStock.Add([ordered]@{
            ordinal=$i
            originalPcReference=[string]$m.pcModel
            nativeRootIndex=[int]$m.nativeXModelRootIndex
            originalXboxReferenceName=[string]$n.model
            convertedOatLoadedName=[string]$p.model
            physicalConvertedModelPointerPresent=$false
            hardwareResidencyVerified=$false
        })
    }
    for($j=0;$j -lt 3;$j++){
        [uint32]$pcWord=[uint32]$p.packedAxisWords[$j]
        [uint32]$referenceWord=[uint32]$n.packedAxisWords[$j]
        if($pcWord -eq $referenceWord){continue}
        [void]$mismatchOrdinals.Add($i)
        $ours=[Gate10XenonUnitAxisUnpack]::Axis($pcWord)
        $original=[Gate10XenonUnitAxisUnpack]::Axis($referenceWord)
        $components=New-Object 'System.Collections.Generic.List[object]'
        for($k=0;$k -lt 3;$k++){
            $difference=[math]::Abs([double]$ours[$k]-[double]$original[$k])
            $components.Add([ordered]@{
                component=$k;sourceDecoded=[double]$ours[$k]
                referenceDecoded=[double]$original[$k]
                absoluteDifference=$difference
            })
            if($difference -gt $maxComponentDiff){
                $maxComponentDiff=$difference
                $worst=[ordered]@{draw=$i;axis=$j;component=$k}
            }
        }
        $wordDiffs.Add([ordered]@{
            draw=$i;axis=$j
            pcDerivedWord=('0x{0:X8}' -f $pcWord)
            referenceXboxWord=('0x{0:X8}' -f $referenceWord)
            xorMask=('0x{0:X8}' -f ($pcWord -bxor $referenceWord))
            componentDifferences=@($components.ToArray())
        })
    }
}
$actualOrdinals=@($mismatchOrdinals | Sort-Object)
$expectedOrdinals=@(4,8,10,300)
$expectedShape=(($actualOrdinals -join ',') -ceq ($expectedOrdinals -join ',')) -and
    $wordDiffs.Count -eq 6 -and $modelStock.Count -eq 1
$withinTolerance=$maxComponentDiff -le $MaxAllowedReferenceAxisComponentDifference
$stockReportStatus='MISSING_NOT_YET_AUDITED'
$stockOriginalVerified=$false
if(Test-Path -LiteralPath $stockAuditPath -PathType Leaf){
    $stockAudit=Get-Content -LiteralPath $stockAuditPath -Raw|ConvertFrom-Json
    if([string]$stockAudit.schema -ne 'WaW360.AztecExpectedLoadedStockXModelReferenceAudit.v1'){
        throw 'Previously saved stock XModel audit schema changed.'
    }
    $stockReportStatus=[string]$stockAudit.status
    $stockOriginalVerified=([int]$stockAudit.unresolvedSourceReferenceNames -eq 0 -and
        [int]$stockAudit.exactSourceRootStockBodyMatches -ge 1 -and
        [bool]$stockAudit.allExpectedLoadedZonesOatListed)
}
$result=[ordered]@{
    schema='WaW360.AztecGate10ReferenceAxisAndStockClosure.v1'
    status=if($expectedShape -and $withinTolerance){
        'AZTEC_GATE10_REFERENCE_AXES_WITHIN_CODEC_TOLERANCE_STOCK_REQUIRES_RUNTIME_PROOF'
    }else{'AZTEC_GATE10_REFERENCE_AXIS_DIFFERENCES_NEED_ABI_REVIEW'}
    convertedFfSha256=[string]$read.stage4EEmittedFfSha256
    sourceOwnedTypedOatRowsVerified=1903
    originalXboxReferenceOatRowsCompared=1903
    originalXboxAxisExactRows=1903-$actualOrdinals.Count
    packedAxisWordDifferences=$wordDiffs.Count
    packedAxisDifferenceOrdinals=$actualOrdinals
    expectedFourOrdinalPattern=$expectedShape
    maximumDecodedAxisComponentDifference=$maxComponentDiff
    maximumAllowedDecodedComponentDifference=$MaxAllowedReferenceAxisComponentDifference
    allDifferencesWithinTolerance=$withinTolerance
    worstDecodedComponent=$worst
    axisWordDiagnostics=@($wordDiffs.ToArray())
    stockReferenceDraws=@($modelStock.ToArray())
    earlierRetailStockBodyIdentityAudit=$stockAuditPath
    earlierRetailStockBodyIdentityStatus=$stockReportStatus
    earlierRetailBaseZoneIdentityEvidencePass=$stockOriginalVerified
    stockBodyRuntimeResidencyProven=$false
    runtimeLightSemanticsIndependentlyProven=$false
    nativeReferenceAxisExactnessProven=$false
    materialAndWorldRuntimeDependenciesComplete=$false
    physicalFFModified=$false
    xboxProductionReady=$false
}|ConvertTo-Json -Depth 10
$result|Set-Content -LiteralPath $out -Encoding UTF8
Write-Host '[AZTEC-GATE10-AXIS] ========================================================'
Write-Host "[AZTEC-GATE10-AXIS] Independent source and retail Xbox GfxWorld rows=1903/1903"
Write-Host "[AZTEC-GATE10-AXIS] Exact packed placement records=$(1903-$actualOrdinals.Count)/1903"
Write-Host "[AZTEC-GATE10-AXIS] Differing draw ordinals=$($actualOrdinals -join ',') words=$($wordDiffs.Count)"
Write-Host ('[AZTEC-GATE10-AXIS] Largest decoded component delta={0:G9}; bound={1:G9}' -f $maxComponentDiff,$MaxAllowedReferenceAxisComponentDifference)
foreach($d in @($wordDiffs.ToArray())){
    Write-Host "[AZTEC-GATE10-AXIS] draw=$($d.draw) axis=$($d.axis) ours=$($d.pcDerivedWord) originalXbox=$($d.referenceXboxWord) xor=$($d.xorMask)"
}
foreach($stock in @($modelStock.ToArray())){
    Write-Host "[AZTEC-GATE10-STOCK] draw=$($stock.ordinal) source='$($stock.originalPcReference)' root=$($stock.nativeRootIndex) FF-loaded='$($stock.convertedOatLoadedName)'"
}
Write-Host "[AZTEC-GATE10-STOCK] Retail base-zone identity audit=$stockReportStatus; hardware residency UNVERIFIED"
Write-Host "[AZTEC-GATE10-AXIS] REPORT=$out"
Write-Host '[AZTEC-GATE10-AXIS] No FF, OAT, asset dump or .NET build performed.'
Write-Host '[AZTEC-GATE10-AXIS] Source-v-reference tolerance is NOT runtime or binary equivalence.'
if(-not $expectedShape -or -not $withinTolerance){
    throw 'Decoded native axis delta does not satisfy the currently known exact 4-ordinal codec tolerance. Review original-PC source and pinned Xenon vector packer.'
}
