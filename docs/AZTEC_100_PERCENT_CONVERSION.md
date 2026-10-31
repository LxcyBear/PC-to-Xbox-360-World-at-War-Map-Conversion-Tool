# Aztec 100% PC-to-Xbox-360 conversion contract

**Scope:** `nazi_zombie_aztec_V2.2` original PC map into a **complete Xbox-format CodXe usermap package**. The prior 451,050 B shell and 3,615,230 B partial GfxWorld FFs are **diagnostics**, not converted maps, and must NEVER be presented as completed Xbox releases.

## Fixed user requirement

The final `nazi_zombie_aztec/` directory must have the full content/layout of a known working native Xbox Aztec usermap, not just a similarly named main `.ff`. The matching native reference directory is a **shape/identity/oracle** only: NEVER copy its actual native FF, XModel, renderer, image, static draw or sound payload bytes to fabricate the converted result. Production asset bytes must originate from the owned original PC map and compatible uniquely proven Xbox **retail stock** assets, with provenance for every body and transcode step. No diagnostic/oracle-only donor bytes in the production output.

The known reference contains **44 files** (37 `highmip/` files) and a main FF of **80,309,061 B**. Those reference sizes are *not* required to be matched: source compression, asset composition, standalone stock assets, and serialization can differ. The final **relative filenames, companion resources and meaningful asset graph** are what must be accounted for. The initial requested topology includes (where present in the reference):

```text
nazi_zombie_aztec/
    highmip/
    nazi_zombie_aztec.ff
    nazi_zombie_aztec_load.ff
    nazi_zombie_aztec_patch.ff
    nazi_zombie_aztec_load.bik
    nazi_zombie_aztec.pak
    map.arena
    preview.dds
```

`preview.dds` was earlier deferred **for initial diagnostics only**; the final complete-package requirement includes it. Any additional reference files (including images, sound banks, metadata) must appear in the production layout with source/identity evidence. Do not invent an empty `.pak`, `_patch.ff`, `.bik`, or `highmip` just to make directory names match.

## Evidence, not wishful percentages

Historical `Show-AztecFirstXboxFFProgress.ps1`: 9/13 **development evidence gates**. Not 69% of total conversion.

| Required output | Current verified position | Remaining production work |
| --- | --- | --- |
| Native XAsset root identity | 8,509 identities proven | Genuine source/stock-owned payload/dependency closure for every live root; shells are not converted assets |
| GfxWorld geometry | 32,713 PC vertices, 58,653 indices, 735 surfaces and 39 Materials integrated into diagnostic | Full DPVS/static/dynamic children, cells, lighting/lightmaps, collision, all indexed references |
| Static draws | 1,903 typed PC draws, 5,709 packed rotation axes, 60 B Xbox ABI readback | 1,903 real Xenon draw structs with semantically correct lighting, flags, and XModel* pointers; physical GfxWorld links; 1 external stock model residency check |
| XModels | 28 real converted XModel graph bodies in diagnostic | Remaining map/stock referenced XModels, dependency ownership, material/image graphs, render/runtime verification |
| Images/materials | 39 Materials bound, 2 real image bodies in current scratch FF | All map-owned textures and correct Xbox IWI/DDS highmips; eliminate identity-only images and missing image payloads |
| Animations/FX/weapons | Existing source probes, XAnim/WeaponDef partial diagnostic work | Real native encoded bodies and every linked child; remove synthetic WeaponDef name/field shells |
| Scripts/data | Some PC rawfile and IWD override proof | Main/patch/mod scripts, IWD precedence, runtime script resolution, strings/tables and complete GSC/CSC closure |
| Audio | Main scratch OAT extraction: 1,532 `WARN: Loaded sound ... has no XMA data`, zero errors | Actual sound ownership, conversion to appropriate Xbox/XMA where needed, loose sound asset packaging, runtime resolution (no blanket XMA warning bypass) |
| Main, load, patch, other FFs | Standalone diagnostic world + generated 7-node load; original-PC selected load verified | Integrated final source-derived FF family with independent OAT and engine validation, no borrowed reference FFs |
| Full CodXe directory | Test-stage main/load only | All native-layout companions, correct map metadata, `.pak`, `.bik`, `highmip`, `preview.dds`, sounds, hash-proven installed output |

## Concrete first work item: static draw relocation

Use **existing evidence**, do not rebuild the 28 XModels just to plan pointers:

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Plan-Aztec1903NativeDrawRelocations.ps1'
```

This verifies typed original-PC rows, 1903 native XModel root identities, the 5709 packed rotation axes, 114180 source geometry bytes, and the **28 physically owned model graph proofs** before writing a deterministic per-row `nativeXModelRootIndex` relocation *intent* list. It does **not** emit pointer words or a FF. The one exact externally referenced Xbox stock model remains explicitly `STOCK_REFERENCE_RESIDENCY_UNVERIFIED`. The next actual emitter change is to consume these verified indices through the existing Stage 4D/4E `stableByRoot` pointer map **and** serialize the remaining genuinely source-derived 60-byte draw fields and parent DPVS array; no fake zeros/native donor draw tails.

## Exact full package layout coverage

The read-only topology comparison defaults to the user's existing known-working native Aztec **reference** directory and a future dedicated production output `output\complete_conversion\nazi_zombie_aztec`. It never copies or changes any native reference file:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Audit-Aztec100PercentPackage.ps1'
```

Override `-ReferenceMapDirectory` for the correct native reference and `-ConvertedMapDirectory` for the future actual converted output. Report: `output\aztec_full_package_closure\aztec_full_package_layout_closure.json`. It lists **every missing companion path**, counts highmip/audio/FF files separately and flags FFs whose SHA exactly matches native reference FFs. It is a **layout auditor only**, always blocks release pending independently proven real conversion and runtime dependency closure. Do not interpret `generatedFileExists` as a valid converted asset.

## Release and hardware testing

