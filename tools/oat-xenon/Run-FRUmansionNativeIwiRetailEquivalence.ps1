[CmdletBinding()]
param(
    [string]$Stage5EManifest = '',
    [string]$NativeClosureManifest = '',
    [string]$NativeDumpRoot = '',
    [string]$RetailDumpRoot = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($Stage5EManifest)) {
    $Stage5EManifest=Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_5e_frumansion_pc_ff_identity_census\nuclear_5e_frumansion_pc_ff_identity_census.json'
}
if ([string]::IsNullOrWhiteSpace($NativeClosureManifest)) {
    $NativeClosureManifest=Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4k_native_image_closure\nuclear_4k_native_image_closure.json'
}
if ([string]::IsNullOrWhiteSpace($NativeDumpRoot)) {
    $NativeDumpRoot=Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4j_native_image_mass\dump'
}
if ([string]::IsNullOrWhiteSpace($RetailDumpRoot)) {
    $RetailDumpRoot=Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_xbox_retail_deep_image_search\zone_dumps'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot=Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_5g_native_iwi_retail_equivalence'
}

foreach ($required in @($Stage5EManifest,$NativeClosureManifest)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required manifest not found: $required"
    }
}
foreach ($root in @($NativeDumpRoot,$RetailDumpRoot)) {
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "Required dump root not found: $root"
    }
}

function Normalize-Identity {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    $v=$Value.Trim().Trim('"').Replace('\','/').TrimStart('/')
    while ($v.StartsWith(',', [StringComparison]::Ordinal)) { $v=$v.Substring(1) }
    if ($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)) {
        $v=$v.Substring(0,$v.Length-3)
    }
    return $v.ToLowerInvariant()
}

function Read-IwiHeader {
    param([string]$Path)

    $bytes=New-Object byte[] 28
    $stream=[IO.File]::OpenRead($Path)
    try { $read=$stream.Read($bytes,0,$bytes.Length) }
    finally { $stream.Dispose() }

    if ($read -lt 12 -or
        $bytes[0] -ne [byte][char]'I' -or
        $bytes[1] -ne [byte][char]'W' -or
        $bytes[2] -ne [byte][char]'i') {
        return [pscustomobject]@{ Valid=$false; Version=0; Format=0; Flags=0; Width=0; Height=0; Depth=0 }
    }

    return [pscustomobject]@{
        Valid=$true
        Version=[int]$bytes[3]
        Format=[int]$bytes[4]
        Flags=[int]$bytes[5]
        Width=[BitConverter]::ToUInt16($bytes,6)
        Height=[BitConverter]::ToUInt16($bytes,8)
        Depth=[BitConverter]::ToUInt16($bytes,10)
    }
}

function Test-HeaderEqual {
    param([object]$A,[object]$B)
    return (
        [bool]$A.Valid -and [bool]$B.Valid -and
        [int]$A.Version -eq [int]$B.Version -and
        [int]$A.Format -eq [int]$B.Format -and
        [int]$A.Flags -eq [int]$B.Flags -and
        [int]$A.Width -eq [int]$B.Width -and
        [int]$A.Height -eq [int]$B.Height -and
        [int]$A.Depth -eq [int]$B.Depth
    )
}

$stage5e=Get-Content -LiteralPath $Stage5EManifest -Raw | ConvertFrom-Json
$closure=Get-Content -LiteralPath $NativeClosureManifest -Raw | ConvertFrom-Json

if ([string]$stage5e.format -ne 'WaW360.Nuclear5E.FRumansionPcFastFileIdentityCensus.v1' -or
    [int]$stage5e.targets -ne 43 -or
    [int]$stage5e.foundInFrumansionPcFastFiles -ne 43) {
    throw 'Stage 5G requires the passing 43/43 Stage 5E manifest.'
}
if ([string]$closure.format -ne 'WaW360.Nuclear4K.NativeImageClosure.v1') {
    throw 'Stage 5G requires the Stage 4K native image closure manifest.'
}

$closureByKey=@{}
foreach ($row in @($closure.largestMatched)) {
    $key=Normalize-Identity -Value ([string]$row.NativeName)
    if (-not [string]::IsNullOrWhiteSpace($key)) {
        $closureByKey[$key]=$row
    }
}

# largestMatched is intentionally capped. Build a full lookup directly from the 4J dump
# and native identity rows in 4K when a target is not represented in that summary.
$nativeFiles=@(
    Get-ChildItem -LiteralPath $NativeDumpRoot -Filter '*.iwi' -File -Recurse -ErrorAction SilentlyContinue |
    Sort-Object FullName
)

