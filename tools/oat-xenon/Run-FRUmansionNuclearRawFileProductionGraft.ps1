[CmdletBinding()]
param(
    [string]$RepoRoot = '',
    [string]$OutputRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $RepoRoot 'output\nazi_zombie_FRUmansion\nuclear_5r_rawfile_production_graft'
}

$base = Join-Path $RepoRoot 'output\nazi_zombie_FRUmansion'
$fiveO = Join-Path $base 'nuclear_5o_eight_root_canary'
$csv = Join-Path $fiveO 'nuclear_5o_results.csv'
$staged = Join-Path $fiveO 'workspace\raw'
$previousReadback = Join-Path $fiveO 'readback'
$nativeMain = 'C:\Users\jorda\OneDrive\Desktop\working frumansion\nazi_zombie_FRUmansion.ff'
$identity = Join-Path $base 'nuclear_xenon_root_binding\native_identity\nazi_zombie_FRUmansion_native_identity_manifest.json'
$binding = Join-Path $base 'nuclear_xenon_root_binding\nuclear_xenon_root_binding.json'
$rendererGraph = Join-Path $base 'nuclear_oat_graph_dump'
$imageManifest = Join-Path $base 'nuclear_4t_production_image_graph\nuclear_4t_production_image_graph.json'
$xmodelGraph = Join-Path $base 'nuclear_4xb_xmodel_relocatable_graph'
$xmodelBinding = Join-Path $base 'nuclear_4w_xmodel_root_binding\nuclear_4w_xmodel_root_binding.json'
$materialGraph = Join-Path $base 'nuclear_4zb_material_relocatable_graph'
$materialBinding = Join-Path $base 'nuclear_4zd_material_root_binding\nuclear_4zd_material_root_binding.json'
$graphUnlinker = Join-Path $RepoRoot '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$rawUnlinker = Join-Path $RepoRoot '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'

$expected = [ordered]@{
    6362 = 'maps/frumansion_j_p.gsc'
    6363 = 'maps/nazi_zombie_frumansion_nozct_fx.gsc'
    6364 = 'maps/createfx/nazi_zombie_frumansion_nozct_fx.gsc'
    6365 = 'clientscripts/frumansion_j_p.csc'
    6366 = 'clientscripts/nazi_zombie_frumansion_nozct_fx.csc'
    6367 = 'clientscripts/createfx/nazi_zombie_frumansion_nozct_fx.csc'
    6808 = 'nazi_zombie_frumansion_nozct'
    6809 = 'shock/zombie_death.shock'
}

foreach ($required in @($csv,$nativeMain,$identity,$binding,$rendererGraph,$imageManifest,
    $xmodelGraph,$xmodelBinding,$materialGraph,$materialBinding,$graphUnlinker,$rawUnlinker)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing 5R input: $required" }
}

$images = Get-Content -LiteralPath $imageManifest -Raw | ConvertFrom-Json
if ([string]$images.format -ne 'WaW360.Nuclear4T.ProductionImageGraph.v1' -or
    -not [bool]$images.mergePass -or [int]$images.imageGraphs -le 0 -or
    -not (Test-Path -LiteralPath ([string]$images.graphRoot) -PathType Container)) {
    throw 'Production image graph is not verified.'
}

$rows = @(Import-Csv -LiteralPath $csv)
if ($rows.Count -ne 8) { throw "5O requires exactly eight rows, got $($rows.Count)." }

