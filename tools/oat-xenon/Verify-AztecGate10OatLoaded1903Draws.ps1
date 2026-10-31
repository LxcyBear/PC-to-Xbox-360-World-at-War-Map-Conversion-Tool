[CmdletBinding()]
param(
    [string]$ExpectedFfSha256='0BB41398C4236EF33CB9D60F923E09152966D52AA623DAEFC33D731A42630564'
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# INDEPENDENT second-pass OAT-loaded typed per-record readback of the
# first physically integrated 1903 native GfxWorld static draw structures.
# No FF build, no OAT build, no Xbox, no donor reference as output.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$work=Join-Path $base 'pc_g10_1903'
$ff=Join-Path $work 'scratch_emit\nazi_zombie_aztec.ff'
$gatePath=Join-Path $work 'aztec_pc_world_core_integrated_oat_run.json'
$sourceProofPath=Join-Path $base 'pc_gate8_static_draw_source\aztec_gate8_pc_static_draw_source_report.json'
$relocSummary=Join-Path $base 'pc_gate8_1903_draw_relocation_plan\aztec_1903_model_pointer_relocation_summary.json'
$lightDir=Join-Path $base 'pc_gate8_source_light_codec_candidate'
$dumper=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$capPath=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$out=Join-Path $work 'oat_loaded_1903_draw_readback'
$witnessCopy=Join-Path $out 'independently_loaded_gfxworld_static_draws.json'
$reportPath=Join-Path $out 'aztec_gate10_independent_1903_draw_readback.json'
$log=Join-Path $out 'independent_oat_dump.log'

function Need([string]$file,[string]$what){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing $what : $file"}
    return (Resolve-Path -LiteralPath $file).Path
}
function Sha([string]$file){return [string](Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash}
function Canon([string]$name){
    $v=$name.TrimStart(',').ToLowerInvariant()
    if($v.EndsWith('_wj')){$v=$v.Substring(0,$v.Length-3)}
    return $v
}
function Be32([byte[]]$bytes,[int]$at){
    return [uint32](
        (([uint64]$bytes[$at]) -shl 24) -bor
        (([uint64]$bytes[$at+1]) -shl 16) -bor
        (([uint64]$bytes[$at+2]) -shl 8) -bor
         ([uint64]$bytes[$at+3]))
}
function Approx([double]$x,[double]$y,[double]$eps){
    return ([math]::Abs($x-$y) -le $eps)
}

Need $ff 'actual source-owned GfxWorld FF'|Out-Null
Need $gatePath 'OAT integrated FF report'|Out-Null
Need $sourceProofPath 'original PC typed draw proof'|Out-Null
Need $relocSummary '1903 per-model XModel root plan'|Out-Null
Need $dumper 'already-built specialized 32-bit OAT Unlinker'|Out-Null
Need $capPath 'OAT installed ABI capability manifest'|Out-Null
if($ExpectedFfSha256.Length -ne 64 -or
   -not ($ExpectedFfSha256 -match '^[A-Fa-f0-9]{64}$') -or
   (Sha $ff) -ine $ExpectedFfSha256){
    throw 'The Gate10 OAT-PASS FF changed from the user-verified exact 0BB41398... build. Refusing a stale readback.'
}
$gate=Get-Content -LiteralPath $gatePath -Raw|ConvertFrom-Json
$srcProof=Get-Content -LiteralPath $sourceProofPath -Raw|ConvertFrom-Json
$planSummary=Get-Content -LiteralPath $relocSummary -Raw|ConvertFrom-Json
$caps=Get-Content -LiteralPath $capPath -Raw|ConvertFrom-Json
if([string]$gate.status -ne 'AZTEC_GATE10_1903_STATIC_DRAW_DIAGNOSTIC_OAT_PASS_STOCK_UNVERIFIED' -or
   -not [bool]$gate.OatPass -or
   [int]$gate.gate10DrawRecordsInEmittedFf -ne 1903 -or
   [int]$gate.gate10StockModelDrawUnverified -ne 1 -or
   [bool]$gate.hardwareReady -or [bool]$gate.gate10ProductionApproved -or
   [string]$srcProof.status -ne 'AZTEC_GATE8_1903_PC_TYPED_STATIC_DRAWS_SOURCE_PASS' -or
   [int]$srcProof.sourceDrawCount -ne 1903 -or
   [string]$planSummary.schema -ne 'WaW360.Aztec1903ModelRootRelocationPlanSummary.v1' -or
   [int]$planSummary.sourceDrawCount -ne 1903 -or
   [int]$planSummary.physicalXModelBodyDraws -ne 1902 -or
   [int]$planSummary.externalStockBodyIdentityDraws -ne 1 -or
   [bool]$planSummary.externalStockResidencyProven -or
   [string]$caps.format -ne 'WaW360.OatGraphDumperCapabilities.v1' -or
   @($caps.capabilities) -notcontains 't4xenon-gfxworld-static-draw-60b-loaded-witness-v1'){
    throw 'Original PC/FF/OAT provenance mismatch or specialized installed OAT capability is absent.'
}
$typedFile=Need ([string]$srcProof.typedDrawFile) 'original PC source typed rows'
if((Sha $typedFile) -ine [string]$srcProof.typedDrawSha256){
    throw 'Original PC typed draw source SHA changed.'
}
$typed=Get-Content -LiteralPath $typedFile -Raw|ConvertFrom-Json
$planFile=Need ([string]$planSummary.planFile) 'source-owned model-root per-draw plan'
if((Sha $planFile) -ine [string]$planSummary.planSha256){
    throw 'Original PC model-root plan SHA changed.'
}
$plan=Get-Content -LiteralPath $planFile -Raw|ConvertFrom-Json
$lightFiles=@(Get-ChildItem -LiteralPath $lightDir -Filter 'aztec_gate10_source_light_candidate_summary.json' -File -Recurse -ErrorAction Stop |
    Sort-Object LastWriteTime -Descending)
if($lightFiles.Count -lt 1){throw 'No source-light candidate from the exact Gate10 FF lineage.'}
$light=Get-Content -LiteralPath $lightFiles[0].FullName -Raw|ConvertFrom-Json
if([string]$light.status -ne 'AZTEC_GATE10_SOURCE_LIGHT_ARITHMETIC_MATCHES_ORIGINAL_XBOX_1903_NOT_PRODUCTION' -or
   [int]$light.reflectionIndexBaseHypothesisExactRows -ne 1903 -or
   [int]$light.lightingHandleAdd256HypothesisExactRows -ne 1903 -or
   [int]$light.convertedOwnedXModelPointersRetained -ne 1902 -or
   [bool]$light.productionApproved -or
   [bool]$light.nativeReferencePayloadBytesCopied){
    throw 'Source-owned light-scalar candidate is not the verified nonproduction input.'
}
$sourceBytesFile=Need ([string]$light.candidate) 'PC-derived native 1903-draw bytes'
if((Sha $sourceBytesFile) -ine [string]$light.candidateSha256){
    throw '1903 x 60B original-PC field candidate SHA changed.'
}
$source=[IO.File]::ReadAllBytes($sourceBytesFile)
if($source.Length -ne 1903*60 -or
   @($typed.records).Count -ne 1903 -or
   @($plan.records).Count -ne 1903){
    throw 'Expected 1903 x 60B source geometry, light, flags and relocation records.'
}
New-Item -ItemType Directory -Path $out -Force|Out-Null
$temp=Join-Path ([IO.Path]::GetTempPath()) ('waw_g10_oat_'+[guid]::NewGuid().ToString('N').Substring(0,10))
if($temp.Length -ge 130){throw 'Temporary native OAT output path too long.'}
New-Item -ItemType Directory -Path $temp -Force|Out-Null

Write-Host '[AZTEC-GATE10-READBACK] ===================================================='
Write-Host '[AZTEC-GATE10-READBACK] Independent OAT readback of the ALREADY BUILT 1903-draw GfxWorld FF.'
Write-Host '[AZTEC-GATE10-READBACK] No .NET compilation, FF rebuild, donor graft or Xbox deployment.'
Write-Host "[AZTEC-GATE10-READBACK] SHA-locked compressed FF=$ExpectedFfSha256"
Write-Host '[AZTEC-GATE10-PROGRESS] [#####---------------] 25% | OAT raw 1903-draw extraction'
$code=9999
$originalError=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    & $dumper '--no-color' '--include-assets' 'gfxworld' '--output-folder' $temp $ff 2>&1 |
        ForEach-Object { [string]$_ } | Set-Content -LiteralPath $log -Encoding UTF8
    $code=[int]$LASTEXITCODE
}finally{$ErrorActionPreference=$originalError}
if($code -ne 0){
    Get-Content -LiteralPath $log -Tail 25|ForEach-Object {Write-Host "[AZTEC-GATE10-READBACK] OAT $_"}
    throw "Native OAT draw dump failed with exit=$code. Check $log; existing FF unchanged."
}
$files=@(Get-ChildItem -LiteralPath $temp -Filter '*.json' -File -Recurse |
    Where-Object { $_.DirectoryName -match 'xenon_gfxworld_surface_abi_witness' })
if($files.Count -ne 1){
    throw "Expected exactly one OAT-dumped GfxWorld witness; got $($files.Count). See $temp"
}
Copy-Item -LiteralPath $files[0].FullName -Destination $witnessCopy -Force
$witness=Get-Content -LiteralPath $witnessCopy -Raw|ConvertFrom-Json
if([string]$witness.schema -ne 'WaW360.T4XenonGfxWorldSurfaceAbiWitness.v1' -or
   [string]$witness.status -ne 'NATIVE_XENON_GFXWORLD_SURFACE_ABI_LOADER_WITNESS' -or
   [string]$witness.platform -ne 'T4Xenon' -or
   -not [bool]$witness.loadedArraysAvailable -or
   -not [bool]$witness.nativeStaticDrawArrayAvailable -or
   [int]$witness.nativeStaticDrawCount -ne 1903 -or
   [int]$witness.nativeStaticDrawAbi.stride -ne 60 -or
   [int]$witness.nativeStaticDrawAbi.modelAt -ne 32 -or
   [int]$witness.nativeStaticDrawAbi.flagsAt -ne 40 -or
   @($witness.nativeStaticDraws).Count -ne 1903){
    throw "Independent OAT loader did not expose 1903 fully typed 60B GfxWorld static draws: $witnessCopy"
}
Write-Host '[AZTEC-GATE10-PROGRESS] [##############------] 70% | checking all 1903 physical typed draw records'
$matches=[ordered]@{
    ordinal=0;ownedXModelNames=0;nullStockReference=0
    cullDist=0;origin=0;scale=0;packedAxes=0
    reflectionIndex=0;primaryLightIndex=0;lightingHandle=0
    flags=0;zeroPadding=0
}
$mismatches=New-Object 'System.Collections.Generic.List[object]'
$rows=@($witness.nativeStaticDraws)
for($i=0;$i -lt 1903;$i++){
    $read=$rows[$i]
    $pc=$typed.records[$i]
    $owner=$plan.records[$i]
    $at=$i*60
    if([int]$read.ordinal -eq $i -and [int]$pc.ordinal -eq $i -and [int]$owner.ordinal -eq $i){
        $matches.ordinal++
    }else{
        if($mismatches.Count -lt 20){$mismatches.Add([ordered]@{ordinal=$i;field='ordinal';actual=[int]$read.ordinal})}
    }
    $expectedName=Canon ([string]$owner.pcModel)
    $actualName=Canon ([string]$read.model)
    if([string]$owner.bodyStatus -eq 'SOURCE_XMODEL_EMITTED'){
        if($expectedName -ceq $actualName -and $actualName.Length -gt 0){
            $matches.ownedXModelNames++
        }elseif($mismatches.Count -lt 20){
            $mismatches.Add([ordered]@{ordinal=$i;field='model';expected=$expectedName;actual=$actualName})
        }
    }elseif([string]$owner.bodyStatus -eq 'STOCK_REFERENCE_RESIDENCY_UNVERIFIED'){
        if([string]::IsNullOrWhiteSpace([string]$read.model) -and
           (Be32 $source ($at+32)) -eq 0){
            $matches.nullStockReference++
        }elseif($mismatches.Count -lt 20){
            $mismatches.Add([ordered]@{ordinal=$i;field='unverified-stock-model';actual=[string]$read.model})
        }
    }else{throw "Unknown model owner class at draw $i."}
    $rowChecks=[ordered]@{}
    $rowChecks.cullDist=Approx ([double]$read.cullDist) ([double]$pc.cullDist) 0.0001
    $rowChecks.scale=Approx ([double]$read.scale) ([double]$pc.scale) 0.00001
    $sameOrigin=(@($read.origin).Count -eq 3 -and @($pc.origin).Count -eq 3)
    if($sameOrigin){
        for($a=0;$a -lt 3;$a++){
            if(-not (Approx ([double]$read.origin[$a]) ([double]$pc.origin[$a]) 0.00001)){
                $sameOrigin=$false
            }
        }
    }
    $rowChecks.origin=$sameOrigin
    $sameAxis=@($read.packedAxisWords).Count -eq 3
    if($sameAxis){
        for($a=0;$a -lt 3;$a++){
            if([uint32]$read.packedAxisWords[$a] -ne (Be32 $source ($at+16+4*$a))){
                $sameAxis=$false
            }
        }
    }
    $rowChecks.packedAxes=$sameAxis
    $rowChecks.reflectionIndex=[int]$read.reflectionProbeIndex -eq [int]$source[$at+36]
    $rowChecks.primaryLightIndex=[int]$read.primaryLightIndex -eq [int]$source[$at+37]
    $be16=([int]$source[$at+38])*256+[int]$source[$at+39]
    $rowChecks.lightingHandle=[int]$read.lightingHandle -eq $be16
    $rowChecks.flags=[int]$read.flags -eq [int]$source[$at+40]
    $rowChecks.zeroPadding=[int]$read.paddingNonZeroBytes -eq 0
    foreach($field in @($rowChecks.Keys)){
        if([bool]$rowChecks[$field]){
            $matches[$field]++
        }elseif($mismatches.Count -lt 20){
            $mismatches.Add([ordered]@{ordinal=$i;field=$field})
        }
    }
}
$full=([int]$matches.ordinal -eq 1903 -and
       [int]$matches.ownedXModelNames -eq 1902 -and
       [int]$matches.nullStockReference -eq 1)
foreach($field in @('cullDist','origin','scale','packedAxes','reflectionIndex','primaryLightIndex','lightingHandle','flags','zeroPadding')){
    if([int]$matches[$field] -ne 1903){$full=$false}
}
$summary=[ordered]@{
    schema='WaW360.AztecGate10IndependentOat1903DrawReadback.v1'
    status=if($full){'AZTEC_GATE10_1903_ALL_SOURCE_DRAW_FIELDS_OAT_READBACK_PASS_STOCK_UNVERIFIED'}else{'AZTEC_GATE10_1903_SOURCE_DRAW_FIELDS_OAT_READBACK_MISMATCH'}
    originalPcDrawSourceSha256=[string]$srcProof.typedDrawSha256
    stage4EEmittedFf=$ff
    stage4EEmittedFfSha256=(Sha $ff)
    independentOatLoadedWitness=$witnessCopy
    independentOatLoadedWitnessSha256=(Sha $witnessCopy)
    actualNativeDrawCount=1903
    nativeRecordStride=60
    sourceOwnedXModelRows=1902
    unresolvedStockModelRows=1
    fieldMatchCounts=$matches
    mismatches=@($mismatches.ToArray())
    originalXboxDonorBodyBytesUsed=$false
    sourceLightRuntimeSemanticCorrectnessIndependentlyProven=$false
    stockHardwareResidencyProven=$false
    fullGfxWorldDpvsClosureProven=$false
    XboxHardwareReady=$false
    gate10ProductionComplete=$false
}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $reportPath -Encoding UTF8
foreach($field in @($matches.Keys)){
    Write-Host "[AZTEC-GATE10-READBACK] $field = $($matches[$field]) / $(if($field -eq 'ownedXModelNames'){1902}elseif($field -eq 'nullStockReference'){1}else{1903})"
}
Write-Host "[AZTEC-GATE10-READBACK] REPORT=$reportPath"
if(-not $full){
    Write-Host '[AZTEC-GATE10-READBACK] MISMATCH: inspected loaded fields are not all consistent with source; no Gate10 approval.'
    throw 'Independent original-PC field OAT readback failed. Repair exact mismatch before further FF builds.'
}
Write-Host '[AZTEC-GATE10-PROGRESS] [####################] 100% | all 1903 records independently checked'
Write-Host '[AZTEC-GATE10-READBACK] PASS: 1903/1903 typed draw records loaded correctly from the newly generated FF.'
Write-Host '[AZTEC-GATE10-READBACK] 1 unresolved stock body, runtime light semantics, 4 original-Xbox rotation differences and other world children still block production.'
Write-Host '[AZTEC-GATE10-READBACK] No FF modified or rebuilt. Not an Xbox test.'
