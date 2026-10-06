#!/usr/bin/env python3
import argparse, hashlib, json, os, struct
from pathlib import Path

PT_LOAD = 1
PF_X = 1

def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()

def elf_exec_ranges(path):
    try:
        with open(path, "rb") as f:
            eh = f.read(64)
            if len(eh) < 64 or eh[:4] != b"\x7fELF" or eh[4] != 2 or eh[5] != 1:
                return []
            e_phoff = struct.unpack_from("<Q", eh, 32)[0]
            e_phentsize = struct.unpack_from("<H", eh, 54)[0]
            e_phnum = struct.unpack_from("<H", eh, 56)[0]
            out = []
            for i in range(e_phnum):
                f.seek(e_phoff + i * e_phentsize)
                ph = f.read(e_phentsize)
                if len(ph) < 56:
                    break
                p_type, p_flags = struct.unpack_from("<II", ph, 0)
                p_offset = struct.unpack_from("<Q", ph, 8)[0]
                p_vaddr = struct.unpack_from("<Q", ph, 16)[0]
                p_filesz = struct.unpack_from("<Q", ph, 32)[0]
                if p_type == PT_LOAD and (p_flags & PF_X) and p_filesz:
                    out.append({"file_offset": p_offset, "vaddr": p_vaddr, "size": p_filesz})
            return out
    except OSError:
        return []

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("root")
    ap.add_argument("-o", "--output", required=True)
    args = ap.parse_args()

    root = Path(args.root)
    rows = []
    for p in sorted(x for x in root.rglob("*") if x.is_file()):
        rel = p.relative_to(root).as_posix()
        rows.append({
            "path": rel,
            "size": p.stat().st_size,
            "sha256": sha256(p),
            "elf64_le": p.read_bytes()[:6] == b"\x7fELF\x02\x01",
            "exec_ranges": elf_exec_ranges(p),
        })

    Path(args.output).parent.mkdir(parents=True, exist_ok=True)
    Path(args.output).write_text(json.dumps({"root": str(root), "files": rows}, indent=2), encoding="utf-8")
    print(f"[OK] inventory: {len(rows)} files -> {args.output}")

if __name__ == "__main__":
    main()
