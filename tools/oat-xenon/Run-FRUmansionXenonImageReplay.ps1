param(
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$graphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4n_xenon_image_graph\dump'
$referenceRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4l_xenon_image_roundtrip\linked'

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4o_image_serializer_replay'
}

if (-not (Test-Path -LiteralPath $graphRoot -PathType Container)) {
    throw "Stage 4N image graph dump not found: $graphRoot"
}

$reference = @(Get-ChildItem -LiteralPath $referenceRoot -Filter '*.ff' -File -Recurse)
if ($reference.Count -ne 1) {
    throw "Expected exactly one Stage 4L linked FF under '$referenceRoot', found $($reference.Count)."
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$cliProject = Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'

Write-Host '[NUCLEAR-4O] ================================================================'
Write-Host '[NUCLEAR-4O] C# XENON IMAGE SERIALIZER REPLAY'
Write-Host '[NUCLEAR-4O] ================================================================'
Write-Host "[NUCLEAR-4O] Graph root : $graphRoot"
Write-Host "[NUCLEAR-4O] Reference  : $($reference[0].FullName)"
Write-Host "[NUCLEAR-4O] Output     : $OutputRoot"
Write-Host ''
Write-Host '[NUCLEAR-4O] Required OAT reference block vector:'
Write-Host '  TEMP=56 RUNTIME=0 LARGE_RUNTIME=35438592 PHYSICAL_RUNTIME=0 VIRTUAL=38008 LARGE=0 PHYSICAL=0'
Write-Host ''

$cliArgs = @(
    'run',
    '--project', $cliProject,
    '-c', 'Release',
    '--',
    'xenon-image-replay',
    $graphRoot,
    $reference[0].FullName,
    $OutputRoot,
    '',
    '378'
)

& dotnet @cliArgs
$code = $LASTEXITCODE

if ($code -ne 0) {
    Write-Host ''
    Write-Host "[NUCLEAR-4O] Replay did not close (exit $code)." -ForegroundColor Yellow
    Write-Host '[NUCLEAR-4O] This is a serializer/layout proof failure, not a hardware candidate.'
    exit $code
}

Write-Host ''
Write-Host '[NUCLEAR-4O] ================================================================'
Write-Host '[NUCLEAR-4O] C# IMAGE SERIALIZER REPLAY: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4O] The converter can reproduce the OAT image-only Xenon block layout.'
Write-Host '[NUCLEAR-4O] Next: graft this exact image serializer into the 6,811-root full-map emitter.'
