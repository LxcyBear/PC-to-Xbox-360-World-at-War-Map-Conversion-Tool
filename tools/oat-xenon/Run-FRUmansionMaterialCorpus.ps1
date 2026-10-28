[CmdletBinding()]
param(
    [string]$PcMapsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps',
    [string]$MapName = 'nazi_zombie_FRUmansion',
    [string]$NativeControlDirectory = 'C:\Users\jorda\OneDrive\Desktop\working frumansion',
    [string]$BridgeRoot = '',
    [string]$OutputRoot = '',
    [switch]$RefreshBridge
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory=$true)][string]$BasePath,
        [Parameter(Mandatory=$true)][string]$TargetPath
    )

    $trimChars = [char[]]@([char]92, [char]47)
    $baseFull = [IO.Path]::GetFullPath($BasePath).TrimEnd($trimChars) +
        [IO.Path]::DirectorySeparatorChar
    $targetFull = [IO.Path]::GetFullPath($TargetPath)

    if (-not $targetFull.StartsWith($baseFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Target path is outside base path: base='$baseFull' target='$targetFull'"
    }

    return $targetFull.Substring($baseFull.Length)
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$linker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Linker.exe'
$unlinker = Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
$bridgeRunner = Join-Path $PSScriptRoot 'Run-FRUmansionNuclearMaterialBridge.ps1'

if ([string]::IsNullOrWhiteSpace($BridgeRoot)) {
    $BridgeRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_material_techset_bridge'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4z_material_corpus'
}

$bridgeManifest = Join-Path $BridgeRoot 'material_techset_bridge.json'

foreach ($required in @($linker, $unlinker, $bridgeRunner)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required material-corpus input is missing: $required"
    }
}

if ($RefreshBridge.IsPresent -or -not (Test-Path -LiteralPath $bridgeManifest -PathType Leaf)) {
    Write-Host '[NUCLEAR-4Z-A] Material bridge is missing/stale; rebuilding PC semantic material dump...'
    $bridgeOutputParent = Split-Path -Parent $BridgeRoot
    & powershell -ExecutionPolicy Bypass -File $bridgeRunner -PcMapsRoot $PcMapsRoot -MapName $MapName -NativeControlDirectory $NativeControlDirectory -OutputRoot $bridgeOutputParent
    if ($LASTEXITCODE -ne 0 -or -not $?) {
        throw "Material bridge failed with exit code $LASTEXITCODE."
    }
}

$bridge = Get-Content -LiteralPath $bridgeManifest -Raw | ConvertFrom-Json
if (-not [bool]$bridge.Verified) {
    throw '4Z-A requires a verified material/TechniqueSet bridge.'
}

$pcDump = [string]$bridge.PcDumpDirectory
if (-not (Test-Path -LiteralPath $pcDump -PathType Container)) {
    throw "PC material dump referenced by bridge does not exist: $pcDump"
}

$materialsRoot = Get-ChildItem -LiteralPath $pcDump -Directory -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq 'materials' } |
    Sort-Object { $_.FullName.Length } |
    Select-Object -First 1

if ($null -eq $materialsRoot) {
    throw "Could not find OAT materials/ directory under: $pcDump"
}

$materialFiles = @(
    Get-ChildItem -LiteralPath $materialsRoot.FullName -Filter '*.json' -File -Recurse |
        Sort-Object FullName
)
if ($materialFiles.Count -eq 0) {
    throw "PC OAT material dump contains zero material JSON files: $($materialsRoot.FullName)"
}

$materialRows = New-Object System.Collections.Generic.List[object]
$techsets = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$images = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$waterTextures = 0
$badStateEntryCounts = 0
$totalTextures = 0
$totalConstants = 0
$totalStateBits = 0

