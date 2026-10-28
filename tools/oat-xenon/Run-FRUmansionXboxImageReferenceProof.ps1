param(
    [string]$RetailImageCensusManifest = '',
    [string]$RetailImageGraphManifest = '',
    [string]$XboxGameRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\XBOX GAME FILES',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$linker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Linker.exe'
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($RetailImageCensusManifest)) {
    $RetailImageCensusManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_xbox_retail_image_census\nuclear_4r_xbox_retail_image_census.json'
}
if ([string]::IsNullOrWhiteSpace($RetailImageGraphManifest)) {
    $RetailImageGraphManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_xbox_retail_image_graph\nuclear_4r_xbox_retail_image_graph.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4x_xbox_image_reference_proof'
}

foreach ($required in @($RetailImageCensusManifest, $RetailImageGraphManifest, $linker, $unlinker)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required file not found: $required"
    }
}
if (-not (Test-Path -LiteralPath $XboxGameRoot -PathType Container)) {
    throw "Xbox retail game-file folder not found: $XboxGameRoot"
}

function Normalize-ImageIdentity {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    $v = $Value.Trim().Trim('"')
    while ($v.StartsWith(',', [StringComparison]::Ordinal)) {
        $v = $v.Substring(1)
    }
    return $v.ToLowerInvariant()
}

function Get-OatReferenceSourceName {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    $v = $Value.Trim()
    while ($v.StartsWith(',', [StringComparison]::Ordinal)) {
        $v = $v.Substring(1)
    }
    return $v
}

function Strip-LogPrefix {
    param([string]$Line)
    $line = $Line.Trim()
    if ($line.StartsWith('[', [StringComparison]::Ordinal)) {
        $close = $line.IndexOf(']')
        if ($close -ge 0 -and $close + 1 -lt $line.Length) {
            $line = $line.Substring($close + 1).TrimStart()
        }
    }
    $colon = $line.IndexOf(':')
    if ($colon -gt 0 -and $colon -lt 12) {
        $prefix = $line.Substring(0, $colon)
        if ($prefix -match '^[A-Za-z ]+$') {
            $line = $line.Substring($colon + 1).TrimStart()
        }
    }
    return $line
}

function Invoke-NativeTee {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][object[]]$Arguments,
        [Parameter(Mandatory=$true)][string]$LogPath
    )
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $FilePath @Arguments 2>&1 |
            Tee-Object -FilePath $LogPath |
            ForEach-Object { Write-Host ([string]$_) }
        return [int]$LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

$census = Get-Content -LiteralPath $RetailImageCensusManifest -Raw | ConvertFrom-Json
$graph = Get-Content -LiteralPath $RetailImageGraphManifest -Raw | ConvertFrom-Json

if ([string]$census.format -ne 'WaW360.Nuclear4R.XboxRetailImageCensus.v1') {
    throw "Unexpected retail image census format: $($census.format)"
}
if ([string]$graph.format -ne 'WaW360.Nuclear4R.XboxRetailImageGraph.v1') {
    throw "Unexpected retail image graph format: $($graph.format)"
}

$unresolved = @($graph.unresolved)
if ($unresolved.Count -eq 0) {
    throw 'Retail image graph has zero unresolved stock identities; no image-reference proof is needed.'
}

$censusByIndex = @{}
foreach ($m in @($census.matches)) {
    $censusByIndex[[int]$m.Index] = $m
}

$candidates = New-Object 'System.Collections.Generic.List[object]'
$payloadRequired = New-Object 'System.Collections.Generic.List[object]'

foreach ($u in $unresolved) {
    $index = [int]$u.Index
    if (-not $censusByIndex.ContainsKey($index)) {
        throw "Unresolved retail image root #$index has no census match."
    }

    $m = $censusByIndex[$index]
    $bestClass = [string]$m.BestClass
    $row = [pscustomobject]@{
        Index = $index
        Name = [string]$u.Name
        BestClass = $bestClass
        Zones = [object[]]@($m.Zones)
    }

    if ($bestClass -in @('core-global-candidate','zombie-base-candidate')) {
        $candidates.Add($row)
    }
    else {
        $payloadRequired.Add($row)
    }
}

$baseNames = @(
    'common.ff',
    'code_post_gfx.ff',
    'ui.ff',
    'patch.ff',
    'nazi_zombie_prototype.ff',
    'nazi_zombie_prototype_load.ff'
)

