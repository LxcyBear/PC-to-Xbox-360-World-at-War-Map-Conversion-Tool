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
        if (-not (Test-Path $vswhere)) { throw 'Visual Studio 2026 C++ tools are required.' }
        $msbuild = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\MSBuild.exe' | Select-Object -First 1
        if (-not $msbuild) { throw 'MSBuild not found.' }
        $env:PREMAKE_NO_PROMPT = '1'
        & cmd.exe /d /c generate.bat
        if ($LASTEXITCODE -ne 0) { throw 'OAT solution generation failed.' }
        & $msbuild 'build\OpenAssetTools.sln' /m /p:Configuration=Release /p:Platform=x86 /v:minimal
        if ($LASTEXITCODE -ne 0) { throw 'OAT x86 build failed.' }
        foreach ($tool in @('Linker.exe','Unlinker.exe')) {
            if (-not (Test-Path (Join-Path 'build\bin\Release_x86' $tool))) { throw "Missing $tool" }
        }
    }
    Write-Host "[OAT] PASS: pinned Xenon contract at $revision"
}
finally { Pop-Location }
