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
$bridgeOut = Join-Path $OutputRoot "nuclear_material_techset_bridge"

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

    Write-Host "[NUCLEAR] Candidate main FFs in $Directory" -ForegroundColor Yellow
    $candidates | ForEach-Object { Write-Host "  $($_.Name)" -ForegroundColor Yellow }
    throw "Could not safely identify one main fastfile in: $Directory"
}

$pcMapDir = Join-Path $PcMapsRoot $MapName
$pcMain = Find-MainFastFile -Directory $pcMapDir -PreferredNames @(
    ($MapName + ".ff"),
    "nazi_zombie_frumansion.ff"
)

$nativeMain = Find-MainFastFile -Directory $NativeControlDirectory -PreferredNames @(
    "frumansion_j_p.ff"
)

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "WaW360.Cli project not found: $project"
}
if (-not (Test-Path -LiteralPath $UnlinkerExe -PathType Leaf)) {
    throw "OAT Xenon Unlinker not found: $UnlinkerExe"
}

Write-Host "[NUCLEAR] ================================================================"
Write-Host "[NUCLEAR] PC -> XENON MATERIAL / TECHNIQUESET BRIDGE"
Write-Host "[NUCLEAR] ================================================================"
Write-Host "[NUCLEAR] PC main      : $pcMain"
Write-Host "[NUCLEAR] Native oracle: $nativeMain"
Write-Host "[NUCLEAR] OAT          : $UnlinkerExe"
Write-Host "[NUCLEAR] Output       : $bridgeOut"
Write-Host ""
Write-Host "[NUCLEAR] Strategy: match the SAME material on PC and Xbox, then learn"
Write-Host "[NUCLEAR]           PC TechniqueSet -> native Xenon TechniqueSet."
Write-Host "[NUCLEAR]           No PC shader bytecode is copied."
Write-Host ""

Push-Location $repoRoot
try {
    dotnet run --project $project -c Release -- material-techset-bridge $pcMain $nativeMain $bridgeOut $UnlinkerExe
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($exitCode -ne 0) {
    throw "Nuclear material/TechniqueSet bridge failed with exit code $exitCode."
}

$report = Join-Path $bridgeOut "material_techset_bridge.txt"
$manifest = Join-Path $bridgeOut "material_techset_bridge.json"

Write-Host ""
Write-Host "[NUCLEAR] BRIDGE PASS" -ForegroundColor Green
Write-Host "[NUCLEAR] Report  : $report"
Write-Host "[NUCLEAR] Manifest: $manifest"
Write-Host ""
Write-Host "[NUCLEAR] This result is the training oracle for the production Xenon"
Write-Host "[NUCLEAR] TechniqueSet remapper. It is NOT a donor FF to copy to Xbox."
