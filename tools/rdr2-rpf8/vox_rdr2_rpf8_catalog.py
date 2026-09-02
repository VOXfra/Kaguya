#!/usr/bin/env python3
"""VOX RDR2 RPF8 Cataloger v0.3.0

Read-only metadata cataloger for Red Dead Redemption 2 RPF8 archives.
It never writes inside the game directory and never guesses/decrypts TFIT data.

RPF8 layout used here is independently implemented from publicly documented
format information: 16-byte header, 256-byte RSA signature, 24-byte entries.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
import os
import struct
from collections import Counter
from dataclasses import dataclass, asdict
from pathlib import Path
from typing import Optional

TOOL_NAME = "VOX RDR2 RPF8 Cataloger"
VERSION = "0.3.0"
RPF8_MAGIC = b"RPF8"
RSC8_MAGIC = b"RSC8"
HEADER_SIZE = 16
RSA_SIGNATURE_SIZE = 256
ENTRY_SIZE = 24
TOC_OFFSET = HEADER_SIZE + RSA_SIGNATURE_SIZE

BASE_EXTS = [
    "rpf", "ymf", "ydr", "yft", "ydd", "ytd", "ybn", "ybd", "ypd", "ybs",
    "ysd", "ymt", "ysc", "ycs",
]
EXTRA_EXTS = [
    "mrf", "cut", "gfx", "ycd", "yld", "ypmd", "ypm", "yed", "ypt",
    "ymap", "ytyp", "ych", "yldb", "yjd", "yad", "ynv", "yhn", "ypl",
    "ynd", "yvr", "ywr", "ynh", "yfd", "yas",
]

CORE_TARGETS = [
    "common_0.rpf",
    "x64/dlcpacks/dlc_content_extra/dlc.rpf",
    "x64/dlcpacks/mp004/dlc.rpf",
    "x64/dlcpacks/mp005/dlc.rpf",
    "x64/dlcpacks/mp006/dlc.rpf",
    "x64/dlcpacks/mp008/dlc.rpf",
    "x64/dlcpacks/patchpack001/dlc.rpf",
    "x64/audio/sfx/S_MISC.rpf",
]

PED_AUDIO_TARGETS = [f"x64/audio/sfx/PEDS_{i:02d}.rpf" for i in range(11)]


def extension_for_id(ext_id: int) -> str:
    if 0 <= ext_id < len(BASE_EXTS):
        return BASE_EXTS[ext_id]
    if ext_id >= 64:
        idx = ext_id - 64
        if 0 <= idx < len(EXTRA_EXTS):
            return EXTRA_EXTS[idx]
    if ext_id == 0xFE:
        return "dir"
    return "bin"


def shannon_entropy(data: bytes) -> float:
    if not data:
        return 0.0
    counts = Counter(data)
    n = len(data)
    return -sum((c / n) * math.log2(c / n) for c in counts.values())


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def read_exact_at(f, offset: int, size: int) -> bytes:
    f.seek(offset)
    data = f.read(size)
    if len(data) != size:
        raise EOFError(f"short read at 0x{offset:X}: expected {size}, got {len(data)}")
    return data


def normalize_root(value: str) -> Path:
    value = (value or "").strip().strip('"')
    return Path(value).expanduser().resolve()


def choose_root(cli_root: Optional[str]) -> Path:
    if cli_root:
        root = normalize_root(cli_root)
    else:
        print("\nColle le dossier RDR2 qui contient RDR2.exe.")
        root = normalize_root(input("Dossier RDR2: "))
    exe = root / "RDR2.exe"
    if not exe.is_file():
        raise SystemExit(f"[ERREUR] RDR2.exe introuvable dans: {root}")
    return root


def safe_rel(path: Path, root: Path) -> str:
    return path.relative_to(root).as_posix()


def detect_magic_at(f, offset: int, archive_size: int) -> str:
    if offset < 0 or offset + 4 > archive_size:
        return "out_of_bounds"
    try:
        f.seek(offset)
        b = f.read(4)
    except OSError:
        return "io_error"
    if b == RPF8_MAGIC:
        return "RPF8"
    if b == RSC8_MAGIC:
        return "RSC8"
    if b:
        printable = ''.join(chr(x) if 32 <= x <= 126 else '.' for x in b)
        return f"other:{b.hex()}:{printable}"
    return "empty"


@dataclass
class ArchiveHeader:
    path: str
    size_bytes: int
    magic: str
    entry_count: int
    names_length: int
    decryption_tag: int
    decryption_tag_hex: str
    platform_id: int
    platform_id_hex: str
    toc_state: str
    toc_sha256: str
    toc_entropy: float
    rsa_signature_sha256: str
    parsed_entries: int
    valid_entries: int
    invalid_entries: int
    note: str


@dataclass
class EntryRecord:
    archive: str
    index: int
    hash_hex: str
    hash_u32: int
    generated_name: str
    extension: str
    ext_id: int
    enc_config: int
    enc_key_id: int
    entry_encrypted: bool
    is_resource: bool
    is_directory: bool
    compressor: int
    byte_offset: int
    on_disk_size: int
    end_offset: int
    uncompressed_size: int
    virtual_flags_hex: str
    physical_flags_hex: str
    offset_in_bounds: bool
    range_in_bounds: bool
    magic_at_offset: str
    magic_after_16: str


def parse_entry(chunk: bytes, archive: str, idx: int, archive_size: int, f) -> EntryRecord:
    q0, q8, q10 = struct.unpack("<QQQ", chunk)
    h = q0 & 0xFFFFFFFF
    enc_config = (q0 >> 32) & 0xFF
    enc_key_id = (q0 >> 40) & 0xFF
    ext_id = (q0 >> 48) & 0xFF
    is_resource = bool((q0 >> 56) & 1)
    is_directory = ext_id == 0xFE

    on_disk_size = (q8 & 0x0FFFFFFF) << 4
    byte_offset = (((q8 >> 28) & 0x7FFFFFFF) << 4)
    compressor = (q8 >> 59) & 0x1F
    entry_encrypted = enc_key_id != 0xFF

    ext = extension_for_id(ext_id)
    generated_name = f"{h:08X}.{ext}"

    if is_resource:
        virt_flags = q10 & 0xFFFFFFFF
        phys_flags = (q10 >> 32) & 0xFFFFFFFF
        uncomp = 0
    else:
        virt_flags = 0
        phys_flags = 0
        uncomp = q10 & 0xFFFFFFFF

    end = byte_offset + on_disk_size
    offset_ok = 0 <= byte_offset < archive_size
    range_ok = offset_ok and on_disk_size >= 0 and end <= archive_size

    magic0 = detect_magic_at(f, byte_offset, archive_size) if offset_ok else "out_of_bounds"
    magic16 = detect_magic_at(f, byte_offset + 16, archive_size) if byte_offset + 20 <= archive_size else "out_of_bounds"

    return EntryRecord(
        archive=archive,
        index=idx,
        hash_hex=f"0x{h:08X}",
        hash_u32=h,
        generated_name=generated_name,
        extension=ext,
        ext_id=ext_id,
        enc_config=enc_config,
        enc_key_id=enc_key_id,
        entry_encrypted=entry_encrypted,
        is_resource=is_resource,
        is_directory=is_directory,
        compressor=compressor,
        byte_offset=byte_offset,
        on_disk_size=on_disk_size,
        end_offset=end,
        uncompressed_size=uncomp,
        virtual_flags_hex=f"0x{virt_flags:08X}" if is_resource else "",
        physical_flags_hex=f"0x{phys_flags:08X}" if is_resource else "",
        offset_in_bounds=offset_ok,
        range_in_bounds=range_ok,
        magic_at_offset=magic0,
        magic_after_16=magic16,
    )


def scan_signatures(path: Path, rel: str, out_writer, chunk_size: int = 8 * 1024 * 1024) -> int:
    """Streaming raw signature scan. Reports byte offsets only; no extraction."""
    hits = 0
    overlap = 3
    carry = b""
    absolute = 0
    with path.open("rb") as f:
        while True:
            block = f.read(chunk_size)
            if not block:
                break
            data = carry + block
            base = absolute - len(carry)
            for magic, label in ((RPF8_MAGIC, "RPF8"), (RSC8_MAGIC, "RSC8")):
                start = 0
                while True:
                    pos = data.find(magic, start)
                    if pos < 0:
                        break
                    out_writer.writerow({"archive": rel, "signature": label, "offset": base + pos, "offset_hex": f"0x{base+pos:X}"})
                    hits += 1
                    start = pos + 1
            absolute += len(block)
            carry = data[-overlap:] if len(data) >= overlap else data
    return hits


def process_archive(path: Path, root: Path, entries_writer, deep_writer=None) -> ArchiveHeader:
    rel = safe_rel(path, root)
    archive_size = path.stat().st_size
    note = ""
    parsed_entries = valid_entries = invalid_entries = 0

    with path.open("rb") as f:
        header = read_exact_at(f, 0, HEADER_SIZE)
        magic = header[0:4]
        if magic != RPF8_MAGIC:
            return ArchiveHeader(rel, archive_size, magic.decode("ascii", "replace"), 0, 0, 0, "", 0, "", "not_rpf8", "", 0.0, "", 0, 0, 0, "Magic is not RPF8")

        entry_count, names_length = struct.unpack("<II", header[4:12])
        decryption_tag, platform_id = struct.unpack("<HH", header[12:16])
        rsa = read_exact_at(f, HEADER_SIZE, RSA_SIGNATURE_SIZE)
        toc_len = entry_count * ENTRY_SIZE

        if TOC_OFFSET + toc_len > archive_size:
            return ArchiveHeader(rel, archive_size, "RPF8", entry_count, names_length, decryption_tag, f"0x{decryption_tag:04X}", platform_id, f"0x{platform_id:04X}", "truncated", "", 0.0, sha256_bytes(rsa), 0, 0, 0, "Declared TOC extends beyond archive")

        toc = read_exact_at(f, TOC_OFFSET, toc_len)
        toc_hash = sha256_bytes(toc)
        entropy = shannon_entropy(toc[: min(len(toc), 1024 * 1024)])

        # Public RPF8 documentation identifies 0x00FF as unencrypted TOC.
        # Any other tag is treated as TFIT-protected. We deliberately do not
        # interpret ciphertext as 24-byte entries.
        if decryption_tag != 0x00FF:
            state = "tfit_encrypted_toc"
            note = "TOC not parsed: TFIT key material is not bundled or guessed."
        else:
            state = "plaintext_toc"
            for idx in range(entry_count):
                off = idx * ENTRY_SIZE
                rec = parse_entry(toc[off:off + ENTRY_SIZE], rel, idx, archive_size, f)
                entries_writer.writerow(asdict(rec))
                parsed_entries += 1
                if rec.offset_in_bounds and (rec.is_directory or rec.range_in_bounds or rec.on_disk_size == 0):
                    valid_entries += 1
                else:
                    invalid_entries += 1
            if invalid_entries:
                note = f"Parsed, but {invalid_entries} entries failed basic bounds validation."
            else:
                note = "TOC parsed; hashes retained as hashes, no names guessed."

    if deep_writer is not None:
        scan_signatures(path, rel, deep_writer)

    return ArchiveHeader(
        path=rel,
        size_bytes=archive_size,
        magic="RPF8",
        entry_count=entry_count,
        names_length=names_length,
        decryption_tag=decryption_tag,
        decryption_tag_hex=f"0x{decryption_tag:04X}",
        platform_id=platform_id,
        platform_id_hex=f"0x{platform_id:04X}",
        toc_state=state,
        toc_sha256=toc_hash,
        toc_entropy=round(entropy, 5),
        rsa_signature_sha256=sha256_bytes(rsa),
        parsed_entries=parsed_entries,
        valid_entries=valid_entries,
        invalid_entries=invalid_entries,
        note=note,
    )


def write_csv_header(path: Path, fieldnames):
    f = path.open("w", newline="", encoding="utf-8-sig")
    writer = csv.DictWriter(f, fieldnames=fieldnames)
    writer.writeheader()
    return f, writer


def target_list(include_ped_audio: bool, custom_targets: list[str]) -> list[str]:
    if custom_targets:
        return custom_targets
    targets = list(CORE_TARGETS)
    if include_ped_audio:
        targets.extend(PED_AUDIO_TARGETS)
    return targets


def main(argv: Optional[list[str]] = None) -> int:
    p = argparse.ArgumentParser(description=f"{TOOL_NAME} v{VERSION} - read-only RPF8 metadata catalog")
    p.add_argument("--root", help="RDR2 directory containing RDR2.exe")
    p.add_argument("--out", default="VOX-RDR2-RPF8-Catalog", help="Output directory (outside game recommended)")
    p.add_argument("--include-ped-audio", action="store_true", help="Also process PEDS_00..PEDS_10 audio archives")
    p.add_argument("--deep-signatures", action="store_true", help="Full streaming RPF8/RSC8 signature scan of target archives")
    p.add_argument("--target", action="append", default=[], help="Custom relative RPF target; may be repeated")
    args = p.parse_args(argv)

    root = choose_root(args.root)
    out = Path(args.out).resolve()
    if root == out or root in out.parents:
        raise SystemExit("[ERREUR] Le dossier de sortie ne doit pas etre dans le dossier du jeu RDR2.")
    out.mkdir(parents=True, exist_ok=True)

    targets = target_list(args.include_ped_audio, args.target)
    archive_csv = out / "RPF8-archives.csv"
    entry_csv = out / "RPF8-entries.csv"
    sig_csv = out / "RPF8-signature-offsets.csv"
    summary_json = out / "RPF8-summary.json"
    report_txt = out / "RPF8-report.txt"

    entry_fields = list(EntryRecord.__dataclass_fields__)
    arch_fields = list(ArchiveHeader.__dataclass_fields__)

    ef, ew = write_csv_header(entry_csv, entry_fields)
    sf = sw = None
    if args.deep_signatures:
        sf, sw = write_csv_header(sig_csv, ["archive", "signature", "offset", "offset_hex"])

    headers: list[ArchiveHeader] = []
    missing: list[str] = []
    errors: list[dict] = []
    try:
        for rel in targets:
            path = root / Path(rel.replace("/", os.sep))
            print(f"[SCAN] {rel}")
            if not path.is_file():
                missing.append(rel)
                print("       -> absent")
                continue
            try:
                h = process_archive(path, root, ew, sw)
                headers.append(h)
                print(f"       -> {h.toc_state}; entries={h.entry_count}; parsed={h.parsed_entries}; tag={h.decryption_tag_hex}")
            except Exception as exc:
                errors.append({"path": rel, "error": f"{type(exc).__name__}: {exc}"})
                print(f"       -> ERREUR: {exc}")
    finally:
        ef.close()
        if sf is not None:
            sf.close()

    af, aw = write_csv_header(archive_csv, arch_fields)
    try:
        for h in headers:
            aw.writerow(asdict(h))
    finally:
        af.close()

    ext_counts = Counter()
    encrypted_entries = 0
    resource_entries = 0
    with entry_csv.open("r", encoding="utf-8-sig", newline="") as f:
        for row in csv.DictReader(f):
            ext_counts[row["extension"]] += 1
            encrypted_entries += row["entry_encrypted"].lower() == "true"
            resource_entries += row["is_resource"].lower() == "true"

    summary = {
        "tool": TOOL_NAME,
        "version": VERSION,
        "root": str(root),
        "read_only": True,
        "targets_requested": targets,
        "archives_processed": len(headers),
        "missing": missing,
        "errors": errors,
        "toc_states": dict(Counter(h.toc_state for h in headers)),
        "declared_entries": sum(h.entry_count for h in headers),
        "parsed_entries": sum(h.parsed_entries for h in headers),
        "valid_entries": sum(h.valid_entries for h in headers),
        "invalid_entries": sum(h.invalid_entries for h in headers),
        "entry_extensions": dict(ext_counts.most_common()),
        "resource_entries": resource_entries,
        "entry_level_encrypted": encrypted_entries,
        "deep_signature_scan": bool(args.deep_signatures),
        "limits": [
            "TFIT-encrypted TOCs are detected but never decrypted or parsed as ciphertext.",
            "Generated names such as DEADBEEF.ycd are hash+extension labels, not claimed original Rockstar filenames.",
            "No game archive is modified. No game asset is extracted by this tool.",
        ],
    }
    summary_json.write_text(json.dumps(summary, indent=2, ensure_ascii=False), encoding="utf-8")

    lines = [
        f"{TOOL_NAME} v{VERSION}",
        f"Root: {root}",
        "Mode: READ-ONLY / metadata only",
        "",
        f"Archives processed: {len(headers)} / {len(targets)}",
        f"Missing: {len(missing)} | Errors: {len(errors)}",
        f"Declared entries: {summary['declared_entries']}",
        f"Parsed entries: {summary['parsed_entries']}",
        f"TOC states: {summary['toc_states']}",
        "",
        "ARCHIVES",
        "========",
    ]
    for h in headers:
        lines.append(f"{h.path} | tag={h.decryption_tag_hex} | {h.toc_state} | declared={h.entry_count} parsed={h.parsed_entries} valid={h.valid_entries} invalid={h.invalid_entries}")
    if missing:
        lines += ["", "MISSING", "======="] + missing
    if errors:
        lines += ["", "ERRORS", "======"] + [f"{x['path']}: {x['error']}" for x in errors]
    lines += [
        "",
        "IMPORTANT",
        "=========",
        "A TFIT-encrypted TOC is not a failed scan: its header is cataloged, but entry metadata cannot be trusted without legitimate local key material.",
        "Send RPF8-archives.csv, RPF8-entries.csv and RPF8-summary.json back for analysis.",
    ]
    report_txt.write_text("\n".join(lines) + "\n", encoding="utf-8")

    print("\n[OK] Catalogue termine.")
    print(f"Sortie: {out}")
    print(f"- {archive_csv.name}")
    print(f"- {entry_csv.name}")
    print(f"- {summary_json.name}")
    print(f"- {report_txt.name}")
    if args.deep_signatures:
        print(f"- {sig_csv.name}")
    return 0 if not errors else 2


if __name__ == "__main__":
    raise SystemExit(main())
