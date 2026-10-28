[CmdletBinding()]
param(
    [string]$PcMapsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps',
    [string]$MapName = 'nazi_zombie_aztec_V2.2',
    [string]$OutputRoot = '',
    [string]$XboxAztecFastFile = '',
    [string]$UnlinkerExe = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$project=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
if (-not $OutputRoot) { $OutputRoot=Join-Path $repo 'output\second_map_baselines' }
if (-not $UnlinkerExe) {
    $UnlinkerExe=Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
}
foreach ($input in @($project,$UnlinkerExe)) {
    if (-not (Test-Path -LiteralPath $input -PathType Leaf)) {
        throw "Required tool missing: $input"
    }
}
if (-not (Test-Path -LiteralPath $PcMapsRoot -PathType Container)) {
    throw "PC map root missing: $PcMapsRoot"
}
$sourceRoot=(Resolve-Path -LiteralPath $PcMapsRoot).Path
$workspace=Join-Path $OutputRoot $MapName
$baselinePath=Join-Path $workspace 'second_map_baseline.json'
if (-not (Test-Path -LiteralPath $baselinePath -PathType Leaf)) {
    throw "Run Run-SecondMapBaseline.ps1 first; missing $baselinePath"
}
$baseline=Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
if ([string]$baseline.format -ne 'WaW360.SecondMapBaseline.v1' -or
    [string]$baseline.mapName -ine $MapName -or
    [string]$baseline.sourceRoot -ine $sourceRoot) {
    throw 'Second-map baseline identity/path mismatch.'
}
$sourceFastfiles=@($baseline.fastfiles |
    Where-Object { $_.Role -in @('map.ff','map_patch.ff','mod.ff') } |
    Sort-Object Role, Path)
$primary=@($sourceFastfiles | Where-Object { $_.Role -eq 'map.ff' })
if ($primary.Count -ne 1) { throw 'Expected exactly one primary map.ff.' }
$pcFastFile=[string]$primary[0].Path
$canonical=[IO.Path]::GetFileNameWithoutExtension($pcFastFile)
if ($baseline.PSObject.Properties['canonicalMapName'] -and
    [string]$baseline.canonicalMapName -ine $canonical) {
    throw 'The canonical FF name differs from the baseline.'
}
foreach ($ff in $sourceFastfiles) {
    if (-not (Test-Path -LiteralPath ([string]$ff.Path) -PathType Leaf)) {
        throw "Original PC fastfile is missing: $($ff.Path)"
    }
    if ([long](Get-Item -LiteralPath ([string]$ff.Path)).Length -ne [long]$ff.Bytes) {
        throw "Original PC fastfile size has changed; refresh baseline: $($ff.Path)"
    }
}
$work=Join-Path $workspace 'material_techset_oat_bridge'
New-Item -ItemType Directory -Force -Path $work | Out-Null

function Invoke-DotnetStage([string]$label,[string[]]$arguments,[string]$log) {
    $prior=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        $lines=@(& dotnet @arguments 2>&1 | ForEach-Object { [string]$_ })
        $code=[int]$LASTEXITCODE
    } finally { $ErrorActionPreference=$prior }
    [IO.File]::WriteAllLines($log,[string[]]$lines,[Text.UTF8Encoding]::new($false))
    Write-Host "[MATERIAL-OAT] $label exit=$code log=$log"
    foreach ($line in @($lines | Select-Object -Last 5)) {
        Write-Host "[MATERIAL-OAT]   $line"
    }
    return [pscustomobject]@{ Label=$label; ExitCode=$code; Log=$log }
}

Write-Host '[MATERIAL-OAT] ==========================================================='
Write-Host '[MATERIAL-OAT] AZTEC / GENERIC FULL-MAP MATERIAL + TECHNIQUESET ANALYSIS'
Write-Host "[MATERIAL-OAT] PC folder: $MapName | Runtime: $canonical"
Write-Host '[MATERIAL-OAT] OAT semantic identities first, no guessing TEMP pointers.'
Write-Host '[MATERIAL-OAT] PC shader graphs are NEVER copied into Xbox output.'
Write-Host '[MATERIAL-OAT] ==========================================================='

