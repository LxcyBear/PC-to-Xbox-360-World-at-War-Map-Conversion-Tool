[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SourceMapDirectory,
    [switch]$Stage
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Source-only package layout staging. This does NOT run OAT, convert a PC FF,
# install anything on an Xbox, or certify unpublished CodXe beta behavior.
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$source=(Resolve-Path -LiteralPath $SourceMapDirectory -ErrorAction Stop).Path
if(-not (Test-Path -LiteralPath $source -PathType Container)){
    throw "SourceMapDirectory must be an existing map folder: $source"
}
$mapName=[IO.Path]::GetFileName($source.TrimEnd('\','/'))
if($mapName -notmatch '^[A-Za-z0-9_-]+$'){
    throw "Unsafe map folder name '$mapName'; expected letters, numbers, underscore or hyphen."
}
$main=Join-Path $source ($mapName+'.ff')
if(-not (Test-Path -LiteralPath $main -PathType Leaf)){
    throw "No matching primary fastfile exists: $main"
}

# Do not follow links/junctions out of the source directory and accidentally
# copy data from other drives or outside the requested reference package.
$srcTree=@(Get-Item -LiteralPath $source)
$srcTree+=@(Get-ChildItem -LiteralPath $source -Recurse -Force -ErrorAction Stop)
foreach($item in $srcTree){
    if(([int]$item.Attributes -band [int][IO.FileAttributes]::ReparsePoint) -ne 0){
        throw "Refusing source symlink/junction/reparse point: $($item.FullName)"
    }
}
$srcFiles=@($srcTree | Where-Object {-not $_.PSIsContainer} | Sort-Object FullName)
$srcDirectories=@($srcTree | Where-Object {$_.PSIsContainer} | Sort-Object FullName)
if($srcFiles.Count -eq 0){throw 'No files found in source map directory.'}
$sourcePrefix=$source.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
$manifest=New-Object 'System.Collections.Generic.List[object]'
$totalBytes=[int64]0
foreach($file in $srcFiles){
    if(-not $file.FullName.StartsWith($sourcePrefix,[StringComparison]::OrdinalIgnoreCase)){
        throw "Source file escaped requested root: $($file.FullName)"
    }
    $relative=$file.FullName.Substring($sourcePrefix.Length)
    if([string]::IsNullOrWhiteSpace($relative) -or
       [IO.Path]::IsPathRooted($relative) -or
       $relative -match '(^|[\\/])\.\.([\\/]|$)'){
        throw "Unsafe relative file path '$relative'"
    }
    $totalBytes+=[int64]$file.Length
    $manifest.Add([pscustomobject]@{
        relativePath=($relative -replace '\\','/')
        bytes=[int64]$file.Length
        sourceSha256=$null
        copiedSha256=$null
        sameHash=$null
    })
}

$root=Join-Path $repo 'output\codxe_beta_stage'
$reports=Join-Path $root 'reports'
New-Item -Path $reports -ItemType Directory -Force | Out-Null
$runId=(Get-Date -Format 'yyyyMMdd_HHmmss')+'_'+[guid]::NewGuid().ToString('N').Substring(0,8)
$stageRun=Join-Path $root ($mapName+'_'+$runId)
$mapTarget=Join-Path $stageRun (Join-Path '_codxe\usermaps' $mapName)
$guard=Join-Path $stageRun 'REFERENCE_ONLY_NOT_XBOX_RELEASE.txt'
$staged=$false
$status='DRY_RUN_ONLY'
$copiedFiles=0
$copiedBytes=[int64]0

Write-Host '[CODXE-BETA-STAGE] ======================================================='
Write-Host "[CODXE-BETA-STAGE] Reference package: $source"
Write-Host "[CODXE-BETA-STAGE] map=$mapName files=$($srcFiles.Count) bytes=$totalBytes"
Write-Host "[CODXE-BETA-STAGE] Beta-expected relative layout: _codxe\usermaps\$mapName"
Write-Host '[CODXE-BETA-STAGE] No native Xbox readiness, no FF conversion, no Xbox deployment.'
if(-not $Stage){
    Write-Host '[CODXE-BETA-STAGE] DRY RUN: no map files copied. Add -Stage to copy a separate, SHA-verified staging tree under repo output/.'
}else{
    if(Test-Path -LiteralPath $stageRun){
        throw "Refusing to overwrite existing staging run: $stageRun"
    }
    try{
        New-Item -Path $mapTarget -ItemType Directory -Force | Out-Null
        foreach($directory in $srcDirectories){
            if($directory.FullName -ieq $source){continue}
            $relativeDir=$directory.FullName.Substring($sourcePrefix.Length)
            if([IO.Path]::IsPathRooted($relativeDir) -or $relativeDir -match '(^|[\\/])\.\.([\\/]|$)'){
                throw "Unsafe relative directory '$relativeDir'"
            }
            New-Item -ItemType Directory -Path (Join-Path $mapTarget $relativeDir) -Force | Out-Null
        }
        for($i=0;$i -lt $srcFiles.Count;$i++){
            $file=$srcFiles[$i]
            $entry=$manifest[$i]
            $relWin=([string]$entry.relativePath).Replace('/','\')
            $dest=Join-Path $mapTarget $relWin
            $parent=Split-Path -Path $dest -Parent
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $dest -ErrorAction Stop
            $targetItem=Get-Item -LiteralPath $dest -ErrorAction Stop
            if([int64]$targetItem.Length -ne [int64]$file.Length){
                throw "Copy byte-count mismatch: $($file.Name)"
            }
            $sha=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
            $copySha=(Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash
            if($sha -ine $copySha){
                throw "Copy SHA256 mismatch: $($entry.relativePath)"
            }
            $entry.sourceSha256=$sha
            $entry.copiedSha256=$copySha
            $entry.sameHash=$true
            $copiedFiles++
            $copiedBytes+=[int64]$file.Length
            Write-Progress -Activity 'CodXe beta package staging (reference only)' -Status "$mapName $copiedFiles/$($srcFiles.Count) files" -PercentComplete ([int](100*$copiedFiles/$srcFiles.Count))
            if(($copiedFiles % 5) -eq 0 -or $copiedFiles -eq $srcFiles.Count){
                $bar=('=' * [int][Math]::Floor(10*$copiedFiles/$srcFiles.Count))+
                    ('-' * (10-[int][Math]::Floor(10*$copiedFiles/$srcFiles.Count)))
                Write-Host "[CODXE-BETA-STAGE] [$bar] $copiedFiles/$($srcFiles.Count) files; $copiedBytes/$totalBytes bytes SHA-verified"
            }
        }
        Write-Progress -Activity 'CodXe beta package staging (reference only)' -Completed
        if($copiedFiles -ne $srcFiles.Count -or $copiedBytes -ne $totalBytes){
            throw 'Staged file count/total bytes do not match the source.'
        }
        @(
            'REFERENCE-ONLY, NOT AN XBOX RELEASE OR PLAYABLE CONVERSION'
            "Original map package: $source"
            "Staged copy: $mapTarget"
            'Purpose: validate the flattened CodXe directory layout without changing any existing package.'
            'This operation did not convert or repair any FF payloads.'
            'CodXe unpublished beta installer behavior and actual hardware support are not verified.'
            'Do not copy this staged reference into the real Xbox until separately validated and intentionally approved.'
        ) | Set-Content -LiteralPath $guard -Encoding UTF8
        $staged=$true
        $status='SHA_VERIFIED_REFERENCE_STAGE_NOT_RELEASE'
    }catch{
        Write-Progress -Activity 'CodXe beta package staging (reference only)' -Completed
        if(Test-Path -LiteralPath $stageRun){
            Remove-Item -LiteralPath $stageRun -Recurse -Force -ErrorAction SilentlyContinue
        }
        throw
    }
}
$report=[ordered]@{
    schema='WaW360.CodXeT4BetaStaging.v1'
    generatedUtc=[DateTime]::UtcNow.ToString('o')
    mapName=$mapName
    sourceMapDirectory=$source
    status=$status
    copyRequested=[bool]$Stage
    staged=$staged
    stagedRelativeMapRoot=('_codxe\usermaps\'+$mapName)
    stagedMapDirectory=if($staged){$mapTarget}else{$null}
    sourceFileCount=$srcFiles.Count
    sourceTotalBytes=$totalBytes
    stagedVerifiedFiles=$copiedFiles
    stagedVerifiedBytes=$copiedBytes
    sourceMainFfPresent=$true
    sourceLoadFfPresent=(Test-Path -LiteralPath (Join-Path $source ($mapName+'_load.ff')) -PathType Leaf)
    sourcePatchFfPresent=(Test-Path -LiteralPath (Join-Path $source ($mapName+'_patch.ff')) -PathType Leaf)
    sourceMapJsonPresent=(Test-Path -LiteralPath (Join-Path $source 'map.json') -PathType Leaf)
    sourcePreviewDdsPresent=(Test-Path -LiteralPath (Join-Path $source 'preview.dds') -PathType Leaf)
    originalFastFilesConverted=$false
    betaInstallVerified=$false
    nativeXboxReleaseAuthorized=$false
    files=@($manifest.ToArray())
}
$json=Join-Path $reports ($mapName+'_'+$runId+'.json')
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $json -Encoding UTF8
Write-Host "[CODXE-BETA-STAGE] Status: $status"
if($staged){Write-Host "[CODXE-BETA-STAGE] Separate SHA-verified map copy: $mapTarget"}
Write-Host "[CODXE-BETA-STAGE] JSON=$json"
Write-Host '[CODXE-BETA-STAGE] Original files unchanged. No Xbox deployment or native-release approval.'
