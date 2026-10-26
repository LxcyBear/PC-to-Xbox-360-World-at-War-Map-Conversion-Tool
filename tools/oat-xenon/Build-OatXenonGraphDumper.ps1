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
    New-Item -ItemType Directory -Force -Path $srcDir | Out-Null

    $graphSrc = Join-Path $PSScriptRoot 'graph-dumper'
    $headerSrc = Join-Path $graphSrc 'TechniqueSetGraphDumperT4Xenon.h'
    $cppSrc = Join-Path $graphSrc 'TechniqueSetGraphDumperT4Xenon.cpp'
    if (-not (Test-Path -LiteralPath $headerSrc -PathType Leaf) -or -not (Test-Path -LiteralPath $cppSrc -PathType Leaf)) { throw 'Graph dumper source is missing from converter repo.' }

    $headerDst = Join-Path $srcDir 'TechniqueSetGraphDumperT4Xenon.h'
    $cppDst = Join-Path $srcDir 'TechniqueSetGraphDumperT4Xenon.cpp'
    Copy-Item -LiteralPath $headerSrc -Destination $headerDst -Force
    Copy-Item -LiteralPath $cppSrc -Destination $cppDst -Force

    $writer = Join-Path $oat 'src\ObjWriting\Game\T4Xenon\ObjWriterT4Xenon.cpp'
    $text = Get-Content -LiteralPath $writer -Raw
    $includeNeedle = '#include "Game/T4Xenon/Techset/PixelShaderDumperT4Xenon.h"'
    $includeLine = '#include "Game/T4Xenon/Techset/TechniqueSetGraphDumperT4Xenon.h"'
    if ($text -notmatch [regex]::Escape($includeLine)) {
        if ($text -notmatch [regex]::Escape($includeNeedle)) { throw 'Could not find T4Xenon PixelShader dumper include anchor.' }
        $text = $text.Replace($includeNeedle, $includeNeedle + [Environment]::NewLine + $includeLine)
    }

    $registerNeedle = 'RegisterAssetDumper(std::make_unique<techset::PixelShaderDumperT4Xenon>());'
    $registerLine = 'RegisterAssetDumper(std::make_unique<techset::TechniqueSetGraphDumperT4Xenon>());'
    if ($text -notmatch [regex]::Escape($registerLine)) {
        if ($text -notmatch [regex]::Escape($registerNeedle)) { throw 'Could not find T4Xenon PixelShader dumper registration anchor.' }
        $text = $text.Replace($registerNeedle, $registerNeedle + [Environment]::NewLine + '    ' + $registerLine)
    }
    Set-Content -LiteralPath $writer -Value $text -Encoding UTF8

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
    Write-Host '[OAT-GRAPH] Specialized Unlinker copied to:'
    Write-Host ('  ' + (Join-Path $out 'Unlinker.exe'))
}
finally {
    if (Test-Path -LiteralPath $oat) {
        Push-Location $oat
        try {
            & git checkout -- 'src/ObjWriting/Game/T4Xenon/ObjWriterT4Xenon.cpp' 2>$null
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/Techset/TechniqueSetGraphDumperT4Xenon.h' -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath 'src/ObjWriting/Game/T4Xenon/Techset/TechniqueSetGraphDumperT4Xenon.cpp' -Force -ErrorAction SilentlyContinue
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
