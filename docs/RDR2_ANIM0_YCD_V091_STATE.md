# RDR2 anim_0 YCD checkpoint — v0.9 real run / v0.9.1 corrective

Date: 2026-09-02

## Do not repeat prior work

Archive discovery/name resolution is solved. Do not restart v0.2 -> v0.8.1. The user's v0.8.1 run resolved all 582 nested anim_0 archives exactly by CitizenFX RAW TOC SHA-256.

## Real v0.9.0 user run

Target archive identity: 13/13 exact.
YCD inspected: 3755.
Full decode reported OK: 0.
Printable useful strings indexed from partial output: 33396.
No raw Rockstar assets, key bytes or process-memory dumps were written.

All 3755 rows ended with `oodle_input_prefix_exhausted`. This is a bug in the VOX Oodle wrapper, not evidence that the YCDs are unreadable.

### Root cause

The old wrapper stopped as soon as the compressed source had no unread bytes while `buffered < needed`, even if valid compressed bytes remained in the decoder's input buffer. Swage's public OodleDecompressor instead marks the input finished and processes the remaining buffered bytes. v0.9.1 mirrors this final-input behavior.

The existing RPF8 resource layout logic remains valid: resource entries skip a 16-byte resource header, compressed resources use the post-header raw size, entry encryption uses a 0x80000 chunk size for compressed resources, and Oodle's target size is the RPF8 logical resource size.

## Strong candidates already recovered from partial v0.9 output

Strict filtering reduced 25,646 anim_0 YCDs -> 3,755 targeted YCDs -> only 36 YCD hashes with particularly clear animation-family strings.

### clip_ai_gestures.rpf

- nested 43 / `0xB1E66E7E`
  - `ai_gestures@gen_male@standing@silent@script@bounty_hunters-0`
  - `ai_gestures@gen_male@standing@silent@script@bounty_hunters`

The archive-level research also exposed `greet_sarcastic_l`, `greet_sarcastic_r`, `calm_threathen`, and `calm_threathen_02`.

### clip_mech_grapple.rpf

Strong hashes:
- `0xD5C2D2C3` (nested 73)
- `0x5177A6F5`
- `0x2FC9368C`
- `0xD8B051BF`
- `0xB3BD72A8`
- `0xE0811EC2`
- `0x9B92433A`

Strings include blade/unarmed intimidation, mounted/tough/environment grapple states, paired attacker/victim clips, table smash clips, and `SKEL_L_FOREARM`.

### clip_mech_melee.rpf

Strong hashes:
- nested 56 / `0x87983B73`
- nested 73 / `0xB99359AA`
- nested 78 / `0xC0661026`
- nested 29 / `0x4667846E`
- nested 58 / `0x8F79F4AB`
- nested 4 / `0x0AA80DB0`
- nested 26 / `0x3FCEAC34`
- nested 87 / `0xCE581487`
- nested 46 / `0x6BAD96CB`

Strings include unarmed intimidation-on-ass states, hit-on-ass -> on-knees transitions, attacker/victim hook/head clips, wall/KO reactions, plus blade/blade_long/blunt/hit-react/noncombat/crouch families.

### clip_mech_doors.rpf

- nested 7 / `0x78B43BB4`
- nested 13 / `0xABD68E1C`

Strings include `mech_doors@locked@generic@shoulder_push@crouch@handle_right`, the left-hand variant, and archive-level `locked@generic@barge_fail`.

### clip_script_common.rpf

- nested 402 / `0xFC1C259E`
- nested 312 / `0xC24362DC`

Strings include shared sheriff-desk seated scenarios and gesture clips such as urgent hand enter/exit, head nod, rub chin and laugh.

### clip_mech_loco_m.rpf

Strong hashes include:
- nested 108 / `0x1FE2B4DE`
- nested 412 / `0x7A388637`
- nested 160 / `0x2DB0866C`
- nested 322 / `0x6029789E`
- `0xBB5872D6`
- `0xFC3F2D1D`
- `0xDDE7C69B`
- `0xFFF9AD44`
- `0x1F92C5EE`

Strings include Arthur avoid/reaction locomotion, low-intensity pistol combat strafe transitions and many `pack:/strafe_*` clips.

### clip_ai_combat.rpf

Strong hashes:
- `0x120D06AF`
- `0x28080F5C`
- `0x4A82344A`
- `0x6DC277E4`
- `0x8BAF6D71`
- `0xE2E36D13`

These mainly expose horse/weapon-fire variation strings and are currently lower priority than interaction/melee/doors.

## v0.9.1

Corrective source: `tools/rdr2-anim0-ycd/VOX-RDR2-Anim0-YCD-Indexer-v091.cpp`
Launcher: `tools/rdr2-anim0-ycd/Run-VOX-RDR2-Anim0-YCD-Indexer-v091.bat`
Workflow: `.github/workflows/build-rdr2-anim0-ycd-indexer-v091.yml`

CI run 33625802068: SUCCESS.
Artifact: `VOX-RDR2-Anim0-YCD-String-Indexer-v0.9.1`.

The real user run is still required to prove full Oodle decode on the 3,755 actual YCDs. If successful, stop broad scanning. Use the strongest YCD hashes above for targeted RSC8/YCD structural inspection and GTA V behavior prototypes.
