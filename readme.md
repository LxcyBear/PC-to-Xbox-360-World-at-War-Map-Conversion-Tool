# PC2XB3 — World at War PC → Xbox 360 Map Converter

PC2XB3 is a work-in-progress converter/linker for **Call of Duty: World at War** custom PC maps targeting **modified Xbox 360 consoles**.

The project does **not** distribute retail World at War game files or copyrighted map content. Users provide their own game data and custom-map sources.

## Current status — October 25, 2026

The deterministic Linker V2 pipeline is in late native-loader bring-up. Source discovery, canonical root accounting, script-string reconstruction, Xbox type mapping, XFile/XAsset emission, block-capacity planning, Xbox single-zlib wrapping, selected-load generation, and real-hardware diagnostics are working foundations.

The current main blocker is now very narrow: the Xbox TU7 `XAnimParts` fixed layout, owned stream structure, and the **10-count PC → 12-count Xbox category expansion pattern** are proven from a same-animation FRUmansion oracle pair. What remains is the semantic rule that splits PC quaternion bucket `[2]` into Xbox `[2]/[3]` and PC translation bucket `[5]` into Xbox `[6]/[7]`, plus final serializer promotion.

## Hardware-proven TU7 XAnim findings

Real Xbox 360 memory and TU7 executable tracing have established:

- XAnim is Xbox XAsset type **4**.
- The TU7 XAnim loader calls `Load_Stream(..., 0x5C)`, proving a **0x5C (92-byte)** fixed Xbox root.
- The loaded XAnim pointer at `+0x00` is the resolved animation-name pointer.
- The loader pushes stream position **4** immediately after loading the fixed XAnim root.
- The XAnim-specific registration wrapper reaches the generic XAsset linker and returns successfully.
- Working runtime XAnim roots are separated by `0x5C`.
- Live working headers show a **12-byte bone-count region** at `+0x12..+0x1D`; the final bytes are meaningful and must not be guessed or zero-filled.
- `+0x1D` is independently proven as `boneCount[11]`, the total/name ScriptString count; `+0x1E` is `notifyCount`.
- A working Xbox header proves the actual grouping is **six quaternion buckets + five translation buckets + total**: `sum(boneCount[0..5]) == boneCount[11]` and `sum(boneCount[6..10]) == boneCount[11]`.
- Working names such as `ai_flame_death_a` resolve through the loaded `+0x00` pointer.
- `+0x24` is proven as `randomDataShortCount`.
- `+0x28` is proven as `indexCount`; `Function_82159ED0` also proves byte indices when `numFrames < 256` and 16-bit indices otherwise.
- Loader-consumed pointers are mapped through the full root: `+0x34 names*`, `+0x38 dataByte*`, `+0x3C dataShort*`, `+0x40 dataInt*`, `+0x44 randomDataShort*`, `+0x48 randomDataByte*`, `+0x4C randomDataInt*`, `+0x50 indices*`, `+0x54 notify*`, and `+0x58 deltaPart*`.
- The generated post-load/mark path independently walks `names[]` using `boneCount[11]` and `notify[]` using `notifyCount`.
- The type-4 registration wrapper was followed through `FUN_82169F78 → Function_82168968`; that path is generic XAsset bookkeeping rather than animation decoding.
- The CLI includes `xanim-bone-compare <pc.ff> <xbox.ff> [name]`, a read-only same-animation oracle.
- The first hardware-backed pair, `ai_flame_death_a`, proves the structural expansion:
  - PC `[0] -> Xbox[0]`
  - PC `[1] -> Xbox[1]`
  - PC `[2] -> Xbox[2] + Xbox[3]`
  - PC `[3] -> Xbox[4]`
  - PC `[4] -> Xbox[5]`
  - PC `[5] -> Xbox[6] + Xbox[7]`
  - PC `[6] -> Xbox[8]`
  - PC `[7] -> Xbox[9]`
  - PC `[8] -> Xbox[10]`
  - PC `[9] -> Xbox[11]`
- For `ai_flame_death_a`, PC `[04 00 67 01 01 0F 06 01 57 6D]` maps to Xbox `[04 00 55 12 01 01 00 0F 06 01 57 6D]`; both split equations and both total invariants hold exactly.

