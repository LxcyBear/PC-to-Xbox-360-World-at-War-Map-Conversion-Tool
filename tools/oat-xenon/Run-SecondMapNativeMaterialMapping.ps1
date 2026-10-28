[CmdletBinding()]
param(
    [string]$MapName = 'nazi_zombie_aztec_V2.2',
    [string]$OutputRoot = '',
    [string]$XboxAztecFastFile = '',
    [string]$NativeSearchRoot = '',
    [string]$NativeArchivePath = '',
    [string]$UnlinkerExe = '',
    [switch]$SkipArchiveSearch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Reference metadata only. No copyrighted game bytes are present in this script.
# Verified from LinkerV2GoldenCatalog.Aztec (known-working CoDXe/t4ff).
$goldenBytes = 82739469L
$goldenSha256 = 'e0f3c244dd7b72d0fe71c05faf014c2c9bfb05f770183954b3e7cde625eba98f'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo 'output\second_map_baselines' }
if (-not $UnlinkerExe) {
    $UnlinkerExe = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
}
$cli = Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
$workspace = Join-Path $OutputRoot $MapName
$identityFile = Join-Path $workspace 'material_techset_oat_bridge\material_technique_result.json'
$baselineFile = Join-Path $workspace 'second_map_baseline.json'
foreach ($f in @($identityFile, $baselineFile, $cli, $UnlinkerExe)) {
    if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { throw "Required input missing: $f" }
}
$identity = Get-Content -LiteralPath $identityFile -Raw | ConvertFrom-Json
$baseline = Get-Content -LiteralPath $baselineFile -Raw | ConvertFrom-Json
if ([string]$identity.schema -ne 'WaW360.SecondMapMaterialTechnique.v1' -or
    [string]$identity.pcMapFolder -ine $MapName -or
    [string]$baseline.format -ne 'WaW360.SecondMapBaseline.v1' -or
    [string]$baseline.mapName -ine $MapName) {
    throw 'PC baseline/TechniqueSet proof map mismatch. Refusing unrelated native mapping.'
}
if ([int]$identity.identityFilesVerified -ne [int]$identity.identityFilesAttempted -or
    [int]$identity.identityMappedTechniqueSets -ne [int]$identity.identityDeclaredTechniqueSets) {
    throw 'PC OAT TechniqueSet identity baseline is incomplete; cannot proceed.'
}
$sourceFastfiles = @($identity.oatIdentityScans | Where-Object { $_.Role -in @('map.ff','map_patch.ff','mod.ff') })
$primary = @($sourceFastfiles | Where-Object { $_.Role -eq 'map.ff' })
if ($primary.Count -ne 1) { throw 'Expected one verified PC main FF.' }
$mainPc = [string]$primary[0].SourceFastFile
$canonical = [IO.Path]::GetFileNameWithoutExtension($mainPc)
if ($canonical -ine 'nazi_zombie_aztec') { throw "Expected canonical Aztec main FF, got $canonical" }
foreach ($source in $sourceFastfiles) {
    $matching = @($baseline.fastfiles | Where-Object { $_.Path -ieq [string]$source.SourceFastFile })
    if ($matching.Count -ne 1) { throw "Missing exact source baseline: $($source.SourceFastFile)" }
    if (-not (Test-Path -LiteralPath ([string]$source.SourceFastFile) -PathType Leaf)) {
        throw "PC fastfile disappeared: $($source.SourceFastFile)"
    }
    $bytes = [long](Get-Item -LiteralPath ([string]$source.SourceFastFile)).Length
    if ($bytes -ne [long]$matching[0].Bytes) {
        throw "PC fastfile changed since proof; rerun baseline: $($source.SourceFastFile)"
    }
}
$work = Join-Path $workspace 'native_material_mapping'
New-Item -ItemType Directory -Force -Path $work | Out-Null

function Invoke-CLI([string]$label,[string[]]$command,[string]$log) {
    $args = @('run','--project',$cli,'-c','Release','--no-build','--') + $command
    $old = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $lines = @(& dotnet @args 2>&1 | ForEach-Object { [string]$_ })
        $exitCode = [int]$LASTEXITCODE
    } finally { $ErrorActionPreference = $old }
    [IO.File]::WriteAllLines($log,[string[]]$lines,[Text.UTF8Encoding]::new($false))
    Write-Host "[NATIVE-AZTEC] $label exit=$exitCode"
    foreach ($line in @($lines | Select-Object -Last 8)) { Write-Host "[NATIVE-AZTEC]   $line" }
    return [pscustomobject]@{ Name=$label; ExitCode=$exitCode; Log=$log }
}

