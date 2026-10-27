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

function Test-NativeReferenceIdentity {
    param([Parameter(Mandatory=$true)][string]$Name)
    return $Name.StartsWith(',', [StringComparison]::Ordinal)
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

$ownedNativeXModels = @(
    $nativeXModels |
        Where-Object { -not (Test-NativeReferenceIdentity -Name ([string]$_.Name)) }
)

$referenceNativeXModels = @(
    $nativeXModels |
        Where-Object { Test-NativeReferenceIdentity -Name ([string]$_.Name) }
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
                Name = [string]$_.Group[0].Name
                Count = [int]$_.Count
            }
        }
)

$ownedExact = @{}
$ownedNormalized = @{}
foreach ($entry in $ownedNativeXModels) {
    $name = [string]$entry.Name

    $exactKey = $name.ToLowerInvariant()
    if (-not $ownedExact.ContainsKey($exactKey)) {
        $ownedExact[$exactKey] = @()
    }
    $ownedExact[$exactKey] += $entry

    $normKey = Normalize-XModelIdentity -Name $name
    if (-not $ownedNormalized.ContainsKey($normKey)) {
        $ownedNormalized[$normKey] = @()
    }
    $ownedNormalized[$normKey] += $entry
}

$corpusNormalized = @{}
foreach ($model in $corpusModels) {
    $key = Normalize-XModelIdentity -Name ([string]$model.Name)
    if (-not $corpusNormalized.ContainsKey($key)) {
        $corpusNormalized[$key] = @()
    }
    $corpusNormalized[$key] += $model
}

$ownedBindings = @()
$referenceBindings = @()
$unmatchedCorpus = @()
$unsafeAmbiguities = @()
$usedOwnedIndices = @{}
$usedReferenceIndices = @{}
$exactOwnedMatches = 0
$normalizedOwnedMatches = 0

foreach ($model in $corpusModels) {
    $corpusName = [string]$model.Name
    $exactKey = $corpusName.ToLowerInvariant()
    $candidates = @()
    $matchKind = ''

    if ($ownedExact.ContainsKey($exactKey)) {
        $candidates = @($ownedExact[$exactKey] | Sort-Object { [int]$_.Index })
        $matchKind = 'EXACT_CASE_INSENSITIVE'
    }
    else {
        $normKey = Normalize-XModelIdentity -Name $corpusName
        if ($ownedNormalized.ContainsKey($normKey)) {
            $candidates = @($ownedNormalized[$normKey] | Sort-Object { [int]$_.Index })
            $matchKind = 'NORMALIZED_WJ'
        }
    }

    if ($candidates.Count -eq 0) {
        $unmatchedCorpus += $corpusName
        continue
    }

    if ($candidates.Count -ne 1) {
        $unsafeAmbiguities += [pscustomobject]@{
            Kind = 'OWNED_ROOT_COLLISION'
            CorpusName = $corpusName
            CandidateCount = $candidates.Count
            NativeIndices = @($candidates | ForEach-Object { [int]$_.Index })
            NativeNames = @($candidates | ForEach-Object { [string]$_.Name })
        }
        continue
    }

    $nativeEntry = $candidates[0]
    $nativeIndex = [int]$nativeEntry.Index

    if ($usedOwnedIndices.ContainsKey($nativeIndex)) {
        $unsafeAmbiguities += [pscustomobject]@{
            Kind = 'OWNED_ROOT_REUSED'
            CorpusName = $corpusName
            CandidateCount = 1
            NativeIndices = @($nativeIndex)
            NativeNames = @([string]$nativeEntry.Name)
            PriorCorpusName = [string]$usedOwnedIndices[$nativeIndex]
        }
        continue
    }

    $usedOwnedIndices[$nativeIndex] = $corpusName
    if ($matchKind -eq 'EXACT_CASE_INSENSITIVE') {
        $exactOwnedMatches++
    }
    else {
        $normalizedOwnedMatches++
    }

    $ownedBindings += [pscustomobject]@{
        NativeRootIndex = $nativeIndex
        NativeName = [string]$nativeEntry.Name
        NativeTypeId = [int]$nativeEntry.TypeId
        NativeTypeName = [string]$nativeEntry.TypeName
        CorpusOrdinal = [int]$model.Ordinal
        CorpusAssetIndex = [int]$model.AssetIndex
        CorpusName = $corpusName
        MatchKind = $matchKind
        FastFile = [string]$model.FastFile
        FastFileSha256 = [string]$model.FastFileSha256
        XModelJsonSha256 = [string]$model.XModelJsonSha256
        ClassificationSha256 = [string]$model.ClassificationSha256
    }
}

