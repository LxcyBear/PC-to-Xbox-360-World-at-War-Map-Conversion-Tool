param(
    [string]$NativeControlDirectory = 'C:\Users\jorda\OneDrive\Desktop\working frumansion',
    [string]$GraphRoot = '',
    [string]$BindingRoot = '',
    [string]$ImageGraphRoot = '',
    [string]$OutputRoot = '',
    [string]$CanonicalBaseName = 'nazi_zombie_FRUmansion'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$graphUnlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($GraphRoot)) {
    $GraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_oat_graph_dump'
}
if ([string]::IsNullOrWhiteSpace($BindingRoot)) {
    $BindingRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_root_binding'
}
if ([string]::IsNullOrWhiteSpace($ImageGraphRoot)) {
    $ImageGraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4n_xenon_image_graph\dump'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4q_image_graft_bisect'
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

    if ($candidates.Count -eq 1) { return $candidates[0].FullName }
    throw "Could not identify one native FRUmansion main fastfile in: $Directory"
}

$nativeMain = Find-MainFastFile -Directory $NativeControlDirectory
$bindingManifest = Join-Path $BindingRoot 'nuclear_xenon_root_binding.json'
$identityDir = Join-Path $BindingRoot 'native_identity'
$identityManifest = @(Get-ChildItem -LiteralPath $identityDir -Filter '*_native_identity_manifest.json' -File |
    Sort-Object FullName |
    Select-Object -First 1)

if ($identityManifest.Count -ne 1) {
    throw "Could not identify the native identity manifest under: $identityDir"
}
if (-not (Test-Path -LiteralPath $ImageGraphRoot -PathType Container)) {
    throw "Missing Stage 4N image graph root: $ImageGraphRoot"
}
if (-not (Test-Path -LiteralPath $graphUnlinker -PathType Leaf)) {
    throw "Missing specialized Xenon OAT Unlinker: $graphUnlinker"
}

