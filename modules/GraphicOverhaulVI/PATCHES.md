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
- **Current version:** `0.1.5`
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
- **v0.1.4 corrections and issue:**
  - moved parser validation into the standalone `tools/Test-ColorCoreVI.ps1` script rather than embedding PowerShell syntax inside CMD quoting;
  - removed the `$PSScriptRoot`-dependent default expression from the parameter declaration and resolved the default output path only after parameter binding;
  - simplified the scanner around Windows PowerShell 5.1-safe syntax;
  - added deterministic GitHub Actions validation on a real Windows runner using Windows PowerShell 5.1;
  - the CI test originally supplied FH6/GTA paths as command-line arguments;
  - the released `.cmd` contained a nested `if ... if ... (...) else (...)` structure which behaved differently when double-clicked with zero arguments: the first false IF skipped both the scanner call and the ELSE branch;
  - the preflight had returned exit code 0, so the launcher then falsely printed `Scan finished` even though the main scanner had never run.
- **v0.1.5 corrections:**
  - replaced the fragile nested IF launcher logic with explicit labels/branches;
  - added an actual zero-argument/double-click CI path;
  - added auto-detection fixtures so CI exercises the same launch route used by the user;
  - deletes stale `scan-output` before each run;
  - refuses to report success unless a fresh `scan-output/scan_summary.json` exists;
  - added a regression check forbidding the broken v0.1.4 nested-IF pattern;
  - aligned the scanner engine version and report version to `0.1.5`.
- **Windows validation for v0.1.5:**
  - workflow run: `34041346603`;
  - job: `101508557706`;
  - Windows PowerShell 5.1: PASS;
  - standalone parser preflight: PASS;
  - exact CMD launcher with ZERO arguments: PASS;
  - automatic fixture install discovery: PASS;
  - full FH6/GTA fixture scan: PASS;
  - all six required output files: PASS;
  - output content/count/error checks: PASS;
  - anti-false-success guard: PASS;
  - regression guard: PASS.
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
