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

function Add-UniqueDirectory {
    param(
        [System.Collections.Generic.List[string]]$List,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return
    }

    $full = (Resolve-Path -LiteralPath $Path).Path
    if (-not $List.Contains($full)) {
        [void]$List.Add($full)
    }
}

function Read-SteamInstallDir {
    param([string]$ManifestPath)

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        return $null
    }

    foreach ($line in Get-Content -LiteralPath $ManifestPath -ErrorAction SilentlyContinue) {
        if ($line -match '"installdir"\s+"([^"]+)"') {
            $steamApps = Split-Path -Parent $ManifestPath
            return (Join-Path (Join-Path $steamApps 'common') $matches[1])
        }
    }

    return $null
}

function Get-SteamLibraryRoots {
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
                    Add-UniqueDirectory -List $roots -Path ([string]$item.$property)
                }
            }
        }
        catch {}
    }

    foreach ($drive in @(Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
        foreach ($relative in @(
            'Steam',
            'SteamLibrary',
            'Games\Steam',
            'Games\SteamLibrary',
            'Program Files (x86)\Steam',
            'Program Files\Steam'
        )) {
            Add-UniqueDirectory -List $roots -Path (Join-Path $drive.Root $relative)
        }
    }

    $initial = @($roots)
    foreach ($root in $initial) {
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path -LiteralPath $vdf -PathType Leaf)) {
            continue
        }

        foreach ($line in Get-Content -LiteralPath $vdf -ErrorAction SilentlyContinue) {
            if ($line -match '"path"\s+"([^"]+)"') {
                $libraryPath = $matches[1].Replace('\\', '\')
                Add-UniqueDirectory -List $roots -Path $libraryPath
            }
        }
    }

    return @($roots)
}

function Get-WaWCandidates {
    param([string]$ExplicitRoot)

    $candidates = New-Object 'System.Collections.Generic.List[string]'

    if (-not [string]::IsNullOrWhiteSpace($ExplicitRoot)) {
        Add-UniqueDirectory -List $candidates -Path $ExplicitRoot
    }

    foreach ($uninstallRoot in @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
    )) {
        if (-not (Test-Path $uninstallRoot)) {
            continue
        }

        foreach ($subkey in Get-ChildItem -Path $uninstallRoot -ErrorAction SilentlyContinue) {
            try {
                $item = Get-ItemProperty -Path $subkey.PSPath -ErrorAction Stop
                $displayName = [string]$item.DisplayName
                $installLocation = [string]$item.InstallLocation
                $keyName = [string]$subkey.PSChildName

                if ($keyName -eq 'Steam App 10090' -or
                    $displayName -match '(?i)Call of Duty.*World at War') {
                    Add-UniqueDirectory -List $candidates -Path $installLocation
                }
            }
            catch {}
        }
    }

    foreach ($library in Get-SteamLibraryRoots) {
        $manifest = Join-Path $library 'steamapps\appmanifest_10090.acf'
        $manifestRoot = Read-SteamInstallDir -ManifestPath $manifest
        Add-UniqueDirectory -List $candidates -Path $manifestRoot

        foreach ($name in @(
            'Call of Duty World at War',
            'Call of Duty - World at War'
        )) {
            Add-UniqueDirectory -List $candidates -Path (Join-Path $library "steamapps\common\$name")
        }
    }

    foreach ($drive in @(Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
        foreach ($relative in @(
            'Games\Call of Duty World at War',
            'Games\Call of Duty - World at War',
            'Call of Duty World at War',
            'Call of Duty - World at War',
            'Program Files (x86)\Activision\Call of Duty - World at War',
            'Program Files\Activision\Call of Duty - World at War'
        )) {
            Add-UniqueDirectory -List $candidates -Path (Join-Path $drive.Root $relative)
        }

        try {
            foreach ($level1 in Get-ChildItem -LiteralPath $drive.Root -Directory -ErrorAction SilentlyContinue) {
                $manifest = Join-Path $level1.FullName 'steamapps\appmanifest_10090.acf'
                $manifestRoot = Read-SteamInstallDir -ManifestPath $manifest
                Add-UniqueDirectory -List $candidates -Path $manifestRoot

                if ($level1.Name -match '(?i)^Call of Duty(?: -)? World at War$') {
                    Add-UniqueDirectory -List $candidates -Path $level1.FullName
                }
            }
        }
        catch {}
    }

    return @($candidates)
}

