[CmdletBinding()]
param(
    [string]$XboxGameRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\XBOX GAME FILES',
    [string]$MaterialSlotManifest = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($MaterialSlotManifest)) {
    $MaterialSlotManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_5b_material_slot_image_closure\nuclear_5b_material_slot_image_closure.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_5c_mapped_pc_xbox_image_census'
}

if (-not (Test-Path -LiteralPath $XboxGameRoot -PathType Container)) {
    throw "Xbox retail root not found: $XboxGameRoot"
}
if (-not (Test-Path -LiteralPath $MaterialSlotManifest -PathType Leaf)) {
    throw "Stage 5B manifest not found: $MaterialSlotManifest"
}
if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) {
    throw "Pinned Xenon OAT Unlinker not found: $unlinker"
}

function Normalize-ImageIdentity {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }

    $v = $Value.Trim().Trim('"').Replace('\','/').TrimStart('/')
    while ($v.StartsWith(',', [StringComparison]::Ordinal)) {
        $v = $v.Substring(1)
    }

    $marker = $v.ToLowerInvariant().LastIndexOf('/images/')
    if ($marker -ge 0) {
        $v = $v.Substring($marker + 8)
    }
    elseif ($v.StartsWith('images/', [StringComparison]::OrdinalIgnoreCase)) {
        $v = $v.Substring(7)
    }

    foreach ($ext in @('.iwi','.dds')) {
        if ($v.EndsWith($ext, [StringComparison]::OrdinalIgnoreCase)) {
            $v = $v.Substring(0, $v.Length - $ext.Length)
            break
        }
    }

    if ($v.EndsWith('_wj', [StringComparison]::OrdinalIgnoreCase)) {
        $v = $v.Substring(0, $v.Length - 3)
    }

    return $v.ToLowerInvariant()
}

function Strip-LogPrefix {
    param([string]$Line)
    if ($null -eq $Line) { return '' }
    $text = $Line.Trim()
    $prefix = $text.IndexOf('] ')
    if ($text.StartsWith('[', [StringComparison]::Ordinal) -and $prefix -ge 0) {
        return $text.Substring($prefix + 2).Trim()
    }
    return $text
}

function Get-ZoneClass {
    param([string]$FileName)
    $n = $FileName.ToLowerInvariant()

    if ($n -in @('common.ff','code_post_gfx.ff','ui.ff','patch.ff')) {
        return 'core-global'
    }
    if ($n -in @('nazi_zombie_prototype.ff','nazi_zombie_prototype_load.ff')) {
        return 'zombie-base'
    }
    if ($n -match '(?i)zombie|dlc') {
        return 'zombie-dlc'
    }
    if ($n -match '^(ber|mak|oki|pel|see|sniper|pby_fly|credits|intro|outro)') {
        return 'campaign'
    }
    if ($n.StartsWith('mp_') -or $n -in @('common_mp.ff','code_post_gfx_mp.ff','ui_mp.ff')) {
        return 'multiplayer'
    }
    return 'other-retail'
}

function Get-ClassRank {
    param([string]$Class)
    switch ($Class) {
        'core-global' { return 0 }
        'zombie-base' { return 1 }
        'zombie-dlc' { return 2 }
        'campaign' { return 3 }
        'other-retail' { return 4 }
        'multiplayer' { return 5 }
        default { return 9 }
    }
}

$stage5b = Get-Content -LiteralPath $MaterialSlotManifest -Raw | ConvertFrom-Json
if ([string]$stage5b.format -ne 'WaW360.Nuclear5B.MaterialSlotImageClosure.v1' -or
    [int]$stage5b.targetImages -ne 43) {
    throw 'Stage 5C requires the 43-image Stage 5B material-slot closure manifest.'
}

$targets = New-Object 'System.Collections.Generic.List[object]'
foreach ($row in @($stage5b.images | Sort-Object Index)) {
    $mapped = [string]$row.MappedPcImage
    if ([string]::IsNullOrWhiteSpace($mapped)) {
        throw "Stage 5B target root #$([int]$row.Index) '$([string]$row.Name)' has no mapped PC image identity."
    }

    $targets.Add([pscustomobject]@{
        Index = [int]$row.Index
        NativeName = [string]$row.Name
        PcImage = $mapped
        PcKey = Normalize-ImageIdentity -Value $mapped
        MaterialOccurrences = [int]$row.MaterialOccurrences
        PriorStatus = [string]$row.Status
    })
}

if ($targets.Count -ne 43) {
    throw "Expected exactly 43 mapped image targets; found $($targets.Count)."
}

$targetByPcKey = @{}
foreach ($target in $targets.ToArray()) {
    $key = [string]$target.PcKey
    if ([string]::IsNullOrWhiteSpace($key)) {
        throw "Empty normalized mapped PC identity for root #$($target.Index)."
    }
    if (-not $targetByPcKey.ContainsKey($key)) {
        $targetByPcKey[$key] = New-Object 'System.Collections.Generic.List[object]'
    }
    $targetByPcKey[$key].Add($target)
}

