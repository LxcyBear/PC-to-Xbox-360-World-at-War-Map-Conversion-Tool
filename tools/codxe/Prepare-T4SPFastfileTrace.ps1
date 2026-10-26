param(
    [string]$Destination = "",
    [switch]$ForceReset
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $repoRoot ".deps\codxe-waw360-trace"
}

$codxeCommit = "421fe682c5ce069b2f8e03ddd1b6999c23644d07"
$sourceFile = Join-Path $Destination "src\game\t4\sp\components\fastfiles.cpp"

Write-Host "[WAW360TRACE] Destination: $Destination"

if (-not (Test-Path (Join-Path $Destination ".git"))) {
    New-Item -ItemType Directory -Force -Path (Split-Path $Destination -Parent) | Out-Null
    git clone https://github.com/michaeloliverx/codxe.git "$Destination"
    if ($LASTEXITCODE -ne 0) { throw "git clone failed: $LASTEXITCODE" }
}

Push-Location $Destination
try {
    git fetch origin
    if ($LASTEXITCODE -ne 0) { throw "git fetch failed: $LASTEXITCODE" }

    if ($ForceReset) {
        git reset --hard
        git clean -fd
    }

    git checkout --detach $codxeCommit
    if ($LASTEXITCODE -ne 0) { throw "git checkout failed: $LASTEXITCODE" }

    $text = Get-Content -LiteralPath $sourceFile -Raw

    if ($text.Contains("[WAW360TRACE] DB_LoadXAssets ENTER")) {
        Write-Host "[WAW360TRACE] Trace patch already present."
    }
    else {
        $needle = @'
unsigned int expandedXAssetPoolCount = 0;
'@
        $replacement = @'
unsigned int expandedXAssetPoolCount = 0;

// WaW360Converter hardware diagnostic only.
// Non-breaking trace of the T4 SP zone-load path. This deliberately avoids
// execution breakpoints so the native loader can run at normal timing.
unsigned int waw360TraceLoadSerial = 0;
'@
        if (-not $text.Contains($needle)) {
            throw "Could not find expandedXAssetPoolCount anchor."
        }
        $text = $text.Replace($needle, $replacement)

        $oldCreate = @'
int Sys_CreateFile_Hook(const char *filename, int desiredAccess, int shareMode, int securityAttributes,
                        int creationDisposition, int flagsAndAttributes)
{
    std::string redirectedPath = ResolveFastfilePath(filename);
    if (!redirectedPath.empty())
        filename = redirectedPath.c_str();
    else
    {
        redirectedPath = ResolveLooseSoundPath(filename);
        if (!redirectedPath.empty())
            filename = redirectedPath.c_str();
    }

    return Sys_CreateFile_Detour.GetOriginal<Sys_CreateFile_t>()(filename, desiredAccess, shareMode, securityAttributes,
                                                                 creationDisposition, flagsAndAttributes);
}
'@
        $newCreate = @'
int Sys_CreateFile_Hook(const char *filename, int desiredAccess, int shareMode, int securityAttributes,
                        int creationDisposition, int flagsAndAttributes)
{
    const char *requestedFilename = filename ? filename : "<null>";
    const bool requestedFastfile = EndsWithIgnoreCase(requestedFilename, ".ff");

    std::string redirectedPath = ResolveFastfilePath(filename);
    if (!redirectedPath.empty())
    {
        if (requestedFastfile)
        {
            DbgPrint("[WAW360TRACE] OPEN FF request='%s' redirect='%s'\n", requestedFilename, redirectedPath.c_str());
        }

        filename = redirectedPath.c_str();
    }
    else
    {
        redirectedPath = ResolveLooseSoundPath(filename);
        if (!redirectedPath.empty())
            filename = redirectedPath.c_str();
    }

    const int result = Sys_CreateFile_Detour.GetOriginal<Sys_CreateFile_t>()(
        filename, desiredAccess, shareMode, securityAttributes, creationDisposition, flagsAndAttributes);

    if (requestedFastfile)
    {
        DbgPrint("[WAW360TRACE] OPEN FF RETURN request='%s' actual='%s' result=%08X\n", requestedFilename,
                 filename ? filename : "<null>", static_cast<unsigned int>(result));
    }

    return result;
}
'@
        if (-not $text.Contains($oldCreate)) {
            throw "Could not find Sys_CreateFile_Hook body. Codxe source changed."
        }
        $text = $text.Replace($oldCreate, $newCreate)

        $oldLoad = @'
void DB_LoadXAssets_Hook(XZoneInfo *zoneInfo, unsigned int zoneCount, int sync)
{
    UpdateActiveUsermap(zoneInfo, zoneCount);

    for (unsigned int i = 0; i < zoneCount; ++i)
    {
        if (!RequiresPriorityOverride(zoneInfo[i].name))
            continue;

        // Preserve the map's normal allocation group while giving its intentional overrides priority.
        zoneInfo[i].allocFlags |= DB_ZONE_DEV;

        // Complete delayed asset clones before script compilation starts.
        sync = 1;
    }

    DB_LoadXAssets_Detour.GetOriginal<DB_LoadXAssets_t>()(zoneInfo, zoneCount, sync);
}
'@
        $newLoad = @'
void DB_LoadXAssets_Hook(XZoneInfo *zoneInfo, unsigned int zoneCount, int sync)
{
    const unsigned int traceSerial = ++waw360TraceLoadSerial;
    DbgPrint("[WAW360TRACE] DB_LoadXAssets ENTER serial=%u zoneInfo=%p count=%u sync=%d\n", traceSerial, zoneInfo,
             zoneCount, sync);

    for (unsigned int i = 0; i < zoneCount; ++i)
    {
        const char *zoneName = zoneInfo && zoneInfo[i].name ? zoneInfo[i].name : "<null>";
        const int allocFlags = zoneInfo ? zoneInfo[i].allocFlags : 0;
        const int freeFlags = zoneInfo ? zoneInfo[i].freeFlags : 0;
        DbgPrint("[WAW360TRACE] ZONE serial=%u index=%u name='%s' alloc=%08X free=%08X\n", traceSerial, i, zoneName,
                 static_cast<unsigned int>(allocFlags), static_cast<unsigned int>(freeFlags));
    }

    UpdateActiveUsermap(zoneInfo, zoneCount);

    for (unsigned int i = 0; i < zoneCount; ++i)
    {
        if (!RequiresPriorityOverride(zoneInfo[i].name))
            continue;

        // Preserve the map's normal allocation group while giving its intentional overrides priority.
        zoneInfo[i].allocFlags |= DB_ZONE_DEV;

        // Complete delayed asset clones before script compilation starts.
        sync = 1;

        DbgPrint("[WAW360TRACE] PRIORITY serial=%u index=%u name='%s' alloc=%08X sync=%d\n", traceSerial, i,
                 zoneInfo[i].name ? zoneInfo[i].name : "<null>", static_cast<unsigned int>(zoneInfo[i].allocFlags),
                 sync);
    }

    DbgPrint("[WAW360TRACE] DB_LoadXAssets CALL serial=%u count=%u sync=%d\n", traceSerial, zoneCount, sync);
    DB_LoadXAssets_Detour.GetOriginal<DB_LoadXAssets_t>()(zoneInfo, zoneCount, sync);
    DbgPrint("[WAW360TRACE] DB_LoadXAssets RETURN serial=%u\n", traceSerial);
}
'@
        if (-not $text.Contains($oldLoad)) {
            throw "Could not find DB_LoadXAssets_Hook body. Codxe source changed."
        }
        $text = $text.Replace($oldLoad, $newLoad)

        Set-Content -LiteralPath $sourceFile -Value $text -Encoding UTF8
        Write-Host "[WAW360TRACE] Patched T4 SP fastfile trace."
    }

    Write-Host ""
    git diff -- "src/game/t4/sp/components/fastfiles.cpp"
    Write-Host ""
    Write-Host "[WAW360TRACE] PREP PASS"
    Write-Host "[WAW360TRACE] Codxe source: $Destination"
    Write-Host "[WAW360TRACE] Build: Release|Xbox 360"
    Write-Host "[WAW360TRACE] Expected XEX: $(Join-Path $Destination 'build\Release\bin\codxe.xex')"
}
finally {
    Pop-Location
}