function Resolve-WaWInstall {
    param([string]$ExplicitRoot)

    $candidates = @(Get-WaWCandidates -ExplicitRoot $ExplicitRoot)

    foreach ($candidate in $candidates) {
        $main = Join-Path $candidate 'main'
        if (-not (Test-Path -LiteralPath $main -PathType Container)) {
            continue
        }

        $iwds = @(Get-ChildItem -LiteralPath $main -Filter '*.iwd' -File -ErrorAction SilentlyContinue)
        if ($iwds.Count -eq 0) {
            continue
        }

        $exeDetected =
            (Test-Path -LiteralPath (Join-Path $candidate 'CoDWaW.exe') -PathType Leaf) -or
            (Test-Path -LiteralPath (Join-Path $candidate 'CoDWaWmp.exe') -PathType Leaf)

        return [pscustomobject]@{
            Root = $candidate
            Main = $main
            IwdCount = $iwds.Count
            ExeDetected = $exeDetected
            Candidates = $candidates
        }
    }

    return [pscustomobject]@{
        Root = $null
        Main = $null
        IwdCount = 0
        ExeDetected = $false
        Candidates = $candidates
    }
}

if ([string]::IsNullOrWhiteSpace($PcFastFile)) {
    $mapDir = Join-Path $PcMapsRoot $MapName

    $preferred = @(
        (Join-Path $mapDir 'nazi_zombie_frumansion.ff'),
        (Join-Path $mapDir 'nazi_zombie_FRUmansion.ff'),
        (Join-Path $mapDir ($MapName + '.ff'))
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }

    if ($preferred.Count -gt 0) {
        $PcFastFile = $preferred[0]
    }
    else {
        $fallback = @(Get-ChildItem -LiteralPath $mapDir -Filter '*.ff' -File -ErrorAction Stop |
            Where-Object {
                $_.Name -notmatch '(?i)_load[.]ff$' -and
                $_.Name -notmatch '(?i)_patch[.]ff$'
            } |
            Sort-Object Name)

        if ($fallback.Count -eq 0) {
            throw "Could not identify the PC main fastfile under: $mapDir"
        }

        $PcFastFile = $fallback[0].FullName
    }
}

if (-not (Test-Path -LiteralPath $PcFastFile -PathType Leaf)) {
    throw "PC fastfile not found: $PcFastFile"
}

if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) {
    throw "OAT Unlinker not found: $unlinker"
}

$install = Resolve-WaWInstall -ExplicitRoot $WaWInstallRoot

if ([string]::IsNullOrWhiteSpace([string]$install.Root)) {
    Write-Host '[NUCLEAR-4I] ================================================================'
    Write-Host '[NUCLEAR-4I] RETAIL WAW INSTALL DISCOVERY'
    Write-Host '[NUCLEAR-4I] ================================================================'
    Write-Host "[NUCLEAR-4I] Explicit root : $WaWInstallRoot"
    Write-Host '[NUCLEAR-4I] Steam App ID  : 10090'
    Write-Host "[NUCLEAR-4I] Candidates    : $($install.Candidates.Count)"
    Write-Host ''

    foreach ($candidate in $install.Candidates) {
        $main = Join-Path $candidate 'main'
        $mainExists = Test-Path -LiteralPath $main -PathType Container
        $iwdCount = 0
        if ($mainExists) {
            $iwdCount = @(Get-ChildItem -LiteralPath $main -Filter '*.iwd' -File -ErrorAction SilentlyContinue).Count
        }

        $exeDetected =
            (Test-Path -LiteralPath (Join-Path $candidate 'CoDWaW.exe') -PathType Leaf) -or
            (Test-Path -LiteralPath (Join-Path $candidate 'CoDWaWmp.exe') -PathType Leaf)

        Write-Host ("[NUCLEAR-4I]   {0}" -f $candidate)
        Write-Host ("[NUCLEAR-4I]      main={0}; iwds={1}; exe={2}" -f $mainExists, $iwdCount, $exeDetected)
    }

    Write-Host ''
    Write-Host '[NUCLEAR-4I] RESULT: no discovered candidate contained main\*.iwd.'
    Write-Host '[NUCLEAR-4I] If you know the install location, rerun with:'
    Write-Host '  -WaWInstallRoot "X:\path\to\Call of Duty World at War"'
    throw "Could not resolve a PC World at War install containing main\*.iwd."
}

