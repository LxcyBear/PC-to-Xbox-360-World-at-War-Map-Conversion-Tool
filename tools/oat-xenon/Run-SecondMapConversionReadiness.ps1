[CmdletBinding()]
param(
    [string]$PcMapsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps',
    [string]$MapName = 'nazi_zombie_aztec_V2.2',
    [string]$OutputRoot = '',
    [switch]$SkipSerializerCoverage,
    [switch]$SkipXAnimProof
)

Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
if (-not $OutputRoot) { $OutputRoot=Join-Path $repo 'output\second_map_baselines' }
if (-not (Test-Path -LiteralPath $PcMapsRoot -PathType Container)) { throw "PC Maps root missing: $PcMapsRoot" }
$pcRoot=(Resolve-Path -LiteralPath $PcMapsRoot).Path
$workspace=Join-Path $OutputRoot $MapName
$baselineFile=Join-Path $workspace 'second_map_baseline.json'
if (-not (Test-Path -LiteralPath $baselineFile -PathType Leaf)) {
    throw "Prior second-map baseline missing: $baselineFile"
}
$baseline=Get-Content -LiteralPath $baselineFile -Raw | ConvertFrom-Json
if ([string]$baseline.format -ne 'WaW360.SecondMapBaseline.v1' -or
    [string]$baseline.mapName -ine $MapName -or
    [string]$baseline.sourceRoot -ine $pcRoot) {
    throw 'Second-map baseline identity/source path mismatch. Refusing to mix map outputs.'
}
$primary=@($baseline.fastfiles | Where-Object { $_.Role -eq 'map.ff' })
if ($primary.Count -ne 1) { throw 'Expected exactly one primary map.ff in the baseline.' }
$canonical=[IO.Path]::GetFileNameWithoutExtension([string]$primary[0].Path)
if (-not $canonical) { throw 'Canonical map runtime identity is missing.' }
if ($baseline.PSObject.Properties['canonicalMapName'] -and [string]$baseline.canonicalMapName -ine $canonical) {
    throw 'Canonical map runtime identity differs from the baseline.'
}
if (@($baseline.fastfiles | Where-Object { -not $_.AnalyzerReadable }).Count -gt 0) {
    throw 'At least one input FF was unreadable in the previous baseline; refusing promotion.'
}
foreach ($ff in @($baseline.fastfiles)) {
    if (-not (Test-Path -LiteralPath ([string]$ff.Path) -PathType Leaf)) {
        throw "Missing original fastfile: $($ff.Path)"
    }
    $currentBytes=[long](Get-Item -LiteralPath ([string]$ff.Path)).Length
    if ($currentBytes -ne [long]$ff.Bytes) {
        throw "Source fastfile size changed since baseline: $($ff.Path); rerun the baseline."
    }
}
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) { throw "CLI project missing: $cli" }
New-Item -ItemType Directory -Force -Path $workspace | Out-Null

