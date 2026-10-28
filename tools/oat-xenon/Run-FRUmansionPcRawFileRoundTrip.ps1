[CmdletBinding()]
param(
    [string]$PcMapsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps',
    [string]$MapName = 'nazi_zombie_FRUmansion',
    [string]$NativeIdentityManifest = '',
    [string]$CriticalReferenceManifest = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$linker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Linker.exe'
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($NativeIdentityManifest)) {
    $NativeIdentityManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_root_binding\native_identity\nazi_zombie_FRUmansion_native_identity_manifest.json'
}
if ([string]::IsNullOrWhiteSpace($CriticalReferenceManifest)) {
    $CriticalReferenceManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4w_xbox_reference_proof\nuclear_4w_xbox_reference_proof.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_5i_pc_rawfile_roundtrip'
}

foreach ($required in @($linker,$unlinker,$NativeIdentityManifest,$CriticalReferenceManifest)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required Stage 5I input not found: $required"
    }
}

$mapDir = Join-Path $PcMapsRoot $MapName
if (-not (Test-Path -LiteralPath $mapDir -PathType Container)) {
    throw "PC map directory not found: $mapDir"
}

function Invoke-NativeCapture {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][object[]]$Arguments,
        [Parameter(Mandatory=$true)][string]$LogPath
    )

    $old=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        $lines=@(& $FilePath @Arguments 2>&1 | ForEach-Object { [string]$_ })
        $code=[int]$LASTEXITCODE
    }
    finally {
        $ErrorActionPreference=$old
    }

    [IO.File]::WriteAllLines($LogPath,[string[]]$lines,[Text.UTF8Encoding]::new($false))
    return [pscustomobject]@{ Code=$code; Lines=[string[]]$lines }
}

function Strip-LogPrefix {
    param([string]$Line)
    if ($null -eq $Line) { return '' }
    $text=$Line.Trim()
    if ($text.StartsWith('[',[StringComparison]::Ordinal)) {
        $close=$text.IndexOf(']')
        if ($close -ge 0 -and $close+1 -lt $text.Length) {
            $text=$text.Substring($close+1).TrimStart()
        }
    }
    return $text
}