### Loader-consumed Xbox TU7 `XAnimParts` layout

```text
+00  name*
+04  dataByteCount          u16
+06  dataShortCount         u16
+08  dataIntCount           u16
+0A  randomDataByteCount    u16
+0C  randomDataIntCount     u16
+0E  numFrames              u16
+10  flags/loop byte        semantics still being named
+11  flags/delta byte       semantics still being named
+12  boneCount[0]           quaternion bucket 0
+13  boneCount[1]           quaternion bucket 1
+14  boneCount[2]           quaternion bucket 2a
+15  boneCount[3]           quaternion bucket 2b
+16  boneCount[4]           quaternion bucket 3
+17  boneCount[5]           quaternion bucket 4
+18  boneCount[6]           translation bucket 5a
+19  boneCount[7]           translation bucket 5b
+1A  boneCount[8]           translation bucket 6
+1B  boneCount[9]           translation bucket 7
+1C  boneCount[10]          translation bucket 8
+1D  boneCount[11]          total bones / names count
+1E  notifyCount
+1F  asset-type/flags area  exact naming still being finalized
+24  randomDataShortCount   u32
+28  indexCount             u32
+2C  framerate              strong layout match; final native-use proof pending
+30  frequency              strong layout match; final native-use proof pending
+34  names*
+38  dataByte*
+3C  dataShort*
+40  dataInt*
+44  randomDataShort*
+48  randomDataByte*
+4C  randomDataInt*
+50  indices*
+54  notify*
+58  deltaPart*
= 0x5C bytes
```

What is **not yet proven** is the per-bone semantic criterion that divides PC `boneCount[2]` between Xbox `boneCount[2]/[3]` and PC `boneCount[5]` between Xbox `boneCount[6]/[7]`. The final semantic naming of small flag/metadata fields around `+0x10..+0x23` and native-use confirmation for `+0x2C/+0x30` also remain. The serializer stays fail-closed until those split rules are derived rather than guessed.

## Roadmap

### 1. Source ingestion and deterministic graph construction — COMPLETE

- [x] Detect Normal-map source packages and participating FF/IWD files.
- [x] Build canonical source archive/package graph.
- [x] Canonicalize and remap script strings.
- [x] Preserve authoritative XAsset provenance.
- [x] Map PC asset IDs to Xbox TU7 asset IDs.
- [x] Derive destination block capacities from source data instead of copying a donor main FF.

### 2. Native fastfile/container foundation — COMPLETE

- [x] Xbox big-endian XFile/XAsset emission.
- [x] Correct `zoneSize` semantics.
- [x] Seven-block Xbox header emission.
- [x] Xbox single-zlib wrapper and round-trip verification.
- [x] Selected-load FF generation and hardware package workflow.
- [x] Real Xbox 360 / xbWatson A/B test workflow.

### 3. Xbox XAsset traversal/order — HARDWARE PROVEN

- [x] Remap PC asset type IDs to Xbox type IDs.
- [x] Emit the main graph in **Xbox type-major order**.
- [x] Keep XAsset table order and physical payload order synchronized.
- [x] Reach Material, TechniqueSet, GfxImage, RawFile, and XAnim registration paths on hardware.
- [x] Reach and return from the generic XAsset linker for XAnim.

### 4. XAnim native layout — IN PROGRESS