$build=Invoke-DotnetStage 'Release CLI build' @('build',$project,'-c','Release','--nologo') (Join-Path $work 'build.log')
if ($build.ExitCode -ne 0) { throw "Cannot run OAT semantic census: CLI build failed. See $($build.Log)" }
$runs=New-Object 'System.Collections.Generic.List[object]'
$verified=0
$declared=0
$oatMapped=0
$ordinal=0
foreach ($ff in $sourceFastfiles) {
    $ordinal++
    $pc=[string]$ff.Path
    $tag=("{0:D2}_{1}" -f $ordinal,([IO.Path]::GetFileNameWithoutExtension($pc)))
    $folder=Join-Path $work $tag
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $json=Join-Path $folder 'pc_oat_techset_identities.json'
    if (Test-Path -LiteralPath $json) { Remove-Item -LiteralPath $json -Force }
    $run=Invoke-DotnetStage "OAT PC semantic identities: $tag" @(
        'run','--no-build','-c','Release','--project',$project,'--',
        'pc-oat-techset-identities',$pc,$folder,$UnlinkerExe
    ) (Join-Path $folder 'oat_identity_console.log')
    $run | Add-Member -NotePropertyName SourceFastFile -NotePropertyValue $pc
    $run | Add-Member -NotePropertyName Role -NotePropertyValue ([string]$ff.Role)
    $run | Add-Member -NotePropertyName IdentityManifest -NotePropertyValue $json
    if (Test-Path -LiteralPath $json) {
        $identity=Get-Content -LiteralPath $json -Raw | ConvertFrom-Json
        $declared+=[int]$identity.DeclaredPhysicalTechniqueSets
        $oatMapped+=@($identity.Entries).Count
        if ([bool]$identity.Verified -and $run.ExitCode -eq 0) { $verified++ }
        $run | Add-Member -NotePropertyName DeclaredTechsets -NotePropertyValue ([int]$identity.DeclaredPhysicalTechniqueSets)
        $run | Add-Member -NotePropertyName ResolvedTechsets -NotePropertyValue (@($identity.Entries).Count)
        $run | Add-Member -NotePropertyName IdentityVerified -NotePropertyValue ([bool]$identity.Verified)
    } else {
        $run | Add-Member -NotePropertyName DeclaredTechsets -NotePropertyValue 0
        $run | Add-Member -NotePropertyName ResolvedTechsets -NotePropertyValue 0
        $run | Add-Member -NotePropertyName IdentityVerified -NotePropertyValue $false
    }
    $runs.Add($run)
}

# Optionally use a known-working, user-provided native Xbox Aztec fastfile.
# This is a semantic A/B calibration oracle, not a production donor graft.
$native=$null
$closure=$null
$closureRun=$null
$nativeStatus='NOT_PROVIDED'
if ($XboxAztecFastFile) {
    if (-not (Test-Path -LiteralPath $XboxAztecFastFile -PathType Leaf)) {
        throw "Native Xbox Aztec FF not found: $XboxAztecFastFile"
    }
    $native=(Resolve-Path -LiteralPath $XboxAztecFastFile).Path
    if ($native -ieq $pcFastFile) { throw 'PC and Xbox fastfiles must be different files.' }
    $nativeStatus='USER_SUPPLIED_NOT_YET_VERIFIED'
    $closureDir=Join-Path $work 'native_xenon_closure'
    New-Item -ItemType Directory -Force -Path $closureDir | Out-Null
    $closureFile=Join-Path $closureDir 'nuclear_technique_closure_plan.json'
    if (Test-Path -LiteralPath $closureFile) { Remove-Item -LiteralPath $closureFile -Force }
    $closureRun=Invoke-DotnetStage 'PC -> native Xenon material/TechniqueSet mapping' @(
        'run','--no-build','-c','Release','--project',$project,'--',
        'nuclear-techset-closure-plan',$pcFastFile,$native,$closureDir,$UnlinkerExe
    ) (Join-Path $closureDir 'nuclear_technique_console.log')
    if (Test-Path -LiteralPath $closureFile) {
        $closure=Get-Content -LiteralPath $closureFile -Raw | ConvertFrom-Json
        $nativeStatus='NATIVE_AB_COMPARISON_COMPLETED'
    } else {
        $nativeStatus='NATIVE_AB_COMPARISON_FAILED'
    }
}

