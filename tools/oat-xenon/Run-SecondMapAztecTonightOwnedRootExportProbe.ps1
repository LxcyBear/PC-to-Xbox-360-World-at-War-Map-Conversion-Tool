[CmdletBinding()]
param(
    # Windows PowerShell 5.1 powershell.exe -File forwards comma-separated
    # values as ONE string. Validate after splitting, not on raw parameter.
    [string[]]$Families=@('stringtable','mapents','comworld','gameworldsp','gfxworld')
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$n=Join-Path $base 'native_material_mapping'
$censusPath=Join-Path $n 'tonight_critical_source_census\aztec_tonight_critical_source_census.json'
$sourcePath=Join-Path $n 'pc_fastfile_image_sources\pc_fastfile_image_sources.json'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
$work=Join-Path $n 'tonight_pc_critical_body_export_probe'
$logs=Join-Path $work 'logs'
$reportPath=Join-Path $work 'aztec_pc_critical_body_export_probe.json'
$textPath=Join-Path $work 'aztec_pc_critical_body_export_probe.txt'
foreach($p in @($censusPath,$sourcePath,$oat)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Aztec critical original-PC body export prerequisite missing: $p"
    }
}
$census=Get-Content -LiteralPath $censusPath -Raw | ConvertFrom-Json
$source=Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json
if([string]$census.schema -ne 'WaW360.AztecTonightCriticalSourceCensus.v1' -or
   [string]$census.status -ne 'AZTEC_TONIGHT_CRITICAL_PC_SOURCE_IDENTITY_CENSUS_PASS' -or
   [int]$census.nativeRoots -ne 8509 -or
   [bool]$census.readyForXbox){
    throw 'Original PC source owned-root census does not pass the required gate.'
}
$pins=@{}
foreach($role in @($census.pcSourceRoles)){
    $key=[string]$role.role
    if($pins.ContainsKey($key)){throw "Duplicate original PC role $key"}
    $matching=@($source.sourceFastfiles | Where-Object {[string]$_.Role -ieq $key})
    if($matching.Count -ne 1){throw "Missing original PC FastFile source hash for role $key"}
    if([string]$matching[0].SourceFastFile -ine [string]$role.sourceFastfile -or
       [string]$matching[0].Sha256 -ine [string]$role.sourceSha256 -or
       -not (Test-Path -LiteralPath ([string]$role.sourceFastfile) -PathType Leaf) -or
       (Get-FileHash -LiteralPath ([string]$role.sourceFastfile) -Algorithm SHA256).Hash -ine [string]$role.sourceSha256){
        throw "Original PC FastFile checksum/ownership changed for $key"
    }
    $pins[$key]=$role
}
if($pins.Count -ne 3){throw 'Expected exactly three pinned PC FastFile roles.'}
# Accept both -Families stringtable,mapents (powershell.exe -File) and
# an actual string[] supplied by a caller in an existing PowerShell session.
$allowed=@('stringtable','mapents','comworld','gameworldsp','gfxworld')
$selected=New-Object 'System.Collections.Generic.List[string]'
foreach($token in @($Families)){
    foreach($part in ([string]$token).Split([char]',')){
        $familyName=$part.Trim().ToLowerInvariant()
        if([string]::IsNullOrWhiteSpace($familyName) -or $familyName -notin $allowed){
            throw "Invalid -Families entry '$familyName'. Allowed: $($allowed -join ',')."
        }
        if(-not $selected.Contains($familyName)){$selected.Add($familyName)}
    }
}
if($selected.Count -eq 0){throw 'Select at least one original-PC asset family.'}
New-Item -Path $work,$logs -ItemType Directory -Force | Out-Null

$results=New-Object 'System.Collections.Generic.List[object]'
$failed=New-Object 'System.Collections.Generic.List[string]'
Write-Host '[AZTEC-PC-CRITICAL] ======================================================'
Write-Host '[AZTEC-PC-CRITICAL] TARGETED OAT ORIGINAL-PC CRITICAL ROOT BODY EXPORT'
Write-Host "[AZTEC-PC-CRITICAL] selected families=$($selected -join ',')"
Write-Host '[AZTEC-PC-CRITICAL] First diagnostic: no FF linking, no Xbox donor, no deployment.'
Write-Host '[AZTEC-PC-CRITICAL] ======================================================'

