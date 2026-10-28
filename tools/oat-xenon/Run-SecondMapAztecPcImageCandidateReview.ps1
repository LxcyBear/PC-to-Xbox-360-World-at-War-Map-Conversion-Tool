[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$work=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping\pc_fastfile_image_sources'
$localPath=Join-Path $work 'aztec_external_pc_image_sources.json'
$extendedPath=Join-Path $work 'aztec_external_pc_image_sources_extended.json'
$triagePath=Join-Path $work 'aztec_image_dependency_triage.json'
foreach($required in @($localPath,$extendedPath,$triagePath)){
    if(-not (Test-Path -LiteralPath $required -PathType Leaf)){throw "Aztec input report is missing: $required"}
}
$local=Get-Content -LiteralPath $localPath -Raw | ConvertFrom-Json
$expanded=Get-Content -LiteralPath $extendedPath -Raw | ConvertFrom-Json
$triage=Get-Content -LiteralPath $triagePath -Raw | ConvertFrom-Json
if([string]$local.schema -ne 'WaW360.AztecExternalPcImageSourceAudit.v1' -or
   [string]$local.status -ne 'AZTEC_PC_EXTERNAL_IMAGE_SOURCE_AUDIT_PASS' -or
   [bool]$local.scannedExtendedPcSourceLibraries -or
   [int]$local.candidateMatchedTargetNames -ne 0 -or
   @($local.results).Count -ne 181 -or
   [string]$expanded.schema -ne 'WaW360.AztecExternalPcImageSourceAudit.v1' -or
   [string]$expanded.status -ne 'AZTEC_PC_EXTERNAL_IMAGE_SOURCE_AUDIT_PASS' -or
   -not [bool]$expanded.scannedExtendedPcSourceLibraries -or
   [int]$expanded.retailUnmatchedTargetNames -ne 181 -or
   [int]$expanded.candidateMatchedTargetNames -ne 32 -or
   [int]$expanded.candidateMatchedNativeRoots -ne 32 -or
   [int]$expanded.candidateConflictingContentNames -ne 8 -or
   [int]$expanded.nativeRootsWithoutCandidateInScannedSources -ne 146 -or
   @($expanded.results).Count -ne 181 -or
   [string]$triage.schema -ne 'WaW360.AztecImageDependencyTriage.v1' -or
   [string]$triage.status -ne 'AZTEC_IMAGE_DEPENDENCY_TRIAGE_PASS' -or
   [int]$triage.missingFromAll67RetailZones -ne 181){
    throw 'Aztec 0/181 local and 32/181 extended source reports or successful dependency triage changed.'
}

$localKeys=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($row in @($local.results)){
    $key=[string]$row.Key
    if([string]::IsNullOrWhiteSpace($key) -or -not $localKeys.Add($key) -or
       [bool]$row.FoundPcCandidate){throw "Local PC map audit changed identity or acquired a source: $key"}
}
$triageKeys=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($row in @($triage.rows)){
    if([string]$row.AuditPartition -ne 'not-in-scanned-retail'){continue}
    if(-not $triageKeys.Add([string]$row.Key)){throw "Triage duplicate source identity $($row.Key)"}
}
if($triageKeys.Count -ne 181){throw 'Triage no-retail population is not exactly 181 names.'}

$allRows=New-Object 'System.Collections.Generic.List[object]'
$matched=New-Object 'System.Collections.Generic.List[object]'
$conflicts=New-Object 'System.Collections.Generic.List[object]'
$unambiguous=New-Object 'System.Collections.Generic.List[object]'
$unmatched=New-Object 'System.Collections.Generic.List[object]'
$packages=@{}
$seenKeys=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($r in @($expanded.results | Sort-Object Key)){
    $key=[string]$r.Key
    if([string]::IsNullOrWhiteSpace($key) -or
       -not $seenKeys.Add($key) -or
       -not $localKeys.Contains($key) -or
       -not $triageKeys.Contains($key)){
        throw "Expanded PC source target key does not match the prior local/retail audits: $key"
    }
    $choices=@($r.Candidates)
    $hashes=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $origins=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $sourceList=New-Object 'System.Collections.Generic.List[object]'
    foreach($choice in $choices){
        $hash=[string]$choice.SourceSha256
        $file=[string]$choice.File
        $entry=[string]$choice.Entry
        $kind=[string]$choice.RootKind
        $sourceType=[string]$choice.SourceType
        if($hash -notmatch '^[0-9a-fA-F]{64}$' -or
           [string]::IsNullOrWhiteSpace($file) -or
           $kind -notin @('pc-map-library','map-assets-library','ugx-map-library','zct-map-library') -or
           $sourceType -notin @('iwd','loose')){
            throw "Source evidence is invalid/unexpected for image $key in $file"
        }
        [void]$hashes.Add($hash)
        [void]$origins.Add($kind)
        if(-not $packages.ContainsKey($kind)){$packages[$kind]=0}
        $packages[$kind]++
        $sourceList.Add([pscustomobject]@{
            Kind=$kind;Type=$sourceType;Path=$file;Entry=$entry
            Sha256=$hash;Bytes=[long]$choice.Bytes
        })
    }
    if([bool]$r.FoundPcCandidate -ne ($choices.Count -gt 0) -or
       [int]$r.CandidateCount -ne $choices.Count -or
       [int]$r.DistinctContentShaCount -ne $hashes.Count -or
       [bool]$r.ConflictingCandidateContent -ne ($hashes.Count -gt 1)){
        throw "Candidate hash/identity reconciliation drift for $key"
    }
    $category='NO_PC_SOURCE_CANDIDATE'
    if($hashes.Count -gt 1){$category='AMBIGUOUS_EXTERNAL_CONTENT'}
    elseif($hashes.Count -eq 1){$category='SINGLE_HASH_EXTERNAL_NAME_CANDIDATE'}

    $out=[pscustomobject]@{
        ImageName=[string]$r.Name;Key=$key;Classification=$category
        IsNativeImageRoot=[bool]$r.IsNativeAztecImageRoot
        LooksGenerated=[bool]$r.LooksGenerated
        CandidateCount=$choices.Count
        DistinctSha256Count=$hashes.Count
        SourceLibraries=[string[]]@($origins | Sort-Object)
        Candidates=$sourceList.ToArray()
        IsDirectAztecSource=$false
        PermittedForAutomaticProductionGraft=$false
    }
    $allRows.Add($out)
    if($category -eq 'NO_PC_SOURCE_CANDIDATE'){$unmatched.Add($out)}
    else{
        $matched.Add($out)
        if($category -eq 'AMBIGUOUS_EXTERNAL_CONTENT'){$conflicts.Add($out)}
        else{$unambiguous.Add($out)}
    }
}
if($seenKeys.Count -ne 181 -or $matched.Count -ne 32 -or
   $conflicts.Count -ne 8 -or $unambiguous.Count -ne 24 -or
   $unmatched.Count -ne 149){
    throw "Extended-PC image candidate classification drift: matched=$($matched.Count) ambiguous=$($conflicts.Count) singleHash=$($unambiguous.Count) unresolved=$($unmatched.Count)"
}
$nativeMissing=@($unmatched.ToArray() | Where-Object {$_.IsNativeImageRoot}).Count
if($nativeMissing -ne 146){throw "Expected 146 unmatched native roots; saw $nativeMissing"}
$generatedMissing=@($unmatched.ToArray() | Where-Object {$_.LooksGenerated}).Count
if($generatedMissing -ne 30){throw "Expected 30 unmatched generated-looking names; saw $generatedMissing"}

$manifest=[ordered]@{
    schema='WaW360.AztecPcImageCandidateReview.v1'
    status='AZTEC_PC_IMAGE_CANDIDATE_PROVENANCE_PASS'
    targetRetailUnmatchedNames=181
    externalLibraryNameMatches=32
    externalCandidatesWithOneUniqueHash=24
    externalCandidatesWithConflictingHashes=8
    withoutExternalSourceCandidate=149
    withoutExternalSourceCandidateNativeRoots=146
    withoutExternalSourceCandidateGeneratedLooking=30
    directAztecOwnedNewCandidates=0
    confirmedProductionSafeCandidates=0
    sourceOccurrencesByLibrary=$packages
    sourceContentRehashedInThisReview=$false
    allCandidateHashesFromPriorReadOnlyAudit=$true
    noXboxReferencePixelsCopied=$true
    noImagePayloadGrafted=$true
    noXboxFFWritten=$true
    hardwareReady=$false
    notes=@(
        'Names in unrelated PC maps do not prove the correct Aztec texture semantics, dimensions, format, or ownership.',
        '24 identical-hash candidate identities are only nonconflicting within the scanned libraries, not production-cleared assets.',
        '8 conflicting-hash identities are excluded from any automatic selection; require original map-author/build provenance.',
        '149 unmatched names may include generated composites, in-fastfile data references, or source packages absent from the scan.',
        'No archive entry or source file is read in this pass; uses the previous hashed evidence to avoid repeated scanning.'
    )
    rows=$allRows.ToArray()
}
$json=Join-Path $work 'aztec_pc_image_candidate_provenance.json'
$txt=Join-Path $work 'aztec_pc_image_candidate_provenance.txt'
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $json -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add('AZTEC PC IMAGE CANDIDATE PROVENANCE - NO AUTOMATIC SOURCE PROMOTION')
$lines.Add('====================================================================')
$lines.Add("status=$($manifest.status)")
$lines.Add('181 no-stock identities: 32 cross-package candidates, 24 single-hash, 8 conflicting, 149 unmatched')
$lines.Add('None of the 32 are backed by the original Aztec IWD/loose map sources. Zero production-safe image payloads declared.')
foreach($key in @($packages.Keys | Sort-Object)){$lines.Add("LIBRARY-APPEARANCES $key=$($packages[$key])")}
$lines.Add('')
$lines.Add('EIGHT CONFLICTING-HASH IMAGE IDENTITIES - DO NOT AUTO-SELECT')
foreach($r in $conflicts.ToArray()){
    $lines.Add("CONFLICT $($r.ImageName) sources=$($r.CandidateCount) distinctHashes=$($r.DistinctSha256Count)")
    foreach($src in $r.Candidates){$lines.Add("  [$($src.Kind)] $($src.Path)::$($src.Entry) [$($src.Bytes) B SHA256=$($src.Sha256)]")}
}
$lines.Add('')
$lines.Add('24 NONCONFLICTING-HASH EXTERNAL CANDIDATES - SOURCE OWNERSHIP UNPROVEN')
foreach($r in $unambiguous.ToArray()){
    $lines.Add("CANDIDATE $($r.ImageName) sources=$($r.CandidateCount) hashCount=$($r.DistinctSha256Count)")
    foreach($src in $r.Candidates){$lines.Add("  [$($src.Kind)] $($src.Path)::$($src.Entry) [$($src.Bytes) B SHA256=$($src.Sha256)]")}
}
$lines.Add('')
$lines.Add('149 NAMES WITHOUT A FOUND SOURCE')
foreach($r in $unmatched.ToArray()){
    $lines.Add("UNFOUND $($r.ImageName) native=$($r.IsNativeImageRoot) generated=$($r.LooksGenerated)")
}
$lines.Add('')
$lines.Add('NEXT: locate original Aztec build/texture source dependencies or prove valid stock runtime asset references.')
$lines.Add('No Xbox game content copied, no PC textures promoted, no FF generated.')
[IO.File]::WriteAllLines($txt,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-PROV] ================= RESULT ==============================='
Write-Host "[AZTEC-PC-PROV] status=$($manifest.status)"
Write-Host "[AZTEC-PC-PROV] 181 names: external matches=$($matched.Count), nonconflicting hash=$($unambiguous.Count), conflicts=$($conflicts.Count), without candidates=$($unmatched.Count)"
Write-Host "[AZTEC-PC-PROV] Unmatched native roots=$nativeMissing; unmatched generated-looking=$generatedMissing"
foreach($r in $conflicts.ToArray()){Write-Host "[AZTEC-PC-PROV] CONFLICT: $($r.ImageName) ($($r.DistinctSha256Count) hashes)"}
Write-Host "[AZTEC-PC-PROV] Candidate report: $txt"
Write-Host '[AZTEC-PC-PROV] Original Aztec payload ownership not proven. Zero FF emission / Xbox donor copies.'
