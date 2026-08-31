# VOX GTA V Enhanced → VI-style systemic overhaul roadmap

This document is the source-of-truth architecture for the Story Mode overhaul.

## Non-negotiable rules

1. **Vanilla-first missions.** When Rockstar owns a mission, cutscene, scripted camera, door, vehicle, wanted state, actor or interior, VOX modules yield. Free-roam systems resume only after a safe handoff/grace period.
2. **Non-destructive install where possible.** Prefer scripts/loaders and additive assets. Do not overwrite vanilla archives unless a future asset module has a reversible loader path.
3. **One fact, one owner.** Vehicle theft belongs to Vehicle Runtime; police consumes the theft event. Character inventory belongs to Inventory Runtime; Police does not invent a second inventory.
4. **Knowledge is not magic.** Player identity, face, outfit, vehicle, plate, weapon and position are independent observations with confidence, source and age.
5. **Historical evidence != current signalment.** A vehicle can remain in the case file while no longer being the vehicle currently sought.
6. **Physical evidence != electronic record.** Lester can corrupt digital links/BOLOs; he cannot magically erase a shell casing already in evidence.
7. **Current-player entity is never automatic police knowledge.** Search/reacquisition must be observation-driven.
8. **Existing mods may inspire behavior/UI concepts, but VOX implementations remain original/clean-room.**

---

# VOX Core VI

Shared event and persistence backbone.

Planned shared entities/events:
- persistent person identity
- persistent vehicle identity (model/plate/appearance/security state)
- WorldEvent / CrimeEvent / SocialEvent
- witness-of / reported-by / evidence-of / vehicle-used-in / weapon-linked-to
- confidence, source, timestamp, decay
- current vs historical knowledge
- local reputation / neighborhood awareness
- cross-module event bus instead of duplicated polling where possible
- unified diagnostics/session id
- save migrations/versioning

Existing first layer:
- persistent world events in WorldMemory.xml
- optional reflection bridges from current modules

---

# Police Overhaul VI

## Pursuit/search
- custom ownership of free-roam search after LOS loss
- last-known position and uncertainty search areas
- continuous-observation reacquisition timer
- micro-glimpse starts a candidate but cannot instantly restore pursuit
- face/outfit/vehicle/plate/mask/weapon observations independent
- active signalment separated from historical case evidence
- changing vehicle out of sight invalidates active vehicle signalment
- changing clothes out of sight invalidates active outfit signalment
- changing mask state out of sight invalidates that mask descriptor
- genuinely known face remains known
- trackers locate the tracked vehicle, not the driver
- tracker pings update search area
- current vehicle swap must not teleport tracker knowledge to new vehicle
- 1–6 threat/search duration and radius scaling
- sixth-star internal tier with military response
- no ordinary police radar dots in free roam when VOX owns HUD/search

## Recognition / BOLO
- police visual recognition with confidence and confirmation time
- CCTV recognition with delay/quality constraints
- ANPR/plate recognition
- merchant/security-guard BOLO after serious case + genuinely known face
- silent merchant report, not instant wanted
- masks/helmets/appearance changes affect confidence
- permanent warrant/BOLO can exist without permanent active wanted stars

## Witnesses
- personality/risk-aware civilian reporting
- visible crime + LOS + report delay
- witnesses may flee, freeze, film, call or refuse involvement
- interrupted/dead/incapacitated witness can fail to report
- multiple independent reports strengthen confidence

## Crime scenes / investigation
- persistent CrimeScene records
- severity/type/source/victims/witnesses/vehicle/weapon links
- blood, shell casing, impact/glass, abandoned vehicle marks
- staged lifecycle: active police perimeter → forensic collection → EMS/coroner/tow → cleanup → residual evidence → expiry
- tape/barriers and scene officers where assets/runtime allow
- revisiting scene can expose player to witnesses/police recognition
- evidence collection updates the SAME police case

## Forensics / investigative leads
- fingerprints on stolen/abandoned vehicles/objects when plausible
- gloves reduce/prevent fresh fingerprints
- projectile/casing gives calibre/firearm-family information, not magical owner identity
- recovered firearm can be ballistically compared to earlier evidence
- legal firearm purchase record can become a lead
- recent ammunition purchase can be a weak lead, not proof
- gun-store/vendor record/witness can identify purchaser if transaction is actually traceable
- lead chain can reach suspect name → home/property surveillance
- evidence confidence and decay
- false/weak leads must be possible rather than every clue solving the crime

