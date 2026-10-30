# PC2XB3 — recipe implementation contract (2026-10-30)

**Single technical reference:** user-supplied `WaW_PC_to_Xbox360_codxe_porting_knowledge.txt`, documenting the WaW360Tool's Xbox 360 + CodXe rules. Follow detailed Part 2 when it differs from Part 1. This checklist tracks PC2XB3, not WaW360Tool: the source's `[AUTO]` tags do not signify a working PC2XB3 feature.

## Required gates

| Step | Requirement | PC2XB3 status |
|---|---|---|
| 0 | Cache Xbox/PC stock/game/campaign/mod asset names and types | PARTIAL: existing PC inputs and retail file-location index; full stock XAsset inventory pending |
| 1 | Parse map FF, patch, mod and IWDs. Honor map-owned and later-alphabetical IWD loose-script priority | PARTIAL: **new** IWD virtual-path and collision index; **new** diagnostic RawFile override path for exact GSC/CSC names; Windows test pending |
| 2 | Follow actual running scripts, entities, weapons and world dependencies | OPEN: incomplete runtime reachability and typed closure |
| 3 | First map-owned assets; proved native Xbox stock; PC stock; campaign; extra assets; other PC maps only filling gaps | OPEN: not enforced for every asset family |
| 4 | Truly convert Xenos textures, material/shaders, XModels, XAnims, weapons, sound/XMA, FX, world/collision, rawfiles | OPEN: partial serializers and diagnostic shells do not qualify |
| 5 | Apply console script compatibility fixes and complete animtree closure | OPEN |
| 6 | Check actual Xbox memory and asset pools; gently reduce less-important assets until fitting | OPEN |
| 7 | Emit native map FF + REQUIRED load FF + CodXe map.json/preview.dds + external SDNS/XMA streams when needed; verify references | OPEN |
| 8 | Report every source, substitution, loss and likely load verdict; test real console | OPEN |

## Source-backed invariants

- An IWD is only a PC source archive, never an Xbox runtime file. Same-path loose GSC/CSC from an IWD overrides an FF rawfile; the alphabetically later map-owned IWD wins among loose IWDs.
- Only exact names in Xbox game-resident `common.ff`, `code_post_gfx.ff`, `patch.ff` may be referenced as already loaded. UI zones do not remain loaded during gameplay.
- Other console ports can provide **only shader/techset templates**; not arbitrary images, models, sounds, weapons, effects, or scripts.
- `<packed-name@...>` is a forensic label for unresolved weapon identity, not a valid WeaponDef.
- The document's standard CodXe package is `<map>.ff`, `<map>_load.ff`, `map.json`, `preview.dds`, plus `sounds/**/*.xma` when streamed sound is required. Do not infer `.pak`, `.bik` or `highmip/*.hi` as mandatory from this document.
- FF size increases, OAT listing, successful full extraction, independent readback, and Xbox hardware gameplay are separate milestones. Diagnostic FFs must not be deployed as production output.
- PC source FFs and IWDs are read-only.

## Windows verification — first recipe Source PASS (October 30)

The user executed commit `3c476a8` with `-Mode Source -IncludeMaps nazi_zombie_FRUmansion,nazi_zombie_aztec_V2.2`: **both maps passed** `linker-v2-source`; the .NET shared engine compiled and emitted no Xbox FFs in Source mode, as expected.

Aztec IWD source report: `IWDs=1; entries=74; unique loose scripts=3; script collisions=0; errors=0`. FRUmansion also passed, but the pasted log truncated its detailed counts. These are SOURCE evidence only, not confirmation that script bytes were overridden in a candidate Xbox FF.

Subsequent development adjustments (Windows untested): Source mode calls `linker-v2-source` directly because it already runs `NormalMapAnalyzer` and writes the same analyzer reports; the former redundant `stage4-normal-analyze` process is removed. The recipe index now limits script winners to `.gsc`/`.csc` (not `.atr`, `.cfg`, `.txt`). The RawFile builder reads the last duplicate path within a winning ZIP, matching the source-index tie-breaker. Candidate logs surface `ScriptClosureIwdScriptsOverridden` and `scriptClosureReady` directly.

**Next targeted Aztec Candidate diagnostic, NOT a release:**

```powershell
git pull --ff-only
if ($LASTEXITCODE -ne 0) { throw 'Git pull failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PowerShell syntax preflight failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapMatrix.ps1' -Mode Candidate -OatVariants Primary -IncludeMaps nazi_zombie_aztec_V2.2
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapOatDifferential.ps1' -OatMode List -IncludeMaps nazi_zombie_aztec_V2.2 -VariantNames hardware_test_main_xanim_native_layout_script_closure
```

