# Xbox 360 Xenon integration

This branch adds a pinned external OpenAssetTools Xenon source/build layer, format contract checks, and PowerShell GUI release publishing. It does not yet convert arbitrary PC maps into playable Xbox content.

## Windows build

Use PowerShell from the repository root:

    powershell -ExecutionPolicy Bypass -File .\Build-Converter.ps1 -Clean

GUI output goes to src\WaW360.Gui\Release under the repository checkout. If the checkout is at C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\Converter, that output resolves to C:\Users\jorda\OneDrive\Desktop\My Xbox\My Projects\World at War\Converter\src\WaW360.Gui\Release.

To build the pinned GPLv3 OAT Xenon x86 dependency as a separate executable, run:

    powershell -ExecutionPolicy Bypass -File .\Build-Converter.ps1 -WithOat

Prerequisites: Git, .NET 8 SDK, Visual Studio 2026 with C++ toolchain. OAT revision 1f7acb3347bc2e44deb609bc8cf7259bf027f176 is cloned under ignored .deps/OpenAssetTools-xenon. Its x86 Linker.exe and Unlinker.exe should land under build\bin\Release_x86 within the OAT checkout. The source and game assets are not redistributed in this project.

## Exit gates

| Milestone | Status | Completion proof |
| --- | --- | --- |
| 1. Xenon integration | Initial scripts and contract checks committed; Windows build untested | Pinned OAT x86 build and schema validation pass |
| 2. PC-to-Xbox translation | Not complete | Real typed asset graph, platform shaders, textures, animations, geometry, collision, sounds, and no substitute payloads |
| 3. First playable custom Zombies map | Not tested | Independent readback and real Xbox 360 TU7 gameplay test |
| 4. Confident multi-map conversion | Not tested | Repeatable full conversion and hardware tests of Tower, Simpsons, Fortress |

Current NormalMapLinkerV2StructuralMainEmitter deliberately emits reduced diagnostic payloads and always reports ProductionMainReady=false. NormalMapLinkerV2TypedClosurePlan says TechniqueSet closure is pending. NormalMapLinkerV2MainReadiness hardcodes typed closure and real block allocation as unfinished. Do not bypass these guards to claim playability.

OpenAssetTools is not an automatic PC T4 to T4Xenon converter. Its per-game asset pools are distinct, and real Xbox graphics require native Xenos shaders rather than renamed PC DX9 bytecode. The next feature is an explicit typed source-to-Xenon intermediate graph and faithful writers, followed by independent readback and hardware validation.

GPLv3 warning: keep the OAT program separate, or review distribution and license obligations before linking or copying its implementation. Never commit game FF/IWD files or native shader binaries.
