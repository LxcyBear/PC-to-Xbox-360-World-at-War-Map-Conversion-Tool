[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$fivePath=Join-Path $base 'pc_two_xmodel_two_material_mapents_fullgraph_integration\aztec_pc_five_payload_mapents_graph.json'
$canaryPath=Join-Path $base 'pc_empty_stringtable_xenon_canary\aztec_empty_stringtable_xenon_canary.json'
$identityPath=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$originalCsv=Join-Path $base 'tonight_pc_critical_body_export_probe\source_exports\stringtable\map_patch_ff\mp\zombiemode.csv'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$work=Join-Path $base 'integrated_empty_stringtable_readback'
$log=Join-Path $work 'integrated_stringtable_dump.log'
$listLog=Join-Path $work 'integrated_stringtable_list.log'
$jsonPath=Join-Path $work 'aztec_integrated_empty_stringtable_readback.json'
$textPath=Join-Path $work 'aztec_integrated_empty_stringtable_readback.txt'
foreach($p in @($fivePath,$canaryPath,$identityPath,$originalCsv,$oat)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec integrated StringTable readback prerequisite missing: $p"
    }
}
$five=Get-Content -LiteralPath $fivePath -Raw | ConvertFrom-Json
$canary=Get-Content -LiteralPath $canaryPath -Raw | ConvertFrom-Json
$identity=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
if([string]$five.schema -ne 'WaW360.AztecPcFivePayloadMapEntsGraph.v1' -or
   [string]$five.status -ne 'AZTEC_PC_FIVE_PAYLOAD_MAPENTS_GRAPH_PASS' -or
   [int]$five.nativeRoots -ne 8509 -or
   [int]$five.sourcePCMapEntsReadbacks -ne 1 -or
   [int]$five.pcXModelsListed -ne 2 -or
   [int]$five.pcMaterialsListed -ne 2 -or
   [int]$five.pcIwiPixelExactReadbacks -ne 2 -or
   [int]$five.pcRawFileExactReadbacks -ne 114 -or
   -not [bool]$five.oatZeroWarningsZeroErrors -or
   -not [bool]$five.layoutReplayExact -or
   [bool]$five.readyForXbox -or
   [string]$canary.schema -ne 'WaW360.AztecPcEmptyStringTableCanary.v1' -or
   [string]$canary.status -ne 'AZTEC_PC_EMPTY_STRINGTABLE_XENON_CANARY_PASS' -or
   [int]$canary.nativeRootIndex -ne 1319 -or
   [int]$canary.xenonOatRootCount -ne 1 -or
   [long]$canary.sourceOatCsvBytes -ne 0L -or
   [long]$canary.xenonOatCsvReadbackBytes -ne 0L -or
   [bool]$canary.originalPcTableFieldsVerified -or
   [bool]$canary.readyForXbox -or
   -not [bool]$identity.TypeSequenceVerified -or
   @($identity.Entries).Count -ne 8509){
    throw 'Passing original-PC StringTable canary / fullgraph / native identity provenance is missing or changed.'
}
$root=$identity.Entries[1319]
if([int]$root.Index -ne 1319 -or
   ([string]$root.TypeName).Replace('_','').ToLowerInvariant() -ne 'stringtable' -or
   [string]$root.Name -ine 'mp/zombiemode.csv'){
    throw 'Expected exact native StringTable root #1319 identity not found.'
}
if((Get-FileHash -LiteralPath $originalCsv -Algorithm SHA256).Hash -ine [string]$canary.sourceOatCsvSha256 -or
   [long](Get-Item -LiteralPath $originalCsv).Length -ne 0L -or
   -not (Test-Path -LiteralPath ([string]$canary.sourceFastFile) -PathType Leaf) -or
   (Get-FileHash -LiteralPath ([string]$canary.sourceFastFile) -Algorithm SHA256).Hash -ine
     [string]$canary.sourceFastFileSha256){
    throw 'Original PC CSV/map_patch.ff source SHA-256 provenance changed.'
}
$ff=[string]$five.diagnosticScratchFF
$allowed=([IO.Path]::GetFullPath((Join-Path $base 'pc_two_xmodel_two_material_mapents_fullgraph_integration\scratch_emit'))).TrimEnd([char]'\')+'\'
if(-not (Test-Path -LiteralPath $ff -PathType Leaf) -or
   -not ([IO.Path]::GetFullPath($ff)).StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){
    throw 'Five-payload scratch FF is absent or outside isolated diagnostic output.'
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
$status='AZTEC_PC_INTEGRATED_EMPTY_STRINGTABLE_READBACK_BLOCKED'
$blocker=''
$assetMatches=0
$readBytes=-1L
$readSha=''
$temp=''
Write-Host '[AZTEC-PC-STRINGTABLE-FULL] ========================================='
Write-Host '[AZTEC-PC-STRINGTABLE-FULL] INDEPENDENT 8509-ROOT STRINGTABLE OAT READBACK'
Write-Host '[AZTEC-PC-STRINGTABLE-FULL] Source is PC 0-byte mp/zombiemode.csv from map_patch.ff'
Write-Host '[AZTEC-PC-STRINGTABLE-FULL] NO BUILD / NO FF MUTATION / NEVER DEPLOY ON XBOX'
try{
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('wawAzTbl_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    if($temp.Length -gt 110){throw 'OAT requires a short physical TEMP path.'}
    New-Item -Path $temp -ItemType Directory -Force | Out-Null
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $oat '--no-color' '--list' $ff 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $listLog -Encoding UTF8
        $exit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    if($exit -ne 0 -or
       @(Select-String -LiteralPath $listLog -Pattern '(?i)^\s*(ERROR:|WARNING:)').Count -ne 0){
        throw "Five-payload Xenon OAT list failed exit=$exit; $listLog"
    }
    $pattern='(?i)(?:^|[\]\s])stringtable\s*,\s*mp/zombiemode\.csv\s*$'
    $assetMatches=@(Get-Content -LiteralPath $listLog | Where-Object {[string]$_ -match $pattern}).Count
    if($assetMatches -ne 1){
        throw "Integrated fullgraph StringTable unique OAT identity count=$assetMatches (expected 1)."
    }
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $oat '--no-color' '--include-assets' 'stringtable' '--output-folder' $temp $ff 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $log -Encoding UTF8
        $exit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    $zero=@(Select-String -LiteralPath $log -Pattern '(?i)^\s*Finished with\s+0\s+warnings?,\s+0\s+errors?\s*$').Count -gt 0
    if($exit -ne 0 -or -not $zero -or
       @(Select-String -LiteralPath $log -Pattern '(?i)^\s*(ERROR:|WARNING:)').Count -ne 0){
        throw "Integrated OAT StringTable dump failed exit=$exit zeroWarningsErrors=$zero; $log"
    }
    $read=Join-Path $temp 'mp\zombiemode.csv'
    if(-not (Test-Path -LiteralPath $read -PathType Leaf)){
        throw 'Fullgraph OAT did not export the expected integrated mp/zombiemode.csv.'
    }
    $readBytes=[long](Get-Item -LiteralPath $read).Length
    $readSha=(Get-FileHash -LiteralPath $read -Algorithm SHA256).Hash
    if($readBytes -ne 0L -or
       $readSha -ine [string]$canary.sourceOatCsvSha256){
        throw "Integrated OAT StringTable differs from source-owned empty CSV; bytes=$readBytes sha=$readSha"
    }
    $status='AZTEC_PC_INTEGRATED_EMPTY_STRINGTABLE_READBACK_PASS'
    Write-Host '[AZTEC-PC-STRINGTABLE-FULL] Exact 0-byte source/OAT semantics and 1/1 native identity: PASS'
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-PC-STRINGTABLE-FULL] BLOCKER: $blocker"
    foreach($path in @($listLog,$log)){
        if(Test-Path -LiteralPath $path -PathType Leaf){
            foreach($line in @(Get-Content -LiteralPath $path -Tail 6)){
                Write-Host "[AZTEC-PC-STRINGTABLE-FULL] OAT: $line"
            }
        }
    }
}finally{
    if(-not [string]::IsNullOrWhiteSpace($temp) -and
       (Test-Path -LiteralPath $temp -PathType Container)){
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
$result=[ordered]@{
    schema='WaW360.AztecIntegratedEmptyStringTableReadback.v1'
    status=$status
    nativeRootIndex=1319
    integratedNativeRoots=8509
    integratedFullgraph=$fivePath
    standalonePcXenonCanary=$canaryPath
    originalPcCsvSha256=[string]$canary.sourceOatCsvSha256
    integratedCsvSha256=$readSha
    integratedCsvBytes=$readBytes
    integratedOatStringTableRoots=$assetMatches
    originalPcRowColumnFieldsProven=$false
    fullMapRuntimeSafe=$false
    xboxDeploymentAuthorized=$false
    blocker=$blocker
    note='Read-only OAT semantic CSV equivalence on 8509-root scratch FF. 0-byte CSV does NOT establish original PC StringTable raw row/column ABI or world asset bodies. NEVER DEPLOY ON XBOX.'
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
[IO.File]::WriteAllLines($textPath,[string[]]@(
    "AZTEC INTEGRATED ORIGINAL-PC EMPTY STRINGTABLE READBACK: $status"
    "OAT native identity #1319 readback=$assetMatches/1"
    "PC csv bytes=0, integrated readback bytes=$readBytes"
    "Source SHA=$([string]$canary.sourceOatCsvSha256), integrated SHA=$readSha"
    "blocker=$blocker"
    'No full source StringTable row/column byte proof. World roots still missing. NEVER DEPLOY ON XBOX.'
),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-STRINGTABLE-FULL] ================= RESULT ====================='
Write-Host "[AZTEC-PC-STRINGTABLE-FULL] status=$status"
Write-Host "[AZTEC-PC-STRINGTABLE-FULL] rootIdentity=$assetMatches/1, sourcePCBytes=$readBytes/0"
Write-Host "[AZTEC-PC-STRINGTABLE-FULL] report=$textPath"
Write-Host '[AZTEC-PC-STRINGTABLE-FULL] NO BUILD / NO FF / NOT FOR XBOX'
if($status -ne 'AZTEC_PC_INTEGRATED_EMPTY_STRINGTABLE_READBACK_PASS'){exit 4}
