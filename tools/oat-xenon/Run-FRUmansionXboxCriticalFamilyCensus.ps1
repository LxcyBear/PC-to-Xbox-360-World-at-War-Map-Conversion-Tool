param(
    [string]$XboxGameRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\XBOX GAME FILES',
    [string]$NativeIdentityManifest = '',
    [string]$RetailListLogRoot = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($NativeIdentityManifest)) {
    $identityRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_root_binding\native_identity'
    $identity = @(Get-ChildItem -LiteralPath $identityRoot -Filter '*_native_identity_manifest.json' -File -ErrorAction SilentlyContinue | Sort-Object FullName)
    if ($identity.Count -ne 1) {
        throw "Expected exactly one native identity manifest under '$identityRoot'; found $($identity.Count)."
    }
    $NativeIdentityManifest = $identity[0].FullName
}
if ([string]::IsNullOrWhiteSpace($RetailListLogRoot)) {
    $RetailListLogRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_xbox_retail_image_census\zone_logs'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4v_xbox_critical_family_census'
}

if (-not (Test-Path -LiteralPath $XboxGameRoot -PathType Container)) {
    throw "Xbox retail game-file folder not found: $XboxGameRoot"
}
if (-not (Test-Path -LiteralPath $NativeIdentityManifest -PathType Leaf)) {
    throw "Native identity manifest not found: $NativeIdentityManifest"
}
if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) {
    throw "Pinned Xenon OAT Unlinker not found: $unlinker"
}

