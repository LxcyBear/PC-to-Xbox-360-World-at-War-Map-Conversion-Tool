[CmdletBinding()]
param(
    [string]$MapName = 'nazi_zombie_aztec_V2.2',
    [string]$OutputRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutputRoot) { $OutputRoot=Join-Path $repo 'output\second_map_baselines' }
$root=Join-Path $OutputRoot $MapName
$sourceProofFile=Join-Path $root 'material_techset_oat_bridge\material_technique_result.json'
$nativeResultFile=Join-Path $root 'native_material_mapping\native_material_mapping_result.json'
$mainClosureFile=Join-Path $root 'native_material_mapping\main_native_closure\nuclear_techset_closure_plan.json'
$nativeIdentityFile=Join-Path $root 'native_material_mapping\main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$reachabilityFile=Join-Path $root 'material_techset_oat_reachability\material_reachability_result.json'
$materialCsv=Join-Path $root 'material_techset_oat_reachability\pc_material_techset_references.csv'
$modBridgeFile=Join-Path $root 'native_material_mapping\mod.ff\material_techset_bridge.json'
$patchBridgeFile=Join-Path $root 'native_material_mapping\map_patch.ff\material_techset_bridge.json'
foreach ($file in @($sourceProofFile,$nativeResultFile,$mainClosureFile,$nativeIdentityFile,$reachabilityFile,$materialCsv,$modBridgeFile,$patchBridgeFile)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Required previous proof missing: $file" }
}
$proof=Get-Content -LiteralPath $sourceProofFile -Raw | ConvertFrom-Json
$nativeResult=Get-Content -LiteralPath $nativeResultFile -Raw | ConvertFrom-Json
$main=Get-Content -LiteralPath $mainClosureFile -Raw | ConvertFrom-Json
$nativeIdentity=Get-Content -LiteralPath $nativeIdentityFile -Raw | ConvertFrom-Json
$reach=Get-Content -LiteralPath $reachabilityFile -Raw | ConvertFrom-Json
$mod=Get-Content -LiteralPath $modBridgeFile -Raw | ConvertFrom-Json
$patch=Get-Content -LiteralPath $patchBridgeFile -Raw | ConvertFrom-Json

$expectedHash='e0f3c244dd7b72d0fe71c05faf014c2c9bfb05f770183954b3e7cde625eba98f'
$expectedBytes=82739469L
$ref=[string]$nativeResult.verifiedXboxReference
if ([string]$proof.schema -ne 'WaW360.SecondMapMaterialTechnique.v1' -or
    [string]$proof.pcMapFolder -ine $MapName -or
    [string]$nativeResult.schema -ne 'WaW360.SecondMapNativeMaterialMapping.v1' -or
    [string]$nativeResult.mapFolder -ine $MapName -or
    [string]$nativeResult.status -ne 'GOLDEN_VERIFIED_NATIVE_MAPPING_RECORDED' -or
    [string]$nativeResult.goldenReferenceSha256 -ine $expectedHash -or
    [long]$nativeResult.goldenReferenceSize -ne $expectedBytes -or
    [string]$reach.format -ne 'WaW360.SecondMapMaterialReachability.v1' -or
    [string]$reach.mapFolder -ine $MapName -or
    -not [bool]$nativeIdentity.TypeSequenceVerified) {
    throw 'One or more native/PC evidence gates failed; prior mapping cannot be trusted.'
}
if (-not (Test-Path -LiteralPath $ref -PathType Leaf)) { throw "Verified native Xbox FF moved: $ref" }
if ([long](Get-Item -LiteralPath $ref).Length -ne $expectedBytes -or
    (Get-FileHash -LiteralPath $ref -Algorithm SHA256).Hash -ine $expectedHash) {
    throw 'Native Xbox Aztec FF no longer matches the pinned golden fingerprint.'
}
if ([string]$main.XboxFastFile -ine $ref -or
    [string]$nativeIdentity.NativeFastFile -ine $ref -or
    [string]$mod.XboxFastFile -ine $ref -or
    [string]$patch.XboxFastFile -ine $ref) {
    throw 'One or more bridge reports used a different native fastfile.'
}
if ([int]$proof.identityFilesVerified -ne [int]$proof.identityFilesAttempted -or
    [int]$proof.identityMappedTechniqueSets -ne [int]$proof.identityDeclaredTechniqueSets) {
    throw 'PC TechniqueSet root identities are not all verified.'
}
if ([int]$main.PcTechniqueRoots -ne [int]$nativeResult.mainPcTechniqueRoots) {
    throw 'Main PC root count changed between recorded proofs.'
}
if ([string]$main.PcFastFile -ine [string]$proof.mainPcFastFile) {
    throw 'Main source fastfile does not match the previously verified proof.'
}
$expectedRoles=@('map.ff','map_patch.ff','mod.ff')
$sourceRuns=@($proof.oatIdentityScans)
foreach ($role in $expectedRoles) {
    if (@($sourceRuns | Where-Object { [string]$_.Role -ieq $role }).Count -ne 1) {
        throw "Expected exactly one source TechniqueSet identity proof for $role"
    }
}