The old `Stage-AztecFirstExceptionCanary.ps1` and `Stage-AztecWorldFirstExceptionCanary.ps1` remain historic **first-exception experiments**. Their 3.45 MiB main FF crashed on Xbox at the familiar `0x82168594`, which does **not** identify a root cause or certify that the Xbox loaded every new byte.

No further Xbox *map conversion release* until:
1. All source-owned or legitimately retail-stock Xbox runtime assets have real native payloads and references; **zero critical identity-only shells or unproven stock residency**.
2. Full 1903-draw / DPVS / renderer / collision graph, all scripts, weapons, animations, sound ownership, images and child pointers are actually emitted and validated.
3. Actual main/load/patch/companion file set passes independent format verification and the **full package topology audit** with explicit per-file provenance (no borrowed reference FFs).
4. Exactly those final converted files and SHA hashes are verified in the actual Xbox installation, then full runtime loading/gameplay/audio/rendering tests pass.

Independent Xbox testing is **verification of the completed payload**; OAT listing/extraction cannot establish gameplay correctness. Do not write status `100% converted` until all stages complete.

**Code status:** The new relocation and package-audit PowerShell scripts have been pushed, but they require the user's Windows PowerShell 5.1 syntax check and local evidence run to verify their execution. No new main FF was generated by these commits.

## Stage4D stable-root pointer emission (next verified step)

After the 1903-draw relocation plan passes, run `tools/oat-xenon/Build-Aztec1903DrawPointerRelocationCandidate.ps1`. It consumes the **actual verified Stage 4D/4E `scratch_emit/layout_recheck/nuclear_xenon_block_layout.json`** (8509 stable root identities, 28 converted XModels), the SHA-bound 1903 source-ordinal plan and source-only 60-byte geometry bridge. It emits 1902 genuine big-endian `XModel*` stable-root pointer words into the 1903 x 60-byte *candidate buffer*, checks every pointer and source geometry byte, and leaves the single external-stock-model draw's pointer unpopulated until runtime residency is proven. This is conversion work, but the candidate remains **NOT FOR FF**: no lighting/flag/padding mapping or full DPVS graph yet, 0/1903 live draw links. The 28-body world FF, original models and final Xbox output remain unchanged. Existing Phase 4D/4E project files need no rebuild to produce this candidate.

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'Syntax check failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Build-Aztec1903DrawPointerRelocationCandidate.ps1'
```

The earlier relocation-intent run on Windows **passed 1903/1903**; the new stable-pointer candidate itself is not Windows verified until this command runs. The next true emitter integration is to populate source-proven Xenon light/flag fields, decide reserved pad semantics, close 1 stock runtime dependency, add the 1903-source-draw array to **both Stage 4D allocation and Stage 4E emission** in the exact OAT T4 Xenon child order, and update parent DPVS pointers/counts. Only then can its native OAT/full-asset closure tests be meaningful.

## Generalizing verified Aztec relocation into the shared PC2XB3 converter

Shared .NET 8 code now includes `src/WaW360.Core/FastFiles/T4XenonStaticDrawPointerCodec.cs`. Two map-independent CLI commands:

- `xenon-static-draw-pointer-selftest` tests synthetic **3 x 48-byte** layouts (not Aztec's 1903 x 60), T4 VIRTUAL packed pointer endianness, quarantined stock references, and rejection of an already-populated pointer.
- `xenon-static-draw-pointer-encode <input.json> <output.bin> <report.json>` processes arbitrary counts/strides/field offsets using neutral schema `WaW360.T4Xenon.StaticDrawPointerInput.v1`. No Aztec-specific file paths or expected counts are in the .NET pointer codec.

The regression tool `tools/oat-xenon/Verify-AztecSharedDrawPointerCodec.ps1` generates generic input from Aztec's SHA-pinned 1903-row source plan, 28-source-XModel staging proof, original-PC geometry and verified Stage4D 8509-root layout. It builds the CLI incrementally, runs the non-Aztec selftest, encodes 1902 owned XModel pointers (one unverified stock reference remains quarantined), then **requires exactly the same SHA-256 and 114180 bytes as the previous user-verified PowerShell candidate**. No FF is changed.

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PS5.1 preflight failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Verify-AztecSharedDrawPointerCodec.ps1'
```

This is reusable source-owned pointer serialization infrastructure, not a completed world conversion. Light/flag fields, DPVS/static array parent, one stock XModel runtime residency, all other assets and the full package remain blocked. The generated candidate is NOT FOR FF or Xbox deployment. Each new map must provide its own provenance-verified input; do not hardcode Aztec's 1903/60/28/8509 in the codec.

## Gate 8G: verify whether original PC draw scalars map directly to Xenon

The owned source extraction already contains per-draw `reflectionProbeIndex`, `primaryLightIndex`, `flags`, `cachedLightSettingIndex`, `lodFade`, `field44`, `field45`, and 4 `smodelCacheIndex` entries. The OAT native Xbox 60B ABI witness independently exposes per-draw `reflectionProbeIndex`, `primaryLightIndex`, `lightingHandle`, `flags`, and a **count** of nonzero padding bytes. These are different source-side layouts; **do not assume the values or semantics match**.

