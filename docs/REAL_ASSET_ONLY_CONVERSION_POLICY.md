# PC2XB3 — real asset only conversion policy (2026-10-31)

Effective immediately for any **new** asset, material, texture, model, animation, WeaponDef, sound, world, map or release candidate in World at War PC → Xbox 360/CodXe conversion.

## Non-negotiable requirements

1. Authentic PC-owned asset bytes must come from the actual source mod/FastFile, verified against source file SHA-256, typed T4 layout and real asset identity, then endian/relocation-converted into the correct Xbox native format.
2. Genuine Xbox 360 retail stock references are acceptable only when the exact asset name, type, stock residency and genuine native body/identity are independently proven. Do not import unrelated donors or oracle payloads as replacement game data.
3. A source-authentic NULL remains NULL. A **non-NULL original PC source reference must not silently become NULL, a dummy pointer, an invented object, a fake material, an empty pixel image, a blank XModel or sound, or a fabricated dependency** in any new production-intended output. If its referenced asset cannot be proven, fail closed and report the unresolved dependency.
4. Never fabricate textures, shader statebits, materials, techniques, meshes/vertices, animations, XStrings, sound aliases, XMA samples, world/lighting/FX/BSP/cell data to increase FF size or claim completion. An identity/name shell is not an asset payload.
5. Every non-NULL PC asset child must link to an independently verified original-PC-derived Xbox-compatible body or a proven genuine Xbox retail stock asset. Original packed PC pointer words are not valid native rebases.
6. OAT zero warnings/errors establishes FF structural acceptance only. CodXe/Xbox gameplay needs a complete asset/dependency graph, correct texture/renderer/sound runtime semantics, full map/_load package when required, then hardware smoke testing.
7. Audit reports separate these categories explicitly: source-owned real bytes; physically emitted real native bodies; verified genuine retail reference; authentic source NULL; identity-only shell; unresolved non-NULL pointer; blocked foreign dependency. A production release must fail closed if a required asset is unresolved or shell-only.
8. The existing historical OAT diagnostic candidates with identity-only roots or intentionally omitted children **remain diagnostic artifacts only**. No additional filler/shell replacement to masquerade as progress, and no promoting historical shell-only content to release.

## Current Aztec verified state vs remaining real asset work

- The user's Windows FF at git commit 1112a89 is a **4,514,599-byte OAT-good diagnostic FF**, with 8,509 verified root identities but NOT 8,509 fully working asset bodies. Sixty-three source-owned WeaponDef roots contain partial genuine children. Two conflicting weapon variants and four Xbox stock-name-only roots still need real closure.
- Genuine source-owned bounce-sound tables: 2 real inline 124-byte tables; 62 original PC nested sound alias records. Additionally 160 fixed WeaponDef sound alias records and 41 original PC inline names (696 bytes) are in the FF. Total **222/228 source-owned alias records in FF**, with 50 source-inline sound name strings containing 895 authentic bytes. No actual sound/XMA sample payloads are proven.
- The independent 7b FlameTable source-only witness at commit 410dfd7 found **four genuine 476-byte inline parent bodies = 1,904 bytes**, **122 natural source NULL** parent fields, zero packed/external FlameTable parents, and **six genuinely owned nested sound alias records**. These four parents and six children were NOT added to the native FF.
- To emit a real FlameTable, first prove parent XString source bytes at +424 (when non-NULL); all eight Material pointers at +428..+456 as real PC source-converted material or exact verified genuine stock; and the four sound alias pointers at +460..+472. Do not zero any originally non-NULL parent material/name reference to force the 476-byte graph into an FF. The six 4-byte SndAliasCustom children do not contain decoded sound samples.
- Six GfxImage identity-name shell bindings still lack actual pixel payload, one external stock XModel lacks residency proof, and GfxWorld/material runtime semantics remain unresolved. These are release blockers, not valid filler data.

## Immediate source-only real-dependency audit

The repository script tools/oat-xenon/Run-AztecWeapon63FlameRealAssetDependencyAudit.ps1 will independently check the SHA-pinned four FlameTable source parents and the previous passing OAT FF. It classifies **32 Material pointer cells**, **four parent name pointers**, and **16 nested sound pointer cells** without creating any new FF, adding a placeholder material or mutating previous accepted binaries.

A non-NULL source pointer in that report is an unresolved real asset link until backed by genuine data, never permission to replace it with a zero. Next stage: actual PC source material/name resolution, exact Xbox body/stock binding, then complete typed FlameTable parent/child graph with no placeholders.


