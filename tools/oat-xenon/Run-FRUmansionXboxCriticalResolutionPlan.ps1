param(
    [string]$CriticalCensusManifest = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($CriticalCensusManifest)) {
    $CriticalCensusManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4v_xbox_critical_family_census\nuclear_4v_xbox_critical_family_census.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4v_xbox_critical_resolution_plan'
}

if (-not (Test-Path -LiteralPath $CriticalCensusManifest -PathType Leaf)) {
    throw "Critical-family census manifest not found: $CriticalCensusManifest"
}

$census = Get-Content -LiteralPath $CriticalCensusManifest -Raw | ConvertFrom-Json
if ([string]$census.format -ne 'WaW360.Nuclear4V.XboxCriticalFamilyCensus.v1') {
    throw "Unexpected critical-family census format: $($census.format)"
}
if ([int]$census.scanFailures -ne 0) {
    throw "Critical-family census contains OAT scan failures: $([int]$census.scanFailures)"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

function Get-ResolutionClass {
    param([string]$BestClass)

    switch ($BestClass) {
        'core-global-candidate' { return 'runtime-reference-candidate' }
        'zombie-base-candidate' { return 'runtime-reference-candidate' }
        'zombie-dlc-candidate' { return 'payload-required' }
        'campaign-zone-only' { return 'payload-required' }
        'multiplayer-zone-only' { return 'payload-required' }
        'other-retail-zone' { return 'payload-required' }
        default { return 'payload-required' }
    }
}

$rows = New-Object 'System.Collections.Generic.List[object]'

foreach ($m in @($census.matches)) {
    $class = Get-ResolutionClass -BestClass ([string]$m.BestClass)
    $rows.Add([pscustomobject]@{
        Index = [int]$m.Index
        Type = [string]$m.Type
        Name = [string]$m.Name
        BestClass = [string]$m.BestClass
        ResolutionClass = $class
        PreferredZone = [string]$m.Zones[0].Zone
        PreferredZonePath = [string]$m.Zones[0].ZonePath
        PreferredListedName = [string]$m.Zones[0].ListedName
        Zones = [object[]]@($m.Zones)
    })
}

$unmatched = New-Object 'System.Collections.Generic.List[object]'
foreach ($m in @($census.notFound)) {
    $unmatched.Add([pscustomobject]@{
        Index = [int]$m.Index
        Type = [string]$m.Type
        Name = [string]$m.Name
        ResolutionClass = 'custom-source-required'
    })
}

$rowArray = [object[]]$rows.ToArray()
$unmatchedArray = [object[]]$unmatched.ToArray()

$referenceCandidates = @($rowArray | Where-Object { $_.ResolutionClass -eq 'runtime-reference-candidate' })
$payloadRequired = @($rowArray | Where-Object { $_.ResolutionClass -eq 'payload-required' })

$typeSummary = @(
    @('fx','weapon','rawfile','gfxworld','clipmap_pvs','comworld','gameworld_sp') |
    ForEach-Object {
        $type = $_
        $native = @($rowArray | Where-Object Type -eq $type)
        $custom = @($unmatchedArray | Where-Object Type -eq $type)
        if ($native.Count -eq 0 -and $custom.Count -eq 0) { return }

        [pscustomobject]@{
            Type = $type
            RetailMatched = $native.Count
            RuntimeReferenceCandidates = @($native | Where-Object ResolutionClass -eq 'runtime-reference-candidate').Count
            PayloadRequired = @($native | Where-Object ResolutionClass -eq 'payload-required').Count
            CustomSourceRequired = $custom.Count
        }
    }
)

$zoneSummary = @(
    $payloadRequired |
    Group-Object PreferredZone |
    Sort-Object @{ Expression = { $_.Count }; Descending = $true }, @{ Expression = { $_.Name }; Descending = $false } |
    ForEach-Object {
        [pscustomobject]@{
            Zone = $_.Name
            Roots = $_.Count
            Fx = @($_.Group | Where-Object Type -eq 'fx').Count
            Weapon = @($_.Group | Where-Object Type -eq 'weapon').Count
            RawFile = @($_.Group | Where-Object Type -eq 'rawfile').Count
        }
    }
)

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4V.XboxCriticalResolutionPlan.v1'
    sourceCensus = (Resolve-Path -LiteralPath $CriticalCensusManifest).Path
    retailMatched = $rowArray.Count
    runtimeReferenceCandidates = $referenceCandidates.Count
    payloadRequired = $payloadRequired.Count
    customSourceRequired = $unmatchedArray.Count
    typeSummary = $typeSummary
    zoneSummary = $zoneSummary
    runtimeReferences = $referenceCandidates
    payloadTargets = $payloadRequired
    customTargets = $unmatchedArray
    policy = @(
        'Only core-global and nazi_zombie_prototype/base identities are classified as runtime-reference candidates.',
        'DLC/campaign/MP/other retail identities remain payload-required until runtime residency is independently proven.',
        'This planning stage does not copy or emit any retail payload bytes.',
        'Runtime-reference candidate status is not yet production credit; the integrated OAT/root-sequence gate must still validate it.'
    )
}

