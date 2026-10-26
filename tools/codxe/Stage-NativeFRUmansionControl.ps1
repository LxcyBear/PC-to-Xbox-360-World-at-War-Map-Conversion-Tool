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
                    $_.Name -notmatch '(?i)_load.ff

if ($Clean -and (Test-Path -LiteralPath $destination)) {
    Remove-Item -LiteralPath $destination -Recurse -Force
}

New-Item -ItemType Directory -Path $destination -Force | Out-Null

Write-Host "[WAW360CONTROL] Source file     : $($mainItem.FullName)"
Write-Host "[WAW360CONTROL] Source basename : $sourceStem"
Write-Host "[WAW360CONTROL] Xbox identity   : $SourceZoneName"
Write-Host "[WAW360CONTROL] Source directory: $SourceDirectory"
Write-Host "[WAW360CONTROL] Stage directory : $destination"
Write-Host ""
Write-Host "[WAW360CONTROL] IMPORTANT: the folder name, FF basename, devmap name and internal BSP identity must agree."
Write-Host "[WAW360CONTROL] For this native FRUmansion control, that identity is '$SourceZoneName'."
Write-Host ""

$copyPlan = @(
    @{ Source = $mainItem.FullName; Target = "$SourceZoneName.ff"; Required = $true },
    @{ Source = (Join-Path $SourceDirectory ("${sourceStem}_load.ff")); Target = "${SourceZoneName}_load.ff"; Required = $false },
    @{ Source = (Join-Path $SourceDirectory ("${sourceStem}_patch.ff")); Target = "${SourceZoneName}_patch.ff"; Required = $false },
    @{ Source = (Join-Path $SourceDirectory "preview.dds"); Target = "preview.dds"; Required = $false }
)

foreach ($entry in $copyPlan) {
    $source = $entry.Source
    $name = $entry.Target

    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        if ($entry.Required) {
            throw "Required control fastfile disappeared while staging: $source"
        }

        Write-Host "[WAW360CONTROL] optional missing: $([System.IO.Path]::GetFileName($source))"
        continue
    }

    $target = Join-Path $destination $name
    Copy-Item -LiteralPath $source -Destination $target -Force

    $item = Get-Item -LiteralPath $target
    $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash

    Write-Host ("[WAW360CONTROL] copied {0} -> {1} ({2:N0} bytes) SHA256={3}" -f ([System.IO.Path]::GetFileName($source)), $name, $item.Length, $hash)
}

$metadata = @{
    version = 1
    name = "FRUmansion - native Xbox control"
    description = "Known-working native Xbox FRUmansion control. Internal map/BSP identity: $SourceZoneName"
} | ConvertTo-Json -Depth 3

$metadataPath = Join-Path $destination "map.json"
Set-Content -LiteralPath $metadataPath -Value $metadata -Encoding ASCII
Write-Host "[WAW360CONTROL] wrote map.json"

$readme = @"
NATIVE FRUMANSION CONTROL
=========================

This control must be installed and launched under its real Xbox zone identity:

    $SourceZoneName

Install the entire folder as:

    D:\_codxe\t4\usermaps\$SourceZoneName\

The required main fastfile path on the Xbox is:

    D:\_codxe\t4\usermaps\$SourceZoneName\$SourceZoneName.ff

Launch/select "$SourceZoneName" in Codxe.

Do NOT rename this control to "nazi_zombie_frumansion".
Its native clipmap reports:

    maps/$SourceZoneName.d3dbsp

Renaming only the folder/FF makes WaW request a different BSP and produces:
"Couldn't find the bsp for this map."

This folder is only an A/B control. It does not alter the generated
nazi_zombie_frumansion candidate.
"@

$readmePath = Join-Path $destination "CONTROL_README.txt"
Set-Content -LiteralPath $readmePath -Value $readme -Encoding ASCII