- [x] Prove TU7 fixed root load size is `0x5C`.
- [x] Prove loaded `XAnimParts +0x00` is the name pointer.
- [x] Prove XAnim owned loading switches to stream position 4.
- [x] Confirm a 12-byte live bone-count region in working Xbox XAnims.
- [x] Retain the existing typed PC XAnim walker for names, ScriptStrings, notifies, delta data, scalar streams, random streams, and frame indices.
- [x] Fully decompile `Function_8215F830` and map every loader-consumed Xbox pointer/count field.
- [x] Prove `+0x24 = randomDataShortCount`.
- [x] Prove `+0x28 = indexCount` and the `numFrames < 256` byte-index / otherwise 16-bit-index rule.
- [x] Prove `boneCount[11]` drives names and establish the Xbox quaternion/translation total invariants.
- [x] Prove `+0x1E = notifyCount` and the generated post-load/mark traversal.
- [x] Prove the owned-pointer chain from `+0x34` through `+0x58`, including `deltaPart*`.
- [x] Add the read-only same-animation `xanim-bone-compare` PC/Xbox oracle to the CLI.
- [x] Run the oracle against the working FRUmansion `ai_flame_death_a` pair.
- [x] Prove Xbox grouping is 6 quaternion buckets + 5 translation buckets + total.
- [x] Prove the PC 10-count → Xbox 12-count structural expansion equations.
- [ ] Derive the per-bone split criterion for PC `[2] -> Xbox[2]/[3]`.
- [ ] Derive the per-bone split criterion for PC `[5] -> Xbox[6]/[7]`.
- [ ] Validate the expansion equations across additional same-name XAnims in the working FRUmansion pair.
- [ ] Finalize the semantic names for the small `+0x10..+0x23` flag/metadata fields.
- [ ] Confirm native-use semantics for `+0x2C` framerate and `+0x30` frequency.
- [ ] Implement the PC `0x58` → Xbox TU7 `0x5C` transform only after those fields are proven.
- [ ] Add an independent Xbox-layout read-back verifier.
- [ ] Promote the full typed XAnim payload into Linker V2.
- [ ] Run one clean FRUmansion hardware test and capture the next native frontier.

### 5. Remaining native asset promotion — IN PROGRESS

- [ ] XModel — promote complete bone/surface/material ownership.
- [ ] Weapon — promote full WeaponDef and dependency closure.
- [ ] FX / ImpactFX — promote complete effect graphs.
- [ ] Material / TechniqueSet / shaders — replace structural/null-graph diagnostics with complete Xenos-compatible data.
- [ ] GfxImage — complete image/load-def/resource handling.
- [ ] Sound / LoadedSound — replace silent structural stubs with final Xbox audio handling.
- [ ] Menu / MenuList / Font / UI — close remaining UI ownership.
- [ ] RawFile / Localize / StringTable — close shared/packed-pointer relocation cases.

### 6. World, collision, and gameplay data — NEXT MAJOR MILESTONE

- [ ] Promote full `clipmap_pvs` / collision graph.
- [ ] Promote `ComWorld`.
- [ ] Promote `GameWorldSp`.
- [ ] Promote `GfxWorld` and render dependencies.
- [ ] Preserve map entities and gameplay scripts.
- [ ] Reach genuine custom-map loading-screen/world initialization on hardware.

### 7. Production linker closure

- [ ] Resolve all packed/shared target ownership.
- [ ] Replace every remaining diagnostic fallback with a typed native serializer.
- [ ] Close dependencies across main FF, load FF, IWD content, and resident assets.
- [ ] Validate destination block allocation against runtime consumption.
- [ ] Add deterministic regression fixtures using known-working converted maps as behavioral oracles only.
- [ ] Run clean hardware tests without debugger-only assumptions.

### 8. Beta readiness

- [ ] Map reaches gameplay on Xbox 360.
- [ ] Rendering, collision, scripts, weapons, FX, audio, UI, and zombies function correctly.
- [ ] Fast Mode produces deterministic output from identical source data.
- [ ] Safe Mode remains available for deep diagnostics.
- [ ] Clean unsupported/broken-map diagnostics.
- [ ] Installation, conversion, and deployment documentation.
- [ ] First public beta.

## Development principles

- **No donor-main dependency.** Working converted maps may be used as behavioral/runtime oracles; the final linker reconstructs output from the user's own source data.
- **Fail closed.** Ambiguous ownership or unproven layouts stop promotion instead of silently emitting guessed data.
- **Hardware evidence wins.** TU7 executable behavior and live Xbox memory are authoritative when PC and Xbox layouts differ.
- **No game content in the repository.**
- **Keep Fast Mode fast.** Proven serializer knowledge moves into Linker V2 instead of forcing the old proof marathon on every conversion.

## Repository layout

- `src/WaW360.Core` — FF parsing, typed serializers, Linker V2, block planning, Xbox writers.
- `src/WaW360.Gui` — Windows GUI and Fast/Safe Mode orchestration.
- `WaW360Converter.sln` — Visual Studio solution.

## Build

The tracked solution targets .NET 8:

```powershell
dotnet build .\WaW360Converter.sln -c Release
```

Local publish/deployment helper scripts may be used separately when present on the developer machine.