$nativeByRelativeIdentity=@{}
foreach ($file in $nativeFiles) {
    $root=[IO.Path]::GetFullPath($NativeDumpRoot)
    if (-not $root.EndsWith('\')) { $root += '\' }
    $full=[IO.Path]::GetFullPath($file.FullName)
    $rel=$(if ($full.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)) { $full.Substring($root.Length) } else { $file.Name })
    $rel=$rel.Replace('\','/')
    if ($rel.StartsWith('images/',[StringComparison]::OrdinalIgnoreCase)) { $rel=$rel.Substring(7) }
    if ($rel.EndsWith('.iwi',[StringComparison]::OrdinalIgnoreCase)) { $rel=$rel.Substring(0,$rel.Length-4) }
    $key=Normalize-Identity -Value $rel
    if (-not $nativeByRelativeIdentity.ContainsKey($key)) {
        $nativeByRelativeIdentity[$key]=New-Object 'System.Collections.Generic.List[object]'
    }
    $nativeByRelativeIdentity[$key].Add([pscustomobject]@{
        Name=$rel
        Path=$file.FullName
        Bytes=[int64]$file.Length
        Header=Read-IwiHeader -Path $file.FullName
        Hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    })
}

$retailByHash=@{}
$retailIwis=@(
    Get-ChildItem -LiteralPath $RetailDumpRoot -Filter '*.iwi' -File -Recurse -ErrorAction SilentlyContinue |
    Sort-Object FullName
)

Write-Host '[NUCLEAR-5G-IMG] ============================================================'
Write-Host '[NUCLEAR-5G-IMG] NATIVE IWI -> RETAIL XBOX EQUIVALENCE'
Write-Host '[NUCLEAR-5G-IMG] ============================================================'
Write-Host "[NUCLEAR-5G-IMG] Targets              : 43"
Write-Host "[NUCLEAR-5G-IMG] Native IWI files      : $($nativeFiles.Count)"
Write-Host "[NUCLEAR-5G-IMG] Retail IWI files      : $($retailIwis.Count)"
Write-Host '[NUCLEAR-5G-IMG] Oracle IWI bytes are fingerprint evidence only.'
Write-Host ''

foreach ($file in $retailIwis) {
    $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not $retailByHash.ContainsKey($hash)) {
        $retailByHash[$hash]=New-Object 'System.Collections.Generic.List[object]'
    }

    $retailByHash[$hash].Add([pscustomobject]@{
        Path=$file.FullName
        Name=[IO.Path]::GetFileNameWithoutExtension($file.Name)
        Bytes=[int64]$file.Length
        Header=Read-IwiHeader -Path $file.FullName
    })
}

$results=New-Object 'System.Collections.Generic.List[object]'
$nativeIwiFound=0
$uniqueNativeIwi=0
$ambiguousNativeIwi=0
$exactRetail=0
$uniqueRetail=0
$ambiguousRetail=0
$noRetail=0
$noNative=0

foreach ($target in @($stage5e.images | Sort-Object Index)) {
    $nativeName=[string]$target.NativeName
    $key=Normalize-Identity -Value $nativeName

    $nativeCandidates=@()
    if ($nativeByRelativeIdentity.ContainsKey($key)) {
        $nativeCandidates=@($nativeByRelativeIdentity[$key].ToArray())
    }

    if ($nativeCandidates.Count -eq 0) {
        $noNative++
        $results.Add([pscustomobject]@{
            Index=[int]$target.Index
            NativeName=$nativeName
            PcImage=[string]$target.PcImage
            Status='no-native-iwi-fingerprint'
            NativeCandidates=@()
            RetailCandidates=@()
        })
        continue
    }

    $nativeIwiFound++
    if ($nativeCandidates.Count -eq 1) { $uniqueNativeIwi++ } else { $ambiguousNativeIwi++ }

    $native=$nativeCandidates |
        Sort-Object @{Expression={$_.Bytes};Descending=$true},Path |
        Select-Object -First 1

    $retailHits=@()
    if ($retailByHash.ContainsKey([string]$native.Hash)) {
        $retailHits=@(
            $retailByHash[[string]$native.Hash].ToArray() |
            Where-Object { Test-HeaderEqual -A $native.Header -B $_.Header } |
            Sort-Object Path
        )
    }

    $status='no-retail-iwi-match'
    if ($retailHits.Count -gt 0) {
        $status='exact-retail-iwi-equivalent'
        $exactRetail++
        if ($retailHits.Count -eq 1) { $uniqueRetail++ } else { $ambiguousRetail++ }
    }
    else {
        $noRetail++
    }

    $results.Add([pscustomobject]@{
        Index=[int]$target.Index
        NativeName=$nativeName
        PcImage=[string]$target.PcImage
        Status=$status
        NativeCandidates=[object[]]$nativeCandidates
        ChosenNativeIwi=[pscustomobject]@{
            Path=[string]$native.Path
            Bytes=[int64]$native.Bytes
            Hash=[string]$native.Hash
            Header=$native.Header
        }
        RetailCandidates=[object[]]@(
            $retailHits |
            ForEach-Object {
                [pscustomobject]@{
                    Path=[string]$_.Path
                    Name=[string]$_.Name
                    Bytes=[int64]$_.Bytes
                    Header=$_.Header
                }
            }
        )
    })
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$manifest=[ordered]@{
    format='WaW360.Nuclear5G.NativeIwiRetailEquivalence.v1'
    stage5EManifest=(Resolve-Path -LiteralPath $Stage5EManifest).Path
    nativeClosureManifest=(Resolve-Path -LiteralPath $NativeClosureManifest).Path
    nativeDumpRoot=(Resolve-Path -LiteralPath $NativeDumpRoot).Path
    retailDumpRoot=(Resolve-Path -LiteralPath $RetailDumpRoot).Path
    targets=43
    nativeIwiFiles=$nativeFiles.Count
    retailIwiFiles=$retailIwis.Count
    nativeIwiFingerprintsFound=$nativeIwiFound
    uniqueNativeIwiFingerprints=$uniqueNativeIwi
    ambiguousNativeIwiFingerprints=$ambiguousNativeIwi
    noNativeIwiFingerprint=$noNative
    exactRetailIwiEquivalents=$exactRetail
    uniqueExactRetailIwiEquivalents=$uniqueRetail
    ambiguousExactRetailIwiEquivalents=$ambiguousRetail
    noRetailIwiMatch=$noRetail
    images=[object[]]$results.ToArray()
    policy=@(
        'Native/oracle IWI files are used only as validation fingerprints and expected image headers.',
        'No native/oracle IWI bytes are promoted into production output.',
        'A retail donor is eligible only when the user-owned retail Xbox IWI output has an exact SHA-256 match and exact IWI header match.',
        'No retail match means the target remains custom/non-retail and requires legitimate custom source reconstruction.'
    )
}

$jsonPath=Join-Path $OutputRoot 'nuclear_5g_native_iwi_retail_equivalence.json'
$txtPath=Join-Path $OutputRoot 'nuclear_5g_native_iwi_retail_equivalence.txt'
$manifest | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report=New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 5G - NATIVE IWI -> RETAIL XBOX EQUIVALENCE')
$report.Add(('=' * 108))
$report.Add(("Targets:                          {0:N0}" -f 43))
$report.Add(("Native IWI fingerprints found:    {0:N0}" -f $nativeIwiFound))
$report.Add(("No native IWI fingerprint:        {0:N0}" -f $noNative))
$report.Add(("Exact retail IWI equivalents:     {0:N0}" -f $exactRetail))
$report.Add(("Unique exact retail equivalents:  {0:N0}" -f $uniqueRetail))
$report.Add(("Ambiguous exact equivalents:      {0:N0}" -f $ambiguousRetail))
$report.Add(("No retail IWI match:              {0:N0}" -f $noRetail))
$report.Add('')
foreach ($row in $results.ToArray() | Sort-Object Index) {
    $report.Add(("  root #{0,4}: {1} -> {2}" -f $row.Index,$row.NativeName,$row.Status))
    $report.Add(("           PC identity: {0}" -f $row.PcImage))
}
[IO.File]::WriteAllLines($txtPath,[string[]]$report.ToArray(),[Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-5G-IMG] ====================== RESULT ==============================='
Write-Host "[NUCLEAR-5G-IMG] Targets                         : 43"
Write-Host "[NUCLEAR-5G-IMG] Native IWI fingerprints found   : $nativeIwiFound"
Write-Host "[NUCLEAR-5G-IMG] No native IWI fingerprint       : $noNative"
Write-Host "[NUCLEAR-5G-IMG] Exact retail IWI equivalents    : $exactRetail"
Write-Host "[NUCLEAR-5G-IMG] Unique exact retail equivalents : $uniqueRetail"
Write-Host "[NUCLEAR-5G-IMG] Ambiguous exact equivalents     : $ambiguousRetail"
Write-Host "[NUCLEAR-5G-IMG] No retail IWI match             : $noRetail"
Write-Host "[NUCLEAR-5G-IMG] Manifest                        : $jsonPath"
Write-Host "[NUCLEAR-5G-IMG] Report                          : $txtPath"
Write-Host '[NUCLEAR-5G-IMG] ================================================================='
Write-Host '[NUCLEAR-5G-IMG] PASS: native-IWI equivalence census complete; no oracle payload promoted.' -ForegroundColor Green
