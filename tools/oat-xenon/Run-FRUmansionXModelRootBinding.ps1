[CmdletBinding()]
param(
    [string]$CorpusRoot = '',
    [string]$BindingRoot = '',
    [string]$OutputRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($CorpusRoot)) {
    $CorpusRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4v_xmodel_corpus'
}
if ([string]::IsNullOrWhiteSpace($BindingRoot)) {
    $BindingRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_root_binding'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4w_xmodel_root_binding'
}

$corpusManifest = Join-Path $CorpusRoot 'nuclear_4v_xmodel_corpus.json'
if (-not (Test-Path -LiteralPath $corpusManifest -PathType Leaf)) {
    throw "Missing passing 4V corpus manifest: $corpusManifest"
}

$identityDir = Join-Path $BindingRoot 'native_identity'
$identityManifests = @(
    Get-ChildItem -LiteralPath $identityDir -Filter '*_native_identity_manifest.json' -File -ErrorAction SilentlyContinue |
        Sort-Object FullName
)
if ($identityManifests.Count -ne 1) {
    throw "Expected exactly one native identity manifest under '$identityDir'; found $($identityManifests.Count)."
}
$nativeIdentityManifest = $identityManifests[0].FullName

$corpus = Get-Content -LiteralPath $corpusManifest -Raw | ConvertFrom-Json
$native = Get-Content -LiteralPath $nativeIdentityManifest -Raw | ConvertFrom-Json

if ([string]$corpus.Format -notlike 'WaW360.Nuclear4V.XModelCorpus.v*') {
    throw "Unexpected 4V corpus manifest format: $($corpus.Format)"
}
if (-not [bool]$corpus.CorpusPass -or [int]$corpus.PassedModels -ne 280 -or [int]$corpus.FailedModels -ne 0) {
    throw "4W requires a 280/280 passing 4V corpus. Current passed/failed=$([int]$corpus.PassedModels)/$([int]$corpus.FailedModels)."
}
if ([string]$native.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or -not [bool]$native.typeSequenceVerified) {
    throw "Native identity manifest is not a verified WaW360.T4Xenon.NativeIdentityManifest.v1."
}