function Invoke-Stage([string]$label,[string]$command,[string]$log) {
    $arguments=@('run','--no-build','-c','Release','--project',$cli,'--',$command,$pcRoot,$MapName,$OutputRoot)
    $saved=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        $lines=@(& dotnet @arguments 2>&1 | ForEach-Object { [string]$_ })
        $exitCode=[int]$LASTEXITCODE
    } finally { $ErrorActionPreference=$saved }
    [IO.File]::WriteAllLines($log,[string[]]$lines,[Text.UTF8Encoding]::new($false))
    Write-Host "[SECOND-READINESS] $label exit=$exitCode"
    foreach ($line in @($lines | Select-Object -Last 7)) { Write-Host "[SECOND-READINESS]   $line" }
    return [pscustomobject]@{ Stage=$label; ExitCode=$exitCode; Log=$log }
}
function Read-StageJson([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    return (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)
}

Write-Host '[SECOND-READINESS] ========================================================='
Write-Host '[SECOND-READINESS] AZTEC LINKER V2 + SERIALIZER READINESS'
Write-Host "[SECOND-READINESS] Folder: $MapName"
Write-Host "[SECOND-READINESS] Runtime: $canonical"
Write-Host "[SECOND-READINESS] Xbox external audio: usermaps/$canonical/sounds"
Write-Host '[SECOND-READINESS] No hardware FF or XMA emitted by this diagnostic.'
Write-Host '[SECOND-READINESS] ========================================================='

# Compile once; subsequent run invocations explicitly use --no-build.
$buildLog=Join-Path $workspace 'readiness_cli_build.log'
$saved=$ErrorActionPreference
try {
    $ErrorActionPreference='Continue'
    $buildLines=@(& dotnet 'build' $cli '-c' 'Release' '--nologo' 2>&1 | ForEach-Object { [string]$_ })
    $buildExit=[int]$LASTEXITCODE
} finally { $ErrorActionPreference=$saved }
[IO.File]::WriteAllLines($buildLog,[string[]]$buildLines,[Text.UTF8Encoding]::new($false))
Write-Host "[SECOND-READINESS] CLI build exit=$buildExit log=$buildLog"
if ($buildExit -ne 0) {
    foreach ($line in @($buildLines | Select-Object -Last 20)) { Write-Host $line }
    throw "CLI build failed. No readiness tests started."
}

$stages=New-Object 'System.Collections.Generic.List[object]'
$sourceFile=Join-Path $workspace ('linker_v2_source_archive\'+$MapName+'_linker_v2_source_archive.json')
if (Test-Path -LiteralPath $sourceFile) { Remove-Item -LiteralPath $sourceFile -Force }
$stages.Add((Invoke-Stage 'Linker V2 canonical source' 'linker-v2-source' (Join-Path $workspace 'readiness_source.log')))
$source=Read-StageJson $sourceFile
if ($null -eq $source) { throw "Canonical source report was not created; see $($stages[0].Log)" }
if ([string]$source.CanonicalBaseName -ine $canonical) {
    throw "Canonical source graph reports $($source.CanonicalBaseName), expected $canonical"
}
# Deduplicate weapon-referenced XAnims using proven source identities first.
# Without this proof, proposed IWD XAnim promotions may be overcounted.
$xanimFile=Join-Path $workspace ('stage4_alpha4_xanim\'+$MapName+'_stage4_alpha4_xanim.json')
$xanim=$null
if (-not $SkipXAnimProof) {
    if (Test-Path -LiteralPath $xanimFile) { Remove-Item -LiteralPath $xanimFile -Force }
    $stages.Add((Invoke-Stage 'Source-backed Xbox XAnim proof' 'stage4-native-xanim' (Join-Path $workspace 'readiness_xanim_proof.log')))
    $xanim=Read-StageJson $xanimFile
}
$typedFile=Join-Path $workspace ('linker_v2_typed_closure\'+$MapName+'_linker_v2_typed_closure.json')
if (Test-Path -LiteralPath $typedFile) { Remove-Item -LiteralPath $typedFile -Force }
$stages.Add((Invoke-Stage 'Linker V2 typed closure' 'linker-v2-typed-closure' (Join-Path $workspace 'readiness_typed_closure.log')))
$typed=Read-StageJson $typedFile
$coverage=$null
if (-not $SkipSerializerCoverage) {
    $coverageFile=Join-Path $workspace ('stage4_alpha2_xbox_build\'+$MapName+'_stage4_alpha2_xbox_build.json')
    if (Test-Path -LiteralPath $coverageFile) { Remove-Item -LiteralPath $coverageFile -Force }
    $stages.Add((Invoke-Stage 'Serializer coverage' 'stage4-alpha2-build' (Join-Path $workspace 'readiness_serializer_coverage.log')))
    $coverage=Read-StageJson $coverageFile
}

$issues=New-Object 'System.Collections.Generic.List[string]'
foreach ($row in $stages) {
    if ($row.ExitCode -notin @(0,3)) { $issues.Add("$($row.Stage) exited $($row.ExitCode)") }
}
if (-not $SkipXAnimProof -and ($null -eq $xanim -or -not [bool]$xanim.Verified)) {
    $issues.Add('Source-backed XAnim proof missing or incomplete; XAnim promotions may be overestimated.')
}
if ($null -eq $typed) { $issues.Add('Typed closure did not produce a report.') }
if (-not $SkipSerializerCoverage -and $null -eq $coverage) {
    $issues.Add('Serializer coverage did not produce a report.')
}
$typedBlockers=$(if ($null -eq $typed) { @() } else { @($typed.Blockers) })
$critical=$(if ($null -eq $coverage) { @() } else { @($coverage.CriticalBlockers) })
$high=$(if ($null -eq $coverage) { @() } else { @($coverage.HighBlockers) })
$manifest=[ordered]@{
    format='WaW360.SecondMapConversionReadiness.v1'
    sourceMapFolder=$MapName; canonicalMapName=$canonical
    mainXboxFF=$canonical+'.ff'
    selectedLoadFF=[string]$source.LoadOutputFastFile
    xboxSoundsFolder=("usermaps/{0}/sounds" -f $canonical)
    primaryGraphRootCount=[int]$source.PrimaryGraphSourceRoots
    iwdSourceEntries=[int]$source.IwdEntries
    externalSoundsExpected=[bool]$source.ExternalSoundsExpected
    canonicalSourceVerified=[bool]$source.Verified
    canonicalSourceBlockers=@($source.Blockers)
    xanimProofAttempted=(-not $SkipXAnimProof)
    xanimProofVerified=$(if ($null -ne $xanim) { [bool]$xanim.Verified } else { $false })
    xanimProofAssets=$(if ($null -ne $xanim) { [int]$xanim.SerializedXAnim } else { 0 })
    proposedXAnimPromotions=$(if ($null -ne $typed) { [int]$typed.PromotedXAnimCandidates } else { 0 })
    proposedRawFilePromotions=$(if ($null -ne $typed) { [int]$typed.PromotedRawFileCandidates } else { 0 })
    xboxDonorSettingsPath=(Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'WaW360Converter\settings.json')
    typedClosureVerified=$(if ($null -ne $typed) { [bool]$typed.Verified } else { $false })
    typedClosureReady=$(if ($null -ne $typed) { [bool]$typed.FullTypedClosureReady } else { $false })
    typedClosureBlockers=[object[]]$typedBlockers
    serializerCoverageCompleted=($null -ne $coverage)
    criticalSerializerBlockers=[object[]]$critical
    highSerializerBlockers=[object[]]$high
    stages=[object[]]$stages.ToArray()
    issues=[string[]]$issues.ToArray()
    hardwareReady=$false
    notes=@(
        'A diagnostic report is not evidence of Xbox runtime acceptance.',
        'PC audio is not deployable Xbox XMA; streamed audio is external in the canonical map sounds folder.',
        'No source or target FF has been replaced by this runner.'
    )
}
$json=Join-Path $workspace 'second_map_conversion_readiness.json'
$txt=Join-Path $workspace 'second_map_conversion_readiness.txt'
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $json -Encoding UTF8
$summary=New-Object 'System.Collections.Generic.List[string]'
$summary.Add('AZTEC SECOND-MAP CONVERSION READINESS')
$summary.Add('==========================================================')
$summary.Add("PC folder: $MapName; Xbox runtime name: $canonical")
$summary.Add("Xbox external sounds: $($manifest.xboxSoundsFolder)")
$summary.Add("Primary graph roots: $($source.PrimaryGraphSourceRoots); IWD source entries: $($source.IwdEntries)")
$summary.Add("Source graph verified: $($source.Verified); selected load: $($source.LoadOutputFastFile)")
$summary.Add("XAnim source proof verified: $($manifest.xanimProofVerified); XAnim proof assets: $($manifest.xanimProofAssets)")
$summary.Add("IWD proposed XAnim promotions: $($manifest.proposedXAnimPromotions); RawFile promotions: $($manifest.proposedRawFilePromotions)")
$summary.Add("Xbox game folder configuration: $($manifest.xboxDonorSettingsPath)")
$summary.Add("Typed closure verified: $($manifest.typedClosureVerified); ready: $($manifest.typedClosureReady); blockers: $($typedBlockers.Count)")
$summary.Add("Serializer blockers: critical=$($critical.Count), high=$($high.Count)")
$summary.Add('RUN STATUSES')
foreach ($row in $stages) { $summary.Add("  $($row.Stage) exit=$($row.ExitCode) log=$($row.Log)") }
$summary.Add('SOURCE GRAPH BLOCKERS')
foreach ($b in @($source.Blockers | Select-Object -First 15)) { $summary.Add("  $b") }
$summary.Add('TYPED CLOSURE BLOCKERS')
foreach ($b in @($typedBlockers | Select-Object -First 25)) { $summary.Add("  $b") }
$summary.Add('CRITICAL SERIALIZER BLOCKERS')
foreach ($b in @($critical | Select-Object -First 30)) { $summary.Add("  $b") }
$summary.Add('HIGH SERIALIZER BLOCKERS')
foreach ($b in @($high | Select-Object -First 20)) { $summary.Add("  $b") }
foreach ($issue in $issues) { $summary.Add("WARNING: $issue") }
$summary.Add('Hardware-ready remains FALSE. No FF or XMA was emitted.')
[IO.File]::WriteAllLines($txt,[string[]]$summary.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[SECOND-READINESS] ===================== RESULT ============================'
Write-Host "[SECOND-READINESS] Canonical=$canonical roots=$($source.PrimaryGraphSourceRoots) selectedLoad=$($source.LoadOutputFastFile)"
Write-Host "[SECOND-READINESS] XAnim proof=$($manifest.xanimProofVerified) assets=$($manifest.xanimProofAssets) | proposed IWD XAnim=$($manifest.proposedXAnimPromotions) RawFile=$($manifest.proposedRawFilePromotions)"
Write-Host "[SECOND-READINESS] Typed-ready=$($manifest.typedClosureReady) blockers=$($typedBlockers.Count)"
Write-Host "[SECOND-READINESS] Serializer critical=$($critical.Count) high=$($high.Count)"
Write-Host "[SECOND-READINESS] Report: $txt"
Write-Host "[SECOND-READINESS] JSON:   $json"
Write-Host '[SECOND-READINESS] Completed diagnostic; hardware-ready is FALSE.'
if ($issues.Count -gt 0) {
    Write-Host "[SECOND-READINESS] $($issues.Count) execution/report issues require log inspection." -ForegroundColor Yellow
}
