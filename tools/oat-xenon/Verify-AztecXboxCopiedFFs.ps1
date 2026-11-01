[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ManifestJson,
    [Parameter(Mandatory=$true)][string]$XboxCopiedMapDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$manifestPath=(Resolve-Path -LiteralPath $ManifestJson -ErrorAction Stop).Path
$m=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if([string]$m.schema -ne 'WaW360.AztecFirstExceptionCanary.v1' -or
    [string]$m.status -ne 'EXPERIMENTAL_FIRST_EXCEPTION_NOT_PRODUCTION' -or
    [bool]$m.nativeReleaseAuthorized){
    throw 'Not an exact first-exception native-debug provenance manifest.'
}
$installed=(Resolve-Path -LiteralPath $XboxCopiedMapDirectory -ErrorAction Stop).Path
$staged=(Resolve-Path -LiteralPath ([string]$m.stagedMapDirectory) -ErrorAction Stop).Path
if($installed -ieq $staged){
    throw 'Supplied directory is the LOCAL staging folder, not a separate Xbox-installed/downloaded copy. Hardware provenance is NOT proven.'
}
if($installed -match '(?i)codxe_beta_stage|working map references'){
    throw 'Reference-stage folder supplied. Need a fresh Xbox-installed or Xbox-downloaded copy instead.'
}
$names=@(
    [pscustomobject]@{file='nazi_zombie_aztec.ff';expected=[string]$m.main.sha256},
    [pscustomobject]@{file='nazi_zombie_aztec_load.ff';expected=[string]$m.load.sha256}
)
$passed=0
foreach($row in $names){
    $path=Join-Path $installed $row.file
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Xbox-copy validation FAILED (missing): $path"
    }
    $actual=[string](Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if($actual -ine $row.expected){
        Write-Host "[AZTEC-INSTALLED] HASH MISMATCH $($row.file): observed=$actual expected=$($row.expected)"
        throw 'Wrong Xbox-file provenance. Do NOT attribute a hardware result to PC2XB3.'
    }
    $passed++
    Write-Host "[AZTEC-INSTALLED] PASS $($row.file): exact staged SHA256=$actual"
}
Write-Host "[AZTEC-INSTALLED] ==================================================="
Write-Host "[AZTEC-INSTALLED] $passed/2 Xbox-side file copies byte-identical to PC2XB3 staging."
Write-Host '[AZTEC-INSTALLED] This proves bytes installed/copied back, NOT runtime load success or production readiness.'
Write-Host '[AZTEC-INSTALLED] Capture CodXe/WV/Watson exception logs for native runtime progress.'