function Normalize-XModelIdentity {
    param([Parameter(Mandatory=$true)][string]$Name)

    $value = $Name.Trim().Replace('\','/')
    if ($value.StartsWith(',')) {
        $value = $value.Substring(1)
    }

    # Explicitly preserve the historical T4 Xbox packaging alias rule used by
    # FRUmansion comparison work: native "_wj" suffixes identify the same model.
    if ($value.EndsWith('_wj', [StringComparison]::OrdinalIgnoreCase)) {
        $value = $value.Substring(0, $value.Length - 3)
    }

    return $value.ToLowerInvariant()
}

$nativeXModels = @(
    $native.entries |
        Where-Object {
            ([int]$_.TypeId -eq 5) -or
            ([string]$_.OatTypeName -ieq 'xmodel') -or
            ([string]$_.TypeName -ieq 'xmodel')
        } |
        Sort-Object { [int]$_.Index }
)

$corpusModels = @(
    $corpus.Models |
        Where-Object { [bool]$_.Pass } |
        Sort-Object { [int]$_.Ordinal }
)

if ($corpusModels.Count -ne 280) {
    throw "4V corpus manifest exposes $($corpusModels.Count) passing model rows; expected 280."
}

$nativeExact = @{}
$nativeNormalized = @{}
foreach ($entry in $nativeXModels) {
    $name = [string]$entry.Name
    $exactKey = $name.ToLowerInvariant()
    if (-not $nativeExact.ContainsKey($exactKey)) {
        $nativeExact[$exactKey] = @()
    }
    $nativeExact[$exactKey] += $entry

    $normKey = Normalize-XModelIdentity -Name $name
    if (-not $nativeNormalized.ContainsKey($normKey)) {
        $nativeNormalized[$normKey] = @()
    }
    $nativeNormalized[$normKey] += $entry
}

$bindings = @()
$unmatchedCorpus = @()
$ambiguousCorpus = @()
$usedNativeIndices = @{}
$exactMatches = 0
$normalizedMatches = 0

foreach ($model in $corpusModels) {
    $corpusName = [string]$model.Name
    $exactKey = $corpusName.ToLowerInvariant()
    $candidates = @()
    $matchKind = ''

    if ($nativeExact.ContainsKey($exactKey)) {
        $candidates = @($nativeExact[$exactKey])
        $matchKind = 'EXACT_CASE_INSENSITIVE'
    }
    else {
        $normKey = Normalize-XModelIdentity -Name $corpusName
        if ($nativeNormalized.ContainsKey($normKey)) {
            $candidates = @($nativeNormalized[$normKey])
            $matchKind = 'NORMALIZED_WJ'
        }
    }

    if ($candidates.Count -eq 0) {
        $unmatchedCorpus += $corpusName
        continue
    }
    if ($candidates.Count -ne 1) {
        $ambiguousCorpus += [pscustomobject]@{
            CorpusName = $corpusName
            CandidateCount = $candidates.Count
            NativeIndices = @($candidates | ForEach-Object { [int]$_.Index })
            NativeNames = @($candidates | ForEach-Object { [string]$_.Name })
        }
        continue
    }

    $nativeEntry = $candidates[0]
    $nativeIndex = [int]$nativeEntry.Index

    if ($usedNativeIndices.ContainsKey($nativeIndex)) {
        $ambiguousCorpus += [pscustomobject]@{
            CorpusName = $corpusName
            CandidateCount = 1
            NativeIndices = @($nativeIndex)
            NativeNames = @([string]$nativeEntry.Name)
            Note = "Native root already bound to corpus '$($usedNativeIndices[$nativeIndex])'."
        }
        continue
    }

    $usedNativeIndices[$nativeIndex] = $corpusName
    if ($matchKind -eq 'EXACT_CASE_INSENSITIVE') {
        $exactMatches++
    }
    else {
        $normalizedMatches++
    }

    $bindings += [pscustomobject]@{
        CorpusOrdinal = [int]$model.Ordinal
        CorpusAssetIndex = [int]$model.AssetIndex
        CorpusName = $corpusName
        NativeRootIndex = $nativeIndex
        NativeName = [string]$nativeEntry.Name
        NativeTypeId = [int]$nativeEntry.TypeId
        NativeTypeName = [string]$nativeEntry.TypeName
        MatchKind = $matchKind
        FastFile = [string]$model.FastFile
        FastFileSha256 = [string]$model.FastFileSha256
        XModelJsonSha256 = [string]$model.XModelJsonSha256
        ClassificationSha256 = [string]$model.ClassificationSha256
    }
}

$unmatchedNative = @(
    $nativeXModels |
        Where-Object { -not $usedNativeIndices.ContainsKey([int]$_.Index) } |
        ForEach-Object {
            [pscustomobject]@{
                NativeRootIndex = [int]$_.Index
                NativeName = [string]$_.Name
                NativeTypeId = [int]$_.TypeId
            }
        }
)

$duplicateCorpusNames = @(
    $corpusModels |
        Group-Object { ([string]$_.Name).ToLowerInvariant() } |
        Where-Object { $_.Count -gt 1 } |
        ForEach-Object {
            [pscustomobject]@{
                Name = [string]$_.Name
                Count = [int]$_.Count
            }
        }
)

$bindingPass =
    $bindings.Count -eq 280 -and
    $unmatchedCorpus.Count -eq 0 -and
    $ambiguousCorpus.Count -eq 0 -and
    $duplicateCorpusNames.Count -eq 0 -and
    $usedNativeIndices.Count -eq 280

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$manifestPath = Join-Path $OutputRoot 'nuclear_4w_xmodel_root_binding.json'
$reportPath = Join-Path $OutputRoot 'nuclear_4w_xmodel_root_binding.txt'

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4W.XModelRootBinding.v1'
    corpusManifest = $corpusManifest
    nativeIdentityManifest = $nativeIdentityManifest
    nativeRoots = [int]$native.assets
    nativeXModelRoots = $nativeXModels.Count
    corpusModels = $corpusModels.Count
    boundModels = $bindings.Count
    exactMatches = $exactMatches
    normalizedWjMatches = $normalizedMatches
    unmatchedCorpusCount = $unmatchedCorpus.Count
    ambiguousCorpusCount = $ambiguousCorpus.Count
    duplicateCorpusIdentityCount = $duplicateCorpusNames.Count
    unmatchedNativeXModelRoots = $unmatchedNative.Count
    bindingPass = $bindingPass
    bindings = @($bindings | Sort-Object NativeRootIndex)
    unmatchedCorpus = $unmatchedCorpus
    ambiguousCorpus = $ambiguousCorpus
    duplicateCorpusNames = $duplicateCorpusNames
    unmatchedNative = $unmatchedNative
}