foreach($family in $selected){
    $roots=@($census.nativeOwnedRootClassifications | Where-Object {
        [string]$_.family -ieq $family
    })
    if($roots.Count -ne 1 -or [string]$roots[0].classification -ne 'PC_OWNED_NAME_PRESENT'){
        $failed.Add("$family missing unique original-PC owned root identity.")
        $results.Add([pscustomobject]@{
            family=$family;status='NO_UNIQUE_PC_OWNER';originalPcAssetName=$null
            ownerRoles=@();attempts=@();blocker='Expected exactly one source-owned PC name.'
        })
        Write-Host "[AZTEC-PC-CRITICAL] ${family}: no unique PC-owned target"
        continue
    }
    $name=[string]$roots[0].nativeName
    $owners=@($roots[0].pcOwnerRoles | Sort-Object -Unique)
    $attempts=New-Object 'System.Collections.Generic.List[object]'
    $ownedExport=0
    foreach($role in $owners){
        if(-not $pins.ContainsKey([string]$role)){throw "Unpinned original PC role $role"}
        $pcPath=[string]$pins[[string]$role].sourceFastfile
        $short=Join-Path ([IO.Path]::GetTempPath()) ('wazCrit_'+[guid]::NewGuid().ToString('N').Substring(0,8))
        if($short.Length -gt 105){throw 'OAT extraction requires short Windows TEMP path.'}
        $log=Join-Path $logs ($family+'_'+([string]$role).Replace('.','_')+'.log')
        $dest=Join-Path $work ('source_exports\'+$family+'\'+([string]$role).Replace('.','_'))
        if(Test-Path -LiteralPath $dest -PathType Container){
            Remove-Item -LiteralPath $dest -Recurse -Force
        }
        New-Item -Path $short,$dest -ItemType Directory -Force | Out-Null
        $exit=-1
        try{
            $previous=$ErrorActionPreference
            try{
                $ErrorActionPreference='Continue'
                & $oat '--no-color' '--include-assets' $family '--output-folder' $short $pcPath 2>&1 |
                    ForEach-Object {[string]$_} | Set-Content -LiteralPath $log -Encoding UTF8
                $exit=[int]$LASTEXITCODE
            }finally{$ErrorActionPreference=$previous}
            $zero=@(Select-String -LiteralPath $log -Pattern '(?i)^\s*Finished with\s+0\s+warnings?,\s+0\s+errors?\s*$').Count -gt 0
            $errorLines=@(Select-String -LiteralPath $log -Pattern '(?i)^\s*(ERROR:|WARNING:)' | Select-Object -First 6 | ForEach-Object {[string]$_.Line})
            $files=@(Get-ChildItem -LiteralPath $short -Recurse -File -ErrorAction Stop)
            $fileRows=New-Object 'System.Collections.Generic.List[object]'
            if($exit -eq 0 -and $zero -and $errorLines.Count -eq 0){
                foreach($file in $files){
                    $relative=$file.FullName.Substring($short.Length).TrimStart([char]'\').Replace('\','/')
                    $sha=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
                    # Preserve exact source bytes for later semantic analysis,
                    # not production promotion. Paths are generated under a short
                    # local OAT export directory and stay confined to output/.
                    $target=Join-Path $dest ($relative.Replace('/','\'))
                    New-Item -Path (Split-Path -Parent $target) -ItemType Directory -Force | Out-Null
                    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
                    if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ine $sha){
                        throw "Source-export copy checksum mismatch: $relative"
                    }
                    $fileRows.Add([pscustomobject]@{
                        relativePath=$relative;bytes=[long]$file.Length
                        sha256=$sha;localEvidencePath=$target
                    })
                }
            }
            # OAT can exit 0/0 while emitting ONLY zone_source metadata for
            # unsupported families (ComWorld/GameWorldSP/GfxWorld). Metadata
            # is not an exported asset body. An empty StringTable .csv is a
            # real asset file (though its content still needs verification).
            $assetRows=@($fileRows.ToArray() | Where-Object {
                -not ([string]$_.relativePath).StartsWith('zone_source/',[StringComparison]::OrdinalIgnoreCase)
            })
            $ok=$exit -eq 0 -and $zero -and $errorLines.Count -eq 0 -and $assetRows.Count -gt 0
            $outcome=if($ok){'PC_OAT_EXPORT_FILES_AVAILABLE'}else{'PC_OAT_BODY_EXPORT_BLOCKED'}
            if($ok){$ownedExport++}
            $attempts.Add([pscustomobject]@{
                role=[string]$role;sourceFastFile=$pcPath;sourceSha256=[string]$pins[[string]$role].sourceSha256
                oatExitCode=$exit;oatZeroWarningsZeroErrors=$zero
                exportedFiles=$fileRows.Count
                actualAssetBodyFiles=$assetRows.Count
                exportedFileInventory=$fileRows.ToArray()
                warningsOrErrors=$errorLines
                status=$outcome
                log=$log
            })
            Write-Host "[AZTEC-PC-CRITICAL] $family from $($role): $outcome, files=$($fileRows.Count), assetFiles=$($assetRows.Count), exit=$exit, OAT 0/0=$zero"
            foreach($sample in @($fileRows.ToArray() | Select-Object -First 4)){
                Write-Host "[AZTEC-PC-CRITICAL]   EXPORTED: $($sample.relativePath) ($($sample.bytes) bytes)"
            }
            foreach($err in $errorLines){
                Write-Host "[AZTEC-PC-CRITICAL]   OAT: $err"
            }
        }catch{
            $problem=$_.Exception.Message
            $failed.Add("$family/$($role) $problem")
            $attempts.Add([pscustomobject]@{
                role=[string]$role;sourceFastFile=$pcPath;oatExitCode=$exit
                exportedFiles=0;status='PC_OAT_EXPORT_EXCEPTION'
                reason=$problem;log=$log
            })
            Write-Host "[AZTEC-PC-CRITICAL] $family/$($role) BLOCKER: $problem"
        }finally{
            if(Test-Path -LiteralPath $short -PathType Container){
                Remove-Item -LiteralPath $short -Recurse -Force
            }
        }
    }
    $state=if($ownedExport -gt 0){'PC_SOURCE_EXPORT_AVAILABLE'}else{'PC_SOURCE_BODY_EXPORT_BLOCKED'}
    if($state -ne 'PC_SOURCE_EXPORT_AVAILABLE'){$failed.Add("${family}: no OAT 0/0 owned PC export produced a body file.")}
    $results.Add([pscustomobject]@{
        family=$family;nativeRoot=[int]$roots[0].nativeRoot
        originalPcAssetName=$name;ownerRoles=$owners
        status=$state;pcOwnerExportsSuccessful=$ownedExport
        attempts=$attempts.ToArray()
        payloadToXboxConverted=$false
    })
}
$status=if($failed.Count -eq 0){'AZTEC_PC_CRITICAL_REQUESTED_FAMILY_EXPORT_EVIDENCE_PASS'}else{'AZTEC_PC_CRITICAL_BODY_EXPORT_PARTIAL'}
$report=[ordered]@{
    schema='WaW360.AztecTonightCriticalBodyExport.v1'
    status=$status
    familiesRequested=$selected
    familyResults=$results.ToArray()
    blockers=$failed.ToArray()
    actualRootBodyProven=$false
    worldBodiesPromotedToXenon=0
    readyForXbox=$false
    safeToDeploy=$false
    note='Source-only OAT export inventories. A folder containing exported assets is not a proven matching world/StringTable body, and successful OAT export does not equal Xenon serialization or hardware readiness.'
}
$report | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $reportPath -Encoding UTF8
$text=New-Object 'System.Collections.Generic.List[string]'
$text.Add("Aztec targeted owned PC OAT critical exports: $status")
foreach($r in $results.ToArray()){$text.Add("FAMILY $($r.family): $($r.status) (owners=$($r.ownerRoles -join ','))")}
foreach($f in $failed.ToArray()){$text.Add("BLOCKER $f")}
$text.Add('No FastFile emitted, no donor bytes, no Xbox deployment.')
[IO.File]::WriteAllLines($textPath,[string[]]$text.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-CRITICAL] ================= RESULT ========================='
Write-Host "[AZTEC-PC-CRITICAL] status=$status"
Write-Host "[AZTEC-PC-CRITICAL] families=$($results.Count), blocked=$($failed.Count)"
Write-Host "[AZTEC-PC-CRITICAL] report=$textPath"
Write-Host '[AZTEC-PC-CRITICAL] SOURCE ONLY / NOT FOR XBOX.'