Write-Host ""
Write-Host "[WAW360CONTROL] STAGE PASS"
Write-Host "[WAW360CONTROL] Copy this entire folder to the Xbox usermaps directory:"
Write-Host "  $destination"
Write-Host ""
Write-Host "[WAW360CONTROL] Xbox destination:"
Write-Host "  D:\_codxe\t4\usermaps\$SourceZoneName"
Write-Host ""
Write-Host "[WAW360CONTROL] Codxe/devmap identity:"
Write-Host "  $SourceZoneName"
 -and
                    $_.Name -notmatch '(?i)_patch.ff

if ($Clean -and (Test-Path -LiteralPath $destination)) {
    Remove-Item -LiteralPath $destination -Recurse -Force
}

New-Item -ItemType Directory -Path $destination -Force | Out-Null

Write-Host "[WAW360CONTROL] Source identity : $SourceZoneName"
Write-Host "[WAW360CONTROL] Source directory: $SourceDirectory"
Write-Host "[WAW360CONTROL] Stage directory : $destination"
Write-Host ""
Write-Host "[WAW360CONTROL] IMPORTANT: the folder name, FF basename, devmap name and internal BSP identity must agree."
Write-Host "[WAW360CONTROL] For this native FRUmansion control, that identity is '$SourceZoneName'."
Write-Host ""

$candidates = @(
    "$SourceZoneName.ff",
    "${SourceZoneName}_load.ff",
    "${SourceZoneName}_patch.ff",
    "preview.dds"
)

foreach ($name in $candidates) {
    $source = Join-Path $SourceDirectory $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        if ($name -eq "$SourceZoneName.ff") {
            throw "Required control fastfile disappeared while staging: $source"
        }

        Write-Host "[WAW360CONTROL] optional missing: $name"
        continue
    }

    $target = Join-Path $destination $name
    Copy-Item -LiteralPath $source -Destination $target -Force

    $item = Get-Item -LiteralPath $target
    $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash

    Write-Host ("[WAW360CONTROL] copied {0} ({1:N0} bytes) SHA256={2}" -f $name, $item.Length, $hash)
}

$metadata = @{
    version = 1
    name = "FRUmansion - native Xbox control"
    description = "Known-working native Xbox FRUmansion control. Internal map/BSP identity: $SourceZoneName"
} | ConvertTo-Json -Depth 3

$metadataPath = Join-Path $destination "map.json"
Set-Content -LiteralPath $metadataPath -Value $metadata -Encoding ASCII
Write-Host "[WAW360CONTROL] wrote map.json"

$readme = @"
NATIVE FRUMANSION CONTROL
=========================

This control must be installed and launched under its real Xbox zone identity:

    $SourceZoneName

Install the entire folder as:

    D:\_codxe\t4\usermaps\$SourceZoneName\

The required main fastfile path on the Xbox is:

    D:\_codxe\t4\usermaps\$SourceZoneName\$SourceZoneName.ff

Launch/select "$SourceZoneName" in Codxe.

Do NOT rename this control to "nazi_zombie_frumansion".
Its native clipmap reports:

    maps/$SourceZoneName.d3dbsp

Renaming only the folder/FF makes WaW request a different BSP and produces:
"Couldn't find the bsp for this map."

This folder is only an A/B control. It does not alter the generated
nazi_zombie_frumansion candidate.
"@

$readmePath = Join-Path $destination "CONTROL_README.txt"
Set-Content -LiteralPath $readmePath -Value $readme -Encoding ASCII

