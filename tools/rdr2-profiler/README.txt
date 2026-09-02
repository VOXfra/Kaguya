VOX RDR2 RPF8 Research Profiler v0.5.0
=======================================

Purpose
-------
Post-process the metadata CSV produced by VOX RDR2 TFIT2 Metadata Bridge.
This stage no longer needs RDR2.exe to be running and never reads game asset bytes.

Inputs
------
- RPF8-decrypted-entries.csv
- RPF8-decrypted-archives.csv (optional but recommended)

The BAT accepts the VOX-RDR2-TFIT2-Catalog folder by drag/drop or prompt.

Public name source
------------------
The launcher temporarily downloads the public femga/rdr3_discoveries repository at pinned commit:
49087fb2756594d2364e4abf79ee1df44d6ef3b4 (2026-08-14)

That repository is used only as an external candidate-string knowledge source. It is not bundled in this package.
If the download fails, the profiler still produces the structural extension/encryption/compression profile.

Hash matching
-------------
The profiler implements RAGE atStringHash (case/slash normalized Jenkins one-at-a-time), matching the
algorithm used for RPF8 entry names.

Matches are confidence-scored:
- 100: exact candidate path/name plus extension hashes to the RPF8 entry
- ~86: basename plus extension match
- ~68: extension-agnostic public identifier hash match
- lower: weaker basename-only candidate

A hash candidate is evidence, not proof of Rockstar's original filename. The reports preserve confidence
and the public source paths so weak candidates are not silently presented as facts.

Outputs
-------
VOX-RDR2-RPF8-Research-Profile\
- PROFILE-summary.txt
- PROFILE-summary.json
- PROFILE-extension-counts.csv
- PROFILE-archive-counts.csv
- PROFILE-archive-extension-counts.csv
- PROFILE-entry-encryption-keys.csv
- PROFILE-compressors.csv
- PROFILE-raw-magics.csv
- PROFILE-resolved-names.csv
- PROFILE-priority-candidates.csv

Project ranking
---------------
Candidates are ranked for the GTA V Enhanced -> GTA VI systemic-overhaul research by:
- archive (common_0 receives a priority boost)
- resource type (YCD/YMT/YAS are especially useful)
- resolved-name confidence
- keywords for interaction, melee, locomotion, ped AI, interiors/objects, scenarios, weapons and physics

Safety / scope
--------------
- No RDR2 archive is modified.
- No RDR2 asset is extracted.
- No TFIT2 key material is required by this post-processor.
- No Rockstar asset bytes are copied into output.
- The output is metadata and public-name-candidate analysis only.
