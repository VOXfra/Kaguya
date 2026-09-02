# RDR2 anim_0 research checkpoint

Last updated: 2026-09-02

## Rule for the next session

Do **not** restart the generic RDR2 scanner chain. The v0.2 -> v0.6 stages already established the format, local TFIT2 discovery, Oodle availability and recursive RPF8 opening on the user's current RDR2 install.

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
- Real focused anim_0 v0.7/v0.8 pass: 582/582 outer entries opened, 582/582 nested RPF8 archives opened, 41827 nested entries enumerated, 25646 YCD, 9927 YAS, 6 YMT, 3524 keyword hits.
- All 582 nested anim_0 RPF8 TOCs in the real v0.8 report have a non-`0x00FF` decryption tag; none is plaintext at the nested-TOC layer.
- Real v0.7 public-name mapper saw 582 CitizenFX names for 582 local nested archives but obtained **0 exact RAGE name-hash matches**. All v0.7 names were order candidates only.
- Real v0.8 decrypted-TOC SHA mapper saw 582 CitizenFX names for 582 local nested archives but obtained **0/582 exact SHA-256 matches**. Therefore the hypothesis that CitizenFX hashes the identical decrypted byte representation produced by our reader is disproven by the real run.
- No raw Rockstar asset, key bytes or process-memory dump was written by these tools.

## What CitizenFX actually proves

CitizenFX RDR3 pure-mode `HookInitialMount.cpp` passes `EntryCount * 24` bytes beginning at `headerData + 0x110` to the validation routine. `PurePackfile.cpp` SHA-256 hashes exactly the bytes supplied to that callback.

This proves the byte range and length, but the real v0.8 result shows that our locally decrypted nested TOC byte sequence is not identical to the representation seen by that hook.

Pinned CitizenFX commit:

`03dcc562ca175e24eb018569ecb919b4b7a56824`

## Current focused tool

`VOX RDR2 anim_0 Research v0.8.1`

Purpose:

1. target only root `anim_0.rpf`;
2. reuse the proven TFIT2/RPF8/Oodle engine;
3. open all 582 nested RPF8 archives in memory;
4. compute SHA-256 of the nested entry table **before** nested TOC decryption (`toc_sha256_raw`);
5. compute SHA-256 of the same entry table **after** our proven nested TOC decryption (`toc_sha256_decrypted`);
6. compare both representations independently against the pinned CitizenFX table;
7. label only successful SHA matches as authoritative (`exact_raw_toc_sha256`, `exact_decrypted_toc_sha256`, `exact_both_toc_sha256`);
8. export the 582-vs-582 CitizenFX order as a separate diagnostic `ANIM0-order-audit.csv`, explicitly never as proof by itself;
9. output metadata only.

## v0.8.1 validation boundary

CI must pass:

- native Windows x64 compilation
- deterministic TFIT2 test
- recursive RPF8 TOC self-test
- SHA-256 standard `abc` vector
- one synthetic CitizenFX exact match through the RAW hash column
- one synthetic CitizenFX exact match through the DECRYPTED hash column
- package validation

Only the user's real RDR2 run can establish which representation, if either, matches the current 582 nested archives.

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
- Do not use v0.7 `citizenfx_order_candidate` names as proof.
- Do not use v0.8 decrypted `toc_sha256` as proof: the real pass produced 0/582 CitizenFX matches.

## Next decision after the real v0.8.1 run

If RAW hashes match, use those exact archive names and immediately select the interaction/melee/loco/doors/reaction packs for targeted YCD/YAS inspection.

If DECRYPTED hashes unexpectedly match after the dual implementation, use those exact names the same way.

If both remain at zero, stop iterating archive scanners. Use `ANIM0-order-audit.csv` only as a diagnostic and move to reproducing/capturing the `fiPackfile::ReInit` in-memory 24-byte entry representation that CitizenFX hashes, rather than inventing another hash transformation.
