[CmdletBinding()]
param(
    [string]$SourceRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\jakes stuff\frumansion decompiled files',
    [string]$AuditManifest = '',
    [string]$OutputRoot = '',
    [switch]$SkipFastFileList
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\nazi_zombie_FRUmansion'
if (-not $AuditManifest) {
    $AuditManifest=Join-Path $base 'nuclear_5u_frumansion_decompiled_source_audit\nuclear_5u_frumansion_decompiled_source_audit.json'
}
if (-not $OutputRoot) {
    $OutputRoot=Join-Path $base 'nuclear_5v_frumansion_decompiled_payload_triage'
}
foreach ($required in @($SourceRoot,$AuditManifest)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required input missing: $required" }
}
$source=(Resolve-Path -LiteralPath $SourceRoot).Path
$audit=Get-Content -LiteralPath $AuditManifest -Raw | ConvertFrom-Json
if ([string]$audit.format -ne 'WaW360.Nuclear5U.FRumansionDecompiledSourceAudit.v1' -or
    @($audit.targets).Count -ne 70) {
    throw 'Stage 5V requires the 70-target Stage 5U source audit.'
}
if ($source -ine [string]$audit.sourceRoot) {
    throw "Source folder differs from Stage 5U manifest. Expected: $($audit.sourceRoot)"
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

function Read-Prefix([string]$path,[int]$count) {
    $s=[IO.File]::OpenRead($path)
    try {
        $len=[Math]::Min([long]$count,$s.Length)
        $bytes=New-Object byte[] ([int]$len)
        [void]$s.Read($bytes,0,$bytes.Length)
        return ,$bytes
    } finally { $s.Dispose() }
}
function Normalize-Identity([string]$value) {
    if (-not $value) { return '' }
    $v=$value.Trim().Trim('"').Replace('\','/').TrimStart('/')
    $pos=$v.ToLowerInvariant().LastIndexOf('/images/')
    if ($pos -ge 0) { $v=$v.Substring($pos+8) }
    elseif ($v.StartsWith('images/',[StringComparison]::OrdinalIgnoreCase)) { $v=$v.Substring(7) }
    foreach ($suffix in @('.iwi','.dds','.tga','.png','.jpg','.jpeg','_wj')) {
        if ($v.EndsWith($suffix,[StringComparison]::OrdinalIgnoreCase)) {
            $v=$v.Substring(0,$v.Length-$suffix.Length)
        }
    }
    return $v.ToLowerInvariant()
}
$images=@($audit.targets | Where-Object { $_.Type -eq 'image' })
$imageIndex=@{}
foreach ($item in $images) {
    foreach ($candidate in @([string]$item.NativeName,[string]$item.PcImage)) {
        $id=Normalize-Identity $candidate
        if ($id) { $imageIndex[$id]=[int]$item.Index }
    }
}
$files=@(Get-ChildItem -LiteralPath $source -File -Recurse -Force -ErrorAction Stop | Sort-Object FullName)
$special=@($files | Where-Object { $_.Extension.ToLowerInvariant() -in @('.iwi','.rmb','.ff','.source') })
$wavFiles=@($files | Where-Object { $_.Extension -ieq '.wav' })
$wavRows=New-Object 'System.Collections.Generic.List[object]'
$specialRows=New-Object 'System.Collections.Generic.List[object]'
$ffRows=New-Object 'System.Collections.Generic.List[object]'
$wavRiff=0
$wavNonRiff=0
Write-Host '[NUCLEAR-5V-PAYLOAD] ======================================================'
Write-Host '[NUCLEAR-5V-PAYLOAD] FRUMANSION DECOMPILED PAYLOAD TRIAGE'
Write-Host "[NUCLEAR-5V-PAYLOAD] Source: $source"
Write-Host "[NUCLEAR-5V-PAYLOAD] Files: $($files.Count); WAVs: $($wavFiles.Count); special: $($special.Count)"
Write-Host '[NUCLEAR-5V-PAYLOAD] Read-only. Audio and image files are NOT grafted.'
Write-Host '[NUCLEAR-5V-PAYLOAD] ======================================================'

foreach ($wav in $wavFiles) {
    $bytes=Read-Prefix $wav.FullName 44
    $valid=$bytes.Length -ge 12 -and
        [Text.Encoding]::ASCII.GetString($bytes,0,4) -eq 'RIFF' -and
        [Text.Encoding]::ASCII.GetString($bytes,8,4) -eq 'WAVE'
    if ($valid) { $wavRiff++ } else { $wavNonRiff++ }
    $channels=0
    $frequency=0
    $formatCode=0
    $bits=0
    # Basic PCM-style fmt chunk at offset 12 only; other WAV variants are
    # valid containers but require a fuller chunk parser for stream details.
    if ($valid -and $bytes.Length -ge 36 -and
        [Text.Encoding]::ASCII.GetString($bytes,12,4) -eq 'fmt ') {
        $formatCode=[BitConverter]::ToUInt16($bytes,20)
        $channels=[BitConverter]::ToUInt16($bytes,22)
        $frequency=[BitConverter]::ToUInt32($bytes,24)
        $bits=[BitConverter]::ToUInt16($bytes,34)
    }
    $wavRows.Add([pscustomobject]@{
        Path=$wav.FullName; Bytes=[long]$wav.Length; RiffWave=$valid
        FormatCode=$formatCode; Channels=$channels; SampleRate=$frequency; BitsPerSample=$bits
    })
}
foreach ($file in $special) {
    $ext=$file.Extension.ToLowerInvariant()
    $prefix=Read-Prefix $file.FullName 32
    $hex=([BitConverter]::ToString($prefix)).Replace('-',' ')
    $ascii=-join @($prefix | ForEach-Object { if ($_ -ge 32 -and $_ -le 126) { [char]$_ } else { '.' } })
    $stem=Normalize-Identity ([IO.Path]::GetFileNameWithoutExtension($file.Name))
    $matched=$(if ($imageIndex.ContainsKey($stem)) { [int]$imageIndex[$stem] } else { -1 })
    $specialRows.Add([pscustomobject]@{
        Extension=$ext; Name=$file.Name; Path=$file.FullName; Bytes=[long]$file.Length
        HeaderHex=$hex; HeaderAscii=$ascii; MatchedMissingImageRoot=$matched
    })
}

$unlinker=Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
$ffLogs=Join-Path $OutputRoot 'fastfile_lists'
New-Item -ItemType Directory -Force -Path $ffLogs | Out-Null
if (-not $SkipFastFileList) {
    if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) {
        throw "OAT Unlinker missing; run with -SkipFastFileList for file-only triage: $unlinker"
    }
    $ordinal=0
    foreach ($ff in @($special | Where-Object { $_.Extension -ieq '.ff' })) {
        $ordinal++
        $old=$ErrorActionPreference
        try {
            $ErrorActionPreference='Continue'
            $lines=@(& $unlinker '--list' '--no-color' $ff.FullName 2>&1 | ForEach-Object { [string]$_ })
            $code=[int]$LASTEXITCODE
        } finally { $ErrorActionPreference=$old }
        $log=Join-Path $ffLogs ("ff_{0:D3}.list.log" -f $ordinal)
        [IO.File]::WriteAllLines($log,[string[]]$lines,[Text.UTF8Encoding]::new($false))
        $counts=@{}
        foreach ($line in $lines) {
            $clean=[string]$line
            $clean=$clean.Trim()
            if ($clean.StartsWith('[')) {
                $stop=$clean.IndexOf(']')
                if ($stop -gt 0) { $clean=$clean.Substring($stop+1).Trim() }
            }
            if ($clean -match '^([a-z][a-z0-9_]*)\s*,') {
                $type=$Matches[1].ToLowerInvariant()
                if (-not $counts.ContainsKey($type)) { $counts[$type]=0 }
                $counts[$type]++
            }
        }
        $ffRows.Add([pscustomobject]@{
            Path=$ff.FullName; ExitCode=$code; Log=$log; AssetRows=[int](($counts.Values | Measure-Object -Sum).Sum)
            AssetCounts=$counts
        })
        Write-Host "[NUCLEAR-5V-PAYLOAD] OAT FF $ordinal: $($ff.Name) exit=$code types=$($counts.Count) log=$log"
    }
}
$audioFolders=@($wavFiles | Group-Object DirectoryName | Sort-Object Count -Descending | Select-Object -First 15 | ForEach-Object {
    [pscustomobject]@{ Folder=$_.Name; WavFiles=$_.Count; Bytes=[long](($_.Group | Measure-Object Length -Sum).Sum) }
})
$wavGroups=@($wavRows.ToArray() | Group-Object SampleRate,Channels,FormatCode | Sort-Object Count -Descending | ForEach-Object {
    $first=$_.Group[0]
    [pscustomobject]@{ SampleRate=$first.SampleRate; Channels=$first.Channels
        FormatCode=$first.FormatCode; Files=$_.Count }
})
$manifest=[ordered]@{
    format='WaW360.Nuclear5V.FRumansionDecompiledPayloadTriage.v1'
    sourceRoot=$source; totalFiles=$files.Count; waveFiles=$wavFiles.Count
    riffWaveContainers=$wavRiff; nonRiffWaveFiles=$wavNonRiff
    wavSummary=$wavGroups; wavFolders=$audioFolders
    missingImageIdentities=$images.Count
    specialFiles=[object[]]$specialRows.ToArray()
    fastfileLists=[object[]]$ffRows.ToArray()
    policy=@(
        'This is a local inventory, not proof of gameplay audio compatibility or Xbox codecs.',
        'WAV samples require legitimate conversion and Xbox 360 audio validation before production use.',
        'IWI filename matches are not accepted without semantic, source, and texture data validation.',
        'RMB and .source headers are recorded as unidentified until format is independently verified.',
        'No actual source bytes are copied into the production output and 4F blocker count remains unchanged.'
    )
}
$json=Join-Path $OutputRoot 'nuclear_5v_frumansion_decompiled_payload_triage.json'
$txt=Join-Path $OutputRoot 'nuclear_5v_frumansion_decompiled_payload_triage.txt'
$csv=Join-Path $OutputRoot 'nuclear_5v_wav_inventory.csv'
$specialCsv=Join-Path $OutputRoot 'nuclear_5v_special_files.csv'
$wavRows.ToArray() | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8
$specialRows.ToArray() | Export-Csv -LiteralPath $specialCsv -NoTypeInformation -Encoding UTF8
$manifest | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $json -Encoding UTF8
$report=New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR-5V FRUMANSION DECOMPILED PAYLOAD TRIAGE')
$report.Add('============================================================')
$report.Add("Source: $source")
$report.Add("All files: $($files.Count)")
$report.Add("WAV files: $($wavFiles.Count), RIFF/WAVE containers: $wavRiff, other: $wavNonRiff")
$report.Add('AUDIO FORMATS (fmt chunk at offset 12 only)')
foreach ($row in $wavGroups) { $report.Add("  $($row.Files) files: $($row.SampleRate) Hz, $($row.Channels) channel(s), format-code $($row.FormatCode)") }
$report.Add('LARGEST AUDIO FOLDERS')
foreach ($row in $audioFolders) { $report.Add("  $($row.WavFiles) WAV: $($row.Folder)") }
$report.Add('SPECIAL FILES (header fingerprints; no format guesses)')
foreach ($row in $specialRows) {
    $report.Add("  $($row.Extension) $($row.Name) [$($row.Bytes) bytes] imageRoot=$($row.MatchedMissingImageRoot)")
    $report.Add("    $($row.Path)")
    $report.Add("    ASCII: $($row.HeaderAscii)")
    $report.Add("    HEX: $($row.HeaderHex)")
}
foreach ($ff in $ffRows) {
    $report.Add("FASTFILE: $($ff.Path) exit=$($ff.ExitCode) assetRows=$($ff.AssetRows)")
    foreach ($kv in $ff.AssetCounts.GetEnumerator() | Sort-Object Key) {
        $report.Add("  $($kv.Key): $($kv.Value)")
    }
    $report.Add("  Full asset listing: $($ff.Log)")
}
$report.Add('No missing-image payload or converted Xbox audio is claimed from this census.')
[IO.File]::WriteAllLines($txt,[string[]]$report.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[NUCLEAR-5V-PAYLOAD] ================= RESULT ==============================='
Write-Host "[NUCLEAR-5V-PAYLOAD] WAV: $($wavFiles.Count) RIFF=$wavRiff non-RIFF=$wavNonRiff"
Write-Host "[NUCLEAR-5V-PAYLOAD] Source images (IWI): $(@($specialRows.ToArray() | Where-Object Extension -eq '.iwi').Count)"
foreach ($row in @($specialRows.ToArray() | Where-Object Extension -eq '.iwi')) {
    Write-Host "[NUCLEAR-5V-PAYLOAD]   IWI $($row.Name) => missing root #$($row.MatchedMissingImageRoot)"
}
Write-Host "[NUCLEAR-5V-PAYLOAD] RMB: $(@($specialRows.ToArray() | Where-Object Extension -eq '.rmb').Count); FF: $(@($ffRows.ToArray()).Count)"
Write-Host "[NUCLEAR-5V-PAYLOAD] Report: $txt"
Write-Host "[NUCLEAR-5V-PAYLOAD] JSON:   $json"
Write-Host "[NUCLEAR-5V-PAYLOAD] WAV CSV: $csv"
Write-Host "[NUCLEAR-5V-PAYLOAD] Special file CSV: $specialCsv"
Write-Host '[NUCLEAR-5V-PAYLOAD] READ-ONLY PASS: no payload grafts; 4F unchanged.'
