param(
    [string]$PcMapsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps',
    [string]$MapName = 'nazi_zombie_FRUmansion',
    [string]$NativeIdentityManifest = '',
    [string]$WaWInstallRoot = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$linker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Linker.exe'
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if ([string]::IsNullOrWhiteSpace($NativeIdentityManifest)) {
    $identityDir = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_root_binding\native_identity'
    $identity = @(Get-ChildItem -LiteralPath $identityDir -Filter '*_native_identity_manifest.json' -File -ErrorAction SilentlyContinue | Sort-Object FullName | Select-Object -First 1)
    if ($identity.Count -eq 1) {
        $NativeIdentityManifest = $identity[0].FullName
    }
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_pc_image_bridge'
}

if (-not (Test-Path -LiteralPath $linker -PathType Leaf)) { throw "Pinned Xenon OAT Linker not found: $linker" }
if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) { throw "Specialized Xenon OAT Unlinker not found: $unlinker" }
if (-not (Test-Path -LiteralPath $NativeIdentityManifest -PathType Leaf)) { throw "Verified native identity manifest not found: $NativeIdentityManifest" }


function Invoke-NativeTee {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][object[]]$Arguments,
        [Parameter(Mandatory=$true)][string]$LogPath
    )

    $previousPreference = $ErrorActionPreference
    try {
        # Windows PowerShell 5.x wraps native stderr as NativeCommandError records.
        # Keep those visible/logged, but do not let ErrorActionPreference=Stop abort
        # before we can collect $LASTEXITCODE and preserve partial OAT output.
        $ErrorActionPreference = 'Continue'
        & $FilePath @Arguments 2>&1 |
            Tee-Object -FilePath $LogPath |
            ForEach-Object { Write-Host ([string]$_) }
        return [int]$LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
}

function Get-RelativePathCompat {
    param([string]$BasePath, [string]$TargetPath)
    $baseFull = [IO.Path]::GetFullPath($BasePath)
    $targetFull = [IO.Path]::GetFullPath($TargetPath)
    if (-not $baseFull.EndsWith('\')) { $baseFull += '\' }
    if ($targetFull.StartsWith($baseFull, [StringComparison]::OrdinalIgnoreCase)) {
        return $targetFull.Substring($baseFull.Length)
    }
    return $targetFull
}

function Add-SourceRoot {
    param([System.Collections.Generic.List[object]]$Roots, [string]$Path, [string]$Kind, [int]$Priority)
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return }
    $full = (Resolve-Path -LiteralPath $Path).Path
    if ($full.StartsWith((Join-Path $repo 'output'), [StringComparison]::OrdinalIgnoreCase)) { return }
    foreach ($existing in $Roots) {
        if ([string]$existing.Path -ieq $full) {
            if ($Priority -lt [int]$existing.Priority) {
                $existing.Kind = $Kind
                $existing.Priority = $Priority
            }
            return
        }
    }
    $Roots.Add([pscustomobject]@{ Path = $full; Kind = $Kind; Priority = $Priority })
}

function Add-UniqueDirectory {
    param([System.Collections.Generic.List[string]]$List, [string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return }
    $full = (Resolve-Path -LiteralPath $Path).Path
    foreach ($existing in $List) {
        if ($existing -ieq $full) { return }
    }
    $List.Add($full)
}

function Read-SteamInstallDir {
    param([string]$ManifestPath)
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) { return $null }
    foreach ($line in Get-Content -LiteralPath $ManifestPath -ErrorAction SilentlyContinue) {
        if ($line -match '"installdir"\s+"([^"]+)"') {
            $steamApps = Split-Path -Parent $ManifestPath
            return (Join-Path (Join-Path $steamApps 'common') $matches[1])
        }
    }
    return $null
}

function Resolve-ConfiguredWaWRoot {
    $configFiles = New-Object 'System.Collections.Generic.List[string]'
    $seen = @{}

    foreach ($candidate in @(
        (Join-Path $repo 'settings.ini'),
        (Join-Path $repo 'config\settings.ini'),
        (Join-Path $repo 'converter\settings.ini')
    )) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $full = (Resolve-Path -LiteralPath $candidate).Path
            $key = $full.ToLowerInvariant()
            if (-not $seen.ContainsKey($key)) {
                $seen[$key] = $true
                $configFiles.Add($full)
            }
        }
    }

    try {
        foreach ($file in @(Get-ChildItem -LiteralPath $repo -Filter 'settings.ini' -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 20)) {
            $key = $file.FullName.ToLowerInvariant()
            if (-not $seen.ContainsKey($key)) {
                $seen[$key] = $true
                $configFiles.Add($file.FullName)
            }
        }
    }
    catch {}

    foreach ($config in $configFiles) {
        foreach ($line in Get-Content -LiteralPath $config -ErrorAction SilentlyContinue) {
            if ($line -notmatch '^\s*PCGame\s*=\s*(.+?)\s*$') { continue }
            $candidate = ([string]$matches[1]).Trim().Trim('"').Trim("'")
            if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
            if (-not (Test-Path -LiteralPath $candidate -PathType Container)) { continue }

            $main = Join-Path $candidate 'main'
            $iwds = @()
            if (Test-Path -LiteralPath $main -PathType Container) {
                $iwds = @(Get-ChildItem -LiteralPath $main -Filter '*.iwd' -File -ErrorAction SilentlyContinue)
            }

            if ($iwds.Count -gt 0) {
                Write-Host "[NUCLEAR-4R] PC WaW root from settings: $candidate"
                Write-Host "[NUCLEAR-4R] Settings source         : $config"
                return (Resolve-Path -LiteralPath $candidate).Path
            }
        }
    }

    return $null
}