$fastFiles = @(
    Get-ChildItem -LiteralPath $XboxGameRoot -Filter '*.ff' -File -Recurse -ErrorAction Stop |
    Sort-Object FullName
)
if ($fastFiles.Count -eq 0) {
    throw "No Xbox retail .ff files found under: $XboxGameRoot"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$logRoot = Join-Path $OutputRoot 'zone_logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

$stockIndex = @{}
$zoneStats = New-Object 'System.Collections.Generic.List[object]'
$scanFailures = 0
$totalImageRows = 0

Write-Host '[NUCLEAR-5C-IMG] ============================================================'
Write-Host '[NUCLEAR-5C-IMG] MAPPED PC IDENTITY -> RETAIL XBOX IMAGE CENSUS'
Write-Host '[NUCLEAR-5C-IMG] ============================================================'
Write-Host "[NUCLEAR-5C-IMG] Targets       : $($targets.Count)"
Write-Host "[NUCLEAR-5C-IMG] Unique PC IDs : $($targetByPcKey.Count)"
Write-Host "[NUCLEAR-5C-IMG] Retail FFs    : $($fastFiles.Count)"
Write-Host '[NUCLEAR-5C-IMG] Searching retail Xbox using the PC-side material-slot identities, not the final FRUmansion names.'
Write-Host ''

$ordinal = 0
foreach ($ff in $fastFiles) {
    $ordinal++
    $zoneClass = Get-ZoneClass -FileName $ff.Name

    $saved = $ErrorActionPreference
    $lines = @()
    $code = -1
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& $unlinker '--list' '--no-color' $ff.FullName 2>&1 | ForEach-Object { [string]$_ })
        $code = [int]$LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $saved
    }

    $safe = ($ff.Name -replace '[^A-Za-z0-9_.-]', '_')
    $log = Join-Path $logRoot ($safe + '.list.log')
    [IO.File]::WriteAllLines($log, [string[]]$lines, [Text.UTF8Encoding]::new($false))

    $zoneImages = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $zoneMatches = 0

    foreach ($raw in $lines) {
        $line = Strip-LogPrefix -Line $raw
        if (-not $line.StartsWith('image,', [StringComparison]::OrdinalIgnoreCase)) { continue }

        $listed = $line.Substring(6).Trim()
        $key = Normalize-ImageIdentity -Value $listed
        if ([string]::IsNullOrWhiteSpace($key)) { continue }

        [void]$zoneImages.Add($key)
        if (-not $targetByPcKey.ContainsKey($key)) { continue }

        if (-not $stockIndex.ContainsKey($key)) {
            $stockIndex[$key] = New-Object 'System.Collections.Generic.List[object]'
        }

        $stockIndex[$key].Add([pscustomobject]@{
            Zone = $ff.Name
            ZonePath = $ff.FullName
            ZoneClass = $zoneClass
            ListedName = $listed
        })
        $zoneMatches++
    }

    $totalImageRows += $zoneImages.Count
    if ($code -ne 0) { $scanFailures++ }

    $zoneStats.Add([pscustomobject]@{
        Zone = $ff.Name
        ZonePath = $ff.FullName
        ZoneClass = $zoneClass
        OatExitCode = $code
        UniqueImageIdentities = $zoneImages.Count
        MappedPcMatches = $zoneMatches
        Log = $log
    })

    if ($zoneMatches -gt 0) {
        Write-Host ("[NUCLEAR-5C-IMG] [{0,3}/{1,3}] {2,-36} -> {3} mapped hit(s)" -f
            $ordinal, $fastFiles.Count, $ff.Name, $zoneMatches) -ForegroundColor Cyan
    }
}

$results = New-Object 'System.Collections.Generic.List[object]'
$found = 0
$missing = 0

foreach ($target in $targets.ToArray()) {
    $key = [string]$target.PcKey
    $zones = @()

    if ($stockIndex.ContainsKey($key)) {
        $zones = @(
            $stockIndex[$key].ToArray() |
            Sort-Object @{ Expression = { Get-ClassRank -Class ([string]$_.ZoneClass) } },
                        @{ Expression = { $_.Zone }; Descending = $false } |
            Select-Object -Unique Zone, ZonePath, ZoneClass, ListedName
        )
    }

    $present = $zones.Count -gt 0
    if ($present) { $found++ } else { $missing++ }

    $results.Add([pscustomobject]@{
        Index = [int]$target.Index
        NativeName = [string]$target.NativeName
        PcImage = [string]$target.PcImage
        PcKey = $key
        MaterialOccurrences = [int]$target.MaterialOccurrences
        FoundInRetailXbox = $present
        RetailHits = $zones.Count
        BestClass = $(if ($present) { [string]$zones[0].ZoneClass } else { '' })
        Zones = [object[]]$zones
    })
}