## Force / arrest
- wanted stars do not automatically authorize lethal force
- police preference: capture alive
- surrender possible at high stars when player ceases lethal threat
- low-level stop/taser/arrest behavior
- PIT authorization separated from lethal authorization
- lethal response based on current threat, weapons and civilian risk
- custody/handcuff/arrest resolution
- arrest should not require death at 3+ stars

## Traffic / citations / charges
- speeding
- reckless speed
- red lights / stop signs where reliably detectable
- wrong way / dangerous driving
- hit-and-run
- illegal/obstructive parking when a passing officer actually observes it
- stolen vehicle
- collision/property damage
- resisting / fleeing
- assault / firearms / homicide etc.
- multiple offenses grouped into one stop/arrest case
- warning vs fine vs arrest according to severity/history
- owner-based automated citations: do not charge player for an unregistered/stolen vehicle merely because player is driving it
- persistent fine/charge history
- true iFruit Mail inbox entry when an Enhanced-safe text-bank/appEmail path is available
- current safe fallback: native notification + persistent TrafficMail.log

## Warrants / Lester
- persistent warrant/BOLO after sufficient identification/evidence
- police can recognize wanted person later without active stars
- home/property surveillance when identity/address are known
- Lester service can remove/corrupt digital warrant/BOLO links for a large fee
- service cost scales with severity/evidence/notoriety
- physical evidence remains unless independently compromised

---

# Vehicle Runtime VI

## Theft / access
- player tests door handle first
- unlocked door permits entry
- unlocked stolen car may still require hotwire
- locked car presents contextual options rather than always auto-smashing window
- break window option with appropriate forced-entry animation/noise/evidence
- lockpick option only if tool available; timed animation and failure/risk
- key replicator can provide clean unlock/start when compatible
- vehicle age/class/security affects difficulty/time
- alarms/security response
- theft state published to Core/Police
- mission vehicles yield completely to Rockstar scripts

## Vehicle security / tracker
- tracker is a property of a specific vehicle identity
- police tracker knowledge attaches to that vehicle only
- tracker can be searched for/disabled with the correct illegal tool
- disabling takes time/animation and can fail/be interrupted
- tracker removal state persists
- no tracker teleport when swapping cars

## Personal vehicle trunk
- physical trunk interaction
- stored long guns
- ammunition
- masks/gloves/tools
- saved outfits
- illegal tools
- trunk open/closed animation and access restrictions
- mission-safe ownership

## Vehicle behavior later
- fuel / realistic theft ignition state
- doors/weather/physics interactions
- realistic damage/functional faults integration
- driver-awareness events supplied to Ped Runtime

---

# Inventory Runtime VI

- limited carried weapons; no full military arsenal in pockets
- explicit carried slots for sidearms/long guns/tools
- remaining weapons stored in personal vehicle/property
- ammo inventory
- tools: lockpick, key replicator, tracker scanner/disabler, gloves etc.
- RDR2/GTA-VI-like radial/context interface
- weapon accessories UI centered around current weapon
- suppressor/light/optic/etc. configurable without GTA V's clumsy menu flow
- inventory persistence per protagonist, including future fourth character
- confiscation/recovery after arrest where appropriate

---

# Ped Overhaul VI

- cognition: attention/suspicion/certainty/fear/morale
- causal memory rather than generic crowd panic
- direct observation vs social warning vs crowd inference
- personalities/archetypes
- distraction states and interruption/recovery
- groups: friends/couples/family/colleagues/gangs
- assist injured / protect friend / call emergency / film / confront / flee
- incident cause/direction awareness
- reactions to police/EMS/firefighters/animals/crashes
- persistent social memory across streaming where feasible

## Driver / occupant layer
- driver stays in vehicle unless reason to exit
- braking/horn/avoidance/fleeing behavior
- passenger behavior distinct from driver
- crash/fire/armed-threat exit logic
- no old bug where normal traffic causes everyone to abandon vehicles

---

# Interaction Runtime VI

- Focus target resolver with stable target lock
- no weapon-key conflicts
- context-dependent intents rather than fixed menu
- greet / antagonize / calm / threaten / ask move / rob / help / role-specific actions
- 1–4 exchange dialogue sequences
- existing GTA speech bank mapped semantically where credible
- gestures/gaze/body orientation
- Ped cognition decides response; player action is not guaranteed to succeed
- social memory/opinion/fear/recognition updates
- group participation
- merchant/witness/police/driver/injured/gang-specific intents
- keyboard/controller remapping

