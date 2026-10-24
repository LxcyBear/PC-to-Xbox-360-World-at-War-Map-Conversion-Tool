[CmdletBinding()]
param([switch]$Build)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$revision = '1f7acb3347bc2e44deb609bc8cf7259bf027f176'
$remote = 'https://github.com/michaeloliverx/OpenAssetTools.git'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$checkout = Join-Path $root '.deps\OpenAssetTools-xenon'
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw 'Git is required.' }
if (-not (Test-Path (Join-Path $checkout '.git'))) {
    if (Test-Path $checkout) { throw 'Dependency folder already exists and is not a Git checkout.' }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $checkout) | Out-Null
    # Normal checkout avoids Git reporting a deliberately missing worktree as edits.
    & git clone --branch xenon $remote $checkout
    if ($LASTEXITCODE -ne 0) { throw 'OAT clone failed.' }
}
Push-Location $checkout
try {
    $origin = (& git remote get-url origin).Trim()
    if ($origin -ne $remote) { throw "Unexpected origin: $origin" }
    # Recover the empty --no-checkout clone created by the initial version of
    # this script. An empty working directory and empty index, with an otherwise
    # valid Git HEAD, are distinguishable from local source modifications.
    # Never force-checkout a populated working tree.
    $rootEntries = @(Get-ChildItem -LiteralPath $checkout -Force |
        Where-Object { $_.Name -ne '.git' })
    $indexEntries = @(& git ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect OAT Git index.' }
    $headFiles = @(& git ls-tree -r --name-only HEAD)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect OAT Git HEAD.' }
    $emptyUnmaterializedClone = $rootEntries.Count -eq 0 -and
        $indexEntries.Count -eq 0 -and $headFiles.Count -gt 0

    $changes = @(& git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect OAT working tree.' }
    if ($changes.Count -gt 0 -and -not $emptyUnmaterializedClone) {
        throw 'OAT has real local changes; refusing checkout. Inspect .deps\OpenAssetTools-xenon before retrying.'
    }

    & git fetch origin $revision
    if ($LASTEXITCODE -ne 0) { throw 'OAT fetch failed.' }
    if ($emptyUnmaterializedClone) {
        Write-Host '[OAT] Recovering the empty checkout left by the earlier bootstrap script.'
        # Safe only because no worktree files or indexed changes exist.
        & git checkout --force --detach $revision
    } else {
        & git checkout --detach $revision
    }
    if ($LASTEXITCODE -ne 0) { throw 'OAT checkout failed.' }
    if ((& git rev-parse HEAD).Trim() -ne $revision) { throw 'OAT revision does not match pin.' }
    & git submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) { throw 'OAT submodule update failed.' }
    & (Join-Path $PSScriptRoot 'Test-XenonContract.ps1') -OatDirectory $checkout
    if (-not $?) { throw 'Xenon contract failed.' }
    if ($Build) {
        $vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (-not (Test-Path -LiteralPath $vswhere)) {
            throw 'Visual Studio Installer not found. Install Visual Studio 2026 with Desktop development with C++.'
        }

        # MSBuild alone is not sufficient for native C++ projects. In particular,
        # MSBuild supplied by a .NET-only workload lacks Microsoft.Cpp.Default.props.
        # Choose an installation with both MSBuild and x86/x64 MSVC tools.
        $msbuild = & $vswhere -latest -products '*' -version '[18.0,19.0)' -requires Microsoft.Component.MSBuild Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\Current\Bin\MSBuild.exe' | Select-Object -First 1
        if (-not $msbuild) {
            throw 'Visual Studio 2026 x86/x64 C++ build tools are missing. Open Visual Studio Installer > Modify (VS 2026) > Desktop development with C++. Ensure MSVC Build Tools for x64/x86 and a Windows SDK are selected, click Modify, then retry. The .NET/MSBuild workload alone cannot compile OpenAssetTools.'
        }
        $msbuild = [string]$msbuild
        # Validate the precise import target that Premake VS2026 projects use,
        # so a missing workload produces one actionable error, not hundreds.
        $cppDefaultProps = [System.IO.Path]::GetFullPath(
            (Join-Path (Split-Path -Parent $msbuild) '..\..\Microsoft\VC\v180\Microsoft.Cpp.Default.props'))
        if (-not (Test-Path -LiteralPath $cppDefaultProps -PathType Leaf)) {
            throw "Required C++ targets missing: $cppDefaultProps. In Visual Studio Installer, modify VS 2026 and install or repair 'Desktop development with C++' plus MSVC x86/x64 build tools and Windows SDK. Do not copy targets from another VS version."
        }
        Write-Host "[OAT] Found VS2026 C++ targets: $cppDefaultProps"
        $env:PREMAKE_NO_PROMPT = '1'
        & cmd.exe /d /c generate.bat
        if ($LASTEXITCODE -ne 0) { throw 'OAT solution generation failed.' }
        # Premake 5 Beta 8 emits .slnx for VS2026. Older Premake/VS generators
        # may still emit .sln; check both rather than assuming either extension.
        $buildFolder = Join-Path $checkout 'build'
        $solutionCandidates = @(
            (Join-Path $buildFolder 'OpenAssetTools.slnx'),
            (Join-Path $buildFolder 'OpenAssetTools.sln')
        )
        $solution = $solutionCandidates |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
            Select-Object -First 1
        if (-not $solution) {
            $contents = if (Test-Path -LiteralPath $buildFolder) {
                (Get-ChildItem -LiteralPath $buildFolder -File |
                    Select-Object -ExpandProperty Name) -join ', '
            } else { '<build directory absent>' }
            throw "OAT Premake generation did not create OpenAssetTools.slnx or OpenAssetTools.sln. Build folder files: $contents. Review generate.bat output for a Premake error."
        }
        # VS2026 solution platforms are not necessarily named "x86".
        # The Premake x86 *project architecture* is commonly exposed as
        # "Win32" at the solution level. Read what Premake actually generated.
        $solutionPlatforms = @()
        if ([System.IO.Path]::GetExtension($solution) -eq '.slnx') {
            [xml]$solutionXml = Get-Content -LiteralPath $solution -Raw
            $platformNodes = @($solutionXml.SelectNodes(
                "/*[local-name()='Solution']/*[local-name()='Configurations']/*[local-name()='Platform']"
            ))
            $solutionPlatforms = @($platformNodes |
                ForEach-Object { $_.GetAttribute('Name') } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        } else {
            $solutionPlatforms = @(Get-Content -LiteralPath $solution |
                Where-Object { $_ -match '^\s*Release\|(.+?)\s*=\s*Release\|' } |
                ForEach-Object { $Matches[1].Trim() })
        }
        $solutionPlatforms = @($solutionPlatforms | Sort-Object -Unique)
        $buildPlatform = @('Win32', 'x86') |
            Where-Object { $solutionPlatforms -contains $_ } |
            Select-Object -First 1
        if (-not $buildPlatform) {
            $actual = if ($solutionPlatforms.Count -gt 0) {
                $solutionPlatforms -join ', '
            } else { '<none discovered>' }
            throw "No verified 32-bit solution platform. Found: $actual. Inspect generated OpenAssetTools.slnx instead of silently building x64."
        }
        Write-Host "[OAT] Building generated solution: $solution"
        Write-Host "[OAT] Release solution platform: $buildPlatform (32-bit)"
        & $msbuild $solution /m /p:Configuration=Release "/p:Platform=$buildPlatform" /v:minimal
        if ($LASTEXITCODE -ne 0) {
            throw "OAT Release|$buildPlatform build failed: $solution"
        }
        foreach ($tool in @('Linker.exe','Unlinker.exe')) {
            if (-not (Test-Path (Join-Path 'build\bin\Release_x86' $tool))) { throw "Missing $tool" }
        }
    }
    Write-Host "[OAT] PASS: pinned Xenon contract at $revision"
}
finally { Pop-Location }
