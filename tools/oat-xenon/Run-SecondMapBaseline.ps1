[CmdletBinding()]
param(
    [string]$PcMapsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps',
    [string]$MapHint = 'aztec',
    [string]$MapName = '',
    [string]$OutputRoot = '',
    [string]$XboxSoundsRoot = '',
    [switch]$SkipOat,
    [switch]$RescanOat
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$unlinker=Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
if (-not $OutputRoot) { $OutputRoot=Join-Path $repo 'output\second_map_baselines' }
if (-not (Test-Path -LiteralPath $PcMapsRoot -PathType Container)) {
    throw "PC Maps root missing: $PcMapsRoot"
}
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) { throw "CLI project missing: $cli" }
$pcRoot=(Resolve-Path -LiteralPath $PcMapsRoot).Path
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

function Run-LoggedCommand {
    param([string]$Label,[string]$Log,[string[]]$Arguments)
    $saved=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        $lines=@(& dotnet @Arguments 2>&1 | ForEach-Object { [string]$_ })
        $code=[int]$LASTEXITCODE
    } finally { $ErrorActionPreference=$saved }
    [IO.File]::WriteAllLines($Log,[string[]]$lines,[Text.UTF8Encoding]::new($false))
    Write-Host "[SECOND-MAP] $Label exit=$code log=$Log"
    if ($code -ne 0) {
        Write-Host "[SECOND-MAP] Last output:" -ForegroundColor Yellow
        foreach ($line in @($lines | Select-Object -Last 18)) { Write-Host $line }
        throw "$Label failed with exit=$code; see $Log"
    }
    return ,$lines
}
function Get-FileFingerprint([string]$path) {
    $f=Get-Item -LiteralPath $path -ErrorAction Stop
    return "$($f.FullName)|$([long]$f.Length)|$($f.LastWriteTimeUtc.Ticks)"
}
function Normalize-SafeName([string]$value) {
    return ($value -replace '[^A-Za-z0-9_.-]','_')
}
function Sum-Count([object[]]$rows,[string]$type) {
    $sum=0
    foreach ($row in $rows) {
        if (-not $row.Success) { continue }
        $property=$row.AssetCounts.PSObject.Properties[$type]
        if ($null -ne $property) { $sum+=[int]$property.Value }
    }
    return $sum
}

Write-Host '[SECOND-MAP] ==============================================================='
Write-Host '[SECOND-MAP] REUSABLE PC->XBOX360 SECOND-MAP BASELINE (AZTEC BY DEFAULT)'
Write-Host "[SECOND-MAP] PC map root: $pcRoot"
Write-Host '[SECOND-MAP] Source analysis and OAT inventory only: no Xbox-ready FF is implied.'
Write-Host '[SECOND-MAP] ==============================================================='

$scanLog=Join-Path $OutputRoot 'map_discovery.log'
$scanArgs=@('run','--project',$cli,'-c','Release','--','scan-maps',$pcRoot)
$scanLines=Run-LoggedCommand -Label 'Map discovery' -Log $scanLog -Arguments $scanArgs
$detected=New-Object 'System.Collections.Generic.List[string]'
foreach ($line in $scanLines) {
    # scan-maps prints map names in a 36-character padded first column.
    if ([string]$line -match '^\s*(\S+)\s{2,}') {
        $name=[string]$Matches[1]
        if ($name -and -not $detected.Contains($name)) { $detected.Add($name) }
    }
}
$allNames=@($detected.ToArray() | Sort-Object -Unique)
if ($allNames.Count -eq 0) { throw "No map names identified by scan-maps; inspect $scanLog" }
Write-Host "[SECOND-MAP] Discovered map names: $($allNames.Count)"
if ($MapName) {
    $exactNames=@($allNames | Where-Object { $_ -ieq $MapName })
    if ($exactNames.Count -ne 1) {
        throw "MapName '$MapName' not found exactly. Available names: $($allNames -join ', ')"
    }
    $selected=[string]$exactNames[0]
} else {
    $candidates=@($allNames | Where-Object { $_ -match [regex]::Escape($MapHint) })
    $preferred=@($candidates | Where-Object { $_ -ieq "nazi_zombie_$MapHint" })
    if ($preferred.Count -eq 1) { $selected=[string]$preferred[0] }
    elseif ($candidates.Count -eq 1) { $selected=[string]$candidates[0] }
    elseif ($candidates.Count -eq 0) {
        throw "No map matching hint '$MapHint'. Available names: $($allNames -join ', ')"
    } else {
        throw "Ambiguous '$MapHint' maps: $($candidates -join ', '). Rerun with -MapName '<exact-name>'."
    }
}
Write-Host "[SECOND-MAP] Selected: $selected" -ForegroundColor Cyan