foreach ($referenceEntry in $referenceNativeXModels) {
    $nativeName = [string]$referenceEntry.Name
    $targetName = $nativeName.Substring(1)
    $normKey = Normalize-XModelIdentity -Name $targetName
    $candidates = @()

    if ($corpusNormalized.ContainsKey($normKey)) {
        $candidates = @($corpusNormalized[$normKey])
    }

    if ($candidates.Count -ne 1) {
        $unsafeAmbiguities += [pscustomobject]@{
            Kind = 'REFERENCE_TARGET_COLLISION'
            CorpusName = $targetName
            CandidateCount = $candidates.Count
            NativeIndices = @([int]$referenceEntry.Index)
            NativeNames = @($nativeName)
        }
        continue
    }

    $model = $candidates[0]
    $referenceIndex = [int]$referenceEntry.Index

    if ($usedReferenceIndices.ContainsKey($referenceIndex)) {
        $unsafeAmbiguities += [pscustomobject]@{
            Kind = 'REFERENCE_ROOT_REUSED'
            CorpusName = [string]$model.Name
            CandidateCount = 1
            NativeIndices = @($referenceIndex)
            NativeNames = @($nativeName)
        }
        continue
    }

    $usedReferenceIndices[$referenceIndex] = [string]$model.Name

    $referenceBindings += [pscustomobject]@{
        NativeRootIndex = $referenceIndex
        NativeReferenceName = $nativeName
        ReferencedAssetName = $targetName
        NativeTypeId = [int]$referenceEntry.TypeId
        NativeTypeName = [string]$referenceEntry.TypeName
        CorpusOrdinal = [int]$model.Ordinal
        CorpusAssetIndex = [int]$model.AssetIndex
        CorpusName = [string]$model.Name
        MatchKind = 'OAT_REFERENCE_ROOT'
        EmitPolicy = 'REFERENCE_SHELL_ONLY'
        FastFileSha256 = [string]$model.FastFileSha256
    }
}

$unmatchedOwnedNative = @(
    $ownedNativeXModels |
        Where-Object { -not $usedOwnedIndices.ContainsKey([int]$_.Index) } |
        ForEach-Object {
            [pscustomobject]@{
                NativeRootIndex = [int]$_.Index
                NativeName = [string]$_.Name
            }
        }
)

$unmatchedReferenceNative = @(
    $referenceNativeXModels |
        Where-Object { -not $usedReferenceIndices.ContainsKey([int]$_.Index) } |
        ForEach-Object {
            [pscustomobject]@{
                NativeRootIndex = [int]$_.Index
                NativeName = [string]$_.Name
            }
        }
)

$bindingPass =
    $ownedNativeXModels.Count -eq 280 -and
    $referenceNativeXModels.Count -eq 9 -and
    $ownedBindings.Count -eq 280 -and
    $referenceBindings.Count -eq 9 -and
    $usedOwnedIndices.Count -eq 280 -and
    $usedReferenceIndices.Count -eq 9 -and
    $unmatchedCorpus.Count -eq 0 -and
    $unsafeAmbiguities.Count -eq 0 -and
    $duplicateCorpusNames.Count -eq 0 -and
    $unmatchedOwnedNative.Count -eq 0 -and
    $unmatchedReferenceNative.Count -eq 0

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$manifestPath = Join-Path $OutputRoot 'nuclear_4w_xmodel_root_binding.json'
$reportPath = Join-Path $OutputRoot 'nuclear_4w_xmodel_root_binding.txt'

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4W.XModelRootBinding.v3'
    corpusManifest = $corpusManifest
    nativeIdentityManifest = $nativeIdentityManifest
    nativeRoots = [int]$native.assets
    nativeXModelRoots = $nativeXModels.Count
    ownedNativeXModelRoots = $ownedNativeXModels.Count
    referenceNativeXModelRoots = $referenceNativeXModels.Count
    corpusModels = $corpusModels.Count
    boundOwnedModels = $ownedBindings.Count
    boundReferenceRoots = $referenceBindings.Count
    exactOwnedMatches = $exactOwnedMatches
    normalizedWjOwnedMatches = $normalizedOwnedMatches
    unmatchedCorpusCount = $unmatchedCorpus.Count
    unsafeAmbiguityCount = $unsafeAmbiguities.Count
    duplicateCorpusIdentityCount = $duplicateCorpusNames.Count
    unmatchedOwnedNativeRoots = $unmatchedOwnedNative.Count
    unmatchedReferenceNativeRoots = $unmatchedReferenceNative.Count
    bindingPass = $bindingPass
    ownedBindings = @($ownedBindings | Sort-Object NativeRootIndex)
    referenceBindings = @($referenceBindings | Sort-Object NativeRootIndex)
    unmatchedCorpus = $unmatchedCorpus
    unsafeAmbiguities = $unsafeAmbiguities
    duplicateCorpusNames = $duplicateCorpusNames
    unmatchedOwnedNative = $unmatchedOwnedNative
    unmatchedReferenceNative = $unmatchedReferenceNative
}

