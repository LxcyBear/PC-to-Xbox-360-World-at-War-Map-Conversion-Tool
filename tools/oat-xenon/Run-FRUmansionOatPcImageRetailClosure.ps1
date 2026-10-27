param(
    [string]$PcMapsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps',
    [string]$MapName = 'nazi_zombie_FRUmansion',
    [string]$PcFastFile = '',
    [string]$WaWInstallRoot = 'D:\\SteamLibrary\\steamapps\\common\\Call of Duty World at War',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4i_oat_pc_image_retail_closure'
}

function Add-UniquePath {
    param(
        [System.Collections.Generic.List[string]]$List,
        [string]$Path
    )
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return }
    $full = (Resolve-Path -LiteralPath $Path).Path
    if (-not $List.Contains($full)) { [void]$List.Add($full) }
}

function Find-SteamRoots {
    $roots = New-Object 'System.Collections.Generic.List[string]'

    foreach ($registryPath in @(
        'HKCU:\Software\Valve\Steam',
        'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam',
        'HKLM:\SOFTWARE\Valve\Steam'
    )) {
        try {
            $item = Get-ItemProperty -Path $registryPath -ErrorAction Stop
            foreach ($property in @('SteamPath', 'InstallPath')) {
                if ($item.PSObject.Properties.Name -contains $property) {
                    Add-UniquePath -List $roots -Path $item.$property
                }
            }
        }
        catch {}
    }

    foreach ($common in @(
        'C:\Program Files (x86)\Steam',
        'C:\Program Files\Steam',
        'D:\Steam',
        'D:\SteamLibrary',
        'E:\SteamLibrary',
        'F:\SteamLibrary'
    )) {
        Add-UniquePath -List $roots -Path $common
    }

    $seed = @($roots)
    foreach ($steam in $seed) {
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path -LiteralPath $vdf -PathType Leaf)) { continue }

        foreach ($line in Get-Content -LiteralPath $vdf) {
            if ($line -match '"path"\s+"([^"]+)"') {
                $path = $matches[1] -replace '\\\\', '\'
                Add-UniquePath -List $roots -Path $path
            }
        }
    }

    return @($roots)
}

function Find-WaWInstall {
    param([string]$Explicit)

    $candidates = New-Object 'System.Collections.Generic.List[string]'
    if (-not [string]::IsNullOrWhiteSpace($Explicit)) {
        Add-UniquePath -List $candidates -Path $Explicit
    }

    # Project-configured WaW PC install. Keep this explicit candidate ahead
    # of Steam discovery because secondary-library registry/VDF discovery is
    # not reliable on every Steam installation.
    Add-UniquePath -List $candidates -Path 'D:\SteamLibrary\steamapps\common\Call of Duty World at War'

    foreach ($steamRoot in Find-SteamRoots) {
        foreach ($name in @(
            'Call of Duty World at War',
            'Call of Duty - World at War'
        )) {
            Add-UniquePath -List $candidates -Path (Join-Path $steamRoot "steamapps\common\$name")
        }
    }

    foreach ($candidate in $candidates) {
        $main = Join-Path $candidate 'main'
        if (Test-Path -LiteralPath $main -PathType Container) {
            $iwdCount = @(Get-ChildItem -LiteralPath $main -Filter '*.iwd' -File -ErrorAction SilentlyContinue).Count
            if ($iwdCount -gt 0) {
                return [pscustomobject]@{
                    Root = $candidate
                    Main = $main
                    IwdCount = $iwdCount
                }
            }
        }
    }

    return $null
}

