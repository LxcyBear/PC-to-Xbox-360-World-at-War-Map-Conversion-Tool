[CmdletBinding()]
param(
    [string]$PcMapsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps',
    [string]$MapName = 'nazi_zombie_FRUmansion',
    [string]$NativeControlDirectory = 'C:\Users\jorda\OneDrive\Desktop\working frumansion',
    [switch]$RefreshBridge,
    [switch]$ForceRebuildGraphDumper
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$corpus = Join-Path $PSScriptRoot 'Run-FRUmansionMaterialCorpus.ps1'
$graphs = Join-Path $PSScriptRoot 'Run-FRUmansionMaterialRelocatableGraph.ps1'
$replay = Join-Path $PSScriptRoot 'Run-FRUmansionMaterialReplay.ps1'

Write-Host '[NUCLEAR-4Z] ================================================================'
Write-Host '[NUCLEAR-4Z] MATERIAL CLOSURE PIPELINE'
Write-Host '[NUCLEAR-4Z] ================================================================'
Write-Host '[NUCLEAR-4Z] 4Z-A: PC semantic material JSON -> Xenon OAT corpus'
Write-Host '[NUCLEAR-4Z] 4Z-B: exact relocatable Xenon Material graph extraction'
Write-Host '[NUCLEAR-4Z] 4Z-C: standalone exact 502-Material replay'
Write-Host '[NUCLEAR-4Z] Native material bytes are oracle-only and never production payload.'
Write-Host ''

$corpusArgs = @{
    PcMapsRoot = $PcMapsRoot
    MapName = $MapName
    NativeControlDirectory = $NativeControlDirectory
}
if ($RefreshBridge.IsPresent) {
    $corpusArgs.RefreshBridge = $true
}

& $corpus @corpusArgs
if ($LASTEXITCODE -ne 0 -or -not $?) {
    throw "Material corpus stage 4Z-A failed with exit code $LASTEXITCODE."
}

$graphArgs = @{}
if ($ForceRebuildGraphDumper.IsPresent) {
    $graphArgs.ForceRebuildGraphDumper = $true
}

& $graphs @graphArgs
if ($LASTEXITCODE -ne 0 -or -not $?) {
    throw "Material graph stage 4Z-B failed with exit code $LASTEXITCODE."
}

& $replay
if ($LASTEXITCODE -ne 0 -or -not $?) {
    throw "Material replay stage 4Z-C failed with exit code $LASTEXITCODE."
}

Write-Host ''
Write-Host '[NUCLEAR-4Z] MATERIAL CORPUS + GRAPH + EXACT REPLAY: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4Z] True PC Material identities are restored before Xenon linking.'
Write-Host '[NUCLEAR-4Z] Next gate: 4Z-D0 root binding, then the 6,811-root production graft.'
exit 0