$discovered = New-Object 'System.Collections.Generic.List[object]'
$lookedIn = New-Object 'System.Collections.Generic.List[string]'
$archiveHints = New-Object 'System.Collections.Generic.List[string]'
$native = $null
$nativeOrigin = ''
function Try-GoldenFF([string]$candidate,[string]$origin) {
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { return $false }
    $file = Get-Item -LiteralPath $candidate
    if ($file.FullName -ieq $mainPc) { return $false }
    $sizeMatches = [long]$file.Length -eq $goldenBytes
    $hash = ''
    $errorText = ''
    if ($sizeMatches) {
        try { $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant() }
        catch { $errorText = $_.Exception.Message }
    }
    $matched = $sizeMatches -and $hash -eq $goldenSha256
    $discovered.Add([pscustomobject]@{
        Path=$file.FullName; Origin=$origin; Bytes=[long]$file.Length
        SizeMatch=$sizeMatches; Sha256=$hash; ExactGoldenMatch=$matched; HashError=$errorText
    })
    if ($matched) {
        $script:native = $file.FullName
        $script:nativeOrigin = $origin
        return $true
    }
    return $false
}

Write-Host '[NATIVE-AZTEC] ============================================================'
Write-Host '[NATIVE-AZTEC] SOURCE-VERIFIED AZTEC PC -> XENON MATERIAL/TECHNIQUESET MAP'
Write-Host "[NATIVE-AZTEC] Canonical map: $canonical"
Write-Host "[NATIVE-AZTEC] Prior OAT identities: $($identity.identityMappedTechniqueSets)/$($identity.identityDeclaredTechniqueSets)"
Write-Host '[NATIVE-AZTEC] Golden fingerprint required; no guessed shader graph or PC bytecode graft.'
Write-Host '[NATIVE-AZTEC] ============================================================'

if ($XboxAztecFastFile) {
    if (-not (Test-Path -LiteralPath $XboxAztecFastFile -PathType Leaf)) {
        throw "Specified Xbox native FF missing: $XboxAztecFastFile"
    }
    if (-not (Try-GoldenFF $XboxAztecFastFile 'explicit-parameter')) {
        $bad = $discovered[$discovered.Count - 1]
        throw "Explicit Xbox Aztec FF does not match known-working reference (expected $goldenBytes bytes, SHA256 $goldenSha256; got $($bad.Bytes) bytes, SHA256 $($bad.Sha256))."
    }
} else {
    $userHome = [Environment]::GetFolderPath('UserProfile')
    $searchRoots = New-Object 'System.Collections.Generic.List[string]'
    if ($NativeSearchRoot) { $searchRoots.Add($NativeSearchRoot) }
    $settingsFile = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'WaW360Converter\settings.json'
    if (Test-Path -LiteralPath $settingsFile -PathType Leaf) {
        try {
            $settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
            foreach ($field in @('CodxeFolder','XboxGameFolder')) {
                $property = $settings.PSObject.Properties[$field]
                if ($null -ne $property -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
                    $searchRoots.Add([string]$property.Value)
                }
            }
        } catch { Write-Host '[NATIVE-AZTEC] Warning: could not parse converter settings.' }
    }
    # Search only the relevant user-owned trees, not entire drives or Xbox devices.
    $searchRoots.Add((Join-Path $userHome 'OneDrive\Desktop\My Xbox\World at War'))
    $searchRoots.Add((Join-Path $userHome 'OneDrive\Desktop\My Xbox\My Projects\World at War'))
    $searchRoots.Add((Join-Path $userHome 'Downloads'))
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($root in $searchRoots) {
        if ($native) { break }
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        $fullRoot = (Resolve-Path -LiteralPath $root).Path
        if (-not $seen.Add($fullRoot)) { continue }
        $lookedIn.Add($fullRoot)
        Write-Host "[NATIVE-AZTEC] Searching for matching-size native FF: $fullRoot"
        # Source names can be changed by ZIP extraction; use exact byte-size as a
        # cheap prefilter across all FF files, then verify the pinned SHA-256.
        foreach ($f in @(Get-ChildItem -LiteralPath $fullRoot -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue)) {
            if ([long]$f.Length -ne $goldenBytes) { continue }
            if (Try-GoldenFF $f.FullName 'loose-ff-size-match') { break }
        }
    }
    if (-not $native -and -not $SkipArchiveSearch) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        Add-Type -AssemblyName System.IO.Compression
        $archivesToCheck = New-Object 'System.Collections.Generic.List[string]'
        if ($NativeArchivePath) {
            if (-not (Test-Path -LiteralPath $NativeArchivePath -PathType Leaf)) {
                throw "Specified ZIP archive missing: $NativeArchivePath"
            }
            $archivesToCheck.Add((Resolve-Path -LiteralPath $NativeArchivePath).Path)
        }
        foreach ($root in @($lookedIn.ToArray())) {
            Write-Host "[NATIVE-AZTEC] Looking for native Aztec FF entries in ZIP archives: $root"
            # Read ZIP central directories, not full archive payloads. The archived
            # file name must be canonical, and the uncompressed length must match.
            foreach ($zipFile in @(Get-ChildItem -LiteralPath $root -Filter '*.zip' -File -Recurse -ErrorAction SilentlyContinue)) {
                $archivesToCheck.Add($zipFile.FullName)
            }
        }
        $uniqueArchives = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        foreach ($archivePath in $archivesToCheck) {
                if ($native) { break }
                if (-not $uniqueArchives.Add($archivePath)) { continue }
                $archiveHints.Add($archivePath)
                $zipFile = Get-Item -LiteralPath $archivePath
                try {
                    $zip = [IO.Compression.ZipFile]::OpenRead($zipFile.FullName)
                    try {
                        foreach ($entry in $zip.Entries) {
                            if ($entry.Name -ine ($canonical + '.ff') -or [long]$entry.Length -ne $goldenBytes) { continue }
                            $stream = $entry.Open()
                            try {
                                $sha = [Security.Cryptography.SHA256]::Create()
                                try { $bytes = $sha.ComputeHash($stream) }
                                finally { $sha.Dispose() }
                                $digest = ([BitConverter]::ToString($bytes) -replace '-', '').ToLowerInvariant()
                            } finally { $stream.Dispose() }
                            if ($digest -ne $goldenSha256) { continue }
                            # Only a validated exact golden entry can be extracted.
                            # Keep user-owned game bytes under .deps (gitignored), never the repo source tree.
                            $localRoot = Join-Path $repo '.deps\native-map-references'
                            New-Item -ItemType Directory -Force -Path $localRoot | Out-Null
                            $localFile = Join-Path $localRoot ($canonical + '.ff')
                            $entryStream = $entry.Open()
                            try {
                                $dest = [IO.File]::Open($localFile,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::None)
                                try { $entryStream.CopyTo($dest) } finally { $dest.Dispose() }
                            } finally { $entryStream.Dispose() }
                            if (Try-GoldenFF $localFile ('verified-zip:' + $zipFile.FullName)) { break }
                        }
                    } finally { $zip.Dispose() }
                } catch {
                    Write-Host "[NATIVE-AZTEC] Cannot inspect archive $($zipFile.FullName) : $($_.Exception.Message)" -ForegroundColor Yellow
                }
        }
    }
}

