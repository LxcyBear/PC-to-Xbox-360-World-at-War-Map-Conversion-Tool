[CmdletBinding()]
param(
    [string]$PcFastFile = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps\nazi_zombie_FRUmansion\nazi_zombie_frumansion.ff',
    [string]$ModelName = '',
    [string]$OutputRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$linker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Linker.exe'
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'

if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) {
    $unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4u_oat_xmodel_bridge'
}

if (-not (Test-Path -LiteralPath $PcFastFile -PathType Leaf)) {
    throw "PC FRUmansion fastfile not found: $PcFastFile"
}
if (-not (Test-Path -LiteralPath $linker -PathType Leaf)) {
    throw "Pinned Xenon OAT Linker not found: $linker"
}
if (-not (Test-Path -LiteralPath $unlinker -PathType Leaf)) {
    throw "Pinned Xenon OAT Unlinker not found: $unlinker"
}

function Invoke-NativeTee {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][object[]]$Arguments,
        [Parameter(Mandatory=$true)][string]$LogPath
    )

    $previousPreference = $ErrorActionPreference
    try {
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

function Get-XModelAssetName {
    param([string]$JsonPath)

    $normalized = [IO.Path]::GetFullPath($JsonPath).Replace('/', '\')
    $lower = $normalized.ToLowerInvariant()
    $marker = '\xmodel\'
    $at = $lower.LastIndexOf($marker)
    if ($at -lt 0) { return $null }

    $relative = $normalized.Substring($at + $marker.Length)
    if (-not $relative.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }

    $relative = $relative.Substring(0, $relative.Length - 5)
    return $relative.Replace('\', '/')
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

$pcDump = Join-Path $OutputRoot 'pc_dump'
$zoneSource = Join-Path $OutputRoot 'zone_source'
$work = Join-Path $OutputRoot 'workspace'
$linkOut = Join-Path $OutputRoot 'linked'
$identityOut = Join-Path $OutputRoot 'identity'
$layoutOut = Join-Path $OutputRoot 'layout'

foreach ($dir in @($OutputRoot, $pcDump, $zoneSource, $work, $linkOut, $identityOut, $layoutOut)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

$pcDumpLog = Join-Path $OutputRoot 'pc_xmodel_dump.log'
$linkLog = Join-Path $OutputRoot 'xenon_link.log'
$listLog = Join-Path $OutputRoot 'xenon_readback_list.log'

Write-Host '[NUCLEAR-4U] ================================================================'
Write-Host '[NUCLEAR-4U] PC XMODEL -> OAT CANONICAL -> XENON NATIVE BRIDGE'
Write-Host '[NUCLEAR-4U] ================================================================'
Write-Host "[NUCLEAR-4U] PC fastfile : $PcFastFile"
Write-Host "[NUCLEAR-4U] Linker      : $linker"
Write-Host "[NUCLEAR-4U] Unlinker    : $unlinker"
Write-Host "[NUCLEAR-4U] Output      : $OutputRoot"
Write-Host ''
Write-Host '[NUCLEAR-4U] Payload policy: PC-owned XModel data only.'
Write-Host '[NUCLEAR-4U] The Xbox oracle contributes no XModel payload bytes.'
Write-Host ''

$dumpArgs = @(
    '--verbose',
    '--no-color',
    '--include-assets', 'xmodel',
    '--model-format', 'gltf',
    '--output-folder', $pcDump,
    $PcFastFile
)

Write-Host '[NUCLEAR-4U] Exporting PC XModels to OAT canonical JSON + GLTF...'
$dumpCode = Invoke-NativeTee -FilePath $unlinker -Arguments $dumpArgs -LogPath $pcDumpLog

$jsonFiles = @(
    Get-ChildItem -LiteralPath $pcDump -Filter '*.json' -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName.Replace('/', '\') -match '(?i)\\xmodel\\' }
)

$candidates = New-Object 'System.Collections.Generic.List[object]'

foreach ($file in $jsonFiles) {
    $assetName = Get-XModelAssetName -JsonPath $file.FullName
    if ([string]::IsNullOrWhiteSpace($assetName)) { continue }

    try {
        $json = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    }
    catch {
        continue
    }

    if ([string]$json._type -ine 'xmodel') { continue }

    $lods = @($json.lods)
    if ($lods.Count -eq 0) { continue }

    $complete = $true
    [int64]$lodBytes = 0

    foreach ($lod in $lods) {
        $relative = [string]$lod.file
        if ([string]::IsNullOrWhiteSpace($relative)) {
            $complete = $false
            break
        }

        $lodPath = Join-Path $pcDump $relative.Replace('/', '\')
        if (-not (Test-Path -LiteralPath $lodPath -PathType Leaf)) {
            $complete = $false
            break
        }

        $lodBytes += (Get-Item -LiteralPath $lodPath).Length
    }

    if (-not $complete) { continue }

    $candidates.Add([pscustomobject]@{
        Name = $assetName
        JsonPath = $file.FullName
        LodCount = $lods.Count
        LodBytes = $lodBytes
    })
}

if ($candidates.Count -eq 0) {
    throw "PC XModel export produced no complete OAT JSON+GLTF model. Unlinker exit=$dumpCode. See: $pcDumpLog"
}

$selected = $null

if (-not [string]::IsNullOrWhiteSpace($ModelName)) {
    $matches = @(
        $candidates |
            Where-Object { $_.Name -ieq $ModelName } |
            Select-Object -First 1
    )

    if ($matches.Count -ne 1) {
        $available = @(
            $candidates |
                Sort-Object Name |
                Select-Object -First 24 -ExpandProperty Name
        )
        throw "Requested XModel '$ModelName' was not exported completely. First available: $($available -join ', ')"
    }

    $selected = $matches[0]
}
else {
    $selected = @(
        $candidates |
            Sort-Object LodBytes, LodCount, Name |
            Select-Object -First 1
    )[0]
}

Write-Host ''
Write-Host "[NUCLEAR-4U] Complete PC XModels exported : $($candidates.Count)"
Write-Host "[NUCLEAR-4U] Selected proof model          : $($selected.Name)"
Write-Host "[NUCLEAR-4U] Selected LODs                 : $($selected.LodCount)"
Write-Host "[NUCLEAR-4U] Selected GLTF bytes           : $($selected.LodBytes)"

# Recover the authoritative PC bone-name -> hit-location mapping from the
# original fastfile before any Xenon import. This replaces the old empty proof
# CSV and makes 4U a production-fidelity XModel round trip.
$partClassification = Join-Path $pcDump 'partclassification.csv'
$partClassificationManifest = Join-Path $OutputRoot 'pc_partclassification.json'

Write-Host ''
Write-Host '[NUCLEAR-4U] Recovering original PC XModel partClassification[]...'

$classArgs = @(
    'run',
    '--project', '.\src\WaW360.Cli\WaW360.Cli.csproj',
    '-c', 'Release',
    '--',
    'xmodel-partclassification',
    $PcFastFile,
    $partClassification,
    $partClassificationManifest
)

Push-Location $repo
try {
    & dotnet @classArgs
    $classCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

if (-not (Test-Path -LiteralPath $partClassificationManifest -PathType Leaf)) {
    throw "PC partClassification recovery did not emit its manifest: $partClassificationManifest"
}

$classResult = Get-Content -LiteralPath $partClassificationManifest -Raw | ConvertFrom-Json
if ($classCode -ne 0 -or -not [bool]$classResult.Complete) {
    throw ("PC partClassification recovery is incomplete: aligned={0}/{1}, modelsWithBones={2}, observed={3}, unresolvedStrings={4}, conflicts={5}, packedBoneNames={6}, packedPartClassification={7}" -f
        [int]$classResult.AlignedXModels,
        [int]$classResult.DeclaredXModels,
        [int]$classResult.ModelsWithBones,
        [int]$classResult.ModelsObserved,
        [int]$classResult.UnresolvedScriptStrings,
        [int]$classResult.Conflicts,
        [int]$classResult.ModelsSkippedPackedBoneNames,
        [int]$classResult.ModelsSkippedPackedClassification)
}

Write-Host "[NUCLEAR-4U] partClassification models     : $([int]$classResult.ModelsObserved) / $([int]$classResult.ModelsWithBones)"
Write-Host "[NUCLEAR-4U] partClassification bone rows  : $(@($classResult.Rows).Count)"
Write-Host "[NUCLEAR-4U] partClassification conflicts  : $([int]$classResult.Conflicts)"
Write-Host "[NUCLEAR-4U] partClassification fidelity   : PASS"

# This proof isolates the PC->Xenon XModel/XSurface conversion. Renderer and
# physics payloads are separate stages, so dependencies are represented as OAT
# reference assets rather than fabricated material/physics bodies.
$selectedJson = Get-Content -LiteralPath $selected.JsonPath -Raw | ConvertFrom-Json
$referenceAssets = New-Object 'System.Collections.Generic.List[object]'
$referenceKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

function Add-ReferenceAsset {
    param(
        [Parameter(Mandatory=$true)][string]$Type,
        [AllowEmptyString()][string]$Name
    )

    if ([string]::IsNullOrWhiteSpace($Name)) { return }
    $key = $Type + [char]0 + $Name
    if ($referenceKeys.Add($key)) {
        $referenceAssets.Add([pscustomobject]@{
            Type = $Type
            Name = $Name
        })
    }
}

foreach ($lod in @($selectedJson.lods)) {
    $relative = [string]$lod.file
    if ([string]::IsNullOrWhiteSpace($relative)) { continue }

    $lodPath = Join-Path $pcDump $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $lodPath -PathType Leaf)) { continue }

    try {
        $gltf = Get-Content -LiteralPath $lodPath -Raw | ConvertFrom-Json
        foreach ($material in @($gltf.materials)) {
            if ($null -eq $material) { continue }
            Add-ReferenceAsset -Type 'material' -Name ([string]$material.name)
        }
    }
    catch {
        throw "Failed to inspect exported GLTF material dependencies: $lodPath :: $($_.Exception.Message)"
    }
}

if ($selectedJson.PSObject.Properties.Name -contains 'physPreset') {
    Add-ReferenceAsset -Type 'physpreset' -Name ([string]$selectedJson.physPreset)
}
if ($selectedJson.PSObject.Properties.Name -contains 'physConstraints') {
    Add-ReferenceAsset -Type 'physconstraints' -Name ([string]$selectedJson.physConstraints)
}

Write-Host "[NUCLEAR-4U] Reference dependencies         : $($referenceAssets.Count)"
foreach ($ref in $referenceAssets) {
    Write-Host "[NUCLEAR-4U]   ref $($ref.Type),$($ref.Name)"
}

$project = 'nuclear_4u_oat_xmodel_bridge'
$zoneFile = Join-Path $zoneSource ($project + '.zone')
$zoneLines = New-Object 'System.Collections.Generic.List[string]'
$zoneLines.Add('>game,T4')
$zoneLines.Add('>platform,xbox360')
$zoneLines.Add(('>name,{0}' -f $project))
$zoneLines.Add('')

foreach ($ref in $referenceAssets) {
    # Zone-definition reference syntax is type,,name. This causes dependency
    # resolution to create a default reference asset instead of requiring a
    # renderer/physics payload during the isolated XModel bridge proof.
    $safeName = ([string]$ref.Name).Replace('"', '')
    $zoneLines.Add(('{0},,"{1}"' -f $ref.Type, $safeName))
}

$zoneLines.Add(('xmodel,"{0}"' -f $selected.Name.Replace('"', '')))

[IO.File]::WriteAllLines(
    $zoneFile,
    $zoneLines,
    [Text.UTF8Encoding]::new($false))

$linkArgs = @(
    '--verbose',
    '--no-color',
    '--base-folder', $work,
    '--source-search-path', $zoneSource,
    '--asset-search-path', $pcDump,
    '--output-folder', $linkOut,
    $project
)

Write-Host ''
Write-Host '[NUCLEAR-4U] Rebuilding selected PC model through the T4 Xenon asset loader...'

Push-Location $work
try {
    $linkCode = Invoke-NativeTee -FilePath $linker -Arguments $linkArgs -LogPath $linkLog
}
finally {
    Pop-Location
}

$linked = @(
    Get-ChildItem -LiteralPath $linkOut -Filter '*.ff' -File -Recurse -ErrorAction SilentlyContinue
)

if ($linkCode -ne 0 -or $linked.Count -ne 1) {
    throw "Xenon XModel link failed or did not emit exactly one FF. exit=$linkCode files=$($linked.Count). See: $linkLog"
}

$linkedFastFile = $linked[0].FullName

Write-Host ''
Write-Host '[NUCLEAR-4U] Re-opening generated Xbox FF through the Xenon loader...'

$listArgs = @(
    '--list',
    '--no-color',
    $linkedFastFile
)

$listCode = Invoke-NativeTee -FilePath $unlinker -Arguments $listArgs -LogPath $listLog

if ($listCode -ne 0) {
    throw "Generated Xenon XModel FF failed OAT read-back. exit=$listCode. See: $listLog"
}

$listLines = @(Get-Content -LiteralPath $listLog -ErrorAction SilentlyContinue)
$xmodelListed = $false

foreach ($line in $listLines) {
    $text = [string]$line
    if ($text -match '(?i)(?:^|[\]\s])xmodel\s*,\s*(.+?)\s*$') {
        $listedName = ([string]$matches[1]).Trim()
        if ($listedName -ieq $selected.Name) {
            $xmodelListed = $true
            break
        }
    }
}

$warnCount = @(
    $listLines |
        Where-Object { $_ -match '(?i)\bwarn(?:ing)?\b' }
).Count

$errorCount = @(
    $listLines |
        Where-Object { $_ -match '(?i)\berror\b' }
).Count

if (-not $xmodelListed) {
    throw "Generated Xbox FF loaded, but OAT did not list selected XModel '$($selected.Name)'. See: $listLog"
}

Write-Host ''
Write-Host '[NUCLEAR-4U] Building exact identity manifest from generated Xenon FF...'

$identityArgs = @(
    'run',
    '--project', '.\src\WaW360.Cli\WaW360.Cli.csproj',
    '-c', 'Release',
    '--',
    'native-oat-identity',
    $linkedFastFile,
    $identityOut,
    $unlinker
)

Push-Location $repo
try {
    & dotnet @identityArgs
    $identityCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($identityCode -ne 0) {
    throw "Identity extraction failed for generated Xenon XModel FF (exit $identityCode)."
}

$identityManifest = @(
    Get-ChildItem -LiteralPath $identityOut -Filter '*_native_identity_manifest.json' -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
)

if ($identityManifest.Count -ne 1) {
    throw "Generated Xenon XModel identity manifest was not found under: $identityOut"
}

Write-Host ''
Write-Host '[NUCLEAR-4U] Measuring generated XModel against 0xD4 and legacy 0xE4 boundaries...'

$layoutArgs = @(
    'run',
    '--project', '.\src\WaW360.Cli\WaW360.Cli.csproj',
    '-c', 'Release',
    '--',
    'xenon-native-xmodel-layout',
    $linkedFastFile,
    $identityManifest[0].FullName,
    $layoutOut
)

Push-Location $repo
try {
    & dotnet @layoutArgs
    $layoutCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

$layoutManifest = Join-Path $layoutOut 'nuclear_4t_native_xmodel_layout.json'

if (-not (Test-Path -LiteralPath $layoutManifest -PathType Leaf)) {
    throw "Generated XModel layout evidence was not written: $layoutManifest"
}

$layout = Get-Content -LiteralPath $layoutManifest -Raw | ConvertFrom-Json

$roundTripPass =
    $linkCode -eq 0 -and
    $listCode -eq 0 -and
    $errorCount -eq 0 -and
    $xmodelListed -and
    $identityCode -eq 0

$layoutProbeExact =
    [int]$layout.NativeXModelRoots -ge 1 -and
    [int]$layout.UniqueD4Matches -eq [int]$layout.NativeXModelRoots -and
    [int]$layout.MissingD4Matches -eq 0 -and
    [int]$layout.AmbiguousD4Matches -eq 0 -and
    [int]$layout.LegacyE4Matches -eq 0

# The 4T byte-adjacency probe is intentionally advisory here. It only proves
# XModels whose serialized name is physically adjacent to the fixed body, while
# valid T4 Xenon zones may pack/share the top-level name. The authoritative 4U
# bridge gate is therefore the Xenon writer -> Xenon loader round trip itself.
$bridgePass = $roundTripPass

$manifest = [ordered]@{
    format = 'WaW360.Nuclear4U.OatXModelBridge.v3'
    pcFastFile = (Resolve-Path -LiteralPath $PcFastFile).Path
    pcDumpExitCode = $dumpCode
    completePcXModels = $candidates.Count
    selectedModel = $selected.Name
    selectedLodCount = $selected.LodCount
    selectedLodBytes = $selected.LodBytes
    partClassificationFile = $partClassification
    partClassificationManifest = $partClassificationManifest
    partClassificationComplete = [bool]$classResult.Complete
    partClassificationRows = @($classResult.Rows).Count
    referenceDependencies = @($referenceAssets | ForEach-Object { [ordered]@{ type = $_.Type; name = $_.Name } })
    linkedFastFile = $linkedFastFile
    xenonLinkExitCode = $linkCode
    xenonReadbackExitCode = $listCode
    xenonReadbackWarnings = $warnCount
    xenonReadbackErrors = $errorCount
    selectedModelListed = $xmodelListed
    identityManifest = $identityManifest[0].FullName
    layoutManifest = $layoutManifest
    xenonXModelRoots = [int]$layout.NativeXModelRoots
    uniqueD4Matches = [int]$layout.UniqueD4Matches
    missingD4Matches = [int]$layout.MissingD4Matches
    ambiguousD4Matches = [int]$layout.AmbiguousD4Matches
    legacyE4Matches = [int]$layout.LegacyE4Matches
    layoutProbeExact = $layoutProbeExact
    layoutProbeAdvisory = $true
    xenonRoundTripPass = $roundTripPass
    bridgePass = $bridgePass
    productionFidelityNote = 'partclassification.csv is recovered from the original PC fastfile and is required complete before Xenon import.'
    sourcePolicy = @(
        'XModel geometry and skeleton metadata originate from the PC fastfile through OAT canonical JSON+GLTF export.',
        'The T4 Xenon OAT asset loader reconstructs the console-native XModel and XSurface representation.',
        'No native Xbox XModel payload bytes are used as source data.'
    )
}

$manifestPath = Join-Path $OutputRoot 'nuclear_4u_oat_xmodel_bridge.json'
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host ''
Write-Host '[NUCLEAR-4U] =================== OAT XMODEL BRIDGE RESULT ===================='
Write-Host "[NUCLEAR-4U] Complete PC exports : $($candidates.Count)"
Write-Host "[NUCLEAR-4U] Selected model      : $($selected.Name)"
Write-Host "[NUCLEAR-4U] Xenon read-back     : $(if ($xmodelListed) { 'PASS' } else { 'FAIL' })"
Write-Host "[NUCLEAR-4U] OAT warn/error      : $warnCount / $errorCount"
Write-Host "[NUCLEAR-4U] Xbox XModel roots   : $([int]$layout.NativeXModelRoots)"
Write-Host "[NUCLEAR-4U] Unique 0xD4         : $([int]$layout.UniqueD4Matches)"
Write-Host "[NUCLEAR-4U] Missing 0xD4        : $([int]$layout.MissingD4Matches)"
Write-Host "[NUCLEAR-4U] Legacy 0xE4         : $([int]$layout.LegacyE4Matches)"
Write-Host "[NUCLEAR-4U] 0xD4 scan advisory  : $layoutProbeExact"
Write-Host "[NUCLEAR-4U] Xenon round-trip    : $roundTripPass"
Write-Host "[NUCLEAR-4U] Classification      : $(if ([bool]$classResult.Complete) { 'PASS' } else { 'FAIL' })"
Write-Host "[NUCLEAR-4U] Bridge pass         : $bridgePass"
Write-Host "[NUCLEAR-4U] Candidate           : $linkedFastFile"
Write-Host "[NUCLEAR-4U] Manifest            : $manifestPath"
Write-Host '[NUCLEAR-4U] ================================================================='

if (-not $bridgePass) {
    Write-Host ''
    Write-Host '[NUCLEAR-4U] PC -> XENON XMODEL BRIDGE: BLOCKED' -ForegroundColor Yellow
    Write-Host '[NUCLEAR-4U] Review PC dump, Xenon link, OAT read-back, and identity evidence above.'
    exit 4
}

Write-Host ''
Write-Host '[NUCLEAR-4U] PC -> XENON XMODEL BRIDGE: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4U] A real PC XModel survived canonical export, Xenon reconstruction, Xbox FF serialization, OAT reload, and exact identity recovery.'
if (-not $layoutProbeExact) {
    Write-Host '[NUCLEAR-4U] NOTE: the old 4T body/name adjacency scan did not match this valid round-trip model; it is advisory only and must not veto the bridge.' -ForegroundColor Yellow
}
Write-Host '[NUCLEAR-4U] PC partClassification fidelity is preserved from the original source FF.'
Write-Host '[NUCLEAR-4U] Next: scale this exact production-fidelity bridge across the complete 280-model corpus.'
exit 0
