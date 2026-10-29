[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'

$repo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$base=Join-Path $repo 'output\second_map_baselines\nazi_zombie_aztec_V2.2'
$n=Join-Path $base 'native_material_mapping'
$auditPath=Join-Path $n 'pc_two_model_material_audit\aztec_two_model_material_dependency_audit.json'
$baselinePath=Join-Path $base 'second_map_baseline.json'
$sourceProofPath=Join-Path $n 'pc_fastfile_image_sources\pc_fastfile_image_sources.json'
$pcOat=Join-Path $repo '.deps\OpenAssetTools-xenon-prebuilt\Release_x86\Unlinker.exe'
$work=Join-Path $n 'pc_two_model_material_source_bodies'
$materialOut=Join-Path $work 'pc_material_json'
$logRoot=Join-Path $work 'logs'
$reportPath=Join-Path $work 'aztec_two_model_pc_material_bodies.json'
$textPath=Join-Path $work 'aztec_two_model_pc_material_bodies.txt'

foreach($path in @($auditPath,$baselinePath,$sourceProofPath,$pcOat)){
    if(-not (Test-Path -LiteralPath $path -PathType Leaf)){
        throw "Aztec original-PC material body prerequisite missing: $path"
    }
}
$audit=Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json
$baseline=Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
$sourceProof=Get-Content -LiteralPath $sourceProofPath -Raw | ConvertFrom-Json
if([string]$audit.schema -ne 'WaW360.AztecPcTwoModelMaterialDependencyAudit.v1' -or
   [string]$audit.status -ne 'AZTEC_PC_TWO_MODEL_MATERIAL_DEPENDENCY_AUDIT_PASS' -or
   [int]$audit.nativeRoots -ne 8509 -or
   [int]$audit.sourcePcXModels -ne 2 -or
   [int]$audit.nativeMissingMaterialRefs -ne 0 -or
   [int]$audit.nativeAmbiguousMaterialRefs -ne 0 -or
   [int]$audit.nonNullMaterialHandles -ne 2 -or
   [int]$audit.pcMaterialNameEvidenceRefs -ne 2 -or
   [int]$audit.materialBodiesPromoted -ne 0 -or
   -not [bool]$audit.validationOnly -or [bool]$audit.readyForXbox -or
   [string]$baseline.format -ne 'WaW360.SecondMapBaseline.v1' -or
   [string]$baseline.mapName -ine 'nazi_zombie_aztec_V2.2'){
    throw 'Expected exact two-model PC-material identity audit PASS and unchanged original-PC baseline.'
}
function Normalize-Name([string]$value){
    $s=$value.Trim().Trim('"').Replace('\','/').TrimStart('/')
    while($s.StartsWith(',',[StringComparison]::Ordinal)){$s=$s.Substring(1)}
    if($s.EndsWith('_wj',[StringComparison]::OrdinalIgnoreCase)){$s=$s.Substring(0,$s.Length-3)}
    return $s.ToLowerInvariant()
}
function Material-Stem([string]$value){
    $s=$value.Trim()
    if([string]::IsNullOrWhiteSpace($s) -or [IO.Path]::IsPathRooted($s) -or
       $s.Contains('..') -or $s.Contains(':')){
        throw "Unsafe original PC Material identity path: $value"
    }
    if($s.StartsWith('*',[StringComparison]::Ordinal)){
        $s=$s.Replace('*','_')
        $paren=$s.IndexOf('(')
        if($paren -ge 0){$s=$s.Substring(0,$paren)}
        $s='generated/'+$s
    }
    return $s.Replace('\','/')
}

$sourceByRole=@{}
foreach($row in @($sourceProof.sourceFastfiles)){
    $role=[string]$row.Role
    if($sourceByRole.ContainsKey($role)){throw "Duplicate source-SHA role: $role"}
    $sourceByRole[$role]=$row
}
$roles=@{}
foreach($ff in @($baseline.fastfiles)){
    $role=[string]$ff.Role
    if($role -notin @('map.ff','map_patch.ff','mod.ff')){continue}
    if($roles.ContainsKey($role) -or -not $sourceByRole.ContainsKey($role)){
        throw "Missing/duplicate pinned original PC source: $role"
    }
    $pin=$sourceByRole[$role]
    if([string]$ff.Path -ine [string]$pin.SourceFastFile -or
       -not (Test-Path -LiteralPath ([string]$ff.Path) -PathType Leaf) -or
       [long](Get-Item -LiteralPath ([string]$ff.Path)).Length -ne [long]$ff.Bytes -or
       (Get-FileHash -LiteralPath ([string]$ff.Path) -Algorithm SHA256).Hash -ine [string]$pin.Sha256){
        throw "Original PC source fastfile changed since SHA-pinned census: $role"
    }
    $roles[$role]=[pscustomobject]@{Path=[string]$ff.Path;SHA256=[string]$pin.Sha256}
}
if($roles.Count -ne 3){throw 'Expected map.ff, map_patch.ff and mod.ff original PC sources.'}

# The two real model surface references define the entire extraction target.
# OAT may export other material JSON from a zone; do not promote these.
$wanted=@{}
foreach($dep in @($audit.dependencies)){
    $key=Normalize-Name ([string]$dep.materialName)
    if([string]::IsNullOrWhiteSpace($key)){throw 'Empty XModel material identity.'}
    if([int]$dep.nativeMatchCount -ne 1 -or [int]$dep.pcSourceNameMatches -lt 1){
        throw "Material $key no longer has exact native/PC identity evidence."
    }
    if(-not $wanted.ContainsKey($key)){
        $wanted[$key]=[pscustomobject]@{Name=[string]$dep.materialName;Sources=@{}}
    }
    foreach($hit in @($dep.pcSourceNameEvidence)){
        $role=[string]$hit.role
        $name=[string]$hit.name
        if(-not $roles.ContainsKey($role) -or (Normalize-Name $name) -ne $key){
            throw "Material $key source role/name evidence mismatch."
        }
        if($wanted[$key].Sources.ContainsKey($role) -and
           [string]$wanted[$key].Sources[$role] -ine $name){
            throw "Ambiguous original PC Material identities within role $role for $key."
        }
        $wanted[$key].Sources[$role]=$name
    }
}
if($wanted.Count -lt 1 -or $wanted.Count -gt 2){
    throw "Expected 1-2 distinct material identities across two PC model handles; got $($wanted.Count)"
}
New-Item -Path $work,$materialOut,$logRoot -ItemType Directory -Force | Out-Null
$bodyRows=New-Object 'System.Collections.Generic.List[object]'
$status='AZTEC_PC_TWO_MODEL_MATERIAL_BODIES_BLOCKED'
$blocker=''
$runRoles=@($wanted.Values | ForEach-Object {$_.Sources.Keys} | Sort-Object -Unique)
Write-Host '[AZTEC-PC-MATERIAL] ==============================================='
Write-Host '[AZTEC-PC-MATERIAL] SOURCE PC MATERIAL BODIES FOR 2 XMODELS'
Write-Host "[AZTEC-PC-MATERIAL] distinct materials=$($wanted.Count) PC fastfile roles=$($runRoles.Count)"
Write-Host '[AZTEC-PC-MATERIAL] No Xbox donor bytes, no FastFile linking, NOT FOR XBOX.'
try{
    foreach($role in $runRoles){
        $short=Join-Path ([IO.Path]::GetTempPath()) ('wawAzMat_'+[guid]::NewGuid().ToString('N').Substring(0,8))
        if($short.Length -gt 115){throw 'PC OAT material extraction requires a short physical temp path.'}
        New-Item -Path $short -ItemType Directory -Force | Out-Null
        $log=Join-Path $logRoot (($role.Replace('.','_'))+'_material_unlink.log')
        try{
            $prev=$ErrorActionPreference
            try{
                $ErrorActionPreference='Continue'
                & $pcOat '--no-color' '--include-assets' 'material' '--output-folder' $short ([string]$roles[$role].Path) 2>&1 |
                    ForEach-Object {[string]$_} | Set-Content -LiteralPath $log -Encoding UTF8
                $exitCode=[int]$LASTEXITCODE
            }finally{$ErrorActionPreference=$prev}
            if($exitCode -ne 0 -or
               @(Select-String -LiteralPath $log -Pattern '^\s*ERROR:').Count -gt 0 -or
               @(Select-String -LiteralPath $log -Pattern '(?i)^\s*Finished with\s+0\s+warnings?,\s+0\s+errors?\s*$').Count -eq 0){
                throw "PC OAT material JSON export failed zero-error gate for $role; inspect $log"
            }
            $files=@(Get-ChildItem -LiteralPath $short -Filter '*.json' -File -Recurse -ErrorAction Stop)
            foreach($target in @($wanted.Values)){
                if(-not $target.Sources.ContainsKey($role)){continue}
                $name=[string]$target.Sources[$role]
                $suffix=('/materials/'+(Material-Stem $name)+'.json').ToLowerInvariant()
                $hits=@($files | Where-Object {
                    ([string]$_.FullName).Replace('\','/').ToLowerInvariant().EndsWith($suffix,[StringComparison]::Ordinal)
                })
                if($hits.Count -ne 1){
                    throw "PC material $name in $role has $($hits.Count) matching OAT JSON body files (expected 1); inspect $log"
                }
                $doc=Get-Content -LiteralPath $hits[0].FullName -Raw | ConvertFrom-Json
                $state=@()
                if($doc.PSObject.Properties.Name -contains 'stateBitsEntry' -and $null -ne $doc.stateBitsEntry){
                    $state=@($doc.stateBitsEntry)
                }
                if($state.Count -ne 59){
                    throw "PC material $name stateBitsEntry count=$($state.Count), expected T4 width 59"
                }
                $textures=@()
                if($doc.PSObject.Properties.Name -contains 'textures' -and $null -ne $doc.textures){
                    $textures=@($doc.textures)
                }
                $constants=@()
                if($doc.PSObject.Properties.Name -contains 'constants' -and $null -ne $doc.constants){
                    $constants=@($doc.constants)
                }
                $bits=@()
                if($doc.PSObject.Properties.Name -contains 'stateBits' -and $null -ne $doc.stateBits){
                    $bits=@($doc.stateBits)
                }
                $textureRefs=New-Object 'System.Collections.Generic.List[string]'
                foreach($tex in $textures){
                    if($null -ne $tex -and $tex.PSObject.Properties.Name -contains 'image' -and
                        -not [string]::IsNullOrWhiteSpace([string]$tex.image)){
                        $textureRefs.Add([string]$tex.image)
                    }
                }
                $dest=Join-Path (Join-Path $materialOut ($role.Replace('.','_'))) ((Material-Stem $name).Replace('/','\')+'.json')
                $destDir=Split-Path -Parent $dest
                New-Item -Path $destDir -ItemType Directory -Force | Out-Null
                Copy-Item -LiteralPath $hits[0].FullName -Destination $dest -Force
                $hash=(Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash
                $originalHash=(Get-FileHash -LiteralPath $hits[0].FullName -Algorithm SHA256).Hash
                if($hash -ine $originalHash){throw "PC material JSON staging SHA mismatch for $name"}
                $technique=''
                if($doc.PSObject.Properties.Name -contains 'techniqueSet' -and $null -ne $doc.techniqueSet){
                    $technique=[string]$doc.techniqueSet
                }
                $bodyRows.Add([pscustomobject]@{
                    material=$name;normalized=(Normalize-Name $name)
                    sourceRole=$role;sourceFastFile=[string]$roles[$role].Path
                    sourceFastFileSha256=[string]$roles[$role].SHA256
                    materialJson=$dest;materialJsonSha256=$hash
                    techniqueSet=$technique
                    textures=$textures.Count;imageReferences=@($textureRefs.ToArray())
                    constants=$constants.Count;stateBits=$bits.Count;stateBitsEntry=$state.Count
                })
                Write-Host "[AZTEC-PC-MATERIAL] $role $name JSON SHA verified; textures=$($textures.Count) stateBitsEntry=59"
            }
        }finally{
            if(Test-Path -LiteralPath $short -PathType Container){Remove-Item -LiteralPath $short -Recurse -Force}
        }
    }
    foreach($target in @($wanted.Values)){
        $key=Normalize-Name ([string]$target.Name)
        $matching=@($bodyRows.ToArray() | Where-Object {[string]$_.normalized -eq $key})
        if($matching.Count -ne $target.Sources.Count){
            throw "PC material $key source-variant coverage incomplete: $($matching.Count)/$($target.Sources.Count)"
        }
        $hashes=@($matching | ForEach-Object {[string]$_.materialJsonSha256} | Sort-Object -Unique)
        if($hashes.Count -ne 1){
            throw "PC material $key has $($hashes.Count) different original PC JSON variants across roles; automatic selection prohibited."
        }
    }
    $status='AZTEC_PC_TWO_MODEL_MATERIAL_BODIES_PASS'
}catch{
    $blocker=$_.Exception.Message
    Write-Host "[AZTEC-PC-MATERIAL] BLOCKER: $blocker"
}
$result=[ordered]@{
    schema='WaW360.AztecPcTwoModelMaterialBodies.v1'
    status=$status
    materialsRequested=$wanted.Count
    pcMaterialBodiesExtracted=$bodyRows.Count
    originalPcMaterialBodies=$bodyRows.ToArray()
    blocker=$blocker
    readyForXbox=$false;materialBodiesGraftedToXenon=0;fullMapConverted=$false
    note='Only PC-source OAT material JSON metadata/body extraction. No Xenon material serialization; no TechniqueSet/shader or image closure proof. Never deploy.'
}
$result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $reportPath -Encoding UTF8
$summary=@(
    "AZTEC ORIGINAL PC MATERIAL BODY PROBE: $status"
    "distinct material identities=$($wanted.Count), JSON variants extracted=$($bodyRows.Count)"
    "blocker=$blocker"
    'PC JSON is source-backed only; no Xbox material graph/FF emitted.'
    'NOT FOR XBOX.'
)
[IO.File]::WriteAllLines($textPath,[string[]]$summary,[Text.UTF8Encoding]::new($false))
Write-Host '[AZTEC-PC-MATERIAL] ================= RESULT ========================'
Write-Host "[AZTEC-PC-MATERIAL] status=$status"
Write-Host "[AZTEC-PC-MATERIAL] PC material JSON variants=$($bodyRows.Count), distinct identities=$($wanted.Count)"
Write-Host "[AZTEC-PC-MATERIAL] report=$textPath"
Write-Host '[AZTEC-PC-MATERIAL] No Xbox FF / NOT FOR XBOX'
if($status -ne 'AZTEC_PC_TWO_MODEL_MATERIAL_BODIES_PASS'){exit 4}
