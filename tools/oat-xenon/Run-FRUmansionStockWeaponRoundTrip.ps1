param(
    [string]$RemainingPlanManifest = '',
    [string]$XboxGameRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\XBOX GAME FILES',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
$linker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Linker.exe'

if ([string]::IsNullOrWhiteSpace($RemainingPlanManifest)) {
    $RemainingPlanManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4x_remaining_critical_plan\nuclear_4x_remaining_critical_plan.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4y_stock_weapon_roundtrip'
}

foreach ($required in @($RemainingPlanManifest, $unlinker, $linker)) {
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
    return $v.Replace('\','/')
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

function Find-RetailZone {
    param(
        [Parameter(Mandatory=$true)][string]$PreferredPath,
        [Parameter(Mandatory=$true)][string]$Root
    )

    if (-not [string]::IsNullOrWhiteSpace($PreferredPath) -and
        (Test-Path -LiteralPath $PreferredPath -PathType Leaf)) {
        return (Resolve-Path -LiteralPath $PreferredPath).Path
    }

    $name = [IO.Path]::GetFileName($PreferredPath)
    if ([string]::IsNullOrWhiteSpace($name)) {
        throw "Cannot infer retail zone filename from '$PreferredPath'."
    }

    $matches = @(Get-ChildItem -LiteralPath $Root -Filter $name -File -Recurse -ErrorAction SilentlyContinue)
    if ($matches.Count -ne 1) {
        throw "Expected one retail zone '$name' under '$Root'; found $($matches.Count)."
    }

    return $matches[0].FullName
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

function Find-DumpedWeaponFile {
    param(
        [Parameter(Mandatory=$true)][string]$DumpRoot,
        [Parameter(Mandatory=$true)][string]$AssetName
    )

    $suffix = ('/weapons/' + (Normalize-AssetName -Value $AssetName)).ToLowerInvariant()
    $hits = @(
        Get-ChildItem -LiteralPath $DumpRoot -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName.Replace('\','/').ToLowerInvariant().EndsWith($suffix, [StringComparison]::Ordinal)
        }
    )

    if ($hits.Count -eq 1) { return $hits[0].FullName }
    if ($hits.Count -eq 0) { return $null }

    throw "Multiple dumped weapon files matched '$AssetName' under '$DumpRoot'."
}

function Copy-DumpSupportFiles {
    param(
        [Parameter(Mandatory=$true)][string]$DumpRoot,
        [Parameter(Mandatory=$true)][string]$RawRoot
    )

    $copied = 0
    foreach ($file in @(Get-ChildItem -LiteralPath $DumpRoot -File -Recurse -ErrorAction SilentlyContinue)) {
        $normalized = $file.FullName.Replace('\','/')
        $lower = $normalized.ToLowerInvariant()

        $marker = $lower.LastIndexOf('/weapons/')
        if ($marker -lt 0) {
            $marker = $lower.LastIndexOf('/accuracy/')
        }
        if ($marker -lt 0) { continue }

        $relative = $normalized.Substring($marker + 1).Replace('/','\')
        $dest = Join-Path $RawRoot $relative
        $parent = Split-Path -Parent $dest
        if (-not [string]::IsNullOrWhiteSpace($parent)) {
            New-Item -ItemType Directory -Force -Path $parent | Out-Null
        }
        Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
        $copied++
    }

    return $copied
}

$plan = Get-Content -LiteralPath $RemainingPlanManifest -Raw | ConvertFrom-Json
if ([string]$plan.format -ne 'WaW360.Nuclear4X.RemainingCriticalPlan.v1' -or
    -not [bool]$plan.reconciles -or
    [int]$plan.criticalFallbackRoots -ne 83) {
    throw 'Stage 4Y requires the fresh reconciled 83-root remaining-critical plan.'
}

$targets = @(
    $plan.retailPayloadTargets |
    Where-Object { [string]$_.Type -eq 'weapon' } |
    Sort-Object Index
)

if ($targets.Count -ne 3) {
    throw "Expected exactly 3 retail Weapon payload targets; found $($targets.Count)."
}

$baseZones = Get-BaseZones -Root $XboxGameRoot

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

$dumpRoot = Join-Path $OutputRoot 'retail_dumps'
$work = Join-Path $OutputRoot 'workspace'
$rawRoot = Join-Path $work 'raw'
$zoneSource = Join-Path $work 'zone_source'
$linkOut = Join-Path $OutputRoot 'linked'
$readbackRoot = Join-Path $OutputRoot 'readback'
$logs = Join-Path $OutputRoot 'logs'
foreach ($path in @($dumpRoot,$rawRoot,$zoneSource,$linkOut,$readbackRoot,$logs)) {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

Write-Host '[NUCLEAR-4Y-WEAPON] ========================================================='
Write-Host '[NUCLEAR-4Y-WEAPON] STOCK XBOX WEAPON BATCH ROUNDTRIP'
Write-Host '[NUCLEAR-4Y-WEAPON] ========================================================='
Write-Host "[NUCLEAR-4Y-WEAPON] Targets : $($targets.Count)"
foreach ($t in $targets) {
    Write-Host ("[NUCLEAR-4Y-WEAPON]   root #{0,4}: {1} <- {2}" -f
        [int]$t.Index, [string]$t.Name, [string]$t.PreferredZone)
}
Write-Host ''

$sourceZones = @(
    $targets |
    ForEach-Object { Find-RetailZone -PreferredPath ([string]$_.PreferredZone) -Root $XboxGameRoot } |
    Select-Object -Unique
)

$dumpRows = New-Object 'System.Collections.Generic.List[object]'
$dumpFailures = New-Object 'System.Collections.Generic.List[string]'
$sourceFiles = @{}

$zoneOrdinal = 0
foreach ($zone in $sourceZones) {
    $zoneOrdinal++
    $zoneName = [IO.Path]::GetFileNameWithoutExtension($zone)
    $safe = ($zoneName -replace '[^A-Za-z0-9_.-]','_')
    $zoneDump = Join-Path $dumpRoot ("{0:D2}_{1}" -f $zoneOrdinal, $safe)
    $log = Join-Path $logs ("dump_{0:D2}_{1}.log" -f $zoneOrdinal, $safe)
    New-Item -ItemType Directory -Force -Path $zoneDump | Out-Null

    $args = New-Object 'System.Collections.Generic.List[object]'
    foreach ($arg in @(
        '--verbose',
        '--no-color',
        '--include-assets','weapon',
        '--output-folder',$zoneDump
    )) { $args.Add($arg) }

    foreach ($base in $baseZones) {
        if (-not $base.Equals($zone, [StringComparison]::OrdinalIgnoreCase)) {
            $args.Add('--load')
            $args.Add($base)
        }
    }
    $args.Add($zone)

    Write-Host "[NUCLEAR-4Y-WEAPON] Dumping WeaponDef set from: $zone"
    Push-Location (Split-Path -Parent $zone)
    try {
        $code = Invoke-NativeTee -FilePath $unlinker -Arguments ([object[]]$args.ToArray()) -LogPath $log
    }
    finally {
        Pop-Location
    }

    $staged = Copy-DumpSupportFiles -DumpRoot $zoneDump -RawRoot $rawRoot

    $dumpRows.Add([pscustomobject]@{
        Zone = $zone
        ExitCode = $code
        StagedSupportFiles = $staged
        DumpRoot = $zoneDump
        Log = $log
    })

    if ($code -ne 0) {
        $dumpFailures.Add("Retail weapon dump failed for '$zone' with exit $code.")
    }

    foreach ($t in @($targets | Where-Object {
        $p = Find-RetailZone -PreferredPath ([string]$_.PreferredZone) -Root $XboxGameRoot
        $p.Equals($zone, [StringComparison]::OrdinalIgnoreCase)
    })) {
        $file = Find-DumpedWeaponFile -DumpRoot $zoneDump -AssetName ([string]$t.Name)
        if ($null -eq $file) {
            $dumpFailures.Add("Weapon '$([string]$t.Name)' was not dumped from '$zone'.")
        }
        else {
            $sourceFiles[[int]$t.Index] = $file
        }
    }
}

foreach ($t in $targets) {
    if (-not $sourceFiles.ContainsKey([int]$t.Index)) {
        continue
    }

    $name = Normalize-AssetName -Value ([string]$t.Name)
    $destination = Join-Path $rawRoot ('weapons\' + $name.Replace('/','\'))
    $parent = Split-Path -Parent $destination
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    Copy-Item -LiteralPath $sourceFiles[[int]$t.Index] -Destination $destination -Force
}

$project = 'nuclear_4y_stock_weapon_roundtrip'
$zoneFile = Join-Path $zoneSource ($project + '.zone')
$zoneLines = New-Object 'System.Collections.Generic.List[string]'
$zoneLines.Add('>game,T4')
$zoneLines.Add('>platform,xbox360')
$zoneLines.Add(('>name,{0}' -f $project))
$zoneLines.Add('')
foreach ($t in $targets) {
    $name = Normalize-AssetName -Value ([string]$t.Name
    )
    $zoneLines.Add(('weapon,"{0}"' -f $name.Replace('"','""')))
}
[IO.File]::WriteAllLines($zoneFile,[string[]]$zoneLines.ToArray(),[Text.UTF8Encoding]::new($false))

$linkCode = -1
$linkLog = Join-Path $logs 'linker.log'
$linkedFf = ''

if ($dumpFailures.Count -eq 0) {
    $args = New-Object 'System.Collections.Generic.List[object]'
    foreach ($arg in @(
        '--verbose',
        '--no-color',
        '--base-folder',$work,
        '--source-search-path',$zoneSource,
        '--asset-search-path',$rawRoot,
        '--output-folder',$linkOut
    )) { $args.Add($arg) }

    foreach ($base in $baseZones) {
        $args.Add('--load')
        $args.Add($base)
    }
    $args.Add($project)

    Write-Host ''
    Write-Host '[NUCLEAR-4Y-WEAPON] Relinking all three dumped WeaponDefs together...'
    Push-Location $work
    try {
        $linkCode = Invoke-NativeTee -FilePath $linker -Arguments ([object[]]$args.ToArray()) -LogPath $linkLog
    }
    finally {
        Pop-Location
    }

    if ($linkCode -eq 0) {
        $linked = @(Get-ChildItem -LiteralPath $linkOut -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue)
        if ($linked.Count -eq 1) {
            $linkedFf = $linked[0].FullName
        }
        else {
            $dumpFailures.Add("Expected one linked FF; found $($linked.Count).")
        }
    }
}

$readbackCode = -1
$readbackLog = Join-Path $logs 'readback.log'
if ($linkCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($linkedFf)) {
    $args = New-Object 'System.Collections.Generic.List[object]'
    foreach ($arg in @(
        '--verbose',
        '--no-color',
        '--include-assets','weapon',
        '--output-folder',$readbackRoot
    )) { $args.Add($arg) }
    foreach ($base in $baseZones) {
        $args.Add('--load')
        $args.Add($base)
    }
    $args.Add($linkedFf)

    Write-Host ''
    Write-Host '[NUCLEAR-4Y-WEAPON] Dumping linked FF back to disk for WeaponDef readback...'
    Push-Location (Split-Path -Parent $linkedFf)
    try {
        $readbackCode = Invoke-NativeTee -FilePath $unlinker -Arguments ([object[]]$args.ToArray()) -LogPath $readbackLog
    }
    finally {
        Pop-Location
    }
}

$weaponRows = New-Object 'System.Collections.Generic.List[object]'
$missingReadback = New-Object 'System.Collections.Generic.List[string]'

foreach ($t in $targets) {
    $source = if ($sourceFiles.ContainsKey([int]$t.Index)) { [string]$sourceFiles[[int]$t.Index] } else { '' }
    $readback = Find-DumpedWeaponFile -DumpRoot $readbackRoot -AssetName ([string]$t.Name)

    $sourceHash = ''
    $readbackHash = ''
    $exact = $false

    if (-not [string]::IsNullOrWhiteSpace($source) -and (Test-Path -LiteralPath $source -PathType Leaf)) {
        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    }
    if ($null -ne $readback -and (Test-Path -LiteralPath $readback -PathType Leaf)) {
        $readbackHash = (Get-FileHash -LiteralPath $readback -Algorithm SHA256).Hash
    }
    else {
        $missingReadback.Add([string]$t.Name)
    }

    if (-not [string]::IsNullOrWhiteSpace($sourceHash) -and
        -not [string]::IsNullOrWhiteSpace($readbackHash)) {
        $exact = $sourceHash.Equals($readbackHash,[StringComparison]::OrdinalIgnoreCase)
    }

    $weaponRows.Add([pscustomobject]@{
        Index = [int]$t.Index
        Name = [string]$t.Name
        PreferredZone = [string]$t.PreferredZone
        SourceDump = $source
        ReadbackDump = if ($null -eq $readback) { '' } else { $readback }
        SourceSha256 = $sourceHash
        ReadbackSha256 = $readbackHash
        ExactInfoStringRoundTrip = $exact
    })
}

$exactCount = @($weaponRows.ToArray() | Where-Object ExactInfoStringRoundTrip).Count

$pass =
    $dumpFailures.Count -eq 0 -and
    $linkCode -eq 0 -and
    $readbackCode -eq 0 -and
    $missingReadback.Count -eq 0 -and
    $weaponRows.Count -eq 3

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4Y.StockWeaponRoundTrip.v1'
    sourcePlan = (Resolve-Path -LiteralPath $RemainingPlanManifest).Path
    xboxGameRoot = (Resolve-Path -LiteralPath $XboxGameRoot).Path
    targetWeapons = $targets.Count
    sourceZones = [object[]]$sourceZones
    baseZones = [object[]]$baseZones
    retailDumpResults = [object[]]$dumpRows.ToArray()
    dumpFailures = [object[]]$dumpFailures.ToArray()
    linkerExitCode = $linkCode
    readbackExitCode = $readbackCode
    linkedFastFile = $linkedFf
    readbackWeapons = $weaponRows.Count - $missingReadback.Count
    missingReadback = [object[]]$missingReadback.ToArray()
    exactInfoStringRoundTrips = $exactCount
    weapons = [object[]]$weaponRows.ToArray()
    roundTripPass = $pass
    productionCreditApproved = $false
    policy = @(
        'Retail WeaponDefs are dumped from the user-owned Xbox 360 game using OAT T4 WeaponDef dumping support.',
        'The batch is relinked from disk weapon info strings; preferred nonresident source zones are not preloaded into the Linker.',
        'Only common/base-Zombies retail zones are preloaded during relink/readback for dependencies already proven runtime-resident.',
        'A passing batch proves OAT dump/load/readback viability for these three WeaponDefs but does not grant Stage 4F production credit until integrated emission consumes the resulting payloads.'
    )
}

$jsonPath = Join-Path $OutputRoot 'nuclear_4y_stock_weapon_roundtrip.json'
$txtPath = Join-Path $OutputRoot 'nuclear_4y_stock_weapon_roundtrip.txt'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4Y - STOCK XBOX WEAPON BATCH ROUNDTRIP')
$report.Add(('=' * 100))
$report.Add(("Targets:                    {0:N0}" -f $targets.Count))
$report.Add(("Retail dump failures:       {0:N0}" -f $dumpFailures.Count))
$report.Add(("Linker exit:                {0}" -f $linkCode))
$report.Add(("Readback exit:              {0}" -f $readbackCode))
$report.Add(("Readback weapons:           {0:N0}/{1:N0}" -f ($weaponRows.Count - $missingReadback.Count), $targets.Count))
$report.Add(("Exact info-string hashes:   {0:N0}/{1:N0}" -f $exactCount, $targets.Count))
$report.Add(("ROUNDTRIP PASS:             {0}" -f $pass))
$report.Add('')
foreach ($row in $weaponRows.ToArray()) {
    $report.Add(("  root #{0,4}: {1} readback={2} exactHash={3}" -f
        $row.Index, $row.Name, (-not [string]::IsNullOrWhiteSpace([string]$row.ReadbackDump)), $row.ExactInfoStringRoundTrip))
}
if ($dumpFailures.Count -gt 0) {
    $report.Add('')
    $report.Add('FAILURES')
    foreach ($failure in $dumpFailures.ToArray()) { $report.Add('  ' + $failure) }
}
[IO.File]::WriteAllLines($txtPath,[string[]]$report.ToArray(),[Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4Y-WEAPON] ====================== RESULT ============================'
Write-Host "[NUCLEAR-4Y-WEAPON] Targets                  : $($targets.Count)"
Write-Host "[NUCLEAR-4Y-WEAPON] Retail dump failures     : $($dumpFailures.Count)"
Write-Host "[NUCLEAR-4Y-WEAPON] Linker exit              : $linkCode"
Write-Host "[NUCLEAR-4Y-WEAPON] Readback exit            : $readbackCode"
Write-Host "[NUCLEAR-4Y-WEAPON] Readback weapons         : $($weaponRows.Count - $missingReadback.Count)/$($targets.Count)"
Write-Host "[NUCLEAR-4Y-WEAPON] Exact info-string hashes : $exactCount/$($targets.Count)"
Write-Host "[NUCLEAR-4Y-WEAPON] Roundtrip pass           : $pass"
Write-Host "[NUCLEAR-4Y-WEAPON] Manifest                 : $jsonPath"
Write-Host "[NUCLEAR-4Y-WEAPON] Report                   : $txtPath"
Write-Host '[NUCLEAR-4Y-WEAPON] ================================================================='

if (-not $pass) {
    exit 4
}