foreach ($file in $materialFiles) {
    $relative = Get-RelativePathCompat -BasePath $materialsRoot.FullName -TargetPath $file.FullName.Replace('\','/')
    if (-not $relative.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase)) {
        continue
    }
    $name = $relative.Substring(0, $relative.Length - 5)
    if (-not $names.Add($name)) {
        throw "Duplicate PC material identity in OAT dump: $name"
    }

    $doc = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json

    $techset = ''
    if ($doc.PSObject.Properties.Name -contains 'techniqueSet') {
        $techset = [string]$doc.techniqueSet
        if (-not [string]::IsNullOrWhiteSpace($techset)) {
            [void]$techsets.Add($techset)
        }
    }

    $textures = @()
    if ($doc.PSObject.Properties.Name -contains 'textures' -and $null -ne $doc.textures) {
        $textures = @($doc.textures)
    }
    foreach ($texture in $textures) {
        if ($null -eq $texture) { continue }
        if ($texture.PSObject.Properties.Name -contains 'image') {
            $image = [string]$texture.image
            if (-not [string]::IsNullOrWhiteSpace($image)) {
                [void]$images.Add($image)
            }
        }
        if ($texture.PSObject.Properties.Name -contains 'water' -and $null -ne $texture.water) {
            $waterTextures++
        }
    }

    $constants = @()
    if ($doc.PSObject.Properties.Name -contains 'constants' -and $null -ne $doc.constants) {
        $constants = @($doc.constants)
    }

    $stateBits = @()
    if ($doc.PSObject.Properties.Name -contains 'stateBits' -and $null -ne $doc.stateBits) {
        $stateBits = @($doc.stateBits)
    }

    $stateEntries = @()
    if ($doc.PSObject.Properties.Name -contains 'stateBitsEntry' -and $null -ne $doc.stateBitsEntry) {
        $stateEntries = @($doc.stateBitsEntry)
    }
    if ($stateEntries.Count -ne 59) {
        $badStateEntryCounts++
    }

    $totalTextures += $textures.Count
    $totalConstants += $constants.Count
    $totalStateBits += $stateBits.Count

    $materialRows.Add([pscustomobject]@{
        Name = $name
        Json = $file.FullName
        TechniqueSet = $techset
        TextureCount = $textures.Count
        ConstantCount = $constants.Count
        StateBitsCount = $stateBits.Count
        StateBitsEntryCount = $stateEntries.Count
        WaterTextures = @($textures | Where-Object {
            $null -ne $_ -and
            $_.PSObject.Properties.Name -contains 'water' -and
            $null -ne $_.water
        }).Count
    })
}

if ($badStateEntryCounts -ne 0) {
    throw "PC material JSON stateBitsEntry width mismatch on $badStateEntryCounts material(s); T4 expects 59 entries."
}

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$zoneSource = Join-Path $OutputRoot 'zone_source'
$workspace = Join-Path $OutputRoot 'workspace'
$linkedRoot = Join-Path $OutputRoot 'linked'
$redumpRoot = Join-Path $OutputRoot 'xenon_redump'
foreach ($dir in @($zoneSource, $workspace, $linkedRoot, $redumpRoot)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}

$project = 'nuclear_4za_material_corpus'
$zoneFile = Join-Path $zoneSource ($project + '.zone')
$zoneLines = @(
    '>game,T4'
    '>platform,xbox360'
    ('>name,{0}' -f $project)
    ''
)

foreach ($name in @($techsets | Sort-Object)) {
    $zoneLines += ('techniqueset,,"{0}"' -f $name.Replace('"',''))
}
foreach ($name in @($images | Sort-Object)) {
    $zoneLines += ('image,,"{0}"' -f $name.Replace('"',''))
}
foreach ($row in @($materialRows | Sort-Object Name)) {
    $zoneLines += ('material,"{0}"' -f ([string]$row.Name).Replace('"',''))
}

[IO.File]::WriteAllLines(
    $zoneFile,
    [string[]]$zoneLines,
    [Text.UTF8Encoding]::new($false))

$linkLog = Join-Path $OutputRoot 'material_corpus_link.log'
$listLog = Join-Path $OutputRoot 'material_corpus_list.log'
$dumpLog = Join-Path $OutputRoot 'material_corpus_redump.log'

