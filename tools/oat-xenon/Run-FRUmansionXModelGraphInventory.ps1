[CmdletBinding()]
param(
    [string]$CorpusRoot = '',
    [string]$RootBindingRoot = '',
    [string]$OutputRoot = '',
    [int]$StartIndex = 0,
    [int]$MaxModels = 0,
    [switch]$NoResume
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$capabilityFile = Join-Path (Split-Path -Parent $unlinker) 'waw360-graph-dumper-capabilities.json'

if ([string]::IsNullOrWhiteSpace($CorpusRoot)) {
    $CorpusRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4v_xmodel_corpus'
}
if ([string]::IsNullOrWhiteSpace($RootBindingRoot)) {
    $RootBindingRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4w_xmodel_root_binding'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4x_xmodel_graph_inventory'
}

$corpusManifest = Join-Path $CorpusRoot 'nuclear_4v_xmodel_corpus.json'
$bindingManifest = Join-Path $RootBindingRoot 'nuclear_4w_xmodel_root_binding.json'

foreach ($required in @($unlinker, $capabilityFile, $corpusManifest, $bindingManifest)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required 4X-A input is missing: $required"
    }
}

$capabilities = Get-Content -LiteralPath $capabilityFile -Raw | ConvertFrom-Json
if ([string]$capabilities.format -ne 'WaW360.OatGraphDumperCapabilities.v1') {
    throw "Unexpected graph dumper capability manifest format: $($capabilities.format)"
}
if (@($capabilities.capabilities) -notcontains 't4xenon-xmodel-graph-inventory-v1') {
    throw @"
Specialized OAT Unlinker does not expose t4xenon-xmodel-graph-inventory-v1.
Rebuild it first:
  powershell -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Build-OatXenonGraphDumper.ps1"
"@
}

$corpus = Get-Content -LiteralPath $corpusManifest -Raw | ConvertFrom-Json
$binding = Get-Content -LiteralPath $bindingManifest -Raw | ConvertFrom-Json

if (-not [bool]$corpus.CorpusPass -or [int]$corpus.PassedModels -ne 280) {
    throw '4X-A requires the passing 280/280 Stage 4V corpus.'
}
if ([string]$binding.format -ne 'WaW360.Nuclear4W.XModelRootBinding.v3' -or -not [bool]$binding.bindingPass) {
    throw '4X-A requires a passing Stage 4W v3 owned/reference root binding manifest.'
}

$owned = @($binding.ownedBindings | Sort-Object { [int]$_.CorpusOrdinal })
if ($owned.Count -ne 280) {
    throw "Stage 4W exposes $($owned.Count) owned XModel bindings; expected 280."
}

if ($StartIndex -lt 0 -or $StartIndex -ge $owned.Count) {
    throw "StartIndex must be between 0 and $($owned.Count - 1)."
}

$endExclusive = $owned.Count
if ($MaxModels -gt 0) {
    $endExclusive = [Math]::Min($owned.Count, $StartIndex + $MaxModels)
}
if ($endExclusive -le $StartIndex) {
    throw 'Selected 4X-A model range is empty.'
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$dumpsRoot = Join-Path $OutputRoot 'dumps'
$logsRoot = Join-Path $OutputRoot 'logs'
$resultsRoot = Join-Path $OutputRoot 'results'
foreach ($dir in @($dumpsRoot, $logsRoot, $resultsRoot)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

function Invoke-NativeTee {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][object[]]$Arguments,
        [Parameter(Mandatory=$true)][string]$LogPath
    )

    $prior = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $FilePath @Arguments 2>&1 |
            Tee-Object -FilePath $LogPath |
            ForEach-Object { Write-Host ([string]$_) }
        return [int]$LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $prior
    }
}