# Only material-derived evidence and individually validated map-local exact names
# may yield a mapping. Shared PC names across source FFs can be reconciled, but
# conflicting Xbox targets remain a HARD FENCE.
$votes=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
function Add-Mapping([string]$pcName,[string]$xboxName,[string]$evidence,[int]$support) {
    if (-not $pcName -or -not $xboxName) { return }
    if (-not $script:votes.ContainsKey($pcName)) {
        $script:votes.Add($pcName,(New-Object 'System.Collections.Generic.List[object]'))
    }
    $script:votes[$pcName].Add([pscustomobject]@{
        PcTechniqueSet=$pcName; XboxTechniqueSet=$xboxName
        Evidence=$evidence; SupportingMaterials=$support
    })
}
foreach ($row in @($main.Rows)) {
    if ([string]$row.Resolution -in @('MATERIAL_BRIDGE','EXACT_NAME_NATIVE_FALLBACK') -and
        $null -ne $row.XboxDonorAssetIndex) {
        Add-Mapping ([string]$row.PcTechniqueSet) ([string]$row.XboxTechniqueSet) ('main:'+ [string]$row.Resolution) ([int]$row.SupportingMaterials)
    }
}
foreach ($m in @($mod.Mappings)) {
    Add-Mapping ([string]$m.PcTechniqueSet) ([string]$m.XboxTechniqueSet) 'mod:shared-material' ([int]$m.SupportingMaterials)
}
foreach ($m in @($patch.Mappings)) {
    Add-Mapping ([string]$m.PcTechniqueSet) ([string]$m.XboxTechniqueSet) 'patch:shared-material' ([int]$m.SupportingMaterials)
}
$nativeTechsets=@($nativeIdentity.Entries | Where-Object { $_.OatTypeName -ieq 'techniqueset' })
$nativeByName=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($rootEntry in $nativeTechsets) {
    $name=[string]$rootEntry.Name
    if ($nativeByName.ContainsKey($name)) {
        throw "Duplicate native TechniqueSet name $name; identity mapping would be ambiguous."
    }
    $nativeByName.Add($name,$rootEntry)
}
$referenced=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($material in @(Import-Csv -LiteralPath $materialCsv)) {
    if (-not [string]::IsNullOrWhiteSpace([string]$material.TechniqueSet)) {
        [void]$referenced.Add([string]$material.TechniqueSet)
    }
}
$allRoots=New-Object 'System.Collections.Generic.List[object]'
$fullConflicts=New-Object 'System.Collections.Generic.List[object]'
$roleStats=New-Object 'System.Collections.Generic.List[object]'
foreach ($role in $expectedRoles) {
    $sourceRun=@($sourceRuns | Where-Object { [string]$_.Role -ieq $role })[0]
    $catalogFile=[string]$sourceRun.IdentityManifest
    if (-not (Test-Path -LiteralPath $catalogFile -PathType Leaf)) { throw "Missing PC root identity manifest: $catalogFile" }
    $pcCatalog=Get-Content -LiteralPath $catalogFile -Raw | ConvertFrom-Json
    if (-not [bool]$pcCatalog.Verified -or
        [string]$pcCatalog.PcFastFile -ine [string]$sourceRun.SourceFastFile -or
        @($pcCatalog.Entries).Count -ne [int]$pcCatalog.DeclaredPhysicalTechniqueSets) {
        throw "Invalid root identity proof: $catalogFile"
    }
    $mapped=0; $exact=0; $unresolved=0; $unreferenced=0; $conflictCount=0
    foreach ($rootEntry in @($pcCatalog.Entries)) {
        $pcName=[string]$rootEntry.Name
        $needed=$referenced.Contains($pcName)
        $targets=@()
        $evidence=@()
        $support=0
        if ($votes.ContainsKey($pcName)) {
            $targets=@($votes[$pcName] | Select-Object -ExpandProperty XboxTechniqueSet -Unique)
            $evidence=@($votes[$pcName] | Select-Object -ExpandProperty Evidence -Unique)
            $support=[int](($votes[$pcName] | Measure-Object -Property SupportingMaterials -Sum).Sum)
        }
        $status='NO_MATERIAL_REFERENCE_OBSERVED'
        $resolvedName=''
        $nativeIndex=$null
        if ($targets.Count -gt 1) {
            $status='CONFLICTING_PC_TO_XBOX_TARGETS'
            $conflictCount++
            $fullConflicts.Add([pscustomobject]@{Role=$role; Name=$pcName; XboxCandidates=$targets})
        } elseif ($targets.Count -eq 1) {
            $resolvedName=[string]$targets[0]
            if ($nativeByName.ContainsKey($resolvedName)) {
                $nativeIndex=[int]$nativeByName[$resolvedName].Index
                $status=if ($needed) { 'MATERIAL_BRIDGE_NATIVE_ROOT_MATCH' } else { 'MATERIAL_BRIDGE_NOT_REACHABILITY_PROOF' }
                $mapped++
            } else {
                $status='MAPPED_TO_RESIDENT_OR_NONLOCAL_NATIVE_TECHSET'
                $unresolved++
            }
        } elseif ($needed -and $nativeByName.ContainsKey($pcName)) {
            $resolvedName=$pcName
            $nativeIndex=[int]$nativeByName[$pcName].Index
            $status='EXACT_NATIVE_NAME_ONLY_NEEDS_MATERIAL_PROOF'
            $exact++
        } elseif ($needed) {
            $status='MATERIAL_REFERENCED_NO_NATIVE_MAPPING'
            $unresolved++
        } else {
            $unreferenced++
        }
        $allRoots.Add([pscustomobject]@{
            Role=$role
            PcFastFile=[string]$sourceRun.SourceFastFile
            PcAssetIndex=[int]$rootEntry.AssetIndex
            PcTechniqueSet=$pcName
            ObservedByAnyExportedMaterial=$needed
            XboxTechniqueSet=$resolvedName
            NativeXboxRootIndex=$nativeIndex
            Evidence=[string]($evidence -join '|')
            SupportingMaterials=$support
            Classification=$status
            HardwareShaderGraphReady=$false
        })
    }
    $roleStats.Add([pscustomobject]@{
        Role=$role
        Roots=@($pcCatalog.Entries).Count
        NativeBoundViaExistingEvidence=$mapped
        NativeExactNameOnly=$exact
        MaterialReachableUnresolved=$unresolved
        NoMaterialReferenceObserved=$unreferenced
        Conflicts=$conflictCount
    })
}

