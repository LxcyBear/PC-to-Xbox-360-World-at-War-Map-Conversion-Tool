[CmdletBinding()]
param(
    [string[]]$SourceRoots=@(),
    [ValidateSet('Inspect','Source','Candidate')]
    [string]$Mode='Inspect',
    [string]$MapName=''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

# A read-only intake for a named WaW Backrooms custom mod map.
# Inspect needs NO dotnet build, NO OAT, NO FF rewrite or Xbox deployment.
# Source/Candidate explicitly hand off to the existing multi-map matrix.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if($SourceRoots.Count -eq 0){
    $SourceRoots=@(
        (Join-Path (Split-Path -Parent $repo) 'PC Maps')
        (Join-Path $env:LOCALAPPDATA 'Activision\CoDWaW\mods')
    )
}
$roots=New-Object 'System.Collections.Generic.List[string]'
foreach($item in @($SourceRoots)){
    foreach($part in (([string]$item) -split ',')){
        if([string]::IsNullOrWhiteSpace($part)){continue}
        $full=[IO.Path]::GetFullPath($part.Trim())
        if(Test-Path -LiteralPath $full -PathType Container){
            if(-not $roots.Contains($full)){$roots.Add($full)}
        }else{
            Write-Host "[BACKROOMS] Source folder not present (skipped): $full"
        }
    }
}
if($roots.Count -eq 0){
    throw 'No PC map source folders found. Supply -SourceRoots with the actual installed Backrooms map directory.'
}
$stamp=Get-Date -Format 'yyyyMMdd_HHmmss'
$outDir=Join-Path $repo 'output\backrooms_intake'
New-Item -ItemType Directory -Path $outDir -Force|Out-Null
$json=Join-Path $outDir ('backrooms_intake_'+$stamp+'.json')

# Match both installed folder names and original source FF names.
# Never guess that a named executable/zip is an unpacked PC FF.
$matches=New-Object 'System.Collections.Generic.List[object]'
$candidates=New-Object 'System.Collections.Generic.List[string]'
Write-Host '[BACKROOMS] ========================================================'
Write-Host '[BACKROOMS] Source-owned Backrooms discovery. Xbox output: DISABLED.'
$ri=0
foreach($sourceRoot in $roots){
    $ri++
    Write-Progress -Activity 'PC2XB3 Backrooms source intake' -Status "$ri/$($roots.Count) scanning source folders" -PercentComplete ([int](100*($ri-1)/$roots.Count))
    Write-Host "[BACKROOMS] Scanning $sourceRoot"
    $files=@(Get-ChildItem -LiteralPath $sourceRoot -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Extension -in @('.ff','.iwd','.pak','.zip') -and
            $_.FullName -match '(?i)backrooms?|liminal'
        } | Sort-Object FullName)
    foreach($f in $files){
        $kind=$f.Extension.ToLowerInvariant()
        $identity=''
        if($kind -eq '.ff'){
            $stem=[IO.Path]::GetFileNameWithoutExtension($f.Name)
            if($stem -notmatch '(?i)^(mod|common|localized_.*|patch|code_post_gfx)$' -and
               $stem -notmatch '(?i)(_load|_patch|_load_patch)$'){
                $identity=$stem
                if(-not $candidates.Contains($identity)){$candidates.Add($identity)}
            }
        }
        $sha=if($kind -in @('.ff','.iwd')){
            (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
        }else{''}
        $matches.Add([pscustomobject]@{
            sourceRoot=$sourceRoot
            path=$f.FullName
            file=$f.Name
            extension=$kind
            bytes=[long]$f.Length
            sha256=$sha
            candidateMapName=$identity
        })
        Write-Host ("[BACKROOMS-FILE] {0} {1:N0} B {2}" -f $kind,$f.Length,$f.FullName)
    }
}
Write-Progress -Activity 'PC2XB3 Backrooms source intake' -Completed
$names=@($candidates.ToArray() | Sort-Object -Unique)
Write-Host ("[BACKROOMS] Matching source files: {0}; possible main map names: {1}" -f $matches.Count,$names.Count)
foreach($name in $names){Write-Host "[BACKROOMS-MAP] $name"}
if($matches.Count -eq 0){
    Write-Host '[BACKROOMS] No unpacked Backrooms FF/IWD found. Install/extract the PC map first; do NOT invent FFs.'
}

