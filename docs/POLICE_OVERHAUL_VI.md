# Police Overhaul VI — design contract

## Player-facing rule

The player does not receive police intelligence magically.

Allowed abstraction:
- wanted stars.

Not allowed:
- police blips on the minimap/map in normal free roam;
- search circles;
- “vehicle identified” text;
- clothing/face/weapon knowledge indicators;
- hidden dispatch radio translated into HUD information.

The player learns what police are doing by seeing/hearing the world: patrols, sirens, helicopters, roadblocks, a cruiser following them, officers waiting near a location, etc.

## Internal police knowledge

Future versions maintain separate knowledge for:
- number of suspects;
- face identity / partial face;
- clothing/appearance;
- weapon category and confidence;
- vehicle model/color;
- plate (none/partial/full);
- last known position and direction;
- CCTV / shop cameras;
- traffic cameras, ANPR and speed/red-light enforcement;
- OEM/police/aftermarket vehicle trackers;
- evidence and warrants;
- known addresses and surveillance.

Wanted stars represent response severity, not omniscience.

## Story Mode safety

Rockstar mission scripts win. When mission/random-event/cutscene ownership is detected, Police Overhaul VI enters passthrough rather than fighting scripted wanted/dispatch logic.

The campaign must eventually be playable from Prologue to ending without uninstalling the module.

## Modularity

Police Overhaul VI is independent. Wildlife & Hunting, NPC Runtime, Character Runtime, Vehicle Theft, Interactions, Environment, etc. are separate modules. Shared communication will later move behind a minimal VOX Core API.

## Vanilla replacement policy

1. Script/native/API solution.
2. Add-on/DLC-style reversible content.
3. Vanilla replacement only when technically unavoidable.

Any unavoidable replacement must be documented and reversible.