$jsonPath = Join-Path $OutputRoot 'nuclear_4v_xbox_critical_resolution_plan.json'
$txtPath = Join-Path $OutputRoot 'nuclear_4v_xbox_critical_resolution_plan.txt'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4V-XBOX - CRITICAL STOCK RESOLUTION PLAN')
$report.Add(('=' * 100))
$report.Add(("Retail matched roots:          {0:N0}" -f $rowArray.Count))
$report.Add(("Runtime-reference candidates:  {0:N0}" -f $referenceCandidates.Count))
$report.Add(("Retail payload required:       {0:N0}" -f $payloadRequired.Count))
$report.Add(("Custom source required:        {0:N0}" -f $unmatchedArray.Count))
$report.Add('')
$report.Add('BY TYPE')
foreach ($row in $typeSummary) {
    $report.Add(("  {0,-14} retail={1,4:N0} ref={2,4:N0} payload={3,4:N0} custom={4,4:N0}" -f
        $row.Type, $row.RetailMatched, $row.RuntimeReferenceCandidates, $row.PayloadRequired, $row.CustomSourceRequired))
}
$report.Add('')
$report.Add('PAYLOAD ZONES')
foreach ($row in $zoneSummary) {
    $report.Add(("  {0,-32} roots={1,4:N0} fx={2,4:N0} weapon={3,4:N0} rawfile={4,4:N0}" -f
        $row.Zone, $row.Roots, $row.Fx, $row.Weapon, $row.RawFile))
}
$report.Add('')
$report.Add('CUSTOM / NON-RETAIL ROOTS')
foreach ($row in $unmatchedArray) {
    $report.Add(("  root #{0,4}: {1,-14} {2}" -f $row.Index, $row.Type, $row.Name))
}

[IO.File]::WriteAllLines($txtPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host '[NUCLEAR-4V-PLAN] ==========================================================='
Write-Host '[NUCLEAR-4V-PLAN] CRITICAL STOCK RESOLUTION PLAN'
Write-Host '[NUCLEAR-4V-PLAN] ==========================================================='
Write-Host "[NUCLEAR-4V-PLAN] Retail matched roots         : $($rowArray.Count)"
Write-Host "[NUCLEAR-4V-PLAN] Runtime-reference candidates : $($referenceCandidates.Count)"
Write-Host "[NUCLEAR-4V-PLAN] Retail payload required      : $($payloadRequired.Count)"
Write-Host "[NUCLEAR-4V-PLAN] Custom source required       : $($unmatchedArray.Count)"
foreach ($row in $typeSummary) {
    Write-Host ("[NUCLEAR-4V-PLAN]   {0,-14} retail={1,3} ref={2,3} payload={3,3} custom={4,3}" -f
        $row.Type, $row.RetailMatched, $row.RuntimeReferenceCandidates, $row.PayloadRequired, $row.CustomSourceRequired)
}
Write-Host "[NUCLEAR-4V-PLAN] Manifest                     : $jsonPath"
Write-Host "[NUCLEAR-4V-PLAN] Report                       : $txtPath"
Write-Host '[NUCLEAR-4V-PLAN] ================================================================='
Write-Host '[NUCLEAR-4V-PLAN] PASS: stock critical roots split into reference/payload/custom lanes.' -ForegroundColor Green
