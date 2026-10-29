[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$n=Join-Path $base 'native_material_mapping'
$baselinePath=Join-Path $base 'second_map_baseline.json'
$identityPath=Join-Path $n 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$combinedPath=Join-Path $n 'pc_iwd_rawfile_integration\aztec_pc_iwd_rawfile_integration.json'
$pcImagePath=Join-Path $n 'pc_fastfile_image_sources\pc_fastfile_image_sources.json'
$work=Join-Path $n 'pc_model_world_source_frontier'
foreach($path in @($baselinePath,$identityPath,$combinedPath,$pcImagePath)){
    if(-not (Test-Path -LiteralPath $path -PathType Leaf)){throw "Aztec prerequisite missing: $path"}
}
$baseline=Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
$native=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$combined=Get-Content -LiteralPath $combinedPath -Raw | ConvertFrom-Json
$pcImages=Get-Content -LiteralPath $pcImagePath -Raw | ConvertFrom-Json
if([string]$baseline.format -ne 'WaW360.SecondMapBaseline.v1' -or
   [string]$baseline.mapName -ine 'nazi_zombie_aztec_V2.2' -or
   -not [bool]$native.TypeSequenceVerified -or
   @($native.Entries).Count -ne 8509 -or
   [string]$combined.status -ne 'AZTEC_PC_IWD_RAWFILE_COMBINED_GRAPH_PASS' -or
   [int]$combined.nativeRoots -ne 8509 -or
   [int]$combined.pixelShaExactReadbacks -ne 2 -or
   [int]$combined.rawFileShaExactReadbacks -ne 114 -or
   [string]$pcImages.schema -ne 'WaW360.AztecPcFastfileImageSourceCensus.v1' -or
   [string]$pcImages.status -ne 'AZTEC_PC_FF_IMAGE_SOURCE_DUMP_PARTIAL_RECOVERED'){
    throw 'The passing 8509-root mixed PC image/RawFile baseline or source FF census changed.'
}
function Normalize-Name([string]$s){
    $s=$s.Trim().Trim('"').Replace('\','/').TrimStart('/')
    if($s.StartsWith(',',[StringComparison]::Ordinal)){$s=$s.Substring(1)}
    if($s.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){$s=$s.Substring(0,$s.Length-3)}
    return $s.ToLowerInvariant()
}
$sourceFFs=@($baseline.fastfiles | Where-Object { [string]$_.Role -in @('map.ff','map_patch.ff','mod.ff') })
if($sourceFFs.Count -ne 3){throw 'Aztec source baseline must contain exactly the three PC main/patch/mod FF roles.'}
$roles=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
foreach($record in @($pcImages.sourceFastfiles)){
    $role=[string]$record.Role
    if($roles.ContainsKey($role)){throw "Duplicate previous PC FF inventory role: $role"}
    $roles.Add($role,$record)
}
if($roles.Count -ne 3){throw 'The previous PC source image inventory must provide three role-specific FF hashes.'}
$interesting=@('xmodel','gfxworld','fxworld','clipmap','comworld','gameworldsp',
    'gameworldmp','mapents','physcollmap','colmap','xmodelsurfs','xmodelpieces',
    'material','image','rawfile')
$records=New-Object 'System.Collections.Generic.List[object]'
$sourceModels=@{}
foreach($source in $sourceFFs){
    $role=[string]$source.Role
    if(-not $roles.ContainsKey($role)){throw "PC source image hash inventory does not contain role $role"}
    $path=[string]$source.Path
    if(-not (Test-Path -LiteralPath $path -PathType Leaf) -or
       [long](Get-Item -LiteralPath $path).Length -ne [long]$source.Bytes -or
       [string]$roles[$role].SourceFastFile -ine $path -or
       (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine [string]$roles[$role].Sha256){
        throw "The pinned original PC source FF changed since previous audits: $role"
    }
}
$ordinal=0
foreach($ff in @($baseline.fastfiles)){
    $ordinal++
    $role=[string]$ff.Role
    if($role -notin @('map.ff','map_patch.ff','mod.ff')){continue}
    $stem=([IO.Path]::GetFileNameWithoutExtension([string]$ff.Path)) -replace '[^A-Za-z0-9_.-]','_'
    $log=Join-Path $base ('oat_asset_lists\{0:D2}_{1}.list.log' -f $ordinal,$stem)
    if(-not (Test-Path -LiteralPath $log -PathType Leaf)){
        throw "Previous OAT PC source --list cache is missing: $log"
    }
    $found=@{}
    foreach($type in $interesting){$found[$type]=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)}
    $rowsRead=0
    foreach($line in [IO.File]::ReadLines($log)){
        $s=$line.Trim()
        if($s.StartsWith('[',[StringComparison]::Ordinal)){
            $end=$s.IndexOf(']')
            if($end -ge 0 -and $end+1 -lt $s.Length){$s=$s.Substring($end+1).TrimStart()}
        }
        if($s -notmatch '^([A-Za-z_][A-Za-z0-9_]*)\s*,\s*(.*?)\s*$'){continue}
        $type=([string]$Matches[1]).ToLowerInvariant()
        if(-not $found.ContainsKey($type)){continue}
        $name=[string]$Matches[2]
        if([string]::IsNullOrWhiteSpace($name)){continue}
        $rowsRead++
        [void]$found[$type].Add($name)
        if($type -eq 'xmodel'){
            $key=Normalize-Name $name
            if(-not $sourceModels.ContainsKey($key)){
                $sourceModels[$key]=New-Object 'System.Collections.Generic.List[object]'
            }
            $sourceModels[$key].Add([pscustomobject]@{Role=$role;Name=$name;SourceFF=[string]$ff.Path})
        }
    }
    $models=@($found['xmodel'] | Sort-Object)
    $worldSummary=@{}
    foreach($type in $interesting){$worldSummary[$type]=$found[$type].Count}
    $record=[pscustomobject]@{
        Role=$role;SourceFF=[string]$ff.Path;Log=$log;AssetRowsParsed=$rowsRead
        UniqueXModels=$models.Count;UniqueTypes=$worldSummary;XModelNames=[string[]]$models
    }
    $records.Add($record)
    Write-Host "[AZTEC-MODEL-WORLD] $($role): xmodels=$($models.Count) parsedRelevantRows=$rowsRead"
}
$nativeRoots=@($native.Entries | Where-Object {[string]$_.TypeName -ieq 'xmodel'})
if($nativeRoots.Count -eq 0 -or $sourceModels.Count -eq 0){
    throw 'Aztec native or PC XModel identity inventory is empty; do not claim a model conversion frontier.'
}
$nativeRecords=New-Object 'System.Collections.Generic.List[object]'
$matchedOwned=0;$matchedRefs=0;$missingOwned=0;$missingRefs=0
$matchedKeys=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($root in $nativeRoots){
    $name=[string]$root.Name
    $key=Normalize-Name $name
    $isRef=$name.StartsWith(',',[StringComparison]::Ordinal)
    [object[]]$matches=@()
    if($sourceModels.ContainsKey($key)){$matches=[object[]]$sourceModels[$key].ToArray()}
    $match=($matches.Count -gt 0)
    if($match){[void]$matchedKeys.Add($key)}
    if($isRef){
        if($match){$matchedRefs++}else{$missingRefs++}
    }else{
        if($match){$matchedOwned++}else{$missingOwned++}
    }
    $nativeRecords.Add([pscustomobject]@{
        NativeRootIndex=[int]$root.Index;NativeName=$name
        NormalizedName=$key;NativeReference=$isRef
        MatchingPcFastfileName=$match
        PcSourceRoles=[string[]]@($matches | ForEach-Object {$_.Role} | Sort-Object -Unique)
        PcSourceNames=[string[]]@($matches | ForEach-Object {$_.Name} | Sort-Object -Unique)
        PayloadProven=$false
    })
}
$nativeOwned=$matchedOwned+$missingOwned
$nativeRefs=$matchedRefs+$missingRefs
$pcNameOnly=@($sourceModels.Keys | Where-Object {-not $matchedKeys.Contains([string]$_)} | Sort-Object)
if($nativeOwned+$nativeRefs -ne $nativeRoots.Count){
    throw 'Aztec native XModel owned/reference identity partitions do not reconcile.'
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
$report=[ordered]@{
    schema='WaW360.AztecPcModelWorldSourceFrontier.v1'
    status='AZTEC_PC_MODEL_WORLD_SOURCE_FRONTIER_PASS'
    nativeRoots=8509;nativeXModelRoots=$nativeRoots.Count
    nativeOwnedXModelRoots=$nativeOwned;nativeReferencedXModelRoots=$nativeRefs
    nativeOwnedModelsWithPcName=$matchedOwned;nativeReferencedModelsWithPcName=$matchedRefs
    nativeOwnedModelsNotNamedInPcFF=$missingOwned;nativeReferencedModelsNotNamedInPcFF=$missingRefs
    uniquePcXModelNames=$sourceModels.Count;pcXModelNamesNotInNative=$pcNameOnly.Count
    pcImageShaReadbacks=2;pcRawFileShaReadbacks=114
    sourceFastfiles=$records.ToArray()
    nativeXModelIdentityBindings=$nativeRecords.ToArray()
    pcOnlyXModelNames=[string[]]$pcNameOnly
    xboxXModelPayloadsPromoted=0;sourceXModelBodiesVerified=0;worldPayloadsPromoted=0
    safeHardwareTestCandidate=$false
    notes=@(
        'This report reuses OAT PC --list metadata. Matching XModel names are identity evidence, not converted geometry or approved source ownership.',
        'References with leading commas are preserved as references; no donor geometry, textures or shaders are imported.',
        'GfxWorld, ClipMap and remaining world types require independently validated source payloads before gameplay.',
        'Next: extract a single original PC XModel in a short physical OAT workspace and validate its required material/partclassification dependencies, before attempting a true Xbox XModel graph.'
    )
}
$json=Join-Path $work 'aztec_pc_model_world_source_frontier.json'
$txt=Join-Path $work 'aztec_pc_model_world_source_frontier.txt'
$report | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $json -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add('AZTEC XMODEL/WORLD PC SOURCE FRONTIER (NO OAT, NO FF)')
$lines.Add('=======================================================')
$lines.Add("status=$($report.status) 8509-native-roots mixed FF: PC images 2/2; RawFiles 114/114")
$lines.Add("Native XModels $($nativeRoots.Count), owned=$nativeOwned, references=$nativeRefs")
$lines.Add("Matching PC names: owned=$matchedOwned / $nativeOwned, references=$matchedRefs / $nativeRefs")
$lines.Add("Not named in PC FF: owned=$missingOwned, references=$missingRefs")
$lines.Add("PC unique model names=$($sourceModels.Count), names absent from native=$($pcNameOnly.Count)")
foreach($rec in $records){
    $lines.Add("ROLE $($rec.Role): xmodels=$($rec.UniqueXModels)")
    foreach($type in @('gfxworld','fxworld','clipmap','comworld','gameworldsp','gameworldmp','mapents','physcollmap','colmap','xmodelsurfs','xmodelpieces')){
        $lines.Add("  TYPE $($type)=$($rec.UniqueTypes[$type])")
    }
}
$lines.Add('=======================================================')
$lines.Add('NATIVE MODEL IDENTITIES')
foreach($item in $nativeRecords.ToArray()){
    $line="ROOT $($item.NativeRootIndex) $($item.NativeName): PC_NAME=$($item.MatchingPcFastfileName) NATIVE_REFERENCE=$($item.NativeReference) ROLES=$(($item.PcSourceRoles) -join ',')"
    $lines.Add($line)
}
$lines.Add('=======================================================')
$lines.Add('PC XMODEL NAMES WITHOUT NATIVE IDENTITY')
foreach($name in $pcNameOnly){$lines.Add([string]$name)}
$lines.Add('All model and world payloads remain unconverted; diagnostic identity census only.')
[IO.File]::WriteAllLines($txt,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-MODEL-WORLD] ================= RESULT ============================='
Write-Host "[AZTEC-MODEL-WORLD] status=$($report.status)"
Write-Host "[AZTEC-MODEL-WORLD] Native XModels=$($nativeRoots.Count): owned=$nativeOwned refs=$nativeRefs"
Write-Host "[AZTEC-MODEL-WORLD] PC name matches: owned=$matchedOwned refs=$matchedRefs; absent-owned=$missingOwned"
Write-Host "[AZTEC-MODEL-WORLD] PC unique XModel names=$($sourceModels.Count), PC-only=$($pcNameOnly.Count)"
Write-Host "[AZTEC-MODEL-WORLD] Report: $txt"
Write-Host '[AZTEC-MODEL-WORLD] No new FF or XModel payload; NOT FOR XBOX.'
