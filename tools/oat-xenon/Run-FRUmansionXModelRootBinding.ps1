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

$nativeDuplicateIdentityGroups = @(
    $nativeXModels |
        Group-Object { ([string]$_.Name).ToLowerInvariant() } |
        Where-Object { $_.Count -gt 1 } |
        ForEach-Object {
            [pscustomobject]@{
                Name = [string]$_.Group[0].Name
                Count = [int]$_.Count
                NativeIndices = @($_.Group | Sort-Object { [int]$_.Index } | ForEach-Object { [int]$_.Index })
            }
        }
)

$sourceBindings = @()
$rootBindings = @()
$unmatchedCorpus = @()
$unsafeAmbiguities = @()
$usedNativeIndices = @{}
$exactSourceMatches = 0
$normalizedSourceMatches = 0
$exactRootBindings = 0
$normalizedRootBindings = 0
$duplicateNativeSlots = 0

foreach ($model in $corpusModels) {
    $corpusName = [string]$model.Name
    $exactKey = $corpusName.ToLowerInvariant()
    $candidates = @()
    $matchKind = ''

    if ($nativeExact.ContainsKey($exactKey)) {
        $candidates = @($nativeExact[$exactKey] | Sort-Object { [int]$_.Index })
        $matchKind = if ($candidates.Count -gt 1) {
            'EXACT_DUPLICATE_NATIVE_IDENTITY'
        }
        else {
            'EXACT_CASE_INSENSITIVE'
        }
    }
    else {
        $normKey = Normalize-XModelIdentity -Name $corpusName
        if ($nativeNormalized.ContainsKey($normKey)) {
            $candidates = @($nativeNormalized[$normKey] | Sort-Object { [int]$_.Index })

            # Normalization may only resolve a single native root. If two different
            # native names collapse to the same normalized identity, that is not
            # evidence that the source graph is valid for both; fail closed.
            if ($candidates.Count -eq 1) {
                $matchKind = 'NORMALIZED_WJ'
            }
            else {
                $unsafeAmbiguities += [pscustomobject]@{
                    CorpusName = $corpusName
                    Reason = 'NORMALIZATION_COLLISION'
                    CandidateCount = $candidates.Count
                    NativeIndices = @($candidates | ForEach-Object { [int]$_.Index })
                    NativeNames = @($candidates | ForEach-Object { [string]$_.Name })
                }
                continue
            }
        }
    }

    if ($candidates.Count -eq 0) {
        $unmatchedCorpus += $corpusName
        continue
    }

    # Multiple candidates are accepted only when they are literal duplicate
    # native identities for the same corpus model.
    if ($candidates.Count -gt 1) {
        $allExactDuplicate = $true
        foreach ($candidate in $candidates) {
            if (-not ([string]$candidate.Name -ieq $corpusName)) {
                $allExactDuplicate = $false
                break
            }
        }

        if (-not $allExactDuplicate) {
            $unsafeAmbiguities += [pscustomobject]@{
                CorpusName = $corpusName
                Reason = 'NON_IDENTICAL_NATIVE_CANDIDATES'
                CandidateCount = $candidates.Count
                NativeIndices = @($candidates | ForEach-Object { [int]$_.Index })
                NativeNames = @($candidates | ForEach-Object { [string]$_.Name })
            }
            continue
        }
    }

    if ($matchKind -eq 'NORMALIZED_WJ') {
        $normalizedSourceMatches++
    }
    else {
        $exactSourceMatches++
    }

    $sourceBinding = [pscustomobject]@{
        CorpusOrdinal = [int]$model.Ordinal
        CorpusAssetIndex = [int]$model.AssetIndex
        CorpusName = $corpusName
        MatchKind = $matchKind
        NativeRootCount = $candidates.Count
        NativeRootIndices = @($candidates | ForEach-Object { [int]$_.Index })
        NativeNames = @($candidates | ForEach-Object { [string]$_.Name })
        FastFile = [string]$model.FastFile
        FastFileSha256 = [string]$model.FastFileSha256
        XModelJsonSha256 = [string]$model.XModelJsonSha256
        ClassificationSha256 = [string]$model.ClassificationSha256
    }
    $sourceBindings += $sourceBinding

    if ($candidates.Count -gt 1) {
        $duplicateNativeSlots += ($candidates.Count - 1)
    }

    foreach ($nativeEntry in $candidates) {
        $nativeIndex = [int]$nativeEntry.Index

        if ($usedNativeIndices.ContainsKey($nativeIndex)) {
            $unsafeAmbiguities += [pscustomobject]@{
                CorpusName = $corpusName
                Reason = 'NATIVE_ROOT_ALREADY_BOUND'
                CandidateCount = 1
                NativeIndices = @($nativeIndex)
                NativeNames = @([string]$nativeEntry.Name)
                PriorCorpusName = [string]$usedNativeIndices[$nativeIndex]
            }
            continue
        }

        $usedNativeIndices[$nativeIndex] = $corpusName

        if ($matchKind -eq 'NORMALIZED_WJ') {
            $normalizedRootBindings++
        }
        else {
            $exactRootBindings++
        }

        $rootBindings += [pscustomobject]@{
            NativeRootIndex = $nativeIndex
            NativeName = [string]$nativeEntry.Name
            NativeTypeId = [int]$nativeEntry.TypeId
            NativeTypeName = [string]$nativeEntry.TypeName
            CorpusOrdinal = [int]$model.Ordinal
            CorpusAssetIndex = [int]$model.AssetIndex
            CorpusName = $corpusName
            MatchKind = $matchKind
            ReusesCorpusGraph = ($candidates.Count -gt 1)
            FastFile = [string]$model.FastFile
            FastFileSha256 = [string]$model.FastFileSha256
            XModelJsonSha256 = [string]$model.XModelJsonSha256
            ClassificationSha256 = [string]$model.ClassificationSha256
        }
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

$bindingPass =
    $sourceBindings.Count -eq 280 -and
    $rootBindings.Count -eq $nativeXModels.Count -and
    $usedNativeIndices.Count -eq $nativeXModels.Count -and
    $unmatchedCorpus.Count -eq 0 -and
    $unsafeAmbiguities.Count -eq 0 -and
    $duplicateCorpusNames.Count -eq 0 -and
    $unmatchedNative.Count -eq 0

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$manifestPath = Join-Path $OutputRoot 'nuclear_4w_xmodel_root_binding.json'
$reportPath = Join-Path $OutputRoot 'nuclear_4w_xmodel_root_binding.txt'

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4W.XModelRootBinding.v2'
    corpusManifest = $corpusManifest
    nativeIdentityManifest = $nativeIdentityManifest
    nativeRoots = [int]$native.assets
    nativeXModelRoots = $nativeXModels.Count
    corpusModels = $corpusModels.Count
    boundCorpusModels = $sourceBindings.Count
    boundNativeRootSlots = $rootBindings.Count
    exactSourceMatches = $exactSourceMatches
    normalizedWjSourceMatches = $normalizedSourceMatches
    exactNativeRootBindings = $exactRootBindings
    normalizedWjNativeRootBindings = $normalizedRootBindings
    duplicateNativeIdentityGroups = $nativeDuplicateIdentityGroups.Count
    duplicateNativeRootSlots = $duplicateNativeSlots
    unmatchedCorpusCount = $unmatchedCorpus.Count
    unsafeAmbiguityCount = $unsafeAmbiguities.Count
    duplicateCorpusIdentityCount = $duplicateCorpusNames.Count
    unmatchedNativeXModelRoots = $unmatchedNative.Count
    bindingPass = $bindingPass
    sourceBindings = @($sourceBindings | Sort-Object CorpusOrdinal)
    rootBindings = @($rootBindings | Sort-Object NativeRootIndex)
    nativeDuplicateIdentities = $nativeDuplicateIdentityGroups
    unmatchedCorpus = $unmatchedCorpus
    unsafeAmbiguities = $unsafeAmbiguities
    duplicateCorpusNames = $duplicateCorpusNames
    unmatchedNative = $unmatchedNative
}

$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$report = @(
    'NUCLEAR STAGE 4W - XMODEL NATIVE ROOT BINDING'
    '=========================================================================='
    "nativeRoots=$([int]$native.assets)"
    "nativeXModelRoots=$($nativeXModels.Count)"
    "corpusModels=$($corpusModels.Count)"
    "boundCorpusModels=$($sourceBindings.Count)"
    "boundNativeRootSlots=$($rootBindings.Count)"
    "exactSourceMatches=$exactSourceMatches"
    "normalizedWjSourceMatches=$normalizedSourceMatches"
    "exactNativeRootBindings=$exactRootBindings"
    "normalizedWjNativeRootBindings=$normalizedRootBindings"
    "duplicateNativeIdentityGroups=$($nativeDuplicateIdentityGroups.Count)"
    "duplicateNativeRootSlots=$duplicateNativeSlots"
    "unmatchedCorpus=$($unmatchedCorpus.Count)"
    "unsafeAmbiguities=$($unsafeAmbiguities.Count)"
    "duplicateCorpusIdentities=$($duplicateCorpusNames.Count)"
    "unmatchedNativeXModelRoots=$($unmatchedNative.Count)"
    "bindingPass=$bindingPass"
    ''
    'SOURCE MODEL -> NATIVE ROOT BINDINGS'
)

foreach ($row in @($sourceBindings | Sort-Object CorpusOrdinal)) {
    $report += ('corpus#{0,3} {1,-34} roots={2,-8} name={3}' -f
        [int]$row.CorpusOrdinal,
        [string]$row.MatchKind,
        ((@($row.NativeRootIndices) -join ',')),
        [string]$row.CorpusName)
}

if ($nativeDuplicateIdentityGroups.Count -gt 0) {
    $report += ''
    $report += 'NATIVE DUPLICATE XMODEL IDENTITIES'
    foreach ($row in $nativeDuplicateIdentityGroups) {
        $report += ('count={0} roots={1,-12} name={2}' -f
            [int]$row.Count,
            ((@($row.NativeIndices) -join ',')),
            [string]$row.Name)
    }
}

if ($unsafeAmbiguities.Count -gt 0) {
    $report += ''
    $report += 'UNSAFE AMBIGUITIES'
    foreach ($row in $unsafeAmbiguities) {
        $report += ('{0}: {1}; roots={2}; native={3}' -f
            [string]$row.CorpusName,
            [string]$row.Reason,
            ((@($row.NativeIndices) -join ',')),
            ((@($row.NativeNames) -join ' | ')))
    }
}

if ($unmatchedCorpus.Count -gt 0) {
    $report += ''
    $report += 'UNMATCHED CORPUS XMODELS'
    foreach ($name in $unmatchedCorpus) {
        $report += [string]$name
    }
}

if ($unmatchedNative.Count -gt 0) {
    $report += ''
    $report += 'UNMATCHED NATIVE XMODEL ROOTS'
    foreach ($row in $unmatchedNative) {
        $report += ('[{0,5}] {1}' -f [int]$row.NativeRootIndex, [string]$row.NativeName)
    }
}

[IO.File]::WriteAllLines(
    $reportPath,
    [string[]]$report,
    [Text.UTF8Encoding]::new($false))

Write-Host '[NUCLEAR-4W] ================================================================'
Write-Host '[NUCLEAR-4W] XMODEL NATIVE ROOT BINDING'
Write-Host '[NUCLEAR-4W] ================================================================'
Write-Host "[NUCLEAR-4W] Native roots        : $([int]$native.assets)"
Write-Host "[NUCLEAR-4W] Native XModel slots : $($nativeXModels.Count)"
Write-Host "[NUCLEAR-4W] Corpus models       : $($corpusModels.Count)"
Write-Host "[NUCLEAR-4W] Bound corpus models : $($sourceBindings.Count)"
Write-Host "[NUCLEAR-4W] Bound native slots  : $($rootBindings.Count)"
Write-Host "[NUCLEAR-4W] Exact source IDs    : $exactSourceMatches"
Write-Host "[NUCLEAR-4W] _wj source IDs      : $normalizedSourceMatches"
Write-Host "[NUCLEAR-4W] Duplicate ID groups : $($nativeDuplicateIdentityGroups.Count)"
Write-Host "[NUCLEAR-4W] Duplicate root slots: $duplicateNativeSlots"
Write-Host "[NUCLEAR-4W] Unmatched corpus    : $($unmatchedCorpus.Count)"
Write-Host "[NUCLEAR-4W] Unsafe ambiguity    : $($unsafeAmbiguities.Count)"
Write-Host "[NUCLEAR-4W] Native-only slots   : $($unmatchedNative.Count)"
Write-Host "[NUCLEAR-4W] Binding pass        : $bindingPass"
Write-Host "[NUCLEAR-4W] Manifest            : $manifestPath"
Write-Host "[NUCLEAR-4W] Report              : $reportPath"
Write-Host '[NUCLEAR-4W] ================================================================'

if (-not $bindingPass) {
    Write-Host ''
    Write-Host '[NUCLEAR-4W] XMODEL ROOT BINDING: BLOCKED' -ForegroundColor Yellow

    if ($unsafeAmbiguities.Count -gt 0) {
        Write-Host ('[NUCLEAR-4W] Unsafe examples: ' +
            (@($unsafeAmbiguities | Select-Object -First 5 | ForEach-Object { "$($_.CorpusName):$($_.Reason)" }) -join ' | '))
    }
    if ($unmatchedCorpus.Count -gt 0) {
        Write-Host ('[NUCLEAR-4W] Unmatched corpus examples: ' +
            (@($unmatchedCorpus | Select-Object -First 10) -join ' | '))
    }

    exit 4
}

Write-Host ''
Write-Host '[NUCLEAR-4W] 280-MODEL / 289-ROOT XMODEL BINDING: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4W] Every validated PC-backed Xenon XModel is bound to all native root slots carrying its identity.'
Write-Host '[NUCLEAR-4W] Duplicate native identities intentionally reuse the same validated source graph.'
Write-Host '[NUCLEAR-4W] Next: dump one relocatable Xenon graph per 280 source models, then replay those graphs into all 289 bound native root slots.'
exit 0
