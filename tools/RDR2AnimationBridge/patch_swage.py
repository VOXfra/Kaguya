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
        print("usage: patch_swage.py <Swage source root>", file=sys.stderr)
        return 2

    root = pathlib.Path(sys.argv[1]).resolve()
    oodle = root / "src" / "asset" / "transform" / "oodle.cpp"
    explorer = root / "src" / "explorer" / "explorer.cpp"

    if not oodle.is_file() or not explorer.is_file():
        raise RuntimeError(f"Swage source tree not found under {root}")

    # Our v0.9.2 RDR2 scan proved this upstream +16 request is wrong for the
    # final Oodle quantum: removing it changed 0/3755 successful YCD decodes to
    # 3755/3755. curQuantumCompLen already describes the required input size.
    replace_once(
        oodle,
        "        // Always try and request slightly more than necessary, to avoid wasting DecomeSome calls\n"
        "        needed_in_ = std::min<usize>(needed_in_ + 16, sizeof(buffer_in_));",
        "        // VOX: curQuantumCompLen is already the exact requirement.\n"
        "        // Requesting another 16 bytes can stall the final quantum at EOF.\n"
        "        needed_in_ = std::min<usize>(needed_in_, sizeof(buffer_in_));",
        "RDR2 Oodle exact-quantum fix",
    )

    # Add a small non-GUI mode. This deliberately reuses Swage's own archive,
    # crypto, RSC-header and extraction code rather than reimplementing RPF8.
    replace_once(
        explorer,
        "#include <stack>",
        "#include <stack>\n#include <cstdlib>",
        "headless environment include",
    )

    marker = '''    if (Rc<Stream> s = AssetManager::Open("user:/rdr2_possible_files.txt"))\n    {\n        BufferedStream reader(s);\n\n        Rage::RPF8::LoadPossibleFileList(reader);\n    }\n\n    window_ = SDL_CreateWindow'''

    injected = '''    if (Rc<Stream> s = AssetManager::Open("user:/rdr2_possible_files.txt"))\n    {\n        BufferedStream reader(s);\n\n        Rage::RPF8::LoadPossibleFileList(reader);\n    }\n\n    // VOX headless extraction bridge. When these environment variables are\n    // absent ArchiveExplorer behaves exactly like upstream.\n    const char* vox_find_keys = std::getenv("SWAGE_FIND_KEYS");\n    const char* vox_rpf = std::getenv("SWAGE_VERIFY_RPF");\n    const char* vox_entry = std::getenv("SWAGE_EXTRACT_ENTRY");\n    const char* vox_out = std::getenv("SWAGE_EXTRACT_OUT");\n\n    const bool vox_should_find_keys =\n        vox_find_keys && vox_find_keys[0] && vox_find_keys[0] != '0';\n\n    if (vox_should_find_keys)\n    {\n        SwLogInfo("VOX headless: finding local RDR2 keys...");\n        SearchForKeys();\n    }\n\n    if (vox_rpf && vox_entry && vox_out)\n    {\n        SwLogInfo("VOX headless: archive='{}' entry='{}' out='{}'", vox_rpf, vox_entry, vox_out);\n\n        Rc<Stream> input = Win32FileOpen(vox_rpf, true);\n        if (!input)\n        {\n            SwLogError("VOX headless: cannot open archive {}", vox_rpf);\n            std::exit(21);\n        }\n\n        Rc<FileDevice> archive;\n        try\n        {\n            archive = LoadArchive(vox_rpf, std::move(input));\n        }\n        catch (const std::exception& ex)\n        {\n            SwLogError("VOX headless: archive open failed: {}", ex.what());\n            std::exit(22);\n        }\n\n        if (!archive)\n        {\n            SwLogError("VOX headless: unsupported archive {}", vox_rpf);\n            std::exit(23);\n        }\n\n        String output_path(vox_out);\n        auto [output_dir_view, output_name_view] = SplitPath(output_path);\n        String output_dir(output_dir_view);\n        String output_name(output_name_view);\n        if (output_dir.empty())\n            output_dir = "./";\n        else if (output_dir.back() != '/' && output_dir.back() != '\\\\')\n            output_dir += '/';\n\n        g_ExtractPath = output_dir;\n        g_AddResourceFileHeader = true;\n\n        bool ok = ExtractFile(archive, "", vox_entry, output_name, false);\n        if (!ok)\n        {\n            SwLogError("VOX headless: extraction failed");\n            std::exit(24);\n        }\n\n        SwLogInfo("VOX headless: extraction complete");\n        std::exit(0);\n    }\n\n    if (vox_should_find_keys)\n    {\n        // Key-only invocation requested.\n        std::exit(0);\n    }\n\n    window_ = SDL_CreateWindow'''

    replace_once(explorer, marker, injected, "headless RPF8 extraction mode")

    print("[PATCH] Swage is ready for VOX RDR2 Animation Bridge")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
