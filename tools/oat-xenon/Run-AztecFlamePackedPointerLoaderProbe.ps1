[CmdletBinding()]
param([switch]$BuildDumper)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# The PC XFile VIRTUAL offset is NOT a raw decompressed-FF byte offset.
# This diagnostic probes the original x86 OAT-loaded source block for
# every still-packed 17 FlameTable Material*, 3 name and 6 sound refs.
# Raw bytes and strings are direct original-PC memory evidence only;
# NEVER treat 32 header bytes as a complete Material or XMA sample.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$oldReport=Join-Path $base 'pc_g11_flame_real_source_sprint\aztec_flame_packed_material_exact_pc_zone_join.json'
$flame=Join-Path $base 'pc_g11_lightning_weapon63_flame_inline_real_source\aztec_63_original_pc_flame_real_inline_child_sha_witness.json'
$oldFf=Join-Path $repo '.deps\aztec-w63-direct-ff-growth\scratch_emit\nazi_zombie_aztec.ff'
$oldFfReport=Join-Path $repo '.deps\aztec-w63-direct-ff-growth\aztec_weapon63_ff_growth_63.json'
$dumper=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$capFile=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$out=Join-Path $base 'pc_g11_flame_loaded_virtual_target_proof'
$report=Join-Path $out 'aztec_26_flame_packed_original_pc_typed_material_alias_probe_v2.json'
$capability='t4-pc-flame-packed-typed-material-and-alias-v2'
foreach($f in @($oldReport,$flame,$oldFf,$oldFfReport)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){throw "No genuine Aztec source/OAT proof file: $f"}
}
$j=Get-Content -LiteralPath $oldReport -Raw|ConvertFrom-Json
$s=Get-Content -LiteralPath $flame -Raw|ConvertFrom-Json
$previous=Get-Content -LiteralPath $oldFfReport -Raw|ConvertFrom-Json
$ffHash=(Get-FileHash -LiteralPath $oldFf -Algorithm SHA256).Hash
if([string]$j.schema -ne 'WaW360.AztecFlameRealSourceMaterialJoin.v1' -or
   [string]$j.status -ne 'AZTEC_FLAME_PACKED_SOURCE_MATERIAL_MATCH_AUDIT_COMPLETE_NATIVE_ASSETS_UNRESOLVED' -or
   [int]$j.originalPcFlameParentSameZoneFixedBodyAndParentShaVerified -ne 4 -or
   [int]$j.nonNullPackedFlameMaterialPointers -ne 17 -or
   [int]$j.packedFlameMaterialPointersStillUnresolved -ne 17 -or
   [int]$j.originalPcPackedFlameNamesStillUnresolved -ne 3 -or
   [int]$j.originalPcPackedFlameSoundAliasReferencesUnresolved -ne 6 -or
   @($j.parents).Count -ne 4 -or
   [int]$s.inlineOriginalPcFlameParentTables -ne 4 -or
   [string]$previous.newScratchFastFileSha256 -ine $ffHash -or
   [long](Get-Item -LiteralPath $oldFf).Length -ne 4514599L){
    throw 'Original PC same-zone FlameTable proof or previous OAT-good 222-alias FF drifted.'
}
$pc=[IO.Path]::GetFullPath([string]$j.worldPcFastfile)
if(-not(Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$j.originalPcFastfileSha256){
    throw 'Original PC FastFile SHA changed. Do not trace different source pointer address space.'
}
$byRaw=@{}
[int]$materialRefs=0;[int]$nameRefs=0;[int]$soundRefs=0
foreach($p in @($j.parents)){
    if(-not [bool]$p.originalPcGfxWorldFastFileByteProvenSameSource){
        throw "FlameTable parent source owner no longer matches PC zone: $($p.weapon)"
    }
    $fields=@($p.sourceNameAndMaterialReferences)+@($p.sourceFlameSoundReferences)
    foreach($c in $fields){
        $raw=if($null -ne $c.PSObject.Properties['sourcePointer']){
            [string]$c.sourcePointer
        }else{[string]$c.raw}
        # Sound rows carry a decoded pointer kind named 'kind' (Offset),
        # NOT a FlameMaterial/FlameName asset kind. Discriminate by the
        # schema-owned original sourcePointer vs raw key.
        $kind=if($null -ne $c.PSObject.Properties['sourcePointer']){
            [string]$c.kind
        }else{'FlameSound'}
        if($raw -notmatch '^0x[0-9A-Fa-f]{8}$'){throw "Invalid original source pointer: $raw"}
        $number=[Convert]::ToUInt64($raw.Substring(2),16)
        if($number -eq 0 -or $number -ge 4294967294L){continue}
        $raw=('{0:X8}' -f $number)
        if($kind -notin @('FlameName','FlameMaterial','FlameSound')){
            throw "Unexpected packed source pointer type: $kind"
        }
        if($kind -eq 'FlameMaterial'){$materialRefs++}
        elseif($kind -eq 'FlameName'){$nameRefs++}
        else{$soundRefs++}
        if(-not $byRaw.ContainsKey($raw)){
            $byRaw[$raw]=[pscustomobject]@{
                raw=$raw
                block=[int](($number-1L) -shr 29)
                virtualOffset=[long](($number-1L) -band 0x1FFFFFFF)
                usages=(New-Object 'System.Collections.Generic.List[object]')
            }
        }
        $byRaw[$raw].usages.Add([pscustomobject]@{
            weapon=[string]$p.weapon
            flameParent=[string]$p.parentLabel
            kind=$kind
            sourceLabel=[string]$c.label
        })
    }
}
if($materialRefs -ne 17 -or $nameRefs -ne 3 -or $soundRefs -ne 6 -or
   $byRaw.Count -gt 26 -or $byRaw.Count -lt 1){
    throw "Real original-PC packed FlameTable source refs changed: material=$materialRefs name=$nameRefs sound=$soundRefs unique=$($byRaw.Count)"
}
New-Item -ItemType Directory -Path $out -Force|Out-Null
Write-Host '[AZTEC-FLAME-OAT] ====================================================='
Write-Host '[AZTEC-FLAME-OAT] ONE NATIVE OAT LOADER PASS: original PC 17 Material, 3 name, 6 sound packed references'
Write-Host '[AZTEC-FLAME-OAT] Source SHA + all four FlameTable parent fixed bytes proven. No pointer/address guessing.'
Write-Host '[AZTEC-FLAME-OAT] No null slot changes, no filler, no FF, no Xbox.'
Write-Host '[AZTEC-FLAME-OAT-PROGRESS] [####----------------] 20% | 26 source pointer occurrences and previous OAT FF hashes locked'
$installed=$false
if((Test-Path -LiteralPath $dumper -PathType Leaf) -and
   (Test-Path -LiteralPath $capFile -PathType Leaf)){
    $c=Get-Content -LiteralPath $capFile -Raw|ConvertFrom-Json
    $installed=([string]$c.format -eq 'WaW360.OatGraphDumperCapabilities.v1' -and
        [string]$c.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($c.capabilities) -contains $capability)
}
if(-not $installed -and $BuildDumper){
    Write-Host '[AZTEC-FLAME-OAT] One-time genuine original-PC OAT x86 loader witness rebuild requested.'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1') -TraceWeaponNamePointers
    if($LASTEXITCODE -ne 0){throw 'Pinned OAT loader FlameTable witness build failed. Previous FF protected.'}
    if(-not(Test-Path -LiteralPath $dumper -PathType Leaf) -or
       -not(Test-Path -LiteralPath $capFile -PathType Leaf)){
        throw 'OAT loader witness binary and capability manifest missing after build.'
    }
    $c=Get-Content -LiteralPath $capFile -Raw|ConvertFrom-Json
    $installed=([string]$c.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($c.capabilities) -contains $capability)
}
if(-not $installed){
    throw 'Original-PC OAT loaded packed pointer trace not installed. Run this script once with -BuildDumper.'
}
Write-Host '[AZTEC-FLAME-OAT-PROGRESS] [########------------] 40% | specialized OAT loaded virtual block evidence installed'
$tmp=Join-Path ([IO.Path]::GetTempPath()) ('WFPL_'+[guid]::NewGuid().ToString('N').Substring(0,9))
New-Item -Path $tmp -ItemType Directory -Force|Out-Null
$code=-1
$traceText=''
$log=Join-Path $out 'aztec_26_flame_packed_typed_material_alias_loader_v2.log'
try{
    $copy=Join-Path $tmp ([IO.Path]::GetFileName($pc))
    Copy-Item -LiteralPath $pc -Destination $copy -Force
    if((Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ine
       [string]$j.originalPcFastfileSha256){
        throw 'Short-path OAT source copy SHA does not match original PC fastfile.'
    }
    $std=Join-Path $tmp 'oat_out.log'
    $err=Join-Path $tmp 'oat_err.log'
    $priorEnv=[Environment]::GetEnvironmentVariable('WAW360_FLAME_POINTER_WITNESS','Process')
    $priorOther=[Environment]::GetEnvironmentVariable('WAW360_WEAPON_POINTER_WITNESS','Process')
    try{
        $tokens=@($byRaw.Keys|Sort-Object)
        [Environment]::SetEnvironmentVariable('WAW360_FLAME_POINTER_WITNESS',($tokens -join ','),'Process')
        [Environment]::SetEnvironmentVariable('WAW360_WEAPON_POINTER_WITNESS',$null,'Process')
        $argLine=(@('--list','--no-color',$copy)|ForEach-Object{'"'+([string]$_).Replace('"','\"')+'"'}) -join ' '
        $proc=Start-Process -FilePath $dumper -ArgumentList $argLine -RedirectStandardOutput $std -RedirectStandardError $err -Wait -PassThru -NoNewWindow
        $code=[int]$proc.ExitCode
    }finally{
        [Environment]::SetEnvironmentVariable('WAW360_FLAME_POINTER_WITNESS',$priorEnv,'Process')
        [Environment]::SetEnvironmentVariable('WAW360_WEAPON_POINTER_WITNESS',$priorOther,'Process')
    }
    $traceText=[IO.File]::ReadAllText($std)+[Environment]::NewLine+[IO.File]::ReadAllText($err)
    [IO.File]::WriteAllText($log,$traceText,[Text.UTF8Encoding]::new($false))
}finally{
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
if($code -ne 0){
    throw "Pinned original PC OAT x86 load refused source FF exit=$code; see $log. No material or XMA identity claimed."
}
Write-Host '[AZTEC-FLAME-OAT-PROGRESS] [############--------] 60% | original PC OAT loader completed; verify typed Material + SoundAlias source targets'
$lines=@($traceText -split '[\r\n]+')
$trace=@($lines|Where-Object {$_ -match '^\[WAW360-FLAMEPTR\] raw='})
$typedTrace=@($lines|Where-Object {$_ -match '^\[WAW360-FLAMETYPED\] raw='})
$materials=@($lines|ForEach-Object {
    $m=[regex]::Match([string]$_,'^\s*material\s*,\s*(\S(?:.*\S)?)\s*$',[Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if($m.Success){$m.Groups[1].Value.Trim()}
}|Sort-Object -Unique)
$sounds=@($lines|ForEach-Object {
    $m=[regex]::Match([string]$_,'^\s*sound\s*,\s*(\S(?:.*\S)?)\s*$',[Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if($m.Success){$m.Groups[1].Value.Trim()}
}|Sort-Object -Unique)
$results=New-Object 'System.Collections.Generic.List[object]'
[int]$loaded=0;[int]$named=0;[int]$unloaded=0;[int]$bad=0
[int]$typedMaterials=0;[int]$oatMaterialMatches=0;[int]$typedAlias=0;[int]$oatSoundMatches=0
[int]$packedNamesDirect=0;[int]$materialRefNameMatches=0;[int]$soundRefNameMatches=0
[int]$rawHeaderTargets=0;[int]$missingTyped=0
$typedPattern='^\[WAW360-FLAMETYPED\] raw=([0-9A-Fa-f]{8}) shape=(PC_MATERIAL112_NAME_MATCH|NO_TYPED_MATERIAL_MATCH) materialName=(\S+) techniqueName=(\S+) textureCount=(\d+) constantCount=(\d+) stateBitsCount=(\d+) namedImages=(\d+) images=(\S+) sndAliasName=(\S+)$'
foreach($raw in @($byRaw.Keys|Sort-Object)){
    $p=$byRaw[$raw]
    $hits=@($trace|Where-Object {$_ -match ('^\[WAW360-FLAMEPTR\] raw='+$raw+'\b')})
    $typedHits=@($typedTrace|Where-Object {$_ -match ('^\[WAW360-FLAMETYPED\] raw='+$raw+'\b')})
    if($hits.Count -ne 1 -or $typedHits.Count -ne 1){
        $missingTyped++
        Write-Host "[AZTEC-FLAME-TYPED] Missing or repeated OAT loaded source trace raw=$raw basic=$($hits.Count) typed=$($typedHits.Count)"
        continue
    }
    $m=[regex]::Match($hits[0].Trim(),'^\[WAW360-FLAMEPTR\] raw=([0-9A-Fa-f]{8}) block=(\d+) offset=0x([0-9A-Fa-f]+) status=([A-Z_]+) header32=([0-9A-Fa-f]*)(?: name=([A-Za-z0-9_./-]+))?$')
    $tm=[regex]::Match($typedHits[0].Trim(),$typedPattern)
    if(-not $m.Success -or -not $tm.Success -or
       [int]$m.Groups[2].Value -ne [int]$p.block -or
       [Convert]::ToUInt64($m.Groups[3].Value,16) -ne [long]$p.virtualOffset){
        $missingTyped++
        Write-Host "[AZTEC-FLAME-TYPED] Original PC loaded pointer/source-block or typed schema mismatch raw=$raw"
        continue
    }
    $status=[string]$m.Groups[4].Value
    $hex=[string]$m.Groups[5].Value
    $directString=[string]$m.Groups[6].Value
    $sourceMatName=[string]$tm.Groups[3].Value
    $sourceTechnique=[string]$tm.Groups[4].Value
    $sourceAlias=[string]$tm.Groups[10].Value
    $materialShape=([string]$tm.Groups[2].Value -eq 'PC_MATERIAL112_NAME_MATCH')
    $textureCount=[int]$tm.Groups[5].Value
    $constantCount=[int]$tm.Groups[6].Value
    $stateCount=[int]$tm.Groups[7].Value
    $namedImageCount=[int]$tm.Groups[8].Value
    $imageNames=[string]$tm.Groups[9].Value
    if($textureCount -gt 64 -or $constantCount -gt 64 -or $stateCount -gt 64 -or
       $namedImageCount -gt $textureCount -or
       ($materialShape -and $sourceMatName -eq '-')){
        $missingTyped++
        continue
    }
    $roles=@($p.usages|ForEach-Object{[string]$_.kind}|Sort-Object -Unique)
    $mixedRoles=$roles.Count -gt 1
    $isMaterial=$roles -contains 'FlameMaterial'
    $isSound=$roles -contains 'FlameSound'
    $isName=$roles -contains 'FlameName'
    $materialCandidate=($isMaterial -and $materialShape -and
        $sourceMatName -ne '-' -and -not $mixedRoles)
    $materialMatchesOat=($materialCandidate -and ($materials -contains $sourceMatName))
    $aliasCandidate=($isSound -and $sourceAlias -ne '-' -and -not $mixedRoles)
    $aliasMatchesOat=($aliasCandidate -and ($sounds -contains $sourceAlias))
    $flameNameDirect=($isName -and -not $mixedRoles -and
        $status -eq 'LOADED_DIRECT_STRING_TARGET' -and
        -not [string]::IsNullOrWhiteSpace($directString))
    if($status -in @('LOADED_DIRECT_STRING_TARGET','LOADED_RAW_TARGET_BYTES_UNTYPED')){$loaded++}
    else{$unloaded++}
    if($status -eq 'LOADED_DIRECT_STRING_TARGET'){$named++}
    if($status -eq 'LOADED_RAW_TARGET_BYTES_UNTYPED'){$rawHeaderTargets++}
    if($materialCandidate){$typedMaterials++}
    if($materialMatchesOat){
        $oatMaterialMatches++
        $materialRefNameMatches+=@($p.usages|Where-Object {$_.kind -eq 'FlameMaterial'}).Count
    }
    if($aliasCandidate){$typedAlias++}
    if($aliasMatchesOat){
        $oatSoundMatches++
        $soundRefNameMatches+=@($p.usages|Where-Object {$_.kind -eq 'FlameSound'}).Count
    }
    if($flameNameDirect){
        $packedNamesDirect+=@($p.usages|Where-Object {$_.kind -eq 'FlameName'}).Count
    }
    Write-Host "[AZTEC-FLAME-TYPED] raw=$raw roles=$($roles -join ',') material=$sourceMatName OAT-Material-root=$materialMatchesOat alias=$sourceAlias OAT-Sound-root=$aliasMatchesOat nameDirect=$flameNameDirect"
    $results.Add([pscustomobject][ordered]@{
        rawOriginalPcPackedPointer=('0x'+$raw)
        originalPcVirtualBlock=[int]$p.block
        decodedVirtualOffsetHex=('0x{0:X8}' -f [long]$p.virtualOffset)
        directlyLoadedOriginalPcSourceStatus=$status
        originalPcFirst32ByteHeaderHex=$hex
        roles=@($roles)
        differentAssetKindsSharePointer=$mixedRoles
        actualOriginalPcFlameNameString=if($flameNameDirect){$directString}else{$null}
        originalPcFlameNameDirectlyByteVerified=$flameNameDirect
        pcLoadedMaterial112ShapeNameCandidate=$materialCandidate
        materialNameFromActualPcLoadedMemory=if($materialCandidate){$sourceMatName}else{$null}
        materialNameIndependentlyListedAsOriginalPcOatAsset=$materialMatchesOat
        pcLoadedMaterialTechniqueName=if($sourceTechnique -ne '-'){$sourceTechnique}else{$null}
        pcLoadedMaterialTextureCount=$textureCount
        pcLoadedMaterialConstantCount=$constantCount
        pcLoadedMaterialStateBitsCount=$stateCount
        actualPcLoadedImageNamesRead=$namedImageCount
        pcLoadedImageNameCandidates=if($imageNames -eq '-'){@()}else{@($imageNames.Split(';'))}
        pcLoadedFourByteSndAliasCustomNameCandidate=$aliasCandidate
        sourceSndAliasName=if($aliasCandidate){$sourceAlias}else{$null}
        soundAliasNameIndependentlyListedAsOriginalPcOatAsset=$aliasMatchesOat
        sourceZoneFastFileSha256=[string]$j.originalPcFastfileSha256
        originalSourcePointerUses=@($p.usages.ToArray())
        authenticMaterialHeaderAndNamesDoNotProveXboxImageTechniqueGraph=$true
        authenticSoundAliasNameDoesNotProveXmaPayload=$true
        nativeMaterialAssetRuntimeValid=$false
        nativeSoundSampleXmaCodecAndBindingValid=$false
        candidateFfEmitted=$false
    })
}
if($missingTyped -ne 0 -or $typedTrace.Count -ne $byRaw.Count -or
   $results.Count -ne $byRaw.Count -or
   $loaded+$unloaded -ne $byRaw.Count -or
   $packedNamesDirect -gt 3 -or $materialRefNameMatches -gt 17 -or
   $soundRefNameMatches -gt 6){
    throw "Original PC OAT typed-v2 trace incomplete, stale or inconsistent: missing=$missingTyped typed=$($typedTrace.Count) unique=$($byRaw.Count) nameRefs=$packedNamesDirect materialRefs=$materialRefNameMatches soundRefs=$soundRefNameMatches. No FF."
}
if((Get-FileHash -LiteralPath $oldFf -Algorithm SHA256).Hash -ine $ffHash){
    throw 'Protected 222/228 original OAT-good Xbox diagnostic FF SHA changed during read-only source trace.'
}
$reportBody=[ordered]@{
    schema='WaW360.AztecFlameOriginalPcOatLoadedTypedPointers.v2'
    status='AZTEC_ORIGINAL_PC_FLAME_TYPED_MATERIAL_NAME_SOUND_SOURCE_RESOLVED_PARTIAL_NO_XBOX_ASSETS'
    originalPcSourceFastfile=$pc
    originalPcSourceSha256=[string]$j.originalPcFastfileSha256
    originalOatGoodXboxDiagnosticFfSha256=$ffHash
    realOriginalPcFlameParents=4
    packedSourceMaterialReferences=17
    packedSourceNameReferences=3
    packedSourceSoundReferences=6
    uniqueSourcePackedPointerTargets=$byRaw.Count
    genuineSourceLoadedTargets=$loaded
    nonLoadedSourceTargets=$unloaded
    rawPreviouslyUntypedTargetsNowInspected=$rawHeaderTargets
    pcMaterial112TypedNameCandidates=$typedMaterials
    sourceMaterialNamesAlsoInOatXAssetList=$oatMaterialMatches
    packedMaterialReferenceOccurrencesWithSourceAssetNameCandidate=$materialRefNameMatches
    originalPcOatMaterialAssetRootsListed=$materials.Count
    sourceSndAliasCustomNamesRead=$typedAlias
    sourceSoundAliasNamesAlsoInOatXAssetList=$oatSoundMatches
    packedSoundReferenceOccurrencesWithSourceAssetNameCandidate=$soundRefNameMatches
    originalPcOatSoundAssetRootsListed=$sounds.Count
    directlyReadActualPackedFlameNameReferenceOccurrences=$packedNamesDirect
    genuineOriginalPcNullParentSlotsPreserved=122
    genuineOriginalPcNullMaterialSlotsPreserved=12
    genuineOriginalPcNullSoundSlotsPreserved=4
    nativeXenonImageTechniqueMaterialAssetGraphsValidated=0
    realAudioXmaEncodedPayloadsProven=0
    realAudioAliasToXmaBindingsProven=0
    OatSourceFileReadOnly=$true
    originalOatGoodFfUnmodified=$true
    realPcAssetsOnlyNoFillerOrSilentAudio=$true
    xboxHardwareReady=$false
    sourceLoaderTraceLog=$log
    typedSourceTargets=@($results.ToArray())
}
$reportBody|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $report -Encoding UTF8
Write-Host '[AZTEC-FLAME-TYPED-PROGRESS] [################----] 80% | all 13 unique PC source pointer targets typed and reported'
Write-Host "[AZTEC-FLAME-TYPED] Loaded source targets=$loaded/$($byRaw.Count); original packed FlameTable name refs directly read=$packedNamesDirect/3"
Write-Host "[AZTEC-FLAME-TYPED] Genuine PC loaded Material112 names=$typedMaterials; OAT-listed roots=$oatMaterialMatches; material ref occurrences with OAT-name identity=$materialRefNameMatches/17"
Write-Host "[AZTEC-FLAME-TYPED] SndAliasCustom name pointers readable=$typedAlias; OAT-listed sound roots=$oatSoundMatches; sound ref occurrences with OAT-name identity=$soundRefNameMatches/6"
Write-Host '[AZTEC-FLAME-TYPED] Source material names, technique/image pointers and sound aliases ARE NOT native Xbox shaders/images or XMA samples. No filler, no FF.'
Write-Host "[AZTEC-FLAME-TYPED] STATUS=$($reportBody.status)"
Write-Host "[AZTEC-FLAME-TYPED] JSON=$report"
Write-Host '[AZTEC-FLAME-TYPED-PROGRESS] [####################] 100% | one OAT build, typed original-PC source proof, native FF preserved'
