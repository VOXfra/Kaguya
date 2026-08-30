# Kaguya — GTA V → VI modular overhaul

Kaguya is the working repository for VOX's modular GTA V Enhanced overhaul project.

Each gameplay overhaul is designed as an independent module. Players should be able to install only the systems they want while modules share compatible conventions and, later, a small common VOX Core.

## Current module

### Police Overhaul VI

Goal: replace GTA V's omniscient police logic with observation, evidence and persistent police knowledge while keeping the Story Mode campaign playable.

Design rules:

- No police ESP: police locations are hidden from the minimap/map in free roam.
- No investigation HUD: no suspect text, vehicle text, search circle or magic radio information for the player.
- Wanted stars remain the only abstract police HUD signal.
- Police knowledge is internal and comes from actual observation.
- Story missions and scripted sequences take priority through a mission-safe passthrough mode.
- Prefer scripts/add-ons over replacing vanilla archives. Vanilla files are only touched when there is no practical alternative.
- Other systems (NPCs, wildlife/hunting, interactions, vehicles, environment, etc.) remain separate modules.

`PoliceOverhaulVI` is currently an early alpha and should be tested only in GTA V Story Mode.

## Repository layout

- `modules/PoliceOverhaulVI/` — police/wanted overhaul.
- `docs/` — design and research notes.
- `.github/workflows/` — reproducible test builds.

## Reference mods

Reference mods are studied for behavior and compatibility only. Their binaries/assets are not committed or redistributed here.
