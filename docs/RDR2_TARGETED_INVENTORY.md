# VOX RDR2 Targeted Inventory v0.2

Purpose: reduce RDR2 reference work to a small number of relevant archives before any deeper extraction attempt.

## What it reads

- local `.rpf` paths and sizes;
- RPF8 header fields;
- declared entry count and names length when the header is plausible;
- TOC entropy / printable / zero-byte ratios;
- loose 24-byte-entry plausibility as a heuristic only;
- sampled RPF8/RSC8/RSC7 signatures;
- filename-based relevance scores for interaction, melee/animation, ped AI, interiors/objects, audio, weapons and world/physics.

## What it never does

- no RDR2 file modification;
- no archive copy;
- no redistribution of Rockstar data;
- no invented filename recovery from hashes;
- no bundled or guessed RPF8 decryption secrets.

## Run

Double-click `TOOLS\Scan-RDR2-Targets.bat` and paste the directory containing `RDR2.exe`, or drag the RDR2 directory onto the BAT.

The scanner writes `TOOLS\VOX-RDR2-Targets` with:

- `RDR2-archive-inventory.csv`
- `RDR2-targets.csv`
- `RDR2-target-summary.json`
- `RDR2-targets.txt`

For the next research pass, only `RDR2-targets.csv`, `RDR2-target-summary.json`, and `RDR2-targets.txt` need to be shared.

## RPF8 boundary

RDR2 RPF8 entries use hashed names and installed archives can have an encrypted/high-entropy table of contents. The scanner therefore reports the state it observes and stops there. A future local reader can consume legitimate locally available reader/decryption material if present, but this repository does not distribute such material.
