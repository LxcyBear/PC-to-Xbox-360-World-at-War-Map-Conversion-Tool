[CmdletBinding()]
param(
    [string]$MapName='nazi_zombie_aztec_V2.2',
    [string]$OutputRoot='',
    [string]$GraphUnlinkerExe='',
    [switch]$BuildGraphDumper,
    [switch]$ForceGraphDump
)

Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
if (-not $OutputRoot) { $OutputRoot=Join-Path $repo 'output\second_map_baselines' }
if (-not $GraphUnlinkerExe) {
    $GraphUnlinkerExe=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\Unlinker.exe'
}
$workspace=Join-Path $OutputRoot $MapName
$nativeResultPath=Join-Path $workspace 'native_material_mapping\native_material_mapping_result.json'
$identityPath=Join-Path $workspace 'native_material_mapping\main_native_closure\native_identity\nazi_zombie_aztec_native_identity_manifest.json'
$closurePath=Join-Path $workspace 'native_material_mapping\closure_reconciliation\closure_reconciliation_result.json'
foreach ($file in @($nativeResultPath,$identityPath,$closurePath)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Required prior native-identity evidence missing: $file"
    }
}
$nativeResult=Get-Content -LiteralPath $nativeResultPath -Raw | ConvertFrom-Json
$identity=Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
$closure=Get-Content -LiteralPath $closurePath -Raw | ConvertFrom-Json

$expectedSha='e0f3c244dd7b72d0fe71c05faf014c2c9bfb05f770183954b3e7cde625eba98f'
$expectedBytes=82739469L
$native=[string]$nativeResult.verifiedXboxReference
if ([string]$nativeResult.schema -ne 'WaW360.SecondMapNativeMaterialMapping.v1' -or
    [string]$nativeResult.mapFolder -ine $MapName -or
    [string]$nativeResult.status -ne 'GOLDEN_VERIFIED_NATIVE_MAPPING_RECORDED' -or
    [string]$closure.schema -ne 'WaW360.SecondMapNativeClosureReconciliation.v1' -or
    [string]$closure.mapFolder -ine $MapName -or
    [string]$closure.verifiedGoldenXboxFastFile -ine $native -or
    -not [bool]$identity.TypeSequenceVerified -or
    [string]$identity.NativeFastFile -ine $native) {
    throw 'Native reference and closure reports are not a verified matching set.'
}
if (-not (Test-Path -LiteralPath $native -PathType Leaf)) { throw "Native FF missing: $native" }
$size=[long](Get-Item -LiteralPath $native).Length
if ($size -ne $expectedBytes) { throw "Native FF size mismatch; expected $expectedBytes got $size" }
$actualSha=(Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualSha -ne $expectedSha) { throw 'Native FF no longer matches verified Aztec golden SHA-256.' }

$nativeTechsets=@($identity.Entries | Where-Object { [string]$_.OatTypeName -ieq 'techniqueset' })
$nativePixels=@($identity.Entries | Where-Object { [string]$_.OatTypeName -ieq 'pixelshader' })
$expectedTechsets=$nativeTechsets.Count
$expectedPixels=$nativePixels.Count
if ($expectedTechsets -lt 1 -or $expectedPixels -lt 1) {
    throw 'Verified Xenon identity manifest does not enumerate TechniqueSets and PixelShaders.'
}
$expectedByName=New-Object 'System.Collections.Generic.Dictionary[string,int]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in $nativeTechsets) {
    $name=[string]$entry.Name
    if (-not $name -or $expectedByName.ContainsKey($name)) {
        throw "Duplicate/empty native TechniqueSet identity: $name"
    }
    $expectedByName.Add($name,[int]$entry.Index)
}

$work=Join-Path $workspace 'native_material_mapping\native_graph_proof'
$graphsRoot=Join-Path $work 'xenon_graph_dump'
$sourceManifest=Join-Path $work 'verified_native_source.json'
$graphReport=Join-Path $work 'native_graph_proof_result.txt'
$graphJson=Join-Path $work 'native_graph_proof_result.json'
New-Item -ItemType Directory -Path $work -Force | Out-Null
$dumpLog=Join-Path $work 'oat_fixed_up_graph_dump.log'
$importLog=Join-Path $work 'typed_graph_import.log'

Write-Host '[AZTEC-GRAPH] ============================================================='
Write-Host '[AZTEC-GRAPH] VERIFIED XENON TECHNIQUESET GRAPH / SHADER BLOB PROOF'
Write-Host "[AZTEC-GRAPH] Golden native FF: $native"
Write-Host "[AZTEC-GRAPH] Expected physical native TechniqueSets=$expectedTechsets PixelShaders=$expectedPixels"
Write-Host '[AZTEC-GRAPH] Fixed output directory; no candidate FF, no release folder mutation.'
Write-Host '[AZTEC-GRAPH] ============================================================='

