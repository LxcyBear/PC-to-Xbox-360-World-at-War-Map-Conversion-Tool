[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Root,
    [string]$MapName = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path -LiteralPath $Root -ErrorAction Stop).Path
$rootPath = [IO.Path]::GetFullPath($rootPath)
$knownRoots = @(
    [pscustomobject]@{ name='flat'; path=(Join-Path $rootPath '_codxe\usermaps') },
    [pscustomobject]@{ name='legacy_nested_t4'; path=(Join-Path $rootPath '_codxe\t4\usermaps') },
    [pscustomobject]@{ name='flat'; path=(Join-Path $rootPath 'usermaps') },
    [pscustomobject]@{ name='legacy_nested_t4'; path=(Join-Path $rootPath 't4\usermaps') }
)
$rootsFound = New-Object 'System.Collections.Generic.List[object]'
$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($candidate in $knownRoots){
    if((Test-Path -LiteralPath $candidate.path -PathType Container) -and $seen.Add($candidate.path)){
        $rootsFound.Add($candidate)
    }
}
# The chosen path can be a reference/library directory rather than the WaW
# install root. Discover nested usermaps and sample fastfiles without moving
# anything. This is a bounded search, never a beta-layout certification.
$referenceFastFiles = New-Object 'System.Collections.Generic.List[object]'
$discoveredFolders = 0
$searchLimitHit = $false
$queue = New-Object 'System.Collections.Generic.Queue[object]'
$queue.Enqueue([pscustomobject]@{path=$rootPath;depth=0})
$searched = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
while($queue.Count -gt 0){
    if($discoveredFolders -ge 2500){$searchLimitHit=$true;break}
    $current = $queue.Dequeue()
    if(-not $searched.Add([string]$current.path)){continue}
    $discoveredFolders++
    if([IO.Path]::GetFileName(([string]$current.path).TrimEnd('\','/')) -ieq 'usermaps'){
        $norm = ([string]$current.path).Replace('/','\').TrimEnd('\')
        $layout=if($norm -match '(?i)\\t4\\usermaps$'){'legacy_nested_t4'}elseif(
            $norm -match '(?i)\\_codxe\\usermaps$'){'flat'}else{'unclassified_usermaps'}
        if($seen.Add($norm)){
            $rootsFound.Add([pscustomobject]@{name=$layout;path=$norm})
        }
    }
    if($referenceFastFiles.Count -lt 25){
        foreach($ff in @(Get-ChildItem -LiteralPath $current.path -File -Filter '*.ff' -ErrorAction SilentlyContinue)){
            if($referenceFastFiles.Count -ge 25){break}
            if([string]::IsNullOrWhiteSpace($MapName) -or
               $ff.Name -like ('*'+$MapName+'*')){
                $referenceFastFiles.Add([pscustomobject]@{
                    file=$ff.FullName;bytes=[long]$ff.Length;directory=$ff.DirectoryName
                })
            }
        }
    }
    if([int]$current.depth -ge 6){continue}
    foreach($child in @(Get-ChildItem -LiteralPath $current.path -Directory -ErrorAction SilentlyContinue)){
        if($child.Name -in @('.git','node_modules','.deps','.vs','obj')){continue}
        $queue.Enqueue([pscustomobject]@{path=$child.FullName;depth=([int]$current.depth+1)})
    }
}
# A reference library can contain <map>\<map>.ff directly, without any
# "usermaps" directory. Inventory these as reference packages, NOT as
# validated CodXe-beta installations or candidate Xbox release packages.
$referencePackages = New-Object 'System.Collections.Generic.List[object]'
$referencePackagesTruncated = $false
foreach($directory in @($searched | Sort-Object)){
    $mapBase=[IO.Path]::GetFileName(([string]$directory).TrimEnd('\','/'))
    if([string]::IsNullOrWhiteSpace($mapBase)){continue}
    if(-not [string]::IsNullOrWhiteSpace($MapName) -and $mapBase -ine $MapName){continue}
    $ffPath=Join-Path $directory ($mapBase+'.ff')
    if(-not (Test-Path -LiteralPath $ffPath -PathType Leaf)){continue}
    if($referencePackages.Count -ge 100){$referencePackagesTruncated=$true;break}

    $mainFf=Get-Item -LiteralPath $ffPath
    $loadPath=Join-Path $directory ($mapBase+'_load.ff')
    $patchPath=Join-Path $directory ($mapBase+'_patch.ff')
    $mapJson=Join-Path $directory 'map.json'
    $previewDds=Join-Path $directory 'preview.dds'
    $relativeDirectory=([string]$directory).Substring($rootPath.TrimEnd('\','/').Length).TrimStart('\','/')
    $allFiles=@(Get-ChildItem -LiteralPath $directory -File -Recurse -ErrorAction Stop)
    $fileBytes=($allFiles | Measure-Object -Property Length -Sum).Sum
    $topFiles=@(Get-ChildItem -LiteralPath $directory -File -ErrorAction Stop | Sort-Object Name)
    $extras=@(Get-ChildItem -LiteralPath $directory -Directory -ErrorAction Stop | Sort-Object Name |
        ForEach-Object {$_.Name})
    # Write every actual relative path and size into the source package
    # manifest. Do not accidentally drop patch/highmip/sound/unknown extras
    # when planning an eventual CodXe-beta flat package.
    $fileInventory=@($allFiles | ForEach-Object {
        [pscustomobject]@{
            relativePath=($_.FullName.Substring(([string]$directory).Length).TrimStart('\','/') -replace '\\','/')
            bytes=[long]$_.Length
            extension=$_.Extension.ToLowerInvariant()
        }
    } | Sort-Object relativePath)
    $highmipFiles=@($fileInventory | Where-Object {$_.relativePath -like 'highmip/*'})
    $soundFiles=@($fileInventory | Where-Object {$_.relativePath -like 'sounds/*'})
    $extensionCounts=@($fileInventory | Group-Object extension | Sort-Object Name | ForEach-Object {
        [pscustomobject]@{extension=$_.Name;files=$_.Count}
    })
    $referencePackages.Add([pscustomobject]@{
        name=$mapBase
        locationKind='REFERENCE_COLLECTION_OR_UNVERIFIED_INSTALL'
        directory=$directory
        relativeDirectory=$relativeDirectory
        expectedFlatInstallRelativePath=('_codxe\usermaps\'+$mapBase)
        mainFfBytes=[long]$mainFf.Length
        loadFfPresent=(Test-Path -LiteralPath $loadPath -PathType Leaf)
        loadFfBytes=$(if(Test-Path -LiteralPath $loadPath -PathType Leaf){
            [long](Get-Item -LiteralPath $loadPath).Length
        }else{$null})
        patchFfPresent=(Test-Path -LiteralPath $patchPath -PathType Leaf)
        patchFfBytes=$(if(Test-Path -LiteralPath $patchPath -PathType Leaf){
            [long](Get-Item -LiteralPath $patchPath).Length
        }else{$null})
        mapJsonPresent=(Test-Path -LiteralPath $mapJson -PathType Leaf)
        previewDdsPresent=(Test-Path -LiteralPath $previewDds -PathType Leaf)
        topLevelFiles=@($topFiles | ForEach-Object {$_.Name})
        extraDirectories=$extras
        highmipFileCount=$highmipFiles.Count
        looseSoundFileCount=$soundFiles.Count
        extensionCounts=$extensionCounts
        allFilesRelativePathAndBytes=$fileInventory
        publicCodxeMapMenuFallback=$(if(Test-Path -LiteralPath $mapJson -PathType Leaf){'METADATA_PRESENT'}else{'FOLDER_NAME_FALLBACK_IN_PUBLIC_MAIN'})
        betaMetadataRequirementConfirmed=$false
        recommendedFlatInstallRelativePath=('_codxe\usermaps\'+$mapBase)
        totalFiles=$allFiles.Count
        totalBytes=[long]$fileBytes
        formatAndLoaderValidityUnknown=$true
        betaInstallLocationProven=$false
        xboxReleaseAuthorized=$false
    })
}
$maps = New-Object 'System.Collections.Generic.List[object]'
foreach($candidate in $rootsFound){
    $folders = @(Get-ChildItem -LiteralPath $candidate.path -Directory -ErrorAction Stop |
        Where-Object { [string]::IsNullOrWhiteSpace($MapName) -or $_.Name -ieq $MapName })
    foreach($folder in $folders){
        $all = @(Get-ChildItem -LiteralPath $folder.FullName -File -Recurse -ErrorAction Stop)
        $main = Join-Path $folder.FullName ($folder.Name + '.ff')
        $load = Join-Path $folder.FullName ($folder.Name + '_load.ff')
        $metadata = Join-Path $folder.FullName 'map.json'
        $preview = Join-Path $folder.FullName 'preview.dds'
        $extraNestedT4 = Join-Path $folder.FullName 't4'
        $item = [pscustomobject]@{
            name=$folder.Name
            layout=$candidate.name
            directory=$folder.FullName
            mainFfPresent=(Test-Path -LiteralPath $main -PathType Leaf)
            loadFfPresent=(Test-Path -LiteralPath $load -PathType Leaf)
            mapJsonPresent=(Test-Path -LiteralPath $metadata -PathType Leaf)
            previewDdsPresent=(Test-Path -LiteralPath $preview -PathType Leaf)
            nestedT4WithinMapPresent=(Test-Path -LiteralPath $extraNestedT4 -PathType Container)
            fileCount=$all.Count
            totalBytes=([long](@($all | Measure-Object -Property Length -Sum)[0].Sum))
            topLevelFiles=@(Get-ChildItem -LiteralPath $folder.FullName -File | ForEach-Object {$_.Name})
            allRelativePaths=@($all | ForEach-Object {
                $_.FullName.Substring($folder.FullName.Length).TrimStart('\','/') -replace '\\','/'
            })
            matchesExpectedFlatPath=($candidate.name -eq 'flat' -and (Test-Path -LiteralPath $main -PathType Leaf))
            safeNativeReleaseAuthorized=$false
        }
        $maps.Add($item)
    }
}
Write-Host '[CODXE-T4-LAYOUT] ================================================='
Write-Host '[CODXE-T4-LAYOUT] READ-ONLY upcoming beta layout inventory. No files moved.'
Write-Host "[CODXE-T4-LAYOUT] Root: $rootPath"
Write-Host '[CODXE-T4-LAYOUT] Expected flat: _codxe\usermaps\<map>\<map>.ff'
Write-Host '[CODXE-T4-LAYOUT] Legacy nested: _codxe\t4\usermaps\<map>\<map>.ff'
foreach($r in $rootsFound){ Write-Host "[CODXE-T4-LAYOUT] Root found: $($r.name) $($r.path)" }
foreach($m in $maps){
    $state=if($m.layout -eq 'legacy_nested_t4'){'LEGACY_PATH_MIGRATION_NEEDED'}elseif(-not $m.mainFfPresent){'MISSING_EXPECTED_MAIN_FF'}else{'FLAT_LOCATION_PRESENT_ONLY'}
    Write-Host "[CODXE-T4-LAYOUT] $($m.name): $state | files=$($m.fileCount) bytes=$($m.totalBytes)"
    Write-Host "[CODXE-T4-LAYOUT]   main=$($m.mainFfPresent) load=$($m.loadFfPresent) map.json=$($m.mapJsonPresent) preview.dds=$($m.previewDdsPresent)"
    if($m.nestedT4WithinMapPresent){ Write-Host '[CODXE-T4-LAYOUT]   NOTE: additional t4/ detected inside map directory; inspect beta reference' }
}
if($maps.Count -eq 0){
    Write-Host '[CODXE-T4-LAYOUT] No installed usermap packages found in scanned directory trees.'
    Write-Host '[CODXE-T4-LAYOUT] This may be a reference asset folder, NOT the CodXe game/install root.'
}
Write-Host "[CODXE-T4-LAYOUT] Map-shaped folders with <name>.ff=$($referencePackages.Count); truncated=$referencePackagesTruncated"
foreach($pkg in $referencePackages){
    $mb=[Math]::Round($pkg.mainFfBytes / 1MB,3)
    Write-Host "[CODXE-T4-LAYOUT] REFERENCE $($pkg.name): FF=$mb MiB, load=$($pkg.loadFfPresent), patch=$($pkg.patchFfPresent), map.json=$($pkg.mapJsonPresent), preview.dds=$($pkg.previewDdsPresent), files=$($pkg.totalFiles)"
    Write-Host "[CODXE-T4-LAYOUT]   path=$($pkg.directory)"
    Write-Host "[CODXE-T4-LAYOUT]   highmipFiles=$($pkg.highmipFileCount) looseSounds=$($pkg.looseSoundFileCount) totalBytes=$($pkg.totalBytes)"
    Write-Host "[CODXE-T4-LAYOUT]   PUBLIC codxe map menu: $($pkg.publicCodxeMapMenuFallback) (upcoming beta requirement UNVERIFIED)"
    Write-Host "[CODXE-T4-LAYOUT]   expected beta flat target: $($pkg.recommendedFlatInstallRelativePath); NO COPYING PERFORMED"
    if(@($pkg.extraDirectories).Count -gt 0){
        Write-Host "[CODXE-T4-LAYOUT]   extra directories=$(@($pkg.extraDirectories) -join ', ')"
    }
}
Write-Host "[CODXE-T4-LAYOUT] Directories inspected=$discoveredFolders; searchLimitReached=$searchLimitHit"
foreach($ff in $referenceFastFiles){
    Write-Host "[CODXE-T4-LAYOUT] FF PATH HINT (not a confirmed installed map): $($ff.file) [$($ff.bytes) B]"
}
if($referenceFastFiles.Count -eq 0 -and $maps.Count -eq 0 -and $referencePackages.Count -eq 0){
    Write-Host '[CODXE-T4-LAYOUT] No FF files found within the bounded 6-level search.'
    Write-Host '[CODXE-T4-LAYOUT] Try -Root with the actual CodXe beta _codxe directory or a folder containing a working usermaps directory.'
}
$result=[ordered]@{
    schema='WaW360.CodXeT4BetaPackageInventory.v1'
    note='Predicted flat CodXe map path. Public CodXe menu falls back to folder names for absent map.json; this does not establish unpublished beta requirements. Actual beta release layout requires beta verification.'
    root=$rootPath
    isReadOnly=$true
    xboxTestAuthorization=$false
    rootsFound=@($rootsFound.ToArray())
    discovery=[ordered]@{
        inspectedDirectories=$discoveredFolders
        maxSearchDepth=6
        searchLimitReached=$searchLimitHit
        candidateFastFiles=@($referenceFastFiles.ToArray())
        note='A *.ff found without a usermaps root is a reference candidate, not proof of a correct CodXe install location.'
    }
    maps=@($maps.ToArray())
    referencePackages=@($referencePackages.ToArray())
    referencePackagesTruncated=$referencePackagesTruncated
}
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$outdir=Join-Path $repo 'output\codxe_beta_layout_audit'
New-Item -Path $outdir -ItemType Directory -Force | Out-Null
$stamp=Get-Date -Format 'yyyyMMdd_HHmmss'
$report=Join-Path $outdir ('codxe_t4_beta_layout_'+$stamp+'.json')
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host "[CODXE-T4-LAYOUT] JSON report: $report"
Write-Host '[CODXE-T4-LAYOUT] This audit does not validate native FF data, assets, or Xbox readiness.'
