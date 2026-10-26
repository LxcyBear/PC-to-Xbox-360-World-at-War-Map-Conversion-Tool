# Xbox 360 Xenon integration

This branch adds a pinned external OpenAssetTools Xenon source/build layer, format contract checks, and PowerShell GUI release publishing. It does not yet convert arbitrary PC maps into playable Xbox content.

## Windows build

Use PowerShell from the repository root:

    powershell -ExecutionPolicy Bypass -File .\Build-Converter.ps1 -Clean

GUI output goes to src\WaW360.Gui\Release under the repository checkout. If the checkout is at C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\Converter, that output resolves to C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\Converter\src\WaW360.Gui\Release.

To build the pinned GPLv3 OAT Xenon x86 dependency as a separate executable, run:

    powershell -ExecutionPolicy Bypass -File .\Build-Converter.ps1 -WithOat

Prerequisites: Git, .NET 8 SDK, Visual Studio 2026 with C++ toolchain. OAT revision 3beaa7c8 is cloned under ignored .deps/OpenAssetTools-xenon. Its x86 Linker.exe and Unlinker.exe should land under build\bin\Release_x86 within the OAT checkout. The source and game assets are not redistributed in this project.

If your original Converter folder is not a Git checkout, clone this branch into a separate sibling folder and point the published GUI at the original Release directory without changing source files:

    git clone --branch xbox360/oat-xenon-integration https://github.com/LxcyBear/PC2XB3-WaW-Converter.git ..\PC2XB3-WaW-Converter
    cd ..\PC2XB3-WaW-Converter
    powershell -ExecutionPolicy Bypass -File .\Build-Converter.ps1 -Clean -WithOat -OutputDirectory "C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\Converter\src\WaW360.Gui\Release"

External Release directories are published in-place and are never recursively deleted by -Clean. Only the release outputs may be overwritten. Keep your existing Converter source folder as a backup until you compare it with GitHub.

## Exit gates

| Milestone | Status | Completion proof |
| --- | --- | --- |
| 1. Xenon integration | Initial scripts and contract checks committed; Windows build untested | Pinned OAT x86 build and schema validation pass |
| 2. PC-to-Xbox translation | Not complete | Real typed asset graph, platform shaders, textures, animations, geometry, collision, sounds, and no substitute payloads |
| 3. First playable custom Zombies map | Not tested | Independent readback and real Xbox 360 TU7 gameplay test |
| 4. Confident multi-map conversion | Not tested | Repeatable full conversion and hardware tests of Tower, Simpsons, Fortress |

Current NormalMapLinkerV2StructuralMainEmitter deliberately emits reduced diagnostic payloads and always reports ProductionMainReady=false. NormalMapLinkerV2TypedClosurePlan says TechniqueSet closure is pending. NormalMapLinkerV2MainReadiness hardcodes typed closure and real block allocation as unfinished. Do not bypass these guards to claim playability.

OpenAssetTools is not an automatic PC T4 to T4Xenon converter. Its per-game asset pools are distinct, and real Xbox graphics require native Xenos shaders rather than renamed PC DX9 bytecode.

## Native-oracle convergence strategy

The primary Linker V2 strategy is now **structural reproduction of a known-working native Xbox graph**, not repeated reduced-payload hardware permutations.

For a user-supplied, known-working Xbox fastfile (for example a native conversion of the same PC custom map), the pinned T4Xenon OAT `Unlinker.exe --list` is used as a loader/fixup oracle. The converter records the exact native XAsset identity sequence:

- root index / insertion order;
- Xbox XAsset type;
- exact resolved asset name;
- duplicate identity behavior;
- ScriptString table identity/order evidence.

The first convergence target is exact `(index,type,name)` identity parity. The following targets are dependency edges, ScriptString index remaps, type-specific platform layouts, pointer/fixup relationships, deduplication, and cross-asset reference parity. Native files are **analysis/oracle inputs only**; their proprietary payload bytes are never embedded in or redistributed by the converter.

Use:

    dotnet run --project .\src\WaW360.Cli\WaW360.Cli.csproj -c Release -- native-oat-identity "<native.ff>" "<output-dir>"

The command auto-discovers the pinned Xenon OAT build under `.deps\OpenAssetTools-xenon\build\bin\Release_x86\Unlinker.exe`. If OAT is not built, run:

    powershell -ExecutionPolicy Bypass -File .\tools\oat-xenon\Prepare-OatXenon.ps1 -Build

The generated native identity manifest is a deterministic convergence oracle, not a donor payload source.

The next feature is an explicit typed source-to-Xenon intermediate graph that is measured against this native identity/dependency oracle, followed by faithful writers, independent readback, and hardware validation.

GPLv3 warning: keep the OAT program separate, or review distribution and license obligations before linking or copying its implementation. Never commit game FF/IWD files or native shader binaries.
