#!/usr/bin/env python3
import json, struct, subprocess, sys, tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent

def make_elf(path, text, vaddr):
    # Minimal ELF64 LE with one executable PT_LOAD.
    eh = bytearray(64)
    eh[0:4] = b"\x7fELF"
    eh[4] = 2
    eh[5] = 1
    struct.pack_into("<H", eh, 16, 2)
    struct.pack_into("<H", eh, 18, 62)
    struct.pack_into("<I", eh, 20, 1)
    struct.pack_into("<Q", eh, 24, vaddr)
    struct.pack_into("<Q", eh, 32, 64)
    struct.pack_into("<H", eh, 52, 64)
    struct.pack_into("<H", eh, 54, 56)
    struct.pack_into("<H", eh, 56, 1)

    ph = bytearray(56)
    struct.pack_into("<I", ph, 0, 1)
    struct.pack_into("<I", ph, 4, 5)
    struct.pack_into("<Q", ph, 8, 0x1000)
    struct.pack_into("<Q", ph, 16, vaddr)
    struct.pack_into("<Q", ph, 24, vaddr)
    struct.pack_into("<Q", ph, 32, len(text))
    struct.pack_into("<Q", ph, 40, len(text))
    struct.pack_into("<Q", ph, 48, 0x1000)

    blob = eh + ph
    blob.extend(b"\0" * (0x1000 - len(blob)))
    blob.extend(text)
    path.write_bytes(blob)

def main():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)
        a = root / "fw1360"
        b = root / "fw1400"
        a.mkdir(); b.mkdir()

        # Non-uniform deterministic code-like bytes, identical between firmwares.
        text = bytes(((i * 73 + (i >> 3) * 19 + 0x31) & 0xFF) for i in range(0x6000))
        delta = 0x28000
        make_elf(a / "libSceNKWebKit.sprx", text, 0x100000)
        make_elf(b / "libSceNKWebKit.sprx", text, 0x100000 + delta)

        amap = root / "map.json"
        subprocess.check_call([
            sys.executable, str(HERE / "anchor_map.py"),
            str(a), str(b), "-o", str(amap),
            "--window", "48", "--stride", "16"
        ])

        db = json.loads(amap.read_text())
        mod = db["modules"]["libSceNKWebKit.sprx"]
        assert mod["anchor_count"] > 100, mod["anchor_count"]
        assert mod["top_deltas"][0]["delta"] == delta, mod["top_deltas"][:3]

        offsets = root / "offsets-13.60.js"
        offsets.write_text("const TEST_OFFSET = 0x102000;\n", encoding="utf-8")
        out = root / "candidates.json"
        subprocess.check_call([
            sys.executable, str(HERE / "translate_offsets.py"),
            str(amap), str(offsets), "-o", str(out),
            "--module", "libSceNKWebKit.sprx", "--radius", "0x40000"
        ])

        rows = json.loads(out.read_text())["candidates"]
        row = next(r for r in rows if r["name"] == "TEST_OFFSET")
        assert row["candidate_1400"] == 0x102000 + delta, row
        assert row["confidence"] == 1.0, row
        print("[PASS] synthetic 13.60 -> 14.00 delta recovered and offset translated")

if __name__ == "__main__":
    main()
