[CmdletBinding()]
param(
    [string]$MapName = 'nazi_zombie_aztec_V2.2',
    [string]$OutputRoot = '',
    [string]$UnlinkerExe = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo 'output\second_map_baselines' }
if (-not $UnlinkerExe) {
    $UnlinkerExe = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
}
if (-not (Test-Path -LiteralPath $UnlinkerExe -PathType Leaf)) {
    throw "Pinned OAT Unlinker.exe is missing: $UnlinkerExe"
}
$workspace = Join-Path $OutputRoot $MapName
$baselineFile = Join-Path $workspace 'second_map_baseline.json'
$identityFile = Join-Path $workspace 'material_techset_oat_bridge\material_technique_result.json'
foreach ($required in @($baselineFile, $identityFile)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required prior-stage manifest missing: $required"
    }
}
$baseline = Get-Content -LiteralPath $baselineFile -Raw | ConvertFrom-Json
$identity = Get-Content -LiteralPath $identityFile -Raw | ConvertFrom-Json
if ([string]$baseline.format -ne 'WaW360.SecondMapBaseline.v1' -or
    [string]$baseline.mapName -ine $MapName -or
    [string]$identity.schema -ne 'WaW360.SecondMapMaterialTechnique.v1' -or
    [string]$identity.pcMapFolder -ine $MapName) {
    throw 'Second-map baseline and OAT identity manifests disagree about the source.'
}
if ([int]$identity.identityMappedTechniqueSets -ne [int]$identity.identityDeclaredTechniqueSets -or
    [int]$identity.identityFilesVerified -ne [int]$identity.identityFilesAttempted) {
    throw 'Full-map OAT TechniqueSet identity proof is not complete. Fix it before material reachability.'
}
$canonical = [string]$identity.canonicalRuntimeName
$runRecords = @($identity.oatIdentityScans)
if ($runRecords.Count -eq 0) { throw 'No prior OAT identity files were recorded.' }
$work = Join-Path $workspace 'material_techset_oat_reachability'
New-Item -ItemType Directory -Force -Path $work | Out-Null

Write-Host '[MATERIAL-REACH] ============================================================'
Write-Host '[MATERIAL-REACH] FULL-MAP PC MATERIAL -> TECHNIQUESET REACHABILITY VIA OAT'
Write-Host "[MATERIAL-REACH] Map folder=$MapName; Xbox runtime=$canonical"
Write-Host "[MATERIAL-REACH] Proven TechniqueSet roots=$($identity.identityMappedTechniqueSets)"
Write-Host '[MATERIAL-REACH] Reuse existing identities; no CLI rebuild or re-scan of TechniqueSet lists.'
Write-Host '[MATERIAL-REACH] All references are diagnostic; no shader bytes are copied.'
Write-Host '[MATERIAL-REACH] ============================================================'

$materialEntries = New-Object 'System.Collections.Generic.List[object]'
$assetRows = New-Object 'System.Collections.Generic.List[object]'
$allReferenced = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$knownRootNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$sourceRecords = New-Object 'System.Collections.Generic.List[object]'
$missingMaterials = New-Object 'System.Collections.Generic.List[string]'