if ([string]::IsNullOrWhiteSpace($PcFastFile)) {
    $mapDir = Join-Path $PcMapsRoot $MapName
    $candidates = @(
        (Join-Path $mapDir 'nazi_zombie_frumansion.ff'),
        (Join-Path $mapDir 'nazi_zombie_FRUmansion.ff'),
        (Join-Path $mapDir ($MapName + '.ff'))
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }

    if ($candidates.Count -eq 0) {
        $candidates = @(Get-ChildItem -LiteralPath $mapDir -Filter '*.ff' -File |
            Where-Object {
                $_.Name -notmatch '(?i)_load[.]ff$' -and
                $_.Name -notmatch '(?i)_patch[.]ff$'
            } |
            Sort-Object Name |
            ForEach-Object { $_.FullName })
    }

    if ($candidates.Count -eq 0) {
        throw "Could not identify the PC main fastfile under: $mapDir"
    }

    $PcFastFile = $candidates[0]
}

if (-not (Test-Path -LiteralPath $PcFastFile -PathType Leaf)) {
    throw "PC fastfile not found: $PcFastFile"
}
if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) {
    throw "OAT Unlinker not found: $unlinker"
}

$install = Find-WaWInstall -Explicit $WaWInstallRoot
if ($null -eq $install) {
    $explicitMain = if (-not [string]::IsNullOrWhiteSpace($WaWInstallRoot)) {
        Join-Path $WaWInstallRoot 'main'
    } else {
        '<not supplied>'
    }

    Write-Host '[NUCLEAR-4I] Install detection diagnostics:' -ForegroundColor Yellow
    Write-Host "[NUCLEAR-4I] Requested root : $WaWInstallRoot"
    Write-Host "[NUCLEAR-4I] Expected main  : $explicitMain"
    if ($explicitMain -ne '<not supplied>') {
        Write-Host "[NUCLEAR-4I] main exists    : $(Test-Path -LiteralPath $explicitMain -PathType Container)"
        if (Test-Path -LiteralPath $explicitMain -PathType Container) {
            $seenIwds = @(Get-ChildItem -LiteralPath $explicitMain -Filter '*.iwd' -File -ErrorAction SilentlyContinue)
            Write-Host "[NUCLEAR-4I] IWDs seen      : $($seenIwds.Count)"
            $seenIwds | Select-Object -First 12 | ForEach-Object { Write-Host "[NUCLEAR-4I]   $($_.Name)" }
        }
    }

    throw "Could not resolve a PC World at War install containing main\*.iwd."
}

$searchPaths = New-Object 'System.Collections.Generic.List[string]'
Add-UniquePath -List $searchPaths -Path (Split-Path -Parent $PcFastFile)
Add-UniquePath -List $searchPaths -Path $install.Main
Add-UniquePath -List $searchPaths -Path (Join-Path $install.Root 'zone\English')

$localMod = Join-Path $env:LOCALAPPDATA "Activision\CoDWaW\mods\$MapName"
Add-UniquePath -List $searchPaths -Path $localMod

$roamingMod = Join-Path $env:APPDATA "Activision\CoDWaW\mods\$MapName"
Add-UniquePath -List $searchPaths -Path $roamingMod

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$dumpRoot = Join-Path $OutputRoot 'dump'
$log = Join-Path $OutputRoot 'oat_pc_image_retail_closure.log'
$searchArg = ($searchPaths -join ';')

Write-Host '[NUCLEAR-4I] ================================================================'
Write-Host '[NUCLEAR-4I] OAT PC IMAGE RETAIL/COMMON DEPENDENCY CLOSURE'
Write-Host '[NUCLEAR-4I] ================================================================'
Write-Host "[NUCLEAR-4I] PC fastfile : $PcFastFile"
Write-Host "[NUCLEAR-4I] WaW root    : $($install.Root)"
Write-Host "[NUCLEAR-4I] main IWDs   : $($install.IwdCount)"
Write-Host "[NUCLEAR-4I] OAT Unlinker: $unlinker"
Write-Host "[NUCLEAR-4I] Output      : $OutputRoot"
Write-Host ''
Write-Host '[NUCLEAR-4I] Search paths:'
foreach ($path in $searchPaths) {
    $iwdCount = @(Get-ChildItem -LiteralPath $path -Filter '*.iwd' -File -ErrorAction SilentlyContinue).Count
    Write-Host ("  [{0,3} IWD] {1}" -f $iwdCount, $path)
}
Write-Host ''
Write-Host '[NUCLEAR-4I] OAT will mount every .iwd in each search-path directory automatically.'
Write-Host '[NUCLEAR-4I] Dumping PC GfxImage closure as IWI v6 for later Xenon re-encoding.'
Write-Host ''

