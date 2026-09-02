VOX RDR2 anim_0 Research v0.8.0
================================

Purpose
-------
Focused read-only research pass for RDR2's root-level anim_0.rpf.
It replaces repeated generic archive scans for animation research.

Why anim_0.rpf
--------------
Real VOX runs proved that anim_0.rpf contains exactly 582 nested RPF8 archives and 25,646 YCD entries on the user's current installation. The TFIT2/Oodle pipeline discovers the full local PC key set (732/732 secrets), so this tool goes directly to the animation layer.

What changed in v0.8
--------------------
v0.7 opened all 582 nested RPFs correctly, but its public archive names were only order candidates because RAGE name-hash matching returned zero exact hits.

v0.8 removes that uncertainty. CitizenFX pure-mode validates RDR3 packfiles by SHA-256 hashing the DECRYPTED RPF8 entry table (EntryCount * 24 bytes). This tool computes the same SHA-256 over each nested anim_0 TOC in memory and maps it against the pinned CitizenFX RDR3 header. Exact matches are labeled exact_toc_sha256.

What it does
------------
1. Opens the user's local anim_0.rpf read-only.
2. Discovers the full local RDR2 PC TFIT2/RPF8 key/context set from the running local RDR2 process.
3. Decrypts the anim_0 TOC in memory.
4. Opens all nested RPF8 archives in memory using the proven entry decrypt/Oodle path.
5. Computes SHA-256 of each decrypted nested TOC only; no raw archive is exported.
6. Exports nested metadata only (hash, extension, size, compression/encryption metadata).
7. Downloads a pinned public CitizenFX RDR3 base-game TOC hash header temporarily.
8. Maps nested archive names by exact TOC SHA-256 where the public table matches the local build.
9. Ranks exact archives/YCDs/YAS files for interaction, melee, locomotion, doors, reactions, weapons, wildlife and vehicle research.

Safety / boundaries
-------------------
- No RDR2 file is modified.
- No raw Rockstar asset is written to disk.
- No process-memory dump is written.
- No key bytes are written.
- Only decrypted TOC hashes and metadata are exported.
- Public Swage/CitizenFX reference files are fetched to a temporary directory and deleted after the run.

Usage
-----
Double-click Run-VOX-RDR2-Anim0-Research.bat and provide the folder containing RDR2.exe and anim_0.rpf.
The launcher can start RDR2 automatically if needed.

Main reports
------------
CONTENT-nested-archives.csv
ANIM0-exact-report.txt
ANIM0-exact-summary.json
ANIM0-exact-archive-map.csv
ANIM0-exact-priority-archives.csv
ANIM0-exact-ycd-candidates.csv
ANIM0-exact-yas-candidates.csv

Validation boundary
-------------------
CI validates compilation, the inherited TFIT2/RPF8 self-tests, SHA-256 with the standard 'abc' vector, the specialized anim_0 target patch and exact CitizenFX TOC-SHA mapper behavior on synthetic metadata. A real run proves how many of the user's 582 local nested archives match the pinned CitizenFX table exactly.
