# PC2XB3 — recipe implementation contract (2026-10-30)

**Single technical reference:** user-supplied `WaW_PC_to_Xbox360_codxe_porting_knowledge.txt`, documenting the WaW360Tool's Xbox 360 + CodXe rules. Follow detailed Part 2 when it differs from Part 1. This checklist tracks PC2XB3, not WaW360Tool: the source's `[AUTO]` tags do not signify a working PC2XB3 feature.

## Readiness tracker Windows syntax preflight (October 30)

First Windows run after commit `3bfc08f` reported PS5.1 syntax **FAIL** in `Run-WaWMultiMapNativeReadiness.ps1` at lines 188–195. The four static `New-Gate` calls had newline-separated positional strings, which PowerShell parsed as terminated commands. The syntax preflight stopped the attempted workflow before readiness reporting or OAT extraction. **No new FFs or Xbox files were affected.** Hotfix `9fe75f8` puts those command arguments on single logical lines; **Windows syntax and runtime recheck are still pending**. Do not count this as an additional readiness gate.

## October 30 — OAT full extraction isolated to the same ten packed weapon labels

Windows run after `388e8a7` **PASS** on all 7 PowerShell syntax scripts and readiness tracker runtime. Existing Aztec Candidate: **3/10 verified evidence gates**, 74 IWD source entries, 6 exact IWD-owned RawFile overrides (readback 0, FF 449,870 B), and script-closure OAT `--list` 0 warnings/errors. OAT **full extraction of that same SHA** failed with ten `Failed to open file 'weapons/<packed-name@...>'` messages even though OAT exit code was 0. The differential checker correctly labeled `WEAPON_OUTPUT_PATH_ERROR`. This proves **the script-closure exporter is also blocked on WeaponDef diagnostic placeholder filenames**, not that the script payloads were invalid. No release authorized.

**Fix to readiness reporting:** a same-SHA failed full OAT extraction now marks `SCRIPT_EXTRACT=BLOCKED` (with exact error category), not `PENDING`. All 10 readiness gates remain fail-closed; 3/10 remains the correct evidenced tally.

**Next isolated exporter A/B experiment (new code, Windows compile and OAT test PENDING):**

- Enable `WAW360_DIAGNOSTIC_WEAPON_ORDER_PROBE=1` **only for a one-map Candidate run**. By default this feature does not run, to avoid wasting OAT PC listing time on every build.
- From each ORIGINAL PC source FF, native OAT `--list` supplies real top-level `weapon, name` identities. Recheck full count, unique names, **every known WeaponDef proof name** in the original per-type XAsset order, and at least 3 known-name anchors PER source. Any disagreement suppresses the probe.
- Only AFTER the six real IWD script overrides have produced a verified script-closure diagnostic FF, create an ADDITIONAL `hardware_test_main_xanim_native_layout_weapon_order_probe/<map>.ff` with the ten **ordinal-only** names substituted in **inert WeaponDef shells**. The existing primary/tracked-shell/script-closure FFs remain unchanged. No donor/model/audio/oracle payload bytes are inserted.
- The probe directory includes `DO_NOT_DEPLOY_WEAPON_ORDER_PROBE.txt`. Its ordinal substitution is **not a proven target of the original packed pointer**, not a real WeaponDef serialization, and does **not** raise `WEAPON_NAMES`, `SCRIPT_EXTRACT`, `MAIN_EXTRACT`, or any native readiness gate. The readiness JSON reports probe FF size/OAT outcome as a separate non-gated experiment.
- Purpose: run independent OAT **full extraction** on this newly emitted probe. If exporter failures move beyond `weapons/<packed-name@...>`, record the next structural blocker without pretending the map has become playable.

Use the following single-map focused sequence. **Windows compilation is required before running Candidate; stop if build fails.**

```powershell
git pull --ff-only
if ($LASTEXITCODE -ne 0) { throw 'Git pull failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PS syntax preflight failed' }
dotnet build '.\src\WaW360.Cli\WaW360.Cli.csproj' -c Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) { throw 'Weapon name probe .NET compilation failed' }
$env:WAW360_DIAGNOSTIC_WEAPON_ORDER_PROBE='1'
try {
    powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapMatrix.ps1' -Mode Candidate -OatVariants Primary -IncludeMaps nazi_zombie_aztec_V2.2
    if ($LASTEXITCODE -ne 0) { throw 'Candidate diagnostic failed' }
} finally { Remove-Item Env:\WAW360_DIAGNOSTIC_WEAPON_ORDER_PROBE -ErrorAction SilentlyContinue }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapOatDifferential.ps1' -OatMode Extract -IncludeMaps nazi_zombie_aztec_V2.2 -VariantNames hardware_test_main_xanim_native_layout_weapon_order_probe
```

**Expected outcome is unknown until the Windows run.** No default rollout; if all ten real names align but the proof still has synthetic pointers, treat OAT full-extraction movement as diagnostic progress only. A separate **pointer-direct** name resolver or exact T4 XAsset source-index validation remains required for production.

## Live native Xbox test-readiness progression (implemented)

