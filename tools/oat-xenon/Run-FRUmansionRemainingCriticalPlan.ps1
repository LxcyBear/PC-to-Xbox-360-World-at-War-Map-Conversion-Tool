param(
    [string]$ReadinessManifest = '',
    [string]$CriticalResolutionPlanManifest = '',
    [string]$ImageReferenceProofManifest = '',
    [string]$NativeIdentityManifest = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($ReadinessManifest)) {
    $ReadinessManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4f_full_content_readiness\nuclear_4f_full_content_readiness.json'
}
if ([string]::IsNullOrWhiteSpace($CriticalResolutionPlanManifest)) {
    $CriticalResolutionPlanManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4v_xbox_critical_resolution_plan\nuclear_4v_xbox_critical_resolution_plan.json'
}
if ([string]::IsNullOrWhiteSpace($ImageReferenceProofManifest)) {
    $ImageReferenceProofManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4x_xbox_image_reference_proof\nuclear_4x_xbox_image_reference_proof.json'
}
if ([string]::IsNullOrWhiteSpace($NativeIdentityManifest)) {
    $identityDir = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_root_binding\native_identity'
    $found = @(Get-ChildItem -LiteralPath $identityDir -Filter '*_native_identity_manifest.json' -File -ErrorAction SilentlyContinue |
        Sort-Object FullName |
        Select-Object -First 1)
    if ($found.Count -ne 1) {
        throw "Could not identify one native identity manifest under: $identityDir"
    }
    $NativeIdentityManifest = $found[0].FullName
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4x_remaining_critical_plan'
}

foreach ($required in @(
    $ReadinessManifest,
    $CriticalResolutionPlanManifest,
    $ImageReferenceProofManifest,
    $NativeIdentityManifest
)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required manifest not found: $required"
    }
}

function Normalize-Type {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    switch ($Value.Trim().ToLowerInvariant()) {
        'clipmap' { return 'clipmap_pvs' }
        'gameworldsp' { return 'gameworld_sp' }
        'techniqueset' { return 'techset' }
        default { return $Value.Trim().ToLowerInvariant() }
    }
}

$readiness = Get-Content -LiteralPath $ReadinessManifest -Raw | ConvertFrom-Json
$criticalPlan = Get-Content -LiteralPath $CriticalResolutionPlanManifest -Raw | ConvertFrom-Json
$imageRefs = Get-Content -LiteralPath $ImageReferenceProofManifest -Raw | ConvertFrom-Json
$native = Get-Content -LiteralPath $NativeIdentityManifest -Raw | ConvertFrom-Json

if ([string]$criticalPlan.format -ne 'WaW360.Nuclear4V.XboxCriticalResolutionPlan.v1') {
    throw "Unexpected critical resolution plan format: $($criticalPlan.format)"
}
if ([string]$imageRefs.format -ne 'WaW360.Nuclear4X.XboxImageRuntimeReferenceProof.v1' -or -not [bool]$imageRefs.referenceProofPass) {
    throw 'Stage 4X image reference proof is missing or not passing.'
}
if ([string]$native.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or -not [bool]$native.typeSequenceVerified) {
    throw 'Native identity manifest is not verified.'
}
if (-not [bool]$readiness.ProperSizeGate) {
    throw 'Remaining-critical plan requires ProperSizeGate=True.'
}
if ([int]$readiness.CriticalFallbackRoots -ne 83) {
    throw "Expected fresh Stage 4F criticalFallback=83; found $([int]$readiness.CriticalFallbackRoots)."
}

$nativeEntries = @($native.entries)
if ($nativeEntries.Count -ne [int]$readiness.NativeRoots) {
    throw "Native identity count $($nativeEntries.Count) != readiness NativeRoots $([int]$readiness.NativeRoots)."
}

$creditedImageIndices = New-Object 'System.Collections.Generic.HashSet[int]'
foreach ($row in @($imageRefs.resolved)) {
    [void]$creditedImageIndices.Add([int]$row.Index)
}

# Production image graph provides 878 payload roots, and Stage 4X provides 7 runtime refs.
# The remaining native image identities are therefore the 43 custom/dependency image lane.
$productionImageManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4t_production_image_graph\nuclear_4t_production_image_graph.json'
if (-not (Test-Path -LiteralPath $productionImageManifest -PathType Leaf)) {
    throw "Production image graph manifest not found: $productionImageManifest"
}
$prodImages = Get-Content -LiteralPath $productionImageManifest -Raw | ConvertFrom-Json
if ([string]$prodImages.format -ne 'WaW360.Nuclear4T.ProductionImageGraph.v1' -or -not [bool]$prodImages.mergePass) {
    throw 'Production image graph is not passing.'
}

$productionImageNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($row in @($prodImages.images)) {
    [void]$productionImageNames.Add([string]$row.Name)
}

$imageTargets = New-Object 'System.Collections.Generic.List[object]'
foreach ($entry in @($nativeEntries | Sort-Object Index)) {
    $type = Normalize-Type -Value $(if (-not [string]::IsNullOrWhiteSpace([string]$entry.OatTypeName)) { [string]$entry.OatTypeName } else { [string]$entry.TypeName })
    if ($type -ne 'image') { continue }

    $index = [int]$entry.Index
    $name = [string]$entry.Name

    if ($productionImageNames.Contains($name)) { continue }
    if ($creditedImageIndices.Contains($index)) { continue }

    $imageTargets.Add([pscustomobject]@{
        Index = $index
        Type = 'image'
        Name = $name
        Lane = 'custom-image-source-required'
        Implementation = 'PC/dependency image source discovery + Xenon conversion'
    })
}

$stockPayload = New-Object 'System.Collections.Generic.List[object]'
foreach ($row in @($criticalPlan.payloadTargets | Sort-Object Index)) {
    $type = Normalize-Type -Value ([string]$row.Type)
    $implementation = switch ($type) {
        'weapon' { 'retail Xbox Weapon dump/relink round-trip' }
        'rawfile' { 'retail Xbox RawFile dump/relink round-trip' }
        'fx' { 'retail Xbox specialized FX graph extraction/replay' }
        default { 'retail Xbox payload extraction/replay' }
    }

    $stockPayload.Add([pscustomobject]@{
        Index = [int]$row.Index
        Type = $type
        Name = [string]$row.Name
        Lane = 'retail-payload-required'
        PreferredZone = [string]$row.PreferredZone
        BestClass = [string]$row.BestClass
        Implementation = $implementation
    })
}

$custom = New-Object 'System.Collections.Generic.List[object]'
foreach ($row in @($criticalPlan.customTargets | Sort-Object Index)) {
    $type = Normalize-Type -Value ([string]$row.Type)
    $implementation = switch ($type) {
        'weapon' { 'batch custom Weapon pointer/string reconstruction' }
        'fx' { 'batch custom FX exact child-layout serializer' }
        'rawfile' { 'batch RawFile source body/name closure' }
        'gfxworld' { 'map world-root conversion from PC BSP/source structures' }
        'clipmap_pvs' { 'map world-root conversion from PC BSP/source structures' }
        'comworld' { 'map world-root conversion from PC BSP/source structures' }
        'gameworld_sp' { 'map world-root conversion from PC BSP/source structures' }
        default { 'custom source conversion' }
    }

    $custom.Add([pscustomobject]@{
        Index = [int]$row.Index
        Type = $type
        Name = [string]$row.Name
        Lane = if ($type -in @('gfxworld','clipmap_pvs','comworld','gameworld_sp')) { 'custom-world-root' } else { 'custom-source-required' }
        Implementation = $implementation
    })
}

$all = @(
    @($imageTargets.ToArray()) +
    @($stockPayload.ToArray()) +
    @($custom.ToArray())
)

$byLane = @(
    $all |
    Group-Object Lane |
    Sort-Object @{ Expression = { $_.Count }; Descending = $true }, Name |
    ForEach-Object {
        [pscustomobject]@{
            Lane = $_.Name
            Roots = $_.Count
        }
    }
)

$byType = @(
    $all |
    Group-Object Type |
    Sort-Object @{ Expression = { $_.Count }; Descending = $true }, Name |
    ForEach-Object {
        [pscustomobject]@{
            Type = $_.Name
            Roots = $_.Count
            StockPayload = @($_.Group | Where-Object Lane -eq 'retail-payload-required').Count
            Custom = @($_.Group | Where-Object Lane -eq 'custom-source-required').Count
            World = @($_.Group | Where-Object Lane -eq 'custom-world-root').Count
            Images = @($_.Group | Where-Object Lane -eq 'custom-image-source-required').Count
        }
    }
)

$expected =
    $imageTargets.Count +
    $stockPayload.Count +
    $custom.Count

