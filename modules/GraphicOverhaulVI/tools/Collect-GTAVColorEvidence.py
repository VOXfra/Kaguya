#!/usr/bin/env python3
"""GraphicOverhaulVI P0004 — targeted GTA V Enhanced ColorCore evidence collector.

Read-only with respect to the game installation. Keys derived from the local
GTA5_Enhanced.exe remain in memory only; this tool never writes them to disk.
"""
from __future__ import annotations

import argparse
import csv
import hashlib
import json
import os
import re
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path, PurePosixPath

COLLECTOR_VERSION = "0.1.0"
ARCHIVE_RELATIVE_PATHS = (
    "common.rpf",
    "update/update.rpf",
    "update/update2.rpf",
)
TEXTISH_EXTENSIONS = {
    ".xml", ".dat", ".txt", ".ini", ".cfg", ".meta", ".json", ".csv",
}
KEYWORD_NAME_RE = re.compile(
    r"(tonemap|tone[_-]?map|postfx|post[_-]?effect|post[_-]?process|"
    r"exposure|colour[_-]?correct|color[_-]?correct|hdr|lut)",
    re.IGNORECASE,
)
MAX_KEYWORD_EXTRACT_BYTES = 8 * 1024 * 1024


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def safe_rel(value: str) -> Path:
    parts = []
    for part in value.replace("\\", "/").split("/"):
        if not part or part in {".", ".."}:
            continue
        cleaned = re.sub(r"[<>:\"|?*]", "_", part)
        parts.append(cleaned)
    return Path(*parts) if parts else Path("unnamed")


def classify_candidate(path: str, stored_size: int) -> str | None:
    p = path.replace("\\", "/").lower()
    name = PurePosixPath(p).name
    suffix = PurePosixPath(name).suffix.lower()

    if name == "visualsettings.dat":
        return "visualsettings"
    if "/timecycle/" in p and suffix in TEXTISH_EXTENSIONS:
        return "timecycle"
    if name.startswith("timecycle") and suffix in TEXTISH_EXTENSIONS:
        return "timecycle"
    if name == "weather.xml":
        return "weather"
    if "/weather/" in p and suffix in TEXTISH_EXTENSIONS:
        return "weather"
    if name in {"cloudkeyframes.xml", "clouds.xml"}:
        return "atmosphere"
    if suffix in TEXTISH_EXTENSIONS and stored_size <= MAX_KEYWORD_EXTRACT_BYTES:
        if KEYWORD_NAME_RE.search(name):
            return "postprocess-keyword"
    return None


def write_csv(path: Path, rows: list[dict], fieldnames: list[str]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8-sig") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def self_test(output: Path) -> int:
    # Import the pinned reader and crypto dependency. This exercises the runtime
    # bootstrap without deriving or serializing any game key.
    from Crypto.Cipher import AES  # noqa: F401
    from rpf_enhanced.keys import GtaKeys  # noqa: F401
    from rpf_enhanced.rpf import Archive  # noqa: F401

    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True, exist_ok=True)
    summary = {
        "collectorVersion": COLLECTOR_VERSION,
        "selfTest": True,
        "runtimeImports": True,
        "keyFilesWritten": False,
        "sourceFilesModified": False,
    }
    (output / "stage3_summary.json").write_text(
        json.dumps(summary, indent=2), encoding="utf-8"
    )
    (output / "README.txt").write_text(
        "P0004 self-test output. No game was read and no key file was written.\n",
        encoding="utf-8",
    )
    return 0