function Normalize-Name {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    $v=$Value.Trim().Trim('"').Replace('\','/')
    while ($v.StartsWith(',',[StringComparison]::Ordinal)) { $v=$v.Substring(1) }
    return $v.ToLowerInvariant()
}

function Resolve-DumpedRawFile {
    param(
        [Parameter(Mandatory=$true)][string]$DumpRoot,
        [Parameter(Mandatory=$true)][string]$AssetName
    )

    $relative=$AssetName.Replace('/','\').TrimStart('\')
    $exact=Join-Path $DumpRoot $relative
    if (Test-Path -LiteralPath $exact -PathType Leaf) {
        return (Resolve-Path -LiteralPath $exact).Path
    }

    $leaf=[IO.Path]::GetFileName($relative)
    if ([string]::IsNullOrWhiteSpace($leaf)) { return $null }

    $matches=@(
        Get-ChildItem -LiteralPath $DumpRoot -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name.Equals($leaf,[StringComparison]::OrdinalIgnoreCase) -and
            (Normalize-Name -Value ($_.FullName.Substring($DumpRoot.Length).TrimStart('\'))) -eq (Normalize-Name -Value $AssetName)
        }
    )
    if ($matches.Count -eq 1) { return $matches[0].FullName }
    return $null
}

function Copy-RawAsset {
    param([string]$Source,[string]$Root,[string]$Name)
    $relative=$Name.Replace('/','\').TrimStart('\')
    $dest=Join-Path $Root $relative
    $parent=Split-Path -Parent $dest
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    Copy-Item -LiteralPath $Source -Destination $dest -Force
    return $dest
}

$identity=Get-Content -LiteralPath $NativeIdentityManifest -Raw | ConvertFrom-Json
if ([string]$identity.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or -not [bool]$identity.typeSequenceVerified) {
    throw 'Stage 5I requires a verified native identity manifest.'
}

$reference=Get-Content -LiteralPath $CriticalReferenceManifest -Raw | ConvertFrom-Json
if ([string]$reference.format -ne 'WaW360.Nuclear4W.XboxRuntimeReferenceProof.v1' -or -not [bool]$reference.referenceProofPass) {
    throw 'Stage 5I requires the passing Stage 4W retail reference proof.'
}

$retailRawIndices=[System.Collections.Generic.HashSet[int]]::new()
foreach ($row in @($reference.resolved)) {
    if ([string]$row.Type -ieq 'rawfile') {
        [void]$retailRawIndices.Add([int]$row.Index)
    }
}
if ($retailRawIndices.Count -ne 46) {
    throw "Stage 5I expected 46 retail RawFile references; found $($retailRawIndices.Count)."
}

$nativeRaw=@(
    $identity.entries |
    Where-Object {
        ([string]$_.TypeName -ieq 'rawfile') -or
        ([string]$_.OatTypeName -ieq 'rawfile')
    } |
    Sort-Object Index
)
if ($nativeRaw.Count -ne 54) {
    throw "Stage 5I expected 54 native RawFile roots; found $($nativeRaw.Count)."
}

$unresolvedNative=@(
    $nativeRaw |
    Where-Object { -not $retailRawIndices.Contains([int]$_.Index) }
)
if ($unresolvedNative.Count -ne 8) {
    throw "Stage 5I expected 8 non-retail native RawFile roots; found $($unresolvedNative.Count)."
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
$pcDumpRoot=Join-Path $OutputRoot 'pc_rawfile_dumps'
$logRoot=Join-Path $OutputRoot 'logs'
$workspace=Join-Path $OutputRoot 'workspace'
$sourceRoot=Join-Path $workspace 'zone_source'
$assetRoot=Join-Path $workspace 'raw'
$linkOut=Join-Path $OutputRoot 'linked'
$readbackRoot=Join-Path $OutputRoot 'readback'
foreach ($p in @($OutputRoot,$pcDumpRoot,$logRoot,$workspace,$sourceRoot,$assetRoot,$linkOut,$readbackRoot)) {
    New-Item -ItemType Directory -Force -Path $p | Out-Null
}

$pcFastFiles=@(
    Get-ChildItem -LiteralPath $mapDir -Filter '*.ff' -File -Recurse -ErrorAction Stop |
    Sort-Object @{Expression={if ($_.Name -match '(?i)_load[.]ff$') {1}else{0}}}, FullName
)
if ($pcFastFiles.Count -eq 0) {
    throw "No PC fastfiles found under: $mapDir"
}

$pcRows=New-Object 'System.Collections.Generic.List[object]'
$pcByName=@{}
$listFailures=0
$dumpFailures=0

Write-Host '[NUCLEAR-5I-RAW] ============================================================'
Write-Host '[NUCLEAR-5I-RAW] AUTHORITATIVE PC RAWFILE CORPUS + XENON ROUNDTRIP'
Write-Host '[NUCLEAR-5I-RAW] ============================================================'
Write-Host "[NUCLEAR-5I-RAW] PC fastfiles          : $($pcFastFiles.Count)"
Write-Host "[NUCLEAR-5I-RAW] Native RawFiles       : $($nativeRaw.Count)"
Write-Host "[NUCLEAR-5I-RAW] Retail RawFile refs   : $($retailRawIndices.Count)"
Write-Host "[NUCLEAR-5I-RAW] Non-retail native     : $($unresolvedNative.Count)"
Write-Host ''

$ffOrdinal=0
foreach ($ff in $pcFastFiles) {
    $ffOrdinal++
    $safe=($ff.BaseName -replace '[^A-Za-z0-9_.-]','_')
    $listLog=Join-Path $logRoot ("{0:D2}_{1}.list.log" -f $ffOrdinal,$safe)
    $dumpLog=Join-Path $logRoot ("{0:D2}_{1}.dump.log" -f $ffOrdinal,$safe)
    $dumpRoot=Join-Path $pcDumpRoot ("{0:D2}_{1}" -f $ffOrdinal,$safe)
    New-Item -ItemType Directory -Force -Path $dumpRoot | Out-Null

    $listed=Invoke-NativeCapture -FilePath $unlinker -Arguments @('--list','--no-color',$ff.FullName) -LogPath $listLog
    if ([int]$listed.Code -ne 0) { $listFailures++ }

    $names=New-Object 'System.Collections.Generic.List[string]'
    foreach ($raw in $listed.Lines) {
        $line=Strip-LogPrefix -Line $raw
        if ($line -match '(?i)^rawfile\s*,\s*(.+?)\s*$') {
            $names.Add(([string]$matches[1]).Trim().Trim('"'))
        }
    }

    $dumped=Invoke-NativeCapture -FilePath $unlinker -Arguments @(
        '--no-color','--include-assets','rawfile','--output-folder',$dumpRoot,$ff.FullName
    ) -LogPath $dumpLog
    if ([int]$dumped.Code -ne 0) { $dumpFailures++ }

    $resolvedBodies=0
    foreach ($name in $names.ToArray()) {
        $path=Resolve-DumpedRawFile -DumpRoot $dumpRoot -AssetName $name
        $exists=-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path -PathType Leaf)
        $hash=''
        $bytes=0L
        if ($exists) {
            $resolvedBodies++
            $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            $bytes=[int64](Get-Item -LiteralPath $path).Length
        }

        $row=[pscustomobject]@{
            FastFile=$ff.FullName
            FastFileName=$ff.Name
            Name=$name
            Key=Normalize-Name -Value $name
            DumpPath=$path
            BodyPresent=$exists
            Bytes=$bytes
            Sha256=$hash
        }
        $pcRows.Add($row)

        $key=[string]$row.Key
        if (-not $pcByName.ContainsKey($key)) {
            $pcByName[$key]=New-Object 'System.Collections.Generic.List[object]'
        }
        $pcByName[$key].Add($row)
    }

    Write-Host ("[NUCLEAR-5I-RAW] {0,-34} listed={1,3} bodies={2,3} listExit={3} dumpExit={4}" -f
        $ff.Name,$names.Count,$resolvedBodies,[int]$listed.Code,[int]$dumped.Code)
}

$mapping=New-Object 'System.Collections.Generic.List[object]'
$exactMapped=0
$missingNative=0
$ambiguousSource=0

foreach ($native in $unresolvedNative) {
    $key=Normalize-Name -Value ([string]$native.Name)
    $sources=@()
    if ($pcByName.ContainsKey($key)) {
        $sources=@($pcByName[$key].ToArray() | Where-Object { [bool]$_.BodyPresent })
    }

    $status='missing-pc-body'
    if ($sources.Count -eq 1) {
        $status='exact-pc-body'
        $exactMapped++
    }
    elseif ($sources.Count -gt 1) {
        $hashes=@($sources | Select-Object -ExpandProperty Sha256 -Unique)
        if ($hashes.Count -eq 1) {
            $status='duplicate-identical-pc-body'
            $exactMapped++
            $sources=@($sources | Select-Object -First 1)
        }
        else {
            $status='ambiguous-pc-body'
            $ambiguousSource++
        }
    }
    else {
        $missingNative++
    }

    $mapping.Add([pscustomobject]@{
        Index=[int]$native.Index
        Name=[string]$native.Name
        Status=$status
        Sources=[object[]]$sources
    })
}

$linkTargets=@(
    $mapping.ToArray() |
    Where-Object { $_.Status -in @('exact-pc-body','duplicate-identical-pc-body') } |
    Sort-Object Index
)

$project='nuclear_5i_pc_rawfile_roundtrip'
$zoneFile=Join-Path $sourceRoot ($project+'.zone')
$zoneLines=New-Object 'System.Collections.Generic.List[string]'
$zoneLines.Add('>game,T4')
$zoneLines.Add('>platform,xbox360')
$zoneLines.Add(('>name,{0}' -f $project))
$zoneLines.Add('')

foreach ($target in $linkTargets) {
    $source=@($target.Sources)[0]
    [void](Copy-RawAsset -Source ([string]$source.DumpPath) -Root $assetRoot -Name ([string]$target.Name))
    $escaped=([string]$target.Name).Replace('"','""')
    $zoneLines.Add(('rawfile,"{0}"' -f $escaped))
}
[IO.File]::WriteAllLines($zoneFile,[string[]]$zoneLines.ToArray(),[Text.UTF8Encoding]::new($false))

$linkCode=-1
$readbackCode=-1
$proofFf=''
$readbackRows=New-Object 'System.Collections.Generic.List[object]'

if ($linkTargets.Count -gt 0) {
    $linkLog=Join-Path $logRoot 'linker.log'
    $link=Invoke-NativeCapture -FilePath $linker -Arguments @(
        '--verbose','--no-color',
        '--base-folder',$workspace,
        '--source-search-path',$sourceRoot,
        '--asset-search-path',$assetRoot,
        '--output-folder',$linkOut,
        $project
    ) -LogPath $linkLog
    $linkCode=[int]$link.Code

    $linked=@(Get-ChildItem -LiteralPath $linkOut -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue)
    if ($linkCode -eq 0 -and $linked.Count -eq 1) {
        $proofFf=$linked[0].FullName
        $readbackLog=Join-Path $logRoot 'readback.log'
        $read=Invoke-NativeCapture -FilePath $unlinker -Arguments @(
            '--no-color','--include-assets','rawfile','--output-folder',$readbackRoot,$proofFf
        ) -LogPath $readbackLog
        $readbackCode=[int]$read.Code

        foreach ($target in $linkTargets) {
            $src=@($target.Sources)[0]
            $roundPath=Resolve-DumpedRawFile -DumpRoot $readbackRoot -AssetName ([string]$target.Name)
            $roundExists=-not [string]::IsNullOrWhiteSpace($roundPath) -and (Test-Path -LiteralPath $roundPath -PathType Leaf)
            $roundHash=$(if ($roundExists) { (Get-FileHash -LiteralPath $roundPath -Algorithm SHA256).Hash.ToLowerInvariant() } else { '' })
            $exact=$roundExists -and $roundHash -eq [string]$src.Sha256
            $readbackRows.Add([pscustomobject]@{
                Index=[int]$target.Index
                Name=[string]$target.Name
                SourcePath=[string]$src.DumpPath
                SourceBytes=[int64]$src.Bytes
                SourceSha256=[string]$src.Sha256
                ReadbackPath=$roundPath
                ReadbackSha256=$roundHash
                ExactRoundTrip=$exact
            })
        }
    }
}

$exactRoundTrips=@($readbackRows.ToArray() | Where-Object { [bool]$_.ExactRoundTrip }).Count
$roundTripPass=
    $linkTargets.Count -gt 0 -and
    $linkCode -eq 0 -and
    $readbackCode -eq 0 -and
    $exactRoundTrips -eq $linkTargets.Count

$manifest=[ordered]@{
    format='WaW360.Nuclear5I.PcRawFileRoundTrip.v1'
    nativeIdentityManifest=(Resolve-Path -LiteralPath $NativeIdentityManifest).Path
    criticalReferenceManifest=(Resolve-Path -LiteralPath $CriticalReferenceManifest).Path
    pcFastFiles=$pcFastFiles.Count
    pcRawFileRows=$pcRows.Count
    pcRawFileBodies=@($pcRows.ToArray() | Where-Object BodyPresent).Count
    listFailures=$listFailures
    dumpFailures=$dumpFailures
    nativeRawFiles=$nativeRaw.Count
    retailReferenceRawFiles=$retailRawIndices.Count
    nonRetailNativeRawFiles=$unresolvedNative.Count
    exactPcBodyMappings=$exactMapped
    missingPcBodyMappings=$missingNative
    ambiguousPcBodyMappings=$ambiguousSource
    standaloneTargets=$linkTargets.Count
    linkerExitCode=$linkCode
    readbackExitCode=$readbackCode
    exactRoundTrips=$exactRoundTrips
    proofFastFile=$proofFf
    roundTripPass=$roundTripPass
    unresolvedNative=[object[]]$mapping.ToArray()
    rawFiles=[object[]]$readbackRows.ToArray()
    policy=@(
        'Stage 5I uses OAT asset loading/dumping to obtain authoritative PC RawFile identities and bodies, bypassing global raw-byte signature ambiguity.',
        'The 46 Stage 4W retail RawFile roots remain runtime references and are not copied from PC.',
        'Only exact native identity matches with an OAT-dumped PC body are eligible for standalone Xenon RawFile serialization.',
        'Production credit requires exact source/readback SHA-256 equality after standalone Xbox360 link and reload.',
        'No native/oracle RawFile payload bytes are copied.'
    )
}

$jsonPath=Join-Path $OutputRoot 'nuclear_5i_pc_rawfile_roundtrip.json'
$txtPath=Join-Path $OutputRoot 'nuclear_5i_pc_rawfile_roundtrip.txt'
$manifest | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report=New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 5I - AUTHORITATIVE PC RAWFILE CORPUS + XENON ROUNDTRIP')
$report.Add(('=' * 108))
$report.Add(("PC fastfiles:                     {0:N0}" -f $pcFastFiles.Count))
$report.Add(("PC RawFile identities:            {0:N0}" -f $pcRows.Count))
$report.Add(("PC RawFile bodies dumped:         {0:N0}" -f @($pcRows.ToArray() | Where-Object BodyPresent).Count))
$report.Add(("Native RawFiles:                  {0:N0}" -f $nativeRaw.Count))
$report.Add(("Retail-reference RawFiles:        {0:N0}" -f $retailRawIndices.Count))
$report.Add(("Non-retail native RawFiles:       {0:N0}" -f $unresolvedNative.Count))
$report.Add(("Exact PC body mappings:           {0:N0}" -f $exactMapped))
$report.Add(("Missing PC body mappings:         {0:N0}" -f $missingNative))
$report.Add(("Ambiguous PC body mappings:       {0:N0}" -f $ambiguousSource))
$report.Add(("Standalone Xenon targets:         {0:N0}" -f $linkTargets.Count))
$report.Add(("Exact standalone round-trips:     {0:N0}" -f $exactRoundTrips))
$report.Add(("ROUNDTRIP PASS:                   {0}" -f $roundTripPass))
$report.Add('')
$report.Add('NON-RETAIL NATIVE RAWFILES')
foreach ($row in $mapping.ToArray()) {
    $report.Add(("  root #{0,4}: {1} -> {2}" -f $row.Index,$row.Name,$row.Status))
    foreach ($src in @($row.Sources)) {
        $report.Add(("           PC <- {0} | {1:N0} B | {2}" -f $src.FastFileName,$src.Bytes,$src.Sha256))
    }
}
$report.Add('')
$report.Add('ROUNDTRIPS')
foreach ($row in $readbackRows.ToArray()) {
    $report.Add(("  root #{0,4}: {1} exact={2} bytes={3:N0}" -f $row.Index,$row.Name,$row.ExactRoundTrip,$row.SourceBytes))
}
[IO.File]::WriteAllLines($txtPath,[string[]]$report.ToArray(),[Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-5I-RAW] ====================== RESULT ==============================='
Write-Host "[NUCLEAR-5I-RAW] PC RawFile identities        : $($pcRows.Count)"
Write-Host "[NUCLEAR-5I-RAW] PC RawFile bodies dumped     : $(@($pcRows.ToArray() | Where-Object BodyPresent).Count)"
Write-Host "[NUCLEAR-5I-RAW] Non-retail native RawFiles   : $($unresolvedNative.Count)"
Write-Host "[NUCLEAR-5I-RAW] Exact PC body mappings       : $exactMapped"
Write-Host "[NUCLEAR-5I-RAW] Missing PC body mappings     : $missingNative"
Write-Host "[NUCLEAR-5I-RAW] Ambiguous PC body mappings   : $ambiguousSource"
Write-Host "[NUCLEAR-5I-RAW] Standalone Xenon targets     : $($linkTargets.Count)"
Write-Host "[NUCLEAR-5I-RAW] Linker exit                  : $linkCode"
Write-Host "[NUCLEAR-5I-RAW] Readback exit                : $readbackCode"
Write-Host "[NUCLEAR-5I-RAW] Exact standalone round-trips : $exactRoundTrips"
foreach ($row in $mapping.ToArray()) {
    Write-Host ("[NUCLEAR-5I-RAW]   root #{0}: {1} -> {2}" -f $row.Index,$row.Name,$row.Status)
}
Write-Host "[NUCLEAR-5I-RAW] Manifest                     : $jsonPath"
Write-Host "[NUCLEAR-5I-RAW] Report                       : $txtPath"
Write-Host '[NUCLEAR-5I-RAW] ================================================================='
if ($roundTripPass) {
    Write-Host '[NUCLEAR-5I-RAW] PASS: all exact PC-backed RawFile targets survived standalone Xenon round-trip.' -ForegroundColor Green
}
else {
    Write-Host '[NUCLEAR-5I-RAW] ANALYSIS COMPLETE: some non-retail RawFile roots still require identity/source closure.' -ForegroundColor Yellow
}
