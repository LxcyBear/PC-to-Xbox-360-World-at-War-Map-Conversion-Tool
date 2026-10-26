param(
    [string]$CodxeRoot = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($CodxeRoot)) {
    $CodxeRoot = Join-Path $repoRoot ".deps\codxe-waw360-trace"
}

$fastFile = Join-Path $CodxeRoot "src\game\t4\sp\components\fastfiles.cpp"
$clipFile = Join-Path $CodxeRoot "src\game\t4\sp\components\clipmap.cpp"

if (-not (Test-Path $fastFile)) {
    throw "Instrumented Codxe tree not found. Run Prepare-T4SPFastfileTrace.ps1 first."
}

$fast = Get-Content -LiteralPath $fastFile -Raw
if (-not $fast.Contains("[WAW360TRACE] DB_LoadXAssets ENTER")) {
    throw "Base WAW360TRACE patch is missing. Run Prepare-T4SPFastfileTrace.ps1 first."
}

if (-not $fast.Contains("[WAW360TRACE] ASSET RING DUMP")) {
    $stateNeedle = @'
unsigned int waw360TraceLoadSerial = 0;
'@
    $stateReplacement = @'
unsigned int waw360TraceLoadSerial = 0;

// Deep trace: keep the last DB_GetXAssetName calls in memory instead of
// printing each one. This keeps the loader timing close to normal while still
// giving us a breadcrumb trail if the title aborts during the synchronous
// custom-map DB_LoadXAssets call.
volatile bool waw360TraceMainZoneActive = false;
volatile LONG waw360TraceAssetSeq = 0;

struct Waw360TraceAssetBreadcrumb
{
    unsigned int sequence;
    unsigned int type;
    unsigned int header;
    char name[64];
};

Waw360TraceAssetBreadcrumb waw360TraceAssetRing[64];
Detour DB_GetXAssetName_Detour;

void Waw360TraceCopyName(char *destination, const char *source)
{
    if (!source)
        source = "<null>";

    unsigned int i = 0;
    for (; i + 1 < 64 && source[i]; ++i)
        destination[i] = source[i];

    destination[i] = '\0';
}

const char *DB_GetXAssetName_Hook(const XAsset *asset)
{
    if (!waw360TraceMainZoneActive)
        return DB_GetXAssetName_Detour.GetOriginal<DB_GetXAssetName_t>()(asset);

    const unsigned int sequence =
        static_cast<unsigned int>(InterlockedIncrement(&waw360TraceAssetSeq));
    Waw360TraceAssetBreadcrumb &breadcrumb = waw360TraceAssetRing[(sequence - 1) % ARRAYSIZE(waw360TraceAssetRing)];

    breadcrumb.sequence = sequence;
    breadcrumb.type = asset ? static_cast<unsigned int>(asset->type) : 0xFFFFFFFF;
    breadcrumb.header = asset ? reinterpret_cast<unsigned int>(asset->header.data) : 0;
    Waw360TraceCopyName(breadcrumb.name, "<pending>");

    const char *name = DB_GetXAssetName_Detour.GetOriginal<DB_GetXAssetName_t>()(asset);
    Waw360TraceCopyName(breadcrumb.name, name);
    return name;
}

void Waw360TraceDumpAssetRing()
{
    if (!waw360TraceMainZoneActive)
        return;

    const unsigned int total = static_cast<unsigned int>(waw360TraceAssetSeq);
    const unsigned int count = total < ARRAYSIZE(waw360TraceAssetRing) ? total : ARRAYSIZE(waw360TraceAssetRing);
    const unsigned int firstSequence = total - count + 1;

    DbgPrint("[WAW360TRACE] ASSET RING DUMP active=1 total=%u showing=%u\n", total, count);

    for (unsigned int sequence = firstSequence; sequence <= total; ++sequence)
    {
        const Waw360TraceAssetBreadcrumb &breadcrumb =
            waw360TraceAssetRing[(sequence - 1) % ARRAYSIZE(waw360TraceAssetRing)];

        if (breadcrumb.sequence != sequence)
            continue;

        DbgPrint("[WAW360TRACE] ASSET seq=%u type=%u header=%08X name='%s'\n", breadcrumb.sequence,
                 breadcrumb.type, breadcrumb.header, breadcrumb.name);
    }
}
'@
    if (-not $fast.Contains($stateNeedle)) {
        throw "Could not find WAW360TRACE state anchor in fastfiles.cpp."
    }
    $fast = $fast.Replace($stateNeedle, $stateReplacement)

    $priorityNeedle = @'
        DbgPrint("[WAW360TRACE] PRIORITY serial=%u index=%u name='%s' alloc=%08X sync=%d\n", traceSerial, i,
                 zoneInfo[i].name ? zoneInfo[i].name : "<null>", static_cast<unsigned int>(zoneInfo[i].allocFlags),
                 sync);
'@
    $priorityReplacement = @'
        DbgPrint("[WAW360TRACE] PRIORITY serial=%u index=%u name='%s' alloc=%08X sync=%d\n", traceSerial, i,
                 zoneInfo[i].name ? zoneInfo[i].name : "<null>", static_cast<unsigned int>(zoneInfo[i].allocFlags),
                 sync);

        waw360TraceMainZoneActive = true;
        waw360TraceAssetSeq = 0;
        memset(waw360TraceAssetRing, 0, sizeof(waw360TraceAssetRing));
        DbgPrint("[WAW360TRACE] ASSET RING ARMED serial=%u zone='%s'\n", traceSerial,
                 zoneInfo[i].name ? zoneInfo[i].name : "<null>");
'@
    if (-not $fast.Contains($priorityNeedle)) {
        throw "Could not find PRIORITY trace anchor in fastfiles.cpp."
    }
    $fast = $fast.Replace($priorityNeedle, $priorityReplacement)

    $returnNeedle = @'
    DB_LoadXAssets_Detour.GetOriginal<DB_LoadXAssets_t>()(zoneInfo, zoneCount, sync);
    DbgPrint("[WAW360TRACE] DB_LoadXAssets RETURN serial=%u\n", traceSerial);
'@
    $returnReplacement = @'
    DB_LoadXAssets_Detour.GetOriginal<DB_LoadXAssets_t>()(zoneInfo, zoneCount, sync);
    DbgPrint("[WAW360TRACE] DB_LoadXAssets RETURN serial=%u\n", traceSerial);

    if (waw360TraceMainZoneActive)
    {
        Waw360TraceDumpAssetRing();
        waw360TraceMainZoneActive = false;
    }
'@
    if (-not $fast.Contains($returnNeedle)) {
        throw "Could not find DB_LoadXAssets RETURN trace anchor in fastfiles.cpp."
    }
    $fast = $fast.Replace($returnNeedle, $returnReplacement)

    $ctorNeedle = @'
    DB_LoadXAssets_Detour = Detour(DB_LoadXAssets, DB_LoadXAssets_Hook);
    DB_LoadXAssets_Detour.Install();
}
'@
    $ctorReplacement = @'
    DB_LoadXAssets_Detour = Detour(DB_LoadXAssets, DB_LoadXAssets_Hook);
    DB_LoadXAssets_Detour.Install();

    DB_GetXAssetName_Detour = Detour(DB_GetXAssetName, DB_GetXAssetName_Hook);
    DB_GetXAssetName_Detour.Install();
}
'@
    if (-not $fast.Contains($ctorNeedle)) {
        throw "Could not find FastFiles constructor anchor."
    }
    $fast = $fast.Replace($ctorNeedle, $ctorReplacement)

    $dtorNeedle = @'