$txt = Join-Path $work 'native_material_mapping_result.txt'
$json = Join-Path $work 'native_material_mapping_result.json'
$stages = New-Object 'System.Collections.Generic.List[object]'
$mappingRows = New-Object 'System.Collections.Generic.List[object]'
$conflicts = New-Object 'System.Collections.Generic.List[object]'
$closure = $null
$status = 'GOLDEN_REFERENCE_NOT_LOCATED'
if ($native) {
    Write-Host "[NATIVE-AZTEC] EXACT golden Xbox fastfile verified: $native" -ForegroundColor Green
    if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) { throw "CLI missing: $cli" }
    # Single build; all CLI commands below are --no-build.
    $buildLog = Join-Path $work 'native_mapping_build.log'
    $old = $ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        $buildLines = @(& dotnet 'build' $cli '-c' 'Release' '--nologo' 2>&1 | ForEach-Object { [string]$_ })
        $buildCode = [int]$LASTEXITCODE
    } finally { $ErrorActionPreference=$old }
    [IO.File]::WriteAllLines($buildLog,[string[]]$buildLines,[Text.UTF8Encoding]::new($false))
    if ($buildCode -ne 0) {
        foreach ($line in @($buildLines | Select-Object -Last 20)) { Write-Host $line }
        throw "CLI build failed; inspect $buildLog"
    }
    $mainDir = Join-Path $work 'main_native_closure'
    New-Item -ItemType Directory -Force -Path $mainDir | Out-Null
    $closurePath = Join-Path $mainDir 'nuclear_techset_closure_plan.json'
    if (Test-Path -LiteralPath $closurePath -PathType Leaf) { Remove-Item -LiteralPath $closurePath -Force }
    $stages.Add((Invoke-CLI 'main PC -> native typed identity closure' @(
        'nuclear-techset-closure-plan',$mainPc,$native,$mainDir,$UnlinkerExe
    ) (Join-Path $mainDir 'nuclear_techset_closure.console.log')))
    if (Test-Path -LiteralPath $closurePath -PathType Leaf) {
        $closure = Get-Content -LiteralPath $closurePath -Raw | ConvertFrom-Json
        foreach ($r in @($closure.Rows)) {
            $mappingRows.Add([pscustomobject]@{
                Role='map.ff'; PcTechniqueSet=[string]$r.PcTechniqueSet
                XboxTechniqueSet=[string]$r.XboxTechniqueSet
                Resolution=[string]$r.Resolution
                SupportingMaterials=[int]$r.SupportingMaterials
                NativeRootIndex=$r.XboxDonorAssetIndex
            })
        }
    }
    foreach ($src in @($sourceFastfiles | Where-Object { $_.Role -in @('map_patch.ff','mod.ff') })) {
        $role=[string]$src.Role
        $subdir = Join-Path $work ($role -replace '[^A-Za-z0-9_.-]','_')
        New-Item -ItemType Directory -Force -Path $subdir | Out-Null
        $bridgePath = Join-Path $subdir 'material_techset_bridge.json'
        if (Test-Path -LiteralPath $bridgePath -PathType Leaf) { Remove-Item -LiteralPath $bridgePath -Force }
        $stages.Add((Invoke-CLI "$role material A/B evidence" @(
            'material-techset-bridge',[string]$src.SourceFastFile,$native,$subdir,$UnlinkerExe
        ) (Join-Path $subdir 'material_bridge.console.log')))
        if (Test-Path -LiteralPath $bridgePath -PathType Leaf) {
            $bridge = Get-Content -LiteralPath $bridgePath -Raw | ConvertFrom-Json
            foreach ($r in @($bridge.Mappings)) {
                $mappingRows.Add([pscustomobject]@{
                    Role=$role; PcTechniqueSet=[string]$r.PcTechniqueSet
                    XboxTechniqueSet=[string]$r.XboxTechniqueSet
                    Resolution='MATERIAL_BRIDGE'
                    SupportingMaterials=[int]$r.SupportingMaterials
                    NativeRootIndex=$null
                })
            }
            foreach ($c in @($bridge.Conflicts)) {
                $conflicts.Add([pscustomobject]@{
                    Role=$role; PcTechniqueSet=[string]$c.PcTechniqueSet
                    XboxCandidates=@($c.XboxTechniqueSets)
                })
            }
        }
    }
    $status=if ($null -eq $closure) { 'GOLDEN_VERIFIED_MAIN_CLOSURE_MISSING' }
            else { 'GOLDEN_VERIFIED_NATIVE_MAPPING_RECORDED' }
}

