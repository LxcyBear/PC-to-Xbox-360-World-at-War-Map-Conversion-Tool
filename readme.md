# PC2XB3 — World at War PC → Xbox 360 Map Converter

PC2XB3 is a work-in-progress converter/linker for **Call of Duty: World at War** custom PC maps targeting **modified Xbox 360 consoles** running TU7-compatible environments such as CodXe or Wonderland.

The project does **not** distribute World at War game files, retail fastfiles, or copyrighted map assets. Users must provide their own legally obtained game data and custom-map source files.

## Current status — October 25, 2026

The project is in late native-loader bring-up. The converter can already reconstruct the Xbox fastfile wrapper, XFile/XAsset table, canonical script strings, source-derived block capacities, load package, and a growing set of Xbox-native asset payloads.

The current main-FF hardware candidate is **not yet a full playable-map conversion**, but it has moved past the original XAnim failure frontier on real Xbox 360 hardware.

### Hardware-proven TU7 XAnim findings

The following behavior has now been confirmed directly against the TU7 Xbox executable and live Xbox memory:

- Xbox `XAnimParts` fixed serialized root is **0x5C (92 bytes)**.
- PC WaW uses the **0x58-byte** desktop root; PC parsing remains 0x58 and Xbox emission is converted explicitly.
- Xbox expands the bone-count area to 12 bytes; the PC total-bone count is moved to Xbox `+0x1D`.
- Xbox owned pointers live at `+0x34 .. +0x58`.
- `XAnimParts +0x00` is the animation-name pointer.
- The XAnim loader switches to stream/block 4 for owned payload data.
- Inline XString loading and NUL-terminated stream advancement are confirmed.
- Real names such as `viewmodel_kar98_idle`, `viewmodel_kar98_solo_reload`, and `viewmodel_kar98_spin` resolve correctly on hardware.
- Recursive `Function_82168570` XAsset linking now returns successfully through `0x82168914`.
- TU7's XAnim replacement/merge behavior has been observed live and is no longer being mistaken for corruption.

The Linker V2 source now promotes the **complete typed XAnim payload** instead of the old name-only XAnim shell.

## Roadmap

### 1. Source ingestion and deterministic graph construction — COMPLETE

- [x] Detect Normal-map source packages and participating FF/IWD files.
- [x] Build canonical source archive/package graph.
- [x] Canonicalize and remap script strings.
- [x] Preserve authoritative XAsset provenance and source order.
- [x] Map PC asset IDs to the Xbox TU7 asset table, including the Xbox-only pixel-shader slot.
- [x] Derive destination block capacities from source data rather than copying a donor map.

### 2. Native fastfile/container foundation — COMPLETE

- [x] Xbox big-endian XFile/XAsset emission.
- [x] Correct `zoneSize` semantics.
- [x] Seven-block Xbox header emission.
- [x] Xbox single-zlib wrapper and round-trip verification.
- [x] Selected-load FF generation and load-package workflow.
- [x] Hardware A/B testing workflow on real Xbox 360.

### 3. XAsset ordering and registration — COMPLETE FOR CURRENT FRONTIER

- [x] Use one authoritative **Xbox type-major emission order** for both XAsset table entries and physical payloads.
- [x] Add a hard table/payload type-order guard so mismatches fail before an FF is emitted.
- [x] Verify generic type-dispatched XAsset name lookup on hardware.
- [x] Verify Material, TechniqueSet, GfxImage, RawFile, and XAnim top-level name/header paths.
- [x] Verify recursive XAsset registration/link return path.

### 4. XAnim native promotion — COMPLETE / HARDWARE VALIDATION CONTINUES

- [x] Keep PC source parsing at `0x58`.
- [x] Convert PC XAnim root to the TU7 Xbox `0x5C` layout.
- [x] Expand the bone-count region and move the total count to Xbox `+0x1D`.
- [x] Shift all Xbox scalar/pointer fields to their TU7 offsets.
- [x] Preserve names, bone ScriptStrings, notifies, delta translation/quaternion data, scalar arrays, random arrays, and frame indices.
- [x] Independently parse emitted Xbox XAnim blobs back to exact record boundaries.
- [x] Regenerate stale pre-v5 XAnim proof artifacts automatically in Fast Mode.
- [x] Rebase packed top-level XAnim names for the self-contained structural candidate while preserving the complete owned animation payload.
- [ ] Run the new full-XAnim FRUmansion hardware candidate without diagnostic breakpoints.
- [ ] Confirm the next native failure frontier after full animation payload promotion.

