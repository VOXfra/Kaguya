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
- Real focused anim_0 v0.7 pass: 582/582 outer entries opened, 582/582 nested RPF8 archives opened, 41827 nested entries enumerated, 25646 YCD, 9927 YAS, 6 YMT, 3524 keyword hits.
- Real v0.7 public-name mapper saw 582 CitizenFX names for 582 local nested archives but obtained **0 exact RAGE name-hash matches**. All v0.7 archive names were therefore order candidates only and must not be treated as authoritative.
- No raw Rockstar asset, key bytes or process-memory dump was written by these tools.

## Exact archive-name shortcut discovered after v0.7

CitizenFX RDR3 pure-mode does not SHA-256 the full nested RPF. In `HookInitialMount.cpp`, the validation routine receives the decrypted RPF8 entry table at `headerData + 0x110` with length `EntryCount * 24`; `PurePackfile.cpp` SHA-256 hashes those bytes.

Therefore the hashes in the pinned `BaseGameRpfHeaderHashes_RDR3.h` can be matched directly against SHA-256 of each **decrypted nested TOC** already available in memory. No large RPF extraction or whole-file hashing is required.

Pinned CitizenFX commit:

`03dcc562ca175e24eb018569ecb919b4b7a56824`

This exact-TOC approach is implemented by anim_0 Research **v0.8.0**. Confidence label for a successful public match is `exact_toc_sha256`.

## Current focused tool

`VOX RDR2 anim_0 Research v0.8.0`

Purpose:

1. target only root `anim_0.rpf`;
2. reuse the proven TFIT2/RPF8/Oodle engine;
3. open all nested RPF8 archives in memory;
4. compute SHA-256 of each decrypted nested entry table only;
5. map that SHA against the pinned CitizenFX RDR3 table;
6. export exact archive names where matched;
7. rank exact archives/YCD/YAS candidates for interactions, melee, locomotion, doors, reactions, weapons, wildlife and vehicles;
8. output metadata only.

CI status for v0.8.0:

- native Windows x64 compilation: passed
- deterministic TFIT2 test: passed
- recursive RPF8 TOC self-test: passed
- SHA-256 standard vector: passed
- exact CitizenFX mapper synthetic test: passed
- package validation: passed

A real v0.8 run is still required to measure how many of the user's 582 current local nested archives match the pinned CitizenFX TOC hashes.

## Public archive examples worth prioritizing once exact

- `clip_ai_gestures.rpf`
- `clip_script_common.rpf`
- `clip_mech_animal_interaction.rpf`
- `clip_ai_combat.rpf`
- `clip_mech_melee.rpf`
- `clip_mech_grapple.rpf`
- `clip_mech_loco_m.rpf`
- `clip_mech_loco_f.rpf`
- `clip_ai_getup.rpf`
- `clip_ai_react.rpf`
- `clip_ai_ragdoll.rpf`
- `clip_mech_doors.rpf`
- `clip_mech_busted.rpf`
- `clip_mech_revive.rpf`
- scenario/script/ambient packs

## Do not repeat these dead ends

- Do not test PC RPF8 magic as ASCII `RPF8`; physical bytes are `8FPR`.
- Do not treat TFIT ciphertext as 24-byte plaintext entries.
- Do not assume `S_MISC.rpf` is plaintext; real tag was `0x002A`.
- Do not keep brute-forcing outer hashes using generic public strings: v0.5 tested 206686 candidates and resolved 0/2947 outer entries.
- Do not prioritize audio PEDS archives as gameplay-AI evidence; their names are audio banks.
- Do not redo generic 8-archive recursion when the research goal is animations; `anim_0.rpf` is the proven high-yield target.
- Do not use v0.7 `citizenfx_order_candidate` names as proof. Exact archive identity should come from v0.8 `exact_toc_sha256` matches.

## Next decision after the real v0.8 run

Use `ANIM0-exact-priority-archives.csv`, `ANIM0-exact-ycd-candidates.csv` and `ANIM0-exact-yas-candidates.csv` to select a small exact set of system archives. Then inspect only those YCD/RSC8 resources in memory. Avoid decoding tens of thousands of animation resources blindly.
