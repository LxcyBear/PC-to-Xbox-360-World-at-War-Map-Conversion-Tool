param(
    [string]$UGXRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\UGX',
    [string]$ZCTRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\ZCT',
    [string]$XboxCensusManifest = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($XboxCensusManifest)) {
    $XboxCensusManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_xbox_retail_image_census\nuclear_4r_xbox_retail_image_census.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_mod_dependency_corpus'
}

if (-not (Test-Path -LiteralPath $UGXRoot -PathType Container)) { throw "UGX corpus folder not found: $UGXRoot" }
if (-not (Test-Path -LiteralPath $ZCTRoot -PathType Container)) { throw "ZCT corpus folder not found: $ZCTRoot" }
if (-not (Test-Path -LiteralPath $XboxCensusManifest -PathType Leaf)) { throw "Xbox retail census manifest not found: $XboxCensusManifest" }
if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) { throw "OAT Unlinker not found: $unlinker" }

function Get-RelativePathCompat {
    param([string]$BasePath, [string]$TargetPath)

    $baseFull = [IO.Path]::GetFullPath($BasePath)
    $targetFull = [IO.Path]::GetFullPath($TargetPath)
    if (-not $baseFull.EndsWith('\')) { $baseFull += '\' }

    if ($targetFull.StartsWith($baseFull, [StringComparison]::OrdinalIgnoreCase)) {
        return $targetFull.Substring($baseFull.Length)
    }

    return $targetFull
}

function Normalize-TargetIdentity {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }

    $v = $Value.Trim().Replace('\', '/')
    while ($v.StartsWith(',', [StringComparison]::Ordinal)) {
        $v = $v.Substring(1)
    }
    if ($v.EndsWith('_wj', [StringComparison]::OrdinalIgnoreCase)) {
        $v = $v.Substring(0, $v.Length - 3)
    }

    return $v.ToLowerInvariant()
}

function Normalize-SourceIdentity {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }

    $v = $Value.Trim().Replace('\', '/').TrimStart('/')
    $marker = $v.ToLowerInvariant().LastIndexOf('/images/')
    if ($marker -ge 0) {
        $v = $v.Substring($marker + 8)
    }
    elseif ($v.StartsWith('images/', [StringComparison]::OrdinalIgnoreCase)) {
        $v = $v.Substring(7)
    }

    if ($v.EndsWith('.iwi', [StringComparison]::OrdinalIgnoreCase) -or
        $v.EndsWith('.dds', [StringComparison]::OrdinalIgnoreCase)) {
        $v = $v.Substring(0, $v.Length - 4)
    }

    return $v.ToLowerInvariant()
}

function Add-Candidate {
    param(
        [System.Collections.Generic.List[object]]$Candidates,
        [hashtable]$TargetByKey,
        [string]$Identity,
        [string]$Corpus,
        [string]$SourceKind,
        [string]$SourcePath,
        [string]$ArchiveEntry,
        [int64]$Bytes,
        [string]$Extension,
        [string]$FastFile
    )

    $key = Normalize-SourceIdentity -Value $Identity
    if ([string]::IsNullOrWhiteSpace($key)) { return }
    if (-not $TargetByKey.ContainsKey($key)) { return }

    $Candidates.Add([pscustomobject]@{
        Normalized = $key
        Corpus = $Corpus
        SourceKind = $SourceKind
        SourcePath = $SourcePath
        ArchiveEntry = $ArchiveEntry
        Bytes = $Bytes
        Extension = $Extension
        FastFile = $FastFile
    })
}

function Get-CorpusSearchPaths {
    param([string]$Root)

    $paths = New-Object 'System.Collections.Generic.List[string]'
    $seen = @{}

    function Add-SearchPath {
        param([string]$Path)
        if ([string]::IsNullOrWhiteSpace($Path)) { return }
        if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return }
        $full = (Resolve-Path -LiteralPath $Path).Path
        $key = $full.ToLowerInvariant()
        if ($seen.ContainsKey($key)) { return }
        $seen[$key] = $true
        $paths.Add($full)
    }

    Add-SearchPath -Path $Root

    $iwds = @(Get-ChildItem -LiteralPath $Root -Filter '*.iwd' -File -Recurse -ErrorAction SilentlyContinue)
    foreach ($iwd in $iwds) {
        Add-SearchPath -Path $iwd.DirectoryName
    }

    $ffs = @(Get-ChildItem -LiteralPath $Root -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue)
    foreach ($ff in $ffs) {
        Add-SearchPath -Path $ff.DirectoryName
    }

    return [string[]]$paths.ToArray()
}

function Invoke-CorpusFastFileDump {
    param(
        [string]$Corpus,
        [string]$Root,
        [System.Collections.Generic.List[object]]$Candidates,
        [hashtable]$TargetByKey,
        [string]$DumpBase,
        [string[]]$SearchPaths
    )

    $rows = New-Object 'System.Collections.Generic.List[object]'
    $ffs = @(Get-ChildItem -LiteralPath $Root -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName)
    $ordinal = 0

    foreach ($ff in $ffs) {
        $ordinal++
        $safe = ([IO.Path]::GetFileNameWithoutExtension($ff.Name) -replace '[^A-Za-z0-9_.-]', '_')
        $dumpRoot = Join-Path $DumpBase ("{0}_{1:D3}_{2}" -f $Corpus.ToLowerInvariant(), $ordinal, $safe)
        $logPath = $dumpRoot + '.log'

        if (Test-Path -LiteralPath $dumpRoot) { Remove-Item -LiteralPath $dumpRoot -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $dumpRoot | Out-Null

        $args = @(
            '--verbose',
            '--no-color',
            '--include-assets', 'image',
            '--image-format', 'IWI',
            '--output-folder', $dumpRoot
        )
        if ($SearchPaths.Count -gt 0) {
            $args += @('--search-path', ($SearchPaths -join ';'))
        }
        $args += $ff.FullName

        Write-Host ("[NUCLEAR-4R-MOD] {0} FF {1}/{2}: {3}" -f $Corpus, $ordinal, $ffs.Count, $ff.Name)

        $previous = $ErrorActionPreference
        $code = -1
        try {
            $ErrorActionPreference = 'Continue'
            & $unlinker @args 2>&1 |
                Tee-Object -FilePath $logPath |
                Out-Null
            $code = [int]$LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previous
        }

        $dumped = @(Get-ChildItem -LiteralPath $dumpRoot -Filter '*.iwi' -File -Recurse -ErrorAction SilentlyContinue)
        foreach ($file in $dumped) {
            $relative = Get-RelativePathCompat -BasePath $dumpRoot -TargetPath $file.FullName
            Add-Candidate -Candidates $Candidates -TargetByKey $TargetByKey -Identity $relative -Corpus $Corpus -SourceKind 'ff-dump' -SourcePath $file.FullName -ArchiveEntry '' -Bytes ([int64]$file.Length) -Extension '.iwi' -FastFile $ff.FullName
        }

        $rows.Add([pscustomobject]@{
            Corpus = $Corpus
            FastFile = $ff.FullName
            ExitCode = $code
            DumpedIwis = $dumped.Count
            DumpRoot = $dumpRoot
            Log = $logPath
        })
    }

    return [object[]]$rows.ToArray()
}

$census = Get-Content -LiteralPath $XboxCensusManifest -Raw | ConvertFrom-Json
if ([string]$census.format -ne 'WaW360.Nuclear4R.XboxRetailImageCensus.v1') {
    throw "Unexpected Xbox census format: $($census.format)"
}

$targets = @($census.notFound)
if ($targets.Count -eq 0) {
    throw 'Xbox retail census has zero unresolved images; dependency corpus scan is unnecessary.'
}

$targetByKey = @{}
foreach ($target in $targets) {
    $key = Normalize-TargetIdentity -Value ([string]$target.Name)
    if ([string]::IsNullOrWhiteSpace($key)) { continue }
    if (-not $targetByKey.ContainsKey($key)) {
        $targetByKey[$key] = New-Object 'System.Collections.Generic.List[object]'
    }
    $targetByKey[$key].Add($target)
}

if (Test-Path -LiteralPath $OutputRoot) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$dumpBase = Join-Path $OutputRoot 'ff_dumps'
New-Item -ItemType Directory -Force -Path $dumpBase | Out-Null

$corpora = @(
    [pscustomobject]@{ Name = 'UGX'; Root = (Resolve-Path -LiteralPath $UGXRoot).Path },
    [pscustomobject]@{ Name = 'ZCT'; Root = (Resolve-Path -LiteralPath $ZCTRoot).Path }
)

$candidates = New-Object 'System.Collections.Generic.List[object]'
$inventory = New-Object 'System.Collections.Generic.List[object]'

Write-Host '[NUCLEAR-4R-MOD] ============================================================'
Write-Host '[NUCLEAR-4R-MOD] UGX/ZCT DEPENDENCY CORPUS RESOLUTION'
Write-Host '[NUCLEAR-4R-MOD] ============================================================'
Write-Host "[NUCLEAR-4R-MOD] Xbox-unresolved targets : $($targets.Count)"
Write-Host "[NUCLEAR-4R-MOD] UGX root                : $UGXRoot"
Write-Host "[NUCLEAR-4R-MOD] ZCT root                : $ZCTRoot"
Write-Host '[NUCLEAR-4R-MOD] POLICY: these corpora are queried ONLY for images absent from retail Xbox.'
Write-Host '[NUCLEAR-4R-MOD] PC retail WaW is not used.'
Write-Host ''

foreach ($corpus in $corpora) {
    $root = [string]$corpus.Root
    $name = [string]$corpus.Name

    $looseCount = 0
    foreach ($extension in @('*.iwi', '*.dds')) {
        $files = @(Get-ChildItem -LiteralPath $root -Filter $extension -File -Recurse -ErrorAction SilentlyContinue)
        foreach ($file in $files) {
            $relative = Get-RelativePathCompat -BasePath $root -TargetPath $file.FullName
            Add-Candidate -Candidates $candidates -TargetByKey $targetByKey -Identity $relative -Corpus $name -SourceKind 'loose' -SourcePath $file.FullName -ArchiveEntry '' -Bytes ([int64]$file.Length) -Extension $file.Extension.ToLowerInvariant() -FastFile ''
            $looseCount++
        }
    }

    $iwdCount = 0
    $iwdImageEntries = 0
    $iwds = @(Get-ChildItem -LiteralPath $root -Filter '*.iwd' -File -Recurse -ErrorAction SilentlyContinue)
    foreach ($iwd in $iwds) {
        $iwdCount++
        try {
            $zip = [IO.Compression.ZipFile]::OpenRead($iwd.FullName)
            try {
                foreach ($entry in $zip.Entries) {
                    $ext = [IO.Path]::GetExtension($entry.FullName)
                    if ($ext -ine '.iwi' -and $ext -ine '.dds') { continue }
                    $iwdImageEntries++
                    Add-Candidate -Candidates $candidates -TargetByKey $targetByKey -Identity $entry.FullName -Corpus $name -SourceKind 'iwd' -SourcePath $iwd.FullName -ArchiveEntry $entry.FullName -Bytes ([int64]$entry.Length) -Extension $ext.ToLowerInvariant() -FastFile ''
                }
            }
            finally { $zip.Dispose() }
        }
        catch {
            Write-Host "[NUCLEAR-4R-MOD] WARN: could not read $($iwd.FullName): $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }

    $searchPaths = Get-CorpusSearchPaths -Root $root
    $ffRows = @(Invoke-CorpusFastFileDump -Corpus $name -Root $root -Candidates $candidates -TargetByKey $targetByKey -DumpBase $dumpBase -SearchPaths $searchPaths)
    foreach ($row in $ffRows) { $inventory.Add($row) }

    Write-Host ("[NUCLEAR-4R-MOD] {0}: loose images={1}, IWDs={2}, IWD image entries={3}, FFs={4}" -f
        $name, $looseCount, $iwdCount, $iwdImageEntries, $ffRows.Count)
    Write-Host ''
}

$candidateArray = [object[]]$candidates.ToArray()
$byIdentity = @{}
foreach ($candidate in $candidateArray) {
    $key = [string]$candidate.Normalized
    if (-not $byIdentity.ContainsKey($key)) {
        $byIdentity[$key] = New-Object 'System.Collections.Generic.List[object]'
    }
    $byIdentity[$key].Add($candidate)
}

$resolved = New-Object 'System.Collections.Generic.List[object]'
$remaining = New-Object 'System.Collections.Generic.List[object]'
$ambiguous = New-Object 'System.Collections.Generic.List[object]'

foreach ($target in ($targets | Sort-Object { [int]$_.Index })) {
    $key = Normalize-TargetIdentity -Value ([string]$target.Name)

    if (-not $byIdentity.ContainsKey($key)) {
        $remaining.Add([pscustomobject]@{
            Index = [int]$target.Index
            Name = [string]$target.Name
            SourceClass = [string]$target.SourceClass
            Normalized = $key
        })
        continue
    }

    $choices = @(
        $byIdentity[$key].ToArray() |
        Sort-Object @{ Expression = { if ([string]$_.SourceKind -eq 'iwd') { 0 } elseif ([string]$_.SourceKind -eq 'loose') { 1 } else { 2 } } },
                    @{ Expression = { if ([string]$_.Extension -eq '.iwi') { 0 } else { 1 } } },
                    @{ Expression = { -[int64]$_.Bytes } },
                    Corpus, SourcePath, ArchiveEntry
    )

    $corpusNames = @($choices | Select-Object -ExpandProperty Corpus -Unique)
    if ($corpusNames.Count -gt 1) {
        $ambiguous.Add([pscustomobject]@{
            Index = [int]$target.Index
            Name = [string]$target.Name
            Corpora = $corpusNames
            Candidates = $choices
        })
    }

    $best = $choices[0]
    $resolved.Add([pscustomobject]@{
        Index = [int]$target.Index
        Name = [string]$target.Name
        SourceClass = [string]$target.SourceClass
        Normalized = $key
        Corpus = [string]$best.Corpus
        SourceKind = [string]$best.SourceKind
        SourcePath = [string]$best.SourcePath
        ArchiveEntry = [string]$best.ArchiveEntry
        Bytes = [int64]$best.Bytes
        Extension = [string]$best.Extension
        FastFile = [string]$best.FastFile
        CandidateCount = $choices.Count
        CandidateCorpora = $corpusNames
    })
}

$ugxResolved = @($resolved.ToArray() | Where-Object { $_.Corpus -eq 'UGX' })
$zctResolved = @($resolved.ToArray() | Where-Object { $_.Corpus -eq 'ZCT' })
$bothResolved = @($resolved.ToArray() | Where-Object { $_.CandidateCorpora.Count -gt 1 })

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4R.ModDependencyCorpus.v1'
    xboxRetailCensusManifest = (Resolve-Path -LiteralPath $XboxCensusManifest).Path
    ugxRoot = (Resolve-Path -LiteralPath $UGXRoot).Path
    zctRoot = (Resolve-Path -LiteralPath $ZCTRoot).Path
    xboxUnresolvedTargets = $targets.Count
    sourceCandidates = $candidateArray.Count
    resolvedFromDependencyCorpus = $resolved.Count
    unresolvedAfterDependencyCorpus = $remaining.Count
    ugxSelected = $ugxResolved.Count
    zctSelected = $zctResolved.Count
    presentInBothCorpora = $bothResolved.Count
    ambiguousCrossCorpus = $ambiguous.Count
    fastFileDumps = [object[]]$inventory.ToArray()
    resolved = [object[]]$resolved.ToArray()
    remaining = [object[]]$remaining.ToArray()
    ambiguous = [object[]]$ambiguous.ToArray()
    policy = @(
        'Dependency corpora are queried only for image identities absent from the supplied retail Xbox fastfiles.',
        'Xbox retail remains authoritative for stock/base WaW assets.',
        'UGX/ZCT matches are candidate custom/mod-framework sources and are not automatically grafted.',
        'PC retail WaW is not a production dependency source.',
        'Exact identity match is evidence; payload provenance and Xenon conversion must still pass the production graph and 4F gates.'
    )
}

$jsonPath = Join-Path $OutputRoot 'nuclear_4r_mod_dependency_corpus.json'
$txtPath = Join-Path $OutputRoot 'nuclear_4r_mod_dependency_corpus.txt'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4R-MOD - UGX/ZCT DEPENDENCY CORPUS')
$report.Add(('=' * 96))
$report.Add(("Xbox-unresolved targets:        {0:N0}" -f $targets.Count))
$report.Add(("Resolved from UGX/ZCT:          {0:N0}" -f $resolved.Count))
$report.Add(("Still unresolved:               {0:N0}" -f $remaining.Count))
$report.Add(("UGX selected:                   {0:N0}" -f $ugxResolved.Count))
$report.Add(("ZCT selected:                   {0:N0}" -f $zctResolved.Count))
$report.Add(("Present in both corpora:        {0:N0}" -f $bothResolved.Count))
$report.Add(("Cross-corpus ambiguous:         {0:N0}" -f $ambiguous.Count))
$report.Add('')
$report.Add('RESOLVED')
foreach ($row in $resolved.ToArray()) {
    $archive = if ([string]::IsNullOrWhiteSpace([string]$row.ArchiveEntry)) { '' } else { " :: $($row.ArchiveEntry)" }
    $report.Add(("  root #{0,4}: {1} <- {2}/{3} {4}{5}" -f $row.Index, $row.Name, $row.Corpus, $row.SourceKind, $row.SourcePath, $archive))
}
$report.Add('')
$report.Add('STILL UNRESOLVED')
foreach ($row in $remaining.ToArray()) {
    $report.Add(("  root #{0,4}: {1} [{2}]" -f $row.Index, $row.Name, $row.SourceClass))
}
[IO.File]::WriteAllLines($txtPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4R-MOD] ====================== CORPUS RESULT ========================='
Write-Host "[NUCLEAR-4R-MOD] Xbox-unresolved targets : $($targets.Count)"
Write-Host "[NUCLEAR-4R-MOD] Resolved from UGX/ZCT   : $($resolved.Count)"
Write-Host "[NUCLEAR-4R-MOD] Still unresolved        : $($remaining.Count)"
Write-Host "[NUCLEAR-4R-MOD] UGX selected            : $($ugxResolved.Count)"
Write-Host "[NUCLEAR-4R-MOD] ZCT selected            : $($zctResolved.Count)"
Write-Host "[NUCLEAR-4R-MOD] Present in both         : $($bothResolved.Count)"
Write-Host "[NUCLEAR-4R-MOD] Ambiguous cross-corpus  : $($ambiguous.Count)"
Write-Host "[NUCLEAR-4R-MOD] Manifest                : $jsonPath"
Write-Host "[NUCLEAR-4R-MOD] Report                  : $txtPath"
Write-Host '[NUCLEAR-4R-MOD] ================================================================='
Write-Host '[NUCLEAR-4R-MOD] PASS: dependency corpus census complete; no candidate has been auto-grafted.' -ForegroundColor Green
