# Build solution and publish the Windows GUI to src\WaW360.Gui\Release.
[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$WithOat,
    [switch]$BuildOatLocally,
    [switch]$RunOatTests,
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$solution = Join-Path $repoRoot 'WaW360Converter.sln'
$guiProject = Join-Path $repoRoot 'src\WaW360.Gui\WaW360.Gui.csproj'
$defaultReleaseDirectory = Join-Path $repoRoot 'src\WaW360.Gui\Release'
$releaseDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $defaultReleaseDirectory
} else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$externalReleaseDirectory = -not $releaseDirectory.Equals($defaultReleaseDirectory, [StringComparison]::OrdinalIgnoreCase)

if ($RunOatTests -and -not $WithOat) { throw '-RunOatTests requires -WithOat.' }
if ($BuildOatLocally -and -not $WithOat) { throw '-BuildOatLocally requires -WithOat.' }
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
        if ($externalReleaseDirectory) {
            Write-Host '[WaW360] External Release output will be published in-place (no deletion of existing files).'
        } elseif (Test-Path -LiteralPath $releaseDirectory) {
            Remove-Item -LiteralPath $releaseDirectory -Force -Recurse
        }
    }

    if ($RunOatTests) { throw '-RunOatTests is not yet available; run the OAT tests separately once the toolchain is built.' }
    if ($WithOat) {
        $prebuiltDir = Join-Path $repoRoot '.deps\OpenAssetTools-xenon-prebuilt\Release_x86'
        $requiredOatTools = @('Linker.exe','Unlinker.exe','ImageConverter.exe')
        $missingPrebuilt = @($requiredOatTools | Where-Object {
            -not (Test-Path -LiteralPath (Join-Path $prebuiltDir $_) -PathType Leaf)
        })

        if ($missingPrebuilt.Count -eq 0) {
            Write-Host '[WaW360] Reusing installed pinned OAT Xenon x86 toolchain.'
            Write-Host "          $prebuiltDir"
        }
        elseif ($BuildOatLocally) {
            Write-Host '[WaW360] No prebuilt OAT toolchain found; local C++ build explicitly requested.'
            $prepare = Join-Path $repoRoot 'tools\oat-xenon\Prepare-OatXenon.ps1'
            & $prepare -Build
        }
        else {
            throw "Pinned OAT x86 binaries are not installed. Download the successful GitHub Actions artifact once and run tools\oat-xenon\Install-OatArtifact.ps1. Future -WithOat builds will reuse it. Use -BuildOatLocally only if you intentionally want the Visual Studio C++ build."
        }
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
