[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Gate 8E: Native T4Xenon 60-byte GfxStaticModelDrawInst *partial*
# geometry layout bridge from the already verified 28-model/1903-draw
# source and 5709-axis numeric preflight. This NEVER produces a valid
# Xbox draw record array: model pointers, lighting, flags and padding
# are deliberately unpopulated. There is no FF mutation or Xbox release.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$sourceReportPath=Join-Path $base 'pc_gate8_static_draw_source\aztec_gate8_pc_static_draw_source_report.json'
$axisReportPath=Join-Path $base 'pc_gate8_static_draw_xenon_axis_preflight_28_model_world\aztec_1903_xenon_axis_pack_preflight.json'
$worldPath=Join-Path $base 'pc_gate8_29x_ff\aztec_pc_world_core_integrated_oat_run.json'
$header=Join-Path $repo '.deps\OpenAssetTools-xenon\src\Common\Game\T4Xenon\T4Xenon_Assets.h'
$oatRoot=Join-Path $repo '.deps\OpenAssetTools-xenon'
$dir=Join-Path $base 'pc_gate8_xenon_60b_draw_layout_bridge_28_model_world'
$binary=Join-Path $dir 'aztec_1903_PARTIAL_60b_draw_geometry_ONLY_NOT_FOR_FF.bin'
$reportPath=Join-Path $dir 'aztec_1903_xenon_60b_draw_layout_bridge.json'
foreach($p in @($sourceReportPath,$axisReportPath,$worldPath,$header)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Gate8E source or pinned Xenon header missing: $p"
    }
}
$head=(& git -C $oatRoot rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0 -or $head -cne '3beaa7c81af517f0c05f2b4612d477ed1541b676'){
    throw "Pinned original OAT revision changed ($head); Xenon ABI not verified."
}
# Read the exact pinned compiler source contract. No unverified generic
# T4/T5 layouts, no hard-coded assumption alone, no donor-FF body graft.
$h=[IO.File]::ReadAllText($header)
$packed=[regex]::Match($h,'(?s)\bstruct\s+GfxPackedPlacement\s*\{(?<body>.*?)\};')
$draw=[regex]::Match($h,'(?s)\bstruct\s+__declspec\s*\(\s*align\s*\(\s*4\s*\)\s*\)\s+GfxStaticModelDrawInst\s*\{(?<body>.*?)\};')
if(-not $packed.Success -or -not $draw.Success){
    throw 'Exact pinned T4Xenon placement/draw struct definitions unavailable.'
}
function Compact([string]$s){return (($s -replace '\s+',' ').Trim())}
$expectedPlacement='float origin[3]; PackedUnitVec axis[3]; float scale;'
$expectedDraw='float cullDist; GfxPackedPlacement placement; XModel* model; unsigned __int8 reflectionProbeIndex; unsigned __int8 primaryLightIndex; unsigned __int16 lightingHandle; unsigned __int8 flags; unsigned __int8 pad[19];'
if((Compact $packed.Groups['body'].Value) -cne $expectedPlacement -or
   (Compact $draw.Groups['body'].Value) -cne $expectedDraw){
    throw 'Pinned T4Xenon GfxStaticModelDrawInst field declaration drifted; refuse candidate serialization.'
}
# Verified layout under 4-byte x86 packing:
# cull +0 (4B); placement +4 (origin 12B, axis 12B, scale 4B);
# XModel* +32 (4B); reflection +36; primary +37;
# lightingHandle +38 (2B); flags +40 (1B); pad +41 (19B).
# 60B is a measured header-derived candidate, not a validated pointer graph.
$source=Get-Content -LiteralPath $sourceReportPath -Raw | ConvertFrom-Json
$axes=Get-Content -LiteralPath $axisReportPath -Raw | ConvertFrom-Json
$world=Get-Content -LiteralPath $worldPath -Raw | ConvertFrom-Json
if([string]$source.schema -ne 'WaW360.AztecGate8PcStaticDrawSourceProof.v1' -or
   [string]$source.status -ne 'AZTEC_GATE8_1903_PC_TYPED_STATIC_DRAWS_SOURCE_PASS' -or
   [int]$source.sourceDrawCount -ne 1903 -or
   [int]$source.verifiedSourceWorldModelBodies -ne 28 -or
   [int]$source.sourceDrawOccurrencesCoveredByRealConvertedModelTypes -ne 1902 -or
   [bool]$source.pcModelPointerBytesCopied -or
   [bool]$source.hardwareReady -or
   [string]$axes.schema -ne 'WaW360.AztecGate8XenonStaticDrawAxisPreflight.v1' -or
   [string]$axes.status -ne 'AZTEC_GATE8_1903_PC_PLACEMENT_XENON_PACKED_AXES_PREFLIGHT_PASS' -or
   [string]$axes.verifiedWorldMode -ne '28-model' -or
   [string]$axes.originalPcDrawSourceSha256 -ine [string]$source.typedDrawSha256 -or
   [int]$axes.originalPcDrawCount -ne 1903 -or
   [int]$axes.encodedT4XenonUnitAxisVectors -ne 5709 -or
   [int]$axes.placementOccurrencesWithRealModelBodies -ne 1902 -or
   [int]$axes.placementOccurrencesWithVerifiedExternalStockIdentity -ne 1 -or
   [int]$axes.placementOccurrencesWithOnlyNativeIdentity -ne 0 -or
   [int]$axes.worldGfxStaticModelDrawInstsIntegrated -ne 0 -or
   [bool]$axes.hardwareReady -or
   [string]$world.status -ne 'AZTEC_GATE8_ALL_CLASSIFIED_STATIC_XMODEL_TYPES_8509_ROOT_WORLD_OAT_PASS' -or
   -not [bool]$world.OatPass -or
   [int]$world.realXModelRootsInCurrentFf -ne 28 -or
   [bool]$world.hardwareReady){
    throw 'Gate8E requires 28-body OAT world plus exact passed 1903/5709 axis and source proofs.'
}
$sourceFile=[string]$source.typedDrawFile
$axisFile=[string]$axes.packedAxesReport
foreach($p in @($sourceFile,$axisFile)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Pinned draw input missing: $p"}
}
if((Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash -ine
    [string]$source.typedDrawSha256 -or
   (Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash -ine
    [string]$axes.originalPcDrawSourceSha256){
    throw 'Original-PC typed draw source changed since axis preflight.'
}
$sourceRows=Get-Content -LiteralPath $sourceFile -Raw | ConvertFrom-Json
$axisRows=Get-Content -LiteralPath $axisFile -Raw | ConvertFrom-Json
if([string]$sourceRows.format -ne 'WaW360.T4PcGate8StaticDrawSource.v1' -or
   [int]$sourceRows.pcDrawSize -ne 92 -or
   [int]$sourceRows.pcPlacementSize -ne 52 -or
   [int]$sourceRows.count -ne 1903 -or
   @($sourceRows.records).Count -ne 1903 -or
   [string]$axisRows.schema -ne 'WaW360.AztecGate8XenonPackedStaticDrawAxes.v1' -or
   [string]$axisRows.status -ne 'AZTEC_GATE8_PC_STATIC_DRAW_PACKED_AXIS_CANDIDATES_SOURCE_ONLY' -or
   [string]$axisRows.verifiedWorldMode -ne '28-model' -or
   [string]$axisRows.sourceDrawsSha256 -ine [string]$source.typedDrawSha256 -or
   @($axisRows.records).Count -ne 1903 -or
   [bool]$axisRows.xboxStaticDrawRecordBytesEmitted){
    throw 'Typed draw or source-only packed-axis candidate ABI changed.'
}
function Write-U32BE([byte[]]$bytes,[int]$at,[uint32]$value){
    $bytes[$at]=[byte](($value -shr 24) -band 255)
    $bytes[$at+1]=[byte](($value -shr 16) -band 255)
    $bytes[$at+2]=[byte](($value -shr 8) -band 255)
    $bytes[$at+3]=[byte]($value -band 255)
}
function Write-F32BE([byte[]]$bytes,[int]$at,[double]$value){
    if([double]::IsNaN($value) -or [double]::IsInfinity($value) -or
       [Math]::Abs($value) -gt [single]::MaxValue){
        throw "Nonfinite/out-of-float32 PC source numeric input at $at."
    }
    $f=[BitConverter]::GetBytes([single]$value)
    if([BitConverter]::IsLittleEndian){[array]::Reverse($f)}
    [array]::Copy($f,0,$bytes,$at,4)
}
function Read-U32BE([byte[]]$bytes,[int]$at){
    # Explicit intermediate expressions retain WinPS 5.1 parser support.
    [uint32]$value=([uint32]$bytes[$at] -shl 24)
    $value=$value -bor ([uint32]$bytes[$at+1] -shl 16)
    $value=$value -bor ([uint32]$bytes[$at+2] -shl 8)
    $value=$value -bor [uint32]$bytes[$at+3]
    return $value
}
$size=60
$buffer=New-Object byte[] (1903*$size)
$owned=0
$external=0
for($i=0;$i -lt 1903;$i++){
    $d=$sourceRows.records[$i]
    $a=$axisRows.records[$i]
    if([int]$d.ordinal -ne $i -or [int]$a.ordinal -ne $i -or
       [string]$d.model -cne [string]$a.model -or
       @($d.origin).Count -ne 3 -or @($a.axisPackedWords).Count -ne 3 -or
       [int]$a.nativeModelRoot -lt 0 -or
       [int]$a.nativeModelRoot -ge 8509){
        throw "PC/Xenon ordinal, model identity or origin/axis shape mismatch at $i."
    }
    $real=[bool]$a.realModelBodyPresentInCurrentWorldFf
    $stock=[bool]$a.exactExternalStockIdentityOnly
    if($real -eq $stock){throw "Owned/external native model classification ambiguous at $i."}
    if($real){$owned++}else{$external++}
    $off=$i*$size
    Write-F32BE $buffer $off ([double]$d.cullDist)
    for($j=0;$j -lt 3;$j++){
        Write-F32BE $buffer ($off+4+4*$j) ([double]$d.origin[$j])
        $word=[string]$a.axisPackedWords[$j]
        if($word -cnotmatch '^0x[0-9A-Fa-f]{8}$'){
            throw "Noncanonical pinned T4Xenon packed axis at $i/$j : $word"
        }
        Write-U32BE $buffer ($off+16+4*$j) ([Convert]::ToUInt32($word.Substring(2),16))
    }
    Write-F32BE $buffer ($off+28) ([double]$d.scale)
    # The entire 28-byte tail (+32..+59) is deliberately ZERO/INVALID:
    # XModel pointer needs native root relocation, and PC light/cache/
    # flags/tail metadata MUST NOT be assumed equivalent to Xenon fields.
}
if($owned -ne 1902 -or $external -ne 1){
    throw "28-world source draw ownership census changed ($owned/$external)."
}
# Independent in-memory endian/field readback and hard guarantee that
# NO fake pointer, lightingHandle, flags or padding was populated.
for($i=0;$i -lt 1903;$i++){
    $off=$i*$size
    for($j=0;$j -lt 3;$j++){
        $expected=[Convert]::ToUInt32(([string]$axisRows.records[$i].axisPackedWords[$j]).Substring(2),16)
        $actual=Read-U32BE $buffer ($off+16+4*$j)
        if($actual -ne $expected){throw "Endian roundtrip mismatch at draw $i axis $j."}
    }
    for($j=32;$j -lt 60;$j++){
        if($buffer[$off+$j] -ne 0){
            throw "Unproven native pointer/lighting/flag/tail bytes populated at draw $i +$j."
        }
    }
}
New-Item -ItemType Directory -Force -Path $dir | Out-Null
[IO.File]::WriteAllBytes($binary,$buffer)
if((Get-Item -LiteralPath $binary).Length -ne 114180){
    throw 'Partial geometry candidate output is not exactly 1903 x 60 bytes.'
}
$sha=(Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
$result=[ordered]@{
    schema='WaW360.AztecGate8Xenon60ByteStaticDrawLayoutBridge.v1'
    status='AZTEC_GATE8_1903_NATIVE_60B_GEOMETRY_SKELETON_SOURCE_ONLY_PASS'
    pinnedOatRevision=$head
    nativeHeader=$header
    nativeHeaderSha256=(Get-FileHash -LiteralPath $header -Algorithm SHA256).Hash
    sourceDrawReport=$sourceReportPath
    axisPreflightReport=$axisReportPath
    verified28ModelWorldOatReport=$worldPath
    pcSourceDraws=1903
    pcSourceDrawStride=92
    xenonHeaderDerivedDrawStride=$size
    geometryCandidateFile=$binary
    geometryCandidateSha256=$sha
    geometryCandidateBytes=$buffer.Length
    validatedPackedAxisVectors=5709
    sourceOwnedXModelDraws=$owned
    nativeStockIdentityDraws=$external
    byteLayout=[ordered]@{
        cullDist=0;origin=4;packedAxis=16;scale=28
        modelPointer=32;reflectionProbeIndex=36
        primaryLightIndex=37;lightingHandle=38
        flags=40;reservedPad=41;stride=60
    }
    writtenFields=@('cullDist','origin','packedAxis','scale')
    intentionallyUnwrittenFields=@('modelPointer','reflectionProbeIndex',
        'primaryLightIndex','lightingHandle','flags','reservedPad')
    xenonModelPointersRelocated=0
    lightingHandlesProven=0
    nativeFlagsAndPaddingSemanticsProven=$false
    actualXboxDrawRecordsWritten=0
    drawRecordsLinkedIntoGfxWorld=0
    ffModified=$false
    hardwareReady=$false
    note='114180-byte geometry-only diagnostic is INVALID AS XBOX DRAW DATA. 28-byte tail at +32..+59 is zero, not an approved neutral lighting policy; never graft into FF. Next: native reference lighting/flag/pad witness, then correctly relocated 1903 XModel* records and OAT FF roundtrip.'
}
$result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Host '[AZTEC-GATE8-60B] ========================================================='
Write-Host "[AZTEC-GATE8-60B] status=$($result.status)"
Write-Host '[AZTEC-GATE8-60B] Pinned T4Xenon header: 60B draw stride, 28B packed placement'
Write-Host "[AZTEC-GATE8-60B] Source typed draws=1903 | 5709 packed axes | owned draws=$owned | external stock identity=$external"
Write-Host "[AZTEC-GATE8-60B] Geometry-only endian-readback=$($buffer.Length) bytes SHA256=$sha"
Write-Host '[AZTEC-GATE8-60B] TODO: model pointer +32, lighting +38, flags +40, remaining pad/tail.'
Write-Host '[AZTEC-GATE8-60B] NOT A VALID XBOX DRAW ARRAY. 0/1903 linked. FF size unchanged.'
Write-Host "[AZTEC-GATE8-60B] report=$reportPath"
Write-Host '[AZTEC-GATE8-60B] ========================================================='
