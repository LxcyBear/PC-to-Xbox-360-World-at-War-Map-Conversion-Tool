[CmdletBinding()]
param([switch]$BuildDumper)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2\native_material_mapping'
$dir=Join-Path $base 'pc_g11_flame_real_xbox_xma_source'
$v4=Join-Path $dir 'aztec_six_real_flame_xbox_sound_to_xma_chain_v4.json'
$trace=Join-Path $dir 'aztec_real_xbox_flame_sound_alias_to_loaded_xma_v4.log'
$out=Join-Path $dir 'aztec_flame_six_genuine_xbox_sound_184b_loaded_160b_ff_metadata_v6.json'
$capPath=Join-Path $repo '.deps\OpenAssetTools-xenon-graph-dumper\Release_x86\waw360-graph-dumper-capabilities.json'
$cap='t4xenon-flame-sound-alias-184b-loaded-160b-original-retail-metadata-v6'
if(-not(Test-Path -LiteralPath $capPath -PathType Leaf) -or
   @((Get-Content -LiteralPath $capPath -Raw|ConvertFrom-Json).capabilities) -notcontains $cap){
    if(-not $BuildDumper){throw 'Genuine Xbox typed Sound/LoadedSound v6 metadata dumper not built. Run -BuildDumper once.'}
    Write-Host '[AZTEC-XMA-V6] ONE pinned native Xbox 360 OAT rebuild for exact 184B snd_alias_t and 160B LoadedSound source scalar metadata.'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Build-OatXenonGraphDumper.ps1') -TraceWeaponNamePointers
    if($LASTEXITCODE -ne 0){throw 'Pinned Xbox OAT native typed Sound metadata build failed.'}
}
$c=Get-Content -LiteralPath $capPath -Raw|ConvertFrom-Json
if([string]$c.oatRevision -ne '3beaa7c81af517f0c05f2b4612d477ed1541b676' -or
   @($c.capabilities) -notcontains $cap){throw 'Pinned genuine Xbox OAT Sound meta v6 capability missing.'}
