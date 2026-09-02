VOX RDR2 TFIT2 Metadata Bridge v0.4.1
========================================

Purpose
-------
Read-only local bridge for the GTA V Enhanced -> GTA VI reference workflow.

v0.4.1 launcher hotfix
---------------------
The native bridge core remains the CI-tested v0.4.0 binary. The v0.4.1 launcher fixes process startup/UX:
- if RDR2.exe is not running, it starts the selected local RDR2.exe automatically;
- it waits until the real RDR2.exe process is visible before scanning;
- after an automatic launch it allows the game runtime to initialize;
- if TFIT2 discovery is incomplete because the required data is not resident yet, it retries up to three times;
- if Windows sees RDR2.exe but read access fails, it can relaunch the same read-only bridge elevated once.

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
1. The BAT makes sure RDR2.exe is running, starting the selected local installation when needed.
2. The BAT fetches only public fingerprint definitions from a pinned Swage commit.
3. The EXE validates the fingerprint schema.
4. The EXE opens RDR2.exe with read-only memory access.
5. It resolves only the TFIT2 blocks needed by the target archive tags.
6. Secret bytes remain in memory only. They are never written to reports.
7. RPF8 TOCs are decrypted in memory.
8. Only metadata is exported: hashes, extensions, offsets, sizes, flags and validation results.

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
1. Double-click Run-VOX-RDR2-TFIT2-Bridge.bat.
2. Paste the folder that contains RDR2.exe.
   You can also drag the RDR2 folder onto the BAT.
3. If RDR2 is not running, the launcher attempts to start it automatically.
4. If Rockstar Games Launcher requires interaction, complete it normally and leave RDR2 open.
5. The bridge continues once RDR2.exe is detected and retries incomplete TFIT2 discovery automatically.
6. Reports are written to:
   VOX-RDR2-TFIT2-Catalog\

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