$baseZones = New-Object 'System.Collections.Generic.List[string]'
foreach ($name in $baseNames) {
    $matches = @(Get-ChildItem -LiteralPath $XboxGameRoot -Filter $name -File -Recurse -ErrorAction SilentlyContinue)
    if ($matches.Count -gt 1) {
        throw "Multiple retail base zones named '$name' were found."
    }
    if ($matches.Count -eq 1) {
        $baseZones.Add($matches[0].FullName)
    }
}
if ($baseZones.Count -eq 0) {
    throw 'No retail common/base-zombie zones were found.'
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

$workspace = Join-Path $OutputRoot 'workspace'
$sourceRoot = Join-Path $workspace 'zone_source'
$assetRoot = Join-Path $workspace 'raw'
$linkOut = Join-Path $OutputRoot 'linked'
$logRoot = Join-Path $OutputRoot 'logs'
foreach ($path in @($sourceRoot, $assetRoot, $linkOut, $logRoot)) {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}

Write-Host '[NUCLEAR-4X-IMGREF] ========================================================='
Write-Host '[NUCLEAR-4X-IMGREF] RETAIL XBOX IMAGE RUNTIME-REFERENCE PROOF'
Write-Host '[NUCLEAR-4X-IMGREF] ========================================================='
Write-Host "[NUCLEAR-4X-IMGREF] Retail graph unresolved : $($unresolved.Count)"
Write-Host "[NUCLEAR-4X-IMGREF] Runtime-ref candidates  : $($candidates.Count)"
Write-Host "[NUCLEAR-4X-IMGREF] Payload-required stock  : $($payloadRequired.Count)"
foreach ($row in $candidates.ToArray()) {
    Write-Host ("[NUCLEAR-4X-IMGREF]   REF root #{0,4}: {1} [{2}]" -f $row.Index, $row.Name, $row.BestClass)
}
foreach ($row in $payloadRequired.ToArray()) {
    Write-Host ("[NUCLEAR-4X-IMGREF]   PAY root #{0,4}: {1} [{2}]" -f $row.Index, $row.Name, $row.BestClass)
}
Write-Host ''

$linkCode = -1
$unlinkCode = -1
$proofFf = ''
$listLog = Join-Path $logRoot 'reference_resolution.list.log'
$linkLog = Join-Path $logRoot 'linker.log'
$listedKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

if ($candidates.Count -gt 0) {
    $project = 'nuclear_4x_xbox_image_reference_proof'
    $zoneFile = Join-Path $sourceRoot ($project + '.zone')
    $zoneLines = New-Object 'System.Collections.Generic.List[string]'
    $zoneLines.Add('>game,T4')
    $zoneLines.Add('>platform,xbox360')
    $zoneLines.Add(('>name,{0}' -f $project))
    $zoneLines.Add('')

    foreach ($row in ($candidates.ToArray() | Sort-Object Index)) {
        $name = Get-OatReferenceSourceName -Value ([string]$row.Name)
        $escaped = $name.Replace('"', '""')
        $zoneLines.Add(('image,,"{0}"' -f $escaped))
    }

    [IO.File]::WriteAllLines($zoneFile, [string[]]$zoneLines.ToArray(), [Text.UTF8Encoding]::new($false))

    $linkArgs = New-Object 'System.Collections.Generic.List[object]'
    foreach ($arg in @(
        '--verbose',
        '--no-color',
        '--base-folder', $workspace,
        '--source-search-path', $sourceRoot,
        '--asset-search-path', $assetRoot,
        '--output-folder', $linkOut
    )) { $linkArgs.Add($arg) }
    foreach ($zone in $baseZones.ToArray()) {
        $linkArgs.Add('--load')
        $linkArgs.Add($zone)
    }
    $linkArgs.Add($project)

    Push-Location $workspace
    try {
        $linkCode = Invoke-NativeTee -FilePath $linker -Arguments ([object[]]$linkArgs.ToArray()) -LogPath $linkLog
    }
    finally {
        Pop-Location
    }

    if ($linkCode -eq 0) {
        $linked = @(Get-ChildItem -LiteralPath $linkOut -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue)
        if ($linked.Count -eq 1) {
            $proofFf = $linked[0].FullName

            $unlinkArgs = New-Object 'System.Collections.Generic.List[object]'
            $unlinkArgs.Add('--list')
            $unlinkArgs.Add('--no-color')
            foreach ($zone in $baseZones.ToArray()) {
                $unlinkArgs.Add('--load')
                $unlinkArgs.Add($zone)
            }
            $unlinkArgs.Add($proofFf)

            Push-Location (Split-Path -Parent $proofFf)
            try {
                $unlinkCode = Invoke-NativeTee -FilePath $unlinker -Arguments ([object[]]$unlinkArgs.ToArray()) -LogPath $listLog
            }
            finally {
                Pop-Location
            }
        }
    }

    if (Test-Path -LiteralPath $listLog -PathType Leaf) {
        foreach ($raw in @(Get-Content -LiteralPath $listLog)) {
            $line = Strip-LogPrefix -Line ([string]$raw)
            if (-not $line.StartsWith('image,', [StringComparison]::OrdinalIgnoreCase)) { continue }
            $name = Normalize-ImageIdentity -Value $line.Substring(6)
            if (-not [string]::IsNullOrWhiteSpace($name)) {
                [void]$listedKeys.Add($name)
            }
        }
    }
}
else {
    $linkCode = 0
    $unlinkCode = 0
}

$resolved = New-Object 'System.Collections.Generic.List[object]'
$missing = New-Object 'System.Collections.Generic.List[object]'

foreach ($row in ($candidates.ToArray() | Sort-Object Index)) {
    $key = Normalize-ImageIdentity -Value ([string]$row.Name)
    if ($listedKeys.Contains($key)) {
        $resolved.Add($row)
    }
    else {
        $missing.Add($row)
    }
}

$pass =
    $linkCode -eq 0 -and
    $unlinkCode -eq 0 -and
    $missing.Count -eq 0 -and
    $resolved.Count -eq $candidates.Count

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4X.XboxImageRuntimeReferenceProof.v1'
    retailImageCensus = (Resolve-Path -LiteralPath $RetailImageCensusManifest).Path
    retailImageGraph = (Resolve-Path -LiteralPath $RetailImageGraphManifest).Path
    retailGraphUnresolved = $unresolved.Count
    runtimeReferenceCandidates = $candidates.Count
    payloadRequiredStockImages = $payloadRequired.Count
    linkerExitCode = $linkCode
    unlinkerExitCode = $unlinkCode
    resolvedReferences = $resolved.Count
    missingReferences = $missing.Count
    referenceProofPass = $pass
    proofFastFile = $proofFf
    resolved = [object[]]$resolved.ToArray()
    missing = [object[]]$missing.ToArray()
    payloadRequired = [object[]]$payloadRequired.ToArray()
    hardwareApproved = $false
    policy = @(
        'Only unresolved stock images whose best retail class is core-global or zombie-base are eligible for runtime-reference credit.',
        'Campaign, MP, DLC-only and other retail identities remain payload-required unless their runtime residency is separately proven.',
        'Leading comma reference markers are normalized before OAT zone reference syntax is emitted.',
        'Stage 4F must independently validate exact native index/type/name before granting production credit.'
    )
}

