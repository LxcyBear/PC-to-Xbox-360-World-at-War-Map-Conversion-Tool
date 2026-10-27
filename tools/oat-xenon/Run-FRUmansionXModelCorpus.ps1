[CmdletBinding()]
param(
    [string]$BridgeRoot = '',
    [string]$OutputRoot = '',
    [int]$StartIndex = 0,
    [int]$MaxModels = 0,
    [switch]$NoResume
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$linker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Linker.exe'
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($BridgeRoot)) {
    $BridgeRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4u_oat_xmodel_bridge'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4v_xmodel_corpus'
}

$bridgeManifest = Join-Path $BridgeRoot 'nuclear_4u_oat_xmodel_bridge.json'
$classManifest = Join-Path $BridgeRoot 'pc_partclassification_per_model.json'
$pcDump = Join-Path $BridgeRoot 'pc_dump'

foreach ($required in @($linker, $unlinker, $bridgeManifest, $classManifest)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required 4V input is missing: $required"
    }
}
if (-not (Test-Path -LiteralPath $pcDump -PathType Container)) {
    throw "4U canonical PC XModel dump is missing: $pcDump"
}

$bridge = Get-Content -LiteralPath $bridgeManifest -Raw | ConvertFrom-Json
$class = Get-Content -LiteralPath $classManifest -Raw | ConvertFrom-Json

if (-not [bool]$bridge.bridgePass) {
    throw '4V requires a passing 4U bridge manifest.'
}
if (-not [bool]$class.Complete) {
    throw ("4V requires complete per-model classification recovery. Current: {0}/{1}, failures={2}" -f
        [int]$class.CompleteModels,
        [int]$class.DeclaredXModels,
        [int]$class.FailedModels)
}
if ([int]$class.CompleteModels -ne 280 -or [int]$class.DeclaredXModels -ne 280) {
    throw "4V FRUmansion gate expects the proven 280/280 corpus; manifest reports $([int]$class.CompleteModels)/$([int]$class.DeclaredXModels)."
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$zoneSource = Join-Path $OutputRoot 'zone_source'
$workspace = Join-Path $OutputRoot 'workspace'
$linkedRoot = Join-Path $OutputRoot 'linked'
$logsRoot = Join-Path $OutputRoot 'logs'
$resultsRoot = Join-Path $OutputRoot 'results'

# OAT stores asset-search paths in a sorted set and opens them in that order.
# Keep the per-model classification overlay under .deps so its absolute path
# sorts before the immutable 4U output\...\pc_dump path.
$classificationOverlay = Join-Path $repo '.deps\0-waw360-xmodel-classification-overlay'
foreach ($dir in @($zoneSource, $workspace, $linkedRoot, $logsRoot, $resultsRoot, $classificationOverlay)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}
$activePartClassification = Join-Path $classificationOverlay 'partclassification.csv'
$assetSearchPath = $classificationOverlay + ';' + $pcDump

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

function Get-XModelJsonPath {
    param([Parameter(Mandatory=$true)][string]$Name)

    $relative = $Name.Replace('/', [IO.Path]::DirectorySeparatorChar)
    return Join-Path (Join-Path $pcDump 'xmodel') ($relative + '.json')
}

function Get-ReferenceAssets {
    param([Parameter(Mandatory=$true)][string]$JsonPath)

    $json = Get-Content -LiteralPath $JsonPath -Raw | ConvertFrom-Json
    $refs = New-Object 'System.Collections.Generic.List[object]'
    $keys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    function Add-Ref {
        param(
            [Parameter(Mandatory=$true)][string]$Type,
            [AllowEmptyString()][string]$Name
        )

        if ([string]::IsNullOrWhiteSpace($Name)) { return }
        $key = $Type + [char]0 + $Name
        if ($keys.Add($key)) {
            $refs.Add([pscustomobject]@{ Type = $Type; Name = $Name })
        }
    }

    foreach ($lod in @($json.lods)) {
        $relative = [string]$lod.file
        if ([string]::IsNullOrWhiteSpace($relative)) { continue }

        $lodPath = Join-Path $pcDump $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path -LiteralPath $lodPath -PathType Leaf)) {
            throw "Missing canonical LOD for '$JsonPath': $lodPath"
        }

        $gltf = Get-Content -LiteralPath $lodPath -Raw | ConvertFrom-Json
        foreach ($material in @($gltf.materials)) {
            if ($null -eq $material) { continue }
            Add-Ref -Type 'material' -Name ([string]$material.name)
        }
    }

    if ($json.PSObject.Properties.Name -contains 'physPreset') {
        Add-Ref -Type 'physpreset' -Name ([string]$json.physPreset)
    }
    if ($json.PSObject.Properties.Name -contains 'physConstraints') {
        Add-Ref -Type 'physconstraints' -Name ([string]$json.physConstraints)
    }

    # Do not wrap a generic List[object] in @(...). Windows PowerShell 5.1's
    # array-subexpression binder can throw "Argument types do not match" here.
    # Let the pipeline enumerate the list naturally.
    return $refs
}

function Get-SafeStem {
    param([Parameter(Mandatory=$true)][string]$Name)

    $chars = foreach ($ch in $Name.ToCharArray()) {
        if ([char]::IsLetterOrDigit($ch) -or $ch -in @('_','-','.')) { $ch } else { '_' }
    }
    $stem = -join $chars
    if ($stem.Length -gt 72) { $stem = $stem.Substring(0,72) }
    if ([string]::IsNullOrWhiteSpace($stem)) { $stem = 'unnamed' }
    return $stem
}

