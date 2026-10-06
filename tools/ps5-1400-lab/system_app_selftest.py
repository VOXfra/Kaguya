#!/usr/bin/env python3
import json
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent


def make_elf(path: Path, vaddr: int, size: int = 0x2400):
    path.parent.mkdir(parents=True, exist_ok=True)
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
    struct.pack_into("<Q", ph, 32, size)
    struct.pack_into("<Q", ph, 40, size)
    struct.pack_into("<Q", ph, 48, 0x1000)

    blob = eh + ph
    blob.extend(b"\0" * (0x1000 - len(blob)))
    blob.extend(bytes((i * 29 + 7) & 0xFF for i in range(size)))
    path.write_bytes(blob)


def main():
    with tempfile.TemporaryDirectory() as td:
        root = Path(td)

        app = root / "system_ex" / "app" / "NPXS40106"
        make_elf(app / "eboot.bin", 0x400000)
        make_elf(app / "sce_module" / "libSystemExample.prx", 0x700000)
        (app / "sce_sys").mkdir(parents=True)
        (app / "sce_sys" / "param.json").write_text(
            '{"titleId":"NPXS40106","titleName":"Synthetic System App"}',
            encoding="utf-8",
        )

        report = root / "report.json"
        stage = root / "stage"
        rc = subprocess.call([
            sys.executable,
            str(HERE / "system_app_intake.py"),
            str(root),
            "-o", str(report),
            "--stage", str(stage),
        ])
        assert rc == 0, rc

        data = json.loads(report.read_text(encoding="utf-8"))
        assert data["status"] == "SYSTEM_APP_READY_FOR_NATIVE_RELINK", data
        assert data["selected_title_id"] == "NPXS40106", data
        assert data["source_class"] == "system_ex_app", data
        assert (stage / "app0" / "eboot.bin").is_file()
        assert (stage / "app0" / "sce_module" / "libSystemExample.prx").is_file()
        assert (stage / "app0" / "sce_sys" / "param.json").is_file()
        assert (stage / "ANYPS5-WINDOWS-COMMAND.txt").is_file()

        badroot = root / "opaque"
        bad = badroot / "NPXS49999-app0"
        bad.mkdir(parents=True)
        (bad / "eboot.bin").write_bytes(b"SELF-OR-OPAQUE" * 64)
        badreport = root / "bad-report.json"
        badrc = subprocess.call([
            sys.executable,
            str(HERE / "system_app_intake.py"),
            str(badroot),
            "-o", str(badreport),
        ])
        assert badrc == 2, badrc
        bdata = json.loads(badreport.read_text(encoding="utf-8"))
        assert bdata["status"] == "SYSTEM_APP_EBOOT_NOT_CLEAN_EXECUTABLE_ELF", bdata

        print("[PASS] decrypted NPXS system-app intake and AnyPS5 staging")


if __name__ == "__main__":
    main()
