[CmdletBinding()]
param([switch]$ReuseArchivedPasses)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# A/B exactly the SAME full original-PC Gate11 native FF:
#   A. 63 legitimate PC WeaponDef source scalar + first strings
#   B. those SAME 63 and extra, genuinely inline original-PC fixed
#      WeaponDef XStrings found by the exact Alpha 7b typed walker.
# Neither carries the unrebased foreign asset/packed-pointer graph.
# Both require Stage4D/4E exact layout and OAT 8509 identities, 0/0.
# Never deploy an A/B FF to the Xbox.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$source=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping\pc_g11_lightning_weapon_69_unified_native_source\aztec_69_weapon_unified_original_pc_xbox_body_selections.json'
$size=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping\pc_g11_native_ff_size_audit\aztec_native_ff_size_and_source_ledger.json'
$parentRunner=Join-Path $PSScriptRoot 'Run-AztecWeapon63FfGrowthCanary.ps1'
$archive=Join-Path $repo '.deps\aztec-w63-inline-ab'
$report=Join-Path $archive 'aztec_weapon63_extra_inline_xstrings_exact_ab.json'
$markdown=Join-Path $archive 'aztec_weapon63_extra_inline_xstrings_exact_ab.md'
$baseWork=Join-Path $repo '.deps\aztec-w63-ff-growth'
$extendedWork=Join-Path $repo '.deps\aztec-w63-xstr-ff-growth'

foreach($file in @($source,$size,$parentRunner)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){
        throw "Source or verified 63-WeaponDef runner missing: $file"
    }
}
$m=Get-Content -LiteralPath $source -Raw|ConvertFrom-Json
$ledger=Get-Content -LiteralPath $size -Raw|ConvertFrom-Json
if([string]$m.schema -ne 'WaW360.AztecWeapon69UnifiedOriginalPcXboxBodySelection.v1' -or
   [string]$m.status -ne 'AZTEC_69_WEAPON_NATIVE_ROOTS_63_AUTHENTIC_PC_XBOX_BODIES_2_CONFLICT_4_STOCK_UNPROVEN_NOT_IN_FF' -or
   [int]$m.nativeWeaponDefRoots -ne 69 -or
   [int]$m.uniqueSourceOwnedXboxWeaponBodyMappings -ne 63 -or
   [long]$m.selectedUncompressedXboxOwnedPayloadBytes -ne 179775 -or
   [int]$m.conflictingDifferentPcSourceBodiesStillFenced -ne 2 -or
   [int]$m.offlineStockOnlyWeaponNamesNotRuntimeProven -ne 4 -or
   [bool]$m.xboxRuntimeReady -or
   [string]$ledger.schema -ne 'WaW360.AztecNativeFfSizeAndSourceCoverage.v1' -or
   [string]$ledger.converted.sha256 -ine 'F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6'){
    throw '63 original-PC WeaponDef source ownership or protected FF evidence changed.'
}
$good=[string]$ledger.converted.path
if(-not(Test-Path -LiteralPath $good -PathType Leaf)){
    throw "Protected source FF missing: $good"
}
$goodLen=[long](Get-Item -LiteralPath $good).Length
$goodHash=(Get-FileHash -LiteralPath $good -Algorithm SHA256).Hash
if($goodLen -ne 4510695 -or $goodHash -ine [string]$ledger.converted.sha256){
    throw 'Protected 4,510,695 B native FF SHA/length changed. Refusing scratch comparison.'
}
$manifestHash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
$ledgerHash=(Get-FileHash -LiteralPath $size -Algorithm SHA256).Hash

# A/B is invalid if any native exporter/serializer/source or wrapper
# is modified mid-run without changing Git HEAD.
$codePaths=@(
    'src\WaW360.Core\FastFiles\T4WeaponVariableSerializerProof.cs',
    'src\WaW360.Core\FastFiles\T4XenonAztecWeapon63ScalarCanary.cs',
    'src\WaW360.Core\FastFiles\T4XenonGraphBlockLayoutPlanner.cs',
    'src\WaW360.Core\FastFiles\T4XenonRendererGraphEmitter.cs',
    'tools\oat-xenon\Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1',
    'tools\oat-xenon\Run-AztecWeapon63FfGrowthCanary.ps1',
    'tools\oat-xenon\Run-AztecWeapon63ExtendedXStringsExactAB.ps1'
)
$codeHashes=@{}
foreach($relative in $codePaths){
    $path=Join-Path $repo $relative
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Source code input for original PC native FF A/B missing: $path"
    }
    $codeHashes[$relative]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
