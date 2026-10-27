param(
    [string]$Candidate = '',
    [int]$TailLines = 160
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($Candidate)) {
    $Candidate = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_renderer_emit\nazi_zombie_FRUmansion.ff'
}

if (-not (Test-Path -LiteralPath $Candidate -PathType Leaf)) {
    throw "Stage 4E candidate not found: $Candidate"
}
if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) {
    throw "Xenon OAT Unlinker not found: $unlinker"
}

$outDir = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_renderer_emit\oat_failtrace'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$log = Join-Path $outDir 'oat_verbose_failtrace.log'

Write-Host '[NUCLEAR-4E-TRACE] =========================================================='
Write-Host '[NUCLEAR-4E-TRACE] VERBOSE OAT LOADER FAIL TRACE'
Write-Host '[NUCLEAR-4E-TRACE] =========================================================='
Write-Host "[NUCLEAR-4E-TRACE] Candidate: $Candidate"
Write-Host "[NUCLEAR-4E-TRACE] Unlinker : $unlinker"
Write-Host "[NUCLEAR-4E-TRACE] Log      : $log"
Write-Host ''

Push-Location (Split-Path -Parent $Candidate)
try {
    & $unlinker --list --verbose --no-color $Candidate 2>&1 |
        Tee-Object -FilePath $log
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

Write-Host ''
Write-Host '[NUCLEAR-4E-TRACE] ================= LAST LOG LINES ========================='
Get-Content -LiteralPath $log -Tail $TailLines
Write-Host '[NUCLEAR-4E-TRACE] =========================================================='
Write-Host "[NUCLEAR-4E-TRACE] OAT exit code: $code"

if ($code -eq 0) {
    Write-Host '[NUCLEAR-4E-TRACE] Unexpectedly PASSed under verbose mode.' -ForegroundColor Green
    exit 0
}

Write-Host '[NUCLEAR-4E-TRACE] Failure captured. The tail above is the loader evidence we need.' -ForegroundColor Yellow
exit $code
