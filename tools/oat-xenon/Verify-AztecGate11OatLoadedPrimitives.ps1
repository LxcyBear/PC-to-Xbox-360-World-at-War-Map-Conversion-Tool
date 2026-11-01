[CmdletBinding()]
param([switch]$BuildDumper)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# One independent read-only OAT reload of the EXISTING new 3,650,170-byte
# source-owned Gate11 primitive FF. Not another .NET build or FF emission.
# Verify every byte of all four loaded, pointer-free x86-native scalar arrays
# against SHA-verified ORIGINAL PC little-endian inputs (NOT Xbox donor data).
# The native OAT dumper emits only 64-bit digests, never payload bytes.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$work=Join-Path $base 'pc_g11_remaining_world_primitive_probe'
$preflight=Join-Path $base 'pc_g11_cells_740\gate11_remaining_unified_source\aztec_gate11_combined_no_ff_build_preflight.json'
$report=Join-Path $work 'oat_loaded_primitive_source_digest\aztec_gate11_independent_loaded_primitive_source_digest.json'
$ffReport=Join-Path $work 'aztec_pc_world_core_integrated_oat_run.json'
$cellReport=Join-Path $work 'oat_loaded_11_cell_aabb_readback\aztec_gate11_independent_cells_aabb_readback.json'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$capFile=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$buildScript=Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1'
$expectedNew='D46ABC8E5681E4F235ED82821016F6F0002505AD0DEF68D680D70FCCE75F1FCB'
$expectedOld='05CEEC82AF5D58706BA5A6EE24995D09A3FB764F3E547A8B4758A9347ECF2862'
foreach($path in @($preflight,$ffReport,$cellReport,$buildScript)){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Existing Gate11 native candidate/readback prerequisite missing: $path"}
}
$p=Get-Content -LiteralPath $preflight -Raw|ConvertFrom-Json
$m=Get-Content -LiteralPath $ffReport -Raw|ConvertFrom-Json
$c=Get-Content -LiteralPath $cellReport -Raw|ConvertFrom-Json
if([string]$p.schema -ne 'WaW360.AztecGate11CombinedNativeOneBuildPreparation.v1' -or
   [string]$p.status -ne 'AZTEC_GATE11_SOURCE_LEAF_13_NATIVE_ENDIAN_PASS_IMAGE_IDENTITIES_AUDITED_FULL_FF_BLOCKED' -or
   [string]$p.previousPassingGate11FfSha256 -ine $expectedOld -or
   [int]$p.originalSourceLeafArraysConverted -ne 13 -or
   [long]$p.nativeEndianLeafBytes -ne 6930524L -or
   -not [bool]$p.endiannessRoundTripProven -or
   [bool]$p.fullGate11Passed -or [bool]$p.XboxHardwareReady -or
   [string]$m.status -ne 'AZTEC_GATE11_PRIMITIVE_SKY_BRUSH_WATER_OAT_LOAD_PASS_REMAINDER_PENDING' -or
   -not [bool]$m.OatPass -or [bool]$m.hardwareReady -or
   [int]$m.gate11PrimitiveSkySurfaces -ne 2 -or
   [int]$m.gate11PrimitiveBrushModels -ne 183 -or
   [long]$m.gate11PrimitiveWaterBytes -ne 2097408L -or
   [string]$c.status -ne 'AZTEC_GATE11_PHYSICAL_11_CELL_740_AABB_SOURCE_FIELDS_INDEPENDENT_OAT_PASS_REMAINDER_PENDING' -or
   [string]$c.actualGfxWorldFfSha256 -ine $expectedNew -or
   -not [bool]$c.sourceOwnedPrimitiveCandidate -or
   [bool]$c.primitiveSkyBrushWaterTypedReadbackProven -or
   [int]$c.exactLoadedCounts.cellOrdinals -ne 11 -or
   [int]$c.exactLoadedCounts.treeOrdinals -ne 740 -or
   [int]$c.exactLoadedCounts.treeModelIndexes -ne 14974){
    throw 'Gate11 new candidate has not passed first/second OAT checks or its provenance is not SHA-locked.'
}
$ff=[string]$m.outputFastFile
$old=[string]$p.previousPassingGate11Ff
foreach($path in @($ff,$old)){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Source-owned/previous native FF missing: $path"}
}
if([IO.Path]::GetFullPath($ff) -ieq [IO.Path]::GetFullPath($old) -or
   (Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash -ine $expectedNew -or
   (Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash -ine $expectedOld -or
   [long](Get-Item -LiteralPath $ff).Length -ne 3650170L -or
   [long](Get-Item -LiteralPath $old).Length -ne 3642932L){
    throw 'Native FF SHA/size changed; refuse an unrelated OAT load.'
}

# A cached specialized OAT may already contain this new read-only witness.
# The first run uses -BuildDumper once to install its C++ digest capability.
function Test-Capability {
    if(-not(Test-Path -LiteralPath $oat -PathType Leaf) -or
       -not(Test-Path -LiteralPath $capFile -PathType Leaf)){return $false}
    $cap=Get-Content -LiteralPath $capFile -Raw|ConvertFrom-Json
    return ([string]$cap.format -eq 'WaW360.OatGraphDumperCapabilities.v1' -and
        [string]$cap.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($cap.capabilities) -contains 't4xenon-gfxworld-gate11-loaded-primitive-digest-v1')
}
if(-not(Test-Capability)){
    if(-not $BuildDumper){
        throw 'New native loaded-primitive digest witness missing. Run THIS script once with -BuildDumper. No map FF rebuild.'
    }
    Write-Host '[AZTEC-GATE11-PRIMITIVE-READBACK] One-time specialized x86 OAT rebuild for independent loaded primitive digests.'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $buildScript
    if($LASTEXITCODE -ne 0 -or -not(Test-Capability)){
        throw 'Pinned OAT loaded-primitive digest capability build FAILED. No map FF modified.'
    }
}

if(-not ('AztecGate11Fnv64' -as [type])){
    Add-Type -TypeDefinition @'
using System;
using System.Globalization;
public static class AztecGate11Fnv64
{
    public static string Digest(byte[] data)
    {
        unchecked
        {
            ulong h=14695981039346656037UL;
            foreach(byte b in data)
            {
                h ^= b;
                h *= 1099511628211UL;
            }
            return h.ToString("x16",CultureInfo.InvariantCulture);
        }
    }
}
'@
}
$leafFile=[string]$p.nativeEndiannessLeafReport
if(-not(Test-Path -LiteralPath $leafFile -PathType Leaf)){throw "13-family original-PC batch report missing: $leafFile"}
$l=Get-Content -LiteralPath $leafFile -Raw|ConvertFrom-Json
if([string]$l.status -ne 'AZTEC_GATE11_13_SOURCE_LEAF_XENON_ENDIAN_CONVERSION_PASS_FULL_WORLD_UNPROVEN' -or
   [int]$l.families -ne 13 -or
   [long]$l.nativeCandidateByteCount -ne 6930524L -or
   [string]$l.protectedIndependentGate11FfSha256 -ine $expectedOld -or
   [bool]$l.newNativeFfEmitted -or [bool]$l.xboxHardwareApproved){
    throw 'The original-PC endian conversion source report has changed.'
}
$sourceDigest=[ordered]@{}
foreach($entry in @(
    @{key='skyStartSurfs';count=2;stride=4},
    @{key='brushModels';count=183;stride=56},
    @{key='waterBuffer0';count=65544;stride=16},
    @{key='waterBuffer1';count=65544;stride=16}
)){
    $name=[string]$entry.key
    $src=$l.leaves.$name
    $file=[string]$src.sourceFile
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Original PC byte source missing for $name : $file"}
    $bytes=[IO.File]::ReadAllBytes($file)
    if([long]$bytes.Length -ne ([long]$entry.count*[long]$entry.stride) -or
       [long]$src.sourceRecordCount -ne [long]$entry.count -or
       [int]$src.sourceAndNativeStructBytes -ne [int]$entry.stride -or
       -not [bool]$src.endianRoundTripExact -or
       -not [bool]$src.sourceOwned -or
       [bool]$src.pointerFieldsPresent -or [bool]$src.containedXboxOraclePayload -or
       (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine [string]$src.sourceSha256){
        throw "Original-PC source/endian provenance FAIL for $name"
    }
    $sourceDigest[$name]=[AztecGate11Fnv64]::Digest($bytes)
}
Write-Host '[AZTEC-GATE11-PRIMITIVE-READBACK] ==================================================================='
Write-Host '[AZTEC-GATE11-PRIMITIVE-READBACK] No .NET build, NO MAP FF BUILD: re-load OAT and compare all 2,107,664 source-owned bytes.'
Write-Host '[AZTEC-GATE11-PROGRESS] [#####---------------] 25% | SHA-authenticated original-PC source leaf digests'
$temp=Join-Path ([IO.Path]::GetTempPath()) ('waw_g11prim_'+[Guid]::NewGuid().ToString('N').Substring(0,10))
$out=Split-Path -Parent $report
$log=Join-Path $out 'oat_loaded_primitives.log'
New-Item -ItemType Directory -Path $temp,$out -Force|Out-Null
try{
    $exitCode=-1
    $save=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $oat '--no-color' '--include-assets' 'gfxworld' '--output-folder' $temp $ff 2>&1|
            ForEach-Object {[string]$_}|Set-Content -LiteralPath $log -Encoding UTF8
        $exitCode=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$save}
    if($exitCode -ne 0){
        Get-Content -LiteralPath $log -Tail 35|
            ForEach-Object {Write-Host "[AZTEC-GATE11-PRIMITIVE-READBACK] OAT $_"}
        throw "Independent loaded-primitive OAT exit=$exitCode. No Xbox approval."
    }
    $witnesses=@(Get-ChildItem -LiteralPath $temp -Recurse -File -Filter '*.json' |
        Where-Object {$_.FullName -match 'xenon_gfxworld_surface_abi_witness'})
    if($witnesses.Count -ne 1){
        throw "Expected one native typed GfxWorld witness, found $($witnesses.Count)"
    }
    $loaded=Get-Content -LiteralPath $witnesses[0].FullName -Raw|ConvertFrom-Json
    $native=$loaded.nativePrimitiveGraph
    if([string]$loaded.schema -ne 'WaW360.T4XenonGfxWorldSurfaceAbiWitness.v1' -or
       [string]$native.schema -ne 'WaW360.T4XenonLoadedGate11PrimitiveDigest.v1' -or
       [string]$native.digest -ne 'FNV1A64_LE_HOST_MEMORY_OF_TYPED_LOADED_FIELDS' -or
       -not [bool]$native.arraysAvailable -or
       -not [bool]$native.loadedHostMemoryIsLittleEndian -or
       [int]$native.skySurfCount -ne 2 -or
       [int]$native.brushModelCount -ne 183 -or
       [int]$native.skySourceFieldBytes -ne 8 -or
       [int]$native.brushSourceFieldBytes -ne 10248 -or
       [int]$native.water0Bytes -ne 1048704 -or
       [int]$native.water1Bytes -ne 1048704 -or
       -not [bool]$native.skyPointerPresent -or
       -not [bool]$native.brushPointerPresent -or
       -not [bool]$native.water0PointerPresent -or
       -not [bool]$native.water1PointerPresent -or
       [bool]$native.sourcePayloadDonorCopied -or
       [bool]$native.hardwareRuntimeSemanticsProven){
        throw 'Independent OAT loader did not populate all expected primitive native fields.'
    }
    Write-Host '[AZTEC-GATE11-PROGRESS] [##############------] 70% | compare every sky/brush/water byte from newly loaded native structures'
    $names=[ordered]@{
        skyStartSurfs=[string]$native.skyDigest
        brushModels=[string]$native.brushDigest
        waterBuffer0=[string]$native.water0Digest
        waterBuffer1=[string]$native.water1Digest
    }
    $passed=0
    foreach($name in @($names.Keys)){
        $loadedDigest=$names[$name]
        $srcDigest=$sourceDigest[$name]
        if($loadedDigest -notmatch '^[0-9a-fA-F]{16}$' -or
           $loadedDigest -ine $srcDigest){
            throw "Source/native mismatch $name : original PC FNV=$srcDigest, loaded Xbox FNV=$loadedDigest"
        }
        $passed++
        Write-Host "[AZTEC-GATE11-PRIMITIVE-READBACK] MATCH original-PC $name FNV64=$loadedDigest"
    }
    if($passed -ne 4){throw 'Four loaded native primitive groups not independently matched.'}
    $readback=[ordered]@{
        schema='WaW360.AztecGate11IndependentLoadedPrimitiveSourceDigest.v1'
        status='AZTEC_GATE11_SOURCE_OWNED_SKY_BRUSH_WATER_NATIVE_LOADED_DIGEST_PASS_REMAINDER_PENDING'
        actualNewNativeFf=$ff
        actualNewNativeFfSha256=$expectedNew
        actualNewNativeFfBytes=3650170
        untouchedPriorGate11FfSha256=$expectedOld
        priorCellsAndTreesIndependentlyOatVerified=$true
        leafGroupsWithExactLoadedSourceDigests=$passed
        sourceUncompressedBytesCovered=2107664
        sourceLittleEndianDigests=$sourceDigest
        nativeLoadedHostEndianDigests=$names
        nativeTypedLoadedPrimitiveWitness=[string]$witnesses[0].FullName
        nativeImagePayloadSemanticsProven=$false
        nativeStreamingMetadataProven=$false
        fullGate11Passed=$false
        xboxHardwareReady=$false
    }
    # Save only hashes and OAT proof, no Xbox reference or new FF payload bytes.
    $readback.nativeTypedLoadedPrimitiveWitness='OAT loaded typed native witness (temporary; deleted after comparison)'
    $readback|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $report -Encoding UTF8
    if((Get-FileHash -LiteralPath $old -Algorithm SHA256).Hash -ine $expectedOld -or
       (Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash -ine $expectedNew){
        throw 'CRITICAL: protected previous/new candidate FF modified during readback.'
    }
    Write-Host '[AZTEC-GATE11-PROGRESS] [####################] 100% | 4/4 loaded primitive digests match ORIGINAL PC source'
    Write-Host "[AZTEC-GATE11-PRIMITIVE-READBACK] PASS 4/4, all 2,107,664 source-owned bytes match native loaded primitive memory."
    Write-Host "[AZTEC-GATE11-PRIMITIVE-READBACK] Report=$report"
    Write-Host '[AZTEC-GATE11-PRIMITIVE-READBACK] Gate11 full world closure STILL PENDING. Xbox NOT approved.'
}finally{
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
