[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$map = 'nazi_zombie_aztec_V2.2'
$canonical = 'nazi_zombie_aztec'
$outputRoot = Join-Path $repo 'output\second_map_baselines'
$base = Join-Path $outputRoot $map
$nativeWork = Join-Path $base 'native_material_mapping'
$baselinePath = Join-Path $base 'second_map_baseline.json'
$nativePath = Join-Path $nativeWork 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$graphPath = Join-Path $nativeWork 'native_graph_proof\native_graph_proof_result.json'
$stagePath = Join-Path $nativeWork 'renderer_preflight\4e_validation\aztec_4e_validation_result.json'
$rawPath = Join-Path $nativeWork 'pc_rawfile_graft\pc_rawfile_graft_result.json'
$cli = Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
foreach($file in @($baselinePath,$nativePath,$graphPath,$stagePath,$rawPath,$cli)) {
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Required previous Aztec proof missing: $file"
    }
}
$baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
$native = Get-Content -LiteralPath $nativePath -Raw | ConvertFrom-Json
$graph = Get-Content -LiteralPath $graphPath -Raw | ConvertFrom-Json
$stage = Get-Content -LiteralPath $stagePath -Raw | ConvertFrom-Json
$raw = Get-Content -LiteralPath $rawPath -Raw | ConvertFrom-Json
$golden = 'e0f3c244dd7b72d0fe71c05faf014c2c9bfb05f770183954b3e7cde625eba98f'
if ([string]$baseline.format -ne 'WaW360.SecondMapBaseline.v1' -or
    [string]$baseline.mapName -ine $map -or
    [string]$graph.status -ne 'IDENTITY_AND_TYPED_GRAPH_IMPORT_PASS' -or
    [string]$graph.nativeSha256 -ine $golden -or
    [string]$stage.status -ne 'NATIVE_4E_BYTE_EMISSION_VALIDATION_PASS' -or
    -not [bool]$stage.validationPass -or
    [string]$raw.schema -ne 'WaW360.AztecPcRawFileGraftResult.v1' -or
    [string]$raw.status -ne 'AZTEC_PC_RAWFILE_GRAFT_OAT_READBACK_PASS' -or
    [int]$raw.pcRawFileRoots -ne 114 -or
    [int]$raw.xboxReadbacksExact -ne 114 -or
    [int]$raw.nativeRawFileRoots -ne 120 -or
    -not [bool]$native.TypeSequenceVerified -or
    [string]$native.NativeFastFile -ine [string]$graph.verifiedGoldenXboxFastFile -or
    @($native.Entries).Count -ne 8509) {
    throw 'PC baseline, native graph, Stage 4E or PC RawFile proof does not match verified Aztec.'
}
$nativeImages = @($native.Entries | Where-Object { [string]$_.TypeName -ieq 'image' })
if($nativeImages.Count -ne 666) {
    throw "Native Aztec image identity drift: expected 666, observed $($nativeImages.Count)."
}
$main = @($baseline.fastfiles | Where-Object { $_.Role -eq 'map.ff' })
if($main.Count -ne 1 -or
    [IO.Path]::GetFileNameWithoutExtension([string]$main[0].Path) -ine $canonical) {
    throw 'PC Aztec canonical map fastfile is ambiguous or does not match the expected runtime name.'
}
$pcRoot = [string]$baseline.sourceRoot
if(-not $pcRoot -or -not (Test-Path -LiteralPath $pcRoot -PathType Container)) {
    throw "PC Maps input folder missing or not recorded in baseline: $pcRoot"
}
$work = Join-Path $base 'native_material_mapping\pc_image_source_census'
New-Item -ItemType Directory -Path $work -Force | Out-Null
$log = Join-Path $work 'pc_iwi_census_cli.log'
$report = Join-Path $work 'pc_image_source_census_result.txt'
$json = Join-Path $work 'pc_image_source_census_result.json'
$toolOutput = Join-Path $base 'nuclear_4g_iwi_image_corpus\nuclear_4g_iwi_image_corpus.json'
Write-Host '[AZTEC-PC-IMAGE] ==========================================================='
Write-Host '[AZTEC-PC-IMAGE] SOURCE-OWNED IWD/IWI IMAGE CENSUS AGAINST 666 XBOX ROOTS'
Write-Host '[AZTEC-PC-IMAGE] 114/114 PC RawFiles previously survived Xbox SHA readback.'
Write-Host '[AZTEC-PC-IMAGE] No image pixels grafted; no OAT shader redump or FF emission.'
Write-Host "[AZTEC-PC-IMAGE] PC Maps: $pcRoot"
Write-Host '[AZTEC-PC-IMAGE] ==========================================================='

