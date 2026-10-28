[CmdletBinding()]
param(
    [string]$PcMapsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps',
    [string]$MapName = 'nazi_zombie_FRUmansion',
    [string]$Stage5EManifest = '',
    [string]$Stage5GManifest = '',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repo=(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

if ([string]::IsNullOrWhiteSpace($Stage5EManifest)) {
    $Stage5EManifest=Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_5e_frumansion_pc_ff_identity_census\nuclear_5e_frumansion_pc_ff_identity_census.json'
}
if ([string]::IsNullOrWhiteSpace($Stage5GManifest)) {
    $Stage5GManifest=Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_5g_native_iwi_retail_equivalence\nuclear_5g_native_iwi_retail_equivalence.json'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot=Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_5h_external_pc_image_source_recovery'
}

foreach ($required in @($Stage5EManifest,$Stage5GManifest)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required manifest not found: $required"
    }
}

function Normalize-ImageIdentity {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }

    $v=$Value.Trim().Trim('"').Replace('\','/').TrimStart('/')
    while ($v.StartsWith(',', [StringComparison]::Ordinal)) { $v=$v.Substring(1) }

    $marker=$v.ToLowerInvariant().LastIndexOf('/images/')
    if ($marker -ge 0) {
        $v=$v.Substring($marker+8)
    }
    elseif ($v.StartsWith('images/',[StringComparison]::OrdinalIgnoreCase)) {
        $v=$v.Substring(7)
    }

    foreach ($ext in @('.iwi','.dds')) {
        if ($v.EndsWith($ext,[StringComparison]::OrdinalIgnoreCase)) {
            $v=$v.Substring(0,$v.Length-$ext.Length)
            break
        }
    }

    if ($v.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)) {
        $v=$v.Substring(0,$v.Length-3)
    }

    return $v.ToLowerInvariant()
}

function Add-Root {
    param(
        [System.Collections.Generic.List[object]]$Roots,
        [hashtable]$Seen,
        [string]$Path,
        [string]$Kind
    )

    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return }

    $full=(Resolve-Path -LiteralPath $Path).Path
    $key=$full.ToLowerInvariant()
    if ($Seen.ContainsKey($key)) { return }

    $Seen[$key]=$true
    $Roots.Add([pscustomobject]@{ Kind=$Kind; Path=$full })
}

