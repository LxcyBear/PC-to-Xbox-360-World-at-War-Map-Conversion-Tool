param(
    [string]$MapAssetsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\map assets',
    [string]$ModCorpusManifest = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($ModCorpusManifest)) {
    $ModCorpusManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_mod_dependency_corpus\nuclear_4r_mod_dependency_corpus.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_map_asset_corpus'
}

if (-not (Test-Path -LiteralPath $MapAssetsRoot -PathType Container)) {
    throw "Packaged map corpus folder not found: $MapAssetsRoot"
}
if (-not (Test-Path -LiteralPath $ModCorpusManifest -PathType Leaf)) {
    throw "Previous UGX/ZCT corpus manifest not found: $ModCorpusManifest"
}
if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) {
    throw "OAT Unlinker not found: $unlinker"
}

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

function Get-PackageName {
    param([string]$Root, [string]$Path)

    $relative = Get-RelativePathCompat -BasePath $Root -TargetPath $Path
    $relative = $relative.Replace('\', '/').TrimStart('/')
    if ([string]::IsNullOrWhiteSpace($relative)) { return '(root)' }

    $slash = $relative.IndexOf('/')
    if ($slash -lt 0) {
        if ((Test-Path -LiteralPath $Path -PathType Container)) {
            return [IO.Path]::GetFileName($Path)
        }
        return '(root)'
    }

    return $relative.Substring(0, $slash)
}

function Add-Candidate {
    param(
        [System.Collections.Generic.List[object]]$Candidates,
        [hashtable]$TargetByKey,
        [string]$Identity,
        [string]$Package,
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
        Package = $Package
        SourceKind = $SourceKind
        SourcePath = $SourcePath
        ArchiveEntry = $ArchiveEntry
        Bytes = $Bytes
        Extension = $Extension
        FastFile = $FastFile
    })
}

function Get-PackageRoots {
    param([string]$Root)

    $dirs = @(Get-ChildItem -LiteralPath $Root -Directory -ErrorAction SilentlyContinue | Sort-Object Name)
    $rootFiles = @(Get-ChildItem -LiteralPath $Root -File -ErrorAction SilentlyContinue)

    $rows = New-Object 'System.Collections.Generic.List[object]'
    foreach ($dir in $dirs) {
        $rows.Add([pscustomobject]@{
            Name = $dir.Name
            Root = $dir.FullName
        })
    }

    if ($rootFiles.Count -gt 0) {
        $rows.Add([pscustomobject]@{
            Name = '(root)'
            Root = (Resolve-Path -LiteralPath $Root).Path
        })
    }

    return [object[]]$rows.ToArray()
}

function Get-PackageSearchPaths {
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
    foreach ($iwd in @(Get-ChildItem -LiteralPath $Root -Filter '*.iwd' -File -Recurse -ErrorAction SilentlyContinue)) {
        Add-SearchPath -Path $iwd.DirectoryName
    }
    foreach ($ff in @(Get-ChildItem -LiteralPath $Root -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue)) {
        Add-SearchPath -Path $ff.DirectoryName
    }

    return [string[]]$paths.ToArray()
}

$previous = Get-Content -LiteralPath $ModCorpusManifest -Raw | ConvertFrom-Json
if ([string]$previous.format -ne 'WaW360.Nuclear4R.ModDependencyCorpus.v1') {
    throw "Unexpected previous corpus format: $($previous.format)"
}

