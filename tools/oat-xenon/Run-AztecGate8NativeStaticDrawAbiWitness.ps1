[CmdletBinding()]
param([switch]$BuildDumper)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
# Original Xbox T4Xenon 60B static draw ABI/light readback. This runner
# never modifies or emits any game FastFile and NEVER copies donor bodies.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$sourceReport=Join-Path $base 'pc_gate8_static_draw_source\aztec_gate8_pc_static_draw_source_report.json'
$axisReport=Join-Path $base 'pc_gate8_static_draw_xenon_axis_preflight_28_model_world\aztec_1903_xenon_axis_pack_preflight.json'
$bridgeReport=Join-Path $base 'pc_gate8_xenon_60b_draw_layout_bridge_28_model_world\aztec_1903_xenon_60b_draw_layout_bridge.json'
$priorNativeReport=Join-Path $base 'xenon_native_gfxworld_76_byte_surface_abi\native_xenon_gfxworld_surface_abi_reconciliation.json'
$capFile=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$out=Join-Path $base 'xenon_native_gate8_static_draw_60b_abi_witness'
$reportPath=Join-Path $out 'aztec_native_1903_static_draw_abi_witness_report.json'
foreach($p in @($sourceReport,$axisReport,$bridgeReport)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing Gate8F proof: $p"}
}
$s=Get-Content -LiteralPath $sourceReport -Raw|ConvertFrom-Json
$a=Get-Content -LiteralPath $axisReport -Raw|ConvertFrom-Json
$b=Get-Content -LiteralPath $bridgeReport -Raw|ConvertFrom-Json
if([string]$s.status -ne 'AZTEC_GATE8_1903_PC_TYPED_STATIC_DRAWS_SOURCE_PASS' -or
   [int]$s.sourceDrawCount -ne 1903 -or
   [int]$s.verifiedSourceWorldModelBodies -ne 28 -or
   [string]$a.status -ne 'AZTEC_GATE8_1903_PC_PLACEMENT_XENON_PACKED_AXES_PREFLIGHT_PASS' -or
   [string]$a.verifiedWorldMode -ne '28-model' -or
   [int]$a.encodedT4XenonUnitAxisVectors -ne 5709 -or
   [string]$b.status -ne 'AZTEC_GATE8_1903_NATIVE_60B_GEOMETRY_SKELETON_SOURCE_ONLY_PASS' -or
   [int]$b.pcSourceDraws -ne 1903 -or
   [int]$b.actualXboxDrawRecordsWritten -ne 0 -or
   [bool]$s.hardwareReady -or [bool]$a.hardwareReady -or [bool]$b.hardwareReady){
    throw 'Gate8F requires the unchanged original-PC 1903 source, packed axis PASS and 60B bridge PASS.'
}
function Test-Capability{
    if(-not(Test-Path -LiteralPath $capFile -PathType Leaf)){return $false}
    $c=Get-Content -LiteralPath $capFile -Raw | ConvertFrom-Json
    return ([string]$c.format -eq 'WaW360.OatGraphDumperCapabilities.v1' -and
        [string]$c.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($c.capabilities) -contains 't4xenon-gfxworld-static-draw-60b-loaded-witness-v1')
}
# Fast path: a prior OAT-loaded 76B native GfxWorld witness may already
# contain the 60B typed static draw array. Reuse it ONLY if the exact Xbox
# provenance and fully loaded 1903 records are present. This avoids an
# unnecessary repeated OAT extraction or special dumper build.
$reuse=$false
$prior=$null
$w=$null
$raw=''
if(Test-Path -LiteralPath $priorNativeReport -PathType Leaf){
    $candidate=Get-Content -LiteralPath $priorNativeReport -Raw | ConvertFrom-Json
    if([string]$candidate.status -eq 'AZTEC_NATIVE_XENON_76_BYTE_GFXSURFACE_ABI_LOADED_SOURCE_WITNESS' -and
       [string]$candidate.originalNativeSha256 -ieq 'E0F3C244DD7B72D0FE71C05FAF014C2C9BFB05F770183954B3E7CDE625EBA98F' -and
       -not [bool]$candidate.hardwareReady){
        $candidateRaw=[string]$candidate.rawNativeOatWitness
        if(-not [string]::IsNullOrWhiteSpace($candidateRaw) -and
           (Test-Path -LiteralPath $candidateRaw -PathType Leaf)){
            $candidateData=Get-Content -LiteralPath $candidateRaw -Raw | ConvertFrom-Json
            if([string]$candidateData.schema -eq 'WaW360.T4XenonGfxWorldSurfaceAbiWitness.v1' -and
               [string]$candidateData.status -eq 'NATIVE_XENON_GFXWORLD_SURFACE_ABI_LOADER_WITNESS' -and
               [bool]$candidateData.nativeStaticDrawWitnessOnly -and
               [bool]$candidateData.nativeStaticDrawArrayAvailable -and
               [int]$candidateData.nativeStaticDrawCount -eq 1903 -and
               @($candidateData.nativeStaticDraws).Count -eq 1903){
                $prior=$candidate
                $w=$candidateData
                $raw=$candidateRaw
                $reuse=$true
                Write-Host '[AZTEC-GATE8-NATIVE] CACHE HIT: 1903 original Xbox static draw rows are already OAT-loaded; skipping native OAT and rebuild.'
            }
        }
    }
}
if(-not $reuse){
    Write-Host '[AZTEC-GATE8-NATIVE] CACHE MISS: native 1903-draw loaded OAT evidence absent or incomplete.'
    if(-not(Test-Capability) -and $BuildDumper){
        Write-Host '[AZTEC-GATE8-NATIVE] Building specialized OAT once because its native static draw capability is missing.'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1')
        if($LASTEXITCODE -ne 0){throw 'Specialized native Xbox OAT build failed.'}
    }
    if(-not(Test-Capability)){
        throw 'Native 1903-draw OAT witness capability missing. Run THIS script with -BuildDumper ONCE.'
    }
    # Only a cache miss needs to rerun the proven native FF OAT loader.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Run-SecondMapAztecXenonGfxWorldSurfaceAbiWitness.ps1')
    if($LASTEXITCODE -ne 0){throw 'Pinned original Xbox GfxWorld OAT witness failed.'}
    if(-not(Test-Path -LiteralPath $priorNativeReport -PathType Leaf)){
        throw 'Original Xbox GfxWorld OAT witness report missing.'
    }
    $prior=Get-Content -LiteralPath $priorNativeReport -Raw | ConvertFrom-Json
    if([string]$prior.status -ne 'AZTEC_NATIVE_XENON_76_BYTE_GFXSURFACE_ABI_LOADED_SOURCE_WITNESS' -or
       [string]$prior.originalNativeSha256 -ine 'E0F3C244DD7B72D0FE71C05FAF014C2C9BFB05F770183954B3E7CDE625EBA98F' -or
       [bool]$prior.hardwareReady){
        throw 'Original Xbox native FF provenance changed after OAT run.'
    }
    $raw=[string]$prior.rawNativeOatWitness
    if(-not(Test-Path -LiteralPath $raw -PathType Leaf)){
        throw "OAT raw native GfxWorld witness missing: $raw"
    }
    $w=Get-Content -LiteralPath $raw -Raw | ConvertFrom-Json
}
$abi=$w.nativeStaticDrawAbi
$rows=@($w.nativeStaticDraws)
if([string]$w.schema -ne 'WaW360.T4XenonGfxWorldSurfaceAbiWitness.v1' -or
   [string]$w.status -ne 'NATIVE_XENON_GFXWORLD_SURFACE_ABI_LOADER_WITNESS' -or
   -not [bool]$w.nativeStaticDrawWitnessOnly -or
   -not [bool]$w.nativeStaticDrawArrayAvailable -or
   [int]$w.nativeStaticDrawCount -ne 1903 -or $rows.Count -ne 1903 -or
   [int]$abi.stride -ne 60 -or [int]$abi.placementStride -ne 28 -or
   [int]$abi.cullDistAt -ne 0 -or [int]$abi.placementAt -ne 4 -or
   [int]$abi.originAt -ne 4 -or [int]$abi.axisAt -ne 16 -or
   [int]$abi.scaleAt -ne 28 -or [int]$abi.modelAt -ne 32 -or
   [int]$abi.reflectionAt -ne 36 -or
   [int]$abi.primaryLightAt -ne 37 -or
   [int]$abi.lightingHandleAt -ne 38 -or
   [int]$abi.flagsAt -ne 40 -or [int]$abi.padAt -ne 41){
    throw 'Independent original Xbox native static draw layout or 1903-count ABI not proven.'
}
$src=[string]$s.typedDrawFile
if(-not(Test-Path -LiteralPath $src -PathType Leaf) -or
   (Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash -ine [string]$s.typedDrawSha256){
    throw 'Original PC typed draw source changed after proof.'
}
$typed=Get-Content -LiteralPath $src -Raw | ConvertFrom-Json
$axisFile=[string]$a.packedAxesReport
if(-not(Test-Path -LiteralPath $axisFile -PathType Leaf)){throw '1903 packed-axis candidate sidecar missing.'}
$candidate=Get-Content -LiteralPath $axisFile -Raw | ConvertFrom-Json
if(@($typed.records).Count -ne 1903 -or @($candidate.records).Count -ne 1903){
    throw 'Original-PC source or packed-axis rows no longer total 1903.'
}
function Canon([string]$v){
    $v=$v.TrimStart(',').ToLowerInvariant()
    if($v.EndsWith('_wj')){$v=$v.Substring(0,$v.Length-3)}
    return $v
}
$names=0;$axesEqual=0;$origins=0;$scales=0;$culls=0
$handles=@{};$flags=@{};$padNonZero=0;$nullModels=0
$examples=New-Object 'System.Collections.Generic.List[object]'
for($i=0;$i -lt 1903;$i++){
    $r=$rows[$i];$p=$typed.records[$i];$x=$candidate.records[$i]
    if([int]$r.ordinal -ne $i -or [int]$p.ordinal -ne $i -or
       [int]$x.ordinal -ne $i -or
       @($r.packedAxisWords).Count -ne 3 -or @($r.origin).Count -ne 3){
        throw "Native vs PC static draw ordinal or shape mismatch at $i."
    }
    $sameName=(Canon ([string]$r.model)) -ceq (Canon ([string]$p.model))
    if($sameName){$names++}
    if([string]::IsNullOrWhiteSpace([string]$r.model)){$nullModels++}
    $sameAxis=$true;$sameOrigin=$true
    for($j=0;$j -lt 3;$j++){
        $packed=[Convert]::ToUInt32(([string]$x.axisPackedWords[$j]).Substring(2),16)
        if([uint32]$r.packedAxisWords[$j] -ne $packed){$sameAxis=$false}
        if([Math]::Abs([double]$r.origin[$j]-[double]$p.origin[$j]) -gt 0.00001){$sameOrigin=$false}
    }
    if($sameAxis){$axesEqual++}
    if($sameOrigin){$origins++}
    if([Math]::Abs([double]$r.scale-[double]$p.scale) -le 0.00001){$scales++}
    if([Math]::Abs([double]$r.cullDist-[double]$p.cullDist) -le 0.0001){$culls++}
    $handle=[string]([int]$r.lightingHandle);$flag=[string]([int]$r.flags)
    if(-not $handles.ContainsKey($handle)){$handles[$handle]=0}
    if(-not $flags.ContainsKey($flag)){$flags[$flag]=0}
    $handles[$handle]++;$flags[$flag]++
    if([int]$r.paddingNonZeroBytes -gt 0){$padNonZero++}
    if($examples.Count -lt 16 -and (-not $sameName -or -not $sameAxis -or -not $sameOrigin)){
        $examples.Add([ordered]@{
            ordinal=$i; pcModel=[string]$p.model; xboxModel=[string]$r.model
            nameEqual=$sameName;axisEqual=$sameAxis;originEqual=$sameOrigin
        })
    }
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
$result=[ordered]@{
    schema='WaW360.AztecGate8NativeStaticDraw60ByteWitness.v1'
    status='AZTEC_GATE8_ORIGINAL_XBOX_1903_NATIVE_STATIC_DRAW_ABI_LOADED_PASS'
    originalXboxGfxWorldWitnessSha256=(Get-FileHash -LiteralPath $raw -Algorithm SHA256).Hash
    originalPcTypedDrawSourceSha256=[string]$s.typedDrawSha256
    nativeAbi=$abi
    sourceDraws=1903
    independentlyOatLoadedNativeDraws=1903
    nativeModelsNull=$nullModels
    ordinalModelNamesEqual=$names
    ordinalPackedAxesExact=$axesEqual
    ordinalOriginNear=$origins
    ordinalScaleNear=$scales
    ordinalCullDistNear=$culls
    nativeLightingHandleHistogram=@($handles.GetEnumerator() | Sort-Object Name | ForEach-Object {
        [ordered]@{value=[int]$_.Key;count=[int]$_.Value}
    })
    nativeFlagsHistogram=@($flags.GetEnumerator() | Sort-Object Name | ForEach-Object {
        [ordered]@{value=[int]$_.Key;count=[int]$_.Value}
    })
    nativeRowsWithNonzeroPadding=$padNonZero
    firstSourceVsNativeDifferences=@($examples.ToArray())
    originalXboxLightingCopiedIntoConvertedFf=$false
    originalXboxGeometryCopiedIntoConvertedFf=$false
    xenonModelPointersRelocated=0
    actualXboxDrawRecordsWritten=0
    sourceFfModified=$false
    hardwareReady=$false
    note='Independent original-Xbox native FF loaded via pinned OAT: ABI, light/flags distributions, and source ordinal comparisons only. Native donor data are NOT imported. Real PC->Xbox lighting mapping, 1903 XModel relocations and GfxWorld OAT FF graft remain pending.'
}
$result|ConvertTo-Json -Depth 10|Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Host '[AZTEC-GATE8-NATIVE] =================================================='
Write-Host "[AZTEC-GATE8-NATIVE] PASS: $($result.status)"
Write-Host "[AZTEC-GATE8-NATIVE] 1903 native Xbox draw records LOADED, ABI stride=$($abi.stride)B; model +32, lighting +38, flags +40"
Write-Host "[AZTEC-GATE8-NATIVE] ordinal equal: model=$names/1903 axes=$axesEqual/1903 origin=$origins/1903 scale=$scales/1903 cull=$culls/1903"
Write-Host "[AZTEC-GATE8-NATIVE] distinct light handles=$($handles.Count) flag values=$($flags.Count) rows with nonzero pad=$padNonZero"
Write-Host "[AZTEC-GATE8-NATIVE] report=$reportPath"
Write-Host '[AZTEC-GATE8-NATIVE] NO FF MODIFICATION, NO DONOR DATA GRAFT, 0/1903 DRAW LINKS'
Write-Host '[AZTEC-GATE8-NATIVE] =================================================='