$imageManifest = Join-Path (Split-Path -Parent $ImageGraphRoot) 'nuclear_4n_xenon_image_graph.json'
if (-not (Test-Path -LiteralPath $imageManifest -PathType Leaf)) {
    throw "Missing Stage 4N image graph manifest: $imageManifest"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$identity = Get-Content -LiteralPath $identityManifest[0].FullName -Raw | ConvertFrom-Json
$imageData = Get-Content -LiteralPath $imageManifest -Raw | ConvertFrom-Json

$imageNames = @{}
foreach ($r in @($imageData.records)) {
    $imageNames[[string]$r.Name] = $true
}

$graftOrder = New-Object 'System.Collections.Generic.List[object]'
foreach ($entry in @($identity.entries)) {
    if ([string]$entry.OatTypeName -ieq 'image' -and $imageNames.ContainsKey([string]$entry.Name)) {
        $graftOrder.Add([pscustomobject]@{
            Ordinal = $graftOrder.Count + 1
            RootIndex = [int]$entry.Index
            Name = [string]$entry.Name
        })
    }
}

if ($graftOrder.Count -ne 378) {
    throw "Expected 378 Stage 4N image identities in native root order; found $($graftOrder.Count)."
}

$probeRows = New-Object 'System.Collections.Generic.List[object]'

function Invoke-Probe {
    param([Parameter(Mandatory=$true)][int]$Limit)

    $probeDir = Join-Path $OutputRoot ("limit_{0:D3}" -f $Limit)
    New-Item -ItemType Directory -Force -Path $probeDir | Out-Null

    Write-Host ''
    Write-Host ("[NUCLEAR-4Q] Probe {0,3}/378 ..." -f $Limit) -ForegroundColor Cyan

    $args = @(
        'run',
        '--project', '.\src\WaW360.Cli\WaW360.Cli.csproj',
        '-c', 'Release',
        '--',
        'xenon-graph-emit',
        $GraphRoot,
        $nativeMain,
        $identityManifest[0].FullName,
        $bindingManifest,
        $probeDir,
        $CanonicalBaseName,
        $graphUnlinker,
        '87',
        '897',
        $ImageGraphRoot,
        '378',
        'true',
        [string]$Limit
    )

    Push-Location $repo
    try {
        $dotnetOutput = @(& dotnet @args 2>&1)
        $exitCode = $LASTEXITCODE
        foreach ($line in $dotnetOutput) {
            Write-Host ([string]$line)
        }
    }
    finally {
        Pop-Location
    }

    $reportPath = Join-Path $probeDir 'nuclear_xenon_renderer_emit.json'
    $pass = $false
    $largeRuntime = [int64]0
    $realImages = 0
    $oatRoots = 0
    $oatError = ''

    if (Test-Path -LiteralPath $reportPath -PathType Leaf) {
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $pass = [bool]$report.ValidationPass
        $largeRuntime = [int64]$report.LargeRuntimeBytes
        $realImages = [int]$report.RealImageRoots
        $oatRoots = [int]$report.OatListedRoots
        $oatError = [string]$report.OatGateError
    }

    $row = [pscustomobject]@{
        Limit = $Limit
        ExitCode = $exitCode
        Pass = $pass
        RealImages = $realImages
        LargeRuntimeBytes = $largeRuntime
        OatListedRoots = $oatRoots
        OatError = $oatError
        OutputDirectory = $probeDir
    }
    $probeRows.Add($row)

    if ($pass) {
        Write-Host ("[NUCLEAR-4Q] PASS limit={0}; realImages={1}; LARGE_RUNTIME={2:N0}; OAT roots={3:N0}" -f
            $Limit, $realImages, $largeRuntime, $oatRoots) -ForegroundColor Green
    }
    else {
        Write-Host ("[NUCLEAR-4Q] FAIL limit={0}; exit={1}; realImages={2}; LARGE_RUNTIME={3:N0}; OAT roots={4:N0}" -f
            $Limit, $exitCode, $realImages, $largeRuntime, $oatRoots) -ForegroundColor Yellow
        if (-not [string]::IsNullOrWhiteSpace($oatError)) {
            Write-Host "[NUCLEAR-4Q] OAT: $oatError"
        }
    }

    return $pass
}

Write-Host '[NUCLEAR-4Q] ================================================================'
Write-Host '[NUCLEAR-4Q] FULL-SEQUENCE IMAGE GRAFT CRASH BISECT'
Write-Host '[NUCLEAR-4Q] ================================================================'
Write-Host '[NUCLEAR-4Q] 4O proves the image serializer in isolation.'
Write-Host '[NUCLEAR-4Q] 4P crashes OAT only after combining images with the 6,811-root graph.'
Write-Host '[NUCLEAR-4Q] This run finds the first graft-count boundary that changes PASS -> FAIL.'
Write-Host '[NUCLEAR-4Q] No candidate from this stage is hardware-approved.'
Write-Host ''

$knownPass = 0
$knownFail = 378

# Sparse probes find a useful bracket quickly. Stop as soon as a failing bound appears.
foreach ($limit in @(1, 8, 32, 128, 256, 378)) {
    $pass = Invoke-Probe -Limit $limit
    if ($pass) {
        $knownPass = $limit
        continue
    }

    $knownFail = $limit
    break
}

if ($knownFail -eq 378 -and $knownPass -eq 378) {
    Write-Host ''
    Write-Host '[NUCLEAR-4Q] Unexpectedly, all 378 images passed on this run.' -ForegroundColor Green
}
elseif ($knownFail -gt $knownPass + 1) {
    while ($knownFail -gt $knownPass + 1) {
        $mid = [int][Math]::Floor(($knownPass + $knownFail) / 2.0)
        if (Invoke-Probe -Limit $mid) {
            $knownPass = $mid
        }
        else {
            $knownFail = $mid
        }
    }
}

$allPassed = $knownPass -eq 378
$firstFail = $null
$firstFailingLimit = $null
if (-not $allPassed -and $knownFail -le 378 -and $knownFail -gt 0) {
    $firstFailingLimit = $knownFail
    $firstFail = $graftOrder[$knownFail - 1]
}

$summary = [ordered]@{
    format = 'WaW360.Nuclear4Q.ImageGraftBisect.v2'
    allImagesPassed = $allPassed
    knownPassingLimit = $knownPass
    firstFailingLimit = $firstFailingLimit
    firstFailingImage = $firstFail
    probes = [object[]]$probeRows.ToArray()
}

$summaryPath = Join-Path $OutputRoot 'nuclear_4q_image_graft_bisect.json'
$summary | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host ''
Write-Host '[NUCLEAR-4Q] ======================== BISECT RESULT =========================='
Write-Host "[NUCLEAR-4Q] Highest passing graft count: $knownPass"

if ($allPassed) {
    Write-Host '[NUCLEAR-4Q] First failing graft count   : none' -ForegroundColor Green
    Write-Host '[NUCLEAR-4Q] RESULT: ALL 378 IMAGE GRAFTS PASS' -ForegroundColor Green
}
else {
    Write-Host "[NUCLEAR-4Q] First failing graft count   : $firstFailingLimit"
}

if ($null -ne $firstFail) {
    Write-Host ("[NUCLEAR-4Q] First failing image candidate: ordinal={0}; root=#{1}; name={2}" -f
        $firstFail.Ordinal, $firstFail.RootIndex, $firstFail.Name) -ForegroundColor Yellow
}

Write-Host "[NUCLEAR-4Q] Summary: $summaryPath"
Write-Host '[NUCLEAR-4Q] ================================================================='
Write-Host '[NUCLEAR-4Q] Do NOT test any 4Q FF on Xbox; these remain oracle validation probes.'