### 5. Remaining full native asset promotion — IN PROGRESS

These families already have useful typed/proof infrastructure, but the main linker still contains reduced payloads for some of them:

- [ ] **XModel** — promote complete model/surface/bone ownership into Linker V2.
- [ ] **Weapon** — promote full WeaponDef plus referenced animation/model/effect/audio dependencies.
- [ ] **FX / ImpactFX** — promote complete effect graphs and visual dependencies.
- [ ] **Material / TechniqueSet / shaders** — replace inert/null-graph structural objects with full Xenos-compatible graphs.
- [ ] **GfxImage** — complete image/load-def/resource handling while preserving the hardware-confirmed Xbox name offset.
- [ ] **Sound / LoadedSound** — replace silent structural stubs with final Xbox audio handling.
- [ ] **Menu / MenuList / Font / UI** — close remaining UI ownership and pointer relocation.
- [ ] **RawFile / Localize / StringTable** — finish shared/packed-pointer relocation cases.

### 6. World, collision, and gameplay data — NEXT MAJOR MILESTONE

- [ ] Promote full `clipmap_pvs` / collision graph.
- [ ] Promote `ComWorld`.
- [ ] Promote `GameWorldSp`.
- [ ] Promote `GfxWorld` and world render dependencies.
- [ ] Preserve map entities and gameplay-facing raw scripts.
- [ ] Validate BSP/world identity and load transition on hardware.

**Target:** reach the first genuine custom-map loading screen/world initialization instead of a structural loader-only success.

### 7. Production linker closure

- [ ] Resolve all packed/shared target ownership.
- [ ] Replace every remaining diagnostic fallback with a typed native serializer.
- [ ] Close dependency promotion across main FF, load FF, IWD content, and resident assets.
- [ ] Validate block allocation against real runtime consumption.
- [ ] Add deterministic regression fixtures for known working maps.
- [ ] Run clean hardware tests with no debugger-only assumptions.

### 8. Beta readiness

- [ ] Map loads to gameplay on Xbox 360.
- [ ] Rendering, collision, scripts, weapons, FX, audio, UI, and zombies function correctly.
- [ ] Fast Mode produces the same deterministic output from the same source.
- [ ] Safe Mode remains available for deeper diagnostics.
- [ ] Clean error reporting for unsupported/broken source maps.
- [ ] Documentation for installation, conversion, and Xbox deployment.
- [ ] First public beta release.

## Current Fast Mode hardware candidate

After building the current `main` branch, the latest main-FF diagnostic package is emitted under:

```text
output\<map-name>\hardware_test_main_xanim_native_layout
```

For FRUmansion, the expected directory is:

```text
output\nazi_zombie_FRUmansion\hardware_test_main_xanim_native_layout
```

Use the map's normal Xbox-facing filenames (for example `nazi_zombie_FRUmansion.ff` and `nazi_zombie_FRUmansion_load.ff`). Do not rename converted files to a donor map's identity.

## Development principles

- **No donor-main dependency.** Known-working converted maps may be used as behavioral or hardware oracles, but the final linker must reconstruct the destination deterministically from the user's source data.
- **Fail closed.** Unknown ownership, ambiguous packed pointers, or unverified layouts should stop emission rather than silently produce corrupt FFs.
- **Hardware evidence wins.** TU7 executable behavior and live Xbox memory are the authority when PC and Xbox layouts differ.
- **No game content in the repository.** The project contains converter code and diagnostics only.
- **Keep Fast Mode fast.** Proven serializer knowledge is migrated into Linker V2; older deep proof stages remain diagnostic tools rather than a mandatory conversion marathon.

## Repository layout

- `src/WaW360.Core` — fastfile parsing, serializers, Linker V2 graph/linker logic, block planning, and Xbox writers.
- `src/WaW360.Gui` — Windows GUI and Fast/Safe Mode orchestration.
- `WaW360Converter.sln` — Visual Studio solution.

## Build

The tracked repository builds with the .NET 8 SDK:

```powershell
dotnet build .\WaW360Converter.sln -c Release
```

If you use a local publishing/helper script around the solution, keep it outside the repository-specific instructions unless that script is also committed.

The repository is under active development; hardware behavior and output paths may change as each native asset family is promoted.
