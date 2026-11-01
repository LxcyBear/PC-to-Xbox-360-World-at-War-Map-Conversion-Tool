[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$censusPath=Join-Path $base 'tonight_critical_source_census\aztec_tonight_critical_source_census.json'
$huntPath=Join-Path $base 'pc_gameworldsp_path_source_hunt\aztec_pc_gameworldsp_header_hunt.json'
$work=Join-Path $base 'pc_gfxworld_alias_anchor'
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$log=Join-Path $work 'original_pc_decompress.log'
$out=Join-Path $work 'aztec_pc_gfxworld_alias_anchor.json'
$outText=Join-Path $work 'aztec_pc_gfxworld_alias_anchor.txt'
foreach($p in @($censusPath,$huntPath,$cli)){
    if(-not (Test-Path -LiteralPath $p -PathType Leaf)){throw "Missing Aztec PC GfxWorld source prerequisite: $p"}
}
$census=Get-Content -LiteralPath $censusPath -Raw | ConvertFrom-Json
$hunt=Get-Content -LiteralPath $huntPath -Raw | ConvertFrom-Json
if([string]$census.status -ne 'AZTEC_TONIGHT_CRITICAL_PC_SOURCE_IDENTITY_CENSUS_PASS' -or
   [int]$census.nativeRoots -ne 8509 -or
   [string]$hunt.Schema -ne 'WaW360.AztecPcGameWorldSpHeaderHunt.v1' -or
   [string]$hunt.Status -ne 'AZTEC_PC_GAMEWORLDSP_HEADER_HUNT_COMPLETE' -or
   [long]$hunt.OriginalPcZoneBytes -ne 113701260L){
    throw 'Original-PC GfxWorld owner/source fingerprint history changed.'
}
$g=@($hunt.PcWorldAssetRoots | Where-Object {
    ([string]$_.PcTypeName).Replace('_','').ToLowerInvariant() -eq 'gfxworld'
})
if($g.Count -ne 1 -or [string]$g[0].RawHeader -ne '0xFFFFFFFF'){
    throw 'Original PC GfxWorld must be exactly one InlineShared XAsset in the source table.'
}
$primary=@($hunt.TopCandidates | Where-Object {[string]$_.OffsetHex -eq '0x05B17945'})
if($primary.Count -ne 1 -or
   [string]$primary[0].NamePtr -ine '0x84702AC9' -or
   [int]$primary[0].NodeCount -ne 7630){
    throw 'Packed potential map-name alias anchor 0x84702AC9 is no longer source-proven at the GameWorldSP lead.'
}
$roles=@($census.pcSourceRoles | Where-Object {[string]$_.role -eq 'map.ff'})
if($roles.Count -ne 1){throw 'Original PC map.ff must have one source role.'}
$pc=[string]$roles[0].sourceFastfile
if(-not (Test-Path -LiteralPath $pc -PathType Leaf) -or
   (Get-FileHash -LiteralPath $pc -Algorithm SHA256).Hash -ine [string]$roles[0].sourceSha256 -or
   [string]$hunt.OriginalPcFastFileSha256 -ine [string]$roles[0].sourceSha256){
    throw 'Original-PC map.ff SHA-256 differs from verified GameWorldSP scan.'
}
function Uint([byte[]]$b,[int]$p){return [BitConverter]::ToUInt32($b,$p)}
function Int([byte[]]$b,[int]$p){return [BitConverter]::ToInt32($b,$p)}
function Hash([byte[]]$b,[int]$start,[int]$count){
    $sha=[Security.Cryptography.SHA256]::Create()
    try{return [BitConverter]::ToString($sha.ComputeHash($b,$start,$count)).Replace('-','')}
    finally{$sha.Dispose()}
}
function PointerMaybe([uint32]$v){
    if($v -eq 0){return $false}
    if($v -eq [uint32]::MaxValue -or $v -eq [uint32]([uint32]::MaxValue-1)){return $true}
    # T4 packed pointers store block id in the top 3 bits.
    $block=[int](([uint64]$v-1) -shr 29)
    return $block -ge 0 -and $block -le 6
}
New-Item -Path $work -ItemType Directory -Force | Out-Null
$temp=''
$status='AZTEC_PC_GFXWORLD_ALIAS_SCAN_BLOCKED'
$blocker=''
$matches=0
$rows=New-Object 'System.Collections.Generic.List[object]'
Write-Host '[AZTEC-PC-GFX] ========================================================='
Write-Host '[AZTEC-PC-GFX] VISUAL-DEMO-FIRST ORIGINAL PC GFXWORLD ALIAS-HEADER HUNT'
Write-Host '[AZTEC-PC-GFX] Scans candidate pointer 0x84702AC9, NOT the shared map-name text.'
Write-Host '[AZTEC-PC-GFX] No .NET build, no FF emit, no Xbox deployment.'
try{
    $temp=Join-Path ([IO.Path]::GetTempPath()) ('wawAzGX_'+[guid]::NewGuid().ToString('N').Substring(0,8))
    if($temp.Length -gt 110){throw 'Use a short physical TEMP directory for decompression.'}
    New-Item -Path $temp -ItemType Directory -Force | Out-Null
    $zone=Join-Path $temp 'pc_original_map.zone'
    $saved=$ErrorActionPreference
    try{
        $ErrorActionPreference='Continue'
        & dotnet 'run' '--no-build' '-c' 'Release' '--project' $cli '--' 'decompress' $pc $zone 2>&1 |
            ForEach-Object {[string]$_} | Set-Content -LiteralPath $log -Encoding UTF8
        $code=[int]$LASTEXITCODE
    }finally{$ErrorActionPreference=$saved}
    if($code -ne 0 -or -not (Test-Path -LiteralPath $zone -PathType Leaf)){
        throw "Original PC map.ff decompression failed ($code): $log"
    }
    $z=[IO.File]::ReadAllBytes($zone)
    if([long]$z.Length -ne 113701260L -or
       (Get-FileHash -LiteralPath $zone -Algorithm SHA256).Hash -ine [string]$hunt.OriginalPcZoneSha256){
        throw 'Original source PC zone hash changed.'
    }
    $hay=[Text.Encoding]::GetEncoding(28591).GetString($z)
    $pattern=[Text.Encoding]::GetEncoding(28591).GetString([byte[]]@(0xC9,0x2A,0x70,0x84))
    $search=0
    while($search -lt $hay.Length){
        $at=$hay.IndexOf($pattern,$search,[StringComparison]::Ordinal)
        if($at -lt 0){break}
        $matches++
        $search=$at+4
        if($matches -gt 100000){throw 'Unexpected alias marker explosion; refuse unbounded source scan.'}
        if($at -gt $z.Length-128){continue}
        # T4 PC GfxWorld leading 32-bit ABI fields from T4_Assets.h.
        # A matching first word alone does not imply a GfxWorld.
        $basePtr=Uint $z ($at+4)
        $planes=Int $z ($at+8)
        $nodes=Int $z ($at+12)
        $indices=Int $z ($at+16)
        $indicesPtr=Uint $z ($at+20)
        $surfaces=Int $z ($at+24)
        $sky=Int $z ($at+32)
        $skyStartPtr=Uint $z ($at+36)
        $vertices=Uint $z ($at+52)
        $vertexArrayPtr=Uint $z ($at+56)
        $score=0
        if(PointerMaybe $basePtr){$score+=2}
        if($planes -gt 0 -and $planes -lt 2000000){$score+=2}
        if($nodes -gt 0 -and $nodes -lt 2000000){$score+=2}
        if($indices -gt 0 -and $indices -lt 30000000){$score+=2}
        if(PointerMaybe $indicesPtr){$score+=2}
        if($surfaces -gt 0 -and $surfaces -lt 2000000){$score+=2}
        if($sky -ge 0 -and $sky -lt 1000000){$score+=2}
        if($sky -eq 0 -or (PointerMaybe $skyStartPtr)){$score+=2}
        if($vertices -gt 0 -and $vertices -lt 30000000){$score+=2}
        if(PointerMaybe $vertexArrayPtr){$score+=2}
        if($score -lt 10){continue}
        $rows.Add([pscustomobject]@{
            candidateOffset=$at
            candidateOffsetHex=('0x{0:X8}' -f $at)
            namePointerHex='0x84702AC9'
            baseNamePointerHex=('0x{0:X8}' -f $basePtr)
            planeCount=$planes
            nodeCount=$nodes
            indexCount=$indices
            indicesPointerHex=('0x{0:X8}' -f $indicesPtr)
            surfaceCount=$surfaces
            skySurfCount=$sky
            skyStartSurfsPointerHex=('0x{0:X8}' -f $skyStartPtr)
            vertexCount=$vertices
            vertexArrayPointerHex=('0x{0:X8}' -f $vertexArrayPtr)
            score=$score
            maxScore=20
            fixed128ByteSourceSha256=(Hash $z $at 128)
            actualGfxWorldBodyLocated=$false
            renderableGeometryConverted=$false
        })
    }
    $ranked=@($rows.ToArray() | Sort-Object -Property @{Expression='score';Descending=$true},@{Expression='candidateOffset'})
    Write-Host "[AZTEC-PC-GFX] packed 0x84702AC9 byte hits=$matches, GfxWorld-shaped candidates=$($ranked.Count)"
    foreach($c in @($ranked | Select-Object -First 15)){
        Write-Host "[AZTEC-PC-GFX] CANDIDATE $($c.candidateOffsetHex) score=$($c.score)/20 planes=$($c.planeCount) nodes=$($c.nodeCount) indices=$($c.indexCount) surfaces=$($c.surfaceCount) vertices=$($c.vertexCount) basePtr=$($c.baseNamePointerHex) sha=$($c.fixed128ByteSourceSha256)"
    }
    $status='AZTEC_PC_GFXWORLD_ALIAS_ANCHOR_SCAN_COMPLETE'
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-PC-GFX] BLOCKER: $blocker"
    if(Test-Path -LiteralPath $log -PathType Leaf){
        Get-Content -LiteralPath $log -Tail 5 | ForEach-Object {Write-Host "[AZTEC-PC-GFX] decompress $_"}
    }
}finally{
    if(-not [string]::IsNullOrWhiteSpace($temp) -and (Test-Path -LiteralPath $temp -PathType Container)){
        Remove-Item -LiteralPath $temp -Force -Recurse
    }
}
$ranked=@($rows.ToArray() | Sort-Object -Property @{Expression='score';Descending=$true},@{Expression='candidateOffset'})
$report=[ordered]@{
    schema='WaW360.AztecPcGfxWorldAliasAnchor.v1'
    status=$status
    originalPcSourceFastFile=$pc
    originalPcSourceFastFileSha256=[string]$roles[0].sourceSha256
    originalPcZoneSha256=[string]$hunt.OriginalPcZoneSha256
    sourcePcGfxWorldXAssetIndex=[int]$g[0].PcAssetIndex
    sourcePcGfxWorldReferenceKind=[string]$g[0].ReferenceKind
    referencePointerHypothesis='0x84702AC9'
    pointerOccurrences=$matches
    candidateHeaders=$ranked
    blocker=$blocker
    sourceGfxWorldBodyDecoded=$false
    sourceRenderGeometryRecovered=$false
    xboxHardwareAuthorized=$false
    note='Read-only raw PC source alias-shaped header scan. 0x84702AC9 appears in one GameWorldSP candidate but its referent is not independently proven. Even a 20/20 header is not rendered GfxWorld geometry; no zone children, DPVS, collision or Xbox FF generated.'
}
$report | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $out -Encoding UTF8
$summary=New-Object 'System.Collections.Generic.List[string]'
$summary.Add("Aztec PC GfxWorld virtual name-alias structural scan: $status")
$summary.Add("source PC .ff SHA=$([string]$roles[0].sourceSha256)")
$summary.Add("0x84702AC9 byte matches=$matches, header candidates=$($ranked.Count)")
foreach($c in @($ranked | Select-Object -First 12)){
    $summary.Add("GFX CANDIDATE $($c.candidateOffsetHex): score=$($c.score)/20 planes=$($c.planeCount) nodes=$($c.nodeCount) indices=$($c.indexCount) surfaces=$($c.surfaceCount) vertices=$($c.vertexCount) SHA=$($c.fixed128ByteSourceSha256)")
}
$summary.Add("blocker=$blocker")
$summary.Add('No actual PC GfxWorld geometry recovered; no FF created; NOT FOR XBOX.')
[IO.File]::WriteAllLines($outText,[string[]]$summary.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-GFX] ================= RESULT ==============================='
Write-Host "[AZTEC-PC-GFX] status=$status"
Write-Host "[AZTEC-PC-GFX] report=$outText"
Write-Host '[AZTEC-PC-GFX] NO BUILD / NO FF / NO XBOX'
if($status -ne 'AZTEC_PC_GFXWORLD_ALIAS_ANCHOR_SCAN_COMPLETE'){exit 4}
