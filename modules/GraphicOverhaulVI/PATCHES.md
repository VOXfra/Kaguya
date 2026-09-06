# GraphicOverhaulVI — Patch ledger

This is the detailed historical ledger behind the compact patch table in `README.md`.

## P0001 — Scope, production order and durable project state

- **Date:** 2026-09-06
- **Status:** `APPLIED`
- **Area:** project governance
- **Purpose:** stop parallel experimentation from fragmenting the GTA V → VI project and make GraphicOverhaulVI the active production priority.
- **Changes:** created the master TODO, locked the work order beginning with ColorCoreVI, moved complex behavior outside the active graphics scope, created validation gates/checkpoints, reference-mod policy and autonomous full-release rule.
- **Files:** `README.md`, `CHECKPOINTS.md`.

## P0002 — ColorCoreVI evidence scanner v0.1.x

- **Date:** 2026-09-06
- **Status:** `APPLIED`
- **Area:** ColorCoreVI / reverse mapping
- **Purpose:** read-only inventory of likely color/HDR/tonemap resources in local FH6 and GTA V Enhanced installations.
- **Final version:** `0.1.5`
- **Historical fixes:**
  - v0.1.2: fixed PowerShell interpolation parser issue;
  - v0.1.3: replaced broken embedded CMD/PowerShell preflight;
  - v0.1.4: exposed a zero-argument launcher bug that could falsely report success;
  - v0.1.5: explicit CMD branches, real zero-argument CI path, stale-output deletion, mandatory fresh summary and regression guards.
- **Windows validation:** workflow `34041346603`, job `101508557706`, Windows PowerShell 5.1, exact zero-argument path PASS.
- **User-machine validation:** FH6 `C:\XboxGames\Forza Horizon 6\Content`; GTA V Enhanced `E:\Jeux Epic\GTAVEnhanced`; 16,688 files indexed; 3,367 candidates; 0 scan errors; ~56 s runtime.
- **Result:** enough native-path evidence to stop broad scanning and move to targeted FH6 containers.

## P0003 — FH6 Color Pipeline Evidence Collector v0.1.0

- **Date:** 2026-09-06
- **Status:** `APPLIED`
- **Area:** ColorCoreVI / FH6 reference pipeline
- **Purpose:** inspect actual native FH6 color/post-processing containers selected by P0002.
- **Windows validation:** workflow `34042571530`, job `101511864585`, Windows PowerShell 5.1, exact no-argument launcher and deterministic fixture extraction PASS.
- **User-machine result:**
  - 15/15 requested native targets found;
  - 12,583 archive entries indexed;
  - 1,074 evidence hits;
  - 395 selected files extracted;
  - 1,311,692 original bytes extracted;
  - source files modified: false;
  - 244 collector errors, all from `media\Camera.zip` entries using ZIP compression method 22, unsupported by the PowerShell/.NET collector. The core ColorCore evidence was not blocked.
- **Confirmed native structure:**
  - 42 creative/color-grade `.lut` files in `colourgrades.zip`;
  - separate `DefaultSDR.lut` and `DefaultHDR.lut` in `displaymappers.zip`;
  - all 44 inspected LUTs use a 32×32×32 RGBA16F cube with header `(0,32,100.0)`;
  - `Default.lut` exposes the large HDR-domain sampling lattice through 100.0;
  - SDR and HDR display mappers perform 3D highlight/chroma compression rather than independent channel clipping;
  - loose time-of-day/default-track configs expose adaptive-exposure, tonemap delay and filmic-curve parameters;
  - `TimeOfDayA.xml` exposes dynamic day/night grade blend weights.
- **Open FH6 evidence items:** final output transfer/runtime shader ordering is not yet proved; `TimeOfDayB.xml` should be collected in a later follow-up; `Camera.zip` method-22 decoding is deferred unless required.
- **Detailed record:** `ColorCoreVI/FH6_FINDINGS.md`.
- **Files:** `tools/Collect-FH6ColorEvidence.ps1`, `tools/Test-FH6ColorEvidence.ps1`, `RUN-FH6-COLOR-EVIDENCE.cmd`, `.github/workflows/test-colorcorevi-p0003.yml`.

