[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$FastFile,
    [Parameter(Mandatory=$true)][string]$ExpectedSha256,
    [Parameter(Mandatory=$true)][string]$OatStatus,
    [Parameter(Mandatory=$true)][string]$HistoryDirectory,
    [int]$RealXModelBodies=0,
    [int]$RealGfxSurfaces=0,
    [int]$RealMaterials=0,
    [int]$DrawRecordsLinked=0
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# A size increase is measurable, NOT evidence of completeness. Update
# only after the top-level runner independently checks a successful OAT
# model/world validation manifest + the generated FF SHA.
$approvedOatStatuses=@(
    'AZTEC_GATE8_ALL_CLASSIFIED_STATIC_XMODEL_TYPES_8509_ROOT_WORLD_OAT_PASS',
    'AZTEC_GATE8_REAL_OWNED_XMODEL_FF_OAT_PASS_REFERENCES_UNRESOLVED'
)
if($OatStatus -notin $approvedOatStatuses){
    throw "Size history refuses to record a non-OAT-approved diagnostic status: $OatStatus"
}
if(-not(Test-Path -LiteralPath $FastFile -PathType Leaf)){
    throw "Physical OAT-validated FF missing: $FastFile"
}
$actualSha=(Get-FileHash -LiteralPath $FastFile -Algorithm SHA256).Hash.ToUpperInvariant()
if($ExpectedSha256 -notmatch '^[0-9a-fA-F]{64}$' -or
   $actualSha -ine $ExpectedSha256){
    throw 'FF changed between full native OAT validation and size tracking; refuse inaccurate size history.'
}
[int64]$length=(Get-Item -LiteralPath $FastFile).Length
if($length -le 0 -or $RealXModelBodies -lt 0 -or $RealGfxSurfaces -lt 0 -or
   $RealMaterials -lt 0 -or $DrawRecordsLinked -lt 0){
    throw 'FF size or asset counts are not valid.'
}
New-Item -ItemType Directory -Path $HistoryDirectory -Force | Out-Null
$manifest=Join-Path $HistoryDirectory 'aztec_ff_size_history.json'
$baseline=[ordered]@{
    bytes=[int64]3615230
    sha256='0CBD019ABB5D702429B9EECA9B04A843311FF118DDEDBD5CF012C757F757CF82'
    source='Verified 2026-10-30 console OAT PASS: 28 real XModels, 735 GfxSurfaces, 39 Materials'
}
$history=@()
if(Test-Path -LiteralPath $manifest -PathType Leaf){
    $prior=Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    if([string]$prior.schema -ne 'WaW360.AztecOatFastFileSizeHistory.v1' -or
       [long]$prior.baseline.bytes -ne [long]$baseline.bytes -or
       [string]$prior.baseline.sha256 -ine [string]$baseline.sha256){
        throw "Existing OAT FF size ledger changed schema or historical baseline. Refuse overwrite: $manifest"
    }
    $history=@($prior.builds)
}
$last=if($history.Count -gt 0){$history[-1]}else{$baseline}
[long]$priorBytes=[long]$last.bytes
[long]$delta=$length-$priorBytes
[long]$peak=[long]$baseline.bytes
foreach($record in $history){
    if([long]$record.bytes -gt $peak){$peak=[long]$record.bytes}
}
$changed=(-not [string]::Equals($actualSha,[string]$last.sha256,[StringComparison]::OrdinalIgnoreCase))
$record=[ordered]@{
    timestampUtc=[DateTime]::UtcNow.ToString('o')
    bytes=$length
    sha256=$actualSha
    differenceFromPriorBytes=$delta
    oatStatus=$OatStatus
    verifiedXModelBodies=$RealXModelBodies
    verifiedGfxSurfaces=$RealGfxSurfaces
    verifiedMaterialBodies=$RealMaterials
    physicalStaticDrawRecordsLinked=$DrawRecordsLinked
    validationOnly=$true
    hardwareReady=$false
}
if($changed){
    $history+=([pscustomobject]$record)
}
if($length -gt $peak){$peak=$length}
$formatBytes = {
    param([long]$b)
    return ('{0:N2} MiB' -f ($b/1MB))
}
$sign=if($delta -gt 0){'+'}else{''}
$trend=if($delta -gt 0){'GROWN'}elseif($delta -lt 0){'SMALLER'}else{'UNCHANGED SIZE'}
Write-Host '[AZTEC-SIZE] ==================================================='
Write-Host "[AZTEC-SIZE] OAT VERIFIED FF: $length bytes ($(& $formatBytes $length))"
Write-Host "[AZTEC-SIZE] Since last distinct OAT build: $sign$delta bytes ($trend)"
Write-Host "[AZTEC-SIZE] RECORD SIZE: $peak bytes ($(& $formatBytes $peak))"
Write-Host "[AZTEC-SIZE] VALIDATED CONTENT: XModels=$RealXModelBodies GfxSurfaces=$RealGfxSurfaces Materials=$RealMaterials actualStaticDrawLinks=$DrawRecordsLinked/1903"
if($changed){
    Write-Host '[AZTEC-SIZE] NEW verified build SHA recorded.'
}else{
    Write-Host '[AZTEC-SIZE] SAME verified binary as last recorded build; duplicate suppressed.'
}
Write-Host "[AZTEC-SIZE] SHA256=$actualSha"
Write-Host '[AZTEC-SIZE] SIZE != COMPLETENESS. Do not add padding to increase this value.'
$document=[ordered]@{
    schema='WaW360.AztecOatFastFileSizeHistory.v1'
    baseline=$baseline
    currentSizeBytes=$length
    biggestVerifiedSizeBytes=$peak
    lastBinaryChanged=$changed
    builds=@($history)
    note='Recorded ONLY after validated native OAT readback of this real source-owned diagnostic FF. Map completeness and hardware readiness are independently gated. Initial baseline comes from a user-verified console log, not an artificial FF.'
}
$temp=Join-Path $HistoryDirectory ('aztec_ff_size_history.json.tmp.'+[Guid]::NewGuid().ToString('N'))
try{
    $document | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temp -Encoding UTF8
    Move-Item -LiteralPath $temp -Destination $manifest -Force
}finally{
    if(Test-Path -LiteralPath $temp -PathType Leaf){
        Remove-Item -LiteralPath $temp -Force
    }
}
Write-Host "[AZTEC-SIZE] HISTORY=$manifest"
Write-Host '[AZTEC-SIZE] ==================================================='
