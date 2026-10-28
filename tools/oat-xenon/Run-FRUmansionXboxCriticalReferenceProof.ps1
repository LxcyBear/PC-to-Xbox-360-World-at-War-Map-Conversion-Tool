param(
    [string]$ResolutionPlanManifest = '',
    [string]$XboxGameRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\XBOX GAME FILES',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$linker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Linker.exe'
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($ResolutionPlanManifest)) {
    $ResolutionPlanManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4v_xbox_critical_resolution_plan\nuclear_4v_xbox_critical_resolution_plan.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4w_xbox_reference_proof'
}

foreach ($required in @($ResolutionPlanManifest, $linker, $unlinker)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required file not found: $required"
    }
}
if (-not (Test-Path -LiteralPath $XboxGameRoot -PathType Container)) {
    throw "Xbox retail game-file folder not found: $XboxGameRoot"
}

function Invoke-NativeTee {
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

function Normalize-Type {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    $v = $Value.Trim().ToLowerInvariant()
    switch ($v) {
        'techset' { return 'techniqueset' }
        'loaded_sound' { return 'loadedsound' }
        'clipmap_pvs' { return 'clipmap' }
        'gameworld_sp' { return 'gameworldsp' }
        default { return $v }
    }
}

function Normalize-Name {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    $v = $Value.Trim().Trim('"')
    while ($v.StartsWith(',', [StringComparison]::Ordinal)) {
        $v = $v.Substring(1)
    }
    return $v.ToLowerInvariant()
}

function Strip-LogPrefix {
    param([string]$Line)
    $line = $Line.Trim()
    if ($line.StartsWith('[', [StringComparison]::Ordinal)) {
        $close = $line.IndexOf(']')
        if ($close -ge 0 -and $close + 1 -lt $line.Length) {
            $line = $line.Substring($close + 1).TrimStart()
        }
    }
    $colon = $line.IndexOf(':')
    if ($colon -gt 0 -and $colon -lt 12) {
        $prefix = $line.Substring(0, $colon)
        if ($prefix -match '^[A-Za-z ]+$') {
            $line = $line.Substring($colon + 1).TrimStart()
        }
    }
    return $line
}

$plan = Get-Content -LiteralPath $ResolutionPlanManifest -Raw | ConvertFrom-Json
if ([string]$plan.format -ne 'WaW360.Nuclear4V.XboxCriticalResolutionPlan.v1') {
    throw "Unexpected resolution-plan format: $($plan.format)"
}

$targets = @($plan.runtimeReferences)
if ($targets.Count -eq 0) {
    throw 'Resolution plan contains zero runtime-reference candidates.'
}
if ($targets.Count -ne [int]$plan.runtimeReferenceCandidates) {
    throw "Runtime-reference count mismatch: list=$($targets.Count), manifest=$([int]$plan.runtimeReferenceCandidates)."
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

$workspace = Join-Path $OutputRoot 'workspace'
$sourceRoot = Join-Path $workspace 'zone_source'
$assetRoot = Join-Path $workspace 'raw'
$linkOut = Join-Path $OutputRoot 'linked'
$logRoot = Join-Path $OutputRoot 'logs'
foreach ($path in @($sourceRoot, $assetRoot, $linkOut, $logRoot)) {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

$project = 'nuclear_4w_xbox_reference_proof'
$zoneFile = Join-Path $sourceRoot ($project + '.zone')

$zoneLines = New-Object 'System.Collections.Generic.List[string]'
$zoneLines.Add('>game,T4')
$zoneLines.Add('>platform,xbox360')
$zoneLines.Add(('>name,{0}' -f $project))
$zoneLines.Add('')

foreach ($target in ($targets | Sort-Object Index)) {
    $type = [string]$target.Type
    $name = [string]$target.Name
    if ([string]::IsNullOrWhiteSpace($type) -or [string]::IsNullOrWhiteSpace($name)) {
        throw "Reference target has blank type/name at root #$([int]$target.Index)."
    }

    # OAT zone syntax: an empty second CSV column marks an explicit reference asset.
    # Example: weapon,,kar98k
    $escaped = $name.Replace('"', '""')
    $zoneLines.Add(('{0},,"{1}"' -f $type, $escaped))
}

[IO.File]::WriteAllLines(
    $zoneFile,
    [string[]]$zoneLines.ToArray(),
    [Text.UTF8Encoding]::new($false))

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
        throw "Multiple retail base zones named '$name' were found under: $XboxGameRoot"
    }
    if ($matches.Count -eq 1) {
        $baseZones.Add($matches[0].FullName)
    }
}

if ($baseZones.Count -eq 0) {
    throw 'No retail common/base-zombie zones were found for reference-resolution proof.'
}

Write-Host '[NUCLEAR-4W-REF] ============================================================'
Write-Host '[NUCLEAR-4W-REF] RETAIL XBOX RUNTIME-REFERENCE RESOLUTION PROOF'
Write-Host '[NUCLEAR-4W-REF] ============================================================'
Write-Host "[NUCLEAR-4W-REF] Reference candidates : $($targets.Count)"
Write-Host "[NUCLEAR-4W-REF] Retail base zones    : $($baseZones.Count)"
foreach ($zone in $baseZones.ToArray()) {
    Write-Host "[NUCLEAR-4W-REF]   load: $zone"
}
Write-Host '[NUCLEAR-4W-REF] Retail base zones are preloaded into BOTH Linker and Unlinker.'
Write-Host '[NUCLEAR-4W-REF] This is an OAT/Xenon loader proof only; hardware remains fenced by Stage 4F.'
Write-Host ''

$linkLog = Join-Path $logRoot 'linker.log'
$linkArgs = New-Object 'System.Collections.Generic.List[object]'
foreach ($arg in @(
    '--verbose',
    '--no-color',
    '--base-folder', $workspace,
    '--source-search-path', $sourceRoot,
    '--asset-search-path', $assetRoot,
    '--output-folder', $linkOut
)) {
    $linkArgs.Add($arg)
}
foreach ($zone in $baseZones.ToArray()) {
    $linkArgs.Add('--load')
    $linkArgs.Add($zone)
}
$linkArgs.Add($project)

Push-Location $workspace
try {
    $linkCode = Invoke-NativeTee -FilePath $linker -Arguments ([object[]]$linkArgs.ToArray()) -LogPath $linkLog
}
finally {
    Pop-Location
}

$linked = @()
if ($linkCode -eq 0) {
    $linked = @(Get-ChildItem -LiteralPath $linkOut -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue)
}

$proofFf = if ($linked.Count -eq 1) { $linked[0].FullName } else { '' }
$unlinkCode = -1
$listLog = Join-Path $logRoot 'reference_resolution.list.log'

if ($linkCode -eq 0 -and $linked.Count -eq 1) {
    $unlinkArgs = New-Object 'System.Collections.Generic.List[object]'
    $unlinkArgs.Add('--list')
    $unlinkArgs.Add('--no-color')
    foreach ($zone in $baseZones.ToArray()) {
        $unlinkArgs.Add('--load')
        $unlinkArgs.Add($zone)
    }
    $unlinkArgs.Add($proofFf)

    Push-Location (Split-Path -Parent $proofFf)
    try {
        $unlinkCode = Invoke-NativeTee -FilePath $unlinker -Arguments ([object[]]$unlinkArgs.ToArray()) -LogPath $listLog
    }
    finally {
        Pop-Location
    }
}

$seen = @{}
$listRows = New-Object 'System.Collections.Generic.List[object]'

if (Test-Path -LiteralPath $listLog -PathType Leaf) {
    foreach ($raw in @(Get-Content -LiteralPath $listLog)) {
        $line = Strip-LogPrefix -Line ([string]$raw)
        $comma = $line.IndexOf(',')
        if ($comma -le 0 -or $comma + 1 -ge $line.Length) { continue }

        $type = Normalize-Type -Value $line.Substring(0, $comma)
        $name = Normalize-Name -Value $line.Substring($comma + 1)
        if ([string]::IsNullOrWhiteSpace($type) -or [string]::IsNullOrWhiteSpace($name)) { continue }

        $key = "$type|$name"
        if (-not $seen.ContainsKey($key)) {
            $seen[$key] = 0
        }
        $seen[$key] = [int]$seen[$key] + 1

        $listRows.Add([pscustomobject]@{
            Type = $type
            Name = $name
        })
    }
}

$resolved = New-Object 'System.Collections.Generic.List[object]'
$missing = New-Object 'System.Collections.Generic.List[object]'

foreach ($target in ($targets | Sort-Object Index)) {
    $type = Normalize-Type -Value ([string]$target.Type)
    $name = Normalize-Name -Value ([string]$target.Name)
    $key = "$type|$name"

    $row = [pscustomobject]@{
        Index = [int]$target.Index
        Type = [string]$target.Type
        Name = [string]$target.Name
        BestClass = [string]$target.BestClass
        PreferredZone = [string]$target.PreferredZone
    }

    if ($seen.ContainsKey($key)) {
        $resolved.Add($row)
    }
    else {
        $missing.Add($row)
    }
}

$referencePass =
    $linkCode -eq 0 -and
    $linked.Count -eq 1 -and
    $unlinkCode -eq 0 -and
    $resolved.Count -eq $targets.Count -and
    $missing.Count -eq 0

$byType = @(
    @('fx','weapon','rawfile') |
    ForEach-Object {
        $type = $_
        $expected = @($targets | Where-Object Type -eq $type)
        $ok = @($resolved.ToArray() | Where-Object Type -eq $type)
        [pscustomobject]@{
            Type = $type
            Expected = $expected.Count
            Resolved = $ok.Count
            Missing = $expected.Count - $ok.Count
        }
    }
)

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4W.XboxRuntimeReferenceProof.v1'
    sourcePlan = (Resolve-Path -LiteralPath $ResolutionPlanManifest).Path
    xboxGameRoot = (Resolve-Path -LiteralPath $XboxGameRoot).Path
    referenceCandidates = $targets.Count
    retailBaseZones = [object[]]$baseZones.ToArray()
    linkerExitCode = $linkCode
    unlinkerExitCode = $unlinkCode
    proofFastFile = $proofFf
    listedRows = $listRows.Count
    resolvedReferences = $resolved.Count
    missingReferences = $missing.Count
    byType = $byType
    resolved = [object[]]$resolved.ToArray()
    missing = [object[]]$missing.ToArray()
    referenceProofPass = $referencePass
    hardwareApproved = $false
    policy = @(
        'This proof serializes each candidate as an explicit T4 reference asset (type,,name).',
        'Pinned Xenon OAT preloads the user-owned retail common/base-zombie zones during both linking and unlink/list validation.',
        'A PASS proves exact type+identity resolution in OAT Xenon global asset pools; it is not by itself Xbox hardware approval.',
        'Stage 4F and the final integrated 6,811-root emitter must still consume and validate these references.'
    )
}

