# PC2XB3 — World at War PC → Xbox 360 Map Converter

## fuck you 0x82168594

PC2XB3 is a work-in-progress converter/linker for **Call of Duty: World at War** custom PC maps targeting **modified Xbox 360 consoles**.

The project does **not** distribute retail World at War game files or copyrighted map content. Users provide their own game data and custom-map sources.

## Latest verified Aztec native-graph milestone — October 28, 2026

The second-map `nazi_zombie_aztec_V2.2` investigation has a verified **reference-only** Xbox 360 TechniqueSet/Material graph foundation. The known-working Xbox `nazi_zombie_aztec.ff` was independently matched to the pinned SHA-256 `e0f3c244dd7b72d0fe71c05faf014c2c9bfb05f770183954b3e7cde625eba98f`. This reference is locally supplied by the developer and never redistributed.

- **PC identity census:** 373/373 physical TechniqueSet roots verified (main 240, patch 1, mod 132).
- **Cross-platform identity reconciliation:** 58 PC root entries have native names backed by shared-material evidence (12 main entries rely on *cross-source* material evidence), 26 further entries match native names but lack shared-material proof, 2 material-referenced root entries remain unresolved, 287 roots were not observed in the currently exported Material references. **Do not interpret unobserved as globally unused.** Zero conflicting native mappings detected.
- **Native fixed-up TechniqueSet corpus:** 70/70 distinct graph identities, 1,216 non-null technique slots and passes, 379 distinct vertex shaders, 436/436 distinct pixel shaders, 1,628 referenced shader blob files, **1,410,348 verified shader blob bytes**. The existing typed Xenon graph importer returned `verified=True` / exit 0.
- **Windows OAT file export:** 3,928 historical `Failed to open file` errors disappeared after switching from a deep output path (and an ineffective `SUBST` alias) to a genuinely short physical temp staging path. Final test produced `0 warnings, 0 errors` and verified transfer of 1,699 files / 4,589,622 bytes into the one ignored repository diagnostic folder.
- **Boundary:** This proves extraction/validation of a **known-working Xbox-native oracle**, not that PC shaders have been translated into Xbox shader bytecode. The reference's shaders, FFs, audio and other game payloads are not converter distribution assets. No source-built, complete Aztec Xbox candidate has been authorized, and no real-hardware Aztec conversion pass has occurred.
- **Latest renderer pre-emission gate (October 28):** Existing Stage 4B native graph serialization plan **PASS**, Stage 4C exact native root binding **70/70 TechniqueSets and 436/436 PixelShaders PASS**, and Stage 4D Xbox TEMP/VIRTUAL/PHYSICAL layout **PASS**. The previously unsupported Aztec `stringtable` at native root #1319 was addressed with the pinned 16-byte T4 Xenon ABI (name pointer, column count, row count, values pointer). That is currently an **empty name-only StringTable shell**, not actual PC CSV contents. Cross-file PC TechniqueSet graph target coverage: **84/86**, including 26 exact-name-only candidates; two material-referenced roots remain unresolved.
- **Stage 4E real emitter first run (Oct 28):** Wrote a **350,723-byte scratch FF** containing 8,509 native-reference root identities, 70 TechniqueSets and 436 PixelShaders; **`layoutReplayExact=True`**, VIRTUAL `0x153AB6`, PHYSICAL `0x888B0`. The independent OAT loader **failed**, reporting `Zone tried to reference invalid block 7` (`0 warnings, 2 errors`). This historical attempt is **not a passing FF**, a PC-source conversion, or an Xbox test candidate. A subsequent instrumented trace identified the first faulting root, as documented below. The vast majority of nonrenderer assets remain name-only shells, which may be structurally incomplete for OAT loading.
- **Targeted OAT first-exception provenance (Oct 28):** Instead of another 8,003-root census, the pinned OAT graph-dumper builder accepts opt-in `-TraceNativeAssetLoader`. It modifies the pinned T4 Xenon loader **only during a temporary diagnostic build** so a thrown exception prints `[AZTEC-OAT-FAULT] stage=asset rootIndex=N typeId=T` (or `stage=scriptstrings` if loading failed before the asset table). The OAT pinned source is restored after compilation. Run `tools/oat-xenon/Run-SecondMapAztecOatFirstFault.ps1 -BuildTraceOat` to explicitly authorize that OAT rebuild, independently load the known-working golden Xbox FF as a control, then trace the **existing** 4E scratch FF. The report captures the first faulting root index/name/type but does not assert which individual field caused the exception. All reports remain in the current `4e_validation` scratch directory; no FF re-emission, Xbox testing or release promotion.
- **StringTable block-7 root cause identified and fixed (Oct 28):** The opt-in OAT loader trace passed the golden native control (exit 0) and pinpointed its first exception at **native root #1319, `stringtable:mp/zombiemode.csv` (type ID 34)**. Inspection of pinned `src/ZoneCode/Game/T4Xenon/XAssets/StringTable.txt` and `ZoneLoadTemplate.cpp` shows `StringTable` lacks `set block XFILE_BLOCK_TEMP`; generated loader accepts **FOLLOWING (`0xFFFFFFFF`)**, not **INSERT (`0xFFFFFFFE`)** for this top-level root. The historical emitter used INSERT for all roots, so a StringTable INSERT was incorrectly treated as an offset and decoded as invalid block 7. Stage 4D planner and 4E emitter now allocate its 16-byte shell **directly in VIRTUAL, with no 4-byte VIRTUAL insert slot**, and emit a FOLLOWING root header. Other sampled T4 Xenon asset ZoneCode root types declare TEMP. Stage 4E runner now incrementally builds the corrected .NET CLI before testing (no OAT build/redump), and its independent OAT gate remains mandatory. **Fix is committed, not yet Windows verified; no hardware claim or release promotion**. The table remains an empty validation-only shell; actual PC `mp/zombiemode.csv` data still needs legitimate source-backed serialization.
- **4E failure audit:** `tools/oat-xenon/Run-SecondMapAztecStage4EFailureAudit.ps1` reuses prior JSON/OAT logs to enumerate all unconverted nonrenderer shells and record the unknown block-7 provenance in one existing scratch directory. This does not rewrite the FF or bypass the OAT gate. Avoid blindly rewriting a block-7 pointer to block 6; the error may represent a misread due to an incomplete asset body.
- **Next Stage 4E test:** `tools/oat-xenon/Run-SecondMapAztecStage4E.ps1` invokes the existing `xenon-graph-emit` path for **native-reference validation only**: 4D recheck, byte emission, zone parser, Xbox FF wrapper round-trip, and independent OAT root/zero-errors gate. It writes one scratch `nazi_zombie_aztec.ff` under `output/second_map_baselines/nazi_zombie_aztec_V2.2/native_material_mapping/renderer_preflight/4e_validation/`. This artifact is not a PC conversion and **must not be deployed or copied to Converted Maps**.
- **Stage 4E native-oracle byte-emission milestone (Oct 28): PASS.** The corrected 16-byte StringTable VIRTUAL/FOLLOWING fix produced a **350,714-byte validation-only Xbox fastfile**. Stage 4D/4E layout replay was exact, and Xenon OAT loaded **8,509/8,509 assets** in the expected native sequence with **0 warnings, 0 errors**. Renderer roots: 70 TechniqueSets, 436 PixelShaders. The reference-only graph and **8,003 name shells** make this a structural PASS, *not a playable PC-converted map*. The original PASS file is retained under `renderer_preflight/4e_validation/`; do not overwrite it with unverified grafts or put it on hardware.
- **Aztec PC RawFile first hardware-independent run (Oct 28):** PC OAT extraction succeeded and found **40** matching names in `map.ff`, **55** in `map_patch.ff`, and **43** in `mod.ff`. The source-aware reconciliation identified **114/120 distinct native Xbox RawFile roots backed by PC data**, with **five cross-FF source-byte conflicts excluded** (one further unmatched/other exclusion inferred from the 120 total). The test stopped before payload emission with `Xenon image graph root not found: <repo>\\0`: **Windows PowerShell 5.1 dropped empty optional CLI arguments**, so `0` shifted into the image-graph path slot. This is an invocation bug, not evidence of invalid PC RawFile payloads. The CLI now has an explicit `xenon-graph-emit ... --rawfile-graft <manifest.json>` mode with no empty placeholders, and the runner supports `-ReusePcCorpus` to reuse the prior staged extraction only after validating its manifest, SHA-256 hashes and OAT extraction logs. **First fixed-CLI run is pending; 0 Xbox RawFile SHA readbacks confirmed so far.** No output is hardware-authorized.
- **Aztec PC-owned RawFile graft newly implemented, pending first Windows test.** `tools/oat-xenon/Run-SecondMapAztecPcRawFileGraft.ps1` uses the verified second-map PC baseline, extracts `rawfile` assets directly from the actual PC `map.ff`, `map_patch.ff`, and `mod.ff` with the PC OAT Unlinker in *short physical temp folders*, intersects names with the 120 native Xbox RawFile roots, excludes conflicting cross-FF bytes, and writes SHA-256-verified source-bound graft entries. A new Aztec manifest branch in `T4XenonRawFileProductionGraft.cs` preserves the existing FRUmansion eight-asset contract, rejects native/oracle payload substitution, and checks origin FF SHA/length on load. The script then runs the real `xenon-graph-emit` with ONLY PC RawFile bodies promoted; it requires 8,509-root OAT validation, exact graph layout replay, and **exact Xbox readback SHA/length for every grafted PC RawFile**. Results are kept in a fixed `pc_rawfile_graft/` scratch folder; **not for Xbox**, since other asset families remain shells. PC script binary compatibility is not established by byte readback.
- **Aztec PC RawFile byte graft/independent Xbox readback (Oct 28): PASS.** On branch `xbox360/oat-xenon-integration` (commit `7c541b2`) the user reused a SHA-pinned PC extraction corpus, promoted **114/120 native RawFile root names from the actual PC `map.ff`, `map_patch.ff`, and `mod.ff`**, and got **114/114 exact SHA-256 Xbox OAT readbacks**, with no discrepancies. Five cross-FF source-name conflicts remain deliberately excluded, and six native RawFile roots lack a promoted unambiguous PC body. This establishes real source-backed RawFile serialization, **not Xbox script execution compatibility**. The resulting Xbox FF remains diagnostic: zero real image, material and XModel payloads, and no hardware/release approval.
- **Next source-graphics gate added (pending Windows run):** `tools/oat-xenon/Run-SecondMapAztecPcImageCorpus.ps1` reuses the existing `linker-v2-iwi-image-corpus` analyzer against the verified Aztec baseline and **666 native Xbox image roots**, matching source IWD/IWI names and reporting source IWI version, format, byte mass, duplication and missing names. It validates the preceding 114/114 RawFile proof and writes only a new report in ignored `output/second_map_baselines/nazi_zombie_aztec_V2.2/`. A source IWI match is **identity evidence**, not completed pixel conversion; FF image assets and shared/retail texture sources may need separate follow-on recovery. This runner never emits or promotes an FF.
- **Aztec PC IWD/IWI image census (Oct 28): PASS.** The map archive contained **2 source IWI files, both matching 2 of 666 native Xbox image identities** (IWI v6/format `0x0D`, 1,398,184 source bytes). This establishes only that **2/666 images are sourced from the scanned map IWD archives**, *not* that 664 native image payloads are missing overall. The largest remaining source families to inventory are embedded PC `map.ff`, `map_patch.ff`, `mod.ff` images and common/stock assets.
- **Aztec PC fastfile image extraction first attempt (Oct 28): PARTIAL, reporting crash.** Cached OAT `--list` inventory found **686 distinct PC FF image names** and **647/666 native Xbox image-root name matches** (562 main, 3 patch, 243 mod before cross-source de-duplication). The attempted PC image export produced **9 IWI files from `map.ff`**, **0 from `map_patch.ff`**, and **2 from `mod.ff`**, while logging **531, 3, and 241 ERROR lines**, respectively, despite OAT process exit code 0 for each. Windows PowerShell 5.1 then threw `Argument types do not match` when processing generic list values, before writing the source-census report. This is a **reporting failure plus incomplete image extraction**, not proof of a PC/Xbox pixel conversion. The runner now explicitly materializes generic-list arrays and supports `-ReusePartialDump`: reuse the previously staged 11 diagnostic IWI candidates and their OAT logs with source FF/hash rechecks, then write an error-category breakdown **without rerunning OAT**, including the crucial distinction between image root names and accessible pixel payloads. Cached IWI provenance is *diagnostic*, not independently proven reproducible; no image graft or Xbox release is authorized.
- **Next fastfile image-source gate (pending Windows run):** `tools/oat-xenon/Run-SecondMapAztecPcFastFileImages.ps1` compares cached OAT `--list` evidence from all three verified PC FFs with the **666 native image identities**, using only names and source manifests. With `-InventoryOnly` it avoids all new OAT extraction; without that switch it extracts PC-source `image` data to IWI in short physical temp roots, stages it under ignored `pc_fastfile_image_sources/pc_fastfile_image_corpus/`, hashes PC outputs, detects conflicting cross-FF variants, and merges its unique root-name matches with the two known IWD image sources. It produces **reports and a source-only IWI corpus**, no Xbox FF. **No image pixels or native shader graph have been converted from the PC image inputs yet.** Both the native 4E and 114/114 RawFile OAT PASS artifacts stay untouched.
- **Aztec fastfile image OAT blocker conclusively classified (Oct 28):** The `-ReusePartialDump` recovery succeeded. Of **775 image-export error lines across map.ff (531), map_patch.ff (3), and mod.ff (241), all 775 are `Could not find data for image`**, rather than confirmed unsupported codec errors. Cached extraction retained **11 diagnostic IWI files, only 4 unambiguous native image-root payload identities**; original name-list matching remains **647/666**. The PC `.ff` image assets carry identities without all pixel data accessible to the current OAT search paths. Sample names include stock-like marine, weapons and Berlin textures, plus generated-looking composite names; those are source-ownership hypotheses, not proof of stock availability or correct runtime residency.
- **Aztec 8-zone retail stock image first run (Oct 28):** OAT `--list` succeeded (exit 0) on **eight core/MP/zombie/UI zones**, and the original PC source logs resolve to **775 missing-pixel errors across 654 distinct image names**. The per-zone image-name hit counts were **1** (`code_post_gfx.ff`), **52** (`code_post_gfx_mp.ff`), **74** (`common.ff`), **148** (`common_mp.ff`), and **195** (`nazi_zombie_prototype.ff`); other scanned zones had zero target-name hits. Those hits overlap; **do not add them up as unique resolved names**. After the final zone the script crashed in report collation with PowerShell StrictMode `The property 'Count' cannot be found on this object`; likely cause was zero-match `$zonesForKey` being unwrapped to `$null`. The collation now uses an explicit `[string[]]` empty array and the runner supports **`-CacheOnly`**, requiring successful, metadata-matched OAT list logs for all selected zones; it refuses to rescan when cache is missing. This should yield distinct resolved-vs-unresolved names in a report without rerunning eight OAT scans. No retail image data was copied or injected.
- **Next Xbox stock identity gate (pending Windows run):** `tools/oat-xenon/Run-SecondMapAztecXboxStockImageClosure.ps1` parses the original 775 OAT missing-data errors, de-duplicates image names, and checks them **by identity only** against the user's existing **Xbox retail common/UI/zombie-zone .ff files** with an OAT `--list` pass. Logs are cached by source size/mtime, and `-AllRetailZones` expands the search only if necessary. Reports show which missing PC pixels may already exist as Xbox assets; there is **no native pixel copying, donor graft, FF emission, runtime-residency proof or release promotion**. Exact image-graph dependencies and stock references still need separate verification.
- **Aztec core/Zombies retail image identity audit PASS (Oct 28):** Retried with `-CacheOnly` and verified **all 8 Xbox retail zone OAT lists** at exit 0. Across 775 missing PC pixel error occurrences, the script identified **654 distinct missing-image names**. **302/654 distinct names appeared in the 8 tested retail zones** (46.2% exact-name identity matches); **352/654 remained unmatched by that scope**. Individual zone matches (195 in `nazi_zombie_prototype.ff`, 148 in `common_mp.ff`, 74 in `common.ff`, 52 in `code_post_gfx_mp.ff`, 1 in `code_post_gfx.ff`) overlap and cannot be summed into a distinct total. These matches establish **stock dependency candidates**, *not* pixel equivalence, runtime residency, conversion success or permission to redistribute stock payloads.
- **Next full-retail identity expansion (pending Windows):** `Run-SecondMapAztecXboxStockImageClosure.ps1 -AllRetailZones` extends the **read-only** OAT `--list` scan across other locally present Xbox retail FFs. Caching is keyed to canonical source paths and validates size, UTC modification time and successful OAT exit; it reuses the eight previously successful logs even when wider sorting changes scan ordinals. The broad result is saved as `pc_external_image_xbox_stock_closure_all_zones.{json,txt}`, preserving the original 8-zone `pc_external_image_xbox_stock_closure.{json,txt}` proof. **No donor payloads, no FF emission, no Converted Maps promotion.** Missing images after wider scope need map-owned IWI/DDS or verified runtime-stock dependency resolution.
- **Aztec full Xbox retail image identity audit (Oct 28): PASS.** The locally owned **67/67 Xbox retail FFs** passed OAT `--list`; the eight earlier core/MP/Zombies/UI lists were reused from validated caches. Across **775 PC image extraction errors / 654 distinct missing-data names**, **473/654 names** (72.3%) occur in at least one Xbox retail zone, an improvement of **171 unique names** over the 8-zone audit's **302**; **181 distinct names** had no match in the 67 scanned zones. Individual per-zone hit counts overlap and are not additive. All results remain **identity evidence only**: campaign/MP-only asset names cannot be assumed resident in Aztec, and no retail image payloads may be bundled into converted maps.
- **Next dependency-triage gate (pending first Windows run):** `tools/oat-xenon/Run-SecondMapAztecImageDependencyTriage.ps1` reconciles the existing core-8 and full-67 JSON audits (requiring the 302/473/181 exact partition), the 114/114 PC RawFile success, the 11-file diagnostic PC image dump and 666-root native identities. **No OAT processes or FFs are generated.** The read-only report `aztec_image_dependency_triage.{json,txt}` lists every one of the **181 unmatched image names**, every one of the **171 newly found outside-core names**, and the **302 core-scope names**, separating global/Zombies-listed, multiplayer-only and campaign/other-only retail identities. It also flags generated-looking names and whether an identity exists among 666 native roots. These tiers prioritize **dependency/residency proof or legitimate PC-map pixel sourcing**, *not automatically grafting console stock pixels*. The existing all-zones and core audit reports are retained unchanged.
- **Aztec image dependency triage PASS (Oct 28):** `AZTEC_IMAGE_DEPENDENCY_TRIAGE_PASS` reconciled **654 missing PC image names** into **302 names present in the original 8 retail zones**, **171 newly found only in the additional 59 retail zones**, and **181 names absent from all 67 scanned Xbox zones**. Among the matched stock identities, **270 names occur in global/Zombies zones**, **32 occur only in multiplayer zones**, and **171 occur only in campaign/other zones**. Of **181 retail-unmatched names**, **178 correspond to actual native Aztec image roots**, and **32 are generated-looking image names** (overlapping categories). These counts are *identity/source-availability evidence*, not runtime residency, pixel equivalence or image-conversion progress. Prior 114/114 PC RawFile Xbox SHA readbacks remain PASS.
- **Next focused PC custom-source hunt (pending Windows run):** `tools/oat-xenon/Run-SecondMapAztecExternalPcImageSources.ps1` reads the successful triage JSON and searches the installed Aztec PC map folders and local/roaming map packages for source-owned **loose IWI/DDS and IWD archive entries** matching those **181 no-retail identities**, hashing matching source content *without writing or grafting pixels*. `-ExtendedSources` optionally scans the existing PC Maps, map-assets, UGX and ZCT libraries to identify *candidate* external sources; identity matches from unrelated map libraries are not automatically owned or production-safe. Results are `aztec_external_pc_image_sources.{json,txt}` (or `_extended`), with exact hits, missing native names, source hashes and conflicting variants. **No OAT, no FF, no hardware-ready claim**; these results support selecting genuinely map-owned payloads for the next legitimate Xbox image graft.
- **Aztec local PC-source image audit PASS (Oct 28):** `Run-SecondMapAztecExternalPcImageSources.ps1` scanned **one existing Aztec source root**, with **0 loose IWI/DDS**, **1 IWD archive containing 2 image entries**, **0 additional matches** against the **181 Xbox-retail-unmatched names**. Thus **178 native image-root names still lack matching source payloads in this narrow PC map-package scan**, and all 32 generated-looking unresolved image names remain without a matching IWI/DDS entry here. The script reported zero archive errors and wrote only a read-only source-evidence report. **This does not establish the images are missing from every legitimate PC/asset source.** The intended next test is the existing `Run-SecondMapAztecExternalPcImageSources.ps1 -ExtendedSources`, which scans the local PC Maps, map assets, UGX and ZCT libraries without OAT, FF emission, or importing/distributing Xbox retail pixels. Cross-package matches are **candidates only**, not approved ownership/semantic equivalence. If wider IWI/DDS/IWD discovery is also empty, investigate map build sources and generated image composites rather than repeat failed OAT dumps.
- **Aztec extended PC IWI/DDS/IWD source hunt PASS (Oct 28):** `Run-SecondMapAztecExternalPcImageSources.ps1 -ExtendedSources` scanned **five legitimate local PC/library roots** with **0 IWD/ZIP errors**. Aztec's own IWD contributed zero new matches, but PC Maps IWDs yielded **47 matching source occurrences** and ZCT IWD **5 occurrences**; across the 181 identities absent from all 67 retail Xbox zones there are **32 distinct matching PC-source candidates**, **8 with conflicting SHA-256 content variants**, and **149 with no source candidates** from the scanned directories. All **32 matched names are among the 178 native image identities**; **146 native image-root names** remain without a candidate. Generated-looking unresolved names without sources dropped to **30**. Other maps' texture-name matches are NOT proof those textures belong to Aztec; no pixel assets have been approved for production graft or redistributed. `map-assets` and UGX loose/IWD sources yielded no additional matches.
- **Next targeted image provenance gate (pending Windows run):** `Run-SecondMapAztecPcImageCandidateReview.ps1` consumes the prior successful local/extended source and stock/triage JSON reports **without rereading IWDs or running OAT**. It requires exact **181=32+149**, **32=24 single-content-hash + 8 cross-package conflicts**, and **146 missing native root** totals; writes `aztec_pc_image_candidate_provenance.{json,txt}` with exact source archive/entry names, prior SHA-256 evidence, all eight conflicting identities and all 149 absent names. Even the **24 single-hash candidates remain unverified external-source leads**; zero image entries automatically promoted. Next production work must establish author/PC build provenance for map-specific content and validate stock-reference residency separately, before image payload conversion or hardware readiness.
- **Aztec external PC image candidate provenance review (Oct 28): PASS.** `Run-SecondMapAztecPcImageCandidateReview.ps1` validated **181** no-retail image names: **32** match at least one unrelated PC map asset source, **24** have one content hash across their discovered candidates, **8** have conflicting source hashes, **149** remain unmatched (including **146 native image-root identities**), and **30** unmatched names look generated. Conflicting exact names: `g43_chalk_c` (2 SHA variants), `orniment_silver_dark` (2), and six Zombies specialty icons `specialty_2x_zombies`, `specialty_doubletap_zombies`, `specialty_fastreload_zombies`, `specialty_instakill_zombies`, `specialty_juggernaut_zombies`, `specialty_quickrevive_zombies` (4 SHA variants each). **No external PC candidate is automatically approved as Aztec-owned or suitable for grafting**, even with one observed hash. The report lists original candidate archive paths and SHA evidence. Full structural native 8,509-root OAT PASS and 114/114 genuine PC RawFile Xbox SHA readback remain intact.
- **Next direct PC-pixel conversion gate (pending Windows test):** `tools/oat-xenon/Run-SecondMapAztecPcIwdImageRoundTrip.ps1` reuses the **two actual PC IWI v6 / BC3 format 0x0D** sources in Aztec's own map IWD (already proven 2/666 identity matches), and the previously validated FRUmansion T4 Xenon OAT **image-only Linker/Unlinker roundtrip**. It copies only these legitimate PC map IWI sources to short physical staging paths, runs the Xenon image encoder to generate one **two-image scratch Xbox FF**, and independently reopens it with OAT to verify exact PC IWI payload SHA-256 and header/dimensions for both images. This is the first targeted **actual PC-source-backed Xbox image-pixel emission experiment** for Aztec; no native/oracle image payload, no unrelated PC source conflicts, and no stock Xbox donor pixels are used. Reports and diagnostic FF are kept in ignored `pc_iwd_image_roundtrip/` scratch, **not the full 8,509-root Aztec FF**, and must **never be deployed or promoted into Converted Maps**. This test does not yet prove an integrated Aztec image graph or hardware compatibility; source-pixel conversion remains pending its first Windows result.
- **First genuine Aztec PC IWI pixel Xbox round-trip (Oct 28): PASS.** `Run-SecondMapAztecPcIwdImageRoundTrip.ps1` read the original installed `nazi_zombie_aztec.iwd`, extracted exactly two PC-source IWI v6 / BC3 images (`goal`, `loadscreen_nazi_zombie_aztec`), ran the T4 Xenon OAT Linker (exit 0, 0 ERROR lines), then independently unlinked the **image-only scratch Xbox FF** (exit 0, 0 errors). **2/2 exact PC IWI pixel SHA-256 readbacks and 2/2 exact source image headers/dimensions.** This is a real PC-source-backed **image encoder / FF byte persistence** milestone, not an oracle pixel copy or a complete playable map. Both the original 114/114 real PC RawFile result and the 8,509-root native structural proof remain separate and unchanged.
- **Next combined PC image + PC RawFile 8,509-root integration (pending first Windows run):** `tools/oat-xenon/Run-SecondMapAztecPcImageRawFileIntegration.ps1` starts from those passing source manifests and the existing image-only FF. It runs the specialized Xenon OAT image-graph dumper in a short physical temp directory, verifies its two PC-derived image graph/pixel/header blobs, and uses a new **strictly named CLI `--aztec-pc-iwd-image-rawfile-graft` mode** to emit a **dedicated scratch-only 8,509-root diagnostic FF** containing **2 real original-PC-IWD image roots and 114 real PC RawFile roots**, alongside the previously validated native-reference renderer structure. A PASS requires the existing native root identity/order, exact block layout, independent OAT zero-errors loader and **fresh dual readbacks: 2/2 source pixel SHA-256 and 114/114 source RawFile SHA-256** from the *combined* FF. All outputs stay in `pc_iwd_rawfile_integration/`; prior 4E and RawFile-only PASS artifacts remain intact. **The remaining images/materials/models/world assets and shader-source translation are incomplete; even a combined PASS is not an Xbox test candidate, not gameplay proof, and cannot be promoted to Converted Maps.**
- **Aztec combined original PC image + RawFile native-graph validation (Oct 28): PASS.** The first run of `Run-SecondMapAztecPcImageRawFileIntegration.ps1` produced **`AZTEC_PC_IWD_RAWFILE_COMBINED_GRAPH_PASS`**: the specialized OAT graph dumper imported **2 genuine original Aztec PC IWD textures** into Xenon GfxImage graphs, the **8,509-root scratch FF** survived exact root sequence/layout replay and the OAT loader, and independent readbacks proved **2/2 exact original PC pixel payload SHA-256 hashes** plus **114/114 exact original PC RawFile SHA-256 hashes** from the **same FF**. No native reference Xbox image pixels were substituted. Renderer graph assets/shaders still use the user-supplied native structural reference, while many other images, XModels, materials, world and StringTable contents remain incomplete. **This remains validation-only and NOT a playable/hardware-testable map.** Preserve prior isolated texture, RawFile and Stage 4E proof artifacts without overwriting.
- **Next source-owned geometry/world frontier (pending Windows run):** `tools/oat-xenon/Run-SecondMapAztecModelWorldSourceFrontier.ps1` requires the passing combined FF result and SHA-pinned original three PC fastfiles, reuses the **existing OAT PC `--list` caches** (no re-extraction, no build or FF generation), and compares PC `xmodel` names with all verified native Aztec XModel roots, distinguishing owned model identities from leading-comma reference roots. It reports source FF role coverage and the existing named world/root types (`gfxworld`, `fxworld`, `clipmap`, `mapents`, etc.) to select an actual PC-owned XModel canary for the existing FRUmansion-derived Xenon model bridge. A matching name **does not prove model body, partclassification, materials or hardware-resident references**; source XModel extraction and geometry conversion will be the next substantive step. Output is read-only under `pc_model_world_source_frontier/`. The combined passing scratch FF is untouched.
- **Aztec PC XModel native-name frontier PASS (Oct 28):** `Run-SecondMapAztecModelWorldSourceFrontier.ps1` reused cached OAT lists without extraction; **265 unique XModel names in `map.ff`**, **0 in `map_patch.ff`**, **53 in `mod.ff`**. There are **288 distinct PC XModel identities**, of which **284/284** match all **284 owned native Aztec XModel roots** (no native reference roots, **zero missing PC name matches**); **four PC names** are not native-root identities. **This is a model *name* coverage PASS, not model payload or geometry translation**. GfxWorld/ClipMap/world payloads still need source-backed conversion. The 8,509-root mixed original-PC **2/2 image pixels + 114/114 RawFiles** OAT/layout/SHA readback proof remains untouched.
- **Next genuine Aztec PC XModel Xenon canary (pending Windows run):** `tools/oat-xenon/Run-SecondMapAztecPcXModelCanary.ps1` verifies the model/world inventory and existing combined-graph proof, chooses a **mod.ff XModel whose name matches an owned native root**, and calls the previously proven FRUmansion T4 Xenon model bridge using an optional **`-AllowedModelNamesFile`** whitelist. That bridge exports source-owned PC XModel JSON/GLTF, requires fidelity-complete per-model `partClassification[]`, writes one **model-only scratch Xbox FF**, checks OAT loader model identity and byte layout. A short physical temporary workspace is deliberately kept for traceable source files and bridge manifest; only a diagnostic summary goes under Aztec `pc_xmodel_canary/`. The FRUmansion bridge behaves unchanged without the whitelist. **Zero model bodies have been grafted into the 8,509-root combined FF; neither the canary nor that existing FF is playable or approved for hardware.** A passing canary would prove one original-PC geometry path, not the full 284-model world.
- **First Aztec original-PC XModel -> Xenon canary (Oct 28): PASS.** `Run-SecondMapAztecPcXModelCanary.ps1` selected **`tag_origin`** from the 53 native-matching original `mod.ff` model names; original PC JSON/GLTF export **1 LOD / 2,853 bytes**, per-model **partClassification 1/1 bone**, OAT Xenon Linker FF and independent Unlinker model readback **PASS**, status `AZTEC_PC_XMODEL_XENON_CANARY_PASS`. This proves the **basic PC XModel serialization path** but is a **tiny 1-bone helper**, not evidence of a substantial renderable 3D mesh or world geometry. No XModel has yet been integrated into the 8,509-root combined 2-PC-image/114-PC-RawFile diagnostic FF, and no FF is authorized for Xbox testing.
- **Next renderable triangle-mesh XModel canary (pending Windows test):** `Run-SecondMapAztecRenderableXModelCanary.ps1` uses the same source-backed, native-identity-whitelisted 4U bridge but requires a PC GLTF containing at least **128 POSITION vertices, 192 triangle indices, and an actual triangle primitive** in one LOD before selecting a model. Only a verified renderable PC model can produce the `AZTEC_PC_XMODEL_RENDERABLE_MESH_PASS` status; the existing `tag_origin` PASS remains intact in its original report. Defaults to `mod.ff` (53 names); accepts `-SourceRole map.ff` (265 names) if mod models have no qualifying meshes. The generic FRUmansion bridge gained optional `-MinimumMeshVertices` and `-MinimumTriangleIndices` parameters; **both default to zero**, preserving its normal behavior. The new canary stores output under dedicated `pc_xmodel_renderable_canary/` and a separate short scratch directory; requires the original PC per-model partClassification and an OAT Xenon link/load PASS. **This is still model-only and not a full-map or hardware test.** Next would be real geometry integration, then materials/world structures.
- **Next production tasks:** Promote legitimate PC-source-backed Material/TechniqueSet, shader, image, XModel, RawFile and StringTable ownership into the Xbox graph emitter; resolve the two remaining material-referenced TechniqueSet identities and all weak matches; verify all output graph relocations and source-map runtime behavior before any hardware-ready release claim.

