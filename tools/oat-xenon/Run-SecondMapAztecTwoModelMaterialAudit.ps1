[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$n=Join-Path $base 'native_material_mapping'
$work=Join-Path $n 'pc_two_model_material_audit'
$proofPath=Join-Path $n 'pc_two_xmodel_image_rawfile_integration\aztec_two_pc_xmodel_image_rawfile_integration.json'
$identityPath=Join-Path $n 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$baselinePath=Join-Path $base 'second_map_baseline.json'
$pcImagePath=Join-Path $n 'pc_fastfile_image_sources\pc_fastfile_image_sources.json'

foreach($p in @($proofPath,$identityPath,$baselinePath,$pcImagePath)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing source/material audit prerequisite: $p"}
}
$proof=Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json
$identity=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$baseline=Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
$pcImageProof=Get-Content -LiteralPath $pcImagePath -Raw | ConvertFrom-Json
if([string]$proof.schema -ne 'WaW360.AztecPcTwoXModelImageRawFileCombinedGraph.v1' -or
   [string]$proof.status -ne 'AZTEC_PC_TWO_XMODEL_IMAGE_RAWFILE_GRAPH_PASS' -or
   [int]$proof.nativeRoots -ne 8509 -or
   [int]$proof.realXModelRoots -ne 2 -or
   [int]$proof.independentModelNamesListed -ne 2 -or
   [int]$proof.independentPcPixelShaExactReadbacks -ne 2 -or
   [int]$proof.independentPcRawFileShaExactReadbacks -ne 114 -or
   -not [bool]$proof.layoutReplayExact -or
   -not [bool]$proof.oatZeroWarningsZeroErrors -or
   [bool]$proof.readyForXbox -or
   -not [bool]$identity.TypeSequenceVerified -or
   @($identity.Entries).Count -ne 8509 -or
   [string]$baseline.format -ne 'WaW360.SecondMapBaseline.v1' -or
   [string]$baseline.mapName -ine 'nazi_zombie_aztec_V2.2'){
    throw 'The 8,509-root original-PC two-model integration proof is missing or changed.'
}
if(-not (Test-Path -LiteralPath ([string]$proof.modelBindingManifest) -PathType Leaf) -or
   -not (Test-Path -LiteralPath ([string]$proof.modelGraphProof) -PathType Leaf)){
    throw 'The two-XModel binding or original PC XModel graph proof is absent.'
}
$binding=Get-Content -LiteralPath ([string]$proof.modelBindingManifest) -Raw | ConvertFrom-Json
$firstProof=Get-Content -LiteralPath ([string]$proof.modelGraphProof) -Raw | ConvertFrom-Json
if([string]$binding.format -ne 'WaW360.AztecPcTwoXModelBinding.v1' -or
   -not [bool]$binding.bindingPass -or
   @($binding.ownedBindings).Count -ne 2 -or
   @($binding.referenceBindings).Count -ne 0 -or
   [string]$firstProof.status -ne 'AZTEC_PC_RENDERABLE_XMODEL_GRAPH_PASS'){
    throw 'Exact PC two-model binding or first model provenance is invalid.'
}
$graphRoot=Join-Path $n 'pc_two_xmodel_image_rawfile_integration\pc_two_xmodel_graph'
$expected=@('zombie_x2_icon','tag_origin')
$graphRows=New-Object 'System.Collections.Generic.List[object]'
for($i=0;$i -lt 2;$i++){
    $row=@($binding.ownedBindings)[$i]
    if([int]$row.CorpusOrdinal -ne $i -or [string]$row.CorpusName -ine $expected[$i]){
        throw "Two-XModel ordinal $i has unexpected source identity."
    }
    $graphDir=Join-Path $graphRoot ('dumps\{0:D3}\xenon_xmodel_graphs' -f $i)
    $files=@(Get-ChildItem -LiteralPath $graphDir -Filter '*.json' -File -ErrorAction Stop)
    if($files.Count -ne 1){throw "XModel graph ordinal $i has $($files.Count) JSON files, expected one."}
    $sha=(Get-FileHash -LiteralPath $files[0].FullName -Algorithm SHA256).Hash
    $want=if($i -eq 0){[string]$firstProof.graphSha256}else{[string]$proof.secondPcXModelGraphSha256}
    if($sha -ine $want){throw "Original PC XModel ordinal $i graph SHA changed."}
    $g=Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json
    if([string]$g.format -ne 'WaW360.T4Xenon.XModelRelocatableGraph.v1' -or
       [string]$g.name -ine $expected[$i] -or
       [int]$g.abi.xmodel -ne 212 -or
       [int]$g.unsupported.count -ne 0 -or
       @($g.materials).Count -ne [int]$g.counts.numSurfs){
        throw "PC XModel ordinal $i graph material topology/ABI is invalid."
    }
    $graphRows.Add([pscustomobject]@{
        ordinal=$i;name=$expected[$i];root=[int]$row.NativeRootIndex
        sha256=$sha;surfaceCount=[int]$g.counts.numSurfs
        materials=@($g.materials);physPreset=[string]$g.physPreset
    })
}
function Normalize-AssetName([string]$value){
    $s=$value.Trim().Replace('\','/')
    while($s.StartsWith(',',[StringComparison]::Ordinal)){$s=$s.Substring(1)}
    if($s.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){$s=$s.Substring(0,$s.Length-3)}
    return $s.ToLowerInvariant()
}
function Resolve-NativeMaterial([string]$name,[object[]]$roots){
    # Same priority ordering as the shared XModel resolver: exact;
    # comma marker removed; then optional Xbox _wj name.
    $name=$name.Trim().Replace('\','/')
    [object[]]$exact=@($roots | Where-Object {
        ([string]$_.Name).Trim().Replace('\','/') -ieq $name
    })
    if($exact.Count -gt 0){return [pscustomobject]@{Tier='exact';Matches=$exact}}
    $withoutComma=$name.TrimStart(',')
    [object[]]$plain=@($roots | Where-Object {
        ([string]$_.Name).Trim().TrimStart(',').Replace('\','/') -ieq $withoutComma
    })
    if($plain.Count -gt 0){return [pscustomobject]@{Tier='no-reference-marker';Matches=$plain}}
    $key=Normalize-AssetName $name
    [object[]]$equivalent=@($roots | Where-Object {
        (Normalize-AssetName ([string]$_.Name)) -eq $key
    })
    return [pscustomobject]@{Tier='xbox-suffix-fallback';Matches=$equivalent}
}
# Cached PC OAT --list material names are *name evidence only*: they do not
# establish serialized Material, TechniqueSet, shader, or pixel ownership.
$pcMaterials=@{}
$rolesSeen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$sourceProofByRole=@{}
foreach($s in @($pcImageProof.sourceFastfiles)){
    $role=[string]$s.Role
    if($sourceProofByRole.ContainsKey($role)){throw "Duplicate PC fastfile inventory role $role"}
    $sourceProofByRole[$role]=$s
}
$ffOrdinal=0
foreach($ff in @($baseline.fastfiles)){
    $ffOrdinal++
    $role=[string]$ff.Role
    if($role -notin @('map.ff','map_patch.ff','mod.ff')){continue}
    if(-not $sourceProofByRole.ContainsKey($role)){throw "No source SHA census for $role"}
    $p=[string]$ff.Path
    $pinned=$sourceProofByRole[$role]
    if([string]$pinned.SourceFastFile -ine $p -or
       -not (Test-Path -LiteralPath $p -PathType Leaf) -or
       [long](Get-Item -LiteralPath $p).Length -ne [long]$ff.Bytes -or
       (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -ine [string]$pinned.Sha256){
        throw "Source PC fastfile $role has changed since its SHA inventory."
    }
    $stem=([IO.Path]::GetFileNameWithoutExtension($p)) -replace '[^A-Za-z0-9_.-]','_'
    $log=Join-Path $base ('oat_asset_lists\{0:D2}_{1}.list.log' -f $ffOrdinal,$stem)
    if(-not (Test-Path -LiteralPath $log -PathType Leaf)){throw "Missing cached PC OAT list: $log"}
    foreach($line in [IO.File]::ReadLines($log)){
        $text=$line.Trim()
        if($text.StartsWith('[',[StringComparison]::Ordinal)){
            $end=$text.IndexOf(']')
            if($end -ge 0 -and $end+1 -lt $text.Length){$text=$text.Substring($end+1).TrimStart()}
        }
        if($text -notmatch '^(?i:material)\s*,\s*(.*?)\s*$'){continue}
        $material=[string]$Matches[1]
        if([string]::IsNullOrWhiteSpace($material)){continue}
        $key=Normalize-AssetName $material
        if(-not $pcMaterials.ContainsKey($key)){
            $pcMaterials[$key]=New-Object 'System.Collections.Generic.List[object]'
        }
        $pcMaterials[$key].Add([pscustomobject]@{role=$role;name=$material})
    }
    [void]$rolesSeen.Add($role)
}
if($rolesSeen.Count -ne 3){throw 'Material PC source inventory must cover all three PC FFs.'}
$nativeMaterials=@($identity.Entries | Where-Object {[string]$_.TypeName -ieq 'material'})
$dependencies=New-Object 'System.Collections.Generic.List[object]'
foreach($model in $graphRows.ToArray()){
    $slot=0
    foreach($mat in @($model.materials)){
        $name=[string]$mat
        if([string]::IsNullOrWhiteSpace($name)){$slot++;continue}
        $resolved=Resolve-NativeMaterial $name $nativeMaterials
        $key=Normalize-AssetName $name
        [object[]]$pcNameHits=@()
        if($pcMaterials.ContainsKey($key)){$pcNameHits=[object[]]$pcMaterials[$key].ToArray()}
        $dependencies.Add([pscustomobject]@{
            model=[string]$model.name
            surface=$slot
            materialName=$name
            nativeResolutionTier=[string]$resolved.Tier
            nativeMatchCount=@($resolved.Matches).Count
            nativeMatches=@($resolved.Matches | ForEach-Object {
                [pscustomobject]@{index=[int]$_.Index;name=[string]$_.Name}
            })
            pcSourceNameEvidence=$pcNameHits
            pcSourceNameMatches=$pcNameHits.Count
            pcMaterialBodyConverted=$false
        })
        $slot++
    }
}
$rows=@($dependencies.ToArray())
$missing=@($rows | Where-Object {[int]$_.nativeMatchCount -eq 0}).Count
$ambiguous=@($rows | Where-Object {[int]$_.nativeMatchCount -gt 1}).Count
$pcNamed=@($rows | Where-Object {[int]$_.pcSourceNameMatches -gt 0}).Count
New-Item -Path $work -ItemType Directory -Force | Out-Null
$result=[ordered]@{
    schema='WaW360.AztecPcTwoModelMaterialDependencyAudit.v1'
    status='AZTEC_PC_TWO_MODEL_MATERIAL_DEPENDENCY_AUDIT_PASS'
    validationOnly=$true;readyForXbox=$false;fullMapConverted=$false
    nativeRoots=8509;sourcePcXModels=2
    nativeMaterialRootCount=$nativeMaterials.Count
    pcFastfileRolesAudited=3
    xmodels=@($graphRows.ToArray())
    nonNullMaterialHandles=$rows.Count
    nativeMissingMaterialRefs=$missing
    nativeAmbiguousMaterialRefs=$ambiguous
    pcMaterialNameEvidenceRefs=$pcNamed
    materialBodiesPromoted=0
    dependencies=$rows
    note='Identity/dependency audit only. PC OAT material listings prove names, not material bodies or shaders. Original 2-XModel FF is unchanged. NO XBOX.'
}
$json=Join-Path $work 'aztec_two_model_material_dependency_audit.json'
$txt=Join-Path $work 'aztec_two_model_material_dependency_audit.txt'
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $json -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add("AZTEC PC TWO-XMODEL MATERIAL DEPENDENCY AUDIT: $($result.status)")
$lines.Add("Models=$($graphRows.Count) nonnullMaterialHandles=$($rows.Count) PC-name-evidence=$pcNamed native-missing=$missing ambiguous=$ambiguous")
foreach($d in $rows){
    $roles=@($d.pcSourceNameEvidence | ForEach-Object {[string]$_.role} | Sort-Object -Unique)
    $nativeNames=@($d.nativeMatches | ForEach-Object {[string]$_.name})
    $lines.Add("model=$($d.model) surface=$($d.surface) material=$($d.materialName) native=$($d.nativeMatchCount):$($nativeNames -join ',') tier=$($d.nativeResolutionTier) pcRoles=$($roles -join ',')")
}
$lines.Add('PC material-name matches are NOT verified material payloads. All original FFs untouched. NOT FOR XBOX.')
[IO.File]::WriteAllLines($txt,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-MATERIAL-AUDIT] ============================================='
Write-Host "[AZTEC-MATERIAL-AUDIT] status=$($result.status)"
Write-Host "[AZTEC-MATERIAL-AUDIT] XModels=2 material handles=$($rows.Count) native missing=$missing ambiguous=$ambiguous PC source-name evidence=$pcNamed"
Write-Host "[AZTEC-MATERIAL-AUDIT] report=$txt"
Write-Host '[AZTEC-MATERIAL-AUDIT] NO OAT / NO FF EMISSION / NOT FOR XBOX'