$searchPaths = New-Object 'System.Collections.Generic.List[string]'
Add-UniqueDirectory -List $searchPaths -Path (Split-Path -Parent $PcFastFile)
Add-UniqueDirectory -List $searchPaths -Path $install.Main
Add-UniqueDirectory -List $searchPaths -Path (Join-Path $install.Root 'zone\English')

$localMod = Join-Path $env:LOCALAPPDATA "Activision\CoDWaW\mods\$MapName"
Add-UniqueDirectory -List $searchPaths -Path $localMod

$roamingMod = Join-Path $env:APPDATA "Activision\CoDWaW\mods\$MapName"
Add-UniqueDirectory -List $searchPaths -Path $roamingMod

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$dumpRoot = Join-Path $OutputRoot 'dump'
$log = Join-Path $OutputRoot 'oat_pc_image_retail_closure.log'
$searchArg = $searchPaths -join ';'

Write-Host '[NUCLEAR-4I] ================================================================'
Write-Host '[NUCLEAR-4I] OAT PC IMAGE RETAIL/COMMON DEPENDENCY CLOSURE'
Write-Host '[NUCLEAR-4I] ================================================================'
Write-Host "[NUCLEAR-4I] PC fastfile : $PcFastFile"
Write-Host "[NUCLEAR-4I] WaW root    : $($install.Root)"
Write-Host "[NUCLEAR-4I] main IWDs   : $($install.IwdCount)"
Write-Host "[NUCLEAR-4I] exe detected: $($install.ExeDetected)"
Write-Host "[NUCLEAR-4I] OAT Unlinker: $unlinker"
Write-Host "[NUCLEAR-4I] Output      : $OutputRoot"
Write-Host ''
Write-Host '[NUCLEAR-4I] Search paths:'

foreach ($path in $searchPaths) {
    $iwdCount = @(Get-ChildItem -LiteralPath $path -Filter '*.iwd' -File -ErrorAction SilentlyContinue).Count
    Write-Host ("  [{0,3} IWD] {1}" -f $iwdCount, $path)
}

Write-Host ''
Write-Host '[NUCLEAR-4I] OAT will mount each .iwd present in the configured search-path directories.'
Write-Host '[NUCLEAR-4I] Dumping PC GfxImage closure as IWI v6 for later Xenon re-encoding.'
Write-Host ''

Push-Location (Split-Path -Parent $PcFastFile)
try {
    $arguments = @(
        '--verbose',
        '--no-color',
        '--include-assets', 'image',
        '--image-format', 'IWI',
        '--output-folder', $dumpRoot,
        '--search-path', $searchArg,
        $PcFastFile
    )

    & $unlinker @arguments 2>&1 | Tee-Object -FilePath $log
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
if ($null -eq $totalBytes) {
    $totalBytes = 0
}

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4I.OatPcImageRetailClosure.v2'
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
    Write-Host '[NUCLEAR-4I] The OAT log tail above names the next unresolved image/source dependency.'
    exit $code
}

if ($iwiFiles.Count -eq 0) {
    Write-Host '[NUCLEAR-4I] RETAIL IMAGE CLOSURE: ZERO IMAGES' -ForegroundColor Yellow
    exit 4
}

Write-Host '[NUCLEAR-4I] RETAIL IMAGE CLOSURE: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4I] Next: exact-match dumped PC images to the 928 native identities, then Xenon-encode the matched corpus.'