$workspace=Join-Path $OutputRoot $selected
New-Item -ItemType Directory -Force -Path $workspace | Out-Null
$analyseLog=Join-Path $workspace 'stage4_normal_analysis_console.log'
$analysisArgs=@('run','--project',$cli,'-c','Release','--','stage4-normal-analyze',$pcRoot,$selected,$OutputRoot)
[void](Run-LoggedCommand -Label 'Normal map analyzer' -Log $analyseLog -Arguments $analysisArgs)
$analysisJson=Join-Path $workspace ($selected+'_stage4_normal_analysis.json')
if (-not (Test-Path -LiteralPath $analysisJson -PathType Leaf)) {
    throw "Stage 4 analysis did not produce expected manifest: $analysisJson"
}
$analysis=Get-Content -LiteralPath $analysisJson -Raw | ConvertFrom-Json
if ([string]$analysis.MapName -ine $selected) { throw 'Analyzer map identity mismatch.' }

$ffRecords=New-Object 'System.Collections.Generic.List[object]'
$oatLogRoot=Join-Path $workspace 'oat_asset_lists'
New-Item -ItemType Directory -Force -Path $oatLogRoot | Out-Null
$ffOrdinal=0
$ffItems=@($analysis.FastFiles)
$primaryFf=@($ffItems | Where-Object { $_.Role -eq 'map.ff' })
if ($primaryFf.Count -ne 1) {
    throw "Expected exactly one map.ff for '$selected', got $($primaryFf.Count). See $analysisJson"
}
$canonicalBase=[IO.Path]::GetFileNameWithoutExtension([string]$primaryFf[0].Path)
if (-not $canonicalBase) { throw 'Canonical map FF basename is empty.' }
if ($ffItems.Count -eq 0) {
    throw "Selected map '$selected' has no analyzable fastfiles. See $analysisJson"
}
Write-Host "[SECOND-MAP] Folder identity: $selected; runtime map identity: $canonicalBase"
Write-Host "[SECOND-MAP] FFs in analysis: $($ffItems.Count); IWDs: $(@($analysis.Iwds).Count)"
foreach ($item in $ffItems) {
    $ffOrdinal++
    $file=[string]$item.Path
    $safe=Normalize-SafeName ([IO.Path]::GetFileNameWithoutExtension($file))
    $log=Join-Path $oatLogRoot ("{0:D2}_{1}.list.log" -f $ffOrdinal,$safe)
    $meta=Join-Path $oatLogRoot ("{0:D2}_{1}.meta.json" -f $ffOrdinal,$safe)
    $counts=@{}
    $success=$false
    $cached=$false
    $code=-1
    $issues=@()
    if (-not $SkipOat -and (Test-Path -LiteralPath $unlinker -PathType Leaf)) {
        $fingerprint=Get-FileFingerprint $file
        $lines=@()
        if (-not $RescanOat -and (Test-Path -LiteralPath $meta) -and (Test-Path -LiteralPath $log)) {
            try {
                $prior=Get-Content -LiteralPath $meta -Raw | ConvertFrom-Json
                if ([string]$prior.Fingerprint -eq $fingerprint -and [int]$prior.ExitCode -eq 0) {
                    $lines=@(Get-Content -LiteralPath $log)
                    $code=0
                    $cached=$true
                }
            } catch {}
        }
        if (-not $cached) {
            $old=$ErrorActionPreference
            try {
                $ErrorActionPreference='Continue'
                $lines=@(& $unlinker '--list' '--no-color' $file 2>&1 | ForEach-Object { [string]$_ })
                $code=[int]$LASTEXITCODE
            } finally { $ErrorActionPreference=$old }
            [IO.File]::WriteAllLines($log,[string[]]$lines,[Text.UTF8Encoding]::new($false))
            ([ordered]@{ Fingerprint=$fingerprint; ExitCode=$code } | ConvertTo-Json) |
                Set-Content -LiteralPath $meta -Encoding UTF8
        }
        $success=$code -eq 0
        foreach ($raw in $lines) {
            $line=([string]$raw).Trim()
            if ($line.StartsWith('[')) {
                $close=$line.IndexOf(']')
                if ($close -ge 0 -and $close+1 -lt $line.Length) {
                    $line=$line.Substring($close+1).TrimStart()
                }
            }
            if ($line -match '^([a-z][a-z0-9_]*)\s*,') {
                $type=[string]$Matches[1].ToLowerInvariant()
                if (-not $counts.ContainsKey($type)) { $counts[$type]=0 }
                $counts[$type]++
            }
        }
        if (-not $success) { $issues+= "OAT --list failed (exit=$code)" }
    } elseif ($SkipOat) { $issues+='OAT listing intentionally skipped.' }
    else { $issues+="OAT Unlinker missing: $unlinker" }

    $ffRecords.Add([pscustomobject]@{
        Role=[string]$item.Role; Path=$file; Bytes=[long](Get-Item -LiteralPath $file).Length
        AnalyzerReadable=[bool]$item.Readable; Success=$success; Cached=$cached
        ExitCode=$code; Log=$log; AssetCounts=[pscustomobject]$counts; Issues=[string[]]$issues
    })
    Write-Host ("[SECOND-MAP] FF {0}/{1}: {2} parsed={3} OAT={4} cached={5} assets={6}" -f
        $ffOrdinal,$ffItems.Count,[IO.Path]::GetFileName($file),[bool]$item.Readable,
        $success,$cached,(($counts.Values | Measure-Object -Sum).Sum))
}

