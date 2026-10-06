#!/usr/bin/env python3
"""
Generic post-decryption intake for PS5 native-rehost work.

This tool does not decrypt protected content. It validates an already-decrypted
title dump and optionally stages the minimum executable/module layout expected
by native relinkers such as AnyPS5.
"""

import argparse
import hashlib
import json
import shutil
import struct
from pathlib import Path

ELF_MAGIC = b"\x7fELF"
PT_LOAD = 1
PF_X = 1
MODULE_DIR_NAMES = ("sce_module", "sce_modules", "prx")


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def read_head(path: Path, n: int = 4096) -> bytes:
    with path.open("rb") as f:
        return f.read(n)


def inspect_elf64_le(path: Path) -> dict:
    result = {
        "clean_elf64_le": False,
        "machine": None,
        "entry": None,
        "program_headers": 0,
        "exec_load_segments": 0,
        "error": None,
    }
    try:
        head = read_head(path, 64)
        if len(head) < 64 or head[:4] != ELF_MAGIC:
            return result
        if head[4] != 2 or head[5] != 1:
            result["error"] = "ELF is not 64-bit little-endian"
            return result

        result["clean_elf64_le"] = True
        result["machine"] = struct.unpack_from("<H", head, 18)[0]
        result["entry"] = struct.unpack_from("<Q", head, 24)[0]
        phoff = struct.unpack_from("<Q", head, 32)[0]
        phentsize = struct.unpack_from("<H", head, 54)[0]
        phnum = struct.unpack_from("<H", head, 56)[0]
        result["program_headers"] = phnum

        if phentsize < 56 or phnum > 4096:
            result["error"] = "implausible program-header table"
            return result

        exec_count = 0
        with path.open("rb") as f:
            for i in range(phnum):
                f.seek(phoff + i * phentsize)
                ph = f.read(phentsize)
                if len(ph) < 56:
                    result["error"] = "truncated program-header table"
                    break
                p_type, p_flags = struct.unpack_from("<II", ph, 0)
                p_filesz = struct.unpack_from("<Q", ph, 32)[0]
                if p_type == PT_LOAD and (p_flags & PF_X) and p_filesz:
                    exec_count += 1
        result["exec_load_segments"] = exec_count
        return result
    except Exception as e:
        result["error"] = str(e)
        return result


def normalized_parts(path: Path):
    return tuple(part.lower() for part in path.parts)


def score_eboot_candidate(path: Path) -> tuple:
    parts = normalized_parts(path)
    score = 0
    if path.name.lower() == "eboot.bin":
        score += 100
    if "app0" in parts:
        score += 40
    if "decrypted" in parts:
        score += 20
    if any(x in parts for x in ("sce_module", "sce_modules", "prx")):
        score -= 50
    return (score, -len(path.parts), str(path).lower())


def discover_eboot(root: Path):
    cands = [p for p in root.rglob("*") if p.is_file() and p.name.lower() == "eboot.bin"]
    return sorted(cands, key=score_eboot_candidate, reverse=True)


def module_files_near(eboot: Path):
    base = eboot.parent
    out = []
    for dirname in MODULE_DIR_NAMES:
        d = base / dirname
        if not d.is_dir():
            continue
        for p in sorted(x for x in d.rglob("*") if x.is_file()):
            if p.suffix.lower() in (".prx", ".sprx", ".elf", ".self", ".bin") or p.name.lower().endswith(".prx"):
                out.append((dirname, p))
    return out


def copy_file(src: Path, dst: Path):
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)


