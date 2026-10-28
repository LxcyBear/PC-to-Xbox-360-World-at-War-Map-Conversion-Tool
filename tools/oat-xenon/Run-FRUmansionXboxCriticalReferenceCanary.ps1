param(
    [string]$ResolutionPlanManifest = '',
    [string]$XboxGameRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\XBOX GAME FILES',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$linker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Linker.exe'

if ([string]::IsNullOrWhiteSpace($ResolutionPlanManifest)) {
    $ResolutionPlanManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4v_xbox_critical_resolution_plan\nuclear_4v_xbox_critical_resolution_plan.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4w_xbox_reference_canary'
}

foreach ($required in @($ResolutionPlanManifest, $linker)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required file not found: $required"
    }
}
if (-not (Test-Path -LiteralPath $XboxGameRoot -PathType Container)) {
    throw "Xbox retail game-file folder not found: $XboxGameRoot"
}

function Invoke-NativeCapture {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][object[]]$Arguments,
        [Parameter(Mandatory=$true)][string]$LogPath
    )

    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $FilePath @Arguments 2>&1 |
            Tee-Object -FilePath $LogPath |
            ForEach-Object { Write-Host ([string]$_) }
        return [int]$LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

function New-ReferenceProject {
    param(
        [Parameter(Mandatory=$true)][string]$CaseName,
        [Parameter(Mandatory=$true)][AllowEmptyCollection()][object[]]$Targets,
        [Parameter(Mandatory=$true)][bool]$UseLoads,
        [Parameter(Mandatory=$true)][string]$CaseRoot,
        [Parameter(Mandatory=$true)][string[]]$BaseZones
    )

    $workspace = Join-Path $CaseRoot 'workspace'
    $sourceRoot = Join-Path $workspace 'zone_source'
    $assetRoot = Join-Path $workspace 'raw'
    $linkOut = Join-Path $CaseRoot 'linked'
    $logRoot = Join-Path $CaseRoot 'logs'

    foreach ($path in @($sourceRoot, $assetRoot, $linkOut, $logRoot)) {
        New-Item -ItemType Directory -Force -Path $path | Out-Null
    }

    $project = 'canary_' + ($CaseName -replace '[^A-Za-z0-9_]', '_')
    $zoneFile = Join-Path $sourceRoot ($project + '.zone')

    $zoneLines = New-Object 'System.Collections.Generic.List[string]'
    $zoneLines.Add('>game,T4')
    $zoneLines.Add('>platform,xbox360')
    $zoneLines.Add(('>name,{0}' -f $project))
    $zoneLines.Add('')

    foreach ($target in $Targets) {
        $type = [string]$target.Type
        $name = [string]$target.Name
        if ([string]::IsNullOrWhiteSpace($type) -or [string]::IsNullOrWhiteSpace($name)) {
            throw "Canary '$CaseName' contains a blank type/name."
        }

        $escaped = $name.Replace('"', '""')
        $zoneLines.Add(('{0},,"{1}"' -f $type, $escaped))
    }

    [IO.File]::WriteAllLines(
        $zoneFile,
        [string[]]$zoneLines.ToArray(),
        [Text.UTF8Encoding]::new($false))

    $args = New-Object 'System.Collections.Generic.List[object]'
    foreach ($arg in @(
        '--verbose',
        '--no-color',
        '--base-folder', $workspace,
        '--source-search-path', $sourceRoot,
        '--asset-search-path', $assetRoot,
        '--output-folder', $linkOut
    )) {
        $args.Add($arg)
    }

    if ($UseLoads) {
        foreach ($zone in $BaseZones) {
            $args.Add('--load')
            $args.Add($zone)
        }
    }

    $args.Add($project)

    $logPath = Join-Path $logRoot 'linker.log'
    Write-Host ''
    Write-Host ("[NUCLEAR-4W-CANARY] CASE {0}: refs={1}, loads={2}" -f $CaseName, $Targets.Count, $UseLoads)

    Push-Location $workspace
    try {
        $exitCode = Invoke-NativeCapture -FilePath $linker -Arguments ([object[]]$args.ToArray()) -LogPath $logPath
    }
    finally {
        Pop-Location
    }

    $linked = @()
    if ($exitCode -eq 0) {
        $linked = @(Get-ChildItem -LiteralPath $linkOut -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue)
    }

    $tail = ''
    if (Test-Path -LiteralPath $logPath -PathType Leaf) {
        $tail = (@(Get-Content -LiteralPath $logPath | Select-Object -Last 12) -join ' | ')
    }

    $status = if ($exitCode -eq 0 -and ($Targets.Count -eq 0 -or $linked.Count -eq 1)) { 'PASS' } else { 'FAIL' }
    Write-Host ("[NUCLEAR-4W-CANARY] CASE {0}: {1} exit={2} ff={3}" -f $CaseName, $status, $exitCode, $linked.Count)

    return [pscustomobject]@{
        Case = $CaseName
        ReferenceCount = $Targets.Count
        UseRetailLoads = $UseLoads
        ExitCode = $exitCode
        FastFiles = $linked.Count
        Status = $status
        Log = $logPath
        LogTail = $tail
        Targets = [object[]]$Targets
    }
}

