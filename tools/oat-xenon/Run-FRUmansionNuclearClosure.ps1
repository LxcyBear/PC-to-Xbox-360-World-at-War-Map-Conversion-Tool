param(
    [string]$PcMapsRoot = "C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps",
    [string]$MapName = "nazi_zombie_FRUmansion",
    [string]$NativeControlDirectory = "C:\Users\jorda\OneDrive\Desktop\working frumansion",
    [string]$OutputRoot = "",
    [string]$UnlinkerExe = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$project = Join-Path $repoRoot "src\WaW360.Cli\WaW360.Cli.csproj"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path (Join-Path $repoRoot "output") $MapName
}
$closureOut = Join-Path $OutputRoot "nuclear_techset_closure"

if ([string]::IsNullOrWhiteSpace($UnlinkerExe)) {
    $UnlinkerExe = Join-Path $repoRoot ".deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe"
}

function Find-MainFastFile {
    param(
        [Parameter(Mandatory=$true)][string]$Directory,
        [string[]]$PreferredNames = @()
    )

    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        throw "Fastfile source directory not found: $Directory"
    }

    foreach ($preferred in $PreferredNames) {
        $candidate = Join-Path $Directory $preferred
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Get-Item -LiteralPath $candidate).FullName
        }
    }

    $candidates = @(
        Get-ChildItem -LiteralPath $Directory -Filter "*.ff" -File |
            Where-Object {
                $_.Name -notmatch '(?i)_load[.]ff$' -and
                $_.Name -notmatch '(?i)_patch[.]ff$'
            }
    )

    if ($candidates.Count -eq 1) {
        return $candidates[0].FullName
    }

    $fru = @($candidates | Where-Object { $_.Name -match '(?i)fru|mansion' })
    if ($fru.Count -eq 1) {
        return $fru[0].FullName
    }

    Write-Host "[NUCLEAR-2] Candidate main FFs in $Directory" -ForegroundColor Yellow
    $candidates | ForEach-Object { Write-Host "  $($_.Name)" -ForegroundColor Yellow }
    throw "Could not safely identify one main fastfile in: $Directory"
}

$pcMapDir = Join-Path $PcMapsRoot $MapName
$pcMain = Find-MainFastFile -Directory $pcMapDir -PreferredNames @(
    ($MapName + ".ff"),
    "nazi_zombie_frumansion.ff"
)

$nativeMain = Find-MainFastFile -Directory $NativeControlDirectory -PreferredNames @(
    "frumansion_j_p.ff",
    "nazi_zombie_FRUmansion.ff",
    "nazi_zombie_frumansion.ff"
)

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "WaW360.Cli project not found: $project"
}
if (-not (Test-Path -LiteralPath $UnlinkerExe -PathType Leaf)) {
    throw "OAT Xenon Unlinker not found: $UnlinkerExe"
}

Write-Host "[NUCLEAR-2] ================================================================"
Write-Host "[NUCLEAR-2] FULL TECHNIQUESET ROOT -> XENON DONOR CLOSURE"
Write-Host "[NUCLEAR-2] ================================================================"
Write-Host "[NUCLEAR-2] PC main      : $pcMain"
Write-Host "[NUCLEAR-2] Native oracle: $nativeMain"
Write-Host "[NUCLEAR-2] Output       : $closureOut"
Write-Host ""
Write-Host "[NUCLEAR-2] Goal: bind all physical PC TechniqueSet roots to concrete"
Write-Host "[NUCLEAR-2]       native Xenon TechniqueSet graphs in the known-good map."
Write-Host ""

Push-Location $repoRoot
try {
    dotnet run --project $project -c Release -- nuclear-techset-closure-plan $pcMain $nativeMain $closureOut $UnlinkerExe
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

$report = Join-Path $closureOut "nuclear_techset_closure_plan.txt"
$manifest = Join-Path $closureOut "nuclear_techset_closure_plan.json"

if (Test-Path -LiteralPath $report -PathType Leaf) {
    Write-Host ""
    Write-Host "[NUCLEAR-2] CENSUS:"
    Select-String -LiteralPath $report -Pattern @(
        "^PC TechniqueSet roots",
        "^material-bridge resolved",
        "^exact-name fallback resolved",
        "^DONOR-BOUND TOTAL",
        "^ambiguous bridge",
        "^mapped target missing donor",
        "^UNRESOLVED TOTAL",
        "^distinct Xenon graph targets",
        "^Xenon technique slots",
        "^identityClosureComplete",
        "^graphExtractionReady"
    ) | ForEach-Object { Write-Host "  $($_.Line)" }
}

if ($exitCode -ne 0) {
    Write-Host ""
    Write-Host "[NUCLEAR-2] Closure is partial, not failed." -ForegroundColor Yellow
    Write-Host "[NUCLEAR-2] The report now contains the exact unresolved remainder."
    Write-Host "[NUCLEAR-2] Report: $report"
    exit $exitCode
}

Write-Host ""
Write-Host "[NUCLEAR-2] GRAPH EXTRACTION FRONTIER: OPEN" -ForegroundColor Green
Write-Host "[NUCLEAR-2] Report  : $report"
Write-Host "[NUCLEAR-2] Manifest: $manifest"