`Run-WaWMultiMapNativeReadiness.ps1` reads an existing Candidate matrix and its SHA-matched OAT differential reports. It records **10 discrete evidence gates** per map (not an estimate of how much conversion is complete), uses a durable `==========`-style console bar, prints a reason for every PASS / PENDING / BLOCKED, shows movement since the previous evidence check, and persists JSON and per-map history. The Candidate runner now invokes this automatically at the end of each run; OAT differential invokes it whenever new validation evidence is saved. It **never builds or deploys FFs, nor declares native hardware test authorization**.

The ten gates are: recipe source-precedence indexed; real IWD script payload overrides with FF readback; script-closure OAT `--list`; script-closure full OAT extraction; primary FF full OAT extraction; genuine WeaponDef names resolved; real typed asset graph (renderer/FX/audio/collision) without placeholders; reachable gameplay/script/world dependency closure; Xbox RAM/asset-pool admission; complete native CodXe package with all required files and sound/texture dependencies. OAT `--list` is deliberately distinct from OAT full extraction.

For Aztec's last Windows evidence, the expected **initial** tally is approximately **3 of 10 independently evidenced gates** (source index, 6 script overrides/readback, script-closure OAT listing), and full primary extraction and the 10 packed WeaponDef identities remain blocked. This is an anticipated outcome to verify on Windows, not a claim that the new tracker has been tested.

Run the tracker without recompiling or invoking OAT:

```powershell
git pull --ff-only
if ($LASTEXITCODE -ne 0) { throw 'Git pull failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PowerShell syntax check failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapNativeReadiness.ps1' -IncludeMaps nazi_zombie_aztec_V2.2
```

