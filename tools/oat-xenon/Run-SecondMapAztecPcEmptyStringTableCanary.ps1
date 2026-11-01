[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$censusPath=Join-Path $base 'tonight_critical_source_census\aztec_tonight_critical_source_census.json'
$source=Join-Path $base 'tonight_pc_critical_body_export_probe\source_exports\stringtable\map_patch_ff\mp\zombiemode.csv'
$pcLog=Join-Path $base 'tonight_pc_critical_body_export_probe\logs\stringtable_map_patch_ff.log'
$linker=Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Linker.exe'
$unlinker=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$work=Join-Path $base 'pc_empty_stringtable_xenon_canary'
$logs=Join-Path $work 'logs'
$reportPath=Join-Path $work 'aztec_empty_stringtable_xenon_canary.json'
$textPath=Join-Path $work 'aztec_empty_stringtable_xenon_canary.txt'
foreach($p in @($censusPath,$source,$pcLog,$linker,$unlinker)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){throw "Aztec original-PC StringTable input missing: $p"}
}
$census=Get-Content -LiteralPath $censusPath -Raw | ConvertFrom-Json
if([string]$census.schema -ne 'WaW360.AztecTonightCriticalSourceCensus.v1' -or
   [string]$census.status -ne 'AZTEC_TONIGHT_CRITICAL_PC_SOURCE_IDENTITY_CENSUS_PASS' -or
   [int]$census.nativeRoots -ne 8509 -or [bool]$census.readyForXbox){
    throw 'Original-PC StringTable corpus/8,509 native-root census changed.'
}
$roots=@($census.nativeOwnedRootClassifications | Where-Object {
    [string]$_.family -eq 'stringtable' -and
    [string]$_.classification -eq 'PC_OWNED_NAME_PRESENT'
})
if($roots.Count -ne 1 -or [int]$roots[0].nativeRoot -ne 1319 -or
   [string]$roots[0].nativeName -ine 'mp/zombiemode.csv' -or
   @($roots[0].pcOwnerRoles).Count -ne 1 -or
   [string]$roots[0].pcOwnerRoles[0] -ne 'map_patch.ff'){
    throw 'PC StringTable native #1319 ownership/name identity is ambiguous or changed.'
}
$pin=@($census.pcSourceRoles | Where-Object {[string]$_.role -eq 'map_patch.ff'})
if($pin.Count -ne 1 -or
   -not (Test-Path -LiteralPath ([string]$pin[0].sourceFastfile) -PathType Leaf) -or
   (Get-FileHash -LiteralPath ([string]$pin[0].sourceFastfile) -Algorithm SHA256).Hash -ine
    [string]$pin[0].sourceSha256){
    throw 'Original PC map_patch.ff source hash changed.'
}
if((Get-Content -LiteralPath $pcLog -Raw) -notmatch '(?i)Finished with\s+0\s+warnings?,\s+0\s+errors?'){
    throw 'Original PC StringTable OAT dump did not prove 0 warnings / 0 errors.'
}
if([long](Get-Item -LiteralPath $source).Length -ne 0L){
    throw 'The source-owned mp/zombiemode.csv is no longer an empty OAT export.'
}
$srcHash=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
New-Item -Path $work,$logs -ItemType Directory -Force | Out-Null
$short=''
$status='AZTEC_PC_EMPTY_STRINGTABLE_XENON_CANARY_BLOCKED'
$blocker=''
$ffOut=''
$listCount=0;$readbackBytes=-1
Write-Host '[AZTEC-PC-STRINGTABLE] =============================================='
Write-Host '[AZTEC-PC-STRINGTABLE] ORIGINAL-PC ZERO-BYTE CSV -> XENON OAT ROUNDTRIP'
Write-Host '[AZTEC-PC-STRINGTABLE] Native #1319 mp/zombiemode.csv: no FF build, single-asset OAT only.'
Write-Host '[AZTEC-PC-STRINGTABLE] NOTE: empty CSV does not prove PC source rows/cols zero.'
try{
    $short=Join-Path ([IO.Path]::GetTempPath()) ('wawAzCsv_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    if($short.Length -gt 110){throw 'Require short physical OAT TEMP path.'}
    $assetDir=Join-Path $short 'assets';$zoneDir=Join-Path $short 'zones'
    $workDir=Join-Path $short 'work';$ffDir=Join-Path $short 'linked'
    $dumpDir=Join-Path $short 'dump'
    New-Item -Path $assetDir,$zoneDir,$workDir,$ffDir,$dumpDir -ItemType Directory -Force | Out-Null
    $copied=Join-Path $assetDir 'mp\zombiemode.csv'
    New-Item -Path (Split-Path -Parent $copied) -ItemType Directory -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $copied -Force
    if((Get-FileHash -LiteralPath $copied -Algorithm SHA256).Hash -ine $srcHash){
        throw 'Original PC empty CSV SHA changed during OAT source staging.'
    }
    $project='aztec_empty_stringtable_only'
    $lines=@('>game,T4','>platform,xbox360',('>name,'+$project),'','stringtable,mp/zombiemode.csv')
    [IO.File]::WriteAllLines((Join-Path $zoneDir ($project+'.zone')),[string[]]$lines,
        [Text.UTF8Encoding]::new($false))
    $linkLog=Join-Path $logs 'stringtable_link.log'
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $linker '--no-color' '--base-folder' $workDir '--source-search-path' $zoneDir '--asset-search-path' $assetDir '--output-folder' $ffDir $project 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $linkLog -Encoding UTF8
        $exit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    if($exit -ne 0 -or
       @(Select-String -LiteralPath $linkLog -Pattern '(?i)^\s*(WARNING:|ERROR:)').Count -ne 0){
        throw "T4 Xenon empty StringTable OAT Linker failed, exit=$exit. See $linkLog"
    }
    $ffFiles=@(Get-ChildItem -LiteralPath $ffDir -Recurse -File -Filter '*.ff')
    if($ffFiles.Count -ne 1){throw "Expected exactly 1 empty StringTable Xenon FF, got $($ffFiles.Count)."}
    $ff=[string]$ffFiles[0].FullName
    $listLog=Join-Path $logs 'stringtable_list.log'
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $unlinker '--no-color' '--list' $ff 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $listLog -Encoding UTF8
        $exit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    if($exit -ne 0 -or
       @(Select-String -LiteralPath $listLog -Pattern '(?i)^\s*(WARNING:|ERROR:)').Count -ne 0){
        throw "Independent OAT empty StringTable list failed, exit=$exit."
    }
    $pattern='(?i)(?:^|[\]\s])stringtable\s*,\s*mp/zombiemode\.csv\s*$'
    $listCount=@(Get-Content -LiteralPath $listLog | Where-Object {[string]$_ -match $pattern}).Count
    if($listCount -ne 1){throw "OAT 1/1 StringTable asset identity not found: count=$listCount."}
    $dumpLog=Join-Path $logs 'stringtable_dump.log'
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $unlinker '--no-color' '--include-assets' 'stringtable' '--output-folder' $dumpDir $ff 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $dumpLog -Encoding UTF8
        $exit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    $zero=@(Select-String -LiteralPath $dumpLog -Pattern '(?i)^\s*Finished with\s+0\s+warnings?,\s+0\s+errors?\s*$').Count -gt 0
    if($exit -ne 0 -or -not $zero -or
       @(Select-String -LiteralPath $dumpLog -Pattern '(?i)^\s*(WARNING:|ERROR:)').Count -ne 0){
        throw "Independent empty StringTable OAT dump failed exit=$exit, OAT 0/0=$zero."
    }
    $csv=Join-Path $dumpDir 'mp\zombiemode.csv'
    if(-not (Test-Path -LiteralPath $csv -PathType Leaf)){
        throw 'OAT readback did not export the expected StringTable CSV.'
    }
    $readbackBytes=[long](Get-Item -LiteralPath $csv).Length
    if($readbackBytes -ne 0L -or
       (Get-FileHash -LiteralPath $csv -Algorithm SHA256).Hash -ine $srcHash){
        throw 'Empty original-PC CSV is not byte-identical after Xenon OAT roundtrip.'
    }
    $ffOut=Join-Path $work 'aztec_pc_empty_stringtable_VALIDATION_ONLY.ff'
    Copy-Item -LiteralPath $ff -Destination $ffOut -Force
    $status='AZTEC_PC_EMPTY_STRINGTABLE_XENON_CANARY_PASS'
    Write-Host '[AZTEC-PC-STRINGTABLE] Independent OAT 1/1, empty CSV 0/0 bytes SHA PASS'
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-PC-STRINGTABLE] BLOCKER: $blocker"
    foreach($log in @('stringtable_link.log','stringtable_list.log','stringtable_dump.log')){
        $path=Join-Path $logs $log
        if(Test-Path -LiteralPath $path -PathType Leaf){
            foreach($line in @(Get-Content -LiteralPath $path -Tail 8)){
                Write-Host "[AZTEC-PC-STRINGTABLE] $log $line"
            }
        }
    }
}finally{
    if(-not [string]::IsNullOrWhiteSpace($short) -and (Test-Path -LiteralPath $short -PathType Container)){
        Remove-Item -LiteralPath $short -Recurse -Force
    }
}
$report=[ordered]@{
    schema='WaW360.AztecPcEmptyStringTableCanary.v1'
    status=$status
    nativeRootIndex=1319
    nativeRootName='mp/zombiemode.csv'
    sourceFastFile=[string]$pin[0].sourceFastfile
    sourceFastFileSha256=[string]$pin[0].sourceSha256
    sourceOatCsvBytes=0
    sourceOatCsvSha256=$srcHash
    xenonOatRootCount=$listCount
    xenonOatCsvReadbackBytes=$readbackBytes
    standaloneValidationOnlyFF=$ffOut
    blocker=$blocker
    originalPcTableFieldsVerified=$false
    integrated8509RootTableVerified=$false
    readyForXbox=$false
    note='This only verifies the OAT-exported empty CSV can round-trip as an independently linked T4 Xenon StringTable. It does not prove the original PC asset has 0 rows/0 columns or that the existing 8509-root empty shell is source-faithful. Not for Xbox.'
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
[IO.File]::WriteAllLines($textPath,[string[]]@(
    "AZTEC EMPTY PC STRINGTABLE XENON CANARY: $status"
    "source bytes=0, OAT listed=$listCount/1, Xenon dump bytes=$readbackBytes"
    "blocker=$blocker"
    'Original PC source row/column structure NOT proven. No integrated full FF. NOT FOR XBOX.'
),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-STRINGTABLE] ================= RESULT ======================'
Write-Host "[AZTEC-PC-STRINGTABLE] status=$status"
Write-Host "[AZTEC-PC-STRINGTABLE] oatIdentity=$listCount/1; csvReadback=$readbackBytes/0 bytes"
Write-Host "[AZTEC-PC-STRINGTABLE] report=$textPath"
Write-Host '[AZTEC-PC-STRINGTABLE] VALIDATION ONLY / NOT FOR XBOX.'
if($status -ne 'AZTEC_PC_EMPTY_STRINGTABLE_XENON_CANARY_PASS'){exit 4}
