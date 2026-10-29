[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$PackageDirectory,
    [string]$MapName = 'nazi_zombie_aztec',
    [string]$UnlinkerExe,
    [string]$ReportDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# SINGLE STRICT HARDWARE-TEST RELEASE RULE:
# (1) FINAL map.ff and map_load.ff BOTH pass native T4Xenon OAT loading,
#     exit 0, and OAT prints 0 warnings and 0 errors.
# (2) The CoD Xe usermap package is complete: both final FFs + sounds\
#     (present, with any map-owned streamed audio there). Optional
#     map.json/preview.dds are checked/reported, but not invented as
#     universal engine requirements. No separate .assets directory required.
# When BOTH pass, approve FIRST CONTROLLED NATIVE SMOKE TEST. Never turn
# gameplay success, perfect GSC scripts or crash-free runtime into an
# additional pre-hardware gate. No warranty against runtime crashes.

if($MapName -notmatch '^[A-Za-z0-9_-]+$'){
    throw 'MapName must be a safe single-path name, e.g. nazi_zombie_aztec.'
}
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$package=(Resolve-Path -LiteralPath $PackageDirectory -ErrorAction Stop).Path
$main=Join-Path $package ($MapName+'.ff')
$load=Join-Path $package ($MapName+'_load.ff')
$sounds=Join-Path $package 'sounds'
if([string]::IsNullOrWhiteSpace($ReportDirectory)){
    $ReportDirectory=Join-Path $repo ('output\native_smoke_test_release\'+$MapName)
}
$reportDir=[IO.Path]::GetFullPath($ReportDirectory)
New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
$logFile=Join-Path $reportDir 'native_smoke_test_package_oat_audit.txt'
$reportFile=Join-Path $reportDir 'native_smoke_test_package_release.json'

$candidates=@()
if(-not [string]::IsNullOrWhiteSpace($UnlinkerExe)){$candidates+=,$UnlinkerExe}
$candidates+=,(Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe')
$candidates+=,(Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe')
$candidates+=,(Join-Path $repo '.deps\OpenAssetTools-xenon\build\bin\Release_x86\Unlinker.exe')
$exe=$null
foreach($candidate in $candidates){
    if(Test-Path -LiteralPath $candidate -PathType Leaf){
        $exe=[IO.Path]::GetFullPath($candidate);break
    }
}
if($null -eq $exe){throw 'Pinned native Xenon OAT Unlinker.exe not found. Supply -UnlinkerExe.'}

Write-Host '[AZTEC-HARDWARE-GATE] ====================================================='
Write-Host '[AZTEC-HARDWARE-GATE] STRICT RELEASE RULE: BOTH FINAL FFs OAT PASS + COMPLETE CODEXE PACKAGE'
Write-Host "[AZTEC-HARDWARE-GATE] package=$package"
Write-Host "[AZTEC-HARDWARE-GATE] oat=$exe"
Write-Host '[AZTEC-HARDWARE-GATE] This approves the FIRST XBOX TEST, not guaranteed gameplay.'
$files=@($main,$load)
$ffResults=New-Object 'System.Collections.Generic.List[object]'
$allLogs=New-Object 'System.Collections.Generic.List[string]'
foreach($ff in $files){
    $stem=[IO.Path]::GetFileName($ff)
    $exists=Test-Path -LiteralPath $ff -PathType Leaf
    $size=if($exists){[long](Get-Item -LiteralPath $ff).Length}else{0L}
    $sha=if($exists -and $size -gt 64){[string](Get-FileHash -LiteralPath $ff -Algorithm SHA256).Hash}else{''}
    $exit=-1
    $zero=$false
    $raw=''
    if($size -gt 64){
        $lines=New-Object 'System.Collections.Generic.List[string]'
        $old=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            $global:LASTEXITCODE=$null
            try{
                & $exe '--list' '--no-color' $ff 2>&1 |
                    ForEach-Object {$lines.Add([string]$_)}
                if($null -ne $LASTEXITCODE){$exit=[int]$LASTEXITCODE}
            }catch{
                $lines.Add("OAT invocation error: $_")
            }
        }finally{$ErrorActionPreference=$old}
        $raw=($lines -join [Environment]::NewLine)
        $zero=($exit -eq 0) -and
            ($raw -match '(?im)Finished with 0 warnings, 0 errors')
    }
    $allLogs.Add("=== $stem ===")
    $allLogs.Add("exists=$exists bytes=$size exit=$exit zeroWarningsAndErrors=$zero sha256=$sha")
    $allLogs.Add($raw)
    $ffResults.Add([ordered]@{
        filename=$stem
        path=$ff
        present=$exists
        bytes=$size
        sha256=$sha
        oatExitCode=$exit
        oatZeroWarningsAndErrors=$zero
        oatNativeLoadPass=($exit -eq 0 -and $zero -and $size -gt 64)
    })
    Write-Host "[AZTEC-HARDWARE-GATE] $stem OAT exit=$exit zeroWarningsAndErrors=$zero bytes=$size"
}
[IO.File]::WriteAllLines($logFile,[string[]]$allLogs.ToArray(),[Text.UTF8Encoding]::new($false))
$hasSounds=Test-Path -LiteralPath $sounds -PathType Container
$soundFiles=if($hasSounds){@(Get-ChildItem -LiteralPath $sounds -File -Recurse -ErrorAction Stop)}else{@()}
$metadata=@(
    [ordered]@{name='map.json';present=(Test-Path -LiteralPath (Join-Path $package 'map.json') -PathType Leaf)},
    [ordered]@{name='preview.dds';present=(Test-Path -LiteralPath (Join-Path $package 'preview.dds') -PathType Leaf)}
)
$fullFF=$ffResults.Count -eq 2 -and
    @($ffResults | Where-Object {-not $_.oatNativeLoadPass}).Count -eq 0
$complete=$hasSounds -and $fullFF
$pass=$complete
$result=[ordered]@{
    schema='WaW360.AztecNativeSmokeTestReleaseGate.v1'
    status=if($pass){'HARDWARE_TEST_READY'}else{'HARDWARE_TEST_BLOCKED'}
    map=$MapName
    package=$package
    nativeOatUnlinker=$exe
    finalFastfiles=@($ffResults.ToArray())
    bothFinalFastfilesOatPass=$fullFF
    soundsFolderPresent=$hasSounds
    streamedSoundFileCount=@($soundFiles).Count
    associatedMetadata=$metadata
    metadataBlocking=$false
    completeCodxePackage=$complete
    readyForFirstControlledHardwareSmokeTest=$pass
    runtimeGameplayGuaranteed=$false
    originalFilesUntouched=$true
    oatAuditLog=$logFile
    note='Hardware readiness is strictly two conditions: both final .ff files pass native T4Xenon OAT with exit 0 and zero warnings/errors, and the complete CoD Xe usermap package includes both FFs plus a sounds folder with any required streamed audio. map.json/preview.dds are advisory until an actual known-working package establishes them as necessary. This is approval to TEST, not a guarantee of successful runtime initialization, scripted gameplay, or absence of crashes.'
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportFile -Encoding UTF8
Write-Host "[AZTEC-HARDWARE-GATE] soundsPresent=$hasSounds streamedFiles=$(@($soundFiles).Count) map.json=$($metadata[0].present) preview.dds=$($metadata[1].present)"
Write-Host "[AZTEC-HARDWARE-GATE] BOTH_FINAL_FFS_OAT_PASS=$fullFF COMPLETE_CODEXE_PACKAGE=$complete"
Write-Host "[AZTEC-HARDWARE-GATE] status=$($result.status)"
Write-Host "[AZTEC-HARDWARE-GATE] report=$reportFile"
if(-not $pass){
    throw 'Native hardware test BLOCKED: both final Xbox FastFiles must individually pass OAT, and the CoD Xe package must include sounds\. See audit report.'
}
Write-Host '[AZTEC-HARDWARE-GATE] FIRST CONTROLLED NATIVE XBOX TEST APPROVED. Do not delay for more canaries.'
