[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Read-only Gate11 frontier, NOT a serializer and NOT a gate approval.
# Compares source 100-byte PC DPVS pointer slots with source-owned
# GfxWorld children physically proven in the SHA-locked Gate10 FF.
# Pointer-slot layout: pinned T4 32-bit DPVS struct (100 bytes);
# runtime-marked fields must not be blindly converted into FF payloads.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$seedPath=Join-Path $base 'pc_source_to_xenon_gfxworld_root_dpvs_scalar_seed\aztec_pc_gfxworld_800_to_xenon_900_root_dpvs_seed.json'
$abiPath=Join-Path $base 'xenon_native_gfxworld_root_dpvs_abi\native_xenon_gfxworld_root_dpvs_abi.json'
$ffGatePath=Join-Path $base 'pc_g10_1903\aztec_pc_world_core_integrated_oat_run.json'
$readbackPath=Join-Path $base 'pc_g10_1903\oat_loaded_1903_draw_readback\aztec_gate10_independent_1903_draw_readback.json'
$outdir=Join-Path $base 'pc_g10_1903\gate11_dpvs_child_frontier'
$report=Join-Path $outdir 'aztec_gate11_source_dpvs_100b_child_frontier.json'
foreach($path in @($seedPath,$abiPath,$ffGatePath,$readbackPath)){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Missing previously proven source/native Gate10 evidence: $path"
    }
}
$seed=Get-Content -LiteralPath $seedPath -Raw|ConvertFrom-Json
$abi=Get-Content -LiteralPath $abiPath -Raw|ConvertFrom-Json
$ffGate=Get-Content -LiteralPath $ffGatePath -Raw|ConvertFrom-Json
$read=Get-Content -LiteralPath $readbackPath -Raw|ConvertFrom-Json
$pc=$abi.originalPcLoadedWorldChildShape
$native=$abi.nativeGfxWorldRootAbi
if([string]$seed.Status -ne 'AZTEC_PC_GFXWORLD_800_TO_XBOX_900_ROOT_SCALAR_AND_DPVS_SEED_PASS_NOT_FF' -or
   [string]$abi.status -ne 'AZTEC_NATIVE_XBOX_GFXWORLD_ROOT_AND_DPVS_ABI_EXACT_SOURCE_WITNESS' -or
   [int]$abi.originalPcGfxWorldRootAbi.dpvsStaticSize -ne 100 -or
   [int]$native.dpvsStaticSize -ne 100 -or
   [int]$native.dpvsStaticAt -ne 704 -or
   [int]$pc.smodelCount -ne 1903 -or
   [int]$pc.staticSurfaceCount -ne 613 -or
   [int]$pc.surfaceVisDataCount -ne 20 -or
   [int]$pc.smodelVisDataCount -ne 60 -or
   [int]$pc.cellCount -ne 11 -or
   [string]$ffGate.status -ne 'AZTEC_GATE10_1903_STATIC_DRAW_DIAGNOSTIC_OAT_PASS_STOCK_UNVERIFIED' -or
   -not [bool]$ffGate.OatPass -or
   [int]$ffGate.gate10DrawRecordsInEmittedFf -ne 1903 -or
   [string]$read.status -ne 'AZTEC_GATE10_1903_ALL_SOURCE_DRAW_FIELDS_OAT_READBACK_PASS_STOCK_UNVERIFIED' -or
   [int]$read.actualNativeDrawCount -ne 1903 -or
   [bool]$read.XboxHardwareReady){
    throw 'Gate11 cannot use unverified source/native layout or older pre-draw OAT FF.'
}
$pcDpvsPath=[string]$seed.OriginalPcDpvs100File
$ffPath=[string]$ffGate.outputFastFile
$witnessPath=[string]$read.independentOatLoadedWitness
foreach($path in @($pcDpvsPath,$ffPath,$witnessPath)){
    if([string]::IsNullOrWhiteSpace($path) -or
       -not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Missing source DPVS or SHA-verified emitted-FF/OAT witness: $path"
    }
}
$dpvs=[IO.File]::ReadAllBytes($pcDpvsPath)
$dpvsSha=(Get-FileHash -LiteralPath $pcDpvsPath -Algorithm SHA256).Hash
$ffSha=(Get-FileHash -LiteralPath $ffPath -Algorithm SHA256).Hash
$witnessSha=(Get-FileHash -LiteralPath $witnessPath -Algorithm SHA256).Hash
if($dpvs.Length -ne 100 -or
   $dpvsSha -ine [string]$seed.OriginalPcDpvsSha256 -or
   $ffSha -ine [string]$read.stage4EEmittedFfSha256 -or
   $witnessSha -ine [string]$read.independentOatLoadedWitnessSha256 -or
   [IO.Path]::GetFullPath($ffPath) -ine [IO.Path]::GetFullPath([string]$read.stage4EEmittedFf)){
    throw 'Source DPVS, independent loaded-witness or FF byte provenance drifted.'
}
function Le32([byte[]]$bytes,[int]$at){
    return [BitConverter]::ToUInt32($bytes,$at)
}
if((Le32 $dpvs 0) -ne 1903 -or
   (Le32 $dpvs 4) -ne 613 -or
   (Le32 $dpvs 32) -ne 60 -or
   (Le32 $dpvs 36) -ne 20){
    throw 'Original PC DPVS count prefix disagrees with independently loaded OAT shape.'
}
# T4 100B DPVS 32-bit pointer layout. Runtime allocations are a
# separate concern; do not assume raw PC pointer words are portable.
$slots=@(
    @{name='smodelVisData[0]';offset=40;kind='runtime';count='smodelCount'},
    @{name='smodelVisData[1]';offset=44;kind='runtime';count='smodelCount'},
    @{name='smodelVisData[2]';offset=48;kind='runtime';count='smodelCount'},
    @{name='surfaceVisData[0]';offset=52;kind='runtime';count='staticSurfaceCount'},
    @{name='surfaceVisData[1]';offset=56;kind='runtime';count='staticSurfaceCount'},
    @{name='surfaceVisData[2]';offset=60;kind='runtime';count='staticSurfaceCount'},
    @{name='lodData';offset=64;kind='runtime';count='2*smodelVisDataCount'},
    @{name='sortedSurfIndex';offset=68;kind='serialized';count='staticSurfaceCount'},
    @{name='smodelInsts';offset=72;kind='serialized';count='smodelCount'},
    @{name='surfaces';offset=76;kind='serialized';count='world.surfaceCount'},
    @{name='cullGroups';offset=80;kind='serialized';count='world.cullGroupCount (not typed-exported yet)'},
    @{name='smodelDrawInsts';offset=84;kind='serialized';count='smodelCount'},
    @{name='surfaceMaterials';offset=88;kind='runtime';count='staticSurfaceCount'},
    @{name='surfaceCastsSunShadow';offset=92;kind='runtime';count='surfaceVisDataCount'}
)
$physicallyVerified=@('sortedSurfIndex','smodelInsts','surfaces','smodelDrawInsts')
$records=New-Object 'System.Collections.Generic.List[object]'
$sourcePresentUnlinked=New-Object 'System.Collections.Generic.List[string]'
$runtimeCount=0
foreach($slot in $slots){
    $offset=[int]$slot.offset
    $word=Le32 $dpvs $offset
    $present=$word -ne [uint32]0
    $verified=([string]$slot.name -in $physicallyVerified)
    if($verified -and -not $present){
        throw "Gate10 previously loaded nonempty serialized child '$($slot.name)' but original PC DPVS source pointer at +$offset is zero."
    }
    if([string]$slot.kind -eq 'runtime'){$runtimeCount++}
    if([string]$slot.kind -eq 'serialized' -and $present -and -not $verified){
        $sourcePresentUnlinked.Add([string]$slot.name)
    }
    $records.Add([ordered]@{
        field=[string]$slot.name
        sourceDpvsOffset=$offset
        sourceLittleEndianWord=('0x{0:X8}' -f $word)
        sourceWordNonzero=$present
        oAtT4BlockClassification=[string]$slot.kind
        structuralCountExpression=[string]$slot.count
        convertedFfTypedOatChildPreviouslyProven=$verified
        note=if($verified){'Previously independently OAT-loaded as original-PC source child'}
             elseif([string]$slot.kind -eq 'runtime'){'Requires separate runtime allocation semantics; not a blind static FF copy'}
             elseif($present){'Missing typed source records, current native allocation plan and independent OAT child readback'}
             else{'PC source pointer NULL; prove zero count/condition independently before considering omitted'}
    })
}
$next=if($sourcePresentUnlinked.Contains('cullGroups')){
    'cullGroups: prove original-PC world.cullGroupCount, child typed ABI and records before Stage4D/4E emission'
}else{
    '11 GfxCell entries: prove cell/portal/AABB child graph, plus lightmap/light-grid/reflection and other non-DPVS GfxWorld dependencies'
}
New-Item -ItemType Directory -Path $outdir -Force|Out-Null
$summary=[ordered]@{
    schema='WaW360.AztecGate11SourceDpvsChildFrontier.v1'
    status='AZTEC_GATE11_SOURCE_DPVS_CHILD_CENSUS_ONLY_FULL_WORLD_NOT_YET_PROVEN'
    originalPcDpvs100Path=$pcDpvsPath
    originalPcDpvs100Sha256=$dpvsSha
    diagnosticFf=$ffPath
    diagnosticFfSha256=$ffSha
    independentLoadedDrawWitnessSha256=$witnessSha
    sourceSmodelCount=1903
    sourceStaticSurfaceCount=613
    sourceCellCount=11
    sourceSmodelVisDataCount=60
    sourceSurfaceVisDataCount=20
    alreadyProvenSerializedStaticChildren=@($physicallyVerified)
    runtimeManagedPointerSlotCount=$runtimeCount
    sourcePresentUnlinkedSerializedChildren=@($sourcePresentUnlinked.ToArray())
    priorityNextChild=$next
    dpvsPointerSlots=@($records.ToArray())
    sourceXboxReferencePayloadBytesCopied=$false
    originalPcPointerValuesCopiedIntoConvertedFf=$false
    gate11Passed=$false
    hardwareReady=$false
}|ConvertTo-Json -Depth 8
$summary|Set-Content -LiteralPath $report -Encoding UTF8
Write-Host '[AZTEC-GATE11] ======================================================'
Write-Host '[AZTEC-GATE11] Source DPVS 100B and live Gate10 FF/OAT witness SHA VERIFIED'
Write-Host '[AZTEC-GATE11] Previously linked: sortedSurfIndex + smodelInsts + surfaces + 1903 smodelDrawInsts'
Write-Host '[AZTEC-GATE11] The 7 initial DPVS visibility/lod pointer slots and trailing 2 slots are RUNTIME-marked; NOT blindly serializable.'
foreach($slot in @($records.ToArray())){
    $state=if($slot.convertedFfTypedOatChildPreviouslyProven){'PHYSICALLY VERIFIED'}
        elseif($slot.oAtT4BlockClassification -eq 'runtime'){'RUNTIME / UNPROVEN'}
        elseif($slot.sourceWordNonzero){'NEXT SOURCE CHILD'}
        else{'SOURCE NULL'}
    Write-Host ('[AZTEC-GATE11] +{0,2} {1,-23} {2,-20} source={3}' -f $slot.sourceDpvsOffset,$slot.field,$state,$slot.sourceLittleEndianWord)
}
Write-Host "[AZTEC-GATE11] Next frontier: $next"
Write-Host "[AZTEC-GATE11] Report=$report"
Write-Host '[AZTEC-GATE11] No OAT, .NET, FF build, native asset graft or Xbox testing. Gate11 PENDING; 10/13 preserved.'