## Actual four FlameTable parents: dependency findings and next source-byte proof

The user ran the FlameTable real-asset dependency audit on Windows at commit 151546b. It passed all 71 PS5.1 source syntax checks and verified:

- Four true source-owned 476-byte FlameTable parents (1,904 authentic source bytes), 122 naturally NULL parent pointers, no packed/external parents.
- Parent name fields: **4 non-NULL / 4 total**, but *none* may be treated as a real string until inline source bytes or a packed string's original source target is proven.
- Parent material slots: **12 authentic NULL / 32; 20 NON-NULL** requiring original-PC Material source proof and valid native Xbox backing.
- Parent sound slots: **6 genuinely INLINE SndAliasCustom records / 16; 4 authentic NULL; 6 PACKED references**. No material, name, or actual XMA/audio sample resolved by these counts.
- No native FF created or modified. Status: AZTEC_REAL_PC_FLAMETABLE_PARENT_DEPENDENCIES_CLASSIFIED_NOT_NATIVE_ASSET_CLOSED.

A stricter next read-only original-PC source witness, tools/oat-xenon/Run-AztecWeapon63FlameInlineRealSourceBodies.ps1, uses the existing genuine 7b walker's physical ownership order. The typed collector records:

- Each **actually INLINE FlameTable name** at parent byte +424 with its genuine original-PC 7b physical byte offset, byte length and SHA-256. Packed name references remain unresolved.
- Each **actually INLINE Material child** at parent +428..+456 with the *entire consumed original-PC 7b typed serialized child extent*, byte length and SHA-256. The raw owned span is evidence, not proof that the Material's technique/images/stream dependencies are native runtime complete.
- Parent and name/material pointer classes MUST conserve exactly four non-NULL names, 20 non-NULL Materials and 12 authentic NULL Materials. No invented bodies, empty replacement textures, or false native binding.
- Existing 4,514,599-byte 222/228-record OAT-good FF hash, original-PC proof containers and previous parent/dependency reports are preconditions. Output goes to a **new** source-only report folder, not the original FlameTable witness or any native FF.

To run from repo root:

    git pull --ff-only
    git rev-parse --short HEAD
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1"
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Run-AztecWeapon63FlameInlineRealSourceBodies.ps1"

The result will report precisely how many of the 4 FlameTable names and 20 non-NULL Material pointers are *physically INLINE in genuine original-PC source*. Non-INLINE pointers require real source target resolution; a Material body still needs actual converted technique/image dependencies and Xbox semantic validation. **No new FF or Xbox deployment is authorized.**


## One-build source resolution sprint: FlameTable packed Material*, real names, sound/XMA provenance

The user's confirmed 2026-10-31 results at fb9bffa:
- Original PC FlameTable name fields: **1 real INLINE 24-byte string; 3 PACKED source strings unresolved**.
- 32 Material slots: **3 real INLINE source material child bodies, 892 verified source serialized bytes; 17 packed source Material* references; 12 original source NULL**.
- 16 sound slots: **6 real source-owned INLINE alias pointer records; 6 packed references; 4 original source NULL**.
- Four 476-byte FlameTable parent bodies are real source-owned. The 4,514,599-byte prior OAT-pass candidate remains untouched; 222/228 genuine alias records in that scratch candidate. NONE of the FlameTable parents has been emitted into it.

New sprint stage in commit family after fb9bffa:

