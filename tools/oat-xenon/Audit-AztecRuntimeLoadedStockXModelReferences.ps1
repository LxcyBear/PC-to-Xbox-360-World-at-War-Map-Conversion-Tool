[CmdletBinding()]
param(
    [string]$XboxGameRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\XBOX GAME FILES',
    [switch]$Rescan
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# READ-ONLY independent proof of PC comma-prefixed XModel source refs
# against the *actual* Xbox zones expected to be preloaded in T4 SP.
# A native-identity root or finding a name in a campaign zone is NOT
# proof the game can resolve it while loading Aztec. This script only
# considers real, non-reference xmodel entries in common.ff,
# code_post_gfx.ff and patch.ff, exact after stripping the PC comma.
# It NEVER copies donor bytes, creates fake owned mesh bodies, or approves
# a full native FF. The existing model FFs are not modified.
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$n = Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$auditPath = Join-Path $n 'pc_gate8_static_model_dependency_audit\aztec_gate8_static_draw_xmodel_dependencies.json'
$nativePath = Join-Path $n 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$stockCache = Join-Path $n 'pc_fastfile_image_sources\stock_identity_logs'
$oat = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$work = Join-Path $n 'pc_gate8_runtime_stock_xmodel_reference_audit'
$reportPath = Join-Path $work 'aztec_runtime_stock_xmodel_reference_audit.json'
$txtPath = Join-Path $work 'aztec_runtime_stock_xmodel_reference_audit.txt'
$zoneNames = @('common.ff','code_post_gfx.ff','patch.ff')
foreach($required in @($auditPath,$nativePath)){
    if(-not (Test-Path -LiteralPath $required -PathType Leaf)){
        throw "Prior original-PC Aztec source provenance missing: $required"
    }
}
if(-not(Test-Path -LiteralPath $XboxGameRoot -PathType Container)){
    throw "Locally owned Xbox retail game FF folder missing: $XboxGameRoot"
}
$source = Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json
$native = Get-Content -LiteralPath $nativePath -Raw | ConvertFrom-Json
if([string]$source.schema -ne 'WaW360.AztecGate8StaticDrawModelDependencyAudit.v1' -or
   [int]$source.sourceDrawInstances -ne 1903 -or
   [int]$source.distinctSourceStaticModelNames -ne 27 -or
   [string]$native.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or
   [int]$native.assets -ne 8509 -or
   -not [bool]$native.typeSequenceVerified){
    throw 'Original PC 27-model/1903-placements/native 8509-root evidence changed; no stock reference conclusion.'
}
New-Item -ItemType Directory -Path $work -Force | Out-Null
if(-not $Rescan -and -not(Test-Path -LiteralPath $stockCache -PathType Container)){
    Write-Host '[AZTEC-XMODEL-REF] Previous 67-zone OAT cache not found: re-indexing only required loaded zones.'
}
$existingCache=@()
if(Test-Path -LiteralPath $stockCache -PathType Container){
    $existingCache=@(Get-ChildItem -LiteralPath $stockCache -Filter '*.meta.json' -File)
}
$zones=New-Object 'System.Collections.Generic.List[object]'
$stockBodies=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
$stockStubs=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
foreach($zoneName in $zoneNames){
    $matches=@(Get-ChildItem -LiteralPath $XboxGameRoot -Filter $zoneName -File -Recurse)
    if($matches.Count -gt 1){
        throw "Multiple '$zoneName' zone files under $XboxGameRoot. Specify a narrower -XboxGameRoot; we cannot guess which Xbox executable loads."
    }
    if($matches.Count -ne 1){
        $zones.Add([pscustomobject]@{
            zone=$zoneName;found=$false;oatValid=$false;cacheReused=$false
            file='';log='';listedRealXModels=0;listedReferenceOnlyXModels=0
        })
        Write-Host "[AZTEC-XMODEL-REF] MISSING base FF: $zoneName (so stock runtime residency cannot be proven)"
        continue
    }
    $ff=$matches[0]
    $log=''
    $reused=$false
    if(-not $Rescan){
        foreach($c in $existingCache){
            try{
                $m=Get-Content -LiteralPath $c.FullName -Raw | ConvertFrom-Json
                $priorLog=$c.FullName.Substring(0,$c.FullName.Length-'.meta.json'.Length)
                if([string]$m.file -ieq $ff.FullName -and
                   [long]$m.bytes -eq [long]$ff.Length -and
                   [string]$m.modifiedUtc -eq $ff.LastWriteTimeUtc.ToString('o') -and
                   [int]$m.exitCode -eq 0 -and
                   (Test-Path -LiteralPath $priorLog -PathType Leaf)){
                    $log=$priorLog
                    $reused=$true
                    break
                }
            }catch{continue}
        }
    }
    if(-not $reused){
        if(-not(Test-Path -LiteralPath $oat -PathType Leaf)){
            throw "No cached successful Xbox stock OAT listing and Xenon Unlinker missing: $oat"
        }
        $log=Join-Path $work ('oat_'+$zoneName+'.list.log')
        $old=$ErrorActionPreference
        try{
            $ErrorActionPreference='Continue'
            & $oat '--list' '--no-color' $ff.FullName 2>&1 |
                ForEach-Object {[string]$_} |
                Set-Content -LiteralPath $log -Encoding UTF8
            $exitCode=[int]$LASTEXITCODE
        }finally{$ErrorActionPreference=$old}
        if($exitCode -ne 0){
            throw "Native OAT could not list stock Xbox FF '$zoneName', code=$exitCode log=$log"
        }
    }
    $real=0
    $stub=0
    foreach($line in [IO.File]::ReadLines($log)){
        $row=$line.Trim()
        if($row.StartsWith('[',[StringComparison]::Ordinal)){
            $close=$row.IndexOf(']')
            if($close -ge 0 -and $close+1 -lt $row.Length){
                $row=$row.Substring($close+1).TrimStart()
            }
        }
        if($row -notmatch '(?i)^xmodel\s*,\s*(.*?)\s*$'){continue}
        $name=([string]$Matches[1]).Trim().Trim('"')
        if([string]::IsNullOrWhiteSpace($name)){continue}
        # An OAT name prefixed with comma indicates a reference-only
        # entry in THIS stock FF, not a stock-resident mesh asset body.
        if($name.StartsWith(',',[StringComparison]::Ordinal)){
            $stub++
            $name=$name.TrimStart(',')
            if(-not $stockStubs.ContainsKey($name)){
                $stockStubs.Add($name,(New-Object 'System.Collections.Generic.List[string]'))
            }
            $stockStubs[$name].Add($zoneName)
        }else{
            $real++
            if(-not $stockBodies.ContainsKey($name)){
                $stockBodies.Add($name,(New-Object 'System.Collections.Generic.List[string]'))
            }
            $stockBodies[$name].Add($zoneName)
        }
    }
    $zones.Add([pscustomobject]@{
        zone=$zoneName;found=$true;oatValid=$true;cacheReused=$reused
        file=$ff.FullName;log=$log;listedRealXModels=$real;listedReferenceOnlyXModels=$stub
    })
    Write-Host "[AZTEC-XMODEL-REF] ${zoneName}: realStockModels=$real referenceOnlyNames=$stub cached=$reused"
}
$sourceReferences=@($source.modelDependencyRows | Where-Object {
    ([string]$_.pcModelName).StartsWith(',',[StringComparison]::Ordinal)
} | Sort-Object pcModelName)
$rows=New-Object 'System.Collections.Generic.List[object]'
$exactLoaded=0
$unresolved=0
foreach($ref in $sourceReferences){
    $pcName=[string]$ref.pcModelName
    $stockName=$pcName.TrimStart(',')
    $rootIndices=@($ref.nativeRootIndices)
    $hasOneRoot=$rootIndices.Count -eq 1
    $rootName=''
    $rootMatches=$false
    if($hasOneRoot){
        $idx=[int]$rootIndices[0]
        $nativeEntries=@($native.entries | Where-Object { [int]$_.Index -eq $idx })
        if($nativeEntries.Count -eq 1){
            $rootName=[string]$nativeEntries[0].Name
            $typeName=if($null -ne $nativeEntries[0].PSObject.Properties['OatTypeName']){
                [string]$nativeEntries[0].OatTypeName
            }else{[string]$nativeEntries[0].TypeName}
            $rootMatches=$typeName -ieq 'xmodel' -and
                $rootName.TrimStart(',') -ieq $stockName
        }
    }
    $ownedByLoadedZone=$stockBodies.ContainsKey($stockName)
    $hasStub=$stockStubs.ContainsKey($stockName)
    $bodyZones=if($ownedByLoadedZone){@($stockBodies[$stockName].ToArray())}else{@()}
    $stubZones=if($hasStub){@($stockStubs[$stockName].ToArray())}else{@()}
    $exactProof=$rootMatches -and $ownedByLoadedZone -and
        @($zones.ToArray()|Where-Object {-not $_.oatValid}).Count -eq 0
    if($exactProof){$exactLoaded++}else{$unresolved++}
    $state=if($exactProof){'STOCK_BODY_EXACT_NAME_IN_EXPECTED_PRELOADED_ZONE'}
        elseif($ownedByLoadedZone){'STOCK_BODY_NAME_FOUND_BUT_NATIVE_ROOT_UNRECONCILED'}
        elseif($hasStub){'ONLY_STOCK_REFERENCE_STUB_FOUND_NO_BODY_PROVEN'}
        else{'NO_STOCK_BODY_PROVEN'}
    $rows.Add([pscustomobject]@{
        pcReferenceName=$pcName;unmarkedStockName=$stockName
        originalPcStaticDraws=[int]$ref.occurrences
        nativeRootIndices=$rootIndices
        nativeRootName=$rootName
        exactNativeAliasMatch=$rootMatches
        expectedPreloadedStockBodyZones=$bodyZones
        onlyReferenceStubZones=$stubZones
        exactExpectedStockBodyFound=$exactProof
        runtimeHardwareValidated=$false
        status=$state
    })
    Write-Host "[AZTEC-XMODEL-REF] $pcName | nativeRoot='$rootName' | stockBodies=$($bodyZones -join ',') | proof=$state"
}
$allZonesAudited=@($zones.ToArray()|Where-Object {-not $_.oatValid}).Count -eq 0
$status=if($allZonesAudited -and $unresolved -eq 0){
    'AZTEC_SOURCE_MODEL_STOCK_REFERENCE_EXACT_RESOLUTION_AUDIT_PASS'
}else{
    'AZTEC_SOURCE_MODEL_STOCK_REFERENCE_RESOLUTION_UNPROVEN'
}
$report=[ordered]@{
    schema='WaW360.AztecExpectedLoadedStockXModelReferenceAudit.v1'
    status=$status
    targetSourceReferences=$sourceReferences.Count
    expectedLoadedZones=$zoneNames
    allExpectedLoadedZonesOatListed=$allZonesAudited
    exactSourceRootStockBodyMatches=$exactLoaded
    unresolvedSourceReferenceNames=$unresolved
    zones=@($zones.ToArray())
    references=@($rows.ToArray())
    xboxMapFfBuilt=$false
    runtimeHardwareValidated=$false
    note='An exact XModel body name in retail common/code_post_gfx/patch with a reconciled native root permits source reference classification; this is name/type OAT evidence only, NOT proof of hardware residency, graph equivalence or the other missing Aztec assets. Campaign maps/UI image lists do NOT establish stock loading.'
}
$report|ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $reportPath -Encoding UTF8
$text=New-Object 'System.Collections.Generic.List[string]'
$text.Add("AZTEC EXACT STOCK XMODEL REFERENT AUDIT: $status")
$text.Add("Expected loaded zones OAT listed: $allZonesAudited; exact matches: $exactLoaded/$($sourceReferences.Count)")
foreach($z in $zones){$text.Add("ZONE $($z.zone) oat=$($z.oatValid) realModels=$($z.listedRealXModels) cached=$($z.cacheReused)")}
foreach($r in $rows){$text.Add("SOURCE $($r.pcReferenceName) native='$($r.nativeRootName)' stock=$($r.expectedPreloadedStockBodyZones -join ',') status=$($r.status)")}
$text.Add('READ ONLY; NO SOURCE ASSET BYTES COPIED; NOT A HARDWARE OR COMPLETE-MAP APPROVAL.')
$text | Set-Content -LiteralPath $txtPath -Encoding UTF8
Write-Host "[AZTEC-XMODEL-REF] status=$status proven=$exactLoaded/$($sourceReferences.Count)"
Write-Host "[AZTEC-XMODEL-REF] report=$txtPath"
Write-Host "[AZTEC-XMODEL-REF] json=$reportPath"
Write-Host '[AZTEC-XMODEL-REF] NO MODEL GRAPH/MAIN FF MODIFIED. Hardware residency remains untested.'
