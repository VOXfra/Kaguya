VOX RDR2 anim_0 Research v0.8.1
================================

Purpose
-------
Focused read-only research pass for RDR2's root-level anim_0.rpf.
It replaces repeated generic archive scans for animation research.

Why anim_0.rpf
--------------
Real VOX runs proved that anim_0.rpf contains exactly 582 nested RPF8 archives and 25,646 YCD entries on the user's current installation. The TFIT2/Oodle pipeline discovers the full local PC key set (732/732 secrets), so this tool goes directly to the animation layer.

What the real v0.8 run proved
-----------------------------
v0.8 opened 582/582 nested RPFs, enumerated 41,827 nested entries, 25,646 YCD and 9,927 YAS, with Oodle loaded and 732/732 local secret blocks found.

However, hashing only the locally DECRYPTED nested entry tables produced 0/582 matches against the CitizenFX RDR3 pure-mode table. Therefore v0.8's assumption that the CitizenFX hook necessarily receives the same decrypted byte representation was not proven by real data.

What changed in v0.8.1
----------------------
CitizenFX's RDR3 pure-mode hook hashes EntryCount * 24 bytes beginning at +0x110 in the buffer supplied to fiPackfile validation. v0.8.1 tests both representations that our local reader can prove without writing Rockstar assets:

1. RAW nested RPF8 TOC bytes, before the nested TOC's own TFIT2 decrypt step.
2. DECRYPTED nested RPF8 TOC bytes, after our proven local TFIT2 decrypt step.

Both SHA-256 values are exported as metadata and compared independently against the pinned CitizenFX table. Exact matches are labeled exact_raw_toc_sha256, exact_decrypted_toc_sha256, or exact_both_toc_sha256.

An order audit is also exported because the local and CitizenFX anim_0 sections both contain 582 nested archives. Those names remain explicitly order-only candidates unless an exact SHA proves them; order is never promoted to authoritative identification by itself.

What it does
------------
1. Opens the user's local anim_0.rpf read-only.
2. Discovers the full local RDR2 PC TFIT2/RPF8 key/context set from the running local RDR2 process.
3. Decrypts the root anim_0 TOC in memory.
4. Opens all nested RPF8 archives in memory using the proven entry decrypt/Oodle path.
5. Computes SHA-256 of both RAW and DECRYPTED nested entry tables.
6. Exports nested metadata only (hash, extension, size, compression/encryption metadata).
7. Downloads pinned public Swage/CitizenFX definitions temporarily.
8. Maps nested archive names only where one of the two SHA representations exactly matches the public table.
9. Separately exports an order audit for diagnosis and prioritization without claiming exact identity.
10. Ranks exact and order-audit candidates for interaction, melee, locomotion, doors, reactions, weapons, wildlife and vehicle research.

Safety / boundaries
-------------------
- No RDR2 file is modified.
- No raw Rockstar asset is written to disk.
- No process-memory dump is written.
- No key bytes are written.
- Only TOC hashes and metadata are exported.
- Public Swage/CitizenFX reference files are fetched to a temporary directory and deleted after the run.

Usage
-----
Double-click Run-VOX-RDR2-Anim0-Research.bat and provide the folder containing RDR2.exe and anim_0.rpf.
The launcher can start RDR2 automatically if needed.

Main reports
------------
CONTENT-nested-archives.csv
ANIM0-dual-report.txt
ANIM0-dual-summary.json
ANIM0-dual-archive-map.csv
ANIM0-dual-exact-priority-archives.csv
ANIM0-order-audit.csv
ANIM0-dual-ycd-candidates.csv
ANIM0-dual-yas-candidates.csv

Validation boundary
-------------------
CI validates compilation, the inherited TFIT2/RPF8 self-tests, SHA-256 with the standard 'abc' vector, the specialized anim_0 target patch, one synthetic RAW CitizenFX match and one synthetic DECRYPTED CitizenFX match. Only the user's real RDR2 run can establish which representation, if either, matches the actual 582 local nested archives.