## P0004 — GTA V Enhanced Color Pipeline Evidence Collector v0.1.1

- **Date:** 2026-09-06
- **Status:** `APPLIED`
- **Area:** ColorCoreVI / GTA V Enhanced baseline mapping
- **Purpose:** extract GTA V Enhanced's relevant color/timecycle/weather/post-processing configuration from encrypted RPF7 archives and compare it against the confirmed FH6 architecture.
- **Primary archives:** `common.rpf`, `update\update.rpf`, `update\update2.rpf`; `mods` overrides intentionally excluded from the vanilla baseline pass.
- **RPF reader:** pinned MIT `wjy000/gtav-enhanced-rpf` revision `c09f1f1b15f7a0ebe77f852c44f8aca3c9e358a7` with pinned `pycryptodome==3.23.0` runtime and `magic.dat` integrity check.
- **Key hygiene:** Enhanced AES/NG material derived from local `GTA5_Enhanced.exe` in memory only; no key-save path used; a second hard guard rejects/deletes output if known GTA key filenames appear.
- **Windows validation:** exact zero-argument CMD bootstrap, Windows PowerShell 5.1, Python 3.11, pinned reader download/integrity, dependency install/import, anti-false-success and key guards all PASS before release.
- **User-machine validation:**
  - GTA root `E:\Jeux Epic\GTAVEnhanced`;
  - 3/3 RPFs opened with NG encryption;
  - 2,913 files indexed;
  - 98 selected candidates;
  - 98 extracted files / 7,448,342 bytes;
  - 0 collector errors;
  - key derivation in memory only;
  - key files written: false;
  - source files modified: false.
- **Confirmed findings:**
  - separate bright/dark filmic A–F/W parameter families already exist;
  - GTA has explicit exposure and adaptation configuration;
  - Enhanced changes `Adaptation.min.step.size` from `0.15` to `0.0001` and adds an `Adaptation.hdr.*` family;
  - Enhanced adds HDR10/dynamic dithering plus `hdr.game.useITMBlend` and dedicated game/UI HDR color-control blocks;
  - weather files expose 58 distinct `postfx_*` controls, usually as 13-value time-of-day tables, including exposure/min/max, independent bright/dark filmic overrides, RGB correction/shift/gradient shaping, bloom and optical post-FX;
  - `timecycle_mods_*` can override the same stack contextually.
- **Architecture result:** preserve Rockstar weather/timecycle/mission orchestration; add/replace the final technical mapping stage instead of replacing the entire ColorCore stack.
- **Important unknowns:** exact meaning of `ITM`, final SDR/HDR transfer, PQ/BT.2020/scRGB semantics, final output shader and the location where a 3D display mapper can be inserted.
- **Detailed record:** `ColorCoreVI/GTA_ENHANCED_FINDINGS.md`.
- **Files:** `tools/Collect-GTAVColorEvidence.ps1`, `tools/Collect-GTAVColorEvidence.py`, `tools/Test-GTAVColorEvidence.ps1`, `RUN-GTAV-COLOR-EVIDENCE.cmd`, `.github/workflows/test-colorcorevi-p0004.yml`.

## P0005 — GTA V Enhanced DX12 Color Output Probe