- tools/oat-xenon/Run-AztecRealAssetResolutionSprint.ps1 runs exactly **one incremental .NET CLI source-only audit** joining the 17 packed FlameTable Material* pointers to the 39 authentic, independently OAT-loaded PC GfxWorld Material* identities. It does not create a new native FF.
- src/WaW360.Core/FastFiles/T4AztecFlameRealSourceJoin.cs permits an EXACT material identity only if the original-PC zone SHA matches the 735-surface worklist, the original WeaponDef fixed 0x9AC bytes match **bit-for-bit after actual LE-to-Xbox-BE conversion** at the source 7b physical offset, and its FlameTable's complete 476-byte fixed body matches the source-PC zone after endian conversion/SHA. Only then may an exactly equal packed pointer be matched to one of the 39 typed, named, previously OAT-loaded original-PC Material* sources. Numeric pointer equality across different zones is prohibited. A matched *source material identity* is not yet a fully converted Xenon shader/techset/image graph.
- tools/oat-xenon/Run-AztecXmaRealSourcePreflight.ps1 enumerates/hash-pins genuine local audio media and opaque containers if an existing source directory is specified with -AudioSourceRoot, checks real RIFF XMA/XMA2 format tags and installed XMA encoder executables (without invoking them). The output explicitly rejects silent stubs, synthetic samples and any untested XMA native binding. If no directory is supplied, actual audio media remain UNCENSUSED, not conveniently declared "resolved". Opaque .iwd/.pak sound archives are not counted as validated individual audio samples. The provenance of existing 3 packed FlameTable name strings and 6 packed SndAliasCustom references remains unresolved until real owning source zone and asset data is proven.
- Native emit is a **separate future hard gate**, not a switch on the source-only sprint. Required: genuine remaining names/materials, all true image/technique bindings, actual audio sample/XMA encoding and source-to-Xbox sample references, fully typed WeaponDef/FlameTable child graphs, verified stock residency, complete map/world roots, OAT and hardware gating. No non-NULL source field may be silently replaced by NULL.

PowerShell 5.1 command from repo root (one incremental .NET source pass, one source audio file provenance pass):

    git pull --ff-only
    git rev-parse --short HEAD
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1"
    if ($LASTEXITCODE -ne 0) { throw "Syntax gate failed" }
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Run-AztecRealAssetResolutionSprint.ps1"
    if ($LASTEXITCODE -ne 0) { throw "Real asset source audit blocked; no FF" }

Optional for actual WAV/MP3/XMA source files already on disk:

    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Run-AztecRealAssetResolutionSprint.ps1" -AudioSourceRoot "X:\your\actual\PC\sound\folder"

Do **not** invent a sound folder path. No original-PC media source directory supplied means no codec inventory yet. An xmaencode executable being present is only a capability detection, not encoding or a valid Xbox SndAlias binding. All source-authentic NULLs are *already correctly resolved as NULL* and must not be populated for progress.

The authoritative summary is in output/second_map_baselines/nazi_zombie_aztec_V2.2/native_material_mapping/pc_g11_flame_real_source_sprint/aztec_real_asset_resolution_sprint.json.

**Status:** code committed, not yet run in Windows; source candidates and actual XMA closure counts unknown until the user runs it. No production real-asset completion is claimed.

## 2026-10-31 follow-up: same-original-PC-zone FlameTable join, zero world-material reuse

At commit 08d689c, the user's Windows run verified all **four FlameTable parents** against the exact original PC GfxWorld FF using full WeaponDef fixed-body/parent endian conversion and SHA. The stricter typed pointer comparison found **0/17** packed FlameTable Material references equal to the 39 independently OAT-loaded GfxWorld surface Material identities. All **17/17** therefore remain unresolved; do not bind them by name guesses or zero them out. Continue with the original weapon/FlameTable **other material-family** source corpus, not only the 39 world-surface materials. No native FF changed.

The same run stopped in the XMA inventory script with a Windows PowerShell 5.1 **Argument types do not match** runtime binder exception. This was caused by the old generic-list array expression when serializing a zero-input report. The fix converts both strongly typed List[object] collections using .ToArray(), preserving empty arrays correctly. Re-run the sprint with **-NoBuild** to reuse the already compiled Release CLI and avoid unnecessary .NET rebuild; only PowerShell XMA handling changed. When -AudioSourceRoot is omitted, audio source discovery remains **NOT YET PROVIDED**, not a claimed XMA conversion or source sample closure.

Quick retry from the repo root:

    git pull --ff-only
    git rev-parse --short HEAD
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\\tools\\oat-xenon\\Run-AztecRealAssetResolutionSprint.ps1" -NoBuild

No silent audio, fake materials/images, raw source packed pointers in production, or zeroing non-NULL pointers is permitted. The 122 parent, 12 material, and four sound slots that were **actually source NULL** remain properly NULL.


## 2026-10-31: directly inspect the 17+3+6 packed original PC FlameTable source targets

The user's e9e1390 test proved all four genuine FlameTable parent bodies belong to the identical original PC Aztec FF as the OAT world material corpus, but **none of 17** packed FlameTable Material* references matched the 39 known GfxWorld Material* source identities. XMA preflight succeeded and detected one installed encoder executable but zero actual source media files (no audio directory was supplied). No XMA samples were converted or bound to alias references.

The next original-PC loaded-pointer witness is a one-specialized-OAT-build source-only stage. It uses an extension of the existing pinned OAT x86 loader's known 10-WeaponDef direct pointer tracing:

