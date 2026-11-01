[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ManifestJson,
    [Parameter(Mandatory=$true)][string]$XboxCopiedMapDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Only compares actual installed/copied-back Xbox files against a physically
# staged PC2XB3 world diagnostic. Does not prove engine loaded either FF.
$m=Get-Content -LiteralPath (Resolve-Path -LiteralPath $ManifestJson -ErrorAction Stop).Path -Raw | ConvertFrom-Json
if([string]$m.schema -ne 'WaW360.AztecWorldFirstExceptionCanary.v1' -or
   [string]$m.status -ne 'SOURCE_WORLD_FIRST_EXCEPTION_DEBUG_ONLY' -or
   [bool]$m.nativeReleaseAuthorized){
    throw 'Expected source-world-first-exception stage manifest (not native-reference or weapon shell stage).'
}
$installed=(Resolve-Path -LiteralPath $XboxCopiedMapDirectory -ErrorAction Stop).Path
$source=(Resolve-Path -LiteralPath ([string]$m.stagedMapDirectory) -ErrorAction Stop).Path
if($installed -ieq $source -or
   $installed -match '(?i)codxe_beta_stage|working.map.references'){
    throw 'Supplied folder is local stage or native reference, not separately recovered Xbox map bytes.'
}
$records=@(
    [pscustomobject]@{FileName='nazi_zombie_aztec.ff';Expected=[string]$m.sourceWorld.sha256},
    [pscustomobject]@{FileName='nazi_zombie_aztec_load.ff';Expected=[string]$m.sourceLoad.sha256}
)
foreach($record in $records){
    $file=Join-Path $installed $record.FileName
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Xbox map file missing: $file"}
    $got=[string](Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    if($record.Expected -notmatch '^[A-Fa-f0-9]{64}$' -or $got -ine $record.Expected){
        Write-Host "[AZTEC-WORLD-XBOX-HASH] MISMATCH $($record.FileName): expected=$($record.Expected), actual=$got"
        throw 'Reference/new output mix-up or incomplete copy: cannot attribute Xbox result to PC2XB3.'
    }
    Write-Host "[AZTEC-WORLD-XBOX-HASH] PASS $($record.FileName) SHA256=$got"
}
Write-Host '[AZTEC-WORLD-XBOX-HASH] BOTH Xbox-copied files match the genuine source-world test; still NOT gameplay/production proof.'