function Get-SafeStem {
    param([Parameter(Mandatory=$true)][string]$Name)

    $chars = foreach ($ch in $Name.ToCharArray()) {
        if ([char]::IsLetterOrDigit($ch) -or $ch -in @('_','-','.')) {
            $ch
        }
        else {
            '_'
        }
    }

    $stem = -join $chars
    if ($stem.Length -gt 72) {
        $stem = $stem.Substring(0,72)
    }
    if ([string]::IsNullOrWhiteSpace($stem)) {
        $stem = 'unnamed'
    }
    return $stem
}

Write-Host '[NUCLEAR-4X-A] =============================================================='
Write-Host '[NUCLEAR-4X-A] XMODEL XENON GRAPH TOPOLOGY INVENTORY'
Write-Host '[NUCLEAR-4X-A] =============================================================='
Write-Host "[NUCLEAR-4X-A] Owned models : $($owned.Count)"
Write-Host "[NUCLEAR-4X-A] This run     : indices $StartIndex..$($endExclusive - 1)"
Write-Host "[NUCLEAR-4X-A] Unlinker     : $unlinker"
Write-Host "[NUCLEAR-4X-A] Output       : $OutputRoot"
Write-Host "[NUCLEAR-4X-A] Resume       : $(-not $NoResume.IsPresent)"
Write-Host ''

$passedThisRun = 0
$failedThisRun = 0
$skippedThisRun = 0

