[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$n=Join-Path $base 'native_material_mapping'
$work=Join-Path $n 'pc_iwd_rawfile_integration'
$imgProofPath=Join-Path $n 'pc_iwd_image_roundtrip\aztec_pc_iwd_xenon_image_roundtrip.json'
$rawProofPath=Join-Path $n 'pc_rawfile_graft\pc_rawfile_graft_result.json'
$rawInputPath=Join-Path $n 'pc_rawfile_graft\pc_rawfile_graft_input.json'
$nativeProofPath=Join-Path $n 'native_graph_proof\native_graph_proof_result.json'
$identityPath=Join-Path $n 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$bind=Join-Path $n 'renderer_preflight\native_root_binding\nuclear_xenon_root_binding.json'
$stage4e=Join-Path $n 'renderer_preflight\4e_validation\aztec_4e_validation_result.json'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$oat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
foreach($p in @($imgProofPath,$rawProofPath,$rawInputPath,$nativeProofPath,$identityPath,$bind,$stage4e,$cli,$oat)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){throw "Required Aztec proof missing: $p"}
}
$ip=Get-Content -LiteralPath $imgProofPath -Raw | ConvertFrom-Json
$rp=Get-Content -LiteralPath $rawProofPath -Raw | ConvertFrom-Json
$rm=Get-Content -LiteralPath $rawInputPath -Raw | ConvertFrom-Json
$np=Get-Content -LiteralPath $nativeProofPath -Raw | ConvertFrom-Json
$nid=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$s4=Get-Content -LiteralPath $stage4e -Raw | ConvertFrom-Json
if([string]$ip.status -ne 'AZTEC_PC_IWD_XENON_IMAGE_ROUNDTRIP_PASS' -or
   [int]$ip.successfulXenonPixelReadbacks -ne 2 -or
   [int]$ip.successfulXenonHeaderReadbacks -ne 2 -or
   @($ip.individualImages).Count -ne 2 -or
   [string]$rp.status -ne 'AZTEC_PC_RAWFILE_GRAFT_OAT_READBACK_PASS' -or
   [int]$rp.xboxReadbacksExact -ne 114 -or
   [string]$rm.format -ne 'WaW360.AztecPcRawFileGraftInput.v1' -or
   @($rm.entries).Count -ne 114 -or
   [string]$np.status -ne 'IDENTITY_AND_TYPED_GRAPH_IMPORT_PASS' -or
   [string]$np.nativeSha256 -ine 'e0f3c244dd7b72d0fe71c05faf014c2c9bfb05f770183954b3e7cde625eba98f' -or
   [string]$s4.status -ne 'NATIVE_4E_BYTE_EMISSION_VALIDATION_PASS' -or
   -not [bool]$s4.validationPass -or
   -not [bool]$nid.TypeSequenceVerified -or
   @($nid.Entries).Count -ne 8509){
    throw 'Prior 2/2 PC images, 114/114 PC RawFiles, or native 8509-root layout proof is inconsistent.'
}
$native=[string]$np.verifiedGoldenXboxFastFile
if(-not (Test-Path -LiteralPath $native -PathType Leaf) -or
   [long](Get-Item -LiteralPath $native).Length -ne 82739469L -or
   (Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash -ine [string]$np.nativeSha256){
    throw 'Local native Aztec identity/layout reference has changed.'
}
if(-not (Test-Path -LiteralPath ([string]$ip.originalPcIwd) -PathType Leaf) -or
   (Get-FileHash -LiteralPath ([string]$ip.originalPcIwd) -Algorithm SHA256).Hash -ine [string]$ip.originalPcIwdSha256){
    throw 'The two original-PC-image IWD source archive changed.'
}
$images=@{}
foreach($r in @($ip.individualImages)){
    $name=([string]$r.Name).ToLowerInvariant()
    $file=Join-Path ([string]$ip.sourcePcIwiCorpus) (($name.Replace('/','\'))+'.iwi')
    if(-not [bool]$r.HeaderExact -or -not [bool]$r.PixelPayloadExact -or
       $images.ContainsKey($name) -or -not (Test-Path -LiteralPath $file -PathType Leaf) -or
       (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine [string]$r.SourcePcIwiSha256){
        throw "Two-image original PC source proof changed: $name"
    }
    $images[$name]=$r
}
if($images.Count -ne 2 -or -not $images.ContainsKey('goal') -or
   -not $images.ContainsKey('loadscreen_nazi_zombie_aztec')){
    throw 'Only the two proven original Aztec PC IWD image names are accepted.'
}
$rawNames=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach($e in @($rm.entries)){
    if(-not $rawNames.Add([string]$e.name) -or
       -not (Test-Path -LiteralPath ([string]$e.sourcePath) -PathType Leaf) -or
       [long](Get-Item -LiteralPath ([string]$e.sourcePath)).Length -ne [long]$e.bytes -or
       (Get-FileHash -LiteralPath ([string]$e.sourcePath) -Algorithm SHA256).Hash -ine [string]$e.sha256){
        throw "One of the 114 verified original PC RawFile bodies changed: $($e.name)"
    }
}
$previous=@(Get-ChildItem -LiteralPath ([string]$ip.xboxImageOnlyDiagnosticFolder) -Filter '*.ff' -File -Recurse -ErrorAction Stop)
if($previous.Count -ne 1){throw 'Expected exactly one proven PC-source image-only FF.'}
$twoImageFF=$previous[0].FullName
$graphRoot=Join-Path $work 'pc_xenon_image_graph'
$emitRoot=Join-Path $work 'emit'
$logs=Join-Path $work 'logs'
New-Item -Path $work,$logs -ItemType Directory -Force | Out-Null
if(Test-Path -LiteralPath $graphRoot -PathType Container){Remove-Item -LiteralPath $graphRoot -Recurse -Force}
if(Test-Path -LiteralPath $emitRoot -PathType Container){Remove-Item -LiteralPath $emitRoot -Recurse -Force}
New-Item -Path $graphRoot,$emitRoot -ItemType Directory -Force | Out-Null
$graphLog=Join-Path $logs 'image_graph_dump.log'
$buildLog=Join-Path $logs 'dotnet_build.log'
$emitLog=Join-Path $logs 'combined_emit.log'
$rawLog=Join-Path $logs 'rawfile_readback.log'
$imgLog=Join-Path $logs 'image_readback.log'
$status='AZTEC_PC_IWD_RAWFILE_COMBINED_GRAPH_BLOCKED'
$graphCount=0;$rawExact=0;$imgExact=0;$exit=-1;$err=''
$emitter=$null
$graphEvidence=New-Object 'System.Collections.Generic.List[object]'
Write-Host '[AZTEC-PC-MIX] ========================================================='
Write-Host '[AZTEC-PC-MIX] ORIGINAL PC IWD IMAGES + 114 PC RAWFILES IN 8509 ROOTS'
Write-Host '[AZTEC-PC-MIX] Source-owned pixels/data; native reference identity/shaders.'
Write-Host '[AZTEC-PC-MIX] SCRATCH FF ONLY. NOT FOR XBOX.'
Write-Host '[AZTEC-PC-MIX] ========================================================='
$temp=Join-Path ([IO.Path]::GetTempPath()) ('wawAzMix_'+[guid]::NewGuid().ToString('N').Substring(0,10))
if($temp.Length -gt 115){throw 'OAT graph scratch must have a genuinely short physical path.'}
$shortG=Join-Path $temp 'graph'
$shortR=Join-Path $temp 'rawfiles'
$shortI=Join-Path $temp 'images'
New-Item -Path $shortG,$shortR,$shortI -ItemType Directory -Force | Out-Null
try{
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $oat '--no-color' '--include-assets' 'image' '--image-format' 'IWI' '--output-folder' $shortG $twoImageFF 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $graphLog -Encoding UTF8
        $graphExit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    if($graphExit -ne 0 -or @(Select-String -LiteralPath $graphLog -Pattern '^\s*ERROR:').Count -ne 0){
        throw "PC-derived image-graph exporter failed (exit=$graphExit); see $graphLog"
    }
    $graphs=@(Get-ChildItem -LiteralPath (Join-Path $shortG 'xenon_image_graphs') -Filter '*.json' -File -Recurse -ErrorAction Stop)
    if($graphs.Count -ne 2){throw "Expected 2 actual PC-source Xenon image graphs, found $($graphs.Count)"}
    $seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach($f in $graphs){
        $g=Get-Content -LiteralPath $f.FullName -Raw | ConvertFrom-Json
        $name=([string]$g.name).ToLowerInvariant()
        if([string]$g.format -ne 'WaW360.T4Xenon.ImageGraph.v1' -or
           -not $images.ContainsKey($name) -or -not $seen.Add($name) -or
           [int]$g.cardMemoryBytes -ne [int]$g.pixelsBytes -or
           [int]$g.loadDef.textureHeaderBytes -ne 52){
            throw "PC image graph source/name or load definition mismatch: $name"
        }
        $pixel=Join-Path $shortG (([string]$g.pixelsFile).Replace('/','\'))
        $header=Join-Path $shortG (([string]$g.loadDef.textureHeaderFile).Replace('/','\'))
        if(-not (Test-Path -LiteralPath $pixel -PathType Leaf) -or
           -not (Test-Path -LiteralPath $header -PathType Leaf) -or
           [long](Get-Item -LiteralPath $pixel).Length -ne [long]$g.pixelsBytes -or
           [long](Get-Item -LiteralPath $header).Length -ne 52){
            throw "PC image Xenon pixel/header blob invalid: $name"
        }
        $graphEvidence.Add([pscustomobject]@{Name=$name;PixelBlobBytes=[long]$g.pixelsBytes
            PixelBlobSha256=(Get-FileHash -LiteralPath $pixel -Algorithm SHA256).Hash
            PcSourceSha256=[string]$images[$name].SourcePcIwiSha256})
    }
    $graphCount=$seen.Count
    $rcText=@(& robocopy.exe $shortG $graphRoot '/E' '/R:1' '/W:1' '/NP' '/NFL' '/NDL' '/NJH' '/NJS' 2>&1 |
        ForEach-Object {[string]$_})
    $rc=[int]$LASTEXITCODE
    if($rc -ge 8){throw "Long-path graph copy failed (robocopy code=$rc)"}
    Write-Host "[AZTEC-PC-MIX] Image graph dump: $graphCount real PC images; OAT exit=0"
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $buildLog -Encoding UTF8
        $buildExit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    if($buildExit -ne 0){throw "Incremental CLI build failed; see $buildLog"}
    $args=@('run','--no-build','-c','Release','--project',$cli,'--',
        'xenon-graph-emit',[string]$np.dumpRoot,$native,$identityPath,$bind,$emitRoot,
        'nazi_zombie_aztec',$oat,'70','436',
        '--aztec-pc-iwd-image-rawfile-graft',$graphRoot,'2',$rawInputPath)
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & dotnet @args 2>&1 | ForEach-Object {[string]$_} | Set-Content -LiteralPath $emitLog -Encoding UTF8
        $exit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    $emitPath=Join-Path $emitRoot 'nuclear_xenon_renderer_emit.json'
    if(-not (Test-Path -LiteralPath $emitPath -PathType Leaf)){
        throw "Combined root-emitter never produced a manifest (exit=$exit)."
    }
    $emitter=Get-Content -LiteralPath $emitPath -Raw | ConvertFrom-Json
    if($exit -ne 0 -or -not [bool]$emitter.ValidationPass -or
       -not [bool]$emitter.LayoutReplayExact -or
       -not [bool]$emitter.ExactIdentitySequence -or
       -not [bool]$emitter.OatZeroWarningsZeroErrors -or
       -not [bool]$emitter.ValidationOnly -or
       [bool]$emitter.ReadyForHardware -or
       [int]$emitter.NativeRoots -ne 8509 -or
       [int]$emitter.RealImageRoots -ne 2 -or
       [int]$emitter.RealRawFileRoots -ne 114 -or
       [int]$emitter.RealMaterialRoots -ne 0 -or
       [int]$emitter.RealXModelRoots -ne 0){
        throw "Combined 8509-root image+RawFile OAT/layout gate failed (exit=$exit)."
    }
    $output=[string]$emitter.OutputFastFile
    if(-not (Test-Path -LiteralPath $output -PathType Leaf) -or
       -not ([IO.Path]::GetFullPath($output)).StartsWith(
           ([IO.Path]::GetFullPath($emitRoot)).TrimEnd([char]'\')+'\',
           [StringComparison]::OrdinalIgnoreCase)){
        throw 'The combined diagnostic FF is not confined to the dedicated scratch output.'
    }
    Write-Host '[AZTEC-PC-MIX] 8509-root OAT loader + layout replay PASS, 2 images + 114 RawFiles'
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $oat '--no-color' '--include-assets' 'rawfile' '--output-folder' $shortR $output 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $rawLog -Encoding UTF8
        $rawExit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    if($rawExit -ne 0 -or @(Select-String -LiteralPath $rawLog -Pattern '^\s*ERROR:').Count -gt 0){
        throw "Combined Xbox RawFile Unlinker readback failed; see $rawLog"
    }
    foreach($e in @($rm.entries)){
        $file=Join-Path $shortR (([string]$e.name).Replace('/','\'))
        if(-not (Test-Path -LiteralPath $file -PathType Leaf) -or
           [long](Get-Item -LiteralPath $file).Length -ne [long]$e.bytes -or
           (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ine [string]$e.sha256){
            throw "PC RawFile no longer survives combined Xbox serialization: $($e.name)"
        }
        $rawExact++
    }
    Write-Host "[AZTEC-PC-MIX] Independent Xbox PC RawFile SHA readbacks=$rawExact/114"
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & $oat '--no-color' '--include-assets' 'image' '--image-format' 'IWI' '--output-folder' $shortI $output 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $imgLog -Encoding UTF8
        $imgExit=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    if($imgExit -ne 0 -or @(Select-String -LiteralPath $imgLog -Pattern '^\s*ERROR:').Count -gt 0){
        throw "Combined Xbox PC image Unlinker readback failed; see $imgLog"
    }
    $files=@(Get-ChildItem -LiteralPath $shortI -Filter '*.iwi' -File -Recurse -ErrorAction Stop)
    if($files.Count -ne 2){throw "Expected two PC image readbacks from combined FF, got $($files.Count)"}
    $seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach($f in $files){
        $relative=$f.FullName.Substring($shortI.Length).TrimStart([char]'\').Replace('\','/')
        if($relative.StartsWith('images/',[StringComparison]::OrdinalIgnoreCase)){$relative=$relative.Substring(7)}
        $name=$relative.Substring(0,$relative.Length-4).ToLowerInvariant()
        if(-not $images.ContainsKey($name) -or -not $seen.Add($name)){
            throw "Duplicate/unexpected combined Xbox image identity: $name"
        }
        $bytes=[IO.File]::ReadAllBytes($f.FullName)
        if($bytes.Length -lt 29 -or $bytes[0] -ne 73 -or $bytes[1] -ne 87 -or
           $bytes[2] -ne 105 -or $bytes[3] -ne 6 -or $bytes[4] -ne 13){
            throw "Combined Xbox PC IWI header invalid: $name"
        }
        $sha=[Security.Cryptography.SHA256]::Create()
        try{$hash=([BitConverter]::ToString($sha.ComputeHash($bytes,28,$bytes.Length-28))).Replace('-','')}
        finally{$sha.Dispose()}
        if($hash -ine [string]$images[$name].InputPayloadSha256){
            throw "Combined Xbox texture pixels changed from original PC IWI: $name"
        }
        $imgExact++
    }
    if($rawExact -ne 114 -or $imgExact -ne 2){throw 'Combined PC image/RawFile readbacks incomplete.'}
    $status='AZTEC_PC_IWD_RAWFILE_COMBINED_GRAPH_PASS'
}catch{
    $err=$_.Exception.Message
    Write-Host "[AZTEC-PC-MIX] BLOCKER: $err"
    foreach($log in @($graphLog,$buildLog,$emitLog,$rawLog,$imgLog)){
        if(Test-Path -LiteralPath $log -PathType Leaf){
            foreach($line in @(Get-Content -LiteralPath $log -Tail 6)){Write-Host "[AZTEC-PC-MIX] LOG: $line"}
        }
    }
}finally{
    if(Test-Path -LiteralPath $temp -PathType Container){Remove-Item -LiteralPath $temp -Recurse -Force}
}
$report=[ordered]@{
    schema='WaW360.AztecPcIwdRawfileCombinedGraph.v1'
    status=$status;nativeRoots=$(if($null -eq $emitter){0}else{[int]$emitter.NativeRoots})
    realPcImageRoots=$(if($null -eq $emitter){0}else{[int]$emitter.RealImageRoots})
    realPcRawFileRoots=$(if($null -eq $emitter){0}else{[int]$emitter.RealRawFileRoots})
    pcImageGraphs=$graphCount;pixelShaExactReadbacks=$imgExact;rawFileShaExactReadbacks=$rawExact
    emitterExitCode=$exit;blocker=$err
    originalPcImageProof=$imgProofPath;originalPcRawFileProof=$rawProofPath
    imageGraphSource=$twoImageFF;imageGraphInfo=$graphEvidence.ToArray()
    combinedScratchFF=$(if($null -eq $emitter){''}else{[string]$emitter.OutputFastFile})
    hardwareReady=$false;fullMapConverted=$false;validationOnly=$true
    pcOwnedMaterialsComplete=$false;pcOwnedModelsComplete=$false
    pcOwnedShaderTranslationComplete=$false;pcStringTablesComplete=$false
    convertedMapsPromotionApproved=$false
    note='Native-oracle shader/identity shell diagnostic combined with only two genuine original PC texture graphs and 114 real PC RawFiles. Zero retail or oracle Xbox image pixels used as production input. Not playable. Not for hardware.'
}
$reportPath=Join-Path $work 'aztec_pc_iwd_rawfile_integration.json'
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Host '[AZTEC-PC-MIX] ================= RESULT ================================'
Write-Host "[AZTEC-PC-MIX] status=$status"
Write-Host "[AZTEC-PC-MIX] Combined PC images=$imgExact/2; combined PC RawFiles=$rawExact/114; native roots=$(if($null -eq $emitter){0}else{[int]$emitter.NativeRoots})"
Write-Host "[AZTEC-PC-MIX] Report: $reportPath"
Write-Host '[AZTEC-PC-MIX] Diagnostic only. NEVER deploy on Xbox or promote to Converted Maps.'
if($status -ne 'AZTEC_PC_IWD_RAWFILE_COMBINED_GRAPH_PASS'){exit 4}
