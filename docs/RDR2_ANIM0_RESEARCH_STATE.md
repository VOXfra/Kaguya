# RDR2 anim_0 research checkpoint

Last updated: 2026-09-02

## Rule for the next session

Do **not** restart the generic RDR2 scanner chain. The v0.2 -> v0.6 stages have already established the format, local TFIT2 discovery, Oodle availability and recursive RPF8 opening on the user's current RDR2 install.

Resume from **anim_0.rpf-focused research** using `tools/rdr2-anim0`.

## Proven on the user's current installation

- RDR2 root: `C:\Jeux\Red Dead Redemption 2`
- RPF8 physical magic on PC: `8FPR`
- TFIT2 local discovery works.
- Metadata bridge: 573/573 required blocks found for the original 8 archive target set.
- 8/8 target TOCs decrypted; 2947/2947 entries passed archive-bound checks.
- Recursive content probe v0.6: 732/732 full local PC secret blocks found.
- Oodle loaded from local `oo2core_5_win64.dll`.
- Generic v0.6 pass: 2947 outer entries, 795 nested RPF8 archives opened, 33816 nested entries, 5044 YCD, 1757 YAS, 225 YMT, 8239 keyword hits.
- No raw Rockstar asset, key bytes or process-memory dump was written by these tools.

## Important older real anim_0 result recovered from project history

A prior VOX RAGE Extractor v0.5 run on 2026-08-29 targeted root-level `anim_0.rpf` directly and reached:

- top-level entries: 582
- top-level TOC plausibility: 582/582
- top-level nested RPF candidates: 582
- nested RPF8 headers valid: 582/582
- nested RPF TOCs decoded: 582 OK / 0 failed
- nested YCD entries found: 25646
- old final barrier: 3 nested entry encryption keys still required live memory

That old three-key barrier should not be assumed to remain: the current v0.6 pipeline has since proven full 732/732 local secret discovery. A current anim_0 run is required to prove this end-to-end.

## Public name source

CitizenFX/FiveM publishes a base-game RDR3 RPF hash header containing the `anim_0.rpf` section and real nested archive names. The v0.7 mapper pins commit:

`03dcc562ca175e24eb018569ecb919b4b7a56824`

Examples include:

- `clip_mech_animal_interaction.rpf`
- `clip_ai_gestures.rpf`
- `clip_mech_doors.rpf`
- `clip_ai_react.rpf`
- `clip_ai_getup.rpf`
- `clip_mech_busted.rpf`
- `clip_mech_revive.rpf`
- `clip_mech_ledge.rpf`
- `clip_ai_avoids.rpf`
- `scenario@code.rpf`
- `script@common.rpf`
- `script@ambient.rpf`
- locomotion/strafe/ambient/vehicle/creature archives

The mapper distinguishes exact RAGE-hash matches from order-only candidates. Do not report order-only candidates as authoritative names.

## Current focused tool

`VOX RDR2 anim_0 Research v0.7.0`

Purpose:

1. target only root `anim_0.rpf`;
2. reuse the proven v0.6 TFIT2/RPF8/Oodle engine;
3. enumerate nested archive metadata and YCD/YAS/YMT entries;
4. map nested RPF names against the pinned CitizenFX public list;
5. rank archives for interactions, melee, locomotion, doors, reactions, weapons, wildlife and vehicle systems;
6. output metadata only.

## Do not repeat these dead ends

- Do not test PC RPF8 magic as ASCII `RPF8`; physical bytes are `8FPR`.
- Do not treat TFIT ciphertext as 24-byte plaintext entries.
- Do not assume `S_MISC.rpf` is plaintext; real tag was `0x002A`.
- Do not keep brute-forcing outer hashes using generic public strings: v0.5 tested 206686 candidates and resolved 0/2947 outer entries.
- Do not prioritize audio PEDS archives as gameplay-AI evidence; their names are audio banks.
- Do not redo generic 8-archive recursion when the research goal is animations; `anim_0.rpf` is the proven high-yield target.

## Next decision after a successful v0.7 real run

Use `ANIM0-priority-archives.csv` and `ANIM0-ycd-candidates.csv` to select a small set of system archives (interaction/melee/loco/doors/reactions). Only then add targeted in-memory YCD/RSC8 structure inspection. Avoid extracting or decoding tens of thousands of animation resources blindly.