for ($ordinal = $StartIndex; $ordinal -lt $endExclusive; $ordinal++) {
    $row = $owned[$ordinal]
    $name = [string]$row.CorpusName
    $safe = Get-SafeStem -Name $name

    $dumpDir = Join-Path $dumpsRoot ('{0:D3}_{1}' -f $ordinal, $safe)
    $logFile = Join-Path $logsRoot ('{0:D3}_{1}.log' -f $ordinal, $safe)
    $resultFile = Join-Path $resultsRoot ('{0:D3}_{1}.json' -f $ordinal, $safe)

    if (-not $NoResume.IsPresent -and (Test-Path -LiteralPath $resultFile -PathType Leaf)) {
        try {
            $prior = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
            if ([bool]$prior.Pass -and
                ([string]$prior.Name -ieq $name) -and
                (Test-Path -LiteralPath ([string]$prior.InventoryFile) -PathType Leaf)) {
                $skippedThisRun++
                Write-Host ("[NUCLEAR-4X-A] [{0,3}/{1}] RESUME PASS  {2}" -f ($ordinal + 1), $owned.Count, $name)
                continue
            }
        }
        catch {
        }
    }

    if (Test-Path -LiteralPath $dumpDir) {
        Remove-Item -LiteralPath $dumpDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $dumpDir | Out-Null

    Write-Host ''
    Write-Host ("[NUCLEAR-4X-A] [{0,3}/{1}] DUMP {2}" -f ($ordinal + 1), $owned.Count, $name)

    $pass = $false
    $errorText = $null
    $exitCode = -1
    $inventoryFile = $null
    $canonicalJson = $null
    $inventory = $null

    try {
        $fastFile = [string]$row.FastFile
        if (-not (Test-Path -LiteralPath $fastFile -PathType Leaf)) {
            throw "Validated Xenon FF is missing: $fastFile"
        }

        $args = @(
            '--no-color',
            '--include-assets', 'xmodel',
            '--model-format', 'gltf',
            '--output-folder', $dumpDir,
            $fastFile
        )
        $exitCode = Invoke-NativeTee -FilePath $unlinker -Arguments $args -LogPath $logFile
        if ($exitCode -ne 0) {
            throw "Specialized Xenon Unlinker failed with exit $exitCode."
        }

        $inventoryFiles = @(
            Get-ChildItem -LiteralPath (Join-Path $dumpDir 'xenon_xmodel_graph_inventory') -Filter '*.json' -File -ErrorAction SilentlyContinue
        )
        if ($inventoryFiles.Count -ne 1) {
            throw "Expected exactly one Xenon graph inventory JSON; found $($inventoryFiles.Count)."
        }
        $inventoryFile = $inventoryFiles[0].FullName
        $inventory = Get-Content -LiteralPath $inventoryFile -Raw | ConvertFrom-Json

        if ([string]$inventory.format -ne 'WaW360.T4Xenon.XModelGraphInventory.v1') {
            throw "Unexpected inventory format: $($inventory.format)"
        }
        if (-not ([string]$inventory.name -ieq $name)) {
            throw "Inventory identity '$([string]$inventory.name)' != expected '$name'."
        }

        $canonicalFiles = @(
            Get-ChildItem -LiteralPath (Join-Path $dumpDir 'xmodel') -Filter '*.json' -File -Recurse -ErrorAction SilentlyContinue
        )
        if ($canonicalFiles.Count -ne 1) {
            throw "Expected exactly one canonical Xenon XModel JSON; found $($canonicalFiles.Count)."
        }
        $canonicalJson = $canonicalFiles[0].FullName

        $pass = $true
    }
    catch {
        $errorText = $_.Exception.Message
    }

    $result = [ordered]@{
        Format = 'WaW360.Nuclear4X.XModelGraphInventoryRow.v1'
        Ordinal = $ordinal
        Name = $name
        NativeRootIndex = [int]$row.NativeRootIndex
        FastFile = [string]$row.FastFile
        FastFileSha256 = [string]$row.FastFileSha256
        ExitCode = $exitCode
        InventoryFile = $inventoryFile
        CanonicalXModelJson = $canonicalJson
        NumBones = if ($null -ne $inventory) { [int]$inventory.root.numBones } else { -1 }
        NumSurfs = if ($null -ne $inventory) { [int]$inventory.root.numsurfs } else { -1 }
        NumLods = if ($null -ne $inventory) { [int]$inventory.root.numLods } else { -1 }
        NumCollSurfs = if ($null -ne $inventory) { [int]$inventory.root.numCollSurfs } else { -1 }
        Verts = if ($null -ne $inventory) { [int]$inventory.surfaceTotals.verts } else { -1 }
        Tris = if ($null -ne $inventory) { [int]$inventory.surfaceTotals.tris } else { -1 }
        BlendEntries = if ($null -ne $inventory) { [int]$inventory.surfaceTotals.blendEntries } else { -1 }
        CollisionTrees = if ($null -ne $inventory) { [int]$inventory.surfaceTotals.collisionTrees } else { -1 }
        CollisionNodes = if ($null -ne $inventory) { [int]$inventory.surfaceTotals.collisionNodes } else { -1 }
        CollisionLeafs = if ($null -ne $inventory) { [int]$inventory.surfaceTotals.collisionLeafs } else { -1 }
        HasHighMipBounds = if ($null -ne $inventory) { [bool]$inventory.root.hasHighMipBounds } else { $false }
        HasPhysGeoms = if ($null -ne $inventory) { [bool]$inventory.root.hasPhysGeoms } else { $false }
        HasCollmap = if ($null -ne $inventory) { [bool]$inventory.root.hasCollmap } else { $false }
        PhysGeomBrushes = if ($null -ne $inventory) { [int]$inventory.physGeoms.brushes } else { -1 }
        CollmapBrushes = if ($null -ne $inventory) { [int]$inventory.collmap.brushes } else { -1 }
        HasPhysPreset = if ($null -ne $inventory) { $null -ne $inventory.dependencies.physPreset } else { $false }
        HasPhysConstraints = if ($null -ne $inventory) { $null -ne $inventory.dependencies.physConstraints } else { $false }
        UniqueReusablePointerTargets = if ($null -ne $inventory) { [int]$inventory.uniqueReusablePointerTargets } else { -1 }
        Pass = $pass
        Error = $errorText
        Log = $logFile
    }

    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultFile -Encoding UTF8

    if ($pass) {
        $passedThisRun++
        Write-Host ("[NUCLEAR-4X-A] [{0,3}/{1}] PASS  {2}" -f ($ordinal + 1), $owned.Count, $name) -ForegroundColor Green
    }
    else {
        $failedThisRun++
        Write-Host ("[NUCLEAR-4X-A] [{0,3}/{1}] FAIL  {2} :: {3}" -f ($ordinal + 1), $owned.Count, $name, $errorText) -ForegroundColor Yellow
    }
}

$rows = @(
    Get-ChildItem -LiteralPath $resultsRoot -Filter '*.json' -File -ErrorAction SilentlyContinue |
        ForEach-Object {
            try {
                Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
            }
            catch {
                $null
            }
        } |
        Where-Object { $null -ne $_ } |
        Sort-Object { [int]$_.Ordinal }
)

$passRows = @($rows | Where-Object { [bool]$_.Pass })
$failRows = @($rows | Where-Object { -not [bool]$_.Pass })

$summary = [ordered]@{
    format = 'WaW360.Nuclear4X.XModelGraphInventory.v1'
    expectedModels = 280
    resultRows = $rows.Count
    passedModels = $passRows.Count
    failedModels = $failRows.Count
    passedThisRun = $passedThisRun
    failedThisRun = $failedThisRun
    resumeSkippedThisRun = $skippedThisRun
    corpusInventoryPass = ($rows.Count -eq 280 -and $passRows.Count -eq 280 -and $failRows.Count -eq 0)
    modelsWithBones = @($passRows | Where-Object { [int]$_.NumBones -gt 0 }).Count
    modelsWithMultipleLods = @($passRows | Where-Object { [int]$_.NumLods -gt 1 }).Count
    modelsWithSurfaces = @($passRows | Where-Object { [int]$_.NumSurfs -gt 0 }).Count
    modelsWithBlendedVerts = @($passRows | Where-Object { [int]$_.BlendEntries -gt 0 }).Count
    modelsWithCollisionTrees = @($passRows | Where-Object { [int]$_.CollisionTrees -gt 0 }).Count
    modelsWithCollSurfs = @($passRows | Where-Object { [int]$_.NumCollSurfs -gt 0 }).Count
    modelsWithHighMipBounds = @($passRows | Where-Object { [bool]$_.HasHighMipBounds }).Count
    modelsWithPhysGeoms = @($passRows | Where-Object { [bool]$_.HasPhysGeoms }).Count
    modelsWithCollmap = @($passRows | Where-Object { [bool]$_.HasCollmap }).Count
    modelsWithPhysPreset = @($passRows | Where-Object { [bool]$_.HasPhysPreset }).Count
    modelsWithPhysConstraints = @($passRows | Where-Object { [bool]$_.HasPhysConstraints }).Count
    totalVerts = ($passRows | Measure-Object -Property Verts -Sum).Sum
    totalTris = ($passRows | Measure-Object -Property Tris -Sum).Sum
    totalBlendEntries = ($passRows | Measure-Object -Property BlendEntries -Sum).Sum
    totalCollisionTrees = ($passRows | Measure-Object -Property CollisionTrees -Sum).Sum
    totalCollisionNodes = ($passRows | Measure-Object -Property CollisionNodes -Sum).Sum
    totalCollisionLeafs = ($passRows | Measure-Object -Property CollisionLeafs -Sum).Sum
    totalPhysGeomBrushes = ($passRows | Measure-Object -Property PhysGeomBrushes -Sum).Sum
    totalCollmapBrushes = ($passRows | Measure-Object -Property CollmapBrushes -Sum).Sum
    rows = $rows
}

$summaryFile = Join-Path $OutputRoot 'nuclear_4x_xmodel_graph_inventory.json'
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $summaryFile -Encoding UTF8

$reportFile = Join-Path $OutputRoot 'nuclear_4x_xmodel_graph_inventory.txt'
$report = @(
    'NUCLEAR STAGE 4X-A - XMODEL XENON GRAPH TOPOLOGY INVENTORY'
    '=========================================================================='
    "expectedModels=280"
    "resultRows=$($rows.Count)"
    "passedModels=$($passRows.Count)"
    "failedModels=$($failRows.Count)"
    "corpusInventoryPass=$([bool]$summary.corpusInventoryPass)"
    ''
    "modelsWithBones=$([int]$summary.modelsWithBones)"
    "modelsWithMultipleLods=$([int]$summary.modelsWithMultipleLods)"
    "modelsWithSurfaces=$([int]$summary.modelsWithSurfaces)"
    "modelsWithBlendedVerts=$([int]$summary.modelsWithBlendedVerts)"
    "modelsWithCollisionTrees=$([int]$summary.modelsWithCollisionTrees)"
    "modelsWithCollSurfs=$([int]$summary.modelsWithCollSurfs)"
    "modelsWithHighMipBounds=$([int]$summary.modelsWithHighMipBounds)"
    "modelsWithPhysGeoms=$([int]$summary.modelsWithPhysGeoms)"
    "modelsWithCollmap=$([int]$summary.modelsWithCollmap)"
    "modelsWithPhysPreset=$([int]$summary.modelsWithPhysPreset)"
    "modelsWithPhysConstraints=$([int]$summary.modelsWithPhysConstraints)"
    ''
    "totalVerts=$($summary.totalVerts)"
    "totalTris=$($summary.totalTris)"
    "totalBlendEntries=$($summary.totalBlendEntries)"
    "totalCollisionTrees=$($summary.totalCollisionTrees)"
    "totalCollisionNodes=$($summary.totalCollisionNodes)"
    "totalCollisionLeafs=$($summary.totalCollisionLeafs)"
    "totalPhysGeomBrushes=$($summary.totalPhysGeomBrushes)"
    "totalCollmapBrushes=$($summary.totalCollmapBrushes)"
)

[IO.File]::WriteAllLines(
    $reportFile,
    [string[]]$report,
    [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4X-A] ================= GRAPH INVENTORY RESULT ======================'
Write-Host "[NUCLEAR-4X-A] Result rows       : $($rows.Count)"
Write-Host "[NUCLEAR-4X-A] Passed            : $($passRows.Count)"
Write-Host "[NUCLEAR-4X-A] Failed            : $($failRows.Count)"
Write-Host "[NUCLEAR-4X-A] This run pass     : $passedThisRun"
Write-Host "[NUCLEAR-4X-A] This run fail     : $failedThisRun"
Write-Host "[NUCLEAR-4X-A] Resume skips      : $skippedThisRun"
Write-Host "[NUCLEAR-4X-A] Multi-LOD models  : $([int]$summary.modelsWithMultipleLods)"
Write-Host "[NUCLEAR-4X-A] Blended models    : $([int]$summary.modelsWithBlendedVerts)"
Write-Host "[NUCLEAR-4X-A] Collision trees   : $([int]$summary.modelsWithCollisionTrees)"
Write-Host "[NUCLEAR-4X-A] PhysGeoms models  : $([int]$summary.modelsWithPhysGeoms)"
Write-Host "[NUCLEAR-4X-A] Collmap models    : $([int]$summary.modelsWithCollmap)"
Write-Host "[NUCLEAR-4X-A] Corpus pass       : $([bool]$summary.corpusInventoryPass)"
Write-Host "[NUCLEAR-4X-A] Manifest          : $summaryFile"
Write-Host "[NUCLEAR-4X-A] Report            : $reportFile"
Write-Host '[NUCLEAR-4X-A] ================================================================='

if ($failedThisRun -gt 0) {
    exit 4
}

Write-Host ''
if ([bool]$summary.corpusInventoryPass) {
    Write-Host '[NUCLEAR-4X-A] 280-XMODEL GRAPH TOPOLOGY INVENTORY: PASS' -ForegroundColor Green
    Write-Host '[NUCLEAR-4X-A] Next: implement the relocatable native-blob graph format against the measured feature census.'
}
else {
    Write-Host '[NUCLEAR-4X-A] Selected range completed successfully. Run without -MaxModels to close all 280.' -ForegroundColor Cyan
}
exit 0
