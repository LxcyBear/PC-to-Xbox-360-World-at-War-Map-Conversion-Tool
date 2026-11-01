[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Read-only, exact-source v5: two unique OAT-typed PC FlameTable
# definitions vs four owned inline source bodies and 67 real original
# Xbox retail OAT Material XAsset lists. Original FFs / XMA untouched.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$dir=Join-Path $base 'pc_g11_flame_loaded_virtual_target_proof'
$typedFile=Join-Path $dir 'aztec_four_pc_flame_tables_typed_weapon_material_names.json'
$v2File=Join-Path $dir 'aztec_26_flame_packed_original_pc_typed_material_alias_probe_v2.json'
$parentFile=Join-Path $base 'pc_g11_flame_real_source_sprint\aztec_flame_packed_material_exact_pc_zone_join.json'
$stock=Join-Path $base 'pc_fastfile_image_sources\stock_identity_logs'
$good=Join-Path $repo '.deps\aztec-w63-direct-ff-growth\scratch_emit\nazi_zombie_aztec.ff'
$goodManifest=Join-Path $repo '.deps\aztec-w63-direct-ff-growth\aztec_weapon63_ff_growth_63.json'
$report=Join-Path $dir 'aztec_4_parents_2_typed_flame_tables_10_material_names_xbox_67_zones_v5.json'
foreach($path in @($typedFile,$v2File,$parentFile,$good,$goodManifest)){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Genuine original source proof is missing: $path"
    }
}
$t=Get-Content -LiteralPath $typedFile -Raw | ConvertFrom-Json
$v=Get-Content -LiteralPath $v2File -Raw | ConvertFrom-Json
$p=Get-Content -LiteralPath $parentFile -Raw | ConvertFrom-Json
$g=Get-Content -LiteralPath $goodManifest -Raw | ConvertFrom-Json
$goodHash=(Get-FileHash -LiteralPath $good -Algorithm SHA256).Hash
$pcSource=[string]$t.originalPcSourceFastFile
if([string]$t.schema -ne 'WaW360.AztecPcOatWeaponFlameTableDirectTypedMaterialNameAudit.v1' -or
   [string]$v.schema -ne 'WaW360.AztecFlameOriginalPcOatLoadedTypedPointers.v2' -or
   [string]$p.schema -ne 'WaW360.AztecFlameRealSourceMaterialJoin.v1' -or
   [int]$t.originalFlameTableParentStructuresFromPreviousPointerWitness -ne 4 -or
   [int]$t.originalPcPackedFlameMaterialReferenceOccurrences -ne 17 -or
   [int]$t.originalPcTypedFlameTableFilesExported -ne 2 -or
   [int]$t.actualTypedMaterialFieldNames -ne 10 -or
   [int]$t.materialNamesAlsoFoundAsOatOriginalPcXAssetRoots -ne 8 -or
   [int]$v.packedSourceNameReferences -ne 3 -or
   [int]$v.packedSourceMaterialReferences -ne 17 -or
   [int]$v.directlyReadActualPackedFlameNameReferenceOccurrences -ne 3 -or
   [int]$p.realOriginalPcFlameParentBodies -ne 4 -or
   [int]$p.nonNullPackedFlameMaterialPointers -ne 17 -or
   @($p.parents).Count -ne 4 -or
   @($t.typedOriginalPcFlameTables).Count -ne 2 -or
   [string]$v.originalPcSourceSha256 -ine [string]$t.originalPcSourceSha256 -or
   [string]$p.originalPcFastfileSha256 -ine [string]$t.originalPcSourceSha256 -or
   [string]$v.originalOatGoodXboxDiagnosticFfSha256 -ine $goodHash -or
   [string]$g.newScratchFastFileSha256 -ine $goodHash -or
   -not(Test-Path -LiteralPath $pcSource -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pcSource -Algorithm SHA256).Hash -ine
     [string]$t.originalPcSourceSha256 -or
   [bool]$t.XboxCodXeRuntimeReady -or [bool]$v.xboxHardwareReady){
    throw 'Actual four source FlameTable parents, 2 typed exports, source PC SHA or native-good FF changed.'
}
Write-Host '[AZTEC-FLAME-V5] ====================================================='
Write-Host '[AZTEC-FLAME-V5] FOUR real PC source FlameTable parents / TWO OAT typed definitions'
Write-Host '[AZTEC-FLAME-V5] TEN typed material names -> PC source roots -> 67 original Xbox retail Material lists'
Write-Host '[AZTEC-FLAME-V5] No new OAT build, no material synthesis, no FF graft, no Xbox.'
Write-Host '[AZTEC-FLAME-V5-PROGRESS] [####----------------] 20% | 4 parent SHA and 2 typed source table identities locked'