To advance the **next independently measurable gate**, run OAT **full extraction of the already-generated script-closure FF** (not the primary FF) on the same Candidate matrix. This does not rebuild an FF or copy anything to Xbox. It can reveal downstream serialization errors even when OAT listing is clean:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapOatDifferential.ps1' -OatMode Extract -IncludeMaps nazi_zombie_aztec_V2.2 -VariantNames hardware_test_main_xanim_native_layout_script_closure
```

The differential tool saves exact FF SHA, OAT exit code, and full extractor warnings/errors. Its automatic readiness refresh will mark the script-closure extraction gate PASS only when the matching FF digest is unchanged and OAT returns a clean full extract; a new failure instead gives us the next technical blocker. **Neither outcome authorizes an Xbox test**; the primary asset graph and other safety gates remain open.

## October 30 — Windows-confirmed WeaponDef ordinal A/B full extraction PASS

User ran the opt-in `WAW360_DIAGNOSTIC_WEAPON_ORDER_PROBE=1` one-map Candidate on commit `2280d44`. Engine compiled, `linker-v2-main-structural` **PASS**, IWD RawFile overrides **6** and internal readback 0/wrapper round-trip true. Probe reported `ready=True`, **61 known exact-name anchors** and **10 UNVERIFIED ordinal name substitutions**. Emitted separate *nondeployable* `hardware_test_main_xanim_native_layout_weapon_order_probe/nazi_zombie_aztec.ff` **449,743 bytes**. Original 449,870-byte script-closure FF, 449,763-byte tracked shell and 6,205,444-byte primary remain byte-identical to earlier builds.

Independent native OAT **full extraction** of the 449,743-byte order probe: **`OAT_EXTRACT_CLEAN_DIAGNOSTIC`, `exit=0`, `Finished with 0 warnings, 0 errors`**. Previous full extraction of the untouched script-closure FF failed on precisely ten `<packed-name@...>` exporter filenames. Therefore the A/B probe isolates those illegal filesystem names as the immediate exporter blocker in the **inert diagnostic-shell variant**. **It does not independently decode original virtual-block name pointer targets, convert live WeaponDef payloads, prove game load, or authorize Xbox testing.**

Latest Candidate readiness tally **2/10 same-run evidence gates** (SOURCE and SCRIPT) because the new matrix had *no same-run `--list` run on the unchanged script-closure FF*. The earlier run independently verified its clean listing with the same FF SHA. This is an evidence-tracking scope change, not a source/FF regression or lost conversion progress. To restore its currently scoped SCRIPT_LIST gate, run a **no-rebuild** `Run-WaWMultiMapOatDifferential.ps1 -OatMode List -IncludeMaps nazi_zombie_aztec_V2.2 -VariantNames hardware_test_main_xanim_native_layout_script_closure`. The 10-name diagnostic never credits the production WEAPON_NAMES or extraction gates.

**Next actual conversion work:** implement source-pool/direct virtual-block pointer-to-string provenance, retain actual non-inert WeaponDef data with correct Xbox child ownership/relocations, and validate primary/full OAT extraction. Keep ordinal names diagnostic only.

## October 30 — Back to the real converter: non-ordinal WeaponDef scalar fingerprint groundwork

**New code, Windows validation pending:** `tools/oat-xenon/Run-WaWMultiMapWeaponScalarWitness.ps1` reads the existing **original-PC-derived Stage 4 alpha7b owned WeaponDef proof files** for the newest selected Candidate matrix. It does not rebuild FFs or run OAT. From each weapon's independently verified big-endian `0x9AC` fixed body, compute a SHA-256 fingerprint from **59 fixed scalar-field locations** already constrained by `T4WeaponSerializerProof` (weapon type/class and other enums, numerous validated booleans and count fields). No `szInternalName`, file position, root ordinal, packed pointer, or source name is included in the fingerprint.

For each unresolved `<packed-name@...>` proof body, the witness reports `uniqueAgainstAllPhysicalProofBodies`, exact `scalarFingerprintSha256`, and any colliding proof labels, alongside the 71-body source corpus and map/mod proof boundaries. The JSON is written under `output/multi_map_matrix/runs/<current>/weapon_scalar_witness_*.json`. Crucially **this is a distinctness/feasibility test only**: no new names are resolved from unique fingerprints alone and all Xbox readiness gates remain unchanged. The next step is collecting the same scalar values from independently loaded **original PC OAT `WeaponDef*`** objects (e.g. with our pinned native graph-dumper pattern), then matching and requiring unique signatures plus known-name/struct-offset controls. Direct packed VIRTUAL pointer target proof is still a separate stronger requirement.

Run this without recompiling or invoking OAT:

```powershell
git pull --ff-only
if ($LASTEXITCODE -ne 0) { throw 'Git pull failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PowerShell syntax preflight failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapWeaponScalarWitness.ps1' -IncludeMaps nazi_zombie_aztec_V2.2
```

**Expected 10 synthetic-name proof bodies, but uniqueness is unknown until Windows output.** No auto-promotion of any ordinal candidate, no FF name replacement, and no Xbox test.

## October 30 — Windows-confirmed lossless beta-layout STAGE PASS (44/44 SHA-256)

User explicitly ran `Stage-CodXeT4BetaPackage.ps1 -Stage` against the existing Aztec native reference package after a successful dry-run. All **44/44 source files** were copied into a **new, unique** local flattened staging tree under `output/codxe_beta_stage/nazi_zombie_aztec_20261030_160821_79b25d10/_codxe/usermaps/nazi_zombie_aztec/`. The script independently checked exact file sizes and **SHA-256 digests** after each copy; final counters **120,068,964 / 120,068,964 bytes**, `Status: SHA_VERIFIED_REFERENCE_STAGE_NOT_RELEASE`. It wrote a distinct JSON report `output/codxe_beta_stage/reports/nazi_zombie_aztec_20261030_160821_79b25d10.json`, and verified no original map data was modified. The console progress advanced from `[=---------] 5/44` to `[==========] 44/44`.

**Result:** separate reference-package staging with file preservation and integrity checks is now **Windows-verified**, including all 37 `highmip/` files. This is the success criterion for **packaging-tool staging**, not a native game-conversion or hardware readiness gate. Keep existing map source, old layouts and optional asset files intact. DO NOT DEPLOY this reference-stage FF or any diagnostic FF as a generated Xbox release. Only an actual CodXe beta installed-map sample/config can verify the predicted flat install directory, metadata requirements, and compatibility.

Xbox-native Aztec conversion remains **3/10 independent readiness gates**, with 10 unresolved WeaponDef packed-name source-pointer targets, real typed asset ownership and complete primary OAT extraction still pending. Next technical work should target authenticated pointer-to-name provenance (not additional legacy-map copying).

## October 30 — Windows-confirmed beta staging DRY RUN PASS (9/9 WinPS syntax)

User ran `Stage-CodXeT4BetaPackage.ps1` without `-Stage` against the existing native Aztec reference folder. PowerShell 5.1 parser preflight **9/9 PASS**, including the staging tool. Staging dry run reported `map=nazi_zombie_aztec`, **44 files**, **120,068,964 bytes**, expected relative target `_codxe\usermaps\nazi_zombie_aztec`, `Status: DRY_RUN_ONLY`, and a JSON report in `output/codxe_beta_stage/reports/`. **No map data was copied or modified.** The real `-Stage` operation and post-copy SHA verification have not yet been Windows-tested.

This is a **packaging-tool milestone**, not a release gate: Aztec still has **3/10 independently evidenced native Xbox test gates**, and the beta's actual install behavior is unverified. Next optional test: explicitly add `-Stage` to create a separate, unique output-only copy of the 44-file reference package and verify each copied SHA-256. Retain `REFERENCE ONLY / DO NOT DEPLOY` labeling and do not upload the copy to Xbox.

## October 30 — Windows-confirmed 44-file Aztec corpus, lossless beta staging path

Latest Windows run of `Test-CodXeT4BetaLayout.ps1` after `ce41b31`: **PowerShell 5.1 AST 8/8 PASS**, Aztec reference `nazi_zombie_aztec` is **44 files / 120,068,964 bytes**, including **37 `highmip/` files** and **0 loose sound files**. Main FF 80,309,061 bytes (76.589 MiB), load and patch FFs present, `preview.dds` present, `map.json` absent. The auditor again found no installed `usermaps` root under the selected *reference collection*, so this is a source-package inventory, not beta-install verification. Current Aztec Xbox readiness remains 3/10 verified evidence gates.

**New non-installing beta-layout staging tool (code pushed, Windows runtime PENDING):** `tools/oat-xenon/Stage-CodXeT4BetaPackage.ps1` accepts a map-shaped reference folder containing `<map>.ff`. It defaults to **dry run**. Opt-in `-Stage` writes a unique, non-overwriting **separate copy** under `output/codxe_beta_stage/<map>_<run-id>/_codxe/usermaps/<map>/`, retaining every source path and file (including `highmip`, `_patch.ff`, DDS and unknown extras). Each staged file gets exact source/destination length and SHA-256 comparison, with a console progress bar and machine-readable JSON manifest. Symlinks/junctions, ambiguous or missing main FF, path escape, hash mismatch and overwrite are blocked. A staging guard outside the map folder labels the copy `REFERENCE ONLY NOT XBOX RELEASE`. Source files and old installs are never modified. This does NOT repair or re-encode any FF, convert missing assets, certify the beta format, or authorize Xbox deployment.

Run its dry run before testing staging:

```powershell
git pull --ff-only
if ($LASTEXITCODE -ne 0) { throw 'Git pull failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PowerShell syntax preflight failed' }
$aztec='C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\codxe latest (working map references)\nazi_zombie_aztec'
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Stage-CodXeT4BetaPackage.ps1' -SourceMapDirectory $aztec
```

When the user explicitly wants a local staged copy, rerun the same command with `-Stage` (no overwrite or Xbox install). The first real beta-installed `_codxe` tree still needs independent validation against this predicted structure, and `map.json` remains optional in PUBLIC CodXe UI only, not independently confirmed optional in the beta.

## October 30 — Aztec 44-file reference package: WinPS inventory PASS; CodXe beta profile hardened

**Windows-verified:** commit `c3d4108` `Test-CodXeT4BetaLayout.ps1` passed AST preflight **8/8** and discovered exactly one map-shaped reference matching `-MapName nazi_zombie_aztec` under the user's `codxe latest (working map references)` folder. Inventory:

- `nazi_zombie_aztec.ff` **80,309,061 bytes** (76.589 MiB), `nazi_zombie_aztec_load.ff` present, `nazi_zombie_aztec_patch.ff` present;
- `preview.dds` **present**, `map.json` **absent**, `highmip/` **present**;
- **44 total files** in the reference directory. The folder does not include a detectable `usermaps` install root, so its compatibility with an unpublished CodXe beta is NOT verified.

**Public CodXe loader evidence, not beta claims:** `src/game/t4/sp/components/ui.cpp` `ScanUsermaps()` looks for `<root>/usermaps/<name>/<name>.ff` to advertise a map and falls back to directory name if `map.json` metadata is absent; `preview.dds` is optional (default preview). `src/game/t4/sp/components/fastfiles.cpp` strips `_load`/`_patch` to locate the same named map folder, and redirects loose sound opens under `sounds/`. The public sources do not establish that a beta *requires* `_patch.ff`, `highmip/`, or `map.json`.

**Implementation update, Windows validation pending:** the read-only `Test-CodXeT4BetaLayout.ps1` now emits `allFilesRelativePathAndBytes`, extension histogram, `highmipFileCount`, loose sound count, and a public menu metadata-fallback annotation for each map-shaped reference package. The Linker V2 SourceArchive package plan explicitly lists `map.json`/`preview.dds` as **recommended in public CodXe**, with future beta behavior unverified, and preserves `_patch.ff`/`highmip/` as conditional native assets without copying donor/oracle data into production. There is no migration, no auto-copy, no Xbox release authorization.

Run a no-build regression:

```powershell
git pull --ff-only
if ($LASTEXITCODE -ne 0) { throw 'Git pull failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PowerShell syntax preflight failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-CodXeT4BetaLayout.ps1' -Root 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\codxe latest (working map references)' -MapName nazi_zombie_aztec
```

**Next acceptance input:** actual beta-installed `_codxe` tree or beta source `Config::data_root`, `ScanUsermaps()` and `GetUsermapFastfilePath()` behavior. Without this, a reference package proves contents only, not install compatibility. Native Xbox readiness stays 3/10 verified evidence gates.

## October 30 — Windows verified beta-reference FF discovery + same-run 3/10 restoration

User ran commit `9c48f39` with PS 5.1 AST **8/8 PASS**, including `Test-CodXeT4BetaLayout.ps1`. Scanned 50 directories in the selected `codxe latest (working map references)` folder. No installed `usermaps/` tree was present, but the script found many **direct `<map>/<map>.ff` reference packages**, including:

- `nazi_zombie_aztec/nazi_zombie_aztec.ff` **80,309,061 bytes**, `_load.ff` **364,367 bytes**, `_patch.ff` **5,964,034 bytes**;
- `backlot_zm/backlot_zm.ff` **112,343,793 bytes** with matching `_load.ff`/`_patch.ff`;
- `bcast`, `cargoship`, `kinodertoten`, `mario`, etc. each had native-sized reference FFs.

**Interpretation:** this folder is a useful native-map **reference collection**, not by itself evidence of how the new CodXe beta installs maps. The 80.3 MB Aztec native reference is NOT interchangeable with the 449,743-byte inert WeaponDef A/B diagnostic; size alone is not proof of matching source versions or production fidelity. Do not use native reference payload bytes as production donors.

The inspector now inventories any map-shaped directory whose actual basename `.ff` exists, even if its parent is not `usermaps`. It reports source main/load/patch FF presence and sizes; `map.json`, `preview.dds`; extra sibling directories and all file counts; it explicitly marks the results as **REFERENCE_COLLECTION_OR_UNVERIFIED_INSTALL**, never a verified beta installation or an Xbox-ready package.

The same Windows test ran `Run-WaWMultiMapOatDifferential.ps1 -OatMode List` on the current script-closure FF: SHA-matched **OAT_LIST_CLEAN_DIAGNOSTIC**, **0 warnings, 0 errors**, and **readiness rose 2/10 → 3/10** for the current Candidate matrix. Source index 74 entries and six real RawFile script overrides remain intact. OAT full-extract and production native graph remain unresolved; 3/10 is a gate count, not 30% gameplay completion.

To inspect just the now-detected Aztec reference package with the next code revision, without touching any FF:

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PowerShell syntax preflight failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-CodXeT4BetaLayout.ps1' -Root 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\codxe latest (working map references)' -MapName nazi_zombie_aztec
```

Beta migration/export rules still require actual beta source or an installed beta map as proof. Never automatically relocate/delete old packages solely from reference-map folder names.

## CodXe upcoming beta — flat shared T4 usermap layout (October 30)

Michael's beta changelog announces: **'New map layout, nested t4 dir gone (sorry)'** and reports that prior converted map packages need layout updates. This is a packaging/installation compatibility requirement *separate from* the Xbox FF binary conversion. Do not assume old converted `.ff` bytes need re-encoding solely due to the directory change.

**Expected migration in the game directory:**

```text
OLD: <WaW game>/_codxe/t4/usermaps/<map>/<map>.ff
NEW: <WaW game>/_codxe/usermaps/<map>/<map>.ff
```

The public CodXe source `src/common/config.cpp` currently chooses its `data_root` from shared `_codxe` or a legacy nested `t4` root; `src/game/t4/sp/components/fastfiles.cpp` constructs `<root>/usermaps/<map>/<zone>.ff` and `<root>/usermaps/<map>/sounds/...`. The upcoming **beta** contract may remove old path handling, and may also introduce extra packaging requirements. These two source files explain the old/new root difference but are **not** evidence of all unpublished beta features. Public references:

- https://github.com/michaeloliverx/codxe/blob/main/src/common/config.cpp
- https://github.com/michaeloliverx/codxe/blob/main/src/game/t4/sp/components/fastfiles.cpp
- https://github.com/michaeloliverx/codxe/blob/main/src/game/t4/sp/components/ui.cpp

**First beta inventory test:** user's selected `codxe latest (working map references)` folder contained no recognized installed `usermaps/<map>` subdirectories at the original shallow roots. This is **not** evidence the beta layout is invalid. A later script revision performs bounded recursive discovery (depth ≤6, ≤2,500 directories) and prints up to 25 `*.ff` path hints to help locate the actual install/map reference files; Windows runtime recheck is pending. The PowerShell syntax preflight now includes this new inspector.

PC2XB3 SourceArchive now explicitly plans the expected flat `_codxe/usermaps/<map>/` installation root without writing files there. `tools/oat-xenon/Test-CodXeT4BetaLayout.ps1 -Root <game-folder-or-beta-reference-folder>` is an independent **read-only** inventory: detects flat vs nested roots, enumerates actual files, checks name-based FF and common menu metadata presence, and creates JSON under the converter's `output/codxe_beta_layout_audit/`. The tool never moves existing maps or approves release.

**Beta acceptance gate:** compare the user's available **actual beta reference map** file tree and beta build version with the expected flat output before implementing a migration/exporter. Confirm whether `map.arena`, `_patch.ff`, `.pak`, `_load.bik`, `highmip/`, streamed `sounds/`, and metadata are required/optional/preserved, rather than inventing a mandatory set. Never silently delete existing highmips, sound streams, patch FFs, videos, or additional metadata.

**First beta inventory command (source folder is an example; no Xbox write):**

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-CodXeT4BetaLayout.ps1' -Root 'C:\Users\jorda\OneDrive\Desktop\My Xbox\World at War\codxe latest (working map references)'
```

**Status: packaging profile and read-only inventory added to repo; actual beta compatibility not yet verified.** Full Xbox readiness remains **3/10** evidence gates for Aztec pending real assets, complete OAT extract, RAM/pool checks and final packaging.

## Required gates

| Step | Requirement | PC2XB3 status |
|---|---|---|
| 0 | Cache Xbox/PC stock/game/campaign/mod asset names and types | PARTIAL: existing PC inputs and retail file-location index; full stock XAsset inventory pending |
| 1 | Parse map FF, patch, mod and IWDs. Honor map-owned and later-alphabetical IWD loose-script priority | PARTIAL: **new** IWD virtual-path and collision index; **new** diagnostic RawFile override path for exact GSC/CSC names; Windows test pending |
| 2 | Follow actual running scripts, entities, weapons and world dependencies | OPEN: incomplete runtime reachability and typed closure |
| 3 | First map-owned assets; proved native Xbox stock; PC stock; campaign; extra assets; other PC maps only filling gaps | OPEN: not enforced for every asset family |
| 4 | Truly convert Xenos textures, material/shaders, XModels, XAnims, weapons, sound/XMA, FX, world/collision, rawfiles | OPEN: partial serializers and diagnostic shells do not qualify |
| 5 | Apply console script compatibility fixes and complete animtree closure | OPEN |
| 6 | Check actual Xbox memory and asset pools; gently reduce less-important assets until fitting | OPEN |
| 7 | Emit native map FF + REQUIRED load FF + CodXe map.json/preview.dds + external SDNS/XMA streams when needed; verify references | OPEN |
| 8 | Report every source, substitution, loss and likely load verdict; test real console | OPEN |

## Source-backed invariants

- An IWD is only a PC source archive, never an Xbox runtime file. Same-path loose GSC/CSC from an IWD overrides an FF rawfile; the alphabetically later map-owned IWD wins among loose IWDs.
- Only exact names in Xbox game-resident `common.ff`, `code_post_gfx.ff`, `patch.ff` may be referenced as already loaded. UI zones do not remain loaded during gameplay.
- Other console ports can provide **only shader/techset templates**; not arbitrary images, models, sounds, weapons, effects, or scripts.
- `<packed-name@...>` is a forensic label for unresolved weapon identity, not a valid WeaponDef.
- The document's standard CodXe package is `<map>.ff`, `<map>_load.ff`, `map.json`, `preview.dds`, plus `sounds/**/*.xma` when streamed sound is required. Do not infer `.pak`, `.bik` or `highmip/*.hi` as mandatory from this document.
- FF size increases, OAT listing, successful full extraction, independent readback, and Xbox hardware gameplay are separate milestones. Diagnostic FFs must not be deployed as production output.
- PC source FFs and IWDs are read-only.

## Windows verification — first recipe Source PASS (October 30)

The user executed commit `3c476a8` with `-Mode Source -IncludeMaps nazi_zombie_FRUmansion,nazi_zombie_aztec_V2.2`: **both maps passed** `linker-v2-source`; the .NET shared engine compiled and emitted no Xbox FFs in Source mode, as expected.

Aztec IWD source report: `IWDs=1; entries=74; unique loose scripts=3; script collisions=0; errors=0`. FRUmansion also passed, but the pasted log truncated its detailed counts. These are SOURCE evidence only, not confirmation that script bytes were overridden in a candidate Xbox FF.

Subsequent development adjustments (Windows untested): Source mode calls `linker-v2-source` directly because it already runs `NormalMapAnalyzer` and writes the same analyzer reports; the former redundant `stage4-normal-analyze` process is removed. The recipe index now limits script winners to `.gsc`/`.csc` (not `.atr`, `.cfg`, `.txt`). The RawFile builder reads the last duplicate path within a winning ZIP, matching the source-index tie-breaker. Candidate logs surface `ScriptClosureIwdScriptsOverridden` and `scriptClosureReady` directly.

**Next targeted Aztec Candidate diagnostic, NOT a release:**

```powershell
git pull --ff-only
if ($LASTEXITCODE -ne 0) { throw 'Git pull failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PowerShell syntax preflight failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapMatrix.ps1' -Mode Candidate -OatVariants Primary -IncludeMaps nazi_zombie_aztec_V2.2
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapOatDifferential.ps1' -OatMode List -IncludeMaps nazi_zombie_aztec_V2.2 -VariantNames hardware_test_main_xanim_native_layout_script_closure
```

Check the matrix for `[MULTIMAP-RECIPE] ... scriptClosureReady=...`, `RECIPE IWD script-closure overrides=N`, emitted FF path/size, and OAT `--list` summary. A script-closure FF may be absent if typed closure cannot build or the index finds no applicable overrides; that is evidence to debug, not a PASS. Full OAT extraction and Xbox hardware testing are separate gates.

## October 30 — first real Windows order witness PASS (map + mod)

User ran the new `Run-WaWMultiMapWeaponNameOrderWitness.ps1` on saved original PC OAT/proof evidence. PowerShell 5.1 syntax preflight **6/6 PASS**. Full source-order witnesses:

- `map.ff`: `ALL_KNOWN_NAMES_MATCH_ORDER_ONLY`, **61/61** source OAT/typed proof counts, **55/55** known-name anchors agreeing, **0** ordering conflicts. Six *UNVERIFIED* positional mappings: ordinal 24 `molotov`; 28 `m7_launcher_zombie`; 29 `m7_launcher_zombie_upgraded`; 48 `ray_gun`; 55 `kar98k`; 56 `30cal`.
- `mod.ff`: `ALL_KNOWN_NAMES_MATCH_ORDER_ONLY`, **10/10** counts, **6/6** known-name anchors, **0** conflicts. Four *UNVERIFIED* positional mappings: ordinal 3 `zombie_perk_bottle_doubletap`; 4 `zombie_perk_bottle_jugg`; 5 `zombie_perk_bottle_revive`; 6 `zombie_perk_bottle_sleight`.
- `map_patch.ff`: 0/0 weapon bodies (no known-name anchor and nothing to resolve).

**61 known anchors out of 61 match; ten remaining have plausible names, but pointer target proof is still missing.** The OAT T4 loader resolves packed block offsets in `ZoneInputStream::ConvertOffsetToPointerNative` using allocation into a **block buffer**, not by blindly treating `pointerOffset` as a decompressed-zone file offset. Never equate an XFile virtual-block pointer offset to a raw byte offset. Pointer semantics provenance: upstream OpenAssetTools `src/ZoneLoading/Zone/Stream/ZoneInputStream.cpp` and `src/ZoneLoading/Game/T4/ContentLoaderT4.cpp`.

**No production weapon renames have been made.** Next proof gate is to trace the XFile VIRTUAL block allocations/alias table or an equivalent exact loader mapping to associate each of the 10 packed name pointers with a known real name. The offline `weapon_order_witness_20261030_151804.json` contains unverified candidate mappings, not a green conversion certificate.

## October 30 — Aztec exact 71/71 OAT weapon names; offline source-order witness

**User Windows run on `f229a8c`:** 5/5 PS 5.1 syntax preflight PASS; packed uint32 self-check PASS; original Aztec WeaponDef proof corpus has 71 physical records (2 proof files), 10 synthetic packed-name placeholders. Original PC OAT `--list` success on all 3 participating FFs:

- `map.ff`: **61** real named WeaponDefs, **61** proof bodies; **55** exact proof names and **6** unresolved packed-name proof bodies.
- `map_patch.ff`: **0** real WeaponDefs, **0** proof bodies.
- `mod.ff`: **10** real named WeaponDefs, **10** proof bodies; **6** exact proof names and **4** unresolved packed-name proof bodies.

Counts now correspond 71/71, but **physical proof body order is not automatically equal to XAsset list order**, especially if packed/aliased references reorder objects. Do not assign names to pointers merely because an FF has the same count. The full genuine names and proof offsets are in `weapon_name_provenance_20261030_151256.json` under `output/multi_map_matrix/runs/run_20261030_024027_373fb3/`.

**New zero-build witness:** `Run-WaWMultiMapWeaponNameOrderWitness.ps1` works entirely from that existing provenance JSON: it compares every already-known proof name at its same ordinal in the OAT weapon list for each *original source FF*. Only if counts, uniqueness, and ALL known name anchors match does it emit **unverified ordinal candidate pairings** for the 6+4 packed-name records. Any mismatches shut down inference and print exact conflicting positions. This does not rebuild anything, call OAT, read original FFs, or apply names to the emitted Xbox FF. The main `Run-WaWMultiMapWeaponNameAudit.ps1` has the same position-witness logic for future fresh evidence, but reruns OAT; this new tool is the faster first check.

```powershell
git pull --ff-only
if ($LASTEXITCODE -ne 0) { throw 'Git pull failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PS syntax preflight failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapWeaponNameOrderWitness.ps1'
```

**Next after witnessing:** if all known anchors align, independently prove packed Virtual-block pointer target resolution using original PC FF semantics before authorizing those names in production. If any anchors disagree, use pointer-directed resolver directly; do not pair by position.

## October 30 — real Aztec weapon pointer and original-PC name census

**Windows-confirmed after 35fdb93:** PowerShell 5.1 syntax preflight PASS 5/5; unsigned-pointer self-test PASS. The **Aztec** typed-proof archive has 71 physical WeaponDef proof records from 2 proof files, with 10 synthetic `<packed-name@...>` placeholders. All five printed unresolved pointers decode into **Virtual block 4** with source-specific packed block offsets; this establishes *pointer provenance*, not the pointed-to string value.

Original PC source graph enumerated 3 participating FFs. Native OAT `--list` returned exit 0 for all three. The previous audit sampled 12 arbitrary lines containing `weapon`, and was polluted by `loadedsound, sfx/weapon/...` and `fx, weapon/...`, so these samples could NOT prove a weapon-name mapping.

**New read-only crosswalk (Windows validation pending):** `Run-WaWMultiMapWeaponNameAudit.ps1` now selects **only** exact `weapon, <name>` OAT asset lines and saves the full per-FF weapon-name list. It computes the corresponding proof file using the same source-basename/role naming convention as `LinkerV2ProofEvidence.GetProofPath`, then reports known exact proof names, unresolved proof bodies, OAT-only names, and mismatches per original source FF. It does **not** pair names to packed pointer records automatically (OAT tables may contain alias references and proof bodies are serialized in physical-offset order). All pointer-to-name mappings remain unresolved until virtual-block loader semantics are independently proven.

**Targeted next Windows run (no conversion, no Xbox FF rebuild, read-only original PC OAT listing):**

```powershell
git pull --ff-only
if ($LASTEXITCODE -ne 0) { throw 'Git pull failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'Syntax check failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapWeaponNameAudit.ps1' -IncludeMaps nazi_zombie_aztec_V2.2
```

Once this produces full per-original-FF real names, implement and prove packed virtual-block name-pointer target resolution instead of copying other weapons or guessing names from index position. Keep 6 proven IWD GSC/CSC overrides and independent OAT `--list` PASS as regression checkpoints.

## October 30 — packed WeaponDef name-audit UInt32 hotfix

User ran `Run-WaWMultiMapWeaponNameAudit.ps1` after `cf2a3ed`; syntax preflight PASS 5/5. Audit stopped at proof record #3 with `Weapon proof pointer is null or special sentinel at record #3` BEFORE inspecting original PC FFs. This was a **Windows PowerShell 5.1 diagnostic arithmetic bug**, not a demonstrated invalid source pointer. The code compared a `UInt32` pointer against `0xFFFFFFFE` using `-ge`; WinPS 5.1 parses the high-bit hex literal as signed **-2**, making valid high-bit block 4 pointers compare greater than -2.

Fix: compare against exact unsigned sentinels parsed via `[Convert]::ToUInt32('FFFFFFFE',16)` and `[uint32]::MaxValue`, rather than an ordered comparison. Added a no-I/O decoder regression on known Aztec raw pointer `0x83445D65` => `Virtual block 4`, offset `0x03445D64`. This is still not actual name-string recovery. No original map/weapon source files are altered. New Windows runtime validation pending.

Use the existing read-only command after `git pull --ff-only`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PowerShell syntax check failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapWeaponNameAudit.ps1' -IncludeMaps nazi_zombie_aztec_V2.2
```

## Windows verification — actual Aztec IWD script payload conversion (October 30)

User ran commit `afbffb5` on Windows 10 with **one-map Candidate** `nazi_zombie_aztec_V2.2` after recipe Source PASS:

- `linker-v2-main-structural => PASS (0)`.
- `RECIPE IWD script-closure overrides=6`: six preexisting FF RawFile roots were rebuilt using the authoritative winning IWD script bytes (3 distinct loose GSC/CSC source script paths were recorded in preceding source inventory). This is an **actual content change**, not a shell-size adjustment.
- `scriptClosureReady=True`; `residentMenusRemoved=0`; `rawFilesPromoted=0`; `readBackErrors=0`; `wrapperRoundTrip=True`.
- Script-closure `.ff` **449,870 bytes**, while the tracked-shell control was **449,763 bytes**. Size delta **+107 bytes**, not a quality score.
- Independent native Xenon OAT `--list` of the script-closure `.ff`: **exit 0**, `Finished with 0 warnings, 0 errors`.
- The primary proof-heavy FF remained **6,205,444 bytes**, full OAT extraction still rejected real unresolved `weapons/<packed-name@...>` labels. **No playable Xbox output approved**.

**Regression retained:** Linker V2 `VerifyReadBack` already compares each emitted payload byte-for-byte against the contiguous rebuilt zone, then the Xbox zlib wrapper round-trips. Reuse those checks; do not add weaker duplicate checks.

**Next priority (read-only, no extra build):** inspect unresolved original-PC WeaponDef internal-name pointers with `Run-WaWMultiMapWeaponNameAudit.ps1 -IncludeMaps nazi_zombie_aztec_V2.2`. The audit now decodes the packed T4 pointer's block and offset (3 block bits, 29 offset bits, +1 bias), while marking the real string name **unresolved** until the virtual-block target is traced. Do NOT replace synthetic labels with arbitrary filenames. This added decoder has not yet passed Windows syntax/runtime testing.

```powershell
git pull --ff-only
if ($LASTEXITCODE -ne 0) { throw 'Git pull failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PowerShell syntax preflight failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapWeaponNameAudit.ps1' -IncludeMaps nazi_zombie_aztec_V2.2
```

## Focused Windows preflight (no FF rebuild)

```powershell
git pull --ff-only
if ($LASTEXITCODE -ne 0) { throw 'Git pull failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapMatrix.ps1' -Mode Source -IncludeMaps nazi_zombie_FRUmansion,nazi_zombie_aztec_V2.2
```

The runner reports `[MULTIMAP-RECIPE]` counts and the `recipe_source_precedence/<map>_recipe_iwd_precedence.json` location under each run's conversion folder. Source mode only indexes precedence; `AppliedToXenonPayload=False`. The distinct script-closure **Candidate** diagnostic now reads that indexed evidence and applies matching GSC/CSC IWD winners to existing RawFile roots and newly promoted missing scripts. Both source index and script-closure changes still require Windows compile/test before being treated as verified.

Update this file from actual logs, not speculative success.