$audioExtensions=@('.wav','.xma','.sdns','.mp3','.ogg','.flac','.wma')
$sourceLoose=New-Object 'System.Collections.Generic.List[object]'
$sourceArchive=New-Object 'System.Collections.Generic.List[object]'
$audioSeen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$ffDirs=@($analysis.InputFiles | Where-Object { $_.Path -match '(?i)\.ff$' } |
    ForEach-Object { Split-Path -Parent ([string]$_.Path) } | Sort-Object -Unique)
foreach ($dir in $ffDirs) {
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) { continue }
    foreach ($file in @(Get-ChildItem -LiteralPath $dir -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension.ToLowerInvariant() -in $audioExtensions })) {
        if (-not $audioSeen.Add($file.FullName)) { continue }
        $sourceLoose.Add([pscustomobject]@{ Path=$file.FullName
            Extension=$file.Extension.ToLowerInvariant(); Bytes=[long]$file.Length })
    }
}
$audioWarnings=New-Object 'System.Collections.Generic.List[string]'
foreach ($iwd in @($analysis.Iwds)) {
    $path=[string]$iwd.Path
    try {
        $zip=[IO.Compression.ZipFile]::OpenRead($path)
        try {
            foreach ($entry in $zip.Entries) {
                $ext=[IO.Path]::GetExtension($entry.FullName).ToLowerInvariant()
                if ($ext -in $audioExtensions) {
                    $sourceArchive.Add([pscustomobject]@{ Archive=$path; Entry=$entry.FullName
                        Extension=$ext; Bytes=[long]$entry.Length })
                }
            }
        } finally { $zip.Dispose() }
    } catch { $audioWarnings.Add("Could not inspect IWD audio entries for $path : $($_.Exception.Message)") }
}

