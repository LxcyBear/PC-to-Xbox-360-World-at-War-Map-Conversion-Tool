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
