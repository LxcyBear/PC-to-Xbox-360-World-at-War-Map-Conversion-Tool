param(
    [string]$CodxeRoot = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($CodxeRoot)) {
    $CodxeRoot = Join-Path $repoRoot ".deps\codxe-waw360-trace"
}

$sln = Join-Path $CodxeRoot "codxe.sln"
if (-not (Test-Path $sln)) {
    throw "Codxe solution not found: $sln. Run Prepare-T4SPFastfileTrace.ps1 first."
}

$candidates = @(
    "C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2017\Professional\MSBuild\15.0\Bin\MSBuild.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2017\Enterprise\MSBuild\15.0\Bin\MSBuild.exe",
    "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
    "C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
)

$msbuild = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $msbuild) {
    throw "No MSBuild found. Codxe upstream documents Visual Studio 2010 Ultimate + Xbox 360 SDK."
}

Write-Host "[WAW360TRACE] MSBuild: $msbuild"
Write-Host "[WAW360TRACE] Solution: $sln"

& $msbuild "$sln" /m /t:Build /p:Configuration=Release /p:Platform="Xbox 360"
if ($LASTEXITCODE -ne 0) {
    throw "Codxe trace build failed: $LASTEXITCODE"
}

$xex = Join-Path $CodxeRoot "build\Release\bin\codxe.xex"
if (-not (Test-Path $xex)) {
    throw "Build reported success but codxe.xex was not found at: $xex"
}

Write-Host ""
Write-Host "[WAW360TRACE] BUILD PASS"
Write-Host "[WAW360TRACE] Trace XEX: $xex"
Write-Host "[WAW360TRACE] Replace ONLY your current codxe.xex with this diagnostic build, keeping a backup."
Write-Host "[WAW360TRACE] Remove all WV execution breakpoints before testing."