$xboxFiles=New-Object 'System.Collections.Generic.List[object]'
if ($XboxSoundsRoot) {
    if (Test-Path -LiteralPath $XboxSoundsRoot -PathType Container) {
        foreach ($f in @(Get-ChildItem -LiteralPath $XboxSoundsRoot -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension.ToLowerInvariant() -in @('.xma','.sdns') })) {
            $xboxFiles.Add([pscustomobject]@{ Path=$f.FullName; Extension=$f.Extension.ToLowerInvariant()
                Bytes=[long]$f.Length })
        }
    } else { $audioWarnings.Add("XboxSoundsRoot supplied but not found: $XboxSoundsRoot") }
}
$stat=[ordered]@{
    mapName=$selected
    canonicalMapName=$canonicalBase
    expectedXboxDeployment=("usermaps/{0}/sounds" -f $canonicalBase)
    sourceLooseAudioFiles=$sourceLoose.Count
    sourceIwdAudioEntries=$sourceArchive.Count
    knownXboxXmaFiles=@($xboxFiles.ToArray() | Where-Object Extension -eq '.xma').Count
    knownXboxSdnsFiles=@($xboxFiles.ToArray() | Where-Object Extension -eq '.sdns').Count
    xboxSoundsDirectoryChecked=$(if ($XboxSoundsRoot) { $XboxSoundsRoot } else { '' })
    policy=@(
        'Xbox 360 streamed custom audio uses separately deployed XMA/SDNS files in usermaps/<map>/sounds.',
        'PC WAV and IWD audio are source inputs, not drop-in Xbox audio.',
        'OAT can list/dump T4 LoadedSound, but cannot import T4 LoadedSound or sound alias lists using its documented standard Linker.',
        'The baseline does not encode XMA, modify original game assets, or emit a hardware-ready map.'
    )
}
$manifest=[ordered]@{
    format='WaW360.SecondMapBaseline.v1'
    sourceRoot=$pcRoot
    mapName=$selected
    canonicalMapName=$canonicalBase
    profile=[string]$analysis.Profile
    analysisJson=$analysisJson
    analysisInputFiles=[object[]]$analysis.InputFiles
    analyzerWarnings=[string[]]$analysis.Warnings
    fastfiles=[object[]]$ffRecords.ToArray()
    audio=$stat
    looseAudio=[object[]]$sourceLoose.ToArray()
    iwdAudio=[object[]]$sourceArchive.ToArray()
    xboxAudio=[object[]]$xboxFiles.ToArray()
    audioWarnings=[string[]]$audioWarnings.ToArray()
    sourceOnly=$true
    readyForHardware=$false
}
$json=Join-Path $workspace 'second_map_baseline.json'
$txt=Join-Path $workspace 'second_map_baseline.txt'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $json -Encoding UTF8
$report=New-Object 'System.Collections.Generic.List[string]'
$report.Add('SECOND MAP BASELINE / AZTEC DEFAULT')
$report.Add('============================================================')
$report.Add("Map source folder: $selected; canonical runtime name: $canonicalBase; Profile: $($analysis.Profile)")
$report.Add("Source root: $pcRoot")
$report.Add("FF files: $($ffRecords.Count); IWD archives: $(@($analysis.Iwds).Count)")
$report.Add("Loose PC audio: $($sourceLoose.Count); PC IWD audio entries: $($sourceArchive.Count)")
$report.Add("External Xbox audio folder: $($stat.expectedXboxDeployment)")
$report.Add("XMA files supplied: $($stat.knownXboxXmaFiles); SDNS files supplied: $($stat.knownXboxSdnsFiles)")
$report.Add('OAT ASSET CENSUS')
foreach ($ff in $ffRecords.ToArray()) {
    $report.Add("  $($ff.Role): $($ff.Path); parsed=$($ff.AnalyzerReadable); OAT=$($ff.Success)")
    foreach ($item in @($ff.AssetCounts.PSObject.Properties | Sort-Object Name)) {
        $report.Add("    $($item.Name): $($item.Value)")
    }
}
$report.Add('STAGE4 ANALYZER ASSET CENSUS')
foreach ($kv in @($analysis.TotalAssetTypes.PSObject.Properties | Sort-Object Name)) {
    $report.Add("  $($kv.Name): $($kv.Value)")
}
$report.Add('WARNINGS')
foreach ($warn in @($analysis.Warnings)+@($audioWarnings.ToArray())) { $report.Add("  $warn") }
$report.Add('Baseline is diagnostic only. Do not copy any FF from this folder to Xbox as a playable map.')
[IO.File]::WriteAllLines($txt,[string[]]$report.ToArray(),[Text.UTF8Encoding]::new($false))

Write-Host '[SECOND-MAP] ================= BASELINE RESULT ============================='
Write-Host "[SECOND-MAP] Map folder: $selected | runtime: $canonicalBase | profile=$($analysis.Profile)"
Write-Host "[SECOND-MAP] Fastfiles=$($ffRecords.Count) OAT-success=$(@($ffRecords.ToArray() | Where-Object Success).Count)"
Write-Host "[SECOND-MAP] IWDs=$(@($analysis.Iwds).Count) PC-loose-audio=$($sourceLoose.Count) PC-IWD-audio=$($sourceArchive.Count)"
Write-Host "[SECOND-MAP] XMA=$($stat.knownXboxXmaFiles) SDNS=$($stat.knownXboxSdnsFiles) (optional source check)"
Write-Host "[SECOND-MAP] Expected Xbox streamed audio: $($stat.expectedXboxDeployment)"
Write-Host "[SECOND-MAP] Report: $txt"
Write-Host "[SECOND-MAP] Manifest: $json"
Write-Host '[SECOND-MAP] PASS: read-only baseline produced. Xbox hardware readiness NOT asserted.'