function Invoke-NativeTee {
    param(
        [Parameter(Mandatory=$true)][string]$FilePath,
        [Parameter(Mandatory=$true)][object[]]$Arguments,
        [Parameter(Mandatory=$true)][string]$LogPath
    )

    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        & $FilePath @Arguments 2>&1 |
            Tee-Object -FilePath $LogPath |
            ForEach-Object { Write-Host ([string]$_) }
        return [int]$LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

Write-Host '[NUCLEAR-4Z-A] =============================================================='
Write-Host '[NUCLEAR-4Z-A] PC MATERIAL -> XENON OAT CORPUS'
Write-Host '[NUCLEAR-4Z-A] =============================================================='
Write-Host "[NUCLEAR-4Z-A] PC material dump : $($materialsRoot.FullName)"
Write-Host "[NUCLEAR-4Z-A] Materials        : $($materialRows.Count)"
Write-Host "[NUCLEAR-4Z-A] Technique refs   : $($techsets.Count)"
Write-Host "[NUCLEAR-4Z-A] Image refs       : $($images.Count)"
Write-Host "[NUCLEAR-4Z-A] Textures         : $totalTextures"
Write-Host "[NUCLEAR-4Z-A] Constants        : $totalConstants"
Write-Host "[NUCLEAR-4Z-A] StateBits rows   : $totalStateBits"
Write-Host "[NUCLEAR-4Z-A] Water textures   : $waterTextures"
Write-Host ''
Write-Host '[NUCLEAR-4Z-A] OAT rule: T4 PC material JSON is loaded directly by the T4 Xenon material loader.'
Write-Host '[NUCLEAR-4Z-A] No native/oracle material payload bytes are used in this corpus.'
Write-Host ''

$linkArgs = @(
    '--no-color',
    '--base-folder', $workspace,
    '--source-search-path', $zoneSource,
    '--asset-search-path', $pcDump,
    '--output-folder', $linkedRoot,
    $project
)

Push-Location $workspace
try {
    $linkCode = Invoke-NativeTee -FilePath $linker -Arguments $linkArgs -LogPath $linkLog
}
finally {
    Pop-Location
}
if ($linkCode -ne 0) {
    throw "Xenon material corpus Linker failed with exit $linkCode. See: $linkLog"
}

$ffs = @(Get-ChildItem -LiteralPath $linkedRoot -Filter '*.ff' -File -Recurse)
if ($ffs.Count -ne 1) {
    throw "Expected exactly one linked material-corpus FF; found $($ffs.Count)."
}
$ff = $ffs[0].FullName

$listCode = Invoke-NativeTee -FilePath $unlinker -Arguments @('--list','--no-color',$ff) -LogPath $listLog
if ($listCode -ne 0) {
    throw "Xenon material corpus readback failed with exit $listCode. See: $listLog"
}

$listLines = @(Get-Content -LiteralPath $listLog -ErrorAction SilentlyContinue)
$summary = @($listLines | Where-Object {
    ([string]$_).Trim() -match '(?i)^Finished with\s+0\s+warnings?,\s+0\s+errors?\s*$'
})
if ($summary.Count -eq 0) {
    throw "Material corpus readback did not prove OAT 0 warnings / 0 errors. See: $listLog"
}

$listedMaterials = New-Object System.Collections.Generic.List[string]
foreach ($line in $listLines) {
    $text = ([string]$line).Trim()
    if ($text -match '(?i)^material\s*,\s*(.+?)\s*$') {
        $listedMaterials.Add(([string]$matches[1]).Trim())
    }
}

$listedSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($name in $listedMaterials) { [void]$listedSet.Add($name) }

$missing = @($names | Where-Object { -not $listedSet.Contains($_) } | Sort-Object)
$extra = @($listedSet | Where-Object { -not $names.Contains($_) } | Sort-Object)
if ($listedMaterials.Count -ne $materialRows.Count -or $missing.Count -ne 0 -or $extra.Count -ne 0) {
    throw ("Material identity readback mismatch: source={0}, listed={1}, missing=[{2}], extra=[{3}]" -f
        $materialRows.Count,
        $listedMaterials.Count,
        (($missing | Select-Object -First 12) -join ', '),
        (($extra | Select-Object -First 12) -join ', '))
}

$dumpCode = Invoke-NativeTee -FilePath $unlinker -Arguments @(
    '--no-color',
    '--include-assets','material',
    '--output-folder',$redumpRoot,
    $ff
) -LogPath $dumpLog
if ($dumpCode -ne 0) {
    throw "Xenon material corpus redump failed with exit $dumpCode. See: $dumpLog"
}

$dumpSummary = @(Get-Content -LiteralPath $dumpLog -ErrorAction SilentlyContinue | Where-Object {
    ([string]$_).Trim() -match '(?i)^Finished with\s+0\s+warnings?,\s+0\s+errors?\s*$'
})
if ($dumpSummary.Count -eq 0) {
    throw "Material corpus redump did not prove OAT 0 warnings / 0 errors. See: $dumpLog"
}

$redumpMaterialsRoot = Get-ChildItem -LiteralPath $redumpRoot -Directory -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq 'materials' } |
    Sort-Object { $_.FullName.Length } |
    Select-Object -First 1

if ($null -eq $redumpMaterialsRoot) {
    throw "OAT Xenon redump produced no materials/ directory: $redumpRoot"
}