$pending=@($closure.pendingCases)
Write-Host "[AZTEC-GRAPH] Material-referenced pending PC root entries=$($pending.Count)"
foreach ($item in $pending) {
    Write-Host "[AZTEC-GRAPH]   PENDING $($item.Role): $($item.PcTechniqueSet) [$($item.Classification)]"
}

$status='GRAPH_DUMPER_NOT_INSTALLED'
$dumpRan=$false
$dumpCode=$null
$importCode=$null
$importOutput=@()
$cacheUsed=$false
if (-not (Test-Path -LiteralPath $GraphUnlinkerExe -PathType Leaf) -and $BuildGraphDumper) {
    Write-Host '[AZTEC-GRAPH] One-time specialized graph-dumper build explicitly requested.'
    $builder=Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1'
    if (-not (Test-Path -LiteralPath $builder -PathType Leaf)) { throw "Graph dumper builder missing: $builder" }
    & powershell '-NoProfile' '-ExecutionPolicy' 'Bypass' '-File' $builder
    if ($LASTEXITCODE -ne 0 -or -not $?) { throw 'Specialized OAT graph dumper build failed.' }
}
if (Test-Path -LiteralPath $GraphUnlinkerExe -PathType Leaf) {
    $cacheOkay=$false
    if (-not $ForceGraphDump -and
        (Test-Path -LiteralPath $sourceManifest -PathType Leaf) -and
        (Test-Path -LiteralPath $graphsRoot -PathType Container)) {
        try {
            $cached=Get-Content -LiteralPath $sourceManifest -Raw | ConvertFrom-Json
            $cacheOkay=([string]$cached.nativeSha256 -eq $actualSha -and
                [string]$cached.graphUnlinker -ieq (Resolve-Path -LiteralPath $GraphUnlinkerExe).Path -and
                [int]$cached.nativeTechniqueSetRoots -eq $expectedTechsets -and
                [int]$cached.nativePixelShaderRoots -eq $expectedPixels)
        } catch {
            $cacheOkay=$false
        }
    }
    if ($cacheOkay) {
        $cacheUsed=$true
        $status='CACHED_GRAPH_DATA_CHECK'
        Write-Host '[AZTEC-GRAPH] Reusing prior dump for identical golden native FF.'
    } else {
        if (Test-Path -LiteralPath $graphsRoot) {
            # Only this fixed, explicitly-owned scratch graph directory is replaced.
            Remove-Item -LiteralPath $graphsRoot -Recurse -Force
        }
        New-Item -ItemType Directory -Path $graphsRoot -Force | Out-Null
        if (Test-Path -LiteralPath $sourceManifest) {
            Remove-Item -LiteralPath $sourceManifest -Force
        }
        Write-Host '[AZTEC-GRAPH] Dumping fixed-up native graph identities and blobs once...'
        $old=$ErrorActionPreference
        try {
            $ErrorActionPreference='Continue'
            & $GraphUnlinkerExe '--no-color' '--include-assets' 'techniqueset' '--output-folder' $graphsRoot $native 2>&1 |
                ForEach-Object { [string]$_ } | Set-Content -LiteralPath $dumpLog -Encoding UTF8
            $dumpCode=[int]$LASTEXITCODE
        } finally {
            $ErrorActionPreference=$old
        }
        $dumpRan=$true
        foreach ($line in @(Get-Content -LiteralPath $dumpLog -Tail 12)) {
            Write-Host "[AZTEC-GRAPH]   $line"
        }
        if ($dumpCode -eq 0) {
            $src=[ordered]@{
                schema='WaW360.AztecNativeGraphSource.v1'
                nativeFF=$native
                nativeSha256=$actualSha
                graphUnlinker=(Resolve-Path -LiteralPath $GraphUnlinkerExe).Path
                nativeTechniqueSetRoots=$expectedTechsets
                nativePixelShaderRoots=$expectedPixels
            }
            $src | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $sourceManifest -Encoding UTF8
            $status='DUMP_COMPLETED_VALIDATION_PENDING'
        } else {
            $status="GRAPH_DUMP_FAILED_EXIT_$dumpCode"
        }
    }
}

