VOX RDR2 anim_0 Targeted YCD String Indexer v0.9.1
==================================================

Purpose
-------
Corrective pass for v0.9.0. The user's real v0.9.0 run proved all 13 target archives exact and inspected 3,755 YCD entries, but every YCD ended with status oodle_input_prefix_exhausted and ycd_decode_ok=0. Partial output still exposed 33,396 diagnostic strings.

Root cause
----------
The v0.9.0 Oodle streaming wrapper incorrectly treated the final partially-filled compressed input buffer as an immediate exhaustion error. Swage's public OodleDecompressor instead marks the input finished and lets OodleLZDecoder_DecodeSome process the remaining buffered bytes.

v0.9.1 fixes exactly that final-input behavior. It does not redo archive discovery or change the 13 target identities.

Selected exact archives
-----------------------
- clip_ai_gestures.rpf
- clip_script_common.rpf
- clip_ai_combat.rpf
- clip_mech_melee.rpf
- clip_mech_grapple.rpf
- clip_mech_loco_m.rpf
- clip_mech_loco_f.rpf
- clip_ai_getup.rpf
- clip_ai_react.rpf
- clip_ai_ragdoll.rpf
- clip_mech_doors.rpf
- clip_mech_busted.rpf
- clip_mech_revive.rpf

What changed
------------
- Correct final Oodle input handling.
- Full YCD decode is requested for every selected YCD; the user's v0.9.0 metadata proves all 3,755 target YCDs are below 8 MiB (largest observed about 3.1 MiB).
- Progress is printed per exact archive and every 100 YCDs.
- If RDR2 is already running, the launcher waits only 3 seconds instead of an unconditional 20 seconds.
- Output goes to VOX-RDR2-ANIM0-YCD-Index-v091 so the v0.9.0 evidence is preserved.

Outputs
-------
YCD-target-archives.csv
YCD-entry-summary.csv
YCD-string-index.csv
YCD-index-report.txt
YCD-index-summary.json

Safety
------
READ-ONLY. No RDR2 archive is modified. No raw Rockstar YCD/RPF asset is written to disk. No key bytes or process-memory dump is written. Only metadata and printable diagnostic strings are exported.

Validation boundary
-------------------
CI proves Windows x64 compilation, inherited TFIT2 crypto self-test, v0.9.1 SHA/target/string self-test, the public fingerprint schema and package integrity. The user's real RDR2 run is still required to prove the corrected Oodle final-chunk path across the 3,755 actual YCD resources.
