# PC2XB3 — World at War PC → Xbox 360 Map Converter

PC2XB3 is a work-in-progress converter/linker for **Call of Duty: World at War** custom PC maps targeting **modified Xbox 360 consoles**.

The project does **not** distribute retail World at War game files or copyrighted map content. Users provide their own game data and custom-map sources.

## Current status — October 25, 2026

The deterministic Linker V2 pipeline is in late native-loader bring-up. Source discovery, canonical root accounting, script-string reconstruction, Xbox type mapping, XFile/XAsset emission, block-capacity planning, Xbox single-zlib wrapping, selected-load generation, and real-hardware diagnostics are working foundations.

The current main blocker is the exact **PC → Xbox TU7 XAnimParts layout/stream transform**. The repository intentionally does not claim that this transform is complete yet.

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
- A working Xbox header satisfies two independent totals: `boneCount[0..4] + boneCount[9] == boneCount[11]` and `boneCount[5..8] + boneCount[10] == boneCount[11]`, strongly identifying `[9]` and `[10]` as Xbox-only quaternion/translation buckets.
- Working names such as `ai_flame_death_a` resolve through the loaded `+0x00` pointer.
- The CLI now includes `xanim-bone-compare <pc.ff> <xbox.ff> [name]`, a read-only same-animation oracle that reports PC/Xbox bone-count arrays and the exact shared-bucket deficits feeding Xbox-only buckets.

What is **not yet proven** is the exact semantic mapping of every PC XAnim header field into the Xbox TU7 `0x5C` structure, especially the extra bone-count categories and the field around `+0x28`. The next implementation will come from the native loader/oracle, not from padding or guessed offsets.

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
- [ ] Finish decompiling `Function_8215F830` and label every Xbox field offset.
- [x] Prove `boneCount[11]` drives names and establish the Xbox quaternion/translation total invariants.
- [ ] Use the same-animation PC/Xbox oracle to identify exactly which PC categories reclassify into Xbox `boneCount[9]` and `boneCount[10]`.
- [ ] Prove the `+0x28` field and exact framerate/frequency offsets.
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