$targets = @($previous.remaining)
if ($targets.Count -eq 0) {
    Write-Host '[NUCLEAR-4R-MAPS] UGX/ZCT already resolved every residual image; packaged-map scan is unnecessary.'
    exit 0
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

$packages = @(Get-PackageRoots -Root $MapAssetsRoot)
if ($packages.Count -eq 0) {
    throw "No packaged-map folders or files were found under: $MapAssetsRoot"
}

if (Test-Path -LiteralPath $OutputRoot) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$dumpBase = Join-Path $OutputRoot 'ff_dumps'
New-Item -ItemType Directory -Force -Path $dumpBase | Out-Null

$candidates = New-Object 'System.Collections.Generic.List[object]'
$packageStats = New-Object 'System.Collections.Generic.List[object]'
$ffStats = New-Object 'System.Collections.Generic.List[object]'

Write-Host '[NUCLEAR-4R-MAPS] ==========================================================='
Write-Host '[NUCLEAR-4R-MAPS] PACKAGED CUSTOM-MAP IMAGE DEPENDENCY CENSUS'
Write-Host '[NUCLEAR-4R-MAPS] ==========================================================='
Write-Host "[NUCLEAR-4R-MAPS] Residual targets : $($targets.Count)"
Write-Host "[NUCLEAR-4R-MAPS] Corpus root      : $MapAssetsRoot"
Write-Host "[NUCLEAR-4R-MAPS] Packages found   : $($packages.Count)"
Write-Host '[NUCLEAR-4R-MAPS] POLICY: only identities still unresolved after Xbox retail + UGX/ZCT are queried.'
Write-Host '[NUCLEAR-4R-MAPS] No packaged-map asset is auto-grafted by this census.'
Write-Host ''

$packageOrdinal = 0
foreach ($package in $packages) {
    $packageOrdinal++
    $packageName = [string]$package.Name
    $packageRoot = [string]$package.Root

    Write-Host ("[NUCLEAR-4R-MAPS] PACKAGE {0}/{1}: {2}" -f $packageOrdinal, $packages.Count, $packageName)

    $looseCount = 0
    foreach ($extension in @('*.iwi', '*.dds')) {
        $files = @(Get-ChildItem -LiteralPath $packageRoot -Filter $extension -File -Recurse -ErrorAction SilentlyContinue)
        foreach ($file in $files) {
            $relative = Get-RelativePathCompat -BasePath $packageRoot -TargetPath $file.FullName
            Add-Candidate -Candidates $candidates -TargetByKey $targetByKey -Identity $relative -Package $packageName -SourceKind 'loose' -SourcePath $file.FullName -ArchiveEntry '' -Bytes ([int64]$file.Length) -Extension $file.Extension.ToLowerInvariant() -FastFile ''
            $looseCount++
        }
    }

    $iwdCount = 0
    $iwdImageEntries = 0
    $iwds = @(Get-ChildItem -LiteralPath $packageRoot -Filter '*.iwd' -File -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName)
    foreach ($iwd in $iwds) {
        $iwdCount++
        try {
            $zip = [IO.Compression.ZipFile]::OpenRead($iwd.FullName)
            try {
                foreach ($entry in $zip.Entries) {
                    $ext = [IO.Path]::GetExtension($entry.FullName)
                    if ($ext -ine '.iwi' -and $ext -ine '.dds') { continue }
                    $iwdImageEntries++
                    Add-Candidate -Candidates $candidates -TargetByKey $targetByKey -Identity $entry.FullName -Package $packageName -SourceKind 'iwd' -SourcePath $iwd.FullName -ArchiveEntry $entry.FullName -Bytes ([int64]$entry.Length) -Extension $ext.ToLowerInvariant() -FastFile ''
                }
            }
            finally { $zip.Dispose() }
        }
        catch {
            Write-Host "[NUCLEAR-4R-MAPS] WARN: could not read IWD '$($iwd.FullName)': $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }

    $searchPaths = @(Get-PackageSearchPaths -Root $packageRoot)
    $ffs = @(Get-ChildItem -LiteralPath $packageRoot -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName)
    $ffOrdinal = 0
    foreach ($ff in $ffs) {
        $ffOrdinal++
        $safePackage = ($packageName -replace '[^A-Za-z0-9_.-]', '_')
        $safeFf = ([IO.Path]::GetFileNameWithoutExtension($ff.Name) -replace '[^A-Za-z0-9_.-]', '_')
        $dumpRoot = Join-Path $dumpBase ("{0:D2}_{1}_{2:D2}_{3}" -f $packageOrdinal, $safePackage, $ffOrdinal, $safeFf)
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
        if ($searchPaths.Count -gt 0) {
            $args += @('--search-path', ($searchPaths -join ';'))
        }
        $args += $ff.FullName

        Write-Host ("[NUCLEAR-4R-MAPS]   FF {0}/{1}: {2}" -f $ffOrdinal, $ffs.Count, $ff.Name)

        $savedPreference = $ErrorActionPreference
        $exitCode = -1
        try {
            $ErrorActionPreference = 'Continue'
            & $unlinker @args 2>&1 |
                Tee-Object -FilePath $logPath |
                Out-Null
            $exitCode = [int]$LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $savedPreference
        }

        $dumped = @(Get-ChildItem -LiteralPath $dumpRoot -Filter '*.iwi' -File -Recurse -ErrorAction SilentlyContinue)
        foreach ($file in $dumped) {
            $relative = Get-RelativePathCompat -BasePath $dumpRoot -TargetPath $file.FullName
            Add-Candidate -Candidates $candidates -TargetByKey $targetByKey -Identity $relative -Package $packageName -SourceKind 'ff-dump' -SourcePath $file.FullName -ArchiveEntry '' -Bytes ([int64]$file.Length) -Extension '.iwi' -FastFile $ff.FullName
        }

        $ffStats.Add([pscustomobject]@{
            Package = $packageName
            FastFile = $ff.FullName
            ExitCode = $exitCode
            DumpedIwis = $dumped.Count
            DumpRoot = $dumpRoot
            Log = $logPath
        })
    }

    $packageStats.Add([pscustomobject]@{
        Package = $packageName
        Root = $packageRoot
        LooseImages = $looseCount
        Iwds = $iwdCount
        IwdImageEntries = $iwdImageEntries
        FastFiles = $ffs.Count
    })

    Write-Host ("[NUCLEAR-4R-MAPS]   loose={0}, IWDs={1}, IWD image entries={2}, FFs={3}" -f
        $looseCount, $iwdCount, $iwdImageEntries, $ffs.Count)
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
                    Package, SourcePath, ArchiveEntry
    )

    $packageNames = @($choices | Select-Object -ExpandProperty Package -Unique)
    if ($packageNames.Count -gt 1) {
        $ambiguous.Add([pscustomobject]@{
            Index = [int]$target.Index
            Name = [string]$target.Name
            Packages = $packageNames
            Candidates = $choices
        })
    }

    $best = $choices[0]
    $resolved.Add([pscustomobject]@{
        Index = [int]$target.Index
        Name = [string]$target.Name
        SourceClass = [string]$target.SourceClass
        Normalized = $key
        Package = [string]$best.Package
        SourceKind = [string]$best.SourceKind
        SourcePath = [string]$best.SourcePath
        ArchiveEntry = [string]$best.ArchiveEntry
        Bytes = [int64]$best.Bytes
        Extension = [string]$best.Extension
        FastFile = [string]$best.FastFile
        CandidateCount = $choices.Count
        CandidatePackages = $packageNames
    })
}

$packageResolution = @(
    $resolved.ToArray() |
    Group-Object Package |
    Sort-Object Count -Descending |
    ForEach-Object {
        [pscustomobject]@{
            Package = $_.Name
            Resolved = $_.Count
        }
    }
)

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4R.MapAssetCorpus.v1'
    priorModCorpusManifest = (Resolve-Path -LiteralPath $ModCorpusManifest).Path
    mapAssetsRoot = (Resolve-Path -LiteralPath $MapAssetsRoot).Path
    residualTargets = $targets.Count
    packagesScanned = $packages.Count
    sourceCandidates = $candidateArray.Count
    resolvedFromPackagedMaps = $resolved.Count
    unresolvedAfterPackagedMaps = $remaining.Count
    ambiguousAcrossPackages = $ambiguous.Count
    packageStats = [object[]]$packageStats.ToArray()
    packageResolution = $packageResolution
    fastFileDumps = [object[]]$ffStats.ToArray()
    resolved = [object[]]$resolved.ToArray()
    remaining = [object[]]$remaining.ToArray()
    ambiguous = [object[]]$ambiguous.ToArray()
    policy = @(
        'Packaged-map corpora are queried only for identities unresolved after retail Xbox and UGX/ZCT scans.',
        'Matches prove candidate shared/custom dependency provenance, not automatic license or runtime-reference safety.',
        'No packaged-map asset is automatically grafted by this census.',
        'PC retail WaW is not used.',
        'Production conversion still requires a verified source payload and OAT/4F validation.'
    )
}

$jsonPath = Join-Path $OutputRoot 'nuclear_4r_map_asset_corpus.json'
$txtPath = Join-Path $OutputRoot 'nuclear_4r_map_asset_corpus.txt'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 4R-MAPS - PACKAGED CUSTOM-MAP IMAGE DEPENDENCY CENSUS')
$report.Add(('=' * 96))
$report.Add(("Residual targets:               {0:N0}" -f $targets.Count))
$report.Add(("Packages scanned:               {0:N0}" -f $packages.Count))
$report.Add(("Resolved from packaged maps:    {0:N0}" -f $resolved.Count))
$report.Add(("Still unresolved:               {0:N0}" -f $remaining.Count))
$report.Add(("Ambiguous across packages:      {0:N0}" -f $ambiguous.Count))
$report.Add('')
$report.Add('PACKAGE RESOLUTION')
foreach ($row in $packageResolution) {
    $report.Add(("  {0,-40} {1,4:N0}" -f $row.Package, $row.Resolved))
}
$report.Add('')
$report.Add('RESOLVED')
foreach ($row in $resolved.ToArray()) {
    $archive = if ([string]::IsNullOrWhiteSpace([string]$row.ArchiveEntry)) { '' } else { " :: $($row.ArchiveEntry)" }
    $report.Add(("  root #{0,4}: {1} <- {2}/{3} {4}{5}" -f $row.Index, $row.Name, $row.Package, $row.SourceKind, $row.SourcePath, $archive))
}
$report.Add('')
$report.Add('STILL UNRESOLVED')
foreach ($row in $remaining.ToArray()) {
    $report.Add(("  root #{0,4}: {1} [{2}]" -f $row.Index, $row.Name, $row.SourceClass))
}
[IO.File]::WriteAllLines($txtPath, [string[]]$report.ToArray(), [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4R-MAPS] ====================== CORPUS RESULT ========================'
Write-Host "[NUCLEAR-4R-MAPS] Residual targets            : $($targets.Count)"
Write-Host "[NUCLEAR-4R-MAPS] Packages scanned            : $($packages.Count)"
Write-Host "[NUCLEAR-4R-MAPS] Resolved from packaged maps : $($resolved.Count)"
Write-Host "[NUCLEAR-4R-MAPS] Still unresolved            : $($remaining.Count)"
Write-Host "[NUCLEAR-4R-MAPS] Ambiguous across packages   : $($ambiguous.Count)"
foreach ($row in $packageResolution) {
    Write-Host ("[NUCLEAR-4R-MAPS]   {0}: {1} resolved" -f $row.Package, $row.Resolved)
}
Write-Host "[NUCLEAR-4R-MAPS] Manifest                    : $jsonPath"
Write-Host "[NUCLEAR-4R-MAPS] Report                      : $txtPath"
Write-Host '[NUCLEAR-4R-MAPS] ================================================================='
Write-Host '[NUCLEAR-4R-MAPS] PASS: packaged-map dependency census complete; nothing was auto-grafted.' -ForegroundColor Green
