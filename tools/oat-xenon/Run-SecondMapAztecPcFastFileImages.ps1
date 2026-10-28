[CmdletBinding()]
param(
    [switch]$InventoryOnly,
    [switch]$ReusePartialDump
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($InventoryOnly -and $ReusePartialDump){throw 'Use either -InventoryOnly or -ReusePartialDump, not both.'}
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$map='nazi_zombie_aztec_V2.2'
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$nativeWork=Join-Path $base 'native_material_mapping'
$baselinePath=Join-Path $base 'second_map_baseline.json'
$identityPath=Join-Path $nativeWork 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$iwdCensusPath=Join-Path $nativeWork 'pc_image_source_census\pc_image_source_census_result.json'
$rawGraftPath=Join-Path $nativeWork 'pc_rawfile_graft\pc_rawfile_graft_result.json'
$unlinker=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
foreach($file in @($baselinePath,$identityPath,$iwdCensusPath,$rawGraftPath,$unlinker)) {
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Verified Aztec prerequisite missing: $file" }
}
$baseline=Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
$identity=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$iwd=Get-Content -LiteralPath $iwdCensusPath -Raw | ConvertFrom-Json
$raw=Get-Content -LiteralPath $rawGraftPath -Raw | ConvertFrom-Json
if([string]$baseline.format -ne 'WaW360.SecondMapBaseline.v1' -or
   [string]$baseline.mapName -ine $map -or
   [string]$iwd.schema -ne 'WaW360.AztecPcImageSourceCensus.v1' -or
   [string]$iwd.status -ne 'AZTEC_PC_IWD_IMAGE_CENSUS_PASS' -or
   [int]$iwd.nativeImageRoots -ne 666 -or [int]$iwd.matchedNativeImageIdentities -ne 2 -or
   [string]$raw.status -ne 'AZTEC_PC_RAWFILE_GRAFT_OAT_READBACK_PASS' -or
   [int]$raw.xboxReadbacksExact -ne 114 -or
   -not [bool]$identity.TypeSequenceVerified -or
   @($identity.Entries).Count -ne 8509) {
    throw 'Aztec PC source baseline, 114/114 RawFile or 2/666 IWD census proof mismatch.'
}
function Normalize-Image([string]$value) {
    $n=$value.Replace('\','/').TrimStart('/')
    $marker='/images/'
    $offset=$n.LastIndexOf($marker,[StringComparison]::OrdinalIgnoreCase)
    if($offset -ge 0){$n=$n.Substring($offset+$marker.Length)}
    elseif($n.StartsWith('images/',[StringComparison]::OrdinalIgnoreCase)){$n=$n.Substring(7)}
    if($n.EndsWith('.iwi',[StringComparison]::OrdinalIgnoreCase)){$n=$n.Substring(0,$n.Length-4)}
    if($n.EndsWith('.dds',[StringComparison]::OrdinalIgnoreCase)){$n=$n.Substring(0,$n.Length-4)}
    if($n.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){$n=$n.Substring(0,$n.Length-3)}
    return $n.ToLowerInvariant()
}
$nativeImages=@($identity.Entries | Where-Object { [string]$_.TypeName -ieq 'image' })
if($nativeImages.Count -ne 666){throw "Expected exactly 666 native image roots; got $($nativeImages.Count)"}
$sourceFFs=@($baseline.fastfiles | Where-Object { $_.Role -in @('map.ff','map_patch.ff','mod.ff') })
if($sourceFFs.Count -ne 3 -or @($sourceFFs | Where-Object {$_.Role -eq 'map.ff'}).Count -ne 1) {
    throw 'Aztec image source census needs three distinct known PC main/patch/mod fastfiles.'
}
$sourceHashes=@{}
foreach($source in $sourceFFs){
    $p=[string]$source.Path
    if(-not (Test-Path -LiteralPath $p -PathType Leaf) -or
        [long](Get-Item -LiteralPath $p).Length -ne [long]$source.Bytes) {
        throw "PC source fastfile changed since Aztec baseline: $p"
    }
    $sourceHashes[$p]=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash
}
$work=Join-Path $nativeWork 'pc_fastfile_image_sources'
$corpus=Join-Path $work 'pc_fastfile_image_corpus'
$logRoot=Join-Path $work 'logs'
New-Item -Path $work -ItemType Directory -Force | Out-Null
New-Item -Path $logRoot -ItemType Directory -Force | Out-Null

# First consume existing cached OAT --list evidence. It is possible to count
# PC image names without any extraction, rebuilding OAT, or making an FF.
$inventory=New-Object 'System.Collections.Generic.List[object]'
$byName=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
$ffOrdinal=0
foreach($source in @($baseline.fastfiles)) {
    $ffOrdinal++
    $role=[string]$source.Role
    if($role -notin @('map.ff','map_patch.ff','mod.ff')){continue}
    $path=[string]$source.Path
    $stem=([IO.Path]::GetFileNameWithoutExtension($path)) -replace '[^A-Za-z0-9_.-]','_'
    $listLog=Join-Path $base ('oat_asset_lists\{0:D2}_{1}.list.log' -f $ffOrdinal,$stem)
    if(-not (Test-Path -LiteralPath $listLog -PathType Leaf)){throw "Prior OAT image --list evidence missing: $listLog"}
    $names=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach($line in [IO.File]::ReadLines($listLog)) {
        if($line -match '^\s*(?:\[[^\]]+\]\s*)?image\s*,\s*(.*?)\s*$') {
            $name=Normalize-Image ([string]$Matches[1])
            if($name) { [void]$names.Add($name) }
        }
    }
    $expected=0
    $prop=$source.AssetCounts.PSObject.Properties['image']
    if($null -ne $prop){$expected=[int]$prop.Value}
    foreach($name in $names){
        if(-not $byName.ContainsKey($name)){
            $byName.Add($name,(New-Object 'System.Collections.Generic.List[string]'))
        }
        $byName[$name].Add($role)
    }
    $inventory.Add([pscustomobject]@{
        Role=$role; SourceFastFile=$path; Sha256=$sourceHashes[$path]
        ParsedImageRootCount=$expected; OatListUniqueImageNames=$names.Count
        Log=$listLog
    })
    Write-Host "[AZTEC-PC-FF-IMAGE] $($role) listed distinct image names: $($names.Count), parsed roots=$expected"
}
$nativeKeys=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($root in $nativeImages){[void]$nativeKeys.Add((Normalize-Image ([string]$root.Name)))}
if($byName.Count -eq 0) {
    throw 'No image identities parsed from prior PC --list logs. Check image listing format before declaring source coverage.'
}
$listedMatch=0
foreach($root in $nativeImages){
    if($byName.ContainsKey((Normalize-Image ([string]$root.Name)))){$listedMatch++}
}
$crossNames=@($byName.Keys | Where-Object { $byName[$_].Count -gt 1 } | Sort-Object)
$extracted=New-Object 'System.Collections.Generic.List[object]'
$roleStats=New-Object 'System.Collections.Generic.List[object]'
$allErrors=New-Object 'System.Collections.Generic.List[string]'
if(-not $InventoryOnly) {
    # Extract into short PHYSICAL paths because pinned OAT resolves real path
    # before writing. Copies are transferred with Robocopy to the single
    # ignored Aztec corpus; original PC FFs and native golden FF are read-only.
    if(-not $ReusePartialDump) {
        if(Test-Path -LiteralPath $corpus -PathType Container){Remove-Item -LiteralPath $corpus -Recurse -Force}
        New-Item -Path $corpus -ItemType Directory -Force | Out-Null
    } elseif(-not (Test-Path -LiteralPath $corpus -PathType Container)) {
        throw "Previous partial OAT image corpus is absent: $corpus"
    }
    foreach($source in $sourceFFs) {
        $role=[string]$source.Role
        $src=[string]$source.Path
        if($ReusePartialDump) {
            # Previous OAT run already copied its IWIs out of the short-path
            # scratch directory. It crashed in PowerShell reporting, AFTER
            # the files and OAT logs had been preserved. Never rerun OAT merely
            # to retry report construction. Re-check all current source FF hashes
            # (done above) and rehash each cached IWI for diagnostic provenance.
            $log=Join-Path $logRoot ('extract_'+$role+'.log')
            $dest=Join-Path $corpus $role
            if(-not (Test-Path -LiteralPath $log -PathType Leaf) -or
               -not (Test-Path -LiteralPath $dest -PathType Container)) {
                throw "Missing previous OAT extraction log/corpus for role $($role). Run without -ReusePartialDump only if a fresh extraction is required."
            }
            $files=@(Get-ChildItem -LiteralPath $dest -Filter '*.iwi' -File -Recurse -ErrorAction Stop)
            $warnings=@(Select-String -LiteralPath $log -Pattern '^\s*WARNING:').Count
            $errors=@(Select-String -LiteralPath $log -Pattern '^\s*ERROR:').Count
            $roleStats.Add([pscustomobject]@{
                Role=$role; OatExitCode=$null; OatWarnings=$warnings
                OatErrors=$errors; DumpedIwiFiles=$files.Count; Log=$log
                Source='CACHED_PARTIAL_FROM_PREVIOUS_RUN'; ExitCodeRecovered=$false
            })
            $allErrors.Add("$($role): prior OAT image dump retained $($files.Count) IWI file(s); $errors errors in log. Original process exit cannot be authenticated from cache.")
            foreach($file in $files) {
                $relative=$file.FullName.Substring($dest.Length).TrimStart([char]'\')
                $key=Normalize-Image $relative
                if(-not $key){continue}
                $extracted.Add([pscustomobject]@{
                    Role=$role; Name=$key; RelativePath=$relative
                    SourceFF=$src; SourceFFSHA256=$sourceHashes[$src]
                    Sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
                    Bytes=[long]$file.Length; MatchesNative=$nativeKeys.Contains($key)
                    Provenance='CACHED_PC_OAT_OUTPUT_NEEDS_REPRODUCIBLE_EXTRACTION_FOR_PRODUCTION'
                })
            }
            Write-Host "[AZTEC-PC-FF-IMAGE] $($role) cached: $($files.Count) IWI(s), prior errors=$errors; OAT not rerun"
            continue
        }
        $temp=Join-Path ([IO.Path]::GetTempPath()) ('wawAzImg_'+[guid]::NewGuid().ToString('N').Substring(0,10))
        if($temp.Length -gt 115){throw "OAT physical temp path too long: $temp"}
        New-Item -Path $temp -ItemType Directory -Force | Out-Null
        $log=Join-Path $logRoot ('extract_'+$role+'.log')
        $files=@()
        $code=-1
        $warnings=0
        $errors=0
        try {
            $args=@('--no-color','--include-assets','image','--image-format','IWI','--output-folder',$temp,
                '--search-path',(Split-Path -Parent $src),$src)
            $old=$ErrorActionPreference
            try {
                $ErrorActionPreference='Continue'
                & $unlinker @args 2>&1 | ForEach-Object {[string]$_} |
                    Set-Content -LiteralPath $log -Encoding UTF8
                $code=[int]$LASTEXITCODE
            } finally {$ErrorActionPreference=$old}
            $warnings=@(Select-String -LiteralPath $log -Pattern '^\s*WARNING:').Count
            $errors=@(Select-String -LiteralPath $log -Pattern '^\s*ERROR:').Count
            $files=@(Get-ChildItem -LiteralPath $temp -Filter '*.iwi' -Recurse -File -ErrorAction SilentlyContinue)
            foreach($file in $files) {
                $relative=$file.FullName.Substring($temp.Length).TrimStart([char]'\')
                $key=Normalize-Image $relative
                if(-not $key){continue}
                $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
                $extracted.Add([pscustomobject]@{
                    Role=$role; Name=$key; RelativePath=$relative
                    SourceFF=$src; SourceFFSHA256=$sourceHashes[$src]
                    Sha256=$hash; Bytes=[long]$file.Length
                    MatchesNative=$nativeKeys.Contains($key)
                })
            }
            # Robocopy's exit codes 0..7 are success; >=8 is a copy error.
            $dest=Join-Path $corpus $role
            New-Item -Path $dest -ItemType Directory -Force | Out-Null
            $rcOutput=@(& robocopy.exe $temp $dest '/E' '/R:1' '/W:1' '/NP' '/NFL' '/NDL' '/NJH' '/NJS' 2>&1 |
                ForEach-Object {[string]$_})
            $rcCode=[int]$LASTEXITCODE
            if($rcCode -ge 8){throw "PC IWI long-path copy failed for $role (robocopy=$rcCode)."}
        } finally {
            if(Test-Path -LiteralPath $temp -PathType Container){Remove-Item -LiteralPath $temp -Recurse -Force}
        }
        $roleStats.Add([pscustomobject]@{
            Role=$role; OatExitCode=$code; OatWarnings=$warnings
            OatErrors=$errors; DumpedIwiFiles=$files.Count; Log=$log
        })
        if($code -ne 0 -or $errors -ne 0) {
            $allErrors.Add("$role OAT extraction incomplete: exit=$code errorLines=$errors; partial PC source dump preserved.")
        }
        Write-Host "[AZTEC-PC-FF-IMAGE] $($role) OAT exit=$code; dumped IWI=$($files.Count); errors=$errors"
    }
}
$grouped=@{}
foreach($record in $extracted) {
    if(-not $grouped.ContainsKey($record.Name)) {
        $grouped[$record.Name]=New-Object 'System.Collections.Generic.List[object]'
    }
    $grouped[$record.Name].Add($record)
}
$unambiguous=0
$conflictKeys=New-Object 'System.Collections.Generic.List[string]'
$matchedNative=0
foreach($root in $nativeImages) {
    $key=Normalize-Image ([string]$root.Name)
    if(-not $grouped.ContainsKey($key)){continue}
    $variants=[object[]]$grouped[$key].ToArray()
    if(@($variants | Select-Object -ExpandProperty Sha256 -Unique).Count -gt 1) {
        if(-not $conflictKeys.Contains($key)){$conflictKeys.Add($key)}
        continue
    }
    $matchedNative++
}
$iwdKeys=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$priorIwd=Join-Path $base 'nuclear_4g_iwi_image_corpus\nuclear_4g_iwi_image_corpus.json'
if(-not (Test-Path -LiteralPath $priorIwd -PathType Leaf)){throw "Prior authoritative IWD image names missing: $priorIwd"}
$iwdRaw=Get-Content -LiteralPath $priorIwd -Raw | ConvertFrom-Json
if([int]$iwdRaw.MatchedNativeImages -ne 2){throw 'IWD source census changed since verified Aztec proof.'}
foreach($image in @($iwdRaw.LargestMatchedImages)){[void]$iwdKeys.Add((Normalize-Image ([string]$image.CanonicalName)))}
$union=0
foreach($root in $nativeImages) {
    $key=Normalize-Image ([string]$root.Name)
    $pcAvailable=$grouped.ContainsKey($key)
    $pcSafe=$false
    if($pcAvailable) {
        $v=[object[]]$grouped[$key].ToArray()
        $pcSafe=(@($v | Select-Object -ExpandProperty Sha256 -Unique).Count -eq 1)
    }
    if($pcSafe -or $iwdKeys.Contains($key)){$union++}
}
$status=if($InventoryOnly){'AZTEC_PC_FF_IMAGE_NAME_INVENTORY_PASS'}
elseif($ReusePartialDump -and $extracted.Count -gt 0){'AZTEC_PC_FF_IMAGE_SOURCE_DUMP_PARTIAL_RECOVERED'}
elseif($ReusePartialDump){'AZTEC_PC_FF_IMAGE_SOURCE_DUMP_BLOCKED'}
elseif($allErrors.Count -eq 0 -and $extracted.Count -gt 0){'AZTEC_PC_FF_IMAGE_SOURCE_DUMP_PASS'}
elseif($extracted.Count -gt 0){'AZTEC_PC_FF_IMAGE_SOURCE_DUMP_PARTIAL'}
else{'AZTEC_PC_FF_IMAGE_SOURCE_DUMP_BLOCKED'}
# Preserve useful failure signatures without another expensive OAT redump.
# OAT sometimes exits 0 after hundreds of per-image ERROR lines; the loader
# status is incomplete regardless of native process exit code.
$errorSamples=New-Object 'System.Collections.Generic.List[object]'
$errorCategories=@{}
foreach($stat in $roleStats) {
    $examples=0
    if(-not (Test-Path -LiteralPath $stat.Log -PathType Leaf)){continue}
    foreach($line in [IO.File]::ReadLines([string]$stat.Log)) {
        if($line -notmatch '(?i)(ERROR:|Could not find data for image|unsupported|failed to load)'){continue}
        $kind=if($line -match '(?i)Could not find data for image'){ 'MISSING_EXTERNAL_IMAGE_DATA' }
              elseif($line -match '(?i)unsupported|not supported'){ 'UNSUPPORTED_IMAGE_FORMAT' }
              elseif($line -match '(?i)Failed to load'){ 'IMAGE_LOADING_ERROR' }
              else { 'OTHER_OAT_IMAGE_ERROR' }
        $id=([string]$stat.Role)+'/'+$kind
        if(-not $errorCategories.ContainsKey($id)){$errorCategories[$id]=0}
        $errorCategories[$id]++
        if($examples -lt 8){
            $errorSamples.Add([pscustomobject]@{Role=[string]$stat.Role;Category=$kind;Message=[string]$line})
            $examples++
        }
    }
}
$json=Join-Path $work 'pc_fastfile_image_sources.json'
$txt=Join-Path $work 'pc_fastfile_image_sources.txt'
$summary=[ordered]@{
    schema='WaW360.AztecPcFastfileImageSourceCensus.v1'
    status=$status
    nativeImageRoots=666
    distinctPcFastfileImageNames=$byName.Count
    pcFastfileNameMatchesNativeRoots=$listedMatch
    pcFastfileImageNamesSharedAcrossSources=$crossNames.Count
    iwdOnlyPriorMatchedNativeRoots=2
    extractedPcIwiFiles=$extracted.Count
    distinctExtractedPcIwiNames=$grouped.Count
    sourceByteUnambiguousNativeImageRoots=$matchedNative
    sourceByteConflictImageNames=[string[]]$conflictKeys.ToArray()
    combinedIwdAndExtractedPcIwiCoverage=$union
    nativeImageRootsStillWithoutMatchedSource=(666-$union)
    sourceFastfiles=$inventory.ToArray()
    extractionStages=$roleStats.ToArray()
    extractedSourceImages=$extracted.ToArray()
    warnings=$allErrors.ToArray()
    errorCategoryCounts=$errorCategories
    firstErrorSamples=$errorSamples.ToArray()
    reusedPreviousPartialOatDump=[bool]$ReusePartialDump
    cachedIwiProvenanceFullyRevalidated=$false
    priorOatProcessExitRecovered=$false
    convertedXboxImagePayloads=0
    pcPixelConversionComplete=$false
    hardwareReady=$false
    convertedMapsPromotionApproved=$false
    provenance='PC source .ff and IWD-derived IWI only. Native Xbox manifest contributes identities; no Xbox image payloads copied.'
    note='OAT-exported IWI is PC source candidate evidence only. Cached files from an interrupted reporting run are not independently proven reproducible. Source-name matching is NOT Xenon pixel conversion or materials dependency proof.'
}
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $json -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add('AZTEC: PC FASTFILE IMAGE SOURCES VS 666 NATIVE IMAGE ROOTS')
$lines.Add('================================================================')
$lines.Add("status=$status")
$lines.Add("PC FF --list unique names=$($byName.Count); native identity hits=$listedMatch/666")
$lines.Add("Shared PC FF image names=$($crossNames.Count)")
$lines.Add("PC OAT-exported IWI files=$($extracted.Count); matched native roots with unambiguous payload hashes=$matchedNative")
$lines.Add("Reused prior partial extraction=$([bool]$ReusePartialDump); only hashes of cached IWI files were rechecked, not OAT extraction reproducibility")
$lines.Add("Duplicate-name PC FF source conflicts=$($conflictKeys.Count)")
$lines.Add("Combined source image matches (PC FF + 2 IWD)=$union/666; no-match=$($summary.nativeImageRootsStillWithoutMatchedSource)")
foreach($stat in $roleStats){$lines.Add("  $($stat.Role): IWI=$($stat.DumpedIwiFiles) OAT=$($stat.OatExitCode) errors=$($stat.OatErrors) log=$($stat.Log)")}
foreach($err in $allErrors){$lines.Add("  WARNING: $err")}
foreach($key in @($errorCategories.Keys | Sort-Object)){$lines.Add("  ERROR CATEGORY $($key)=$($errorCategories[$key])")}
foreach($example in @($errorSamples | Select-Object -First 12)){$lines.Add("  SAMPLE [$($example.Role)/$($example.Category)] $($example.Message)")}
foreach($name in @($conflictKeys | Select-Object -First 30)){$lines.Add("  CONFLICT $name")}
$lines.Add('PC source classification only: ZERO native Xbox image bytes converted or emitted.')
$lines.Add('NOT FOR XBOX; Converted Maps untouched.')
[IO.File]::WriteAllLines($txt,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-FF-IMAGE] ================= RESULT ==========================='
Write-Host "[AZTEC-PC-FF-IMAGE] status=$status"
Write-Host "[AZTEC-PC-FF-IMAGE] PC FF image names=$($byName.Count), native matches=$listedMatch/666"
Write-Host "[AZTEC-PC-FF-IMAGE] PC IWI files=$($extracted.Count), unambiguous native payload identities=$matchedNative"
Write-Host "[AZTEC-PC-FF-IMAGE] Combined PC FF/IWD source identities=$union/666; remaining no-match=$($summary.nativeImageRootsStillWithoutMatchedSource)"
Write-Host "[AZTEC-PC-FF-IMAGE] Report: $txt"
Write-Host '[AZTEC-PC-FF-IMAGE] Source evidence ONLY. No Xbox FF created.'
if($status -eq 'AZTEC_PC_FF_IMAGE_SOURCE_DUMP_BLOCKED'){exit 4}
