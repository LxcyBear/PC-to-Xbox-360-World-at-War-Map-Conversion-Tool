param(
    [string]$SourceDirectory = "C:\Users\jorda\OneDrive\Desktop\working frumansion",
    [string]$SourceFastfile = "",
    [string]$SourceZoneName = "frumansion_j_p",
    [string]$DestinationRoot = "",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    $DestinationRoot = Join-Path $repoRoot "output\_native_controls"
}

if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
    throw "Native control source directory not found: $SourceDirectory"
}

if ([string]::IsNullOrWhiteSpace($SourceZoneName)) {
    throw "SourceZoneName cannot be empty."
}

if (-not [string]::IsNullOrWhiteSpace($SourceFastfile)) {
    if ([System.IO.Path]::IsPathRooted($SourceFastfile)) {
        $mainSource = $SourceFastfile
    }
    else {
        $mainSource = Join-Path $SourceDirectory $SourceFastfile
    }

    if (-not (Test-Path -LiteralPath $mainSource -PathType Leaf)) {
        throw "Explicit native control fastfile not found: $mainSource"
    }
}
else {
    $preferred = Join-Path $SourceDirectory ($SourceZoneName + ".ff")

    if (Test-Path -LiteralPath $preferred -PathType Leaf) {
        $mainSource = $preferred
    }
    else {
        $mainCandidates = @(
            Get-ChildItem -LiteralPath $SourceDirectory -Filter "*.ff" -File |
                Where-Object {
                    $_.Name -notmatch '(?i)_load[.]ff$' -and
                    $_.Name -notmatch '(?i)_patch[.]ff$'
                }
        )

        if ($mainCandidates.Count -eq 0) {
            throw "No main .ff files found in native control source directory: $SourceDirectory"
        }

        if ($mainCandidates.Count -eq 1) {
            $mainSource = $mainCandidates[0].FullName
        }
        else {
            $fruCandidates = @(
                $mainCandidates |
                    Where-Object { $_.Name -match '(?i)fru|mansion' }
            )

            if ($fruCandidates.Count -eq 1) {
                $mainSource = $fruCandidates[0].FullName
            }
            else {
                Write-Host "[WAW360CONTROL] Multiple possible main FFs found:" -ForegroundColor Yellow
                $mainCandidates | ForEach-Object { Write-Host "  $($_.Name)" -ForegroundColor Yellow }
                throw "Could not safely choose the control FF. Re-run with -SourceFastfile <filename>."
            }
        }
    }
}

$mainItem = Get-Item -LiteralPath $mainSource
$sourceStem = [System.IO.Path]::GetFileNameWithoutExtension($mainItem.Name)

$destination = Join-Path $DestinationRoot $SourceZoneName

if ($Clean -and (Test-Path -LiteralPath $destination)) {
    Remove-Item -LiteralPath $destination -Recurse -Force
}

New-Item -ItemType Directory -Path $destination -Force | Out-Null

Write-Host "[WAW360CONTROL] Source file     : $($mainItem.FullName)"
Write-Host "[WAW360CONTROL] Source basename : $sourceStem"
Write-Host "[WAW360CONTROL] Xbox identity   : $SourceZoneName"
Write-Host "[WAW360CONTROL] Stage directory : $destination"
Write-Host ""

$copyPlan = @(
    @{
        Source   = $mainItem.FullName
        Target   = "$SourceZoneName.ff"
        Required = $true
    },
    @{
        Source   = (Join-Path $SourceDirectory ($sourceStem + "_load.ff"))
        Target   = ($SourceZoneName + "_load.ff")
        Required = $false
    },
    @{
        Source   = (Join-Path $SourceDirectory ($sourceStem + "_patch.ff"))
        Target   = ($SourceZoneName + "_patch.ff")
        Required = $false
    },
    @{
        Source   = (Join-Path $SourceDirectory "preview.dds")
        Target   = "preview.dds"
        Required = $false
    }
)

foreach ($entry in $copyPlan) {
    $source = $entry.Source
    $targetName = $entry.Target

    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        if ($entry.Required) {
            throw "Required control fastfile disappeared while staging: $source"
        }

        Write-Host "[WAW360CONTROL] optional missing: $([System.IO.Path]::GetFileName($source))"
        continue
    }

    $target = Join-Path $destination $targetName
    Copy-Item -LiteralPath $source -Destination $target -Force

    $item = Get-Item -LiteralPath $target
    $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash

    Write-Host (
        "[WAW360CONTROL] copied {0} -> {1} ({2:N0} bytes) SHA256={3}" -f
        ([System.IO.Path]::GetFileName($source)),
        $targetName,
        $item.Length,
        $hash
    )
}

$metadata = @{
    version = 1
    name = "FRUmansion - native Xbox control"
    description = "Known-working native Xbox FRUmansion control. Internal map/BSP identity: $SourceZoneName"
} | ConvertTo-Json -Depth 3

$metadataPath = Join-Path $destination "map.json"
Set-Content -LiteralPath $metadataPath -Value $metadata -Encoding ASCII

$readme = @"
NATIVE FRUMANSION CONTROL
=========================

This control is staged under its real Xbox zone/BSP identity:

    $SourceZoneName

Original source fastfile:

    $($mainItem.Name)

Staged main fastfile:

    $SourceZoneName.ff

Install the entire staged folder as:

    D:\_codxe\t4\usermaps\$SourceZoneName\

Codxe must launch:

    $SourceZoneName

The native clipmap reports:

    maps/$SourceZoneName.d3dbsp

Do not rename this control to nazi_zombie_frumansion.
"@

Set-Content -LiteralPath (Join-Path $destination "CONTROL_README.txt") -Value $readme -Encoding ASCII

Write-Host ""
Write-Host "[WAW360CONTROL] STAGE PASS" -ForegroundColor Green
Write-Host "[WAW360CONTROL] Copy this folder to the Xbox:"
Write-Host "  $destination"
Write-Host ""
Write-Host "[WAW360CONTROL] Xbox destination:"
Write-Host "  D:\_codxe\t4\usermaps\$SourceZoneName"
Write-Host ""
Write-Host "[WAW360CONTROL] Codxe/devmap identity:"
Write-Host "  $SourceZoneName"
