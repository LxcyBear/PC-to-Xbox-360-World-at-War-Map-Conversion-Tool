[CmdletBinding()]
param([switch]$BuildTraceOat)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$work=Join-Path $base 'renderer_preflight\4e_validation'
$ff=Join-Path $work 'nazi_zombie_aztec.ff'
$emitPath=Join-Path $work 'nuclear_xenon_renderer_emit.json'
$previousPath=Join-Path $work 'aztec_4e_validation_result.json'
$identityPath=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$proofPath=Join-Path $base 'native_graph_proof\native_graph_proof_result.json'
$binRoot=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86'
$exe=Join-Path $binRoot 'Unlinker.exe'
$capFile=Join-Path $binRoot 'waw360-graph-dumper-capabilities.json'
foreach($f in @($ff,$emitPath,$previousPath,$identityPath,$proofPath)) {
    if(-not (Test-Path -LiteralPath $f -PathType Leaf)) { throw "Missing prior Stage 4E input: $f" }
}
$emit=Get-Content -LiteralPath $emitPath -Raw | ConvertFrom-Json
$prev=Get-Content -LiteralPath $previousPath -Raw | ConvertFrom-Json
$identity=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$proof=Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json
$native=[string]$proof.verifiedGoldenXboxFastFile
$golden='e0f3c244dd7b72d0fe71c05faf014c2c9bfb05f770183954b3e7cde625eba98f'
if ([string]$prev.status -ne '4E_EMISSION_VALIDATION_BLOCKED' -or
    [string]$prev.schema -ne 'WaW360.AztecStage4ENativeProof.v1' -or
    [string]$prev.outputFF -ine $ff -or
    [long]$prev.outputFFBytes -ne [long](Get-Item -LiteralPath $ff).Length -or
    -not [bool]$emit.LayoutReplayExact -or
    [bool]$emit.ValidationPass -or
    -not [bool]$identity.TypeSequenceVerified -or
    [string]$identity.NativeFastFile -ine $native -or
    [string]$proof.nativeSha256 -ine $golden) {
    throw 'The existing failed native Stage 4E proof does not match this source set.'
}
if (-not (Test-Path -LiteralPath $native -PathType Leaf) -or
    [long](Get-Item -LiteralPath $native).Length -ne 82739469L -or
    (Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash -ine $golden) {
    throw 'Known-working golden Xbox reference is missing or changed.'
}
if ($BuildTraceOat) {
    Write-Host '[AZTEC-OAT-FAULT] ONE-TIME diagnostic OAT rebuild requested (opt-in).'
    $builder=Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1'
    & powershell.exe '-NoProfile' '-ExecutionPolicy' 'Bypass' '-File' $builder '-TraceNativeAssetLoader'
    if ($LASTEXITCODE -ne 0) { throw 'OAT trace build failed; no FF was changed.' }
}
if (-not (Test-Path -LiteralPath $exe -PathType Leaf) -or
    -not (Test-Path -LiteralPath $capFile -PathType Leaf)) {
    throw 'Traced OAT binary missing. Rerun with -BuildTraceOat if you authorize the one-time build.'
}
$cap=Get-Content -LiteralPath $capFile -Raw | ConvertFrom-Json
if ([string]$cap.oatRevision -ne '3beaa7c81af517f0c05f2b4612d477ed1541b676' -or
    't4xenon-asset-fault-trace-v1' -notin @($cap.capabilities)) {
    throw 'Traced loader capability absent. Rerun with -BuildTraceOat.'
}
function Invoke-Oat([string]$inputFF,[string]$log) {
    $save=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        & $exe '--list' '--no-color' $inputFF 2>&1 |
            ForEach-Object { [string]$_ } |
            Set-Content -LiteralPath $log -Encoding UTF8
        return [int]$LASTEXITCODE
    } finally { $ErrorActionPreference=$save }
}
Write-Host '[AZTEC-OAT-FAULT] ======================================================='
Write-Host '[AZTEC-OAT-FAULT] FIRST FAILED ROOT TRACE - REUSE EXISTING 4E FASTFILE'
Write-Host '[AZTEC-OAT-FAULT] No OAT graph redump, no FF emission, no hardware candidate.'
Write-Host '[AZTEC-OAT-FAULT] ======================================================='
$controlLog=Join-Path $work 'oat_first_fault_golden_control.log'
$failLog=Join-Path $work 'oat_first_fault_candidate.log'
$controlCode=Invoke-Oat $native $controlLog
Write-Host "[AZTEC-OAT-FAULT] Golden control OAT exit=$controlCode"
if ($controlCode -ne 0) {
    Get-Content -LiteralPath $controlLog -Tail 12 | ForEach-Object { Write-Host "[AZTEC-OAT-FAULT] $_" }
    throw 'The instrumented OAT loader cannot load the known-working native FF; do not trust the diagnosis.'
}
$failedCode=Invoke-Oat $ff $failLog
$trace=@(Select-String -LiteralPath $failLog -Pattern 'AZTEC-OAT-FAULT\] stage=' | Select-Object -Last 1)
$stage='UNATTRIBUTED';$idx=$null;$rootType='';$rootName='';$typeId=$null
if ($trace.Count -eq 1) {
    $line=[string]$trace[0].Line
    if($line -match 'stage=([A-Za-z]+)') { $stage=$Matches[1] }
    if($line -match 'rootIndex=(\d+)') {
        $idx=[int]$Matches[1]
        $entries=@($identity.Entries)
        if($idx -lt 0 -or $idx -ge $entries.Count) { throw "OAT reported out-of-range root index $idx" }
        $root=$entries[$idx]
        $rootType=[string]$root.TypeName
        $rootName=[string]$root.Name
        $typeId=[int]$root.TypeId
        if($line -match 'typeId=(\d+)') {
            if([int]$Matches[1] -ne $typeId) { throw "OAT type mismatch at root index $idx" }
        }
    }
}
$invalidBlock=@(Select-String -LiteralPath $failLog -Pattern 'invalid block 7').Count -gt 0
if($stage -eq 'asset' -and $null -ne $idx -and $invalidBlock) { $status='FIRST_BLOCK7_ASSET_CAPTURED' }
elseif($stage -eq 'scriptstrings' -and $invalidBlock) { $status='BLOCK7_BEFORE_FIRST_ASSET' }
else { $status='FIRST_FAULT_STILL_UNATTRIBUTED' }
$json=Join-Path $work 'oat_first_fault_result.json'
$txt=Join-Path $work 'oat_first_fault_result.txt'
$tail=@(Get-Content -LiteralPath $failLog -Tail 14)
$report=[ordered]@{
    schema='WaW360.AztecOatFirstFault.v1'
    status=$status
    goldenReferenceOatExit=$controlCode
    candidateOatExit=$failedCode
    faultStage=$stage
    firstFaultRootIndex=$idx
    firstFaultNativeType=$rootType
    firstFaultNativeName=$rootName
    firstFaultTypeId=$typeId
    invalidBlock7=$invalidBlock
    specificPointerFieldKnown=$false
    candidateFF=$ff
    candidateLog=$failLog
    controlLog=$controlLog
    tail=[string[]]$tail
    emittedNewFF=$false
    readyForHardware=$false
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $json -Encoding UTF8
$lines=@(
    'AZTEC 4E FIRST OAT LOADER EXCEPTION - EXISTING FF ONLY'
    "status=$status"
    "goldenControl=$controlCode candidateExit=$failedCode"
    "stage=$stage index=$idx type=$rootType name=$rootName typeId=$typeId"
    'This identifies the root being loaded, NOT the exact bad pointer field.'
    'NO FF rebuilt; no Xbox test; no Converted Maps promotion.'
    "log=$failLog"
) + $tail
[IO.File]::WriteAllLines($txt,[string[]]$lines,[Text.UTF8Encoding]::new($false))
Write-Host "[AZTEC-OAT-FAULT] status=$status; candidate OAT exit=$failedCode"
Write-Host "[AZTEC-OAT-FAULT] first root index=$idx type=$rootType name=$rootName stage=$stage"
Write-Host "[AZTEC-OAT-FAULT] Report: $txt"
foreach($line in @($tail | Select-Object -Last 6)) { Write-Host "[AZTEC-OAT-FAULT] $line" }
if ($status -eq 'FIRST_FAULT_STILL_UNATTRIBUTED') { exit 4 }
