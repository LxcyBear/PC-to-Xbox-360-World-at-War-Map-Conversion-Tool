param(
    [string]$NativeControlDirectory = 'C:\Users\jorda\OneDrive\Desktop\working frumansion',
    [string]$GraphRoot = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($GraphRoot)) {
    $GraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_oat_graph_dump'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_root_binding'
}

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

if (-not (Test-Path -LiteralPath $GraphRoot -PathType Container)) {
    throw "Missing Stage 3B fixed-up graph dump: $GraphRoot"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

Write-Host '[NUCLEAR-4C] ================================================================'
Write-Host '[NUCLEAR-4C] XENON GRAPH -> EXACT NATIVE ROOT BINDING'
Write-Host '[NUCLEAR-4C] ================================================================'
Write-Host "[NUCLEAR-4C] Graph root    : $GraphRoot"
Write-Host "[NUCLEAR-4C] Native oracle : $nativeMain"
Write-Host "[NUCLEAR-4C] Output        : $OutputRoot"
Write-Host ''
Write-Host '[NUCLEAR-4C] Contract: identity/order evidence only; NO donor payload bytes.'
Write-Host '[NUCLEAR-4C] Proving 87 TechniqueSet graphs + 897 PixelShaders bind to exact native roots.'
Write-Host ''

Push-Location $repo
try {
    dotnet run --project '.\src\WaW360.Cli\WaW360.Cli.csproj' -c Release -- xenon-graph-bind "$GraphRoot" "$nativeMain" "$OutputRoot" 87 897
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($code -ne 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4C] ROOT BINDING FAILED.' -ForegroundColor Yellow
    Write-Host '[NUCLEAR-4C] Do not start pointer allocation/emission until this gate closes.'
    exit $code
}

Write-Host ''
Write-Host '[NUCLEAR-4C] ROOT BINDING CLOSURE: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4C] Every renderer asset reference now has an exact native XAsset destination.'
Write-Host '[NUCLEAR-4C] Next frontier: OAT-style VIRTUAL insert-slot + TEMP/PHYSICAL cursor emission.'
