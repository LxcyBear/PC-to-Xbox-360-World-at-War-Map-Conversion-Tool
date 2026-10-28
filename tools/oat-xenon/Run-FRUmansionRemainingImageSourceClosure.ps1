[CmdletBinding()]
param(
    [string]$ReadinessManifest = '',
    [string]$ProductionImageGraphManifest = '',
    [string]$ImageReferenceProofManifest = '',
    [string]$NativeIdentityManifest = '',
    [string]$PcMapRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\PC Maps\nazi_zombie_FRUmansion',
    [string]$MapAssetsRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\map assets',
    [string]$UgxRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\UGX',
    [string]$ZctRoot = 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\ZCT',
    [string]$OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($ReadinessManifest)) { $ReadinessManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4f_full_content_readiness\nuclear_4f_full_content_readiness.json' }
if ([string]::IsNullOrWhiteSpace($ProductionImageGraphManifest)) { $ProductionImageGraphManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4t_production_image_graph\nuclear_4t_production_image_graph.json' }
if ([string]::IsNullOrWhiteSpace($ImageReferenceProofManifest)) { $ImageReferenceProofManifest = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4x_xbox_image_reference_proof\nuclear_4x_xbox_image_reference_proof.json' }
if ([string]::IsNullOrWhiteSpace($NativeIdentityManifest)) {
    $identityDir = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_xenon_root_binding\native_identity'
    $found = @(Get-ChildItem -LiteralPath $identityDir -Filter '*_native_identity_manifest.json' -File -ErrorAction SilentlyContinue | Sort-Object FullName | Select-Object -First 1)
    if ($found.Count -ne 1) { throw "Could not identify one native identity manifest under: $identityDir" }
    $NativeIdentityManifest = $found[0].FullName
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_5a_image_source_closure' }

foreach ($required in @($ReadinessManifest,$ProductionImageGraphManifest,$ImageReferenceProofManifest,$NativeIdentityManifest)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required manifest not found: $required" }
}

function Normalize-ImageIdentity {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
    $v = $Value.Trim().Trim('"').Replace('\','/').TrimStart('/')
    while ($v.StartsWith(',', [StringComparison]::Ordinal)) { $v = $v.Substring(1) }
    $marker = $v.ToLowerInvariant().LastIndexOf('/images/')
    if ($marker -ge 0) { $v = $v.Substring($marker + 8) }
    elseif ($v.StartsWith('images/', [StringComparison]::OrdinalIgnoreCase)) { $v = $v.Substring(7) }
    if ($v.EndsWith('.iwi', [StringComparison]::OrdinalIgnoreCase) -or $v.EndsWith('.dds', [StringComparison]::OrdinalIgnoreCase)) { $v = $v.Substring(0, $v.Length - 4) }
    return $v.ToLowerInvariant()
}

function Get-FamilyKey {
    param([string]$Value)
    $v = Normalize-ImageIdentity -Value $Value
    if ([string]::IsNullOrWhiteSpace($v)) { return '' }
    if ($v.StartsWith('~', [StringComparison]::Ordinal)) { $v = $v.Substring(1) }
    $v = [regex]::Replace($v, '~[0-9a-f]{8}$', '', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $cut = $v.IndexOf('&'); if ($cut -ge 0) { $v = $v.Substring(0, $cut) }
    $cut = $v.IndexOf('$'); if ($cut -ge 0) { $v = $v.Substring(0, $cut) }
    $v = [regex]::Replace($v, '_s-rg(?:b)?$', '', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $v = [regex]::Replace($v, '_(?:c|n|s|spec|normal|color|diffuse)$', '', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    return $v.Trim('_','-','~')
}

function Get-MatchRank {
    param([string]$Target,[string]$Candidate)
    $t = Normalize-ImageIdentity -Value $Target; $c = Normalize-ImageIdentity -Value $Candidate
    if ([string]::IsNullOrWhiteSpace($t) -or [string]::IsNullOrWhiteSpace($c)) { return 999 }
    if ($t.Equals($c, [StringComparison]::OrdinalIgnoreCase)) { return 0 }
    $tf = Get-FamilyKey -Value $t; $cf = Get-FamilyKey -Value $c
    if (-not [string]::IsNullOrWhiteSpace($tf) -and -not [string]::IsNullOrWhiteSpace($cf)) {
        if ($tf.Equals($cf, [StringComparison]::OrdinalIgnoreCase)) { return 1 }
        if ($tf.Length -ge 12 -and ($cf.StartsWith($tf, [StringComparison]::OrdinalIgnoreCase) -or $tf.StartsWith($cf, [StringComparison]::OrdinalIgnoreCase))) { return 2 }
    }
    return 999
}

function Add-SourceCandidate {
    param([System.Collections.Generic.List[object]]$Candidates,[object[]]$Targets,[string]$SourceArea,[string]$SourceKind,[string]$SourcePath,[string]$ArchiveEntry,[int64]$Bytes,[string]$Identity)
    $normalized = Normalize-ImageIdentity -Value $Identity
    if ([string]::IsNullOrWhiteSpace($normalized)) { return }
    foreach ($target in $Targets) {
        $rank = Get-MatchRank -Target ([string]$target.Name) -Candidate $normalized
        if ($rank -ge 999) { continue }
        $Candidates.Add([pscustomobject]@{
            TargetIndex=[int]$target.Index; TargetName=[string]$target.Name; MatchRank=$rank;
            MatchClass=$(if ($rank -eq 0) { 'exact' } elseif ($rank -eq 1) { 'family-exact' } else { 'family-prefix' });
            SourceArea=$SourceArea; SourceKind=$SourceKind; SourcePath=$SourcePath; ArchiveEntry=$ArchiveEntry; Bytes=$Bytes;
            CandidateIdentity=$normalized; TargetFamily=(Get-FamilyKey -Value ([string]$target.Name)); CandidateFamily=(Get-FamilyKey -Value $normalized)
        })
    }
}

function Scan-SourceRoot {
    param([System.Collections.Generic.List[object]]$Candidates,[object[]]$Targets,[string]$Area,[string]$Root)
    if ([string]::IsNullOrWhiteSpace($Root) -or -not (Test-Path -LiteralPath $Root -PathType Container)) {
        return [pscustomobject]@{ SourceArea=$Area; Root=$Root; Present=$false; LooseFiles=0; Iwds=0; IwdImageEntries=0; IwdFailures=0 }
    }
    $looseFiles=0
    foreach ($extension in @('*.iwi','*.dds')) {
        foreach ($file in @(Get-ChildItem -LiteralPath $Root -Filter $extension -File -Recurse -ErrorAction SilentlyContinue)) {
            $looseFiles++
            Add-SourceCandidate -Candidates $Candidates -Targets $Targets -SourceArea $Area -SourceKind 'loose' -SourcePath $file.FullName -ArchiveEntry '' -Bytes ([int64]$file.Length) -Identity $file.FullName
        }
    }
    $iwdCount=0; $iwdEntries=0; $iwdFailures=0
    foreach ($iwd in @(Get-ChildItem -LiteralPath $Root -Filter '*.iwd' -File -Recurse -ErrorAction SilentlyContinue)) {
        $iwdCount++; $zip=$null
        try {
            $zip=[IO.Compression.ZipFile]::OpenRead($iwd.FullName)
            foreach ($entry in $zip.Entries) {
                if ($entry.FullName.EndsWith('/', [StringComparison]::Ordinal)) { continue }
                $ext=[IO.Path]::GetExtension($entry.FullName)
                if (-not ($ext.Equals('.iwi',[StringComparison]::OrdinalIgnoreCase) -or $ext.Equals('.dds',[StringComparison]::OrdinalIgnoreCase))) { continue }
                $iwdEntries++
                Add-SourceCandidate -Candidates $Candidates -Targets $Targets -SourceArea $Area -SourceKind 'iwd' -SourcePath $iwd.FullName -ArchiveEntry $entry.FullName -Bytes ([int64]$entry.Length) -Identity $entry.FullName
            }
        } catch { $iwdFailures++ } finally { if ($null -ne $zip) { $zip.Dispose() } }
    }
    return [pscustomobject]@{ SourceArea=$Area; Root=(Resolve-Path -LiteralPath $Root).Path; Present=$true; LooseFiles=$looseFiles; Iwds=$iwdCount; IwdImageEntries=$iwdEntries; IwdFailures=$iwdFailures }
}

$readiness=Get-Content -LiteralPath $ReadinessManifest -Raw | ConvertFrom-Json
$production=Get-Content -LiteralPath $ProductionImageGraphManifest -Raw | ConvertFrom-Json
$imageRefs=Get-Content -LiteralPath $ImageReferenceProofManifest -Raw | ConvertFrom-Json
$native=Get-Content -LiteralPath $NativeIdentityManifest -Raw | ConvertFrom-Json
if ([int]$readiness.CriticalFallbackRoots -ne 70 -or [int]$readiness.ProductionRawFileRoots -ne 8 -or -not [bool]$readiness.ProperSizeGate) { throw 'Stage 5A requires the fresh 70-root readiness state with 8 integrated RawFiles and ProperSizeGate=True.' }
if ([string]$production.format -ne 'WaW360.Nuclear4T.ProductionImageGraph.v1' -or -not [bool]$production.mergePass -or [int]$production.imageGraphs -ne 878) { throw 'Production image graph is not the expected passing 878-root graph.' }
if ([string]$imageRefs.format -ne 'WaW360.Nuclear4X.XboxImageRuntimeReferenceProof.v1' -or -not [bool]$imageRefs.referenceProofPass -or [int]$imageRefs.resolvedReferences -ne 7) { throw 'Retail image reference proof is not the expected passing 7-root proof.' }
if ([string]$native.format -ne 'WaW360.T4Xenon.NativeIdentityManifest.v1' -or -not [bool]$native.typeSequenceVerified) { throw 'Native identity manifest is not verified.' }

$productionNames=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($row in @($production.images)) { [void]$productionNames.Add([string]$row.Name) }
$referenceIndices=New-Object 'System.Collections.Generic.HashSet[int]'
foreach ($row in @($imageRefs.resolved)) { [void]$referenceIndices.Add([int]$row.Index) }
$targets=New-Object 'System.Collections.Generic.List[object]'
foreach ($entry in @($native.entries | Sort-Object Index)) {
    $type=$(if (-not [string]::IsNullOrWhiteSpace([string]$entry.OatTypeName)) { [string]$entry.OatTypeName } else { [string]$entry.TypeName })
    if (-not $type.Equals('image',[StringComparison]::OrdinalIgnoreCase)) { continue }
    $index=[int]$entry.Index; $name=[string]$entry.Name
    if ($productionNames.Contains($name) -or $referenceIndices.Contains($index)) { continue }
    $targets.Add([pscustomobject]@{ Index=$index; Name=$name; Normalized=(Normalize-ImageIdentity -Value $name); Family=(Get-FamilyKey -Value $name); GeneratedIdentity=$name.StartsWith('~',[StringComparison]::Ordinal) })
}
if ($targets.Count -ne 43) { throw "Expected exactly 43 remaining image roots; found $($targets.Count)." }

if (Test-Path -LiteralPath $OutputRoot) { Remove-Item -LiteralPath $OutputRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$candidates=New-Object 'System.Collections.Generic.List[object]'
$sourceStats=New-Object 'System.Collections.Generic.List[object]'
$roots=@(
    [pscustomobject]@{ Area='frumansion-map'; Root=$PcMapRoot },
    [pscustomobject]@{ Area='map-assets'; Root=$MapAssetsRoot },
    [pscustomobject]@{ Area='ugx'; Root=$UgxRoot },
    [pscustomobject]@{ Area='zct'; Root=$ZctRoot },
    [pscustomobject]@{ Area='map-assets-ff-dumps'; Root=(Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_map_asset_corpus\ff_dumps') },
    [pscustomobject]@{ Area='mod-dependency-ff-dumps'; Root=(Join-Path $repo 'output\nazi_zombie_FRUmansion\nuclear_4r_mod_dependency_corpus\ff_dumps') }
)

Write-Host '[NUCLEAR-5A-IMG] ============================================================'
Write-Host '[NUCLEAR-5A-IMG] 43-IMAGE SOURCE / PROVENANCE CLOSURE SWEEP'
Write-Host '[NUCLEAR-5A-IMG] ============================================================'
Write-Host "[NUCLEAR-5A-IMG] Targets : $($targets.Count)"
Write-Host '[NUCLEAR-5A-IMG] Family matches are diagnostic only; no fuzzy match receives production credit.'
Write-Host ''
foreach ($root in $roots) {
    Write-Host ("[NUCLEAR-5A-IMG] Scanning {0}: {1}" -f $root.Area,$root.Root)
    $stat=Scan-SourceRoot -Candidates $candidates -Targets ([object[]]$targets.ToArray()) -Area ([string]$root.Area) -Root ([string]$root.Root)
    $sourceStats.Add($stat)
    if ([bool]$stat.Present) { Write-Host ("[NUCLEAR-5A-IMG]   loose={0:N0}, iwds={1:N0}, iwdImages={2:N0}, iwdFailures={3:N0}" -f [int]$stat.LooseFiles,[int]$stat.Iwds,[int]$stat.IwdImageEntries,[int]$stat.IwdFailures) }
    else { Write-Host '[NUCLEAR-5A-IMG]   source root not present; skipped' }
}

$candidateArray=[object[]]$candidates.ToArray()
$rows=New-Object 'System.Collections.Generic.List[object]'
$exactResolved=New-Object 'System.Collections.Generic.List[object]'
$exactAmbiguous=New-Object 'System.Collections.Generic.List[object]'
$familyOnly=New-Object 'System.Collections.Generic.List[object]'
$absent=New-Object 'System.Collections.Generic.List[object]'
foreach ($target in $targets.ToArray()) {
    $matches=@($candidateArray | Where-Object { [int]$_.TargetIndex -eq [int]$target.Index } | Sort-Object MatchRank,SourceArea,SourceKind,SourcePath,ArchiveEntry)
    $exact=@($matches | Where-Object { [int]$_.MatchRank -eq 0 })
    $family=@($matches | Where-Object { [int]$_.MatchRank -gt 0 })
    $status=$(if ($exact.Count -eq 1) { 'exact-resolved' } elseif ($exact.Count -gt 1) { 'exact-ambiguous' } elseif ($family.Count -gt 0) { 'family-candidates-only' } else { 'absent' })
    $row=[pscustomobject]@{ Index=[int]$target.Index; Name=[string]$target.Name; GeneratedIdentity=[bool]$target.GeneratedIdentity; Family=[string]$target.Family; Status=$status; ExactCandidates=$exact.Count; FamilyCandidates=$family.Count; Exact=[object[]]$exact; FamilyMatches=[object[]]@($family | Select-Object -First 25) }
    $rows.Add($row)
    switch ($status) { 'exact-resolved' { $exactResolved.Add($row) } 'exact-ambiguous' { $exactAmbiguous.Add($row) } 'family-candidates-only' { $familyOnly.Add($row) } default { $absent.Add($row) } }
}
$byArea=@($exactResolved.ToArray() | ForEach-Object { $_.Exact[0] } | Group-Object SourceArea | Sort-Object @{Expression={$_.Count};Descending=$true}, @{Expression={$_.Name};Descending=$false} | ForEach-Object { [pscustomobject]@{ SourceArea=$_.Name; ExactResolved=$_.Count } })
$familyGroups=@($rows.ToArray() | Group-Object Family | Sort-Object @{Expression={$_.Count};Descending=$true}, @{Expression={$_.Name};Descending=$false} | ForEach-Object { [pscustomobject]@{ Family=$_.Name; Targets=$_.Count; ExactResolved=@($_.Group | Where-Object Status -eq 'exact-resolved').Count; ExactAmbiguous=@($_.Group | Where-Object Status -eq 'exact-ambiguous').Count; FamilyOnly=@($_.Group | Where-Object Status -eq 'family-candidates-only').Count; Absent=@($_.Group | Where-Object Status -eq 'absent').Count } })

$manifest=[ordered]@{
    format='WaW360.Nuclear5A.ImageSourceClosure.v1'; readinessManifest=(Resolve-Path -LiteralPath $ReadinessManifest).Path; productionImageGraphManifest=(Resolve-Path -LiteralPath $ProductionImageGraphManifest).Path;
    imageReferenceProofManifest=(Resolve-Path -LiteralPath $ImageReferenceProofManifest).Path; nativeIdentityManifest=(Resolve-Path -LiteralPath $NativeIdentityManifest).Path;
    targetImages=$targets.Count; exactResolved=$exactResolved.Count; exactAmbiguous=$exactAmbiguous.Count; familyCandidatesOnly=$familyOnly.Count; absent=$absent.Count;
    sourceStats=[object[]]$sourceStats.ToArray(); exactResolutionByArea=$byArea; familyGroups=$familyGroups; targets=[object[]]$rows.ToArray();
    policy=@('Exact identity matches are source candidates only; conversion and OAT validation remain mandatory before Stage 4F credit.','Family-exact and family-prefix matches are diagnostic evidence only and are never auto-grafted.','Retail Xbox assets are not used here because all stock-image runtime-reference work is already closed.','PC retail World at War is not required or scanned.','The scan is intentionally batch-wide across all 43 remaining image roots.')
}
$jsonPath=Join-Path $OutputRoot 'nuclear_5a_image_source_closure.json'
$txtPath=Join-Path $OutputRoot 'nuclear_5a_image_source_closure.txt'
$manifest | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $jsonPath -Encoding UTF8
$report=New-Object 'System.Collections.Generic.List[string]'
$report.Add('NUCLEAR STAGE 5A - 43-IMAGE SOURCE / PROVENANCE CLOSURE SWEEP')
$report.Add(('=' * 104))
$report.Add(("Targets:                     {0:N0}" -f $targets.Count))
$report.Add(("Exact resolved:              {0:N0}" -f $exactResolved.Count))
$report.Add(("Exact ambiguous:             {0:N0}" -f $exactAmbiguous.Count))
$report.Add(("Family candidates only:      {0:N0}" -f $familyOnly.Count))
$report.Add(("Absent from supplied roots:  {0:N0}" -f $absent.Count))
$report.Add('')
$report.Add('EXACT RESOLUTION BY SOURCE AREA')
foreach ($row in $byArea) { $report.Add(("  {0,-28} {1,4:N0}" -f $row.SourceArea,$row.ExactResolved)) }
$report.Add('')
$report.Add('TARGETS')
foreach ($row in $rows.ToArray()) {
    $report.Add(("  root #{0,4}: {1,-58} {2}" -f $row.Index,$row.Name,$row.Status))
    foreach ($candidate in @($row.Exact | Select-Object -First 5)) {
        $suffix=$(if ([string]::IsNullOrWhiteSpace([string]$candidate.ArchiveEntry)) { '' } else { " :: $($candidate.ArchiveEntry)" })
        $report.Add(("           EXACT <- {0}/{1}: {2}{3}" -f $candidate.SourceArea,$candidate.SourceKind,$candidate.SourcePath,$suffix))
    }
    foreach ($candidate in @($row.FamilyMatches | Select-Object -First 5)) {
        $suffix=$(if ([string]::IsNullOrWhiteSpace([string]$candidate.ArchiveEntry)) { '' } else { " :: $($candidate.ArchiveEntry)" })
        $report.Add(("           FAMILY({0}) <- {1}/{2}: {3}{4}" -f $candidate.MatchClass,$candidate.SourceArea,$candidate.SourceKind,$candidate.SourcePath,$suffix))
    }
}
[IO.File]::WriteAllLines($txtPath,[string[]]$report.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host ''
Write-Host '[NUCLEAR-5A-IMG] ====================== RESULT ==============================='
Write-Host "[NUCLEAR-5A-IMG] Targets                    : $($targets.Count)"
Write-Host "[NUCLEAR-5A-IMG] Exact resolved             : $($exactResolved.Count)"
Write-Host "[NUCLEAR-5A-IMG] Exact ambiguous            : $($exactAmbiguous.Count)"
Write-Host "[NUCLEAR-5A-IMG] Family candidates only     : $($familyOnly.Count)"
Write-Host "[NUCLEAR-5A-IMG] Absent from supplied roots : $($absent.Count)"
foreach ($row in $byArea) { Write-Host ("[NUCLEAR-5A-IMG]   {0}: {1} exact" -f $row.SourceArea,$row.ExactResolved) }
Write-Host "[NUCLEAR-5A-IMG] Manifest                   : $jsonPath"
Write-Host "[NUCLEAR-5A-IMG] Report                     : $txtPath"
Write-Host '[NUCLEAR-5A-IMG] ================================================================='
Write-Host '[NUCLEAR-5A-IMG] PASS: batch provenance sweep complete; no fuzzy match received production credit.' -ForegroundColor Green