$classSummary = @(
    $results.ToArray() |
    Where-Object { [bool]$_.FoundInRetailXbox } |
    Group-Object BestClass |
    Sort-Object @{ Expression = { Get-ClassRank -Class $_.Name } },
                @{ Expression = { $_.Name }; Descending = $false } |
    ForEach-Object {
        [pscustomobject]@{
            Classification = $_.Name
            Images = $_.Count
        }
    }
)

$manifest = [ordered]@{
    format = 'WaW360.Nuclear5C.MappedPcXboxImageCensus.v1'
    stage5BManifest = (Resolve-Path -LiteralPath $MaterialSlotManifest).Path
    xboxGameRoot = (Resolve-Path -LiteralPath $XboxGameRoot).Path
    targets = $targets.Count
    uniqueMappedPcIdentities = $targetByPcKey.Count
    retailFastFilesScanned = $fastFiles.Count
    scanFailures = $scanFailures
    totalZoneImageIdentityRows = $totalImageRows
    foundInRetailXbox = $found
    notFoundInRetailXbox = $missing
    classSummary = $classSummary
    zones = [object[]]$zoneStats.ToArray()
    images = [object[]]$results.ToArray()
    policy = @(
        'Stage 5C searches the user-owned retail Xbox 360 corpus using PC-side material-slot image identities recovered by Stage 5B.',
        'This stage is identity evidence only and copies no retail payload bytes.',
        'A retail match may become a production payload donor only after exact Xenon graph extraction and standalone OAT validation.',
        'The native FRUmansion oracle contributes only the final root identity and material-slot relationship; no oracle image payload bytes are used.'
    )
}

$jsonPath = Join-Path $OutputRoot 'nuclear_5c_mapped_pc_xbox_image_census.json'
$txtPath = Join-Path $OutputRoot 'nuclear_5c_mapped_pc_xbox_image_census.txt'
$manifest | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 5C - MAPPED PC IDENTITY -> RETAIL XBOX IMAGE CENSUS')
$report.Add(('=' * 104))
$report.Add(("Targets:                       {0:N0}" -f $targets.Count))
$report.Add(("Unique mapped PC identities:   {0:N0}" -f $targetByPcKey.Count))
$report.Add(("Retail FFs scanned:            {0:N0}" -f $fastFiles.Count))
$report.Add(("OAT scan failures:             {0:N0}" -f $scanFailures))
$report.Add(("Found in retail Xbox:          {0:N0}" -f $found))
$report.Add(("Not found in retail Xbox:      {0:N0}" -f $missing))
$report.Add('')
$report.Add('CLASSIFICATION')
foreach ($row in $classSummary) {
    $report.Add(("  {0,-24} {1,4:N0}" -f $row.Classification, $row.Images))
}
$report.Add('')
$report.Add('TARGETS')
foreach ($row in $results.ToArray() | Sort-Object Index) {
    $report.Add(("  root #{0,4}: {1}" -f $row.Index, $row.NativeName))
    $report.Add(("           PC identity: {0}" -f $row.PcImage))
    if ([bool]$row.FoundInRetailXbox) {
        $zoneText = (@($row.Zones) | ForEach-Object { "$($_.Zone)[$($_.ZoneClass)]" }) -join '; '
        $report.Add(("           Xbox retail: {0}" -f $zoneText))
    }
    else {
        $report.Add('           Xbox retail: NOT FOUND')
    }
}
[IO.File]::WriteAllLines($txtPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-5C-IMG] ====================== RESULT ==============================='
Write-Host "[NUCLEAR-5C-IMG] Targets                  : $($targets.Count)"
Write-Host "[NUCLEAR-5C-IMG] Unique mapped PC IDs     : $($targetByPcKey.Count)"
Write-Host "[NUCLEAR-5C-IMG] Retail FFs scanned       : $($fastFiles.Count)"
Write-Host "[NUCLEAR-5C-IMG] OAT scan failures        : $scanFailures"
Write-Host "[NUCLEAR-5C-IMG] Found in retail Xbox     : $found"
Write-Host "[NUCLEAR-5C-IMG] Not found in retail Xbox : $missing"
foreach ($row in $classSummary) {
    Write-Host ("[NUCLEAR-5C-IMG]   {0}: {1}" -f $row.Classification, $row.Images)
}
Write-Host "[NUCLEAR-5C-IMG] Manifest                 : $jsonPath"
Write-Host "[NUCLEAR-5C-IMG] Report                   : $txtPath"
Write-Host '[NUCLEAR-5C-IMG] ================================================================='
Write-Host '[NUCLEAR-5C-IMG] PASS: mapped-PC retail Xbox identity census complete.' -ForegroundColor Green