Check the matrix for `[MULTIMAP-RECIPE] ... scriptClosureReady=...`, `RECIPE IWD script-closure overrides=N`, emitted FF path/size, and OAT `--list` summary. A script-closure FF may be absent if typed closure cannot build or the index finds no applicable overrides; that is evidence to debug, not a PASS. Full OAT extraction and Xbox hardware testing are separate gates.

## October 30 — packed WeaponDef name-audit UInt32 hotfix

User ran `Run-WaWMultiMapWeaponNameAudit.ps1` after `cf2a3ed`; syntax preflight PASS 5/5. Audit stopped at proof record #3 with `Weapon proof pointer is null or special sentinel at record #3` BEFORE inspecting original PC FFs. This was a **Windows PowerShell 5.1 diagnostic arithmetic bug**, not a demonstrated invalid source pointer. The code compared a `UInt32` pointer against `0xFFFFFFFE` using `-ge`; WinPS 5.1 parses the high-bit hex literal as signed **-2**, making valid high-bit block 4 pointers compare greater than -2.

Fix: compare against exact unsigned sentinels parsed via `[Convert]::ToUInt32('FFFFFFFE',16)` and `[uint32]::MaxValue`, rather than an ordered comparison. Added a no-I/O decoder regression on known Aztec raw pointer `0x83445D65` => `Virtual block 4`, offset `0x03445D64`. This is still not actual name-string recovery. No original map/weapon source files are altered. New Windows runtime validation pending.

Use the existing read-only command after `git pull --ff-only`:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PowerShell syntax check failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapWeaponNameAudit.ps1' -IncludeMaps nazi_zombie_aztec_V2.2
```

## Windows verification — actual Aztec IWD script payload conversion (October 30)

User ran commit `afbffb5` on Windows 10 with **one-map Candidate** `nazi_zombie_aztec_V2.2` after recipe Source PASS:

- `linker-v2-main-structural => PASS (0)`.
- `RECIPE IWD script-closure overrides=6`: six preexisting FF RawFile roots were rebuilt using the authoritative winning IWD script bytes (3 distinct loose GSC/CSC source script paths were recorded in preceding source inventory). This is an **actual content change**, not a shell-size adjustment.
- `scriptClosureReady=True`; `residentMenusRemoved=0`; `rawFilesPromoted=0`; `readBackErrors=0`; `wrapperRoundTrip=True`.
- Script-closure `.ff` **449,870 bytes**, while the tracked-shell control was **449,763 bytes**. Size delta **+107 bytes**, not a quality score.
- Independent native Xenon OAT `--list` of the script-closure `.ff`: **exit 0**, `Finished with 0 warnings, 0 errors`.
- The primary proof-heavy FF remained **6,205,444 bytes**, full OAT extraction still rejected real unresolved `weapons/<packed-name@...>` labels. **No playable Xbox output approved**.

**Regression retained:** Linker V2 `VerifyReadBack` already compares each emitted payload byte-for-byte against the contiguous rebuilt zone, then the Xbox zlib wrapper round-trips. Reuse those checks; do not add weaker duplicate checks.

**Next priority (read-only, no extra build):** inspect unresolved original-PC WeaponDef internal-name pointers with `Run-WaWMultiMapWeaponNameAudit.ps1 -IncludeMaps nazi_zombie_aztec_V2.2`. The audit now decodes the packed T4 pointer's block and offset (3 block bits, 29 offset bits, +1 bias), while marking the real string name **unresolved** until the virtual-block target is traced. Do NOT replace synthetic labels with arbitrary filenames. This added decoder has not yet passed Windows syntax/runtime testing.

```powershell
git pull --ff-only
if ($LASTEXITCODE -ne 0) { throw 'Git pull failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
if ($LASTEXITCODE -ne 0) { throw 'PowerShell syntax preflight failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapWeaponNameAudit.ps1' -IncludeMaps nazi_zombie_aztec_V2.2
```

## Focused Windows preflight (no FF rebuild)

```powershell
git pull --ff-only
if ($LASTEXITCODE -ne 0) { throw 'Git pull failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapMatrix.ps1' -Mode Source -IncludeMaps nazi_zombie_FRUmansion,nazi_zombie_aztec_V2.2
```

The runner reports `[MULTIMAP-RECIPE]` counts and the `recipe_source_precedence/<map>_recipe_iwd_precedence.json` location under each run's conversion folder. Source mode only indexes precedence; `AppliedToXenonPayload=False`. The distinct script-closure **Candidate** diagnostic now reads that indexed evidence and applies matching GSC/CSC IWD winners to existing RawFile roots and newly promoted missing scripts. Both source index and script-closure changes still require Windows compile/test before being treated as verified.

Update this file from actual logs, not speculative success.
