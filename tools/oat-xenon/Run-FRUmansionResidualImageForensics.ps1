param(
    [string]$ModCorpusManifest = '',
    [string]$NativeImageDumpRoot = '',
    [string]$NativeImageGraphRoot = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($ModCorpusManifest)) {
    $ModCorpusManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_mod_dependency_corpus\nuclear_4r_mod_dependency_corpus.json'
}
if ([string]::IsNullOrWhiteSpace($NativeImageDumpRoot)) {
    $NativeImageDumpRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4j_native_image_mass\dump'
}
if ([string]::IsNullOrWhiteSpace($NativeImageGraphRoot)) {
    $NativeImageGraphRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4n_xenon_image_graph\dump'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_residual_image_forensics'
}

if (-not (Test-Path -LiteralPath $ModCorpusManifest -PathType Leaf)) {
    throw "Mod dependency manifest not found: $ModCorpusManifest"
}

function Normalize-Identity {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }

    $v = $Value.Trim().Replace('\', '/').TrimStart('/')
    while ($v.StartsWith(',', [StringComparison]::Ordinal)) {
        $v = $v.Substring(1)
    }

    $marker = $v.ToLowerInvariant().LastIndexOf('/images/')
    if ($marker -ge 0) {
        $v = $v.Substring($marker + 8)
    }
    elseif ($v.StartsWith('images/', [StringComparison]::OrdinalIgnoreCase)) {
        $v = $v.Substring(7)
    }

    foreach ($ext in @('.iwi', '.dds')) {
        if ($v.EndsWith($ext, [StringComparison]::OrdinalIgnoreCase)) {
            $v = $v.Substring(0, $v.Length - $ext.Length)
            break
        }
    }

    if ($v.EndsWith('_wj', [StringComparison]::OrdinalIgnoreCase)) {
        $v = $v.Substring(0, $v.Length - 3)
    }

    return $v.ToLowerInvariant()
}

function Get-GeneratedKind {
    param([string]$Name)
    if ([string]::IsNullOrWhiteSpace($Name)) { return $null }

    $n = $Name.ToLowerInvariant()

    if ($n.StartsWith('*reflection_probe')) { return 'runtime-reflection-probe' }
    if ($n.StartsWith('*lightmap')) { return 'runtime-lightmap' }
    if ($n.StartsWith('*')) { return 'runtime-star-identity' }
    if ($n.StartsWith('$')) { return 'runtime-dollar-identity' }
    if ($n.StartsWith(',')) { return 'external-reference-identity' }

    return $null
}

$mod = Get-Content -LiteralPath $ModCorpusManifest -Raw | ConvertFrom-Json
if ([string]$mod.format -ne 'WaW360.Nuclear4R.ModDependencyCorpus.v1') {
    throw "Unexpected mod corpus format: $($mod.format)"
}

$remaining = @($mod.remaining)
if ($remaining.Count -eq 0) {
    Write-Host '[NUCLEAR-4R-RESIDUAL] No residual images remain after the dependency corpus.'
    exit 0
}

$nativeDumpIndex = @{}
$nativeDumpFiles = @()
if (Test-Path -LiteralPath $NativeImageDumpRoot -PathType Container) {
    $nativeDumpFiles = @(Get-ChildItem -LiteralPath $NativeImageDumpRoot -Filter '*.iwi' -File -Recurse -ErrorAction SilentlyContinue)
    foreach ($file in $nativeDumpFiles) {
        $key = Normalize-Identity -Value $file.Name
        if (-not [string]::IsNullOrWhiteSpace($key)) {
            if (-not $nativeDumpIndex.ContainsKey($key)) {
                $nativeDumpIndex[$key] = New-Object 'System.Collections.Generic.List[string]'
            }
            $nativeDumpIndex[$key].Add($file.FullName)
        }
    }
}

