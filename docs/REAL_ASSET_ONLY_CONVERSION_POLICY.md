# PC2XB3 — real asset only conversion policy (2026-10-31)

Effective immediately for any **new** asset, material, texture, model, animation, WeaponDef, sound, world, map or release candidate in World at War PC → Xbox 360/CodXe conversion.

## Non-negotiable requirements

1. Authentic PC-owned asset bytes must come from the actual source mod/FastFile, verified against source file SHA-256, typed T4 layout and real asset identity, then endian/relocation-converted into the correct Xbox native format.
2. Genuine Xbox 360 retail stock references are acceptable only when the exact asset name, type, stock residency and genuine native body/identity are independently proven. Do not import unrelated donors or oracle payloads as replacement game data.
3. A source-authentic NULL remains NULL. A **non-NULL original PC source reference must not silently become NULL, a dummy pointer, an invented object, a fake material, an empty pixel image, a blank XModel or sound, or a fabricated dependency** in any new production-intended output. If its referenced asset cannot be proven, fail closed and report the unresolved dependency.
4. Never fabricate textures, shader statebits, materials, techniques, meshes/vertices, animations, XStrings, sound aliases, XMA samples, world/lighting/FX/BSP/cell data to increase FF size or claim completion. An identity/name shell is not an asset payload.
5. Every non-NULL PC asset child must link to an independently verified original-PC-derived Xbox-compatible body or a proven genuine Xbox retail stock asset. Original packed PC pointer words are not valid native rebases.
6. OAT zero warnings/errors establishes FF structural acceptance only. CodXe/Xbox gameplay needs a complete asset/dependency graph, correct texture/renderer/sound runtime semantics, full map/_load package when required, then hardware smoke testing.
7. Audit reports separate these categories explicitly: source-owned real bytes; physically emitted real native bodies; verified genuine retail reference; authentic source NULL; identity-only shell; unresolved non-NULL pointer; blocked foreign dependency. A production release must fail closed if a required asset is unresolved or shell-only.
8. The existing historical OAT diagnostic candidates with identity-only roots or intentionally omitted children **remain diagnostic artifacts only**. No additional filler/shell replacement to masquerade as progress, and no promoting historical shell-only content to release.

## Current Aztec verified state vs remaining real asset work

- The user's Windows FF at git commit 1112a89 is a **4,514,599-byte OAT-good diagnostic FF**, with 8,509 verified root identities but NOT 8,509 fully working asset bodies. Sixty-three source-owned WeaponDef roots contain partial genuine children. Two conflicting weapon variants and four Xbox stock-name-only roots still need real closure.
- Genuine source-owned bounce-sound tables: 2 real inline 124-byte tables; 62 original PC nested sound alias records. Additionally 160 fixed WeaponDef sound alias records and 41 original PC inline names (696 bytes) are in the FF. Total **222/228 source-owned alias records in FF**, with 50 source-inline sound name strings containing 895 authentic bytes. No actual sound/XMA sample payloads are proven.
- The independent 7b FlameTable source-only witness at commit 410dfd7 found **four genuine 476-byte inline parent bodies = 1,904 bytes**, **122 natural source NULL** parent fields, zero packed/external FlameTable parents, and **six genuinely owned nested sound alias records**. These four parents and six children were NOT added to the native FF.
- To emit a real FlameTable, first prove parent XString source bytes at +424 (when non-NULL); all eight Material pointers at +428..+456 as real PC source-converted material or exact verified genuine stock; and the four sound alias pointers at +460..+472. Do not zero any originally non-NULL parent material/name reference to force the 476-byte graph into an FF. The six 4-byte SndAliasCustom children do not contain decoded sound samples.
- Six GfxImage identity-name shell bindings still lack actual pixel payload, one external stock XModel lacks residency proof, and GfxWorld/material runtime semantics remain unresolved. These are release blockers, not valid filler data.

## Immediate source-only real-dependency audit

The repository script tools/oat-xenon/Run-AztecWeapon63FlameRealAssetDependencyAudit.ps1 will independently check the SHA-pinned four FlameTable source parents and the previous passing OAT FF. It classifies **32 Material pointer cells**, **four parent name pointers**, and **16 nested sound pointer cells** without creating any new FF, adding a placeholder material or mutating previous accepted binaries.

A non-NULL source pointer in that report is an unresolved real asset link until backed by genuine data, never permission to replace it with a zero. Next stage: actual PC source material/name resolution, exact Xbox body/stock binding, then complete typed FlameTable parent/child graph with no placeholders.


## Actual four FlameTable parents: dependency findings and next source-byte proof

The user ran the FlameTable real-asset dependency audit on Windows at commit 151546b. It passed all 71 PS5.1 source syntax checks and verified:

- Four true source-owned 476-byte FlameTable parents (1,904 authentic source bytes), 122 naturally NULL parent pointers, no packed/external parents.
- Parent name fields: **4 non-NULL / 4 total**, but *none* may be treated as a real string until inline source bytes or a packed string's original source target is proven.
- Parent material slots: **12 authentic NULL / 32; 20 NON-NULL** requiring original-PC Material source proof and valid native Xbox backing.
- Parent sound slots: **6 genuinely INLINE SndAliasCustom records / 16; 4 authentic NULL; 6 PACKED references**. No material, name, or actual XMA/audio sample resolved by these counts.
- No native FF created or modified. Status: AZTEC_REAL_PC_FLAMETABLE_PARENT_DEPENDENCIES_CLASSIFIED_NOT_NATIVE_ASSET_CLOSED.

A stricter next read-only original-PC source witness, tools/oat-xenon/Run-AztecWeapon63FlameInlineRealSourceBodies.ps1, uses the existing genuine 7b walker's physical ownership order. The typed collector records:

- Each **actually INLINE FlameTable name** at parent byte +424 with its genuine original-PC 7b physical byte offset, byte length and SHA-256. Packed name references remain unresolved.
- Each **actually INLINE Material child** at parent +428..+456 with the *entire consumed original-PC 7b typed serialized child extent*, byte length and SHA-256. The raw owned span is evidence, not proof that the Material's technique/images/stream dependencies are native runtime complete.
- Parent and name/material pointer classes MUST conserve exactly four non-NULL names, 20 non-NULL Materials and 12 authentic NULL Materials. No invented bodies, empty replacement textures, or false native binding.
- Existing 4,514,599-byte 222/228-record OAT-good FF hash, original-PC proof containers and previous parent/dependency reports are preconditions. Output goes to a **new** source-only report folder, not the original FlameTable witness or any native FF.

To run from repo root:

    git pull --ff-only
    git rev-parse --short HEAD
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1"
    powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\oat-xenon\Run-AztecWeapon63FlameInlineRealSourceBodies.ps1"

The result will report precisely how many of the 4 FlameTable names and 20 non-NULL Material pointers are *physically INLINE in genuine original-PC source*. Non-INLINE pointers require real source target resolution; a Material body still needs actual converted technique/image dependencies and Xbox semantic validation. **No new FF or Xbox deployment is authorized.**
