[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# ONE no-build, SHA-pinned, source-owned PC little-endian to Xenon-native
# leaf batch conversion. Never imports Xbox reference payloads; only
# references the original Xbox ABI sizeof/offsetof witness.
# This is NOT a complete GfxWorld or native FF build: image/material asset
# rebinding, Xbox streaming metadata and shadow nested graphs remain gated.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$src=Join-Path $base 'pc_g11_cells_740\gate11_remaining_unified_source'
$sourceManifest=Join-Path $src 'aztec_gate11_remaining_world_batch_source_gate.json'
$out=Join-Path $src 'native_leaf_batch'
$report=Join-Path $out 'aztec_gate11_native_leaf_batch_conversion.json'
$expectedGate11SHA='05CEEC82AF5D58706BA5A6EE24995D09A3FB764F3E547A8B4758A9347ECF2862'
$expectedGate10SHA='0BB41398C4236EF33CB9D60F923E09152966D52AA623DAEFC33D731A42630564'
if(-not(Test-Path -LiteralPath $sourceManifest -PathType Leaf)){
    throw "First run the already-proven 8-family unified source census; report absent: $sourceManifest"
}
$s=Get-Content -LiteralPath $sourceManifest -Raw|ConvertFrom-Json
if([string]$s.schema -ne 'WaW360.AztecGate11SingleBatchRemainingWorldSource.v1' -or
   [string]$s.status -ne 'AZTEC_GATE11_ALL_REMAINING_WORLD_SOURCE_CORPUS_CAPTURED_FF_BATCH_NOT_YET_APPROVED' -or
   [int]$s.sourceFamilyCount -ne 8 -or
   [long]$s.originalPcSourceLeafByteCount -ne 6930524L -or
   [string]$s.protectedPreviousGate11FfSha256 -ine $expectedGate11SHA -or
   [bool]$s.actualMapFfBuildPerformed -or
   [bool]$s.singleCombinedFfReadyToEmit -or
   [bool]$s.hardwareReady -or
   [bool]$s.runtimeOnlyBuffersCopyAllowed -or
   -not [bool]$s.imageAndMaterialRootRelocationStillNeeded -or
   -not [bool]$s.xenonOnlyWorldStreamingMetadataStillNeeded){
    throw 'Cannot convert leaves from an incomplete, hardware-approved, or unverified source graph.'
}
$ff=[string]$s.protectedPreviousGate11Ff
$gate10=Join-Path $base 'pc_g10_1903\oat_loaded_1903_draw_readback\aztec_gate10_independent_1903_draw_readback.json'
foreach($path in @($ff,$gate10,[string]$s.sourceTypedRemainderGraph,[string]$s.nativeXboxAbiOnlyFile)){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Source provenance or prior proved FF missing: $path"
    }
}
$g10=Get-Content -LiteralPath $gate10 -Raw|ConvertFrom-Json
$originalFFSHA=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
if($originalFFSHA -ine $expectedGate11SHA -or
   [string]$g10.stage4EEmittedFfSha256 -ine $expectedGate10SHA -or
   (Get-FileHash -LiteralPath ([string]$g10.stage4EEmittedFf) -Algorithm SHA256).Hash -ine $expectedGate10SHA -or
   (Get-FileHash -LiteralPath ([string]$s.sourceTypedRemainderGraph) -Algorithm SHA256).Hash -ine
     [string]$s.sourceTypedRemainderGraphSha256 -or
   (Get-FileHash -LiteralPath ([string]$s.nativeXboxAbiOnlyFile) -Algorithm SHA256).Hash -ine
     [string]$s.nativeXboxAbiOnlySha256){
    throw 'Gate10, Gate11, original PC source, or native ABI SHA differs from the proven baseline.'
}
$abi=Get-Content -LiteralPath ([string]$s.nativeXboxAbiOnlyFile) -Raw|ConvertFrom-Json
if([string]$abi.schema -ne 'WaW360.T4XenonGate11RemainingWorldNativeAbi.v1' -or
   [int]$abi.gfxWorldBytes -ne 900 -or
   [bool]$abi.containsOriginalXboxPayload){
    throw 'A source-only measured Xbox struct ABI is required; native Xbox payloads are forbidden.'
}

