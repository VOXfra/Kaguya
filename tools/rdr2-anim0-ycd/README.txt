VOX RDR2 anim_0 Targeted YCD String Indexer v0.9.0
==================================================

Purpose
-------
This is the first post-mapping anim_0 research pass. The previous real v0.8.1 run proved exact identity for all 582 nested anim_0 RPF8 archives using CitizenFX RAW TOC SHA-256 hashes (582/582 exact, 0 unresolved).

This tool therefore stops scanning 25,646 anonymous YCD resources indiscriminately. It opens only a focused set of exact system archives and associates readable animation/dictionary strings with individual YCD hashes.

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

Identity safety
---------------
Each selected pack is checked against the exact outer index/hash and the exact RAW nested-TOC SHA-256 proven by the user's v0.8.1 run. A mismatch is reported and that pack is not treated as authoritative.

What it does
------------
1. Opens local anim_0.rpf read-only.
2. Discovers the full local RDR2 PC TFIT2/RPF8 key/context set from the running local RDR2 process.
3. Loads local Oodle from the RDR2 folder.
4. Fully decodes only the selected nested RPF packs in memory.
5. Enumerates their YCD entries.
6. Decrypts/decompresses each selected YCD in memory.
7. Records RSC8/other magic, metadata and useful printable strings such as animation dictionaries, clip names, pack:/ references, skeleton/bone names and interaction/melee/locomotion keywords.
8. Writes metadata/string reports only.

It does NOT
-----------
- modify RDR2 files;
- export or repack Rockstar YCD/RPF assets;
- write key bytes;
- write process-memory dumps;
- redistribute Rockstar content.

Outputs
-------
YCD-target-archives.csv
  Exact identity check and counts for selected nested archives.

YCD-entry-summary.csv
  One row per inspected YCD with hash, nested index, encryption/compression metadata, status, magic and string count.

YCD-string-index.csv
  Readable diagnostic strings associated with the exact archive and exact YCD hash they came from.

YCD-index-report.txt / YCD-index-summary.json
  Totals and highest-yield YCDs for the GTA V -> VI project.

Validation boundary
-------------------
CI validates Windows x64 compilation, inherited TFIT2/RPF8 tests, SHA-256, in-memory nested-entry decoding on synthetic data, exact target identity checks and package structure. The real RDR2 run validates which actual YCD resources expose useful strings on the user's installation.
