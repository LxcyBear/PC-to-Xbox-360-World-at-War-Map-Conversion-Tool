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

## Focused Windows preflight (no FF rebuild)

```powershell
git pull --ff-only
if ($LASTEXITCODE -ne 0) { throw 'Git pull failed' }
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Run-WaWMultiMapMatrix.ps1' -Mode Source -IncludeMaps nazi_zombie_FRUmansion,nazi_zombie_aztec_V2.2
```

The runner reports `[MULTIMAP-RECIPE]` counts and the `recipe_source_precedence/<map>_recipe_iwd_precedence.json` location under each run's conversion folder. Source mode only indexes precedence; `AppliedToXenonPayload=False`. The distinct script-closure **Candidate** diagnostic now reads that indexed evidence and applies matching GSC/CSC IWD winners to existing RawFile roots and newly promoted missing scripts. Both source index and script-closure changes still require Windows compile/test before being treated as verified.

Update this file from actual logs, not speculative success.
