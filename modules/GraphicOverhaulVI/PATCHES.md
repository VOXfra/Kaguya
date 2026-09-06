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
- **Current version:** `0.1.4`
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
  - bounded ASCII/binary string probing for likely executable/shader containers;
  - SHA-256 hashing of reasonable-size candidates;
  - deliberate avoidance of blind hashing for huge `.rpf`/texture archives;
  - CSV/JSON reports and scan-error log;
  - no modification of either game.
- **v0.1.2 issue:**
  - Windows PowerShell rejected interpolated `"$Game: ..."` strings because the colon was parsed as part of a variable reference.
- **v0.1.3 corrections and issue:**
  - changed those strings to explicit `${Game}` delimiters;
  - rewrote an extension-summary conditional for Windows PowerShell 5.1;
  - attempted to add a parser preflight directly in the `.cmd` launcher;
  - that embedded preflight was invalid because `cmd.exe` escaping left a literal `^` before a PowerShell pipeline.
- **v0.1.4 corrections:**
  - moved parser validation into the standalone `tools/Test-ColorCoreVI.ps1` script rather than embedding PowerShell syntax inside CMD quoting;
  - removed the `$PSScriptRoot`-dependent default expression from the parameter declaration and resolves the default output path only after parameter binding;
  - simplified the scanner around Windows PowerShell 5.1-safe syntax;
  - added deterministic GitHub Actions validation on a real Windows runner using Windows PowerShell 5.1;
  - CI runs the exact `RUN-COLORCORE-SCAN.cmd` entry point against fixture FH6/GTA directories and validates all six generated reports.
- **Windows validation:**
  - environment: Windows Server 2025 runner, Windows PowerShell `5.1.26100.33296`;
  - standalone parser preflight: PASS;
  - exact CMD launcher: PASS;
  - full fixture scan: PASS;
  - required output files: PASS;
  - output content/count/error checks: PASS;
  - the first CI pass also caught a `$PSScriptRoot` runtime bug before v0.1.4 was released to the user.
- **Files:**
  - `tools/Scan-ColorCoreVI.ps1`
  - `tools/Test-ColorCoreVI.ps1`
  - `RUN-COLORCORE-SCAN.cmd`
  - `.github/workflows/test-colorcorevi-scanner.yml`
- **Expected output:**
  - `scan_summary.json`
  - `color_candidates.csv`
  - `extension_summary.csv`
  - `scan_errors.csv`
  - `file_inventory.csv`
  - `README.txt`
- **Validation required before APPLIED:**
  - scanner completes against the user's actual local FH6 installation;
  - scanner completes against the user's actual local GTA V Enhanced installation;
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