Run the **read-only** `tools/oat-xenon/Audit-Aztec1903StaticDrawScalarEquivalence.ps1` against the existing SHA-bound PC sidecar and exact native OAT witness. It first compares source and native ordinal model identity, packed axes, origin, scale and culling distance, then counts per-field exact matches for the reflection/primary light indices, flags, and the hypothesis `cachedLightSettingIndex == lightingHandle`. A direct source field is reported as a candidate only if **all 1903 rows have geometry/model correspondence and exact matching field values with valid Xenon byte widths**. Nonmatching fields remain blocked; padding is **never** imported from the Xbox reference. No FastFile is emitted.

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PS51 preflight failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Audit-Aztec1903StaticDrawScalarEquivalence.ps1'
```

The native original-Xbox ABI witness must already exist from prior `Run-AztecGate8NativeStaticDrawAbiWitness.ps1`; this script does **not** rebuild or rerun native OAT. A hard missing prerequisite is reported explicitly. This calibration applies to Aztec and is evidence for a source-owned mapping rule, **not** generic certification for arbitrary PC maps. The decoder/editor must still correctly map per-map light handles, non-owned model availability and full DPVS serialized children before any native production attempt.

### Restoring the missing native 60-byte ABI report (without unnecessary OAT work)

Gate8G's first Windows run parsed correctly (21/21 scripts), but halted because `xenon_native_gate8_static_draw_60b_abi_witness/aztec_native_1903_static_draw_abi_witness_report.json` did not exist. This is a **prerequisite gap**, not a failed scalar-equivalence test.

`Run-AztecGate8NativeStaticDrawAbiWitness.ps1` now attempts a **strict cache hit** from the existing Xbox native 76B GfxWorld OAT witness when its pinned reference SHA, 1,903 typed records, and exact 60B/model/light/flags ABI are present. Only a cache miss reruns OAT (and only a missing specialized capability requires the explicit one-time `-BuildDumper` switch). No game FF is modified by the witness or audit.

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PS 5.1 preflight failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-AztecGate8NativeStaticDrawAbiWitness.ps1'
if ($LASTEXITCODE -ne 0) { throw 'Native 60B draw witness did not pass; inspect the exact cache/capability message' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Audit-Aztec1903StaticDrawScalarEquivalence.ps1'
```

If the witness reports the specialized OAT dumper capability missing, run **that witness script only** with `-BuildDumper` once, then repeat the scalar audit. No need to rerun the already verified 28-XModel integrated-world build or generic pointer regression. Strict release gates remain blocked.

## Gate 10 source-owned static draw serialization frontier — 1903 flags, 1902 model pointers

The actual Xbox native 60-byte draw ABI witness now passes. Source-vs-reference agreement for Aztec is: **1903/1903** model identities, origins, scales and culling distances; **1899/1903** exact packed axis words; **1903/1903** flags; **0/1903** reflection indices; **28/1903** primary light indices; and **0/1903** direct PC cachedLightIndex vs Xbox lightingHandle values. Four rotation-word discrepancies and all actual light remapping must remain unresolved, not filled from Xbox donor bytes.

Updated `Audit-Aztec1903StaticDrawScalarEquivalence.ps1` prints the **four exact per-axis XORs**, keeps separate (a) fully identical rotations, (b) 1903 name/origin/scale/cull identity anchors, and (c) exact field bytes, and gives value-pair histograms and a deterministic-remapping hypothesis for reflection, primary light and cached lighting handles. An observed numeric remapping is **not** sufficient proof of corresponding source asset semantics. The 1903/1903 flags result can become a source-only **diagnostic** candidate despite the unrelated four packed axes. Production draw-array approval still needs all 1903 full records with proven fields.

The map-independent engine `src/WaW360.Core/FastFiles/T4XenonStaticDrawScalarCodec.cs` and CLI `xenon-static-draw-scalar-encode` safely overlay source-owned uint8 fields onto a previously verified XModel-pointer buffer without touching model pointer words, geometry, light fields or unproven padding. An unrelated **3 x 48-byte** synthetic self-test checks field width and protection.

