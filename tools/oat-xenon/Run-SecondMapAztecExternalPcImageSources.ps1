[CmdletBinding()]
param(
    [switch]$ExtendedSources,
    [string]$MapAssetsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\map assets',
    [string]$UgxRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\UGX',
    [string]$ZctRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\ZCT'
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$work=Join-Path $base 'native_material_mapping\pc_fastfile_image_sources'
$triagePath=Join-Path $work 'aztec_image_dependency_triage.json'
$baselinePath=Join-Path $base 'second_map_baseline.json'
$nativePath=Join-Path $base 'native_material_mapping\main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
foreach($p in @($triagePath,$baselinePath,$nativePath)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){throw "Prior Aztec evidence missing: $p"}
}
$triage=Get-Content -LiteralPath $triagePath -Raw | ConvertFrom-Json
$baseline=Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
$native=Get-Content -LiteralPath $nativePath -Raw | ConvertFrom-Json
if([string]$triage.schema -ne 'WaW360.AztecImageDependencyTriage.v1' -or
    [string]$triage.status -ne 'AZTEC_IMAGE_DEPENDENCY_TRIAGE_PASS' -or
    [int]$triage.missingFromAll67RetailZones -ne 181 -or
    [int]$triage.unresolvedNativeImageRootNames -ne 178 -or
    [int]$triage.unresolvedGeneratedLookingNames -ne 32 -or
    @($triage.rows).Count -ne 654 -or
    [string]$baseline.format -ne 'WaW360.SecondMapBaseline.v1' -or
    [string]$baseline.mapName -ine 'nazi_zombie_aztec_V2.2' -or
    -not [bool]$native.TypeSequenceVerified -or
    @($native.Entries).Count -ne 8509){
    throw 'Aztec 654/181 missing pixel triage or native 8,509-root manifest changed.'
}
$targets=@($triage.rows | Where-Object {$_.AuditPartition -eq 'not-in-scanned-retail'})
if($targets.Count -ne 181){throw "Expected 181 retail-unmatched targets, got $($targets.Count)"}
function Normalize([string]$v){
    if([string]::IsNullOrWhiteSpace($v)){return ''}
    $v=$v.Trim().Trim('"').Replace('\','/').TrimStart('/')
    $marker='/images/'
    $idx=$v.LastIndexOf($marker,[StringComparison]::OrdinalIgnoreCase)
    if($idx -ge 0){$v=$v.Substring($idx+$marker.Length)}
    elseif($v.StartsWith('images/',[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(7)}
    foreach($ext in @('.iwi','.dds')){
        if($v.EndsWith($ext,[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(0,$v.Length-$ext.Length)}
    }
    if($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(0,$v.Length-3)}
    return $v.ToLowerInvariant()
}
$targetIndex=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
foreach($target in $targets){
    $key=Normalize ([string]$target.Key)
    if([string]::IsNullOrWhiteSpace($key) -or $targetIndex.ContainsKey($key)){throw "Duplicate invalid target: $key"}
    $targetIndex.Add($key,$target)
}
$pcRoot=[string]$baseline.sourceRoot
if(-not (Test-Path -LiteralPath $pcRoot -PathType Container)){
    throw "PC map source root absent: $pcRoot"
}
$roots=New-Object 'System.Collections.Generic.List[object]'
$seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
function Add-SourceDir([string]$path,[string]$kind){
    if([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path -PathType Container)){return}
    $full=(Resolve-Path -LiteralPath $path).Path
    if($full.StartsWith((Join-Path $repo 'output')+'\',[StringComparison]::OrdinalIgnoreCase) -or
        $full.StartsWith((Join-Path $repo '.cache')+'\',[StringComparison]::OrdinalIgnoreCase)){
        throw "Output/cache directory is not a permitted PC source: $full"
    }
    if($seen.Add($full)){$roots.Add([pscustomobject]@{Kind=$kind;Path=$full})}
}
# Default search is restricted to the Aztec map package and installed map
# folders; it does NOT imply a PC retail game install or any Xbox donor data.
Add-SourceDir -path (Join-Path $pcRoot 'nazi_zombie_aztec_V2.2') -kind 'pc-aztec-source'
Add-SourceDir -path (Join-Path $pcRoot 'nazi_zombie_aztec') -kind 'pc-aztec-alt'
foreach($appVar in @('LOCALAPPDATA','APPDATA')){
    $app=[Environment]::GetEnvironmentVariable($appVar)
    if([string]::IsNullOrWhiteSpace($app)){continue}
    foreach($sub in @('mods','usermaps')){
        foreach($name in @('nazi_zombie_aztec_V2.2','nazi_zombie_aztec')){
            Add-SourceDir -path (Join-Path $app ("Activision\CoDWaW\$sub\$name")) -kind ("installed-"+$sub)
        }
    }
}
if($ExtendedSources){
    # Optional wider search may contain unrelated map packages. Image names
    # found there are CANDIDATES only, not automatically Aztec-owned bytes.
    Add-SourceDir -path $pcRoot -kind 'pc-map-library'
    Add-SourceDir -path $MapAssetsRoot -kind 'map-assets-library'
    Add-SourceDir -path $UgxRoot -kind 'ugx-map-library'
    Add-SourceDir -path $ZctRoot -kind 'zct-map-library'
}
if($roots.Count -eq 0){throw 'No legitimate PC Aztec source folders were found.'}
$hits=New-Object 'System.Collections.Generic.List[object]'
$stats=New-Object 'System.Collections.Generic.List[object]'
$warnings=New-Object 'System.Collections.Generic.List[string]'
$observed=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$largeArchiveLimit=0
Write-Host '[AZTEC-PC-SOURCE] ====================================================='
Write-Host '[AZTEC-PC-SOURCE] FIND AZTEC CUSTOM IMAGE SOURCES MISSING IN 67 XBOX FFS'
Write-Host "[AZTEC-PC-SOURCE] Targets: $($targets.Count) (178 native; 32 generated-looking)"
Write-Host "[AZTEC-PC-SOURCE] Scan roots=$($roots.Count) extended=$([bool]$ExtendedSources)"
Write-Host '[AZTEC-PC-SOURCE] Read-only .iwi/.dds and .iwd entry audit; no FF.'
Write-Host '[AZTEC-PC-SOURCE] ====================================================='
foreach($root in $roots.ToArray()){
    $looseCount=0;$iwdCount=0;$entryCount=0;$matchedCount=0;$archiveErrors=0
    $files=@()
    foreach($pat in @('*.iwi','*.dds')){
        $files+=@(Get-ChildItem -LiteralPath ([string]$root.Path) -Recurse -File -Filter $pat -ErrorAction SilentlyContinue)
    }
    foreach($file in $files){
        $looseCount++
        $name=Normalize ([string]$file.FullName)
        if(-not $targetIndex.ContainsKey($name)){continue}
        $identity=([string]$file.FullName).ToLowerInvariant()
        if(-not $observed.Add($identity)){continue}
        $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        $hits.Add([pscustomobject]@{
            Key=$name;RootKind=[string]$root.Kind;SourceType='loose'
            File=$file.FullName;Entry='';Bytes=[long]$file.Length
            SourceSha256=$hash;ShaType='loose-content';Extension=$file.Extension.ToLowerInvariant()
            IsNative=[bool]$targetIndex[$name].IsNativeAztecImageRoot
            SourceOwnershipEstablished=$false
        })
        $matchedCount++
    }
    foreach($archive in @(Get-ChildItem -LiteralPath ([string]$root.Path) -Recurse -File -Filter '*.iwd' -ErrorAction SilentlyContinue)){
        $iwdCount++
        $zip=$null
        try{
            $zip=[IO.Compression.ZipFile]::OpenRead($archive.FullName)
            foreach($entry in $zip.Entries){
                if($entry.FullName.EndsWith('/')){continue}
                $ext=[IO.Path]::GetExtension($entry.FullName).ToLowerInvariant()
                if($ext -notin @('.iwi','.dds')){continue}
                $entryCount++
                $key=Normalize ([string]$entry.FullName)
                if(-not $targetIndex.ContainsKey($key)){continue}
                $identifier=([string]$archive.FullName+'::'+[string]$entry.FullName).ToLowerInvariant()
                if(-not $observed.Add($identifier)){continue}
                # Hash only the matching entry. This reads legitimate PC
                # source bytes in memory, but neither extracts nor copies
                # image payloads into the output tree.
                $stream=$entry.Open()
                $sha=[Security.Cryptography.SHA256]::Create()
                try{
                    $digest=([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','')
                }finally{$sha.Dispose();$stream.Dispose()}
                $hits.Add([pscustomobject]@{
                    Key=$key;RootKind=[string]$root.Kind;SourceType='iwd'
                    File=$archive.FullName;Entry=[string]$entry.FullName;Bytes=[long]$entry.Length
                    SourceSha256=$digest;ShaType='iwd-entry-content';Extension=$ext
                    IsNative=[bool]$targetIndex[$key].IsNativeAztecImageRoot
                    SourceOwnershipEstablished=$false
                })
                $matchedCount++
            }
        }catch{
            $archiveErrors++
            $warnings.Add("IWD unreadable: $($archive.FullName): $($_.Exception.Message)")
        }finally{if($null -ne $zip){$zip.Dispose()}}
    }
    $stats.Add([pscustomobject]@{
        RootKind=[string]$root.Kind;Path=[string]$root.Path
        LooseIwiDds=$looseCount;IwdArchives=$iwdCount
        IwdImageEntries=$entryCount;MatchingCandidates=$matchedCount
        ArchiveErrors=$archiveErrors
    })
    Write-Host "[AZTEC-PC-SOURCE] $($root.Kind): loose=$looseCount iwd=$iwdCount entries=$entryCount matched=$matchedCount errors=$archiveErrors"
}
$byTarget=@{}
foreach($hit in $hits.ToArray()){
    if(-not $byTarget.ContainsKey([string]$hit.Key)){
        $byTarget[[string]$hit.Key]=New-Object 'System.Collections.Generic.List[object]'
    }
    $byTarget[[string]$hit.Key].Add($hit)
}
$found=0;$ambiguous=0;$foundNative=0;$missingNative=0;$generatedMissing=0
$rows=New-Object 'System.Collections.Generic.List[object]'
foreach($target in $targets){
    $key=Normalize ([string]$target.Key)
    $choices=if($byTarget.ContainsKey($key)){[object[]]$byTarget[$key].ToArray()}else{[object[]]@()}
    $shas=@($choices | Select-Object -ExpandProperty SourceSha256 -Unique)
    $conflict=($shas.Count -gt 1)
    if($choices.Count -gt 0){$found++}
    if($conflict){$ambiguous++}
    $isNative=[bool]$target.IsNativeAztecImageRoot
    if($isNative -and $choices.Count -gt 0){$foundNative++}
    if($isNative -and $choices.Count -eq 0){$missingNative++}
    if([bool]$target.LooksGenerated -and $choices.Count -eq 0){$generatedMissing++}
    $rows.Add([pscustomobject]@{
        Key=$key;Name=[string]$target.Name;LooksGenerated=[bool]$target.LooksGenerated
        IsNativeAztecImageRoot=$isNative
        FoundPcCandidate=($choices.Count -gt 0)
        CandidateCount=$choices.Count;DistinctContentShaCount=$shas.Count
        ConflictingCandidateContent=$conflict
        Candidates=[object[]]$choices
    })
}
if(($found+$(@($rows.ToArray() | Where-Object {-not $_.FoundPcCandidate}).Count)) -ne 181){
    throw 'Aztec 181-target source hunt lost source-identity rows.'
}
$status=if($warnings.Count -eq 0){'AZTEC_PC_EXTERNAL_IMAGE_SOURCE_AUDIT_PASS'}else{'AZTEC_PC_EXTERNAL_IMAGE_SOURCE_AUDIT_PARTIAL'}
$report=[ordered]@{
    schema='WaW360.AztecExternalPcImageSourceAudit.v1';status=$status
    scannedExtendedPcSourceLibraries=[bool]$ExtendedSources
    nativeImageRoots=666;retailUnmatchedTargetNames=181;retailUnmatchedNativeRoots=178
    candidateMatchedTargetNames=$found
    candidateMatchedNativeRoots=$foundNative
    nativeRootsWithoutCandidateInScannedSources=$missingNative
    candidateConflictingContentNames=$ambiguous
    generatedLookingNamesWithoutCandidate=$generatedMissing
    sourceRoots=$stats.ToArray();warnings=$warnings.ToArray()
    results=$rows.ToArray()
    zeroXboxFastfilesWritten=$true
    zeroXboxRetailPixelsCopied=$true
    zeroPCPixelPayloadsCopied=$true
    readyForHardware=$false
    productionSourceEquivalenceProven=$false
    note='Names and hashes from PC map or user map-asset libraries are candidate evidence only. Other installed packages are not guaranteed to be Aztec-owned. No pixel conversion, no FF emission, no runtime proof.'
}
$json=Join-Path $work ('aztec_external_pc_image_sources'+$(if($ExtendedSources){'_extended'}else{''})+'.json')
$txt=Join-Path $work ('aztec_external_pc_image_sources'+$(if($ExtendedSources){'_extended'}else{''})+'.txt')
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $json -Encoding UTF8
$out=New-Object 'System.Collections.Generic.List[string]'
$out.Add("AZTEC: PC EXTERNAL IMAGE SOURCE AUDIT $status")
$out.Add("181 no-retail image names: found source candidates=$found; remaining without candidate=$(181-$found)")
$out.Add("178 native-root names: found=$foundNative; no candidate=$missingNative")
$out.Add("Content-conflict names=$ambiguous; generated-looking names without candidates=$generatedMissing")
$out.Add("Extended source libraries=$([bool]$ExtendedSources); scanned source roots=$($roots.Count); unreadable archives=$($warnings.Count)")
foreach($row in $rows.ToArray()){
    $mark=if($row.FoundPcCandidate){'CANDIDATE'}else{'MISSING'}
    $out.Add("$mark $($row.Name) [native=$($row.IsNativeAztecImageRoot) generated=$($row.LooksGenerated) hits=$($row.CandidateCount) conflicts=$($row.ConflictingCandidateContent)]")
    foreach($candidate in $row.Candidates){
        $out.Add("    source=$($candidate.RootKind) $($candidate.File)::$($candidate.Entry) sha=$($candidate.SourceSha256)")
    }
}
foreach($warn in $warnings.ToArray()){$out.Add("WARN $warn")}
$out.Add('No source equivalence or runtime residency proven. No FFs, donor bytes or pixel copies.')
[IO.File]::WriteAllLines($txt,[string[]]$out.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-SOURCE] ================= RESULT ========================='
Write-Host "[AZTEC-PC-SOURCE] status=$status"
Write-Host "[AZTEC-PC-SOURCE] 181 no-retail identities: PC source candidates=$found, absent-in-scan=$(181-$found)"
Write-Host "[AZTEC-PC-SOURCE] 178 native roots: candidates=$foundNative, absent-in-scan=$missingNative"
Write-Host "[AZTEC-PC-SOURCE] conflicts=$ambiguous generated-looking-without-source=$generatedMissing"
Write-Host "[AZTEC-PC-SOURCE] Report: $txt"
Write-Host '[AZTEC-PC-SOURCE] Read-only source audit. No OAT, no FF, no Xbox payload graft.'
if($warnings.Count -gt 0){exit 4}
