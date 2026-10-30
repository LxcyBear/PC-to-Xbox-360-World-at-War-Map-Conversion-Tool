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
if($maps.Count -eq 0){Write-Host '[CODXE-T4-LAYOUT] No map directories found. Check -Root or the beta reference layout.'}
$result=[ordered]@{
    schema='WaW360.CodXeT4BetaPackageInventory.v1'
    note='Predicted flat CodXe map path. Definitive beta layout must be checked against Michael beta binaries/documentation.'
    root=$rootPath
    isReadOnly=$true
    xboxTestAuthorization=$false
    rootsFound=@($rootsFound.ToArray())
    maps=@($maps.ToArray())
}
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$outdir=Join-Path $repo 'output\codxe_beta_layout_audit'
New-Item -Path $outdir -ItemType Directory -Force | Out-Null
$stamp=Get-Date -Format 'yyyyMMdd_HHmmss'
$report=Join-Path $outdir ('codxe_t4_beta_layout_'+$stamp+'.json')
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host "[CODXE-T4-LAYOUT] JSON report: $report"
Write-Host '[CODXE-T4-LAYOUT] This audit does not validate native FF data, assets, or Xbox readiness.'