The Aztec-specific `Build-AztecGate10SourceFlagsCandidate.ps1` revalidates all independent source/native witnesses, checks every PC flag byte equals the observed original Xbox flag **without copying any native reference bytes**, builds the shared .NET converter incrementally, runs the generic self-test, writes all 1903 source flags into the existing 1902-pointer structure, and rechecks every non-flag byte stayed identical. It writes only a new incomplete sidecar candidate; 0 physical FF draw links; never an Xbox package. This command relies on the **updated** scalar audit, so run audit first:

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'Syntax check failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Audit-Aztec1903StaticDrawScalarEquivalence.ps1'
if ($LASTEXITCODE -ne 0) { throw 'Draw scalar source equivalence audit failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Build-AztecGate10SourceFlagsCandidate.ps1'
```

**Next source-side blocker:** interpret reflection/primary light source indices relative to the PC and Xbox light/probe tables, derive `lightingHandle` from genuine original-PC cached light data (not the original Xbox row values), explain the four rotation XORs, and prove the last stock XModel's runtime residency. Then add the fully validated 1903 x 60B array to both Stage 4D and Stage 4E in OAT child order and make the parent DPVS draw pointer FOLLOWING. Do not label Gate 10 PASS until native OAT independently loads and validates the physically linked 1903 records. The old 9/13 count remains unchanged until then.

## Gate 10 light-arithmetic hypothesis (original-PC source, not donor)

User-verified source/native comparison: **1903/1903** model names, origins, scales, cull distances and flags; **1899/1903** exactly matching packed-axis record triplets. The differing words are at draws 4, 8, 10 and 300. Individual changed decoded 10-bit components differ by approximately **0.00195695** (not a full rotation error proof). Reflection PC `1` versus Xbox `0` for all 1903. Primary light PC `0` on 28 and `1` on 1875; native light handles are `256` on 28 and `257` on 1875. The last matching frequency **does not** prove they coincide per ordinal.

`Audit-Aztec1903StaticDrawScalarEquivalence.ps1` now explicitly tests **all 1903 individual original-PC rows** for two empirical hypotheses: `Xbox reflectionProbeIndex = PC reflectionProbeIndex - 1`; `Xbox lightingHandle = 256 + PC primaryLightIndex`. It prints exact ordinal match counts and a PC primary -> native uint16 handle cross-table. No raw Xbox native body/field bytes are ever copied to output.

If BOTH are **exactly 1903/1903**, `Build-AztecGate10SourceLightCandidate.ps1` uses the original PC typed records and the existing 1903-flags / 1902-pointers candidate to run the new reusable `T4XenonStaticDrawDerivedScalarCodec` in the .NET converter. The generic codec supports uint8 and **big-endian uint16** additive scalar fields for arbitrary map sizes/record strides; an independent synthetic **2 x 52-byte** selftest guards pointer and flags bytes, overflow, and field overlap. Aztec then independently compares source-derived light values to the OAT witness and writes a new 1903 x 60-byte diagnostic candidate with reflection at `+36` and uint16 lighting handle at `+38`, while preserving flags at `+40`, geometry and XModel* pointers. It does not populate the Xbox primary light index at `+37`, resolve external stock residency, fix rotation quantization, or link into the FF.

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PS 5.1 syntax check failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Audit-Aztec1903StaticDrawScalarEquivalence.ps1'
if ($LASTEXITCODE -ne 0) { throw 'Light hypothesis audit failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Build-AztecGate10SourceLightCandidate.ps1'
```

If the light formulas fail, the script **fails closed before building any candidate** and prints exact counts. If they match, the arithmetic is still a **hypothesis for this one map**, not a fully validated general engine-light semantics rule. Xbox runtime light/probe table ownership, stable stock-model resolution, full native draw contents, and physical DPVS child-array integration remain prerequisites for Gate 10 PASS. OAT and hardware approval remain pending.

## First physical 1903-draw Gate10 OAT integration probe

User-verified **Gate10 rebase PASS**: 1902/1902 source-owned XModel root addresses regenerated from the previously verified 8509-root Stage4D layout; the entire resulting 114180-byte candidate's SHA is `2CDB6B42082EDEF00AC2EC4CE2BF429894BAFF02E1BDDA370C401383D98BC2A7`. The one stock XModel remains deliberately NULL and explicitly unverified for hardware.

The integrated `T4XenonAztecWorldGraft` now supports opt-in environment variables `WAW360_AZTEC_GATE10_SOURCE_LIGHT_REPORT` and `WAW360_AZTEC_GATE10_ROOT_RELOCATION_SUMMARY`. They enable an **OAT diagnostic only**: after existing Gate8 children, the source-derived 1903 x 60-byte static-draw array is planned/emitted as `GfxWorldDpvsStatic.smodelDrawInsts` (Xbox DPVS +84, absolute GfxWorld +788). The verified T4 order is sorted surface indices, static model instances, surfaces, optional cull groups, then static draw instances. Stage4D reserves the array; Stage4E uses new current-build stable root pointers to rebase the model fields. Without BOTH env vars, existing world/FF output behavior remains unchanged.

The shared runner `Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1 -Gate10StaticDrawProbe` requires the full original-PC Gate8 static DPVS, source child corpus, and all 28 real XModel owners. It selects **`pc_g10_1903`** for the new OAT scratch FF, not the previous passing `pc_gate8_29x_ff`. It reuses staged original-PC source proof and the latest 1903-light candidate, with no source XModel reconversion. The result manifest distinguishes `AZTEC_GATE10_1903_STATIC_DRAW_DIAGNOSTIC_OAT_PASS_STOCK_UNVERIFIED` from diagnostic blocked. An OAT LIST pass is not the independent full per-draw or Xbox hardware proof.

First launch command (after a pull):

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'Syntax preflight failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-AztecGate10RealDrawWorldProbe.ps1'
```

The new launcher checks the previous 28-model FF SHA **before and after** the test, reports the new FF's real byte size and change from the preserved baseline, and fails closed if OAT cannot load it. Do not copy the diagnostic FF to the Xbox. A full Gate10 PASS requires independent loaded draw fields including stock ownership, source-derived runtime light semantics and the four rotation discrepancies resolved. Full-map/hardware testing needs still more world and asset closure; the original smoke-test checklist stays **9/13** until then.

## Gate 10 first physical FF and independent OAT draw-record readback

**User-verified Windows pass:** `Run-AztecGate10RealDrawWorldProbe.ps1` (after all 27 PS 5.1 syntax checks) emitted a second isolated FF at `pc_g10_1903/scratch_emit/nazi_zombie_aztec.ff`. Stage4D/Stage4E integration and the first real OAT loader/list gate passed with `AZTEC_GATE10_1903_STATIC_DRAW_DIAGNOSTIC_OAT_PASS_STOCK_UNVERIFIED` and 8509 native roots. Original verified 28-body FF: **3,615,230 bytes**; new compressed FF: **3,625,073 bytes** (**+9,843 bytes**), SHA-256 **`0BB41398C4236EF33CB9D60F923E09152966D52AA623DAEFC33D731A42630564`**. The uncompressed new draw child is 1903 x 60B = 114180B. The previous FF was SHA-confirmed unchanged. These are *verified FF build facts*, not evidence of a playable map.

**Next no-FF-build validation:** `Verify-AztecGate10OatLoaded1903Draws.ps1` invokes the **already installed** specialized x86 OAT GfxWorld dumper against that exact SHA-pinned *newly generated* FF, stores the independent typed loader record data under `pc_g10_1903/oat_loaded_1903_draw_readback`, and compares all 1903 ordinal rows against the original-PC typed source and source-derived 60-byte candidate. Checks per-row model names and pointers for all 1902 owned models, the one deliberately NULL stock reference, cull, origin, scale, three packed axis words, reflection, primary-light, BE16 lighting handle, flags and zero padding. This is **not** a new OAT build or another FF emission. The 4 original-PC-vs-Xbox-reference packed-axis differences do not by themselves fail this test: the emitted source candidate's axes must read back unchanged.

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PS5.1 syntax check failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Verify-AztecGate10OatLoaded1903Draws.ps1'
```

Only on independently loaded exact 1903 rows does the script report `AZTEC_GATE10_1903_ALL_SOURCE_DRAW_FIELDS_OAT_READBACK_PASS_STOCK_UNVERIFIED`; otherwise it saves a mismatch report and fails. Preserve the passing FF and avoid repetitive builds. Runtime light/probe semantics, native reference rotation quantization, the one stock body, remaining DPVS/cell/lightmap/world children, paired main/load FF and hardware tests remain open. The original 13-step smoke-test checklist is still 9/13 until production Gate 10 conditions close.

## Gate 10 independent full OAT readback passed: four-axis and stock closure next

The user verified that the SHA-pinned generated `pc_g10_1903` FF (`0BB41398C4236EF33CB9D60F923E09152966D52AA623DAEFC33D731A42630564`) is read back successfully by the specialized OAT Xenon loader. All **1903/1903** ordinals, cull distances, origins, scales, packed axes, reflection indices, primary light indices, BE16 lighting handles, flags, and zero padding match the *PC-derived native-format candidate*; all **1902/1902** owned-model names are resolved and the one stock reference stays NULL and unresolved. Script `Verify-AztecGate10OatLoaded1903Draws.ps1` saved a `AZTEC_GATE10_1903_ALL_SOURCE_DRAW_FIELDS_OAT_READBACK_PASS_STOCK_UNVERIFIED` report. This is actual physical serialization and independent loaded-child field validation, **not** proof of original Xbox reference identity or Xbox hardware readiness.

New **read-only** `Audit-AztecGate10AxisAndStockClosure.ps1` compares the newly OAT-loaded GfxWorld draws against the ORIGINAL RETAIL XBOX OAT witness using the same pinned T4Xenon ThirdBased axis unpacker as the source preflight. It measures all component differences for the four known draw ordinals `4, 8, 10, 300` (six packed-word differences), with a conservative default maximum absolute component difference of `0.003`. This confirms or rejects a *quantization tolerance hypothesis*, never patches the converted FF with Xbox donor bytes, and cannot prove runtime transforms. It also reports the stock draw ordinal/name/root and summarizes any prior retail base-zone identity audit. No OAT/dotnet build or new FF is needed.

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PS5.1 AST parser failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Audit-AztecGate10AxisAndStockClosure.ps1'
```

If that output reports the retail stock body audit missing/unproven, separately use the EXISTING `Audit-AztecRuntimeLoadedStockXModelReferences.ps1` on the locally owned retail `common.ff`, `code_post_gfx.ff`, `patch.ff`. It reuses prior successful OAT list caches and does **not** rebuild the generated FF; if its default `-XboxGameRoot` differs from the actual retail folder, supply a verified path. Even an exact body name in a retail preloaded zone remains **identity evidence only**, not runtime hardware residency, so a diagnostic NULL pointer cannot be promoted to a valid hardware draw. The 13-step smoke-test checklist remains at **9/13** until every Gate10 release condition (stock residency/semantics and other relevant closure) is met.

## Gate 10 axis delta and stock XModel audit — verified Windows result

Windows PowerShell 5.1 AST gate **29/29 PASS**. The read-only original Xbox comparison of the independently OAT-loaded 1903 PC-derived native draw records found **1899/1903 bitwise exact packed placements** and **exactly six differing packed words at draw ordinals 4, 8, 10 and 300**. Largest decoded T4Xenon component delta was **0.0019569695**, smaller than the explicitly tested tolerance **0.003**. This is a tolerance diagnosis, not bitwise equality, and no original Xbox donor bytes were emitted.

The single stock reference is **`,viewmodel_default`**, draw **#27**, native Xbox root **7506**, intentionally NULL in the diagnostic FF. The earlier retail audit reported `AZTEC_SOURCE_MODEL_STOCK_REFERENCE_INDIVIDUAL_BASE_ZONE_IDENTITY_MATCH_CENSUS_INCOMPLETE`: by its own definitions, an exact body-name/root match exists in at least one individually OAT-verified retail base zone, but not all expected `common.ff`, `code_post_gfx.ff`, `patch.ff` listings are complete. That is **not** a runtime residency proof.

The updated `Audit-AztecGate10AxisAndStockClosure.ps1` now validates the stock name and native root against the source relocation plan, and reports **independent base-zone identity match** separately from **full expected-zone census**, plus exactly which zones lack a valid OAT listing. Run only this fast read-only script after `git pull --ff-only`; repeating an FF build, OAT extraction or entire 29-script syntax gate is unnecessary for this follow-up. If a base zone is missing, use the existing stock audit with the correct local Xbox root to identify it rather than importing any Xbox reference body into the generated FF. Even complete offline base-zone listings cannot certify that CodXe/TU7 actually loads and resolves the stock pointer on console.

## 2026-10-30 — Gate 10 tracker closure from actual independent draw FF readback

**Definition of gate 10 in the existing 13-gate `Show-AztecFirstXboxFFProgress.ps1`:** `Physically link all 1,903 static-model GfxWorld draw records`. This is an **intermediate development-evidence gate**, *not* production completeness/hardware admission. The tracker was stale: it only checked `pc_gate8_29x_ff` (the pre-draw 28-model FF), so its 9/13 tally did not reflect the already-verified later FF at `pc_g10_1903`.

Windows evidence now independently includes (a) real Stage4D/Stage4E source-GfxWorld `smodelDrawInsts` serialization, OAT loading and 8509 root order, and (b) a **second** OAT loaded typed record dump against the SHA-locked 3,625,073 B FF. Loaded 1903/1903 counts and exact PC-derived values for transforms, packed axes, reflection, primary light, BE16 handle, flags and padding; 1902/1902 model names; 1 deliberately NULL, externally referenced stock XModel `,viewmodel_default` at draw 27/root 7506. Retail base-zone identity found in `common.ff`, but `patch.ff` was absent from local library and hardware preload is not proven. Largest measured original Xbox axis component delta was 0.0019569695 (4 draw ordinals, 6 words) within the tested 0.003 tolerance. Do not copy reference/oracle bytes into output or auto-replace the null pointer.

The progress tracker **now** evaluates Gate10 against the actual `pc_g10_1903` OAT FF report **and** the independent typed OAT readback report. It fails closed if the reports/flags are wrong, per-field counts are not exact, the source-PC draw digest mismatches, the emitted FF SHA doesn't match the readback report, the native dump SHA doesn't match the readback report, or any of the 1902 source-owned names or one stock NULL cases fail. The older FF is preserved. Only when *all* evidence validates does milestone 10 advance to PASS, **10/13 development gates**. Gates 11–13 remain PENDING: complete DPVS/cells/lightmaps/render/runtime child closure, valid main/load native pair and full dependency audit, and controlled hardware-admission release. Production approval is still FALSE even with 10/13.

Run the existing fast tracker (no FF/OAT rebuild or Xbox):

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PS5.1 syntax gate failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Show-AztecFirstXboxFFProgress.ps1'
```

Expect **10/13 only if** the user's on-disk verified FF and OAT witness manifests still pass all hash, file and source conditions. Never interpret this 10/13 tally as 77% of full conversion or a deployable Xbox map.

## October 30 — first Gate11 source-DPVS child census without rebuilding

The **13-gate development tracker has now been run and passed 10/13 on Windows** after user pull, with 30/30 PS5.1 script syntax checks and source-owned Gate10 1903-draw OAT readback (FF `3,625,073` bytes) confirmed. Gate 10 is the physical static-draw link/independent OAT witness milestone; Gate 11/12/13 remain legitimately pending. Do not change the existing Gate10 FF or its SHA-locked manifest just to explore missing GfxWorld children.

Source-backed **new script** `Audit-AztecGate11DpvsChildFrontier.ps1` inspects the original-PC SHA-pinned 100B GfxWorldDpvsStatic object and already-produced native/PC ABI, FF/OAT manifests. It has **no OAT, .NET, or FF build step**. It inventories all 14 pointer slots (offsets +40 through +92), classifies the nine `XFILE_BLOCK_RUNTIME` slots separately from five pointer-bearing serialized children, asserts nonzero source pointers for the four already verified serialized children (`sortedSurfIndex`, `smodelInsts`, `surfaces`, `smodelDrawInsts`), reports any source-present but unlinked `cullGroups` pointer at +80, and selects a next source-child proof objective. The source count prefix 1903 smodels / 613 static surfaces / 60 smodelVisDataCount / 20 surfaceVisDataCount and 11 cells must match OAT. T4 asset dependency layout comes from OpenAssetTools `src/ZoneCode/Game/T4/XAssets/GfxWorld.txt`, and source/static source-pointer bytes remain original-PC-owned; no runtime or donor fields are blindly copied.

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Audit-AztecGate11DpvsChildFrontier.ps1'
```

Expected report path: `output/second_map_baselines/nazi_zombie_aztec_V2.2/native_material_mapping/pc_g10_1903/gate11_dpvs_child_frontier/aztec_gate11_source_dpvs_100b_child_frontier.json`. This will **not** award Gate 11 automatically. If `cullGroups` is genuinely source-present, next work is to prove original-PC `GfxWorld.cullGroupCount` and typed `GfxCullGroup` records, then implement current-layout source-derived Stage4D/4E emission and independently dump/read back. Otherwise prioritize 11-cell/portal/AABB, lighting/lightmaps, reflection, sky, dynamic DPVS and other remaining renderer/world dependencies. Runtime-marked OAT fields must receive correct engine allocation semantics, not copied PC pointers. Continue with one actual child milestone at a time, only rebuilding after a new real child is added.

## October 30 — Gate11 source DPVS outcome and one-build 11-GfxCell topology witness

Windows result for `Audit-AztecGate11DpvsChildFrontier.ps1`: verified the source DPVS 100B and SHA-pinned independently OAT-loaded Gate10 FF; `sortedSurfIndex`, `smodelInsts`, `surfaces` and `smodelDrawInsts` are physically OAT-loaded, `cullGroups` at source DPVS +80 is genuinely NULL. Runtime-marked source pointer fields (+40 to +64 and +88/+92) contain `0xFFFFFFFF` zone FOLLOWING placeholders and are **not** to be copied as machine addresses or declared as production child payloads. Source T4 world cell count is **11**. Gate 11 remains pending; **10/13 development gates verified**, not 77% complete map.

Next is `Run-AztecGate11CellTopologySourceWitness.ps1`, which adds a **two-sided typed original-PC and native Xbox ABI witness** to the existing pinned specialized OAT graph-dumper. The original PC `GfxWorldMaterialCorpusDumperT4.cpp` now outputs per-cell AABB/portal/cull/reflection counts, nested pointer-presence flags and source `GfxPortal.cell` target **cell ordinal** for all 11 cells—not process pointer bytes. The native `GfxWorldSurfaceAbiWitnessT4Xenon.cpp` records exact compiled Xbox `sizeof`/`offsetof` for `GfxCell`, `GfxAabbTree`, `GfxPortal`, GfxWorld::cells and the child fields. One pinned OAT rebuild installs both capabilities together; subsequent runs reuse the installed binary. This is a data/ABI probe only; it does **not** write 11 native GfxCells or their nested AABB/portal arrays yet.

First run after pulling (one *specialized OAT* build; **no converter/.NET or GfxWorld FF build**):

```powershell
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PowerShell 5.1 AST preflight failed; do not start native OAT compilation' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-AztecGate11CellTopologySourceWitness.ps1' -BuildDumper
```

The runner SHA-checks the original-PC FF, read-only native Xbox FF and unchanged Gate10 FF/OAT witness, then invokes the specialized x86 OAT dumper on the original-PC and reference Xbox **without exporting any Xbox donor body bytes**. It writes `pc_g10_1903/gate11_cell_topology_source/aztec_gate11_pc_source_cells_native_abi.json`, including source cell/AABB/portal totals, measured structure sizes, pointer-presence/count validation, cross-cell target ordinal coverage, and independent PC/Xbox ABI comparison. It fails closed on mismatches. **No other FF should be rebuilt**, and a probe PASS is not Gate11 physical completion. After inspecting the counts, integrate genuinely source-derived 11 GfxCells and each required owned nested child into isolated Stage4D/4E OAT FF, independently read back, and only then consider the rest of GfxWorld (lightgrid, lightmaps, reflection and dynamic DPVS).


## Gate 11: complete pointer-free 740-AABB original-PC source exporter (next)

**Verified on Windows**: previous 11-cell OAT source/native ABI witness PASSED with 11 original-PC GfxCells, **740 AABB trees, zero portals, zero per-cell cull references**, and no unresolved portal targets. Original-PC/Xbox structure sizes match: GfxCell 56/56 bytes, GfxPortal 68/68, and GfxAabbTree 40/40. The previous specialized OAT binary was built successfully, and the existing Gate10 FF has not changed. The 13-gate development tracker remains **10/13**; no cells or trees have yet been emitted in the FF.

The source OAT dumper now adds a distinct, pointer-free typed graph export containing all 11 cell bounds, each of the 740 AABB bounds/child/surface fields, every owned nested static-model-index child list and every per-cell reflection-probe index. The Xbox OAT struct witness measures all corresponding scalar and pointer field offsets in the pinned native 56B/40B ABIs. No Xbox reference payloads, raw PC host pointers or oracle bytes are copied.

The existing Run-AztecGate11CellTopologySourceWitness.ps1 now requires two *new* installed capability markers (typed AABB source graph and full Xenon cell/AABB field-offset ABI); even though its earlier topology-only version passed, the installed dumper must be rebuilt ONCE to add the actual records. The updated script verifies source/donor hashes, 11 cell / 740 AABB ordinals, source-model indexes in range, and all native field offsets. It preserves a SHA-256-pinned original-PC typed JSON source file under pc_g10_1903/gate11_cell_topology_source/original_pc_oat_loaded_11_cells_740_aabb_typed.json, and records its SHA in aztec_gate11_pc_source_cells_native_abi.json. This is a **source-only export**, not a new converted FF.

PowerShell 5.1 command sequence: pull the branch, run Test-WaWMultiMapScriptSyntax.ps1, then run Run-AztecGate11CellTopologySourceWitness.ps1 with -BuildDumper. This builds only the specialized x86 OAT dumper and executes two OAT source/ABI dumps; **no map FF, .NET converter build or Xbox deployment**.

Only after inspecting the typed source data and proving all children are safe should an opt-in isolated Stage4D/Stage4E emitter link GfxWorld::cells, the 11 native cells, 740 trees and their model-index/probe-index child arrays. Independently read those back through OAT before moving to reflection images, lightmaps, light-grid or dynamic visibility closure. Do not advance Gate11/13 or authorize hardware simply because this source exporter passes.


## Gate11 real physical source-derived GfxCells/AABB — first opt-in FF and independent OAT readback

**Actual Windows proof to date:** source-only full cell corpus PASS: 11 GfxCells, 740 AABB trees, 14,974 nested static-model indices, 11 reflection-probe index bytes, 0 portals, 0 cell-cull refs, 0 unresolved portal destinations; original-PC/Xbox structure sizes match 56B/40B/68B. No cells/trees were yet written into the diagnostic FF. Gate10 source draw FF remains independently OAT-proven, 10/13 development milestones PASS.

NEW source-only cell FF path is opt-in and fully isolated:
- C# T4XenonAztecGate11CellProbe.cs consumes a SHA-locked *original-PC typed JSON cell/tree graph*, native 900B/56B/40B Xbox ABI offsets and the already-verified original Gate10 FF provenance. It reconstructs 11 native 56B GfxCell bodies and 740 native 40B GfxAabbTree bodies from PC-owned scalar data; swaps floats, integer counts and all 14,974 16-bit smodel indexes to big endian; carries only the 11 PC-owned reflection-probe index bytes. All newly constructed pointers use current-zone FOLLOWING fixups; source process pointers and original Xbox reference body bytes are excluded. Source-proven empty portals/cull children remain NULL.
- Stage4D/Stage4E T4XenonAztecWorldGraft.cs now accepts opt-in WAW360_AZTEC_GATE11_CELL_GRAPH_REPORT. GfxWorld::cells is allocated after original-PC dpvsPlanes children and BEFORE the reordered materialMemory/vertex data. Per-cell AABB tree arrays, their nested smodel indexes, and per-cell reflection bytes follow each cell. New C# FF result telemetry records cell/tree/index/probe counts; all earlier builds skip this code entirely unless opt-in is provided.
- Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1 accepts -Gate11CellProbe only WITH full Gate8+Gate10 source child flags and writes to NEW pc_g11_cells_740, never overwriting pc_g10_1903. Hardware and full-world closure stay FALSE. All original 1903 draw proof conditions continue to apply.
- Specialized pinned Xenon OAT GfxWorldSurfaceAbiWitnessT4Xenon.cpp now independently dumps the LOADED 11 native cells, each of 740 loaded trees, nested model indexes and reflection index bytes. Verify-AztecGate11OatLoadedCells.ps1 opens the new FF with OAT independently and checks each field against the PC source, with baseline/input/output hashes and exact count checks. Only a real positive SECOND OAT readback grants the physical-cell subproof.
- Run-AztecGate11PhysicalCellWorldProbe.ps1 combines the new isolated map FF emitter and the independent typed OAT witness, enforces SHA preservation of existing passing Gate10 FF, and writes progress/size/verification output. FIRST run needs ONE specialized x86 OAT rebuild because a new native loaded-cell witness was added, plus ONE map FF build; later runs reuse the native OAT. No Xbox deployment.

First untested Windows command after git pull, from the PC2XB3 repo root:

    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1"
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Run-AztecGate11PhysicalCellWorldProbe.ps1" -BuildDumper

If first OAT load fails, inspect output/second_map_baselines/nazi_zombie_aztec_V2.2/native_material_mapping/pc_g11_cells_740/integrated_emitter.log and the new FF OAT hard-gate log. The old FF remains unchanged. Even if all 11 cells and 740 trees independently load, Gate11 OVERALL remains PENDING until lighting, reflection-image, remaining GfxWorld child and dynamic visibility/runtime closure are proven. This first physical Gate11 build and readback have NOT yet been run on Windows.

## Gate11 FIRST real FF result — OAT physical block offset fault (do not recompile blindly)

Windows actual test (2026-10-31) ran from `e4a507e`: WinPS5.1 preflight **34/34 PASS**, pinned specialized OAT graph dumper build **PASS**, .NET Stage4E incremental build **PASS**, isolated `pc_g11_cells_740/scratch_emit/nazi_zombie_aztec.ff` physically emitted **3,642,925 bytes**, and builder reported **11 cells, 740 AABB trees, 14,974 model-index children**. But the *first native OAT load rejected this FF*, with the exact error:

    Zone referenced offset 86484991 of block T4Xenon::XFILE_BLOCK_PHYSICAL which is larger than its size 559280

Source FF zone ~11,190,286 bytes; actual emitter reported PHYSICAL `0x888B0` (= 559,280 bytes), VIRTUAL `0x82B158`. OAT loaded **0/8509 roots**. OAT first-loader outcome `AZTEC_GATE11_11_CELL_740_AABB_DIAGNOSTIC_OAT_LOAD_BLOCKED`. This proves the new FF is **invalid** even though `layoutReplayExact=True` for the converter's own planner; the latter is necessary but not an independent native pointer proof. Gate 10's previously verified FF remains unchanged. Gate11 stays pending; no Xbox deployment. **Do not grow the PHYSICAL block merely to satisfy an absurd out-of-range decoded pointer.**

The follow-up diagnostic `Audit-AztecGate11FailedFfPointerLayout.ps1` is read-only and operates on the **already failed** FF. It uses existing Release .NET CLI with `--no-build` to decompress the failed FF to a disposable local temp file; scans for the raw packed candidate corresponding to OAT's invalid 86484991 offset; locates the native 11×56B cell header array by the first original-PC cell's source-owned bounds; checks the contiguous 11 cell headers and the emitted 740×40B tree records, 14,974 uint16 source-model indices and 11 reflection-probe indices in their expected physical serialized sequence. It reports any first mismatch and pointer-word occurrence in `pc_g11_cells_740/gate11_failed_ff_pointer_layout_audit.json`, then deletes only the temp decompressed zone. **No .NET build, no FF rebuild, no OAT call, no Xbox work.**

To run from the PC2XB3 repo root:

    git pull --ff-only
    powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
    if ($LASTEXITCODE -ne 0) { throw 'PowerShell syntax gate failed' }
    powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Audit-AztecGate11FailedFfPointerLayout.ps1'

Interpretation: if the byte scan finds a mismatch, correct the recorded serialized stream order/offset at that earliest field before ONE new isolated OAT FF test; if the source-vs-FF sequence matches, look at pinned T4Xenon `GfxWorld.txt` **loader traversal order** (`GfxCell`, `GfxAabbTree`, reusable `smodelIndexes`) and identify the nested pointer field whose position in the file differs from native loader expectations. Do not repeat the full FF/OAT native build until this no-build provenance evidence narrows the cause.

## Gate11 existing failed FF: 100% serialized data PASS, native OAT pointer fault remains

User's 2026-10-31 Windows no-build forensic after fixing WinPS5.1 line 122: **35/35 script syntax PASS**, decompression of **existing** rejected `pc_g11_cells_740/scratch_emit/nazi_zombie_aztec.ff` PASS, original source/header witness SHA check PASS, decoded zone bytes **11,190,286**. Offline source-vs-emitted stream walk located **1/1 matching 11×56B GfxCell header array**, **740/740 40B GfxAabbTree records**, **14,974/14,974 big-endian 16-bit static-model indexes**, and **11/11 reflection-probe indices**, with **no first mismatch**. A literal byte search for the raw packed pointer candidate `0x0527A800` found **zero** occurrences. OAT's actual exception remains: `Zone referenced offset 86484991 of block T4Xenon::XFILE_BLOCK_PHYSICAL which is larger than its size 559280`. This does **not** mean the OAT validator passed; it distinguishes correct emitted source content under the serializer's byte order from native loader traversal/pointer interpretation.

**Next native boundary evidence, NOT ANOTHER MAP FF BUILD:** the existing `Build-OatXenonGraphDumper.ps1 -TraceNativeAssetLoader` diagnostic mode has been extended, guarded against all **five** throw sites in the pinned OAT `ZoneInputStream.cpp`, to print `[AZTEC-OAT-BADPTR]` with actual raw pointer, target block and offset, stream position, current block index/cursor. Its existing `[AZTEC-OAT-FAULT]` wrapper additionally records first faulting Xbox XAsset root index/type. All added native source patches are temporarily applied for a special x86 OAT build and restored to a clean pinned checkout afterward. Capability `t4xenon-pointer-stream-fault-trace-v1` distinguishes the new binary.

`Run-AztecGate11ExistingFfOatPointerTrace.ps1` SHA-checks the already rejected FF and the preserved Gate10 baseline against the independent readback reports; requires the offline matching 740-tree/14,974-index source audit; builds pinned diagnostic OAT **once** if `-BuildTrace` is supplied and the two trace capabilities are absent; then invokes `--list` on the **same failed FF** and captures native bad-pointer and asset-fault messages without editing any FF. Evidence is saved to `pc_g11_cells_740/oat_first_bad_pointer_trace/aztec_gate11_native_oat_first_bad_pointer.json` and `native_oat_first_bad_pointer.log`. An OAT failure is **expected** here and the script records context without promoting any conversion gate. Full GfxWorld/hardware status remains pending.

Commands from converter repo root (one native OAT diagnostic build, **zero map FF/.NET build**):

    git pull --ff-only
    powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
    if ($LASTEXITCODE -ne 0) { throw 'WinPS5.1 syntax preflight failed' }
    powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-AztecGate11ExistingFfOatPointerTrace.ps1' -BuildTrace

Use the traced native `streamPos`, `currentBlock/currentCursor`, `rootIndex`, and *actual* raw word to correct the first wrong field or child traversal. Do not artificially enlarge the PHYSICAL block, mark Gate11 complete, or rebuild/re-extract the 740-tree source. The passing Gate10 FF is unchanged.