$work=Join-Path $root 'native_material_mapping\closure_reconciliation'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$csv=Join-Path $work 'pc_to_xbox_techset_root_classifications.csv'
$allRoots.ToArray() | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8
$pending=@($allRoots | Where-Object { $_.ObservedByAnyExportedMaterial -and
    $_.Classification -notin @('MATERIAL_BRIDGE_NATIVE_ROOT_MATCH','MATERIAL_BRIDGE_NOT_REACHABILITY_PROOF') })
$pendingCsv=Join-Path $work 'techset_pending_or_ambiguous.csv'
$pending | Export-Csv -LiteralPath $pendingCsv -NoTypeInformation -Encoding UTF8
$json=Join-Path $work 'closure_reconciliation_result.json'
$txt=Join-Path $work 'closure_reconciliation_result.txt'
$report=[ordered]@{
    schema='WaW360.SecondMapNativeClosureReconciliation.v1'
    mapFolder=$MapName
    verifiedGoldenXboxFastFile=$ref
    verifiedGoldenSha256=$expectedHash
    totalPcPhysicalTechsetRoots=$allRoots.Count
    nativeXboxTechsetRoots=$nativeTechsets.Count
    pcTechniqueNamesReferencedByExportedMaterials=$referenced.Count
    roleStatistics=[object[]]$roleStats.ToArray()
    pendingReferencedRows=$pending.Count
    conflicts=[object[]]$fullConflicts.ToArray()
    pendingCases=[object[]]$pending
    classificationCsv=$csv
    pendingCsv=$pendingCsv
    hardwareReady=$false
    notes=@(
        'Only source-verified material bridges and native identity names are used. No shader bodies are copied.',
        'An exact-native-name-only match is weaker than a demonstrated material bridge and is NOT production-ready.',
        'A root not observed in Material JSON is not proven globally unreachable. Other assets may still need it.',
        'Different roots sharing a TechniqueSet name are listed separately; their payload graph identity is not inferred.',
        'A referenced TechniqueSet absent from the native map may exist in stock Xbox zones; exact identity/source still needs proof.'
    )
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $json -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add('AZTEC NATIVE MATERIAL TECHNIQUESET ROOT RECONCILIATION — EXISTING PROOFS')
$lines.Add('=========================================================================')
$lines.Add("Verified native Xbox reference: $ref")
$lines.Add("PC physical TechniqueSet root entries: $($allRoots.Count)")
$lines.Add("Xbox native TechniqueSet roots: $($nativeTechsets.Count)")
$lines.Add("Distinct PC TechniqueSet names used by exported Materials: $($referenced.Count)")
foreach ($st in $roleStats) {
    $lines.Add("$($st.Role): roots=$($st.Roots) mapped=$($st.NativeBoundViaExistingEvidence) exact-name-only=$($st.NativeExactNameOnly) referenced-unresolved=$($st.MaterialReachableUnresolved) no-material-reference=$($st.NoMaterialReferenceObserved) conflicts=$($st.Conflicts)")
}
$lines.Add("Pending mapped/reachability clarification rows: $($pending.Count)")
foreach ($entry in @($pending | Select-Object -First 60)) {
    $lines.Add("  $($entry.Role) pc#$($entry.PcAssetIndex) $($entry.PcTechniqueSet) => $($entry.XboxTechniqueSet) | $($entry.Classification)")
}
$lines.Add("Full identity classifications: $csv")
$lines.Add("Pending subset: $pendingCsv")
$lines.Add('Xbox shader graph serialization, stock-zone donor ownership and full asset reachability remain separate gates.')
[IO.File]::WriteAllLines($txt,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-CLOSURE] =========================================================='
Write-Host '[AZTEC-CLOSURE] RECONCILE ALL PC TECHNIQUESET ROOTS AGAINST NATIVE EVIDENCE'
Write-Host '[AZTEC-CLOSURE] Prior proofs and material CSV only. No build, no OAT, no FF emission.'
Write-Host "[AZTEC-CLOSURE] PC roots=$($allRoots.Count) native Xbox TechniqueSet roots=$($nativeTechsets.Count)"
foreach ($st in $roleStats) {
    Write-Host "[AZTEC-CLOSURE] $($st.Role): mapped=$($st.NativeBoundViaExistingEvidence) exact-only=$($st.NativeExactNameOnly) unresolved=$($st.MaterialReachableUnresolved) unobserved=$($st.NoMaterialReferenceObserved) conflicts=$($st.Conflicts)"
}
Write-Host "[AZTEC-CLOSURE] Pending material-referenced cases=$($pending.Count)"
Write-Host "[AZTEC-CLOSURE] Report: $txt"
Write-Host "[AZTEC-CLOSURE] JSON: $json"
Write-Host '[AZTEC-CLOSURE] Identity closure diagnostic only; hardware readiness unchanged.'