# The established 4G command inspects the map's IWD archives through the
# verified Linker V2 source archive. It does NOT copy native Xbox image data.
# Its report contains matched/unmatched names and the IWI format/byte mass.
if(Test-Path -LiteralPath $toolOutput -PathType Leaf) { Remove-Item -LiteralPath $toolOutput -Force }
$cliArgs = @(
    'run','--no-build','-c','Release','--project',$cli,'--',
    'linker-v2-iwi-image-corpus',$pcRoot,$map,$outputRoot,$nativePath
)
$save = $ErrorActionPreference
try {
    $ErrorActionPreference='Continue'
    & dotnet @cliArgs 2>&1 |
        ForEach-Object { [string]$_ } |
        Set-Content -LiteralPath $log -Encoding UTF8
    $exitCode = [int]$LASTEXITCODE
} finally {
    $ErrorActionPreference=$save
}
$analysis=$null
if(Test-Path -LiteralPath $toolOutput -PathType Leaf) {
    $analysis=Get-Content -LiteralPath $toolOutput -Raw | ConvertFrom-Json
}
$matched=if($null -ne $analysis){[int]$analysis.MatchedNativeImages}else{0}
$unmatched=if($null -ne $analysis){[int]$analysis.UnmatchedNativeImages}else{666}
$ready=($exitCode -eq 0 -and $null -ne $analysis -and
    [string]$analysis.MapName -ieq $map -and
    [int]$analysis.NativeImages -eq 666 -and
    ($matched+$unmatched) -eq 666 -and
    [bool]$analysis.ReadyForCodecPlanning)
$status=if($ready){'AZTEC_PC_IWD_IMAGE_CENSUS_PASS'}else{'AZTEC_PC_IWD_IMAGE_CENSUS_BLOCKED'}
$largest=@()
if($null -ne $analysis) {
    $largest=@($analysis.Formats | Sort-Object -Property Bytes -Descending | Select-Object -First 10 |
        ForEach-Object {
            [pscustomobject]@{
                Version=[int]$_.Version
                Format=('0x{0:X2}' -f [int]$_.Format)
                SourceIwiCount=[int]$_.Images
                NativeMatches=[int]$_.NativeMatches
                SourceBytes=[long]$_.Bytes
            }
        })
}
$summary=[ordered]@{
    schema='WaW360.AztecPcImageSourceCensus.v1'
    mapFolder=$map
    status=$status
    nativeImageRoots=666
    sourceIwiEntries=if($null -ne $analysis){[int]$analysis.SourceIwiEntries}else{0}
    uniquePcIwiIdentities=if($null -ne $analysis){[int]$analysis.UniqueSourceImages}else{0}
    matchedNativeImageIdentities=$matched
    unmatchedNativeImageIdentities=$unmatched
    duplicatePcIwiIdentities=if($null -ne $analysis){[int]$analysis.DuplicateSourceIdentities}else{0}
    matchedSourceImageBytes=if($null -ne $analysis){[long]$analysis.MatchedIwiBytes}else{0L}
    sourceImageBytes=if($null -ne $analysis){[long]$analysis.SourceIwiBytes}else{0L}
    topIwiFormats=[object[]]$largest
    childCorpusManifest=$toolOutput
    cliExitCode=$exitCode
    pcImageConversionCompleted=$false
    pcShaderConversionCompleted=$false
    fullXboxMapCreated=$false
    hardwareReady=$false
    convertedMapsPromotionApproved=$false
    notes=@(
        'Identity and IWI header/format census ONLY: no pixels were converted or serialized.',
        'Only the discovered map IWD source archive is inspected here. PC fastfile image assets or other installed archives may require separate source recovery.',
        'Identical image names do not prove equivalent pixels, format compatibility or complete Material/XModel dependencies.',
        'The previously validated native-reference FF and PC RawFile FF are not modified or released.'
    )
}
$summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $json -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add('AZTEC PC IWD/IWI IMAGE SOURCE CENSUS - NO PIXEL CONVERSION')
$lines.Add('=========================================================')
$lines.Add("status=$status cliExit=$exitCode")
$lines.Add("native image roots=666 matched=$matched unmatched=$unmatched")
$lines.Add("PC IWI entries=$($summary.sourceIwiEntries) unique=$($summary.uniquePcIwiIdentities) duplicate IDs=$($summary.duplicatePcIwiIdentities)")
$lines.Add("matched PC IWI bytes=$($summary.matchedSourceImageBytes) of total=$($summary.sourceImageBytes)")
foreach($format in $largest) {
    $lines.Add("format v$($format.Version) $($format.Format): sourceIwi=$($format.SourceIwiCount) nativeMatches=$($format.NativeMatches) sourceBytes=$($format.SourceBytes)")
}
$lines.Add("CLI log=$log")
$lines.Add("Full format and missing-name report=$toolOutput")
$lines.Add('No output FF or release; no hardware readiness claim.')
[IO.File]::WriteAllLines($report,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-IMAGE] ======================= RESULT ============================='
Write-Host "[AZTEC-PC-IMAGE] status=$status"
Write-Host "[AZTEC-PC-IMAGE] PC IWD image identities=$matched/666; unmatched=$unmatched"
Write-Host "[AZTEC-PC-IMAGE] Source IWIs=$($summary.sourceIwiEntries); source bytes=$($summary.sourceImageBytes)"
foreach($format in @($largest | Select-Object -First 6)) {
    Write-Host "[AZTEC-PC-IMAGE] v$($format.Version) fmt=$($format.Format): $($format.NativeMatches) native matches, $($format.SourceBytes) PC bytes"
}
if(-not $ready) {
    foreach($line in @(Get-Content -LiteralPath $log -Tail 12)) { Write-Host "[AZTEC-PC-IMAGE] $line" }
}
Write-Host "[AZTEC-PC-IMAGE] Report: $report"
Write-Host '[AZTEC-PC-IMAGE] Evidence only; ZERO new FF files. Converted Maps untouched.'
if(-not $ready) { exit 4 }