# A mapping from another source may disagree with the main source. Report this
# explicitly; do not resolve it by choosing an arbitrary donor.
$crossRoleConflicts = @($mappingRows.ToArray() |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_.XboxTechniqueSet) } |
    Group-Object PcTechniqueSet |
    Where-Object { @($_.Group | Select-Object -ExpandProperty XboxTechniqueSet -Unique).Count -gt 1 } |
    ForEach-Object { [pscustomobject]@{
        PcTechniqueSet=$_.Name
        XboxTargets=@($_.Group | Select-Object -ExpandProperty XboxTechniqueSet -Unique)
    } })
$allStages = @($stages.ToArray())
$manifest = [ordered]@{
    schema='WaW360.SecondMapNativeMaterialMapping.v1'
    mapFolder=$MapName
    canonicalRuntimeName=$canonical
    goldenReferenceSize=$goldenBytes
    goldenReferenceSha256=$goldenSha256
    verifiedXboxReference=$native
    verifiedReferenceOrigin=$nativeOrigin
    status=$status
    searchRoots=[string[]]$lookedIn.ToArray()
    candidates=[object[]]$discovered.ToArray()
    archiveCandidates=[string[]]$archiveHints.ToArray()
    mainPcTechniqueRoots=[int]$primary[0].DeclaredTechsets
    mainNativeClosureComputed=($null -ne $closure)
    mainReachablePcTechniqueSets=$(if ($null -ne $closure) { [int]$closure.ReachablePcTechniqueRoots } else { $null })
    mainDonorBound=$(if ($null -ne $closure) { [int]$closure.DonorBoundTechniqueRoots } else { $null })
    mainUnresolved=$(if ($null -ne $closure) { [int]$closure.UnresolvedTechniqueRoots } else { $null })
    mainResidentPending=$(if ($null -ne $closure) { [int]$closure.ResidentXboxTargetsPending } else { $null })
    mappingEvidenceRows=[object[]]$mappingRows.ToArray()
    withinSourceConflicts=[object[]]$conflicts.ToArray()
    crossRoleConflicts=[object[]]$crossRoleConflicts
    stageRuns=[object[]]$allStages
    productionXboxMaterialReady=$false
    policy=@(
        'Only exact pinned known-working Xbox Aztec FF bytes authorize this specific A/B comparison.',
        'An exact TechniqueSet identity map is not an Xbox-native shader/pass byte serializer.',
        'Map FF donor-root indices are not portable to mod/patch FF without typed graph ownership proof.',
        'PC Direct3D shader bytecode is not copied into Xenon fastfiles.',
        'No native Xbox FF, image, shader, sound, XMA, or game binary is written into Git-tracked sources.'
    )
}
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $json -Encoding UTF8
$report = New-Object 'System.Collections.Generic.List[string]'
$report.Add('AZTEC PC -> NATIVE XBOX MATERIAL / TECHNIQUESET IDENTITY MAPPING')
$report.Add('=====================================================================')
$report.Add("Status: $status")
$report.Add("Verified native reference: $(if ($native) { $native } else { '<not found>' })")
$report.Add("Reference origin: $nativeOrigin")
$report.Add("Golden size=$goldenBytes sha256=$goldenSha256")
$report.Add("Candidate FF files checked: $($discovered.Count); candidate archives: $($archiveHints.Count)")
foreach ($candidate in $discovered) {
    $report.Add("  file: $($candidate.Path) bytes=$($candidate.Bytes) exact=$($candidate.ExactGoldenMatch)")
}
if (-not $native) {
    $report.Add('Native FF not found in configured CodxeFolder/XboxGameFolder, default World at War trees, Downloads or optional search root.')
    $report.Add('Search included all .ff files by exact byte-size and ZIP central directories regardless of archive filename; full hashes computed only for candidates.')
    $report.Add('Try -XboxAztecFastFile "<full-path-to-known-working-nazi_zombie_aztec.ff>"')
    $report.Add('Or -NativeSearchRoot "<folder-containing-native-Xbox-Aztec>"')
    $report.Add('If inside an unrelated-named ZIP, use -NativeArchivePath "<full-path-to-archive.zip>".')
    $report.Add('No material comparisons ran; prior PC identities remain preserved.')
} else {
    if ($null -ne $closure) {
        $report.Add("Main reachable=$($closure.ReachablePcTechniqueRoots), bound=$($closure.DonorBoundTechniqueRoots), unresolved=$($closure.UnresolvedTechniqueRoots), residentPending=$($closure.ResidentXboxTargetsPending)")
        $report.Add("Main dead/unreferenced=$($closure.DeadPcTechniqueRoots); identities=$($closure.PcTechniqueRoots)")
    }
    foreach ($s in $stages) { $report.Add("  $($s.Name): exit=$($s.ExitCode); $($s.Log)") }
    $report.Add("Cross-FF mapping conflicts=$($crossRoleConflicts.Count); within-FF conflicts=$($conflicts.Count)")
    foreach ($item in @($crossRoleConflicts | Select-Object -First 25)) {
        $report.Add("  CONFLICT $($item.PcTechniqueSet): $($item.XboxTargets -join ', ')")
    }
    foreach ($item in @($mappingRows | Where-Object { $_.Resolution -ne 'DROP_UNREACHABLE_PC_ROOT' } | Select-Object -First 120)) {
        $report.Add("  $($item.Role) $($item.PcTechniqueSet) => $($item.XboxTechniqueSet) $($item.Resolution)")
    }
    $report.Add('Full row evidence is in JSON. No production Xbox material/shader graph was emitted.')
}
$report.Add('Hardware readiness remains FALSE.')
[IO.File]::WriteAllLines($txt,[string[]]$report.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[NATIVE-AZTEC] ================= RESULT ====================================='
Write-Host "[NATIVE-AZTEC] status=$status"
if ($null -ne $closure) {
    Write-Host "[NATIVE-AZTEC] main reachable=$($closure.ReachablePcTechniqueRoots) bound=$($closure.DonorBoundTechniqueRoots) unresolved=$($closure.UnresolvedTechniqueRoots)"
    Write-Host "[NATIVE-AZTEC] cross-file conflicts=$($crossRoleConflicts.Count)"
}
if (-not $native) {
    Write-Host '[NATIVE-AZTEC] Exact native reference not found. Give the script -XboxAztecFastFile or -NativeSearchRoot.' -ForegroundColor Yellow
}
Write-Host "[NATIVE-AZTEC] Report: $txt"
Write-Host "[NATIVE-AZTEC] JSON: $json"
Write-Host '[NATIVE-AZTEC] Done. Source/identity/oracle work only; no Xbox candidate produced.'