$typedTables=@{}
$typedNames=@{}
$materials=@('fire','smoke','heat','drips','streamFuel','streamFuel2','streamFlame','streamFlame2')
$namedOccurrences=0;$pcOwnedOccurrences=0
foreach($row in @($t.typedOriginalPcFlameTables)){
    $internal=[string]$row.originalPcFlameTableName
    $sourcePath=[string]$row.originalPcOatExportRelativeFile
    $basename=[IO.Path]::GetFileName(($sourcePath -replace '\\','/'))
    if([string]::IsNullOrWhiteSpace($internal) -or
       [string]::IsNullOrWhiteSpace($basename) -or
       $typedTables.ContainsKey($internal)){
        throw 'Contradictory unique typed original-PC FlameTable source export identity.'
    }
    $typedTables[$internal]=$row
    foreach($field in @($row.materialFields)){
        $label=[string]$field.typedSourceFlameMaterialField
        if($materials -notcontains $label){throw "Unexpected typed PC FlameTable material slot: $label"}
        $name=[string]$field.originalPcMaterialName
        if([string]::IsNullOrWhiteSpace($name)){continue}
        if($name -notmatch '^[A-Za-z0-9_./-]{1,160}$'){throw "Unsafe OAT source Material name $name"}
        $namedOccurrences++
        if([bool]$field.realLoadedMaterialRootAlsoListedByOat){$pcOwnedOccurrences++}
        if(-not $typedNames.ContainsKey($name)){
            $typedNames[$name]=[pscustomobject]@{
                pcRootMatches=(New-Object 'System.Collections.Generic.List[bool]')
                occurrences=(New-Object 'System.Collections.Generic.List[object]')
                retail=(New-Object 'System.Collections.Generic.List[object]')
            }
        }
        $typedNames[$name].pcRootMatches.Add([bool]$field.realLoadedMaterialRootAlsoListedByOat)
        $typedNames[$name].occurrences.Add([pscustomobject]@{
            typedFlameTableInternalName=$internal
            typedFlameTableExportBasename=$basename
            materialField=$label
            independentPcMaterialOatRoot=[bool]$field.realLoadedMaterialRootAlsoListedByOat
        })
    }
}
if($namedOccurrences -ne 10 -or $pcOwnedOccurrences -ne 8){
    throw "Typed PC field counts changed: names=$namedOccurrences roots=$pcOwnedOccurrences."
}
Write-Host '[AZTEC-FLAME-V5-PROGRESS] [########------------] 40% | 10 actual original-PC typed material identities and two unconfirmed PC roots classified'