foreach ($run in $runRecords) {
    $source = [string]$run.SourceFastFile
    $manifestFile = [string]$run.IdentityManifest
    $matchingBaseline = @($baseline.fastfiles | Where-Object { [string]$_.Path -ieq $source })
    if ($matchingBaseline.Count -ne 1) {
        throw "Baseline does not contain exactly one matching FF for $source"
    }
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Source disappeared: $source" }
    if ([long](Get-Item -LiteralPath $source).Length -ne [long]$matchingBaseline[0].Bytes) {
        throw "Source FF size changed since baseline. Rerun the OAT identity census: $source"
    }
    if (-not (Test-Path -LiteralPath $manifestFile -PathType Leaf)) {
        throw "Missing OAT TechniqueSet identity file: $manifestFile"
    }
    $proven = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
    if (-not [bool]$proven.Verified -or [string]$proven.PcFastFile -ine $source) {
        throw "OAT TechniqueSet proof not verified or source mismatch: $manifestFile"
    }
    foreach ($entry in @($proven.Entries)) {
        $name = [string]$entry.Name
        if ($name) { [void]$knownRootNames.Add($name) }
        $assetRows.Add([pscustomobject]@{
            Role=[string]$run.Role; FastFile=$source
            AssetIndex=[int]$entry.AssetIndex; TechniqueSet=$name
        })
    }
    $folderName = [IO.Path]::GetFileNameWithoutExtension($source)
    $suffix = [string]$run.Role -replace '[^a-zA-Z0-9_]', '_'
    $folder = Join-Path $work ($folderName+'_'+$suffix)
    if (Test-Path -LiteralPath $folder -PathType Container) {
        Remove-Item -LiteralPath $folder -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $oldPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& $UnlinkerExe '--no-color' '--include-assets' 'material' '--output-folder' $folder $source 2>&1 |
            ForEach-Object { [string]$_ })
        $code = [int]$LASTEXITCODE
    } finally {
        $ErrorActionPreference = $oldPreference
    }
    $logFile = Join-Path $work ($folderName+'_'+$suffix+'.oat.log')
    [IO.File]::WriteAllLines($logFile,[string[]]$lines,[Text.UTF8Encoding]::new($false))
    if ($code -ne 0) {
        foreach ($line in @($lines | Select-Object -Last 15)) { Write-Host $line }
        throw "OAT material export failed for $source (exit=$code). Details: $logFile"
    }
    $materialFolders = @(Get-ChildItem -LiteralPath $folder -Directory -Recurse -Filter 'materials' |
        Sort-Object FullName)
    $matCount = 0
    $refCount = 0
    $localNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($matDir in $materialFolders) {
        foreach ($file in @(Get-ChildItem -LiteralPath $matDir.FullName -File -Recurse -Filter '*.json')) {
            $mat = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
            $p = $mat.PSObject.Properties['techniqueSet']
            $target = if ($null -ne $p) { ([string]$p.Value).Trim() } else { '' }
            $matCount++
            if ($target) {
                $refCount++
                [void]$allReferenced.Add($target)
                [void]$localNames.Add($target)
            }
            $materialEntries.Add([pscustomobject]@{
                Role=[string]$run.Role
                FastFile=$source
                ExportedFile=$file.FullName
                TechniqueSet=$target
            })
        }
    }
    $expected=0
    $maybe=$matchingBaseline[0].AssetCounts.PSObject.Properties['material']
    if ($null -ne $maybe) { $expected=[int]$maybe.Value }
    if ($expected -gt 0 -and $matCount -eq 0) {
        throw "OAT listed $expected Material(s) but exported no Material JSON for $source. See $logFile"
    }
    $sourceRecords.Add([pscustomobject]@{
        Role=[string]$run.Role; FastFile=$source
        ExpectedOatMaterialEntries=$expected; MaterialJsonFiles=$matCount
        NonEmptyTechniqueReferences=$refCount
        UniqueTechniqueReferences=$localNames.Count
        Log=$logFile
    })
    Write-Host "[MATERIAL-REACH] $($run.Role): $matCount Material JSON, $refCount nonempty technique references, $($localNames.Count) unique targets"
}

$rootCount = $assetRows.Count
$referenced = 0
$missing = 0
$reachabilityRows = New-Object 'System.Collections.Generic.List[object]'
foreach ($entry in $assetRows) {
    $isReferenced=$allReferenced.Contains([string]$entry.TechniqueSet)
    if ($isReferenced) { $referenced++ } else { $missing++ }
    $reachabilityRows.Add([pscustomobject]@{
        Role=$entry.Role; FastFile=$entry.FastFile; AssetIndex=$entry.AssetIndex
        TechniqueSet=$entry.TechniqueSet
        ReferencedByAnyExportedMaterial=$isReferenced
        Policy='DIAGNOSTIC_ONLY_NO_AUTOMATIC_ROOT_REMOVAL'
    })
}
$external=@($allReferenced | Where-Object { -not $knownRootNames.Contains($_) } | Sort-Object)
$csv=Join-Path $work 'pc_techset_material_reachability.csv'
$materialCsv=Join-Path $work 'pc_material_techset_references.csv'
$reachabilityRows.ToArray() | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8
$materialEntries.ToArray() | Export-Csv -LiteralPath $materialCsv -NoTypeInformation -Encoding UTF8

