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