# Offline retail names from source-hash-checked OAT --list data. The
# source zone FF must match cached metadata. Reference-only Material
# names are NEVER treated as owned bodies or as native renderer graph.
$accepted=0;$stale=0;$seenZones=@{}
if(-not(Test-Path -LiteralPath $stock -PathType Container)){
    throw "Actual 67-zone Xbox retail OAT Material identity catalog missing: $stock"
}
foreach($meta in @(Get-ChildItem -LiteralPath $stock -File -Filter '*.meta.json')){
    $m=$null
    try{
        $m=Get-Content -LiteralPath $meta.FullName -Raw|ConvertFrom-Json
        $src=[string]$m.file
        $oatLog=$meta.FullName.Substring(0,$meta.FullName.Length-'.meta.json'.Length)
        if(-not(Test-Path -LiteralPath $src -PathType Leaf) -or
           -not(Test-Path -LiteralPath $oatLog -PathType Leaf) -or
           [int]$m.exitCode -ne 0){$stale++;continue}
        $info=Get-Item -LiteralPath $src
        if($seenZones.ContainsKey($info.FullName) -or
           [long]$info.Length -ne [long]$m.bytes -or
           $info.LastWriteTimeUtc.ToString('o') -cne [string]$m.modifiedUtc){
            $stale++;continue
        }
        $seenZones[$info.FullName]=$true
        $accepted++
        foreach($line in [IO.File]::ReadLines($oatLog)){
            $str=$line.TrimStart()
            if($str.StartsWith('[',[StringComparison]::Ordinal)){
                $ix=$str.IndexOf(']')
                if($ix -ge 0 -and $ix+1 -lt $str.Length){
                    $str=$str.Substring($ix+1).TrimStart()
                }
            }
            if($str -notmatch '(?i)^material\s*,\s*(.*?)\s*$'){continue}
            $listed=[string]$Matches[1]
            $listed=$listed.Trim().Trim('"')
            if([string]::IsNullOrWhiteSpace($listed)){continue}
            $reference=$listed.StartsWith(',',[StringComparison]::Ordinal)
            $name=if($reference){$listed.TrimStart(',')}else{$listed}
            if(-not $typedNames.ContainsKey($name)){continue}
            $typedNames[$name].retail.Add([pscustomobject]@{
                originalXboxSourceFf=$info.FullName
                sourceZone=[IO.Path]::GetFileName($info.FullName)
                sourceOatList=$oatLog
                recordOwnership=if($reference){'REFERENCE_ONLY'}else{'OAT_OWNED_ROOT_NAME'}
                stockBaseZone=([IO.Path]::GetFileName($info.FullName).ToLowerInvariant() -in
                    @('common.ff','code_post_gfx.ff','patch.ff'))
                nativeImageTechniqueShaderGraphProven=$false
                CodXeRuntimeAssetResident=$false
            })
        }
    }catch{
        $stale++
    }
}
if($accepted -ne 67 -or $stale -ne 0){
    throw "Original Xbox retail Material OAT catalog incomplete/stale: accepted=$accepted stale=$stale. No stock substitution."
}
Write-Host '[AZTEC-FLAME-V5-PROGRESS] [############--------] 60% | 67 actual original Xbox Material XAsset name catalogs scanned, stale=0'