$report=[ordered]@{
    format='WaW360.SecondMapMaterialReachability.v1'
    mapFolder=$MapName
    canonicalRuntimeName=$canonical
    physicalTechniqueSetRows=$rootCount
    previouslyVerifiedTechniqueSetRows=[int]$identity.identityMappedTechniqueSets
    distinctTechniqueSetsReferencedByMaterial=$allReferenced.Count
    physicalTechniqueSetRowsReferenced=$referenced
    physicalTechniqueSetRowsNotObservedInMaterials=$missing
    materialReferencesWithoutPhysicalTechniqueSet=[object[]]$external
    inputFastfiles=[object[]]$sourceRecords.ToArray()
    reachabilityCsv=$csv; materialReferencesCsv=$materialCsv
    productionMaterialReady=$false
    policy=@(
        'A Material->TechniqueSet name relationship is evidence of reachability, not proof of complete shader graph ownership.',
        'Unused-root candidates may also be referenced by other asset types, stock zones or override layers; do not delete them automatically.',
        'Non-null PC shader graphs are not Xbox/Xenos-compatible and cannot be copied or silently dropped.',
        'No Xbox-native material graph, shader, fastfile, or streamed XMA is emitted.'
    )
}
$json=Join-Path $work 'material_reachability_result.json'
$txt=Join-Path $work 'material_reachability_result.txt'
$report | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $json -Encoding UTF8
$body=New-Object 'System.Collections.Generic.List[string]'
$body.Add('AZTEC PC MATERIAL -> TECHNIQUESET REACHABILITY, OAT SEMANTIC')
$body.Add('=================================================================')
$body.Add("Map: $canonical")
$body.Add("Physical PC TechniqueSet entries: $rootCount")
$body.Add("Material-export referenced physical entries: $referenced")
$body.Add("Material-export unobserved physical entries: $missing")
$body.Add("Distinct TechniqueSet names used by exported Materials: $($allReferenced.Count)")
$body.Add("Material references without physical TechniqueSet: $($external.Count)")
foreach ($line in $sourceRecords) {
    $body.Add("  $($line.Role): OAT material JSON=$($line.MaterialJsonFiles) nonemptyRefs=$($line.NonEmptyTechniqueReferences) uniqueTechsets=$($line.UniqueTechniqueReferences) expectedOatMaterialRows=$($line.ExpectedOatMaterialEntries)")
}
$body.Add('UNRESOLVED EXTERNAL / STOCK / OVERRIDE TECHNIQUE NAMES (first 40)')
foreach ($name in @($external | Select-Object -First 40)) { $body.Add("  $name") }
$body.Add('Do not delete unobserved roots or promote shader graphs based on this diagnostic alone.')
$body.Add("All per-root reachability rows: $csv")
$body.Add("All exported material->technique references: $materialCsv")
[IO.File]::WriteAllLines($txt,[string[]]$body.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[MATERIAL-REACH] ====================== RESULT ============================='
Write-Host "[MATERIAL-REACH] Physical TechniqueSet entries: $rootCount"
Write-Host "[MATERIAL-REACH] Referenced by exported Materials: $referenced; unobserved: $missing"
Write-Host "[MATERIAL-REACH] Distinct Material targets=$($allReferenced.Count); external/stock candidates=$($external.Count)"
Write-Host "[MATERIAL-REACH] Report: $txt"
Write-Host "[MATERIAL-REACH] JSON:   $json"
Write-Host '[MATERIAL-REACH] PASS: diagnostic only; hardware readiness unchanged.'
