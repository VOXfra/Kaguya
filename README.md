# Kaguya — GTA V → VI modular overhaul

Kaguya is the working repository for VOX's modular GTA V Enhanced overhaul project.

Each gameplay overhaul is an independent module. Players can install only the systems they want; compatible modules coordinate ownership instead of fighting over the same game entities.

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

## Repository layout

- `modules/PoliceOverhaulVI/` — police, wanted, evidence, search and dispatch systems.
- `modules/PedOverhaulVI/` — civilian/gang/ambient NPC behavior runtime.
- `docs/` — design and research notes.
- `.github/workflows/` — reproducible Enhanced test builds.

Reference mods are studied for behavior and compatibility only. Third-party binaries/assets are not committed to this public repository.
