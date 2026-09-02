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

    # Our RDR2 scan proved this upstream +16 request is wrong for the final
    # Oodle quantum. curQuantumCompLen already describes the exact input size.
    replace_once(
        oodle,
        "        // Always try and request slightly more than necessary, to avoid wasting DecomeSome calls\n"
        "        needed_in_ = std::min<usize>(needed_in_ + 16, sizeof(buffer_in_));",
        "        // VOX: curQuantumCompLen is already the exact requirement.\n"
        "        // Requesting another 16 bytes can stall the final quantum at EOF.\n"
        "        needed_in_ = std::min<usize>(needed_in_, sizeof(buffer_in_));",
        "RDR2 Oodle exact-quantum fix",
    )

    # Add a non-GUI extraction mode. This deliberately reuses Swage's archive,
    # key finder, TFIT, RSC-header and extraction code rather than reimplementing
    # RPF8. The retry loop is important: RDR2.exe can exist before its sgaWindow
    # is created, and SearchForKeys() only scans RDR2 once that window exists.
    replace_once(
        explorer,
        "#include <stack>",
        "#include <stack>\n#include <cstdlib>",
        "headless environment include",
    )

    marker = '''    if (Rc<Stream> s = AssetManager::Open("user:/rdr2_possible_files.txt"))\n    {\n        BufferedStream reader(s);\n\n        Rage::RPF8::LoadPossibleFileList(reader);\n    }\n\n    window_ = SDL_CreateWindow'''

    injected = '''    if (Rc<Stream> s = AssetManager::Open("user:/rdr2_possible_files.txt"))\n    {\n        BufferedStream reader(s);\n\n        Rage::RPF8::LoadPossibleFileList(reader);\n    }\n\n    // VOX headless extraction bridge. When these environment variables are\n    // absent ArchiveExplorer behaves exactly like upstream.\n    const char* vox_find_keys = std::getenv("SWAGE_FIND_KEYS");\n    const char* vox_rpf = std::getenv("SWAGE_VERIFY_RPF");\n    const char* vox_entry = std::getenv("SWAGE_EXTRACT_ENTRY");\n    const char* vox_out = std::getenv("SWAGE_EXTRACT_OUT");\n\n    const bool vox_should_find_keys =\n        vox_find_keys && vox_find_keys[0] && vox_find_keys[0] != '0';\n\n    // SearchForKeys() stores discoveries in Secrets, but RPF8::LoadKeys() is\n    // normally called earlier during startup. Therefore a key search performed\n    // here must explicitly reload RPF8 afterwards. Also retry until RDR2 has\n    // created its sgaWindow; merely seeing RDR2.exe is not sufficient.\n    if (vox_should_find_keys)\n    {\n        SwLogInfo("VOX headless: waiting for usable local RDR2 keys...");\n        bool keys_ready = false;\n\n        for (int attempt = 1; attempt <= 90; ++attempt)\n        {\n            SearchForKeys();\n            Rage::RPF8::LoadKeys();\n\n            if (vox_rpf && vox_rpf[0])\n            {\n                Rc<Stream> probe = Win32FileOpen(vox_rpf, true);\n                if (probe)\n                {\n                    try\n                    {\n                        Rc<FileDevice> probe_archive = LoadArchive(vox_rpf, std::move(probe));\n                        if (probe_archive)\n                        {\n                            keys_ready = true;\n                            SwLogInfo("VOX headless: RDR2 keys are loaded and RPF8 is readable (attempt {}).", attempt);\n                            break;\n                        }\n                    }\n                    catch (const std::exception& ex)\n                    {\n                        if (attempt == 1 || (attempt % 5) == 0)\n                            SwLogInfo("VOX headless: RDR2 not ready yet (attempt {}): {}", attempt, ex.what());\n                    }\n                }\n            }\n            else\n            {\n                // Key-only mode has no archive to probe. One search/reload pass\n                // is still useful and preserves the old behaviour.\n                keys_ready = true;\n                break;\n            }\n\n            if (attempt < 90)\n                SDL_Delay(1000);\n        }\n\n        if (!keys_ready)\n        {\n            SwLogError("VOX headless: RDR2 keys never became usable. Keep RDR2 open until its game window is visible and retry.");\n            std::exit(20);\n        }\n    }\n\n    if (vox_rpf && vox_entry && vox_out)\n    {\n        SwLogInfo("VOX headless: archive='{}' entry='{}' out='{}'", vox_rpf, vox_entry, vox_out);\n\n        Rc<Stream> input = Win32FileOpen(vox_rpf, true);\n        if (!input)\n        {\n            SwLogError("VOX headless: cannot open archive {}", vox_rpf);\n            std::exit(21);\n        }\n\n        Rc<FileDevice> archive;\n        try\n        {\n            archive = LoadArchive(vox_rpf, std::move(input));\n        }\n        catch (const std::exception& ex)\n        {\n            SwLogError("VOX headless: archive open failed: {}", ex.what());\n            std::exit(22);\n        }\n\n        if (!archive)\n        {\n            SwLogError("VOX headless: unsupported archive {}", vox_rpf);\n            std::exit(23);\n        }\n\n        String output_path(vox_out);\n        auto [output_dir_view, output_name_view] = SplitPath(output_path);\n        String output_dir(output_dir_view);\n        String output_name(output_name_view);\n        if (output_dir.empty())\n            output_dir = "./";\n        else if (output_dir.back() != '/' && output_dir.back() != '\\\\')\n            output_dir += '/';\n\n        g_ExtractPath = output_dir;\n        g_AddResourceFileHeader = true;\n\n        bool ok = ExtractFile(archive, "", vox_entry, output_name, false);\n        if (!ok)\n        {\n            SwLogError("VOX headless: extraction failed");\n            std::exit(24);\n        }\n\n        SwLogInfo("VOX headless: extraction complete");\n        std::exit(0);\n    }\n\n    if (vox_should_find_keys)\n    {\n        std::exit(0);\n    }\n\n    window_ = SDL_CreateWindow'''

    replace_once(explorer, marker, injected, "headless RPF8 extraction mode")

    print("[PATCH] Swage is ready for VOX RDR2 Animation Bridge")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