$jsonPath = Join-Path $OutputRoot 'nuclear_4x_xbox_image_reference_proof.json'
$txtPath = Join-Path $OutputRoot 'nuclear_4x_xbox_image_reference_proof.txt'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4X-XBOX - IMAGE RUNTIME REFERENCE PROOF')
$report.Add(('=' * 100))
$report.Add(("Retail graph unresolved:       {0:N0}" -f $unresolved.Count))
$report.Add(("Runtime-reference candidates:   {0:N0}" -f $candidates.Count))
$report.Add(("Payload-required stock images:  {0:N0}" -f $payloadRequired.Count))
$report.Add(("Linker exit:                    {0}" -f $linkCode))
$report.Add(("Unlinker exit:                  {0}" -f $unlinkCode))
$report.Add(("Resolved references:            {0:N0}" -f $resolved.Count))
$report.Add(("Missing references:             {0:N0}" -f $missing.Count))
$report.Add(("REFERENCE PROOF PASS:            {0}" -f $pass))
$report.Add('')
$report.Add('RESOLVED')
foreach ($row in $resolved.ToArray()) {
    $report.Add(("  root #{0,4}: {1} [{2}]" -f $row.Index, $row.Name, $row.BestClass))
}
$report.Add('')
$report.Add('PAYLOAD REQUIRED')
foreach ($row in $payloadRequired.ToArray()) {
    $report.Add(("  root #{0,4}: {1} [{2}]" -f $row.Index, $row.Name, $row.BestClass))
}
[IO.File]::WriteAllLines($txtPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4X-IMGREF] ====================== RESULT ============================'
Write-Host "[NUCLEAR-4X-IMGREF] Retail graph unresolved : $($unresolved.Count)"
Write-Host "[NUCLEAR-4X-IMGREF] Runtime-ref candidates  : $($candidates.Count)"
Write-Host "[NUCLEAR-4X-IMGREF] Payload-required stock  : $($payloadRequired.Count)"
Write-Host "[NUCLEAR-4X-IMGREF] Linker exit             : $linkCode"
Write-Host "[NUCLEAR-4X-IMGREF] Unlinker exit           : $unlinkCode"
Write-Host "[NUCLEAR-4X-IMGREF] Resolved references     : $($resolved.Count)"
Write-Host "[NUCLEAR-4X-IMGREF] Missing references      : $($missing.Count)"
Write-Host "[NUCLEAR-4X-IMGREF] Reference proof pass    : $pass"
Write-Host "[NUCLEAR-4X-IMGREF] Manifest                : $jsonPath"
Write-Host "[NUCLEAR-4X-IMGREF] Report                  : $txtPath"
Write-Host '[NUCLEAR-4X-IMGREF] ================================================================='

if (-not $pass) {
    exit 4
}
