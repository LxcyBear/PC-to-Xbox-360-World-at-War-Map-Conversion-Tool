[CmdletBinding()]
param(
    [string]$ImageClosureManifest = '',
    [string]$MaterialBindingManifest = '',
    [string]$ProductionImageGraphManifest = '',
    [string]$MapAssetsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\map assets',
    [string]$UgxRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\UGX',
    [string]$ZctRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\ZCT',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($ImageClosureManifest)) { $ImageClosureManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_5a_image_source_closure\nuclear_5a_image_source_closure.json' }
if ([string]::IsNullOrWhiteSpace($MaterialBindingManifest)) { $MaterialBindingManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4zd_material_root_binding\nuclear_4zd_material_root_binding.json' }
if ([string]::IsNullOrWhiteSpace($ProductionImageGraphManifest)) { $ProductionImageGraphManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4t_production_image_graph\nuclear_4t_production_image_graph.json' }
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_5b_material_slot_image_closure' }
foreach ($required in @($ImageClosureManifest,$MaterialBindingManifest,$ProductionImageGraphManifest)) { if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required manifest not found: $required" } }

function Normalize-AssetName {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    $v=$Value.Trim().Trim('"').Replace('\','/').TrimStart('/')
    while ($v.StartsWith(',', [StringComparison]::Ordinal)) { $v=$v.Substring(1) }
    if ($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)) { $v=$v.Substring(0,$v.Length-3) }
    return $v.ToLowerInvariant()
}

function Get-OatMaterialDumpStem {
    param([string]$AssetName)
    $value=$AssetName
    if ($value.StartsWith('*',[StringComparison]::Ordinal)) {
        $value=$value.Replace('*','_')
        $paren=$value.IndexOf('(')
        if ($paren -ge 0) { $value=$value.Substring(0,$paren) }
        $value='generated/' + $value
    }
    return $value.Replace('\','/')
}

function Get-MaterialsRoot {
    param([string]$DumpRoot)
    $root=Get-ChildItem -LiteralPath $DumpRoot -Directory -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq 'materials' } | Sort-Object { $_.FullName.Length } | Select-Object -First 1
    if ($null -eq $root) { throw "Could not find materials/ under: $DumpRoot" }
    return $root.FullName
}

function Build-MaterialIndex {
    param([string]$MaterialsRoot)
    $index=@{}
    foreach ($file in @(Get-ChildItem -LiteralPath $MaterialsRoot -Filter '*.json' -File -Recurse -ErrorAction SilentlyContinue)) {
        $base=[IO.Path]::GetFullPath($MaterialsRoot)
        if (-not $base.EndsWith('\')) { $base += '\' }
        $full=[IO.Path]::GetFullPath($file.FullName)
        if (-not $full.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)) { continue }
        $rel=$full.Substring($base.Length).Replace('\','/')
        if (-not $rel.EndsWith('.json',[StringComparison]::OrdinalIgnoreCase)) { continue }
        $key=$rel.Substring(0,$rel.Length-5).ToLowerInvariant()
        if ($index.ContainsKey($key)) { throw "Duplicate material dump stem: $key" }
        $index[$key]=$file.FullName
    }
    return $index
}

function Get-MaterialFile {
    param([hashtable]$Index,[string]$AssetName)
    $key=(Get-OatMaterialDumpStem -AssetName $AssetName).ToLowerInvariant()
    if ($Index.ContainsKey($key)) { return [string]$Index[$key] }
    return $null
}

function Find-ExactImageSources {
    param([string]$ImageName,[object[]]$Roots)
    $target=Normalize-AssetName -Value $ImageName
    $rows=New-Object 'System.Collections.Generic.List[object]'
    foreach ($root in $Roots) {
        $area=[string]$root.Area; $path=[string]$root.Root
        if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path -PathType Container)) { continue }
        foreach ($ext in @('*.iwi','*.dds')) {
            foreach ($file in @(Get-ChildItem -LiteralPath $path -Filter $ext -File -Recurse -ErrorAction SilentlyContinue)) {
                $name=[IO.Path]::GetFileNameWithoutExtension($file.Name)
                if ((Normalize-AssetName -Value $name) -eq $target) {
                    $rows.Add([pscustomobject]@{ SourceArea=$area; SourceKind='loose'; SourcePath=$file.FullName; ArchiveEntry=''; Bytes=[int64]$file.Length })
                }
            }
        }
        foreach ($iwd in @(Get-ChildItem -LiteralPath $path -Filter '*.iwd' -File -Recurse -ErrorAction SilentlyContinue)) {
            $zip=$null
            try {
                $zip=[IO.Compression.ZipFile]::OpenRead($iwd.FullName)
                foreach ($entry in $zip.Entries) {
                    $ext=[IO.Path]::GetExtension($entry.FullName)
                    if (-not ($ext.Equals('.iwi',[StringComparison]::OrdinalIgnoreCase) -or $ext.Equals('.dds',[StringComparison]::OrdinalIgnoreCase))) { continue }
                    $name=[IO.Path]::GetFileNameWithoutExtension($entry.FullName)
                    if ((Normalize-AssetName -Value $name) -eq $target) {
                        $rows.Add([pscustomobject]@{ SourceArea=$area; SourceKind='iwd'; SourcePath=$iwd.FullName; ArchiveEntry=$entry.FullName; Bytes=[int64]$entry.Length })
                    }
                }
            } finally { if ($null -ne $zip) { $zip.Dispose() } }
        }
    }
    return [object[]]$rows.ToArray()
}

$closure=Get-Content -LiteralPath $ImageClosureManifest -Raw | ConvertFrom-Json
$binding=Get-Content -LiteralPath $MaterialBindingManifest -Raw | ConvertFrom-Json
$production=Get-Content -LiteralPath $ProductionImageGraphManifest -Raw | ConvertFrom-Json
if ([string]$closure.format -ne 'WaW360.Nuclear5A.ImageSourceClosure.v1' -or [int]$closure.targetImages -ne 43) { throw 'Stage 5B requires the 43-image Stage 5A closure manifest.' }
if (-not [bool]$binding.BindingPass -or [int]$binding.CorpusMaterials -ne 502) { throw 'Stage 5B requires the passing 502-material root binding.' }
if ([string]$production.format -ne 'WaW360.Nuclear4T.ProductionImageGraph.v1' -or -not [bool]$production.mergePass -or [int]$production.imageGraphs -ne 878) { throw 'Stage 5B requires the passing 878-image production graph.' }

$bridgePath=[string]$binding.BridgeManifest
if (-not (Test-Path -LiteralPath $bridgePath -PathType Leaf)) { throw "Material bridge not found: $bridgePath" }
$bridge=Get-Content -LiteralPath $bridgePath -Raw | ConvertFrom-Json
if (-not [bool]$bridge.Verified) { throw 'Material bridge is not verified.' }
$pcDump=[string]$bridge.PcDumpDirectory; $xboxDump=[string]$bridge.XboxDumpDirectory
$pcMaterialsRoot=Get-MaterialsRoot -DumpRoot $pcDump
$xboxMaterialsRoot=Get-MaterialsRoot -DumpRoot $xboxDump
$pcIndex=Build-MaterialIndex -MaterialsRoot $pcMaterialsRoot
$xboxIndex=Build-MaterialIndex -MaterialsRoot $xboxMaterialsRoot

$productionByName=@{}
foreach ($img in @($production.images)) {
    $key=Normalize-AssetName -Value ([string]$img.Name)
    if (-not $productionByName.ContainsKey($key)) { $productionByName[$key]=New-Object 'System.Collections.Generic.List[object]' }
    $productionByName[$key].Add($img)
}

$targetsByName=@{}
foreach ($target in @($closure.targets)) { $targetsByName[(Normalize-AssetName -Value ([string]$target.Name))]=$target }
$occurrences=@{}
foreach ($key in $targetsByName.Keys) { $occurrences[$key]=New-Object 'System.Collections.Generic.List[object]' }

$bindingsProcessed=0; $slotCountMismatches=0; $semanticMismatches=0
foreach ($row in @($binding.OwnedBindings)) {
    $nativeName=[string]$row.NativeName; $pcName=[string]$row.CorpusName
    $nativeFile=Get-MaterialFile -Index $xboxIndex -AssetName $nativeName
    $pcFile=Get-MaterialFile -Index $pcIndex -AssetName $pcName
    if ($null -eq $nativeFile -or $null -eq $pcFile) { continue }
    $nativeDoc=Get-Content -LiteralPath $nativeFile -Raw | ConvertFrom-Json
    $pcDoc=Get-Content -LiteralPath $pcFile -Raw | ConvertFrom-Json
    $nativeTextures=@($nativeDoc.textures); $pcTextures=@($pcDoc.textures)
    $bindingsProcessed++
    if ($nativeTextures.Count -ne $pcTextures.Count) { $slotCountMismatches++; continue }
    for ($i=0; $i -lt $nativeTextures.Count; $i++) {
        $nx=$nativeTextures[$i]; $px=$pcTextures[$i]
        if ($null -eq $nx -or $null -eq $px) { continue }
        $nativeImage=[string]$nx.image
        if ([string]::IsNullOrWhiteSpace($nativeImage)) { continue }
        $targetKey=Normalize-AssetName -Value $nativeImage
        if (-not $targetsByName.ContainsKey($targetKey)) { continue }
        $nativeSemantic=$(if ($nx.PSObject.Properties.Name -contains 'semantic' -and $null -ne $nx.semantic) { ([string]$nx.semantic).Trim().ToLowerInvariant() } else { '' })
        $pcSemantic=$(if ($px.PSObject.Properties.Name -contains 'semantic' -and $null -ne $px.semantic) { ([string]$px.semantic).Trim().ToLowerInvariant() } else { '' })
        if (-not [string]::IsNullOrWhiteSpace($nativeSemantic) -and
            -not [string]::IsNullOrWhiteSpace($pcSemantic) -and
            -not $nativeSemantic.Equals($pcSemantic,[StringComparison]::OrdinalIgnoreCase)) {
            $semanticMismatches++
            continue
        }
        $pcImage=[string]$px.image
        if ([string]::IsNullOrWhiteSpace($pcImage)) { continue }
        $occurrences[$targetKey].Add([pscustomobject]@{ NativeMaterial=$nativeName; PcMaterial=$pcName; Slot=$i; Semantic=$nativeSemantic; NativeImage=$nativeImage; PcImage=$pcImage; MatchKind=[string]$row.MatchKind })
    }
}

$sourceRoots=@(
    [pscustomobject]@{ Area='map-assets'; Root=$MapAssetsRoot },
    [pscustomobject]@{ Area='ugx'; Root=$UgxRoot },
    [pscustomobject]@{ Area='zct'; Root=$ZctRoot },
    [pscustomobject]@{ Area='map-assets-ff-dumps'; Root=(Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_map_asset_corpus\ff_dumps') },
    [pscustomobject]@{ Area='mod-dependency-ff-dumps'; Root=(Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_mod_dependency_corpus\ff_dumps') }
)
$results=New-Object 'System.Collections.Generic.List[object]'
$productionAlias=0; $sourceResolved=0; $ambiguous=0; $unreferenced=0
foreach ($targetKey in @($targetsByName.Keys | Sort-Object)) {
    $target=$targetsByName[$targetKey]
    $hits=[object[]]$occurrences[$targetKey].ToArray()
    $pcNames=@($hits | ForEach-Object { Normalize-AssetName -Value ([string]$_.PcImage) } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
    $status='unreferenced-by-bound-materials'; $mappedPc=''; $prod=@(); $sources=@()
    if ($pcNames.Count -eq 1) {
        $mappedPc=$pcNames[0]
        if ($productionByName.ContainsKey($mappedPc)) {
            $prod=[object[]]$productionByName[$mappedPc].ToArray(); $status='existing-production-image-alias'; $productionAlias++
        } else {
            $sources=@(Find-ExactImageSources -ImageName $mappedPc -Roots $sourceRoots)
            if (@($sources).Count -gt 0) { $status='pc-image-source-resolved'; $sourceResolved++ } else { $status='pc-image-mapped-source-missing' }
        }
    } elseif ($pcNames.Count -gt 1) { $status='ambiguous-material-slot-mapping'; $ambiguous++ } else { $unreferenced++ }
    $results.Add([pscustomobject]@{ Index=[int]$target.Index; Name=[string]$target.Name; Status=$status; MaterialOccurrences=@($hits).Count; CandidatePcImages=[object[]]@($pcNames); MappedPcImage=$mappedPc; Occurrences=[object[]]@($hits); ProductionMatches=[object[]]@($prod); SourceMatches=[object[]]@($sources) })
}

$sourceMissing=@($results.ToArray() | Where-Object Status -eq 'pc-image-mapped-source-missing').Count
$resolvedTotal=$productionAlias + $sourceResolved
if (Test-Path -LiteralPath $OutputRoot) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$manifest=[ordered]@{
    format='WaW360.Nuclear5B.MaterialSlotImageClosure.v1'; imageClosureManifest=(Resolve-Path -LiteralPath $ImageClosureManifest).Path; materialBindingManifest=(Resolve-Path -LiteralPath $MaterialBindingManifest).Path; productionImageGraphManifest=(Resolve-Path -LiteralPath $ProductionImageGraphManifest).Path;
    targetImages=43; materialBindingsProcessed=$bindingsProcessed; slotCountMismatches=$slotCountMismatches; semanticMismatches=$semanticMismatches;
    productionImageAliases=$productionAlias; pcImageSourceResolved=$sourceResolved; pcImageMappedSourceMissing=$sourceMissing; ambiguousMappings=$ambiguous; unreferencedByBoundMaterials=$unreferenced; resolvedByMaterialSlots=$resolvedTotal;
    images=[object[]]$results.ToArray();
    policy=@('Native Xbox material JSON is used only as validation/binding evidence; no native image or material payload bytes are copied.','A target image is slot-resolved only when all bound material occurrences agree on one PC image identity at the same texture slot.','Existing production-image aliases reuse already converted PC/Xbox-safe image payloads; source-resolved mappings still require conversion before credit.','Ambiguous or unreferenced targets remain blocked.')
}
$jsonPath=Join-Path $OutputRoot 'nuclear_5b_material_slot_image_closure.json'
$txtPath=Join-Path $OutputRoot 'nuclear_5b_material_slot_image_closure.txt'
$manifest | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
$report=New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 5B - MATERIAL-SLOT IMAGE CLOSURE')
$report.Add(('=' * 104))
$report.Add(("Targets:                         {0:N0}" -f 43))
$report.Add(("Material bindings processed:     {0:N0}" -f $bindingsProcessed))
$report.Add(("Slot-count mismatches:            {0:N0}" -f $slotCountMismatches))
$report.Add(("Semantic mismatches:              {0:N0}" -f $semanticMismatches))
$report.Add(("Existing production aliases:      {0:N0}" -f $productionAlias))
$report.Add(("PC image source resolved:          {0:N0}" -f $sourceResolved))
$report.Add(("Mapped but source still missing:   {0:N0}" -f $sourceMissing))
$report.Add(("Ambiguous slot mappings:           {0:N0}" -f $ambiguous))
$report.Add(("Unreferenced by bound materials:   {0:N0}" -f $unreferenced))
$report.Add(("Resolved by material slots:        {0:N0}" -f $resolvedTotal))
$report.Add('')
foreach ($row in $results.ToArray() | Sort-Object Index) {
    $report.Add(("  root #{0,4}: {1,-58} {2}" -f $row.Index,$row.Name,$row.Status))
    if (-not [string]::IsNullOrWhiteSpace([string]$row.MappedPcImage)) { $report.Add(("           PC image <= {0}" -f $row.MappedPcImage)) }
    foreach ($hit in @($row.Occurrences | Select-Object -First 8)) { $report.Add(("           material {0} <= {1} slot={2} semantic={3} pcImage={4}" -f $hit.NativeMaterial,$hit.PcMaterial,$hit.Slot,$hit.Semantic,$hit.PcImage)) }
}
[IO.File]::WriteAllLines($txtPath,[string[]]$report.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[NUCLEAR-5B-IMG] ============================================================'
Write-Host '[NUCLEAR-5B-IMG] MATERIAL-SLOT IMAGE CLOSURE'
Write-Host '[NUCLEAR-5B-IMG] ============================================================'
Write-Host "[NUCLEAR-5B-IMG] Targets                       : 43"
Write-Host "[NUCLEAR-5B-IMG] Material bindings processed   : $bindingsProcessed"
Write-Host "[NUCLEAR-5B-IMG] Slot-count mismatches          : $slotCountMismatches"
Write-Host "[NUCLEAR-5B-IMG] Semantic mismatches            : $semanticMismatches"
Write-Host "[NUCLEAR-5B-IMG] Existing production aliases    : $productionAlias"
Write-Host "[NUCLEAR-5B-IMG] PC image source resolved        : $sourceResolved"
Write-Host "[NUCLEAR-5B-IMG] Mapped source still missing     : $sourceMissing"
Write-Host "[NUCLEAR-5B-IMG] Ambiguous slot mappings         : $ambiguous"
Write-Host "[NUCLEAR-5B-IMG] Unreferenced by bound materials : $unreferenced"
Write-Host "[NUCLEAR-5B-IMG] Resolved by material slots      : $resolvedTotal"
Write-Host "[NUCLEAR-5B-IMG] Manifest                       : $jsonPath"
Write-Host "[NUCLEAR-5B-IMG] Report                         : $txtPath"
Write-Host '[NUCLEAR-5B-IMG] ================================================================='
Write-Host '[NUCLEAR-5B-IMG] PASS: material-slot provenance analysis complete; no oracle payload bytes copied.' -ForegroundColor Green
