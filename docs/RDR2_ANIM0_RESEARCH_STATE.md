# RDR2 anim_0 research checkpoint

Last updated: 2026-09-02

## Rule for future sessions

Do **not** restart the generic RDR2 scanner chain. Do **not** redo v0.2 -> v0.8.1.

The current research state is now past archive discovery and archive-name resolution. Resume from **targeted YCD/YAS inspection inside exact anim_0 packs**.

## Proven on the user's current installation

- RDR2 root: `C:\Jeux\Red Dead Redemption 2`
- RPF8 PC physical magic: `8FPR`
- Local TFIT2 discovery works.
- Full current PC secret discovery: 732/732.
- Oodle loads from local `oo2core_5_win64.dll`.
- Generic v0.6: 2947 outer entries, 795 nested RPF8 archives, 33816 nested entries, 5044 YCD, 1757 YAS, 225 YMT.
- Focused anim_0: 582/582 nested RPF8 archives open successfully.
- Focused anim_0 nested inventory: 41827 entries, 25646 YCD, 9927 YAS, 6 YMT.
- No raw Rockstar asset, key bytes or process-memory dump is written by the VOX research tools.

## Exact archive-name resolution: SOLVED

Real user run of **anim_0 Research v0.8.1**:

- nested archives: 582
- CitizenFX anim_0 subarchives: 582
- `exact_raw_toc_sha256_matches`: **582**
- `exact_decrypted_toc_sha256_matches`: 0
- `exact_both_matches`: 0
- conflicts: 0
- ambiguous: 0
- unresolved: **0**
- exact matches consistent with CitizenFX order: **582/582**

Therefore the pinned CitizenFX RDR3 hashes correspond to the **RAW encrypted nested RPF8 entry-table bytes** as present before our nested TOC decryption, not to the decrypted TOC bytes produced by our reader.

This means all 582 nested archive names are now authoritative for the user's current install. Confidence label: `exact_raw_toc_sha256`.

Pinned CitizenFX source commit:

`03dcc562ca175e24eb018569ecb919b4b7a56824`

## High-value exact packs already proven

### Interaction / dialogue / contextual behavior

- `clip_ai_gestures.rpf` — 53 YCD
- `clip_script_common.rpf` — 409 YCD
- `clip_mech_animal_interaction.rpf` — 40 YCD
- `clip_ai_react.rpf` — 573 YCD
- `scenario@mech.rpf` — 317 YAS
- `script@common.rpf` — 178 YAS
- `script@ambient.rpf` — 166 YAS
- scenario ambient/procedural/random-event packs

Real archive-prefix strings already observed include:

- `ai_gestures@arthur@stand@grapple...`
- `calm_threathen`
- `calm_threathen_02`
- `greet_sarcastic_l`
- `greet_sarcastic_r`
- `ai_react@breakouts@gen_female@chair@feet_together...@defuse`

### Melee / grappling / combat

- `clip_ai_combat.rpf` — 1485 YCD
- `clip_mech_melee.rpf` — 106 YCD
- `clip_mech_grapple.rpf` — 100 YCD
- `clip_mega_ai_combat.rpf` — 84 YCD
- `clip_mega_mech_grapple.rpf` — 24 YCD

Observed real strings include:

- `mech_melee@blade@_male@_ambient@_healthy@_hit_reacts@stationary`
- `mech_melee@blade@_male@_ambient@_healthy@_noncombat`
- `mech_melee@blade_long...`
- `mech_melee@blunt...`
- `mech_grapple@blade...@mounted@intimidation@loco@attacker`
- `mech_grapple@blade...@mounted@intimidation@loco@victim`
- `robbery_pocke...`

### Locomotion / reactions

- `clip_mech_loco_m.rpf` — 851 YCD
- `clip_mech_loco_f.rpf` — 76 YCD
- `clip_ai_getup.rpf` — 53 YCD
- `clip_ai_ragdoll.rpf` — 27 YCD
- `clip_ai_damage.rpf` — 7 YCD
- climb/ledge/strafe/avoid packs

Observed real strings include:

- `ai_ragdoll@getup@standard@base`
- `ai_ragdoll@recover_balance@cop@injured@pistol`
- `ai_ragdoll@recover_balance@cop@injured@rifle`

### Doors / arrest / revive

- `clip_mech_doors.rpf` — 18 YCD
- `clip_mega_mech_doors.rpf` — 8 YCD
- `clip_mech_busted.rpf` — 2 YCD
- `clip_mech_revive.rpf` — 2 YCD

Observed real door strings include:

- `mech_doors@2handed...barge`
- `mech_doors@locked@generic@barge_fail`

## Current tool / next step

`VOX RDR2 anim_0 Targeted YCD String Indexer v0.9.0`

This is **not** another whole-archive scanner. It targets 13 exact packs only:

- clip_ai_gestures
- clip_script_common
- clip_ai_combat
- clip_mech_melee
- clip_mech_grapple
- clip_mech_loco_m
- clip_mech_loco_f
- clip_ai_getup
- clip_ai_react
- clip_ai_ragdoll
- clip_mech_doors
- clip_mech_busted
- clip_mech_revive

Each pack is verified using the proven outer index/hash plus its exact RAW TOC SHA-256 before inspection.

The selected YCD set represents roughly 364 MiB of logical decompressed YCD data, not the full 25,646-YCD corpus.

Outputs:

- `YCD-target-archives.csv`
- `YCD-entry-summary.csv`
- `YCD-string-index.csv`
- `YCD-index-summary.json`
- `YCD-index-report.txt`

Goal: associate readable animation dictionary/clip/pack/skeleton strings with **individual YCD hashes** so the next analysis can choose a small number of concrete RDR2 animation resources and behavior families.

## Dead ends that must not be repeated

- PC RPF8 physical bytes are `8FPR`, not ASCII `RPF8`.
- Never parse TFIT ciphertext as plaintext entries.
- `S_MISC.rpf` is TFIT (`0x002A`), not plaintext.
- Generic public-string brute force resolved 0/2947 outer hashes; do not repeat it.
- Audio PEDS archives are audio banks, not gameplay-AI evidence.
- Do not redo the generic 8-archive recursion for animation research.
- v0.7 order candidates were not proof; this is now superseded by 582/582 RAW TOC SHA exact matches.
- v0.8 decrypted TOC SHA produced 0/582 and is not the CitizenFX representation.

## Decision after v0.9 real output

Do not build another broad inventory scanner. Use the YCD string index to select concrete YCD hashes/dictionaries for:

1. contextual greet/antagonize/defuse interaction states;
2. grapple/intimidation/robbery/melee transitions;
3. locked-door/barge interactions;
4. get-up/ragdoll/recover-balance reactions;
5. busted/revive states;
6. locomotion transitions only where needed.

Then move to **targeted YCD/RSC8 structure decoding or GTA V behavior implementation**, depending on what the string index proves.
