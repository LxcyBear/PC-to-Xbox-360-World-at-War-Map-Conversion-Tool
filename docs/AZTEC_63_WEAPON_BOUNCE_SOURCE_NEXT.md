# Aztec WeaponDef bounceSound and SndAliasCustom source ownership experiment

**Latest verified user hardware-independent checkpoint (2026-10-31):**
\`Run-AztecWeapon63AccuracyVec2ExactAB.ps1\` at \`54d2217\` built two fresh identical-Git native OAT diagnostics: original-PC scalar + 207 fixed-body inline XStrings **4,513,376 B** vs identical world plus **96 real accuracy Vec2 arrays (612 knots, 4,896 B)** **4,514,237 B**. **+4,896 original PC serialized/zone bytes and +861 compressed FF bytes**, OAT all 8,509 root identities and 0 warnings/errors in BOTH runs. The original protected **4,510,695 B** baseline FF was SHA protected. Neither result is Xbox ready.

## Next source-owned child class

T4 WeaponDef fixed-body \`bounceSound\` pointer at **byte 624** references (when the original 7b stream owns it inline) **31 SndAliasCustom pointers × 4 B = 124 B**. The original 7b typed walker already traverses those 31 entries and their nested \`SndAliasCustom\` name-pointer records, followed by inline sound-name XStrings where genuinely owned. Do not confuse these pointer tables or names with XMA sound wave payloads.

A new collector in \`T4WeaponVariableSerializerProof\` observes raw source-owned bounceSound table bytes and nested inline sound-alias-name record bytes **without** copying packed/external pointer children. The new independent \`T4XenonAztecWeaponBounceSourceWitness\` verifies the same 63 original-PC-source 7b blob SHAs, original PC loaded direct name proof, byte offset, name/body record SHA, 31-entry cardinality and source-owned sound name byte/terminator checks. Results are a read-only JSON classification and SHA ledger, not Xbox payloads.

**First Windows run** (from repo root; no existing Xbox FF is modified):

\`\`\`powershell
git pull --ff-only
git rev-parse --short HEAD
powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1"
if ($LASTEXITCODE -ne 0) { throw "PS 5.1 parser regression" }
powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Run-AztecWeapon63BounceSoundSourceWitness.ps1"
if ($LASTEXITCODE -ne 0) { throw "Original-PC WeaponDef bounce witness blocked" }
\`\`\`

Output: \`output/second_map_baselines/nazi_zombie_aztec_V2.2/native_material_mapping/pc_g11_lightning_weapon63_bounce_sound/aztec_63_original_pc_bounce_sound_source_witness.json\`.

**Possible outcomes:** genuine inline bounceSound table source proof / no inline table (null or packed only), or a genuine typed walker/source SHA refusal. No table size or FF growth is promised ahead of this Windows result. PowerShell 5.1 parser and .NET compile still need local verification of the new commits.

## Strict follow-on

Once source ownership counts are known, use a **separate isolated scratch native FF** to encode validated T4 Xenon pointer arrays, allocate each genuinely owned inline SndAliasCustom child in correct 7b ZoneCode traversal order, rebase all genuine name pointers, and quarantine any unresolved packed/external references; prove exact root IDs and OAT 0/0. Do NOT write the original-PC raw packed pointer words into native FF; do not pretend sound-alias name strings are actual Xbox XMA audio.

All remaining six GfxWorld image pixel bodies, streaming semantics, lightmap semantics, external stock XModel residency, original PC WeaponDef source conflicts and stock-only weapon roots, incomplete WeaponDef FX/Material/XModel/Audio graphs, and native runtime/code dependency closure remain **blocked**. Maintain the current protected FF SHA and the existing no-hardware-deployment rule until an independently verified complete package exists.


## Two genuine inline bounce tables: opt-in native FF smoke-build (not yet Windows-tested)

The first Windows source-only bounce witness at \`c2dc88d\` **PASS** established 63/63 source-owned WeaponDef bounce pointer fields classified:
- **2 genuinely INLINE** 31-entry bounceSound tables, 248 real original-PC table bytes.
- **61 source NULL** table slots, **0 packed/external** table slots. The 61 absent tables cannot be materialized without inventing source data.
- **62 original-PC inline bounce SndAliasCustom pointer records** (248 bytes), and **9 actual inline sound-name XStrings** (199 bytes); 2 tables + 62 records + 9 names = **695 additional native diagnostic serialized bytes if accepted**.
- The wider 63 WeaponDef owned 7b corpus additionally contains 228 inline sound-alias records and 895 bytes of inline name text across non-bounce sound fields; those **remain for a later separate graph stage**.

The newly wired \`-OwnedBounceSound\` path in \`Run-AztecWeapon63FfGrowthCanary.ps1\` uses \`-Weapon63OwnedBounceSound\` only in \`Run-SecondMapAztec39WorldMaterialIntegratedScratchFF.ps1\`. The writer re-opens the same 63 independently SHA-verified original-PC 7b blobs, uses the typed source walker to prove every original byte offset and nested name SHA, copies ONLY 2 native 124-byte table bodies with 31 correctly rebased FOLLOWING references each, 62 inline 4-byte name-pointer records with source-packed/non-inline names set NULL, and 9 exact owned inline name strings. It includes all previously OAT-accepted scalar, 207 inline-XString and 96 Vec2 proof modes without replacing their archives.

An isolated FastFile is generated under \`repo/.deps/aztec-w63-bounce-ff-growth/scratch_emit/nazi_zombie_aztec.ff\`. Previous 4,514,237 B OAT-good Vec2 candidate, two exact A/B archive cases, protected original 4,510,695 B FF, and other source proofs remain untouched. The stage MUST pass strict 7b self-contained typed walk, Stage4D native block replay, exact 8509 native root identity, OAT warnings/errors 0/0, and parent runner 2-table/62-record/9-name counts. No source FOREIGN audio/XMA assets, no fake 61 tables, no Xbox hardware. **No claim about compressed FF growth is possible before the first Windows build.**

**First one-build Windows command from repo root:**

\`\`\`powershell
git pull --ff-only
git rev-parse --short HEAD
powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1"
if ($LASTEXITCODE -ne 0) { throw "PowerShell 5.1 parser BLOCKED; no build" }
powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Run-AztecWeapon63FfGrowthCanary.ps1" -Limit 63 -ExpandedInlineXStrings -OwnedAccuracyVec2 -OwnedBounceSound
if ($LASTEXITCODE -ne 0) { throw "Genuine two-table native scratch FF/OAT BLOCKED" }
\`\`\`

Success status is \`AZTEC_W63_TWO_REAL_PC_BOUNCE_TABLES_62_ALIAS_RECORDS_NATIVE_FF_OAT_PASS_NOT_FULL_AUDIO\`. This is **not** 63 real audio tables or Xbox-runtime approval. Follow-on speedrun target: **228 total genuine inline source SndAliasCustom records / 895 inline name bytes** across all 63 original PC weapons, after independent strict pointer rebasing and an isolated per-graph OAT pass.


## NEXT SPEEDRUN: 160 direct fixed-slot SndAliasCustom children (after 4d42a4e audit PASS)

The Windows read-only audit at \`4d42a4e\` returned:
- 63/63 genuine original PC WeaponDef 7b streams rechecked, native OAT-good bounce FF SHA pinned.
- 228/228 total real typed inline SndAliasCustom records, 895 original-PC inline name bytes.
- 62 alias records / 9 names / 199 name bytes **already in the OAT-good bounce FF** (4,514,324 compressed bytes).
- **160 directly-owned fixed WeaponDef sound alias records**, of which **41** have source-inline sound-name strings (696 physical original-PC bytes). Each record's source offset, pointer word and inline string bytes/SHA are independently recorded.
- 6 additional alias records are owned by **nested FlameTable parents** with NO inline names. These 6 stay FENCED because grafting their 4-byte records without the parent FlameTable graph would be invalid.
- 0 unknown/other foreign-parent records.

The **opt-in** next case \`Run-AztecWeapon63FfGrowthCanary.ps1 -Limit 63 -ExpandedInlineXStrings -OwnedAccuracyVec2 -OwnedBounceSound -OwnedDirectSoundAliases\` emits in **one isolated** native diagnostic FF under \`repo/.deps/aztec-w63-direct-ff-growth\`. It never overwrites the prior OAT-passing 2-table FF, and fails closed if the source frontier, earlier bounce witness, old FF SHA, or any of the original 63 7b blobs changes.

The same typed original-PC source stream walker, SHA checks, variable-child position sort, self-contained Xbox 7b validation, Stage4D replay, native 8,509-root exact identity and OAT 0/0 remain mandatory. The new stage adds exactly **160 × 4B = 640B** of rebased SndAliasCustom records and **696B** of original-PC sound-name string bytes (41 strings): **+1,336 authentic serialized child bytes over the previous bounce graph** if validation accepts the emitter. No exact compressed FF delta can be claimed until a same-world same-revision A/B experiment; the single-run byte comparison is archive-relative only.

Required Windows command from repo root:

\`\`\`powershell
git pull --ff-only
git rev-parse --short HEAD
powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1"
if ($LASTEXITCODE -ne 0) { throw "PowerShell 5.1 syntax gate blocked" }
powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Run-AztecWeapon63FfGrowthCanary.ps1" -Limit 63 -ExpandedInlineXStrings -OwnedAccuracyVec2 -OwnedBounceSound -OwnedDirectSoundAliases
if ($LASTEXITCODE -ne 0) { throw "Original-PC 160 direct aliases FF/OAT gate blocked; no Xbox" }
\`\`\`

On strict OAT success: \`AZTEC_W63_160_REAL_PC_DIRECT_SOUND_ALIASES_NATIVE_FF_OAT_PASS_FLAME_AND_AUDIO_BLOCKED\`. This would physically serialize 222/228 real alias records (62 bounce + 160 direct) and all 895 known inline name bytes (199 bounce + 696 direct) while still **fencing six nested FlameTable records and all real Xbox audio/XMA sample assets**. It does not imply full 179,775B WeaponDefs, runtime-compatible sounds, map dependencies, CodXe playability, or hardware approval.

**Status:** source census PASS. New 160-record FF code committed but not yet tested on Windows/OAT.


## FlameTable parent closure — source-only proof BEFORE the final six aliases

At commit \`1112a89\`, the Windows 160-direct-alias build was **OAT PASS**:
- 4,514,599-byte compressed native diagnostic FF; previous 4,514,324-byte bounce FF preserved; +275 B historical comparison **not** an exact same-source A/B.
- 222/228 original-PC SndAliasCustom owned records in native scratch FF: 62 bounce + 160 direct. All 50 truly inline name strings / 895 name bytes were already serializable.
- Layout replay exact; OAT recognized 8,509 native root identities, warnings 0 / errors 0. Still not runtime audio or hardware-ready.

Six remaining owned 4-byte SndAliasCustom child records belong to optional \`FlameTable\` parents. The T4 typed walker consumes two parent pointer fields per WeaponDef at 7b body offsets **2460** (first-person) and **2464** (third-person). Each truly inline parent is a 476-byte native-endian source FlameTable fixed body with a name pointer at +424, eight material pointer cells at +428..+456, and four sound alias pointers at +460..+472. **Do not graft this 476-byte block as raw parent bytes:** all parent pointers must be fully rebased or fenced and any genuinely inline name/material children require source proof.

The next stage \`Run-AztecWeapon63FlameParentSourceWitness.ps1\` reads only the original-PC-owned 7b proof files and previous independently hashed 228-alias source witness. It:
- Revalidates 63 source-body SHA owners (including six packed-name direct sources), original protected OAT-good FF and the 4,514,599-byte 160-direct FF.
- Performs a real typed T4 Alpha 7b read through both FlameTable fields for every WeaponDef, capturing actual inline 476-byte parent start offset, source byte SHA and all original pointer words, **not inferred parents**.
- Rejoins the six already-proven original-PC inline flame sound alias records by typed label, nested field, record offset and independent original source record pointer.
- Reports real INLINE, NULL and PACKED parents; parent physical bytes and all nested sound pointer classes, without creating any FastFile or changing the Xbox runtime.

Run from repo root:

\`\`\`powershell
git pull --ff-only
git rev-parse --short HEAD
powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1"
if ($LASTEXITCODE -ne 0) { throw "FlameTable witness PS5.1 syntax gate failed" }
powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Run-AztecWeapon63FlameParentSourceWitness.ps1"
if ($LASTEXITCODE -ne 0) { throw "Original-PC FlameTable genuine parent proof BLOCKED; no FF" }
\`\`\`

The proof report is \`output/second_map_baselines/nazi_zombie_aztec_V2.2/native_material_mapping/pc_g11_lightning_weapon63_flame_parent/aztec_63_original_pc_flame_parent_source_witness.json\`. If this stage passes, the next FF attempt must still prove the FlameTable parent pointer layout, source-owned name/material dependencies, and non-reused native child rebases before counting any extra alias records. All audio samples/XMA assets remain absent. **Source-proof PASS does not imply a 228/228 FF or Xbox runtime support.**