$graphNames=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$invalidGraphs=New-Object 'System.Collections.Generic.List[string]'
$slots=0
$passes=0
$allGraphFiles=@()
$shaderBlobFiles=@()
if ($status -in @('DUMP_COMPLETED_VALIDATION_PENDING','CACHED_GRAPH_DATA_CHECK')) {
    $allGraphFiles=@(Get-ChildItem -LiteralPath $graphsRoot -Filter '*.json' -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/]xenon_techset_graphs[\\/]' })
    $shaderBlobFiles=@(Get-ChildItem -LiteralPath $graphsRoot -Filter '*.bin' -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/]xenon_graph_shaders[\\/]' })
    foreach ($file in $allGraphFiles) {
        try {
            $graph=Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
            $name=[string]$graph.name
            $slotArray=@($graph.slots)
            if (-not $name -or -not $graphNames.Add($name)) { throw 'Empty or duplicate graph name.' }
            if ($slotArray.Count -ne 51) { throw "Graph has $($slotArray.Count) slots, not 51." }
            $slots+=@($slotArray | Where-Object { $null -ne $_.technique }).Count
            foreach ($slot in $slotArray) {
                if ($null -ne $slot.technique) { $passes+=@($slot.technique.passes).Count }
            }
        } catch {
            $invalidGraphs.Add("$($file.FullName): $($_.Exception.Message)")
        }
    }
}
$missing=@($expectedByName.Keys | Where-Object { -not $graphNames.Contains($_) } | Sort-Object)
$extras=@($graphNames | Where-Object { -not $expectedByName.ContainsKey($_) } | Sort-Object)
$graphNamePass=($graphNames.Count -eq $expectedTechsets -and
    $missing.Count -eq 0 -and $extras.Count -eq 0 -and $invalidGraphs.Count -eq 0)

# Reuse the existing typed C# graph importer, but never rebuild the CLI.
# The importer validates shader blob lengths, slots/passes and exact PS cardinality.
if ($graphNamePass) {
    $cli=Join-Path $repo 'src\WaW360.Cli\WaW360.Cli.csproj'
    if (Test-Path -LiteralPath $cli -PathType Leaf) {
        $old=$ErrorActionPreference
        try {
            $ErrorActionPreference='Continue'
            $importOutput=@(& dotnet 'run' '--project' $cli '-c' 'Release' '--no-build' '--' 'xenon-graph-import' $graphsRoot ([string]$expectedTechsets) ([string]$expectedPixels) 2>&1 |
                ForEach-Object { [string]$_ })
            $importCode=[int]$LASTEXITCODE
        } finally { $ErrorActionPreference=$old }
        [IO.File]::WriteAllLines($importLog,[string[]]$importOutput,[Text.UTF8Encoding]::new($false))
        foreach ($line in $importOutput) { Write-Host "[AZTEC-GRAPH]   $line" }
    }
    $status=if ($importCode -eq 0) { 'IDENTITY_AND_TYPED_GRAPH_IMPORT_PASS' } else { 'GRAPH_IDENTITIES_PASS_TYPED_IMPORT_PENDING' }
} elseif ($status -in @('DUMP_COMPLETED_VALIDATION_PENDING','CACHED_GRAPH_DATA_CHECK')) {
    $status='NATIVE_GRAPH_IDENTITY_MISMATCH'
}

