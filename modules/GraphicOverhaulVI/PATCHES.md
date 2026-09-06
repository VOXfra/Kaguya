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

## P0004 — GTA V Enhanced Color Pipeline Evidence Collector v0.1.x

- **Date:** 2026-09-06
- **Status:** `TEST`
- **Area:** ColorCoreVI / GTA V Enhanced baseline mapping
- **Purpose:** extract only GTA V Enhanced's relevant color/timecycle/weather/post-processing configuration from encrypted RPF7 archives so it can be compared directly with the confirmed FH6 architecture.
- **Primary archives:** `common.rpf`, `update\update.rpf`, `update\update2.rpf` from the original game install; `mods` overrides are intentionally excluded from the vanilla baseline pass.
- **Target evidence:** `visualsettings.dat`, timecycle XML/DAT resources, `weather.xml`, atmosphere configs and small filename-confirmed postprocess/tonemap/exposure/HDR/color-correction resources.
- **RPF reader:** pinned `wjy000/gtav-enhanced-rpf` revision `c09f1f1b15f7a0ebe77f852c44f8aca3c9e358a7`; upstream is MIT licensed and reports real Enhanced validation on `common.rpf`/`update2.rpf`.
- **Dependency:** isolated `pycryptodome==3.23.0` runtime.
- **Integrity:** pinned upstream `magic.dat` SHA-256 must equal `dc35981f822e892ced3aa81d31e7a96927d573ee28f67417592b5afeaf330832` before use.
- **Key hygiene:** Enhanced AES/NG material is derived from the local `GTA5_Enhanced.exe` in memory only. P0004 never calls the upstream key-save path. `stage3-output` has a second hard guard against `gtav_aes_key.dat`, `gtav_ng_key.dat` and `gtav_ng_decrypt_tables.dat`; if any appears, output is deleted and the run fails.
- **Source safety:** read-only on GTA files; output is written only beside the P0004 tool.
- **CI scope:** validates Windows PowerShell 5.1 launcher/bootstrap, Python 3.11 runtime, pinned upstream download/integrity, crypto import, Python collector syntax, anti-false-success behavior and secret guard. A CI fixture cannot prove extraction from Rockstar's real encrypted archives; that gate is completed by the user's real install run.
- **Files:** `tools/Collect-GTAVColorEvidence.ps1`, `tools/Collect-GTAVColorEvidence.py`, `tools/Test-GTAVColorEvidence.ps1`, `RUN-GTAV-COLOR-EVIDENCE.cmd`, `.github/workflows/test-colorcorevi-p0004.yml`.
- **Validation gate before APPLIED:** open at least one real target RPF, extract the expected baseline resources, confirm zero key artifacts and return a usable `stage3-output` for analysis.

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
