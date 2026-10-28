[CmdletBinding()]
param(
    [string]$SourceRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\jakes stuff\frumansion decompiled files',
    [string]$TargetManifest = '',
    [string]$OutputRoot = '',
    [switch]$SkipTextScan,
    [switch]$SkipArchives,
    [long]$MaxTextFileBytes = 16777216
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$base = Join-Path $repo 'output\nazi_zombie_FRUmansion'
if (-not $TargetManifest) { $TargetManifest=Join-Path $base 'nuclear_5t_jake_fastfile_corpus_audit\nuclear_5t_jake_fastfile_corpus_audit.json' }
if (-not $OutputRoot) { $OutputRoot=Join-Path $base 'nuclear_5u_frumansion_decompiled_source_audit' }
if (-not (Test-Path -LiteralPath $SourceRoot -PathType Container)) { throw "Decompiled source folder not found: $SourceRoot" }
if (-not (Test-Path -LiteralPath $TargetManifest -PathType Leaf)) { throw "Target manifest missing (run 5T first): $TargetManifest" }
if ($MaxTextFileBytes -le 0) { throw 'MaxTextFileBytes must be positive.' }

$source=(Resolve-Path -LiteralPath $SourceRoot).Path
if (Test-Path -LiteralPath $OutputRoot -PathType Container) {
    $existingOutput=(Resolve-Path -LiteralPath $OutputRoot).Path
    if ($source.Equals($existingOutput,[StringComparison]::OrdinalIgnoreCase) -or
        $existingOutput.StartsWith($source.TrimEnd('\') + '\',[StringComparison]::OrdinalIgnoreCase)) {
        throw 'OutputRoot must not be inside SourceRoot.'
    }
}
$prior=Get-Content -LiteralPath $TargetManifest -Raw | ConvertFrom-Json
if ([string]$prior.format -ne 'WaW360.Nuclear5T.JakeFastFileCorpusAudit.v1' -or
    [int]$prior.targetCount -ne 70 -or
    @($prior.rootTargets | Where-Object { $_.Type -eq 'image' }).Count -ne 43) {
    throw '5U needs the verified 70-target, 43-image NUCLEAR-5T manifest.'
}

function Normalize-Identity([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return '' }
    $v=$value.Trim().Trim('"').Replace('\','/').TrimStart('/')
    while ($v.StartsWith(',')) { $v=$v.Substring(1) }
    $marker=$v.ToLowerInvariant().LastIndexOf('/images/')
    if ($marker -ge 0) { $v=$v.Substring($marker+8) }
    elseif ($v.StartsWith('images/',[StringComparison]::OrdinalIgnoreCase)) { $v=$v.Substring(7) }
    foreach ($extension in @('.iwi','.dds','.tga','.png','.jpg','.jpeg','.bmp','.tif','.tiff','.psd')) {
        if ($v.EndsWith($extension,[StringComparison]::OrdinalIgnoreCase)) {
            $v=$v.Substring(0,$v.Length-$extension.Length)
            break
        }
    }
    if ($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)) { $v=$v.Substring(0,$v.Length-3) }
    return $v.ToLowerInvariant()
}
function String-Id([object]$target) {
    return "$([int]$target.Index)|$([string]$target.Type)|$([string]$target.Name)"
}

$imageExtensions=@('.iwi','.dds','.tga','.png','.jpg','.jpeg','.bmp','.tif','.tiff','.psd','.exr','.ktx')
$textExtensions=@('.txt','.gdt','.map','.gsc','.csc','.csv','.json','.xml','.fx','.weapon',
    '.def','.cfg','.ini','.zone','.str','.menu','.material','.shader','.mtl','.xmodel_export',
    '.xanim_export','.sun','.vision','.log','.lua','.h','.c','.cpp','.script')
$archiveExtensions=@('.zip','.iwd')
$targets=@($prior.rootTargets)
$names=@{}
foreach ($target in $targets) {
    foreach ($alias in @([string]$target.Name,[string]$target.PcImage)) {
        $normalized=Normalize-Identity $alias
        if (-not $normalized) { continue }
        if (-not $names.ContainsKey($normalized)) {
            $names[$normalized]=New-Object 'System.Collections.Generic.List[object]'
        }
        $uniqueId=String-Id $target
        if (-not @($names[$normalized].ToArray() | Where-Object { (String-Id $_) -eq $uniqueId }).Count) {
            $names[$normalized].Add($target)
        }
    }
}
$terms=@($names.Keys | Where-Object { $_.Length -ge 4 } | Sort-Object { $_.Length } -Descending)
if ($terms.Count -eq 0) { throw 'No image/asset identities in 5T target manifest.' }
$escaped=@($terms | ForEach-Object { [regex]::Escape($_) })
$expression='(?i)(?<![a-z0-9_~])(?:' + ($escaped -join '|') + ')(?![a-z0-9_~])'
$matcher=[regex]::new($expression, [Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
    [Text.RegularExpressions.RegexOptions]::CultureInvariant)

$hits=New-Object 'System.Collections.Generic.List[object]'
$seenHits=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$warnings=New-Object 'System.Collections.Generic.List[string]'
$inventory=New-Object 'System.Collections.Generic.List[object]'
$skippedLargeText=0
$textFilesRead=0
$archiveEntriesRead=0
$archiveCount=0

function Add-Hit([object]$target,[string]$kind,[string]$physical,[string]$entry,
    [string]$listed,[long]$bytes,[int]$lineNo) {
    $identity=String-Id $target
    $unique="$identity|$kind|$physical|$entry|$listed|$lineNo"
    if (-not $seenHits.Add($unique)) { return }
    $hits.Add([pscustomobject]@{
        RootIndex=[int]$target.Index; Type=[string]$target.Type
        NativeName=[string]$target.Name; PcImage=[string]$target.PcImage
        Evidence=$kind; SourceFile=$physical; ArchiveEntry=$entry
        MatchedIdentity=$listed; Bytes=$bytes; Line=$lineNo
    })
}

function Inspect-Name([string]$path,[string]$entry,[long]$length,[string]$extension) {
    # Asset filename matches identify source *candidates*, not validated payloads.
    $stem=[IO.Path]::GetFileNameWithoutExtension($(
        if ($entry) { $entry.Replace('\','/') } else { $path.Replace('\','/') }))
    $norm=Normalize-Identity $stem
    if (-not $names.ContainsKey($norm)) { return }
    foreach ($target in $names[$norm]) {
        if ([string]$target.Type -eq 'image') {
            if ($extension -in $imageExtensions) { $kind='image-source-file-candidate' }
            else { $kind='named-file-not-image' }
        } else {
            if ($extension -in $textExtensions -or $extension -in @('.xmodel_bin','.xanim_bin','.bin')) {
                $kind='named-asset-source-candidate'
            } else { $kind='named-file-unverified' }
        }
        Add-Hit $target $kind $path $entry $stem $length 0
    }
}

function Inspect-Text([IO.Stream]$stream,[string]$path,[string]$entry,[long]$length) {
    if ($SkipTextScan) { return }
    if ($length -gt $MaxTextFileBytes) {
        $script:skippedLargeText++
        return
    }
    $reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8,$true,65536,$true)
    try {
        $script:textFilesRead++
        $lineNo=0
        $foundInFile=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        while (-not $reader.EndOfStream) {
            $line=$reader.ReadLine()
            $lineNo++
            foreach ($m in $matcher.Matches($line)) {
                $key=Normalize-Identity ([string]$m.Value)
                if (-not $names.ContainsKey($key)) { continue }
                foreach ($target in $names[$key]) {
                    $identity=String-Id $target
                    # Only first reference per target per source file; no payload verification.
                    if (-not $foundInFile.Add($identity)) { continue }
                    Add-Hit $target 'text-reference-only' $path $entry $m.Value $length $lineNo
                }
            }
        }
    } finally { $reader.Dispose() }
}

Write-Host '[NUCLEAR-5U-SOURCE] ======================================================='
Write-Host '[NUCLEAR-5U-SOURCE] FRUMANSION DECOMPILED SOURCE / DEPENDENCY AUDIT'
Write-Host "[NUCLEAR-5U-SOURCE] Root: $source"
Write-Host "[NUCLEAR-5U-SOURCE] Targets: $($targets.Count) (43 images + 27 others)"
Write-Host '[NUCLEAR-5U-SOURCE] READ-ONLY: never copies source bytes into Xbox outputs.'
Write-Host '[NUCLEAR-5U-SOURCE] ======================================================='

$files=@(Get-ChildItem -LiteralPath $source -File -Recurse -Force -ErrorAction SilentlyContinue | Sort-Object FullName)
Write-Host "[NUCLEAR-5U-SOURCE] Files found: $($files.Count)"
$ordinal=0
foreach ($file in $files) {
    $ordinal++
    $extension=$file.Extension.ToLowerInvariant()
    $inventory.Add([pscustomobject]@{
        Path=$file.FullName; Extension=$(if ($extension) { $extension } else { '(none)' })
        Bytes=[long]$file.Length
    })
    Inspect-Name $file.FullName '' ([long]$file.Length) $extension
    if ($extension -in $textExtensions) {
        try {
            $stream=[IO.File]::OpenRead($file.FullName)
            try { Inspect-Text $stream $file.FullName '' ([long]$file.Length) }
            finally { $stream.Dispose() }
        } catch { $warnings.Add("Text read failed $($file.FullName): $($_.Exception.Message)") }
    } elseif ($extension -in $archiveExtensions -and -not $SkipArchives) {
        $archiveCount++
        try {
            $zip=[IO.Compression.ZipFile]::OpenRead($file.FullName)
            try {
                foreach ($entry in $zip.Entries) {
                    if ($entry.Name -eq '') { continue }
                    $archiveEntriesRead++
                    $entryExt=[IO.Path]::GetExtension($entry.Name).ToLowerInvariant()
                    Inspect-Name $file.FullName $entry.FullName ([long]$entry.Length) $entryExt
                    if ($entryExt -in $textExtensions -and [long]$entry.Length -le $MaxTextFileBytes) {
                        try {
                            $entryStream=$entry.Open()
                            try { Inspect-Text $entryStream $file.FullName $entry.FullName ([long]$entry.Length) }
                            finally { $entryStream.Dispose() }
                        } catch { $warnings.Add("Archive entry read failed $($file.FullName)::$($entry.FullName): $($_.Exception.Message)") }
                    }
                }
            } finally { $zip.Dispose() }
        } catch { $warnings.Add("Archive read failed $($file.FullName): $($_.Exception.Message)") }
    } elseif ($extension -in @('.7z','.rar')) {
        $warnings.Add("Not expanded automatically: $($file.FullName) (extract separately to scan contents).")
    }
    if ($ordinal -eq 1 -or ($ordinal % 250) -eq 0 -or $ordinal -eq $files.Count) {
        Write-Host "[NUCLEAR-5U-SOURCE] files=$ordinal/$($files.Count) matches=$($hits.Count) text=$textFilesRead"
    }
}
$rows=New-Object 'System.Collections.Generic.List[object]'
foreach ($target in $targets) {
    $id=String-Id $target
    $matched=@($hits.ToArray() | Where-Object { "$($_.RootIndex)|$($_.Type)|$($_.NativeName)" -eq $id })
    $physical=@($matched | Where-Object Evidence -eq 'image-source-file-candidate').Count
    $named=@($matched | Where-Object Evidence -eq 'named-asset-source-candidate').Count
    $reference=@($matched | Where-Object Evidence -eq 'text-reference-only').Count
    $rows.Add([pscustomobject]@{
        Index=[int]$target.Index; Type=[string]$target.Type; NativeName=[string]$target.Name
        PcImage=[string]$target.PcImage
        ImageSourceCandidates=$physical; NamedAssetSourceCandidates=$named
        TextReferenceFiles=$reference; TotalHits=$matched.Count
        Status=$(if ($physical -gt 0) { 'image-file-candidate' } elseif ($named -gt 0) {
            'named-source-candidate' } elseif ($reference -gt 0) { 'text-reference-only' } else { 'not-found' })
        Hits=[object[]]$matched
    })
}
$byType=@($rows.ToArray() | Group-Object Type | Sort-Object Name | ForEach-Object {
    [pscustomobject]@{
        Type=$_.Name; Targets=$_.Count
        ImageFiles=@($_.Group | Where-Object Status -eq 'image-file-candidate').Count
        NamedSources=@($_.Group | Where-Object Status -eq 'named-source-candidate').Count
        ReferencesOnly=@($_.Group | Where-Object Status -eq 'text-reference-only').Count
        Missing=@($_.Group | Where-Object Status -eq 'not-found').Count
    }
})
$extensions=@($inventory.ToArray() | Group-Object Extension | Sort-Object Count -Descending | ForEach-Object {
    [pscustomobject]@{ Extension=$_.Name; Count=$_.Count; Bytes=[long](($_.Group | Measure-Object Bytes -Sum).Sum) }
})
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$reportPath=Join-Path $OutputRoot 'nuclear_5u_frumansion_decompiled_source_audit.txt'
$jsonPath=Join-Path $OutputRoot 'nuclear_5u_frumansion_decompiled_source_audit.json'
$inventoryPath=Join-Path $OutputRoot 'nuclear_5u_source_inventory.csv'
$matchPath=Join-Path $OutputRoot 'nuclear_5u_matches.csv'
$inventory.ToArray() | Export-Csv -LiteralPath $inventoryPath -NoTypeInformation -Encoding UTF8
$hits.ToArray() | Export-Csv -LiteralPath $matchPath -NoTypeInformation -Encoding UTF8
$manifest=[ordered]@{
    format='WaW360.Nuclear5U.FRumansionDecompiledSourceAudit.v1'
    sourceRoot=$source; targetManifest=(Resolve-Path -LiteralPath $TargetManifest).Path
    scannedFiles=$files.Count; textFilesRead=$textFilesRead; textFilesSkippedLarge=$skippedLargeText
    archivesOpened=$archiveCount; archiveEntriesIndexed=$archiveEntriesRead
    matches=$hits.Count; extensionInventory=$extensions; byType=$byType
    targets=[object[]]$rows.ToArray(); warnings=[string[]]$warnings.ToArray()
    policy=@(
        'Source-file identity matches are candidates, not verified Xenon payloads.',
        'Text matches prove only a reference, never image pixel ownership.',
        'The audit is read-only and never grafts donor/oracle bytes into output.',
        'Full-content readiness gate and original critical-blocker count remain unchanged.'
    )
}
$manifest | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
$report=New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR-5U FRUMANSION DECOMPILED SOURCE AUDIT')
$report.Add('========================================================')
$report.Add("Root: $source")
$report.Add("Files: $($files.Count); text read: $textFilesRead; skipped large text: $skippedLargeText")
$report.Add("Archives: $archiveCount; indexed entries: $archiveEntriesRead")
$report.Add("Total evidence hits: $($hits.Count)")
$report.Add('FILE EXTENSION INVENTORY')
foreach ($row in @($extensions | Select-Object -First 50)) { $report.Add(("  {0,-20} {1,7:N0} files {2,13:N0} bytes" -f $row.Extension,$row.Count,$row.Bytes)) }
$report.Add('TARGET FAMILY SUMMARY')
foreach ($row in $byType) {
    $report.Add(("  {0,-14} targets={1,3} images={2,3} sources={3,3} textOnly={4,3} missing={5,3}" -f
        $row.Type,$row.Targets,$row.ImageFiles,$row.NamedSources,$row.ReferencesOnly,$row.Missing))
}
$report.Add('TARGET DETAILS')
foreach ($row in $rows.ToArray()) {
    $report.Add(("  #{0,4} {1,-14} {2} => {3}" -f $row.Index,$row.Type,$row.NativeName,$row.Status))
    if ($row.PcImage) { $report.Add("       PC image identity: $($row.PcImage)") }
    foreach ($hit in @($row.Hits | Select-Object -First 7)) {
        $report.Add("       $($hit.Evidence): $($hit.SourceFile) :: $($hit.ArchiveEntry) (line $($hit.Line))")
    }
}
if ($warnings.Count -gt 0) {
    $report.Add('WARNINGS / UNSCANNED CONTAINERS')
    foreach ($warning in @($warnings.ToArray() | Select-Object -First 75)) { $report.Add("  $warning") }
}
[IO.File]::WriteAllLines($reportPath,[string[]]$report.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[NUCLEAR-5U-SOURCE] ================= RESULT ==============================='
foreach ($row in $byType) {
    Write-Host ("[NUCLEAR-5U-SOURCE] {0,-14} targets={1,3} images={2,3} named={3,3} text={4,3} absent={5,3}" -f
        $row.Type,$row.Targets,$row.ImageFiles,$row.NamedSources,$row.ReferencesOnly,$row.Missing)
}
Write-Host "[NUCLEAR-5U-SOURCE] Text files: $textFilesRead; archives: $archiveCount; warnings: $($warnings.Count)"
Write-Host "[NUCLEAR-5U-SOURCE] Report:    $reportPath"
Write-Host "[NUCLEAR-5U-SOURCE] Inventory: $inventoryPath"
Write-Host "[NUCLEAR-5U-SOURCE] Matches:   $matchPath"
Write-Host "[NUCLEAR-5U-SOURCE] JSON:      $jsonPath"
Write-Host '[NUCLEAR-5U-SOURCE] Analysis only — no blocker count was lowered.'
