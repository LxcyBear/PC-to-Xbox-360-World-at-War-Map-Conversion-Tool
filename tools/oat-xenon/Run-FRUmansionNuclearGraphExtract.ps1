param(
    [string]$NativeControlDirectory = "C:\Users\jorda\OneDrive\Desktop\working frumansion",
    [string]$OutputRoot = "",
    [string]$UnlinkerExe = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$project = Join-Path $repoRoot "src\WaW360.Cli\WaW360.Cli.csproj"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "output\nazi_zombie_FRUmansion\nuclear_xenon_techset_bodies"
}

if ([string]::IsNullOrWhiteSpace($UnlinkerExe)) {
    $UnlinkerExe = Join-Path $repoRoot ".deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe"
}

function Find-MainFastFile {
    param([Parameter(Mandatory=$true)][string]$Directory)

    foreach ($name in @("frumansion_j_p.ff", "nazi_zombie_FRUmansion.ff", "nazi_zombie_frumansion.ff")) {
        $candidate = Join-Path $Directory $name
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

    throw "Could not identify one native FRUmansion main fastfile in: $Directory"
}

$nativeMain = Find-MainFastFile -Directory $NativeControlDirectory

Write-Host "[NUCLEAR-3A] ================================================================"
Write-Host "[NUCLEAR-3A] XENON TECHNIQUESET BODY + 51-SLOT POINTER CENSUS"
Write-Host "[NUCLEAR-3A] ================================================================"
Write-Host "[NUCLEAR-3A] Native oracle: $nativeMain"
Write-Host "[NUCLEAR-3A] Output       : $OutputRoot"
Write-Host ""

Push-Location $repoRoot
try {
    dotnet run --project $project -c Release -- xenon-techset-bodies $nativeMain $OutputRoot $UnlinkerExe
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

$report = Join-Path $OutputRoot "xenon_techset_body_catalog.txt"

if (Test-Path -LiteralPath $report -PathType Leaf) {
    Write-Host ""
    Write-Host "[NUCLEAR-3A] CENSUS:"
    Select-String -LiteralPath $report -Pattern @(
        "^expected TechniqueSets",
        "^recovered TechniqueSets",
        "^VERIFIED",
        "^root ownership",
        "^technique slots"
    ) | ForEach-Object { Write-Host "  $($_.Line)" }
}

if ($exitCode -ne 0) {
    Write-Host ""
    Write-Host "[NUCLEAR-3A] Body catalog is incomplete; do NOT advance to graph extraction." -ForegroundColor Yellow
    if (Test-Path -LiteralPath $report -PathType Leaf) {
        Write-Host "[NUCLEAR-3A] Report: $report"
    }
    exit $exitCode
}

Write-Host ""
Write-Host "[NUCLEAR-3A] 107/107 BODY FRONTIER: OPEN" -ForegroundColor Green
Write-Host "[NUCLEAR-3A] Next: walk MaterialTechnique -> MaterialPass -> shaders from these exact pointers."
