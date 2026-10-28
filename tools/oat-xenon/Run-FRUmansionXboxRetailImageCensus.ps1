param(
    [string]$XboxGameRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\XBOX GAME FILES',
    [string]$Stage4RManifest = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($Stage4RManifest)) {
    $Stage4RManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_pc_image_bridge\nuclear_4r_pc_image_bridge.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_xbox_retail_image_census'
}

if (-not (Test-Path -LiteralPath $XboxGameRoot -PathType Container)) {
    throw "Xbox retail game-file folder not found: $XboxGameRoot"
}
if (-not (Test-Path -LiteralPath $Stage4RManifest -PathType Leaf)) {
    throw "Stage 4R manifest not found: $Stage4RManifest"
}
if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) {
    throw "Pinned Xenon OAT Unlinker not found: $unlinker"
}

function Normalize-ImageIdentity {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    $v = $Value.Trim().Replace('\', '/')
    while ($v.StartsWith(',', [StringComparison]::Ordinal)) {
        $v = $v.Substring(1)
    }
    if ($v.EndsWith('_wj', [StringComparison]::OrdinalIgnoreCase)) {
        $v = $v.Substring(0, $v.Length - 3)
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

function Get-ZoneClass {
    param([string]$FileName)
    $n = $FileName.ToLowerInvariant()

    if ($n -in @('common.ff','code_post_gfx.ff','ui.ff','patch.ff')) {
        return 'core-global-candidate'
    }
    if ($n -eq 'nazi_zombie_prototype.ff' -or $n -eq 'nazi_zombie_prototype_load.ff') {
        return 'zombie-base-candidate'
    }
    if ($n.StartsWith('mp_') -or $n -in @('common_mp.ff','code_post_gfx_mp.ff','ui_mp.ff')) {
        return 'multiplayer-zone-only'
    }
    if ($n -match '^(ber|mak|oki|pel|see|sniper|pby_fly|credits|intro|outro)') {
        return 'campaign-zone-only'
    }
    if ($n -match '(?i)zombie|dlc') {
        return 'zombie-dlc-candidate'
    }
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

$stage4r = Get-Content -LiteralPath $Stage4RManifest -Raw | ConvertFrom-Json
if ([string]$stage4r.format -ne 'WaW360.Nuclear4R.PcOwnedImageBridge.v1') {
    throw "Unexpected Stage 4R manifest format: $($stage4r.format)"
}

$targets = New-Object 'System.Collections.Generic.List[object]'
foreach ($item in @($stage4r.missing)) {
    $targets.Add([pscustomobject]@{
        Index = [int]$item.Index
        Name = [string]$item.Name
        Normalized = Normalize-ImageIdentity -Value ([string]$item.Name)
        SourceClass = 'ordinary-unresolved'
    })
}
foreach ($item in @($stage4r.nonPortable)) {
    $targets.Add([pscustomobject]@{
        Index = [int]$item.Index
        Name = [string]$item.Name
        Normalized = Normalize-ImageIdentity -Value ([string]$item.Name)
        SourceClass = 'generated-or-nonportable'
    })
}

$targetByKey = @{}
foreach ($target in $targets.ToArray()) {
    $key = [string]$target.Normalized
    if ([string]::IsNullOrWhiteSpace($key)) { continue }
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

$zoneResults = New-Object 'System.Collections.Generic.List[object]'
$stockIndex = @{}
$totalImageRows = 0
$scanFailures = 0

Write-Host '[NUCLEAR-4R-XBOX] ============================================================'
Write-Host '[NUCLEAR-4R-XBOX] RETAIL XBOX IMAGE IDENTITY CENSUS'
Write-Host '[NUCLEAR-4R-XBOX] ============================================================'
Write-Host "[NUCLEAR-4R-XBOX] Xbox FF root       : $XboxGameRoot"
Write-Host "[NUCLEAR-4R-XBOX] FF files           : $($fastFiles.Count)"
Write-Host "[NUCLEAR-4R-XBOX] 4R unresolved      : $($targets.Count)"
Write-Host '[NUCLEAR-4R-XBOX] POLICY: identity/reference evidence only; ZERO Xbox payload bytes are copied.'
Write-Host ''

$ordinal = 0
foreach ($ff in $fastFiles) {
    $ordinal++
    $zoneName = $ff.Name
    $zoneClass = Get-ZoneClass -FileName $zoneName
    Write-Host ("[NUCLEAR-4R-XBOX] [{0,3}/{1,3}] {2}" -f $ordinal, $fastFiles.Count, $zoneName)

    $previous = $ErrorActionPreference
    $lines = @()
    $exitCode = -1
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& $unlinker '--list' '--no-color' $ff.FullName 2>&1 | ForEach-Object { [string]$_ })
        $exitCode = [int]$LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }

    $safeStem = ($zoneName -replace '[^A-Za-z0-9_.-]', '_')
    $logPath = Join-Path $logRoot ($safeStem + '.list.log')
    [IO.File]::WriteAllLines($logPath, [string[]]$lines, [Text.UTF8Encoding]::new($false))

    $imageNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($raw in $lines) {
        $line = Strip-LogPrefix -Line $raw
        if (-not $line.StartsWith('image,', [StringComparison]::OrdinalIgnoreCase)) { continue }

        $name = $line.Substring(6).Trim()
        $key = Normalize-ImageIdentity -Value $name
        if ([string]::IsNullOrWhiteSpace($key)) { continue }

        [void]$imageNames.Add($key)
        if ($targetByKey.ContainsKey($key)) {
            if (-not $stockIndex.ContainsKey($key)) {
                $stockIndex[$key] = New-Object 'System.Collections.Generic.List[object]'
            }
            $stockIndex[$key].Add([pscustomobject]@{
                Zone = $zoneName
                ZonePath = $ff.FullName
                ZoneClass = $zoneClass
                ListedName = $name
            })
        }
    }

    $totalImageRows += $imageNames.Count
    if ($exitCode -ne 0) { $scanFailures++ }

    $zoneResults.Add([pscustomobject]@{
        Zone = $zoneName
        ZonePath = $ff.FullName
        ZoneClass = $zoneClass
        OatExitCode = $exitCode
        UniqueImageIdentities = $imageNames.Count
        Log = $logPath
    })
}

$matches = New-Object 'System.Collections.Generic.List[object]'
$notFound = New-Object 'System.Collections.Generic.List[object]'

foreach ($target in @($targets.ToArray() | Sort-Object Index)) {
    $key = [string]$target.Normalized
    if (-not $stockIndex.ContainsKey($key)) {
        $notFound.Add([pscustomobject]@{
            Index = $target.Index
            Name = $target.Name
            Normalized = $key
            SourceClass = $target.SourceClass
        })
        continue
    }

    $zones = @(
        $stockIndex[$key] |
        Sort-Object @{ Expression = { Get-ClassRank -Class ([string]$_.ZoneClass) } }, Zone |
        Select-Object -Unique Zone, ZonePath, ZoneClass, ListedName
    )
    $bestClass = [string]$zones[0].ZoneClass

    $matches.Add([pscustomobject]@{
        Index = $target.Index
        Name = $target.Name
        Normalized = $key
        SourceClass = $target.SourceClass
        BestClass = $bestClass
        Zones = $zones
    })
}

$classSummary = @(
    $matches.ToArray() |
    Group-Object BestClass |
    Sort-Object @{ Expression = { Get-ClassRank -Class $_.Name } }, Name |
    ForEach-Object {
        [pscustomobject]@{
            Classification = $_.Name
            Images = $_.Count
        }
    }
)

$coreCandidates = @($matches.ToArray() | Where-Object { $_.BestClass -eq 'core-global-candidate' })
$zombieCandidates = @($matches.ToArray() | Where-Object { $_.BestClass -in @('zombie-base-candidate','zombie-dlc-candidate') })
$mapSpecific = @($matches.ToArray() | Where-Object { $_.BestClass -in @('campaign-zone-only','multiplayer-zone-only','other-retail-zone') })

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4R.XboxRetailImageCensus.v1'
    xboxGameRoot = (Resolve-Path -LiteralPath $XboxGameRoot).Path
    stage4RManifest = (Resolve-Path -LiteralPath $Stage4RManifest).Path
    fastFilesScanned = $fastFiles.Count
    scanFailures = $scanFailures
    totalZoneImageIdentityRows = $totalImageRows
    unresolvedStage4RImages = $targets.Count
    foundInRetailXbox = $matches.Count
    notFoundInRetailXbox = $notFound.Count
    coreGlobalCandidates = $coreCandidates.Count
    zombieCandidates = $zombieCandidates.Count
    mapSpecificRetail = $mapSpecific.Count
    classSummary = $classSummary
    zones = [object[]]$zoneResults.ToArray()
    matches = [object[]]$matches.ToArray()
    notFound = [object[]]$notFound.ToArray()
    policy = @(
        'Retail Xbox fastfiles are read only for asset identity/reference evidence.',
        'No Xbox asset payload bytes are copied into production output.',
        'core-global-candidate means identity is present in common/code_post_gfx/ui/patch; runtime residency still requires proof.',
        'zombie-base/dlc and campaign/mp classifications are stock evidence only; they are NOT automatically safe runtime references.',
        'Images absent from all supplied Xbox retail FFs remain custom/unresolved and require map-owned source conversion or separate handling.'
    )
}

$jsonPath = Join-Path $OutputRoot 'nuclear_4r_xbox_retail_image_census.json'
$txtPath = Join-Path $OutputRoot 'nuclear_4r_xbox_retail_image_census.txt'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4R-XBOX - RETAIL XBOX IMAGE IDENTITY CENSUS')
$report.Add(('=' * 96))
$report.Add("Xbox game root:              $XboxGameRoot")
$report.Add(("Fastfiles scanned:           {0:N0}" -f $fastFiles.Count))
$report.Add(("OAT scan failures:           {0:N0}" -f $scanFailures))
$report.Add(("4R unresolved images:        {0:N0}" -f $targets.Count))
$report.Add(("Found in retail Xbox:        {0:N0}" -f $matches.Count))
$report.Add(("Not found in retail Xbox:    {0:N0}" -f $notFound.Count))
$report.Add(("Core/global candidates:      {0:N0}" -f $coreCandidates.Count))
$report.Add(("Zombie-zone candidates:      {0:N0}" -f $zombieCandidates.Count))
$report.Add(("Map/other retail only:       {0:N0}" -f $mapSpecific.Count))
$report.Add('')
$report.Add('CLASSIFICATION')
foreach ($row in $classSummary) {
    $report.Add(("  {0,-28} {1,5:N0}" -f $row.Classification, $row.Images))
}
$report.Add('')
$report.Add('IMPORTANT')
$report.Add('  This report proves stock identity presence only. It does NOT prove runtime residency.')
$report.Add('  No Xbox image payload bytes were copied.')
$report.Add('')
$report.Add('FIRST RETAIL MATCHES')
foreach ($m in @($matches.ToArray() | Select-Object -First 150)) {
    $zoneText = ($m.Zones | ForEach-Object { "$($_.Zone)[$($_.ZoneClass)]" }) -join '; '
    $report.Add(("  root #{0,4}: {1} -> {2}" -f $m.Index, $m.Name, $zoneText))
}
$report.Add('')
$report.Add('NOT FOUND IN SUPPLIED RETAIL XBOX FASTFILES')
foreach ($m in @($notFound.ToArray() | Select-Object -First 250)) {
    $report.Add(("  root #{0,4}: {1} [{2}]" -f $m.Index, $m.Name, $m.SourceClass))
}
if ($notFound.Count -gt 250) {
    $report.Add(("  ... {0:N0} more" -f ($notFound.Count - 250)))
}

[IO.File]::WriteAllLines($txtPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4R-XBOX] ====================== CENSUS RESULT ========================'
Write-Host "[NUCLEAR-4R-XBOX] Xbox FFs scanned       : $($fastFiles.Count)"
Write-Host "[NUCLEAR-4R-XBOX] OAT scan failures      : $scanFailures"
Write-Host "[NUCLEAR-4R-XBOX] 4R unresolved images   : $($targets.Count)"
Write-Host "[NUCLEAR-4R-XBOX] Found in retail Xbox   : $($matches.Count)"
Write-Host "[NUCLEAR-4R-XBOX] Not found in Xbox      : $($notFound.Count)"
Write-Host "[NUCLEAR-4R-XBOX] Core/global candidates : $($coreCandidates.Count)"
Write-Host "[NUCLEAR-4R-XBOX] Zombie candidates      : $($zombieCandidates.Count)"
Write-Host "[NUCLEAR-4R-XBOX] Map/other retail only  : $($mapSpecific.Count)"
Write-Host "[NUCLEAR-4R-XBOX] Report                  : $txtPath"
Write-Host "[NUCLEAR-4R-XBOX] Manifest                : $jsonPath"
Write-Host '[NUCLEAR-4R-XBOX] ================================================================='
Write-Host '[NUCLEAR-4R-XBOX] PASS: Xbox retail identity census complete; no payload bytes copied.' -ForegroundColor Green
