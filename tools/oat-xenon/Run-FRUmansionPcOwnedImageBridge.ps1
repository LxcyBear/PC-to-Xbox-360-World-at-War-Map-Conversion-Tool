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

function Resolve-SteamWaWRoot {
    $steamRoots = New-Object 'System.Collections.Generic.List[string]'
    $seenSteamRoots = @{}

    foreach ($registryPath in @('HKCU:\Software\Valve\Steam','HKLM:\SOFTWARE\WOW6432Node\Valve\Steam','HKLM:\SOFTWARE\Valve\Steam')) {
        try {
            $item = Get-ItemProperty -Path $registryPath -ErrorAction Stop
            foreach ($property in @('SteamPath', 'InstallPath')) {
                if (-not ($item.PSObject.Properties.Name -contains $property)) { continue }
                $steam = [string]$item.$property
                if ([string]::IsNullOrWhiteSpace($steam) -or -not (Test-Path -LiteralPath $steam -PathType Container)) { continue }
                $fullSteam = (Resolve-Path -LiteralPath $steam).Path
                $key = $fullSteam.ToLowerInvariant()
                if (-not $seenSteamRoots.ContainsKey($key)) {
                    $seenSteamRoots[$key] = $true
                    $steamRoots.Add($fullSteam)
                }
            }
        }
        catch {}
    }

    foreach ($steam in @('C:\Program Files (x86)\Steam', 'C:\Program Files\Steam')) {
        if (-not (Test-Path -LiteralPath $steam -PathType Container)) { continue }
        $fullSteam = (Resolve-Path -LiteralPath $steam).Path
        $key = $fullSteam.ToLowerInvariant()
        if (-not $seenSteamRoots.ContainsKey($key)) {
            $seenSteamRoots[$key] = $true
            $steamRoots.Add($fullSteam)
        }
    }

    $seenLibraries = @{}
    foreach ($steam in $steamRoots) {
        $libraries = New-Object 'System.Collections.Generic.List[string]'
        $libraries.Add($steam)

        $libraryFile = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $libraryFile -PathType Leaf) {
            foreach ($line in Get-Content -LiteralPath $libraryFile -ErrorAction SilentlyContinue) {
                if ($line -match '"path"\s+"([^"]+)"') {
                    $libraryPath = ([string]$matches[1]).Replace('\\', '\')
                    if (Test-Path -LiteralPath $libraryPath -PathType Container) {
                        $libraries.Add((Resolve-Path -LiteralPath $libraryPath).Path)
                    }
                }
            }
        }

        foreach ($library in $libraries) {
            $libraryKey = $library.ToLowerInvariant()
            if ($seenLibraries.ContainsKey($libraryKey)) { continue }
            $seenLibraries[$libraryKey] = $true

            $steamApps = Join-Path $library 'steamapps'
            $manifest = Join-Path $steamApps 'appmanifest_10090.acf'
            if (Test-Path -LiteralPath $manifest -PathType Leaf) {
                foreach ($line in Get-Content -LiteralPath $manifest -ErrorAction SilentlyContinue) {
                    if ($line -match '"installdir"\s+"([^"]+)"') {
                        $candidate = Join-Path (Join-Path $steamApps 'common') $matches[1]
                        if (Test-Path -LiteralPath $candidate -PathType Container) {
                            return (Resolve-Path -LiteralPath $candidate).Path
                        }
                    }
                }
            }

            foreach ($name in @('Call of Duty World at War', 'Call of Duty - World at War')) {
                $candidate = Join-Path $steamApps "common\$name"
                if (Test-Path -LiteralPath $candidate -PathType Container) {
                    return (Resolve-Path -LiteralPath $candidate).Path
                }
            }
        }
    }

    foreach ($candidate in @(
        'C:\Program Files\Activision\Call of Duty - World at War',
        'C:\Program Files (x86)\Activision\Call of Duty - World at War'
    )) {
        if (Test-Path -LiteralPath $candidate -PathType Container) {
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
    $envRoot = [Environment]::GetEnvironmentVariable('WAW_PC_ROOT')
    if (-not [string]::IsNullOrWhiteSpace($envRoot)) { $WaWInstallRoot = $envRoot }
}
if ([string]::IsNullOrWhiteSpace($WaWInstallRoot)) { $WaWInstallRoot = Resolve-SteamWaWRoot }

$resolvedWaWRoot = $null
if (-not [string]::IsNullOrWhiteSpace($WaWInstallRoot) -and (Test-Path -LiteralPath $WaWInstallRoot -PathType Container)) {
    $resolvedWaWRoot = (Resolve-Path -LiteralPath $WaWInstallRoot).Path
    Add-SourceRoot -Roots $roots -Path $resolvedWaWRoot -Kind 'retail-root' -Priority 50
    Add-SourceRoot -Roots $roots -Path (Join-Path $resolvedWaWRoot 'main') -Kind 'retail-main' -Priority 45
    Add-SourceRoot -Roots $roots -Path (Join-Path $resolvedWaWRoot 'raw') -Kind 'retail-raw' -Priority 40
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
Write-Host ''
Write-Host '[NUCLEAR-4R] SOURCE POLICY:'
Write-Host '  - input pixels come only from PC-side loose IWI/DDS or IWD entries'
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
    try { & $linker @linkArgs 2>&1 | Tee-Object -FilePath $linkLog; $linkCode = $LASTEXITCODE }
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
            try { & $unlinker @unlinkArgs 2>&1 | Tee-Object -FilePath $unlinkLog; $unlinkCode = $LASTEXITCODE }
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
        'Pixel payload provenance is PC-side loose IWI/DDS or IWD only.',
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
