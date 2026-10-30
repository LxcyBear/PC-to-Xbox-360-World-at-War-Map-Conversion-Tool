# Backrooms — Call of Duty WaW PC to Xbox 360 intake

This is a NEW map family. Branch xbox360/backrooms-intake starts from the last tested real-scalar FF pipeline (commit 3dd403bddfab48b7b0914670379e8ed3878f2f5a), separate from the Aztec diagnostic and reference-package staging artifacts; no original PC2XB3 Aztec fastfile has been verified playable on Xbox hardware.

Different maps have used the Backrooms label. Examples are nz_backroom_prototype (Backroom Trapped) and nz_backroom_prototype1 (Backrooms Lost). Do not assume either is the user's selected map; identify the main FF from the local files.

## Source discovery (Windows PowerShell 5.1)

1. git fetch origin
2. git switch --track origin/xbox360/backrooms-intake
3. powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\oat-xenon\Test-WaWMultiMapScriptSyntax.ps1
4. powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\oat-xenon\Run-WaWBackroomsIntake.ps1 -Mode Inspect

The default source roots are ..\PC Maps alongside the repo and %LOCALAPPDATA%\Activision\CoDWaW\mods. Use -SourceRoots with the real installed map folder if it is elsewhere. The read-only intake records FF/IWD size and SHA and reports possible main FF names. A ZIP or EXE is NOT a decoded fastfile. Inventory report: output\backrooms_intake\backrooms_intake_*.json.

## Source conversion

After identifying the exact PC main FF basename, substitute it for MAP_NAME in:

powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\oat-xenon\Run-WaWBackroomsIntake.ps1 -Mode Source -MapName MAP_NAME

Only once source blockers are understood, run the explicit diagnostic Candidate stage:

powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\oat-xenon\Run-WaWBackroomsIntake.ps1 -Mode Candidate -MapName MAP_NAME

Candidate uses the existing shared .NET engine, progress reporting, compressed FF size history and primary OAT diagnostics. All diagnostic FFs remain withheld from Xbox deployment. There is NO automatic package install.

## Conversion rules

- No Aztec witness, scalar or ordinal environment variables can be imported into Backrooms. Intake temporarily clears and then restores them.
- Do not copy unrelated Xbox donor/oracle bytes into production. Source provenance and native Xbox asset identity need independent proof.
- Preserve the 451,050-byte OAT-extracted Aztec scalar diagnostic and its SHA separately from the native reference-package stage. No playable PC2XB3 Aztec build has been confirmed. Avoid accidental substitution of a staged retail/reference FF for converter output.
- OAT listing or extraction is not full XModel/material/image/FX/WeaponDef/audio/RawFile closure; monitor all unresolved root types, runtime dependencies and Xbox memory budget.
- Do not assert a map has never been converted based on its marketing name alone.
