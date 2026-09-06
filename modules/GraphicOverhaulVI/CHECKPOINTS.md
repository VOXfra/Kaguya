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
- **State:** `LOCKED`
- **Decision:** The FH6/GT reference phase starts with a read-only inventory and evidence collection. We distinguish confirmed file/shader/config evidence from hypotheses before implementing a GTA V replacement.
- **Evidence:** `P0002`.
- **Current action:** run the ColorCoreVI scanner against local FH6 and GTA V Enhanced installations and feed the generated report back into the project.

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