function Get-ListedXModels {
    param(
        [Parameter(Mandatory=$true)]
        [AllowEmptyString()]
        [string[]]$Lines
    )

    $names = New-Object 'System.Collections.Generic.List[string]'
    foreach ($line in $Lines) {
        if ($null -eq $line) { continue }
        $text = ([string]$line).Trim()
        if ([string]::IsNullOrWhiteSpace($text)) { continue }

        if ($text -match '(?i)^xmodel\s*,\s*(.+?)\s*
$models = @($class.Models | Sort-Object {[int]$_.XModelOrdinal})
if ($models.Count -ne 280) {
    throw "Classification manifest contains $($models.Count) model rows; expected 280."
}

if ($StartIndex -lt 0 -or $StartIndex -ge $models.Count) {
    throw "StartIndex must be between 0 and $($models.Count - 1)."
}

$endExclusive = $models.Count
if ($MaxModels -gt 0) {
    $endExclusive = [Math]::Min($models.Count, $StartIndex + $MaxModels)
}
$selectedModels = @($models[$StartIndex..($endExclusive - 1)])

Write-Host '[NUCLEAR-4V] ================================================================'
Write-Host '[NUCLEAR-4V] PRODUCTION-FIDELITY XMODEL CORPUS BUILD'
Write-Host '[NUCLEAR-4V] ================================================================'
Write-Host "[NUCLEAR-4V] Bridge root   : $BridgeRoot"
Write-Host "[NUCLEAR-4V] PC dump       : $pcDump"
Write-Host "[NUCLEAR-4V] Output        : $OutputRoot"
Write-Host "[NUCLEAR-4V] Corpus        : $($models.Count) XModels"
Write-Host "[NUCLEAR-4V] This run      : indices $StartIndex..$($endExclusive - 1) ($($selectedModels.Count) models)"
Write-Host "[NUCLEAR-4V] Resume        : $(-not $NoResume.IsPresent)"
Write-Host "[NUCLEAR-4V] Class overlay : $classificationOverlay"
Write-Host ''
Write-Host '[NUCLEAR-4V] Policy: every model uses its own PC-recovered partclassification.csv.'
Write-Host '[NUCLEAR-4V] Pass gate: Xenon link succeeds + generated FF reopens + exact XModel identity is listed + zero OAT errors.'
Write-Host ''

$results = New-Object 'System.Collections.Generic.List[object]'
$passed = 0
$failed = 0
$skipped = 0

for ($ordinal = $StartIndex; $ordinal -lt $endExclusive; $ordinal++) {
    $model = $models[$ordinal]
    $name = [string]$model.Name
    $safe = Get-SafeStem -Name $name
    $project = ('nuclear_4v_xm_{0:D3}' -f $ordinal)

    $resultFile = Join-Path $resultsRoot ('{0:D3}_{1}.json' -f $ordinal, $safe)
    $modelOut = Join-Path $linkedRoot ('{0:D3}_{1}' -f $ordinal, $safe)
    $modelWork = Join-Path $workspace ('{0:D3}' -f $ordinal)
    $linkLog = Join-Path $logsRoot ('{0:D3}_{1}_link.log' -f $ordinal, $safe)
    $readLog = Join-Path $logsRoot ('{0:D3}_{1}_readback.log' -f $ordinal, $safe)

    if (-not $NoResume.IsPresent -and (Test-Path -LiteralPath $resultFile -PathType Leaf)) {
        try {
            $prior = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
            if ([bool]$prior.Pass -and
                [string]$prior.Name -ieq $name -and
                (Test-Path -LiteralPath ([string]$prior.FastFile) -PathType Leaf)) {
                $skipped++
                $passed++
                $results.Add($prior)
                Write-Host ("[NUCLEAR-4V] [{0,3}/{1}] RESUME PASS  {2}" -f ($ordinal + 1), $models.Count, $name)
                continue
            }
        }
        catch {
            # Stale/corrupt result: rebuild this model.
        }
    }

    Write-Host ''
    Write-Host ("[NUCLEAR-4V] [{0,3}/{1}] BUILD {2}" -f ($ordinal + 1), $models.Count, $name)

    $pass = $false
    $linkCode = -1
    $readCode = -1
    $warnCount = 0
    $errorCount = 0
    $listed = $false
    $listedXModels = @()
    $ffPath = $null
    $classificationSha256 = $null
    $xmodelJsonSha256 = $null
    $fastFileSha256 = $null
    $errorText = $null
    $errorPosition = $null
    $errorStack = $null

    try {
        if (-not [bool]$model.Complete) {
            throw "Classification manifest marks this model incomplete: $([string]$model.Error)"
        }

        $classificationCsv = [string]$model.CsvFile
        if (-not (Test-Path -LiteralPath $classificationCsv -PathType Leaf)) {
            throw "Per-model classification CSV is missing: $classificationCsv"
        }

        $jsonPath = Get-XModelJsonPath -Name $name
        if (-not (Test-Path -LiteralPath $jsonPath -PathType Leaf)) {
            throw "Canonical XModel JSON is missing: $jsonPath"
        }

        $classificationSha256 = (Get-FileHash -LiteralPath $classificationCsv -Algorithm SHA256).Hash.ToLowerInvariant()
        $xmodelJsonSha256 = (Get-FileHash -LiteralPath $jsonPath -Algorithm SHA256).Hash.ToLowerInvariant()

        Copy-Item -LiteralPath $classificationCsv -Destination $activePartClassification -Force

        $refs = @(Get-ReferenceAssets -JsonPath $jsonPath)

        if (Test-Path -LiteralPath $modelOut) {
            Remove-Item -LiteralPath $modelOut -Recurse -Force
        }
        if (Test-Path -LiteralPath $modelWork) {
            Remove-Item -LiteralPath $modelWork -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path $modelOut | Out-Null
        New-Item -ItemType Directory -Force -Path $modelWork | Out-Null

        $zoneFile = Join-Path $zoneSource ($project + '.zone')
        $zoneLines = New-Object 'System.Collections.Generic.List[string]'
        $zoneLines.Add('>game,T4')
        $zoneLines.Add('>platform,xbox360')
        $zoneLines.Add(('>name,{0}' -f $project))
        $zoneLines.Add('')

        foreach ($ref in $refs) {
            $refName = ([string]$ref.Name).Replace('"','')
            $zoneLines.Add(('{0},,"{1}"' -f [string]$ref.Type, $refName))
        }
        $zoneLines.Add(('xmodel,"{0}"' -f $name.Replace('"','')))

        [IO.File]::WriteAllLines($zoneFile, $zoneLines, [Text.UTF8Encoding]::new($false))

        $linkArgs = @(
            '--no-color',
            '--base-folder', $modelWork,
            '--source-search-path', $zoneSource,
            '--asset-search-path', $assetSearchPath,
            '--output-folder', $modelOut,
            $project
        )

        Push-Location $modelWork
        try {
            $linkCode = Invoke-NativeTee -FilePath $linker -Arguments $linkArgs -LogPath $linkLog
        }
        finally {
            Pop-Location
        }

        if ($linkCode -ne 0) {
            throw "Xenon Linker failed with exit $linkCode."
        }

        $ffs = @(Get-ChildItem -LiteralPath $modelOut -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue)
        if ($ffs.Count -ne 1) {
            throw "Expected exactly one Xenon FF, found $($ffs.Count)."
        }
        $ffPath = $ffs[0].FullName

        $readCode = Invoke-NativeTee -FilePath $unlinker -Arguments @('--list','--no-color',$ffPath) -LogPath $readLog
        $readLines = @(Get-Content -LiteralPath $readLog -ErrorAction SilentlyContinue)
        $validationLines = [string[]]@(
            $readLines |
                ForEach-Object { [string]$_ } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        )
        $diagnostics = Get-OatDiagnosticCounts -Lines $validationLines
        $warnCount = [int]$diagnostics.Warnings
        $errorCount = [int]$diagnostics.Errors
        $listedXModels = @(Get-ListedXModels -Lines $validationLines)
        $listed = $listedXModels.Count -eq 1 -and [string]$listedXModels[0] -ieq $name

        if ($readCode -ne 0) {
            throw "Xenon read-back failed with exit $readCode."
        }
        if (-not [bool]$diagnostics.SummaryFound) {
            throw "Xenon read-back did not emit OAT's completion summary."
        }
        if ($warnCount -ne 0 -or $errorCount -ne 0) {
            throw "Xenon read-back logged warnings/errors=$warnCount/$errorCount."
        }
        if ($listedXModels.Count -ne 1) {
            throw "Xenon read-back listed $($listedXModels.Count) XModel roots; expected exactly 1."
        }
        if (-not $listed) {
            throw "Xenon read-back did not list exact XModel identity '$name'."
        }

        $fastFileSha256 = (Get-FileHash -LiteralPath $ffPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $pass = $true
    }
    catch {
        $errorText = $_.Exception.Message
        $errorPosition = $_.InvocationInfo.PositionMessage
        $errorStack = $_.ScriptStackTrace
    }

    $row = [ordered]@{
        Format = 'WaW360.Nuclear4V.XModelCorpusRow.v1'
        Ordinal = $ordinal
        AssetIndex = [int]$model.AssetIndex
        Name = $name
        BoneCount = [int]$model.BoneCount
        ClassificationCsv = [string]$model.CsvFile
        ClassificationSha256 = $classificationSha256
        XModelJsonSha256 = $xmodelJsonSha256
        FastFileSha256 = $fastFileSha256
        LinkExitCode = $linkCode
        ReadbackExitCode = $readCode
        ReadbackWarnings = $warnCount
        ReadbackErrors = $errorCount
        ExactIdentityListed = $listed
        ListedXModelCount = @($listedXModels).Count
        FastFile = $ffPath
        LinkLog = $linkLog
        ReadbackLog = $readLog
        Pass = $pass
        Error = $errorText
        ErrorPosition = $errorPosition
        ErrorStack = $errorStack
    }

    $row | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resultFile -Encoding UTF8
    $results.Add([pscustomobject]$row)

    if ($pass) {
        $passed++
        Write-Host ("[NUCLEAR-4V] [{0,3}/{1}] PASS  {2}" -f ($ordinal + 1), $models.Count, $name) -ForegroundColor Green
    }
    else {
        $failed++
        Write-Host ("[NUCLEAR-4V] [{0,3}/{1}] FAIL  {2} :: {3}" -f ($ordinal + 1), $models.Count, $name, $errorText) -ForegroundColor Yellow
        if (-not [string]::IsNullOrWhiteSpace([string]$errorPosition)) {
            Write-Host ("[NUCLEAR-4V]   at " + ([string]$errorPosition).Trim()) -ForegroundColor DarkYellow
        }
    }
}

$summaryRows = @(
    Get-ChildItem -LiteralPath $resultsRoot -Filter '*.json' -File -ErrorAction SilentlyContinue |
    ForEach-Object {
        try { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } catch { $null }
    } |
    Where-Object { $null -ne $_ } |
    Sort-Object {[int]$_.Ordinal}
)

$totalPass = @($summaryRows | Where-Object { [bool]$_.Pass }).Count
$totalFail = @($summaryRows | Where-Object { -not [bool]$_.Pass }).Count
$corpusPass = $summaryRows.Count -eq 280 -and $totalPass -eq 280 -and $totalFail -eq 0

$summary = [ordered]@{
    Format = 'WaW360.Nuclear4V.XModelCorpus.v1'
    BridgeManifest = $bridgeManifest
    ClassificationManifest = $classManifest
    ExpectedModels = 280
    ResultRows = $summaryRows.Count
    PassedModels = $totalPass
    FailedModels = $totalFail
    ResumeSkippedThisRun = $skipped
    CorpusPass = $corpusPass
    Models = $summaryRows
}

$summaryFile = Join-Path $OutputRoot 'nuclear_4v_xmodel_corpus.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryFile -Encoding UTF8

$reportFile = Join-Path $OutputRoot 'nuclear_4v_xmodel_corpus.txt'
$report = @(
    'NUCLEAR STAGE 4V XMODEL CORPUS'
    "expected=280"
    "results=$($summaryRows.Count)"
    "passed=$totalPass"
    "failed=$totalFail"
    "corpusPass=$corpusPass"
    ''
)
foreach ($row in $summaryRows) {
    $status = if ([bool]$row.Pass) { 'PASS' } else { 'FAIL' }
    $report += ('{0:D3} {1,-4} {2}' -f [int]$row.Ordinal, $status, [string]$row.Name)
}
[IO.File]::WriteAllLines($reportFile, $report, [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4V] ===================== XMODEL CORPUS RESULT ======================'
Write-Host "[NUCLEAR-4V] Expected     : 280"
Write-Host "[NUCLEAR-4V] Result rows  : $($summaryRows.Count)"
Write-Host "[NUCLEAR-4V] Passed       : $totalPass"
Write-Host "[NUCLEAR-4V] Failed       : $totalFail"
Write-Host "[NUCLEAR-4V] Resume skips : $skipped"
Write-Host "[NUCLEAR-4V] Corpus pass  : $corpusPass"
Write-Host "[NUCLEAR-4V] Manifest     : $summaryFile"
Write-Host "[NUCLEAR-4V] Report       : $reportFile"
Write-Host '[NUCLEAR-4V] ================================================================='

if ($corpusPass) {
    Write-Host ''
    Write-Host '[NUCLEAR-4V] 280-XMODEL XENON CORPUS: PASS' -ForegroundColor Green
    Write-Host '[NUCLEAR-4V] All PC-backed XModels linked independently with their own recovered classifications and re-opened cleanly through the Xenon loader.'
    Write-Host '[NUCLEAR-4V] Next: extract/graft the validated Xenon XModel graphs into the 6,811-root production sequence.'
    exit 0
}

if ($failed -gt 0) {
    Write-Host ''
    Write-Host "[NUCLEAR-4V] This invocation had $failed failure(s). Rerun after fixes; passing rows will resume-skip automatically." -ForegroundColor Yellow
    exit 4
}

Write-Host ''
Write-Host '[NUCLEAR-4V] Partial range completed successfully. Run again without -MaxModels (or continue with -StartIndex) to close all 280.' -ForegroundColor Cyan
exit 0
) {
            $names.Add((([string]$matches[1]).Trim()))
        }
    }

    # Same PS5.1 generic-list binder caveat as Get-ReferenceAssets.
    return $names
}

function Get-OatDiagnosticCounts {
    param(
        [Parameter(Mandatory=$true)]
        [AllowEmptyString()]
        [string[]]$Lines
    )

    $warnings = 0
    $errors = 0
    $foundSummary = $false

    foreach ($line in $Lines) {
        if ($null -eq $line) { continue }
        $text = ([string]$line).Trim()

        if ($text -match '(?i)^Finished with\s+(\d+)\s+warnings?,\s+(\d+)\s+errors?\s*
$models = @($class.Models | Sort-Object {[int]$_.XModelOrdinal})
if ($models.Count -ne 280) {
    throw "Classification manifest contains $($models.Count) model rows; expected 280."
}

if ($StartIndex -lt 0 -or $StartIndex -ge $models.Count) {
    throw "StartIndex must be between 0 and $($models.Count - 1)."
}

$endExclusive = $models.Count
if ($MaxModels -gt 0) {
    $endExclusive = [Math]::Min($models.Count, $StartIndex + $MaxModels)
}
$selectedModels = @($models[$StartIndex..($endExclusive - 1)])

Write-Host '[NUCLEAR-4V] ================================================================'
Write-Host '[NUCLEAR-4V] PRODUCTION-FIDELITY XMODEL CORPUS BUILD'
Write-Host '[NUCLEAR-4V] ================================================================'
Write-Host "[NUCLEAR-4V] Bridge root   : $BridgeRoot"
Write-Host "[NUCLEAR-4V] PC dump       : $pcDump"
Write-Host "[NUCLEAR-4V] Output        : $OutputRoot"
Write-Host "[NUCLEAR-4V] Corpus        : $($models.Count) XModels"
Write-Host "[NUCLEAR-4V] This run      : indices $StartIndex..$($endExclusive - 1) ($($selectedModels.Count) models)"
Write-Host "[NUCLEAR-4V] Resume        : $(-not $NoResume.IsPresent)"
Write-Host "[NUCLEAR-4V] Class overlay : $classificationOverlay"
Write-Host ''
Write-Host '[NUCLEAR-4V] Policy: every model uses its own PC-recovered partclassification.csv.'
Write-Host '[NUCLEAR-4V] Pass gate: Xenon link succeeds + generated FF reopens + exact XModel identity is listed + zero OAT errors.'
Write-Host ''

$results = New-Object 'System.Collections.Generic.List[object]'
$passed = 0
$failed = 0
$skipped = 0

for ($ordinal = $StartIndex; $ordinal -lt $endExclusive; $ordinal++) {
    $model = $models[$ordinal]
    $name = [string]$model.Name
    $safe = Get-SafeStem -Name $name
    $project = ('nuclear_4v_xm_{0:D3}' -f $ordinal)

    $resultFile = Join-Path $resultsRoot ('{0:D3}_{1}.json' -f $ordinal, $safe)
    $modelOut = Join-Path $linkedRoot ('{0:D3}_{1}' -f $ordinal, $safe)
    $modelWork = Join-Path $workspace ('{0:D3}' -f $ordinal)
    $linkLog = Join-Path $logsRoot ('{0:D3}_{1}_link.log' -f $ordinal, $safe)
    $readLog = Join-Path $logsRoot ('{0:D3}_{1}_readback.log' -f $ordinal, $safe)

    if (-not $NoResume.IsPresent -and (Test-Path -LiteralPath $resultFile -PathType Leaf)) {
        try {
            $prior = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
            if ([bool]$prior.Pass -and
                [string]$prior.Name -ieq $name -and
                (Test-Path -LiteralPath ([string]$prior.FastFile) -PathType Leaf)) {
                $skipped++
                $passed++
                $results.Add($prior)
                Write-Host ("[NUCLEAR-4V] [{0,3}/{1}] RESUME PASS  {2}" -f ($ordinal + 1), $models.Count, $name)
                continue
            }
        }
        catch {
            # Stale/corrupt result: rebuild this model.
        }
    }

    Write-Host ''
    Write-Host ("[NUCLEAR-4V] [{0,3}/{1}] BUILD {2}" -f ($ordinal + 1), $models.Count, $name)

    $pass = $false
    $linkCode = -1
    $readCode = -1
    $warnCount = 0
    $errorCount = 0
    $listed = $false
    $listedXModels = @()
    $ffPath = $null
    $classificationSha256 = $null
    $xmodelJsonSha256 = $null
    $fastFileSha256 = $null
    $errorText = $null
    $errorPosition = $null
    $errorStack = $null

    try {
        if (-not [bool]$model.Complete) {
            throw "Classification manifest marks this model incomplete: $([string]$model.Error)"
        }

        $classificationCsv = [string]$model.CsvFile
        if (-not (Test-Path -LiteralPath $classificationCsv -PathType Leaf)) {
            throw "Per-model classification CSV is missing: $classificationCsv"
        }

        $jsonPath = Get-XModelJsonPath -Name $name
        if (-not (Test-Path -LiteralPath $jsonPath -PathType Leaf)) {
            throw "Canonical XModel JSON is missing: $jsonPath"
        }

        $classificationSha256 = (Get-FileHash -LiteralPath $classificationCsv -Algorithm SHA256).Hash.ToLowerInvariant()
        $xmodelJsonSha256 = (Get-FileHash -LiteralPath $jsonPath -Algorithm SHA256).Hash.ToLowerInvariant()

        Copy-Item -LiteralPath $classificationCsv -Destination $activePartClassification -Force

        $refs = @(Get-ReferenceAssets -JsonPath $jsonPath)

        if (Test-Path -LiteralPath $modelOut) {
            Remove-Item -LiteralPath $modelOut -Recurse -Force
        }
        if (Test-Path -LiteralPath $modelWork) {
            Remove-Item -LiteralPath $modelWork -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path $modelOut | Out-Null
        New-Item -ItemType Directory -Force -Path $modelWork | Out-Null

        $zoneFile = Join-Path $zoneSource ($project + '.zone')
        $zoneLines = New-Object 'System.Collections.Generic.List[string]'
        $zoneLines.Add('>game,T4')
        $zoneLines.Add('>platform,xbox360')
        $zoneLines.Add(('>name,{0}' -f $project))
        $zoneLines.Add('')

        foreach ($ref in $refs) {
            $refName = ([string]$ref.Name).Replace('"','')
            $zoneLines.Add(('{0},,"{1}"' -f [string]$ref.Type, $refName))
        }
        $zoneLines.Add(('xmodel,"{0}"' -f $name.Replace('"','')))

        [IO.File]::WriteAllLines($zoneFile, $zoneLines, [Text.UTF8Encoding]::new($false))

        $linkArgs = @(
            '--no-color',
            '--base-folder', $modelWork,
            '--source-search-path', $zoneSource,
            '--asset-search-path', $assetSearchPath,
            '--output-folder', $modelOut,
            $project
        )

        Push-Location $modelWork
        try {
            $linkCode = Invoke-NativeTee -FilePath $linker -Arguments $linkArgs -LogPath $linkLog
        }
        finally {
            Pop-Location
        }

        if ($linkCode -ne 0) {
            throw "Xenon Linker failed with exit $linkCode."
        }

        $ffs = @(Get-ChildItem -LiteralPath $modelOut -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue)
        if ($ffs.Count -ne 1) {
            throw "Expected exactly one Xenon FF, found $($ffs.Count)."
        }
        $ffPath = $ffs[0].FullName

        $readCode = Invoke-NativeTee -FilePath $unlinker -Arguments @('--list','--no-color',$ffPath) -LogPath $readLog
        $readLines = @(Get-Content -LiteralPath $readLog -ErrorAction SilentlyContinue)
        $warnCount = @($readLines | Where-Object { $_ -match '(?i)\bwarn(?:ing)?\b' }).Count
        $errorCount = @($readLines | Where-Object { $_ -match '(?i)\berror\b' }).Count
        $listedXModels = @(Get-ListedXModels -Lines $readLines)
        $listed = $listedXModels.Count -eq 1 -and [string]$listedXModels[0] -ieq $name

        if ($readCode -ne 0) {
            throw "Xenon read-back failed with exit $readCode."
        }
        if ($warnCount -ne 0 -or $errorCount -ne 0) {
            throw "Xenon read-back logged warnings/errors=$warnCount/$errorCount."
        }
        if ($listedXModels.Count -ne 1) {
            throw "Xenon read-back listed $($listedXModels.Count) XModel roots; expected exactly 1."
        }
        if (-not $listed) {
            throw "Xenon read-back did not list exact XModel identity '$name'."
        }

        $fastFileSha256 = (Get-FileHash -LiteralPath $ffPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $pass = $true
    }
    catch {
        $errorText = $_.Exception.Message
        $errorPosition = $_.InvocationInfo.PositionMessage
        $errorStack = $_.ScriptStackTrace
    }

    $row = [ordered]@{
        Format = 'WaW360.Nuclear4V.XModelCorpusRow.v1'
        Ordinal = $ordinal
        AssetIndex = [int]$model.AssetIndex
        Name = $name
        BoneCount = [int]$model.BoneCount
        ClassificationCsv = [string]$model.CsvFile
        ClassificationSha256 = $classificationSha256
        XModelJsonSha256 = $xmodelJsonSha256
        FastFileSha256 = $fastFileSha256
        LinkExitCode = $linkCode
        ReadbackExitCode = $readCode
        ReadbackWarnings = $warnCount
        ReadbackErrors = $errorCount
        ExactIdentityListed = $listed
        ListedXModelCount = @($listedXModels).Count
        FastFile = $ffPath
        LinkLog = $linkLog
        ReadbackLog = $readLog
        Pass = $pass
        Error = $errorText
        ErrorPosition = $errorPosition
        ErrorStack = $errorStack
    }

    $row | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resultFile -Encoding UTF8
    $results.Add([pscustomobject]$row)

    if ($pass) {
        $passed++
        Write-Host ("[NUCLEAR-4V] [{0,3}/{1}] PASS  {2}" -f ($ordinal + 1), $models.Count, $name) -ForegroundColor Green
    }
    else {
        $failed++
        Write-Host ("[NUCLEAR-4V] [{0,3}/{1}] FAIL  {2} :: {3}" -f ($ordinal + 1), $models.Count, $name, $errorText) -ForegroundColor Yellow
        if (-not [string]::IsNullOrWhiteSpace([string]$errorPosition)) {
            Write-Host ("[NUCLEAR-4V]   at " + ([string]$errorPosition).Trim()) -ForegroundColor DarkYellow
        }
    }
}

$summaryRows = @(
    Get-ChildItem -LiteralPath $resultsRoot -Filter '*.json' -File -ErrorAction SilentlyContinue |
    ForEach-Object {
        try { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } catch { $null }
    } |
    Where-Object { $null -ne $_ } |
    Sort-Object {[int]$_.Ordinal}
)

$totalPass = @($summaryRows | Where-Object { [bool]$_.Pass }).Count
$totalFail = @($summaryRows | Where-Object { -not [bool]$_.Pass }).Count
$corpusPass = $summaryRows.Count -eq 280 -and $totalPass -eq 280 -and $totalFail -eq 0

$summary = [ordered]@{
    Format = 'WaW360.Nuclear4V.XModelCorpus.v1'
    BridgeManifest = $bridgeManifest
    ClassificationManifest = $classManifest
    ExpectedModels = 280
    ResultRows = $summaryRows.Count
    PassedModels = $totalPass
    FailedModels = $totalFail
    ResumeSkippedThisRun = $skipped
    CorpusPass = $corpusPass
    Models = $summaryRows
}

$summaryFile = Join-Path $OutputRoot 'nuclear_4v_xmodel_corpus.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryFile -Encoding UTF8

$reportFile = Join-Path $OutputRoot 'nuclear_4v_xmodel_corpus.txt'
$report = @(
    'NUCLEAR STAGE 4V XMODEL CORPUS'
    "expected=280"
    "results=$($summaryRows.Count)"
    "passed=$totalPass"
    "failed=$totalFail"
    "corpusPass=$corpusPass"
    ''
)
foreach ($row in $summaryRows) {
    $status = if ([bool]$row.Pass) { 'PASS' } else { 'FAIL' }
    $report += ('{0:D3} {1,-4} {2}' -f [int]$row.Ordinal, $status, [string]$row.Name)
}
[IO.File]::WriteAllLines($reportFile, $report, [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4V] ===================== XMODEL CORPUS RESULT ======================'
Write-Host "[NUCLEAR-4V] Expected     : 280"
Write-Host "[NUCLEAR-4V] Result rows  : $($summaryRows.Count)"
Write-Host "[NUCLEAR-4V] Passed       : $totalPass"
Write-Host "[NUCLEAR-4V] Failed       : $totalFail"
Write-Host "[NUCLEAR-4V] Resume skips : $skipped"
Write-Host "[NUCLEAR-4V] Corpus pass  : $corpusPass"
Write-Host "[NUCLEAR-4V] Manifest     : $summaryFile"
Write-Host "[NUCLEAR-4V] Report       : $reportFile"
Write-Host '[NUCLEAR-4V] ================================================================='

if ($corpusPass) {
    Write-Host ''
    Write-Host '[NUCLEAR-4V] 280-XMODEL XENON CORPUS: PASS' -ForegroundColor Green
    Write-Host '[NUCLEAR-4V] All PC-backed XModels linked independently with their own recovered classifications and re-opened cleanly through the Xenon loader.'
    Write-Host '[NUCLEAR-4V] Next: extract/graft the validated Xenon XModel graphs into the 6,811-root production sequence.'
    exit 0
}

if ($failed -gt 0) {
    Write-Host ''
    Write-Host "[NUCLEAR-4V] This invocation had $failed failure(s). Rerun after fixes; passing rows will resume-skip automatically." -ForegroundColor Yellow
    exit 4
}

Write-Host ''
Write-Host '[NUCLEAR-4V] Partial range completed successfully. Run again without -MaxModels (or continue with -StartIndex) to close all 280.' -ForegroundColor Cyan
exit 0
) {
            $warnings = [int]$matches[1]
            $errors = [int]$matches[2]
            $foundSummary = $true
        }
    }

    if (-not $foundSummary) {
        # Fail closed if OAT's normal completion summary is absent.
        return [pscustomobject]@{
            Warnings = -1
            Errors = -1
            SummaryFound = $false
        }
    }

    return [pscustomobject]@{
        Warnings = $warnings
        Errors = $errors
        SummaryFound = $true
    }
}

$models = @($class.Models | Sort-Object {[int]$_.XModelOrdinal})
if ($models.Count -ne 280) {
    throw "Classification manifest contains $($models.Count) model rows; expected 280."
}

if ($StartIndex -lt 0 -or $StartIndex -ge $models.Count) {
    throw "StartIndex must be between 0 and $($models.Count - 1)."
}

$endExclusive = $models.Count
if ($MaxModels -gt 0) {
    $endExclusive = [Math]::Min($models.Count, $StartIndex + $MaxModels)
}
$selectedModels = @($models[$StartIndex..($endExclusive - 1)])

Write-Host '[NUCLEAR-4V] ================================================================'
Write-Host '[NUCLEAR-4V] PRODUCTION-FIDELITY XMODEL CORPUS BUILD'
Write-Host '[NUCLEAR-4V] ================================================================'
Write-Host "[NUCLEAR-4V] Bridge root   : $BridgeRoot"
Write-Host "[NUCLEAR-4V] PC dump       : $pcDump"
Write-Host "[NUCLEAR-4V] Output        : $OutputRoot"
Write-Host "[NUCLEAR-4V] Corpus        : $($models.Count) XModels"
Write-Host "[NUCLEAR-4V] This run      : indices $StartIndex..$($endExclusive - 1) ($($selectedModels.Count) models)"
Write-Host "[NUCLEAR-4V] Resume        : $(-not $NoResume.IsPresent)"
Write-Host "[NUCLEAR-4V] Class overlay : $classificationOverlay"
Write-Host ''
Write-Host '[NUCLEAR-4V] Policy: every model uses its own PC-recovered partclassification.csv.'
Write-Host '[NUCLEAR-4V] Pass gate: Xenon link succeeds + generated FF reopens + exact XModel identity is listed + zero OAT errors.'
Write-Host ''

$results = New-Object 'System.Collections.Generic.List[object]'
$passed = 0
$failed = 0
$skipped = 0

for ($ordinal = $StartIndex; $ordinal -lt $endExclusive; $ordinal++) {
    $model = $models[$ordinal]
    $name = [string]$model.Name
    $safe = Get-SafeStem -Name $name
    $project = ('nuclear_4v_xm_{0:D3}' -f $ordinal)

    $resultFile = Join-Path $resultsRoot ('{0:D3}_{1}.json' -f $ordinal, $safe)
    $modelOut = Join-Path $linkedRoot ('{0:D3}_{1}' -f $ordinal, $safe)
    $modelWork = Join-Path $workspace ('{0:D3}' -f $ordinal)
    $linkLog = Join-Path $logsRoot ('{0:D3}_{1}_link.log' -f $ordinal, $safe)
    $readLog = Join-Path $logsRoot ('{0:D3}_{1}_readback.log' -f $ordinal, $safe)

    if (-not $NoResume.IsPresent -and (Test-Path -LiteralPath $resultFile -PathType Leaf)) {
        try {
            $prior = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
            if ([bool]$prior.Pass -and
                [string]$prior.Name -ieq $name -and
                (Test-Path -LiteralPath ([string]$prior.FastFile) -PathType Leaf)) {
                $skipped++
                $passed++
                $results.Add($prior)
                Write-Host ("[NUCLEAR-4V] [{0,3}/{1}] RESUME PASS  {2}" -f ($ordinal + 1), $models.Count, $name)
                continue
            }
        }
        catch {
            # Stale/corrupt result: rebuild this model.
        }
    }

    Write-Host ''
    Write-Host ("[NUCLEAR-4V] [{0,3}/{1}] BUILD {2}" -f ($ordinal + 1), $models.Count, $name)

    $pass = $false
    $linkCode = -1
    $readCode = -1
    $warnCount = 0
    $errorCount = 0
    $listed = $false
    $listedXModels = @()
    $ffPath = $null
    $classificationSha256 = $null
    $xmodelJsonSha256 = $null
    $fastFileSha256 = $null
    $errorText = $null
    $errorPosition = $null
    $errorStack = $null

    try {
        if (-not [bool]$model.Complete) {
            throw "Classification manifest marks this model incomplete: $([string]$model.Error)"
        }

        $classificationCsv = [string]$model.CsvFile
        if (-not (Test-Path -LiteralPath $classificationCsv -PathType Leaf)) {
            throw "Per-model classification CSV is missing: $classificationCsv"
        }

        $jsonPath = Get-XModelJsonPath -Name $name
        if (-not (Test-Path -LiteralPath $jsonPath -PathType Leaf)) {
            throw "Canonical XModel JSON is missing: $jsonPath"
        }

        $classificationSha256 = (Get-FileHash -LiteralPath $classificationCsv -Algorithm SHA256).Hash.ToLowerInvariant()
        $xmodelJsonSha256 = (Get-FileHash -LiteralPath $jsonPath -Algorithm SHA256).Hash.ToLowerInvariant()

        Copy-Item -LiteralPath $classificationCsv -Destination $activePartClassification -Force

        $refs = @(Get-ReferenceAssets -JsonPath $jsonPath)

        if (Test-Path -LiteralPath $modelOut) {
            Remove-Item -LiteralPath $modelOut -Recurse -Force
        }
        if (Test-Path -LiteralPath $modelWork) {
            Remove-Item -LiteralPath $modelWork -Recurse -Force
        }
        New-Item -ItemType Directory -Force -Path $modelOut | Out-Null
        New-Item -ItemType Directory -Force -Path $modelWork | Out-Null

        $zoneFile = Join-Path $zoneSource ($project + '.zone')
        $zoneLines = New-Object 'System.Collections.Generic.List[string]'
        $zoneLines.Add('>game,T4')
        $zoneLines.Add('>platform,xbox360')
        $zoneLines.Add(('>name,{0}' -f $project))
        $zoneLines.Add('')

        foreach ($ref in $refs) {
            $refName = ([string]$ref.Name).Replace('"','')
            $zoneLines.Add(('{0},,"{1}"' -f [string]$ref.Type, $refName))
        }
        $zoneLines.Add(('xmodel,"{0}"' -f $name.Replace('"','')))

        [IO.File]::WriteAllLines($zoneFile, $zoneLines, [Text.UTF8Encoding]::new($false))

        $linkArgs = @(
            '--no-color',
            '--base-folder', $modelWork,
            '--source-search-path', $zoneSource,
            '--asset-search-path', $assetSearchPath,
            '--output-folder', $modelOut,
            $project
        )

        Push-Location $modelWork
        try {
            $linkCode = Invoke-NativeTee -FilePath $linker -Arguments $linkArgs -LogPath $linkLog
        }
        finally {
            Pop-Location
        }

        if ($linkCode -ne 0) {
            throw "Xenon Linker failed with exit $linkCode."
        }

        $ffs = @(Get-ChildItem -LiteralPath $modelOut -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue)
        if ($ffs.Count -ne 1) {
            throw "Expected exactly one Xenon FF, found $($ffs.Count)."
        }
        $ffPath = $ffs[0].FullName

        $readCode = Invoke-NativeTee -FilePath $unlinker -Arguments @('--list','--no-color',$ffPath) -LogPath $readLog
        $readLines = @(Get-Content -LiteralPath $readLog -ErrorAction SilentlyContinue)
        $warnCount = @($readLines | Where-Object { $_ -match '(?i)\bwarn(?:ing)?\b' }).Count
        $errorCount = @($readLines | Where-Object { $_ -match '(?i)\berror\b' }).Count
        $listedXModels = @(Get-ListedXModels -Lines $readLines)
        $listed = $listedXModels.Count -eq 1 -and [string]$listedXModels[0] -ieq $name

        if ($readCode -ne 0) {
            throw "Xenon read-back failed with exit $readCode."
        }
        if ($warnCount -ne 0 -or $errorCount -ne 0) {
            throw "Xenon read-back logged warnings/errors=$warnCount/$errorCount."
        }
        if ($listedXModels.Count -ne 1) {
            throw "Xenon read-back listed $($listedXModels.Count) XModel roots; expected exactly 1."
        }
        if (-not $listed) {
            throw "Xenon read-back did not list exact XModel identity '$name'."
        }

        $fastFileSha256 = (Get-FileHash -LiteralPath $ffPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $pass = $true
    }
    catch {
        $errorText = $_.Exception.Message
        $errorPosition = $_.InvocationInfo.PositionMessage
        $errorStack = $_.ScriptStackTrace
    }

    $row = [ordered]@{
        Format = 'WaW360.Nuclear4V.XModelCorpusRow.v1'
        Ordinal = $ordinal
        AssetIndex = [int]$model.AssetIndex
        Name = $name
        BoneCount = [int]$model.BoneCount
        ClassificationCsv = [string]$model.CsvFile
        ClassificationSha256 = $classificationSha256
        XModelJsonSha256 = $xmodelJsonSha256
        FastFileSha256 = $fastFileSha256
        LinkExitCode = $linkCode
        ReadbackExitCode = $readCode
        ReadbackWarnings = $warnCount
        ReadbackErrors = $errorCount
        ExactIdentityListed = $listed
        ListedXModelCount = @($listedXModels).Count
        FastFile = $ffPath
        LinkLog = $linkLog
        ReadbackLog = $readLog
        Pass = $pass
        Error = $errorText
        ErrorPosition = $errorPosition
        ErrorStack = $errorStack
    }

    $row | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resultFile -Encoding UTF8
    $results.Add([pscustomobject]$row)

    if ($pass) {
        $passed++
        Write-Host ("[NUCLEAR-4V] [{0,3}/{1}] PASS  {2}" -f ($ordinal + 1), $models.Count, $name) -ForegroundColor Green
    }
    else {
        $failed++
        Write-Host ("[NUCLEAR-4V] [{0,3}/{1}] FAIL  {2} :: {3}" -f ($ordinal + 1), $models.Count, $name, $errorText) -ForegroundColor Yellow
        if (-not [string]::IsNullOrWhiteSpace([string]$errorPosition)) {
            Write-Host ("[NUCLEAR-4V]   at " + ([string]$errorPosition).Trim()) -ForegroundColor DarkYellow
        }
    }
}

$summaryRows = @(
    Get-ChildItem -LiteralPath $resultsRoot -Filter '*.json' -File -ErrorAction SilentlyContinue |
    ForEach-Object {
        try { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } catch { $null }
    } |
    Where-Object { $null -ne $_ } |
    Sort-Object {[int]$_.Ordinal}
)

$totalPass = @($summaryRows | Where-Object { [bool]$_.Pass }).Count
$totalFail = @($summaryRows | Where-Object { -not [bool]$_.Pass }).Count
$corpusPass = $summaryRows.Count -eq 280 -and $totalPass -eq 280 -and $totalFail -eq 0

$summary = [ordered]@{
    Format = 'WaW360.Nuclear4V.XModelCorpus.v1'
    BridgeManifest = $bridgeManifest
    ClassificationManifest = $classManifest
    ExpectedModels = 280
    ResultRows = $summaryRows.Count
    PassedModels = $totalPass
    FailedModels = $totalFail
    ResumeSkippedThisRun = $skipped
    CorpusPass = $corpusPass
    Models = $summaryRows
}

$summaryFile = Join-Path $OutputRoot 'nuclear_4v_xmodel_corpus.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryFile -Encoding UTF8

$reportFile = Join-Path $OutputRoot 'nuclear_4v_xmodel_corpus.txt'
$report = @(
    'NUCLEAR STAGE 4V XMODEL CORPUS'
    "expected=280"
    "results=$($summaryRows.Count)"
    "passed=$totalPass"
    "failed=$totalFail"
    "corpusPass=$corpusPass"
    ''
)
foreach ($row in $summaryRows) {
    $status = if ([bool]$row.Pass) { 'PASS' } else { 'FAIL' }
    $report += ('{0:D3} {1,-4} {2}' -f [int]$row.Ordinal, $status, [string]$row.Name)
}
[IO.File]::WriteAllLines($reportFile, $report, [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4V] ===================== XMODEL CORPUS RESULT ======================'
Write-Host "[NUCLEAR-4V] Expected     : 280"
Write-Host "[NUCLEAR-4V] Result rows  : $($summaryRows.Count)"
Write-Host "[NUCLEAR-4V] Passed       : $totalPass"
Write-Host "[NUCLEAR-4V] Failed       : $totalFail"
Write-Host "[NUCLEAR-4V] Resume skips : $skipped"
Write-Host "[NUCLEAR-4V] Corpus pass  : $corpusPass"
Write-Host "[NUCLEAR-4V] Manifest     : $summaryFile"
Write-Host "[NUCLEAR-4V] Report       : $reportFile"
Write-Host '[NUCLEAR-4V] ================================================================='

if ($corpusPass) {
    Write-Host ''
    Write-Host '[NUCLEAR-4V] 280-XMODEL XENON CORPUS: PASS' -ForegroundColor Green
    Write-Host '[NUCLEAR-4V] All PC-backed XModels linked independently with their own recovered classifications and re-opened cleanly through the Xenon loader.'
    Write-Host '[NUCLEAR-4V] Next: extract/graft the validated Xenon XModel graphs into the 6,811-root production sequence.'
    exit 0
}

if ($failed -gt 0) {
    Write-Host ''
    Write-Host "[NUCLEAR-4V] This invocation had $failed failure(s). Rerun after fixes; passing rows will resume-skip automatically." -ForegroundColor Yellow
    exit 4
}

Write-Host ''
Write-Host '[NUCLEAR-4V] Partial range completed successfully. Run again without -MaxModels (or continue with -StartIndex) to close all 280.' -ForegroundColor Cyan
exit 0
