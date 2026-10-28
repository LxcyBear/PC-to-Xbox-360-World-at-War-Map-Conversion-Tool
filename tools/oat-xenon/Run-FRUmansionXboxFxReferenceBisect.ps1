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
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4w_fx_reference_bisect'
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

function Invoke-FxCase {
    param(
        [Parameter(Mandatory=$true)][string]$CaseName,
        [Parameter(Mandatory=$true)][AllowEmptyCollection()][object[]]$Targets,
        [Parameter(Mandatory=$true)][string[]]$BaseZones,
        [Parameter(Mandatory=$true)][string]$Root
    )

    $caseRoot = Join-Path $Root $CaseName
    $workspace = Join-Path $caseRoot 'workspace'
    $sourceRoot = Join-Path $workspace 'zone_source'
    $assetRoot = Join-Path $workspace 'raw'
    $linkOut = Join-Path $caseRoot 'linked'
    $logRoot = Join-Path $caseRoot 'logs'

    foreach ($path in @($sourceRoot, $assetRoot, $linkOut, $logRoot)) {
        New-Item -ItemType Directory -Force -Path $path | Out-Null
    }

    $project = 'fx_bisect_' + ($CaseName -replace '[^A-Za-z0-9_]', '_')
    $zoneFile = Join-Path $sourceRoot ($project + '.zone')

    $zoneLines = New-Object 'System.Collections.Generic.List[string]'
    $zoneLines.Add('>game,T4')
    $zoneLines.Add('>platform,xbox360')
    $zoneLines.Add(('>name,{0}' -f $project))
    $zoneLines.Add('')

    foreach ($target in $Targets) {
        $name = [string]$target.Name
        $escaped = $name.Replace('"', '""')
        $zoneLines.Add(('fx,,"{0}"' -f $escaped))
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

    foreach ($zone in $BaseZones) {
        $args.Add('--load')
        $args.Add($zone)
    }
    $args.Add($project)

    $logPath = Join-Path $logRoot 'linker.log'

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

    $pass = $exitCode -eq 0 -and $linked.Count -eq 1

    return [pscustomobject]@{
        Case = $CaseName
        Count = $Targets.Count
        ExitCode = $exitCode
        FastFiles = $linked.Count
        Pass = $pass
        FirstIndex = if ($Targets.Count -gt 0) { [int]$Targets[0].Index } else { -1 }
        LastIndex = if ($Targets.Count -gt 0) { [int]$Targets[$Targets.Count - 1].Index } else { -1 }
        FirstName = if ($Targets.Count -gt 0) { [string]$Targets[0].Name } else { '' }
        LastName = if ($Targets.Count -gt 0) { [string]$Targets[$Targets.Count - 1].Name } else { '' }
        Log = $logPath
    }
}

$plan = Get-Content -LiteralPath $ResolutionPlanManifest -Raw | ConvertFrom-Json
if ([string]$plan.format -ne 'WaW360.Nuclear4V.XboxCriticalResolutionPlan.v1') {
    throw "Unexpected resolution-plan format: $($plan.format)"
}

$fx = @(
    $plan.runtimeReferences |
    Where-Object { [string]$_.Type -eq 'fx' } |
    Sort-Object Index
)
if ($fx.Count -ne 110) {
    Write-Host "[NUCLEAR-4W-FX] WARNING: expected 110 FX reference candidates; found $($fx.Count)." -ForegroundColor Yellow
}
if ($fx.Count -lt 2) {
    throw "Need at least two FX reference candidates for a bisect."
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

$results = New-Object 'System.Collections.Generic.List[object]'

Write-Host '[NUCLEAR-4W-FX] ============================================================='
Write-Host '[NUCLEAR-4W-FX] FX REFERENCE CRASH BISECT'
Write-Host '[NUCLEAR-4W-FX] ============================================================='
Write-Host "[NUCLEAR-4W-FX] FX candidates      : $($fx.Count)"
Write-Host "[NUCLEAR-4W-FX] Retail load zones : $($baseZones.Count)"
Write-Host '[NUCLEAR-4W-FX] Binary-searching the smallest crashing FX prefix.'
Write-Host ''

$single = Invoke-FxCase -CaseName 'single_first' -Targets @($fx[0]) -BaseZones ([string[]]$baseZones.ToArray()) -Root $OutputRoot
$results.Add($single)
if (-not [bool]$single.Pass) {
    throw "First FX singleton unexpectedly failed; canary regression. See $($single.Log)"
}

$full = Invoke-FxCase -CaseName ('prefix_{0:D3}' -f $fx.Count) -Targets ([object[]]$fx) -BaseZones ([string[]]$baseZones.ToArray()) -Root $OutputRoot
$results.Add($full)
if ([bool]$full.Pass) {
    $manifest = [ordered]@{
        format = 'WaW360.Nuclear4W.FxReferenceBisect.v1'
        sourcePlan = (Resolve-Path -LiteralPath $ResolutionPlanManifest).Path
        fxCandidates = $fx.Count
        unexpectedFullPass = $true
        results = [object[]]$results.ToArray()
    }
    $manifestPath = Join-Path $OutputRoot 'nuclear_4w_fx_reference_bisect.json'
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    Write-Host '[NUCLEAR-4W-FX] Full 110-FX batch unexpectedly passed this time.' -ForegroundColor Yellow
    Write-Host "[NUCLEAR-4W-FX] Manifest: $manifestPath"
    exit 0
}

$low = 1
$high = $fx.Count

while (($high - $low) -gt 1) {
    $mid = [int][Math]::Floor(($low + $high) / 2.0)
    $prefix = @($fx | Select-Object -First $mid)
    $case = Invoke-FxCase -CaseName ('prefix_{0:D3}' -f $mid) -Targets ([object[]]$prefix) -BaseZones ([string[]]$baseZones.ToArray()) -Root $OutputRoot
    $results.Add($case)

    Write-Host ("[NUCLEAR-4W-FX] prefix {0,3}: {1} exit={2}" -f $mid, $(if ($case.Pass) { 'PASS' } else { 'FAIL' }), $case.ExitCode)

    if ([bool]$case.Pass) {
        $low = $mid
    }
    else {
        $high = $mid
    }
}

$firstFailCount = $high
$suspect = $fx[$firstFailCount - 1]
$previous = if ($firstFailCount -gt 1) { $fx[$firstFailCount - 2] } else { $null }

Write-Host ''
Write-Host "[NUCLEAR-4W-FX] Smallest failing prefix : $firstFailCount"
Write-Host "[NUCLEAR-4W-FX] Last passing prefix     : $low"
Write-Host "[NUCLEAR-4W-FX] Boundary root           : #$([int]$suspect.Index) $([string]$suspect.Name)"

$suspectSingle = Invoke-FxCase -CaseName 'suspect_single' -Targets @($suspect) -BaseZones ([string[]]$baseZones.ToArray()) -Root $OutputRoot
$results.Add($suspectSingle)

$withoutSuspect = @()
if ($firstFailCount -gt 1) {
    $withoutSuspect = @($fx | Select-Object -First ($firstFailCount - 1))
}
if ($firstFailCount -lt $fx.Count) {
    $withoutSuspect += $fx[$firstFailCount]
}
$replacementCase = $null
if ($withoutSuspect.Count -eq $firstFailCount) {
    $replacementCase = Invoke-FxCase -CaseName 'boundary_count_replacement' -Targets ([object[]]$withoutSuspect) -BaseZones ([string[]]$baseZones.ToArray()) -Root $OutputRoot
    $results.Add($replacementCase)
}

$diagnosis = 'unknown'
if (-not [bool]$suspectSingle.Pass) {
    $diagnosis = 'asset-specific-fx-reference-crash'
}
elseif ($null -ne $replacementCase -and -not [bool]$replacementCase.Pass) {
    $diagnosis = 'fx-reference-count-or-cumulative-state-limit'
}
elseif ($null -ne $replacementCase -and [bool]$replacementCase.Pass) {
    $diagnosis = 'fx-reference-interaction-boundary'
}
else {
    $diagnosis = 'fx-prefix-boundary-needs-followup'
}

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4W.FxReferenceBisect.v1'
    sourcePlan = (Resolve-Path -LiteralPath $ResolutionPlanManifest).Path
    xboxGameRoot = (Resolve-Path -LiteralPath $XboxGameRoot).Path
    fxCandidates = $fx.Count
    lastPassingPrefix = $low
    smallestFailingPrefix = $firstFailCount
    suspect = [pscustomobject]@{
        Index = [int]$suspect.Index
        Name = [string]$suspect.Name
        BestClass = [string]$suspect.BestClass
        PreferredZone = [string]$suspect.PreferredZone
    }
    previous = if ($null -eq $previous) { $null } else {
        [pscustomobject]@{
            Index = [int]$previous.Index
            Name = [string]$previous.Name
        }
    }
    suspectSingletonPass = [bool]$suspectSingle.Pass
    replacementBoundaryPass = if ($null -eq $replacementCase) { $null } else { [bool]$replacementCase.Pass }
    diagnosis = $diagnosis
    results = [object[]]$results.ToArray()
    hardwareApproved = $false
}

$manifestPath = Join-Path $OutputRoot 'nuclear_4w_fx_reference_bisect.json'
$reportPath = Join-Path $OutputRoot 'nuclear_4w_fx_reference_bisect.txt'
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4W-XBOX - FX REFERENCE CRASH BISECT')
$report.Add(('=' * 100))
$report.Add(("FX candidates:               {0:N0}" -f $fx.Count))
$report.Add(("Last passing prefix:         {0:N0}" -f $low))
$report.Add(("Smallest failing prefix:     {0:N0}" -f $firstFailCount))
$report.Add(("Boundary root:               #{0} {1}" -f [int]$suspect.Index, [string]$suspect.Name))
$report.Add(("Boundary singleton pass:     {0}" -f [bool]$suspectSingle.Pass))
$report.Add(("Replacement boundary pass:   {0}" -f $(if ($null -eq $replacementCase) { '<n/a>' } else { [bool]$replacementCase.Pass })))
$report.Add(("Diagnosis:                   {0}" -f $diagnosis))
$report.Add('')
foreach ($row in $results.ToArray()) {
    $report.Add(("{0,-30} count={1,4} exit={2,12} pass={3}" -f $row.Case, $row.Count, $row.ExitCode, $row.Pass))
}
[IO.File]::WriteAllLines($reportPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4W-FX] ====================== RESULT ================================'
Write-Host "[NUCLEAR-4W-FX] Last passing prefix     : $low"
Write-Host "[NUCLEAR-4W-FX] Smallest failing prefix : $firstFailCount"
Write-Host "[NUCLEAR-4W-FX] Boundary FX             : #$([int]$suspect.Index) $([string]$suspect.Name)"
Write-Host "[NUCLEAR-4W-FX] Boundary singleton pass : $([bool]$suspectSingle.Pass)"
if ($null -ne $replacementCase) {
    Write-Host "[NUCLEAR-4W-FX] Replacement-count pass  : $([bool]$replacementCase.Pass)"
}
Write-Host "[NUCLEAR-4W-FX] Diagnosis               : $diagnosis"
Write-Host "[NUCLEAR-4W-FX] Manifest                : $manifestPath"
Write-Host "[NUCLEAR-4W-FX] Report                  : $reportPath"
Write-Host '[NUCLEAR-4W-FX] ================================================================='