def stage_anyps5(eboot: Path, modules, stage: Path):
    if stage.exists():
        shutil.rmtree(stage)
    stage.mkdir(parents=True, exist_ok=True)

    copy_file(eboot, stage / "input.elf")

    copied = []
    for dirname, src in modules:
        rel = src.relative_to(eboot.parent / dirname)
        dst = stage / dirname / rel
        copy_file(src, dst)
        copied.append(dst.relative_to(stage).as_posix())

    sce_sys = eboot.parent / "sce_sys"
    for name in ("param.json", "param.sfo"):
        src = sce_sys / name
        if src.is_file():
            copy_file(src, stage / "sce_sys" / name)

    (stage / "ANYPS5-WINDOWS-COMMAND.txt").write_text(
        "relinker --windows --to-intel --windows-diagnostics input.elf game.exe\n",
        encoding="utf-8",
    )
    return copied


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dump_root", help="already-decrypted title dump root")
    ap.add_argument("-o", "--output", default="postdecrypt-report.json")
    ap.add_argument("--stage", help="optional output directory for minimal AnyPS5 input layout")
    args = ap.parse_args()

    root = Path(args.dump_root).resolve()
    if not root.is_dir():
        raise SystemExit(f"not a directory: {root}")

    eboots = discover_eboot(root)
    report = {
        "root": str(root),
        "status": "EBOOT_NOT_FOUND",
        "selected_eboot": None,
        "eboot_candidates": [],
        "modules": [],
        "anyps5_stage": None,
        "notes": [],
    }

    for p in eboots:
        info = inspect_elf64_le(p)
        report["eboot_candidates"].append({
            "path": str(p),
            "size": p.stat().st_size,
            "sha256": sha256_file(p),
            **info,
        })

    if not eboots:
        report["notes"].append("No eboot.bin was found in the supplied dump.")
    else:
        eboot = eboots[0]
        einfo = inspect_elf64_le(eboot)
        report["selected_eboot"] = str(eboot)

        modules = module_files_near(eboot)
        for dirname, p in modules:
            info = inspect_elf64_le(p)
            report["modules"].append({
                "directory": dirname,
                "path": str(p),
                "relative": p.relative_to(eboot.parent).as_posix(),
                "size": p.stat().st_size,
                "sha256": sha256_file(p),
                **info,
            })

        clean_modules = sum(1 for m in report["modules"] if m["clean_elf64_le"])
        if einfo["clean_elf64_le"] and einfo["exec_load_segments"] > 0:
            if modules and clean_modules == len(modules):
                report["status"] = "READY_FOR_NATIVE_RELINK"
            elif modules:
                report["status"] = "EBOOT_READY_MODULES_MIXED"
                report["notes"].append("eboot is a clean executable ELF, but at least one bundled module is not a clean ELF.")
            else:
                report["status"] = "EBOOT_READY_NO_BUNDLED_MODULES_FOUND"
                report["notes"].append("eboot is a clean executable ELF; no sibling sce_module/sce_modules/prx directory was found.")
        else:
            report["status"] = "EBOOT_NOT_CLEAN_EXECUTABLE_ELF"
            report["notes"].append("Selected eboot.bin is not yet a clean executable ELF accepted by the native-relink pipeline.")

        if args.stage and einfo["clean_elf64_le"] and einfo["exec_load_segments"] > 0:
            stage = Path(args.stage).resolve()
            copied = stage_anyps5(eboot, modules, stage)
            report["anyps5_stage"] = {
                "path": str(stage),
                "module_files_copied": copied,
                "assets_copied": False,
                "command_file": str(stage / "ANYPS5-WINDOWS-COMMAND.txt"),
            }

    out = Path(args.output)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2), encoding="utf-8")

    print(f"[STATUS] {report['status']}")
    if report["selected_eboot"]:
        print(f"[EBOOT]  {report['selected_eboot']}")
    print(f"[MODULES] {len(report['modules'])}")
    print(f"[REPORT] {out.resolve()}")
    if report["anyps5_stage"]:
        print(f"[STAGE]  {report['anyps5_stage']['path']}")

    return 0 if report["status"].startswith("READY_") or report["status"].startswith("EBOOT_READY_") else 2


if __name__ == "__main__":
    raise SystemExit(main())
