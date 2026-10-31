[CmdletBinding()]
param([switch]$BuildTrace)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# FIRST-EXCEPTION native OAT trace of the EXISTING failed Gate11 FF.
# Does not rebuild or mutate any map FF, original PC source, or Gate10.
# A one-time specialized OAT rebuild is required only when this new
# diagnostic capability has not yet been installed.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$work=Join-Path $base 'pc_g11_cells_740'
$failedFF=Join-Path $work 'scratch_emit\nazi_zombie_aztec.ff'
$failedManifest=Join-Path $work 'aztec_pc_world_core_integrated_oat_run.json'
$gate10Manifest=Join-Path $base 'pc_g10_1903\oat_loaded_1903_draw_readback\aztec_gate10_independent_1903_draw_readback.json'
$forensicPath=Join-Path $work 'gate11_failed_ff_pointer_layout_audit.json'
$out=Join-Path $work 'oat_first_bad_pointer_trace'
$traceLog=Join-Path $out 'native_oat_first_bad_pointer.log'
$report=Join-Path $out 'aztec_gate11_native_oat_first_bad_pointer.json'
$builder=Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$capPath=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
foreach($p in @($failedFF,$failedManifest,$gate10Manifest,$forensicPath,$builder)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing already-emitted FF or source provenance: $p"}
}
$failed=Get-Content -LiteralPath $failedManifest -Raw|ConvertFrom-Json
$g10=Get-Content -LiteralPath $gate10Manifest -Raw|ConvertFrom-Json
$forensic=Get-Content -LiteralPath $forensicPath -Raw|ConvertFrom-Json
if([string]$failed.status -ne 'AZTEC_GATE11_11_CELL_740_AABB_DIAGNOSTIC_OAT_LOAD_BLOCKED' -or
   [bool]$failed.OatPass -or [bool]$failed.hardwareReady -or
   [int]$failed.gate11SourceCellsInFf -ne 11 -or
   [int]$failed.gate11AabbTreesInFf -ne 740 -or
   [string]$g10.stage4EEmittedFfSha256 -ine '0BB41398C4236EF33CB9D60F923E09152966D52AA623DAEFC33D731A42630564' -or
   [string]$forensic.status -ne 'AZTEC_GATE11_FAILED_FF_OFFLINE_POINTER_LAYOUT_AUDIT_NOT_OAT' -or
   [int]$forensic.matchedAabbTreeHeaders -ne 740 -or
   [int]$forensic.matchedStaticModelIndexEntries -ne 14974 -or
   [int]$forensic.matchedReflectionProbeIndexEntries -ne 11){
    throw 'First OAT fault trace requires the actual source-verified and OAT-REJECTED Gate11 FF.'
}
$baselineFF=[string]$g10.stage4EEmittedFf
if(-not(Test-Path -LiteralPath $baselineFF -PathType Leaf)){throw "Original Gate10 FF missing: $baselineFF"}
$baselineHash=(Get-FileHash -LiteralPath $baselineFF -Algorithm SHA256).Hash
$failedHash=(Get-FileHash -LiteralPath $failedFF -Algorithm SHA256).Hash
if($baselineHash -ine [string]$g10.stage4EEmittedFfSha256 -or
   $failedHash -ine [string]$forensic.failedDiagnosticFfSha256 -or
   [IO.Path]::GetFullPath($baselineFF) -ieq [IO.Path]::GetFullPath($failedFF)){
    throw 'The pinned Gate10 baseline or failed Gate11 artifact SHA/path changed. Refuse OAT.'
}
function Has-PointerTrace {
    if(-not(Test-Path -LiteralPath $capPath -PathType Leaf) -or
       -not(Test-Path -LiteralPath $oat -PathType Leaf)){return $false}
    $cap=Get-Content -LiteralPath $capPath -Raw|ConvertFrom-Json
    return ([string]$cap.format -eq 'WaW360.OatGraphDumperCapabilities.v1' -and
        [string]$cap.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($cap.capabilities) -contains 't4xenon-asset-fault-trace-v1' -and
        @($cap.capabilities) -contains 't4xenon-pointer-stream-fault-trace-v1')
}
if(-not(Has-PointerTrace)){
    if(-not $BuildTrace){
        throw 'Pinned OAT native pointer+asset first exception capability not installed. Re-run ONCE with -BuildTrace. DO NOT REBUILD FF.'
    }
    Write-Host '[AZTEC-GATE11-FIRSTPTR] Installing OAT first-exception asset + invalid packed zone pointer cursor trace.'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $builder -TraceNativeAssetLoader
    if($LASTEXITCODE -ne 0 -or -not(Has-PointerTrace)){
        throw 'Specialized first-exception OAT diagnostic build failed; old FF remains unchanged.'
    }
}
New-Item -ItemType Directory -Path $out -Force|Out-Null
Write-Host '[AZTEC-GATE11-FIRSTPTR] =============================================================='
Write-Host '[AZTEC-GATE11-FIRSTPTR] Replaying FAILED 3642925-byte FF through OAT only.'
Write-Host '[AZTEC-GATE11-FIRSTPTR] Last source-byte audit: 11 cells/740 trees/14974 indexes/11 probes MATCH.'
Write-Host '[AZTEC-GATE11-FIRSTPTR] Tracing first failing XAsset root, raw pointer, block cursor, stream position.'
$exitCode=999
$previous=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & $oat '--no-color' '--list' $failedFF 2>&1|
        ForEach-Object {[string]$_}|Set-Content -LiteralPath $traceLog -Encoding UTF8
    $exitCode=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$previous}