Write-Host ""
Write-Host "[WAW360CONTROL] STAGE PASS"
Write-Host "[WAW360CONTROL] Copy this entire folder to the Xbox usermaps directory:"
Write-Host "  $destination"
Write-Host ""
Write-Host "[WAW360CONTROL] Xbox destination:"
Write-Host "  D:\_codxe\t4\usermaps\$SourceZoneName"
Write-Host ""
Write-Host "[WAW360CONTROL] Codxe/devmap identity:"
Write-Host "  $SourceZoneName"

                }
        )

        if ($mainCandidates.Count -eq 0) {
            throw "No main .ff files found in native control source directory: $SourceDirectory"
        }

        if ($mainCandidates.Count -eq 1) {
            $mainSource = $mainCandidates[0].FullName
        }
        else {
            $fruCandidates = @($mainCandidates | Where-Object { $_.Name -match '(?i)fru|mansion' })
            if ($fruCandidates.Count -eq 1) {
                $mainSource = $fruCandidates[0].FullName
            }
            else {
                $listing = ($mainCandidates | ForEach-Object { "  " + $_.Name }) -join [Environment]::NewLine
                throw "Multiple possible main .ff files found. Re-run with -SourceFastfile <name>.$([Environment]::NewLine)$listing"
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

Write-Host "[WAW360CONTROL] Source identity : $SourceZoneName"
Write-Host "[WAW360CONTROL] Source directory: $SourceDirectory"
Write-Host "[WAW360CONTROL] Stage directory : $destination"
Write-Host ""
Write-Host "[WAW360CONTROL] IMPORTANT: the folder name, FF basename, devmap name and internal BSP identity must agree."
Write-Host "[WAW360CONTROL] For this native FRUmansion control, that identity is '$SourceZoneName'."
Write-Host ""

$candidates = @(
    "$SourceZoneName.ff",
    "${SourceZoneName}_load.ff",
    "${SourceZoneName}_patch.ff",
    "preview.dds"
)

foreach ($name in $candidates) {
    $source = Join-Path $SourceDirectory $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        if ($name -eq "$SourceZoneName.ff") {
            throw "Required control fastfile disappeared while staging: $source"
        }

        Write-Host "[WAW360CONTROL] optional missing: $name"
        continue
    }

    $target = Join-Path $destination $name
    Copy-Item -LiteralPath $source -Destination $target -Force

    $item = Get-Item -LiteralPath $target
    $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash

    Write-Host ("[WAW360CONTROL] copied {0} ({1:N0} bytes) SHA256={2}" -f $name, $item.Length, $hash)
}

$metadata = @{
    version = 1
    name = "FRUmansion - native Xbox control"
    description = "Known-working native Xbox FRUmansion control. Internal map/BSP identity: $SourceZoneName"
} | ConvertTo-Json -Depth 3

$metadataPath = Join-Path $destination "map.json"
Set-Content -LiteralPath $metadataPath -Value $metadata -Encoding ASCII
Write-Host "[WAW360CONTROL] wrote map.json"

$readme = @"
NATIVE FRUMANSION CONTROL
=========================

This control must be installed and launched under its real Xbox zone identity:

    $SourceZoneName

Install the entire folder as:

    D:\_codxe\t4\usermaps\$SourceZoneName\

The required main fastfile path on the Xbox is:

    D:\_codxe\t4\usermaps\$SourceZoneName\$SourceZoneName.ff

Launch/select "$SourceZoneName" in Codxe.

Do NOT rename this control to "nazi_zombie_frumansion".
Its native clipmap reports:

    maps/$SourceZoneName.d3dbsp

Renaming only the folder/FF makes WaW request a different BSP and produces:
"Couldn't find the bsp for this map."

This folder is only an A/B control. It does not alter the generated
nazi_zombie_frumansion candidate.
"@

$readmePath = Join-Path $destination "CONTROL_README.txt"
Set-Content -LiteralPath $readmePath -Value $readme -Encoding ASCII

Write-Host ""
Write-Host "[WAW360CONTROL] STAGE PASS"
Write-Host "[WAW360CONTROL] Copy this entire folder to the Xbox usermaps directory:"
Write-Host "  $destination"
Write-Host ""
Write-Host "[WAW360CONTROL] Xbox destination:"
Write-Host "  D:\_codxe\t4\usermaps\$SourceZoneName"
Write-Host ""
Write-Host "[WAW360CONTROL] Codxe/devmap identity:"
Write-Host "  $SourceZoneName"
