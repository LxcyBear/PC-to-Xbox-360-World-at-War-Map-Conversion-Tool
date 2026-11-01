[CmdletBinding()]
param(
    [switch]$BuildDumper,
    [switch]$SkipXboxXmaExtract,
    [int]$MaxXboxRetailZones=2,
    [string]$XboxSoundPackRoot=''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$capFile=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$dumper=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$v2=Join-Path $base 'pc_g11_flame_loaded_virtual_target_proof\aztec_26_flame_packed_original_pc_typed_material_alias_probe_v2.json'
$stock=Join-Path $base 'pc_g11_flame_loaded_virtual_target_proof\aztec_flame_sound_alias_real_xbox_retail_stock_names.json'
$work=Join-Path $base 'pc_g11_flame_loaded_virtual_target_proof'
$output=Join-Path $work 'aztec_five_original_pc_material_pointer_cells_and_real_xbox_xma_sprint.json'
$cap='t4-pc-flame-real-material-pointer-slot-hop-v3'
Write-Host '[AZTEC-FLAME-XMA-V3] ====================================================='
Write-Host '[AZTEC-FLAME-XMA-V3] ORIGINAL PC 5 MATERIAL TARGETS / 17 REFS + REAL XBOX STOCK XMA'
Write-Host '[AZTEC-FLAME-XMA-V3] 3/3 Flame names & 6/6 PC SndAlias and 67 Xbox retail root names already proven.'
Write-Host '[AZTEC-FLAME-XMA-V3] NO donor FF, NO synthesized materials, NO silent audio, NO native Aztec FF changes.'
$installed=$false
if((Test-Path -LiteralPath $dumper -PathType Leaf) -and
   (Test-Path -LiteralPath $capFile -PathType Leaf)){
    $c=Get-Content -LiteralPath $capFile -Raw|ConvertFrom-Json
    $installed=([string]$c.format -eq 'WaW360.OatGraphDumperCapabilities.v1' -and
        [string]$c.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($c.capabilities) -contains $cap)
}
if(-not $installed -and $BuildDumper){
    Write-Host '[AZTEC-FLAME-XMA-V3] ONE new pinned x86 OAT build for bounded material pointer-cell hops.'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1') -TraceWeaponNamePointers
    if($LASTEXITCODE -ne 0){throw 'Pinned OAT original PC material pointer-cell v3 x86 build failed; source-only.'}
    $c=Get-Content -LiteralPath $capFile -Raw|ConvertFrom-Json
    $installed=([string]$c.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($c.capabilities) -contains $cap)
}
if(-not $installed){throw 'OAT material pointer slot v3 capability absent. Run this script with -BuildDumper once.'}
Write-Host '[AZTEC-FLAME-XMA-V3-PROGRESS] [####----------------] 20% | pinned OAT x86 indirection witness installed'
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Run-AztecFlamePackedPointerLoaderProbe.ps1')
if($LASTEXITCODE -ne 0){throw 'Original PC 13-target v2 loaded source proof or Xbox 67-zone name crosswalk blocked.'}
if(-not(Test-Path -LiteralPath $v2 -PathType Leaf) -or
   -not(Test-Path -LiteralPath $stock -PathType Leaf)){
    throw 'One-source-loader v2 proof and 67-zone Xbox original retail sound crosswalk missing.'
}
Write-Host '[AZTEC-FLAME-XMA-V3-PROGRESS] [########------------] 40% | original PC same-zone 13 targets/26 refs verified + six genuine Sound names'
$v=Get-Content -LiteralPath $v2 -Raw|ConvertFrom-Json
$stockProof=Get-Content -LiteralPath $stock -Raw|ConvertFrom-Json
if([string]$v.schema -ne 'WaW360.AztecFlameOriginalPcOatLoadedTypedPointers.v2' -or
   [int]$v.genuineSourceLoadedTargets -ne 13 -or
   [int]$v.uniqueSourcePackedPointerTargets -ne 13 -or
   [int]$v.packedSourceMaterialReferences -ne 17 -or
   [int]$v.packedSourceNameReferences -ne 3 -or
   [int]$v.packedSourceSoundReferences -ne 6 -or
   [int]$v.directlyReadActualPackedFlameNameReferenceOccurrences -ne 3 -or
   [int]$v.sourceSndAliasCustomNamesRead -ne 6 -or
   [int]$v.sourceSoundAliasNamesAlsoInOatXAssetList -ne 6 -or
   [int]$stockProof.realOriginalPcSoundAliasNames -ne 6 -or
   [int]$stockProof.uniqueSourceSoundNamesWithOfflineXboxOwnedNameCandidate -ne 6 -or
   [int]$stockProof.offlineOriginalXboxRetailZonesAccepted -ne 67 -or
   [int]$stockProof.offlineOriginalXboxRetailZonesStaleOrInvalid -ne 0 -or
   [bool]$v.xboxHardwareReady){
    throw 'Original PC / Xbox retail source witness changed; refusing false material or XMA promotion.'
}
$sourceLog=[string]$v.sourceLoaderTraceLog
if(-not(Test-Path -LiteralPath $sourceLog -PathType Leaf)){
    throw "Actual OAT v3 loaded source memory log missing: $sourceLog"
}
$trace=@([IO.File]::ReadAllLines($sourceLog)|Where-Object {$_ -match '^\[WAW360-FLAMEMATPTR\]'})
$rows=@($v.typedSourceTargets)
if($trace.Count -ne 13 -or $rows.Count -ne 13){
    throw "Expected exact 13 genuine OAT loaded material-slot type observations: rows=$($rows.Count) traces=$($trace.Count)"
}
$index=@{}
$pat='^\[WAW360-FLAMEMATPTR\] raw=([0-9A-Fa-f]{8}) depth=(-?\d+) firstU32=0x([0-9A-Fa-f]+) name=(\S+) technique=(\S+) textures=(\d+) constants=(\d+) statebits=(\d+) namedImages=(\d+) images=(\S+)$'
foreach($line in $trace){
    $m=[regex]::Match([string]$line.Trim(),$pat)
    if(-not $m.Success){throw "Malformed OAT original PC material-pointer v3 trace: $line"}
    $id=[string]$m.Groups[1].Value.ToUpperInvariant()
    if($index.ContainsKey($id)){throw "Repeated OAT material pointer-cell source address: $id"}
    $index[$id]=$m
}
$outRows=New-Object 'System.Collections.Generic.List[object]'
$visited=0;$refCount=0;$nameCandidates=0;$oatOwned=0;$direct=0;$oneHop=0;$twoHops=0
$oatMaterialNames=@([IO.File]::ReadAllLines($sourceLog)|ForEach-Object{
    $m=[regex]::Match([string]$_,'^\s*material\s*,\s*(\S(?:.*\S)?)\s*$',[Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if($m.Success){$m.Groups[1].Value.Trim()}
}|Sort-Object -Unique)
foreach($entry in $rows){
    $roles=@($entry.roles)
    if($roles -notcontains 'FlameMaterial'){continue}
    if([bool]$entry.differentAssetKindsSharePointer){
        throw 'Original PC material pointer target overlaps a different asset family; never bind automatically.'
    }
    $raw=[string]$entry.rawOriginalPcPackedPointer
    $key=$raw.Substring(2).ToUpperInvariant()
    if(-not $index.ContainsKey($key)){
        throw "Real loaded material pointer memory target not in OAT v3 source log: $raw"
    }
    $m=$index[$key]
    $depth=[int]$m.Groups[2].Value
    if($depth -lt -1 -or $depth -gt 2){throw "Unbounded material-pointer dereference attempt: $raw"}
    $firstWord=[Convert]::ToUInt64($m.Groups[3].Value,16)
    $header=[string]$entry.originalPcFirst32ByteHeaderHex
    if($header.Length -lt 8){
        throw "Original OAT byte header insufficient for independent pointer-word check: $raw"
    }
    $b0=[Convert]::ToUInt64($header.Substring(0,2),16)
    $b1=[Convert]::ToUInt64($header.Substring(2,2),16)
    $b2=[Convert]::ToUInt64($header.Substring(4,2),16)
    $b3=[Convert]::ToUInt64($header.Substring(6,2),16)
    $fromSourceBytes=$b0+($b1 -shl 8)+($b2 -shl 16)+($b3 -shl 24)
    if($fromSourceBytes -ne $firstWord){
        throw "OAT direct pointer first four source bytes changed between v2 and v3: $raw"
    }
    $name=[string]$m.Groups[4].Value
    $tech=[string]$m.Groups[5].Value
    $tex=[int]$m.Groups[6].Value
    $const=[int]$m.Groups[7].Value
    $bits=[int]$m.Groups[8].Value
    $imgCount=[int]$m.Groups[9].Value
    $imgText=[string]$m.Groups[10].Value
    $canBeMaterial=($depth -ge 0 -and $name -ne '-')
    if($canBeMaterial -and ($name -notmatch '^[A-Za-z0-9_./-]{1,96}$' -or
       $tex -gt 64 -or $const -gt 64 -or $bits -gt 64 -or
       $imgCount -gt $tex)){
        throw "Impossible original PC loaded material typed source identity: $raw / $name"
    }
    if($depth -ge 0 -and -not $canBeMaterial){
        throw "Original PC loaded material depth claims resolution but name missing: $raw"
    }
    $uses=@($entry.originalSourcePointerUses|Where-Object {$_.kind -eq 'FlameMaterial'})
    $refCount+=$uses.Count
    $visited++
    $oatMatch=($canBeMaterial -and ($oatMaterialNames -contains $name))
    if($canBeMaterial){
        $nameCandidates++
        if($depth -eq 0){$direct++}elseif($depth -eq 1){$oneHop++}else{$twoHops++}
    }
    if($oatMatch){$oatOwned++}
    Write-Host "[AZTEC-FLAME-MATERIAL-V3] $raw source-fields=$($uses.Count) indirectionDepth=$depth original-PC-real-Material-name=$name OAT-material-root=$oatMatch tech=$tech images=$imgCount/$tex"
    $outRows.Add([pscustomobject][ordered]@{
        rawOriginalPcMaterialPackedPointer=$raw
        materialReferenceOccurrences=$uses.Count
        originalSourceUses=$uses
        actualSourceHeader32Hex=$header
        directLoadedFirstFourBytes=('0x{0:X8}' -f $firstWord)
        materialHeaderProvenAtPointerDepth=$depth
        genuinePcLoadedMaterialNameCandidate=if($canBeMaterial){$name}else{$null}
        identicalMaterialNameIndependentlyListedInOatPcXAssetRoots=$oatMatch
        genuineSourceTechniqueNameCandidate=if($tech -eq '-'){$null}else{$tech}
        materialTextureCount=if($canBeMaterial){$tex}else{$null}
        materialConstantCount=if($canBeMaterial){$const}else{$null}
        materialStatebitsCount=if($canBeMaterial){$bits}else{$null}
        namedOriginalPcSourceImagesVerified=$imgCount
        actualOriginalPcImageNames=if($imgText -eq '-'){@()}else{@($imgText.Split(';'))}
        originalPcSourceFastFileSha256=[string]$v.originalPcSourceSha256
        material112HeaderAndImageReferencesAreNotNativeXboxGraph=$true
        nativeXboxMaterialDependencyClosureCompleted=$false
        changedExistingFf=$false
    })
}
if($visited -ne 5 -or $refCount -ne 17){
    throw "Expected five unique original PC FlameMaterial source pointer targets covering exactly 17 fields; found $visited/$refCount."
}
Write-Host "[AZTEC-FLAME-MATERIAL-V3] Material pointer-cells: real PC name candidates=$nameCandidates/5, direct=$direct, one-hop=$oneHop, two-hops=$twoHops, OAT source asset root exact names=$oatOwned."
$xmaStatus='SKIPPED_EXPLICITLY';$xmaReport=$null
if(-not $SkipXboxXmaExtract){
    Write-Host '[AZTEC-FLAME-XMA-V3-PROGRESS] [############--------] 60% | extracting real Xbox 360 retail XMA source samples, no transcoding'
    $a=@('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'Run-AztecFlameRealXboxXmaExtract.ps1'),'-MaxZones',[string]$MaxXboxRetailZones)
    if($XboxSoundPackRoot){$a+=@('-XboxSoundPackRoot',$XboxSoundPackRoot)}
    & powershell @a
    if($LASTEXITCODE -ne 0){
        throw 'Real original Xbox 360 retail Sound XMA source extraction blocked. V2 source and pointer-cell data remain read-only.'
    }
    $xmaReport=Join-Path $base 'pc_g11_flame_real_xbox_xma_source\aztec_six_real_original_xbox_sound_xma_source_provenance.json'
    if(-not(Test-Path -LiteralPath $xmaReport -PathType Leaf)){throw 'Xbox authentic XMA source report missing.'}
    $audio=Get-Content -LiteralPath $xmaReport -Raw|ConvertFrom-Json
    if([string]$audio.schema -ne 'WaW360.AztecGenuineXboxRetailSoundXmaSourceExtraction.v1' -or
       [int]$audio.originalPcFlameSoundAliasNames -ne 6 -or
       [bool]$audio.xboxRuntimeSoundResidencyVerified -or
       [bool]$audio.authenticXmaSampleToOriginalFlameSoundAliasBindingProven){
        throw 'Xbox original XMA extraction evidence claims incorrect native readiness.'
    }
    $xmaStatus=[string]$audio.status
}else{
    Write-Host '[AZTEC-FLAME-XMA-V3] Xbox sample extraction explicitly skipped; material-pointer source evidence still complete.'
}
$reportBody=[ordered]@{
    schema='WaW360.AztecFiveFlameMaterialSlotRealPcSourceAndXboxXmaSprint.v3'
    status='AZTEC_REAL_PC_FLAME_MATERIAL_POINTER_INDIRECTION_AUDITED_XBOX_XMA_DEPENDENT'
    originalPcSourceFastFile=[string]$v.originalPcSourceFastfile
    originalPcSourceFastFileSha256=[string]$v.originalPcSourceSha256
    originalFlameTableParents=4
    originalPcFlameNamePackedReferencesResolved=3
    originalPcFlameSoundAliasNamesMatchedExactlyInPcAndRetail=6
    originalPcFlameMaterialPackedReferences=17
    uniqueMaterialPointerTargets=5
    provenMaterial112OriginalPcNameCandidates=$nameCandidates
    materialSourceAssetRootExactNames=$oatOwned
    genuineSourceNullParentSlotsPreserved=122
    genuineSourceNullFlameMaterialSlotsPreserved=12
    genuineSourceNullFlameSoundSlotsPreserved=4
    originalNativeFFSha256=[string]$v.originalOatGoodXboxDiagnosticFfSha256
    originalOatGoodFFModified=$false
    nativeXboxMaterialGraphTechniqueImageClosureProven=$false
    xboxRealSoundAliasToXmaBindingProven=$false
    xboxRuntimeResidencyProven=$false
    sourceXmaExtractionStatus=$xmaStatus
    sourceXmaReportFile=$xmaReport
    xboxHardwareReady=$false
    noDummyOrFakeAssets=$true
    sourceMaterialTargets=@($outRows.ToArray())
}
$reportBody|ConvertTo-Json -Depth 14|Set-Content -LiteralPath $output -Encoding UTF8
Write-Host "[AZTEC-FLAME-XMA-V3] Original PC Material root names=$oatOwned/5; other material names candidates=$($nameCandidates-$oatOwned); native shader/texture graphs NOT yet grafted."
Write-Host "[AZTEC-FLAME-XMA-V3] STATUS=$($reportBody.status) Xbox-real-audio=$xmaStatus"
Write-Host "[AZTEC-FLAME-XMA-V3] JSON=$output"
Write-Host '[AZTEC-FLAME-XMA-V3-PROGRESS] [####################] 100% | exact 17 real material refs + 3 names + 6 sound names and genuine Xbox XMA sample source audit; native FF protected'