- The optional WAW360_FLAME_POINTER_WITNESS environment variable reads up to 32 actual bytes from the **allocated original-PC loaded block**, not from an erroneously assumed decompressed-FF physical offset. It also detects truly terminated direct ASCII strings. It neither writes a FastFile nor uses an Xbox retail donor.
- The script tools/oat-xenon/Run-AztecFlamePackedPointerLoaderProbe.ps1 first rechecks the old 4,514,599-byte OAT-good 222-alias FF SHA, the original compressed PC map source SHA and all four genuine FlameTable parent proof flags.
- It visits exactly 17 packed FlameTable material pointer **occurrences**, three packed name references, and six packed sound references; duplicate raw pointer values are grouped and a **single OAT --list loader run** inspects each distinct virtual target.
- All loader results remain source-provenance only. A 32-byte Material target header is NOT a complete typed converted Material body, image/technique dependency or Xbox runtime binding; a packed SoundAlias is NOT an XMA sample. Any missing or ambiguous target remains blocked.
- One OAT x86 specialized build installs capability t4-pc-flame-packed-pointer-virtual-byte-probe-v1. It reuses the existing loader trace instrumentation, changes no production FF and leaves genuine NULL fields untouched.

Run (repo root):

    git pull --ff-only
    git rev-parse --short HEAD
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1"
    if ($LASTEXITCODE -ne 0) { throw "Syntax failure" }
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Run-AztecFlamePackedPointerLoaderProbe.ps1" -BuildDumper

Only use -BuildDumper for the first specialized OAT build; subsequent reruns use the installed binary. The report is pc_g11_flame_loaded_virtual_target_proof/aztec_26_flame_packed_original_pc_virtual_target_probe.json under the native material mapping folder.

Audio remains dependent on actual original PC samples/archives and a verified Xbox-compatible XMA encoder/output/alias binding. A detected encoder executable is not sample conversion or runtime evidence. Preserve natural source NULL parent/material/sound slots (122/12/4) as true NULL. No filler or silent stub audio.

This extension is committed but not yet run or compiled on the user's Windows PC. Source observation and complete native asset production remain distinct gates.


## Next one-build FlameTable typed + audio alias recovery (v2)

The user ran the original source-only packed-pointer witness at 319d391 successfully: all 75 PowerShell 5.1 scripts parsed; the pinned OAT specialized x86 Unlinker was built with Visual Studio 2026; the original 26 non-NULL FlameTable packed reference *occurrences* collapsed to 13 unique raw pointer targets; **all 13/13 targets were loaded**, no trace misses/ambiguities, two distinct direct ASCII source-string candidates, and 11 non-ASCII raw target headers. That was pointer recovery, **not** native asset conversion.

This v2 stage extends the existing one-pass original-PC OAT loader memory witness to interpret those **11 previously untyped target headers**, plus all 13 pointer values consistently:

- It reads a full **112-byte PC Material fixed header** only when bounded to the original loaded OAT block. It checks real material name, texture/constant/state table counts (max 64), technique-set name and texture image names via pointers that are independently proven to land inside the already loaded blocks. A plausible header/name is a PC-loaded source identity *candidate*, not the full material payload, shader or all technique/image children.
- It treats **SndAliasCustom** as the genuine PC **4-byte name-pointer record**; only bounded, NUL-terminated loaded PC strings can be accepted as sound-alias name candidates. An alias record is not a SoundFile, not audio sample data and not valid XMA.
- Names are compared to the original OAT --list output: exact Material XAsset root names and exact Sound XAsset root names are separate source witnesses (if those names are listed). Numeric pointer equality alone is never promoted. If a named original-PC Material is not in the independent OAT XAsset list, it remains a candidate only.
- It counts precisely how many of the *three packed FlameTable name reference occurrences* map to the original two directly readable PC string targets, while the one previously inline 24-byte FlameTable name remains authentic as before. Any mixed-role pointer is fenced.
- New OAT specialized capability identifier is **t4-pc-flame-packed-typed-material-and-alias-v2**; the older installed v1 OAT Unlinker does NOT satisfy this requirement. One -BuildDumper run rebuilds and installs v2. No production fastfiles are ever written by the probe.
- Report: pc_g11_flame_loaded_virtual_target_proof/aztec_26_flame_packed_original_pc_typed_material_alias_probe_v2.json. Original v1 raw-target witness is left untouched.
- The prior 4,514,599-byte 222/228 real alias record OAT-good diagnostic FF remains SHA-pinned and must stay unchanged. The 122/12/4 source-NULL parent/material/sound slots remain exactly NULL.

