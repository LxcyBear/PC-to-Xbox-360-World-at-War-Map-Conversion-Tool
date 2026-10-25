[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$ArtifactZip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$zip = (Resolve-Path -LiteralPath $ArtifactZip).Path
$target = Join-Path $repoRoot '.deps\OpenAssetTools-xenon-prebuilt\Release_x86'
$temp = Join-Path $env:TEMP ('waw360-oat-' + [Guid]::NewGuid().ToString('N'))

try {
    New-Item -ItemType Directory -Force -Path $temp | Out-Null
    Expand-Archive -LiteralPath $zip -DestinationPath $temp -Force

    $linker = Get-ChildItem -LiteralPath $temp -Recurse -Filter 'Linker.exe' -File | Select-Object -First 1
    if (-not $linker) { throw 'Artifact does not contain Linker.exe.' }

    $sourceDir = $linker.Directory.FullName
    foreach ($tool in @('Linker.exe','Unlinker.exe','ImageConverter.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $sourceDir $tool))) {
            throw "Artifact is missing required tool: $tool"
        }
    }

    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceDir '*') -Destination $target -Recurse -Force

    Write-Host '[OAT] PREBUILT INSTALL PASS'
    Write-Host "      $target"
    foreach ($tool in @('Linker.exe','Unlinker.exe','ImageConverter.exe')) {
        $toolPath = Join-Path $target $tool
        Write-Host "      $tool SHA256=$((Get-FileHash -LiteralPath $toolPath -Algorithm SHA256).Hash)"
    }
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
