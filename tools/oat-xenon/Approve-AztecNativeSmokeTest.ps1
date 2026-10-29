[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$PackageDirectory,
    [string]$MapName = 'nazi_zombie_aztec',
    [string]$UnlinkerExe,
    [string]$ReportDirectory,
    [switch]$SkipWorldGfxWorldCheck
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# SINGLE STRICT HARDWARE-TEST RELEASE RULE:
# (1) FINAL map.ff and map_load.ff BOTH pass native T4Xenon OAT loading,
#     exit 0, and OAT prints 0 warnings and 0 errors.
# (2) The known Aztec CoD Xe usermap package is complete: both final
#     FFs + map.json + preview.dds + sounds\ (streamed XMA if required).
#     highmip\ and other known-working extras are preserved if present;
#     no separate .assets requirement is assumed.
# Native Xbox GfxWorld verification runs as PART of OAT FF validation,
# not as a new pre-hardware diagnostic campaign. Never approve a shell FF.
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
$candidates+=,(Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe')
$candidates+=,(Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe')
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
    [pscustomobject]@{name='map.json';present=(Test-Path -LiteralPath (Join-Path $package 'map.json') -PathType Leaf)},
    [pscustomobject]@{name='preview.dds';present=(Test-Path -LiteralPath (Join-Path $package 'preview.dds') -PathType Leaf)}
)
$metadataPresent=@($metadata | Where-Object {-not $_.present}).Count -eq 0
$fullFF=$ffResults.Count -eq 2 -and
    @($ffResults | Where-Object {-not $_.oatNativeLoadPass}).Count -eq 0
# True FF validity means OAT loads the actual world asset and its 735
# original-PC-owned surfaces, not just the 8,509 identity/name shells.
# The existing pinned specialized OAT witness checks the loaded GfxWorld
# in the same release audit. No extra standalone debug/test milestone.
$worldProof=$false
$worldReason='Native Xbox GfxWorld proof not attempted.'
$worldEvidence=''
if($fullFF -and -not $SkipWorldGfxWorldCheck){
    $special=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
    if(-not(Test-Path -LiteralPath $special -PathType Leaf)){
        $worldReason='Specialized pinned OAT Unlinker for real GfxWorld source loader not found.'
    }else{
        $worldDir=Join-Path $reportDir 'loaded_main_gfxworld'
        New-Item -ItemType Directory -Force -Path $worldDir | Out-Null
        $nativeLog=New-Object 'System.Collections.Generic.List[string]'
        $worldExit=-1
        $old=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            $global:LASTEXITCODE=$null
            try{
                & $special '--no-color' '--include-assets' 'gfxworld' '--output-folder' $worldDir $main 2>&1 |
                    ForEach-Object {$nativeLog.Add([string]$_)}
                if($null -ne $LASTEXITCODE){$worldExit=[int]$LASTEXITCODE}
            }catch{$nativeLog.Add("World OAT check error: $_")}
        }finally{$ErrorActionPreference=$old}
        $worldLogFile=Join-Path $reportDir 'loaded_main_gfxworld_oat.log'
        [IO.File]::WriteAllLines($worldLogFile,[string[]]$nativeLog.ToArray(),[Text.UTF8Encoding]::new($false))
        $worldFiles=@(Get-ChildItem -LiteralPath (Join-Path $worldDir 'xenon_gfxworld_surface_abi_witness') -Filter '*.json' -File -ErrorAction SilentlyContinue)
        if($worldExit -eq 0 -and $worldFiles.Count -eq 1){
            $world=Get-Content -LiteralPath $worldFiles[0].FullName -Raw | ConvertFrom-Json
            $worldProof=[bool]$world.loadedArraysAvailable -and
                [string]$world.status -eq 'NATIVE_XENON_GFXWORLD_SURFACE_ABI_LOADER_WITNESS' -and
                [int]$world.surfaceCount -eq 735 -and
                [int]$world.materialMemoryCount -eq 39 -and
                [int]$world.vertexCount -eq 32713 -and
                [int]$world.indexCount -eq 58653 -and
                [int]$world.triangles -eq 19551 -and
                [int]$world.directMatches -eq 735 -and
                [int]$world.unmatched -eq 0 -and
                [int]$world.ambiguous -eq 0
            $worldEvidence=$worldFiles[0].FullName
            $worldReason=if($worldProof){'Final PC-origin Xbox GfxWorld and 735 real linked surfaces loaded in OAT.'}else{'OAT parsed GfxWorld but source world surfaces/materials remain incomplete or do not match the expected map.'}
        }else{
            $worldReason="Pinned OAT GfxWorld witness exit=$worldExit fileCount=$($worldFiles.Count); see $worldLogFile"
        }
    }
}
if($SkipWorldGfxWorldCheck){
    # Provided solely to inspect package/OAT FF metadata on a system that
    # cannot run the specialized witness. NEVER authorizes hardware tests.
    $worldReason='World check skipped explicitly; hardware approval forbidden.'
}
$complete=$hasSounds -and $metadataPresent -and $fullFF
$pass=$complete -and $worldProof -and -not $SkipWorldGfxWorldCheck
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
    metadataBlocking=$true
    realMainGfxWorldAnd735MaterialReferencesOatPass=$worldProof
    realMainGfxWorldAuditResult=$worldReason
    realMainGfxWorldAuditFile=$worldEvidence
    completeCodxePackage=$complete
    readyForFirstControlledHardwareSmokeTest=$pass
    runtimeGameplayGuaranteed=$false
    originalFilesUntouched=$true
    oatAuditLog=$logFile
    note='First hardware TEST approval requires two criteria: (1) both FINAL Xbox .ff files individually pass native T4Xenon OAT with exit 0 and zero warnings/errors, including genuine non-shell source GfxWorld and 735 real Material references in main FF; (2) Aztec CoD Xe package contains both FastFiles, streamed sounds folder and its known map.json / preview.dds metadata. No standalone canary, gameplay perfection, weapon/GSC-script error fix, or crash-free guarantee is required. Preserves optional highmip/other package contents.'
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportFile -Encoding UTF8
Write-Host "[AZTEC-HARDWARE-GATE] soundsPresent=$hasSounds streamedFiles=$(@($soundFiles).Count) map.json=$($metadata[0].present) preview.dds=$($metadata[1].present)"
Write-Host "[AZTEC-HARDWARE-GATE] MAIN_FF_SOURCE_GFXWORLD_OAT_PASS=$worldProof reason=$worldReason"
Write-Host "[AZTEC-HARDWARE-GATE] BOTH_FINAL_FFS_OAT_PASS=$fullFF COMPLETE_CODEXE_PACKAGE=$complete"
Write-Host "[AZTEC-HARDWARE-GATE] status=$($result.status)"
Write-Host "[AZTEC-HARDWARE-GATE] report=$reportFile"
if(-not $pass){
    throw 'Native hardware test BLOCKED: final two FastFiles must each OAT load, main must contain the real linked GfxWorld (not a shell), and sounds\ + map.json + preview.dds must be present. Review audit report.'
}
Write-Host '[AZTEC-HARDWARE-GATE] FIRST CONTROLLED NATIVE XBOX TEST APPROVED. Do not delay for more canaries.'
