param(
    [string]$InputFastFile = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$builder = Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1'
$graphUnlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($InputFastFile)) {
    $linkedRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4l_xenon_image_roundtrip\linked'
    $candidates = @(Get-ChildItem -LiteralPath $linkedRoot -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue)
    if ($candidates.Count -ne 1) {
        throw "Expected exactly one Stage 4L linked FF under '$linkedRoot', found $($candidates.Count)."
    }
    $InputFastFile = $candidates[0].FullName
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4n_xenon_image_graph'
}

if (-not (Test-Path -LiteralPath $InputFastFile -PathType Leaf)) {
    throw "Stage 4N input FF not found: $InputFastFile"
}
if (-not (Test-Path -LiteralPath $builder -PathType Leaf)) {
    throw "Specialized OAT builder not found: $builder"
}

Write-Host '[NUCLEAR-4N] ================================================================'
Write-Host '[NUCLEAR-4N] FIXED-UP XENON IMAGE GRAPH EXTRACTION'
Write-Host '[NUCLEAR-4N] ================================================================'
Write-Host "[NUCLEAR-4N] Input FF : $InputFastFile"
Write-Host "[NUCLEAR-4N] FF bytes : $((Get-Item -LiteralPath $InputFastFile).Length)"
Write-Host ''
Write-Host '[NUCLEAR-4N] Rebuilding specialized OAT Unlinker with image graph export...'
Write-Host ''

& $builder
if ($LASTEXITCODE -ne 0 -or -not $?) {
    throw 'Stage 4N specialized OAT graph-dumper build failed.'
}