$results=New-Object 'System.Collections.Generic.List[object]'
$ownedUnique=0;$ownedOccurrences=0
$unmatchedPcNames=New-Object 'System.Collections.Generic.List[string]'
foreach($name in @($typedNames.Keys|Sort-Object)){
    $vrow=$typedNames[$name]
    $catalog=@($vrow.retail.ToArray())
    $owned=@($catalog|Where-Object {$_.recordOwnership -eq 'OAT_OWNED_ROOT_NAME'})
    $refs=@($catalog|Where-Object {$_.recordOwnership -eq 'REFERENCE_ONLY'})
    $pcRoot=(@($vrow.pcRootMatches.ToArray()|Where-Object {$_}).Count -gt 0)
    if($owned.Count -gt 0){
        $ownedUnique++
        $ownedOccurrences+=@($vrow.occurrences.ToArray()).Count
    }
    if(-not $pcRoot){$unmatchedPcNames.Add($name)}
    $state=if($pcRoot -and $owned.Count -gt 0){'PC_SOURCE_ROOT_AND_XBOX_RETAIL_OWNED_NAME'}
        elseif($owned.Count -gt 0){'XBOX_RETAIL_OWNED_NAME_PC_ROOT_UNCONFIRMED'}
        elseif($pcRoot){'PC_SOURCE_ROOT_XBOX_RETAIL_OWNED_BODY_UNCONFIRMED'}
        elseif($refs.Count -gt 0){'XBOX_REFERENCE_ONLY_PC_ROOT_UNCONFIRMED'}
        else{'ORIGINAL_PC_TYPED_NAME_ONLY_NO_PC_LISTED_ROOT_OR_XBOX_RETAIL_OWNER'}
    Write-Host "[AZTEC-FLAME-V5-MATERIAL] name=$name pc-root=$pcRoot xbox-owned=$($owned.Count) xbox-ref-only=$($refs.Count) state=$state"
    $results.Add([pscustomobject][ordered]@{
        exactPcTypedMaterialName=$name
        actualOriginalPcTypedFlameOccurrences=@($vrow.occurrences.ToArray())
        originalPcOatMaterialRootNameMatched=$pcRoot
        xboxRetailOatOwnedNameMatches=$owned.Count
        xboxRetailOatReferenceOnlyMatches=$refs.Count
        nameEvidenceState=$state
        xboxRetailNameEvidence=@($catalog)
        originalPCMaterialBodyForThisFlameFieldProven=$false
        actualNativeXboxTechniqueImageShaderGraphClosed=$false
        approvedNativeFfBinding=$false
    })
}
# Reconcile each source-owned parent to OAT-exported logical table.
# Only use actual original PC packed name string bytes; never infer
# logical ownership from "first"/"third" filename resemblance.
$realPackedNames=@{}
foreach($row in @($v.typedSourceTargets)){
    if(@($row.roles) -notcontains 'FlameName'){continue}
    $name=[string]$row.actualOriginalPcFlameNameString
    if(-not [bool]$row.originalPcFlameNameDirectlyByteVerified -or
       [string]::IsNullOrWhiteSpace($name)){continue}
    $raw=[string]$row.rawOriginalPcPackedPointer
    $realPackedNames[$raw.ToUpperInvariant()]=$name
}
$parentRows=New-Object 'System.Collections.Generic.List[object]'
$resolvedParents=0;$unresolvedParents=0;$packedMaterialCount=0;$inlineMaterialCount=0;$nullMaterialCount=0
foreach($parent in @($p.parents)){
    if(-not [bool]$parent.originalPcGfxWorldFastFileByteProvenSameSource){
        throw "Four Flame parent proof not identical to original PC source FF: $($parent.weapon)"
    }
    $nameCells=@($parent.sourceNameAndMaterialReferences|Where-Object {$_.kind -eq 'FlameName'})
    $materialCells=@($parent.sourceNameAndMaterialReferences|Where-Object {$_.kind -eq 'FlameMaterial'})
    if($nameCells.Count -ne 1 -or $materialCells.Count -ne 8){
        throw "Original PC Flame parent typed field slots changed: $($parent.weapon) / $($parent.parentLabel)"
    }
    $rawName=([string]$nameCells[0].sourcePointer).ToUpperInvariant()
    $realName=if($realPackedNames.ContainsKey($rawName)){$realPackedNames[$rawName]}else{$null}
    $logical=@()
    if($realName){
        foreach($table in @($t.typedOriginalPcFlameTables)){
            $internal=[string]$table.originalPcFlameTableName
            $basenm=[IO.Path]::GetFileName(([string]$table.originalPcOatExportRelativeFile -replace '\\','/'))
            if($realName -ceq $internal -or $realName -ceq $basenm){$logical+=@($table)}
        }
    }
    if($logical.Count -gt 1){
        throw "Source Flame name resolves to two contradictory OAT-typed definitions: $realName"
    }
    if($logical.Count -eq 1){$resolvedParents++}else{$unresolvedParents++}
    $cells=New-Object 'System.Collections.Generic.List[object]'
    foreach($child in $materialCells){
        $raw=[string]$child.sourcePointer
        $sourceKind=[string]$child.rawPointerKind
        $labels=[string]$child.label
        if($raw -eq '0x00000000'){$nullMaterialCount++}
        elseif($raw -in @('0xFFFFFFFF','0xFFFFFFFE')){$inlineMaterialCount++}
        else{$packedMaterialCount++}
        $cells.Add([pscustomobject][ordered]@{
            originalPcMaterialField=$labels
            actualOriginalPcSourcePointer=$raw
            originalPcPointerKind=$sourceKind
            sourceOwnedNullPreserved=($raw -eq '0x00000000')
            exactMaterialNameFromThisSpecificPointerProven=$false
        })
    }
    $parentRows.Add([pscustomobject][ordered]@{
        originalPcWeaponOwner=[string]$parent.weapon
        originalPcParentLabel=[string]$parent.parentLabel
        originalPcParentSourceSha256=[string]$parent.parentSourceSha256
        originalPcFlameNamePointer=$rawName
        originalPcFlameNameFromOriginalSourceBytes=$realName
        logicalOatFlameTableMatch=if($logical.Count -eq 1){
            [string]$logical[0].originalPcFlameTableName
        }else{$null}
        genuinePackedNameByteIdentified=($null -ne $realName)
        sourceSpecificMaterialPointers=@($cells.ToArray())
        nativeXboxMaterialGraphReady=$false
    })
    Write-Host "[AZTEC-FLAME-V5-PARENT] weapon=$($parent.weapon) slot=$($parent.parentLabel) source-name=$realName typed-table-match=$($logical.Count -eq 1)"
}
if($parentRows.Count -ne 4 -or $packedMaterialCount -ne 17 -or
   $inlineMaterialCount -ne 3 -or $nullMaterialCount -ne 12){
    throw "Four parent source owned FlameMaterial fields changed: packed=$packedMaterialCount inline=$inlineMaterialCount null=$nullMaterialCount"
}
if((Get-FileHash -LiteralPath $pcSource -Algorithm SHA256).Hash -ine
   [string]$t.originalPcSourceSha256 -or
   (Get-FileHash -LiteralPath $good -Algorithm SHA256).Hash -ine $goodHash){
    throw 'Protected PC or OAT-good Xbox FF changed during read-only retail material census.'
}
$body=[ordered]@{
    schema='WaW360.AztecRealPcFlameFourParentTwoTypedMaterialRetailCrosswalk.v5'
    status='TWO_PC_OAT_TYPED_FLAME_MATERIAL_NAME_DEFINITIONS_AND_67_XBOX_RETAIL_NAME_LISTS_AUDITED_NO_NATIVE_GRAFT'
    actualOriginalPcInlineFlameTableParents=4
    originalPcTypedOatFlameDefinitions=2
    originalPcNamedMaterialOccurrencesInTwoTypedTables=10
    materialOccurrencesMatchedOriginalPcOatRootNames=8
    uniqueNamedMaterialIdentities=$typedNames.Count
    uniqueMaterialNamesWithXboxOwnedAssetNameEvidence=$ownedUnique
    materialNameOccurrencesWithXboxOwnedAssetNameEvidence=$ownedOccurrences
    materialNamesWithoutIndependentPcOatRoot=@($unmatchedPcNames.ToArray())
    fourOriginalPcParentsWithExactNameToTypedDefinitionJoin=$resolvedParents
    fourOriginalPcParentsStillRequiringNameOwnerProof=$unresolvedParents
    originalPcPackedMaterialPointerReferencesPreserved=$packedMaterialCount
    originalPcInlineMaterialPointerReferencesPreserved=$inlineMaterialCount
    originalPcNullMaterialPointerFieldsPreserved=$nullMaterialCount
    originalXboxRetailSourceZonesValidated=$accepted
    xboxRetailSourceCatalogStale=$stale
    allOriginalPcMaterialBodiesAndXboxTechniqueImagesClosed=$false
    authenticFlamethrowerXboxXmaSamplesAlreadyProvenByPriorV4=6
    outputMapFfWritten=$false
    originalPcSourceFFUnmodified=$true
    originalOatGoodNativeFFUnmodified=$true
    XboxCodXeRuntimeReady=$false
    sourceMaterialNameEvidence=@($results.ToArray())
    originalPcFourParentReconciliation=@($parentRows.ToArray())
}
$body|ConvertTo-Json -Depth 18|Set-Content -LiteralPath $report -Encoding UTF8
Write-Host '[AZTEC-FLAME-V5-PROGRESS] [################----] 80% | four genuine PC parent source names and both typed definition identities reconciled where proven'
Write-Host "[AZTEC-FLAME-V5] Source logical Flame parent->typed definition exact-name joins=$resolvedParents/4; source material refs=17 packed + 3 inline + 12 NULL"
Write-Host "[AZTEC-FLAME-V5] PC OAT typed material root matches=8/10; unique Xbox retail owned name evidence=$ownedUnique/$($typedNames.Count)"
Write-Host "[AZTEC-FLAME-V5] PC Material root nonmatches: $($unmatchedPcNames -join ', ')"
Write-Host "[AZTEC-FLAME-V5] STATUS=$($body.status)"
Write-Host "[AZTEC-FLAME-V5] JSON=$report"
Write-Host '[AZTEC-FLAME-V5-PROGRESS] [####################] 100% | real source assets, retail names, no donor bytes or Xbox FF modification'