if((Get-FileHash -LiteralPath $failedFF -Algorithm SHA256).Hash -ine $failedHash -or
   (Get-FileHash -LiteralPath $baselineFF -Algorithm SHA256).Hash -ine $baselineHash){
    throw 'CRITICAL: protected FF hash changed during read-only OAT trace.'
}
$lines=@(Get-Content -LiteralPath $traceLog)
$ptrLines=@($lines|Where-Object {$_ -match '\[AZTEC-OAT-BADPTR\]'})
$assetLines=@($lines|Where-Object {$_ -match '\[AZTEC-OAT-FAULT\]'})
$errors=@($lines|Where-Object {$_ -match 'Zone referenced offset|Loading fastfile failed|ERROR:'}|Select-Object -First 8)
Write-Host "[AZTEC-GATE11-FIRSTPTR] OAT exit=$exitCode badPtrMarkers=$($ptrLines.Count) assetFaultMarkers=$($assetLines.Count)"
foreach($line in $ptrLines){Write-Host "[AZTEC-GATE11-FIRSTPTR] $line"}
foreach($line in $assetLines){Write-Host "[AZTEC-GATE11-FIRSTPTR] $line"}
foreach($line in $errors){Write-Host "[AZTEC-GATE11-FIRSTPTR] $line"}
if($exitCode -eq 0){
    throw 'Unexpected: previously rejected FF now loads successfully. Do not claim Gate11 without an independent source-field readback.'
}
$cursor=-1L;$rawValue='';$rootIndex=-1;$assetType=-1
foreach($line in $ptrLines){
    if($line -match 'streamPos=(\d+)'){$cursor=[long]$Matches[1]}
    if($line -match 'raw=(0x[0-9a-fA-F]+)'){$rawValue=$Matches[1]}
}
foreach($line in $assetLines){
    if($line -match 'rootIndex=(\d+)'){$rootIndex=[int]$Matches[1]}
    if($line -match 'typeId=(\d+)'){$assetType=[int]$Matches[1]}
}
$summary=[ordered]@{
    schema='WaW360.AztecGate11OatFirstInvalidPointerTrace.v1'
    status='AZTEC_GATE11_FIRST_OAT_NATIVE_INVALID_POINTER_CONTEXT_CAPTURED_NOT_FIXED'
    failedDiagnosticFastFile=$failedFF
    failedDiagnosticFastFileSha256=$failedHash
    preservedGate10FastFileSha256=$baselineHash
    nativeOatExitCode=$exitCode
    nativeFaultingRootIndex=$rootIndex
    nativeFaultingTypeId=$assetType
    nativeStreamPositionAtInvalidPointer=$cursor
    nativeRawPointerAtInvalidOffset=$rawValue
    nativeBadPointerMessages=$ptrLines
    nativeFirstFaultAssetMessages=$assetLines
    nativeErrorMessages=$errors
    previousOfflineSourceCellAndIndexBytesFullyMatch=$true
    passedGate11=$false
    hardwareReady=$false
}|ConvertTo-Json -Depth 5
$summary|Set-Content -LiteralPath $report -Encoding UTF8
Write-Host "[AZTEC-GATE11-FIRSTPTR] Diagnostic report=$report"
Write-Host '[AZTEC-GATE11-FIRSTPTR] NO FF generation. Existing rejected FF and Gate10 baseline SHA unchanged.'
if($ptrLines.Count -eq 0 -or $assetLines.Count -eq 0 -or $cursor -lt 0){
    throw 'OAT pointer/asset trace was incomplete. Inspect log; do not infer a loader cause without both markers.'
}
