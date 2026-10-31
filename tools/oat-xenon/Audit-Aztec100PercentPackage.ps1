[CmdletBinding()]
param(
    [string]$ReferenceMapDirectory='',
    [string]$ConvertedMapDirectory='',
    [string]$ReportDirectory=''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# READ ONLY: enforce the user's 100% package requirement against an existing
# working Xbox Aztec DIRECTORY TOPOLOGY. Never copy any reference asset bytes
# into conversion output and never mistake equal files for converted files.
# This is NOT a production/hardware approval tool; even 100% file names
# says nothing about engine validity of the contained assets.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$map='nazi_zombie_aztec'
if([string]::IsNullOrWhiteSpace($ReferenceMapDirectory)){
    $ReferenceMapDirectory=Join-Path $env:USERPROFILE 'OneDrive\Desktop\My Xbox\World at War\codxe latest (working map references)\nazi_zombie_aztec'
}
if([string]::IsNullOrWhiteSpace($ConvertedMapDirectory)){
    $ConvertedMapDirectory=Join-Path $repo 'output\complete_conversion\nazi_zombie_aztec'
}
if([string]::IsNullOrWhiteSpace($ReportDirectory)){
    $ReportDirectory=Join-Path $repo 'output\aztec_full_package_closure'
}
if(-not(Test-Path -LiteralPath $ReferenceMapDirectory -PathType Container)){
    throw "Reference Aztec map inventory missing; pass -ReferenceMapDirectory with your existing known-working native Aztec folder: $ReferenceMapDirectory"
}
$ref=(Resolve-Path -LiteralPath $ReferenceMapDirectory).Path.TrimEnd('\','/')
$candidate=[IO.Path]::GetFullPath($ConvertedMapDirectory).TrimEnd('\','/')
if($ref -ieq $candidate){throw 'ConvertedMapDirectory must NEVER be the native Xbox reference collection.'}
if($candidate.StartsWith($ref+'\',[StringComparison]::OrdinalIgnoreCase) -or
   $ref.StartsWith($candidate+'\',[StringComparison]::OrdinalIgnoreCase)){
    throw 'Converted and native reference roots may not be nested inside one another.'
}
function Relative([string]$absolute,[string]$root) {
    $prefix=$root.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if(-not $absolute.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){
        throw "File outside bounded inventory root: $absolute"
    }
    $rel=$absolute.Substring($prefix.Length).Replace('\','/')
    if([IO.Path]::IsPathRooted($rel) -or $rel -match '(^|/)\.\.(/|$)'){
        throw "Unsafe relative filename: $rel"
    }
    return $rel
}
function SafeTree([string]$root){
    $all=@(Get-Item -LiteralPath $root)
    $all+=@(Get-ChildItem -LiteralPath $root -Force -Recurse -ErrorAction Stop)
    foreach($item in $all){
        if(([int]$item.Attributes -band [int][IO.FileAttributes]::ReparsePoint) -ne 0){
            throw "Inventory rejects reparse point: $($item.FullName)"
        }
    }
    return @($all | Where-Object {-not $_.PSIsContainer} | Sort-Object FullName)
}
$refFiles=@(SafeTree $ref)
if($refFiles.Count -lt 2 -or
   @($refFiles | Where-Object {$_.Name -ieq ($map+'.ff')}).Count -ne 1){
    throw 'Native reference is not a plausible complete Aztec package with a main FF.'
}
$haveCandidate=Test-Path -LiteralPath $candidate -PathType Container
$candidateFiles=@()
if($haveCandidate){$candidateFiles=@(SafeTree $candidate)}
$byRelative=@{}
foreach($item in $candidateFiles){
    $rel=Relative $item.FullName $candidate
    if($byRelative.ContainsKey($rel)){throw "Duplicate candidate relative path: $rel"}
    $byRelative[$rel]=$item
}
function Category([string]$rel){
    $name=[IO.Path]::GetFileName($rel)
    if($name -ieq ($map+'.ff')){return 'MAIN_FF'}
    if($name -ieq ($map+'_load.ff')){return 'LOAD_FF'}
    if($name -ieq ($map+'_patch.ff')){return 'PATCH_FF'}
    if($rel -match '^(?i:highmip)/'){return 'HIGHMIP'}
    if($rel -match '^(?i:sounds)/'){return 'SOUND'}
    if($name -match '(?i)\.pak$'){return 'PAK'}
    if($name -match '(?i)\.bik$'){return 'BIK'}
    if($name -match '(?i)\.dds$'){return 'DDS'}
    if($name -match '(?i)\.arena$'){return 'ARENA'}
    if($name -match '(?i)\.json$'){return 'JSON'}
    return 'OTHER_COMPANION'
}
$rows=New-Object 'System.Collections.Generic.List[object]'
$missing=New-Object 'System.Collections.Generic.List[string]'
$inheritedNativeFF=New-Object 'System.Collections.Generic.List[string]'
$seen=@{}
foreach($item in $refFiles){
    $rel=Relative $item.FullName $ref
    $cat=Category $rel
    $present=$byRelative.ContainsKey($rel)
    $referenceBytes=[long]$item.Length
    $candidateBytes=if($present){[long]$byRelative[$rel].Length}else{0L}
    $candidateSha=if($present){
        [string](Get-FileHash -LiteralPath $byRelative[$rel].FullName -Algorithm SHA256).Hash
    }else{''}
    # Byte-identical native reference FastFiles are not evidence of conversion.
    # Never silently accept a copied-oracle main/load/patch FF as genuine PC2XB3.
    $referenceSha=if($present -and $cat -in @('MAIN_FF','LOAD_FF','PATCH_FF')){
        [string](Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
    }else{''}
    $copiedNativeFF=$present -and $referenceSha -ne '' -and $referenceSha -ieq $candidateSha
    if(-not $present){$missing.Add($rel)}
    if($copiedNativeFF){$inheritedNativeFF.Add($rel)}
    $seen[$rel]=$true
    $rows.Add([pscustomobject]@{
        relativePath=$rel
        type=$cat
        referenceBytes=$referenceBytes
        generatedFileExists=$present
        generatedBytes=$candidateBytes
        generatedSha256=$candidateSha
        forbiddenReferenceFfShaMatch=$copiedNativeFF
        sizeMatchRequired=$false
        sourcePayloadProven=$false
        sourceOwnerVerification='PENDING'
        nativeEngineLoadValidated=$false
    })
}
$extra=@()
foreach($rel in @($byRelative.Keys | Sort-Object)){
    if(-not $seen.ContainsKey($rel)){$extra+=,$rel}
}
$rowsArray=@($rows.ToArray())
$presentCount=@($rowsArray | Where-Object {$_.generatedFileExists}).Count
$mainPresent=@($rowsArray | Where-Object {$_.type -eq 'MAIN_FF' -and $_.generatedFileExists}).Count -eq 1
$loadPresent=@($rowsArray | Where-Object {$_.type -eq 'LOAD_FF' -and $_.generatedFileExists}).Count -eq 1
$missingCounts=@{}
foreach($row in $rowsArray){
    if(-not $row.generatedFileExists){
        $type=[string]$row.type
        if(-not $missingCounts.ContainsKey($type)){$missingCounts[$type]=0}
        $missingCounts[$type]++
    }
}
New-Item -ItemType Directory -Force -Path $ReportDirectory | Out-Null
$json=Join-Path $ReportDirectory 'aztec_full_package_layout_closure.json'
$report=[ordered]@{
    schema='WaW360.AztecFullPackageLayoutClosure.v1'
    status='NOT_100_PERCENT_CONVERTED_NO_NATIVE_DEPLOY'
    generatedUtc=[DateTime]::UtcNow.ToString('o')
    nativeReferenceRole='TOPOLOGY_AND_IDENTITY_ORACLE_ONLY_NEVER_DONOR'
    referenceMapDirectory=$ref
    convertedMapDirectory=$candidate
    convertedMapFolderExists=[bool]$haveCandidate
    nativeReferenceFileCount=$refFiles.Count
    generatedFilesAtExpectedPaths=$presentCount
    missingExpectedFiles=$missing.Count
    missingByRole=$missingCounts
    referenceFfsCopiedIntoCandidate=@($inheritedNativeFF.ToArray())
    additionalFilesRequireSourceReview=@($extra)
    fileRows=$rowsArray
    layoutNamesComplete=($missing.Count -eq 0 -and $extra.Count -eq 0)
    runtimeAssetGraphClosureVerified=$false
    fullGfxWorldDpvsVerified=$false
    staticDrawRecordPointersLinked=0
    requiredStaticDrawRecordPointers=1903
    imageSourcePayloadsClosed=$false
    nativeSoundXmaRuntimeClosureVerified=$false
    scriptsAndWeaponBodiesClosed=$false
    mainLoadPatchProductionOatAndRuntimeVerified=$false
    perFileSourceOwnershipEvidenceComplete=$false
    nativeProductionReleaseAuthorized=$false
    note='Full original PC+Xbox retail provenance and runtime dependency graph is REQUIRED. Matching file names or bytes is NOT conversion. Reference FF bytes MUST NEVER be used as production data. No FFs generated, copied or modified by this audit.'
}
$report|ConvertTo-Json -Depth 9|Set-Content -LiteralPath $json -Encoding UTF8
Write-Host '[AZTEC-100-PERCENT] ====================================================='
Write-Host "[AZTEC-100-PERCENT] Expected native reference layout: $($refFiles.Count) files"
Write-Host "[AZTEC-100-PERCENT] Converted output files at expected paths: $presentCount/$($refFiles.Count)"
Write-Host "[AZTEC-100-PERCENT] Missing expected files: $($missing.Count); additional candidate files: $($extra.Count)"
Write-Host "[AZTEC-100-PERCENT] Source main=$mainPresent source load=$loadPresent; forbidden byte-identical reference FFs=$($inheritedNativeFF.Count)"
foreach($role in @($missingCounts.Keys | Sort-Object)){
    Write-Host "[AZTEC-100-PERCENT] MISSING ROLE $role : $($missingCounts[$role])"
}
foreach($name in @($missing.ToArray() | Select-Object -First 12)){
    Write-Host "[AZTEC-100-PERCENT] MISSING $name"
}
Write-Host '[AZTEC-100-PERCENT] Production graph: 0/1903 linked draw pointers; full DPVS/images/audio/script dependencies remain UNVERIFIED.'
Write-Host "[AZTEC-100-PERCENT] REPORT=$json"
Write-Host '[AZTEC-100-PERCENT] STOP: no Xbox output approval. Reference file names are layout witnesses ONLY.'