$revision=(& git -C $repo rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($revision)){
    throw 'Cannot identify current source revision for exact native FF A/B.'
}
New-Item -ItemType Directory -Path $archive -Force|Out-Null
Write-Host '[AZTEC-W63-XSTR-AB] ==========================================================='
Write-Host '[AZTEC-W63-XSTR-AB] ORIGINAL PC WEAPON SOURCE INLINE XSTRING NATIVE FF A/B'
Write-Host '[AZTEC-W63-XSTR-AB] Same full Gate11 world: 63 previously passing vs same 63 + extra typed source strings.'
Write-Host "[AZTEC-W63-XSTR-AB] Protected historical good FF SHA=$goodHash"
Write-Host "[AZTEC-W63-XSTR-AB] Mode=$(if($ReuseArchivedPasses){'READ EXISTING ARCHIVES ONLY'}else{'FRESH BOTH OAT BUILDS'})"
Write-Host '[AZTEC-W63-XSTR-AB] Source fragments only. No full owned WeaponDef graph, no Xbox hardware.'
Write-Host '[AZTEC-W63-XSTR-AB-PROGRESS] [####----------------] 20% | FF SHA / original PC 63 source roots / exporter code pinned'

function Assert-Unchanged {
    if(-not(Test-Path -LiteralPath $good -PathType Leaf) -or
       [long](Get-Item -LiteralPath $good).Length -ne $goodLen -or
       (Get-FileHash -LiteralPath $good -Algorithm SHA256).Hash -ine $goodHash -or
       (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ine $manifestHash -or
       (Get-FileHash -LiteralPath $size -Algorithm SHA256).Hash -ine $ledgerHash){
        throw 'Protected FF or original-PC ownership/size report changed during exact WeaponDef XString A/B.'
    }
    $now=(& git -C $repo rev-parse HEAD).Trim()
    if($LASTEXITCODE -ne 0 -or $now -ine $revision){
        throw 'Git HEAD changed during original-PC WeaponDef XString A/B; measurement invalid.'
    }
    foreach($relative in $codePaths){
        $path=Join-Path $repo $relative
        if(-not(Test-Path -LiteralPath $path -PathType Leaf) -or
           (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine [string]$codeHashes[$relative]){
            throw "Native WeaponDef source writer changed during A/B: $relative"
        }
    }
}
function Save-ExistingScratch([string]$work,[string]$label,[string]$expectedStatus){
    $currentFf=Join-Path $work 'scratch_emit\nazi_zombie_aztec.ff'
    $currentReport=Join-Path $work 'aztec_weapon63_ff_growth_63.json'
    if(-not(Test-Path -LiteralPath $currentFf -PathType Leaf) -or
       -not(Test-Path -LiteralPath $currentReport -PathType Leaf)){
        return
    }
    $p=Get-Content -LiteralPath $currentReport -Raw|ConvertFrom-Json
    $h=(Get-FileHash -LiteralPath $currentFf -Algorithm SHA256).Hash
    if([string]$p.status -ne $expectedStatus -or
       [int]$p.nativeWeaponRootScalarXstringCanariesEmitted -ne 63 -or
       [string]$p.newScratchFastFileSha256 -ine $h){
        throw "An existing $label scratch FF exists without an exact matching OAT-good report. Refusing to overwrite it."
    }
    $dir=Join-Path $archive ('previous_oat_passed_'+$label)
    New-Item -ItemType Directory -Path $dir -Force|Out-Null
    Copy-Item -LiteralPath $currentFf -Destination (Join-Path $dir 'nazi_zombie_aztec.ff') -Force
    Copy-Item -LiteralPath $currentReport -Destination (Join-Path $dir 'prior_pass.json') -Force
    Write-Host "[AZTEC-W63-XSTR-AB] Preserved existing OAT-good $label FF before new isolated test."
}
if(-not $ReuseArchivedPasses){
    # The parent runners overwrite ONLY their own scratch FF. Preserve
    # both previous passes first, without touching earlier permanent A/B.
    Save-ExistingScratch $baseWork 'base_63' 'AZTEC_W63_SOURCE_SCALAR_XSTRINGS_IN_NATIVE_FF_OAT_LOAD_PASS_FULL_WEAPON_GRAPH_BLOCKED'
    Save-ExistingScratch $extendedWork 'extended_63' 'AZTEC_W63_EXTENDED_ORIGINAL_PC_INLINE_XSTRINGS_NATIVE_FF_OAT_PASS_NOT_FULL_WEAPON_GRAPH'
}

function Invoke-ValidatedCase([string]$label,[bool]$extended,[string]$work){
    Assert-Unchanged
    $dir=Join-Path $archive $label
    if($ReuseArchivedPasses){
        Write-Host "[AZTEC-W63-XSTR-AB] READBACK $label : verify already saved FF + OAT manifest"
        $ff=Join-Path $dir 'nazi_zombie_aztec.ff'
        $json=Join-Path $dir 'case_size_and_oat.json'
        $emit=Join-Path $dir 'nuclear_xenon_renderer_emit.json'
    }else{
        Write-Host "[AZTEC-W63-XSTR-AB] BUILD $label : 63 source-backed Xbox WeaponDef canaries, additional strings=$extended"
        $args=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$parentRunner,'-Limit','63')
        if($extended){$args+='-ExpandedInlineXStrings'}
        # Under PowerShell 5.1 native stdout otherwise pollutes the
        # function's SUCCESS stream, turning the returned case object
        # into an array of console log lines + PSCustomObject.
        & powershell @args | Out-Host
        $exit=[int]$LASTEXITCODE
        if($exit -ne 0){
            throw "Isolated native FF build/OAT rejected $label (exit=$exit); no size claim. Logs under $work."
        }
        $ff=Join-Path $work 'scratch_emit\nazi_zombie_aztec.ff'
        $json=Join-Path $work 'aztec_weapon63_ff_growth_63.json'
        $emit=Join-Path $work 'scratch_emit\nuclear_xenon_renderer_emit.json'
    }
    foreach($path in @($ff,$json,$emit)){
        if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
            throw "A/B case $label missing an OAT-validated output: $path"
        }
    }
    $r=Get-Content -LiteralPath $json -Raw|ConvertFrom-Json
    $e=Get-Content -LiteralPath $emit -Raw|ConvertFrom-Json
    $expected=if($extended){
        'AZTEC_W63_EXTENDED_ORIGINAL_PC_INLINE_XSTRINGS_NATIVE_FF_OAT_PASS_NOT_FULL_WEAPON_GRAPH'
    }else{
        'AZTEC_W63_SOURCE_SCALAR_XSTRINGS_IN_NATIVE_FF_OAT_LOAD_PASS_FULL_WEAPON_GRAPH_BLOCKED'
    }
    $length=[long](Get-Item -LiteralPath $ff).Length
    $hash=(Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash
    if([string]$r.schema -ne 'WaW360.AztecWeapon63RealSourceScalarXstringFfGrowthCanary.v1' -or
       [string]$r.status -ne $expected -or
       [int]$r.parentRunnerExitCode -ne 0 -or
       [int]$r.requestedCanaries -ne 63 -or
       [int]$r.nativeWeaponRootScalarXstringCanariesEmitted -ne 63 -or
       [bool]$r.extendedOriginalPcFixedInlineXStringsMode -ne $extended -or
       [int]$e.OriginalPcWeaponDiagnosticRoots -ne 63 -or
       [bool]$e.OriginalPcWeaponFullOwnedGraphsEmitted -or
       [bool]$r.fullWeaponDefChildrenAndDependenciesEmitted -or
       [bool]$r.outputNativeXboxHardwareReady -or
       [bool]$r.userMapDeployable -or
       [bool]$r.gate11Closed -or
       [int]$e.NativeRoots -ne 8509 -or
       -not [bool]$e.LayoutReplayExact -or
       -not [bool]$e.ExactIdentitySequence -or
       -not [bool]$e.OatZeroWarningsZeroErrors -or
       -not [bool]$e.ValidationPass -or
       -not [bool]$e.ValidationOnly -or
       [bool]$e.ReadyForHardware -or
       [long]$r.newScratchFastFileBytes -ne $length -or
       [string]$r.newScratchFastFileSha256 -ine $hash -or
       [string]$r.protectedPriorSha256 -ine $goodHash -or
       [long]$r.protectedPriorNativeFastFileBytes -ne $goodLen){
        throw "A/B case $label OAT, SHA, original PC weapon root identity, or runtime fence mismatch."
    }
    $fields=[int]$e.OriginalPcWeaponExpandedInlineXStringFields
    $parentFields=[int]$r.extendedSourceFixedInlineStringFieldsEmitted
    if($fields -ne $parentFields -or
       (-not $extended -and $fields -ne 0) -or
       ($extended -and $fields -le 0)){
        throw "A/B case $label original-PC extra inline XString field count did not match the independently emitted manifest ($fields vs $parentFields)."
    }
    if(-not $ReuseArchivedPasses){
        New-Item -ItemType Directory -Path $dir -Force|Out-Null
        Copy-Item -LiteralPath $ff -Destination (Join-Path $dir 'nazi_zombie_aztec.ff') -Force
        Copy-Item -LiteralPath $json -Destination (Join-Path $dir 'case_size_and_oat.json') -Force
        Copy-Item -LiteralPath $emit -Destination (Join-Path $dir 'nuclear_xenon_renderer_emit.json') -Force
        foreach($optional in @('integrated_emitter.log','aztec_pc_world_core_integrated_oat_run.json')){
            $candidate=Join-Path $work $optional
            if(Test-Path -LiteralPath $candidate -PathType Leaf){
                Copy-Item -LiteralPath $candidate -Destination (Join-Path $dir $optional) -Force
            }
        }
    }
    $saved=Join-Path $dir 'nazi_zombie_aztec.ff'
    if((Get-FileHash -LiteralPath $saved -Algorithm SHA256).Hash -ine $hash){
        throw "A/B $label saved FF SHA changed after the original OAT evidence."
    }
    Write-Host "[AZTEC-W63-XSTR-AB] $label OAT PASS 8509 roots, 0 warnings/errors, bytes=$length, extra source string fields=$fields, SHA=$hash"
    return [pscustomobject]@{
        label=$label
        extendedOriginalPcOwnedInlineXStrings=$extended
        nativeWeaponCanaries=63
        compressedNativeFfBytes=$length
        compressedNativeFfSha256=$hash
        archivedNativeFastFile=$saved
        archivedNativeOatManifest=Join-Path $dir 'nuclear_xenon_renderer_emit.json'
        extraFixedSourceInlineXStringFields=$fields
        diagnosticOriginalPcScalarAndXStringZoneBytes=[long]$e.OriginalPcWeaponDiagnosticSerializedBytes
        originalPcSource7bFullOwnedProofBytes=[long]$e.OriginalPcWeaponSourceProofBytes
        wholeNativeZoneBytes=[long]$e.ZoneBytes
        tempZoneBytes=[long]$e.TempBytes
        virtualZoneBytes=[long]$e.VirtualBytes
        physicalZoneBytes=[long]$e.PhysicalBytes
        realMaterialRoots=[int]$e.RealMaterialRoots
        realXModelRoots=[int]$e.RealXModelRoots
        realImageRoots=[int]$e.RealImageRoots
        realRawFileRoots=[int]$e.RealRawFileRoots
        gate10SourceStaticDraws=[int]$e.OriginalPcGfxWorldGate10DrawRecords
        gate11SourceCells=[int]$e.OriginalPcGfxWorldGate11Cells
        gate11SourceAabbTrees=[int]$e.OriginalPcGfxWorldGate11AabbTrees
        gate11SourceModelIndices=[int]$e.OriginalPcGfxWorldGate11SmodelIndexes
        oat8509OriginalNativeIdentitiesAndZeroWarningsErrors=$true
        childPointerGraphFullyRebased=$false
        readyForXboxHardware=$false
    }
}

$baseline=Invoke-ValidatedCase 'same_world_base_63' $false $baseWork
Write-Host '[AZTEC-W63-XSTR-AB-PROGRESS] [##########----------] 50% | original 63 scalar+opening XString FF archived and OAT 0/0 checked'
$extra=Invoke-ValidatedCase 'same_world_extended_63' $true $extendedWork
Write-Host '[AZTEC-W63-XSTR-AB-PROGRESS] [#################---] 85% | extra original PC typed inline XStrings FF archived and OAT 0/0 checked'
Assert-Unchanged

foreach($case in @(@{label='base';value=$baseline},@{label='extended';value=$extra})){
    if($null -eq $case.value -or $case.value -is [array] -or
       $case.value -isnot [pscustomobject]){
        $actual=if($null -eq $case.value){'<null>'}else{$case.value.GetType().FullName}
        throw "PowerShell result stream polluted for $($case.label) ($actual). No A/B delta accepted."
    }
}
foreach($key in @('nativeWeaponCanaries','realMaterialRoots','realXModelRoots','realImageRoots',
                  'realRawFileRoots','gate10SourceStaticDraws','gate11SourceCells',
                  'gate11SourceAabbTrees','gate11SourceModelIndices','physicalZoneBytes')){
    $left=$baseline.PSObject.Properties[$key]
    $right=$extra.PSObject.Properties[$key]
    if($null -eq $left -or $null -eq $right){
        throw "A/B world asset count field '$key' missing; cannot assert identical non-weapon graph."
    }
    if($left.Value -ne $right.Value){
        throw "A/B changed non-weapon world field '$key' ($($left.Value) vs $($right.Value)); no weapon-only delta claim."
    }
}
if($baseline.extraFixedSourceInlineXStringFields -ne 0 -or
   $extra.extraFixedSourceInlineXStringFields -le 0 -or
   $baseline.originalPcSource7bFullOwnedProofBytes -ne 179775 -or
   $extra.originalPcSource7bFullOwnedProofBytes -ne 179775 -or
   $extra.diagnosticOriginalPcScalarAndXStringZoneBytes -le $baseline.diagnosticOriginalPcScalarAndXStringZoneBytes){
    throw 'A/B original-PC source string ownership or added-zone-byte count failed.'
}
$compressedDelta=[long]$extra.compressedNativeFfBytes-$baseline.compressedNativeFfBytes
$zoneDelta=[long]$extra.wholeNativeZoneBytes-$baseline.wholeNativeZoneBytes
$stringDelta=[long]$extra.diagnosticOriginalPcScalarAndXStringZoneBytes-$baseline.diagnosticOriginalPcScalarAndXStringZoneBytes
if($zoneDelta -le 0 -or $stringDelta -le 0){
    throw "No real additional original PC WeaponDef XStrings were emitted (zoneDelta=$zoneDelta, stringDelta=$stringDelta)."
}
$status=if($compressedDelta -gt 0){
    'AZTEC_WEAPON63_EXTRA_PC_INLINE_XSTRINGS_EXACT_COMPRESSED_FF_GROWTH_OAT_PASS'
}elseif($compressedDelta -eq 0){
    'AZTEC_WEAPON63_EXTRA_PC_INLINE_XSTRINGS_SAME_COMPRESSED_FF_SIZE_OAT_PASS'
}else{
    'AZTEC_WEAPON63_EXTRA_PC_INLINE_XSTRINGS_COMPRESSED_FF_DECREASE_OAT_PASS'
}
$result=[ordered]@{
    schema='WaW360.AztecWeapon63ExtraPcInlineStringSameWorldAB.v1'
    status=$status
    freshBothSidesRebuiltUnderSameGitCodeRevision=(-not $ReuseArchivedPasses)
    archiveReadbackOnly=[bool]$ReuseArchivedPasses
    sourceRevisionForCurrentRun=$revision
    archivedOriginalBuildRevisionNotPinnedWhenReadbackOnly=[bool]$ReuseArchivedPasses
    exactNativeFfWriterSourceHashesForCurrentRun=$codeHashes
    genuineOriginalPcWeapon69RootSourceManifestHash=$manifestHash
    protectedOriginalOatPassingNativeFastFileBytes=$goodLen
    protectedOriginalOatPassingNativeFastFileSha256=$goodHash
    sourceOwnedFull7bOriginalPcWeaponBodiesNotFullyIntegratedBytes=179775
    sameWorldBase63=$baseline
    sameWorldWithExtraOriginalPcFixedInlineXStrings63=$extra
    addedGenuinelyInlineOriginalPcWeaponStringFields=$extra.extraFixedSourceInlineXStringFields
    extraSourceOwnedDiagnosticZoneStringBytes=$stringDelta
    zoneWholeUncompressedByteDifference=$zoneDelta
    nativeCompressedFastFileExactABDeltaBytes=$compressedDelta
    compressionPercentComparedToBase=[Math]::Round(100.0*$compressedDelta/$baseline.compressedNativeFfBytes,5)
    allTrackedNonWeaponWorldCountsRemainEqual=$true
    bothNative8509OatZeroWarningsErrors=$true
    fullWeaponDefAssetChildGraphsEmitted=$false
    conflictingTwoOriginalPcWeaponVersionsStillBlocked=$true
    stockOnlyFourWeaponRuntimeDependenciesUnverified=$true
    sixGfxImageRootsStillHaveNoOriginalPcPixelPayloads=$true
    noProtectedOriginalNativeFfModified=$true
    gameXboxHardwareReady=$false
    gate11Closed=$false
    caveat=if($ReuseArchivedPasses){
        'OAT and file SHA evidence from saved per-case reports is verified without rebuilding. Unlike a fresh A/B run, the original Git revision for archived FFs is not cryptographically recorded. Source attribution of an exact same-code difference is therefore qualified even though source/world identity counts match. This is scalar+fixed-inline-XString-only, not full Xbox WeaponDef child graph readiness.'
    }else{
        'Both FFs freshly compiled/emitted/OAT loaded using the same Git revision, source manifests and writer file SHA set, only extra inline fixed-body original-PC WeaponDef strings differ. Neither FF contains complete 7b-owned nested WeaponDef graph or is Xbox gameplay-ready.'
    }
}
$result|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $report -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add('# Aztec 63 original-PC WeaponDef source inline XString same-world OAT A/B')
$lines.Add('')
$lines.Add("**$status**")
$lines.Add('')
$lines.Add('| Actual FastFile | Real original-PC weapon canaries | Extra typed inline strings | Compressed bytes | OAT 8,509, 0/0 |')
$lines.Add('|---|---:|---:|---:|---|')
$lines.Add("| Source base | 63 | 0 | $($baseline.compressedNativeFfBytes) | PASS |")
$lines.Add("| Source extra | 63 | $($extra.extraFixedSourceInlineXStringFields) | $($extra.compressedNativeFfBytes) | PASS |")
$lines.Add('')
$lines.Add("**Exact compressed FF delta: $compressedDelta bytes**; added source-authentic serialized XString bytes: $stringDelta; complete uncompressed zone difference: $zoneDelta.")
$lines.Add("Historical protected 0-weapon FF SHA: $goodHash ($goodLen B), never changed.")
$lines.Add("All 179,775 B full source-owned 7b nested WeaponDef graphs are still source proofs, NOT in the runtime FF. 2 PC version conflicts and 4 stock-only name identities remain blocked.")
if($ReuseArchivedPasses){
    $lines.Add('Archival readback only; source Git revision of original build was not recorded, so exact same-code source attribution is qualified.')
}
[IO.File]::WriteAllLines($markdown,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-W63-XSTR-AB-PROGRESS] [####################] 100% | both native FFs archived+SHA/OAT checked, original PC extra inline strings isolated'
Write-Host '[AZTEC-W63-XSTR-AB] ==========================================================='
Write-Host "[AZTEC-W63-XSTR-AB] SAME-WORLD BASE 63 FF = $($baseline.compressedNativeFfBytes) B"
Write-Host "[AZTEC-W63-XSTR-AB] SAME-WORLD EXTENDED 63 FF = $($extra.compressedNativeFfBytes) B"
Write-Host "[AZTEC-W63-XSTR-AB] Additional ORIGINAL-PC INLINE WeaponDef string fields = $($extra.extraFixedSourceInlineXStringFields)"
Write-Host "[AZTEC-W63-XSTR-AB] EXTRA SOURCE INLINE STRING SERIALIZED BYTES = $stringDelta"
Write-Host "[AZTEC-W63-XSTR-AB] UNCOMPRESSED ZONE DELTA = $zoneDelta B"
Write-Host "[AZTEC-W63-XSTR-AB] COMPRESSED FF DELTA = $compressedDelta B"
Write-Host '[AZTEC-W63-XSTR-AB] Both FFs OAT PASS, 8509/8509 native roots, 0 warnings/errors. No Xbox hardware approval.'
Write-Host "[AZTEC-W63-XSTR-AB] STATUS=$status"
Write-Host "[AZTEC-W63-XSTR-AB] JSON=$report"
Write-Host "[AZTEC-W63-XSTR-AB] Markdown=$markdown"