$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$report = @(
    'NUCLEAR STAGE 4W - XMODEL NATIVE ROOT BINDING'
    '=========================================================================='
    "nativeRoots=$([int]$native.assets)"
    "nativeXModelRoots=$($nativeXModels.Count)"
    "ownedNativeXModelRoots=$($ownedNativeXModels.Count)"
    "referenceNativeXModelRoots=$($referenceNativeXModels.Count)"
    "corpusModels=$($corpusModels.Count)"
    "boundOwnedModels=$($ownedBindings.Count)"
    "boundReferenceRoots=$($referenceBindings.Count)"
    "exactOwnedMatches=$exactOwnedMatches"
    "normalizedWjOwnedMatches=$normalizedOwnedMatches"
    "unmatchedCorpus=$($unmatchedCorpus.Count)"
    "unsafeAmbiguities=$($unsafeAmbiguities.Count)"
    "duplicateCorpusIdentities=$($duplicateCorpusNames.Count)"
    "unmatchedOwnedNativeRoots=$($unmatchedOwnedNative.Count)"
    "unmatchedReferenceNativeRoots=$($unmatchedReferenceNative.Count)"
    "bindingPass=$bindingPass"
    ''
    'OWNED XMODEL ROOT BINDINGS'
)

foreach ($row in @($ownedBindings | Sort-Object NativeRootIndex)) {
    $report += ('[{0,5}] {1,-24} corpus#{2,3} native={3} corpus={4}' -f
        [int]$row.NativeRootIndex,
        [string]$row.MatchKind,
        [int]$row.CorpusOrdinal,
        [string]$row.NativeName,
        [string]$row.CorpusName)
}

$report += ''
$report += 'REFERENCE XMODEL ROOT BINDINGS'
foreach ($row in @($referenceBindings | Sort-Object NativeRootIndex)) {
    $report += ('[{0,5}] REFERENCE corpus#{1,3} native={2} target={3}' -f
        [int]$row.NativeRootIndex,
        [int]$row.CorpusOrdinal,
        [string]$row.NativeReferenceName,
        [string]$row.CorpusName)
}

if ($unsafeAmbiguities.Count -gt 0) {
    $report += ''
    $report += 'UNSAFE AMBIGUITIES'
    foreach ($row in $unsafeAmbiguities) {
        $report += ('{0}: {1}; roots={2}; native={3}' -f
            [string]$row.CorpusName,
            [string]$row.Kind,
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

[IO.File]::WriteAllLines(
    $reportPath,
    [string[]]$report,
    [Text.UTF8Encoding]::new($false))

Write-Host '[NUCLEAR-4W] ================================================================'
Write-Host '[NUCLEAR-4W] XMODEL NATIVE ROOT BINDING'
Write-Host '[NUCLEAR-4W] ================================================================'
Write-Host "[NUCLEAR-4W] Native roots          : $([int]$native.assets)"
Write-Host "[NUCLEAR-4W] Native XModel slots   : $($nativeXModels.Count)"
Write-Host "[NUCLEAR-4W] Owned XModel roots    : $($ownedNativeXModels.Count)"
Write-Host "[NUCLEAR-4W] Reference XModel roots: $($referenceNativeXModels.Count)"
Write-Host "[NUCLEAR-4W] Corpus models         : $($corpusModels.Count)"
Write-Host "[NUCLEAR-4W] Bound owned models    : $($ownedBindings.Count)"
Write-Host "[NUCLEAR-4W] Bound references      : $($referenceBindings.Count)"
Write-Host "[NUCLEAR-4W] Exact owned IDs       : $exactOwnedMatches"
Write-Host "[NUCLEAR-4W] _wj owned IDs         : $normalizedOwnedMatches"
Write-Host "[NUCLEAR-4W] Unmatched corpus      : $($unmatchedCorpus.Count)"
Write-Host "[NUCLEAR-4W] Unsafe ambiguity      : $($unsafeAmbiguities.Count)"
Write-Host "[NUCLEAR-4W] Unmatched owned roots : $($unmatchedOwnedNative.Count)"
Write-Host "[NUCLEAR-4W] Unmatched references  : $($unmatchedReferenceNative.Count)"
Write-Host "[NUCLEAR-4W] Binding pass          : $bindingPass"
Write-Host "[NUCLEAR-4W] Manifest              : $manifestPath"
Write-Host "[NUCLEAR-4W] Report                : $reportPath"
Write-Host '[NUCLEAR-4W] ================================================================'

if (-not $bindingPass) {
    Write-Host ''
    Write-Host '[NUCLEAR-4W] XMODEL ROOT BINDING: BLOCKED' -ForegroundColor Yellow

    if ($unsafeAmbiguities.Count -gt 0) {
        Write-Host ('[NUCLEAR-4W] Unsafe examples: ' +
            (@($unsafeAmbiguities | Select-Object -First 5 | ForEach-Object { "$($_.CorpusName):$($_.Kind)" }) -join ' | '))
    }
    if ($unmatchedCorpus.Count -gt 0) {
        Write-Host ('[NUCLEAR-4W] Unmatched corpus examples: ' +
            (@($unmatchedCorpus | Select-Object -First 10) -join ' | '))
    }

    exit 4
}

Write-Host ''
Write-Host '[NUCLEAR-4W] 280 OWNED + 9 REFERENCE XMODEL ROOTS: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4W] All 280 validated PC-backed Xenon XModels bind one-to-one to native owned XModel roots.'
Write-Host '[NUCLEAR-4W] All 9 extra native XModel slots are OAT reference assets and remain reference-shell roots.'
Write-Host '[NUCLEAR-4W] Next: dump relocatable Xenon graphs for the 280 owned roots only; preserve the 9 reference roots as references during 6,811-root replay.'
exit 0
