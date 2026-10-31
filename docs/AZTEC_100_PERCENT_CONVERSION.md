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
