param(
    [string]$GraphRoot = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($GraphRoot)) {
    $GraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_oat_graph_dump'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_graph_plan'
}

Write-Host '[NUCLEAR-4B] ================================================================'
Write-Host '[NUCLEAR-4B] XENON GRAPH SERIALIZATION + DEDUPE PLAN'
Write-Host '[NUCLEAR-4B] ================================================================'
Write-Host "[NUCLEAR-4B] Graph root : $GraphRoot"
Write-Host "[NUCLEAR-4B] Output     : $OutputRoot"
Write-Host ''

Push-Location $repo
try {
    dotnet run --project '.\src\WaW360.Cli\WaW360.Cli.csproj' -c Release -- xenon-graph-plan "$GraphRoot" "$OutputRoot" 87 897
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($code -ne 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4B] SERIALIZATION PLAN FAILED.' -ForegroundColor Yellow
    exit $code
}

Write-Host ''
Write-Host '[NUCLEAR-4B] SERIALIZATION PLAN: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4B] Canonical graph emitter frontier is open.'