From the repo root:

    git pull --ff-only
    git rev-parse --short HEAD
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1"
    if ($LASTEXITCODE -ne 0) { throw "PS 5.1 syntax failed" }
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Run-AztecFlamePackedPointerLoaderProbe.ps1" -BuildDumper
    if ($LASTEXITCODE -ne 0) { throw "Original PC typed FlameTable pointer witness blocked" }

IMPORTANT: an XMA encoder executable was detected in a previous Windows test, but no actual original-PC WAV/MP3/sound archive sources were scanned. A genuine Sound XAsset name does not imply an audio sample/XMA asset is present or converted. Next native audio stage requires exact real input audio payload provenance/stream decoding, actual Xbox XMA encoding with roundtrip test, and correct source-owned SoundFile/Xbox SndAlias binding. Dummy/muted samples are disallowed.

The v2 C++ OAT code and PowerShell parser have been pushed but have NOT YET compiled or run on the user's Windows PC. Do not claim v2 sample/Material child names were recovered until its results are verified.


## One-build Xbox-first sound alias path added to FlameTable typed v2

The user's prior no-source-folder XMA preflight detected one encoder executable but 0 real input samples. Do not assume a Steam install folder exists; the previous Steam retail path was explicitly invalid. The correct Xbox-first approach is to identify genuinely owned stock Xbox 360 sound XAsset names and then separately prove whether the relevant zones/runtime sound assets are available. A genuine offline Xbox XAsset name is *never* itself an XMA sample byte provenance or a proven stock-preload contract.

The typed v2 FlameTable loader probe now automatically calls tools/oat-xenon/Run-AztecFlameXboxSoundStockCrosswalk.ps1 after the original-PC 13-target OAT memory test. This extra step is source-only: it checks each authentic original-PC *SndAliasCustom* name against the existing 67 original Xbox retail OAT --list cache. Cache rows must retain the exact retail source path, original file length, UTC modification timestamp, and successful OAT exit code. Genuine asset-owned names and reference-only names are separately recorded. Original retail Sound XAssets in common.ff/code_post_gfx.ff/patch.ff are labeled only as possible stock-base candidates, NOT proven runtime residency.

The 67-zone crosswalk does NOT extract any SoundFile bytes, perform Xbox XMA encoding, copy data from a donor/oracle, or modify the existing 4,514,599-byte OAT-good map. Its optional presence is not a justification for replacing PC references with arbitrarily selected stock sounds. Unknown/unmatched names are fenced. The 122/12/4 authentic source NULLs remain NULL. The v2 report and stock crosswalk report both describe incomplete native/XMA closure.

To repeat after v2 has already installed the new OAT capability, omit -BuildDumper:

    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Run-AztecFlamePackedPointerLoaderProbe.ps1"

Report pair:

- pc_g11_flame_loaded_virtual_target_proof/aztec_26_flame_packed_original_pc_typed_material_alias_probe_v2.json
- pc_g11_flame_loaded_virtual_target_proof/aztec_flame_sound_alias_real_xbox_retail_stock_names.json

Next after this v2 run: for source-proven Material names, recover complete original-PC-owned serialized Material children and verify real Xbox technique/image counterparts; for SoundAlias names, select source-owned or resident stock Xbox SoundFile and actual XMA audio bytes only with byte-level and runtime provenance. No filler/silence allowed. This code is committed but has not yet been Windows run.


## 2026-10-31 v2 Windows runtime correction: Material-only count checks must be role-scoped

The user's actual 27c9635 Windows run confirmed 76/76 PowerShell syntax PASS, pinned OAT x86 v2 rebuild PASS, loaded PC source OAT PASS and 13 distinct original-PC typed target trace rows emitted. Five named SoundAliasCustom source records were directly resolved and independently present in OAT's original-PC Sound XAsset list:
- weap_flamethrower_start
- weap_flamethrower_fireloop
- weap_flamethrower_start_3p
- weap_flamethrower_fireloop_3p
- weap_flamethrower_cooldown_3p

