# Third-party reference study

The user-supplied releases listed below are research material, not source or runtime
dependencies. Their binaries, artwork, audio, configuration files and implementation
code must not be copied or redistributed by Kaguya.

Kaguya keeps its own VOX modules and uses the references only to validate gameplay
state machines, native-engine techniques and compatibility assumptions.

## Player-facing UX rules

- No trainer-style menu, settings list, debug list or phone app is part of normal play.
- A compact limited-weapon wheel is allowed.
- A radial vehicle interaction wheel is allowed when it only exposes actions relevant
  to the vehicle part the player is facing.
- A radial accessory wheel is allowed.
- Ordinary actions should otherwise be physical and contextual: look, approach, hold
  or press the normal game control, play an animation, then change world state.
- Configuration belongs in files and development tooling, not in an in-game mod menu.
- Rockstar missions, cutscenes and character switches always take ownership.
- Runtime ownership is fail-closed: loss of player control and screen transitions also
  suspend VOX, a post-scene grace period prevents teardown races, and mission entities
  remain protected even if the global mission flag is late or absent.

## Study inventory

| Supplied release | VOX destination | What is useful to study | What Kaguya rejects |
|---|---|---|---|
| Advanced Persistence | `VehicleRuntimeVI`, `VOXCoreVI` | Stable vehicle identity, bounded streaming, per-vehicle state, mission-safe save policy | Phone app, remote-control UI, wholesale character/world replacement |
| Enable All Interiors 45.2 | future `InteriorRuntimeVI` | IPL/entity-set loading, door compatibility and interior lifecycle | Teleport markers, map clutter, copying its giant fixed interior table |
| Contextual Car Control 0.9 | `VehicleRuntimeVI` | Vehicle-part detection, ownership gates, door/hood/trunk/window/engine state, compact radial interaction | Shared generic menu API, real-time scrolling list, startup/tutorial UI |
| Limited Weapons 1.2 | `InventoryRuntimeVI` | Enforcing one carried weapon per class and replacement rules | Help spam and blind deletion of the player's inventory |
| BetterChaseRemade | `PoliceOverhaulVI` | Witness recognition, warrants, search units, continuous reacquisition, compatibility/capability ownership | In-game settings menus and duplicate HUD layers |
| PullMeOverRemade 3.3-A | `PoliceOverhaulVI` | Violation grace periods, officer personality, warnings, citations, records and non-lethal traffic stops | HUD editor, persistent speed/citation overlays, settings menu |
| Surrender and Serve | `PoliceOverhaulVI` | Hold-to-surrender, officer perimeter roles, arrest/transport state machines and mission suspension | F11 menus, lawyer/jail lists, debug minigames as normal UX |
| Enhanced Car Theft | `VehicleRuntimeVI` | Trigger theft only from a real locked-entry attempt; separate lock, access, engine and hotwire state; physical animation | Its menu and 2D lockpick/hotwire minigame artwork |
| Gymnasium 1.1 | `CharacterRuntimeVI` | Reliable world-position/scenario workout triggering and animation lifetime | Workout list menu and numeric weapon-slot controls |
| OnTheBlock 3.2.765 | `PedOverhaulVI`, `InteractionRuntimeVI`, `VOXCoreVI` | Bounded ambient events, social/criminal memory, configurable spawn tables | NativeUI/web/phone interfaces, monolithic takeover, bundled assets and dependencies |
| Glory 1.0.0 Gold | `CameraRuntimeVI` | Separate on-foot/car/bike/aircraft camera states, exponential smoothing, inertia, G-force, engine vibration and gear-shift impulses | Toggle key, ammo/reticle overlays, unrelated suicide/weapon-inspect features |
| Dialogue System 1.2 | `InteractionRuntimeVI` | Camera-directed ped selection, a small greet/antagonize/defuse state machine, ambient speech and gesture responses | Direct reuse of its code or a dialogue list UI |
| New Fitness & Vitality 1.2 | `CharacterRuntimeVI` | Per-protagonist fitness persistence, fatigue movement sets and exercise definitions | NativeUI inventory/shop/stats menus and mandatory survival bars |
| Immersify 2.4 | `PedOverhaulVI`, `WorldLifeVI` | Bounded nearby-ped processing, morale, distraction, group reactions, incident reporting and mission yield | WebView/licensing stack, personality/body blips, HUD bars and broad monolithic ownership |
| Proper Car Inventory | `InventoryRuntimeVI` | Physical rear-of-vehicle detection, per-vehicle manifest and safe persistence | On-screen weapon list and keyboard list navigation |
| VI Wanted System | `PoliceOverhaulVI` | Face/clothing/weapon/vehicle snapshots, recognition decay and reacquisition | Fake star textures and evidence-icon HUD duplication |
| Better Chases+ Enhanced 1.1.0 | `PoliceOverhaulVI` | Crime thresholds, phased chase escalation, PIT/lethal authorization and persistent warrants | NativeUI settings, spotted meters and replacement star HUD |
| Dynamic Population Density | `WorldLifeVI` | Separate hourly curves for moving traffic, parked vehicles, ambient peds and scenario peds | Treating a global multiplier as the whole world simulation |
| TWS 1.0.7 | `InventoryRuntimeVI` | Rear-facing checks, category/capacity rules, per-vehicle manifests and orphan cleanup | Multi-level text menus, confirmation lists and loadout management UI |
| Dispatch Reworked 2.1 | `PoliceOverhaulVI` | Explicit escalation stages, jurisdiction and specialized-unit authorization | Unbounded military spectacle, duplicate wanted ownership and debug hotkeys |

## Concrete correction strategy

1. Repair `InteractionRuntimeVI` targeting and input using camera-directed acquisition,
   native control polling and a tiny three-direction interaction surface.
2. Rework `VehicleRuntimeVI` so lockpicking begins only after an actual attempt to
   enter a locked vehicle. Proximity alone must never start or loop an action.
3. Merge trunk storage and carried-weapon limits into one inventory authority with a
   limited weapon wheel and physical trunk access, without a weapon list menu.
4. Replace workout timing with explicit activity states and verified Rockstar
   scenarios/animations while retaining VOX per-character progression.
5. Consolidate police ownership around observation, chase authorization, search and
   surrender state machines. Vanilla stars 1-5 remain vanilla.
6. Replace camera amplitude-only tuning with separate locomotion/vehicle state and
   damped inertia. Retain mission-safe passthrough.
7. Keep `WorldLifeVI` zone and DLC-vehicle logic, but apply independent hourly traffic,
   parked-car, ambient-ped and scenario-ped curves.

No reference mod is shipped alongside Kaguya merely to make a VOX feature appear to
work. A reference may be used temporarily in an isolated test profile to compare
behavior, never as a hidden production dependency.