$reconciles = $expected -eq [int]$readiness.CriticalFallbackRoots
if (-not $reconciles) {
    throw "Remaining-critical reconciliation failed: plan=$expected, readiness=$([int]$readiness.CriticalFallbackRoots)."
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4X.RemainingCriticalPlan.v1'
    readinessManifest = (Resolve-Path -LiteralPath $ReadinessManifest).Path
    criticalResolutionPlan = (Resolve-Path -LiteralPath $CriticalResolutionPlanManifest).Path
    imageReferenceProof = (Resolve-Path -LiteralPath $ImageReferenceProofManifest).Path
    nativeIdentityManifest = (Resolve-Path -LiteralPath $NativeIdentityManifest).Path
    criticalFallbackRoots = [int]$readiness.CriticalFallbackRoots
    customImages = $imageTargets.Count
    retailPayloadRequired = $stockPayload.Count
    customNonImage = $custom.Count
    reconciles = $reconciles
    byLane = $byLane
    byType = $byType
    imageTargets = [object[]]$imageTargets.ToArray()
    retailPayloadTargets = [object[]]$stockPayload.ToArray()
    customTargets = [object[]]$custom.ToArray()
    executionOrder = @(
        'retail Weapon payload batch',
        'retail FX payload batch',
        'custom image source/conversion batch',
        'custom Weapon batch',
        'custom FX batch',
        'custom RawFile batch',
        'four world roots as one BSP/world conversion batch'
    )
    policy = @(
        'This plan is a batching/provenance manifest. It does not grant production credit.',
        'No individual-root patch should be promoted without validating the whole affected family batch.',
        'After first FRUmansion hardware success, the same lanes become corpus-wide multi-map family sweeps.'
    )
}

$jsonPath = Join-Path $OutputRoot 'nuclear_4x_remaining_critical_plan.json'
$txtPath = Join-Path $OutputRoot 'nuclear_4x_remaining_critical_plan.txt'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4X - REMAINING CRITICAL CONTENT PLAN')
$report.Add(('=' * 100))
$report.Add(("Critical fallback roots:     {0:N0}" -f [int]$readiness.CriticalFallbackRoots))
$report.Add(("Custom image roots:          {0:N0}" -f $imageTargets.Count))
$report.Add(("Retail payload roots:        {0:N0}" -f $stockPayload.Count))
$report.Add(("Custom non-image roots:      {0:N0}" -f $custom.Count))
$report.Add(("Reconciles exactly:          {0}" -f $reconciles))
$report.Add('')
$report.Add('BY TYPE')
foreach ($row in $byType) {
    $report.Add(("  {0,-14} roots={1,3} stockPayload={2,3} custom={3,3} world={4,3} images={5,3}" -f
        $row.Type, $row.Roots, $row.StockPayload, $row.Custom, $row.World, $row.Images))
}
$report.Add('')
$report.Add('RETAIL PAYLOAD TARGETS')
foreach ($row in $stockPayload.ToArray()) {
    $report.Add(("  root #{0,4}: {1,-12} {2} <- {3} | {4}" -f
        $row.Index, $row.Type, $row.Name, $row.PreferredZone, $row.Implementation))
}
$report.Add('')
$report.Add('CUSTOM NON-IMAGE TARGETS')
foreach ($row in $custom.ToArray()) {
    $report.Add(("  root #{0,4}: {1,-12} {2} | {3}" -f
        $row.Index, $row.Type, $row.Name, $row.Implementation))
}
$report.Add('')
$report.Add('CUSTOM IMAGE TARGETS')
foreach ($row in $imageTargets.ToArray()) {
    $report.Add(("  root #{0,4}: {1}" -f $row.Index, $row.Name))
}
[IO.File]::WriteAllLines($txtPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host '[NUCLEAR-4X-PLAN] ==========================================================='
Write-Host '[NUCLEAR-4X-PLAN] REMAINING CRITICAL CONTENT PLAN'
Write-Host '[NUCLEAR-4X-PLAN] ==========================================================='
Write-Host "[NUCLEAR-4X-PLAN] Critical fallback roots : $([int]$readiness.CriticalFallbackRoots)"
Write-Host "[NUCLEAR-4X-PLAN] Custom images           : $($imageTargets.Count)"
Write-Host "[NUCLEAR-4X-PLAN] Retail payload required : $($stockPayload.Count)"
Write-Host "[NUCLEAR-4X-PLAN] Custom non-image        : $($custom.Count)"
foreach ($row in $byType) {
    Write-Host ("[NUCLEAR-4X-PLAN]   {0,-14} roots={1,3} stock={2,2} custom={3,2} world={4,2} images={5,2}" -f
        $row.Type, $row.Roots, $row.StockPayload, $row.Custom, $row.World, $row.Images)
}
Write-Host "[NUCLEAR-4X-PLAN] Reconciles exactly      : $reconciles"
Write-Host "[NUCLEAR-4X-PLAN] Manifest                : $jsonPath"
Write-Host "[NUCLEAR-4X-PLAN] Report                  : $txtPath"
Write-Host '[NUCLEAR-4X-PLAN] ================================================================='

if (-not $reconciles) {
    exit 4
}