# Offsets are within each TYPE-checked original-PC leaf record.
# All 4B offsets are float32/u32 (reversible byte swapping); 2B offsets
# are original-PC u16. Never swap graphics byte colors/flags/encoded rows.
# If PC/Xenon sizeof differs, STOP instead of generic byte grafting.
$rules=[ordered]@{
    skyStartSurfs=@{bytes=4;word4=@(0);word2=@()}
    coronas=@{bytes=32;word4=@(0,4,8,12,16,20,24,28);word2=@()}
    brushModels=@{bytes=56;word4=@(0,4,8,12,16,20,24,28,32,36,40,44,48,52);word2=@()}
    cullGroups=@{bytes=32;word4=@(0,4,8,12,16,20,24,28);word2=@()}
    worldLodChains=@{bytes=24;word4=@(0,4,8,12,16);word2=@(20)}
    worldLodInfos=@{bytes=12;word4=@(0,4);word2=@(8)}
    worldLodSurfaces=@{bytes=4;word4=@(0);word2=@()}
    gridRowDataStart=@{bytes=2;word4=@();word2=@(0)}
    gridRawRowData=@{bytes=1;word4=@();word2=@()}
    gridEntries=@{bytes=4;word4=@();word2=@(0)}
    gridColors=@{bytes=168;word4=@();word2=@()}
    waterBuffer0=@{bytes=16;word4=@(0,4,8,12);word2=@()}
    waterBuffer1=@{bytes=16;word4=@(0,4,8,12);word2=@()}
}
if(-not ('WaWWorldEndianBatch' -as [type])){
    Add-Type -TypeDefinition @'
using System;
public static class WaWWorldEndianBatch
{
    // Endian conversion is an involution: convert(convert(source)) == source.
    // No host pointers, no Xbox oracle payloads, no serializer shortcuts.
    public static byte[] Convert(byte[] src, int stride, int[] four, int[] two)
    {
        if(src==null || stride<1 || src.Length%stride!=0)
            throw new ArgumentException("Input records are not stride-aligned.");
        var used = new bool[stride];
        foreach(int off in four)
        {
            if(off<0 || off+4>stride)
                throw new ArgumentException("4-byte source field out of bounds.");
            for(int j=0;j<4;j++)
            {
                if(used[off+j])throw new ArgumentException("Source endian fields overlap.");
                used[off+j]=true;
            }
        }
        foreach(int off in two)
        {
            if(off<0 || off+2>stride)
                throw new ArgumentException("2-byte source field out of bounds.");
            for(int j=0;j<2;j++)
            {
                if(used[off+j])throw new ArgumentException("Source endian fields overlap.");
                used[off+j]=true;
            }
        }
        var output=(byte[])src.Clone();
        for(int i=0;i<src.Length;i+=stride)
        {
            foreach(int off in four)
            {
                int j=i+off;
                output[j]=src[j+3];
                output[j+1]=src[j+2];
                output[j+2]=src[j+1];
                output[j+3]=src[j];
            }
            foreach(int off in two)
            {
                int j=i+off;
                output[j]=src[j+1];
                output[j+1]=src[j];
            }
        }
        return output;
    }
    public static bool Exact(byte[] x, byte[] y)
    {
        if(x==null || y==null || x.Length!=y.Length)return false;
        for(int i=0;i<x.Length;i++)
            if(x[i]!=y[i])return false;
        return true;
    }
}
'@
}
New-Item -ItemType Directory -Path $out -Force|Out-Null
Write-Host '[AZTEC-GATE11-LEAF] ================================================================'
Write-Host '[AZTEC-GATE11-LEAF] One source batch: 13 original-PC leaf families => verified Xenon BE.'
Write-Host '[AZTEC-GATE11-LEAF] No OAT, .NET build, Xbox donor bytes, or FF emission.'
$converted=[ordered]@{}
$totalSourceBytes=0L;$totalOutputBytes=0L;$totalRecords=0L
foreach($name in @($rules.Keys)){
    $rule=$rules[$name]
    $entry=$s.originalPcLeafFiles.$name
    $sourceFile=[string]$entry.file
    $sourceSize=[int]$entry.elementBytes
    $nativeSize=[int]$entry.measuredNativeElementBytes
    $count=[long]$entry.count
    if([bool]$entry.rawPointersCopied -or
       [bool]$entry.requiresCrossAbiRepacking -or
       $sourceSize -ne [int]$rule.bytes -or
       $nativeSize -ne [int]$rule.bytes -or
       $count -lt 0 -or $count -gt 2000000L -or
       [string]$entry.endian -ne 'ORIGINAL_PC_LITTLE_ENDIAN_REQUIRES_NATIVE_CONVERSION'){
        throw "Source leaf $name is missing, contains pointers, or requires unsupported cross-ABI field repacking. Abort."
    }
    if(-not(Test-Path -LiteralPath $sourceFile -PathType Leaf) -or
       (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash -ine
           [string]$entry.sha256){
        throw "Original-PC verified source $name SHA drifted."
    }
    $bytes=[IO.File]::ReadAllBytes($sourceFile)
    if($bytes.Length -ne [long]$entry.byteLength -or
       $bytes.Length -ne $count*$sourceSize){
        throw "Original-PC leaf $name length/count mismatch."
    }
    $fours=[int[]]@($rule.word4)
    $twos=[int[]]@($rule.word2)
    $convertedBytes=[WaWWorldEndianBatch]::Convert($bytes,$nativeSize,$fours,$twos)
    $reversed=[WaWWorldEndianBatch]::Convert($convertedBytes,$nativeSize,$fours,$twos)
    if(-not [WaWWorldEndianBatch]::Exact($bytes,$reversed)){
        throw "Original-PC $name endian conversion failed independent byte-for-byte round trip."
    }
    $dst=Join-Path $out ('aztec_'+$name+'_xenon_BE.bin')
    [IO.File]::WriteAllBytes($dst,$convertedBytes)
    $converted[$name]=[ordered]@{
        sourceOwned=$true
        sourceFile=$sourceFile
        sourceSha256=[string]$entry.sha256
        xenonNativeEndianFile=$dst
        xenonNativeEndianSha256=(Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash
        sourceRecordCount=$count
        sourceAndNativeStructBytes=$nativeSize
        fourByteFieldsPerRecord=$fours.Length
        twoByteFieldsPerRecord=$twos.Length
        byteCount=[long]$convertedBytes.Length
        endianRoundTripExact=$true
        pointerFieldsPresent=$false
        xassetReferencesRebased=$false
        containedXboxOraclePayload=$false
        hardwareReady=$false
    }
    $totalSourceBytes += $bytes.Length
    $totalOutputBytes += $convertedBytes.Length
    $totalRecords += $count
    Write-Host "[AZTEC-GATE11-LEAF] $name records=$count bytes=$($bytes.Length) BE roundtrip=PASS"
}
if($converted.Count -ne 13 -or $totalSourceBytes -ne 6930524L -or
   $totalOutputBytes -ne $totalSourceBytes){
    throw '13 typed source arrays and 6,930,524 original PC bytes must be accounted exactly.'
}
if((Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash -ine $originalFFSHA){
    throw 'CRITICAL: passing independently OAT-verified Gate11 FF changed.'
}
$result=[ordered]@{
    schema='WaW360.AztecGate11SourceOwnedNativeEndianLeafBatch.v1'
    status='AZTEC_GATE11_13_SOURCE_LEAF_XENON_ENDIAN_CONVERSION_PASS_FULL_WORLD_UNPROVEN'
    sourceReadinessReport=$sourceManifest
    protectedIndependentGate11Ff=$ff
    protectedIndependentGate11FfSha256=$originalFFSHA
    priorIndependentOatGfxCellCount=11
    priorIndependentOatAabbTreeCount=740
    sourceByteCount=$totalSourceBytes
    nativeCandidateByteCount=$totalOutputBytes
    totalLeafRecords=$totalRecords
    families=13
    leaves=$converted
    nativeImageRootReferencesResolved=$false
    nativeMaterialRootReferencesResolved=$false
    nativeStreamingInfoReconstructed=$false
    nestedShadowRegionSourceGraphSerialized=$false
    allWorldChildrenComplete=$false
    newNativeFfEmitted=$false
    gate11OverallPassed=$false
    xboxHardwareApproved=$false
}
$result|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $report -Encoding UTF8
Write-Host "[AZTEC-GATE11-LEAF] SOURCE->NATIVE PASS 13/13 leaf families, $totalSourceBytes bytes, $totalRecords records."
Write-Host "[AZTEC-GATE11-LEAF] Combined native-endian report=$report"
Write-Host '[AZTEC-GATE11-LEAF] Next: rebase source Image/Material names and native world stream metadata; ONE future isolated FF OAT build.'
Write-Host '[AZTEC-GATE11-LEAF] Gate11 OVERALL PENDING, Xbox hardware FALSE. No FF rebuilt.'
