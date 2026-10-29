[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$censusPath=Join-Path $base 'tonight_critical_source_census\aztec_tonight_critical_source_census.json'
$pcLog=Join-Path $base 'tonight_pc_critical_body_export_probe\logs\mapents_map_ff.log'
$source=Join-Path $base 'tonight_pc_critical_body_export_probe\source_exports\mapents\map_ff\maps\nazi_zombie_aztec.d3dbsp.ents'
$linker=Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Linker.exe'
$unlinker=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$work=Join-Path $base 'pc_mapents_xenon_canary'
$logs=Join-Path $work 'logs'
$json=Join-Path $work 'aztec_pc_mapents_xenon_canary.json'
$txt=Join-Path $work 'aztec_pc_mapents_xenon_canary.txt'
foreach($p in @($censusPath,$pcLog,$source,$linker,$unlinker)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing MapEnts source-backed canary input: $p"}
}
$census=Get-Content -LiteralPath $censusPath -Raw | ConvertFrom-Json
if([string]$census.schema -ne 'WaW360.AztecTonightCriticalSourceCensus.v1' -or
   [string]$census.status -ne 'AZTEC_TONIGHT_CRITICAL_PC_SOURCE_IDENTITY_CENSUS_PASS' -or
   [int]$census.nativeRoots -ne 8509 -or [bool]$census.readyForXbox){
    throw 'PC source-census 8509-root provenance is no longer valid.'
}
$rows=@($census.nativeOwnedRootClassifications | Where-Object {
    [string]$_.family -ieq 'mapents' -and
    [string]$_.classification -eq 'PC_OWNED_NAME_PRESENT'
})
if($rows.Count -ne 1 -or @($rows[0].pcOwnerRoles).Count -ne 1 -or
   [string]$rows[0].pcOwnerRoles[0] -ne 'map.ff'){
    throw 'Require exactly one PC-owned Aztec MapEnts native root from map.ff.'
}
$asset=([string]$rows[0].nativeName).Replace('\','/')
if($asset -notmatch '^[a-zA-Z0-9_./-]+$' -or $asset.Contains('..') -or
   ($asset+'.ents') -ine 'maps/nazi_zombie_aztec.d3dbsp.ents'){
    throw "Native root identity not proven to match source PC MapEnts export: $asset"
}
$pcSource=@($census.pcSourceRoles | Where-Object {[string]$_.role -ieq 'map.ff'})
if($pcSource.Count -ne 1 -or
   -not (Test-Path -LiteralPath ([string]$pcSource[0].sourceFastfile) -PathType Leaf) -or
   (Get-FileHash -LiteralPath ([string]$pcSource[0].sourceFastfile) -Algorithm SHA256).Hash -ine [string]$pcSource[0].sourceSha256){
    throw 'Original PC map.ff SHA-256 provenance changed.'
}
if((Get-Content -LiteralPath $pcLog -Raw) -notmatch '(?i)Finished with\s+0\s+warnings?,\s+0\s+errors?'){
    throw 'Original PC MapEnts OAT source export did not prove zero warnings/errors.'
}
$sourceSize=[long](Get-Item -LiteralPath $source).Length
if($sourceSize -ne 571011L){throw "Unexpected original-PC MapEnts source bytes=$sourceSize (expected 571011)."}
$sourceSha=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
New-Item -Path $work,$logs -ItemType Directory -Force | Out-Null
$status='AZTEC_PC_MAPENTS_XENON_CANARY_BLOCKED'
$blocker=''
$temp=''
$readSha=''
$rootListed=0
$validationFF=''
Write-Host '[AZTEC-PC-MAPENTS] ==========================================='
Write-Host '[AZTEC-PC-MAPENTS] PC MAPENTS -> XENON LINKER -> BYTE READBACK'
Write-Host "[AZTEC-PC-MAPENTS] source=$asset; bytes=$sourceSize"
Write-Host '[AZTEC-PC-MAPENTS] Standalone scratch only / NEVER DEPLOY ON XBOX.'
try{
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('wawAzEnt_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    if($temp.Length -gt 110){throw 'OAT needs a short physical TEMP folder.'}
    $assets=Join-Path $temp 'assets'
    $zones=Join-Path $temp 'zones'
    $build=Join-Path $temp 'work'
    $linked=Join-Path $temp 'linked'
    $dump=Join-Path $temp 'dump'
    New-Item -Path $assets,$zones,$build,$linked,$dump -ItemType Directory -Force | Out-Null
    $target=Join-Path $assets (($asset+'.ents').Replace('/','\'))
    New-Item -Path (Split-Path -Parent $target) -ItemType Directory -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
    if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ine $sourceSha){
        throw 'Original-PC MapEnts bytes changed in short OAT staging directory.'
    }
    $project='aztec_pc_mapents_only'
    $zone=@('>game,T4','>platform,xbox360',('>name,'+$project),'',('mapents,'+$asset))
    [IO.File]::WriteAllLines((Join-Path $zones ($project+'.zone')),
        [string[]]$zone,[Text.UTF8Encoding]::new($false))
    $linkArgs=@('--no-color','--base-folder',$build,'--source-search-path',$zones,
        '--asset-search-path',$assets,'--output-folder',$linked,$project)
    $linkLog=Join-Path $logs 'xenon_mapents_link.log'
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $linker @linkArgs 2>&1 | ForEach-Object {[string]$_} |
            Set-Content -LiteralPath $linkLog -Encoding UTF8
        $exit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    if($exit -ne 0 -or
       @(Select-String -LiteralPath $linkLog -Pattern '(?i)^\s*(ERROR:|WARNING:)').Count -ne 0){
        throw "OAT Xenon MapEnts Linker failed (exit=$exit): $linkLog"
    }
    $files=@(Get-ChildItem -LiteralPath $linked -Filter '*.ff' -File -Recurse)
    if($files.Count -ne 1){throw "Linker output FF count=$($files.Count), expected 1."}
    $ff=[string]$files[0].FullName
    $listLog=Join-Path $logs 'xenon_mapents_list.log'
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $unlinker '--no-color' '--list' $ff 2>&1 | ForEach-Object {[string]$_} |
            Set-Content -LiteralPath $listLog -Encoding UTF8
        $exit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    if($exit -ne 0 -or
       @(Select-String -LiteralPath $listLog -Pattern '(?i)^\s*(ERROR:|WARNING:)').Count -ne 0){
        throw "Independent Xenon MapEnts list failed (exit=$exit): $listLog"
    }
    $pattern='(?i)(?:^|[\]\s])mapents\s*,\s*'+[regex]::Escape($asset)+'\s*$'
    $rootListed=@(Get-Content -LiteralPath $listLog | Where-Object {[string]$_ -match $pattern}).Count
    if($rootListed -ne 1){throw "Xenon OAT listed MapEnts root count=$rootListed, expected exactly 1."}
    $dumpLog=Join-Path $logs 'xenon_mapents_dump.log'
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $unlinker '--no-color' '--include-assets' 'mapents' '--output-folder' $dump $ff 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $dumpLog -Encoding UTF8
        $exit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    $zero=@(Select-String -LiteralPath $dumpLog -Pattern '(?i)^\s*Finished with\s+0\s+warnings?,\s+0\s+errors?\s*$').Count -gt 0
    if($exit -ne 0 -or -not $zero -or
       @(Select-String -LiteralPath $dumpLog -Pattern '(?i)^\s*(ERROR:|WARNING:)').Count -ne 0){
        throw "Independent Xenon OAT MapEnts dump failed (exit=$exit, OAT0/0=$zero): $dumpLog"
    }
    $readFile=Join-Path $dump (($asset+'.ents').Replace('/','\'))
    if(-not (Test-Path -LiteralPath $readFile -PathType Leaf)){
        throw "OAT readback missing original-PC MapEnts asset $asset"
    }
    $readSha=(Get-FileHash -LiteralPath $readFile -Algorithm SHA256).Hash
    if([long](Get-Item -LiteralPath $readFile).Length -ne $sourceSize -or
       $readSha -ine $sourceSha){
        throw 'Xenon output MapEnts bytes changed vs original PC source SHA/length.'
    }
    $validationFF=Join-Path $work 'aztec_pc_mapents_only_VALIDATION_ONLY.ff'
    Copy-Item -LiteralPath $ff -Destination $validationFF -Force
    if((Get-FileHash -LiteralPath $validationFF -Algorithm SHA256).Hash -ine
       (Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash){
        throw 'Copied standalone validation FF SHA mismatch.'
    }
    $status='AZTEC_PC_MAPENTS_XENON_CANARY_PASS'
    Write-Host "[AZTEC-PC-MAPENTS] PC entity bytes $sourceSize/$sourceSize, Xenon SHA readback PASS"
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-PC-MAPENTS] BLOCKER: $blocker"
    foreach($filename in @('xenon_mapents_link.log','xenon_mapents_list.log','xenon_mapents_dump.log')){
        $pathLog=Join-Path $logs $filename
        if(Test-Path -LiteralPath $pathLog -PathType Leaf){
            foreach($line in @(Get-Content -LiteralPath $pathLog -Tail 7)){
                Write-Host "[AZTEC-PC-MAPENTS] $filename $line"
            }
        }
    }
}finally{
    if(-not [string]::IsNullOrWhiteSpace($temp) -and (Test-Path -LiteralPath $temp -PathType Container)){
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
$result=[ordered]@{
    schema='WaW360.AztecPcMapEntsXenonCanary.v1'
    status=$status
    nativeRootIndex=[int]$rows[0].nativeRoot
    asset=$asset
    sourceFastFile=[string]$pcSource[0].sourceFastfile
    sourceFastFileSha256=[string]$pcSource[0].sourceSha256
    originalPcMapEntsBytes=$sourceSize
    originalPcMapEntsSha256=$sourceSha
    xboxFormatOatRootListed=$rootListed
    xenonMapEntsSha256=$readSha
    standaloneValidationFF=$validationFF
    blocker=$blocker
    fullMapIntegrated=$false
    readyForXbox=$false
    note='Original PC MapEnts source -> standalone Xenon Linker -> Xenon Unlinker byte readback only. No world conversion or gameplay proof. NEVER DEPLOY ON XBOX.'
}
$result | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $json -Encoding UTF8
[IO.File]::WriteAllLines($txt,[string[]]@(
    "AZTEC PC MAPENTS XENON CANARY: $status"
    "source asset=$asset, bytes=$sourceSize"
    "PC source sha256=$sourceSha; xenon sha256=$readSha"
    "OAT MapEnts root listed=$rootListed"
    "blocker=$blocker"
    'Standalone validation-only Xenon FF. NOT FOR XBOX.'
),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-MAPENTS] ================= RESULT ========================'
Write-Host "[AZTEC-PC-MAPENTS] status=$status"
Write-Host "[AZTEC-PC-MAPENTS] originalPcBytes=$sourceSize oatMapEntsListed=$rootListed/1"
Write-Host "[AZTEC-PC-MAPENTS] report=$txt"
Write-Host '[AZTEC-PC-MAPENTS] VALIDATION ONLY / NEVER DEPLOY ON XBOX.'
if($status -ne 'AZTEC_PC_MAPENTS_XENON_CANARY_PASS'){exit 4}
