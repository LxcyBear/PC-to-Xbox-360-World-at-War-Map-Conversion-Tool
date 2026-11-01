[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# ONE ACTUAL integrated Stage 4D/4E FastFile build, not a standalone
# structure canary: 39 PC-owned Xenon Materials + source world root +
# 735 live GfxSurface Material* refs + 32713 vertices / 58653 indices.
# Current implementation emits the world CORE; OAT's real loader is the
# next decision point and missing world child arrays remain open work.
# This is NOT an approved native Xbox package until the final paired FFs
# and the CoD Xe package pass Approve-AztecNativeSmokeTest.ps1.
$runner=Join-Path $PSScriptRoot 'Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1'
& $runner -IntegrateWorld
if(-not $?){throw "Actual original-PC GfxWorld integrated FF/OAT run failed."}
