[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# Fail FAST before an expensive x86 OAT rebuild or an entire 8509-root FF.
# Pinned T4Xenon GfxWorld.txt traversal:
# indices/sky, reflectionProbes, dpvsPlanes(planes/nodes), cells,
# lightmaps, embedded lightGrid, models ...
# Both the virtual allocation plan and physical emitter MUST match.
# Previous 4510629-byte native FF remains untouched by this source test.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$graft=Join-Path $repo 'src\WaW360.Core\FastFiles\T4XenonAztecWorldGraft.cs'
$headers=Join-Path $repo 'src\WaW360.Core\FastFiles\T4XenonAztecGate11ImageHeaders.cs'
foreach($file in @($graft,$headers)){
    if(-not(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing source: $file"}
}
$c=[IO.File]::ReadAllText($graft)
$h=[IO.File]::ReadAllText($headers)
$start=$c.IndexOf('    public void Plan(Action<long,int,bool> virtualAlloc,Action<int> tempRoot)',[StringComparison]::Ordinal)
$mid=$c.IndexOf('    public void Emit(Stream zone,Action<long,int,bool> virtualAlloc,',[StringComparison]::Ordinal)
if($start -lt 0 -or $mid -lt $start){throw 'Gate11 GfxWorld Plan/Emit functions absent or reordered unexpectedly.'}
$plan=$c.Substring($start,$mid-$start)
$emit=$c.Substring($mid)
function Assert-Order([string]$source,[string[]]$markers,[string]$label){
    $last=-1
    foreach($marker in $markers){
        $pos=$source.IndexOf($marker,[StringComparison]::Ordinal)
        if($pos -le $last){
            throw "$label native GfxWorld child load order WRONG at '$marker' (offset=$pos previous=$last)."
        }
        if($source.IndexOf($marker,$pos+1,[StringComparison]::Ordinal) -ge 0){
            throw "$label repeated native GfxWorld child marker '$marker'."
        }
        $last=$pos
    }
    Write-Host "[AZTEC-GATE11-ORDER] PASS $label reflection BEFORE dpvsPlanes/cells; lightmaps AFTER cells, BEFORE embedded grid"
}
$planOrder=@(
    '_gate11Primitives?.PlanSky(virtualAlloc);',
    '_gate11ImageHeaders?.PlanReflectionProbes(virtualAlloc);',
    'if(_gate8Planes is not null)',
    'if(_gate8Nodes is not null)',
    '_gate11Cells?.Plan(virtualAlloc);',
    '_gate11ImageHeaders?.PlanLightmaps(virtualAlloc);',
    '_gate11Lighting?.PlanGrid(virtualAlloc);',
    '_gate11Primitives?.PlanModels(virtualAlloc);'
)
$emitOrder=@(
    '_gate11Primitives?.EmitSky(zone,virtualAlloc);',
    '_gate11ImageHeaders?.EmitReflectionProbes(zone,virtualAlloc);',
    'if(_gate8Planes is not null)',
    'if(_gate8Nodes is not null)',
    '_gate11Cells?.Emit(zone,virtualAlloc);',
    '_gate11ImageHeaders?.EmitLightmaps(zone,virtualAlloc);',
    '_gate11Lighting?.EmitGrid(zone,virtualAlloc);',
    '_gate11Primitives?.EmitModels(zone,virtualAlloc);'
)
Assert-Order $plan $planOrder 'Plan'
Assert-Order $emit $emitOrder 'Emit'
foreach($method in @('PlanReflectionProbes','EmitReflectionProbes','PlanLightmaps','EmitLightmaps')){
    if($h.IndexOf("void $method(",[StringComparison]::Ordinal) -lt 0){
        throw "Gate11 image header serializer missing split native method: $method"
    }
}
if($c.Contains('?.PlanHeaders(') -or $c.Contains('?.EmitHeaders(')){
    throw 'Old reflection+lightmap combined writer still emits BOTH arrays at the same wrong child position.'
}
# Optional independent direct contract test when pinned OAT tree is installed.
$nativeContract=Join-Path $repo '.deps\OpenAssetTools-xenon\src\ZoneCode\Game\T4Xenon\XAssets\GfxWorld.txt'
if(Test-Path -LiteralPath $nativeContract -PathType Leaf){
    $t=[IO.File]::ReadAllText($nativeContract)
    $gfx=$t.Substring(0,$t.IndexOf('// GfxWorldDpvsPlanes',[StringComparison]::Ordinal))
    Assert-Order $gfx @(
        'set count reflectionProbes reflectionProbeCount;',
        'set count cells dpvsPlanes::cellCount;',
        'set count lightmaps lightmapCount;',
        'set count models modelCount;'
    ) 'Pinned OAT GfxWorld.txt'
}else{
    Write-Host '[AZTEC-GATE11-ORDER] Local pinned OAT source not installed yet; source-order invariant validated against known pinned contract.'
}
Write-Host '[AZTEC-GATE11-ORDER] PASS source-only native plan/emitter order; no build, FF, or Xbox hardware.'
