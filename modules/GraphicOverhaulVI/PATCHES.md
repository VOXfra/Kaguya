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
- **Files:** `README.md`, `CHECKPOINTS.md`.
- **Validation:** documentation/state consistency only; no in-game visual claim is attached to this patch.

## P0002 — ColorCoreVI evidence scanner v0.1.x

- **Date:** 2026-09-06
- **Status:** `APPLIED`
- **Area:** ColorCoreVI / reverse mapping
- **Purpose:** build a reproducible, read-only map of likely color/HDR/tonemap resources in local Forza Horizon 6 and GTA V Enhanced installations before implementing visual changes.
- **Final version:** `0.1.5`
- **Core changes:**
  - FH6 and GTA V Enhanced discovery;
  - recursive metadata inventory and extension summary;
  - color/HDR/tonemap keyword candidate mapping;
  - bounded text/binary probing and SHA-256 where appropriate;
  - deliberate avoidance of blind processing of huge `.rpf` archives;
  - six CSV/JSON/text reports;
  - zero source-game modification.
- **v0.1.2 issue:** PowerShell rejected interpolated `"$Game: ..."` strings.
- **v0.1.3 issue:** the embedded CMD/PowerShell preflight leaked a literal `^` into PowerShell.
- **v0.1.4 issue:** the no-argument/double-click launcher path skipped the main scanner because of nested CMD `if` semantics, then falsely reported success from the preflight exit code.
- **v0.1.5 corrections:** explicit CMD labels/branches; real zero-argument CI path; stale-output deletion; mandatory fresh `scan_summary.json`; regression guards; aligned report version.
- **Windows validation:** workflow `34041346603`, job `101508557706`, Windows PowerShell 5.1, full zero-argument launcher path PASS.
- **User-machine validation:**
  - FH6 root: `C:\XboxGames\Forza Horizon 6\Content`;
  - GTA V Enhanced root: `E:\Jeux Epic\GTAVEnhanced`;
  - 16,688 files indexed;
  - 3,367 candidates;
  - 12 nominal HIGH candidates;
  - 0 scan errors;
  - scanner runtime about 56 seconds;
  - enough evidence found to choose a targeted next step.
- **Important interpretation:** nominal candidate priority is not evidence quality. The local FH6 folder contains `reshade-shaders`, and broad keyword matching also produced substring noise. Native-path evidence is therefore separated from ReShade/noise in P0003.
- **Confirmed high-value FH6 native paths:**
  - `media\colourgrades.zip`;
  - `media\displaymappers.zip`;
  - `media\postEffects.zip`;
  - `media\_library\Shaders.zip`;
  - `media\timeofday\TimeOfDay.xml` / `TimeOfDayA.xml`;
  - weather, camera and sky configuration/resources.
- **Files:** `tools/Scan-ColorCoreVI.ps1`, `tools/Test-ColorCoreVI.ps1`, `RUN-COLORCORE-SCAN.cmd`, `.github/workflows/test-colorcorevi-scanner.yml`.

## P0003 — FH6 Color Pipeline Evidence Collector v0.1.x

- **Date:** 2026-09-06
- **Status:** `TEST`
- **Area:** ColorCoreVI / FH6 reference pipeline
- **Purpose:** move from broad inventory to actual contents of the native FH6 color/post-processing containers identified by P0002.
- **Current version:** `0.1.0`
- **Behavior:**
  - auto-detects the FH6 Xbox/Steam install and prefers `XboxGames\Forza Horizon 6\Content` over its parent directory;
  - copies selected loose time-of-day, weather, photo-mode and configuration evidence;
  - copies the small native `colourgrades`, `displaymappers`, `postEffects`, `Camera` and `Sky` archives intact into the evidence output;
  - does not copy the ~165 MB `_library\Shaders.zip` wholesale;
  - indexes every entry in selected archives;
  - extracts small relevant text/shader entries and scans bounded binary prefixes for ColorCore keywords;
  - SHA-256 fingerprints selected source containers;
  - enforces safe extraction paths and a total extraction budget;
  - produces `stage2_summary.json`, `source_manifest.csv`, `archive_entries.csv`, `evidence_hits.csv`, `collector_errors.csv`, copied loose files, copied small archives and selected extracted evidence;
  - never modifies FH6 source files.
- **Windows validation:**
  - workflow run `34042571530`;
  - job `101511864585`;
  - Windows PowerShell 5.1: PASS;
  - exact no-argument launcher: PASS;
  - Xbox `Content` auto-detection: PASS;
  - synthetic native archive creation and inspection: PASS;
  - archive indexing: PASS;
  - targeted extraction: PASS;
  - output validation: PASS;
  - cleanup: PASS.
- **Validation still required before APPLIED:** run against the user's actual FH6 installation and inspect the returned `stage2-output` evidence.
- **Files:** `tools/Collect-FH6ColorEvidence.ps1`, `tools/Test-FH6ColorEvidence.ps1`, `RUN-FH6-COLOR-EVIDENCE.cmd`, `.github/workflows/test-colorcorevi-p0003.yml`.

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
