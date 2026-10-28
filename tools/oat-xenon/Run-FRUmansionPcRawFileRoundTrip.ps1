[CmdletBinding()]
param(
    [string]$PcMapsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps',
    [string]$MapName = 'nazi_zombie_FRUmansion',
    [string]$NativeIdentityManifest = '',
    [string]$CriticalReferenceManifest = '',
    [string]$NativeOracleFastFile = 'C:\Users\jorda\OneDrive\Desktop\working frumansion\nazi_zombie_FRUmansion.ff',
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

foreach ($required in @($linker,$unlinker,$NativeIdentityManifest,$CriticalReferenceManifest,$NativeOracleFastFile)) {
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

function Get-ByteHash {
    param([byte[]]$Bytes)
    $sha=[Security.Cryptography.SHA256]::Create()
    try {
        $digest=$sha.ComputeHash($Bytes)
    }
    finally {
        $sha.Dispose()
    }
    return (($digest | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Get-RawCanonicalFingerprints {
    param([Parameter(Mandatory=$true)][string]$Path)

    $bytes=[IO.File]::ReadAllBytes($Path)
    $end=$bytes.Length
    while ($end -gt 0 -and $bytes[$end-1] -eq 0) { $end-- }

    $trimmed=New-Object byte[] $end
    if ($end -gt 0) { [Array]::Copy($bytes,0,$trimmed,0,$end) }

    $noNullHash=Get-ByteHash -Bytes $trimmed

    $text=''
    $textValid=$true
    try {
        $utf8=New-Object Text.UTF8Encoding($false,$true)
        $text=$utf8.GetString($trimmed)
    }
    catch {
        $textValid=$false
    }

    if (-not $textValid) {
        return [pscustomobject]@{
            TextValid=$false
            NoTrailingNullSha256=$noNullHash
            EolNormalizedSha256=''
            TrimmedLinesSha256=''
            WhitespaceFoldedSha256=''
        }
    }

    if ($text.Length -gt 0 -and [int][char]$text[0] -eq 0xFEFF) {
        $text=$text.Substring(1)
    }

    $crlf=([string][char]13)+([string][char]10)
    $lf=[string][char]10
    $cr=[string][char]13
    $eol=$text.Replace($crlf,$lf).Replace($cr,$lf)
    $eolHash=Get-ByteHash -Bytes ([Text.Encoding]::UTF8.GetBytes($eol))

    $lines=@($eol.Split([char]10))
    $clean=New-Object 'System.Collections.Generic.List[string]'
    foreach ($line in $lines) {
        $clean.Add($line.Trim())
    }
    while ($clean.Count -gt 0 -and [string]::IsNullOrWhiteSpace($clean[$clean.Count-1])) {
        $clean.RemoveAt($clean.Count-1)
    }
    $trimmedLines=($clean.ToArray() -join $lf)
    $trimmedHash=Get-ByteHash -Bytes ([Text.Encoding]::UTF8.GetBytes($trimmedLines))

    $folded=[Text.RegularExpressions.Regex]::Replace($eol,'\s+',' ').Trim()
    $foldedHash=Get-ByteHash -Bytes ([Text.Encoding]::UTF8.GetBytes($folded))

    return [pscustomobject]@{
        TextValid=$true
        NoTrailingNullSha256=$noNullHash
        EolNormalizedSha256=$eolHash
        TrimmedLinesSha256=$trimmedHash
        WhitespaceFoldedSha256=$foldedHash
    }
}

function Add-CanonicalIndex {
    param(
        [hashtable]$Index,
        [string]$Hash,
        [object]$Row
    )
    if ([string]::IsNullOrWhiteSpace($Hash)) { return }
    if (-not $Index.ContainsKey($Hash)) {
        $Index[$Hash]=New-Object 'System.Collections.Generic.List[object]'
    }
    $Index[$Hash].Add($Row)
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
$nativeDumpRoot=Join-Path $OutputRoot 'native_rawfile_validation_dump'
$logRoot=Join-Path $OutputRoot 'logs'
$workspace=Join-Path $OutputRoot 'workspace'
$sourceRoot=Join-Path $workspace 'zone_source'
$assetRoot=Join-Path $workspace 'raw'
$linkOut=Join-Path $OutputRoot 'linked'
$readbackRoot=Join-Path $OutputRoot 'readback'
foreach ($p in @($OutputRoot,$pcDumpRoot,$nativeDumpRoot,$logRoot,$workspace,$sourceRoot,$assetRoot,$linkOut,$readbackRoot)) {
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
$pcByHash=@{}
$pcByNoNullHash=@{}
$pcByEolHash=@{}
$pcByTrimmedLinesHash=@{}
$pcByWhitespaceHash=@{}
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
        $canonical=$null
        if ($exists) {
            $resolvedBodies++
            $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            $bytes=[int64](Get-Item -LiteralPath $path).Length
            $canonical=Get-RawCanonicalFingerprints -Path $path
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
            Canonical=$canonical
        }
        $pcRows.Add($row)

        $key=[string]$row.Key
        if (-not $pcByName.ContainsKey($key)) {
            $pcByName[$key]=New-Object 'System.Collections.Generic.List[object]'
        }
        $pcByName[$key].Add($row)

        if ($exists -and -not [string]::IsNullOrWhiteSpace($hash)) {
            if (-not $pcByHash.ContainsKey($hash)) {
                $pcByHash[$hash]=New-Object 'System.Collections.Generic.List[object]'
            }
            $pcByHash[$hash].Add($row)
            Add-CanonicalIndex -Index $pcByNoNullHash -Hash ([string]$canonical.NoTrailingNullSha256) -Row $row
            if ([bool]$canonical.TextValid) {
                Add-CanonicalIndex -Index $pcByEolHash -Hash ([string]$canonical.EolNormalizedSha256) -Row $row
                Add-CanonicalIndex -Index $pcByTrimmedLinesHash -Hash ([string]$canonical.TrimmedLinesSha256) -Row $row
                Add-CanonicalIndex -Index $pcByWhitespaceHash -Hash ([string]$canonical.WhitespaceFoldedSha256) -Row $row
            }
        }
    }

    Write-Host ("[NUCLEAR-5I-RAW] {0,-34} listed={1,3} bodies={2,3} listExit={3} dumpExit={4}" -f
        $ff.Name,$names.Count,$resolvedBodies,[int]$listed.Code,[int]$dumped.Code)
}


$nativeDumpLog=Join-Path $logRoot 'native_rawfile_validation.dump.log'
$nativeDump=Invoke-NativeCapture -FilePath $unlinker -Arguments @(
    '--no-color','--include-assets','rawfile','--output-folder',$nativeDumpRoot,$NativeOracleFastFile
) -LogPath $nativeDumpLog

$nativeDumpExit=[int]$nativeDump.Code
$nativeBodyByIndex=@{}
$nativeValidationBodies=0
foreach ($native in $unresolvedNative) {
    $path=Resolve-DumpedRawFile -DumpRoot $nativeDumpRoot -AssetName ([string]$native.Name)
    $present=-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path -PathType Leaf)
    $hash=''
    $bytes=0L
    $canonical=$null
    if ($present) {
        $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        $bytes=[int64](Get-Item -LiteralPath $path).Length
        $canonical=Get-RawCanonicalFingerprints -Path $path
        $nativeValidationBodies++
    }

    $nativeBodyByIndex[[int]$native.Index]=[pscustomobject]@{
        Index=[int]$native.Index
        Name=[string]$native.Name
        DumpPath=$path
        BodyPresent=$present
        Bytes=$bytes
        Sha256=$hash
        Canonical=$canonical
    }
}

$mapping=New-Object 'System.Collections.Generic.List[object]'
$exactMapped=0
$hashMapped=0
$canonicalNoNullMapped=0
$canonicalEolMapped=0
$canonicalTrimmedLinesMapped=0
$canonicalWhitespaceMapped=0
$missingNative=0
$ambiguousSource=0

foreach ($native in $unresolvedNative) {
    $key=Normalize-Name -Value ([string]$native.Name)
    $sources=@()
    if ($pcByName.ContainsKey($key)) {
        $sources=@($pcByName[$key].ToArray() | Where-Object { [bool]$_.BodyPresent })
    }

    $nativeValidation=$nativeBodyByIndex[[int]$native.Index]
    $status='missing-pc-body'

    if ($sources.Count -eq 1) {
        $status='exact-name-pc-body'
        $exactMapped++
    }
    elseif ($sources.Count -gt 1) {
        $hashes=@($sources | Select-Object -ExpandProperty Sha256 -Unique)
        if ($hashes.Count -eq 1) {
            $status='duplicate-identical-name-pc-body'
            $exactMapped++
            $sources=@($sources | Select-Object -First 1)
        }
        else {
            $status='ambiguous-name-pc-body'
            $ambiguousSource++
        }
    }
    elseif ($null -ne $nativeValidation -and [bool]$nativeValidation.BodyPresent) {
        $canonical=[object]$nativeValidation.Canonical
        $matchedTier=''
        $candidateSources=@()

        if (-not [string]::IsNullOrWhiteSpace([string]$nativeValidation.Sha256) -and
            $pcByHash.ContainsKey([string]$nativeValidation.Sha256)) {
            $candidateSources=@($pcByHash[[string]$nativeValidation.Sha256].ToArray() | Where-Object { [bool]$_.BodyPresent })
            $matchedTier='exact-body-hash-pc-source'
        }
        elseif ($null -ne $canonical -and
                -not [string]::IsNullOrWhiteSpace([string]$canonical.NoTrailingNullSha256) -and
                $pcByNoNullHash.ContainsKey([string]$canonical.NoTrailingNullSha256)) {
            $candidateSources=@($pcByNoNullHash[[string]$canonical.NoTrailingNullSha256].ToArray() | Where-Object { [bool]$_.BodyPresent })
            $matchedTier='canonical-no-trailing-null-pc-source'
        }
        elseif ($null -ne $canonical -and [bool]$canonical.TextValid -and
                -not [string]::IsNullOrWhiteSpace([string]$canonical.EolNormalizedSha256) -and
                $pcByEolHash.ContainsKey([string]$canonical.EolNormalizedSha256)) {
            $candidateSources=@($pcByEolHash[[string]$canonical.EolNormalizedSha256].ToArray() | Where-Object { [bool]$_.BodyPresent })
            $matchedTier='canonical-eol-pc-source'
        }
        elseif ($null -ne $canonical -and [bool]$canonical.TextValid -and
                -not [string]::IsNullOrWhiteSpace([string]$canonical.TrimmedLinesSha256) -and
                $pcByTrimmedLinesHash.ContainsKey([string]$canonical.TrimmedLinesSha256)) {
            $candidateSources=@($pcByTrimmedLinesHash[[string]$canonical.TrimmedLinesSha256].ToArray() | Where-Object { [bool]$_.BodyPresent })
            $matchedTier='canonical-trimmed-lines-pc-source'
        }
        elseif ($null -ne $canonical -and [bool]$canonical.TextValid -and
                -not [string]::IsNullOrWhiteSpace([string]$canonical.WhitespaceFoldedSha256) -and
                $pcByWhitespaceHash.ContainsKey([string]$canonical.WhitespaceFoldedSha256)) {
            $candidateSources=@($pcByWhitespaceHash[[string]$canonical.WhitespaceFoldedSha256].ToArray() | Where-Object { [bool]$_.BodyPresent })
            $matchedTier='canonical-whitespace-pc-source'
        }

        if ($candidateSources.Count -gt 0) {
            $rawHashes=@($candidateSources | Select-Object -ExpandProperty Sha256 -Unique)
            if ($rawHashes.Count -eq 1) {
                $sources=@($candidateSources | Select-Object -First 1)
                $status=$matchedTier
                switch ($matchedTier) {
                    'exact-body-hash-pc-source' { $hashMapped++ }
                    'canonical-no-trailing-null-pc-source' { $canonicalNoNullMapped++ }
                    'canonical-eol-pc-source' { $canonicalEolMapped++ }
                    'canonical-trimmed-lines-pc-source' { $canonicalTrimmedLinesMapped++ }
                    'canonical-whitespace-pc-source' { $canonicalWhitespaceMapped++ }
                }
            }
            else {
                $status='ambiguous-canonical-pc-source'
                $sources=$candidateSources
                $ambiguousSource++
            }
        }
        else {
            $missingNative++
        }
    }
    else {
        $missingNative++
    }

    $mapping.Add([pscustomobject]@{
        Index=[int]$native.Index
        Name=[string]$native.Name
        Status=$status
        NativeValidation=$nativeValidation
        Sources=[object[]]$sources
    })
}

$linkTargets=@(
    $mapping.ToArray() |
    Where-Object { $_.Status -in @(
        'exact-name-pc-body',
        'duplicate-identical-name-pc-body',
        'exact-body-hash-pc-source',
        'canonical-no-trailing-null-pc-source',
        'canonical-eol-pc-source',
        'canonical-trimmed-lines-pc-source',
        'canonical-whitespace-pc-source'
    ) } |
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
    exactBodyHashMappings=$hashMapped
    canonicalNoTrailingNullMappings=$canonicalNoNullMapped
    canonicalEolMappings=$canonicalEolMapped
    canonicalTrimmedLinesMappings=$canonicalTrimmedLinesMapped
    canonicalWhitespaceMappings=$canonicalWhitespaceMapped
    nativeValidationDumpExitCode=$nativeDumpExit
    nativeValidationBodies=$nativeValidationBodies
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
        'Exact native-name and exact byte-hash matches are preferred. When platform serialization differs, validation-only canonical fingerprints are attempted in strict order: trailing-NUL removal, EOL normalization, per-line trim, then whitespace folding.',
        'Canonical matches are accepted only when they resolve to one unique raw PC byte hash; ambiguous canonical collisions stay blocked.',
        'Any bound production payload is copied from the PC dump and emitted under the native Xbox identity; native/oracle body bytes are never copied.',
        'Production credit requires exact PC-source/readback SHA-256 equality after standalone Xbox360 link and reload.',
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
$report.Add(("Exact-name PC body mappings:      {0:N0}" -f $exactMapped))
$report.Add(("Exact body-hash mappings:         {0:N0}" -f $hashMapped))
$report.Add(("Canonical no-NUL mappings:        {0:N0}" -f $canonicalNoNullMapped))
$report.Add(("Canonical EOL mappings:           {0:N0}" -f $canonicalEolMapped))
$report.Add(("Canonical trimmed-line mappings:  {0:N0}" -f $canonicalTrimmedLinesMapped))
$report.Add(("Canonical whitespace mappings:    {0:N0}" -f $canonicalWhitespaceMapped))
$report.Add(("Native validation bodies:         {0:N0}/8" -f $nativeValidationBodies))
$report.Add(("Native validation dump exit:      {0}" -f $nativeDumpExit))
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
Write-Host "[NUCLEAR-5I-RAW] Exact-name PC body mappings  : $exactMapped"
Write-Host "[NUCLEAR-5I-RAW] Exact body-hash mappings     : $hashMapped"
Write-Host "[NUCLEAR-5I-RAW] Canonical no-NUL mappings    : $canonicalNoNullMapped"
Write-Host "[NUCLEAR-5I-RAW] Canonical EOL mappings       : $canonicalEolMapped"
Write-Host "[NUCLEAR-5I-RAW] Canonical trimmed mappings   : $canonicalTrimmedLinesMapped"
Write-Host "[NUCLEAR-5I-RAW] Canonical whitespace mappings: $canonicalWhitespaceMapped"
Write-Host "[NUCLEAR-5I-RAW] Native validation bodies     : $nativeValidationBodies/8"
Write-Host "[NUCLEAR-5I-RAW] Native validation dump exit  : $nativeDumpExit"
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
