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

if ([string]::IsNullOrWhiteSpace($CorpusRoot)) { $CorpusRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4v_xmodel_corpus' }
if ([string]::IsNullOrWhiteSpace($RootBindingRoot)) { $RootBindingRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4w_xmodel_root_binding' }
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4xb_xmodel_relocatable_graph' }

$corpusManifest = Join-Path $CorpusRoot 'nuclear_4v_xmodel_corpus.json'
$bindingManifest = Join-Path $RootBindingRoot 'nuclear_4w_xmodel_root_binding.json'
foreach ($required in @($unlinker, $capabilityFile, $corpusManifest, $bindingManifest)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required 4X-B input is missing: $required" }
}

$capabilities = Get-Content -LiteralPath $capabilityFile -Raw | ConvertFrom-Json
if (@($capabilities.capabilities) -notcontains 't4xenon-xmodel-relocatable-graph-v1') {
    throw 'Specialized OAT Unlinker lacks t4xenon-xmodel-relocatable-graph-v1. Rebuild with Build-OatXenonGraphDumper.ps1.'
}

$corpus = Get-Content -LiteralPath $corpusManifest -Raw | ConvertFrom-Json
$binding = Get-Content -LiteralPath $bindingManifest -Raw | ConvertFrom-Json
if (-not [bool]$corpus.CorpusPass -or [int]$corpus.PassedModels -ne 280) { throw '4X-B requires the passing 280/280 Stage 4V corpus.' }
if ([string]$binding.format -ne 'WaW360.Nuclear4W.XModelRootBinding.v3' -or -not [bool]$binding.bindingPass) { throw '4X-B requires the passing Stage 4W v3 root binding.' }

$owned = @($binding.ownedBindings | Sort-Object { [int]$_.CorpusOrdinal })
if ($owned.Count -ne 280) { throw "Stage 4W exposes $($owned.Count) owned XModels; expected 280." }
if ($StartIndex -lt 0 -or $StartIndex -ge $owned.Count) { throw "StartIndex must be 0..$($owned.Count - 1)." }

$endExclusive = $owned.Count
if ($MaxModels -gt 0) { $endExclusive = [Math]::Min($owned.Count, $StartIndex + $MaxModels) }

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$dumpsRoot = Join-Path $OutputRoot 'dumps'
$logsRoot = Join-Path $OutputRoot 'logs'
$resultsRoot = Join-Path $OutputRoot 'results'
foreach ($dir in @($dumpsRoot, $logsRoot, $resultsRoot)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

function Invoke-NativeTee {
    param([string]$FilePath, [object[]]$Arguments, [string]$LogPath)
    $prior = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $FilePath @Arguments 2>&1 | Tee-Object -FilePath $LogPath | ForEach-Object { Write-Host ([string]$_) }
        return [int]$LASTEXITCODE
    }
    finally { $ErrorActionPreference = $prior }
}

function Get-SafeStem {
    param([string]$Name)
    $chars = foreach ($ch in $Name.ToCharArray()) { if ([char]::IsLetterOrDigit($ch) -or $ch -in @('_','-','.')) { $ch } else { '_' } }
    $stem = -join $chars
    if ($stem.Length -gt 72) { $stem = $stem.Substring(0,72) }
    if ([string]::IsNullOrWhiteSpace($stem)) { $stem = 'unnamed' }
    return $stem
}

function Resolve-GraphPath {
    param([string]$DumpDir, [AllowNull()][AllowEmptyString()][string]$Relative)
    if ([string]::IsNullOrWhiteSpace($Relative)) { return $null }
    return Join-Path $DumpDir ($Relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
}

function Assert-Blob {
    param([string]$DumpDir, [AllowNull()][AllowEmptyString()][string]$Relative, [long]$ExpectedBytes, [string]$Label)
    if ($ExpectedBytes -eq 0) { return }
    if ([string]::IsNullOrWhiteSpace($Relative)) { throw "$Label path missing; expected $ExpectedBytes bytes." }
    $path = Resolve-GraphPath $DumpDir $Relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "$Label missing: $path" }
    $actual = (Get-Item -LiteralPath $path).Length
    if ($actual -ne $ExpectedBytes) { throw "$Label size mismatch: actual=$actual expected=$ExpectedBytes path=$path" }
}

function Assert-OatZero {
    param([string]$LogPath)
    $ok = @(Get-Content -LiteralPath $LogPath | Where-Object { ([string]$_).Trim() -match '(?i)^Finished with\s+0\s+warnings?,\s+0\s+errors?\s*$' }).Count -gt 0
    if (-not $ok) { throw "OAT did not prove 0 warnings / 0 errors." }
}

Write-Host '[NUCLEAR-4X-B] =============================================================='
Write-Host '[NUCLEAR-4X-B] RELOCATABLE XENON XMODEL GRAPH DUMP'
Write-Host '[NUCLEAR-4X-B] =============================================================='
Write-Host "[NUCLEAR-4X-B] Owned models : $($owned.Count)"
Write-Host "[NUCLEAR-4X-B] This run     : indices $StartIndex..$($endExclusive - 1)"
Write-Host "[NUCLEAR-4X-B] Output       : $OutputRoot"
Write-Host ''

$passedThisRun = 0
$failedThisRun = 0
$skippedThisRun = 0

for ($ordinal = $StartIndex; $ordinal -lt $endExclusive; $ordinal++) {
    $bindingRow = $owned[$ordinal]
    $name = [string]$bindingRow.CorpusName
    $safe = Get-SafeStem $name
    $dumpDir = Join-Path $dumpsRoot ('{0:D3}' -f $ordinal)
    $logFile = Join-Path $logsRoot ('{0:D3}_{1}.log' -f $ordinal, $safe)
    $resultFile = Join-Path $resultsRoot ('{0:D3}_{1}.json' -f $ordinal, $safe)

    if (-not $NoResume.IsPresent -and (Test-Path -LiteralPath $resultFile -PathType Leaf)) {
        try {
            $prior = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
            if ([bool]$prior.Pass -and ([string]$prior.Name -ieq $name) -and (Test-Path -LiteralPath ([string]$prior.GraphFile) -PathType Leaf)) {
                $skippedThisRun++
                Write-Host ("[NUCLEAR-4X-B] [{0,3}/{1}] RESUME PASS  {2}" -f ($ordinal + 1), $owned.Count, $name)
                continue
            }
        }
        catch {}
    }

    if (Test-Path -LiteralPath $dumpDir) { Remove-Item -LiteralPath $dumpDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dumpDir | Out-Null
    Write-Host ''
    Write-Host ("[NUCLEAR-4X-B] [{0,3}/{1}] DUMP {2}" -f ($ordinal + 1), $owned.Count, $name)

    $pass = $false
    $errorText = $null
    $exitCode = -1
    $graphFile = $null
    $graph = $null

    try {
        $fastFile = [string]$bindingRow.FastFile
        if (-not (Test-Path -LiteralPath $fastFile -PathType Leaf)) { throw "Validated Xenon FF missing: $fastFile" }

        $args = @('--no-color','--include-assets','xmodel','--model-format','gltf','--output-folder',$dumpDir,$fastFile)
        $exitCode = Invoke-NativeTee $unlinker $args $logFile
        if ($exitCode -ne 0) { throw "Specialized Xenon Unlinker failed with exit $exitCode." }
        Assert-OatZero $logFile

        $graphs = @(Get-ChildItem -LiteralPath (Join-Path $dumpDir 'xenon_xmodel_graphs') -Filter '*.json' -File -ErrorAction SilentlyContinue)
        if ($graphs.Count -ne 1) { throw "Expected one relocatable graph JSON; found $($graphs.Count)." }

        $graphFile = $graphs[0].FullName
        $graph = Get-Content -LiteralPath $graphFile -Raw | ConvertFrom-Json
        if ([string]$graph.format -ne 'WaW360.T4Xenon.XModelRelocatableGraph.v1') { throw "Unexpected graph format: $($graph.format)" }
        if (-not ([string]$graph.name -ieq $name)) { throw "Graph identity '$([string]$graph.name)' != '$name'." }
        if ([int]$graph.abi.xmodel -ne 0xD4) { throw "XModel ABI=$([int]$graph.abi.xmodel); expected 212." }
        if ([int]$graph.unsupported.count -ne 0) { throw "Unsupported graph branches present: $($graph.unsupported | ConvertTo-Json -Compress)" }
        if ([int]$graph.reusable.aliasCount -ne 0) { throw "Reusable alias count=$([int]$graph.reusable.aliasCount); replay support required." }

        $numBones = [int]$graph.counts.numBones
        $nonRootBones = [int]$graph.counts.nonRootBones
        $numSurfs = [int]$graph.counts.numsurfs
        $highMipCount = [int]$graph.counts.highMipBounds

        if (@($graph.boneNames).Count -ne $numBones) { throw "Bone-name count mismatch." }
        if (@($graph.materials).Count -ne $numSurfs) { throw "Material-ref count mismatch." }
        if (@($graph.surfaces).Count -ne $numSurfs) { throw "Surface-row count mismatch." }

        Assert-Blob $dumpDir ([string]$graph.rootFile) 0xD4 'root'
        Assert-Blob $dumpDir ([string]$graph.parentListFile) $nonRootBones 'parentList'
        Assert-Blob $dumpDir ([string]$graph.quatsFile) ([long]$nonRootBones * [int]$graph.abi.xmodelQuat) 'quats'
        Assert-Blob $dumpDir ([string]$graph.transFile) ([long]$nonRootBones * 16) 'trans'
        Assert-Blob $dumpDir ([string]$graph.partClassificationFile) $numBones 'partClassification'
        Assert-Blob $dumpDir ([string]$graph.baseMatFile) ([long]$numBones * [int]$graph.abi.dobjAnimMat) 'baseMat'
        Assert-Blob $dumpDir ([string]$graph.surfacesFile) ([long]$numSurfs * [int]$graph.abi.xsurface) 'surfaces'
        Assert-Blob $dumpDir ([string]$graph.boneInfoFile) ([long]$numBones * [int]$graph.abi.boneInfo) 'boneInfo'
        Assert-Blob $dumpDir ([string]$graph.highMipBoundsFile) ([long]$highMipCount * [int]$graph.abi.highMipBounds) 'highMipBounds'

        foreach ($surface in @($graph.surfaces)) {
            $si = [int]$surface.index
            Assert-Blob $dumpDir ([string]$surface.vertsBlendFile) ([long][int]$surface.vertsBlendCount * 2) "surface[$si].vertsBlend"
            Assert-Blob $dumpDir ([string]$surface.verts0File) ([long][int]$surface.vertCount * [int]$graph.abi.packedVertex) "surface[$si].verts0"
            Assert-Blob $dumpDir ([string]$surface.vertListFile) ([long][int]$surface.vertListCount * [int]$graph.abi.rigidVertList) "surface[$si].vertList"
            Assert-Blob $dumpDir ([string]$surface.triIndicesFile) ([long][int]$surface.triCount * [int]$graph.abi.triIndex) "surface[$si].triIndices"
            foreach ($tree in @($surface.collisionTrees)) {
                $vi = [int]$tree.vertListIndex
                Assert-Blob $dumpDir ([string]$tree.treeFile) ([int]$graph.abi.collisionTree) "surface[$si].vertList[$vi].tree"
                Assert-Blob $dumpDir ([string]$tree.nodesFile) ([long][int]$tree.nodeCount * [int]$graph.abi.collisionNode) "surface[$si].vertList[$vi].nodes"
                Assert-Blob $dumpDir ([string]$tree.leafsFile) ([long][int]$tree.leafCount * [int]$graph.abi.collisionLeaf) "surface[$si].vertList[$vi].leafs"
            }
        }

        $pass = $true
    }
    catch { $errorText = $_.Exception.Message }

    $result = [ordered]@{
        Format = 'WaW360.Nuclear4XB.XModelRelocatableGraphRow.v1'
        Ordinal = $ordinal
        Name = $name
        NativeRootIndex = [int]$bindingRow.NativeRootIndex
        FastFile = [string]$bindingRow.FastFile
        ExitCode = $exitCode
        GraphFile = $graphFile
        NumBones = if ($null -ne $graph) { [int]$graph.counts.numBones } else { -1 }
        NumSurfs = if ($null -ne $graph) { [int]$graph.counts.numsurfs } else { -1 }
        CollisionTrees = if ($null -ne $graph) { [int]$graph.counts.collisionTrees } else { -1 }
        UnsupportedCount = if ($null -ne $graph) { [int]$graph.unsupported.count } else { -1 }
        ReusableAliasCount = if ($null -ne $graph) { [int]$graph.reusable.aliasCount } else { -1 }
        Pass = $pass
        Error = $errorText
        Log = $logFile
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultFile -Encoding UTF8

    if ($pass) {
        $passedThisRun++
        Write-Host ("[NUCLEAR-4X-B] [{0,3}/{1}] PASS  {2}" -f ($ordinal + 1), $owned.Count, $name) -ForegroundColor Green
    }
    else {
        $failedThisRun++
        Write-Host ("[NUCLEAR-4X-B] [{0,3}/{1}] FAIL  {2} :: {3}" -f ($ordinal + 1), $owned.Count, $name, $errorText) -ForegroundColor Yellow
    }
}

$rows = @(Get-ChildItem -LiteralPath $resultsRoot -Filter '*.json' -File -ErrorAction SilentlyContinue | ForEach-Object { try { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } catch { $null } } | Where-Object { $null -ne $_ } | Sort-Object { [int]$_.Ordinal })
$passRows = @($rows | Where-Object { [bool]$_.Pass })
$failRows = @($rows | Where-Object { -not [bool]$_.Pass })
$corpusPass = ($rows.Count -eq 280 -and $passRows.Count -eq 280 -and $failRows.Count -eq 0)

$summary = [ordered]@{
    format = 'WaW360.Nuclear4XB.XModelRelocatableGraph.v1'
    expectedModels = 280
    resultRows = $rows.Count
    passedModels = $passRows.Count
    failedModels = $failRows.Count
    passedThisRun = $passedThisRun
    failedThisRun = $failedThisRun
    resumeSkippedThisRun = $skippedThisRun
    corpusGraphPass = $corpusPass
    rows = $rows
}
$summaryFile = Join-Path $OutputRoot 'nuclear_4xb_xmodel_relocatable_graph.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryFile -Encoding UTF8

Write-Host ''
Write-Host '[NUCLEAR-4X-B] ================= RELOCATABLE GRAPH RESULT ===================='
Write-Host "[NUCLEAR-4X-B] Result rows   : $($rows.Count)"
Write-Host "[NUCLEAR-4X-B] Passed        : $($passRows.Count)"
Write-Host "[NUCLEAR-4X-B] Failed        : $($failRows.Count)"
Write-Host "[NUCLEAR-4X-B] This run pass : $passedThisRun"
Write-Host "[NUCLEAR-4X-B] This run fail : $failedThisRun"
Write-Host "[NUCLEAR-4X-B] Resume skips  : $skippedThisRun"
Write-Host "[NUCLEAR-4X-B] Corpus pass   : $corpusPass"
Write-Host "[NUCLEAR-4X-B] Manifest      : $summaryFile"
Write-Host '[NUCLEAR-4X-B] ================================================================='

if ($failedThisRun -gt 0) { exit 4 }
if ($corpusPass) {
    Write-Host ''
    Write-Host '[NUCLEAR-4X-B] 280-XMODEL RELOCATABLE GRAPH: PASS' -ForegroundColor Green
}
else {
    Write-Host ''
    Write-Host '[NUCLEAR-4X-B] Selected graph range passed. Next: standalone replay proof.' -ForegroundColor Cyan
}
exit 0
