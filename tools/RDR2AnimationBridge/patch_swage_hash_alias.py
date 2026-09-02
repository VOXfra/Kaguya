from __future__ import annotations

import pathlib
import sys


def replace_once(path: pathlib.Path, old: str, new: str, label: str) -> None:
    text = path.read_text(encoding="utf-8-sig")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one match, found {count} in {path}")
    path.write_text(text.replace(old, new, 1), encoding="utf-8")
    print(f"[PATCH] {label}: OK")


def main() -> int:
    if len(sys.argv) != 2:
        print("usage: patch_swage_hash_alias.py <Swage source root>", file=sys.stderr)
        return 2

    root = pathlib.Path(sys.argv[1]).resolve()
    rpf8 = root / "src" / "games" / "rage" / "rpf8.cpp"
    if not rpf8.is_file():
        raise RuntimeError(f"Swage RPF8 source not found under {root}")

    old = '''    void fiPackfile8::AddToVFS(VFS& vfs, const fiPackEntry8& entry)\n    {\n        String path = RPF8::GetFileName(entry.GetHash(), entry.GetFileExtId(), static_cast<char>(Header.PlatformId));\n\n        AddFile(vfs, path, entry);\n    }'''

    new = '''    void fiPackfile8::AddToVFS(VFS& vfs, const fiPackEntry8& entry)\n    {\n        const char platform = static_cast<char>(Header.PlatformId);\n        String path = RPF8::GetFileName(entry.GetHash(), entry.GetFileExtId(), platform);\n\n        // Preserve the resolved Rockstar path when known.\n        AddFile(vfs, path, entry);\n\n        // VOX bridge: always expose a deterministic hash alias too. Upstream\n        // replaces hash/XXXXXXXX.ext with a resolved/possible filename when a\n        // local name database knows the hash. That makes headless extraction\n        // dependent on which rdr2_files/rdr2_possible_files happen to exist on\n        // the user's machine. Keeping both aliases makes extraction stable for\n        // outer RPFs and nested YCD resources alike.\n        String hash_path = fmt::format(\n            "hash/{:08X}.{}", entry.GetHash(), RPF8::GetFileExt(entry.GetFileExtId(), platform));\n\n        if (path != hash_path)\n            AddFile(vfs, hash_path, entry);\n    }'''

    # GetFileExt is currently file-local inside RPF8 namespace. The AddToVFS
    # method lives in Swage::Rage, so qualify by moving the helper out of static
    # namespace visibility is unnecessary: RPF8::GetFileExt is addressable in
    # the same translation unit despite internal linkage.
    replace_once(rpf8, old, new, "deterministic RPF8 hash aliases")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
