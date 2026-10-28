[CmdletBinding()]
param(
    [string]$CorpusRoot = '',
    [string]$OutputRoot = '',
    [switch]$ForceRebuildGraphDumper
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$buildGraphDumper = Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1'
$graphBin = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86'
$unlinker = Join-Path $graphBin 'Unlinker.exe'
$capabilityFile = Join-Path $graphBin 'waw360-graph-dumper-capabilities.json'

if ([string]::IsNullOrWhiteSpace($CorpusRoot)) {
    $CorpusRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4z_material_corpus'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4zb_material_relocatable_graph'
}

$corpusManifest = Join-Path $CorpusRoot 'nuclear_4za_material_corpus.json'
if (-not (Test-Path -LiteralPath $corpusManifest -PathType Leaf)) {
    throw "4Z-B requires the passing 4Z-A material corpus: $corpusManifest"
}

$corpus = Get-Content -LiteralPath $corpusManifest -Raw | ConvertFrom-Json
if ([string]$corpus.format -ne 'WaW360.Nuclear4ZA.MaterialCorpus.v1' -or
    -not [bool]$corpus.corpusPass -or
    -not [bool]$corpus.oatListZeroWarningsZeroErrors -or
    -not [bool]$corpus.oatRedumpZeroWarningsZeroErrors -or
    -not [bool]$corpus.exactMaterialIdentitySet) {
    throw '4Z-B requires a passing 4Z-A material corpus manifest.'
}

$needBuild = $ForceRebuildGraphDumper.IsPresent
if (-not $needBuild) {
    if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf) -or
        -not (Test-Path -LiteralPath $capabilityFile -PathType Leaf)) {
        $needBuild = $true
    }
    else {
        try {
            $cap = Get-Content -LiteralPath $capabilityFile -Raw | ConvertFrom-Json
            $caps = @($cap.capabilities)
            if ($caps -notcontains 't4xenon-material-relocatable-graph-v1') {
                $needBuild = $true
            }
        }
        catch {
            $needBuild = $true
        }
    }
}

if ($needBuild) {
    Write-Host '[NUCLEAR-4Z-B] Specialized OAT lacks Material graph support; rebuilding once...'
    & powershell -ExecutionPolicy Bypass -File $buildGraphDumper
    if ($LASTEXITCODE -ne 0 -or -not $?) {
        throw "Specialized OAT graph-dumper build failed with exit code $LASTEXITCODE."
    }
}

foreach ($required in @($unlinker, $capabilityFile)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "4Z-B graph-dumper dependency is missing: $required"
    }
}

$fastFile = [string]$corpus.linkedFastFile
if (-not (Test-Path -LiteralPath $fastFile -PathType Leaf)) {
    throw "4Z-B material corpus FF is missing: $fastFile"
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$log = Join-Path $OutputRoot 'material_graph_dump.log'

$previous = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    & $unlinker '--no-color' '--include-assets' 'material' '--output-folder' $OutputRoot $fastFile 2>&1 |
        Tee-Object -FilePath $log |
        ForEach-Object { Write-Host ([string]$_) }
    $exitCode = [int]$LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previous
}

if ($exitCode -ne 0) {
    throw "4Z-B specialized OAT dump failed with exit $exitCode. See: $log"
}

$zero = @(Get-Content -LiteralPath $log -ErrorAction SilentlyContinue | Where-Object {
    ([string]$_).Trim() -match '(?i)^Finished with\s+0\s+warnings?,\s+0\s+errors?\s*$'
}).Count -gt 0
if (-not $zero) {
    throw "4Z-B dump did not prove OAT 0 warnings / 0 errors. See: $log"
}

$graphRoot = Join-Path $OutputRoot 'xenon_material_graphs'
if (-not (Test-Path -LiteralPath $graphRoot -PathType Container)) {
    throw "4Z-B produced no xenon_material_graphs directory: $OutputRoot"
}

