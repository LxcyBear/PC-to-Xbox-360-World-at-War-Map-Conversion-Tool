param(
    [string]$PcMapsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps',
    [string]$MapName = 'nazi_zombie_FRUmansion',
    [string]$NativeControlDirectory = 'C:\Users\jorda\OneDrive\Desktop\working frumansion',
    [double]$MinimumSizeRatio = 0.80,
    [double]$MaximumSizeRatio = 1.20
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$outputRoot = Join-Path $repo 'output'
$graphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_oat_graph_dump'
$bindingRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_root_binding'
$bindingManifest = Join-Path $bindingRoot 'nuclear_xenon_root_binding.json'
$identityDir = Join-Path $bindingRoot 'native_identity'
$identityManifest = @(Get-ChildItem -LiteralPath $identityDir -Filter '*_native_identity_manifest.json' -File |
    Sort-Object FullName |
    Select-Object -First 1)

$productionImageGraftManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4u_production_image_graft\nuclear_4u_production_image_graft.json'
$productionXModelGraftManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4y_xmodel_production_graft\nuclear_4y_xmodel_production_graft.json'
$productionMaterialGraftManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4zd_material_production_graft\nuclear_4zd_material_production_graft.json'
$productionCriticalReferenceProofManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4w_xbox_reference_proof\nuclear_4w_xbox_reference_proof.json'
$productionImageReferenceProofManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4x_xbox_image_reference_proof\nuclear_4x_xbox_image_reference_proof.json'
$productionStockWeaponRoundTripManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4y_stock_weapon_roundtrip\nuclear_4y_stock_weapon_roundtrip.json'
$productionStockFxRoundTripManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4z_stock_fx_roundtrip\nuclear_4z_stock_fx_roundtrip.json'

function Find-MainFastFile {
    param([Parameter(Mandatory=$true)][string]$Directory)

    foreach ($name in @('frumansion_j_p.ff', 'nazi_zombie_FRUmansion.ff', 'nazi_zombie_frumansion.ff')) {
        $candidate = Join-Path $Directory $name
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }

    $candidates = @(Get-ChildItem -LiteralPath $Directory -Filter '*.ff' -File |
        Where-Object {
            $_.Name -notmatch '(?i)_load[.]ff$' -and
            $_.Name -notmatch '(?i)_patch[.]ff$'
        })

    if ($candidates.Count -eq 1) {
        return $candidates[0].FullName
    }

    throw "Could not identify one native FRUmansion main fastfile in: $Directory"
}

$nativeMain = Find-MainFastFile -Directory $NativeControlDirectory

if (-not (Test-Path -LiteralPath $PcMapsRoot -PathType Container)) {
    throw "PC maps root not found: $PcMapsRoot"
}
if (-not (Test-Path -LiteralPath $graphRoot -PathType Container)) {
    throw "Missing nuclear graph dump: $graphRoot"
}
if (-not (Test-Path -LiteralPath $bindingManifest -PathType Leaf)) {
    throw "Missing Stage 4C root-binding manifest: $bindingManifest"
}
if ($identityManifest.Count -ne 1) {
    throw "Could not identify Stage 4C native identity manifest under: $identityDir"
}

$productionImageCredit = $null
if (Test-Path -LiteralPath $productionImageGraftManifest -PathType Leaf) {
    try {
        $stage4u = Get-Content -LiteralPath $productionImageGraftManifest -Raw | ConvertFrom-Json
        if ([string]$stage4u.format -eq 'WaW360.Nuclear4U.ProductionImageGraft.v1' -and
            [bool]$stage4u.productionImageGraftPass -and
            [int]$stage4u.nativeRoots -eq 6811 -and
            [int]$stage4u.oatListedRoots -eq 6811 -and
            [bool]$stage4u.exactIdentitySequence -and
            [bool]$stage4u.oatZeroWarningsZeroErrors -and
            [bool]$stage4u.layoutReplayExact) {
            $productionImageCredit = $productionImageGraftManifest
        }
    }
    catch {
        throw "Stage 4U manifest exists but could not be validated: $($_.Exception.Message)"
    }
}

$productionXModelCredit = $null
if (Test-Path -LiteralPath $productionXModelGraftManifest -PathType Leaf) {
    try {
        $stage4y = Get-Content -LiteralPath $productionXModelGraftManifest -Raw | ConvertFrom-Json
        if ([string]$stage4y.format -eq 'WaW360.Nuclear4Y.XModelProductionGraft.v1' -and
            [bool]$stage4y.productionXModelGraftPass -and
            [int]$stage4y.pcBackedOwnedXModels -eq 280 -and
            [int]$stage4y.preservedXModelReferenceRoots -eq 9 -and
            [bool]$stage4y.exactIdentitySequence -and
            [bool]$stage4y.oatZeroWarningsZeroErrors -and
            [bool]$stage4y.layoutReplayExact) {
            $productionXModelCredit = $productionXModelGraftManifest
        }
    }
    catch {
        throw "Stage 4Y manifest exists but could not be validated: $($_.Exception.Message)"
    }
}

$productionMaterialCredit = $null
if (Test-Path -LiteralPath $productionMaterialGraftManifest -PathType Leaf) {
    try {
        $stage4zd = Get-Content -LiteralPath $productionMaterialGraftManifest -Raw | ConvertFrom-Json
        if ([string]$stage4zd.format -eq 'WaW360.Nuclear4ZD.MaterialProductionGraft.v1' -and
            [bool]$stage4zd.productionMaterialGraftPass -and
            [int]$stage4zd.nativeRoots -eq 6811 -and
            [int]$stage4zd.oatListedRoots -eq 6811 -and
            [int]$stage4zd.pcBackedMaterials -eq 502 -and
            [int]$stage4zd.materialReferenceRoots -eq 88 -and
            [int]$stage4zd.pcBackedOwnedXModels -eq 280 -and
            [int]$stage4zd.preservedXModelReferenceRoots -eq 9 -and
            [bool]$stage4zd.exactIdentitySequence -and
            [bool]$stage4zd.oatZeroWarningsZeroErrors -and
            [bool]$stage4zd.layoutReplayExact) {
            $productionMaterialCredit = $productionMaterialGraftManifest
        }
    }
    catch {
        throw "Stage 4Z-D manifest exists but could not be validated: $($_.Exception.Message)"
    }
}

$productionCriticalReferenceCredit = $null
if (Test-Path -LiteralPath $productionCriticalReferenceProofManifest -PathType Leaf) {
    try {
        $stage4w = Get-Content -LiteralPath $productionCriticalReferenceProofManifest -Raw | ConvertFrom-Json
        if ([string]$stage4w.format -eq 'WaW360.Nuclear4W.XboxRuntimeReferenceProof.v1' -and
            [bool]$stage4w.referenceProofPass -and
            [int]$stage4w.referenceCandidates -eq 200 -and
            [int]$stage4w.resolvedReferences -eq 200 -and
            [int]$stage4w.missingReferences -eq 0 -and
            [int]$stage4w.linkerExitCode -eq 0 -and
            [int]$stage4w.unlinkerExitCode -eq 0) {
            $productionCriticalReferenceCredit = $productionCriticalReferenceProofManifest
        }
    }
    catch {
        throw "Stage 4W manifest exists but could not be validated: $($_.Exception.Message)"
    }
}

$productionImageReferenceCredit = $null
if (Test-Path -LiteralPath $productionImageReferenceProofManifest -PathType Leaf) {
    try {
        $stage4x = Get-Content -LiteralPath $productionImageReferenceProofManifest -Raw | ConvertFrom-Json
        if ([string]$stage4x.format -eq 'WaW360.Nuclear4X.XboxImageRuntimeReferenceProof.v1' -and
            [bool]$stage4x.referenceProofPass -and
            [int]$stage4x.runtimeReferenceCandidates -eq 7 -and
            [int]$stage4x.payloadRequiredStockImages -eq 0 -and
            [int]$stage4x.resolvedReferences -eq 7 -and
            [int]$stage4x.missingReferences -eq 0 -and
            [int]$stage4x.linkerExitCode -eq 0 -and
            [int]$stage4x.unlinkerExitCode -eq 0) {
            $productionImageReferenceCredit = $productionImageReferenceProofManifest
        }
    }
    catch {
        throw "Stage 4X manifest exists but could not be validated: $($_.Exception.Message)"
    }
}

$productionStockWeaponCredit = $null
if (Test-Path -LiteralPath $productionStockWeaponRoundTripManifest -PathType Leaf) {
    try {
        $stage4yWeapon = Get-Content -LiteralPath $productionStockWeaponRoundTripManifest -Raw | ConvertFrom-Json
        if ([string]$stage4yWeapon.format -eq 'WaW360.Nuclear4Y.StockWeaponRoundTrip.v1' -and
            [bool]$stage4yWeapon.roundTripPass -and
            [int]$stage4yWeapon.targetWeapons -eq 3 -and
            [int]$stage4yWeapon.linkerExitCode -eq 0 -and
            [int]$stage4yWeapon.readbackExitCode -eq 0 -and
            [int]$stage4yWeapon.readbackWeapons -eq 3 -and
            [int]$stage4yWeapon.exactInfoStringRoundTrips -eq 3) {
            $productionStockWeaponCredit = $productionStockWeaponRoundTripManifest
        }
    }
    catch {
        throw "Stock Weapon round-trip manifest exists but could not be validated: $($_.Exception.Message)"
    }
}

$productionStockFxCredit = $null
if (Test-Path -LiteralPath $productionStockFxRoundTripManifest -PathType Leaf) {
    try {
        $stage4zFx = Get-Content -LiteralPath $productionStockFxRoundTripManifest -Raw | ConvertFrom-Json
        if ([string]$stage4zFx.format -eq 'WaW360.Nuclear4Z.StockFxRoundTrip.v1' -and
            [bool]$stage4zFx.roundTripPass -and
            [int]$stage4zFx.targetFx -eq 2 -and
            [int]$stage4zFx.linkerExitCode -eq 0 -and
            [int]$stage4zFx.readbackExitCode -eq 0 -and
            [int]$stage4zFx.resolvedFx -eq 2 -and
            @($stage4zFx.missingFx).Count -eq 0) {
            $productionStockFxCredit = $productionStockFxRoundTripManifest
        }
    }
    catch {
        throw "Stock FX round-trip manifest exists but could not be validated: $($_.Exception.Message)"
    }
}

Write-Host '[NUCLEAR-4F] ================================================================ '
Write-Host '[NUCLEAR-4F] FULL-CONTENT MASS / PRE-HARDWARE GATE'
Write-Host '[NUCLEAR-4F] ================================================================'
Write-Host "[NUCLEAR-4F] PC maps root       : $PcMapsRoot"
Write-Host "[NUCLEAR-4F] Map                : $MapName"
Write-Host "[NUCLEAR-4F] Native oracle      : $nativeMain"
Write-Host "[NUCLEAR-4F] Renderer graph     : $graphRoot"
Write-Host "[NUCLEAR-4F] Native identities  : $($identityManifest[0].FullName)"
Write-Host "[NUCLEAR-4F] Renderer bindings  : $bindingManifest"
Write-Host "[NUCLEAR-4F] 4U image credit    : $(if ($null -eq $productionImageCredit) { '<none>' } else { $productionImageCredit })"
Write-Host "[NUCLEAR-4F] 4Y XModel credit   : $(if ($null -eq $productionXModelCredit) { '<none>' } else { $productionXModelCredit })"
Write-Host "[NUCLEAR-4F] 4Z-D Material credit: $(if ($null -eq $productionMaterialCredit) { '<none>' } else { $productionMaterialCredit })"
Write-Host "[NUCLEAR-4F] 4W retail refs     : $(if ($null -eq $productionCriticalReferenceCredit) { '<none>' } else { $productionCriticalReferenceCredit })"
Write-Host "[NUCLEAR-4F] 4X image refs      : $(if ($null -eq $productionImageReferenceCredit) { '<none>' } else { $productionImageReferenceCredit })"
Write-Host "[NUCLEAR-4F] Stock Weapon proof : $(if ($null -eq $productionStockWeaponCredit) { '<none>' } else { $productionStockWeaponCredit })"
Write-Host "[NUCLEAR-4F] Stock FX proof     : $(if ($null -eq $productionStockFxCredit) { '<none>' } else { $productionStockFxCredit })"
Write-Host "[NUCLEAR-4F] Size window        : $([math]::Round($MinimumSizeRatio * 100))% .. $([math]::Round($MaximumSizeRatio * 100))% of decompressed native oracle"
Write-Host ''
Write-Host '[NUCLEAR-4F] POLICY: no Xbox test candidate until size + critical-content gates both pass.'
Write-Host ''

$minInvariant = $MinimumSizeRatio.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$maxInvariant = $MaximumSizeRatio.ToString([System.Globalization.CultureInfo]::InvariantCulture)

Push-Location $repo
try {
    $args = @(
        'run',
        '--project', '.\src\WaW360.Cli\WaW360.Cli.csproj',
        '-c', 'Release',
        '--',
        'linker-v2-full-content-readiness',
        $PcMapsRoot,
        $MapName,
        $outputRoot,
        $nativeMain,
        $graphRoot,
        $identityManifest[0].FullName,
        $bindingManifest,
        $minInvariant,
        $maxInvariant
    )
    if ($null -ne $productionImageCredit -or
        $null -ne $productionXModelCredit -or
        $null -ne $productionMaterialCredit -or
        $null -ne $productionCriticalReferenceCredit -or
        $null -ne $productionImageReferenceCredit -or
        $null -ne $productionStockWeaponCredit -or
        $null -ne $productionStockFxCredit) {
        # CLI positions keep image -> 4Y -> 4Z-D -> 4W. Preserve empty earlier
        # slots whenever a later production credit exists.
        $args += $(if ($null -eq $productionImageCredit) { '' } else { $productionImageCredit })
    }
    if ($null -ne $productionXModelCredit -or
        $null -ne $productionMaterialCredit -or
        $null -ne $productionCriticalReferenceCredit -or
        $null -ne $productionImageReferenceCredit -or
        $null -ne $productionStockWeaponCredit -or
        $null -ne $productionStockFxCredit) {
        $args += $(if ($null -eq $productionXModelCredit) { '' } else { $productionXModelCredit })
    }
    if ($null -ne $productionMaterialCredit -or
        $null -ne $productionCriticalReferenceCredit -or
        $null -ne $productionImageReferenceCredit -or
        $null -ne $productionStockWeaponCredit -or
        $null -ne $productionStockFxCredit) {
        $args += $(if ($null -eq $productionMaterialCredit) { '' } else { $productionMaterialCredit })
    }
    if ($null -ne $productionCriticalReferenceCredit -or
        $null -ne $productionImageReferenceCredit -or
        $null -ne $productionStockWeaponCredit -or
        $null -ne $productionStockFxCredit) {
        $args += $(if ($null -eq $productionCriticalReferenceCredit) { '' } else { $productionCriticalReferenceCredit })
    }
    if ($null -ne $productionImageReferenceCredit -or
        $null -ne $productionStockWeaponCredit -or
        $null -ne $productionStockFxCredit) {
        $args += $(if ($null -eq $productionImageReferenceCredit) { '' } else { $productionImageReferenceCredit })
    }
    if ($null -ne $productionStockWeaponCredit -or
        $null -ne $productionStockFxCredit) {
        $args += $(if ($null -eq $productionStockWeaponCredit) { '' } else { $productionStockWeaponCredit })
    }
    if ($null -ne $productionStockFxCredit) {
        $args += $productionStockFxCredit
    }

    & dotnet @args
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

$report = Join-Path $outputRoot "$MapName\nuclear_4f_full_content_readiness\nuclear_4f_full_content_readiness.txt"

Write-Host ''
if (($code -eq 0 -or $code -eq 4) -and (Test-Path -LiteralPath $report -PathType Leaf)) {
    Write-Host '[NUCLEAR-4F] ================= FULL CONTENT REPORT =========================='
    Get-Content -LiteralPath $report
    Write-Host '[NUCLEAR-4F] ================================================================='
}
elseif (Test-Path -LiteralPath $report -PathType Leaf) {
    Write-Host '[NUCLEAR-4F] Previous readiness report exists, but this run failed before producing a trustworthy fresh result.' -ForegroundColor Yellow
}

if ($code -ne 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4F] FULL-CONTENT READINESS: BLOCKED' -ForegroundColor Yellow
    Write-Host '[NUCLEAR-4F] This is expected until the remaining critical families are promoted.'
    Write-Host '[NUCLEAR-4F] Do NOT hardware-test the 4Z-D integrated candidate while this gate is blocked.'
    exit $code
}

Write-Host ''
Write-Host '[NUCLEAR-4F] FULL-CONTENT READINESS: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4F] Size and critical-content gates are closed. Full integrated emission may begin.'
