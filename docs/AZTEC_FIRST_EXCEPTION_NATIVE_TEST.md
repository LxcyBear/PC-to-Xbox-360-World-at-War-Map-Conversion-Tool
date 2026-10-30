# Aztec first real native-PC2XB3 experiment — no reference fastfile substitution

**Scope:** Manual Xbox 360 / CodXe first-exception diagnostic, not a gameplay-ready map, production approval, or asset-graph closure. Explicit user choice to run native diagnostics today is separate from strict `Approve-AztecNativeSmokeTest.ps1` and its full-source world, memory and release requirements. Do not weaken that gate.

## Which file is genuine PC2XB3 output?

The OAT-clean `hardware_test_main_xanim_native_layout_weapon_real_scalars` variant is the 451,050-byte **structural diagnostic** from Candidate matrix `output/multi_map_matrix/runs/run_20261030_174448_98e189/multi_map_matrix.json`. This file includes 71 real weapon names and 4,189 real source numeric fields but **not** all gameplay, real World/GfxWorld, XModels, sounds, FX or animation links. Its XFile readback and native Xenon OAT List and Extract are independently clean, zero warnings and zero errors. The primary proof-heavy FF is OAT-rejected and must not be used in this canary.

The source of the misleading 'Aztec works' hardware claim was the separate 44-file `output/codxe_beta_stage/nazi_zombie_aztec_20261030_160821_79b25d10` folder, a SHA-preserved **native reference** copy. **NEVER use any .ff from that tree in this experiment.**

## Minimal console folder

From the staging script, a unique non-overwriting path:

    output/native_debug_stage/aztec_first_exception_<id>/_codxe/usermaps/nazi_zombie_aztec/
        nazi_zombie_aztec.ff       (source-owned PC2XB3 diagnostic, SHA from matrix)
        nazi_zombie_aztec_load.ff  (new original-PC-selected converter output, OAT List+Extract 0/0)
        map.arena                 (optional, only if it exists in explicitly sourced metadata)

Also optionally `mod.arena`, `map.json` from original-PC or explicitly supplied *metadata-only* source. No `preview.dds` required for the FF-load probe. No `.pak`, `*_patch.ff`, `*_load.bik`, `highmip/`, sounds or other files are borrowed, generated, or invented; these remain separate conversion obligations.

## Windows PowerShell 5.1 commands from repo

```powershell
git switch xbox360/aztec-two-xmodel-diagnostic
git pull --ff-only
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1'
$matrix = '.\output\multi_map_matrix\runs\run_20261030_174448_98e189\multi_map_matrix.json'
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Stage-AztecFirstExceptionCanary.ps1' -MatrixJson $matrix
powershell -NoProfile -ExecutionPolicy Bypass -File '.\tools\oat-xenon\Stage-AztecFirstExceptionCanary.ps1' -MatrixJson $matrix -Stage
```

Optional if you need an existing `map.arena` for CodXe menu visibility: append `-MetadataDirectory 'C:\path\to\known\Aztec\metadata\folder'`. This copies **only** `map.arena`, `mod.arena`, or `map.json` (when missing), never any FF/PAK/assets from that folder. The JSON manifest records the metadata source and SHA. If CLI DLL is missing, first build it once: `dotnet build .\src\WaW360.Cli\WaW360.Cli.csproj -c Release`.

The staging script (1) locates the exact SHA-matched matrix Candidate; (2) requires SHA-matched independent OAT LIST + EXTRACT results; (3) builds selected original-PC `*_load.ff` using `aztec-native-test-load-emit`; (4) verifies internal readback and SHA, then native OAT LIST + EXTRACT on the newly emitted load; (5) copies two FFs into a **fresh unique debug-only** path, rechecking both hashes; (6) writes provenance and first-exception instructions **outside** the Xbox-visible map directory. It does not auto-deploy, overwrite an Xbox map, copy a native reference FF, or certify release.

## Manually testing on real hardware

1. BACK UP the existing `/_codxe/usermaps/nazi_zombie_aztec/` folder and its FF hashes. Do not merge test files into a reference package. Make a new clean target map folder containing only the staged debug package files (plus optional metadata); confirm the local/installed `nazi_zombie_aztec.ff` SHA is identical.
2. Start WV first-exception capture and xbWatson; preserve full CodXe logs and Title Update/codxe version. Manually run the map once.
3. Record furthest checkpoint (menu entry, loading screen, first GSC error, return to main menu, crash/freeze) and first exception address/module/context. **Expected failure is not a problem if we get a reproducible first fault.**
4. Return the logs and staged `PROVENANCE_NATIVE_DEBUG_NOT_RELEASE.json`; attribute success only to genuine PC2XB3 hash-matched files, never the native reference staged copy.

**Important:** This canary is incomplete by design. A premature return to menu cannot on its own identify whether failure is missing World/GfxWorld, absent sound/patch, script error, bad weapon dependency, or CodXe fastfile interception; the logs and first-fault context discriminate them. If it cannot start without `map.arena` or other metadata, add only verified metadata and run again, keeping exact hash provenance.
