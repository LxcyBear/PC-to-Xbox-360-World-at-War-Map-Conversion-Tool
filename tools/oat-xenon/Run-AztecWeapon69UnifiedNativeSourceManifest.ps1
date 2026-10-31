[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Read-only join of 57 existing exact source-named Xbox WeaponDef blobs,
# 6 newly DIRECT original-PC Virtual-pointer proven names, 2 conflicting
# same-name PC bodies, 4 offline-retail-only dependencies. NEVER emits FF.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$three=Join-Path $base 'pc_g11_lightning_three_class_native_root_resolution\aztec_three_class_native_root_body_resolution.json'
$direct=Join-Path $base 'pc_g11_lightning_six_weapon_direct_pointer_closure\aztec_six_weapon_direct_pointer_body_closure.json'
$stock=Join-Path $base 'pc_g11_lightning_conflict_stock_provenance\aztec_43_conflicts_24_missing_stock_name_provenance.json'
$size=Join-Path $base 'pc_g11_native_ff_size_audit\aztec_native_ff_size_and_source_ledger.json'
$out=Join-Path $base 'pc_g11_lightning_weapon_69_unified_native_source'
$json=Join-Path $out 'aztec_69_weapon_unified_original_pc_xbox_body_selections.json'
$md=Join-Path $out 'aztec_69_weapon_unified_original_pc_xbox_body_selections.md'
foreach($f in @($three,$direct,$stock,$size)){
    if(-not(Test-Path -LiteralPath $f -PathType Leaf)){throw "Weapon69 exact source evidence missing: $f"}
}
$t=Get-Content -LiteralPath $three -Raw|ConvertFrom-Json
$d=Get-Content -LiteralPath $direct -Raw|ConvertFrom-Json
$p=Get-Content -LiteralPath $stock -Raw|ConvertFrom-Json
$s=Get-Content -LiteralPath $size -Raw|ConvertFrom-Json
if([string]$t.schema -ne 'WaW360.AztecThreeOwnedClassNativeRootSelection.v1' -or
   [string]$t.status -ne 'AZTEC_MODELS_FX_WEAPONS_EXACT_BODY_HASH_SELECTION_AND_GAPS_PASS_NOT_GRAFTED' -or
   [int]$t.nativeThreeClassRoots -ne 586 -or
   [int]$t.sourceAuthenticXboxBlobRecords -ne 623 -or
   [string]$d.schema -ne 'WaW360.AztecSixWeaponDirectPointerSourceBodyClosure.v1' -or
   [string]$d.status -ne 'AZTEC_SIX_REAL_PC_NAME_DIRECT_POINTER_AND_VERIFIED_XBOX_OWNED_BODIES_PROVEN_NOT_IN_FF' -or
   [int]$d.originalPcSourceOwnedXboxWeaponDefBodiesReadback -ne 71 -or
   [int]$d.pcPackedWeaponNamesDirectLoadedPointerProven -ne 10 -or
   [int]$d.sixNativeWeaponIdentityMatchedExactDirectPointer -ne 6 -or
   [bool]$d.readyForXboxHardware -or
   [string]$p.schema -ne 'WaW360.AztecAsset43Conflict24MissingCachedStockProvenance.v1' -or
   [int]$p.stockCacheAcceptedZones -ne 67 -or
   [int]$p.offlineStockBodyNameMatchesForMissing -ne 18 -or
   [int]$p.threeTypeConflicts -ne 31 -or
   [int]$p.xanimConflicts -ne 12 -or
   [string]$s.schema -ne 'WaW360.AztecNativeFfSizeAndSourceCoverage.v1' -or
   [string]$s.converted.sha256 -ine 'F74BE6EA0C402C56D9074D6B38F80F2D6D71A5C3297EFA703714FA9C1176A0B6'){
    throw 'One or more upstream 623-source-body, DIRECT 10/10 pointer or Xbox stock provenance invariants changed.'
}
$ff=[string]$s.converted.path
if(-not(Test-Path -LiteralPath $ff -PathType Leaf) -or
  (Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash -ine [string]$s.converted.sha256){
    throw 'Original OAT-good 4,510,695-byte Aztec FF SHA changed. No source selection permitted.'
}
$old=@($t.exactNativeRootSelection|Where-Object {[string]$_.category -eq 'weapon'})
$oldS=@($old|Where-Object {[string]$_.decision -eq 'SINGLE_NATIVE_SOURCE_BLOB'})
$oldC=@($old|Where-Object {[string]$_.decision -eq 'DUPLICATE_BODY_SHA_CONFLICT_NO_SELECTION'})
$oldM=@($old|Where-Object {[string]$_.decision -eq 'NO_EXACT_NAME_TYPED_SOURCE_BODY'})
if($old.Count -ne 69 -or $oldS.Count -ne 57 -or $oldC.Count -ne 2 -or $oldM.Count -ne 10 -or
    @($old|Select-Object -ExpandProperty nativeRootOrdinal -Unique).Count -ne 69){
    throw "Weapon-native-root baseline changed: total=$($old.Count), single=$($oldS.Count), conflicting=$($oldC.Count), stock/missing=$($oldM.Count)."
}
$directNames=@('zombie_perk_bottle_doubletap','zombie_perk_bottle_jugg',
 'zombie_perk_bottle_revive','zombie_perk_bottle_sleight',
 'm7_launcher_zombie','m7_launcher_zombie_upgraded')
$stockNames=@('molotov','ray_gun','kar98k','30cal')
$conflictNames=@('fraggrenade','mine_bouncing_betty')
$directRows=@($d.sixProvenNativeNameToSourceXboxBodies)
if($directRows.Count -ne 6 -or
   @($directRows|Select-Object -ExpandProperty resolvedName -Unique).Count -ne 6){
    throw 'Six unique direct pointer source-name bodies disappeared.'
}
foreach($name in $directNames){
    if(@($directRows|Where-Object {[string]$_.resolvedName -ieq $name}).Count -ne 1 -or
       @($oldM|Where-Object {[string]$_.nativeRootName -ieq $name}).Count -ne 1){
        throw "Direct source identity does not match one previously missing native root: $name"
    }
}
foreach($name in $stockNames){
    if(@($oldM|Where-Object {[string]$_.nativeRootName -ieq $name}).Count -ne 1){
        throw "Expected remaining off-line stock dependent root absent: $name"
    }
}
foreach($name in $conflictNames){
    if(@($oldC|Where-Object {[string]$_.nativeRootName -ieq $name}).Count -ne 1){
        throw "Expected unresolvable PC WeaponDef duplicate conflict absent: $name"
    }
}
Write-Host '[AZTEC-WEAPON69] ========================================================'
Write-Host '[AZTEC-WEAPON69] Native WeaponDef 69: 57 exact + 6 direct PC pointer bodies + 2 conflicts + 4 stock'
Write-Host '[AZTEC-WEAPON69] All data original PC / readback Xbox proof; no retail donor bytes'
Write-Host '[AZTEC-WEAPON69-PROGRESS] [####----------------] 20% | upstream proofs and protected Xbox FF SHA locked'

function Read-Be32([IO.BinaryReader]$reader){
    $a=$reader.ReadBytes(4)
    if($a.Length -ne 4){throw 'Truncated native Xbox WeaponDef proof u32'}
    return [uint32](([uint64]$a[0]*16777216)+([uint64]$a[1]*65536)+([uint64]$a[2]*256)+[uint64]$a[3])
}
# Independently read Xbox proof records, not just JSON SHA text.
function Index-WeaponProof([string]$file){
    $stream=[IO.File]::OpenRead($file)
    $reader=[IO.BinaryReader]::new($stream,[Text.Encoding]::UTF8,$true)
    try{
        $magic=[Text.Encoding]::ASCII.GetString($reader.ReadBytes(8)).TrimEnd([char]0)
        $version=Read-Be32 $reader
        $count=Read-Be32 $reader
        $fixed=Read-Be32 $reader
        if($magic -ne 'W360A7W' -or $version -ne 8 -or $fixed -ne 0x9AC -or
           $count -lt 1 -or $count -gt 1000){
            throw "Unsupported Xbox WeaponDef full owned 7b proof header: $file"
        }
        $sha=[Security.Cryptography.SHA256]::Create()
        try{
            $records=New-Object 'System.Collections.Generic.List[object]'
            for($i=0;$i -lt [int]$count;$i++){
                $source=Read-Be32 $reader
                $bytes=Read-Be32 $reader
                $nameLen=Read-Be32 $reader
                if($bytes -lt 0x9AC -or $bytes -gt 67108864 -or $nameLen -gt 2048 -or
                    $stream.Position+[int64]$nameLen+[int64]$bytes -gt $stream.Length){
                    throw "WeaponDef proof has invalid size/bounds at record $i : $file"
                }
                $name=[Text.Encoding]::UTF8.GetString($reader.ReadBytes([int]$nameLen))
                $blobOffset=[long]$stream.Position
                $blob=$reader.ReadBytes([int]$bytes)
                if($blob.Length -ne $bytes){throw "WeaponDef proof body truncated at $i"}
                $hash=([BitConverter]::ToString($sha.ComputeHash($blob))).Replace('-','')
                $records.Add([pscustomobject]@{
                    record=$i;sourceOffset=[long]$source;name=$name
                    blobOffset=$blobOffset;blobBytes=[int]$bytes;blobSha256=$hash
                })
            }
            if($stream.Position -ne $stream.Length){throw "Unexpected trailing WeaponDef proof bytes: $file"}
            return ,@($records.ToArray())
        }finally{$sha.Dispose()}
    }finally{$reader.Dispose();$stream.Dispose()}
}
$cache=@{}
function Get-ProofRecords([string]$file,[string]$expectedSha){
    $file=[IO.Path]::GetFullPath($file)
    if(-not(Test-Path -LiteralPath $file -PathType Leaf) -or
       (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine $expectedSha){
        throw "Native 7b WeaponDef proof container SHA changed: $file"
    }
    if(-not $cache.ContainsKey($file)){$cache[$file]=@(Index-WeaponProof $file)}
    return ,@($cache[$file])
}
$rows=New-Object 'System.Collections.Generic.List[object]'
$selected=0;$uncompressed=0L
foreach($root in ($old|Sort-Object nativeRootOrdinal)){
    $name=[string]$root.nativeRootName
    $base=[ordered]@{
        nativeRootOrdinal=[int]$root.nativeRootOrdinal
        nativeRootName=$name
        originalThreeClassDecision=[string]$root.decision
        conclusion=''
        proofFile=$null
        proofContainerSha256=$null
        proofRecordOrdinal=$null
        xboxBlobByteOffset=$null
        xboxBlobBytes=$null
        xboxBlobSha256=$null
        originalPcSourceFile=$null
        originalPcSourceSha256=$null
        originalPcRole=$null
        originalPcBodyOffset=$null
        originalPcRawNamePointer=$null
        nativeMapFfPayloadInserted=$false
        originalPcWeaponDependenciesLoadedAtRuntime=$false
        manualSourceOverrideChosen=$false
    }
    if($root.decision -eq 'SINGLE_NATIVE_SOURCE_BLOB'){
        $file=[string]$root.selectedProofFile
        $sha=[string]$root.selectedProofSha256
        $ordinal=[int]$root.selectedProofOrdinal
        $records=Get-ProofRecords $file $sha
        if($ordinal -lt 0 -or $ordinal -ge $records.Count){throw "Existing weapon proof ordinal invalid: $name"}
        $record=$records[$ordinal]
        if([string]$record.name -ine $name -or
           [string]$record.blobSha256 -ine [string]$root.selectedBlobSha256 -or
           [long]$record.blobOffset -ne [long]$root.selectedBlobOffset -or
           [long]$record.blobBytes -ne [long]$root.selectedBlobLength){
            throw "Original 57 exact WeaponDef root/body SHA or offset mismatch: $name"
        }
        $base.conclusion='EXACT_ORIGINAL_PC_NAME_AND_XBOX_OWNED_BODY_SHA_VERIFIED'
        $base.proofFile=$file;$base.proofContainerSha256=$sha
        $base.proofRecordOrdinal=$ordinal
        $base.xboxBlobByteOffset=$record.blobOffset;$base.xboxBlobBytes=$record.blobBytes
        $base.xboxBlobSha256=$record.blobSha256
        $selected++;$uncompressed+=[long]$record.blobBytes
    }elseif($name -in $directNames){
        $w=@($directRows|Where-Object {[string]$_.resolvedName -ieq $name})[0]
        $file=[string]$w.xbox7bProofFile
        $sha=[string]$w.xbox7bProofSha256
        $ordinal=[int]$w.xbox7bRecordOrdinal
        $records=Get-ProofRecords $file $sha
        if($ordinal -lt 0 -or $ordinal -ge $records.Count){throw "Direct WeaponDef record ordinal out of bounds: $name"}
        $record=$records[$ordinal]
        $pointer=[Convert]::ToUInt64([string]$w.originalPackedPointer,16)
        $synthetic=('<packed-name@0x{0:X}:0x{1:X8}>' -f
                   [long]$w.originalPcWeaponDefBodyOffset,$pointer)
        if([string]$record.name -ine $synthetic -or
           [long]$record.sourceOffset -ne [long]$w.originalPcWeaponDefBodyOffset -or
           [int]$record.blobBytes -ne [int]$w.realXboxOwnedWeaponBlobBytes){
            throw "Original PC DIRECT pointer name and readback Xbox proof record do not agree: $name"
        }
        if(-not(Test-Path -LiteralPath ([string]$w.sourcePcFastFile) -PathType Leaf) -or
           (Get-FileHash -LiteralPath ([string]$w.sourcePcFastFile) -Algorithm SHA256).Hash -ine
               [string]$w.sourcePcSha256){
            throw "Original PC source FF SHA changed for direct WeaponDef: $name"
        }
        $base.conclusion='DIRECT_PC_LOADED_VIRTUAL_POINTER_NAME_AND_XBOX_OWNED_BODY_SHA_VERIFIED'
        $base.proofFile=$file;$base.proofContainerSha256=$sha
        $base.proofRecordOrdinal=$ordinal
        $base.xboxBlobByteOffset=$record.blobOffset;$base.xboxBlobBytes=$record.blobBytes
        $base.xboxBlobSha256=$record.blobSha256
        $base.originalPcSourceFile=[string]$w.sourcePcFastFile
        $base.originalPcSourceSha256=[string]$w.sourcePcSha256
        $base.originalPcRole=[string]$w.sourceRole
        $base.originalPcBodyOffset=[long]$record.sourceOffset
        $base.originalPcRawNamePointer=[string]$w.originalPackedPointer
        $selected++;$uncompressed+=[long]$record.blobBytes
    }elseif($name -in $conflictNames){
        if($root.decision -ne 'DUPLICATE_BODY_SHA_CONFLICT_NO_SELECTION' -or
           @($root.candidates).Count -lt 2){
            throw "Expected conflicting same-name source WeaponDef payloads changed: $name"
        }
        $base.conclusion='MULTIPLE_DIFFERENT_PC_XBOX_BODY_HASHES_QUARANTINED'
    }elseif($name -in $stockNames){
        if($root.decision -ne 'NO_EXACT_NAME_TYPED_SOURCE_BODY'){
            throw "Expected stock-dependent WeaponDef missing PC source-owned body changed: $name"
        }
        $offline=@($p.originalPcMissingBodyRoots|Where-Object {
            [string]$_.type -eq 'weapon' -and [string]$_.name -ieq $name
        })
        if($offline.Count -ne 1 -or
           [string]$offline[0].offlineRetailSourceNameClassification -ne
               'OFFLINE_OTHER_RETAIL_ZONE_OWNED_BODY_NAME_FOUND_NOT_PRELOAD_PROVEN'){
            throw "Four stock-dependent WeaponDef retail OAT type/name evidence changed: $name"
        }
        $base.conclusion='RETAIL_ZONE_NAME_ONLY_RUNTIME_DEPENDENCY_UNVERIFIED'
    }else{
        throw "Native Xbox WeaponDef has no approved current source verdict: $name"
    }
    $rows.Add([pscustomobject]$base)
}
if($rows.Count -ne 69 -or $selected -ne 63 -or
   @($rows|Where-Object {$_.conclusion -eq 'MULTIPLE_DIFFERENT_PC_XBOX_BODY_HASHES_QUARANTINED'}).Count -ne 2 -or
   @($rows|Where-Object {$_.conclusion -eq 'RETAIL_ZONE_NAME_ONLY_RUNTIME_DEPENDENCY_UNVERIFIED'}).Count -ne 4){
    throw "69-root source selection coverage not 63/2/4; selected=$selected."
}
Write-Host '[AZTEC-WEAPON69-PROGRESS] [##############------] 70% | independently SHA-hash verified 63 genuine Xbox payloads'
New-Item -ItemType Directory -Force -Path $out|Out-Null
$report=[ordered]@{
    schema='WaW360.AztecWeapon69UnifiedOriginalPcXboxBodySelection.v1'
    status='AZTEC_69_WEAPON_NATIVE_ROOTS_63_AUTHENTIC_PC_XBOX_BODIES_2_CONFLICT_4_STOCK_UNPROVEN_NOT_IN_FF'
    nativeWeaponDefRoots=69
    priorExactNamedOwnedSourceBodies=57
    directlyRecoveredPackedSourceOwnedNames=6
    uniqueSourceOwnedXboxWeaponBodyMappings=63
    selectedUncompressedXboxOwnedPayloadBytes=$uncompressed
    conflictingDifferentPcSourceBodiesStillFenced=2
    offlineStockOnlyWeaponNamesNotRuntimeProven=4
    allRealOriginalPcSourceOwnedXboxBodyBlobShasRevalidated=$true
    nativeWeaponRootSourceDecisions=@($rows.ToArray())
    untouchedXModelSourceConflicts=21
    untouchedFxSourceConflicts=8
    untouchedXAnimSourceConflicts=12
    materialAndLocalizationNativeDependenciesStillUnresolved=$true
    noNativeFastfileBuiltOrModified=$true
    noRetailOwnedBodyBytesCopied=$true
    noOatOrXboxRun=$true
    xboxRuntimeReady=$false
    gate11Closed=$false
    note='63 original-PC-source Xbox 7b WeaponDef owned blobs have independently verified source name/hash evidence. In two source conflicts neither map.ff nor mod.ff is arbitrarily selected, and four name-only offline retail listings cannot prove stock residency. Genuine source blobs are NOT written to Aztec XAsset graph and do not make map Xbox playable.'
}
$report|ConvertTo-Json -Depth 14|Set-Content -LiteralPath $json -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add('# Aztec: all 69 native WeaponDef roots, source-accurate Xbox owned-body manifest')
$lines.Add('')
$lines.Add("**SOURCE / STANDALONE Xbox proof only.** 63/69 exact PC-source weapon owned payload identities and SHA-256 verified. 2 source body conflicts and 4 offline retail-only names remain.")
$lines.Add("Selected standalone native Xbox WeaponDef payload bytes (UNCOMPRESSED, not FF file growth): $uncompressed")
$lines.Add('')
$lines.Add('| Native root | Weapon name | Source evidence status | Xbox owned payload bytes |')
$lines.Add('|---:|---|---|---:|')
foreach($row in $rows){
    $lines.Add("| $($row.nativeRootOrdinal) | $($row.nativeRootName) | $($row.conclusion) | $($row.xboxBlobBytes) |")
}
$lines.Add('')
$lines.Add('Two unresolved conflicting original-PC WeaponDef versions: fraggrenade, mine_bouncing_betty. Four offline OTHER-retail-zone type/name-only bodies (runtime residency NOT proven): molotov, ray_gun, kar98k, 30cal.')
$lines.Add('No FF payload graft, no OAT, no Xbox test; native 8509-root FF, Material/FX/XModel/XAnim/localize/image/audio/world child closures unchanged.')
[IO.File]::WriteAllLines($md,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-WEAPON69-PROGRESS] [####################] 100% | all 69 native WeaponDef roots classified'
Write-Host "[AZTEC-WEAPON69] Real original-PC source named Xbox 7b bodies=$selected/69; total isolated bytes=$uncompressed"
Write-Host '[AZTEC-WEAPON69] Differing PC version conflicts=2 (fraggrenade, mine_bouncing_betty). No winner chosen.'
Write-Host '[AZTEC-WEAPON69] Stock name-only offline=4 (molotov, ray_gun, kar98k, 30cal). No runtime residency claim.'
Write-Host "[AZTEC-WEAPON69] STATUS=$($report.status)"
Write-Host "[AZTEC-WEAPON69] JSON: $json"
Write-Host "[AZTEC-WEAPON69] Markdown: $md"
Write-Host '[AZTEC-WEAPON69] Current protected OAT-passing FF unchanged at 4,510,695 bytes. NO Xbox.'
