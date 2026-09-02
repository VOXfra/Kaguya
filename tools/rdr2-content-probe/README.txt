VOX RDR2 RPF8 Recursive Content Probe v0.6.0
=============================================

Purpose
-------
Read-only second-stage RDR2 research tool for the GTA V Enhanced -> VI-style systemic overhaul.
It is designed for the point after TFIT2 TOC decryption has already been validated.

What it does
------------
- Opens RDR2.exe with PROCESS_QUERY_INFORMATION | PROCESS_VM_READ only.
- Resolves all public-fingerprint-addressable RDR2 PC RPF8 key/context blocks in local process memory.
- Uses the Oodle DLL already present in the selected local RDR2 installation when available.
- Decrypts/decompresses only in memory.
- Probes prefixes of the 8 previously selected outer archives.
- Detects nested RPF8 archives and decrypts their TOCs.
- Enumerates nested hashes/extensions/entry crypto metadata.
- Records limited diagnostic keyword strings from decoded prefixes.

What it does NOT do
-------------------
- no game modification
- no injection or patching
- no raw process memory dump
- no keys written to disk
- no Rockstar asset files extracted or written
- no Swage source or fingerprints bundled in this package

Outputs
-------
VOX-RDR2-RPF8-Content-Probe\
  CONTENT-summary.json
  CONTENT-report.txt
  CONTENT-nested-entries.csv
  CONTENT-direct-probes.csv
  CONTENT-keyword-hits.csv
  CONTENT-priority-candidates.csv

Priority extensions
-------------------
ycd = animation clip dictionary
ymt = metadata
 yas = RAGE asset type exposed by RPF8 extension mapping
Other y* resource types are kept for later classification.

Validation boundary
-------------------
CI proves compilation, the existing deterministic TFIT2 vector, the public fingerprint schema parser,
and a synthetic nested plaintext RPF8 TOC test. Live entry-level RDR2 decryption and Oodle compatibility
are only proven by the user's local report output.
