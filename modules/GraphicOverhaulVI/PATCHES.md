# GraphicOverhaulVI — Patch ledger

This is the detailed historical ledger behind the compact patch table in `README.md`.

## P0001 — Scope, production order and durable project state

- **Date:** 2026-09-06
- **Status:** `APPLIED`
- **Area:** project governance
- **Purpose:** stop parallel experimentation from fragmenting the GTA V → VI project and make GraphicOverhaulVI the active production priority.
- **Changes:**
  - created the GraphicOverhaulVI master TODO;
  - locked the work order beginning with ColorCoreVI;
  - moved complex gameplay/behavior work outside the active graphics scope;
  - created validation-gate rules;
  - created the durable checkpoint system;
  - established the reference-mod policy;
  - established the autonomous full-release rule.
- **Files:**
  - `README.md`
  - `CHECKPOINTS.md`
- **Validation:** documentation/state consistency only; no in-game visual claim is attached to this patch.

## P0002 — ColorCoreVI evidence scanner v0.1.x

- **Date:** 2026-09-06
- **Status:** `IN PROGRESS`
- **Area:** ColorCoreVI / reverse mapping
- **Purpose:** build a reproducible, read-only map of likely color/HDR/tonemap resources in local Forza Horizon 6 and GTA V Enhanced installations before implementing visual changes.
- **Current version:** `0.1.3`
- **Changes:**
  - optional FH6 and GTA V Enhanced root auto-detection;
  - Steam library discovery;
  - Epic GTA V manifest discovery;
  - common XboxGames path discovery;
  - interactive folder prompt when auto-detection fails;
  - recursive file metadata inventory;
  - extension summary;
  - keyword detection for tonemap, HDR/HDR10, PQ/ST.2084, BT/Rec.2020, gamut, scRGB, linear, white point, exposure, bloom, color, LUT, gamma/sRGB, display, post-process, ACES and nits;
  - text-file content probing;
  - bounded ASCII/UTF-16 binary string probing for likely executables/shader containers;
  - SHA-256 hashing of reasonable-size candidates;
  - deliberate avoidance of blind hashing for huge `.rpf`/texture archives;
  - CSV/JSON reports and scan-error log;
  - no modification of either game.
- **v0.1.3 correction:**
  - fixes the Windows PowerShell parser error caused by interpolated `"$Game: ..."` strings by using explicit `${Game}` delimiters;
  - rewrites the extension-summary conditional into syntax safe for Windows PowerShell 5.1;
  - adds a parser preflight in `RUN-COLORCORE-SCAN.cmd` so syntax errors are detected before the scan starts.
- **Files:**
  - `tools/Scan-ColorCoreVI.ps1`
  - `RUN-COLORCORE-SCAN.cmd`
- **Expected output:**
  - `scan_summary.json`
  - `color_candidates.csv`
  - `extension_summary.csv`
  - `scan_errors.csv`
  - `file_inventory.csv`
- **Validation required before APPLIED:**
  - launcher preflight reports `Syntax OK` on the target Windows machine;
  - scanner completes against the local FH6 installation;
  - scanner completes against the local GTA V Enhanced installation;
  - no source game file is modified;
  - candidate report contains enough evidence to choose the next inspection/disassembly step.

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
