[CmdletBinding()]
param(
    [string]$RemainingPlanManifest = '',
    [string]$StockWeaponRoundTripManifest = '',
    [string]$XboxGameRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\XBOX GAME FILES',
    [string]$OutputRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$linker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Linker.exe'
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($RemainingPlanManifest)) {
    $RemainingPlanManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4x_remaining_critical_plan\nuclear_4x_remaining_critical_plan.json'
}
if ([string]::IsNullOrWhiteSpace($StockWeaponRoundTripManifest)) {
    $StockWeaponRoundTripManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4y_stock_weapon_roundtrip\nuclear_4y_stock_weapon_roundtrip.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4z_stock_fx_roundtrip'
}

foreach ($required in @($RemainingPlanManifest, $StockWeaponRoundTripManifest, $linker, $unlinker)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required file not found: $required"
    }
}
if (-not (Test-Path -LiteralPath $XboxGameRoot -PathType Container)) {
    throw "Xbox retail game-file folder not found: $XboxGameRoot"
}

function Normalize-AssetName {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    $v = $Value.Trim().Trim('"')
    while ($v.StartsWith(',', [StringComparison]::Ordinal)) {
        $v = $v.Substring(1)
    }
    return $v
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

function Get-BaseZones {
    param([Parameter(Mandatory=$true)][string]$Root)

    $names = @(
        'common.ff',
        'code_post_gfx.ff',
        'ui.ff',
        'patch.ff',
        'nazi_zombie_prototype.ff',
        'nazi_zombie_prototype_load.ff'
    )

    $zones = New-Object 'System.Collections.Generic.List[string]'
    foreach ($name in $names) {
        $matches = @(Get-ChildItem -LiteralPath $Root -Filter $name -File -Recurse -ErrorAction SilentlyContinue)
        if ($matches.Count -gt 1) {
            throw "Multiple retail base zones named '$name' were found."
        }
        if ($matches.Count -eq 1) {
            $zones.Add($matches[0].FullName)
        }
    }

    if ($zones.Count -eq 0) {
        throw 'No retail common/base-Zombies zones were found.'
    }

    return [string[]]$zones.ToArray()
}

function Parse-FxList {
    param([Parameter(Mandatory=$true)][string]$LogPath)

    $names = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        return $names
    }

    foreach ($raw in @(Get-Content -LiteralPath $LogPath)) {
        $line = ([string]$raw).Trim()
        if (-not $line.StartsWith('fx,', [StringComparison]::OrdinalIgnoreCase)) { continue }

        $parts = $line.Split(@(','), 2)
        if ($parts.Count -ne 2) { continue }

        $name = Normalize-AssetName -Value $parts[1]
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            [void]$names.Add($name)
        }
    }

    return $names
}

$plan = Get-Content -LiteralPath $RemainingPlanManifest -Raw | ConvertFrom-Json
$weaponProof = Get-Content -LiteralPath $StockWeaponRoundTripManifest -Raw | ConvertFrom-Json

if ([string]$plan.format -ne 'WaW360.Nuclear4X.RemainingCriticalPlan.v1' -or
    -not [bool]$plan.reconciles -or
    [int]$plan.criticalFallbackRoots -ne 83) {
    throw 'Stock FX proof requires the reconciled 83-root remaining-critical plan.'
}

if ([string]$weaponProof.format -ne 'WaW360.Nuclear4Y.StockWeaponRoundTrip.v1' -or
    -not [bool]$weaponProof.roundTripPass -or
    [int]$weaponProof.targetWeapons -ne 3 -or
    [int]$weaponProof.linkerExitCode -ne 0 -or
    [int]$weaponProof.readbackExitCode -ne 0 -or
    [int]$weaponProof.exactInfoStringRoundTrips -ne 3) {
    throw 'Stock FX proof requires the passing 3/3 stock Weapon round-trip donor.'
}

$donorFf = [string]$weaponProof.linkedFastFile
if ([string]::IsNullOrWhiteSpace($donorFf) -or -not (Test-Path -LiteralPath $donorFf -PathType Leaf)) {
    throw "Passing stock Weapon donor FF was not found: $donorFf"
}
$donorFf = (Resolve-Path -LiteralPath $donorFf).Path