$plan = Get-Content -LiteralPath $ResolutionPlanManifest -Raw | ConvertFrom-Json
if ([string]$plan.format -ne 'WaW360.Nuclear4V.XboxCriticalResolutionPlan.v1') {
    throw "Unexpected resolution-plan format: $($plan.format)"
}

$targets = @($plan.runtimeReferences)
if ($targets.Count -ne [int]$plan.runtimeReferenceCandidates) {
    throw "Reference target count mismatch: $($targets.Count) != $([int]$plan.runtimeReferenceCandidates)."
}

$baseNames = @(
    'common.ff',
    'code_post_gfx.ff',
    'ui.ff',
    'patch.ff',
    'nazi_zombie_prototype.ff',
    'nazi_zombie_prototype_load.ff'
)

$baseZones = New-Object 'System.Collections.Generic.List[string]'
foreach ($name in $baseNames) {
    $matches = @(Get-ChildItem -LiteralPath $XboxGameRoot -Filter $name -File -Recurse -ErrorAction SilentlyContinue)
    if ($matches.Count -gt 1) {
        throw "Multiple retail base zones named '$name' were found."
    }
    if ($matches.Count -eq 1) {
        $baseZones.Add($matches[0].FullName)
    }
}

if ($baseZones.Count -eq 0) {
    throw 'No retail base zones were found.'
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$byType = @{}
foreach ($type in @('rawfile','weapon','fx')) {
    $family = @($targets | Where-Object { [string]$_.Type -eq $type } | Sort-Object Index)
    if ($family.Count -eq 0) {
        throw "Reference plan has no '$type' candidates."
    }
    $byType[$type] = $family
}

$results = New-Object 'System.Collections.Generic.List[object]'

Write-Host '[NUCLEAR-4W-CANARY] ========================================================='
Write-Host '[NUCLEAR-4W-CANARY] T4 XENON REFERENCE LINKER DIAGNOSTIC MATRIX'
Write-Host '[NUCLEAR-4W-CANARY] ========================================================='
Write-Host "[NUCLEAR-4W-CANARY] Reference candidates : $($targets.Count)"
Write-Host "[NUCLEAR-4W-CANARY] Retail load zones    : $($baseZones.Count)"
Write-Host '[NUCLEAR-4W-CANARY] Goal: separate Linker --load failure from reference/family failure.'
Write-Host ''

$empty = @()
$case = New-ReferenceProject -CaseName 'empty_no_loads' -Targets $empty -UseLoads $false -CaseRoot (Join-Path $OutputRoot 'empty_no_loads') -BaseZones ([string[]]$baseZones.ToArray())
$results.Add($case)

$case = New-ReferenceProject -CaseName 'empty_with_loads' -Targets $empty -UseLoads $true -CaseRoot (Join-Path $OutputRoot 'empty_with_loads') -BaseZones ([string[]]$baseZones.ToArray())
$results.Add($case)

foreach ($type in @('rawfile','weapon','fx')) {
    $one = @($byType[$type][0])

    $case = New-ReferenceProject -CaseName ("{0}_one_no_loads" -f $type) -Targets $one -UseLoads $false -CaseRoot (Join-Path $OutputRoot ("{0}_one_no_loads" -f $type)) -BaseZones ([string[]]$baseZones.ToArray())
    $results.Add($case)

    $loadedOne = New-ReferenceProject -CaseName ("{0}_one_with_loads" -f $type) -Targets $one -UseLoads $true -CaseRoot (Join-Path $OutputRoot ("{0}_one_with_loads" -f $type)) -BaseZones ([string[]]$baseZones.ToArray())
    $results.Add($loadedOne)

    if ([string]$loadedOne.Status -eq 'PASS') {
        $family = [object[]]$byType[$type]
        $case = New-ReferenceProject -CaseName ("{0}_family_with_loads" -f $type) -Targets $family -UseLoads $true -CaseRoot (Join-Path $OutputRoot ("{0}_family_with_loads" -f $type)) -BaseZones ([string[]]$baseZones.ToArray())
        $results.Add($case)
    }
}

$resultArray = [object[]]$results.ToArray()
$emptyNoLoad = $resultArray | Where-Object Case -eq 'empty_no_loads' | Select-Object -First 1
$emptyLoaded = $resultArray | Where-Object Case -eq 'empty_with_loads' | Select-Object -First 1

$diagnosis = 'mixed-reference-failure'
if ([string]$emptyNoLoad.Status -ne 'PASS') {
    $diagnosis = 'xenon-linker-baseline-failure'
}
elseif ([string]$emptyLoaded.Status -ne 'PASS') {
    $diagnosis = 'xbox-zone-preload-crashes-linker'
}
else {
    $singletonLoaded = @($resultArray | Where-Object { $_.Case -match '_one_with_loads$' })
    if (@($singletonLoaded | Where-Object Status -eq 'PASS').Count -eq 0) {
        $diagnosis = 't4-xenon-top-level-reference-path-broken'
    }
    elseif (@($singletonLoaded | Where-Object Status -eq 'FAIL').Count -eq 0) {
        $familyLoaded = @($resultArray | Where-Object { $_.Case -match '_family_with_loads$' })
        if ($familyLoaded.Count -eq 3 -and @($familyLoaded | Where-Object Status -eq 'PASS').Count -eq 3) {
            $diagnosis = 'all-families-survive-individually-combined-only-failure'
        }
        else {
            $diagnosis = 'family-scale-or-specific-asset-failure'
        }
    }
    else {
        $diagnosis = 'family-specific-reference-failure'
    }
}

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4W.XboxReferenceCanary.v1'
    sourcePlan = (Resolve-Path -LiteralPath $ResolutionPlanManifest).Path
    xboxGameRoot = (Resolve-Path -LiteralPath $XboxGameRoot).Path
    retailBaseZones = [object[]]$baseZones.ToArray()
    referenceCandidates = $targets.Count
    diagnosis = $diagnosis
    results = $resultArray
    hardwareApproved = $false
}