if (-not (Test-Path -LiteralPath $graphUnlinker -PathType Leaf)) {
    throw "Specialized OAT Unlinker was not produced: $graphUnlinker"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$dumpRoot = Join-Path $OutputRoot 'dump'
$log = Join-Path $OutputRoot 'oat_image_graph_dump.log'

Write-Host ''
Write-Host '[NUCLEAR-4N] Dumping fixed-up image objects, texture headers, and tiled pixel blocks...'
Write-Host ''

Push-Location (Split-Path -Parent $InputFastFile)
try {
    $args = @(
        '--verbose',
        '--no-color',
        '--include-assets', 'image',
        '--image-format', 'IWI',
        '--output-folder', $dumpRoot,
        $InputFastFile
    )

    & $graphUnlinker @args 2>&1 | Tee-Object -FilePath $log
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($code -ne 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4N] GRAPH DUMP FAILED. Log tail:' -ForegroundColor Yellow
    Get-Content -LiteralPath $log -Tail 120
    exit $code
}

$graphs = @(Get-ChildItem -LiteralPath $dumpRoot -Filter '*.json' -File -Recurse |
    Where-Object { $_.FullName -match '[\\/]xenon_image_graphs[\\/]' })
$pixels = @(Get-ChildItem -LiteralPath $dumpRoot -Filter '*.pixels.bin' -File -Recurse |
    Where-Object { $_.FullName -match '[\\/]xenon_image_pixels[\\/]' })
$headers = @(Get-ChildItem -LiteralPath $dumpRoot -Filter '*.d3dtexture.bin' -File -Recurse |
    Where-Object { $_.FullName -match '[\\/]xenon_image_headers[\\/]' })

$errors = New-Object 'System.Collections.Generic.List[string]'
$records = New-Object 'System.Collections.Generic.List[object]'
$totalCardMemory = [int64]0
$totalPixelFileBytes = [int64]0
$delayed = 0
$immediate = 0
$streaming = 0
$cubes = 0

foreach ($graphFile in ($graphs | Sort-Object FullName)) {
    $g = Get-Content -LiteralPath $graphFile.FullName -Raw | ConvertFrom-Json

    if ([string]$g.format -ne 'WaW360.T4Xenon.ImageGraph.v1') {
        $errors.Add("Unexpected graph format in $($graphFile.FullName)")
        continue
    }

    $cardBytes = [int64]$g.cardMemoryBytes
    $pixelBytes = [int64]$g.pixelsBytes
    $totalCardMemory += $cardBytes

    if ([bool]$g.delayLoadPixels) { $delayed++ } else { $immediate++ }
    if ([bool]$g.streaming) { $streaming++ }
    if ([int]$g.mapType -eq 5) { $cubes++ }

    if ($null -eq $g.loadDef) {
        $errors.Add("Image '$($g.name)' has no loadDef.")
        continue
    }

    if ([int]$g.loadDef.textureHeaderBytes -ne 52) {
        $errors.Add("Image '$($g.name)' texture header is $($g.loadDef.textureHeaderBytes) bytes; expected 52.")
    }

    $pixelFile = $null
    if (-not [string]::IsNullOrWhiteSpace([string]$g.pixelsFile)) {
        $pixelFile = Join-Path $dumpRoot ([string]$g.pixelsFile).Replace('/', '\')
        if (-not (Test-Path -LiteralPath $pixelFile -PathType Leaf)) {
            $errors.Add("Image '$($g.name)' pixel blob is missing: $pixelFile")
        }
        else {
            $actual = [int64](Get-Item -LiteralPath $pixelFile).Length
            $totalPixelFileBytes += $actual
            if ($actual -ne $pixelBytes -or $actual -ne $cardBytes) {
                $errors.Add("Image '$($g.name)' pixel size mismatch: file=$actual graph=$pixelBytes cardMemory=$cardBytes")
            }
        }
    }
    elseif ($cardBytes -ne 0) {
        $errors.Add("Image '$($g.name)' has cardMemory=$cardBytes but no pixel file.")
    }

    $headerFile = $null
    if (-not [string]::IsNullOrWhiteSpace([string]$g.loadDef.textureHeaderFile)) {
        $headerFile = Join-Path $dumpRoot ([string]$g.loadDef.textureHeaderFile).Replace('/', '\')
        if (-not (Test-Path -LiteralPath $headerFile -PathType Leaf)) {
            $errors.Add("Image '$($g.name)' texture header file is missing: $headerFile")
        }
        elseif ((Get-Item -LiteralPath $headerFile).Length -ne 52) {
            $errors.Add("Image '$($g.name)' texture header file is not 52 bytes.")
        }
    }
    else {
        $errors.Add("Image '$($g.name)' has no texture header file.")
    }

    $records.Add([pscustomobject]@{
        Name = [string]$g.name
        MapType = [int]$g.mapType
        Semantic = [int]$g.semantic
        Category = [int]$g.category
        Width = [int]$g.width
        Height = [int]$g.height
        Depth = [int]$g.depth
        DelayLoadPixels = [bool]$g.delayLoadPixels
        Streaming = [bool]$g.streaming
        CardMemoryBytes = $cardBytes
        BaseSize = [uint32]$g.baseSize
        StreamSlot = [int]$g.streamSlot
        LevelCount = [int]$g.loadDef.levelCount
        LoadFlags = [int]$g.loadDef.flags
        D3DFormat = [int]$g.loadDef.format
        PixelFile = [string]$g.pixelsFile
        TextureHeaderFile = [string]$g.loadDef.textureHeaderFile
    })
}

$recordsArray = [object[]]$records.ToArray()
$errorsArray = [string[]]$errors.ToArray()

$expected = 378
if ($graphs.Count -ne $expected) {
    $errors.Add("Expected $expected fixed-up image graphs from Stage 4L; found $($graphs.Count).")
}
if ($pixels.Count -ne $expected) {
    $errors.Add("Expected $expected pixel blobs from Stage 4L; found $($pixels.Count).")
}
if ($headers.Count -ne $expected) {
    $errors.Add("Expected $expected D3D texture headers from Stage 4L; found $($headers.Count).")
}
if ($totalCardMemory -ne $totalPixelFileBytes) {
    $errors.Add("Aggregate pixel mismatch: cardMemory=$totalCardMemory fileBytes=$totalPixelFileBytes")
}

$errorsArray = [string[]]$errors.ToArray()
$pass = $errorsArray.Count -eq 0

$manifestPath = Join-Path $OutputRoot 'nuclear_4n_xenon_image_graph.json'
$manifest = [ordered]@{
    format = 'WaW360.Nuclear4N.XenonImageGraph.v1'
    inputFastFile = $InputFastFile
    inputFastFileBytes = [int64](Get-Item -LiteralPath $InputFastFile).Length
    graphCount = $graphs.Count
    pixelBlobCount = $pixels.Count
    textureHeaderCount = $headers.Count
    cardMemoryBytes = $totalCardMemory
    pixelBlobBytes = $totalPixelFileBytes
    delayedImages = $delayed
    immediateImages = $immediate
    streamingImages = $streaming
    cubeImages = $cubes
    graphVerified = $pass
    errors = $errorsArray
    records = $recordsArray
    notes = @(
        'Stage 4N extracts fixed-up OAT-produced Xenon GfxImage graphs from the Stage 4L encoder proof fastfile.',
        'The graph records root metadata, GfxImageLoadDef fields, the raw 52-byte Xenos D3DBaseTexture header, and the tiled/endian-adjusted pixel block.',
        'Oracle-derived image inputs remain validation vectors only. Production payloads must come from user-owned PC IWI/DDS sources.',
        'XMAs remain external under sounds\ and are outside this graph.'
    )
}

$manifest | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host ''
Write-Host '[NUCLEAR-4N] ================= IMAGE GRAPH RESULT ============================'
Write-Host "[NUCLEAR-4N] Image graphs       : $($graphs.Count) / $expected"
Write-Host "[NUCLEAR-4N] Pixel blobs        : $($pixels.Count) / $expected"
Write-Host "[NUCLEAR-4N] Texture headers    : $($headers.Count) / $expected"
Write-Host "[NUCLEAR-4N] Xenon cardMemory   : $totalCardMemory bytes"
Write-Host "[NUCLEAR-4N] Pixel file bytes   : $totalPixelFileBytes bytes"
Write-Host "[NUCLEAR-4N] Delayed / immediate: $delayed / $immediate"
Write-Host "[NUCLEAR-4N] Streaming images   : $streaming"
Write-Host "[NUCLEAR-4N] Cube images        : $cubes"
Write-Host "[NUCLEAR-4N] Manifest           : $manifestPath"
Write-Host '[NUCLEAR-4N] ================================================================='

if (-not $pass) {
    Write-Host '[NUCLEAR-4N] IMAGE GRAPH EXTRACTION: FAILED' -ForegroundColor Yellow
    foreach ($error in $errorsArray | Select-Object -First 40) {
        Write-Host "  $error"
    }
    exit 5
}

Write-Host '[NUCLEAR-4N] IMAGE GRAPH EXTRACTION: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4N] Fixed-up OAT image serialization is now available as block-aware graph input for the converter emitter.'
Write-Host '[NUCLEAR-4N] Next: import these graphs in C# and replace image shells with real TEMP/VIRTUAL/LARGE_RUNTIME image serialization.'