$entries = @()
# OrderedDictionary treats numeric [key] as positional access; use the entry
# directly so native root #6362 is never mistaken for collection index 6362.
foreach ($pair in $expected.GetEnumerator()) {
    $index = [int]$pair.Key
    $name = [string]$pair.Value
    $matches = @($rows | Where-Object { $_.Asset -ieq $name })
    if ($matches.Count -ne 1) { throw "Missing/duplicate 5O identity: $name" }
    $row = $matches[0]
    if ($row.Exact -ne 'True') { throw "5O row did not pass: $name" }
    $relative = $name.Replace('/','\')
    $source = Join-Path $staged $relative
    $oldReadback = Join-Path $previousReadback $relative
    foreach ($file in @($source,$oldReadback)) {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
            throw "Missing source/readback: $file"
        }
    }
    $sourceSha = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    $readSha = (Get-FileHash -LiteralPath $oldReadback -Algorithm SHA256).Hash
    $bytes = (Get-Item -LiteralPath $source).Length
    if ($sourceSha -ne $readSha -or
        $sourceSha -ne $row.SourceSHA256 -or
        $readSha -ne $row.ReadbackSHA256 -or
        $bytes -ne [int64]$row.Bytes) {
        throw "5O source/readback proof changed: $name"
    }
    $entries += [ordered]@{
        rootIndex = [int]$index
        name = $name
        sourcePath = (Resolve-Path -LiteralPath $source).Path
        bytes = $bytes
        sha256 = $sourceSha
    }
}

# Prevent accidental grafting of a donor/oracle body under a production identity.
if ($entries.Count -ne 8) { throw 'Incomplete eight-root proof.' }

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$graftInput = Join-Path $OutputRoot 'nuclear_5r_rawfile_graft_input.json'
([ordered]@{
    format = 'WaW360.Nuclear5R.RawFileGraftInput.v1'
    sourceStage = 'nuclear_5o_eight_root_canary'
    entries = $entries
} | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $graftInput -Encoding UTF8

Write-Host '[NUCLEAR-5R] ============================================================'
Write-Host '[NUCLEAR-5R] 6,811-ROOT PC RAWFILE PRODUCTION GRAFT'
Write-Host '[NUCLEAR-5R] Source: eight exact PC-derived/ZCT RawFiles from 5O'
Write-Host '[NUCLEAR-5R] Policy: no donor/oracle bytes and no hardware candidate'
Write-Host '[NUCLEAR-5R] ============================================================'

$cliArgs = @(
    'run', '--project', '.\src\WaW360.Cli\WaW360.Cli.csproj',
    '-c', 'Release', '--', 'xenon-graph-emit',
    $rendererGraph, $nativeMain, $identity, $binding,
    $OutputRoot, 'nazi_zombie_FRUmansion', $graphUnlinker,
    '87', '897', [string]$images.graphRoot, [string]$images.imageGraphs,
    'true', '0', $xmodelGraph, $xmodelBinding,
    $materialGraph, $materialBinding, $graftInput
)

Push-Location $RepoRoot
try {
    & dotnet @cliArgs
    $emitExit = $LASTEXITCODE
}
finally { Pop-Location }

$emitterPath = Join-Path $OutputRoot 'nuclear_xenon_renderer_emit.json'
if ($emitExit -ne 0 -or -not (Test-Path -LiteralPath $emitterPath -PathType Leaf)) {
    throw "Integrated RawFile emitter failed (exit=$emitExit). Check the OAT gate logs."
}
$emitter = Get-Content -LiteralPath $emitterPath -Raw | ConvertFrom-Json
if (-not [bool]$emitter.ValidationPass -or
    -not [bool]$emitter.LayoutReplayExact -or
    -not [bool]$emitter.ExactIdentitySequence -or
    -not [bool]$emitter.OatZeroWarningsZeroErrors -or
    [int]$emitter.NativeRoots -ne 6811 -or
    [int]$emitter.OatListedRoots -ne 6811 -or
    [int]$emitter.RealRawFileRoots -ne 8 -or
    [int]$emitter.RealMaterialRoots -ne 502 -or
    [int]$emitter.RealXModelRoots -ne 280 -or
    [int]$emitter.RealImageRoots -ne [int]$images.imageGraphs) {
    throw '5R full-graph OAT/root/layout/production-family validation failed.'
}

$producedFf = [string]$emitter.OutputFastFile
if (-not (Test-Path -LiteralPath $producedFf -PathType Leaf)) {
    throw "5R output FF missing: $producedFf"
}

$dump = Join-Path $OutputRoot 'rawfile_readback'
$dumpLog = Join-Path $OutputRoot 'rawfile_readback.log'
New-Item -ItemType Directory -Path $dump -Force | Out-Null
$oldPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    $lines = @(& $rawUnlinker '--no-color' '--include-assets' 'rawfile' '--output-folder' $dump $producedFf 2>&1 |
        ForEach-Object { [string]$_ })
    $dumpExit = [int]$LASTEXITCODE
}
finally { $ErrorActionPreference = $oldPreference }
[IO.File]::WriteAllLines($dumpLog,[string[]]$lines,[Text.UTF8Encoding]::new($false))
if ($dumpExit -ne 0) { throw "5R integrated rawfile redump failed (exit=$dumpExit). Log: $dumpLog" }