$nativeGraphIndex = @{}
$nativeGraphFiles = @()
$graphDir = Join-Path $NativeImageGraphRoot 'xenon_image_graphs'
if (Test-Path -LiteralPath $graphDir -PathType Container) {
    $nativeGraphFiles = @(Get-ChildItem -LiteralPath $graphDir -Filter '*.json' -File -Recurse -ErrorAction SilentlyContinue)
    foreach ($file in $nativeGraphFiles) {
        try {
            $doc = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
            if ([string]$doc.format -ne 'WaW360.T4Xenon.ImageGraph.v1') { continue }
            $key = Normalize-Identity -Value ([string]$doc.name)
            if ([string]::IsNullOrWhiteSpace($key)) { continue }

            if (-not $nativeGraphIndex.ContainsKey($key)) {
                $nativeGraphIndex[$key] = New-Object 'System.Collections.Generic.List[object]'
            }
            $nativeGraphIndex[$key].Add([pscustomobject]@{
                Name = [string]$doc.name
                GraphFile = $file.FullName
                CardMemoryBytes = [int64]$doc.cardMemoryBytes
                PixelsBytes = [int64]$doc.pixelsBytes
                Width = [int]$doc.width
                Height = [int]$doc.height
                Depth = [int]$doc.depth
                Streaming = [bool]$doc.streaming
                DelayLoadPixels = [bool]$doc.delayLoadPixels
            })
        }
        catch {}
    }
}

$rows = New-Object 'System.Collections.Generic.List[object]'

foreach ($item in ($remaining | Sort-Object { [int]$_.Index })) {
    $name = [string]$item.Name
    $key = Normalize-Identity -Value $name
    $generatedKind = Get-GeneratedKind -Name $name
    $nativeDumpHit = $nativeDumpIndex.ContainsKey($key)
    $nativeGraphHit = $nativeGraphIndex.ContainsKey($key)

    $classification = 'unresolved-ordinary'
    $confidence = 'low'
    $reason = 'Not found in retail Xbox, UGX, ZCT, or existing native-image evidence.'

    if ($null -ne $generatedKind) {
        $classification = $generatedKind
        $confidence = 'high'
        $reason = 'Identity uses an engine-generated/special naming convention.'
    }
    elseif ($nativeGraphHit) {
        $classification = 'native-inline-image-evidence'
        $confidence = 'high'
        $reason = 'A fixed-up Xenon image graph exists in the native validation corpus, indicating a real inline image payload in the native reference path.'
    }
    elseif ($nativeDumpHit) {
        $classification = 'native-inline-iwi-evidence'
        $confidence = 'medium'
        $reason = 'The native FRUmansion image mass dump contains this identity, indicating native payload evidence even though no fixed-up graph was located.'
    }

    $graphEvidence = @()
    if ($nativeGraphHit) {
        $graphEvidence = @($nativeGraphIndex[$key].ToArray())
    }

    $dumpEvidence = @()
    if ($nativeDumpHit) {
        $dumpEvidence = @($nativeDumpIndex[$key].ToArray())
    }

    $rows.Add([pscustomobject]@{
        Index = [int]$item.Index
        Name = $name
        Normalized = $key
        Classification = $classification
        Confidence = $confidence
        Reason = $reason
        NativeGraphHit = $nativeGraphHit
        NativeDumpHit = $nativeDumpHit
        NativeGraphs = $graphEvidence
        NativeDumpFiles = $dumpEvidence
    })
}

$rowArray = [object[]]$rows.ToArray()
$groups = @(
    $rowArray |
    Group-Object Classification |
    Sort-Object Count -Descending, Name |
    ForEach-Object {
        [pscustomobject]@{
            Classification = $_.Name
            Count = $_.Count
        }
    }
)

$generated = @($rowArray | Where-Object { $_.Classification -like 'runtime-*' -or $_.Classification -eq 'external-reference-identity' })
$nativeInline = @($rowArray | Where-Object { $_.Classification -in @('native-inline-image-evidence','native-inline-iwi-evidence') })
$ordinary = @($rowArray | Where-Object { $_.Classification -eq 'unresolved-ordinary' })