$targets = @(
    $plan.retailPayloadTargets |
    Where-Object { [string]$_.Type -eq 'fx' } |
    Sort-Object Index
)

if ($targets.Count -ne 2) {
    throw "Expected exactly 2 retail FX payload targets; found $($targets.Count)."
}

$baseZones = Get-BaseZones -Root $XboxGameRoot

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

$workspace = Join-Path $OutputRoot 'workspace'
$zoneSource = Join-Path $workspace 'zone_source'
$rawRoot = Join-Path $workspace 'raw'
$linkOut = Join-Path $OutputRoot 'linked'
$logs = Join-Path $OutputRoot 'logs'
$readback = Join-Path $OutputRoot 'readback'
foreach ($path in @($workspace,$zoneSource,$rawRoot,$linkOut,$logs,$readback)) {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

Write-Host '[NUCLEAR-4Z-FX] ============================================================='
Write-Host '[NUCLEAR-4Z-FX] STOCK XBOX FX BATCH ROUNDTRIP'
Write-Host '[NUCLEAR-4Z-FX] ============================================================='
Write-Host "[NUCLEAR-4Z-FX] Donor FF : $donorFf"
Write-Host "[NUCLEAR-4Z-FX] Targets  : $($targets.Count)"
foreach ($t in $targets) {
    Write-Host ("[NUCLEAR-4Z-FX]   root #{0,4}: {1} <- {2}" -f
        [int]$t.Index, [string]$t.Name, [string]$t.PreferredZone)
}
Write-Host ''

$project = 'nuclear_4z_stock_fx_roundtrip'
$zoneFile = Join-Path $zoneSource ($project + '.zone')
$zoneLines = New-Object 'System.Collections.Generic.List[string]'
$zoneLines.Add('>game,T4')
$zoneLines.Add('>platform,xbox360')
$zoneLines.Add(('>name,{0}' -f $project))
$zoneLines.Add('')
foreach ($t in $targets) {
    $name = Normalize-AssetName -Value ([string]$t.Name)
    $zoneLines.Add(('fx,"{0}"' -f $name.Replace('"','""')))
}
[IO.File]::WriteAllLines($zoneFile, [string[]]$zoneLines.ToArray(), [Text.UTF8Encoding]::new($false))

$linkArgs = New-Object 'System.Collections.Generic.List[object]'
foreach ($arg in @(
    '--verbose',
    '--no-color',
    '--base-folder',$workspace,
    '--source-search-path',$zoneSource,
    '--asset-search-path',$rawRoot,
    '--output-folder',$linkOut
)) { $linkArgs.Add($arg) }

foreach ($base in $baseZones) {
    $linkArgs.Add('--load')
    $linkArgs.Add($base)
}
$linkArgs.Add('--load')
$linkArgs.Add($donorFf)
$linkArgs.Add($project)

$linkLog = Join-Path $logs 'linker.log'
Write-Host '[NUCLEAR-4Z-FX] Building both real FX roots from the self-contained Weapon donor...'
Push-Location $workspace
try {
    $linkCode = Invoke-NativeTee -FilePath $linker -Arguments ([object[]]$linkArgs.ToArray()) -LogPath $linkLog
}
finally {
    Pop-Location
}

$linkedFf = ''
if ($linkCode -eq 0) {
    $linked = @(Get-ChildItem -LiteralPath $linkOut -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue)
    if ($linked.Count -eq 1) {
        $linkedFf = $linked[0].FullName
    }
}

$listCode = -1
$listLog = Join-Path $logs 'list.log'
$listedFx = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)

if ($linkCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($linkedFf)) {
    $listArgs = New-Object 'System.Collections.Generic.List[object]'
    $listArgs.Add('--list')
    $listArgs.Add('--no-color')
    foreach ($base in $baseZones) {
        $listArgs.Add('--load')
        $listArgs.Add($base)
    }
    # Deliberately do NOT load the Weapon donor here. The standalone FX FF must
    # open and expose both roots using only normal base-Zombies pools.
    $listArgs.Add($linkedFf)

    Write-Host ''
    Write-Host '[NUCLEAR-4Z-FX] Reloading standalone FX FF without the Weapon donor...'
    Push-Location (Split-Path -Parent $linkedFf)
    try {
        $listCode = Invoke-NativeTee -FilePath $unlinker -Arguments ([object[]]$listArgs.ToArray()) -LogPath $listLog
    }
    finally {
        Pop-Location
    }

    $parsed = Parse-FxList -LogPath $listLog
    foreach ($name in $parsed) {
        [void]$listedFx.Add($name)
    }
}