function Get-RelativePathCompat {
    param([string]$BasePath,[string]$TargetPath)

    $base=[IO.Path]::GetFullPath($BasePath)
    if (-not $base.EndsWith('\')) { $base += '\' }

    $target=[IO.Path]::GetFullPath($TargetPath)
    if ($target.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)) {
        return $target.Substring($base.Length)
    }

    return $target
}

$stage5e=Get-Content -LiteralPath $Stage5EManifest -Raw | ConvertFrom-Json
$stage5g=Get-Content -LiteralPath $Stage5GManifest -Raw | ConvertFrom-Json

if ([string]$stage5e.format -ne 'WaW360.Nuclear5E.FRumansionPcFastFileIdentityCensus.v1' -or
    [int]$stage5e.targets -ne 43 -or
    [int]$stage5e.foundInFrumansionPcFastFiles -ne 43) {
    throw 'Stage 5H requires the passing 43/43 Stage 5E manifest.'
}
if ([string]$stage5g.format -ne 'WaW360.Nuclear5G.NativeIwiRetailEquivalence.v1' -or
    [int]$stage5g.targets -ne 43) {
    throw 'Stage 5H requires the Stage 5G native-IWI equivalence manifest.'
}

$targets=New-Object 'System.Collections.Generic.List[object]'
foreach ($row in @($stage5e.images | Sort-Object Index)) {
    $pc=[string]$row.PcImage
    if ([string]::IsNullOrWhiteSpace($pc)) {
        throw "Mapped PC identity missing for root #$([int]$row.Index)."
    }

    $targets.Add([pscustomobject]@{
        Index=[int]$row.Index
        NativeName=[string]$row.NativeName
        PcImage=$pc
        PcKey=Normalize-ImageIdentity -Value $pc
    })
}
if ($targets.Count -ne 43) {
    throw "Expected 43 mapped PC image identities; found $($targets.Count)."
}

$targetByKey=@{}
foreach ($target in $targets.ToArray()) {
    $key=[string]$target.PcKey
    if (-not $targetByKey.ContainsKey($key)) {
        $targetByKey[$key]=New-Object 'System.Collections.Generic.List[object]'
    }
    $targetByKey[$key].Add($target)
}

$roots=New-Object 'System.Collections.Generic.List[object]'
$seen=@{}

Add-Root -Roots $roots -Seen $seen -Path (Join-Path $PcMapsRoot $MapName) -Kind 'pc-map-folder'
Add-Root -Roots $roots -Seen $seen -Path $PcMapsRoot -Kind 'pc-maps-root'
Add-Root -Roots $roots -Seen $seen -Path (Join-Path $env:LOCALAPPDATA "Activision\CoDWaW\mods\$MapName") -Kind 'local-mod-map'
Add-Root -Roots $roots -Seen $seen -Path (Join-Path $env:LOCALAPPDATA "Activision\CoDWaW\usermaps\$MapName") -Kind 'local-usermap-map'
Add-Root -Roots $roots -Seen $seen -Path (Join-Path $env:LOCALAPPDATA 'Activision\CoDWaW\mods') -Kind 'local-mods-root'
Add-Root -Roots $roots -Seen $seen -Path (Join-Path $env:LOCALAPPDATA 'Activision\CoDWaW\usermaps') -Kind 'local-usermaps-root'
Add-Root -Roots $roots -Seen $seen -Path (Join-Path $env:APPDATA "Activision\CoDWaW\mods\$MapName") -Kind 'roaming-mod-map'
Add-Root -Roots $roots -Seen $seen -Path (Join-Path $env:APPDATA "Activision\CoDWaW\usermaps\$MapName") -Kind 'roaming-usermap-map'
Add-Root -Roots $roots -Seen $seen -Path (Join-Path $env:APPDATA 'Activision\CoDWaW\mods') -Kind 'roaming-mods-root'
Add-Root -Roots $roots -Seen $seen -Path (Join-Path $env:APPDATA 'Activision\CoDWaW\usermaps') -Kind 'roaming-usermaps-root'

if ($roots.Count -eq 0) {
    throw 'No PC map/mod/usermap source roots exist.'
}

$hits=New-Object 'System.Collections.Generic.List[object]'
$inventory=New-Object 'System.Collections.Generic.List[object]'

Write-Host '[NUCLEAR-5H-IMG] ============================================================'
Write-Host '[NUCLEAR-5H-IMG] EXTERNAL PC IMAGE SOURCE RECOVERY'
Write-Host '[NUCLEAR-5H-IMG] ============================================================'
Write-Host "[NUCLEAR-5H-IMG] Targets      : $($targets.Count)"
Write-Host "[NUCLEAR-5H-IMG] Source roots : $($roots.Count)"
Write-Host '[NUCLEAR-5H-IMG] Searching loose IWI/DDS + every IWD using the proven PC identities.'
Write-Host ''

foreach ($root in $roots.ToArray()) {
    $loose=0
    $iwds=0
    $iwdImages=0

    foreach ($pattern in @('*.iwi','*.dds')) {
        foreach ($file in @(Get-ChildItem -LiteralPath ([string]$root.Path) -Filter $pattern -File -Recurse -ErrorAction SilentlyContinue)) {
            $loose++
            $relative=Get-RelativePathCompat -BasePath ([string]$root.Path) -TargetPath $file.FullName
            $key=Normalize-ImageIdentity -Value $relative
            if (-not $targetByKey.ContainsKey($key)) { continue }

            $hits.Add([pscustomobject]@{
                PcKey=$key
                RootKind=[string]$root.Kind
                SourceKind='loose'
                SourcePath=$file.FullName
                ArchiveEntry=''
                Bytes=[int64]$file.Length
                Extension=$file.Extension.ToLowerInvariant()
            })
        }
    }

    foreach ($iwd in @(Get-ChildItem -LiteralPath ([string]$root.Path) -Filter '*.iwd' -File -Recurse -ErrorAction SilentlyContinue | Sort-Object FullName)) {
        $iwds++
        $zip=$null
        try {
            $zip=[IO.Compression.ZipFile]::OpenRead($iwd.FullName)
            foreach ($entry in $zip.Entries) {
                $ext=[IO.Path]::GetExtension($entry.FullName)
                if ($ext -ine '.iwi' -and $ext -ine '.dds') { continue }

                $iwdImages++
                $key=Normalize-ImageIdentity -Value $entry.FullName
                if (-not $targetByKey.ContainsKey($key)) { continue }

                $hits.Add([pscustomobject]@{
                    PcKey=$key
                    RootKind=[string]$root.Kind
                    SourceKind='iwd'
                    SourcePath=$iwd.FullName
                    ArchiveEntry=$entry.FullName
                    Bytes=[int64]$entry.Length
                    Extension=$ext.ToLowerInvariant()
                })
            }
        }
        catch {
            Write-Host "[NUCLEAR-5H-IMG] WARN: could not read $($iwd.FullName): $($_.Exception.Message)" -ForegroundColor Yellow
        }
        finally {
            if ($null -ne $zip) { $zip.Dispose() }
        }
    }

    $inventory.Add([pscustomobject]@{
        RootKind=[string]$root.Kind
        Root=[string]$root.Path
        LooseImages=$loose
        Iwds=$iwds
        IwdImageEntries=$iwdImages
    })

    Write-Host ("[NUCLEAR-5H-IMG] {0,-22} loose={1,5} IWDs={2,3} IWD-images={3,6}" -f
        [string]$root.Kind,$loose,$iwds,$iwdImages)
}

$byKey=@{}
foreach ($hit in $hits.ToArray()) {
    $key=[string]$hit.PcKey
    if (-not $byKey.ContainsKey($key)) {
        $byKey[$key]=New-Object 'System.Collections.Generic.List[object]'
    }
    $byKey[$key].Add($hit)
}

$results=New-Object 'System.Collections.Generic.List[object]'
$resolved=0
$missing=0
$ambiguous=0

foreach ($target in $targets.ToArray()) {
    $choices=@()
    if ($byKey.ContainsKey([string]$target.PcKey)) {
        $choices=@(
            $byKey[[string]$target.PcKey].ToArray() |
            Sort-Object @{Expression={if ([string]$_.SourceKind -eq 'iwd') { 0 } else { 1 }}},
                        @{Expression={if ([string]$_.Extension -eq '.iwi') { 0 } else { 1 }}},
                        @{Expression={-[int64]$_.Bytes}},
                        RootKind,SourcePath,ArchiveEntry
        )
    }

    if ($choices.Count -gt 0) { $resolved++ } else { $missing++ }

    $physicalSources=@(
        $choices |
        ForEach-Object {
            if ([string]$_.SourceKind -eq 'iwd') {
                "$($_.SourcePath)::$($_.ArchiveEntry)"
            }
            else {
                [string]$_.SourcePath
            }
        } |
        Sort-Object -Unique
    )
    if ($physicalSources.Count -gt 1) { $ambiguous++ }

    $results.Add([pscustomobject]@{
        Index=[int]$target.Index
        NativeName=[string]$target.NativeName
        PcImage=[string]$target.PcImage
        Found=$choices.Count -gt 0
        CandidateCount=$choices.Count
        Candidates=[object[]]$choices
    })
}

$noNativeRows=@($stage5g.images | Where-Object { [string]$_.Status -eq 'no-native-iwi-fingerprint' })

if (Test-Path -LiteralPath $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$manifest=[ordered]@{
    format='WaW360.Nuclear5H.ExternalPcImageSourceRecovery.v1'
    stage5EManifest=(Resolve-Path -LiteralPath $Stage5EManifest).Path
    stage5GManifest=(Resolve-Path -LiteralPath $Stage5GManifest).Path
    targets=$targets.Count
    sourceRoots=$roots.Count
    sourceCandidates=$hits.Count
    resolvedExternalPcImages=$resolved
    unresolvedExternalPcImages=$missing
    ambiguousExternalPcImages=$ambiguous
    nativeIwiFingerprintMissingTargets=$noNativeRows.Count
    nativeIwiFingerprintMissing=[object[]]$noNativeRows
    inventory=[object[]]$inventory.ToArray()
    images=[object[]]$results.ToArray()
    policy=@(
        'Stage 5H searches only user-supplied/installed PC map, mod, usermap, IWD, IWI, and DDS sources.',
        'It uses the 43 PC image identities proven by Stage 5B/5E rather than final Xbox-generated names.',
        'No native/oracle payload bytes are used as production source.',
        'If a target remains unresolved here, the supplied PC map package is missing the external image payload expected by the fastfile.'
    )
}

$jsonPath=Join-Path $OutputRoot 'nuclear_5h_external_pc_image_source_recovery.json'
$txtPath=Join-Path $OutputRoot 'nuclear_5h_external_pc_image_source_recovery.txt'
$manifest | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$report=New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 5H - EXTERNAL PC IMAGE SOURCE RECOVERY')
$report.Add(('=' * 104))
$report.Add(("Targets:                         {0:N0}" -f $targets.Count))
$report.Add(("Source roots scanned:            {0:N0}" -f $roots.Count))
$report.Add(("Source candidates:               {0:N0}" -f $hits.Count))
$report.Add(("Resolved external PC images:     {0:N0}" -f $resolved))
$report.Add(("Unresolved external PC images:   {0:N0}" -f $missing))
$report.Add(("Ambiguous external PC images:    {0:N0}" -f $ambiguous))
$report.Add(("No-native-IWI target count:      {0:N0}" -f $noNativeRows.Count))
$report.Add('')
if ($noNativeRows.Count -gt 0) {
    $report.Add('NO NATIVE IWI FINGERPRINT')
    foreach ($row in $noNativeRows) {
        $report.Add(("  root #{0,4}: {1} | PC={2}" -f $row.Index,$row.NativeName,$row.PcImage))
    }
    $report.Add('')
}
$report.Add('TARGETS')
foreach ($row in $results.ToArray() | Sort-Object Index) {
    $report.Add(("  root #{0,4}: {1}" -f $row.Index,$row.NativeName))
    $report.Add(("           PC identity: {0}" -f $row.PcImage))
    if ([bool]$row.Found) {
        foreach ($candidate in @($row.Candidates)) {
            $suffix=$(if ([string]$candidate.SourceKind -eq 'iwd') { " :: $($candidate.ArchiveEntry)" } else { '' })
            $report.Add(("           SOURCE <- {0}/{1}: {2}{3}" -f $candidate.RootKind,$candidate.SourceKind,$candidate.SourcePath,$suffix))
        }
    }
    else {
        $report.Add('           SOURCE <- NOT FOUND')
    }
}
[IO.File]::WriteAllLines($txtPath,[string[]]$report.ToArray(),[Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host '[NUCLEAR-5H-IMG] ====================== RESULT ==============================='
Write-Host "[NUCLEAR-5H-IMG] Targets                       : $($targets.Count)"
Write-Host "[NUCLEAR-5H-IMG] Source roots scanned          : $($roots.Count)"
Write-Host "[NUCLEAR-5H-IMG] Source candidates             : $($hits.Count)"
Write-Host "[NUCLEAR-5H-IMG] Resolved external PC images   : $resolved"
Write-Host "[NUCLEAR-5H-IMG] Unresolved external PC images : $missing"
Write-Host "[NUCLEAR-5H-IMG] Ambiguous external PC images  : $ambiguous"
Write-Host "[NUCLEAR-5H-IMG] No-native-IWI targets         : $($noNativeRows.Count)"
foreach ($row in $noNativeRows) {
    Write-Host ("[NUCLEAR-5H-IMG]   missing native IWI root #{0}: {1} | PC={2}" -f $row.Index,$row.NativeName,$row.PcImage) -ForegroundColor Yellow
}
Write-Host "[NUCLEAR-5H-IMG] Manifest                      : $jsonPath"
Write-Host "[NUCLEAR-5H-IMG] Report                        : $txtPath"
Write-Host '[NUCLEAR-5H-IMG] ================================================================='
Write-Host '[NUCLEAR-5H-IMG] PASS: external-PC source recovery census complete.' -ForegroundColor Green
