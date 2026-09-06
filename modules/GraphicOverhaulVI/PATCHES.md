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
- **Purpose:** observe GTA V Enhanced's real DX12 presentation path before any 3D display-mapper injection is attempted.
- **Required observations:** swapchain format, width/height, buffer count, Present/resize behavior, `SetColorSpace1` calls, `SetHDRMetaData` calls and HDR10 metadata, plus enough lifecycle information to distinguish SDR and HDR paths.
- **Implementation direction:** native x64 ASI/DXGI probe, logging only. No shader replacement or image modification in P0005.
- **Safety rule:** P0005 must not alter output pixels. Its only purpose is to identify the correct insertion point for the later ColorCoreVI mapper.
- **Validation gate:** build passes Windows CI; the user's GTA V Enhanced run produces a log that identifies the active swapchain/output color path in SDR and, ideally, HDR.

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
