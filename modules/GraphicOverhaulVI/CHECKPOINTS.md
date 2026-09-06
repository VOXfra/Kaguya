# GraphicOverhaulVI — Durable checkpoints

This file is the durable project-state ledger. It exists so the project can be resumed without reconstructing decisions from chat history.

Do not rewrite historical checkpoints to make later work look cleaner. If a decision changes, mark the old checkpoint `REOPENED` or `SUPERSEDED` and add a new checkpoint.

## CP-0001 — GraphicOverhaulVI becomes active priority

- **Date:** 2026-09-06
- **State:** `LOCKED`
- **Decision:** The GTA V → VI project stops spreading active effort across unrelated systems. `GraphicOverhaulVI` is the current production priority.
- **In scope:** rendering/color pipeline, exposure/camera image response, lighting/RT, atmosphere/weather, materials, textures, geometry/LOD, vegetation, water/rain, characters/clothing visuals, vehicles, VFX, reflections, interiors/night, draw distance, performance, QA and packaging.
- **Out of scope for this phase:** complex character behavior, procedural interactions, gameplay AI and other behavior systems unless a tiny change is strictly required to fix a visual/rendering defect.
- **Evidence:** `P0001`.
- **Next allowed block:** ColorCoreVI.

## CP-0002 — ColorCoreVI is first technical block

- **Date:** 2026-09-06
- **State:** `LOCKED`
- **Decision:** Color management is completed before materials, weather art direction or asset remastering are treated as final, because those systems must be judged through a stable image pipeline.
- **Reference direction:** Gran Turismo's documented wide-gamut/HDR philosophy is a reference target. Forza Horizon 6 is inspected locally to determine its actual implementation. No claim that FH6 literally contains Gran Turismo code is accepted without evidence.
- **Target topics:** scene-linear handling where accessible, wide-gamut work/output, white point, gamut mapping, tonemapping, HDR10/PQ, SDR mapping, exposure interaction, highlight hue preservation and clipping control.
- **Evidence:** `P0001`, `P0002`.
- **Validation required:** `C01` in the master TODO.
- **Next allowed block:** camera/exposure only after C01 is validated.

## CP-0003 — Reference mods are references, not the final dependency stack

- **Date:** 2026-09-06
- **State:** `LOCKED`
- **Decision:** VisualV and other graphics mods may be inspected component by component. GraphicOverhaulVI decides whether to reproduce the principle, extend it, replace it or reject it. The final pack must be coherent and should not require a pile of separate graphics mods.
- **Legal constraint:** third-party binaries/assets are not committed or redistributed unless their license/permission allows it.
- **Evidence:** `P0001`.

## CP-0004 — Complete releases are autonomous

- **Date:** 2026-09-06
- **State:** `LOCKED`
- **Decision:** Any ZIP/version described as a complete GraphicOverhaulVI release must contain all still-required components from previous validated releases. Experimental/additive patches are named explicitly and never presented as full replacements.
- **Evidence:** `P0001`.

## CP-0005 — Evidence before imitation

- **Date:** 2026-09-06
- **State:** `VALIDATED`
- **Decision:** The FH6/GT reference phase starts with read-only evidence collection. Confirmed files/containers are separated from hypotheses before implementing a GTA V replacement.
- **Evidence:** `P0002` completed on the user's real installations with 16,688 files indexed, 3,367 candidates and 0 scan errors.
- **Result:** the inventory was sufficient to select a targeted second-stage inspection instead of continuing broad scans.

## CP-0006 — FH6 exposes explicit ColorCore container families

- **Date:** 2026-09-06
- **State:** `LOCKED`
- **Decision/result:** P0002 identified native FH6 resources whose names directly map to the ColorCoreVI problem: `media\colourgrades.zip`, `media\displaymappers.zip`, `media\postEffects.zip`, `media\_library\Shaders.zip`, plus `media\timeofday\TimeOfDay*.xml`, weather presets and supporting camera/sky resources.
- **Important caveat:** the broad P0002 keyword score is not itself evidence quality. The FH6 install also contains a `reshade-shaders` folder and the v0.1.x keyword matcher produced substring noise. P0003 therefore targets native paths explicitly and does not treat ReShade hits as FH6 engine evidence.
- **Evidence / patch IDs:** `P0002`, `P0003`.
- **Validation gate:** inspect the actual contents/entry names of the selected FH6 containers and isolate confirmed display mapping, grading, tonemapping/HDR and post-effect structures.
- **Next allowed action:** P0003 FH6 Color Pipeline Evidence Collector. GTA V RPF mapping follows once the FH6 reference pipeline is concrete enough to know exactly what equivalents we need.

---

## Checkpoint template

```text
## CP-XXXX — Short name
- Date: YYYY-MM-DD
- State: LOCKED | VALIDATED | REOPENED | SUPERSEDED
- Decision/result:
- Evidence / patch IDs:
- Explicit non-goals:
- Validation gate:
- Next allowed block:
```