if (Test-Path -LiteralPath $OutputRoot) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4R.ResidualImageForensics.v1'
    sourceManifest = (Resolve-Path -LiteralPath $ModCorpusManifest).Path
    residualImages = $rowArray.Count
    generatedOrSpecial = $generated.Count
    nativeInlineEvidence = $nativeInline.Count
    unresolvedOrdinary = $ordinary.Count
    nativeDumpRoot = $NativeImageDumpRoot
    nativeDumpAvailable = (Test-Path -LiteralPath $NativeImageDumpRoot -PathType Container)
    nativeDumpImagesIndexed = $nativeDumpFiles.Count
    nativeGraphRoot = $NativeImageGraphRoot
    nativeGraphAvailable = (Test-Path -LiteralPath $graphDir -PathType Container)
    nativeGraphsIndexed = $nativeGraphFiles.Count
    classificationSummary = $groups
    images = $rowArray
    policy = @(
        'Native/oracle image payloads are evidence only and are never promoted as production source bytes by this forensic stage.',
        'Generated/special identities may be structurally valid without a source image file, but must still survive OAT and the 4F gate.',
        'Native-inline evidence identifies likely custom/inlined content that needs a legitimate production source or a separately proven reconstruction path.'
    )
}

$jsonPath = Join-Path $OutputRoot 'nuclear_4r_residual_image_forensics.json'
$txtPath = Join-Path $OutputRoot 'nuclear_4r_residual_image_forensics.txt'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4R-RESIDUAL - IMAGE FORENSICS')
$report.Add(('=' * 96))
$report.Add(("Residual images:                {0:N0}" -f $rowArray.Count))
$report.Add(("Generated/special:              {0:N0}" -f $generated.Count))
$report.Add(("Native-inline evidence:         {0:N0}" -f $nativeInline.Count))
$report.Add(("Still unresolved ordinary:      {0:N0}" -f $ordinary.Count))
$report.Add(("Native IWI dump available:      {0}" -f (Test-Path -LiteralPath $NativeImageDumpRoot -PathType Container)))
$report.Add(("Native IWI identities indexed:  {0:N0}" -f $nativeDumpFiles.Count))
$report.Add(("Native graph available:         {0}" -f (Test-Path -LiteralPath $graphDir -PathType Container)))
$report.Add(("Native image graphs indexed:    {0:N0}" -f $nativeGraphFiles.Count))
$report.Add('')
$report.Add('CLASSIFICATION')
foreach ($g in $groups) {
    $report.Add(("  {0,-34} {1,4:N0}" -f $g.Classification, $g.Count))
}
$report.Add('')
$report.Add('RESIDUAL IDENTITIES')
foreach ($row in $rowArray) {
    $report.Add(("  root #{0,4}: {1} | {2} | nativeGraph={3} nativeDump={4}" -f
        $row.Index, $row.Name, $row.Classification, $row.NativeGraphHit, $row.NativeDumpHit))
}

[IO.File]::WriteAllLines($txtPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host '[NUCLEAR-4R-RESIDUAL] ========================================================'
Write-Host '[NUCLEAR-4R-RESIDUAL] RESIDUAL IMAGE FORENSICS'
Write-Host '[NUCLEAR-4R-RESIDUAL] ========================================================'
Write-Host "[NUCLEAR-4R-RESIDUAL] Residual images           : $($rowArray.Count)"
Write-Host "[NUCLEAR-4R-RESIDUAL] Generated/special         : $($generated.Count)"
Write-Host "[NUCLEAR-4R-RESIDUAL] Native-inline evidence    : $($nativeInline.Count)"
Write-Host "[NUCLEAR-4R-RESIDUAL] Still unresolved ordinary : $($ordinary.Count)"
Write-Host "[NUCLEAR-4R-RESIDUAL] Native IWI indexed        : $($nativeDumpFiles.Count)"
Write-Host "[NUCLEAR-4R-RESIDUAL] Native graphs indexed     : $($nativeGraphFiles.Count)"
Write-Host ''
foreach ($g in $groups) {
    Write-Host ("[NUCLEAR-4R-RESIDUAL] {0,-34} {1,4}" -f $g.Classification, $g.Count)
}
Write-Host ''
Write-Host '[NUCLEAR-4R-RESIDUAL] Residual identities:'
foreach ($row in $rowArray) {
    Write-Host ("  root #{0,4}: {1} -> {2}" -f $row.Index, $row.Name, $row.Classification)
}
Write-Host ''
Write-Host "[NUCLEAR-4R-RESIDUAL] Manifest: $jsonPath"
Write-Host "[NUCLEAR-4R-RESIDUAL] Report  : $txtPath"
Write-Host '[NUCLEAR-4R-RESIDUAL] ========================================================'