function Normalize-Identity {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    $v = $Value.Trim().Replace('\', '/')
    while ($v.StartsWith(',', [StringComparison]::Ordinal)) {
        $v = $v.Substring(1)
    }
    return $v.ToLowerInvariant()
}

function Normalize-Type {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    $v = $Value.Trim().ToLowerInvariant()
    if ($v -eq 'clipmap') { return 'clipmap_pvs' }
    return $v
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

function Get-ZoneClass {
    param([string]$FileName)
    $n = $FileName.ToLowerInvariant()

    if ($n -in @('common.ff','code_post_gfx.ff','ui.ff','patch.ff')) { return 'core-global-candidate' }
    if ($n -eq 'nazi_zombie_prototype.ff' -or $n -eq 'nazi_zombie_prototype_load.ff') { return 'zombie-base-candidate' }
    if ($n.StartsWith('mp_') -or $n -in @('common_mp.ff','code_post_gfx_mp.ff','ui_mp.ff')) { return 'multiplayer-zone-only' }
    if ($n -match '^(ber|mak|oki|pel|see|sniper|pby_fly|credits|intro|outro)') { return 'campaign-zone-only' }
    if ($n -match '(?i)zombie|dlc') { return 'zombie-dlc-candidate' }
    return 'other-retail-zone'
}

function Get-ClassRank {
    param([string]$Class)
    switch ($Class) {
        'core-global-candidate' { return 0 }
        'zombie-base-candidate' { return 1 }
        'zombie-dlc-candidate' { return 2 }
        'campaign-zone-only' { return 3 }
        'other-retail-zone' { return 4 }
        'multiplayer-zone-only' { return 5 }
        default { return 9 }
    }
}

$targetTypes = @(
    'fx',
    'weapon',
    'rawfile',
    'gfxworld',
    'clipmap_pvs',
    'comworld',
    'gameworld_sp'
)

$native = Get-Content -LiteralPath $NativeIdentityManifest -Raw | ConvertFrom-Json
if ([string]$native.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1') {
    throw "Unexpected native identity manifest format: $($native.format)"
}
if (-not [bool]$native.typeSequenceVerified) {
    throw 'Native identity manifest is not type-sequence verified.'
}

$targets = New-Object 'System.Collections.Generic.List[object]'
foreach ($entry in @($native.entries)) {
    $oatType = Normalize-Type -Value ([string]$entry.OatTypeName)
    $nativeType = Normalize-Type -Value ([string]$entry.TypeName)
    $type = if ($oatType -in $targetTypes) { $oatType } else { $nativeType }
    if ($type -notin $targetTypes) { continue }

    $targets.Add([pscustomobject]@{
        Index = [int]$entry.Index
        Type = $type
        Name = [string]$entry.Name
        Normalized = Normalize-Identity -Value ([string]$entry.Name)
    })
}

$targetByKey = @{}
foreach ($target in $targets.ToArray()) {
    $key = "$($target.Type)|$($target.Normalized)"
    if ([string]::IsNullOrWhiteSpace([string]$target.Normalized)) { continue }
    if (-not $targetByKey.ContainsKey($key)) {
        $targetByKey[$key] = New-Object 'System.Collections.Generic.List[object]'
    }
    $targetByKey[$key].Add($target)
}

$fastFiles = @(
    Get-ChildItem -LiteralPath $XboxGameRoot -Filter '*.ff' -File -Recurse -ErrorAction Stop |
    Sort-Object FullName
)
if ($fastFiles.Count -eq 0) {
    throw "No Xbox retail .ff files were found under: $XboxGameRoot"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$logRoot = Join-Path $OutputRoot 'zone_logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

$stockIndex = @{}
$zoneResults = New-Object 'System.Collections.Generic.List[object]'
$scanFailures = 0
$reusedLogs = 0
$freshScans = 0

Write-Host '[NUCLEAR-4V-XBOX] ==========================================================='
Write-Host '[NUCLEAR-4V-XBOX] RETAIL XBOX CRITICAL-FAMILY IDENTITY CENSUS'
Write-Host '[NUCLEAR-4V-XBOX] ==========================================================='
Write-Host "[NUCLEAR-4V-XBOX] Xbox FF root       : $XboxGameRoot"
Write-Host "[NUCLEAR-4V-XBOX] Native identities  : $NativeIdentityManifest"
Write-Host "[NUCLEAR-4V-XBOX] Target roots       : $($targets.Count)"
Write-Host '[NUCLEAR-4V-XBOX] Families           : FX + Weapon + RawFile + 4 world roots'
Write-Host '[NUCLEAR-4V-XBOX] POLICY: identity/reference evidence only; no retail payload bytes are copied.'
Write-Host ''

$ordinal = 0
foreach ($ff in $fastFiles) {
    $ordinal++
    $zoneName = $ff.Name
    $zoneClass = Get-ZoneClass -FileName $zoneName
    $safeStem = ($zoneName -replace '[^A-Za-z0-9_.-]', '_')
    $cachedLog = Join-Path $RetailListLogRoot ($safeStem + '.list.log')
    $localLog = Join-Path $logRoot ($safeStem + '.list.log')

    $lines = @()
    $exitCode = 0

    if (Test-Path -LiteralPath $cachedLog -PathType Leaf) {
        $lines = @(Get-Content -LiteralPath $cachedLog)
        $reusedLogs++
    }
    else {
        Write-Host ("[NUCLEAR-4V-XBOX] [{0,3}/{1,3}] scanning {2}" -f $ordinal, $fastFiles.Count, $zoneName)
        $previous = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $lines = @(& $unlinker '--list' '--no-color' $ff.FullName 2>&1 | ForEach-Object { [string]$_ })
            $exitCode = [int]$LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previous
        }
        $freshScans++
        [IO.File]::WriteAllLines($localLog, [string[]]$lines, [Text.UTF8Encoding]::new($false))
    }

    if ($exitCode -ne 0) { $scanFailures++ }

    $zoneHits = 0
    foreach ($raw in $lines) {
        $line = Strip-LogPrefix -Line $raw
        $comma = $line.IndexOf(',')
        if ($comma -le 0 -or $comma + 1 -ge $line.Length) { continue }

        $type = Normalize-Type -Value $line.Substring(0, $comma)
        if ($type -notin $targetTypes) { continue }

        $listedName = $line.Substring($comma + 1).Trim()
        $normalized = Normalize-Identity -Value $listedName
        if ([string]::IsNullOrWhiteSpace($normalized)) { continue }

        $key = "$type|$normalized"
        if (-not $targetByKey.ContainsKey($key)) { continue }

        if (-not $stockIndex.ContainsKey($key)) {
            $stockIndex[$key] = New-Object 'System.Collections.Generic.List[object]'
        }

        $stockIndex[$key].Add([pscustomobject]@{
            Zone = $zoneName
            ZonePath = $ff.FullName
            ZoneClass = $zoneClass
            ListedName = $listedName
        })
        $zoneHits++
    }

    $zoneResults.Add([pscustomobject]@{
        Zone = $zoneName
        ZonePath = $ff.FullName
        ZoneClass = $zoneClass
        OatExitCode = $exitCode
        TargetHits = $zoneHits
        Source = $(if (Test-Path -LiteralPath $cachedLog -PathType Leaf) { 'reused-image-census-log' } else { 'fresh-scan' })
    })
}

$matches = New-Object 'System.Collections.Generic.List[object]'
$notFound = New-Object 'System.Collections.Generic.List[object]'

foreach ($target in @($targets.ToArray() | Sort-Object Index)) {
    $key = "$($target.Type)|$($target.Normalized)"
    if (-not $stockIndex.ContainsKey($key)) {
        $notFound.Add([pscustomobject]@{
            Index = $target.Index
            Type = $target.Type
            Name = $target.Name
            Normalized = $target.Normalized
        })
        continue
    }

    $zones = @(
        $stockIndex[$key] |
        Sort-Object @{ Expression = { Get-ClassRank -Class ([string]$_.ZoneClass) } }, Zone |
        Select-Object -Unique Zone, ZonePath, ZoneClass, ListedName
    )

    $matches.Add([pscustomobject]@{
        Index = $target.Index
        Type = $target.Type
        Name = $target.Name
        Normalized = $target.Normalized
        BestClass = [string]$zones[0].ZoneClass
        Zones = $zones
    })
}

$typeSummary = New-Object 'System.Collections.Generic.List[object]'
foreach ($type in $targetTypes) {
    $typeTargets = @($targets.ToArray() | Where-Object { $_.Type -eq $type })
    if ($typeTargets.Count -eq 0) { continue }

    $found = @($matches.ToArray() | Where-Object { $_.Type -eq $type })
    $missing = @($notFound.ToArray() | Where-Object { $_.Type -eq $type })

    $typeSummary.Add([pscustomobject]@{
        Type = $type
        NativeRoots = $typeTargets.Count
        FoundInRetailXbox = $found.Count
        NotFoundInRetailXbox = $missing.Count
        Coverage = if ($typeTargets.Count -eq 0) { 0.0 } else { $found.Count / [double]$typeTargets.Count }
    })
}

$coreCandidates = @($matches.ToArray() | Where-Object { $_.BestClass -eq 'core-global-candidate' })
$zombieCandidates = @($matches.ToArray() | Where-Object { $_.BestClass -in @('zombie-base-candidate','zombie-dlc-candidate') })
$otherCandidates = @($matches.ToArray() | Where-Object { $_.BestClass -notin @('core-global-candidate','zombie-base-candidate','zombie-dlc-candidate') })

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4V.XboxCriticalFamilyCensus.v1'
    xboxGameRoot = (Resolve-Path -LiteralPath $XboxGameRoot).Path
    nativeIdentityManifest = (Resolve-Path -LiteralPath $NativeIdentityManifest).Path
    fastFilesScanned = $fastFiles.Count
    reusedListLogs = $reusedLogs
    freshScans = $freshScans
    scanFailures = $scanFailures
    targetRoots = $targets.Count
    foundInRetailXbox = $matches.Count
    notFoundInRetailXbox = $notFound.Count
    coreGlobalCandidates = $coreCandidates.Count
    zombieCandidates = $zombieCandidates.Count
    otherRetailCandidates = $otherCandidates.Count
    typeSummary = [object[]]$typeSummary.ToArray()
    zones = [object[]]$zoneResults.ToArray()
    matches = [object[]]$matches.ToArray()
    notFound = [object[]]$notFound.ToArray()
    policy = @(
        'This stage classifies critical non-image roots by exact type + identity against user-owned retail Xbox 360 WaW fastfiles.',
        'No retail payload bytes are copied by this census.',
        'A retail match is provenance evidence, not yet production payload credit.',
        'Stock matches should preferentially use Xbox-native extraction; only non-retail/custom roots should require PC-to-Xenon conversion.'
    )
}

$jsonPath = Join-Path $OutputRoot 'nuclear_4v_xbox_critical_family_census.json'
$txtPath = Join-Path $OutputRoot 'nuclear_4v_xbox_critical_family_census.txt'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4V-XBOX - RETAIL XBOX CRITICAL-FAMILY IDENTITY CENSUS')
$report.Add(('=' * 100))
$report.Add(("Retail FFs:                 {0:N0}" -f $fastFiles.Count))
$report.Add(("Reused --list logs:         {0:N0}" -f $reusedLogs))
$report.Add(("Fresh OAT scans:            {0:N0}" -f $freshScans))
$report.Add(("OAT scan failures:          {0:N0}" -f $scanFailures))
$report.Add(("Critical target roots:      {0:N0}" -f $targets.Count))
$report.Add(("Found in retail Xbox:       {0:N0}" -f $matches.Count))
$report.Add(("Not found in retail Xbox:   {0:N0}" -f $notFound.Count))
$report.Add('')
$report.Add('BY TYPE')
foreach ($row in $typeSummary.ToArray()) {
    $report.Add(("  {0,-14} native={1,4:N0} retail={2,4:N0} custom/unmatched={3,4:N0} coverage={4,7:P2}" -f
        $row.Type, $row.NativeRoots, $row.FoundInRetailXbox, $row.NotFoundInRetailXbox, $row.Coverage))
}
$report.Add('')
$report.Add('NOT FOUND IN SUPPLIED RETAIL XBOX FASTFILES')
foreach ($row in $notFound.ToArray()) {
    $report.Add(("  root #{0,4}: {1,-14} {2}" -f $row.Index, $row.Type, $row.Name))
}

[IO.File]::WriteAllLines($txtPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4V-XBOX] ====================== RESULT =============================='
Write-Host "[NUCLEAR-4V-XBOX] Retail FFs             : $($fastFiles.Count)"
Write-Host "[NUCLEAR-4V-XBOX] Reused --list logs     : $reusedLogs"
Write-Host "[NUCLEAR-4V-XBOX] Fresh OAT scans        : $freshScans"
Write-Host "[NUCLEAR-4V-XBOX] OAT scan failures      : $scanFailures"
Write-Host "[NUCLEAR-4V-XBOX] Critical target roots  : $($targets.Count)"
Write-Host "[NUCLEAR-4V-XBOX] Found in retail Xbox   : $($matches.Count)"
Write-Host "[NUCLEAR-4V-XBOX] Not found in retail    : $($notFound.Count)"
foreach ($row in $typeSummary.ToArray()) {
    Write-Host ("[NUCLEAR-4V-XBOX]   {0,-14} {1,4}/{2,4} retail ({3:P2})" -f
        $row.Type, $row.FoundInRetailXbox, $row.NativeRoots, $row.Coverage)
}
Write-Host "[NUCLEAR-4V-XBOX] Manifest                : $jsonPath"
Write-Host "[NUCLEAR-4V-XBOX] Report                  : $txtPath"
Write-Host '[NUCLEAR-4V-XBOX] ================================================================='

if ($scanFailures -ne 0) {
    Write-Host '[NUCLEAR-4V-XBOX] CENSUS: COMPLETED WITH OAT SCAN FAILURES' -ForegroundColor Yellow
    exit 4
}

Write-Host '[NUCLEAR-4V-XBOX] PASS: Xbox critical-family identity census complete.' -ForegroundColor Green
