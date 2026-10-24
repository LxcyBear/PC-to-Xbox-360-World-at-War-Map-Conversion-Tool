# Build solution and publish the Windows GUI to src\WaW360.Gui\Release.
[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$WithOat,
    [switch]$RunOatTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$solution = Join-Path $repoRoot 'WaW360Converter.sln'
$guiProject = Join-Path $repoRoot 'src\WaW360.Gui\WaW360.Gui.csproj'
$releaseDirectory = Join-Path $repoRoot 'src\WaW360.Gui\Release'

if ($RunOatTests -and -not $WithOat) { throw '-RunOatTests requires -WithOat.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 8 SDK is required. Install the .NET 8 SDK first.'
}
if (-not (Test-Path -LiteralPath $solution) -or -not (Test-Path -LiteralPath $guiProject)) {
    throw "Missing solution or GUI project under $repoRoot."
}

Push-Location $repoRoot
try {
    if ($Clean) {
        Write-Host '[WaW360] Cleaning Release artifacts...'
        & dotnet clean $solution --configuration Release --nologo
        if ($LASTEXITCODE -ne 0) { throw 'dotnet clean failed.' }
        if (Test-Path -LiteralPath $releaseDirectory) {
            Remove-Item -LiteralPath $releaseDirectory -Force -Recurse
        }
    }

    if ($RunOatTests) { throw '-RunOatTests is not yet available; run the OAT tests separately once the toolchain is built.' }
    if ($WithOat) {
        Write-Host '[WaW360] Preparing pinned OpenAssetTools Xenon x86 toolchain...'
        $prepare = Join-Path $repoRoot 'tools\oat-xenon\Prepare-OatXenon.ps1'
        & $prepare -Build
    }

    Write-Host '[WaW360] Building complete C# solution (Release)...'
    & dotnet build $solution --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }

    Write-Host "[WaW360] Publishing Windows GUI to $releaseDirectory"
    $publishArgs = @('publish', $guiProject, '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'false', '--output', $releaseDirectory, '--nologo', '-p:UseAppHost=true')
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw 'GUI publish failed.' }

    $exe = Join-Path $releaseDirectory 'WaW360.Gui.exe'
    if (-not (Test-Path -LiteralPath $exe)) {
        throw "GUI publish finished but $exe is missing."
    }

    $revision = 'unknown'
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $head = & git rev-parse --short HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $head) { $revision = ($head | Select-Object -First 1).Trim() }
    }

    @(
        'WaW360 GUI Release'
        "Built: $(Get-Date -Format o)"
        "Source revision: $revision"
        "Target framework: net8.0-windows"
        'Runtime: win-x64 (requires .NET 8 Desktop Runtime)'
        "EXE SHA256: $((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash)"
        'NOTE: This build is NOT proof that a converted map is native-playable.'
    ) | Set-Content -LiteralPath (Join-Path $releaseDirectory 'BUILD_INFO.txt') -Encoding UTF8

    Write-Host '[WaW360] BUILD PASS: GUI Release files exist.'
    Write-Host "          $exe"
    Write-Host '          Native-playable maps require separate hardware validation.'
}
finally { Pop-Location }