$manifestPath = Join-Path $OutputRoot 'nuclear_4w_xbox_reference_proof.json'
$reportPath = Join-Path $OutputRoot 'nuclear_4w_xbox_reference_proof.txt'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4W-XBOX - RUNTIME REFERENCE RESOLUTION PROOF')
$report.Add(('=' * 100))
$report.Add(("Reference candidates:        {0:N0}" -f $targets.Count))
$report.Add(("Retail base zones loaded:    {0:N0}" -f $baseZones.Count))
$report.Add(("Linker exit:                 {0}" -f $linkCode))
$report.Add(("Unlinker exit:               {0}" -f $unlinkCode))
$report.Add(("Resolved references:         {0:N0}" -f $resolved.Count))
$report.Add(("Missing references:          {0:N0}" -f $missing.Count))
foreach ($row in $byType) {
    $report.Add(("  {0,-10} {1,4:N0}/{2,4:N0} resolved; missing={3,4:N0}" -f
        $row.Type, $row.Resolved, $row.Expected, $row.Missing))
}
$report.Add(("REFERENCE PROOF PASS:         {0}" -f $referencePass))
$report.Add('')
$report.Add('MISSING')
foreach ($row in $missing.ToArray()) {
    $report.Add(("  root #{0,4}: {1,-10} {2}" -f $row.Index, $row.Type, $row.Name))
}
[IO.File]::WriteAllLines($reportPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4W-REF] ====================== RESULT ==============================='
Write-Host "[NUCLEAR-4W-REF] Reference candidates : $($targets.Count)"
Write-Host "[NUCLEAR-4W-REF] Linker exit          : $linkCode"
Write-Host "[NUCLEAR-4W-REF] Unlinker exit        : $unlinkCode"
Write-Host "[NUCLEAR-4W-REF] Resolved references  : $($resolved.Count)"
Write-Host "[NUCLEAR-4W-REF] Missing references   : $($missing.Count)"
foreach ($row in $byType) {
    Write-Host ("[NUCLEAR-4W-REF]   {0,-10} {1,3}/{2,3} resolved" -f
        $row.Type, $row.Resolved, $row.Expected)
}
Write-Host "[NUCLEAR-4W-REF] Reference proof pass : $referencePass"
Write-Host "[NUCLEAR-4W-REF] Manifest             : $manifestPath"
Write-Host "[NUCLEAR-4W-REF] Report               : $reportPath"
Write-Host '[NUCLEAR-4W-REF] ================================================================='

if (-not $referencePass) {
    Write-Host '[NUCLEAR-4W-REF] REFERENCE PROOF: BLOCKED' -ForegroundColor Yellow
    exit 4
}

Write-Host '[NUCLEAR-4W-REF] REFERENCE PROOF: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4W-REF] All candidate stock roots resolved through preloaded retail Xbox global pools.'