Write-Host '[AZTEC-XMA-V6-PROGRESS] [######--------------] 30% | pinned source-owned T4 Xenon audio metadata instrument installed'
# The pinned v6 OAT dumper accidentally prefixed a literal '-' before
# the 160-byte genuine Owned LoadedSound hex. Consume only the exact 320
# subsequent hex digits, never the prefix; avoids a second expensive
# Win32 OAT rebuild purely to fix a formatting artifact.
# Rerun the already passing v4 OAT Xbox audio extraction. This creates
# fresh same-run raw metadata AND source-verified audio XMA under existing
# evidence-only paths; no FF or production map is changed.
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Run-AztecFlameXboxLoadedSampleChain.ps1')
if($LASTEXITCODE -ne 0){throw 'Actual original Xbox Sound/LoadedSound alias chain + XMA v4 failed.'}
$v=Get-Content -LiteralPath $v4 -Raw|ConvertFrom-Json
if([string]$v.schema -ne 'WaW360.AztecOriginalXboxSoundFileToXmaChain.v4' -or
   [int]$v.actualXenonSoundRootsTyped -ne 6 -or
   [int]$v.aliasesWithActualOriginalXboxLinkedSample -ne 6 -or
   [int]$v.riffXmaCodecHeaderProvenSampleCount -ne 6 -or
   [bool]$v.nativeAztecFFModified -or
   [bool]$v.xboxHardwareReady){
    throw 'Cannot produce genuine native Xbox audio metadata: six source/XMA sample chains not proven.'
}
$wanted=@{}
foreach($p in @($v.verifiedOriginalXboxSourceSamplePayloads)){
    $root=[string]$p.originalPcAndRetailSoundAliasName
    $idx=[int]$p.exactOatSourceSoundAliasIndex
    $key=('{0}|{1}' -f $root,$idx)
    if($wanted.ContainsKey($key) -or
       [string]$p.sourceSoundFileType -ne 'LOADED' -or
       -not [bool]$p.riffXmaCodecHeaderProven -or
       -not [bool]$p.sourceAliasToSampleFileRelationshipProven -or
       -not(Test-Path -LiteralPath ([string]$p.stagedAuthenticSample) -PathType Leaf) -or
       (Get-FileHash -LiteralPath ([string]$p.stagedAuthenticSample) -Algorithm SHA256).Hash -ine [string]$p.sampleSha256){
        throw "Invalid/missing genuine original Xbox source XMA packet sample: $key"
    }
    $wanted[$key]=$p
}
if($wanted.Count -ne 6){throw "Expected six and only six genuine original Xbox XMA sample relationships; have $($wanted.Count)"}
$lines=@([IO.File]::ReadAllLines($trace)|Where-Object{$_ -match '^\[WAW360-XMAALIASMETA\] '})
$regex='^\[WAW360-XMAALIASMETA\] root=(\S+) index=(\d+) listName=(\S+) aliasName=(\S+) subtitle=(\S+) secondary=(\S+) chain=(\S+) type=(\d+) exists=(\d+) loadedOwned=(0|1) loadedName=(\S+) aliasHostBytes=([A-Fa-f0-9]{368}) loadedHostBytes=-?([A-Fa-f0-9]{320})$'
$seen=@{}
$rows=New-Object 'System.Collections.Generic.List[object]'
foreach($line in $lines){
    $m=[regex]::Match([string]$line.Trim(),$regex)
    if(-not $m.Success){throw "Genuine Xbox OAT Sound alias metadata malformed: $line"}
    $root=$m.Groups[1].Value
    $idx=[int]$m.Groups[2].Value
    $key=('{0}|{1}' -f $root,$idx)
    if(-not $wanted.ContainsKey($key)){
        # Do not discard other real source records: if the target alias
        # list contains additional SoundFile entries, the full graph writer
        # needs each of them, not a partially selected synthesized list.
        throw "Source Xbox Sound alias $key has additional unproven record; full native per-list graph required before emission."
    }
    if($seen.ContainsKey($key)){throw "Repeated original Xbox Sound alias source metadata: $key"}
    $seen[$key]=$true
    $ref=$wanted[$key]
    if($m.Groups[8].Value -ne '1' -or
       $m.Groups[9].Value -ne '1' -or
       $m.Groups[10].Value -ne '1' -or
       [string]$m.Groups[11].Value -cne [string]$ref.sourceLoadedSoundName -or
       [string]$m.Groups[4].Value -cne $root -or
       [string]$m.Groups[3].Value -cne $root){
        throw "Real original Xbox Sound struct owner/type/existence/loaded-sample mismatch: $key"
    }
    $rows.Add([pscustomobject][ordered]@{
        originalXboxSoundRoot=$root
        originalXboxSoundAliasIndex=$idx
        originalXboxAliasName=[string]$m.Groups[4].Value
        originalXboxSubtitle=if($m.Groups[5].Value -eq '-'){$null}else{[string]$m.Groups[5].Value}
        originalXboxSecondaryAlias=if($m.Groups[6].Value -eq '-'){$null}else{[string]$m.Groups[6].Value}
        originalXboxChainAlias=if($m.Groups[7].Value -eq '-'){$null}else{[string]$m.Groups[7].Value}
        originalXboxSoundFileType=[int]$m.Groups[8].Value
        originalXboxSoundFileExists=[int]$m.Groups[9].Value
        sourceOwnedLoadedSoundName=[string]$m.Groups[11].Value
        originalXboxX86SoundAlias184Hex=[string]$m.Groups[12].Value
        originalXboxX86LoadedSound160Hex=[string]$m.Groups[13].Value
        authenticSourceXma=[string]$ref.stagedAuthenticSample
        authenticSourceXmaSha256=[string]$ref.sampleSha256
        actualRiffXmaFileBytes=[long]$ref.sampleBytes
        nativeSoundFileAudioPacketReemitted=$false
        exactOriginalXboxSourceMetadataCaptured=$true
    })
}
if($seen.Count -ne 6){throw "Actual typed Xbox metadata covers $($seen.Count)/6 loaded Sound aliases."}
if((Get-FileHash -LiteralPath ([string]$v.sourceXboxRetailFf) -Algorithm SHA256).Hash -ine
   [string]$v.sourceXboxRetailSha256){
    throw 'Original retail Xbox FF changed during metadata observation.'
}
$body=[ordered]@{
    schema='WaW360.AztecOriginalXboxFlameSixSoundAliasLoadedMetadata.v6'
    status='SIX_ACTUAL_XBOX_SOUND_184B_LOADED_160B_AND_XMA_SOURCE_READY_FOR_STAGE4D_4E_NATIVE_REBASE'
    originalXboxRetailFf=[string]$v.sourceXboxRetailFf
    originalXboxRetailFfSha256=[string]$v.sourceXboxRetailSha256
    pinnedOatRevision=[string]$c.oatRevision
    verifiedActualOriginalXboxAliasRootCount=6
    verifiedActualOriginalXboxLoadedSoundMetadataCount=6
    exactOriginalXboxSoundAliases=@($rows.ToArray())
    actualNativeSoundMetadataAndXmaFFGraftCompleted=$false
    originalPcSourceAndNativeDiagnosticFfUnmodified=$true
    xboxCodXeAudioRuntimeProven=$false
}
$body|ConvertTo-Json -Depth 13|Set-Content -LiteralPath $out -Encoding UTF8
Write-Host "[AZTEC-XMA-V6] six retail Xbox 360 source-owned 184B alias records + six 160B genuine LoadedSound records verified; original Xbox RIFF XMA packet files SHA-preserved"
Write-Host "[AZTEC-XMA-V6] SOURCE=$out"
Write-Host '[AZTEC-XMA-V6-PROGRESS] [####################] 100% | handoff to actual native Stage 4D/4E Sound/LoadedSound FF rebase'