$bound=@($closure | Out-Null)
$pcRows=@()
$classificationCsv=[string]$closure.classificationCsv
if ($classificationCsv -and (Test-Path -LiteralPath $classificationCsv -PathType Leaf)) {
    $pcRows=@(Import-Csv -LiteralPath $classificationCsv)
}
$graphCoverage=New-Object 'System.Collections.Generic.List[object]'
foreach ($row in $pcRows) {
    $target=[string]$row.XboxTechniqueSet
    if ([string]::IsNullOrWhiteSpace($target)) { continue }
    if (-not [bool]::Parse([string]$row.ObservedByAnyExportedMaterial)) { continue }
    $graphCoverage.Add([pscustomobject]@{
        Role=$row.Role
        PcTechniqueSet=$row.PcTechniqueSet
        XboxTechniqueSet=$target
        Classification=$row.Classification
        HasNativeFixedUpGraph=$graphNames.Contains($target)
        ProductionReady=$false
    })
}
$coverageCsv=Join-Path $work 'material_referenced_native_graph_coverage.csv'
$graphCoverage.ToArray() | Export-Csv -LiteralPath $coverageCsv -NoTypeInformation -Encoding UTF8
$report=[ordered]@{
    schema='WaW360.SecondMapAztecNativeGraphProof.v1'
    mapFolder=$MapName
    verifiedGoldenXboxFastFile=$native
    nativeSha256=$actualSha
    nativeIdentityTechniqueSets=$expectedTechsets
    nativeIdentityPixelShaders=$expectedPixels
    dumpRoot=$graphsRoot
    graphUnlinker=$GraphUnlinkerExe
    status=$status
    usedCachedDump=$cacheUsed
    ranOatGraphDump=$dumpRan
    oatExitCode=$dumpCode
    typedGraphImportExitCode=$importCode
    graphFiles=$allGraphFiles.Count
    distinctGraphNames=$graphNames.Count
    nonNullTechniqueSlots=$slots
    passes=$passes
    shaderBlobFiles=$shaderBlobFiles.Count
    nativeGraphsMissing=[string[]]$missing
    graphsNotInNativeRootIdentity=[string[]]$extras
    invalidGraphs=[string[]]$invalidGraphs.ToArray()
    pendingMaterialReferencedPcRoots=$pending.Count
    pcMaterialTargetCoverage=[object[]]$graphCoverage.ToArray()
    pcMaterialTargetCoverageCsv=$coverageCsv
    nativeGraphIdentityPass=$graphNamePass
    typedGraphImporterPass=($importCode -eq 0)
    productionGraphReady=$false
    policy=@(
        'The native Xbox graph dump is an on-device-compatible ORACLE, not PC-to-Xbox shader translation.',
        'No native shader bytecode or donor FF may be committed or automatically copied into the converter release folder.',
        'Graph import PASS is a typed graph evidence milestone, not a linker/hardware PASS.',
        'Unmapped TechniqueSets, resident stock shaders and unproven source ownership remain fences.'
    )
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $graphJson -Encoding UTF8
$lines=New-Object 'System.Collections.Generic.List[string]'
$lines.Add('AZTEC VERIFIED XENON MATERIAL TECHNIQUESET / SHADER GRAPH PROOF')
$lines.Add('====================================================================')
$lines.Add("Status: $status")
$lines.Add("Golden native FF: $native")
$lines.Add("Native TechniqueSet roots=$expectedTechsets; PixelShader roots=$expectedPixels")
$lines.Add("Exported graph files=$($allGraphFiles.Count); distinct graph names=$($graphNames.Count); missing=$($missing.Count); extras=$($extras.Count); invalid=$($invalidGraphs.Count)")
$lines.Add("Non-null TechniqueSet slots=$slots; passes=$passes; shader blob files=$($shaderBlobFiles.Count)")
$lines.Add("Typed graph importer exit=$importCode; cache reused=$cacheUsed")
$lines.Add("Pending material-referenced PC roots=$($pending.Count)")
foreach ($p in $pending) { $lines.Add("  $($p.Role): $($p.PcTechniqueSet) | $($p.Classification)") }
foreach ($name in $missing) { $lines.Add("  NATIVE GRAPH MISSING: $name") }
foreach ($name in $extras) { $lines.Add("  EXTRA GRAPH NAME: $name") }
foreach ($bad in $invalidGraphs) { $lines.Add("  INVALID GRAPH: $bad") }
$lines.Add("Coverage by PC material-mapped root: $coverageCsv")
$lines.Add("OAT dump log: $dumpLog")
$lines.Add("Typed importer log: $importLog")
if ($status -eq 'GRAPH_DUMPER_NOT_INSTALLED') {
    $lines.Add('Specialized OAT Xenon graph dumper not installed. Rerun with -BuildGraphDumper only if you want to build this dependency once.')
}
$lines.Add('No Xbox map FF was generated, and the release-only Converted Maps folder was not touched.')
[IO.File]::WriteAllLines($graphReport,[string[]]$lines.ToArray(),[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-GRAPH] =================== RESULT ================================='
Write-Host "[AZTEC-GRAPH] Status=$status"
Write-Host "[AZTEC-GRAPH] Graphs=$($graphNames.Count)/$expectedTechsets blobs=$($shaderBlobFiles.Count) missing=$($missing.Count) unknown=$($extras.Count)"
Write-Host "[AZTEC-GRAPH] Typed importer exit=$importCode; release/payload creation=NO"
if ($status -eq 'GRAPH_DUMPER_NOT_INSTALLED') {
    Write-Host '[AZTEC-GRAPH] Graph dumper missing. Build manually via -BuildGraphDumper if desired.' -ForegroundColor Yellow
}
Write-Host "[AZTEC-GRAPH] Report: $graphReport"
Write-Host "[AZTEC-GRAPH] JSON: $graphJson"
Write-Host '[AZTEC-GRAPH] Diagnostic only; hardware readiness unchanged.'