$redumpFiles = @(Get-ChildItem -LiteralPath $redumpMaterialsRoot.FullName -Filter '*.json' -File -Recurse)
$redumpNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($file in $redumpFiles) {
    $relative = Get-RelativePathCompat -BasePath $redumpMaterialsRoot.FullName -TargetPath $file.FullName.Replace('\','/')
    [void]$redumpNames.Add($relative.Substring(0, $relative.Length - 5))
}

$redumpMissing = @($names | Where-Object { -not $redumpNames.Contains($_) } | Sort-Object)
$redumpExtra = @($redumpNames | Where-Object { -not $names.Contains($_) } | Sort-Object)
if ($redumpNames.Count -ne $materialRows.Count -or $redumpMissing.Count -ne 0 -or $redumpExtra.Count -ne 0) {
    throw ("Material redump identity mismatch: source={0}, redump={1}, missing=[{2}], extra=[{3}]" -f
        $materialRows.Count,
        $redumpNames.Count,
        (($redumpMissing | Select-Object -First 12) -join ', '),
        (($redumpExtra | Select-Object -First 12) -join ', '))
}

$manifestPath = Join-Path $OutputRoot 'nuclear_4za_material_corpus.json'
$reportPath = Join-Path $OutputRoot 'nuclear_4za_material_corpus.txt'

$result = [ordered]@{
    format = 'WaW360.Nuclear4ZA.MaterialCorpus.v1'
    pcMaterialBridgeManifest = $bridgeManifest
    pcDumpDirectory = $pcDump
    linkedFastFile = $ff
    xenonRedumpDirectory = $redumpRoot
    materials = $materialRows.Count
    listedMaterials = $listedMaterials.Count
    redumpedMaterials = $redumpNames.Count
    distinctTechniqueSetReferences = $techsets.Count
    distinctImageReferences = $images.Count
    textureRows = $totalTextures
    constantRows = $totalConstants
    stateBitsRows = $totalStateBits
    waterTextures = $waterTextures
    stateBitsEntryWidth = 59
    oatListZeroWarningsZeroErrors = $true
    oatRedumpZeroWarningsZeroErrors = $true
    exactMaterialIdentitySet = $true
    corpusPass = $true
    materialsRoot = $materialsRoot.FullName
    materialRows = @($materialRows)
    linkLog = $linkLog
    listLog = $listLog
    redumpLog = $dumpLog
}

$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$report = @(
    'NUCLEAR STAGE 4Z-A - PC MATERIAL -> XENON OAT CORPUS'
    '=========================================================================='
    "materials=$($materialRows.Count)"
    "listedMaterials=$($listedMaterials.Count)"
    "redumpedMaterials=$($redumpNames.Count)"
    "distinctTechniqueSetReferences=$($techsets.Count)"
    "distinctImageReferences=$($images.Count)"
    "textureRows=$totalTextures"
    "constantRows=$totalConstants"
    "stateBitsRows=$totalStateBits"
    "waterTextures=$waterTextures"
    'stateBitsEntryWidth=59'
    'oatListZeroWarningsZeroErrors=True'
    'oatRedumpZeroWarningsZeroErrors=True'
    'exactMaterialIdentitySet=True'
    'corpusPass=True'
    ''
    'Production source: PC OAT material JSON only.'
    'Native material payload bytes are not copied.'
    'Next: extract relocatable Xenon Material graphs and prove exact standalone replay.'
)
[IO.File]::WriteAllLines($reportPath, [string[]]$report, [Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-4Z-A] ================= MATERIAL CORPUS RESULT ======================'
Write-Host "[NUCLEAR-4Z-A] Materials        : $($materialRows.Count)"
Write-Host "[NUCLEAR-4Z-A] Listed           : $($listedMaterials.Count)"
Write-Host "[NUCLEAR-4Z-A] Redumped         : $($redumpNames.Count)"
Write-Host "[NUCLEAR-4Z-A] Technique refs   : $($techsets.Count)"
Write-Host "[NUCLEAR-4Z-A] Image refs       : $($images.Count)"
Write-Host "[NUCLEAR-4Z-A] Water textures   : $waterTextures"
Write-Host '[NUCLEAR-4Z-A] OAT list 0/0     : True'
Write-Host '[NUCLEAR-4Z-A] OAT redump 0/0   : True'
Write-Host '[NUCLEAR-4Z-A] Identity set exact: True'
Write-Host '[NUCLEAR-4Z-A] Corpus pass      : True'
Write-Host "[NUCLEAR-4Z-A] Manifest         : $manifestPath"
Write-Host '[NUCLEAR-4Z-A] ================================================================='

Write-Host ''
Write-Host '[NUCLEAR-4Z-A] PC -> XENON MATERIAL CORPUS: PASS' -ForegroundColor Green
Write-Host '[NUCLEAR-4Z-A] Every PC OAT material in the map linked as a T4 Xenon material and reopened/redumped cleanly.'
Write-Host '[NUCLEAR-4Z-A] Next: relocatable Material graph extraction + exact replay canary.'
exit 0