# Audit report is explicitly not a native XAsset proof or a map release.
[ordered]@{
    schema='WaW360.BackroomsSourceIntake.v1'
    generatedUtc=[DateTime]::UtcNow.ToString('o')
    mode=$Mode
    sourceRoots=@($roots.ToArray())
    candidates=$names
    files=@($matches.ToArray())
    pcSourceAssetsDecoded=$false
    oAtValidated=$false
    xboxReleaseAuthorized=$false
    notes=@(
        'Original PC FFs remain read-only. ZIP/exe downloads are not treated as decompressed FF assets.',
        'The FF basename is a discovery candidate, NOT a guaranteed native Xbox map ID.',
        'The existing multi-map matrix verifies typed roots and OAT separately; a successful OAT diagnostic is NOT Xbox release authorization.'
    )
}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $json -Encoding UTF8
Write-Host "[BACKROOMS] Source manifest: $json"

if($Mode -eq 'Inspect'){
    if($names.Count -eq 1){
        Write-Host "[BACKROOMS] To analyze source: powershell -ExecutionPolicy Bypass -File .\tools\oat-xenon\Run-WaWBackroomsIntake.ps1 -Mode Source -MapName $($names[0])"
    }elseif($names.Count -gt 1){
        Write-Host '[BACKROOMS] Multiple maps found. Select the exact map using -MapName before proceeding.'
    }
    Write-Host '[BACKROOMS] Inspect complete. NO compiler, FF builder, OAT, or Xbox action.'
    return
}

$target=$MapName.Trim()
if([string]::IsNullOrWhiteSpace($target)){
    if($names.Count -ne 1){throw 'Explicit -MapName required when zero/multiple Backrooms FF identities were discovered.'}
    $target=$names[0]
}
if($names -notcontains $target){
    throw "Requested map '$target' not in source inventory. Available: $($names -join ', ')"
}
$matrix=Join-Path $PSScriptRoot 'Run-WaWMultiMapMatrix.ps1'
if(-not(Test-Path -LiteralPath $matrix -PathType Leaf)){throw "Multi-map engine script missing: $matrix"}
Write-Host "[BACKROOMS] Delegating '$target' to shared matrix $Mode. No Aztec-specific witness may be reused."
$oldDirect=$env:WAW360_WEAPON_DIRECT_WITNESS_JSON
$oldScalar=$env:WAW360_WEAPON_REAL_SCALAR_DIAGNOSTIC
$oldProbe=$env:WAW360_DIAGNOSTIC_WEAPON_ORDER_PROBE
try{
    Remove-Item Env:\WAW360_WEAPON_DIRECT_WITNESS_JSON -ErrorAction SilentlyContinue
    Remove-Item Env:\WAW360_WEAPON_REAL_SCALAR_DIAGNOSTIC -ErrorAction SilentlyContinue
    Remove-Item Env:\WAW360_DIAGNOSTIC_WEAPON_ORDER_PROBE -ErrorAction SilentlyContinue
    & $matrix -SourceRoots @($roots.ToArray()) -IncludeMaps @($target) -Mode $Mode -OatVariants Primary
    if(-not $?){throw "Shared map matrix reported a failure in mode $Mode"}
}finally{
    if($null -eq $oldDirect){Remove-Item Env:\WAW360_WEAPON_DIRECT_WITNESS_JSON -ErrorAction SilentlyContinue}
    else{$env:WAW360_WEAPON_DIRECT_WITNESS_JSON=$oldDirect}
    if($null -eq $oldScalar){Remove-Item Env:\WAW360_WEAPON_REAL_SCALAR_DIAGNOSTIC -ErrorAction SilentlyContinue}
    else{$env:WAW360_WEAPON_REAL_SCALAR_DIAGNOSTIC=$oldScalar}
    if($null -eq $oldProbe){Remove-Item Env:\WAW360_DIAGNOSTIC_WEAPON_ORDER_PROBE -ErrorAction SilentlyContinue}
    else{$env:WAW360_DIAGNOSTIC_WEAPON_ORDER_PROBE=$oldProbe}
}
Write-Host '[BACKROOMS] Matrix complete. Read the source/OAT results; no Xbox release was authorized.'
