[CmdletBinding()]
param([switch]$ReusePcCorpus)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$n=Join-Path $base 'native_material_mapping'
$work=Join-Path $n 'pc_rawfile_graft'
$corpus=Join-Path $work 'pc_rawfile_corpus'
$emit=Join-Path $work 'emit'
$baselinePath=Join-Path $base 'second_map_baseline.json'
$identityPath=Join-Path $n 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$proofPath=Join-Path $n 'native_graph_proof\native_graph_proof_result.json'
$priorPath=Join-Path $n 'renderer_preflight\4e_validation\aztec_4e_validation_result.json'
$bind=Join-Path $n 'renderer_preflight\native_root_binding\nuclear_xenon_root_binding.json'
$pcOat=Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
$xboxOat=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
foreach($f in @($baselinePath,$identityPath,$proofPath,$priorPath,$bind,$pcOat,$xboxOat,$cli)) {
    if(-not (Test-Path -LiteralPath $f -PathType Leaf)) { throw "Required Aztec input missing: $f" }
}
$baseline=Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
$identity=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$proof=Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json
$prior=Get-Content -LiteralPath $priorPath -Raw | ConvertFrom-Json
$native=[string]$proof.verifiedGoldenXboxFastFile
if([string]$baseline.format -ne 'WaW360.SecondMapBaseline.v1' -or
   [string]$baseline.mapName -ine 'nazi_zombie_aztec_V2.2' -or
   [string]$prior.status -ne 'NATIVE_4E_BYTE_EMISSION_VALIDATION_PASS' -or
   -not [bool]$prior.validationPass -or
   [string]$proof.status -ne 'IDENTITY_AND_TYPED_GRAPH_IMPORT_PASS' -or
   [string]$proof.nativeSha256 -ine 'e0f3c244dd7b72d0fe71c05faf014c2c9bfb05f770183954b3e7cde625eba98f' -or
   -not [bool]$identity.TypeSequenceVerified -or
   [string]$identity.NativeFastFile -ine $native -or
   @($identity.Entries).Count -ne 8509) {
    throw 'PC baseline, native identity and previous 4E OAT PASS do not match.'
}
if (-not (Test-Path -LiteralPath $native -PathType Leaf) -or
    [long](Get-Item -LiteralPath $native).Length -ne 82739469L -or
    (Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash -ine
        'e0f3c244dd7b72d0fe71c05faf014c2c9bfb05f770183954b3e7cde625eba98f') {
    throw 'Golden Aztec reference changed since the verified Stage 4E baseline.'
}
$roots=@($identity.Entries | Where-Object { [string]$_.TypeName -ieq 'rawfile' })
if($roots.Count -ne 120) { throw "Expected 120 native RawFile roots, found $($roots.Count)" }
$sourceFFs=@($baseline.fastfiles | Where-Object { $_.Role -in @('map.ff','map_patch.ff','mod.ff') })
if(@($sourceFFs | Where-Object { $_.Role -eq 'map.ff' }).Count -ne 1) { throw 'PC map.ff role missing/ambiguous' }
$originHashes=@{}
foreach($source in $sourceFFs) {
    $src=[string]$source.Path
    if(-not (Test-Path -LiteralPath $src -PathType Leaf) -or
       [long](Get-Item -LiteralPath $src).Length -ne [long]$source.Bytes) { throw "PC source FF baseline changed: $src" }
    $originHashes[$src]=(Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
if (-not $ReusePcCorpus -and (Test-Path -LiteralPath $corpus -PathType Container)) {
    Remove-Item -LiteralPath $corpus -Recurse -Force
}
if(-not (Test-Path -LiteralPath $corpus -PathType Container)) {
    New-Item -Path $corpus -ItemType Directory -Force | Out-Null
}
New-Item -Path $emit -ItemType Directory -Force | Out-Null
Write-Host '[AZTEC-PC-RAW] PC source fastfiles -> REAL RawFile bodies -> Xbox 360 OAT readback'
Write-Host '[AZTEC-PC-RAW] Original 4E PASS kept intact; Converted Maps untouched.'

if ($ReusePcCorpus) {
    # Reuse the LAST completed PC OAT extraction, not any unproven corpus.
    # All source .ff SHA-256 values were recomputed above; pin the previous
    # manifest, verify every promoted source file's content, and require
    # successfully completed OAT extraction logs from each input role.
    $cachePath=Join-Path $work 'pc_rawfile_graft_input.json'
    if(-not (Test-Path -LiteralPath $cachePath -PathType Leaf)) {
        throw 'PC RawFile corpus reuse requires the previous source manifest.'
    }
    $cache=Get-Content -LiteralPath $cachePath -Raw | ConvertFrom-Json
    if([string]$cache.format -ne 'WaW360.AztecPcRawFileGraftInput.v1' -or
       [int]$cache.nativeRootCount -ne 8509 -or @($cache.entries).Count -eq 0) {
        throw 'Previously extracted PC RawFile corpus manifest is not verified.'
    }
    foreach($ff in $sourceFFs) {
        $role=[string]$ff.Role
        $log=Join-Path $work ('pc_rawfile_'+$role+'.log')
        if(-not (Test-Path -LiteralPath $log -PathType Leaf) -or
           @(Select-String -LiteralPath $log -Pattern '^\s*ERROR:').Count -ne 0) {
            throw "Prior PC extraction log missing or contains errors: $log"
        }
    }
    foreach($entry in @($cache.entries)) {
        $role=[string]$entry.sourceRole
        $source=[string]$entry.sourceFastFile
        if($role -notin @('map.ff','map_patch.ff','mod.ff') -or
           -not $originHashes.ContainsKey($source) -or
           [string]$entry.sourceFastFileSha256 -ine [string]$originHashes[$source]) {
            throw "PC input fastfile identity changed since last extraction: $source"
        }
        $expected=Join-Path (Join-Path $corpus $role) (([string]$entry.name).Replace('/','\'))
        if([IO.Path]::GetFullPath($expected) -ine [IO.Path]::GetFullPath([string]$entry.sourcePath) -or
           -not (Test-Path -LiteralPath $expected -PathType Leaf) -or
           [long](Get-Item -LiteralPath $expected).Length -ne [long]$entry.bytes -or
           (Get-FileHash -LiteralPath $expected -Algorithm SHA256).Hash -ine [string]$entry.sha256) {
            throw "Previously extracted PC RawFile changed: $expected"
        }
    }
    Write-Host "[AZTEC-PC-RAW] Reusing verified previous PC corpus: $(@($cache.entries).Count) source-bound roots, no OAT re-extraction."
} else {
foreach($source in $sourceFFs) {
    $role=[string]$source.Role
    $src=[string]$source.Path
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('wawazraw_'+[guid]::NewGuid().ToString('N').Substring(0,10))
    if($temp.Length -gt 115) { throw "OAT temp path too deep: $temp" }
    New-Item -Path $temp -ItemType Directory -Force | Out-Null
    $dest=Join-Path $corpus $role
    New-Item -Path $dest -ItemType Directory -Force | Out-Null
    $log=Join-Path $work ('pc_rawfile_'+$role+'.log')
    $count=0
    try {
        $saved=$ErrorActionPreference
        try {
            $ErrorActionPreference='Continue'
            & $pcOat '--no-color' '--include-assets' 'rawfile' '--output-folder' $temp $src 2>&1 |
                ForEach-Object { [string]$_ } | Set-Content -LiteralPath $log -Encoding UTF8
            $code=[int]$LASTEXITCODE
        } finally { $ErrorActionPreference=$saved }
        $errors=@(Select-String -LiteralPath $log -Pattern '^\s*ERROR:').Count
        if($code -ne 0 -or $errors -gt 0) {
            Get-Content -LiteralPath $log -Tail 10 | ForEach-Object { Write-Host $_ }
            throw "PC OAT extraction failed for $role; exit=$code errors=$errors"
        }
        # A short OAT physical temp root prevents writer MAX_PATH issues.
        # Robocopy (long-path-aware) transfers the corpus back to the single
        # existing ignored repo workspace, without PowerShell 5.1 Copy-Item
        # failing on long PC RawFile paths.
        $robolog=@(& robocopy.exe $temp $dest '/E' '/R:1' '/W:1' '/NP' '/NFL' '/NDL' '/NJH' '/NJS' 2>&1 |
            ForEach-Object { [string]$_ })
        $roboCode=[int]$LASTEXITCODE
        if($roboCode -ge 8) { throw "Long-path PC RawFile copy failed for $($role): robocopy code $roboCode." }
        foreach($root in $roots) {
            $name=([string]$root.Name).Replace('/','\')
            if($name.Contains('..') -or [IO.Path]::IsPathRooted($name)) { throw "Unsafe Root name $name" }
            $from=Join-Path $temp $name
            if(Test-Path -LiteralPath $from -PathType Leaf) { $count++ }
        }
    } finally {
        if(Test-Path -LiteralPath $temp -PathType Container) { Remove-Item -LiteralPath $temp -Recurse -Force }
    }
    Write-Host "[AZTEC-PC-RAW] $($role): $count PC RawFiles with native names"
}
}
$entries=New-Object 'System.Collections.Generic.List[object]'
$conflicts=New-Object 'System.Collections.Generic.List[string]'
foreach($root in $roots) {
    $name=[string]$root.Name
    $relative=$name.Replace('/','\')
    $variants=@()
    foreach($source in $sourceFFs) {
        $role=[string]$source.Role
        $src=[string]$source.Path
        $p=Join-Path (Join-Path $corpus $role) $relative
        if(-not (Test-Path -LiteralPath $p -PathType Leaf)) { continue }
        $variants+= [pscustomobject]@{role=$role;file=$src;path=$p
            fileSha=$originHashes[$src]
            sha=(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash
            bytes=[long](Get-Item -LiteralPath $p).Length}
    }
    if($variants.Count -eq 0) { continue }
    if(@($variants | Select-Object -ExpandProperty sha -Unique).Count -gt 1) {
        $conflicts.Add("PC source bytes conflict at root #$($root.Index) $name")
        continue
    }
    $v=$variants[0]
    $entries.Add([ordered]@{rootIndex=[int]$root.Index;name=$name;sourcePath=$v.path
        sourceRole=$v.role;sourceFastFile=$v.file;sourceFastFileSha256=$v.fileSha
        bytes=$v.bytes;sha256=$v.sha})
}
Write-Host "[AZTEC-PC-RAW] Exact PC-owned native roots=$($entries.Count)/120, conflicting names=$($conflicts.Count)"
if($entries.Count -eq 0) { throw 'No exact PC-owned RawFile bodies matched the native roots. No FF emitted.' }
$manifest=Join-Path $work 'pc_rawfile_graft_input.json'
@{format='WaW360.AztecPcRawFileGraftInput.v1';mapFolder='nazi_zombie_aztec_V2.2'
    canonicalBaseName='nazi_zombie_aztec';nativeRootCount=8509;entries=@($entries.ToArray())} |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifest -Encoding UTF8

$buildLog=Join-Path $work 'incremental_build.log'
$saved=$ErrorActionPreference
try {
    $ErrorActionPreference='Continue'
    & dotnet 'build' $cli '-c' 'Release' '--nologo' '-v:q' 2>&1 |
        ForEach-Object { [string]$_ } | Set-Content -LiteralPath $buildLog -Encoding UTF8
    $buildCode=[int]$LASTEXITCODE
} finally { $ErrorActionPreference=$saved }
if($buildCode -ne 0) {
    Get-Content -LiteralPath $buildLog -Tail 20 | ForEach-Object { Write-Host $_ }
    throw 'Incremental .NET graft validator build failed.'
}
$manifestOut=Join-Path $emit 'nuclear_xenon_renderer_emit.json'
if(Test-Path -LiteralPath $manifestOut -PathType Leaf){Remove-Item -LiteralPath $manifestOut -Force}
$cliArgs=@('run','--no-build','-c','Release','--project',$cli,'--',
    'xenon-graph-emit',[string]$proof.dumpRoot,$native,$identityPath,$bind,$emit,
    'nazi_zombie_aztec',$xboxOat,'70','436','--rawfile-graft',$manifest)
$emitLog=Join-Path $work 'pc_rawfile_emit.log'
$saved=$ErrorActionPreference
try {
    $ErrorActionPreference='Continue'
    & dotnet @cliArgs 2>&1 | ForEach-Object { [string]$_ } | Set-Content -LiteralPath $emitLog -Encoding UTF8
    $exitCode=[int]$LASTEXITCODE
} finally { $ErrorActionPreference=$saved }
$emitter=$null
if(Test-Path -LiteralPath $manifestOut -PathType Leaf) {
    $emitter=Get-Content -LiteralPath $manifestOut -Raw | ConvertFrom-Json
}
$pass=$exitCode -eq 0 -and $null -ne $emitter -and
    [bool]$emitter.ValidationPass -and [bool]$emitter.LayoutReplayExact -and
    [bool]$emitter.ExactIdentitySequence -and [bool]$emitter.OatZeroWarningsZeroErrors -and
    [int]$emitter.RealRawFileRoots -eq $entries.Count -and
    [int]$emitter.NativeRoots -eq 8509 -and [int]$emitter.RealImageRoots -eq 0 -and
    [int]$emitter.RealMaterialRoots -eq 0 -and [int]$emitter.RealXModelRoots -eq 0
$readback=0
if($pass) {
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('wawazchk_'+[guid]::NewGuid().ToString('N').Substring(0,10))
    New-Item -Path $temp -ItemType Directory -Force | Out-Null
    try {
        $readLog=Join-Path $work 'pc_rawfile_readback.log'
        $saved=$ErrorActionPreference
        try {
            $ErrorActionPreference='Continue'
            & $xboxOat '--no-color' '--include-assets' 'rawfile' '--output-folder' $temp ([string]$emitter.OutputFastFile) 2>&1 |
                ForEach-Object { [string]$_ } | Set-Content -LiteralPath $readLog -Encoding UTF8
            $readExit=[int]$LASTEXITCODE
        } finally { $ErrorActionPreference=$saved }
        if($readExit -ne 0 -or @(Select-String -LiteralPath $readLog -Pattern '^\s*ERROR:').Count -gt 0) {
            throw "Xbox RawFile readback Unlinker failed: $readLog"
        }
        foreach($entry in $entries) {
            $p=Join-Path $temp (([string]$entry.name).Replace('/','\'))
            if(-not (Test-Path -LiteralPath $p -PathType Leaf) -or
                [long](Get-Item -LiteralPath $p).Length -ne [long]$entry.bytes -or
                (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -ine [string]$entry.sha256) {
                throw "Xbox RawFile bytes differ at root #$($entry.rootIndex) $($entry.name)"
            }
            $readback++
        }
    } finally { if(Test-Path -LiteralPath $temp -PathType Container){Remove-Item -LiteralPath $temp -Recurse -Force} }
}
$status=if($pass -and $readback -eq $entries.Count) {'AZTEC_PC_RAWFILE_GRAFT_OAT_READBACK_PASS'} else {'AZTEC_PC_RAWFILE_GRAFT_BLOCKED'}
$report=[ordered]@{schema='WaW360.AztecPcRawFileGraftResult.v1';status=$status
    pcRawFileRoots=$entries.Count;nativeRawFileRoots=120;xboxReadbacksExact=$readback
    conflictingPcRoots=[string[]]$conflicts.ToArray();emitterExitCode=$exitCode
    outputFF=(Join-Path $emit 'nazi_zombie_aztec.ff');hardwareReady=$false
    convertedMapsPromotionApproved=$false
    note='Only actual PC RawFile bytes grafted, using native reference identity/order. Other assets remain placeholders; NOT FOR XBOX.'}
$reportPath=Join-Path $work 'pc_rawfile_graft_result.json'
$report | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Write-Host "[AZTEC-PC-RAW] status=$status; PC RawFiles=$($entries.Count)/120; exact Xbox readbacks=$readback"
Write-Host "[AZTEC-PC-RAW] Report: $reportPath"
Write-Host '[AZTEC-PC-RAW] NOT FOR XBOX. Converted Maps untouched.'
if (-not $pass -or $readback -ne $entries.Count) {
    Get-Content -LiteralPath $emitLog -Tail 15 | ForEach-Object { Write-Host "[AZTEC-PC-RAW] $_" }
    exit 4
}