function Resolve-SteamWaWRoot {
    $steamRoots = New-Object 'System.Collections.Generic.List[string]'
    $candidates = New-Object 'System.Collections.Generic.List[string]'

    foreach ($uninstallRoot in @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
    )) {
        if (-not (Test-Path $uninstallRoot)) { continue }
        foreach ($subkey in Get-ChildItem -Path $uninstallRoot -ErrorAction SilentlyContinue) {
            try {
                $item = Get-ItemProperty -Path $subkey.PSPath -ErrorAction Stop
                $displayName = [string]$item.DisplayName
                $installLocation = [string]$item.InstallLocation
                $keyName = [string]$subkey.PSChildName
                if ($keyName -eq 'Steam App 10090' -or $displayName -match '(?i)Call of Duty.*World at War') {
                    Add-UniqueDirectory -List $candidates -Path $installLocation
                }
            }
            catch {}
        }
    }

    foreach ($registryPath in @(
        'HKCU:\Software\Valve\Steam',
        'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam',
        'HKLM:\SOFTWARE\Valve\Steam'
    )) {
        try {
            $item = Get-ItemProperty -Path $registryPath -ErrorAction Stop
            foreach ($property in @('SteamPath', 'InstallPath')) {
                if ($item.PSObject.Properties.Name -contains $property) {
                    Add-UniqueDirectory -List $steamRoots -Path ([string]$item.$property)
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
            Add-UniqueDirectory -List $steamRoots -Path (Join-Path $drive.Root $relative)
        }
    }

    $initialSteamRoots = @($steamRoots)
    foreach ($steam in $initialSteamRoots) {
        $libraryFile = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path -LiteralPath $libraryFile -PathType Leaf)) { continue }
        foreach ($line in Get-Content -LiteralPath $libraryFile -ErrorAction SilentlyContinue) {
            $libraryPath = $null
            if ($line -match '"path"\s+"([^"]+)"') {
                $libraryPath = [string]$matches[1]
            }
            elseif ($line -match '^\s*"\d+"\s+"([A-Za-z]:\\[^"]+)"') {
                $libraryPath = [string]$matches[1]
            }
            if (-not [string]::IsNullOrWhiteSpace($libraryPath)) {
                $libraryPath = $libraryPath.Replace('\\', '\')
                Add-UniqueDirectory -List $steamRoots -Path $libraryPath
            }
        }
    }

    foreach ($library in @($steamRoots)) {
        $manifest = Join-Path $library 'steamapps\appmanifest_10090.acf'
        Add-UniqueDirectory -List $candidates -Path (Read-SteamInstallDir -ManifestPath $manifest)

        foreach ($name in @('Call of Duty World at War', 'Call of Duty - World at War')) {
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
                Add-UniqueDirectory -List $candidates -Path (Read-SteamInstallDir -ManifestPath $manifest)
                if ($level1.Name -match '(?i)^Call of Duty(?: -)? World at War$') {
                    Add-UniqueDirectory -List $candidates -Path $level1.FullName
                }
            }
        }
        catch {}
    }

    foreach ($candidate in @($candidates)) {
        $main = Join-Path $candidate 'main'
        if (-not (Test-Path -LiteralPath $main -PathType Container)) { continue }
        $iwds = @(Get-ChildItem -LiteralPath $main -Filter '*.iwd' -File -ErrorAction SilentlyContinue)
        if ($iwds.Count -gt 0) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

function Normalize-SourceIdentity {
    param([string]$Value)
    $v = $Value.Replace('\', '/').TrimStart('/')
    $marker = $v.ToLowerInvariant().LastIndexOf('/images/')
    if ($marker -ge 0) {
        $v = $v.Substring($marker + 8)
    }
    elseif ($v.StartsWith('images/', [StringComparison]::OrdinalIgnoreCase)) {
        $v = $v.Substring(7)
    }
    if ($v.EndsWith('.iwi', [StringComparison]::OrdinalIgnoreCase) -or $v.EndsWith('.dds', [StringComparison]::OrdinalIgnoreCase)) {
        $v = $v.Substring(0, $v.Length - 4)
    }
    return $v
}

function Normalize-NativeIdentity {
    param([string]$Value)
    $v = $Value.Replace('\', '/')
    if ($v.EndsWith('_wj', [StringComparison]::OrdinalIgnoreCase)) {
        return $v.Substring(0, $v.Length - 3)
    }
    return $v
}

function Has-PortableAssetPath {
    param([string]$AssetName)
    if ([string]::IsNullOrWhiteSpace($AssetName)) { return $false }
    $invalid = [IO.Path]::GetInvalidFileNameChars()
    foreach ($segment in $AssetName.Replace('\', '/').Split('/')) {
        if ([string]::IsNullOrWhiteSpace($segment)) { return $false }
        if ($segment.IndexOfAny($invalid) -ge 0) { return $false }
    }
    return $true
}

function Add-Candidate {
    param(
        [System.Collections.Generic.List[object]]$Candidates,
        [string]$Identity,
        [string]$Extension,
        [string]$SourceKind,
        [string]$SourceRoot,
        [string]$SourcePath,
        [string]$ArchiveEntry,
        [int]$Priority,
        [int64]$Bytes
    )
    if ([string]::IsNullOrWhiteSpace($Identity)) { return }
    if ($Extension -ine '.iwi' -and $Extension -ine '.dds') { return }
    $Candidates.Add([pscustomobject]@{
        Identity = $Identity
        Extension = $Extension.ToLowerInvariant()
        SourceKind = $SourceKind
        SourceRoot = $SourceRoot
        SourcePath = $SourcePath
        ArchiveEntry = $ArchiveEntry
        Priority = $Priority
        Bytes = $Bytes
    })
}

$nativeDoc = Get-Content -LiteralPath $NativeIdentityManifest -Raw | ConvertFrom-Json
if ([string]$nativeDoc.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or -not [bool]$nativeDoc.typeSequenceVerified) {
    throw 'Stage 4R requires a verified T4Xenon native identity manifest.'
}

$nativeImages = @(
    $nativeDoc.entries |
    Where-Object { ([string]$_.TypeName -ieq 'image') -or ([string]$_.OatTypeName -ieq 'image') } |
    Sort-Object Index |
    ForEach-Object {
        [pscustomobject]@{
            Index = [int]$_.Index
            TypeId = [int]$_.TypeId
            Name = [string]$_.Name
            Normalized = Normalize-NativeIdentity -Value ([string]$_.Name)
        }
    }
)
if ($nativeImages.Count -eq 0) { throw 'Verified native identity manifest contains no image roots.' }

$roots = New-Object 'System.Collections.Generic.List[object]'
$mapDir = Join-Path $PcMapsRoot $MapName
Add-SourceRoot -Roots $roots -Path $mapDir -Kind 'map-folder' -Priority 10

$localMod = Join-Path $env:LOCALAPPDATA "Activision\CoDWaW\mods\$MapName"
$localUsermap = Join-Path $env:LOCALAPPDATA "Activision\CoDWaW\usermaps\$MapName"
$roamingMod = Join-Path $env:APPDATA "Activision\CoDWaW\mods\$MapName"
$roamingUsermap = Join-Path $env:APPDATA "Activision\CoDWaW\usermaps\$MapName"

Add-SourceRoot -Roots $roots -Path $localMod -Kind 'local-mod' -Priority 20
Add-SourceRoot -Roots $roots -Path $localUsermap -Kind 'local-usermap' -Priority 20
Add-SourceRoot -Roots $roots -Path $roamingMod -Kind 'roaming-mod' -Priority 25
Add-SourceRoot -Roots $roots -Path $roamingUsermap -Kind 'roaming-usermap' -Priority 25

if ([string]::IsNullOrWhiteSpace($WaWInstallRoot)) {
    $WaWInstallRoot = Resolve-ConfiguredWaWRoot
}
if ([string]::IsNullOrWhiteSpace($WaWInstallRoot)) {
    $envRoot = [Environment]::GetEnvironmentVariable('WAW_PC_ROOT')
    if (-not [string]::IsNullOrWhiteSpace($envRoot)) { $WaWInstallRoot = $envRoot }
}
if ([string]::IsNullOrWhiteSpace($WaWInstallRoot)) { $WaWInstallRoot = Resolve-SteamWaWRoot }

$resolvedWaWRoot = $null
$retailMain = $null
$retailIwdCount = 0
$retailStockIwdCount = 0

if (-not [string]::IsNullOrWhiteSpace($WaWInstallRoot)) {
    if (-not (Test-Path -LiteralPath $WaWInstallRoot -PathType Container)) {
        throw "PC WaW install root does not exist: $WaWInstallRoot"
    }

    $resolvedWaWRoot = (Resolve-Path -LiteralPath $WaWInstallRoot).Path
    $retailMain = Join-Path $resolvedWaWRoot 'main'

    if (-not (Test-Path -LiteralPath $retailMain -PathType Container)) {
        throw "PC WaW install is missing its main folder: $retailMain"
    }

    $retailIwds = @(Get-ChildItem -LiteralPath $retailMain -Filter '*.iwd' -File -ErrorAction SilentlyContinue)
    $retailStockIwds = @($retailIwds | Where-Object { $_.Name -match '(?i)^iw_[0-9]+[.]iwd

$pcFastFile = $null
$preferredPcFastFiles = @(
    @(
        (Join-Path $mapDir 'nazi_zombie_frumansion.ff'),
        (Join-Path $mapDir 'nazi_zombie_FRUmansion.ff'),
        (Join-Path $mapDir ($MapName + '.ff'))
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
)

if ($preferredPcFastFiles.Count -gt 0) {
    $pcFastFile = $preferredPcFastFiles[0]
}
else {
    $fallbackPcFastFiles = @(Get-ChildItem -LiteralPath $mapDir -Filter '*.ff' -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -notmatch '(?i)_load[.]ff$' -and
            $_.Name -notmatch '(?i)_patch[.]ff$'
        } |
        Sort-Object Name)
    if ($fallbackPcFastFiles.Count -gt 0) { $pcFastFile = $fallbackPcFastFiles[0].FullName }
}

$pcFastFileDumpRoot = Join-Path $repo '.cache\nuclear_4r_pc_fastfile_images'
$pcFastFileDumpLog = Join-Path $repo '.cache\nuclear_4r_pc_fastfile_images.log'
$pcFastFileDumpCode = -1
$pcFastFileDumpCount = 0
$pcFastFileFirstError = $null

if (-not [string]::IsNullOrWhiteSpace($pcFastFile) -and (Test-Path -LiteralPath $pcFastFile -PathType Leaf)) {
    if (Test-Path -LiteralPath $pcFastFileDumpRoot) { Remove-Item -LiteralPath $pcFastFileDumpRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $pcFastFileDumpRoot | Out-Null
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $pcFastFileDumpLog) | Out-Null

    $pcDumpSearchPaths = New-Object 'System.Collections.Generic.List[string]'
    Add-UniqueDirectory -List $pcDumpSearchPaths -Path $mapDir
    Add-UniqueDirectory -List $pcDumpSearchPaths -Path (Join-Path $mapDir 'images')
    Add-UniqueDirectory -List $pcDumpSearchPaths -Path $localMod
    Add-UniqueDirectory -List $pcDumpSearchPaths -Path $localUsermap
    Add-UniqueDirectory -List $pcDumpSearchPaths -Path $roamingMod
    Add-UniqueDirectory -List $pcDumpSearchPaths -Path $roamingUsermap
    if ($null -ne $resolvedWaWRoot) {
        Add-UniqueDirectory -List $pcDumpSearchPaths -Path $retailMain
        Add-UniqueDirectory -List $pcDumpSearchPaths -Path (Join-Path $resolvedWaWRoot 'zone\English')
        Add-UniqueDirectory -List $pcDumpSearchPaths -Path (Join-Path $resolvedWaWRoot "mods\$MapName")
        Add-UniqueDirectory -List $pcDumpSearchPaths -Path (Join-Path $resolvedWaWRoot "usermaps\$MapName")
    }

    $pcDumpArgs = @(
        '--verbose',
        '--no-color',
        '--include-assets', 'image',
        '--image-format', 'IWI',
        '--output-folder', $pcFastFileDumpRoot
    )
    if ($pcDumpSearchPaths.Count -gt 0) {
        $pcDumpArgs += @('--search-path', (@($pcDumpSearchPaths) -join ';'))
    }
    $pcDumpArgs += $pcFastFile

    Write-Host "[NUCLEAR-4R] Extracting PC-owned image closure from fastfile: $pcFastFile"
    Push-Location (Split-Path -Parent $pcFastFile)
    try {
        $pcFastFileDumpCode = Invoke-NativeTee -FilePath $unlinker -Arguments $pcDumpArgs -LogPath $pcFastFileDumpLog
    }
    finally { Pop-Location }

    $pcDumpIwis = @(Get-ChildItem -LiteralPath $pcFastFileDumpRoot -Filter '*.iwi' -File -Recurse -ErrorAction SilentlyContinue)
    $pcFastFileDumpCount = $pcDumpIwis.Count
    if (Test-Path -LiteralPath $pcFastFileDumpLog -PathType Leaf) {
        $errorLines = @(Get-Content -LiteralPath $pcFastFileDumpLog -ErrorAction SilentlyContinue |
            Where-Object { $_ -match '(?i)ERROR:|Could not find data for image' } |
            Select-Object -First 1)
        if ($errorLines.Count -gt 0) { $pcFastFileFirstError = [string]$errorLines[0] }
    }
    if ($pcFastFileDumpCount -gt 0) {
        Add-SourceRoot -Roots $roots -Path $pcFastFileDumpRoot -Kind 'pc-fastfile-dump' -Priority 5
    }

    Write-Host "[NUCLEAR-4R] PC fastfile dump exit=$pcFastFileDumpCode, IWI files=$pcFastFileDumpCount"
    if ($pcFastFileDumpCode -ne 0) {
        Write-Host '[NUCLEAR-4R] NOTE: partial PC dump preserved; unresolved external/common image data will be reported below.' -ForegroundColor Yellow
    }
}
else {
    Write-Host '[NUCLEAR-4R] WARN: no PC main fastfile was identified for direct image extraction.' -ForegroundColor Yellow
}

$candidates = New-Object 'System.Collections.Generic.List[object]'
$seenLoose = @{}
$seenIwd = @{}

foreach ($root in @($roots | Sort-Object Priority, Path)) {
    Write-Host ("[NUCLEAR-4R] Scanning [{0}] {1}" -f $root.Kind, $root.Path)

    foreach ($extension in @('*.iwi', '*.dds')) {
        $loose = @()
        try { $loose = @(Get-ChildItem -LiteralPath $root.Path -Filter $extension -File -Recurse -ErrorAction SilentlyContinue) } catch {}
        foreach ($file in $loose) {
            $full = $file.FullName
            if ($seenLoose.ContainsKey($full.ToLowerInvariant())) { continue }
            $seenLoose[$full.ToLowerInvariant()] = $true
            $relative = Get-RelativePathCompat -BasePath $root.Path -TargetPath $full
            $identity = Normalize-SourceIdentity -Value $relative
            Add-Candidate -Candidates $candidates -Identity $identity -Extension $file.Extension -SourceKind ($root.Kind + '-loose') -SourceRoot $root.Path -SourcePath $full -ArchiveEntry '' -Priority ([int]$root.Priority) -Bytes ([int64]$file.Length)
        }
    }

    $iwds = @()
    try { $iwds = @(Get-ChildItem -LiteralPath $root.Path -Filter '*.iwd' -File -Recurse -ErrorAction SilentlyContinue) } catch {}
    foreach ($iwd in $iwds) {
        $iwdKey = $iwd.FullName.ToLowerInvariant()
        if ($seenIwd.ContainsKey($iwdKey)) { continue }
        $seenIwd[$iwdKey] = $true

        try {
            $zip = [IO.Compression.ZipFile]::OpenRead($iwd.FullName)
            try {
                foreach ($entry in $zip.Entries) {
                    $ext = [IO.Path]::GetExtension($entry.FullName)
                    if ($ext -ine '.iwi' -and $ext -ine '.dds') { continue }
                    $identity = Normalize-SourceIdentity -Value $entry.FullName
                    Add-Candidate -Candidates $candidates -Identity $identity -Extension $ext -SourceKind ($root.Kind + '-iwd') -SourceRoot $root.Path -SourcePath $iwd.FullName -ArchiveEntry $entry.FullName -Priority ([int]$root.Priority + 1) -Bytes ([int64]$entry.Length)
                }
            }
            finally { $zip.Dispose() }
        }
        catch {
            Write-Host "[NUCLEAR-4R] WARN: could not read IWD '$($iwd.FullName)': $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
}

$candidateArray = [object[]]$candidates.ToArray()
$byIdentity = @{}
foreach ($candidate in $candidateArray) {
    $key = ([string]$candidate.Identity).ToLowerInvariant()
    if (-not $byIdentity.ContainsKey($key)) { $byIdentity[$key] = New-Object 'System.Collections.Generic.List[object]' }
    $byIdentity[$key].Add($candidate)
}

$resolved = New-Object 'System.Collections.Generic.List[object]'
$missing = New-Object 'System.Collections.Generic.List[object]'
$nonPortable = New-Object 'System.Collections.Generic.List[object]'

foreach ($native in $nativeImages) {
    if (-not (Has-PortableAssetPath -AssetName $native.Name)) {
        $nonPortable.Add([pscustomobject]@{ Index = $native.Index; Name = $native.Name; Reason = 'native identity cannot be represented as a Windows source filename' })
        continue
    }

    $keys = @($native.Name.ToLowerInvariant(), $native.Normalized.ToLowerInvariant()) | Select-Object -Unique
    $choices = New-Object 'System.Collections.Generic.List[object]'
    foreach ($key in $keys) {
        if ($byIdentity.ContainsKey($key)) {
            foreach ($item in $byIdentity[$key]) { $choices.Add($item) }
        }
    }

    $choice = @(
        $choices |
        Sort-Object Priority, @{ Expression = { if ([string]$_.Extension -ieq '.iwi') { 0 } else { 1 } } }, @{ Expression = { -[int64]$_.Bytes } }, SourcePath, ArchiveEntry |
        Select-Object -First 1
    )

    if ($choice.Count -eq 0) {
        $missing.Add([pscustomobject]@{ Index = $native.Index; Name = $native.Name; Normalized = $native.Normalized })
        continue
    }

    $selected = $choice[0]
    $resolved.Add([pscustomobject]@{
        Index = $native.Index
        Name = $native.Name
        Normalized = $native.Normalized
        Extension = [string]$selected.Extension
        SourceKind = [string]$selected.SourceKind
        SourceRoot = [string]$selected.SourceRoot
        SourcePath = [string]$selected.SourcePath
        ArchiveEntry = [string]$selected.ArchiveEntry
        SourceIdentity = [string]$selected.Identity
        SourceBytes = [int64]$selected.Bytes
        Priority = [int]$selected.Priority
    })
}

$resolvedArray = [object[]]$resolved.ToArray()
$missingArray = [object[]]$missing.ToArray()
$nonPortableArray = [object[]]$nonPortable.ToArray()

if (Test-Path -LiteralPath $OutputRoot) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }

$work = Join-Path $OutputRoot 'workspace'
$rawImages = Join-Path $work 'raw\images'
$zoneSource = Join-Path $work 'zone_source'
$linkOut = Join-Path $OutputRoot 'linked'
$graphOut = Join-Path $OutputRoot 'graph_dump'
$linkLog = Join-Path $OutputRoot 'oat_linker.log'
$unlinkLog = Join-Path $OutputRoot 'oat_graph_unlinker.log'
$project = 'nuclear_4r_pc_owned_images'
$zoneFile = Join-Path $zoneSource ($project + '.zone')

New-Item -ItemType Directory -Force -Path $rawImages | Out-Null
New-Item -ItemType Directory -Force -Path $zoneSource | Out-Null
New-Item -ItemType Directory -Force -Path $linkOut | Out-Null
New-Item -ItemType Directory -Force -Path $graphOut | Out-Null

$zoneLines = New-Object 'System.Collections.Generic.List[string]'
$zoneLines.Add('>game,T4')
$zoneLines.Add('>platform,xbox360')
$zoneLines.Add(('>name,{0}' -f $project))
$zoneLines.Add('')

$archiveHandles = @{}
try {
    foreach ($item in ($resolvedArray | Sort-Object Index)) {
        $assetRelative = $item.Name.Replace('/', '\') + $item.Extension
        $destination = Join-Path $rawImages $assetRelative
        $destinationDir = Split-Path -Parent $destination
        if (-not [string]::IsNullOrWhiteSpace($destinationDir)) { New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null }

        if ([string]::IsNullOrWhiteSpace($item.ArchiveEntry)) {
            Copy-Item -LiteralPath $item.SourcePath -Destination $destination -Force
        }
        else {
            $archiveKey = $item.SourcePath.ToLowerInvariant()
            if (-not $archiveHandles.ContainsKey($archiveKey)) { $archiveHandles[$archiveKey] = [IO.Compression.ZipFile]::OpenRead($item.SourcePath) }
            $zip = $archiveHandles[$archiveKey]
            $entry = $zip.GetEntry($item.ArchiveEntry)
            if ($null -eq $entry) { throw "IWD entry disappeared: $($item.SourcePath) :: $($item.ArchiveEntry)" }
            $src = $entry.Open()
            $dst = [IO.File]::Create($destination)
            try { $src.CopyTo($dst) }
            finally { $dst.Dispose(); $src.Dispose() }
        }

        $zoneLines.Add(('image,"{0}"' -f $item.Name.Replace('"', '')))
    }
}
finally {
    foreach ($zip in $archiveHandles.Values) { try { $zip.Dispose() } catch {} }
}

[IO.File]::WriteAllLines($zoneFile, $zoneLines, [Text.UTF8Encoding]::new($false))
$coverage = if ($nativeImages.Count -eq 0) { 0.0 } else { $resolvedArray.Count / [double]$nativeImages.Count }

Write-Host ''
Write-Host '[NUCLEAR-4R] ================================================================'
Write-Host '[NUCLEAR-4R] PC-OWNED IMAGE SOURCE -> T4 XENON GRAPH BRIDGE'
Write-Host '[NUCLEAR-4R] ================================================================'
Write-Host "[NUCLEAR-4R] Native image roots : $($nativeImages.Count)"
Write-Host "[NUCLEAR-4R] Source candidates  : $($candidateArray.Count)"
Write-Host "[NUCLEAR-4R] Resolved sources   : $($resolvedArray.Count)"
Write-Host "[NUCLEAR-4R] Missing sources    : $($missingArray.Count)"
Write-Host "[NUCLEAR-4R] Non-portable names : $($nonPortableArray.Count)"
Write-Host ("[NUCLEAR-4R] Identity coverage  : {0:P2}" -f $coverage)
Write-Host "[NUCLEAR-4R] PC WaW root        : $(if ($null -eq $resolvedWaWRoot) { '<not resolved>' } else { $resolvedWaWRoot })"
Write-Host "[NUCLEAR-4R] Retail main IWDs    : $retailIwdCount"
Write-Host "[NUCLEAR-4R] Stock iw_*.iwd      : $retailStockIwdCount"
Write-Host "[NUCLEAR-4R] PC fastfile        : $(if ([string]::IsNullOrWhiteSpace($pcFastFile)) { '<not resolved>' } else { $pcFastFile })"
Write-Host "[NUCLEAR-4R] PC FF dump IWIs    : $pcFastFileDumpCount (exit $pcFastFileDumpCode)"
if (-not [string]::IsNullOrWhiteSpace($pcFastFileFirstError)) {
    Write-Host "[NUCLEAR-4R] PC FF first error   : $pcFastFileFirstError"
}
Write-Host ''
Write-Host '[NUCLEAR-4R] SOURCE POLICY:'
Write-Host '  - input pixels come only from PC-side loose IWI/DDS, IWD entries, or IWI dumped from the PC fastfile'
Write-Host '  - PC fastfile extraction is provenance-safe: the Xbox oracle contributes no image payload bytes'
Write-Host '  - native Xbox data is used only for root identity/order'
Write-Host '  - no Stage 4J/4L/4M/4N native image payload is accepted as source'
Write-Host ''

$linkedFastFile = $null
$linkCode = 0
$unlinkCode = 0
$cardMemoryBytes = [int64]0
$graphErrors = New-Object 'System.Collections.Generic.List[string]'

if ($resolvedArray.Count -gt 0) {
    $linkArgs = @('--verbose','--no-color','--base-folder',$work,'--source-search-path',$zoneSource,'--asset-search-path',(Join-Path $work 'raw'),'--output-folder',$linkOut,$project)
    Push-Location $work
    try { $linkCode = Invoke-NativeTee -FilePath $linker -Arguments $linkArgs -LogPath $linkLog }
    finally { Pop-Location }

    if ($linkCode -eq 0) {
        $linked = @(Get-ChildItem -LiteralPath $linkOut -Filter '*.ff' -File -Recurse)
        if ($linked.Count -ne 1) {
            $graphErrors.Add("Expected one linked image-only FF; found $($linked.Count).")
        }
        else {
            $linkedFastFile = $linked[0].FullName
            $unlinkArgs = @('--verbose','--no-color','--include-assets','image','--image-format','IWI','--output-folder',$graphOut,$linkedFastFile)
            Push-Location (Split-Path -Parent $linkedFastFile)
            try { $unlinkCode = Invoke-NativeTee -FilePath $unlinker -Arguments $unlinkArgs -LogPath $unlinkLog }
            finally { Pop-Location }
        }
    }
}

$graphDir = Join-Path $graphOut 'xenon_image_graphs'
$pixelDir = Join-Path $graphOut 'xenon_image_pixels'
$headerDir = Join-Path $graphOut 'xenon_image_headers'

$graphFiles = @()
$pixelFiles = @()
$headerFiles = @()
if (Test-Path -LiteralPath $graphDir -PathType Container) { $graphFiles = @(Get-ChildItem -LiteralPath $graphDir -Filter '*.json' -File -Recurse) }
if (Test-Path -LiteralPath $pixelDir -PathType Container) { $pixelFiles = @(Get-ChildItem -LiteralPath $pixelDir -Filter '*.pixels.bin' -File -Recurse) }
if (Test-Path -LiteralPath $headerDir -PathType Container) { $headerFiles = @(Get-ChildItem -LiteralPath $headerDir -Filter '*.d3dtexture.bin' -File -Recurse) }

$graphCount = $graphFiles.Count
$pixelCount = $pixelFiles.Count
$headerCount = $headerFiles.Count

$graphNames = @{}
foreach ($file in $graphFiles) {
    try {
        $g = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        if ([string]$g.format -ne 'WaW360.T4Xenon.ImageGraph.v1') {
            $graphErrors.Add("$($file.Name): unexpected graph format '$($g.format)'.")
            continue
        }
        $name = [string]$g.name
        if ([string]::IsNullOrWhiteSpace($name)) {
            $graphErrors.Add("$($file.Name): empty image name.")
            continue
        }
        $graphNames[$name.ToLowerInvariant()] = $true
        $cardMemoryBytes += [int64]$g.cardMemoryBytes
    }
    catch {
        $graphErrors.Add("$($file.Name): graph parse failed: $($_.Exception.Message)")
    }
}

foreach ($item in $resolvedArray) {
    if (-not $graphNames.ContainsKey($item.Name.ToLowerInvariant())) {
        $graphErrors.Add("Missing output graph for PC source-backed image '$($item.Name)'.")
    }
}

if ($graphCount -ne $resolvedArray.Count) { $graphErrors.Add("Graph count $graphCount != resolved source count $($resolvedArray.Count).") }
if ($pixelCount -ne $resolvedArray.Count) { $graphErrors.Add("Pixel blob count $pixelCount != resolved source count $($resolvedArray.Count).") }
if ($headerCount -ne $resolvedArray.Count) { $graphErrors.Add("Texture header count $headerCount != resolved source count $($resolvedArray.Count).") }

$bridgePass = $resolvedArray.Count -gt 0 -and $linkCode -eq 0 -and $unlinkCode -eq 0 -and $graphErrors.Count -eq 0
$fullOrdinaryClosure = $bridgePass -and ($resolvedArray.Count + $nonPortableArray.Count -eq $nativeImages.Count) -and $missingArray.Count -eq 0

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4R.PcOwnedImageBridge.v1'
    mapName = $MapName
    nativeIdentityManifest = $NativeIdentityManifest
    nativeImageRoots = $nativeImages.Count
    sourceRoots = [object[]]$roots.ToArray()
    pcWaWInstallRoot = $resolvedWaWRoot
    retailMain = $retailMain
    retailIwdCount = $retailIwdCount
    retailStockIwdCount = $retailStockIwdCount
    pcFastFile = $pcFastFile
    pcFastFileDumpRoot = $pcFastFileDumpRoot
    pcFastFileDumpExitCode = $pcFastFileDumpCode
    pcFastFileDumpIwis = $pcFastFileDumpCount
    pcFastFileFirstError = $pcFastFileFirstError
    sourceCandidates = $candidateArray.Count
    resolvedSources = $resolvedArray.Count
    missingSources = $missingArray.Count
    nonPortableNativeNames = $nonPortableArray.Count
    nativeIdentityCoverage = $coverage
    linkedFastFile = $linkedFastFile
    oatLinkExitCode = $linkCode
    oatUnlinkExitCode = $unlinkCode
    graphRoot = $graphOut
    imageGraphs = $graphCount
    pixelBlobs = $pixelCount
    textureHeaders = $headerCount
    xenonCardMemoryBytes = $cardMemoryBytes
    bridgePass = $bridgePass
    fullOrdinaryClosure = $fullOrdinaryClosure
    productionSourcePolicy = @(
        'Pixel payload provenance is PC-side loose IWI/DDS, IWD, or IWI extracted from the PC fastfile only.',
        'PC fastfile extraction may use legitimate PC retail IWDs as dependency search paths.',
        'Native Xbox oracle contributes identity/order only.',
        'Stage 4J/4L/4M/4N native-derived payloads are prohibited as production inputs.'
    )
    resolved = $resolvedArray
    missing = $missingArray
    nonPortable = $nonPortableArray
    errors = [object[]]$graphErrors.ToArray()
}

$manifestPath = Join-Path $OutputRoot 'nuclear_4r_pc_image_bridge.json'
$manifest | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host ''
Write-Host '[NUCLEAR-4R] ================= PRODUCTION IMAGE BRIDGE RESULT ================'
Write-Host "[NUCLEAR-4R] Resolved PC images : $($resolvedArray.Count) / $($nativeImages.Count)"
Write-Host "[NUCLEAR-4R] Missing ordinary    : $($missingArray.Count)"
Write-Host "[NUCLEAR-4R] Non-portable/native: $($nonPortableArray.Count)"
Write-Host "[NUCLEAR-4R] Image graphs        : $graphCount"
Write-Host "[NUCLEAR-4R] Pixel blobs         : $pixelCount"
Write-Host "[NUCLEAR-4R] Texture headers     : $headerCount"
Write-Host "[NUCLEAR-4R] Xenon cardMemory    : $cardMemoryBytes bytes"
Write-Host "[NUCLEAR-4R] Bridge pass         : $bridgePass"
Write-Host "[NUCLEAR-4R] Full ordinary close : $fullOrdinaryClosure"
Write-Host "[NUCLEAR-4R] Manifest            : $manifestPath"
Write-Host '[NUCLEAR-4R] ================================================================='

if ($missingArray.Count -gt 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4R] First missing PC image sources:'
    $missingArray | Select-Object -First 40 | ForEach-Object { Write-Host ("  root #{0,4}: {1}" -f $_.Index, $_.Name) }
}

if ($nonPortableArray.Count -gt 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4R] Native generated/function identities requiring synthetic handling:'
    $nonPortableArray | Select-Object -First 20 | ForEach-Object { Write-Host ("  root #{0,4}: {1}" -f $_.Index, $_.Name) }
}

if ($graphErrors.Count -gt 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4R] Graph errors:'
    foreach ($e in @($graphErrors | Select-Object -First 40)) { Write-Host "  $e" }
}

if (-not $bridgePass) {
    Write-Host ''
    Write-Host '[NUCLEAR-4R] PC-OWNED IMAGE BRIDGE: BLOCKED' -ForegroundColor Yellow
    if ($resolvedArray.Count -eq 0) {
        Write-Host '[NUCLEAR-4R] No legitimate PC IWI/DDS source matched the native image identities.'
        if ($pcFastFileDumpCount -eq 0) {
            Write-Host '[NUCLEAR-4R] Direct PC fastfile extraction produced no usable IWI source images.'
            if ($pcFastFileDumpCode -ne 0 -and (Test-Path -LiteralPath $pcFastFileDumpLog -PathType Leaf)) {
                Write-Host "[NUCLEAR-4R] PC fastfile dump log: $pcFastFileDumpLog"
            }
        }
        if ($null -eq $resolvedWaWRoot) {
            Write-Host '[NUCLEAR-4R] A PC WaW install was not resolved; stock/common images therefore cannot be sourced yet.'
            Write-Host "[NUCLEAR-4R] Override discovery with: -WaWInstallRoot '<your PC Call of Duty World at War folder>'"
        }
    }
    exit 4
}

Write-Host ''
Write-Host '[NUCLEAR-4R] PC-OWNED IMAGE BRIDGE: PASS' -ForegroundColor Green
if ($fullOrdinaryClosure) {
    Write-Host '[NUCLEAR-4R] All portable native image identities have legitimate PC-side source payloads.'
}
else {
    Write-Host '[NUCLEAR-4R] Partial legitimate source closure is proven; unresolved identities remain fenced.'
}
Write-Host '[NUCLEAR-4R] Next: graft this PC-backed graph into the 6,811-root emitter and credit only these images in 4F.'
 })
    $retailIwdCount = $retailIwds.Count
    $retailStockIwdCount = $retailStockIwds.Count

    if ($retailIwdCount -eq 0) {
        throw "PC WaW main folder contains no .iwd archives: $retailMain"
    }

    Write-Host "[NUCLEAR-4R] Retail PC WaW root : $resolvedWaWRoot"
    Write-Host "[NUCLEAR-4R] Retail main IWDs    : $retailIwdCount"
    Write-Host "[NUCLEAR-4R] Stock iw_*.iwd      : $retailStockIwdCount"

    Add-SourceRoot -Roots $roots -Path $resolvedWaWRoot -Kind 'retail-root' -Priority 50
    Add-SourceRoot -Roots $roots -Path $retailMain -Kind 'retail-main' -Priority 45
    Add-SourceRoot -Roots $roots -Path (Join-Path $resolvedWaWRoot 'raw') -Kind 'retail-raw' -Priority 40
    Add-SourceRoot -Roots $roots -Path (Join-Path $resolvedWaWRoot "mods\$MapName") -Kind 'retail-mod' -Priority 30
    Add-SourceRoot -Roots $roots -Path (Join-Path $resolvedWaWRoot "usermaps\$MapName") -Kind 'retail-usermap' -Priority 30
}

$pcFastFile = $null
$preferredPcFastFiles = @(
    @(
        (Join-Path $mapDir 'nazi_zombie_frumansion.ff'),
        (Join-Path $mapDir 'nazi_zombie_FRUmansion.ff'),
        (Join-Path $mapDir ($MapName + '.ff'))
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
)

if ($preferredPcFastFiles.Count -gt 0) {
    $pcFastFile = $preferredPcFastFiles[0]
}
else {
    $fallbackPcFastFiles = @(Get-ChildItem -LiteralPath $mapDir -Filter '*.ff' -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -notmatch '(?i)_load[.]ff$' -and
            $_.Name -notmatch '(?i)_patch[.]ff$'
        } |
        Sort-Object Name)
    if ($fallbackPcFastFiles.Count -gt 0) { $pcFastFile = $fallbackPcFastFiles[0].FullName }
}

$pcFastFileDumpRoot = Join-Path $repo '.cache\nuclear_4r_pc_fastfile_images'
$pcFastFileDumpLog = Join-Path $repo '.cache\nuclear_4r_pc_fastfile_images.log'
$pcFastFileDumpCode = -1
$pcFastFileDumpCount = 0
$pcFastFileFirstError = $null

if (-not [string]::IsNullOrWhiteSpace($pcFastFile) -and (Test-Path -LiteralPath $pcFastFile -PathType Leaf)) {
    if (Test-Path -LiteralPath $pcFastFileDumpRoot) { Remove-Item -LiteralPath $pcFastFileDumpRoot -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $pcFastFileDumpRoot | Out-Null
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $pcFastFileDumpLog) | Out-Null

    $pcDumpSearchPaths = New-Object 'System.Collections.Generic.List[string]'
    Add-UniqueDirectory -List $pcDumpSearchPaths -Path $mapDir
    Add-UniqueDirectory -List $pcDumpSearchPaths -Path (Join-Path $mapDir 'images')
    Add-UniqueDirectory -List $pcDumpSearchPaths -Path $localMod
    Add-UniqueDirectory -List $pcDumpSearchPaths -Path $localUsermap
    Add-UniqueDirectory -List $pcDumpSearchPaths -Path $roamingMod
    Add-UniqueDirectory -List $pcDumpSearchPaths -Path $roamingUsermap
    if ($null -ne $resolvedWaWRoot) {
        Add-UniqueDirectory -List $pcDumpSearchPaths -Path (Join-Path $resolvedWaWRoot 'main')
        Add-UniqueDirectory -List $pcDumpSearchPaths -Path (Join-Path $resolvedWaWRoot 'zone\English')
    }

    $pcDumpArgs = @(
        '--verbose',
        '--no-color',
        '--include-assets', 'image',
        '--image-format', 'IWI',
        '--output-folder', $pcFastFileDumpRoot
    )
    if ($pcDumpSearchPaths.Count -gt 0) {
        $pcDumpArgs += @('--search-path', (@($pcDumpSearchPaths) -join ';'))
    }
    $pcDumpArgs += $pcFastFile

    Write-Host "[NUCLEAR-4R] Extracting PC-owned image closure from fastfile: $pcFastFile"
    Push-Location (Split-Path -Parent $pcFastFile)
    try {
        $pcFastFileDumpCode = Invoke-NativeTee -FilePath $unlinker -Arguments $pcDumpArgs -LogPath $pcFastFileDumpLog
    }
    finally { Pop-Location }

    $pcDumpIwis = @(Get-ChildItem -LiteralPath $pcFastFileDumpRoot -Filter '*.iwi' -File -Recurse -ErrorAction SilentlyContinue)
    $pcFastFileDumpCount = $pcDumpIwis.Count
    if (Test-Path -LiteralPath $pcFastFileDumpLog -PathType Leaf) {
        $errorLines = @(Get-Content -LiteralPath $pcFastFileDumpLog -ErrorAction SilentlyContinue |
            Where-Object { $_ -match '(?i)ERROR:|Could not find data for image' } |
            Select-Object -First 1)
        if ($errorLines.Count -gt 0) { $pcFastFileFirstError = [string]$errorLines[0] }
    }
    if ($pcFastFileDumpCount -gt 0) {
        Add-SourceRoot -Roots $roots -Path $pcFastFileDumpRoot -Kind 'pc-fastfile-dump' -Priority 5
    }

    Write-Host "[NUCLEAR-4R] PC fastfile dump exit=$pcFastFileDumpCode, IWI files=$pcFastFileDumpCount"
    if ($pcFastFileDumpCode -ne 0) {
        Write-Host '[NUCLEAR-4R] NOTE: partial PC dump preserved; unresolved external/common image data will be reported below.' -ForegroundColor Yellow
    }
}
else {
    Write-Host '[NUCLEAR-4R] WARN: no PC main fastfile was identified for direct image extraction.' -ForegroundColor Yellow
}

$candidates = New-Object 'System.Collections.Generic.List[object]'
$seenLoose = @{}
$seenIwd = @{}

foreach ($root in @($roots | Sort-Object Priority, Path)) {
    Write-Host ("[NUCLEAR-4R] Scanning [{0}] {1}" -f $root.Kind, $root.Path)

    foreach ($extension in @('*.iwi', '*.dds')) {
        $loose = @()
        try { $loose = @(Get-ChildItem -LiteralPath $root.Path -Filter $extension -File -Recurse -ErrorAction SilentlyContinue) } catch {}
        foreach ($file in $loose) {
            $full = $file.FullName
            if ($seenLoose.ContainsKey($full.ToLowerInvariant())) { continue }
            $seenLoose[$full.ToLowerInvariant()] = $true
            $relative = Get-RelativePathCompat -BasePath $root.Path -TargetPath $full
            $identity = Normalize-SourceIdentity -Value $relative
            Add-Candidate -Candidates $candidates -Identity $identity -Extension $file.Extension -SourceKind ($root.Kind + '-loose') -SourceRoot $root.Path -SourcePath $full -ArchiveEntry '' -Priority ([int]$root.Priority) -Bytes ([int64]$file.Length)
        }
    }

    $iwds = @()
    try { $iwds = @(Get-ChildItem -LiteralPath $root.Path -Filter '*.iwd' -File -Recurse -ErrorAction SilentlyContinue) } catch {}
    foreach ($iwd in $iwds) {
        $iwdKey = $iwd.FullName.ToLowerInvariant()
        if ($seenIwd.ContainsKey($iwdKey)) { continue }
        $seenIwd[$iwdKey] = $true

        try {
            $zip = [IO.Compression.ZipFile]::OpenRead($iwd.FullName)
            try {
                foreach ($entry in $zip.Entries) {
                    $ext = [IO.Path]::GetExtension($entry.FullName)
                    if ($ext -ine '.iwi' -and $ext -ine '.dds') { continue }
                    $identity = Normalize-SourceIdentity -Value $entry.FullName
                    Add-Candidate -Candidates $candidates -Identity $identity -Extension $ext -SourceKind ($root.Kind + '-iwd') -SourceRoot $root.Path -SourcePath $iwd.FullName -ArchiveEntry $entry.FullName -Priority ([int]$root.Priority + 1) -Bytes ([int64]$entry.Length)
                }
            }
            finally { $zip.Dispose() }
        }
        catch {
            Write-Host "[NUCLEAR-4R] WARN: could not read IWD '$($iwd.FullName)': $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
}

$candidateArray = [object[]]$candidates.ToArray()
$byIdentity = @{}
foreach ($candidate in $candidateArray) {
    $key = ([string]$candidate.Identity).ToLowerInvariant()
    if (-not $byIdentity.ContainsKey($key)) { $byIdentity[$key] = New-Object 'System.Collections.Generic.List[object]' }
    $byIdentity[$key].Add($candidate)
}

$resolved = New-Object 'System.Collections.Generic.List[object]'
$missing = New-Object 'System.Collections.Generic.List[object]'
$nonPortable = New-Object 'System.Collections.Generic.List[object]'

foreach ($native in $nativeImages) {
    if (-not (Has-PortableAssetPath -AssetName $native.Name)) {
        $nonPortable.Add([pscustomobject]@{ Index = $native.Index; Name = $native.Name; Reason = 'native identity cannot be represented as a Windows source filename' })
        continue
    }

    $keys = @($native.Name.ToLowerInvariant(), $native.Normalized.ToLowerInvariant()) | Select-Object -Unique
    $choices = New-Object 'System.Collections.Generic.List[object]'
    foreach ($key in $keys) {
        if ($byIdentity.ContainsKey($key)) {
            foreach ($item in $byIdentity[$key]) { $choices.Add($item) }
        }
    }

    $choice = @(
        $choices |
        Sort-Object Priority, @{ Expression = { if ([string]$_.Extension -ieq '.iwi') { 0 } else { 1 } } }, @{ Expression = { -[int64]$_.Bytes } }, SourcePath, ArchiveEntry |
        Select-Object -First 1
    )

    if ($choice.Count -eq 0) {
        $missing.Add([pscustomobject]@{ Index = $native.Index; Name = $native.Name; Normalized = $native.Normalized })
        continue
    }

    $selected = $choice[0]
    $resolved.Add([pscustomobject]@{
        Index = $native.Index
        Name = $native.Name
        Normalized = $native.Normalized
        Extension = [string]$selected.Extension
        SourceKind = [string]$selected.SourceKind
        SourceRoot = [string]$selected.SourceRoot
        SourcePath = [string]$selected.SourcePath
        ArchiveEntry = [string]$selected.ArchiveEntry
        SourceIdentity = [string]$selected.Identity
        SourceBytes = [int64]$selected.Bytes
        Priority = [int]$selected.Priority
    })
}

$resolvedArray = [object[]]$resolved.ToArray()
$missingArray = [object[]]$missing.ToArray()
$nonPortableArray = [object[]]$nonPortable.ToArray()

if (Test-Path -LiteralPath $OutputRoot) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }

$work = Join-Path $OutputRoot 'workspace'
$rawImages = Join-Path $work 'raw\images'
$zoneSource = Join-Path $work 'zone_source'
$linkOut = Join-Path $OutputRoot 'linked'
$graphOut = Join-Path $OutputRoot 'graph_dump'
$linkLog = Join-Path $OutputRoot 'oat_linker.log'
$unlinkLog = Join-Path $OutputRoot 'oat_graph_unlinker.log'
$project = 'nuclear_4r_pc_owned_images'
$zoneFile = Join-Path $zoneSource ($project + '.zone')

New-Item -ItemType Directory -Force -Path $rawImages | Out-Null
New-Item -ItemType Directory -Force -Path $zoneSource | Out-Null
New-Item -ItemType Directory -Force -Path $linkOut | Out-Null
New-Item -ItemType Directory -Force -Path $graphOut | Out-Null

$zoneLines = New-Object 'System.Collections.Generic.List[string]'
$zoneLines.Add('>game,T4')
$zoneLines.Add('>platform,xbox360')
$zoneLines.Add(('>name,{0}' -f $project))
$zoneLines.Add('')

$archiveHandles = @{}
try {
    foreach ($item in ($resolvedArray | Sort-Object Index)) {
        $assetRelative = $item.Name.Replace('/', '\') + $item.Extension
        $destination = Join-Path $rawImages $assetRelative
        $destinationDir = Split-Path -Parent $destination
        if (-not [string]::IsNullOrWhiteSpace($destinationDir)) { New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null }

        if ([string]::IsNullOrWhiteSpace($item.ArchiveEntry)) {
            Copy-Item -LiteralPath $item.SourcePath -Destination $destination -Force
        }
        else {
            $archiveKey = $item.SourcePath.ToLowerInvariant()
            if (-not $archiveHandles.ContainsKey($archiveKey)) { $archiveHandles[$archiveKey] = [IO.Compression.ZipFile]::OpenRead($item.SourcePath) }
            $zip = $archiveHandles[$archiveKey]
            $entry = $zip.GetEntry($item.ArchiveEntry)
            if ($null -eq $entry) { throw "IWD entry disappeared: $($item.SourcePath) :: $($item.ArchiveEntry)" }
            $src = $entry.Open()
            $dst = [IO.File]::Create($destination)
            try { $src.CopyTo($dst) }
            finally { $dst.Dispose(); $src.Dispose() }
        }

        $zoneLines.Add(('image,"{0}"' -f $item.Name.Replace('"', '')))
    }
}
finally {
    foreach ($zip in $archiveHandles.Values) { try { $zip.Dispose() } catch {} }
}

[IO.File]::WriteAllLines($zoneFile, $zoneLines, [Text.UTF8Encoding]::new($false))
$coverage = if ($nativeImages.Count -eq 0) { 0.0 } else { $resolvedArray.Count / [double]$nativeImages.Count }

Write-Host ''
Write-Host '[NUCLEAR-4R] ================================================================'
Write-Host '[NUCLEAR-4R] PC-OWNED IMAGE SOURCE -> T4 XENON GRAPH BRIDGE'
Write-Host '[NUCLEAR-4R] ================================================================'
Write-Host "[NUCLEAR-4R] Native image roots : $($nativeImages.Count)"
Write-Host "[NUCLEAR-4R] Source candidates  : $($candidateArray.Count)"
Write-Host "[NUCLEAR-4R] Resolved sources   : $($resolvedArray.Count)"
Write-Host "[NUCLEAR-4R] Missing sources    : $($missingArray.Count)"
Write-Host "[NUCLEAR-4R] Non-portable names : $($nonPortableArray.Count)"
Write-Host ("[NUCLEAR-4R] Identity coverage  : {0:P2}" -f $coverage)
Write-Host "[NUCLEAR-4R] PC WaW root        : $(if ($null -eq $resolvedWaWRoot) { '<not resolved>' } else { $resolvedWaWRoot })"
Write-Host "[NUCLEAR-4R] PC fastfile        : $(if ([string]::IsNullOrWhiteSpace($pcFastFile)) { '<not resolved>' } else { $pcFastFile })"
Write-Host "[NUCLEAR-4R] PC FF dump IWIs    : $pcFastFileDumpCount (exit $pcFastFileDumpCode)"
if (-not [string]::IsNullOrWhiteSpace($pcFastFileFirstError)) {
    Write-Host "[NUCLEAR-4R] PC FF first error   : $pcFastFileFirstError"
}
Write-Host ''
Write-Host '[NUCLEAR-4R] SOURCE POLICY:'
Write-Host '  - input pixels come only from PC-side loose IWI/DDS, IWD entries, or IWI dumped from the PC fastfile'
Write-Host '  - PC fastfile extraction is provenance-safe: the Xbox oracle contributes no image payload bytes'
Write-Host '  - native Xbox data is used only for root identity/order'
Write-Host '  - no Stage 4J/4L/4M/4N native image payload is accepted as source'
Write-Host ''

$linkedFastFile = $null
$linkCode = 0
$unlinkCode = 0
$cardMemoryBytes = [int64]0
$graphErrors = New-Object 'System.Collections.Generic.List[string]'

if ($resolvedArray.Count -gt 0) {
    $linkArgs = @('--verbose','--no-color','--base-folder',$work,'--source-search-path',$zoneSource,'--asset-search-path',(Join-Path $work 'raw'),'--output-folder',$linkOut,$project)
    Push-Location $work
    try { $linkCode = Invoke-NativeTee -FilePath $linker -Arguments $linkArgs -LogPath $linkLog }
    finally { Pop-Location }

    if ($linkCode -eq 0) {
        $linked = @(Get-ChildItem -LiteralPath $linkOut -Filter '*.ff' -File -Recurse)
        if ($linked.Count -ne 1) {
            $graphErrors.Add("Expected one linked image-only FF; found $($linked.Count).")
        }
        else {
            $linkedFastFile = $linked[0].FullName
            $unlinkArgs = @('--verbose','--no-color','--include-assets','image','--image-format','IWI','--output-folder',$graphOut,$linkedFastFile)
            Push-Location (Split-Path -Parent $linkedFastFile)
            try { $unlinkCode = Invoke-NativeTee -FilePath $unlinker -Arguments $unlinkArgs -LogPath $unlinkLog }
            finally { Pop-Location }
        }
    }
}

$graphDir = Join-Path $graphOut 'xenon_image_graphs'
$pixelDir = Join-Path $graphOut 'xenon_image_pixels'
$headerDir = Join-Path $graphOut 'xenon_image_headers'

$graphFiles = @()
$pixelFiles = @()
$headerFiles = @()
if (Test-Path -LiteralPath $graphDir -PathType Container) { $graphFiles = @(Get-ChildItem -LiteralPath $graphDir -Filter '*.json' -File -Recurse) }
if (Test-Path -LiteralPath $pixelDir -PathType Container) { $pixelFiles = @(Get-ChildItem -LiteralPath $pixelDir -Filter '*.pixels.bin' -File -Recurse) }
if (Test-Path -LiteralPath $headerDir -PathType Container) { $headerFiles = @(Get-ChildItem -LiteralPath $headerDir -Filter '*.d3dtexture.bin' -File -Recurse) }

$graphCount = $graphFiles.Count
$pixelCount = $pixelFiles.Count
$headerCount = $headerFiles.Count

$graphNames = @{}
foreach ($file in $graphFiles) {
    try {
        $g = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        if ([string]$g.format -ne 'WaW360.T4Xenon.ImageGraph.v1') {
            $graphErrors.Add("$($file.Name): unexpected graph format '$($g.format)'.")
            continue
        }
        $name = [string]$g.name
        if ([string]::IsNullOrWhiteSpace($name)) {
            $graphErrors.Add("$($file.Name): empty image name.")
            continue
        }
        $graphNames[$name.ToLowerInvariant()] = $true
        $cardMemoryBytes += [int64]$g.cardMemoryBytes
    }
    catch {
        $graphErrors.Add("$($file.Name): graph parse failed: $($_.Exception.Message)")
    }
}

foreach ($item in $resolvedArray) {
    if (-not $graphNames.ContainsKey($item.Name.ToLowerInvariant())) {
        $graphErrors.Add("Missing output graph for PC source-backed image '$($item.Name)'.")
    }
}

if ($graphCount -ne $resolvedArray.Count) { $graphErrors.Add("Graph count $graphCount != resolved source count $($resolvedArray.Count).") }
if ($pixelCount -ne $resolvedArray.Count) { $graphErrors.Add("Pixel blob count $pixelCount != resolved source count $($resolvedArray.Count).") }
if ($headerCount -ne $resolvedArray.Count) { $graphErrors.Add("Texture header count $headerCount != resolved source count $($resolvedArray.Count).") }

$bridgePass = $resolvedArray.Count -gt 0 -and $linkCode -eq 0 -and $unlinkCode -eq 0 -and $graphErrors.Count -eq 0
$fullOrdinaryClosure = $bridgePass -and ($resolvedArray.Count + $nonPortableArray.Count -eq $nativeImages.Count) -and $missingArray.Count -eq 0

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4R.PcOwnedImageBridge.v1'
    mapName = $MapName
    nativeIdentityManifest = $NativeIdentityManifest
    nativeImageRoots = $nativeImages.Count
    sourceRoots = [object[]]$roots.ToArray()
    pcWaWInstallRoot = $resolvedWaWRoot
    pcFastFile = $pcFastFile
    pcFastFileDumpRoot = $pcFastFileDumpRoot
    pcFastFileDumpExitCode = $pcFastFileDumpCode
    pcFastFileDumpIwis = $pcFastFileDumpCount
    pcFastFileFirstError = $pcFastFileFirstError
    sourceCandidates = $candidateArray.Count
    resolvedSources = $resolvedArray.Count
    missingSources = $missingArray.Count
    nonPortableNativeNames = $nonPortableArray.Count
    nativeIdentityCoverage = $coverage
    linkedFastFile = $linkedFastFile
    oatLinkExitCode = $linkCode
    oatUnlinkExitCode = $unlinkCode
    graphRoot = $graphOut
    imageGraphs = $graphCount
    pixelBlobs = $pixelCount
    textureHeaders = $headerCount
    xenonCardMemoryBytes = $cardMemoryBytes
    bridgePass = $bridgePass
    fullOrdinaryClosure = $fullOrdinaryClosure
    productionSourcePolicy = @(
        'Pixel payload provenance is PC-side loose IWI/DDS, IWD, or IWI extracted from the PC fastfile only.',
        'PC fastfile extraction may use legitimate PC retail IWDs as dependency search paths.',
        'Native Xbox oracle contributes identity/order only.',
        'Stage 4J/4L/4M/4N native-derived payloads are prohibited as production inputs.'
    )
    resolved = $resolvedArray
    missing = $missingArray
    nonPortable = $nonPortableArray
    errors = [object[]]$graphErrors.ToArray()
}

$manifestPath = Join-Path $OutputRoot 'nuclear_4r_pc_image_bridge.json'
$manifest | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host ''
Write-Host '[NUCLEAR-4R] ================= PRODUCTION IMAGE BRIDGE RESULT ================'
Write-Host "[NUCLEAR-4R] Resolved PC images : $($resolvedArray.Count) / $($nativeImages.Count)"
Write-Host "[NUCLEAR-4R] Missing ordinary    : $($missingArray.Count)"
Write-Host "[NUCLEAR-4R] Non-portable/native: $($nonPortableArray.Count)"
Write-Host "[NUCLEAR-4R] Image graphs        : $graphCount"
Write-Host "[NUCLEAR-4R] Pixel blobs         : $pixelCount"
Write-Host "[NUCLEAR-4R] Texture headers     : $headerCount"
Write-Host "[NUCLEAR-4R] Xenon cardMemory    : $cardMemoryBytes bytes"
Write-Host "[NUCLEAR-4R] Bridge pass         : $bridgePass"
Write-Host "[NUCLEAR-4R] Full ordinary close : $fullOrdinaryClosure"
Write-Host "[NUCLEAR-4R] Manifest            : $manifestPath"
Write-Host '[NUCLEAR-4R] ================================================================='

if ($missingArray.Count -gt 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4R] First missing PC image sources:'
    $missingArray | Select-Object -First 40 | ForEach-Object { Write-Host ("  root #{0,4}: {1}" -f $_.Index, $_.Name) }
}

if ($nonPortableArray.Count -gt 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4R] Native generated/function identities requiring synthetic handling:'
    $nonPortableArray | Select-Object -First 20 | ForEach-Object { Write-Host ("  root #{0,4}: {1}" -f $_.Index, $_.Name) }
}

if ($graphErrors.Count -gt 0) {
    Write-Host ''
    Write-Host '[NUCLEAR-4R] Graph errors:'
    foreach ($e in @($graphErrors | Select-Object -First 40)) { Write-Host "  $e" }
}

if (-not $bridgePass) {
    Write-Host ''
    Write-Host '[NUCLEAR-4R] PC-OWNED IMAGE BRIDGE: BLOCKED' -ForegroundColor Yellow
    if ($resolvedArray.Count -eq 0) {
        Write-Host '[NUCLEAR-4R] No legitimate PC IWI/DDS source matched the native image identities.'
        if ($pcFastFileDumpCount -eq 0) {
            Write-Host '[NUCLEAR-4R] Direct PC fastfile extraction produced no usable IWI source images.'
            if ($pcFastFileDumpCode -ne 0 -and (Test-Path -LiteralPath $pcFastFileDumpLog -PathType Leaf)) {
                Write-Host "[NUCLEAR-4R] PC fastfile dump log: $pcFastFileDumpLog"
            }
        }
        if ($null -eq $resolvedWaWRoot) {
            Write-Host '[NUCLEAR-4R] A PC WaW install was not resolved; stock/common images therefore cannot be sourced yet.'
            Write-Host "[NUCLEAR-4R] Override discovery with: -WaWInstallRoot '<your PC Call of Duty World at War folder>'"
        }
    }
    exit 4
}

Write-Host ''
Write-Host '[NUCLEAR-4R] PC-OWNED IMAGE BRIDGE: PASS' -ForegroundColor Green
if ($fullOrdinaryClosure) {
    Write-Host '[NUCLEAR-4R] All portable native image identities have legitimate PC-side source payloads.'
}
else {
    Write-Host '[NUCLEAR-4R] Partial legitimate source closure is proven; unresolved identities remain fenced.'
}
Write-Host '[NUCLEAR-4R] Next: graft this PC-backed graph into the 6,811-root emitter and credit only these images in 4F.'
