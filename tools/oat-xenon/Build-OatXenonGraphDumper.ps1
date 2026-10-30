[CmdletBinding()]
param(
    [switch]$TraceNativeAssetLoader
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$revision = '3beaa7c81af517f0c05f2b4612d477ed1541b676'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$oat = Join-Path $root '.deps\OpenAssetTools-xenon'
$out = Join-Path $root '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86'
$prepare = Join-Path $PSScriptRoot 'Prepare-OatXenon.ps1'

if (-not (Test-Path -LiteralPath $prepare -PathType Leaf)) { throw "Missing OAT bootstrap helper: $prepare" }

Write-Host '[OAT-GRAPH] Ensuring pinned clean Xenon OAT checkout...'
& $prepare
if ($LASTEXITCODE -ne 0 -or -not $?) { throw 'Pinned OAT preparation failed.' }

Push-Location $oat
try {
    $head = (& git rev-parse HEAD).Trim()
    if ($head -ne $revision) { throw "Unexpected OAT revision: $head" }

    $changes = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect OAT working tree.' }
    if ($changes.Count -ne 0) { throw ('OAT checkout is not clean before graph patch:' + [Environment]::NewLine + ($changes -join [Environment]::NewLine)) }

    $srcDir = Join-Path $oat 'src\ObjWriting\Game\T4Xenon\Techset'
    $imageSrcDir = Join-Path $oat 'src\ObjWriting\Game\T4Xenon\Image'
    $materialSrcDir = Join-Path $oat 'src\ObjWriting\Game\T4Xenon\Material'
    $pcXModelSrcDir = Join-Path $oat 'src\ObjWriting\Game\T4\XModel'
    $pcWorldSrcDir = Join-Path $oat 'src\ObjWriting\Game\T4\GfxWorld'
    $xenonXModelSrcDir = Join-Path $oat 'src\ObjWriting\Game\T4Xenon\XModel'
    $xenonWorldSrcDir = Join-Path $oat 'src\ObjWriting\Game\T4Xenon\GfxWorld'
    New-Item -ItemType Directory -Force -Path $srcDir | Out-Null
    New-Item -ItemType Directory -Force -Path $imageSrcDir | Out-Null
    New-Item -ItemType Directory -Force -Path $materialSrcDir | Out-Null
    New-Item -ItemType Directory -Force -Path $pcXModelSrcDir | Out-Null
    New-Item -ItemType Directory -Force -Path $pcWorldSrcDir | Out-Null
    New-Item -ItemType Directory -Force -Path $xenonXModelSrcDir | Out-Null
    New-Item -ItemType Directory -Force -Path $xenonWorldSrcDir | Out-Null

    $graphSrc = Join-Path $PSScriptRoot 'graph-dumper'
    $headerSrc = Join-Path $graphSrc 'TechniqueSetGraphDumperT4Xenon.h'
    $cppSrc = Join-Path $graphSrc 'TechniqueSetGraphDumperT4Xenon.cpp'
    if (-not (Test-Path -LiteralPath $headerSrc -PathType Leaf) -or -not (Test-Path -LiteralPath $cppSrc -PathType Leaf)) { throw 'Graph dumper source is missing from converter repo.' }

    $headerDst = Join-Path $srcDir 'TechniqueSetGraphDumperT4Xenon.h'
    $cppDst = Join-Path $srcDir 'TechniqueSetGraphDumperT4Xenon.cpp'
    Copy-Item -LiteralPath $headerSrc -Destination $headerDst -Force
    Copy-Item -LiteralPath $cppSrc -Destination $cppDst -Force

    $imageHeaderSrc = Join-Path $graphSrc 'ImageGraphDumperT4Xenon.h'
    $imageCppSrc = Join-Path $graphSrc 'ImageGraphDumperT4Xenon.cpp'
    if (-not (Test-Path -LiteralPath $imageHeaderSrc -PathType Leaf) -or -not (Test-Path -LiteralPath $imageCppSrc -PathType Leaf)) { throw 'Image graph dumper source is missing from converter repo.' }
    $imageHeaderDst = Join-Path $imageSrcDir 'ImageGraphDumperT4Xenon.h'
    $imageCppDst = Join-Path $imageSrcDir 'ImageGraphDumperT4Xenon.cpp'
    Copy-Item -LiteralPath $imageHeaderSrc -Destination $imageHeaderDst -Force
    Copy-Item -LiteralPath $imageCppSrc -Destination $imageCppDst -Force

    $materialHeaderSrc = Join-Path $graphSrc 'MaterialRelocatableGraphDumperT4Xenon.h'
    $materialCppSrc = Join-Path $graphSrc 'MaterialRelocatableGraphDumperT4Xenon.cpp'
    if (-not (Test-Path -LiteralPath $materialHeaderSrc -PathType Leaf) -or
        -not (Test-Path -LiteralPath $materialCppSrc -PathType Leaf)) {
        throw 'Xenon Material relocatable graph dumper source is missing from converter repo.'
    }
    $materialHeaderDst = Join-Path $materialSrcDir 'MaterialRelocatableGraphDumperT4Xenon.h'
    $materialCppDst = Join-Path $materialSrcDir 'MaterialRelocatableGraphDumperT4Xenon.cpp'
    Copy-Item -LiteralPath $materialHeaderSrc -Destination $materialHeaderDst -Force
    Copy-Item -LiteralPath $materialCppSrc -Destination $materialCppDst -Force

    $pcClassHeaderSrc = Join-Path $graphSrc 'XModelClassificationDumperT4.h'
    $pcClassCppSrc = Join-Path $graphSrc 'XModelClassificationDumperT4.cpp'
    if (-not (Test-Path -LiteralPath $pcClassHeaderSrc -PathType Leaf) -or -not (Test-Path -LiteralPath $pcClassCppSrc -PathType Leaf)) { throw 'PC XModel classification dumper source is missing from converter repo.' }
    $pcClassHeaderDst = Join-Path $pcXModelSrcDir 'XModelClassificationDumperT4.h'
    $pcClassCppDst = Join-Path $pcXModelSrcDir 'XModelClassificationDumperT4.cpp'
    Copy-Item -LiteralPath $pcClassHeaderSrc -Destination $pcClassHeaderDst -Force
    Copy-Item -LiteralPath $pcClassCppSrc -Destination $pcClassCppDst -Force

    # Independent post-OAT-LOAD original-PC GfxWorld Material* witness.
    # A new asset dumper, not a byte scanner or a binding writer.
    $pcWorldHeaderSrc = Join-Path $graphSrc 'GfxWorldMaterialPointerWitnessT4.h'
    $pcWorldCppSrc = Join-Path $graphSrc 'GfxWorldMaterialPointerWitnessT4.cpp'
    if (-not (Test-Path -LiteralPath $pcWorldHeaderSrc -PathType Leaf) -or
        -not (Test-Path -LiteralPath $pcWorldCppSrc -PathType Leaf)) {
        throw 'Required PC OAT loaded GfxWorld Material* witness dumper sources missing.'
    }
    Copy-Item -LiteralPath $pcWorldHeaderSrc -Destination (Join-Path $pcWorldSrcDir 'GfxWorldMaterialPointerWitnessT4.h') -Force
    Copy-Item -LiteralPath $pcWorldCppSrc -Destination (Join-Path $pcWorldSrcDir 'GfxWorldMaterialPointerWitnessT4.cpp') -Force

    # Export all 39 original-PC world Material body/table sources so the
    # production graph writer can eventually own their real dependencies.
    $pcCorpusH = Join-Path $graphSrc 'GfxWorldMaterialCorpusDumperT4.h'
    $pcCorpusC = Join-Path $graphSrc 'GfxWorldMaterialCorpusDumperT4.cpp'
    if (-not (Test-Path -LiteralPath $pcCorpusH -PathType Leaf) -or
        -not (Test-Path -LiteralPath $pcCorpusC -PathType Leaf)) {
        throw 'PC source-owned 39-world-Material graph corpus native dumper sources missing.'
    }
    Copy-Item -LiteralPath $pcCorpusH -Destination (Join-Path $pcWorldSrcDir 'GfxWorldMaterialCorpusDumperT4.h') -Force
    Copy-Item -LiteralPath $pcCorpusC -Destination (Join-Path $pcWorldSrcDir 'GfxWorldMaterialCorpusDumperT4.cpp') -Force

    $xmodelInventoryHeaderSrc = Join-Path $graphSrc 'XModelGraphInventoryDumperT4Xenon.h'
    $xmodelInventoryCppSrc = Join-Path $graphSrc 'XModelGraphInventoryDumperT4Xenon.cpp'
    if (-not (Test-Path -LiteralPath $xmodelInventoryHeaderSrc -PathType Leaf) -or
        -not (Test-Path -LiteralPath $xmodelInventoryCppSrc -PathType Leaf)) {
        throw 'Xenon XModel graph inventory dumper source is missing from converter repo.'
    }
    $xmodelInventoryHeaderDst = Join-Path $xenonXModelSrcDir 'XModelGraphInventoryDumperT4Xenon.h'
    $xmodelInventoryCppDst = Join-Path $xenonXModelSrcDir 'XModelGraphInventoryDumperT4Xenon.cpp'
    Copy-Item -LiteralPath $xmodelInventoryHeaderSrc -Destination $xmodelInventoryHeaderDst -Force
    Copy-Item -LiteralPath $xmodelInventoryCppSrc -Destination $xmodelInventoryCppDst -Force

    $xmodelRelocHeaderSrc = Join-Path $graphSrc 'XModelRelocatableGraphDumperT4Xenon.h'
    $xmodelRelocCppSrc = Join-Path $graphSrc 'XModelRelocatableGraphDumperT4Xenon.cpp'
    if (-not (Test-Path -LiteralPath $xmodelRelocHeaderSrc -PathType Leaf) -or
        -not (Test-Path -LiteralPath $xmodelRelocCppSrc -PathType Leaf)) {
        throw 'Xenon relocatable XModel graph dumper source is missing from converter repo.'
    }
    $xmodelRelocHeaderDst = Join-Path $xenonXModelSrcDir 'XModelRelocatableGraphDumperT4Xenon.h'
    $xmodelRelocCppDst = Join-Path $xenonXModelSrcDir 'XModelRelocatableGraphDumperT4Xenon.cpp'
    Copy-Item -LiteralPath $xmodelRelocHeaderSrc -Destination $xmodelRelocHeaderDst -Force
    Copy-Item -LiteralPath $xmodelRelocCppSrc -Destination $xmodelRelocCppDst -Force

    $xenonWorldHeaderSrc = Join-Path $graphSrc 'GfxWorldSurfaceAbiWitnessT4Xenon.h'
    $xenonWorldCppSrc = Join-Path $graphSrc 'GfxWorldSurfaceAbiWitnessT4Xenon.cpp'
    if (-not (Test-Path -LiteralPath $xenonWorldHeaderSrc -PathType Leaf) -or
        -not (Test-Path -LiteralPath $xenonWorldCppSrc -PathType Leaf)) {
        throw 'Pinned native Xbox GfxWorld ABI/topMip pointer witness source missing.'
    }
    Copy-Item -LiteralPath $xenonWorldHeaderSrc -Destination (Join-Path $xenonWorldSrcDir 'GfxWorldSurfaceAbiWitnessT4Xenon.h') -Force
    Copy-Item -LiteralPath $xenonWorldCppSrc -Destination (Join-Path $xenonWorldSrcDir 'GfxWorldSurfaceAbiWitnessT4Xenon.cpp') -Force

    $writer = Join-Path $oat 'src\ObjWriting\Game\T4Xenon\ObjWriterT4Xenon.cpp'
    $text = Get-Content -LiteralPath $writer -Raw
    # ObjWriterT4Xenon.cpp lives in Game/T4Xenon, so this pinned revision
    # uses paths relative to that directory for local dumpers.
    $includeNeedle = '#include "Techset/PixelShaderDumperT4Xenon.h"'
    $includeLine = '#include "Techset/TechniqueSetGraphDumperT4Xenon.h"'
    if ($text -notmatch [regex]::Escape($includeLine)) {
        if ($text -notmatch [regex]::Escape($includeNeedle)) { throw 'Could not find T4Xenon PixelShader dumper include anchor.' }
        $text = $text.Replace($includeNeedle, $includeNeedle + [Environment]::NewLine + $includeLine)
    }

    $imageIncludeNeedle = '#include "Game/T4Xenon/Image/ImageDumperT4Xenon.h"'
    $imageIncludeLine = '#include "Image/ImageGraphDumperT4Xenon.h"'
    if ($text -notmatch [regex]::Escape($imageIncludeLine)) {
        if ($text -notmatch [regex]::Escape($imageIncludeNeedle)) { throw 'Could not find T4Xenon image dumper include anchor.' }
        $text = $text.Replace($imageIncludeNeedle, $imageIncludeNeedle + [Environment]::NewLine + $imageIncludeLine)
    }

    $materialIncludeNeedle = '#include "Game/T4Xenon/Material/MaterialJsonDumperT4Xenon.h"'
    $materialIncludeLine = '#include "Material/MaterialRelocatableGraphDumperT4Xenon.h"'
    if ($text -notmatch [regex]::Escape($materialIncludeLine)) {
        if ($text -notmatch [regex]::Escape($materialIncludeNeedle)) { throw 'Could not find T4Xenon material dumper include anchor.' }
        $text = $text.Replace($materialIncludeNeedle, $materialIncludeNeedle + [Environment]::NewLine + $materialIncludeLine)
    }

    $xmodelIncludeNeedle = '#include "Game/T4Xenon/XModel/XModelDumperT4Xenon.h"'
    $xmodelIncludeLine = '#include "XModel/XModelGraphInventoryDumperT4Xenon.h"'
    if ($text -notmatch [regex]::Escape($xmodelIncludeLine)) {
        if ($text -notmatch [regex]::Escape($xmodelIncludeNeedle)) { throw 'Could not find T4Xenon XModel dumper include anchor.' }
        $text = $text.Replace($xmodelIncludeNeedle, $xmodelIncludeNeedle + [Environment]::NewLine + $xmodelIncludeLine)
    }

    $xmodelRelocIncludeLine = '#include "XModel/XModelRelocatableGraphDumperT4Xenon.h"'
    if ($text -notmatch [regex]::Escape($xmodelRelocIncludeLine)) {
        if ($text -notmatch [regex]::Escape($xmodelIncludeLine)) { throw 'Could not find T4Xenon graph inventory include anchor.' }
        $text = $text.Replace($xmodelIncludeLine, $xmodelIncludeLine + [Environment]::NewLine + $xmodelRelocIncludeLine)
    }

    $worldXenonInclude = '#include "GfxWorld/GfxWorldSurfaceAbiWitnessT4Xenon.h"'
    if ($text -notmatch [regex]::Escape($worldXenonInclude)) {
        if ($text -notmatch [regex]::Escape($xmodelRelocIncludeLine)) {
            throw 'Pinned T4Xenon XModel reloc include anchor missing for GfxWorld.'
        }
        $text = $text.Replace($xmodelRelocIncludeLine,
            $xmodelRelocIncludeLine + [Environment]::NewLine + $worldXenonInclude)
    }

    $registerNeedle = 'RegisterAssetDumper(std::make_unique<techset::PixelShaderDumperT4Xenon>());'
    $registerLine = 'RegisterAssetDumper(std::make_unique<techset::TechniqueSetGraphDumperT4Xenon>());'
    if ($text -notmatch [regex]::Escape($registerLine)) {
        if ($text -notmatch [regex]::Escape($registerNeedle)) { throw 'Could not find T4Xenon PixelShader dumper registration anchor.' }
        $text = $text.Replace($registerNeedle, $registerNeedle + [Environment]::NewLine + '    ' + $registerLine)
    }

    $imageRegisterNeedle = 'RegisterAssetDumper(std::make_unique<image::DumperT4Xenon>());'
    $imageRegisterLine = 'RegisterAssetDumper(std::make_unique<image::ImageGraphDumperT4Xenon>());'
    if ($text -notmatch [regex]::Escape($imageRegisterLine)) {
        if ($text -notmatch [regex]::Escape($imageRegisterNeedle)) { throw 'Could not find T4Xenon image dumper registration anchor.' }
        $text = $text.Replace($imageRegisterNeedle, $imageRegisterNeedle + [Environment]::NewLine + '    ' + $imageRegisterLine)
    }

    $materialRegisterNeedle = 'RegisterAssetDumper(std::make_unique<material::JsonDumperT4Xenon>());'
    $materialRegisterLine = 'RegisterAssetDumper(std::make_unique<material::RelocatableGraphDumperT4Xenon>());'
    if ($text -notmatch [regex]::Escape($materialRegisterLine)) {
        if ($text -notmatch [regex]::Escape($materialRegisterNeedle)) { throw 'Could not find T4Xenon material dumper registration anchor.' }
        $text = $text.Replace($materialRegisterNeedle, $materialRegisterNeedle + [Environment]::NewLine + '    ' + $materialRegisterLine)
    }

    $xmodelRegisterNeedle = 'RegisterAssetDumper(std::make_unique<xmodel::DumperT4Xenon>());'
    $xmodelRegisterLine = 'RegisterAssetDumper(std::make_unique<xmodel::GraphInventoryDumperT4Xenon>());'
    if ($text -notmatch [regex]::Escape($xmodelRegisterLine)) {
        if ($text -notmatch [regex]::Escape($xmodelRegisterNeedle)) { throw 'Could not find T4Xenon XModel dumper registration anchor.' }
        $text = $text.Replace($xmodelRegisterNeedle, $xmodelRegisterNeedle + [Environment]::NewLine + '    ' + $xmodelRegisterLine)
    }

    $xmodelRelocRegisterLine = 'RegisterAssetDumper(std::make_unique<xmodel::RelocatableGraphDumperT4Xenon>());'
    if ($text -notmatch [regex]::Escape($xmodelRelocRegisterLine)) {
        if ($text -notmatch [regex]::Escape($xmodelRegisterLine)) { throw 'Could not find T4Xenon graph inventory registration anchor.' }
        $text = $text.Replace($xmodelRegisterLine, $xmodelRegisterLine + [Environment]::NewLine + '    ' + $xmodelRelocRegisterLine)
    }
    # Set-Content appends a record terminator and can also change BOM semantics.
    # Preserve UTF-8 without BOM and force exactly one trailing LF.
    $worldXenonRegister = 'RegisterAssetDumper(std::make_unique<gfxworld::SurfaceAbiWitnessT4Xenon>());'
    if ($text -notmatch [regex]::Escape($worldXenonRegister)) {
        if ($text -notmatch [regex]::Escape($xmodelRelocRegisterLine)) {
            throw 'Pinned T4Xenon XModel reloc registration anchor missing for GfxWorld.'
        }
        $text = $text.Replace($xmodelRelocRegisterLine,
            $xmodelRelocRegisterLine + [Environment]::NewLine + '    ' + $worldXenonRegister)
    }

    $normalizedText = $text.TrimEnd([char]13, [char]10) + [char]10
    [System.IO.File]::WriteAllText(
        $writer,
        $normalizedText,
        [System.Text.UTF8Encoding]::new($false))

    $pcWriter = Join-Path $oat 'src\ObjWriting\Game\T4\ObjWriterT4.cpp'
    $pcText = Get-Content -LiteralPath $pcWriter -Raw
    $pcIncludeNeedle = '#include "Game/T4/XModel/XModelDumperT4.h"'
    $pcIncludeLine = '#include "Game/T4/XModel/XModelClassificationDumperT4.h"'
    if ($pcText -notmatch [regex]::Escape($pcIncludeLine)) {
        if ($pcText -notmatch [regex]::Escape($pcIncludeNeedle)) { throw 'Could not find T4 XModel dumper include anchor.' }
        $pcText = $pcText.Replace($pcIncludeNeedle, $pcIncludeNeedle + [Environment]::NewLine + $pcIncludeLine)
    }

    $worldPcInclude = '#include "Game/T4/GfxWorld/GfxWorldMaterialPointerWitnessT4.h"'
    if ($pcText -notmatch [regex]::Escape($worldPcInclude)) {
        if ($pcText -notmatch [regex]::Escape($pcIncludeNeedle)) {
            throw 'Pinned OAT PC ObjWriter XModel include anchor missing before GfxWorld registration.'
        }
        $pcText = $pcText.Replace($pcIncludeNeedle, $pcIncludeNeedle + [Environment]::NewLine + $worldPcInclude)
    }

    $pcCorpusInclude = '#include "Game/T4/GfxWorld/GfxWorldMaterialCorpusDumperT4.h"'
    if ($pcText -notmatch [regex]::Escape($pcCorpusInclude)) {
        if ($pcText -notmatch [regex]::Escape($worldPcInclude)) {
            throw 'Pinned T4 GfxWorld PC witness include anchor missing for source corpus.'
        }
        $pcText = $pcText.Replace($worldPcInclude,
            $worldPcInclude + [Environment]::NewLine + $pcCorpusInclude)
    }

    $pcRegisterNeedle = 'RegisterAssetDumper(std::make_unique<xmodel::DumperT4>());'
    $pcRegisterLine = 'RegisterAssetDumper(std::make_unique<xmodel::ClassificationDumperT4>());'
    if ($pcText -notmatch [regex]::Escape($pcRegisterLine)) {
        if ($pcText -notmatch [regex]::Escape($pcRegisterNeedle)) { throw 'Could not find T4 XModel dumper registration anchor.' }
        $pcText = $pcText.Replace($pcRegisterNeedle, $pcRegisterNeedle + [Environment]::NewLine + '    ' + $pcRegisterLine)
    }

    $worldPcRegister = 'RegisterAssetDumper(std::make_unique<gfxworld::MaterialPointerWitnessT4>());'
    if ($pcText -notmatch [regex]::Escape($worldPcRegister)) {
        if ($pcText -notmatch [regex]::Escape($pcRegisterLine)) {
            throw 'Pinned OAT PC ObjWriter XModel registration anchor missing before GfxWorld witness.'
        }
        $pcText = $pcText.Replace($pcRegisterLine, $pcRegisterLine + [Environment]::NewLine + '    ' + $worldPcRegister)
    }

    $pcCorpusRegister = 'RegisterAssetDumper(std::make_unique<gfxworld::MaterialCorpusDumperT4>());'
    if ($pcText -notmatch [regex]::Escape($pcCorpusRegister)) {
        if ($pcText -notmatch [regex]::Escape($worldPcRegister)) {
            throw 'Pinned T4 GfxWorld PC witness registration missing for source corpus.'
        }
        $pcText = $pcText.Replace($worldPcRegister,
            $worldPcRegister + [Environment]::NewLine + '    ' + $pcCorpusRegister)
    }

    $normalizedPcText = $pcText.TrimEnd([char]13, [char]10) + [char]10
    [System.IO.File]::WriteAllText(
        $pcWriter,
        $normalizedPcText,
        [System.Text.UTF8Encoding]::new($false))

    if ($TraceNativeAssetLoader) {
        # A diagnostic-only exception boundary in pinned OAT's native loader.
        # The stock --list path logs only the generic invalid-block error; at
        # this point OAT knows which XAsset root it was trying to deserialize.
        # We emit ONE line on exception, not 8,509 verbose per-asset messages.
        $loaderRel='src/ZoneLoading/Game/T4Xenon/ContentLoaderT4Xenon.cpp'
        $loader=Join-Path $oat 'src\ZoneLoading\Game\T4Xenon\ContentLoaderT4Xenon.cpp'
        $loaderText=[IO.File]::ReadAllText($loader).Replace("`r`n","`n")
        $incAnchor='#include <cassert>'
        if ($loaderText.IndexOf($incAnchor,[StringComparison]::Ordinal) -lt 0 -or
            $loaderText.IndexOf($incAnchor,[StringComparison]::Ordinal) -ne
            $loaderText.LastIndexOf($incAnchor,[StringComparison]::Ordinal)) {
            throw 'Pinned OAT native loader include anchor is not unique.'
        }
        $loaderText=$loaderText.Replace($incAnchor,
            $incAnchor + [Environment]::NewLine + '#include <exception>' +
                         [Environment]::NewLine + '#include <iostream>')
        $oldLoop=@'
    for (size_t index = 0; index < count; index++)
    {
        LoadXAsset(false);
        varXAsset++;
    }
'@
        $newLoop=@'
    for (size_t index = 0; index < count; index++)
    {
        try
        {
            LoadXAsset(false);
        }
        catch (const std::exception& ex)
        {
            std::cerr << "[AZTEC-OAT-FAULT] stage=asset rootIndex=" << index
                      << " typeId=" << varXAsset->type
                      << " error=" << ex.what() << std::endl;
            throw;
        }
        varXAsset++;
    }
'@
        $oldLoop=$oldLoop.Replace("`r`n","`n")
        $newLoop=$newLoop.Replace("`r`n","`n")
        if ($loaderText.IndexOf($oldLoop,[StringComparison]::Ordinal) -lt 0 -or
            $loaderText.IndexOf($oldLoop,[StringComparison]::Ordinal) -ne
            $loaderText.LastIndexOf($oldLoop,[StringComparison]::Ordinal)) {
            throw 'Pinned OAT native asset loader loop anchor is not unique.'
        }
        $loaderText=$loaderText.Replace($oldLoop,$newLoop)
        $first='    LoadScriptStringList(false);'
        $replacement=@'
    try
    {
        LoadScriptStringList(false);
    }
    catch (const std::exception& ex)
    {
        std::cerr << "[AZTEC-OAT-FAULT] stage=scriptstrings rootIndex=none error="
                  << ex.what() << std::endl;
        throw;
    }
'@
        $replacement=$replacement.Replace("`r`n","`n")
        if ($loaderText.IndexOf($first,[StringComparison]::Ordinal) -lt 0 -or
            $loaderText.IndexOf($first,[StringComparison]::Ordinal) -ne
            $loaderText.LastIndexOf($first,[StringComparison]::Ordinal)) {
            throw 'Pinned OAT native ScriptString entry anchor is not unique.'
        }
        $loaderText=$loaderText.Replace($first,$replacement.TrimEnd([char]13,[char]10))
        [IO.File]::WriteAllText($loader,
            $loaderText.TrimEnd([char]13,[char]10) + [char]10,
            [Text.UTF8Encoding]::new($false))
        Write-Host '[OAT-GRAPH] Native loader first-exception index tracing enabled (diagnostic build).'
    }

    & git diff --check
    if ($LASTEXITCODE -ne 0) { throw 'OAT graph patch failed git diff --check.' }

    Write-Host '[OAT-GRAPH] Temporary graph dumper patch applied.'
    $vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio Installer not found.' }
    $msbuild = & $vswhere -latest -products '*' -version '[18.0,19.0)' -requires Microsoft.Component.MSBuild Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\Current\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuild) { throw 'Visual Studio 2026 C++ build tools are required for the OAT graph dumper.' }
    $msbuild = [string]$msbuild

    $env:PREMAKE_NO_PROMPT = '1'
    & cmd.exe /d /c generate.bat
    if ($LASTEXITCODE -ne 0) { throw 'OAT graph-dumper solution generation failed.' }

    $buildFolder = Join-Path $oat 'build'
    $solution = @((Join-Path $buildFolder 'OpenAssetTools.slnx'), (Join-Path $buildFolder 'OpenAssetTools.sln')) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $solution) { throw 'Generated OAT solution was not found.' }

    $solutionPlatforms = @()
    if ([System.IO.Path]::GetExtension($solution) -eq '.slnx') {
        [xml]$solutionXml = Get-Content -LiteralPath $solution -Raw
        $platformNodes = @($solutionXml.SelectNodes("/*[local-name()='Solution']/*[local-name()='Configurations']/*[local-name()='Platform']"))
        $solutionPlatforms = @($platformNodes | ForEach-Object { $_.GetAttribute('Name') } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    } else {
        $solutionPlatforms = @(Get-Content -LiteralPath $solution | Where-Object { $_ -match '^\s*Release\|(.+?)\s*=\s*Release\|' } | ForEach-Object { $Matches[1].Trim() })
    }
    $solutionPlatforms = @($solutionPlatforms | Sort-Object -Unique)
    $platform = @('Win32', 'x86') | Where-Object { $solutionPlatforms -contains $_ } | Select-Object -First 1
    if (-not $platform) { throw "No 32-bit OAT solution platform found. Available: $($solutionPlatforms -join ', ')" }

    Write-Host "[OAT-GRAPH] Building Release|$platform..."
    & $msbuild $solution /m /p:Configuration=Release "/p:Platform=$platform" /v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'OAT graph-dumper build failed.' }

    $bin = Join-Path $oat 'build\bin\Release_x86'
    $unlinker = Join-Path $bin 'Unlinker.exe'
    if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) { throw "Patched Unlinker.exe was not produced: $unlinker" }

    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    Copy-Item -Path (Join-Path $bin '*') -Destination $out -Recurse -Force

    $capabilities = [ordered]@{
        format = 'WaW360.OatGraphDumperCapabilities.v1'
        oatRevision = $revision
        capabilities = @(
            't4xenon-techset-graph-v1',
            't4xenon-image-graph-v1',
            't4xenon-material-relocatable-graph-v1',
            't4-xmodel-classification-v1',
            't4xenon-xmodel-graph-inventory-v1',
            't4-pc-gfxworld-loaded-material-pointer-witness-v1',
            't4-pc-gfxworld-39-material-source-graph-corpus-v1',
            't4-pc-gfxworld-gate8-child-source-v1',
            't4-pc-gfxworld-gate8-static-draw-source-v1',
            't4xenon-gfxworld-surface-abi-loaded-pointer-witness-v1',
            't4xenon-gfxworld-root-dpvs-layout-witness-v1',
            't4xenon-gfxworld-static-draw-60b-loaded-witness-v1',
            't4xenon-xmodel-relocatable-graph-v1'
        )
    }
    if ($TraceNativeAssetLoader) {
        $capabilities.capabilities += 't4xenon-asset-fault-trace-v1'
    }
    $capabilityFile = Join-Path $out 'waw360-graph-dumper-capabilities.json'
    $capabilities | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $capabilityFile -Encoding UTF8

    Write-Host '[OAT-GRAPH] Specialized Unlinker copied to:'
    Write-Host ('  ' + (Join-Path $out 'Unlinker.exe'))
    Write-Host '[OAT-GRAPH] Capabilities:'
    Write-Host ('  ' + $capabilityFile)
}
finally {
    if (Test-Path -LiteralPath $oat) {
        Push-Location $oat
        try {
            & git checkout -- 'src/ObjWriting/Game/T4Xenon/ObjWriterT4Xenon.cpp' 2>$null
            & git checkout -- 'src/ObjWriting/Game/T4/ObjWriterT4.cpp' 2>$null
            & git checkout -- 'src/ZoneLoading/Game/T4Xenon/ContentLoaderT4Xenon.cpp' 2>$null
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/Techset/TechniqueSetGraphDumperT4Xenon.h' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/Techset/TechniqueSetGraphDumperT4Xenon.cpp' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/Image/ImageGraphDumperT4Xenon.h' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/Image/ImageGraphDumperT4Xenon.cpp' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/Material/MaterialRelocatableGraphDumperT4Xenon.h' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/Material/MaterialRelocatableGraphDumperT4Xenon.cpp' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/XModel/XModelGraphInventoryDumperT4Xenon.h' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/XModel/XModelGraphInventoryDumperT4Xenon.cpp' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/XModel/XModelRelocatableGraphDumperT4Xenon.h' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/XModel/XModelRelocatableGraphDumperT4Xenon.cpp' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4/XModel/XModelClassificationDumperT4.h' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4/XModel/XModelClassificationDumperT4.cpp' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4/GfxWorld/GfxWorldMaterialPointerWitnessT4.h' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4/GfxWorld/GfxWorldMaterialPointerWitnessT4.cpp' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4/GfxWorld/GfxWorldMaterialCorpusDumperT4.h' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4/GfxWorld/GfxWorldMaterialCorpusDumperT4.cpp' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/GfxWorld/GfxWorldSurfaceAbiWitnessT4Xenon.h' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/GfxWorld/GfxWorldSurfaceAbiWitnessT4Xenon.cpp' -Force -ErrorAction SilentlyContinue
        } finally { Pop-Location }
    }
    Pop-Location
}

Push-Location $oat
try {
    $after = @(& git status --porcelain --untracked-files=all)
    if ($after.Count -ne 0) { throw ('OAT source was not restored cleanly after graph-dumper build:' + [Environment]::NewLine + ($after -join [Environment]::NewLine)) }
} finally { Pop-Location }

Write-Host '[OAT-GRAPH] PASS: specialized graph-dumper Unlinker built; pinned source restored clean.'