def collect(game_root: Path, output: Path) -> int:
    started = utc_now()
    exe = game_root / "GTA5_Enhanced.exe"
    if not exe.is_file():
        raise FileNotFoundError(f"GTA5_Enhanced.exe not found in {game_root}")

    if output.exists():
        shutil.rmtree(output)
    extracted_root = output / "extracted"
    extracted_root.mkdir(parents=True, exist_ok=True)

    from rpf_enhanced.keys import GtaKeys
    from rpf_enhanced.rpf import Archive, BinaryFileEntry, ResourceFileEntry

    # Deliberately keep derived keys in memory only. Do NOT call keys.save().
    print("[P0004] Deriving Enhanced RPF keys in memory (nothing is written)...")
    keys = GtaKeys.from_enhanced_exe(exe)

    archive_rows: list[dict] = []
    selected_rows: list[dict] = []
    error_rows: list[dict] = []
    indexed_count = 0
    selected_count = 0
    extracted_count = 0
    extracted_bytes = 0

    for archive_rel in ARCHIVE_RELATIVE_PATHS:
        archive_path = game_root / Path(archive_rel)
        print(f"[P0004] Inspecting {archive_rel} ...")
        if not archive_path.is_file():
            archive_rows.append({
                "Archive": archive_rel,
                "State": "MISSING",
                "SizeBytes": 0,
                "Encryption": "",
                "EntryCount": 0,
                "FileCount": 0,
            })
            continue

        try:
            archive = Archive(archive_path, keys=keys)
            files = archive.list_files()
            archive_rows.append({
                "Archive": archive_rel,
                "State": "OPENED",
                "SizeBytes": archive_path.stat().st_size,
                "Encryption": getattr(archive.encryption, "name", str(archive.encryption)),
                "EntryCount": archive.entry_count,
                "FileCount": len(files),
            })
        except Exception as exc:
            archive_rows.append({
                "Archive": archive_rel,
                "State": "ERROR",
                "SizeBytes": archive_path.stat().st_size,
                "Encryption": "",
                "EntryCount": 0,
                "FileCount": 0,
            })
            error_rows.append({
                "Archive": archive_rel,
                "Entry": "",
                "Stage": "open",
                "Error": str(exc),
            })
            continue

        for entry in files:
            indexed_count += 1
            path = entry.path.replace("\\", "/")
            stored_size = int(getattr(entry, "file_size", 0) or 0)
            uncompressed_size = int(getattr(entry, "file_uncompressed_size", 0) or 0)
            category = classify_candidate(path, max(stored_size, uncompressed_size))
            if category is None:
                continue

            selected_count += 1
            entry_kind = (
                "resource" if isinstance(entry, ResourceFileEntry)
                else "binary" if isinstance(entry, BinaryFileEntry)
                else entry.__class__.__name__
            )
            row = {
                "Archive": archive_rel,
                "Entry": path,
                "Category": category,
                "EntryKind": entry_kind,
                "StoredSizeBytes": stored_size,
                "UncompressedSizeBytes": uncompressed_size,
                "ExtractState": "PENDING",
                "OutputRelativePath": "",
                "ExtractedSizeBytes": 0,
                "SHA256": "",
            }

            try:
                data = archive.extract(entry)
                # Guard against an unexpectedly huge keyword false-positive.
                if category == "postprocess-keyword" and len(data) > MAX_KEYWORD_EXTRACT_BYTES:
                    row["ExtractState"] = "SKIPPED_TOO_LARGE"
                    selected_rows.append(row)
                    continue

                archive_id = safe_rel(archive_rel.replace("/", "__"))
                # entry.path starts with the archive name. Preserve it: it is useful
                # evidence when comparing overrides across common/update/update2.
                dest = extracted_root / archive_id / safe_rel(path)
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_bytes(data)
                digest = hashlib.sha256(data).hexdigest().upper()
                rel_out = dest.relative_to(output).as_posix()

                row.update({
                    "ExtractState": "EXTRACTED",
                    "OutputRelativePath": rel_out,
                    "ExtractedSizeBytes": len(data),
                    "SHA256": digest,
                })
                extracted_count += 1
                extracted_bytes += len(data)
            except Exception as exc:
                row["ExtractState"] = "ERROR"
                error_rows.append({
                    "Archive": archive_rel,
                    "Entry": path,
                    "Stage": "extract",
                    "Error": str(exc),
                })

            selected_rows.append(row)

    write_csv(
        output / "archive_inventory.csv",
        archive_rows,
        ["Archive", "State", "SizeBytes", "Encryption", "EntryCount", "FileCount"],
    )
    write_csv(
        output / "selected_manifest.csv",
        selected_rows,
        [
            "Archive", "Entry", "Category", "EntryKind", "StoredSizeBytes",
            "UncompressedSizeBytes", "ExtractState", "OutputRelativePath",
            "ExtractedSizeBytes", "SHA256",
        ],
    )
    write_csv(
        output / "collector_errors.csv",
        error_rows,
        ["Archive", "Entry", "Stage", "Error"],
    )

    opened = sum(1 for row in archive_rows if row["State"] == "OPENED")
    summary = {
        "collectorVersion": COLLECTOR_VERSION,
        "startedUtc": started,
        "finishedUtc": utc_now(),
        "gameRoot": str(game_root),
        "archivesRequested": len(ARCHIVE_RELATIVE_PATHS),
        "archivesOpened": opened,
        "archiveFilesIndexed": indexed_count,
        "selectedCandidateCount": selected_count,
        "extractedFileCount": extracted_count,
        "extractedBytes": extracted_bytes,
        "collectorErrorCount": len(error_rows),
        "keyDerivation": "IN_MEMORY_ONLY",
        "keyFilesWritten": False,
        "sourceFilesModified": False,
    }
    (output / "stage3_summary.json").write_text(
        json.dumps(summary, indent=2), encoding="utf-8"
    )
    (output / "README.txt").write_text(
        "GraphicOverhaulVI P0004 GTA V Enhanced ColorCore evidence.\n"
        "This folder intentionally contains no GTA encryption keys.\n"
        "ZIP this stage3-output folder and send it back in ChatGPT.\n",
        encoding="utf-8",
    )

    # Hard local guard: do not allow suspicious key-like artifacts in output.
    forbidden = []
    for p in output.rglob("*"):
        if not p.is_file():
            continue
        lower = p.name.lower()
        if lower in {
            "gtav_aes_key.dat", "gtav_ng_key.dat", "gtav_ng_decrypt_tables.dat"
        }:
            forbidden.append(str(p))
    if forbidden:
        raise RuntimeError("Forbidden key artifacts detected in output: " + ", ".join(forbidden))

    print("\n=== P0004 GTA Color evidence complete ===")
    print(f"Archives opened: {opened}/{len(ARCHIVE_RELATIVE_PATHS)}")
    print(f"Archive files indexed: {indexed_count}")
    print(f"Selected candidates: {selected_count}")
    print(f"Extracted files: {extracted_count}")
    print(f"Errors: {len(error_rows)}")
    print("Key files written: NO")
    print(f"Output: {output}")
    return 0 if opened > 0 and extracted_count > 0 else 3


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--game-root", type=Path)
    ap.add_argument("--output", type=Path, required=True)
    ap.add_argument("--self-test", action="store_true")
    args = ap.parse_args()

    if args.self_test:
        return self_test(args.output.resolve())
    if args.game_root is None:
        ap.error("--game-root is required unless --self-test is used")
    return collect(args.game_root.resolve(), args.output.resolve())


if __name__ == "__main__":
    raise SystemExit(main())