---

# World Life VI

- context/time/weather/zone population budgets
- rush hours
- office/nightlife/beach/commercial activity cycles
- rural/city differences
- weekend/weather effects where data permits
- GTA Online civilian vehicle integration weighted by neighborhood/class/rarity/age/tuning
- no militarized/weaponized nonsense in normal traffic
- invisible donor swaps only when safe/out of sight
- mission entity exclusion
- common performance budget with Ped/Police/World systems

---

# Camera Runtime VI

VehicleCameraVI 0.1 is deprecated.

Global camera goals:
- player always owns camera direction
- no forced yaw/pitch/recentering
- walk/run/sprint/fall secondary motion
- vehicle/motorcycle speed motion
- acceleration/braking/collision feedback
- subtle surface/suspension influence later
- progressive speed FOV later
- positional spring/inertia only after manual-input-safe implementation exists
- first/third-person profiles
- drift/turn lateral cues
- no nausea-oriented excessive headbob

Always yield on:
- manual camera input
- look-behind
- aiming
- cinematic/scripted camera
- cutscene
- Rockstar mission ownership
- protagonist switch

---

# Character Runtime VI

## Physical character
- improved locomotion/contextual transitions
- IK hands/feet where a safe implementation path exists
- leaning/support/steps/impact/injury context
- object handling

## Fitness
- strength/endurance progression through training
- gameplay effects bounded to remain believable
- visual muscle progression only through safe model/morph tiers that do not break clothing/rigging
- do not fake continuous bone scaling if it produces broken characters

## Fourth protagonist
- fourth wheel slot replaces Online destination in Story Mode when VOX freeroam runtime owns it
- freemode-style custom character
- own appearance/sex/heritage/hair/facial details/clothes/accessories/tattoos
- own money, stats, fitness, inventory, outfits
- own vehicles, properties, garages
- own police case/notoriety/warrants
- own persistence and off-screen location/state
- switch integration with safe fallback
- existing Rockstar story missions keep Michael/Franklin/Trevor as authored; custom protagonist is not injected into missions unless a future bespoke mission explicitly supports it

---

# Affordance / Interior Runtime VI

- more existing interiors accessible in free roam where actual interior assets exist
- new interiors require original/additive assets; scripts do not invent missing geometry
- locked-door handle-test/rejection animation rather than walking into door
- lock/unlock/forced-entry interactions
- light switches
- room/building electrical circuits
- fuse boxes / breaker panels
- power loss affects lights, powered doors, CCTV/alarms when linked
- context interaction points
- stores/homes/services can expose role-specific affordances
- Rockstar mission-owned doors/interiors/props always take priority

---

# Underground Services VI / Lester

- key replicator sales
- lockpick / tracker tools
- other illegal equipment
- high-cost warrant/BOLO digital cleanup
- costs and availability scale with notoriety/progression
- no magical deletion of physical evidence
- future bespoke contacts/services can reuse the same Core inventory/service API

---

# Visual / Asset Overhaul VI

Separate from systemic script runtime:
- higher-quality character materials/textures
- map/environment texture/material pass
- props/decals/roads/buildings where safe
- improved interiors/additive assets
- graphical consistency rather than random high-res texture replacement
- reversible asset-loader architecture preferred
- performance/VRAM budget and LOD discipline
- no dependency on leaked proprietary source/assets

---

# Audio

Realistic Gun Sound may own firearm audio.

Future environmental audio layer should avoid duplicating gunshot playback:
- speed-dependent wind
- tire/road surface presence
- gravel/water/material events
- suspension/body noises
- vehicle pass-by presence
- tunnel/interior/exterior acoustic context if achievable cleanly
- Ped/Police acoustic perception is logical data and must not depend on replacing RGS files

---

# Validation matrix

Every release candidate eventually requires:
- Michael / Franklin / Trevor
- future fourth protagonist
- death/respawn
- save/reload
- protagonist switch
- free-roam crime 1→6
- lose LOS / swap car / swap outfit / mask changes
- tracker vehicle abandoned
- arrest/surrender
- traffic stop/fine
- crime-scene revisit
- shop BOLO recognition
- dense city traffic + pursuit + Ped cognition
- interiors/doors
- camera manual look/look-behind/aim
- at least several representative Rockstar story missions
- 30–60 minute stability/performance run
- coexistence with approved external mods such as weapon-audio replacements

Compile success is API compatibility only. Runtime testing and logs are authoritative.