FastFiles::~FastFiles()
{
    DB_LoadXAssets_Detour.Remove();
    Sys_CreateFile_Detour.Remove();

    RestoreExpandedXAssetPools();

    activeUsermap[0] = '\0';
}
'@
    $dtorReplacement = @'
FastFiles::~FastFiles()
{
    // If the custom main-zone call never returned, dump its final asset-name
    // breadcrumbs while codxe is still alive and Watson can receive them.
    Waw360TraceDumpAssetRing();

    DB_GetXAssetName_Detour.Remove();
    DB_LoadXAssets_Detour.Remove();
    Sys_CreateFile_Detour.Remove();

    RestoreExpandedXAssetPools();

    waw360TraceMainZoneActive = false;
    activeUsermap[0] = '\0';
}
'@
    if (-not $fast.Contains($dtorNeedle)) {
        throw "Could not find FastFiles destructor anchor."
    }
    $fast = $fast.Replace($dtorNeedle, $dtorReplacement)

    Set-Content -LiteralPath $fastFile -Value $fast -Encoding UTF8
    Write-Host "[WAW360TRACE] Added 64-entry DB_GetXAssetName breadcrumb ring."
}
else {
    Write-Host "[WAW360TRACE] Asset breadcrumb ring already present."
}

if (-not (Test-Path $clipFile)) {
    throw "clipmap.cpp not found: $clipFile"
}

$clip = Get-Content -LiteralPath $clipFile -Raw
if (-not $clip.Contains("[WAW360TRACE] CLIPMAP ENTER")) {
    $clipNeedle = @'
void Load_clipMap_t_Hook(bool atStreamStart)
{
    // Call the original function to load the clip map
    Load_clipMap_t_Detour.GetOriginal<decltype(Load_clipMap_t)>()(atStreamStart);

    if (!varclipMap_t || !*varclipMap_t || !(*varclipMap_t)->name || !(*varclipMap_t)->mapEnts)
        return;
'@
    $clipReplacement = @'
void Load_clipMap_t_Hook(bool atStreamStart)
{
    DbgPrint("[WAW360TRACE] CLIPMAP ENTER atStreamStart=%d varclipMap_t=%p\n", atStreamStart ? 1 : 0, varclipMap_t);

    // Call the original function to load the clip map
    Load_clipMap_t_Detour.GetOriginal<decltype(Load_clipMap_t)>()(atStreamStart);

    const char *traceName =
        (varclipMap_t && *varclipMap_t && (*varclipMap_t)->name) ? (*varclipMap_t)->name : "<null>";
    DbgPrint("[WAW360TRACE] CLIPMAP RETURN atStreamStart=%d name='%s'\n", atStreamStart ? 1 : 0, traceName);

    if (!varclipMap_t || !*varclipMap_t || !(*varclipMap_t)->name || !(*varclipMap_t)->mapEnts)
        return;
'@
    if (-not $clip.Contains($clipNeedle)) {
        throw "Could not find Load_clipMap_t_Hook anchor in clipmap.cpp."
    }
    $clip = $clip.Replace($clipNeedle, $clipReplacement)
    Set-Content -LiteralPath $clipFile -Value $clip -Encoding UTF8
    Write-Host "[WAW360TRACE] Added clipmap ENTER/RETURN trace."
}
else {
    Write-Host "[WAW360TRACE] Clipmap trace already present."
}

Write-Host ""
Write-Host "[WAW360TRACE] DEEP TRACE PREP PASS"
Write-Host "[WAW360TRACE] Rebuild with .\tools\codxe\Build-T4SPFastfileTrace.ps1"
