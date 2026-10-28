[CmdletBinding()]
param(
    [string]$MapName='nazi_zombie_aztec_V2.2',
    [string]$OutputRoot=''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($MapName -ine 'nazi_zombie_aztec_V2.2') { throw 'Aztec V2.2 only.' }
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if(-not $OutputRoot) { $OutputRoot=Join-Path $repo 'output\second_map_baselines' }
$root=Join-Path $OutputRoot $MapName
$base=Join-Path $root 'native_material_mapping'
$work=Join-Path $base 'renderer_preflight\4e_validation'
$identityPath=Join-Path $base 'main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$stagePath=Join-Path $work 'aztec_4e_validation_result.json'
$emitterPath=Join-Path $work 'nuclear_xenon_renderer_emit.json'
$oatLog=Join-Path $work 'oat_hard_gate\nazi_zombie_aztec_oat_xenon_list.log'
foreach($file in @($identityPath,$stagePath,$emitterPath,$oatLog)) {
    if(-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing Stage 4E evidence: $file" }
}
$identity=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$stage=Get-Content -LiteralPath $stagePath -Raw | ConvertFrom-Json
$emit=Get-Content -LiteralPath $emitterPath -Raw | ConvertFrom-Json
if(-not [bool]$identity.TypeSequenceVerified -or
    [string]$stage.schema -ne 'WaW360.AztecStage4ENativeProof.v1' -or
    [string]$stage.mapFolder -ine $MapName -or
    [string]$stage.status -ne '4E_EMISSION_VALIDATION_BLOCKED' -or
    -not [bool]$emit.LayoutReplayExact -or
    [bool]$emit.ValidationPass) {
    throw 'This source-safe audit requires the exact failed 4E native-reference byte emission.'
}
$roots=@($identity.Entries)
if($roots.Count -ne [int]$emit.NativeRoots) { throw 'Root identity counts mismatch.' }
$rendererTypes=@('pixelshader','techset','techniqueset')
$renderer=@($roots | Where-Object { ([string]$_.TypeName).Replace('_','').ToLowerInvariant() -in $rendererTypes })
$shells=@($roots | Where-Object { ([string]$_.TypeName).Replace('_','').ToLowerInvariant() -notin $rendererTypes })
$counts=@($shells | Group-Object -Property TypeName | Sort-Object -Property Count -Descending | ForEach-Object {
    $members=@($_.Group | Sort-Object Index)
    [pscustomobject]@{
        Type=[string]$_.Name
        Count=[int]$_.Count
        FirstRootIndex=[int]$members[0].Index
        FirstName=[string]$members[0].Name
        LastRootIndex=[int]$members[-1].Index
    }
})
$highDependency=@('material','image','xmodel','xanim','gfxworld','clipmap_pvs','clipmap','fx','weapon',
    'rawfile','stringtable','sound','loadedsound','mapents','comworld','gameworldsp','gameworldmp')
$complex=@($counts | Where-Object { ([string]$_.Type).ToLowerInvariant() -in $highDependency })
$logTail=@(Get-Content -LiteralPath $oatLog -Tail 20)
$block7=@($logTail | Where-Object {$_ -match 'invalid block 7'})
$status=if($block7.Count -gt 0){'INVALID_BLOCK_7_UNATTRIBUTED'}else{'OAT_LOAD_FAILURE_UNATTRIBUTED'}
$report=[ordered]@{
    schema='WaW360.AztecStage4EFailureAudit.v1'
    status=$status
    mapFolder=$MapName
    nativeRootCount=$roots.Count
    nativeRendererRoots=$renderer.Count
    nonRendererNameShells=$shells.Count
    shellTypes=[object[]]$counts
    complexNameShells=[object[]]$complex
    layoutReplayExact=[bool]$emit.LayoutReplayExact
    oatGateError=[string]$emit.OatGateError
    rawOatLog=$oatLog
    oatTail=[string[]]$logTail
    faultAssetIndex=$null
    faultPointerOffset=$null
    rootCauseProven=$false
    guidance=@(
        'The 4D and 4E byte streams agree; this does not prove OAT can traverse placeholder asset graphs.',
        'Invalid block 7 means the OAT loader interpreted a pointer with block index 7, outside the seven valid T4 blocks (0..6).',
        'Do not blindly remap block 7 to 6. The value may have been read from the wrong field due to a shell ABI or dependency graph mismatch.',
        'Most nonrenderer roots are intentionally empty/name-only. Empty shells are NOT complete materials, images, models, scripts or StringTables.',
        'The exact root and pointer field responsible have not been observed in the current log; instrument OAT load progress or source-backed asset serialization before claiming a cause.',
        'This stage never approves Xbox hardware testing or copying a diagnostic FF to Converted Maps.'
    )
}
$json=Join-Path $work 'aztec_4e_failure_audit.json'
$txt=Join-Path $work 'aztec_4e_failure_audit.txt'
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $json -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add('AZTEC 4E OAT INVALID BLOCK AUDIT - NO REDUMP / NO REBUILD / NO FF')
$lines.Add('===================================================================')
$lines.Add("status=$status nativeRoots=$($roots.Count) rendererRoots=$($renderer.Count) placeholderShells=$($shells.Count)")
$lines.Add("4E exact layout replay=$($emit.LayoutReplayExact)")
$lines.Add('Native non-renderer asset shells grouped by type:')
foreach($c in $counts) { $lines.Add("  $($c.Type): count=$($c.Count) first=$($c.FirstRootIndex) last=$($c.LastRootIndex)") }
$lines.Add('OAT error tail:')
foreach($line in $logTail) { $lines.Add("  $line") }
$lines.Add('PROVEN: OAT rejected an invalid block index; which root/field generated it is NOT proven.')
$lines.Add('NEXT: source-backed shell graph reconstruction and/or exact loader field tracing.')
$lines.Add('NO Xbox hardware candidate or release promotion.')
[IO.File]::WriteAllLines($txt,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-4E-AUDIT] ========================================================'
Write-Host "[AZTEC-4E-AUDIT] status=$status"
Write-Host "[AZTEC-4E-AUDIT] nativeRoots=$($roots.Count) renderer=$($renderer.Count) nameShells=$($shells.Count)"
Write-Host "[AZTEC-4E-AUDIT] high-dependency shell types=$($complex.Count)"
foreach($c in @($complex | Select-Object -First 16)) {
    Write-Host "[AZTEC-4E-AUDIT]  $($c.Type): $($c.Count) (root #$($c.FirstRootIndex))"
}
Write-Host '[AZTEC-4E-AUDIT] The responsible pointer/asset is still unknown. No byte patches applied.'
Write-Host "[AZTEC-4E-AUDIT] Report: $txt"
Write-Host "[AZTEC-4E-AUDIT] JSON: $json"
