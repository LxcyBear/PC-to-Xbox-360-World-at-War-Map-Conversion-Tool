[CmdletBinding()]
param(
    [string]$MapName = 'nazi_zombie_aztec_V2.2',
    [string]$OutputRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutputRoot) { $OutputRoot=Join-Path $repo 'output\second_map_baselines' }
$workspace=Join-Path $OutputRoot $MapName
$baselineFile=Join-Path $workspace 'second_map_baseline.json'
$reachFile=Join-Path $workspace 'material_techset_oat_reachability\material_reachability_result.json'
foreach ($f in @($baselineFile,$reachFile)) {
    if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { throw "Required existing report missing: $f" }
}
$baseline=Get-Content -LiteralPath $baselineFile -Raw | ConvertFrom-Json
$reach=Get-Content -LiteralPath $reachFile -Raw | ConvertFrom-Json
if ([string]$baseline.format -ne 'WaW360.SecondMapBaseline.v1' -or
    [string]$baseline.mapName -ine $MapName -or
    [string]$reach.format -ne 'WaW360.SecondMapMaterialReachability.v1' -or
    [string]$reach.mapFolder -ine $MapName) {
    throw 'Baseline/reachability source identities disagree.'
}
$work=Join-Path $workspace 'material_techset_oat_reconciliation'
New-Item -ItemType Directory -Path $work -Force | Out-Null
$logs=Join-Path $workspace 'oat_asset_lists'
$export=Join-Path $workspace 'material_techset_oat_reachability'

Write-Host '[MATERIAL-RECON] ============================================================'
Write-Host '[MATERIAL-RECON] COMPARE OAT LISTED MATERIAL ENTRIES VS DUMPED MATERIAL NAMES'
Write-Host '[MATERIAL-RECON] Existing logs only: no re-dump, no build, no shader conversion.'
Write-Host "[MATERIAL-RECON] Map=$MapName"
Write-Host '[MATERIAL-RECON] ============================================================'

function Get-Names([string]$path,[string]$pattern) {
    $names=New-Object 'System.Collections.Generic.List[string]'
    foreach ($line in [IO.File]::ReadLines($path)) {
        if ($line -match $pattern) {
            $name=[string]$Matches[1]
            if ($name.Trim()) { $names.Add($name.Trim()) }
        }
    }
    return ,$names.ToArray()
}
function Get-Frequencies([string[]]$names) {
    $counts=New-Object 'System.Collections.Generic.Dictionary[string,int]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $names) {
        if ($counts.ContainsKey($name)) { $counts[$name]++ }
        else { $counts.Add($name,1) }
    }
    return $counts
}

