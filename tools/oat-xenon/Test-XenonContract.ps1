[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OatDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Assert-Source([string]$file, [string[]]$patterns) {
    $path = Join-Path $OatDirectory $file
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing Xenon contract source: $file" }
    $data = Get-Content -LiteralPath $path -Raw
    foreach ($pattern in $patterns) {
        if ($data -cnotmatch [regex]::Escape($pattern)) { throw "Xenon contract mismatch in $file : $pattern" }
    }
}
Assert-Source 'src\ZoneCommon\Game\T4Xenon\ZoneConstantsT4Xenon.h' @(
 'MAGIC_SIGNED = "IWff0100"','MAGIC_UNSIGNED = "IWffu100"','ZONE_VERSION = 387','OFFSET_BLOCK_BIT_COUNT = 3u'
)
Assert-Source 'src\ZoneCode\Game\T4Xenon\T4Xenon_Commands.txt' @(
 'game T4Xenon;','wordsize 32;','endianness big;','asset MaterialPixelShader AssetPixelShader;','asset clipMap_t AssetClipMapPvs;'
)
Assert-Source 'src\Common\Game\T4Xenon\T4Xenon_Assets.h' @(
 'ASSET_TYPE_PIXELSHADER = 0x7','ASSET_TYPE_TECHNIQUE_SET = 0x8','ASSET_TYPE_IMAGE = 0x9','ASSET_TYPE_CLIPMAP_PVS = 0xD','ASSET_TYPE_PACKINDEX = 0x23','ASSET_TYPE_COUNT = 0x24',
 'XFILE_BLOCK_VIRTUAL = 0x4','XFILE_BLOCK_LARGE = 0x5','XFILE_BLOCK_PHYSICAL = 0x6'
)
Assert-Source 'src\ZoneCode\Game\T4Xenon\XAssets\GfxImage.txt' @('set allocalign pixels 4096;')
Assert-Source 'src\ZoneCode\Game\T4Xenon\XAssets\LoadedSound.txt' @('set allocalign packet::pBuffer 2048;')
Assert-Source 'src\ZoneCode\Game\T4Xenon\XAssets\MaterialTechniqueSet.txt' @('set reusable vertexShader;','XFILE_BLOCK_PHYSICAL')
$catalog = Join-Path $PSScriptRoot '..\..\src\WaW360.Core\FastFiles\T4AssetTypeCatalog.cs'
$catalogText = Get-Content -LiteralPath $catalog -Raw
$xboxBlock = [regex]::Match($catalogText, 'private static readonly string\[\] XboxTypes\s*=\s*\{(?<items>.*?)\};', 'Singleline')
if (-not $xboxBlock.Success) { throw 'Converter Xbox asset table could not be read.' }
$names = [regex]::Matches($xboxBlock.Groups['items'].Value, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
if ($names.Count -ne 36) { throw "Expected 36 Xbox asset kinds, got $($names.Count)" }
foreach ($pair in @(@(7,'pixelshader'),@(8,'techset'),@(9,'image'),@(13,'clipmap_pvs'),@(35,'packindex'))) {
    if ($names[$pair[0]] -ne $pair[1]) { throw "Xbox type index $($pair[0]) mismatch." }
}
Write-Host '[OAT] PASS: source format contract, alignment and C# Xbox asset index agreement.'
