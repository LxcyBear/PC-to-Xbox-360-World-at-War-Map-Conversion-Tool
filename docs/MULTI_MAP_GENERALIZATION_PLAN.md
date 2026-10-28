# Multi-Map Generalization Plan

## Trigger

This plan becomes the primary development workflow immediately after the first FRUmansion Xbox 360 hardware test that reaches a meaningful working state.

FRUmansion then becomes the first regression fixture. It must not remain the permanent development target.

## Non-Negotiable Architecture

- Final production output is Xbox 360 based.
- User-owned retail Xbox 360 World at War fastfiles are the authoritative source for stock/base-game assets.
- PC map/dependency content is source provenance for genuinely custom content only.
- Native custom Xbox map files may be used for validation, ordering, ABI/layout evidence, and regression comparison, but never as production payload donors.
- Unsupported or unresolved content must fail closed and be reported explicitly. Do not silently synthesize production content.
- No hardware candidate is approved until its critical-content gates pass.

## Development Model: Family-First, Multi-Map

Do not complete one map end-to-end, stop on its first failure, patch it, rebuild it, and repeat.

Instead, enumerate the full map corpus and process one asset family across all maps. Every family pass must continue past per-map failures and write an aggregate report.

The working order is:

1. XAsset/root identity and source provenance
2. Images
3. Materials / technique bindings
4. XModels
5. XAnim
6. Weapons
7. FX
8. RawFiles / scripts / localize / map ents
9. World roots: GfxWorld, ClipMap, ComWorld, GameWorldSP
10. Audio / LoadedSound / external sound payloads
11. Final integrated emission and hardware validation

A later family may depend on earlier family artifacts, but one bad map must not abort the sweep for the rest of the corpus.

## Required Result Classes

Every asset encountered across every map must end in one of these lanes:

- `retail-runtime-reference`: exact stock identity proven resident through Xbox retail pools.
- `retail-payload`: stock Xbox asset whose payload must be extracted/relinked because runtime residency is not proven.
- `custom-converted`: map/dependency-owned PC asset converted to valid Xenon representation.
- `intentional-external`: content intentionally kept outside the main FF with a validated runtime contract.
- `blocked-unsupported`: safely rejected with an explicit reason and no production credit.

These classes replace ad-hoc per-map exceptions.

## Failure Aggregation

Each family sweep writes both per-map manifests and one global aggregate manifest.

Failures are grouped by normalized signature, not by map. At minimum aggregate:

- asset family
- serializer / converter stage
- exception or rejection reason
- offending field / pointer / child layout when known
- affected asset identities
- affected maps
- count of occurrences
- stock vs custom provenance
- candidate shared fix

Example:

```text
weapon:pointer:szDisplayName
  maps affected: 8
  roots affected: 43
  pattern: packed/source pointer remains after PC structural import
```

One shared fix is implemented and validated against every affected map before moving on.

## Build Cadence

Do not create one Git commit/build per individual failing asset.

Preferred cadence:

1. run an entire family sweep across all maps;
2. collect and cluster failures;
3. implement a coherent batch of fixes for the largest shared signatures;
4. run unit/static gates;
5. rerun the family sweep across all affected maps;
6. commit the batch when the shared behavior is proven.

Small canaries are allowed only when isolating a dangerous serializer/layout bug. Once isolated, the fix must immediately be exercised across the full affected map set.

## Regression Set

After FRUmansion first succeeds on hardware:

- FRUmansion remains a permanent regression map.
- Add at least two additional maps with meaningfully different content profiles as early regression fixtures.
- Every generalized family fix must preserve all previously passing fixtures.
- A map-specific workaround is allowed only when the asset format itself is genuinely map-specific and the reason is documented.

## Global Progress Matrix

Maintain one machine-readable matrix with rows = maps and columns = families.

Suggested states:

- `PASS`
- `PASS_WITH_REFS`
- `BLOCKED_SHARED_<signature>`
- `BLOCKED_MAP_SPECIFIC`
- `NOT_RUN`

The matrix is the project roadmap. Development priority is driven by the shared blocker that unlocks the largest number of map/family cells safely.

## Hardware Testing

Hardware testing is not the primary debugging loop for generalization.

Use OAT/linker/readback/identity/layout gates first. Xbox testing is reserved for candidates that have already passed the structural and critical-content gates.

When a hardware failure occurs, capture it as another cross-map signature and determine whether the same structural pattern exists in other maps before patching.

## Immediate Post-FRUmansion Work

As soon as FRUmansion reaches the first meaningful Xbox success:

1. freeze its current artifacts and manifests as regression evidence;
2. parameterize FRUmansion-specific wrappers into generic map-name / source-root / output-root tools;
3. enumerate every available PC map;
4. run root/XAsset census across the whole corpus without stopping on errors;
5. generate the global map-family matrix;
6. begin the Images sweep across all maps;
7. continue family-by-family using shared failure clustering.

The objective is a converter, not a collection of hand-fixed map ports.
