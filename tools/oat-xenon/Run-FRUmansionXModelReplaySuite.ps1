[CmdletBinding()]
param(
    [string]$InventoryRoot = '',
    [string]$GraphRoot = '',
    [string]$OutputRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($InventoryRoot)) {
    $InventoryRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4x_xmodel_graph_inventory'
}
if ([string]::IsNullOrWhiteSpace($GraphRoot)) {
    $GraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4xb_xmodel_relocatable_graph'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4xc_xmodel_replay_suite'
}

$inventoryManifest = Join-Path $InventoryRoot 'nuclear_4x_xmodel_graph_inventory.json'
$graphManifest = Join-Path $GraphRoot 'nuclear_4xb_xmodel_relocatable_graph.json'
foreach ($required in @($inventoryManifest, $graphManifest)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Replay suite input missing: $required"
    }
}

$inventory = Get-Content -LiteralPath $inventoryManifest -Raw | ConvertFrom-Json
$graph = Get-Content -LiteralPath $graphManifest -Raw | ConvertFrom-Json
if (-not [bool]$inventory.corpusInventoryPass) { throw '4X-A corpus inventory is not passing.' }
if (-not [bool]$graph.corpusGraphPass) { throw '4X-B relocatable graph corpus is not passing.' }

$rows = @($inventory.rows | Where-Object { [bool]$_.Pass } | Sort-Object { [int]$_.Ordinal })
if ($rows.Count -ne 280) { throw "Expected 280 passing inventory rows; found $($rows.Count)." }

$selected = New-Object 'System.Collections.Generic.List[object]'
$used = @{}

function Add-Case {
    param([string]$Label, [scriptblock]$Predicate)

    $candidate = $rows | Where-Object {
        -not $used.ContainsKey([int]$_.Ordinal) -and (& $Predicate $_)
    } | Select-Object -First 1

    if ($null -eq $candidate) {
        throw "Could not select replay-suite case '$Label'."
    }

    $used[[int]$candidate.Ordinal] = $true
    $selected.Add([pscustomobject]@{
        Label = $Label
        Ordinal = [int]$candidate.Ordinal
        Name = [string]$candidate.Name
    })
}

Add-Case 'RIGID_COLLISION' { param($r) [int]$r.BlendEntries -eq 0 -and [int]$r.CollisionTrees -gt 0 -and [int]$r.NumLods -gt 1 }
Add-Case 'SKINNED_COLLISION' { param($r) [int]$r.BlendEntries -gt 0 -and [int]$r.CollisionTrees -gt 0 }
Add-Case 'NO_COLLISION_TREE' { param($r) [int]$r.CollisionTrees -eq 0 }
Add-Case 'PHYS_PRESET' { param($r) [bool]$r.HasPhysPreset }

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$runner = Join-Path $PSScriptRoot 'Run-FRUmansionXModelReplayCanary.ps1'

Write-Host '[NUCLEAR-4X-C] =============================================================='
Write-Host '[NUCLEAR-4X-C] REPRESENTATIVE XMODEL REPLAY SUITE'
Write-Host '[NUCLEAR-4X-C] =============================================================='
foreach ($case in $selected) {
    Write-Host ("[NUCLEAR-4X-C] {0,-20} ordinal={1,3}  {2}" -f $case.Label, $case.Ordinal, $case.Name)
}
Write-Host ''

$results = @()
foreach ($case in $selected) {
    $caseOut = Join-Path $OutputRoot $case.Label.ToLowerInvariant()
    Write-Host ''
    Write-Host ("[NUCLEAR-4X-C] RUN {0}: {1}" -f $case.Label, $case.Name) -ForegroundColor Cyan

    & powershell -ExecutionPolicy Bypass -File $runner -Ordinal ([int]$case.Ordinal) -GraphRoot $GraphRoot -OutputRoot $caseOut
    $exitCode = [int]$LASTEXITCODE

    $results += [pscustomobject]@{
        Label = [string]$case.Label
        Ordinal = [int]$case.Ordinal
        Name = [string]$case.Name
        ExitCode = $exitCode
        Pass = ($exitCode -eq 0)
    }

    if ($exitCode -ne 0) {
        Write-Host ''
        Write-Host ("[NUCLEAR-4X-C] SUITE BLOCKED at {0}: {1}" -f $case.Label, $case.Name) -ForegroundColor Yellow
        $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputRoot 'nuclear_4xc_replay_suite.json') -Encoding UTF8
        exit $exitCode
    }
}

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4XC.XModelReplaySuite.v1'
    cases = $results
    pass = (@($results | Where-Object { -not [bool]$_.Pass }).Count -eq 0 -and $results.Count -eq 4)
}
$manifestFile = Join-Path $OutputRoot 'nuclear_4xc_replay_suite.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestFile -Encoding UTF8

Write-Host ''
Write-Host '[NUCLEAR-4X-C] REPRESENTATIVE XMODEL REPLAY SUITE: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4X-C] Rigid, skinned, collision-free, and PhysPreset graph shapes all survived exact OAT replay.'
Write-Host "[NUCLEAR-4X-C] Manifest: $manifestFile"
exit 0
