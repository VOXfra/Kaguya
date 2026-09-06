# Kaguya — GTA V → VI modular overhaul

Kaguya is the working repository for VOX's modular GTA V Enhanced overhaul project.

The long-term project remains modular, but development priorities are intentionally serialized: one major system is pushed to a validated state before attention moves to another. This avoids accumulating many impressive-but-imperfect prototypes.

## Current active priority — GraphicOverhaulVI

`modules/GraphicOverhaulVI/` is the current production priority.

Goal: rebuild GTA V Enhanced's complete visual presentation as one coherent, autonomous graphics pack rather than a stack of unrelated presets/mods.

Locked production order:

1. ColorCoreVI — color management, HDR/SDR and tonemapping.
2. Camera/exposure.
3. Lighting, RT and shadows.
4. Atmosphere/weather.
5. Materials.
6. World textures, geometry and LOD.
7. Characters/clothing visuals.
8. Vehicles.
9. Water/rain/VFX.
10. Performance.
11. QA and packaging.

Complex gameplay behavior and procedural-interaction work are outside the active GraphicOverhaulVI phase unless a small change is strictly required by rendering.

The module README is the graphics production source of truth:

- `modules/GraphicOverhaulVI/README.md` — complete master TODO, validation gates and compact patch registry.
- `modules/GraphicOverhaulVI/CHECKPOINTS.md` — durable project decisions and validated states.
- `modules/GraphicOverhaulVI/PATCHES.md` — detailed patch history.
- `modules/GraphicOverhaulVI/tools/Scan-ColorCoreVI.ps1` — read-only FH6/GTA V Enhanced ColorCoreVI evidence scanner.

Reference mods such as VisualV are studied component by component. Useful principles may be reimplemented, extended, replaced or rejected. Third-party binaries/assets are not committed to this public repository unless their license explicitly allows redistribution.

Any version explicitly described as a complete GraphicOverhaulVI release must be cumulative and autonomous. Experimental/additive patches must be labelled as such and are never substitutes for the full pack.

## Police Overhaul VI

Goal: replace GTA V's omniscient and overly binary police logic with observation, evidence, persistent police knowledge and proportional escalation while keeping Story Mode playable.

Current rules:
- police dots stay hidden from the minimap/map;
- two VI-style search radii may show the approximate police search area around the last actually known position;
- compact face/clothes/vehicle evidence icons may be shown without explanatory text;
- wanted severity, identification, evidence, PIT authorization and lethal-force authorization are separate states;
- low-level recognition is not automatic lethal force;
- Story missions/cutscenes take priority through conservative mission-safe passthrough;
- scripts/add-ons are preferred over vanilla replacement.

## Ped Overhaul VI

Goal: rebuild ambient NPC decision-making toward a more believable next-generation crowd/combat simulation.

V0.1 introduces bounded local perception, session-persistent personality, differentiated civilian reactions, panic propagation, hostile morale, retreat and surrender. When Police Overhaul VI is present, Ped Overhaul VI leaves law-enforcement peds to the police module to prevent AI conflicts.

These gameplay modules remain in Kaguya but are not the current active production focus while GraphicOverhaulVI is being established.

## Repository layout

- `modules/GraphicOverhaulVI/` — active graphics-overhaul program and ColorCoreVI work.
- `modules/PoliceOverhaulVI/` — police, wanted, evidence, search and dispatch systems.
- `modules/PedOverhaulVI/` — civilian/gang/ambient NPC behavior runtime.
- `modules/VOXCoreVI/` and other runtime modules — shared/experimental systems retained for future phases.
- `docs/` — project design and research notes.
- `.github/workflows/` — reproducible Enhanced test builds where applicable.
