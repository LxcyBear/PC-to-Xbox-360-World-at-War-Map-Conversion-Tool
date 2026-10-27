param(
    [string]$PcMapsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps',
    [string]$MapName = 'nazi_zombie_FRUmansion',
    [string]$PcFastFile = '',
    [string]$WaWInstallRoot = '',
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


function Add-UniqueString {
    param(
        [System.Collections.Generic.List[string]]$List,
        [string]$Value
    )
    if ([string]::IsNullOrWhiteSpace($Value)) { return }
    if (-not $List.Contains($Value)) { [void]$List.Add($Value) }
}

function Find-WaWFilesystemCandidates {
    $hits = New-Object 'System.Collections.Generic.List[string]'

    foreach ($drive in @(Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
        $root = $drive.Root
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path -LiteralPath $root -PathType Container)) {
            continue
        }

        # Steam manifest probes: inspect common first/second-level library layouts
        # without recursively crawling an entire multi-terabyte drive.
        $manifestPatterns = @(
            'Steam\steamapps\appmanifest_10090.acf',
            'SteamLibrary\steamapps\appmanifest_10090.acf',
            'Games\Steam\steamapps\appmanifest_10090.acf',
            'Games\SteamLibrary\steamapps\appmanifest_10090.acf',
            'Program Files (x86)\Steam\steamapps\appmanifest_10090.acf',
            'Program Files\Steam\steamapps\appmanifest_10090.acf'
        )

        foreach ($relative in $manifestPatterns) {
            $manifest = Join-Path $root $relative
            if (Test-Path -LiteralPath $manifest -PathType Leaf) {
                $install = Parse-SteamAppManifestInstallDir -ManifestPath $manifest
                Add-UniqueString -List $hits -Value $install
            }
        }

        # Direct install probes for retail/manual layouts.
        $directPatterns = @(
            'Games\Call of Duty World at War',
            'Games\Call of Duty - World at War',
            'Call of Duty World at War',
            'Call of Duty - World at War',
            'Program Files (x86)\Activision\Call of Duty - World at War',
            'Program Files\Activision\Call of Duty - World at War'
        )
        foreach ($relative in $directPatterns) {
            $candidate = Join-Path $root $relative
            if (Test-Path -LiteralPath $candidate -PathType Container) {
                Add-UniqueString -List $hits -Value $candidate
            }
        }

        # Bounded two-level directory scan for nonstandard Steam libraries / game roots.
        # This is intentionally not an unrestricted recursive drive crawl.
        try {
            foreach ($level1 in Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue) {
                $steamApps = Join-Path $level1.FullName 'steamapps'
                $manifest = Join-Path $steamApps 'appmanifest_10090.acf'
                if (Test-Path -LiteralPath $manifest -PathType Leaf) {
                    $install = Parse-SteamAppManifestInstallDir -ManifestPath $manifest
                    Add-UniqueString -List $hits -Value $install
                }

                foreach ($level2 in Get-ChildItem -LiteralPath $level1.FullName -Directory -ErrorAction SilentlyContinue) {
                    $steamApps2 = Join-Path $level2.FullName 'steamapps'
                    $manifest2 = Join-Path $steamApps2 'appmanifest_10090.acf'
                    if (Test-Path -LiteralPath $manifest2 -PathType Leaf) {
                        $install2 = Parse-SteamAppManifestInstallDir -ManifestPath $manifest2
                        Add-UniqueString -List $hits -Value $install2
                    }

                    if ($level2.Name -match '(?i)^Call of Duty(?: -)? World at War    param([string]$ManifestPath)

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) { return $null }

    $installDir = $null
    foreach ($line in Get-Content -LiteralPath $ManifestPath -ErrorAction SilentlyContinue) {
        if ($line -match '"installdir"\s+"([^"]+)"') {
            $installDir = $matches[1]
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($installDir)) { return $null }

    $steamApps = Split-Path -Parent $ManifestPath
    return (Join-Path (Join-Path $steamApps 'common') $installDir)
}

function Find-SteamLibraryRoots {
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

    foreach ($drive in @(Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
        $root = $drive.Root
        foreach ($relative in @(
            'Steam',
            'SteamLibrary',
            'Games\Steam',
            'Games\SteamLibrary',
            'Program Files (x86)\Steam',
            'Program Files\Steam'
        )) {
            Add-UniquePath -List $roots -Path (Join-Path $root $relative)
        }
    }

    # Expand libraryfolders.vdf from every discovered Steam client/library root.
    $queue = @($roots)
    foreach ($steam in $queue) {
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path -LiteralPath $vdf -PathType Leaf)) { continue }

        foreach ($line in Get-Content -LiteralPath $vdf -ErrorAction SilentlyContinue) {
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

    # Windows uninstall metadata can identify both Steam and older retail installs.
    foreach ($uninstallRoot in @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
    )) {
        if (-not (Test-Path $uninstallRoot)) { continue }

        foreach ($subkey in Get-ChildItem -Path $uninstallRoot -ErrorAction SilentlyContinue) {
            try {
                $item = Get-ItemProperty -Path $subkey.PSPath -ErrorAction Stop
                $display = [string]$item.DisplayName
                $location = [string]$item.InstallLocation
                $keyName = [string]$subkey.PSChildName

                if ($keyName -eq 'Steam App 10090' -or
                    $display -match '(?i)Call of Duty.*World at War') {
                    Add-UniquePath -List $candidates -Path $location
                }
            }
            catch {}
        }
    }

    # Steam's authoritative app manifest for World at War is appmanifest_10090.acf.
    foreach ($library in Find-SteamLibraryRoots) {
        $manifest = Join-Path $library 'steamapps\appmanifest_10090.acf'
        $manifestInstall = Parse-SteamAppManifestInstallDir -ManifestPath $manifest
        Add-UniquePath -List $candidates -Path $manifestInstall

        # Keep conventional folder names as a secondary fallback.
        foreach ($name in @(
            'Call of Duty World at War',
            'Call of Duty - World at War'
        )) {
            Add-UniquePath -List $candidates -Path (Join-Path $library "steamapps\common\$name")
        }
    }

    # Bounded drive-layout fallback for nonstandard/manual installs.
    foreach ($drive in @(Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
        $root = $drive.Root
        foreach ($relative in @(
            'Games\Call of Duty World at War',
            'Games\Call of Duty - World at War',
            'Call of Duty World at War',
            'Call of Duty - World at War',
            'Program Files (x86)\Activision\Call of Duty - World at War',
            'Program Files\Activision\Call of Duty - World at War'
        )) {
            Add-UniquePath -List $candidates -Path (Join-Path $root $relative)
        }
    }

    foreach ($candidate in Find-WaWFilesystemCandidates) {
        Add-UniquePath -List $candidates -Path $candidate
    }

    foreach ($candidate in $candidates) {
        $main = Join-Path $candidate 'main'
        if (-not (Test-Path -LiteralPath $main -PathType Container)) { continue }

        $iwdCount = @(Get-ChildItem -LiteralPath $main -Filter '*.iwd' -File -ErrorAction SilentlyContinue).Count
        $exeExists =
            (Test-Path -LiteralPath (Join-Path $candidate 'CoDWaW.exe') -PathType Leaf) -or
            (Test-Path -LiteralPath (Join-Path $candidate 'CoDWaWmp.exe') -PathType Leaf)

        if ($iwdCount -gt 0) {
            return [pscustomobject]@{
                Root = $candidate
                Main = $main
                IwdCount = $iwdCount
                ExeDetected = $exeExists
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
    Write-Host '[NUCLEAR-4I] Install discovery diagnostics:' -ForegroundColor Yellow
    Write-Host "[NUCLEAR-4I] Explicit root : $WaWInstallRoot"
    Write-Host '[NUCLEAR-4I] Steam App ID  : 10090'
    Write-Host ''

    $steamRoots = @(Find-SteamLibraryRoots)
    Write-Host "[NUCLEAR-4I] Steam/library roots found: $($steamRoots.Count)"
    foreach ($root in $steamRoots) {
        $manifest = Join-Path $root 'steamapps\appmanifest_10090.acf'
        Write-Host ("[NUCLEAR-4I]   {0} | WaW manifest={1}" -f $root, (Test-Path -LiteralPath $manifest -PathType Leaf))
    }

    $fsCandidates = @(Find-WaWFilesystemCandidates)
    Write-Host ''
    Write-Host "[NUCLEAR-4I] Filesystem WaW candidates found: $($fsCandidates.Count)"
    foreach ($candidate in $fsCandidates) {
        $main = Join-Path $candidate 'main'
        $iwdCount = if (Test-Path -LiteralPath $main -PathType Container) {
            @(Get-ChildItem -LiteralPath $main -Filter '*.iwd' -File -ErrorAction SilentlyContinue).Count
        } else { 0 }
        $exe = (Test-Path -LiteralPath (Join-Path $candidate 'CoDWaW.exe') -PathType Leaf) -or
               (Test-Path -LiteralPath (Join-Path $candidate 'CoDWaWmp.exe') -PathType Leaf)
        Write-Host ("[NUCLEAR-4I]   {0} | main={1} | iwds={2} | exe={3}" -f $candidate, (Test-Path -LiteralPath $main -PathType Container), $iwdCount, $exe)
    }

    Write-Host ''
    Write-Host '[NUCLEAR-4I] RESULT: no candidate contained main\*.iwd.'
    Write-Host '[NUCLEAR-4I] If you know the PC WaW install location, rerun with:'
    Write-Host '  -WaWInstallRoot "X:\path\to\Call of Duty World at War"'
    Write-Host '[NUCLEAR-4I] If no PC retail install exists, this confirms retail/common image data must be sourced from a user-supplied WaW PC installation before generic conversion can close these image dependencies.'
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
) {
                        Add-UniqueString -List $hits -Value $level2.FullName
                    }
                }
            }
        }
        catch {}
    }

    return @($hits)
}

function Parse-SteamAppManifestInstallDir {
    param([string]$ManifestPath)

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) { return $null }

    $installDir = $null
    foreach ($line in Get-Content -LiteralPath $ManifestPath -ErrorAction SilentlyContinue) {
        if ($line -match '"installdir"\s+"([^"]+)"') {
            $installDir = $matches[1]
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($installDir)) { return $null }

    $steamApps = Split-Path -Parent $ManifestPath
    return (Join-Path (Join-Path $steamApps 'common') $installDir)
}

function Find-SteamLibraryRoots {
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

    foreach ($drive in @(Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
        $root = $drive.Root
        foreach ($relative in @(
            'Steam',
            'SteamLibrary',
            'Games\Steam',
            'Games\SteamLibrary',
            'Program Files (x86)\Steam',
            'Program Files\Steam'
        )) {
            Add-UniquePath -List $roots -Path (Join-Path $root $relative)
        }
    }

    # Expand libraryfolders.vdf from every discovered Steam client/library root.
    $queue = @($roots)
    foreach ($steam in $queue) {
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path -LiteralPath $vdf -PathType Leaf)) { continue }

        foreach ($line in Get-Content -LiteralPath $vdf -ErrorAction SilentlyContinue) {
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

    # Windows uninstall metadata can identify both Steam and older retail installs.
    foreach ($uninstallRoot in @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
    )) {
        if (-not (Test-Path $uninstallRoot)) { continue }

        foreach ($subkey in Get-ChildItem -Path $uninstallRoot -ErrorAction SilentlyContinue) {
            try {
                $item = Get-ItemProperty -Path $subkey.PSPath -ErrorAction Stop
                $display = [string]$item.DisplayName
                $location = [string]$item.InstallLocation
                $keyName = [string]$subkey.PSChildName

                if ($keyName -eq 'Steam App 10090' -or
                    $display -match '(?i)Call of Duty.*World at War') {
                    Add-UniquePath -List $candidates -Path $location
                }
            }
            catch {}
        }
    }

    # Steam's authoritative app manifest for World at War is appmanifest_10090.acf.
    foreach ($library in Find-SteamLibraryRoots) {
        $manifest = Join-Path $library 'steamapps\appmanifest_10090.acf'
        $manifestInstall = Parse-SteamAppManifestInstallDir -ManifestPath $manifest
        Add-UniquePath -List $candidates -Path $manifestInstall

        # Keep conventional folder names as a secondary fallback.
        foreach ($name in @(
            'Call of Duty World at War',
            'Call of Duty - World at War'
        )) {
            Add-UniquePath -List $candidates -Path (Join-Path $library "steamapps\common\$name")
        }
    }

    # Bounded drive-layout fallback for nonstandard/manual installs.
    foreach ($drive in @(Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
        $root = $drive.Root
        foreach ($relative in @(
            'Games\Call of Duty World at War',
            'Games\Call of Duty - World at War',
            'Call of Duty World at War',
            'Call of Duty - World at War',
            'Program Files (x86)\Activision\Call of Duty - World at War',
            'Program Files\Activision\Call of Duty - World at War'
        )) {
            Add-UniquePath -List $candidates -Path (Join-Path $root $relative)
        }
    }

    foreach ($candidate in $candidates) {
        $main = Join-Path $candidate 'main'
        if (-not (Test-Path -LiteralPath $main -PathType Container)) { continue }

        $iwdCount = @(Get-ChildItem -LiteralPath $main -Filter '*.iwd' -File -ErrorAction SilentlyContinue).Count
        $exeExists =
            (Test-Path -LiteralPath (Join-Path $candidate 'CoDWaW.exe') -PathType Leaf) -or
            (Test-Path -LiteralPath (Join-Path $candidate 'CoDWaWmp.exe') -PathType Leaf)

        if ($iwdCount -gt 0) {
            return [pscustomobject]@{
                Root = $candidate
                Main = $main
                IwdCount = $iwdCount
                ExeDetected = $exeExists
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
    Write-Host '[NUCLEAR-4I] Install discovery diagnostics:' -ForegroundColor Yellow
    Write-Host "[NUCLEAR-4I] Explicit root : $WaWInstallRoot"
    Write-Host '[NUCLEAR-4I] Steam App ID  : 10090'
    Write-Host '[NUCLEAR-4I] Checked Steam appmanifest_10090.acf, uninstall registry entries, common Steam libraries, and bounded common install layouts.'
    Write-Host ''
    Write-Host '[NUCLEAR-4I] If WaW is installed in a custom/nonstandard folder, rerun with:'
    Write-Host '  -WaWInstallRoot "X:\path\to\Call of Duty World at War"'
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
