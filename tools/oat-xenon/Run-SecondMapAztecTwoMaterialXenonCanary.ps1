[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$work=Join-Path $base 'pc_two_material_xenon_canary'
$bodyPath=Join-Path $base 'pc_two_model_material_source_bodies\aztec_two_model_pc_material_bodies.json'
$auditPath=Join-Path $base 'pc_two_model_material_audit\aztec_two_model_material_dependency_audit.json'
$pcLinker=Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Linker.exe'
$xboxUnlinker=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
foreach($p in @($bodyPath,$auditPath,$pcLinker,$xboxUnlinker)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing original-PC material/Xenon input: $p"}
}
$bodies=Get-Content -LiteralPath $bodyPath -Raw | ConvertFrom-Json
$audit=Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json
if([string]$bodies.schema -ne 'WaW360.AztecPcTwoModelMaterialBodies.v1' -or
   [string]$bodies.status -ne 'AZTEC_PC_TWO_MODEL_MATERIAL_BODIES_PASS' -or
   [int]$bodies.materialsRequested -ne 2 -or
   [int]$bodies.pcMaterialBodiesExtracted -ne 2 -or
   [int]$bodies.referenceOnlyMaterialIdentityCount -ne 0 -or
   [int]$bodies.materialBodiesGraftedToXenon -ne 0 -or
   [bool]$bodies.readyForXbox -or
   [string]$audit.status -ne 'AZTEC_PC_TWO_MODEL_MATERIAL_DEPENDENCY_AUDIT_PASS' -or
   [int]$audit.nativeMissingMaterialRefs -ne 0 -or
   [int]$audit.nativeAmbiguousMaterialRefs -ne 0){
    throw 'The exact two source-owned PC Material bodies and native identity audit have not passed.'
}

function Normalize-Name([string]$name){
    $v=$name.Trim().Trim('"').Replace('\','/').TrimStart('/')
    while($v.StartsWith(',',[StringComparison]::Ordinal)){$v=$v.Substring(1)}
    if($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){$v=$v.Substring(0,$v.Length-3)}
    return $v.ToLowerInvariant()
}
function Material-Stem([string]$name){
    $v=$name.Trim()
    if([string]::IsNullOrWhiteSpace($v) -or [IO.Path]::IsPathRooted($v) -or
       $v.Contains('..') -or $v.Contains(':') -or $v.StartsWith(',')){
        throw "Unsafe or reference-only owned Material name: $name"
    }
    if($v.StartsWith('*',[StringComparison]::Ordinal)){
        $v=$v.Replace('*','_')
        $paren=$v.IndexOf('(')
        if($paren -ge 0){$v=$v.Substring(0,$paren)}
        $v='generated/'+$v
    }
    return $v.Replace('\','/')
}
function Invoke-Oat([string]$exe,[object[]]$arguments,[string]$log,[bool]$requireZeroSummary=$true){
    $prev=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $exe @arguments 2>&1 | ForEach-Object {[string]$_} | Set-Content -LiteralPath $log -Encoding UTF8
        $exitCode=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$prev}
    if($exitCode -ne 0 -or
       @(Select-String -LiteralPath $log -Pattern '(?i)^\s*(ERROR:|WARNING:)').Count -ne 0 -or
       ($requireZeroSummary -and
        @(Select-String -LiteralPath $log -Pattern '(?i)^\s*Finished with\s+0\s+warnings?,\s+0\s+errors?\s*$').Count -eq 0)){
        throw "OAT link/readback gate failed (exit=$exitCode, requireZeroSummary=$requireZeroSummary): $log"
    }
}
function SortedRefs([object[]]$values){
    return @($values | ForEach-Object {Normalize-Name ([string]$_)} | Sort-Object)
}

New-Item -Path $work -ItemType Directory -Force | Out-Null
$logRoot=Join-Path $work 'logs'
New-Item -Path $logRoot -ItemType Directory -Force | Out-Null
$reportPath=Join-Path $work 'aztec_pc_two_material_xenon_canary.json'
$textPath=Join-Path $work 'aztec_pc_two_material_xenon_canary.txt'
$sourceRows=New-Object 'System.Collections.Generic.List[object]'
$graphRows=New-Object 'System.Collections.Generic.List[object]'
$status='AZTEC_PC_TWO_MATERIAL_XENON_CANARY_BLOCKED'
$blocker=''
$standaloneFF=''
$graphRoot=''
$short=''
Write-Host '[AZTEC-PC-MATERIAL-XENON] ===================================================='
Write-Host '[AZTEC-PC-MATERIAL-XENON] 2 ORIGINAL PC MATERIAL JSON -> XENON LINKER/GRAPH'
Write-Host '[AZTEC-PC-MATERIAL-XENON] Standalone canary only. NO full map FF. NOT FOR XBOX.'
try{
    $short=Join-Path ([IO.Path]::GetTempPath()) ('wawAz2Mat_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    if($short.Length -gt 110){throw 'OAT material canary needs a short physical TEMP path.'}
    $pcDump=Join-Path $short 'pc'
    $zoneSource=Join-Path $short 'zone'
    $workspace=Join-Path $short 'work'
    $linked=Join-Path $short 'linked'
    $unlinked=Join-Path $short 'unlinked'
    New-Item -Path $pcDump,$zoneSource,$workspace,$linked,$unlinked -ItemType Directory -Force | Out-Null

    $names=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $techNames=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $imageNames=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $expectedMaterials=@(@($audit.dependencies) | ForEach-Object {Normalize-Name ([string]$_.materialName)} | Sort-Object -Unique)
    foreach($row in @($bodies.originalPcMaterialBodies)){
        $name=[string]$row.material
        $normalized=Normalize-Name $name
        if(-not $names.Add($normalized) -or $expectedMaterials -notcontains $normalized){
            throw "Unexpected/duplicate original-PC Material source identity: $name"
        }
        $src=[string]$row.materialJson
        if(-not (Test-Path -LiteralPath $src -PathType Leaf) -or
           (Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash -ine [string]$row.materialJsonSha256 -or
           -not (Test-Path -LiteralPath ([string]$row.sourceFastFile) -PathType Leaf) -or
           (Get-FileHash -LiteralPath ([string]$row.sourceFastFile) -Algorithm SHA256).Hash -ine
           [string]$row.sourceFastFileSha256){
            throw "PC Material JSON or original source FF SHA mismatch: $name"
        }
        $doc=Get-Content -LiteralPath $src -Raw | ConvertFrom-Json
        $state=@()
        if($doc.PSObject.Properties.Name -contains 'stateBitsEntry' -and $null -ne $doc.stateBitsEntry){
            $state=@($doc.stateBitsEntry)
        }
        if($state.Count -ne 59){throw "${name}: invalid T4 stateBitsEntry width $($state.Count)"}
        $textures=@()
        if($doc.PSObject.Properties.Name -contains 'textures' -and $null -ne $doc.textures){
            $textures=@($doc.textures)
        }
        if($textures.Count -ne [int]$row.textures){
            throw "${name}: texture count changed after source extraction."
        }
        $imageRefs=New-Object 'System.Collections.Generic.List[string]'
        foreach($tex in $textures){
            if($null -eq $tex){continue}
            if($tex.PSObject.Properties.Name -contains 'water' -and $null -ne $tex.water){
                throw "${name}: water texture branch is not supported by the simple material canary."
            }
            if($tex.PSObject.Properties.Name -contains 'image' -and
               -not [string]::IsNullOrWhiteSpace([string]$tex.image)){
                $image=[string]$tex.image
                $imageRefs.Add($image)
                [void]$imageNames.Add($image)
            }
        }
        $technique=''
        if($doc.PSObject.Properties.Name -contains 'techniqueSet' -and $null -ne $doc.techniqueSet){
            $technique=[string]$doc.techniqueSet
            if(-not [string]::IsNullOrWhiteSpace($technique)){[void]$techNames.Add($technique)}
        }
        if($technique -ine [string]$row.techniqueSet -or
           (@(SortedRefs $imageRefs.ToArray()) -join '|') -ine (@(SortedRefs @($row.imageReferences)) -join '|')){
            throw "${name}: original PC TechniqueSet or image references changed since material body proof."
        }
        $target=Join-Path (Join-Path $pcDump 'materials') ((Material-Stem $name).Replace('/','\')+'.json')
        New-Item -Path (Split-Path -Parent $target) -ItemType Directory -Force | Out-Null
        Copy-Item -LiteralPath $src -Destination $target -Force
        if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ine [string]$row.materialJsonSha256){
            throw "${name}: copied original-PC material JSON does not match SHA."
        }
        $sourceRows.Add([pscustomobject]@{
            name=$name;nameNormalized=$normalized;sourceFastFile=[string]$row.sourceFastFile
            sourceJsonSha256=[string]$row.materialJsonSha256
            textures=$textures.Count;images=@($imageRefs.ToArray())
            techniqueSet=$technique
        })
    }
    if($names.Count -ne 2 -or $expectedMaterials.Count -ne 2){
        throw "Expected exactly two distinct source-owned Material identities; found $($names.Count)."
    }

    $project='aztec_pc_two_materials'
    $zoneLines=New-Object 'System.Collections.Generic.List[string]'
    $zoneLines.Add('>game,T4')
    $zoneLines.Add('>platform,xbox360')
    $zoneLines.Add('>name,'+$project)
    $zoneLines.Add('')
    foreach($tech in @($techNames | Sort-Object)){$zoneLines.Add('techniqueset,,"'+([string]$tech).Replace('"','')+'"')}
    foreach($img in @($imageNames | Sort-Object)){$zoneLines.Add('image,,"'+([string]$img).Replace('"','')+'"')}
    foreach($row in @($sourceRows.ToArray() | Sort-Object name)){$zoneLines.Add('material,"'+([string]$row.name).Replace('"','')+'"')}
    $zoneFile=Join-Path $zoneSource ($project+'.zone')
    [IO.File]::WriteAllLines($zoneFile,[string[]]$zoneLines.ToArray(),[Text.UTF8Encoding]::new($false))
    Write-Host "[AZTEC-PC-MATERIAL-XENON] original PC Material JSON SHA PASS: 2/2; TechniqueSets=$($techNames.Count); distinct image references=$($imageNames.Count)"
    $linkLog=Join-Path $logRoot 'two_material_xenon_link.log'
    $linkArguments=@('--no-color','--base-folder',$workspace,'--source-search-path',$zoneSource,
            '--asset-search-path',$pcDump,'--output-folder',$linked,$project)
    Invoke-Oat $pcLinker $linkArguments $linkLog $false
    $ffs=@(Get-ChildItem -LiteralPath $linked -Filter '*.ff' -File -Recurse)
    if($ffs.Count -ne 1){throw "Xenon Linker created $($ffs.Count) standalone material FFs; expected 1."}
    $ff=[string]$ffs[0].FullName
    $listLog=Join-Path $logRoot 'two_material_xenon_list.log'
    Invoke-Oat $xboxUnlinker @('--list','--no-color',$ff) $listLog
    $list=@(Get-Content -LiteralPath $listLog)
    foreach($row in @($sourceRows.ToArray())){
        $name=[string]$row.name
        $matches=@($list | Where-Object {
            [string]$_ -match ('(?i)(?:^|[\]\s])material\s*,\s*'+[regex]::Escape($name)+'\s*$')
        })
        if($matches.Count -ne 1){
            throw "Standalone Xenon FF does not list exactly one owned Material $name (got $($matches.Count))."
        }
    }
    Write-Host '[AZTEC-PC-MATERIAL-XENON] Independent Xenon OAT list: both PC materials PASS'

    $dumpLog=Join-Path $logRoot 'two_material_xenon_graph.log'
    Invoke-Oat $xboxUnlinker @('--no-color','--include-assets','material','--output-folder',$unlinked,$ff) $dumpLog
    $materialDir=Join-Path $unlinked 'xenon_material_graphs'
    if(-not (Test-Path -LiteralPath $materialDir -PathType Container)){
        throw 'Specialized Xenon Unlinker emitted no relocatable Material graphs.'
    }
    $graphs=@(Get-ChildItem -LiteralPath $materialDir -Filter '*.json' -File -Recurse)
    if($graphs.Count -ne 2){throw "Expected 2 Xenon relocatable Material graphs; got $($graphs.Count)."}
    $graphNames=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach($file in $graphs){
        $g=Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $key=Normalize-Name ([string]$g.name)
        if(-not $names.Contains($key) -or -not $graphNames.Add($key) -or
           [string]$g.format -ne 'WaW360.T4Xenon.MaterialRelocatableGraph.v1' -or
           [int]$g.abi.material -ne 96 -or
           [int]$g.reusable.aliasCount -ne 0 -or
           [int]$g.inventory.waterTextures -ne 0 -or
           [int]$g.inventory.waterSamples -ne 0 -or
           [int]$g.inventory.invalidWaterDimensions -ne 0 -or
           [int]$g.counts.textures -ne @($g.textures).Count){
            throw "Xenon material graph failed format, ABI, alias, water or identity gate: $($file.FullName)"
        }
        $source=@($sourceRows.ToArray() | Where-Object {$_.nameNormalized -eq $key})
        if($source.Count -ne 1){throw "Xenon material graph $key has no unique original-PC owner."}
        $graphImages=@($g.textures | Where-Object {-not [string]::IsNullOrWhiteSpace([string]$_.image)} | ForEach-Object {[string]$_.image})
        if((Normalize-Name ([string]$g.techniqueSet)) -ne (Normalize-Name ([string]$source[0].techniqueSet)) -or
           (@(SortedRefs $graphImages) -join '|') -ine (@(SortedRefs @($source[0].images)) -join '|')){
            throw "Xenon graph material $key lost/changed original PC TechniqueSet or image reference identities."
        }
        $graphRows.Add([pscustomobject]@{
            name=[string]$g.name
            jsonSha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            textureCount=[int]$g.counts.textures
            constants=[int]$g.counts.constants
            stateBits=[int]$g.counts.stateBits
            techniqueSet=[string]$g.techniqueSet
            imageRefs=$graphImages
        })
    }
    $graphRoot=Join-Path $work 'xenon_graph'
    if(Test-Path -LiteralPath $graphRoot -PathType Container){
        Remove-Item -LiteralPath $graphRoot -Recurse -Force
    }
    New-Item -Path $graphRoot -ItemType Directory -Force | Out-Null
    & robocopy.exe $unlinked $graphRoot '/E' '/R:1' '/W:1' '/NP' '/NFL' '/NDL' '/NJH' '/NJS' | Out-Null
    if([int]$LASTEXITCODE -ge 8){throw 'Xenon material graph copy failed.'}
    foreach($row in $graphRows.ToArray()){
        $graphsCopied=@(Get-ChildItem -LiteralPath (Join-Path $graphRoot 'xenon_material_graphs') -Filter '*.json' -File -Recurse)
        $match=@($graphsCopied | Where-Object {
            (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash -ieq [string]$row.jsonSha256
        })
        if($match.Count -ne 1){throw "Source-backed Xenon material graph $($row.name) did not survive copied SHA validation."}
    }
    $standaloneFF=Join-Path $work 'aztec_pc_two_materials_VALIDATION_ONLY.ff'
    Copy-Item -LiteralPath $ff -Destination $standaloneFF -Force
    if((Get-FileHash -LiteralPath $standaloneFF -Algorithm SHA256).Hash -ine
       (Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash){
        throw 'Xenon standalone material FF copy SHA mismatch.'
    }
    $status='AZTEC_PC_TWO_MATERIAL_XENON_CANARY_PASS'
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-PC-MATERIAL-XENON] BLOCKER: $blocker"
    foreach($log in @('two_material_xenon_link.log','two_material_xenon_list.log','two_material_xenon_graph.log')){
        $file=Join-Path $logRoot $log
        if(Test-Path -LiteralPath $file -PathType Leaf){
            foreach($line in @(Get-Content -LiteralPath $file -Tail 5)){
                Write-Host "[AZTEC-PC-MATERIAL-XENON] $log $line"
            }
        }
    }
}finally{
    if(-not [string]::IsNullOrWhiteSpace($short) -and
       (Test-Path -LiteralPath $short -PathType Container)){
        Remove-Item -LiteralPath $short -Recurse -Force
    }
}
$report=[ordered]@{
    schema='WaW360.AztecPcTwoMaterialXenonCanary.v1'
    status=$status
    sourcePcMaterials=$sourceRows.ToArray()
    sourcePcMaterialCount=$sourceRows.Count
    xenonMaterialGraphs=$graphRows.ToArray()
    xenonMaterialGraphCount=$graphRows.Count
    xenonGraphRoot=$graphRoot
    standaloneValidationFastFile=$standaloneFF
    blocker=$blocker
    nativeRootGraphIntegrated=$false
    materialBodiesPromotedIntoAztecMap=0
    readyForXbox=$false;convertedMapsPromotionApproved=$false
    note='Standalone PC-source Material graph roundtrip only. External TechniqueSet/image names are references, not resident renderer or pixel payloads. NOT FOR XBOX.'
}
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath -Encoding UTF8
$summary=@(
    "Aztec original PC two-Material standalone Xenon OAT canary: $status"
    "source PC bodies=$($sourceRows.Count) Xenon relocated graphs=$($graphRows.Count)"
    "blocker=$blocker"
    "standalone validation-only FF=$standaloneFF"
    "graph root=$graphRoot"
    'NO complete 8,509-root Aztec FF, no runtime/shader/material closure. NOT FOR XBOX.'
)
[IO.File]::WriteAllLines($textPath,[string[]]$summary,[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-MATERIAL-XENON] ================= RESULT ========================='
Write-Host "[AZTEC-PC-MATERIAL-XENON] status=$status"
Write-Host "[AZTEC-PC-MATERIAL-XENON] sourcePCMaterials=$($sourceRows.Count) xenonMaterialGraphs=$($graphRows.Count)"
Write-Host "[AZTEC-PC-MATERIAL-XENON] report=$textPath"
Write-Host '[AZTEC-PC-MATERIAL-XENON] SCRATCH ONLY. NEVER DEPLOY ON XBOX.'
if($status -ne 'AZTEC_PC_TWO_MATERIAL_XENON_CANARY_PASS'){exit 4}