$perFile=New-Object 'System.Collections.Generic.List[object]'
$unexported=New-Object 'System.Collections.Generic.List[object]'
$crossRefs=New-Object 'System.Collections.Generic.Dictionary[string,int]' ([StringComparer]::OrdinalIgnoreCase)
$idx=0
foreach ($ff in @($baseline.fastfiles)) {
    $idx++
    $role=[string]$ff.Role
    if ($role -notin @('map.ff','map_patch.ff','mod.ff')) { continue }
    $stem=[IO.Path]::GetFileNameWithoutExtension([string]$ff.Path) -replace '[^A-Za-z0-9_.-]','_'
    $roleSafe=$role -replace '[^A-Za-z0-9_]','_'
    $listLog=Join-Path $logs ("{0:D2}_{1}.list.log" -f $idx,$stem)
    $dumpLog=Join-Path $export ($stem+'_'+$roleSafe+'.oat.log')
    foreach ($f in @($listLog,$dumpLog)) {
        if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { throw "Required prior-run OAT log missing: $f" }
    }
    $listed=[string[]](Get-Names $listLog '^\s*(?:\[[^\]]+\]\s*)?material\s*,\s*(.*?)\s*$')
    $dumped=[string[]](Get-Names $dumpLog '^\s*Dumped material "([^"]+)"\s*$')
    $listNames=Get-Frequencies $listed
    $dumpNames=Get-Frequencies $dumped
    $expected=0
    $materialProp=$ff.AssetCounts.PSObject.Properties['material']
    if ($null -ne $materialProp) { $expected=[int]$materialProp.Value }
    if ($listed.Count -ne $expected) {
        throw "Baseline/OAT --list material count mismatch for $role : saved=$expected parsed=$($listed.Count). Check $listLog"
    }
    $missing=@($listNames.Keys | Where-Object { -not $dumpNames.ContainsKey($_) } | Sort-Object)
    $extra=@($dumpNames.Keys | Where-Object { -not $listNames.ContainsKey($_) } | Sort-Object)
    $duplicates=$listed.Count-$listNames.Count
    $dumpDuplicates=$dumped.Count-$dumpNames.Count
    foreach ($name in $missing) {
        $unexported.Add([pscustomobject]@{
            Role=$role; FastFile=[string]$ff.Path; Name=$name
            ListOccurrences=[int]$listNames[$name]
        })
    }
    foreach ($name in $dumpNames.Keys) {
        if ($crossRefs.ContainsKey($name)) { $crossRefs[$name]++ }
        else { $crossRefs.Add($name,1) }
    }
    $row=[pscustomobject]@{
        Role=$role
        ListOccurrences=$listed.Count
        ListUniqueNames=$listNames.Count
        ListDuplicateOccurrences=$duplicates
        ExportOccurrences=$dumped.Count
        ExportUniqueNames=$dumpNames.Count
        ExportDuplicateOccurrences=$dumpDuplicates
        MissingUniqueNames=$missing.Count
        UnexpectedExportNames=$extra.Count
        CountDifference=$listed.Count-$dumped.Count
        DifferenceFullyExplainedByListDuplicates=($missing.Count -eq 0 -and
            $extra.Count -eq 0 -and $dumpDuplicates -eq 0 -and
            $duplicates -eq ($listed.Count-$dumped.Count))
        ListLog=$listLog
        MaterialDumpLog=$dumpLog
        MissingExamples=[string[]]@($missing | Select-Object -First 25)
        UnexpectedExamples=[string[]]@($extra | Select-Object -First 25)
    }
    $perFile.Add($row)
    Write-Host "[MATERIAL-RECON] $role listed=$($row.ListOccurrences) distinct=$($row.ListUniqueNames) dupExtras=$duplicates | dumped=$($row.ExportOccurrences) missingNames=$($row.MissingUniqueNames) unexpected=$($row.UnexpectedExportNames)"
}
$sharedNames=@($crossRefs.Keys | Where-Object { $crossRefs[$_] -gt 1 } | Sort-Object)
$allSafe=@($perFile | Where-Object { -not $_.DifferenceFullyExplainedByListDuplicates }).Count -eq 0
$report=[ordered]@{
    schema='WaW360.MaterialOatReconciliation.v1'
    mapFolder=$MapName
    physicalTechniqueSetRoots=[int]$reach.physicalTechniqueSetRows
    previouslyObservedMaterialReferencedRoots=[int]$reach.physicalTechniqueSetRowsReferenced
    perFastFile=[object[]]$perFile.ToArray()
    unmatchedListedNames=[object[]]$unexported.ToArray()
    materialNamesSharedAcrossSourceFiles=[string[]]$sharedNames
    allListToDumpCountDifferencesExplainedByDuplicates=$allSafe
    sourceOnly=$true
    hardwareReady=$false
    notes=@(
        'Listed asset occurrences and uniquely dumped material files are not necessarily equivalent populations.',
        'An unexported or duplicated material name is not proof of a missing shader graph.',
        'Material->TechniqueSet census stays diagnostic until all source relationships and Xbox-specific dependencies are resolved.',
        'This comparison reads existing logs and does not reread or mutate PC fastfiles.'
    )
}
$json=Join-Path $work 'material_oat_reconciliation.json'
$txt=Join-Path $work 'material_oat_reconciliation.txt'
$csv=Join-Path $work 'material_oat_unexported_names.csv'
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $json -Encoding UTF8
$unexported.ToArray() | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8
$body=New-Object 'System.Collections.Generic.List[string]'
$body.Add('OAT LISTED MATERIAL ENTRIES VS SEMANTIC MATERIAL DUMP')
$body.Add('================================================================')
foreach ($row in $perFile) {
    $body.Add("$($row.Role): list=$($row.ListOccurrences) distinct=$($row.ListUniqueNames) duplicateOccurrences=$($row.ListDuplicateOccurrences); dump=$($row.ExportOccurrences) missingNames=$($row.MissingUniqueNames) unexpected=$($row.UnexpectedExportNames); explainedByDuplicates=$($row.DifferenceFullyExplainedByListDuplicates)")
    if ($row.MissingUniqueNames) {
        $body.Add('  Missing from dumped material names:')
        foreach ($name in $row.MissingExamples) { $body.Add("  - $name") }
    }
    if ($row.UnexpectedExportNames) {
        $body.Add('  Dumped names missing from list:')
        foreach ($name in $row.UnexpectedExamples) { $body.Add("  + $name") }
    }
}
$body.Add("Cross-source shared Material names=$($sharedNames.Count)")
$body.Add("All source list/dump gaps explained by duplicates=$allSafe")
$body.Add('This is evidence classification only; do not delete or serialize any Xbox roots from this report.')
$body.Add("Unexported names: $csv")
$body.Add("Machine-readable: $json")
[IO.File]::WriteAllLines($txt,[string[]]$body.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[MATERIAL-RECON] ================= RESULT =================================='
Write-Host "[MATERIAL-RECON] All gaps explained by list duplicates: $allSafe"
Write-Host "[MATERIAL-RECON] Shared source Material names: $($sharedNames.Count)"
Write-Host "[MATERIAL-RECON] Report: $txt"
Write-Host "[MATERIAL-RECON] Unexported: $csv"
Write-Host '[MATERIAL-RECON] Hardware readiness unchanged.'
