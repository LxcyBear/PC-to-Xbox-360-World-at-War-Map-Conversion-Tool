[CmdletBinding()]
param(
    [ValidateRange(0.0001, 0.02)]
    [double]$MaxPackedComponentError=0.003,
    [ValidateRange(0.001, 0.25)]
    [double]$MaxSourceAxisLengthError=0.05
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Gate 8D: original-PC 1903 GfxStaticModelDrawInst numeric placement preflight.
# The exact Xbox T4Xenon vec packer is copied from PINNED OAT:
# src/Common/Game/T4Xenon/CommonT4Xenon.cpp uses
# pack32::Vec3PackUnitVecThirdBased in src/Common/Utils/Pack.cpp
# at 3beaa7c81af517f0c05f2b4612d477ed1541b676.
# Not a game FastFile serializer; unresolved pointer/lighting/flag fields
# remain hard-fenced and NO Xbox static draw record bytes are emitted.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$n=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$sourceProofPath=Join-Path $n 'pc_gate8_static_draw_source\aztec_gate8_pc_static_draw_source_report.json'
$fivePath=Join-Path $n 'pc_gate8_5x_ff\aztec_pc_world_core_integrated_oat_run.json'
$auditPath=Join-Path $n 'pc_gate8_static_model_dependency_audit\aztec_gate8_static_draw_xmodel_dependencies.json'
$identityPath=Join-Path $n 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$out=Join-Path $n 'pc_gate8_static_draw_xenon_axis_preflight'
$reportPath=Join-Path $out 'aztec_1903_xenon_axis_pack_preflight.json'
$entriesPath=Join-Path $out 'aztec_1903_packed_axis_candidates.json'
foreach($p in @($sourceProofPath,$fivePath,$auditPath,$identityPath)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Missing original-PC Xenon placement preflight input: $p"
    }
}
$source=Get-Content -LiteralPath $sourceProofPath -Raw | ConvertFrom-Json
$five=Get-Content -LiteralPath $fivePath -Raw | ConvertFrom-Json
$audit=Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json
$identity=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
if([string]$source.schema -ne 'WaW360.AztecGate8PcStaticDrawSourceProof.v1' -or
   [string]$source.status -ne 'AZTEC_GATE8_1903_PC_TYPED_STATIC_DRAWS_SOURCE_PASS' -or
   [int]$source.sourceDrawCount -ne 1903 -or
   [int]$source.distinctSourceModels -ne 27 -or
   [int]$source.fiveRealModelBodyDrawsRepresented -ne 1572 -or
   [bool]$source.pcModelPointerBytesCopied -or
   [bool]$source.hardwareReady -or
   [string]$five.status -ne 'AZTEC_GATE8_FIVE_REAL_XMODELS_8509_ROOT_WORLD_OAT_PASS' -or
   -not [bool]$five.OatPass -or
   [int]$five.realXModelRootsInCurrentFf -ne 5 -or
   -not [bool]$five.fiveSourceOwnedXModelProbe -or
   [bool]$five.hardwareReady -or
   [string]$audit.schema -ne 'WaW360.AztecGate8StaticDrawModelDependencyAudit.v1' -or
   [int]$audit.sourceDrawInstances -ne 1903 -or
   [int]$audit.distinctSourceStaticModelNames -ne 27 -or
   [string]$identity.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
   [int]$identity.assets -ne 8509 -or
   -not [bool]$identity.typeSequenceVerified){
    throw 'Pinned PC 1903 draw, 5-XModel OAT world or 8509-root native model identity changed.'
}
$sourcePath=[string]$source.typedDrawFile
if(-not(Test-Path -LiteralPath $sourcePath -PathType Leaf) -or
   (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ine
        [string]$source.typedDrawSha256){
    throw "Typed original-PC static placement sidecar missing or SHA modified: $sourcePath"
}
$pcDraws=Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json
if([string]$pcDraws.format -ne 'WaW360.T4PcGate8StaticDrawSource.v1' -or
   [int]$pcDraws.pcDrawSize -ne 92 -or
   [int]$pcDraws.pcPlacementSize -ne 52 -or
   [int]$pcDraws.modelPointerOffset -ne 56 -or
   [int]$pcDraws.count -ne 1903 -or @($pcDraws.records).Count -ne 1903){
    throw 'Pinned source PC x86 draw ABI/row count missing.'
}
# This code is executed under Windows PowerShell 5.1's .NET Framework.
# Use BitConverter.* rather than .NET Core-only SingleToUInt32Bits APIs.
$packer=@'
using System;
public static class AztecGate8T4XenonPackedUnitVec
{
    // Exact T4Xenon Common::Vec3PackUnitVec -> pack32 ThirdBased
    // from pinned OpenAssetTools Utils/Pack.cpp, revision 3beaa7c8.
    private static uint PackComponent(float value)
    {
        float encoded = (value - -24624.0939334638f) * 0.0001218318939208984f;
        return BitConverter.ToUInt32(BitConverter.GetBytes(encoded), 0) & 0x3FFu;
    }
    public static uint Pack(float a, float b, float c)
    {
        return PackComponent(a) | (PackComponent(b) << 10) | (PackComponent(c) << 20);
    }
    public static float UnpackComponent(uint bits10)
    {
        uint bits = bits10 & 0x3FFu;
        bits = unchecked(bits - (2u * (bits & 0x200u)) + 0x40400000u);
        float f = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        return (f - 3.0f) * 8208.0312f;
    }
    public static float[] Unpack(uint bits)
    {
        return new float[] {
            UnpackComponent(bits),
            UnpackComponent(bits >> 10),
            UnpackComponent(bits >> 20)
        };
    }
}
'@
Add-Type -TypeDefinition $packer -Language CSharp -ErrorAction Stop
foreach($vector in @(
    @{v=[single]-1;code=[uint32]0x201},
    @{v=[single]0;code=[uint32]0},
    @{v=[single]1;code=[uint32]0x1FF})){
    $p=[AztecGate8T4XenonPackedUnitVec]::Pack($vector.v,[single]0,[single]0)
    $actual=$p -band [uint32]0x3FF
    if($actual -ne $vector.code -or
       [Math]::Abs([double]([AztecGate8T4XenonPackedUnitVec]::UnpackComponent($actual)) -
                   [double]$vector.v) -gt 0.000001){
        throw 'Pinned T4Xenon 10-bit unit-vector reference vectors failed. No candidate data emitted.'
    }
}
function Norm([string]$value){
    $s=$value.Trim().Replace('\','/').TrimStart(',')
    if($s.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){$s=$s.Substring(0,$s.Length-3)}
    return $s.ToLowerInvariant()
}
$lookup=@{}
foreach($row in @($audit.modelDependencyRows)){
    $key=Norm ([string]$row.pcModelName)
    if($lookup.ContainsKey($key) -or @($row.nativeRootIndices).Count -ne 1){
        throw "Original-PC model $key does not have exactly one native identity."
    }
    $nativeRoot=[int]$row.nativeRootIndices[0]
    $roots=@($identity.entries | Where-Object {
        [int]$_.Index -eq $nativeRoot -and
        [string]$_.OatTypeName -eq 'xmodel' -and
        (Norm ([string]$_.Name)) -eq $key
    })
    if($roots.Count -ne 1){throw "Original PC draw model $key has ambiguous native ownership."}
    $lookup[$key]=[pscustomobject]@{
        root=$nativeRoot;count=[int]$row.occurrences
    }
}
if($lookup.Count -ne 27){throw 'Expected 27 distinct original-PC static model native bindings.'}
$real=@{
    'foliage_oki2_tallgrass'=7502
    'foliage_pacific_grass01_large'=7634
    'foliage_cod5_hedgerow04'=7465
}
foreach($name in @($real.Keys)){
    if(-not $lookup.ContainsKey($name) -or $lookup[$name].root -ne $real[$name]){
        throw "Gate8 source-owned real foliage native identity drift: $name"
    }
}
$maxError=0.0
$maxLengthError=0.0
$maxErrorRecord=-1
$outsideUnit=0
$badAxes=0
$knownReal=0
$pending=0
$seen=@{}
$candidates=New-Object 'System.Collections.Generic.List[object]'
$quantErrors=New-Object 'System.Collections.Generic.List[object]'
for($i=0;$i -lt 1903;$i++){
    $d=$pcDraws.records[$i]
    $name=Norm ([string]$d.model)
    if([int]$d.ordinal -ne $i -or -not $lookup.ContainsKey($name) -or
       @($d.origin).Count -ne 3 -or @($d.axis).Count -ne 3){
        throw "PC original static draw transform/native name mismatch at ordinal $i"
    }
    $isReal=$real.ContainsKey($name)
    if($isReal){$knownReal++}else{$pending++}
    if(-not $seen.ContainsKey($name)){$seen[$name]=0}
    $seen[$name]++
    $encoded=New-Object 'System.Collections.Generic.List[string]'
    $rowMax=0.0
    for($axisOrdinal=0;$axisOrdinal -lt 3;$axisOrdinal++){
        $axis=@($d.axis[$axisOrdinal])
        if($axis.Count -ne 3){
            throw "Malformed original-PC 3x3 placement at ordinal $i, axis $axisOrdinal."
        }
        $x=[single]$axis[0];$y=[single]$axis[1];$z=[single]$axis[2]
        foreach($v in @($x,$y,$z)){
            if([single]::IsNaN($v) -or [single]::IsInfinity($v)){
                throw "Nonfinite original-PC static draw axis at record $i."
            }
            if([Math]::Abs([double]$v) -gt 1.01){$outsideUnit++}
        }
        $len=[Math]::Sqrt([double]$x*$x+[double]$y*$y+[double]$z*$z)
        $lenError=[Math]::Abs($len-1.0)
        if($lenError -gt $maxLengthError){$maxLengthError=$lenError}
        if($lenError -gt $MaxSourceAxisLengthError){$badAxes++}
        $p=[AztecGate8T4XenonPackedUnitVec]::Pack($x,$y,$z)
        $u=[AztecGate8T4XenonPackedUnitVec]::Unpack($p)
        $err=[Math]::Max([Math]::Abs([double]$u[0]-$x),
             [Math]::Max([Math]::Abs([double]$u[1]-$y),[Math]::Abs([double]$u[2]-$z)))
        if($err -gt $rowMax){$rowMax=$err}
        if($err -gt $maxError){$maxError=$err;$maxErrorRecord=$i}
        $encoded.Add(('0x{0:X8}' -f $p))
    }
    # Candidate is ONLY the three packed rotation words plus stable XModel
    # native root identity. It is NOT a 60B game record: the Xenon
    # lightingHandle/padding semantics have not been reconstructed.
    $candidates.Add([pscustomobject]@{
        ordinal=$i
        model=[string]$d.model
        nativeModelRoot=[int]$lookup[$name].root
        realModelBodyPresentInFiveXmodelFf=$isReal
        axisPackedWords=@($encoded.ToArray())
        maxComponentRoundTripError=$rowMax
    })
}
foreach($kv in @($lookup.Keys)){
    if(-not $seen.ContainsKey($kv) -or
       [int]$seen[$kv] -ne [int]$lookup[$kv].count){
        throw "PC static draw ordinal/name histogram changed for $kv."
    }
}
if($knownReal -ne 1572 -or $pending -ne 331){
    throw "Five-model scratch FF source model placement counts changed: real=$knownReal shells=$pending."
}
New-Item -ItemType Directory -Force -Path $out | Out-Null
$candidateDoc=[ordered]@{
    schema='WaW360.AztecGate8XenonPackedStaticDrawAxes.v1'
    status='AZTEC_GATE8_PC_STATIC_DRAW_PACKED_AXIS_CANDIDATES_SOURCE_ONLY'
    packer='T4Xenon.Common::Vec3PackUnitVec = pack32::Vec3PackUnitVecThirdBased'
    pinnedOatCommit='3beaa7c81af517f0c05f2b4612d477ed1541b676'
    sourceDrawsSha256=[string]$source.typedDrawSha256
    records=@($candidates.ToArray())
    originalPcModelPointerBytesCopied=$false
    xboxLightingHandleSemanticsVerified=$false
    xboxStaticDrawRecordBytesEmitted=$false
    hardwareReady=$false
}
$candidateDoc|ConvertTo-Json -Depth 7|Set-Content -LiteralPath $entriesPath -Encoding UTF8
$allPass=$maxError -le $MaxPackedComponentError -and
    $badAxes -eq 0 -and $outsideUnit -eq 0
$result=[ordered]@{
    schema='WaW360.AztecGate8XenonStaticDrawAxisPreflight.v1'
    status=if($allPass){'AZTEC_GATE8_1903_PC_PLACEMENT_XENON_PACKED_AXES_PREFLIGHT_PASS'}else{'AZTEC_GATE8_PC_PLACEMENT_AXES_NEED_SOURCE_ABI_REVIEW'}
    sourceDrawReport=$sourceProofPath
    originalPcDrawSourceSha256=[string]$source.typedDrawSha256
    verifiedFiveRealXModelWorldOatReport=$fivePath
    nativeIdentityManifest=$identityPath
    packedAxesReport=$entriesPath
    originalPcDrawCount=1903
    distinctOriginalPcModels=27
    modelNamesWithRealBodiesInFiveRootWorldFf=3
    placementOccurrencesWithRealModelBodies=1572
    placementOccurrencesWithOnlyNativeIdentity=331
    encodedT4XenonUnitAxisVectors=5709
    largestComponentQuantizationError=$maxError
    largestComponentErrorRecord=$maxErrorRecord
    maxOriginalPcAxisLengthDeviation=$maxLengthError
    sourceAxesOverLengthTolerance=$badAxes
    sourceAxisComponentsOutsideUnitRange=$outsideUnit
    maximumPermittedComponentError=$MaxPackedComponentError
    maximumPermittedSourceAxisLengthError=$MaxSourceAxisLengthError
    packedAxisCodecBasedOnPinnedT4XenonOat=$true
    pcModelPointerBytesCopied=$false
    xenonLightingHandleMappingProven=$false
    xenonGfxStaticModelDrawInstABIAndTailProven=$false
    xenonDrawRecordsWritten=0
    worldGfxStaticModelDrawInstsIntegrated=0
    hardwareReady=$false
    note='All 1903 source draw placements mapped by root identity. Exact pinned T4Xenon pack/unpack codec exercised for 3x3 axes. Candidate output is ONLY per-axis packed words and native root identities, not complete Xenon draw bytes; lighting handle, flags, runtime bytes and pointer binding remain unverified.'
}
$result|ConvertTo-Json -Depth 7|Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Host '[AZTEC-GATE8-AXES] =================================================='
Write-Host "[AZTEC-GATE8-AXES] status=$($result.status)"
Write-Host "[AZTEC-GATE8-AXES] source typed draws=1903, distinct models=27, packed axes=5709"
Write-Host "[AZTEC-GATE8-AXES] proven model bodies=1572 placements, remaining shells=331"
Write-Host ('[AZTEC-GATE8-AXES] maxPackedComponentError={0:G9} (limit={1:G9})' -f $maxError,$MaxPackedComponentError)
Write-Host ('[AZTEC-GATE8-AXES] maxSourceAxisLengthError={0:G9} (limit={1:G9})' -f $maxLengthError,$MaxSourceAxisLengthError)
Write-Host "[AZTEC-GATE8-AXES] badSourceAxes=$badAxes outsideUnitComponents=$outsideUnit"
Write-Host "[AZTEC-GATE8-AXES] candidates=$entriesPath"
Write-Host "[AZTEC-GATE8-AXES] report=$reportPath"
Write-Host '[AZTEC-GATE8-AXES] AXES ONLY. NO MODEL POINTERS, DRAW FF, HARDWARE.'
if(-not $allPass){throw 'Original-PC to pinned T4Xenon axis-packing tolerance requires source review.'}