$manifest=[ordered]@{
    schema='WaW360.SecondMapMaterialTechnique.v1'
    pcMapFolder=$MapName
    canonicalRuntimeName=$canonical
    mainPcFastFile=$pcFastFile
    oatIdentityScans=@($runs.ToArray())
    identityFilesVerified=$verified
    identityFilesAttempted=$sourceFastfiles.Count
    identityDeclaredTechniqueSets=$declared
    identityMappedTechniqueSets=$oatMapped
    nativeXboxFastFile=$native
    nativeStatus=$nativeStatus
    nativeClosureReport=$(if ($null -ne $closure) { (Join-Path $work 'native_xenon_closure\nuclear_technique_closure_plan.json') } else { '' })
    nativeIdentityClosureReady=$(if ($null -ne $closure) { [bool]$closure.IdentityClosureComplete } else { $false })
    nativeGraphExtractionReady=$(if ($null -ne $closure) { [bool]$closure.GraphExtractionReady } else { $false })
    nativeRemainingUnresolved=$(if ($null -ne $closure) { [int]$closure.UnresolvedTechniqueRoots } else { $null })
    productionReady=$false
    policy=@(
        'OAT identity mapping is diagnostic; it does not resolve PC shader pass ownership or produce Xbox bytes.',
        'A native Aztec FF enables material-to-techset semantic calibration and identity closure, not permission to redistribute donor bytes.',
        'Unresolved names or conflicting native references must fail closed.',
        'No Xbox map FF or loose XMA sound files are created here.'
    )
}
$manifestFile=Join-Path $work 'material_technique_result.json'
$reportFile=Join-Path $work 'material_technique_result.txt'
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestFile -Encoding UTF8
$text=New-Object 'System.Collections.Generic.List[string]'
$text.Add('AZTEC FULL-MAP OAT MATERIAL/TECHNIQUESET RESEARCH')
$text.Add('================================================================')
$text.Add("PC: $pcFastFile")
$text.Add("OAT identity scans: $verified/$($sourceFastfiles.Count) verified")
$text.Add("OAT mapped TechniqueSets: $oatMapped/$declared")
foreach ($entry in $runs) {
    $text.Add("  $($entry.Role): $($entry.ResolvedTechsets)/$($entry.DeclaredTechsets) | verified=$($entry.IdentityVerified) | exit=$($entry.ExitCode)")
    $text.Add("  source=$($entry.SourceFastFile)")
    $text.Add("  log=$($entry.Log)")
}
$text.Add("Native Xbox Aztec FF status: $nativeStatus")
$text.Add("Native Xbox FF: $(if ($native) { $native } else { '<not provided>' })")
if ($null -ne $closure) {
    $text.Add("Native matched TechniqueSets: $($closure.DonorBoundTechniqueRoots)")
    $text.Add("Native reachable unresolved TechniqueSets: $($closure.UnresolvedTechniqueRoots)")
    $text.Add("Native dead/unreachable PC TechniqueSets: $($closure.DeadPcTechniqueRoots)")
    $text.Add("Native identity closure ready: $($closure.IdentityClosureComplete)")
    $text.Add("Native graph extraction ready: $($closure.GraphExtractionReady)")
    $text.Add("Full details: $($manifest.nativeClosureReport)")
}
if ($null -ne $closureRun) { $text.Add("Native CLI exit=$($closureRun.ExitCode), log=$($closureRun.Log)") }
$text.Add('NO hardware FF generated. Output is diagnostic/oracle metadata only.')
[IO.File]::WriteAllLines($reportFile,[string[]]$text.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[MATERIAL-OAT] ==================== RESULT ==============================='
Write-Host "[MATERIAL-OAT] OAT TechniqueSet identities mapped: $oatMapped/$declared"
Write-Host "[MATERIAL-OAT] FF identity passes: $verified/$($sourceFastfiles.Count)"
Write-Host "[MATERIAL-OAT] Native Aztec FF: $nativeStatus"
if ($null -ne $closure) {
    Write-Host "[MATERIAL-OAT] Native donor-bound=$($closure.DonorBoundTechniqueRoots) unresolved=$($closure.UnresolvedTechniqueRoots) graphReady=$($closure.GraphExtractionReady)"
}
Write-Host "[MATERIAL-OAT] Report: $reportFile"
Write-Host "[MATERIAL-OAT] JSON: $manifestFile"
Write-Host '[MATERIAL-OAT] Diagnostic stage only; Xbox FF production still gated.'