The script then mistakenly marked five of the 13 targets as missing and blocked before the Xbox retail crosswalk. Root cause: the script applied arbitrary offsets 91,92,93 as Material texture/constant/state-bit counts on **every** loaded pointer target, including 4-byte SndAliasCustom and plain original-PC name strings. Those bytes do not have Material semantics for non-Material assets, so the unconditional >64 threshold silently dropped valid rows. These skipped rows included valid source references and were NOT evidence of missing/unloaded memory.

The PowerShell-only correction at 083f771 tests Material count consistency only when a source field is FlameMaterial and its typed 112-byte Material header has a plausible real loaded name; it does NOT invent Material identities for headers that do not parse. For other roles it reports Material count fields as null (not applicable). This correction retains the complete 13/13 typed row accounting and allows the existing real-asset sound-name crosswalk to execute. **The patch has been committed but not yet rerun by the user, so no new names or Xbox sound byte matches are claimed.**

Fast retry from repo root (the OAT v2 Unlinker is already built; do NOT use -BuildDumper):

    git pull --ff-only
    git rev-parse --short HEAD
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Run-AztecFlamePackedPointerLoaderProbe.ps1"
    if ($LASTEXITCODE -ne 0) { throw "FlameTable typed-v2 real-source and stock-name crosswalk blocked" }

This reruns the original PC OAT --list source-only loader (NO MSBuild) with proper role classification, then performs read-only comparison to actual OAT-listed Xbox retail Sound XAssets across up to 67 source zones. Five verified PC alias names are NOT XMA sample/codec proof, and none of the synthetic stub/zero-pointer shortcuts are permitted. All four authentic source-owned FlameTable parent bodies and 122/12/4 source-NULL fields remain unchanged.


## 2026-10-31 real Flame material pointer cell + Xbox XMA one-build sprint

The user's f363a89 test is a full original-PC source audit PASS: 4/4 real FlameTable parent bodies in the same exact source zone, 3/3 packed name references directly recovered, 6/6 original PC SndAliasCustom names directly recovered and independently OAT listed, with all six independently found as owned Sound root names in 67/67 genuine Xbox retail FF OAT listings (0 stale). All 17 packed FlameMaterial pointer occurrences collapse to five unique raw target pointers: 83061811, 83061911, 83769AC1, 83769AC5, and 83769AC9. None passed the direct 112-byte PC Material name test. Three target addresses differ by four bytes, so a live in-block 32-bit Material pointer CELL is an important candidate, not yet proven.

The new OAT x86 v3 native source witness (capability t4-pc-flame-real-material-pointer-slot-hop-v3) follows at most two bounded pointers inside actual loaded original PC OAT blocks and reads genuine Material112/technique/texture/image names. A new WAW360-FLAMEMATPTR line is logged for each source-packed pointer. Actual source byte first-word evidence is independently matched by the v3 PowerShell report against the prior 32-byte memory witness. A real source name is only a candidate until an independent OAT Material XAsset name and its full native Xbox image/shader/technique graph are verified. No arbitrary dereference, source mutation, fake names or material graft is allowed.

Real Xbox XMA opportunities: the pinned Xenon OAT revision already includes LoadedSoundDumperT4Xenon and SoundDumperT4Xenon. They can extract genuine RIFF XMA for actual loaded Xbox sounds, or extract streamed Xbox XMA from loose audio files and indexed Xbox sound archives. tools/oat-xenon/Run-AztecFlameRealXboxXmaExtract.ps1 selects a small number of genuine Xbox retail FFs with actual owned Sound XAsset name evidence for the six flamethrower aliases, runs the pinned native OAT dumper restricted to Sound and LoadedSound, and copies only direct matching named .xma files to SHA-pinned source-evidence staging. It inspects actual RIFF WAVE XMA headers and records unbound loaded sound candidates separately; it does not invent a sample, claim stock preload, link any SoundFile, or modify the good FF. Optional XboxSoundPackRoot can refer to the user's real Xbox archive folder. Zero available samples is a valid blocking finding, not success or an excuse for silent stubs. Retail sample provenance is not authorization to graft unrelated donor/oracle bodies.

Run one sprint from the repository root:

    git pull --ff-only
    git rev-parse --short HEAD
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1"
    if ($LASTEXITCODE -ne 0) { throw "PS5.1 parser failed" }
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Run-AztecFlameMaterialAndXmaSourceSprint.ps1" -BuildDumper
    if ($LASTEXITCODE -ne 0) { throw "Real Flame + XMA original-source sprint blocked" }