$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$report = @(
    'NUCLEAR STAGE 4W - XMODEL NATIVE ROOT BINDING'
    '=========================================================================='
    "nativeRoots=$([int]$native.assets)"
    "nativeXModelRoots=$($nativeXModels.Count)"
    "corpusModels=$($corpusModels.Count)"
    "boundModels=$($bindings.Count)"
    "exactMatches=$exactMatches"
    "normalizedWjMatches=$normalizedMatches"
    "unmatchedCorpus=$($unmatchedCorpus.Count)"
    "ambiguousCorpus=$($ambiguousCorpus.Count)"
    "duplicateCorpusIdentities=$($duplicateCorpusNames.Count)"
    "unmatchedNativeXModelRoots=$($unmatchedNative.Count)"
    "bindingPass=$bindingPass"
    ''
    'BOUND CORPUS XMODELS'
)
foreach ($row in @($bindings | Sort-Object NativeRootIndex)) {
    $report += ('[{0,5}] {1,-24} corpus#{2,3}  native={3}  corpus={4}' -f
        [int]$row.NativeRootIndex,
        [string]$row.MatchKind,
        [int]$row.CorpusOrdinal,
        [string]$row.NativeName,
        [string]$row.CorpusName)
}

if ($unmatchedNative.Count -gt 0) {
    $report += ''
    $report += 'UNMATCHED NATIVE XMODEL ROOTS'
    foreach ($row in $unmatchedNative) {
        $report += ('[{0,5}] {1}' -f [int]$row.NativeRootIndex, [string]$row.NativeName)
    }
}

if ($unmatchedCorpus.Count -gt 0) {
    $report += ''
    $report += 'UNMATCHED CORPUS XMODELS'
    foreach ($name in $unmatchedCorpus) {
        $report += [string]$name
    }
}

if ($ambiguousCorpus.Count -gt 0) {
    $report += ''
    $report += 'AMBIGUOUS CORPUS XMODELS'
    foreach ($row in $ambiguousCorpus) {
        $report += ("{0}: native indices {1}" -f
            [string]$row.CorpusName,
            (@($row.NativeIndices) -join ','))
    }
}

[IO.File]::WriteAllLines(
    $reportPath,
    [string[]]$report,
    [Text.UTF8Encoding]::new($false))

Write-Host '[NUCLEAR-4W] ================================================================'
Write-Host '[NUCLEAR-4W] XMODEL NATIVE ROOT BINDING'
Write-Host '[NUCLEAR-4W] ================================================================'
Write-Host "[NUCLEAR-4W] Native roots       : $([int]$native.assets)"
Write-Host "[NUCLEAR-4W] Native XModels     : $($nativeXModels.Count)"
Write-Host "[NUCLEAR-4W] Corpus XModels     : $($corpusModels.Count)"
Write-Host "[NUCLEAR-4W] Bound XModels      : $($bindings.Count)"
Write-Host "[NUCLEAR-4W] Exact matches      : $exactMatches"
Write-Host "[NUCLEAR-4W] _wj-normalized     : $normalizedMatches"
Write-Host "[NUCLEAR-4W] Unmatched corpus   : $($unmatchedCorpus.Count)"
Write-Host "[NUCLEAR-4W] Ambiguous corpus   : $($ambiguousCorpus.Count)"
Write-Host "[NUCLEAR-4W] Native-only roots  : $($unmatchedNative.Count)"
Write-Host "[NUCLEAR-4W] Binding pass       : $bindingPass"
Write-Host "[NUCLEAR-4W] Manifest           : $manifestPath"
Write-Host "[NUCLEAR-4W] Report             : $reportPath"
Write-Host '[NUCLEAR-4W] ================================================================'

if (-not $bindingPass) {
    Write-Host ''
    Write-Host '[NUCLEAR-4W] XMODEL ROOT BINDING: BLOCKED' -ForegroundColor Yellow
    if ($unmatchedCorpus.Count -gt 0) {
        Write-Host ('[NUCLEAR-4W] Unmatched corpus examples: ' + (@($unmatchedCorpus | Select-Object -First 10) -join ' | '))
    }
    if ($ambiguousCorpus.Count -gt 0) {
        Write-Host ('[NUCLEAR-4W] Ambiguous corpus examples: ' + (@($ambiguousCorpus | Select-Object -First 5 | ForEach-Object { $_.CorpusName }) -join ' | '))
    }
    exit 4
}

Write-Host ''
Write-Host '[NUCLEAR-4W] 280-XMODEL ROOT BINDING: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4W] Every validated PC-backed Xenon XModel is bound to one unique native XModel root.'
Write-Host '[NUCLEAR-4W] Next: dump relocatable Xenon XModel graphs for exactly these 280 bound roots.'
exit 0