- **Date:** 2026-09-06
- **Status:** `IN PROGRESS`
- **Area:** ColorCoreVI / runtime output-stage mapping
- **Purpose:** identify GTA V Enhanced's real presentation/color-output chain before any 3D display-mapper injection.
- **v0.1.0:** public `Present/ResizeBuffers/SetColorSpace1/SetHDRMetaData` hooks compiled and passed CI, but the user's GTA run only reached `HOOKS_READY`; no runtime traffic was intercepted.
- **v0.1.1:** added `Present1/ResizeBuffers1`; CI explicitly exercised those methods, but the user's GTA run again stopped at `HOOKS_READY` with no runtime traffic.
- **v0.1.2:** added hardware-derived method targets plus global `IDXGIFactory::CreateSwapChain*` and `CreateDXGIFactory*` interception. CI passed and captured a real harness swapchain. **User-machine result: REJECTED.** GTA V Enhanced crashed before the first rendered image. The probe log reached hardware adapter detection (`vendor=0x10DE`, `device=0x2783`, ~12.58 GB dedicated VRAM), factory/swapchain target discovery, export-hook targets and `HOOKS_READY`, then stopped. ASI Loader, RageOpenV and ScriptHookV all initialized normally. No `REAL_SWAPCHAIN_CREATED` line was reached. This isolates the regression to the v0.1.2 factory/export-hook strategy rather than ASI loading or the pre-existing mods.
- **Important v0.1.2 observation:** hardware and earlier WARP dummy swapchains resolved the same public DXGI `Present/Present1` implementation addresses on the user's system. The previous lack of traffic therefore cannot be explained simply by WARP-vs-hardware method addresses.
- **Proxy/interposer hypothesis:** NVIDIA Streamline documentation states that DLSS-G integrations can expose proxy `IDXGIFactory`/`IDXGISwapChain` interfaces and warns third-party tools not to treat SL proxies as native interfaces. GTA V Enhanced supports DLSS/Frame Generation, so a pre-existing Streamline/NVIDIA presentation proxy is a high-priority explanation for both the missing public-DXGI traffic and the v0.1.2 startup crash. This remains a hypothesis until the real process chain is inspected.
- **v0.1.3:** removed all hooks and used an in-process passive ASI. User-machine result: the probe started, reported `NO API hooks`, observed module counts `60 -> 84 -> 97`, but classified `0` interesting modules and found `0` relevant import DLLs/symbols through t5. The log then ended before t10/t20/t60. ASI Loader, RageOpenV and ScriptHookV initialization logs remained normal. The files alone do not prove whether the GTA process was manually closed or exited/crashed, but the requested observation window was not reached. v0.1.3 CI was also found to be insufficient because it asserted generic markers (`MODULE_SCAN`, `IAT_SUMMARY`) rather than requiring actual DXGI/D3D12 detection.
- **v0.1.4 strategy:** remove the diagnostic code from GTA completely. `ColorCoreVIExternalProbe.exe` is a standalone x64 monitor that waits for `GTA5_Enhanced.exe`, opens it with read/query rights only, snapshots loaded modules and reads PE import/IAT metadata using `ReadProcessMemory`. No ASI/DLL injection, hooks, vtable patching or process writes are used.
- **v0.1.4 strict CI:** workflow `34052161734` PASS. A deterministic fixture process named `GTA5_Enhanced.exe` actually loads `dxgi.dll`, `d3d12.dll` and a fake `sl.interposer.dll`; the external probe must detect all three exact module names, parse relevant `dxgi.dll` and `d3d12.dll` import DLLs, emit IAT symbols, reach `PROBE_COMPLETE`, and pass an x64 PE check. CI additionally rejects source containing `WriteProcessMemory`, `VirtualAllocEx`, `CreateRemoteThread`, `SetWindowsHookEx`, `MH_CreateHook` or `MH_EnableHook`.
- **v0.1.4 release hashes:** ZIP SHA-256 `3d81c1dbb88a010ab7a3f1f5e33dac5053cb35ad9c3d4d9c93cfbbdedb29706c`; EXE SHA-256 `daaa80c650dd1f47a92e257ad975da66e6d94d295420ff5fc9a429a8f5289086`.
- **Validation gate:** user must remove the old ASI entirely, run the standalone v0.1.4 monitor, then launch GTA normally. The returned `ColorCoreVI_ExternalProbe.log` must identify the actual DXGI/D3D12/Streamline/NVIDIA module chain or explicitly record an early process exit.

## Patch template

```text
## PXXXX — Name
- Date:
- Status: PLANNED | IN PROGRESS | TEST | APPLIED | REJECTED | SUPERSEDED
- Area:
- Purpose:
- Changes:
- Files:
- Test result:
- Validation gate:
- Supersedes / superseded by:
```