$manifestPath = Join-Path $OutputRoot 'nuclear_4w_xbox_reference_canary.json'
$reportPath = Join-Path $OutputRoot 'nuclear_4w_xbox_reference_canary.txt'
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4W-XBOX - REFERENCE LINKER DIAGNOSTIC MATRIX')
$report.Add(('=' * 100))
foreach ($row in $resultArray) {
    $report.Add(("{0,-30} refs={1,4:N0} loads={2,-5} exit={3,12} ff={4} {5}" -f $row.Case, $row.ReferenceCount, $row.UseRetailLoads, $row.ExitCode, $row.FastFiles, $row.Status))
}
$report.Add('')
$report.Add(("DIAGNOSIS: {0}" -f $diagnosis))
[IO.File]::WriteAllLines($reportPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4W-CANARY] ====================== RESULT ============================'
foreach ($row in $resultArray) {
    Write-Host ("[NUCLEAR-4W-CANARY] {0,-30} refs={1,3} loads={2,-5} exit={3,12} {4}" -f $row.Case, $row.ReferenceCount, $row.UseRetailLoads, $row.ExitCode, $row.Status)
}
Write-Host "[NUCLEAR-4W-CANARY] Diagnosis : $diagnosis"
Write-Host "[NUCLEAR-4W-CANARY] Manifest  : $manifestPath"
Write-Host "[NUCLEAR-4W-CANARY] Report    : $reportPath"
Write-Host '[NUCLEAR-4W-CANARY] ================================================================='
Write-Host '[NUCLEAR-4W-CANARY] Diagnostic complete. This stage intentionally does not grant production credit.'