$checks = @()
foreach ($entry in $entries) {
    $readFile = Join-Path $dump (([string]$entry.name).Replace('/','\'))
    $found = Test-Path -LiteralPath $readFile -PathType Leaf
    $readSha = if ($found) { (Get-FileHash -LiteralPath $readFile -Algorithm SHA256).Hash } else { '' }
    $readBytes = if ($found) { (Get-Item -LiteralPath $readFile).Length } else { -1 }
    $exact = $found -and $readSha -eq [string]$entry.sha256 -and $readBytes -eq [int64]$entry.bytes
    $checks += [pscustomobject]@{
        RootIndex = [int]$entry.rootIndex
        Name = [string]$entry.name
        Bytes = [int64]$entry.bytes
        SHA256 = [string]$entry.sha256
        ReadbackSHA256 = $readSha
        Exact = $exact
    }
    Write-Host "[NUCLEAR-5R] Root #$($entry.rootIndex): $(if ($exact) { 'EXACT PASS' } else { 'FAIL' })"
}

$matched = @($checks | Where-Object { $_.Exact }).Count
$pass = ($matched -eq 8)
$summary = [ordered]@{
    format = 'WaW360.Nuclear5R.RawFileProductionGraft.v1'
    productionRawFileGraftPass = $pass
    pcBackedRawFileRoots = $matched
    nativeRoots = [int]$emitter.NativeRoots
    oatListedRoots = [int]$emitter.OatListedRoots
    oatZeroWarningsZeroErrors = [bool]$emitter.OatZeroWarningsZeroErrors
    exactIdentitySequence = [bool]$emitter.ExactIdentitySequence
    layoutReplayExact = [bool]$emitter.LayoutReplayExact
    integratedOutputFastFile = $producedFf
    integratedZoneBytes = [int64]$emitter.ZoneBytes
    productionRawFiles = $checks
    rawFileInputManifest = $graftInput
    hardwareApproved = $false
    note = 'Only the eight proven RawFiles were grafted; unresolved world/FX/weapon/image families still require the independent 4F readiness gate.'
}

$summaryPath = Join-Path $OutputRoot 'nuclear_5r_rawfile_production_graft.json'
$reportPath = Join-Path $OutputRoot 'nuclear_5r_rawfile_production_graft.txt'
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
@(
    "NUCLEAR-5R 6,811-root RawFile graft",
    "Integrated 6811-root OAT gate: $([bool]$emitter.ValidationPass)",
    "Native identity sequence: $([bool]$emitter.ExactIdentitySequence)",
    "Layout replay exact: $([bool]$emitter.LayoutReplayExact)",
    "Readback verified: $matched / 8",
    "Production image roots: $([int]$emitter.RealImageRoots)",
    "Production XModel roots: $([int]$emitter.RealXModelRoots)",
    "Production Material roots: $([int]$emitter.RealMaterialRoots)",
    "Output FF: $producedFf",
    "HARDWARE APPROVED: FALSE - 4F gate remains mandatory.",
    "5R PASS: $pass"
) | Set-Content -LiteralPath $reportPath -Encoding UTF8

Write-Host "[NUCLEAR-5R] Verified RawFiles: $matched / 8"
Write-Host "[NUCLEAR-5R] Manifest: $summaryPath"
Write-Host "[NUCLEAR-5R] FF: $producedFf"
if (-not $pass) { throw '5R full-graph RawFile redump mismatch; NOT FOR XBOX.' }
Write-Host '[NUCLEAR-5R] FULL-GRAPH RAWFILE GRAFT: PASS (NOT FOR XBOX YET)' -ForegroundColor Green