Push-Location (Split-Path -Parent $PcFastFile)
try {
    $args = @(
        '--verbose',
        '--no-color',
        '--include-assets', 'image',
        '--image-format', 'IWI',
        '--output-folder', $dumpRoot,
        '--search-path', $searchArg,
        $PcFastFile
    )

    & $unlinker @args 2>&1 | Tee-Object -FilePath $log
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

$iwiFiles = @()
if (Test-Path -LiteralPath $dumpRoot -PathType Container) {
    $iwiFiles = @(Get-ChildItem -LiteralPath $dumpRoot -Filter '*.iwi' -File -Recurse)
}

$totalBytes = ($iwiFiles | Measure-Object -Property Length -Sum).Sum
if ($null -eq $totalBytes) { $totalBytes = 0 }

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4I.OatPcImageRetailClosure.v1'
    pcFastFile = $PcFastFile
    wawInstallRoot = $install.Root
    retailMain = $install.Main
    retailIwdCount = $install.IwdCount
    searchPaths = @($searchPaths)
    oatExitCode = $code
    dumpedIwiFiles = $iwiFiles.Count
    dumpedIwiBytes = [int64]$totalBytes
    dumpRoot = $dumpRoot
    logFile = $log
    files = @($iwiFiles | Sort-Object Length -Descending | ForEach-Object {
        [ordered]@{
            name = $_.Name
            relativePath = [IO.Path]::GetRelativePath($dumpRoot, $_.FullName)
            bytes = $_.Length
        }
    })
}

$manifestPath = Join-Path $OutputRoot 'nuclear_4i_oat_pc_image_retail_closure.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host ''
Write-Host '[NUCLEAR-4I] ================= IMAGE CLOSURE RESULT =========================='
Write-Host "[NUCLEAR-4I] OAT exit code : $code"
Write-Host "[NUCLEAR-4I] Dumped IWIs   : $($iwiFiles.Count)"
Write-Host "[NUCLEAR-4I] Dumped bytes  : $([int64]$totalBytes)"
Write-Host "[NUCLEAR-4I] Manifest      : $manifestPath"

if ($iwiFiles.Count -gt 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4I] Largest dumped images:'
    $iwiFiles |
        Sort-Object Length -Descending |
        Select-Object -First 30 |
        ForEach-Object {
            Write-Host ("  {0,12:N0} B  {1}" -f $_.Length, [IO.Path]::GetRelativePath($dumpRoot, $_.FullName))
        }
}

Write-Host ''
Write-Host '[NUCLEAR-4I] OAT log tail:'
Get-Content -LiteralPath $log -Tail 120
Write-Host '[NUCLEAR-4I] ================================================================='

if ($code -ne 0) {
    Write-Host '[NUCLEAR-4I] RETAIL IMAGE CLOSURE: FAILED' -ForegroundColor Yellow
    Write-Host '[NUCLEAR-4I] The log tail above names the next unresolved external image/source.'
    exit $code
}

if ($iwiFiles.Count -eq 0) {
    Write-Host '[NUCLEAR-4I] RETAIL IMAGE CLOSURE: ZERO IMAGES' -ForegroundColor Yellow
    exit 4
}

Write-Host '[NUCLEAR-4I] RETAIL IMAGE CLOSURE: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4I] Next: exact-match dumped PC images to the 928 native identities, then Xenon-encode the matched corpus.'
