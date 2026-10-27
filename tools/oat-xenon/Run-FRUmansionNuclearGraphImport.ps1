param(
    [string]$GraphRoot = ''
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($GraphRoot)) {
    $GraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_oat_graph_dump'
}

Write-Host '[NUCLEAR-4A] ================================================================'
Write-Host '[NUCLEAR-4A] TYPED XENON GRAPH IMPORT + BLOB VALIDATION'
Write-Host '[NUCLEAR-4A] ================================================================'
Write-Host "[NUCLEAR-4A] Graph root: $GraphRoot"
Write-Host ''

Push-Location $repo
try {
    dotnet run --project '.\src\WaW360.Cli\WaW360.Cli.csproj' -c Release -- xenon-graph-import "$GraphRoot" 87 897
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($code -ne 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4A] IMPORT FAILED.' -ForegroundColor Yellow
    exit $code
}

Write-Host ''
Write-Host '[NUCLEAR-4A] TYPED GRAPH IMPORT: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4A] Stage 4B serializer frontier is open.'