$graphs = @(Get-ChildItem -LiteralPath $graphRoot -Filter '*.json' -File -Recurse | Sort-Object FullName)
$expected = [int]$corpus.materials
if ($graphs.Count -ne $expected) {
    throw "4Z-B graph count $($graphs.Count) != 4Z-A material count $expected."
}

$rows = New-Object System.Collections.Generic.List[object]
$names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$aliasGraphs = 0
$aliasCount = 0
$waterTextures = 0
$waterSamples = [long]0
$invalidWater = 0
$totalTextures = 0
$totalConstants = 0
$totalStateBits = 0
$techniqueRefs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$imageRefs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$abiShapes = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

foreach ($file in $graphs) {
    $g = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if ([string]$g.format -ne 'WaW360.T4Xenon.MaterialRelocatableGraph.v1') {
        throw "Unexpected Material graph format in $($file.FullName): $([string]$g.format)"
    }

    $name = [string]$g.name
    if (-not $names.Add($name)) {
        throw "Duplicate Material graph identity: $name"
    }

    $aliases = [int]$g.reusable.aliasCount
    if ($aliases -gt 0) {
        $aliasGraphs++
        $aliasCount += $aliases
    }

    $invalid = [int]$g.inventory.invalidWaterDimensions
    if ($invalid -ne 0) {
        $invalidWater += $invalid
    }

    $waterTextures += [int]$g.inventory.waterTextures
    $waterSamples += [long]$g.inventory.waterSamples
    $totalTextures += [int]$g.counts.textures
    $totalConstants += [int]$g.counts.constants
    $totalStateBits += [int]$g.counts.stateBits

    $tech = [string]$g.techniqueSet
    if (-not [string]::IsNullOrWhiteSpace($tech)) {
        [void]$techniqueRefs.Add($tech)
    }

    foreach ($t in @($g.textures)) {
        if ($null -eq $t) { continue }
        $image = [string]$t.image
        if (-not [string]::IsNullOrWhiteSpace($image)) {
            [void]$imageRefs.Add($image)
        }
    }

    $shape = ('{0}/{1}/{2}/{3}/{4}/{5}/{6}/{7}/{8}/{9}' -f
        [int]$g.abi.material,
        [int]$g.abi.textureDef,
        [int]$g.abi.constantDef,
        [int]$g.abi.stateBits,
        [int]$g.abi.water,
        [int]$g.alignments.material,
        [int]$g.alignments.textureDef,
        [int]$g.alignments.constantDef,
        [int]$g.alignments.stateBits,
        [int]$g.alignments.water)
    [void]$abiShapes.Add($shape)

    $rows.Add([pscustomobject]@{
        Name = $name
        Graph = $file.FullName
        Textures = [int]$g.counts.textures
        Constants = [int]$g.counts.constants
        StateBits = [int]$g.counts.stateBits
        WaterTextures = [int]$g.inventory.waterTextures
        WaterSamples = [long]$g.inventory.waterSamples
        ReusableAliases = $aliases
        TechniqueSet = $tech
    })
}

if ($invalidWater -ne 0) {
    throw "4Z-B found $invalidWater invalid Xenon water dimension record(s)."
}
if ($abiShapes.Count -ne 1) {
    throw "4Z-B observed $($abiShapes.Count) different Material ABI/alignment shapes; expected one pinned T4 Xenon ABI."
}

$sourceNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($row in @($corpus.materialRows)) {
    [void]$sourceNames.Add([string]$row.Name)
}
$missing = @($sourceNames | Where-Object { -not $names.Contains($_) } | Sort-Object)
$extra = @($names | Where-Object { -not $sourceNames.Contains($_) } | Sort-Object)
if ($missing.Count -ne 0 -or $extra.Count -ne 0) {
    throw ("4Z-B graph identity mismatch: missing=[{0}], extra=[{1}]" -f
        (($missing | Select-Object -First 12) -join ', '),
        (($extra | Select-Object -First 12) -join ', '))
}

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4ZB.MaterialRelocatableGraph.v1'
    corpusManifest = $corpusManifest
    fastFile = $fastFile
    materials = $graphs.Count
    exactIdentitySet = $true
    oatZeroWarningsZeroErrors = $true
    abiShapeCount = $abiShapes.Count
    abiShape = @($abiShapes)[0]
    totalTextureRows = $totalTextures
    totalConstantRows = $totalConstants
    totalStateBitsRows = $totalStateBits
    waterTextures = $waterTextures
    waterSamples = $waterSamples
    invalidWaterDimensions = $invalidWater
    reusableAliasGraphs = $aliasGraphs
    reusableAliasCount = $aliasCount
    distinctTechniqueSetReferences = $techniqueRefs.Count
    distinctImageReferences = $imageRefs.Count
    graphPass = $true
    rows = @($rows)
    log = $log
}

$manifestPath = Join-Path $OutputRoot 'nuclear_4zb_material_relocatable_graph.json'
$reportPath = Join-Path $OutputRoot 'nuclear_4zb_material_relocatable_graph.txt'
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$report = @(
    'NUCLEAR STAGE 4Z-B - XENON MATERIAL RELOCATABLE GRAPH'
    '=========================================================================='
    "materials=$($graphs.Count)"
    'exactIdentitySet=True'
    'oatZeroWarningsZeroErrors=True'
    "abiShape=$(@($abiShapes)[0])"
    "textureRows=$totalTextures"
    "constantRows=$totalConstants"
    "stateBitsRows=$totalStateBits"
    "waterTextures=$waterTextures"
    "waterSamples=$waterSamples"
    "invalidWaterDimensions=$invalidWater"
    "reusableAliasGraphs=$aliasGraphs"
    "reusableAliasCount=$aliasCount"
    "distinctTechniqueSetReferences=$($techniqueRefs.Count)"
    "distinctImageReferences=$($imageRefs.Count)"
    'graphPass=True'
)
[IO.File]::WriteAllLines($reportPath, [string[]]$report, [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4Z-B] ================= MATERIAL GRAPH RESULT ======================'
Write-Host "[NUCLEAR-4Z-B] Materials        : $($graphs.Count)"
Write-Host "[NUCLEAR-4Z-B] Texture rows     : $totalTextures"
Write-Host "[NUCLEAR-4Z-B] Constant rows    : $totalConstants"
Write-Host "[NUCLEAR-4Z-B] StateBits rows   : $totalStateBits"
Write-Host "[NUCLEAR-4Z-B] Water textures   : $waterTextures"
Write-Host "[NUCLEAR-4Z-B] Water samples    : $waterSamples"
Write-Host "[NUCLEAR-4Z-B] Reusable aliases : $aliasCount across $aliasGraphs graph(s)"
Write-Host "[NUCLEAR-4Z-B] Technique refs   : $($techniqueRefs.Count)"
Write-Host "[NUCLEAR-4Z-B] Image refs       : $($imageRefs.Count)"
Write-Host '[NUCLEAR-4Z-B] OAT 0/0          : True'
Write-Host '[NUCLEAR-4Z-B] Identity exact   : True'
Write-Host '[NUCLEAR-4Z-B] Graph pass       : True'
Write-Host "[NUCLEAR-4Z-B] Manifest         : $manifestPath"
Write-Host '[NUCLEAR-4Z-B] ================================================================='

Write-Host ''
Write-Host '[NUCLEAR-4Z-B] XENON MATERIAL RELOCATABLE GRAPH: PASS' -ForegroundColor Green
if ($aliasCount -eq 0) {
    Write-Host '[NUCLEAR-4Z-B] No reusable aliases observed; standalone replay can use the simple OAT traversal path.'
}
else {
    Write-Host '[NUCLEAR-4Z-B] Reusable aliases are present; replay must model OAT insert-pointer reuse before production graft.' -ForegroundColor Yellow
}
exit 0
