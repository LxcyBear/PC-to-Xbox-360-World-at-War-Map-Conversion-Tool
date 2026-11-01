[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Reuse PINNED OAT's existing original-PC T4 WeaponDumperT4. It directly
# walks its already-loaded typed WeaponDef->FlameTable->Material* objects
# and outputs original FlameTable field names. No serialized pointer
# guessing, no new C++ build, no FF production or Xbox deployment.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$prior=Join-Path $base 'pc_g11_flame_loaded_virtual_target_proof\aztec_five_original_pc_material_pointer_cells_and_real_xbox_xma_sprint.json'
$out=Join-Path $base 'pc_g11_flame_loaded_virtual_target_proof'
$report=Join-Path $out 'aztec_four_pc_flame_tables_typed_weapon_material_names.json'
$log=Join-Path $out 'aztec_original_pc_weapon_native_oat_flame_table_export.log'
$evidence=Join-Path $repo '.deps\aztec-original-pc-typed-flame-table-proofs'
$pcList=Join-Path $out 'aztec_26_flame_packed_typed_material_alias_loader_v2.log'
$bin=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$goodFF=Join-Path $repo '.deps\aztec-w63-direct-ff-growth\scratch_emit\nazi_zombie_aztec.ff'
foreach($f in @($prior,$bin,$goodFF)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){
        throw "Genuine original-PC Material/Flame source prerequisite missing: $f"
    }
}
$j=Get-Content -LiteralPath $prior -Raw|ConvertFrom-Json
$pc=[string]$j.originalPcSourceFastFile
if([string]$j.schema -ne 'WaW360.AztecFiveFlameMaterialSlotRealPcSourceAndXboxXmaSprint.v3' -or
   [int]$j.originalFlameTableParents -ne 4 -or
   [int]$j.originalPcFlameMaterialPackedReferences -ne 17 -or
   [int]$j.uniqueMaterialPointerTargets -ne 5 -or
   @($j.sourceMaterialTargets).Count -ne 5 -or
   [bool]$j.originalOatGoodFFModified -or
   [bool]$j.xboxHardwareReady -or
   -not(Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$j.originalPcSourceFastFileSha256){
    throw 'Original-PC exact source FF or 5/17 Flame Material reference evidence inconsistent. No name promotion.'
}
$goodSHA=(Get-FileHash -LiteralPath $goodFF -Algorithm SHA256).Hash
if($goodSHA -ine [string]$j.originalNativeFFSha256){
    throw 'Previously OAT-good original Xbox diagnostic FF SHA changed; source-only scan refused.'
}
New-Item -ItemType Directory -Path $out -Force|Out-Null
Write-Host '[AZTEC-FLAME-PC-TYPED] ====================================================='
Write-Host '[AZTEC-FLAME-PC-TYPED] Existing PINNED T4 OAT WeaponDumper direct typed FlameTable Material fields.'
Write-Host '[AZTEC-FLAME-PC-TYPED] Original PC zone SHA pinned; old native FF protected. No build, no filler.'
Write-Host '[AZTEC-FLAME-PC-TYPED-PROGRESS] [####----------------] 20% | original PC zone and five material-source target proofs locked'
$tmp=Join-Path ([IO.Path]::GetTempPath()) ('ZFLMAT_'+[Guid]::NewGuid().ToString('N').Substring(0,10))
New-Item -ItemType Directory -Path $tmp -Force|Out-Null
$export=Join-Path $tmp 'original_pc_oat_weapon_export'
$stdout=Join-Path $tmp 'out.log'
$stderr=Join-Path $tmp 'err.log'
$exit=-1
$rows=New-Object 'System.Collections.Generic.List[object]'
$materialFieldNames=@('fire','smoke','heat','drips','streamFuel','streamFuel2','streamFlame','streamFlame2')
$rootHash=@{}
$materialOccurrences=0
$materialNamed=0
$materialVerified=0
$materialNames=@{}
$unparsed=New-Object 'System.Collections.Generic.List[object]'
try{
    $argParts=@('--no-color','--include-assets','weapon','--output-folder',$export,$pc)
    $args=($argParts|ForEach-Object{'"'+([string]$_).Replace('"','\"')+'"'}) -join ' '
    $proc=Start-Process -FilePath $bin -ArgumentList $args -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru -Wait -NoNewWindow
    $exit=[int]$proc.ExitCode
    $combined=[IO.File]::ReadAllText($stdout)+[Environment]::NewLine+[IO.File]::ReadAllText($stderr)
    [IO.File]::WriteAllText($log,$combined,[Text.UTF8Encoding]::new($false))
    if($exit -ne 0){
        Write-Host "[AZTEC-FLAME-PC-TYPED] Original-PC OAT weapon export failed exit=$exit; log=$log"
        throw 'Original PC loaded WeaponDef->FlameTable typed material export failed. No source identities promoted.'
    }
    Write-Host '[AZTEC-FLAME-PC-TYPED-PROGRESS] [########------------] 40% | original PC typed OAT weapon dump returned'
    $files=@(Get-ChildItem -LiteralPath $export -Recurse -File -ErrorAction SilentlyContinue)
    if($files.Count -gt 20000){throw 'Unexpected excessive file count in original PC-only OAT weapon export.'}
    New-Item -ItemType Directory -Path $evidence -Force|Out-Null
    # --include-assets weapon does not necessarily print all Material
    # roots. Reuse the SHA-pinned previous OAT --list loaded ORIGINAL PC
    # source log rather than falsely reporting zero independent roots.
    $sourceLists=@($combined)
    if(Test-Path -LiteralPath $pcList -PathType Leaf){
        $sourceLists+=@([IO.File]::ReadAllText($pcList))
    }
    $materialRoots=@(($sourceLists -join [Environment]::NewLine) -split '[\r\n]+'|ForEach-Object{
        $m=[regex]::Match([string]$_,'^\s*material\s*,\s*(\S(?:.*\S)?)\s*$',
           [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if($m.Success){$m.Groups[1].Value.Trim()}
    }|Sort-Object -Unique)
    foreach($file in $files){
        if([long]$file.Length -gt 524288L -or [long]$file.Length -lt 60L){continue}
        $bytes=[IO.File]::ReadAllText($file.FullName)
        # Actual pinned OAT T4 InfoString::ToString(prefix) output:
        # prefix\key\value\key\value ...
        if(-not $bytes.Contains('\flameVar_streamChunkGravityStart\')){continue}
        $parts=@($bytes -split '\\')
        $dict=@{}
        for($i=1;$i+1 -lt $parts.Count;$i+=2){
            $key=[string]$parts[$i]
            $val=[string]$parts[$i+1]
            if($dict.ContainsKey($key)){throw "Repeated FlameTable source key $key in real OAT export $($file.Name)"}
            $dict[$key]=$val
        }
        if(-not $dict.ContainsKey('flameVar_streamChunkGravityStart')){
            $unparsed.Add([pscustomobject]@{file=$file.FullName;reason='Unknown typed original PC FlameTable property layout'})
            continue
        }
        $relative=$file.FullName.Substring($export.Length).TrimStart('\','/')
        $flameName=if($dict.ContainsKey('name')){[string]$dict['name']}else{''}
        $sha=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        $stage=Join-Path $evidence ('{0}_{1}.txt' -f $sha.Substring(0,16),[IO.Path]::GetFileName($file.FullName))
        Copy-Item -LiteralPath $file.FullName -Destination $stage -Force
        if((Get-FileHash -LiteralPath $stage -Algorithm SHA256).Hash -ine $sha){throw 'Original PC FlameTable typed export copied bytes SHA mismatch'}
        if($rootHash.ContainsKey($flameName)){
            if($rootHash[$flameName] -ine $sha){
                throw "Original PC FlameTable $flameName exported with contradictory actual source bytes."
            }
            continue
        }
        $rootHash[$flameName]=$sha
        $fields=New-Object 'System.Collections.Generic.List[object]'
        foreach($field in $materialFieldNames){
            $value=if($dict.ContainsKey($field)){[string]$dict[$field]}else{$null}
            $isReal=($value -and $value -match '^[A-Za-z0-9_./-]{1,160}$')
            $identity=($isReal -and $materialRoots -contains $value)
            if($isReal){
                $materialOccurrences++
                $materialNamed++
                if($identity){$materialVerified++}
                $materialNames[$value]=$true
            }
            $fields.Add([pscustomobject][ordered]@{
                typedSourceFlameMaterialField=$field
                originalPcMaterialName=if($isReal){$value}else{$null}
                realLoadedMaterialRootAlsoListedByOat=$identity
                actualOriginalPcNullField=($value -eq '')
                nativeXboxMaterialImageTechniqueGraphProven=$false
            })
        }
        $rows.Add([pscustomobject][ordered]@{
            originalPcFlameTableName=$flameName
            originalPcOatExportRelativeFile=$relative
            exactOriginalPcTypedFlameTableSourceSha256=$sha
            actualTypedOatSourceDumpStagedFile=$stage
            materialFields=@($fields.ToArray())
        })
        Write-Host "[AZTEC-FLAME-PC-TYPED] original-PC FlameTable=$flameName materialNames=$(@($fields.ToArray()|Where-Object {$_.originalPcMaterialName}).Count) export=$relative"
    }
    if((Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$j.originalPcSourceFastFileSha256 -or
       (Get-FileHash -LiteralPath $goodFF -Algorithm SHA256).Hash -ine $goodSHA){
        throw 'Protected original-PC FF or OAT-good native FF changed during material source scan.'
    }
    $reportBody=[ordered]@{
        schema='WaW360.AztecPcOatWeaponFlameTableDirectTypedMaterialNameAudit.v1'
        status=if($rows.Count -gt 0){'GENUINE_ORIGINAL_PC_TYPED_FLAME_MATERIAL_NAMES_EXPORTED_XBOX_NATIVE_GRAPHS_PENDING'}
           else{'NO_TYPED_FLAME_TABLE_FILES_EXPORTED_BY_ORIGINAL_PC_OAT_WEAPON_DUMPER'}
        originalPcSourceFastFile=$pc
        originalPcSourceSha256=[string]$j.originalPcSourceFastFileSha256
        pinnedNativeOatWin32UnlinkerSourceBuild='3beaa7c81af517f0c05f2b4612d477ed1541b676'
        originalFlameTableParentStructuresFromPreviousPointerWitness=4
        originalPcPackedFlameMaterialReferenceOccurrences=17
        uniqueOriginalPcMaterialPackedPointerTargets=5
        originalPcTypedFlameTableFilesExported=$rows.Count
        originalPcTypedNonNullMaterialFieldOccurrences=$materialOccurrences
        actualTypedMaterialFieldNames=$materialNamed
        materialNamesAlsoFoundAsOatOriginalPcXAssetRoots=$materialVerified
        originalPcTypedUniqueMaterialNames=$materialNames.Count
        originalPcSourceNullMaterialFieldsModified=$false
        xboxNativeMaterialShaderImageTechniqueGraphValidated=$false
        originalOatGoodNativeFfUnmodified=$true
        XboxCodXeRuntimeReady=$false
        originalPcOatWeaponDumpExitCode=$exit
        originalPcOatWeaponDumpLog=$log
        typedOriginalPcFlameTables=@($rows.ToArray())
        unparsedTypedSourceFiles=@($unparsed.ToArray())
    }
    $reportBody|ConvertTo-Json -Depth 14|Set-Content -LiteralPath $report -Encoding UTF8
    Write-Host "[AZTEC-FLAME-PC-TYPED] OAT typed source FlameTables=$($rows.Count), original-PC material name occurrences=$materialNamed, OAT Material roots=$materialVerified"
    Write-Host "[AZTEC-FLAME-PC-TYPED] STATUS=$($reportBody.status)"
    Write-Host "[AZTEC-FLAME-PC-TYPED] JSON=$report"
    Write-Host '[AZTEC-FLAME-PC-TYPED-PROGRESS] [####################] 100% | OAT true typed original-PC FlameTable material fields; native FF still protected'
}finally{
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
