param(
    [string]$PcMapsRoot = "C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps",
    [string]$MapName = "nazi_zombie_FRUmansion",
    [string]$OutputRoot = "",
    [string]$UnlinkerExe = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "output"
}

if ([string]::IsNullOrWhiteSpace($UnlinkerExe)) {
    $UnlinkerExe = Join-Path $repoRoot ".deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe"
}

$project = Join-Path $repoRoot "src\WaW360.Cli\WaW360.Cli.csproj"
$packageDir = Join-Path (Join-Path $OutputRoot $MapName) "hardware_test_main_xanim_native_layout_real_techset_names"

Write-Host "[TECHSET-A/B] ================================================"
Write-Host "[TECHSET-A/B] REAL TECHNIQUESET NAME ISOLATION"
Write-Host "[TECHSET-A/B] ================================================"
Write-Host "[TECHSET-A/B] Map       : $MapName"
Write-Host "[TECHSET-A/B] PC root   : $PcMapsRoot"
Write-Host "[TECHSET-A/B] Output    : $OutputRoot"
Write-Host ""

if (-not (Test-Path -LiteralPath $PcMapsRoot -PathType Container)) {
    throw "PC Maps root not found: $PcMapsRoot"
}
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "WaW360.Cli project not found: $project"
}
if (-not (Test-Path -LiteralPath $UnlinkerExe -PathType Leaf)) {
    throw "OAT Xenon Unlinker not found: $UnlinkerExe"
}

Push-Location $repoRoot
try {
    dotnet run --project $project -c Release -- linker-v2-main-structural $PcMapsRoot $MapName $OutputRoot

    if ($LASTEXITCODE -ne 0) {
        throw "Structural emitter failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $packageDir -PathType Container)) {
    throw "Real-TechniqueSet-name package directory was not emitted: $packageDir"
}

$candidates = @(Get-ChildItem -LiteralPath $packageDir -Filter "*.ff" -File)
if ($candidates.Count -ne 1) {
    $names = ($candidates | ForEach-Object { $_.FullName }) -join [Environment]::NewLine
    throw "Expected exactly one A/B fastfile in '$packageDir'; found $($candidates.Count).$([Environment]::NewLine)$names"
}

$candidate = $candidates[0].FullName
$hash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
Write-Host ""
Write-Host "[TECHSET-A/B] Candidate : $candidate"
Write-Host "[TECHSET-A/B] Bytes     : $((Get-Item -LiteralPath $candidate).Length)"
Write-Host "[TECHSET-A/B] SHA256    : $hash"
Write-Host ""
Write-Host "[TECHSET-A/B] OAT HARD GATE..."

$oatOutput = & $UnlinkerExe --list --no-color $candidate 2>&1
$oatExit = $LASTEXITCODE
$oatOutput | ForEach-Object { Write-Host $_ }

Write-Host ""
if ($oatExit -eq 0) {
    Write-Host "[TECHSET-A/B] OAT HARD GATE: PASS" -ForegroundColor Green
    Write-Host "[TECHSET-A/B] READY FOR XBOX:"
    Write-Host "  $candidate"
    Write-Host ""
    Write-Host "[TECHSET-A/B] Hardware interpretation:"
    Write-Host "  - reaches CLIPMAP / DB_LoadXAssets RETURN => TechniqueSet identity strings were a real blocker"
    Write-Host "  - same ~2,045/type-8 abort => real names alone are insufficient; proceed to Xbox donor graph materialization"
    exit 0
}

Write-Host "[TECHSET-A/B] OAT HARD GATE: FAIL (exit=$oatExit)" -ForegroundColor Red
Write-Host "[TECHSET-A/B] DO NOT COPY THIS CANDIDATE TO XBOX."
exit $oatExit
