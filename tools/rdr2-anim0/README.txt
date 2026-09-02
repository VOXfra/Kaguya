VOX RDR2 anim_0 Research v0.7.0
================================

Purpose
-------
Focused read-only research pass for RDR2's root-level anim_0.rpf.
It replaces repeated generic archive scans for animation research.

Why anim_0.rpf
--------------
A prior real VOX extraction pass already proved that anim_0.rpf contains 582 nested RPF8 archives and tens of thousands of YCD entries. The current TFIT2/Oodle pipeline can now discover the full local PC key set, so this tool reuses that proven path directly.

What it does
------------
1. Opens the user's local anim_0.rpf read-only.
2. Discovers the full local RDR2 PC TFIT2/RPF8 key/context set from the running local RDR2 process.
3. Decrypts the anim_0 TOC in memory.
4. Opens nested RPF8 archives in memory using the same entry decrypt/Oodle path proven by the v0.6 content probe.
5. Exports nested metadata only (hash, extension, size, compression/encryption metadata).
6. Downloads a pinned public CitizenFX RDR3 base-game hash header temporarily.
7. Maps nested archive hashes/names where possible and labels order-only candidates separately.
8. Ranks archives/YCDs for interaction, melee, locomotion, doors, reactions, weapons, wildlife and vehicle research.

Safety / boundaries
-------------------
- No RDR2 file is modified.
- No raw Rockstar asset is written to disk.
- No process-memory dump is written.
- No key bytes are written.
- Public Swage/CitizenFX reference files are fetched to a temporary directory and deleted after the run.
- Reports contain metadata and public-name matches only.

Usage
-----
Double-click Run-VOX-RDR2-Anim0-Research.bat and provide the folder containing RDR2.exe and anim_0.rpf.
The launcher can start RDR2 automatically if needed.

Main reports
------------
ANIM0-report.txt
ANIM0-summary.json
ANIM0-archive-map.csv
ANIM0-priority-archives.csv
ANIM0-ycd-candidates.csv

Validation boundary
-------------------
CI validates compilation, the inherited TFIT2/RPF8 self-tests, the specialized anim_0 target patch and the public-name mapper against a synthetic CSV. A successful real run is still required to prove the current local anim_0 contents on the user's installed RDR2 build.
