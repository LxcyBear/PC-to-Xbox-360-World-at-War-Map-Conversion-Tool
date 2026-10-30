[CmdletBinding()]
param(
    [string]$ModelName='foliage_dest_tree_jungle_02_fronds_dmg0'
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Isolate the Windows MAX_PATH regression with an already OAT-verified PC
# XModel -> Xenon standalone FF. NO main-world build and NO PC reconversion.
# Both file bytes and the actual extracted Xenon graph must be real.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$n=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$wavePath=Join-Path $n 'pc_gate8_static_model_wave\aztec_gate8_static_model_wave.json'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$work=Join-Path $repo '.deps\aztec8-all-static-xmodels'
$log=Join-Path $work 'aztec_short_path_single_model_probe.log'
foreach($p in @($wavePath,$oat)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){
        throw "Required existing source XModel manifest/OAT tool missing: $p"
    }
}
New-Item -ItemType Directory -Path $work -Force|Out-Null
$wave=Get-Content -LiteralPath $wavePath -Raw|ConvertFrom-Json
if([string]$wave.schema -ne 'WaW360.AztecGate8StandaloneXModelWave.v1' -or
   [int]$wave.sourceStaticModels -ne 27 -or
   [int]$wave.modelOnlyOatProven -lt 26){
    throw '26 original-PC XModel body proofs must be present before short-path test.'
}
$matches=@($wave.verifiedModels | Where-Object {
    [string]$_.model -ieq $ModelName
})
if($matches.Count -ne 1){
    throw "Model $ModelName is not a unique OAT-verified PC-owned source model."
}
$w=$matches[0]
$original=[IO.Path]::GetFullPath([string]$w.ff)
$expected=[string]$w.sha256
if($expected -notmatch '^[0-9a-fA-F]{64}$' -or
   -not(Test-Path -LiteralPath $original -PathType Leaf)){
    throw "Missing cached source model FF or SHA proof: $original"
}
if((Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -ine $expected){
    throw "Cached source model FF changed: $original"
}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('AzShort_'+[Guid]::NewGuid().ToString('N').Substring(0,8))
$short=Join-Path $temp 'source.ff'
if($temp.Length -ge 115 -or $short.Length -ge 240){
    throw "Temporary OAT probe path is not short enough: $short"
}
New-Item -ItemType Directory -Path $temp -Force|Out-Null
try{
    Copy-Item -LiteralPath $original -Destination $short
    if(-not(Test-Path -LiteralPath $short -PathType Leaf) -or
       (Get-FileHash -LiteralPath $short -Algorithm SHA256).Hash -ine $expected){
        throw "Short-path staged source FF does not match its verified bytes: $short"
    }
    Write-Host '[AZTEC-SHORT-OAT] ==============================================='
    Write-Host "[AZTEC-SHORT-OAT] Original cached PC->Xenon body input path chars=$($original.Length) : $original"
    Write-Host "[AZTEC-SHORT-OAT] Native OAT input path chars=$($short.Length) : $short"
    Write-Host "[AZTEC-SHORT-OAT] OAT input SHA256 preserved=$expected"
    $old=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $oat '--no-color' '--include-assets' 'xmodel' '--model-format' 'gltf' '--output-folder' $temp $short 2>&1 |
            ForEach-Object {[string]$_} |
            Set-Content -LiteralPath $log -Encoding UTF8
        $exit=[int]$LASTEXITCODE
    }finally{
        $ErrorActionPreference=$old
    }
    $lines=@(Get-Content -LiteralPath $log)
    $errs=@($lines | Where-Object {
        $_ -match '(?i)(?:^|\]\s*|\s)(?:ERROR\s*:|FATAL\s*:|fatal error\b|Unhandled exception\b)'
    })
    foreach($line in $errs){
        Write-Host "[AZTEC-SHORT-OAT] ACTUAL ERROR: $line"
    }
    $term=@($lines | Where-Object {
        $_ -match '(?i)Finished with\s+\d+\s+warnings?\s*,\s*\d+\s+errors?'
    })
    if($exit -ne 0 -or $errs.Count -gt 0 -or $term.Count -gt 1){
        throw "OAT reported genuine native extraction errors/ambiguous result (exit=$exit errors=$($errs.Count), summaries=$($term.Count)). Log=$log"
    }
    if($term.Count -eq 1){
        if($term[0] -notmatch '(?i)Finished with\s+(\d+)\s+warnings?\s*,\s+(\d+)\s+errors?' -or
           [int]$Matches[1] -ne 0 -or [int]$Matches[2] -ne 0){
            throw "OAT reported nonzero warnings/errors for model $ModelName. Log=$log"
        }
    }
    $dir=Join-Path $temp 'xenon_xmodel_graphs'
    if(-not(Test-Path -LiteralPath $dir -PathType Container)){
        throw "OAT could not output real Xenon relocatable graph directory: $dir (log=$log)"
    }
    $g=@(Get-ChildItem -LiteralPath $dir -File -Filter '*.json')
    if($g.Count -ne 1){
        throw "OAT must emit exactly one actual source-owned XModel graph, got $($g.Count). Log=$log"
    }
    $graph=Get-Content -LiteralPath $g[0].FullName -Raw | ConvertFrom-Json
    # Keep the exact same canonical-name rule as the real graph corpus
    # loader: external marker and optional '_wj' suffix are not new meshes.
    $canonicalActual=([string]$graph.name).Trim().Replace('\','/').TrimStart(',')
    $canonicalExpected=([string]$ModelName).Trim().Replace('\','/').TrimStart(',')
    if($canonicalActual.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){
        $canonicalActual=$canonicalActual.Substring(0,$canonicalActual.Length-3)
    }
    if($canonicalExpected.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){
        $canonicalExpected=$canonicalExpected.Substring(0,$canonicalExpected.Length-3)
    }
    if([string]$graph.format -ne 'WaW360.T4Xenon.XModelRelocatableGraph.v1' -or
       $canonicalActual -ine $canonicalExpected -or
       [int]$graph.abi.xmodel -ne 212 -or
       [int]$graph.unsupported.count -ne 0 -or
       [int]$graph.reusable.aliasCount -ne 0){
        throw "Native Xenon XModel graph exists but failed ABI/identity/provenance validation: $($g[0].FullName)"
    }
    Write-Host "[AZTEC-SHORT-OAT] PASS: $ModelName"
    Write-Host '[AZTEC-SHORT-OAT] REAL source FF SHA and extracted 212B Xenon XModel graph verified.'
    Write-Host "[AZTEC-SHORT-OAT] Log=$log"
    Write-Host '[AZTEC-SHORT-OAT] This was a one-model extraction TEST, not a complete world FF or hardware-ready package.'
}finally{
    if(Test-Path -LiteralPath $temp -PathType Container){
        Remove-Item -LiteralPath $temp -Force -Recurse -ErrorAction SilentlyContinue
    }
}