Reproduce the successful reference graph proof from the repository using `tools/oat-xenon/Run-SecondMapAztecNativeGraphProof.ps1`. The main result is `IDENTITY_AND_TYPED_GRAPH_IMPORT_PASS`. All experimental outputs stay under ignored `output/`; the developer's `Converted Maps` directory is **reserved exclusively for hardware-verified, working Xbox map packages**.

## Historical status — October 25, 2026



The deterministic Linker V2 pipeline is in late native-loader bring-up. Source discovery, canonical root accounting, script-string reconstruction, Xbox type mapping, XFile/XAsset emission, block-capacity planning, Xbox single-zlib wrapping, selected-load generation, and real-hardware diagnostics are working foundations.

The current main blocker is now very narrow: the Xbox TU7 `XAnimParts` fixed layout, owned stream structure, and the **10-count PC → 12-count Xbox category expansion pattern** are proven across 413 valid same-name FRUmansion XAnims. Xbox adds two quaternion subcategories; translation categories remain one-to-one and shift by two slots. What remains is the per-bone semantic rule that splits PC full-quaternion buckets `[2]` and `[4]` into the Xbox sub-buckets, plus final serializer promotion.

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
- The multi-animation oracle proves the actual grouping is **seven quaternion buckets + four translation buckets + total**: `sum(boneCount[0..6]) == boneCount[11]` and `sum(boneCount[7..10]) == boneCount[11]`.
- Working names such as `ai_flame_death_a` resolve through the loaded `+0x00` pointer.
- `+0x24` is proven as `randomDataShortCount`.
- `+0x28` is proven as `indexCount`; `Function_82159ED0` also proves byte indices when `numFrames < 256` and 16-bit indices otherwise.
- Loader-consumed pointers are mapped through the full root: `+0x34 names*`, `+0x38 dataByte*`, `+0x3C dataShort*`, `+0x40 dataInt*`, `+0x44 randomDataShort*`, `+0x48 randomDataByte*`, `+0x4C randomDataInt*`, `+0x50 indices*`, `+0x54 notify*`, and `+0x58 deltaPart*`.
- The generated post-load/mark path independently walks `names[]` using `boneCount[11]` and `notify[]` using `notifyCount`.
- The type-4 registration wrapper was followed through `FUN_82169F78 → Function_82168968`; that path is generic XAsset bookkeeping rather than animation decoding.
- The CLI includes `xanim-bone-compare <pc.ff> <xbox.ff> [name]`, a read-only same-animation oracle.
- 413 valid same-name FRUmansion XAnims prove the same structural expansion with zero exceptions:
  - PC `[0] -> Xbox[0]`
  - PC `[1] -> Xbox[1]`
  - PC `[2] -> Xbox[2] + Xbox[3]`
  - PC `[3] -> Xbox[4]`
  - PC `[4] -> Xbox[5] + Xbox[6]`
  - PC `[5] -> Xbox[7]`
  - PC `[6] -> Xbox[8]`
  - PC `[7] -> Xbox[9]`
  - PC `[8] -> Xbox[10]`
  - PC `[9] -> Xbox[11]`
