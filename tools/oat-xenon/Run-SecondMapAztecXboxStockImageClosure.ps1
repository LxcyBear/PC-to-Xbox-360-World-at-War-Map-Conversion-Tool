[CmdletBinding()]
param(
    [string]$XboxGameRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\XBOX GAME FILES',
    [switch]$AllRetailZones,
    [switch]$Rescan,
    [switch]$CacheOnly
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($CacheOnly -and $Rescan){throw 'Cannot use -CacheOnly and -Rescan together.'}
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$nativeWork=Join-Path $base 'native_material_mapping'
$identityPath=Join-Path $nativeWork 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$sourcePath=Join-Path $nativeWork 'pc_fastfile_image_sources\pc_fastfile_image_sources.json'
$rawPath=Join-Path $nativeWork 'pc_rawfile_graft\pc_rawfile_graft_result.json'
$unlinker=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$logRoot=Join-Path $nativeWork 'pc_fastfile_image_sources\stock_identity_logs'
$outRoot=Join-Path $nativeWork 'pc_fastfile_image_sources'
foreach($f in @($identityPath,$sourcePath,$rawPath,$unlinker)){
    if(-not (Test-Path -LiteralPath $f -PathType Leaf)){throw "Prerequisite missing: $f"}
}
if(-not (Test-Path -LiteralPath $XboxGameRoot -PathType Container)){
    throw "Xbox retail game-file root missing: $XboxGameRoot. Use -XboxGameRoot with your locally owned Xbox game files."
}
$xboxRoot=(Resolve-Path -LiteralPath $XboxGameRoot).Path
$identity=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$source=Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json
$raw=Get-Content -LiteralPath $rawPath -Raw | ConvertFrom-Json
if(-not [bool]$identity.TypeSequenceVerified -or
    @($identity.Entries).Count -ne 8509 -or
    [string]$source.schema -ne 'WaW360.AztecPcFastfileImageSourceCensus.v1' -or
    [string]$source.status -ne 'AZTEC_PC_FF_IMAGE_SOURCE_DUMP_PARTIAL_RECOVERED' -or
    [int]$source.nativeImageRoots -ne 666 -or
    [int]$source.pcFastfileNameMatchesNativeRoots -ne 647 -or
    [string]$raw.status -ne 'AZTEC_PC_RAWFILE_GRAFT_OAT_READBACK_PASS' -or
    [int]$raw.xboxReadbacksExact -ne 114){
    throw 'Aztec native identity, PC image partial dump or 114/114 PC RawFile gate changed.'
}
function Normalize([string]$s){
    $v=$s.Trim().Trim('"').Replace('\','/').TrimStart('/')
    $mark='/images/'
    $at=$v.LastIndexOf($mark,[StringComparison]::OrdinalIgnoreCase)
    if($at -ge 0){$v=$v.Substring($at+$mark.Length)}
    elseif($v.StartsWith('images/',[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(7)}
    foreach($ext in @('.iwi','.dds')){
        if($v.EndsWith($ext,[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(0,$v.Length-$ext.Length)}
    }
    if($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(0,$v.Length-3)}
    return $v.ToLowerInvariant()
}
$nativeImages=@($identity.Entries | Where-Object { [string]$_.TypeName -ieq 'image' })
if($nativeImages.Count -ne 666){throw "Expected 666 Aztec Xbox image roots, found $($nativeImages.Count)"}
$allNative=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($x in $nativeImages){[void]$allNative.Add((Normalize ([string]$x.Name)))}
$errors=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
$roles=@('map.ff','map_patch.ff','mod.ff')
foreach($role in $roles){
    $path=Join-Path $outRoot ('logs\extract_'+$role+'.log')
    if(-not (Test-Path -LiteralPath $path -PathType Leaf)){throw "Original OAT failure log missing: $path"}
    foreach($line in [IO.File]::ReadLines($path)){
        if($line -notmatch '(?i)Could not find data for image\s+"([^"]+)"'){continue}
        $name=[string]$Matches[1]
        $key=Normalize $name
        if(-not $errors.ContainsKey($key)){
            $errors[$key]=[pscustomobject]@{Name=$name;Key=$key;Roles=(New-Object 'System.Collections.Generic.List[string]');Occurrences=0}
        }
        $errors[$key].Occurrences++
        if(-not $errors[$key].Roles.Contains($role)){$errors[$key].Roles.Add($role)}
    }
}
if($errors.Count -eq 0){throw 'No missing-external-image errors parsed from the previous OAT output.'}
$totalErrorOccurrences=0
foreach($entry in $errors.Values){$totalErrorOccurrences += [int]$entry.Occurrences}
if($totalErrorOccurrences -ne 775){
    throw "Expected 775 source OAT missing-external-image failures; parsed $totalErrorOccurrences. Recheck the source logs before scanning stock Xbox files."
}
$available=@(
    Get-ChildItem -LiteralPath $xboxRoot -Filter '*.ff' -File -Recurse -ErrorAction Stop |
        Sort-Object -Property FullName
)
if($available.Count -eq 0){throw "No Xbox fastfiles (.ff) exist under $xboxRoot"}
$coreNames=@('common.ff','code_post_gfx.ff','ui.ff','patch.ff','common_mp.ff',
    'code_post_gfx_mp.ff','ui_mp.ff','nazi_zombie_prototype.ff','nazi_zombie_prototype_load.ff')
$zones=if($AllRetailZones){@($available)}else{@($available | Where-Object { $_.Name.ToLowerInvariant() -in $coreNames })}
if($zones.Count -eq 0){throw 'No core/zombie retail fastfiles found; use -AllRetailZones for broader local evidence.'}
New-Item -Path $logRoot -ItemType Directory -Force | Out-Null

# Legacy eight-zone scan used ordinal-prefixed cache files. When the scope
# expands to all retail zones, sorted ordinals change. Index ALL prior good
# cache metadata by the actual retail FF path, not by its prior ordinal.
# This preserves the eight fully completed OAT --list scans automatically.
$cachedBySource=@{}
foreach($priorMeta in @(Get-ChildItem -LiteralPath $logRoot -Filter '*.meta.json' -File -ErrorAction SilentlyContinue | Sort-Object Name)){
    try{
        $m=Get-Content -LiteralPath $priorMeta.FullName -Raw | ConvertFrom-Json
        $prevLog=$priorMeta.FullName.Substring(0,$priorMeta.FullName.Length - '.meta.json'.Length)
        if([string]::IsNullOrWhiteSpace([string]$m.file) -or
           [int]$m.exitCode -ne 0 -or
           -not (Test-Path -LiteralPath $prevLog -PathType Leaf)){continue}
        $cachedBySource[[string]$m.file]=[pscustomobject]@{
            Meta=$m
            Log=$prevLog
        }
    }catch{
        # Invalid/stale cache entries are never reused. The selected retail
        # zone will be scanned afresh or fail closed in -CacheOnly mode.
        continue
    }
}
Write-Host '[AZTEC-STOCK-IMG] ==========================================================='
Write-Host '[AZTEC-STOCK-IMG] LOCATE EXTERNAL IMAGE ROOTS IN OWNED RETAIL XBOX ZONES'
Write-Host "[AZTEC-STOCK-IMG] Prior image errors: $totalErrorOccurrences occurrences, $($errors.Count) distinct names"
Write-Host "[AZTEC-STOCK-IMG] Retail identity scan: $($zones.Count) zones"
Write-Host '[AZTEC-STOCK-IMG] Names and zone identities only. NO RETAIL PIXEL COPIES.'
if($CacheOnly){Write-Host '[AZTEC-STOCK-IMG] CACHE-ONLY: no OAT process will be launched.'}
Write-Host '[AZTEC-STOCK-IMG] ==========================================================='

$index=@{}
$zoneStats=New-Object 'System.Collections.Generic.List[object]'
$ord=0
foreach($ff in $zones){
    $ord++
    $safe=([IO.Path]::GetFileNameWithoutExtension($ff.Name) -replace '[^A-Za-z0-9_.-]','_')
    # Stable path-derived key prevents different zones with the same basename
    # overwriting each other's OAT evidence. The hash is of the PATH, not game
    # content, and must never be interpreted as asset-equivalence evidence.
    $hasher=[Security.Cryptography.SHA256]::Create()
    try{
        $hashBytes=$hasher.ComputeHash(
            [Text.Encoding]::UTF8.GetBytes($ff.FullName.ToLowerInvariant()))
        $pathHash=([BitConverter]::ToString($hashBytes)).Replace('-','').Substring(0,16)
    }finally{$hasher.Dispose()}
    $cacheName=($safe+'_'+$pathHash+'.list.log')
    $stableLog=Join-Path $logRoot $cacheName
    $meta=Join-Path $logRoot ($cacheName+'.meta.json')
    $log=$stableLog
    $fileInfo=Get-Item -LiteralPath $ff.FullName
    $reuse=$false
    if(-not $Rescan -and $cachedBySource.ContainsKey($ff.FullName)){
        $old=$cachedBySource[$ff.FullName]
        $m=$old.Meta
        if([string]$m.file -ieq $ff.FullName -and
            [long]$m.bytes -eq $fileInfo.Length -and
            [string]$m.modifiedUtc -eq $fileInfo.LastWriteTimeUtc.ToString('o') -and
            [int]$m.exitCode -eq 0 -and
            (Test-Path -LiteralPath ([string]$old.Log) -PathType Leaf)){
            $reuse=$true
            $log=[string]$old.Log
        }
    }
    if($CacheOnly -and -not $reuse){
        throw "Cached retail OAT --list evidence is missing, changed, or failed for $($ff.Name): $log. Refusing to rescan in -CacheOnly mode."
    }
    $exit=0
    if(-not $reuse){
        $log=$stableLog
        $save=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            & $unlinker '--list' '--no-color' $ff.FullName 2>&1 |
                ForEach-Object {[string]$_} |
                Set-Content -LiteralPath $log -Encoding UTF8
            $exit=[int]$LASTEXITCODE
        }finally{$ErrorActionPreference=$save}
        @{file=$ff.FullName;bytes=[long]$fileInfo.Length;modifiedUtc=$fileInfo.LastWriteTimeUtc.ToString('o');exitCode=$exit} |
            ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $meta -Encoding UTF8
    }
    $listed=0;$hits=0
    if($exit -eq 0){
        foreach($line in [IO.File]::ReadLines($log)){
            # OAT --list can prefix each row with [assetIndex] before image,...
            $raw=$line.Trim()
            if($raw.StartsWith('[',[StringComparison]::Ordinal)){
                $close=$raw.IndexOf(']')
                if($close -ge 0 -and $close+1 -lt $raw.Length){$raw=$raw.Substring($close+1).TrimStart()}
            }
            if($raw -notmatch '(?i)^image\s*,\s*(.*?)\s*$'){continue}
            $key=Normalize ([string]$Matches[1])
            $listed++
            if($errors.ContainsKey($key)){
                if(-not $index.ContainsKey($key)){$index[$key]=New-Object 'System.Collections.Generic.List[string]'}
                if(-not $index[$key].Contains($ff.Name)){$index[$key].Add($ff.Name)}
                $hits++
            }
        }
    }
    $zoneStats.Add([pscustomobject]@{Zone=$ff.Name;Path=$ff.FullName
        Loaded=$exit -eq 0;OatExitCode=$exit;ListedImages=$listed
        TargetNameMatches=$hits;CacheReused=$reuse;Log=$log})
    Write-Host "[AZTEC-STOCK-IMG] $ord/$($zones.Count) $($ff.Name): exit=$exit imageNames=$listed missingPCNameHits=$hits cached=$reuse"
}
$rows=New-Object 'System.Collections.Generic.List[object]'
$nativeMatched=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$uniqueFailures=0;$matchingFailures=0;$generatedNames=0
foreach($key in @($errors.Keys | Sort-Object)){
    $e=$errors[$key]
    # Windows PowerShell 5.1 unwraps pipeline results of an if expression.
    # An empty else branch may become $null under StrictMode, then .Count
    # throws even though 'not found in retail' is perfectly valid evidence.
    # Always assign an explicit strongly typed array, including zero matches.
    [string[]]$zonesForKey=@()
    if($index.ContainsKey($key)){
        $zonesForKey=[string[]]$index[$key].ToArray()
    }
    $inNative=$allNative.Contains($key)
    $generated=$key.StartsWith('~',[StringComparison]::Ordinal)
    if($generated){$generatedNames++}
    if($zonesForKey.Count -gt 0){
        $matchingFailures++
        if($inNative){[void]$nativeMatched.Add($key)}
    }else{$uniqueFailures++}
    $rows.Add([pscustomobject]@{Name=$e.Name;Key=$key;OatMissingOccurrences=[int]$e.Occurrences
        SourceRoles=$e.Roles.ToArray();MatchesNativeAztecRoot=$inNative
        LooksGenerated=$generated;FoundInScannedXboxRetail=$zonesForKey.Count -gt 0
        XboxRetailZones=$zonesForKey})
}
$scannedPaths=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($ff in $zones){[void]$scannedPaths.Add([string]$ff.FullName)}
$notScanned=@($available | Where-Object {-not $scannedPaths.Contains([string]$_.FullName)}).Count
$complete=(@($zoneStats.ToArray() | Where-Object {-not $_.Loaded}).Count -eq 0)
$status=if($complete){'AZTEC_EXTERNAL_IMAGE_RETAIL_IDENTITY_AUDIT_PASS'}else{'AZTEC_EXTERNAL_IMAGE_RETAIL_IDENTITY_AUDIT_PARTIAL'}
$summary=[ordered]@{
    schema='WaW360.AztecExternalImageRetailIdentity.v1'
    status=$status
    scope=$(if($AllRetailZones){'all-retail-zones'}else{'core-zombie-zones-only'})
    distinctFailedPcImageNames=$errors.Count
    missingPcImageOatErrorsAcrossRoles=$totalErrorOccurrences
    failedNamesFoundInScannedXboxRetail=$matchingFailures
    failedNamesNotFoundInScannedXboxRetail=$uniqueFailures
    generatedLookingNames=$generatedNames
    failedNamesAlsoNativeImageRoot=$(@($rows.ToArray() | Where-Object {$_.MatchesNativeAztecRoot}).Count)
    matchingNativeImageRoots=$nativeMatched.Count
    zonesScanned=$zoneStats.Count
    otherZonesNotScanned=$notScanned
    scanComplete=$complete
    cacheOnly=[bool]$CacheOnly
    usedOnlyVerifiedCachedLogs=[bool]($CacheOnly -and $complete)
    zones=$zoneStats.ToArray()
    missingPcImageClassifications=$rows.ToArray()
    copiedXboxPixelPayloads=0
    reusedNativeImagePayloads=0
    xboxFastfileBuilt=$false
    hardwareReady=$false
    sourcePolicy='Read-only source-name lookup against locally owned retail Xbox zones. No donor bytes are copied or released. Identity matches are NOT graph/value equivalence.'
}
# Keep the first eight-zone passing proof intact. The wider stock scan is
# an additional independent identity audit, never a destructive replacement.
$scopeSuffix=if($AllRetailZones){'_all_zones'}else{''}
$json=Join-Path $outRoot ('pc_external_image_xbox_stock_closure'+$scopeSuffix+'.json')
$txt=Join-Path $outRoot ('pc_external_image_xbox_stock_closure'+$scopeSuffix+'.txt')
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $json -Encoding UTF8
$report=New-Object 'System.Collections.Generic.List[string]'
$report.Add('AZTEC: MISSING PC IMAGE PIXELS VS XBOX RETAIL IMAGE IDENTITIES')
$report.Add('================================================================')
$report.Add("status=$status scannedZones=$($zoneStats.Count) otherZonesNotScanned=$notScanned")
$report.Add("missing PC pixel names=$($errors.Count), found in scanned retail zones=$matchingFailures, not found=$uniqueFailures")
$report.Add("native-image-root names matched in retail=$($nativeMatched.Count); generated-looking missing names=$generatedNames")
$report.Add('Identity matches do not prove equivalent pixel payloads or runtime residency.')
foreach($z in $zoneStats){$report.Add("ZONE $($z.Zone): imageRows=$($z.ListedImages) hits=$($z.TargetNameMatches) OAT=$($z.OatExitCode)")}
foreach($x in @($rows | Where-Object {$_.FoundInScannedXboxRetail} | Select-Object -First 100)){
    $report.Add("RETAIL-IDENTITY $($x.Name): $($x.XboxRetailZones -join ', ')")
}
foreach($x in @($rows | Where-Object {-not $_.FoundInScannedXboxRetail} | Select-Object -First 100)){
    $report.Add("NO-RETAIL-IDENTITY-YET $($x.Name)")
}
$report.Add('NO output fastfile; NO Xbox retail bytes copied; no Converted Maps promotion.')
[IO.File]::WriteAllLines($txt,[string[]]$report.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-STOCK-IMG] ========================= RESULT ========================='
Write-Host "[AZTEC-STOCK-IMG] status=$status"
Write-Host "[AZTEC-STOCK-IMG] Unique PC missing-pixel names=$($errors.Count)"
Write-Host "[AZTEC-STOCK-IMG] Found in scanned Xbox stock zones=$matchingFailures"
Write-Host "[AZTEC-STOCK-IMG] Still unresolved by these zones=$uniqueFailures"
Write-Host "[AZTEC-STOCK-IMG] Report: $txt"
Write-Host '[AZTEC-STOCK-IMG] Identity evidence only. No FF and no donor payload copies.'
if(-not $complete){exit 4}
