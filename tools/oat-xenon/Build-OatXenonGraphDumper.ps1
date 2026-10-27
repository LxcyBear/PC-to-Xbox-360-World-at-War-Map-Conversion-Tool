[CmdletBinding()]
param()

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
    $pcXModelSrcDir = Join-Path $oat 'src\ObjWriting\Game\T4\XModel'
    $xenonXModelSrcDir = Join-Path $oat 'src\ObjWriting\Game\T4Xenon\XModel'
    New-Item -ItemType Directory -Force -Path $srcDir | Out-Null
    New-Item -ItemType Directory -Force -Path $imageSrcDir | Out-Null
    New-Item -ItemType Directory -Force -Path $pcXModelSrcDir | Out-Null
    New-Item -ItemType Directory -Force -Path $xenonXModelSrcDir | Out-Null

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

    $pcClassHeaderSrc = Join-Path $graphSrc 'XModelClassificationDumperT4.h'
    $pcClassCppSrc = Join-Path $graphSrc 'XModelClassificationDumperT4.cpp'
    if (-not (Test-Path -LiteralPath $pcClassHeaderSrc -PathType Leaf) -or -not (Test-Path -LiteralPath $pcClassCppSrc -PathType Leaf)) { throw 'PC XModel classification dumper source is missing from converter repo.' }
    $pcClassHeaderDst = Join-Path $pcXModelSrcDir 'XModelClassificationDumperT4.h'
    $pcClassCppDst = Join-Path $pcXModelSrcDir 'XModelClassificationDumperT4.cpp'
    Copy-Item -LiteralPath $pcClassHeaderSrc -Destination $pcClassHeaderDst -Force
    Copy-Item -LiteralPath $pcClassCppSrc -Destination $pcClassCppDst -Force

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

    $pcRegisterNeedle = 'RegisterAssetDumper(std::make_unique<xmodel::DumperT4>());'
    $pcRegisterLine = 'RegisterAssetDumper(std::make_unique<xmodel::ClassificationDumperT4>());'
    if ($pcText -notmatch [regex]::Escape($pcRegisterLine)) {
        if ($pcText -notmatch [regex]::Escape($pcRegisterNeedle)) { throw 'Could not find T4 XModel dumper registration anchor.' }
        $pcText = $pcText.Replace($pcRegisterNeedle, $pcRegisterNeedle + [Environment]::NewLine + '    ' + $pcRegisterLine)
    }

    $normalizedPcText = $pcText.TrimEnd([char]13, [char]10) + [char]10
    [System.IO.File]::WriteAllText(
        $pcWriter,
        $normalizedPcText,
        [System.Text.UTF8Encoding]::new($false))

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
            't4-xmodel-classification-v1',
            't4xenon-xmodel-graph-inventory-v1',
            't4xenon-xmodel-relocatable-graph-v1'
        )
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
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/Techset/TechniqueSetGraphDumperT4Xenon.h' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/Techset/TechniqueSetGraphDumperT4Xenon.cpp' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/Image/ImageGraphDumperT4Xenon.h' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/Image/ImageGraphDumperT4Xenon.cpp' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/XModel/XModelGraphInventoryDumperT4Xenon.h' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/XModel/XModelGraphInventoryDumperT4Xenon.cpp' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/XModel/XModelRelocatableGraphDumperT4Xenon.h' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/XModel/XModelRelocatableGraphDumperT4Xenon.cpp' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4/XModel/XModelClassificationDumperT4.h' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4/XModel/XModelClassificationDumperT4.cpp' -Force -ErrorAction SilentlyContinue
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
