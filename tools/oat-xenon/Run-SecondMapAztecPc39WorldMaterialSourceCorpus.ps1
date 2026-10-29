[CmdletBinding()]
param([switch]$BuildDumper)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$witness=Join-Path $base 'pc_oat_loaded_gfxworld_material_pointer_witness\pc_oat_gfxworld_material_pointer_reconciliation.json'
$worklist=Join-Path $base 'pc_verified_xbox_surface_material_relocation_worklist\aztec_verified_surface_material_relocation_worklist.json'
$alias=Join-Path $base 'pc_xenon_world_surface_material_root_alias_link\aztec_735_gfxsurfaces_material_root_alias_link_audit.json'
$geometry=Join-Path $base 'pc_gfxworld_xenon_geometry_components\gfxworld_source_to_xenon_geometry_components.json'
$dumper=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
$capFile=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$capability='t4-pc-gfxworld-39-material-source-graph-corpus-v1'
$sourceRoot=Join-Path $repo 'output\aztec_pc_world_39_material_source_corpus'
$out=Join-Path $base 'pc_39_world_material_corpus_source'
$log=Join-Path $out 'oat_pc_material_corpus.log'
$report=Join-Path $out 'aztec_pc_39_world_material_corpus_summary.json'
$dumpRoot=Join-Path ([IO.Path]::GetTempPath()) ('WaW360_PcMat39_' + [guid]::NewGuid().ToString('N').Substring(0,12))
foreach($p in @($witness,$worklist,$alias,$geometry)){
    if(-not(Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing proven world material prerequisite: $p"}
}
$w=Get-Content -LiteralPath $witness -Raw | ConvertFrom-Json
$m=Get-Content -LiteralPath $worklist -Raw | ConvertFrom-Json
$a=Get-Content -LiteralPath $alias -Raw | ConvertFrom-Json
$g=Get-Content -LiteralPath $geometry -Raw | ConvertFrom-Json
if([string]$w.status -ne 'AZTEC_PC_OAT_LOADER_735_TYPED_MATERIAL_POINTER_IDENTITIES_MATCH_ORDINAL_CANDIDATES' -or
   [int]$w.loadedDirectPointerMatches -ne 735 -or
   [int]$w.memoryRowsWithExactSourceCandidateName -ne 39 -or
   [int]$w.unresolvedOrAmbiguous -ne 0 -or
   [bool]$w.xboxHardwareReady -or
   [string]$m.Status -ne 'AZTEC_PC_735_TYPED_MATERIALS_VERIFIED_XBOX_ROOT_RELOCATION_WORKLIST_ONLY' -or
   [int]$m.VerifiedTypedPcSurfaceIdentities -ne 735 -or
   [bool]$m.XboxHardwareReady -or
   [string]$a.Status -ne 'AZTEC_735_XENON_MATERIAL_ROOT_ALIAS_PACKED_POINTERS_DIAGNOSTIC_ONLY' -or
   [int]$a.DistinctWorldMaterialRoots -ne 39 -or
   [int]$a.TotalSurfaceMaterialReferences -ne 735 -or
   [int]$a.WorldRootsWithRealMaterialGraph -ne 0 -or
   [int]$a.WorldRootsWithIdentityShellOnly -ne 39 -or
   [bool]$a.XboxHardwareReady -or
   [string]$g.Status -ne 'AZTEC_PC_GFXWORLD_3_BUFFER_XENON_ENDIAN_READBACK_PASS'){
    throw 'Require exact 39 PC Material identities and 39 still-missing real Xbox world Material graph bodies.'
}
$pc=[string]$g.OriginalPCFastFile
if(-not(Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$g.OriginalPCFastFileSha256){
    throw 'Original-PC fastfile SHA changed.'
}
function Test-Installed{
    if(-not(Test-Path -LiteralPath $dumper -PathType Leaf) -or
       -not(Test-Path -LiteralPath $capFile -PathType Leaf)){return $false}
    $c=Get-Content -LiteralPath $capFile -Raw | ConvertFrom-Json
    return ([string]$c.format -eq 'WaW360.OatGraphDumperCapabilities.v1' -and
        [string]$c.oatRevision -eq '3beaa7c81af517f0c05f2b4612d477ed1541b676' -and
        @($c.capabilities) -contains $capability)
}
if(-not(Test-Installed) -and $BuildDumper){
    Write-Host '[AZTEC-39MAT] One-time pinned OAT native build for PC Material source corpus.'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1')
    if($LASTEXITCODE -ne 0 -or -not $?){throw 'Native OAT PC Material corpus build failed.'}
}
if(-not(Test-Installed)){throw "Missing $capability: run this script ONCE with -BuildDumper."}
$worst=Join-Path $dumpRoot ('pc_gfxworld_39_material_source_corpus\' + ('A'*96) + '\material_38_statebits_pc_le.bin')
if($worst.Length -ge 240){throw "OAT output path too long ($($worst.Length)), shorten TEMP."}
New-Item -ItemType Directory -Force -Path $out,$sourceRoot,$dumpRoot | Out-Null
Write-Host '[AZTEC-39MAT] ============================================================='
Write-Host '[AZTEC-39MAT] ORIGINAL-PC 39 WORLD MATERIAL HEADERS / REAL CHILD TABLES'
Write-Host '[AZTEC-39MAT] Typed Material* from original PC OAT loader, not Xbox donor'
Write-Host '[AZTEC-39MAT] PC little-endian source blobs, technique/image dependencies'
Write-Host '[AZTEC-39MAT] NOT yet Xenon 39 Material graphs or FF. NO XBOX.'
Write-Host "[AZTEC-39MAT] nativeOutput=$dumpRoot"
$captured=New-Object 'System.Collections.Generic.List[string]'
$exit=$null;$callError=$null
$saved=$ErrorActionPreference
try{
    $ErrorActionPreference='Continue'
    $global:LASTEXITCODE=$null
    try{
        & $dumper '--no-color' '--include-assets' 'gfxworld' '--output-folder' $dumpRoot $pc 2>&1 |
            ForEach-Object {$captured.Add([string]$_)}
        if($null -ne $LASTEXITCODE){$exit=[int]$LASTEXITCODE}
    }catch{
        $callError=[string]$_
        $captured.Add("Native invocation error: $callError")
    }
}finally{$ErrorActionPreference=$saved}
if($null -eq $exit){$exit=-1}
$hex='0x{0:X8}' -f ([long]$exit -band 4294967295L)
$lines=@("ExitDecimal=$exit","ExitHex=$hex","NativeOutput=$dumpRoot","Source=$pc")
$lines+=@($captured)
if($captured.Count -eq 0){$lines+='[NO STDOUT/STDERR]'}
[IO.File]::WriteAllLines($log,[string[]]$lines,[Text.UTF8Encoding]::new($false))
Write-Host "[AZTEC-39MAT] OAT exit=$exit ($hex), outputLines=$($captured.Count)"
if($exit -ne 0){
    Get-Content -LiteralPath $log -TotalCount 16 | ForEach-Object {Write-Host "[AZTEC-39MAT] TRACE $_"}
    throw "PC 39 Material source export native failure ($hex): $log"
}
$category=Join-Path $dumpRoot 'pc_gfxworld_39_material_source_corpus'
$manifests=@(Get-ChildItem -LiteralPath $category -Filter manifest.json -Recurse -File -ErrorAction SilentlyContinue)
if($manifests.Count -ne 1){throw "Expected one native PC GfxWorld Material corpus manifest, found $($manifests.Count)"}
$c=Get-Content -LiteralPath $manifests[0].FullName -Raw | ConvertFrom-Json
if([string]$c.format -ne 'WaW360.T4PcGfxWorld39MaterialSourceCorpus.v1' -or
   [string]$c.sourcePlatform -ne 'PC_LOADED_T4_X86_LITTLE_ENDIAN' -or
   -not [bool]$c.abiVerified -or
   [int]$c.abi.material -ne 112 -or
   [int]$c.abi.textureDef -ne 16 -or
   [int]$c.abi.constantDef -ne 32 -or
   [int]$c.abi.stateBits -ne 8 -or
   [int]$c.materialMemoryCount -ne 39 -or
   [int]$c.surfaceCount -ne 735 -or
   @($c.materials).Count -ne 39 -or
   [int]$c.exportedMaterialHeaders -ne 39 -or
   [int]$c.missingMaterials -ne 0 -or
   [int]$c.failedMaterialTables -ne 0 -or
   [int]$c.xenonMaterialGraphBodiesEmitted -ne 0 -or
   [bool]$c.xboxHardwareReady){
    throw "Material corpus source headers/children incomplete: $($manifests[0].FullName)"
}
$raw=Get-Content -LiteralPath ([string]$w.oatGraphFile) -Raw | ConvertFrom-Json
for($i=0;$i -lt 39;$i++){
    $row=$c.materials[$i];$loaded=$raw.memoryRows[$i]
    if([int]$row.ordinal -ne $i -or
       [string]$row.name -cne [string]$loaded.materialName -or
       -not [bool]$row.completeSourceTablesExported){
        throw "Material source graph identity/children mismatch at ordinal $i"
    }
    foreach($spec in @(
        @{name='headerFile';bytes=112},
        @{name='textureTableFile';bytes=([int]$row.counts.textures*16)},
        @{name='constantTableFile';bytes=([int]$row.counts.constants*32)},
        @{name='stateBitsTableFile';bytes=([int]$row.counts.stateBits*8)})){
        $rel=[string]$row.($spec.name)
        if(-not $rel.StartsWith('pc_gfxworld_39_material_source_corpus/',[StringComparison]::Ordinal)){
            throw "Untrusted source corpus blob path: $rel"
        }
        $file=Join-Path $dumpRoot ($rel.Replace('/','\'))
        if(-not(Test-Path -LiteralPath $file -PathType Leaf) -or
           (Get-Item -LiteralPath $file).Length -ne [int]$spec.bytes){
            throw "Source Material #$i missing typed blob or byte length: $file"
        }
    }
}
$target=Join-Path $sourceRoot 'pc_gfxworld_39_material_source_corpus'
if(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Recurse -Force}
Copy-Item -LiteralPath $category -Destination $target -Recurse -Force
$copiedManifest=Join-Path (Join-Path $target (Split-Path $manifests[0].DirectoryName -Leaf)) 'manifest.json'
if(-not(Test-Path -LiteralPath $copiedManifest -PathType Leaf) -or
   (Get-FileHash -LiteralPath $copiedManifest -Algorithm SHA256).Hash -ine
   (Get-FileHash -LiteralPath $manifests[0].FullName -Algorithm SHA256).Hash){
    throw 'PC material corpus persistent copy was not exact.'
}
$summary=[ordered]@{
    schema='WaW360.AztecPc39WorldMaterialSourceCorpusReport.v1'
    status='AZTEC_PC_OAT_39_MATERIAL_HEADERS_AND_CHILD_TABLES_SOURCE_EXPORTED'
    originalPcFastFile=$pc
    sourceFastFileSha256=[string]$g.OriginalPCFastFileSha256
    sourceCorpusRoot=$sourceRoot
    sourceMaterialManifest=$copiedManifest
    typedWorldMaterialOwners=39
    worldSurfaces=735
    sourceHeaders=[int]$c.exportedMaterialHeaders
    textures=[int]$c.totalTextureDescriptors
    constants=[int]$c.totalConstantRecords
    stateBits=[int]$c.totalStateBitsRecords
    waterNestedGraphsPending=[int]$c.waterTexturesNeedingSeparateConversion
    realXenonWorldMaterialGraphBodies=0
    realXboxGfxWorldRootEmitted=$false
    integratedOatFfValidated=$false
    hardwareReady=$false
    note='Original PC typed world Material header/tables. Xbox ABI/endian, technique/image/water mapping, Material graph and FF pending.'
}
$summary|ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host "[AZTEC-39MAT] sourceMaterialHeaders=$($summary.sourceHeaders)/39 textures=$($summary.textures) constants=$($summary.constants) statebits=$($summary.stateBits)"
Write-Host "[AZTEC-39MAT] waterNestedPending=$($summary.waterNestedGraphsPending)"
Write-Host "[AZTEC-39MAT] sourceCorpus=$copiedManifest"
Write-Host "[AZTEC-39MAT] status=$($summary.status)"
Write-Host "[AZTEC-39MAT] report=$report"
Write-Host '[AZTEC-39MAT] NEXT: translate these real 39 PC Material graphs into Xenon ABI/dependencies; Gate 7 pending.'
& (Join-Path $PSScriptRoot 'Show-AztecFirstXboxFFProgress.ps1')