- The five apparent failures in the first broad scan were false-positive header matches; enforcing the quaternion/translation total invariants removes them.
- For `ai_flame_death_a`, PC `[04 00 67 01 01 0F 06 01 57 6D]` maps to Xbox `[04 00 55 12 01 01 00 0F 06 01 57 6D]`; `0x67 = 0x55 + 0x12`, `0x01 = 0x01 + 0x00`, and translation maps directly to Xbox `[7..10]`.

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
+12  boneCount[0]           quaternion category 0
+13  boneCount[1]           quaternion category 1
+14  boneCount[2]           full-quaternion subcategory A
+15  boneCount[3]           full-quaternion subcategory B
+16  boneCount[4]           quaternion category 3
+17  boneCount[5]           full-quat-no-size subcategory A
+18  boneCount[6]           full-quat-no-size subcategory B
+19  boneCount[7]           translation category 5
+1A  boneCount[8]           translation category 6
+1B  boneCount[9]           translation category 7
+1C  boneCount[10]          translation category 8
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

What is **not yet proven** is the per-bone semantic criterion that divides PC `boneCount[2]` between Xbox `boneCount[2]/[3]` and PC `boneCount[4]` between Xbox `boneCount[5]/[6]`. Translation is now structurally solved. The final semantic naming of small flag/metadata fields around `+0x10..+0x23` and native-use confirmation for `+0x2C/+0x30` also remain. The serializer stays fail-closed until those two quaternion split rules are derived rather than guessed.

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
- [x] Prove Xbox grouping is 7 quaternion buckets + 4 translation buckets + total.
- [x] Prove the PC 10-count → Xbox 12-count structural expansion equations across 413/413 valid same-name XAnims.
- [x] Prove translation categories map directly: PC `[5..8] -> Xbox[7..10]`.
- [x] Identify and reject the five broad-scan false-positive headers using the count invariants.
- [ ] Derive the per-bone split criterion for PC full-quat `[2] -> Xbox[2]/[3]`.
- [ ] Derive the per-bone split criterion for PC full-quat-no-size `[4] -> Xbox[5]/[6]`.
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

## Release output policy — developer workspace

The local **release-only** directory for verified Xbox 360 converted maps is:

```text
C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\Converted Maps
```

- **Only working, hardware-verified Xbox 360 map packages** belong in this directory, arranged by canonical map name (for example, `Converted Maps\nazi_zombie_aztec\`). Include only the Xbox files actually required to deploy/play that map (main/load/patch/mod fastfiles and any needed accompanying assets such as stream audio).
- **All intermediate outputs** (analysis reports, probes, experiments, generated candidates, validation oracles, logs, staging folders and temporary FFs) stay under the repository's ignored `output/` and other scratch/diagnostic paths, **never** in `Converted Maps`.
- **No automatic promotion on serializer/OAT success alone.** An FF that parses, links, or passes OAT is not necessarily working on real Xbox hardware. Promote to the release-only directory after the relevant real-hardware checks pass.
- **No proliferating FF build folders or backup version stacks in the release directory.** Keep one clean package per canonical map; preserve build history, when necessary, in development workspaces, not the published destination.
- These are **local developer-machine paths**, not portable defaults for other users. The public converter must not rely on this user's directory existing.

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
