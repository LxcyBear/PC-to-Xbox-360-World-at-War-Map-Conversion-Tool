[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# Forensic interpretation of real v3 ORIGINAL PC loaded material-packed
# target bytes. Avoid guessing Material*, force binding or chasing beyond
# actual OAT-loaded source memory. No OAT/MSBuild/FF mutations.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$source=Join-Path $base 'pc_g11_flame_loaded_virtual_target_proof\aztec_five_original_pc_material_pointer_cells_and_real_xbox_xma_sprint.json'
$output=Join-Path $base 'pc_g11_flame_loaded_virtual_target_proof\aztec_five_original_pc_real_material_memory_words.json'
$goodFf=Join-Path $repo '.deps\aztec-w63-direct-ff-growth\scratch_emit\nazi_zombie_aztec.ff'
foreach($f in @($source,$goodFf)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){
        throw "Verified real original-PC source word evidence missing: $f"
    }
}
$j=Get-Content -LiteralPath $source -Raw|ConvertFrom-Json
if([string]$j.schema -ne 'WaW360.AztecFiveFlameMaterialSlotRealPcSourceAndXboxXmaSprint.v3' -or
   [int]$j.originalFlameTableParents -ne 4 -or
   [int]$j.originalPcFlameMaterialPackedReferences -ne 17 -or
   [int]$j.uniqueMaterialPointerTargets -ne 5 -or
   [int]$j.provenMaterial112OriginalPcNameCandidates -ne 0 -or
   [bool]$j.originalOatGoodFFModified -or
   [bool]$j.xboxHardwareReady -or
   @($j.sourceMaterialTargets).Count -ne 5 -or
   (Get-FileHash -LiteralPath $goodFf -Algorithm SHA256).Hash -ine
       [string]$j.originalNativeFFSha256){
    throw 'Five original-PC material-packed source targets and old native test FF changed.'
}
$rows=New-Object 'System.Collections.Generic.List[object]'
$points=@{}
$total=0
foreach($item in @($j.sourceMaterialTargets)){
    $raw=[string]$item.rawOriginalPcMaterialPackedPointer
    $hex=[string]$item.actualSourceHeader32Hex
    if($raw -notmatch '^0x[0-9A-Fa-f]{8}$' -or
       $hex -notmatch '^[0-9A-Fa-f]{64}$' -or
       [int]$item.materialHeaderProvenAtPointerDepth -ne -1){
        throw "Original PC packed pointer or 32 actual memory bytes not real/complete: $raw"
    }
    if($points.ContainsKey($raw)){throw "Duplicate original FlameMaterial pointer $raw"}
    $points[$raw]=$true
    $words=New-Object 'System.Collections.Generic.List[object]'
    $bytes=New-Object byte[] 32
    for($i=0;$i -lt 32;$i++){
        $bytes[$i]=[Convert]::ToByte($hex.Substring($i*2,2),16)
    }
    for($i=0;$i -lt 8;$i++){
        $value=[BitConverter]::ToUInt32($bytes,$i*4)
        # These are hypothetical address shapes, not proven material
        # pointers or asset identities. Report rather than dereference.
        $looksSerialized=($value -gt 0 -and $value -lt 4294967294L)
        $shifted=if($looksSerialized){[long]$value - 1L}else{0L}
        $words.Add([pscustomobject][ordered]@{
            wordIndex=$i
            byteOffset=('{0:X2}' -f ($i*4))
            littleEndianWord=('0x{0:X8}' -f $value)
            originalPcPackedBlockCandidate=if($looksSerialized){[int](($shifted -shr 29) -band 7)}else{$null}
            originalPcPackedOffsetCandidate=if($looksSerialized){('0x{0:X8}' -f ($shifted -band 0x1FFFFFFF))}else{$null}
            typedMaterialPointerVerified=$false
        })
    }
    $count=[int]$item.materialReferenceOccurrences
    if($count -lt 1){throw "Original PC FlameMaterial source field ownership missing $raw"}
    $total+=$count
    $rows.Add([pscustomobject][ordered]@{
        sourcePackedFlameMaterialPointer=$raw
        originalPcFieldsReferencingTarget=$count
        actualOriginalPcOatLoadedFirst32Bytes=$hex
        firstEightActualLittleEndian32BitMemoryWords=@($words.ToArray())
        originalPcMaterial112HeaderNameVerified=$false
        fullyTypedMaterialBodyAndChildrenAvailable=$false
        XboxNativeMaterialGraphReady=$false
    })
    $firstWords=@($words.ToArray()|Select-Object -First 4|ForEach-Object {$_.littleEndianWord})
    Write-Host "[AZTEC-FLAME-CELLS] $raw references=$count first32=$hex"
    Write-Host "[AZTEC-FLAME-CELLS] firstFourU32=$($firstWords -join ', ')"
}
if($total -ne 17 -or $rows.Count -ne 5){
    throw "Actual original PC memory coverage changed: material refs=$total unique=$($rows.Count)"
}
$report=[ordered]@{
    schema='WaW360.AztecFiveRealPcFlameMaterialMemoryWordEvidence.v1'
    status='FIVE_GENUINE_ORIGINAL_PC_MATERIAL_SOURCE_TARGET_32B_WORDS_AUDITED_NO_TYPE_PROMOTION'
    genuineOriginalPcMaterialPackedReferenceFields=17
    distinctOriginalPcLoadedPackedMaterialTargetValues=5
    sourceMaterial112NamesIndependentlyVerified=0
    originalPcRealSourceSha256=[string]$j.originalPcSourceFastFileSha256
    originalOatGoodNativeFfSha256=[string]$j.originalNativeFFSha256
    nativeProductionMaterialAssetsEmitted=0
    actualXboxFfModified=$false
    realSourceNullMaterialFieldsPreserved=12
    requiresNextValidTypedSourceOwnerGraph=$true
    evidenceTargets=@($rows.ToArray())
}
$report|ConvertTo-Json -Depth 12|Set-Content -LiteralPath $output -Encoding UTF8
Write-Host "[AZTEC-FLAME-CELLS] Original-PC exact memory word audit PASS: $total FlameMaterial uses, $($rows.Count) targets; zero invented Material112 matches"
Write-Host "[AZTEC-FLAME-CELLS] JSON=$output"