Only build once; later runs can omit -BuildDumper. You can cap real Xbox FF scans with -MaxXboxRetailZones 1 or explicitly skip Xbox sample extraction with -SkipXboxXmaExtract. Report files are pc_g11_flame_loaded_virtual_target_proof/aztec_five_original_pc_material_pointer_cells_and_real_xbox_xma_sprint.json and pc_g11_flame_real_xbox_xma_source/aztec_six_real_original_xbox_sound_xma_source_provenance.json under Aztec native material mapping output. The exact original PC FF and 4,514,599-byte previous OAT accepted Xbox test FF are read-only. Real source NULL fields (122 parent, 12 material, 4 sound) remain NULL. Production Xbox material graphs, alias-to-real-sample bindings and runtime validation remain fenced pending independent proof.

The new code has not yet been Windows compiled or runtime tested by the user.


## Original PC Flame v3 real results and Xbox alias-to-XMA v4

The user's f9cb964 run COMPLETED without fatal error: PS51 syntax PASS 78/78, pinned OAT x86 build PASS, all 13/13 original PC loaded FlameTable targets found, all 3/3 packed name references resolved, and all 6/6 source SndAliasCustom sound names matched exactly to the original PC OAT Sound XAsset roots and to owned XAsset Sound names in all 67 valid Xbox retail FF lists. The v3 two-hop material-source test found 0/5 genuine Material112 names across five unique pointer targets covering 17 FlameMaterial field references. Do not assert the pointer-cell hypothesis has been proven; inspect their exact actual source bytes.

The real Xbox sound stage selected ber3.ff and actually exported 1,049 Xbox XMA file candidates, but matching sound/aliasName.aliasIndex.xma files were 0/6. The pinned T4Xenon SoundFile schema explains why simple filename matching is insufficient: SAT_LOADED references a separate LoadedSound whose genuine name determines the OAT output filename. Only SAT_STREAMED has the aliasName.index.xma relationship.

New v4 native Xbox SoundFile source witness: one pinned OAT Win32 rebuild with graph-dumper/XboxSoundAliasLoadedSampleTrace.snippet.cpp in the real T4Xenon SoundDumper. Controlled by WAW360_XBOX_FLAME_SOUND_CHAINS and scoped to the six already source-verified flamethrower aliases, it records actual loaded Sound alias-list root, per-record alias index and name, SoundFile exists flag, SAT_LOADED referenced LoadedSound.name/packet size, or SAT_STREAMED directory/name, without copying data into a production FF. OAT's original source checkout is restored clean after compilation. New capability t4xenon-real-flame-sound-to-loaded-xma-chain-v4.

Run-AztecFlameXboxLoadedSampleChain.ps1 verifies the original 67-zone cache, prior v3 actual ber3.ff SHA, six PC and Xbox owned aliases and new v4 OAT capability; it runs the one actual Sound/LoadedSound dump, joins the real alias->SoundFile->LoadedSound name or streamed alias index to its native sample, SHA-pins only matching genuine .xma files and records RIFF XMA codec header validation separately. Unbound 1,049 other OAT sound dumps are NEVER used as donors. The Xbox stock residency and final native SoundFile/FlameTable binding remain unverified.

Run-AztecFlameFiveMaterialCellEvidence.ps1 independently prints actual original-PC five 32-byte header samples as eight little-endian 32-bit words each, grouped by all 17 original material reference occurrences. None are typed into a native Material on mere numeric similarity. It reuses last successful v3 proof; no extra OAT build is needed for material evidence.

One local Windows sprint:

    git pull --ff-only
    git rev-parse --short HEAD
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1"
    if ($LASTEXITCODE -ne 0) { throw "PS5.1 syntax failed" }
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Run-AztecFlameV4MaterialAndRealXboxSoundChains.ps1" -BuildDumper
    if ($LASTEXITCODE -ne 0) { throw "Authentic original PC Flame and Xbox XMA sound sample-chain scan blocked" }

Output is in the Aztec native material mapping folder: pc_g11_flame_loaded_virtual_target_proof/aztec_five_original_pc_real_material_memory_words.json, pc_g11_flame_real_xbox_xma_source/aztec_six_real_flame_xbox_sound_to_xma_chain_v4.json, and pc_g11_flame_loaded_virtual_target_proof/aztec_flame_v4_original_pc_real_material_memory_and_xbox_real_sound_source_sprint.json.

Committed but NOT YET built or executed by the user. No new production fastfiles, stock runtime preload claim, synthetic Material/technique/image asset or silent XMA sample; 122/12/4 original NULLs and the old good native FF remain untouched.


