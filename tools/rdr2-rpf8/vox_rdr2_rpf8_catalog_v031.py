#!/usr/bin/env python3
"""VOX RDR2 RPF8 Cataloger v0.3.1 compatibility entry point.

RDR2 PC archives use the physical four-byte sequence 8FPR for the
little-endian RPF8 magic value 0x52504638. v0.3.0 incorrectly tested
for the textual bytes RPF8. Keep the implementation in the audited
v0.3.0 core and correct the runtime constant here.
"""

import vox_rdr2_rpf8_catalog as core

core.VERSION = "0.3.1"
core.RPF8_MAGIC = b"8FPR"

if __name__ == "__main__":
    raise SystemExit(core.main())
