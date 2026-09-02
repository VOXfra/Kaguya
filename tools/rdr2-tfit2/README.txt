VOX RDR2 TFIT2 Metadata Bridge v0.4.0
========================================

Purpose
-------
Read-only local bridge for the GTA V Enhanced -> GTA VI reference workflow.

It targets these RDR2 archives by default:
- common_0.rpf
- x64/dlcpacks/dlc_content_extra/dlc.rpf
- x64/dlcpacks/mp004/dlc.rpf
- x64/dlcpacks/mp005/dlc.rpf
- x64/dlcpacks/mp006/dlc.rpf
- x64/dlcpacks/mp008/dlc.rpf
- x64/dlcpacks/patchpack001/dlc.rpf
- x64/audio/sfx/S_MISC.rpf

What it does
------------
1. The BAT fetches only public fingerprint definitions from a pinned Swage commit.
2. The EXE validates the fingerprint schema.
3. The EXE opens the already-running RDR2.exe process with read-only memory access.
4. It resolves only the TFIT2 blocks needed by the target archive tags.
5. Secret bytes remain in memory only. They are never written to reports.
6. RPF8 TOCs are decrypted in memory.
7. Only metadata is exported: hashes, extensions, offsets, sizes, flags and validation results.

What it does NOT do
-------------------
- It does not modify RDR2.
- It does not patch/inject into RDR2.
- It does not extract Rockstar assets.
- It does not write raw process memory.
- It does not write keys or TFIT2 tables.
- It does not bundle Rockstar key material.
- It does not bundle Swage source/fingerprint files.

Usage
-----
1. Launch RDR2 and leave it at the main menu or in Story Mode.
2. Double-click Run-VOX-RDR2-TFIT2-Bridge.bat.
3. Paste the folder that contains RDR2.exe.
   You can also drag the RDR2 folder onto the BAT.
4. Let the read-only memory discovery finish.
5. Reports are written to:
   VOX-RDR2-TFIT2-Catalog\

If process access is denied, close the bridge and run the BAT as administrator.

Reports to send back
--------------------
- TFIT2-report.txt
- TFIT2-summary.json
- TFIT2-discovery.csv
- RPF8-decrypted-archives.csv
- RPF8-decrypted-entries.csv

Interpretation
--------------
"decrypted_validated" means at least 95% of decoded entries passed basic archive-bounds validation.
"decrypted_suspicious" means the TOC was processed but basic validation suggests that the discovered data,
the TFIT2 implementation, or the archive interpretation still needs repair.

A successful CI build proves compilation, the deterministic TFIT2 test vector and the pinned fingerprint
schema parser. It does NOT prove live RDR2 process-memory discovery on your specific game build; that part
requires the local run.
