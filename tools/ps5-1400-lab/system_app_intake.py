#!/usr/bin/env python3
"""
Post-decryption intake for a small PS5 system application.

The tool does not decrypt or acquire console files. It validates an already
decrypted NPXS application, inventories bundled modules, and optionally stages
the layout expected by AnyPS5 for a native Windows relink attempt.
"""

import argparse
import json
import re
import shutil
from pathlib import Path

from postdecrypt_intake import (
    inspect_elf64_le,
    module_files_near,
    sha256_file,
    copy_file,
)

TITLE_RE = re.compile(r"^(NPXS\d{5})(?:-app0)?$", re.IGNORECASE)


def title_id_from_path(path: Path):
    for part in reversed(path.parts):
        m = TITLE_RE.match(part)
        if m:
            return m.group(1).upper()
    return None


def source_class(path: Path) -> str:
    parts = [p.lower() for p in path.parts]
    joined = "/".join(parts)
    if "/system_ex/app/" in f"/{joined}/":
        return "system_ex_app"
    if "/system/vsh/app/" in f"/{joined}/":
        return "system_vsh_app"
    return "npxs_dump"


def score_candidate(path: Path) -> tuple:
    tid = title_id_from_path(path)
    score = 100 if tid else 0
    cls = source_class(path)
    if cls == "system_ex_app":
        score += 30
    elif cls == "system_vsh_app":
        score += 20
    if path.parent.name.upper().startswith("NPXS"):
        score += 10
    return (score, -len(path.parts), str(path).lower())


def discover(root: Path):
    cands = []
    for p in root.rglob("*"):
        if not p.is_file() or p.name.lower() != "eboot.bin":
            continue
        if title_id_from_path(p):
            cands.append(p)
    return sorted(cands, key=score_candidate, reverse=True)


def stage_anyps5_system_app(eboot: Path, modules, stage: Path):
    if stage.exists():
        shutil.rmtree(stage)
    app0 = stage / "app0"
    app0.mkdir(parents=True, exist_ok=True)

    copy_file(eboot, app0 / "eboot.bin")

    copied = []
    for dirname, src in modules:
        rel = src.relative_to(eboot.parent / dirname)
        dst = app0 / dirname / rel
        copy_file(src, dst)
        copied.append(dst.relative_to(stage).as_posix())

    sce_sys = eboot.parent / "sce_sys"
    metadata = []
    for name in ("param.json", "param.sfo"):
        src = sce_sys / name
        if src.is_file():
            dst = app0 / "sce_sys" / name
            copy_file(src, dst)
            metadata.append(dst.relative_to(stage).as_posix())

    (stage / "ANYPS5-WINDOWS-COMMAND.txt").write_text(
        "relinker --windows --to-intel --windows-diagnostics app0\\eboot.bin system-app.exe\n",
        encoding="utf-8",
    )

    return copied, metadata


def candidate_report(path: Path):
    info = inspect_elf64_le(path)
    modules = module_files_near(path)
    mod_rows = []
    for dirname, p in modules:
        minfo = inspect_elf64_le(p)
        mod_rows.append({
            "directory": dirname,
            "path": str(p),
            "relative": p.relative_to(path.parent).as_posix(),
            "size": p.stat().st_size,
            "sha256": sha256_file(p),
            **minfo,
        })

    return {
        "title_id": title_id_from_path(path),
        "source_class": source_class(path),
        "path": str(path),
        "size": path.stat().st_size,
        "sha256": sha256_file(path),
        **info,
        "modules": mod_rows,
    }


def status_for(row: dict) -> str:
    if not row["clean_elf64_le"] or row["exec_load_segments"] < 1:
        return "SYSTEM_APP_EBOOT_NOT_CLEAN_EXECUTABLE_ELF"

    modules = row["modules"]
    if not modules:
        return "SYSTEM_APP_EBOOT_READY_NO_BUNDLED_MODULES_FOUND"

    clean = sum(1 for m in modules if m["clean_elf64_le"] and m["exec_load_segments"] > 0)
    if clean == len(modules):
        return "SYSTEM_APP_READY_FOR_NATIVE_RELINK"
    return "SYSTEM_APP_EBOOT_READY_MODULES_MIXED"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dump_root", help="root containing an already-decrypted NPXS system application")
    ap.add_argument("-o", "--output", default="system-app-report.json")
    ap.add_argument("--stage", help="optional AnyPS5 staging directory")
    args = ap.parse_args()

    root = Path(args.dump_root).resolve()
    if not root.is_dir():
        raise SystemExit(f"not a directory: {root}")

    candidates = discover(root)
    report = {
        "root": str(root),
        "status": "SYSTEM_APP_EBOOT_NOT_FOUND",
        "selected_title_id": None,
        "selected_eboot": None,
        "source_class": None,
        "candidates": [],
        "anyps5_stage": None,
        "notes": [
            "This gate proves clean executable input only; it does not prove that the PS5 menu, ShellUI, or SceShellCore can run on Windows."
        ],
    }

    for p in candidates:
        report["candidates"].append(candidate_report(p))

    if candidates:
        selected = report["candidates"][0]
        report["selected_title_id"] = selected["title_id"]
        report["selected_eboot"] = selected["path"]
        report["source_class"] = selected["source_class"]
        report["status"] = status_for(selected)

        if args.stage and selected["clean_elf64_le"] and selected["exec_load_segments"] > 0:
            eboot = Path(selected["path"])
            modules = module_files_near(eboot)
            stage = Path(args.stage).resolve()
            copied, metadata = stage_anyps5_system_app(eboot, modules, stage)
            report["anyps5_stage"] = {
                "path": str(stage),
                "module_files_copied": copied,
                "metadata_files_copied": metadata,
                "command_file": str(stage / "ANYPS5-WINDOWS-COMMAND.txt"),
            }

    out = Path(args.output)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2), encoding="utf-8")

    print(f"[STATUS] {report['status']}")
    if report["selected_title_id"]:
        print(f"[TITLE]  {report['selected_title_id']}")
    if report["selected_eboot"]:
        print(f"[EBOOT]  {report['selected_eboot']}")
    print(f"[REPORT] {out.resolve()}")
    if report["anyps5_stage"]:
        print(f"[STAGE]  {report['anyps5_stage']['path']}")

    ok = {
        "SYSTEM_APP_READY_FOR_NATIVE_RELINK",
        "SYSTEM_APP_EBOOT_READY_NO_BUNDLED_MODULES_FOUND",
    }
    return 0 if report["status"] in ok else 2


if __name__ == "__main__":
    raise SystemExit(main())