## 2026-10-31 actual original Xbox Flame sound bytes 6/6 (v4 PASS) + next typed-PC Material proof

The user's v4 result at commit 94f06a3 was fully successful on their Windows 10 PS5.1 / VS2026 environment. 81/81 PS51 scripts parsed, pinned Xenon OAT Win32 built without errors, original source FF hashes checked and the prior accepted native diagnostic FF remained intact. The actual Xbox SoundFile -> LoadedSound source ownership witness recovered **all 6/6** PC-verifiable flamethrower sound aliases from original retail ber3.ff. All six have an actual linked RIFF-XMA sample with header validation, SHA256 identity and staged sample bytes:
- weap_flamethrower_start -> sound/sfx/weapon/special/flame_thrower/wpn_flm_ignite.xma (20,608 bytes)
- weap_flamethrower_fireloop -> sound/sfx/weapon/special/flame_thrower/wpn_flmthwr_st_flame.xma (45,232 bytes)
- weap_flamethrower_cooldown -> sound/sfx/weapon/special/flame_thrower/wpn_flm_cooldown.xma (16,504 bytes)
- weap_flamethrower_start_3p -> sound/sfx/weapon/special/flame_thrower/wpn_flm_ignite_mn.xma (10,348 bytes)
- weap_flamethrower_fireloop_3p -> sound/sfx/weapon/special/flame_thrower/wpn_flmthwr_mn_flame.xma (22,660 bytes)
- weap_flamethrower_cooldown_3p -> sound/sfx/weapon/special/flame_thrower/wpn_flm_cooldown_mn.xma (8,296 bytes)

Status GENUiNE_XBOX_SOUND_TO_REAL_XMA_SAMPLE_NAME_AND_BYTES_LINK_PROVEN_OFFLINE means actual original Xbox source audio and alias-file provenance are now **complete**, NOT that the current native map package or CodXe runtime contains or plays those sounds. Any native sound package/import still needs exact real SoundFile metadata/root ordering and Xbox runtime evidence.

The v4 original-PC memory ledger found 5 unique packed Material destination cells for the 17 non-NULL FlameTable Material field occurrences:
- 0x83061811: 0x07B6D450 (4 fields)
- 0x83061911: 0x07B6CFA0 (4 fields)
- 0x83769AC1: 0x07BE5830 (3 fields)
- 0x83769AC5: 0x07BE6370 (3 fields)
- 0x83769AC9: 0x07BE5C68 (3 fields)

These first-word values look like host x86 addresses in the original OAT process; they are NOT valid serialized FF offset or Material names by themselves. The earlier 0/5 direct Material header and 0/5 two-hop source probe remain valid, and no real material name has yet been proven by those methods.

**Better next source proof:** pinned OAT already has a first-party original-PC T4 WeaponDumperT4::DumpFlameTables, which uses a typed loaded WeaponDef->FlameTable and standard InfoStringFromStructConverter::CSPFT_MATERIAL to export actual MaterialInfo.name without guessing serialized/host pointer decoding. The new zero-build tools/oat-xenon/Run-AztecFlamePcTypedWeaponMaterialAudit.ps1 reuses the previously built pinned Unlinker, SHA-pins the exact original-PC FF, executes OAT --include-assets weapon in an isolated temporary dump directory (not the user's game), parses only genuine FlameTable prefix source files, records the eight named material slots and authentic NULL fields per parent, SHA-stages only actual typed FlameTable exports, compares any names against the original PC OAT material root list and refuses to label any name as a working Xbox material shader/image/techset graph. Counts differing from 4 parents or 17 packed occurrences are reported as uncertainty; no filler is emitted. This is read-only evidence, not an original PC asset donor into production.

From repo root, no C++ rebuild:

    git pull --ff-only
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1"
    if ($LASTEXITCODE -ne 0) { throw "PS5.1 syntax blocked" }
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Run-AztecFlamePcTypedWeaponMaterialAudit.ps1"
    if ($LASTEXITCODE -ne 0) { throw "Original PC typed Material source audit blocked" }

The typed material name audit code has been pushed but NOT yet Windows-executed. Its output is under pc_g11_flame_loaded_virtual_target_proof/aztec_four_pc_flame_tables_typed_weapon_material_names.json. Do not advance material graft or Xbox deployment until names match the actual loaded source and a real native image/technique graph can be provided. Original source NULLs (122 parent / 12 material / 4 sound) remain unchanged.