$rows = New-Object 'System.Collections.Generic.List[object]'
$missing = New-Object 'System.Collections.Generic.List[string]'
foreach ($t in $targets) {
    $name = Normalize-AssetName -Value ([string]$t.Name)
    $present = $listedFx.Contains($name)
    if (-not $present) {
        $missing.Add($name)
    }

    $rows.Add([pscustomobject]@{
        Index = [int]$t.Index
        Name = [string]$t.Name
        PreferredZone = [string]$t.PreferredZone
        ListedAfterStandaloneReload = $present
    })
}

$pass =
    $linkCode -eq 0 -and
    $listCode -eq 0 -and
    -not [string]::IsNullOrWhiteSpace($linkedFf) -and
    $missing.Count -eq 0 -and
    $rows.Count -eq 2

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4Z.StockFxRoundTrip.v1'
    sourcePlan = (Resolve-Path -LiteralPath $RemainingPlanManifest).Path
    stockWeaponDonorManifest = (Resolve-Path -LiteralPath $StockWeaponRoundTripManifest).Path
    donorFastFile = $donorFf
    targetFx = $targets.Count
    linkerExitCode = $linkCode
    readbackExitCode = $listCode
    linkedFastFile = $linkedFf
    resolvedFx = $rows.Count - $missing.Count
    missingFx = [object[]]$missing.ToArray()
    fx = [object[]]$rows.ToArray()
    roundTripPass = $pass
    productionCreditApproved = $false
    policy = @(
        'The donor is the previously proven self-contained stock Weapon round-trip FF, not mak.ff.',
        'Both target FX are requested as real FxEffectDef roots in one OAT build.',
        'OAT global asset pools may source the FX from the donor, then serialize them into the new standalone Xenon FF.',
        'The standalone FX FF is reloaded without the donor and must expose both roots using only common/base-Zombies pools.',
        'This is a batch proof. Stage 4F production credit remains separate and fail-closed.'
    )
}

$jsonPath = Join-Path $OutputRoot 'nuclear_4z_stock_fx_roundtrip.json'
$txtPath = Join-Path $OutputRoot 'nuclear_4z_stock_fx_roundtrip.txt'
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4Z - STOCK XBOX FX BATCH ROUNDTRIP')
$report.Add(('=' * 100))
$report.Add(("Targets:                    {0:N0}" -f $targets.Count))
$report.Add(("Linker exit:                {0}" -f $linkCode))
$report.Add(("Readback exit:              {0}" -f $listCode))
$report.Add(("Resolved FX:                {0:N0}/{1:N0}" -f ($rows.Count - $missing.Count), $targets.Count))
$report.Add(("ROUNDTRIP PASS:             {0}" -f $pass))
$report.Add('')
foreach ($row in $rows.ToArray()) {
    $report.Add(("  root #{0,4}: {1} present={2}" -f
        $row.Index, $row.Name, $row.ListedAfterStandaloneReload))
}
[IO.File]::WriteAllLines($txtPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4Z-FX] ====================== RESULT ================================'
Write-Host "[NUCLEAR-4Z-FX] Targets          : $($targets.Count)"
Write-Host "[NUCLEAR-4Z-FX] Linker exit      : $linkCode"
Write-Host "[NUCLEAR-4Z-FX] Readback exit    : $listCode"
Write-Host "[NUCLEAR-4Z-FX] Resolved FX      : $($rows.Count - $missing.Count)/$($targets.Count)"
Write-Host "[NUCLEAR-4Z-FX] Roundtrip pass   : $pass"
Write-Host "[NUCLEAR-4Z-FX] Standalone FF    : $linkedFf"
Write-Host "[NUCLEAR-4Z-FX] Manifest         : $jsonPath"
Write-Host "[NUCLEAR-4Z-FX] Report           : $txtPath"
Write-Host '[NUCLEAR-4Z-FX] ================================================================='

if (-not $pass) {
    exit 4
}
