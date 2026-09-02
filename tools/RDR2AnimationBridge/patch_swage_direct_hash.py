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
        print("usage: patch_swage_direct_hash.py <Swage source root>", file=sys.stderr)
        return 2

    root = pathlib.Path(sys.argv[1]).resolve()
    rpf8_h = root / "src" / "games" / "rage" / "rpf8.h"
    rpf8_cpp = root / "src" / "games" / "rage" / "rpf8.cpp"
    explorer = root / "src" / "explorer" / "explorer.cpp"

    for path in (rpf8_h, rpf8_cpp, explorer):
        if not path.is_file():
            raise RuntimeError(f"required Swage source missing: {path}")

    replace_once(
        rpf8_h,
        "    Rc<FileDevice> LoadRPF8(Rc<Stream> input);",
        "    Rc<Stream> OpenRPF8EntryByHash(Rc<Stream> input, u32 hash, u8 ext_id, datResourceFileHeader* resource_header);\n"
        "    Rc<FileDevice> LoadRPF8(Rc<Stream> input);",
        "declare direct RPF8 hash opener",
    )

    marker = "    Rc<FileDevice> LoadRPF8(Rc<Stream> input)\n    {"
    injected = r'''    Rc<Stream> OpenRPF8EntryByHash(
        Rc<Stream> input, u32 hash, u8 ext_id, datResourceFileHeader* resource_header)
    {
        fiPackHeader8 header;

        if (!input->Rewind() || !input->TryRead(&header, sizeof(header)))
            throw std::runtime_error("Failed to read RPF8 header");

        if (header.Magic != 0x52504638)
            throw std::runtime_error("Invalid RPF8 header magic");

        u8 rsa_signature[0x100];
        if (!input->TryRead(&rsa_signature, sizeof(rsa_signature)))
            throw std::runtime_error("Failed to read RPF8 RSA signature");

        Vec<fiPackEntry8> entries(header.EntryCount);
        if (!input->TryRead(entries.data(), ByteSize(entries)))
            throw std::runtime_error("Failed to read RPF8 entries");

        if (Option<Ptr<Cipher>> cipher = RPF8::MakeCipher(header.PlatformId, header.DecryptionTag))
        {
            if (*cipher)
                (*cipher)->Update(entries.data(), ByteSize(entries));
        }
        else
        {
            throw std::runtime_error(fmt::format(
                "Unknown cipher key for RPF8 header: 0x{:02X}, 0x{:02X}", header.PlatformId, header.DecryptionTag));
        }

        Option<fiPackEntry8> selected;
        usize selected_index = 0;
        for (usize i = 0; i < entries.size(); ++i)
        {
            const fiPackEntry8& entry = entries[i];
            if (entry.IsDirectory() || entry.GetHash() != hash)
                continue;
            if (ext_id != 0xFF && entry.GetFileExtId() != ext_id)
                continue;

            selected = entry;
            selected_index = i;
            break;
        }

        if (!selected)
            throw std::runtime_error(fmt::format(
                "RPF8 entry not found: hash=0x{:08X} ext=0x{:02X}", hash, ext_id));

        const fiPackEntry8 entry = *selected;
        SwLogInfo(
            "VOX direct RPF8 match: index={} hash=0x{:08X} ext=0x{:02X} size={} raw={} offset=0x{:X}",
            selected_index, entry.GetHash(), entry.GetFileExtId(), entry.GetSize(), entry.GetOnDiskSize(), entry.GetOffset());

        if (resource_header)
        {
            std::memset(resource_header, 0, sizeof(*resource_header));
            entry.GetResourceFileHeader(*resource_header);
        }

        u64 size = entry.GetSize();
        u64 raw_size = entry.GetOnDiskSize();
        u64 offset = entry.GetOffset();
        bool is_compressed = entry.GetCompressorId() != 0;

        if (entry.IsSignatureProtected())
        {
            if (raw_size < 0x100)
                throw std::runtime_error("Signature protected RPF8 entry is too small");
            raw_size -= 0x100;
        }

        if (entry.IsResource())
        {
            if (raw_size < 16)
                throw std::runtime_error("RPF8 resource raw size is too small");
            offset += 16;
            raw_size -= 16;
        }

        Rc<Stream> result = swref PartialStream(offset, is_compressed ? raw_size : size, input);

        if (Option<Ptr<Cipher>> cipher = RPF8::MakeCipher(header.PlatformId, entry.GetEncryptionKeyId()))
        {
            if (*cipher)
            {
                i64 chunk_size = entry.IsResource()
                    ? (is_compressed ? 0x80000 : size)
                    : (is_compressed ? 0x2000 : 0x1000);

                result = swref EcbCipherStream(std::move(result),
                    swnew RPF8::StridedCipher(
                        entry.GetEncryptionConfig(), raw_size, std::move(*cipher), chunk_size));
            }
        }
        else
        {
            throw std::runtime_error(fmt::format(
                "Unknown cipher key for RPF8 entry 0x{:08X}: 0x{:02X}, 0x{:02X}",
                entry.GetHash(), header.PlatformId, entry.GetEncryptionKeyId()));
        }

        switch (entry.GetCompressorId())
        {
            case 1:
                result = swref DecodeStream(std::move(result), CreateDeflateDecompressor(-15), size);
                break;
            case 2:
                result = swref DecodeStream(std::move(result), CreateOodleDecompressor(size), size);
                break;
        }

        return result;
    }

    Rc<FileDevice> LoadRPF8(Rc<Stream> input)
    {'''
    replace_once(rpf8_cpp, marker, injected, "direct RPF8 hash opener")

    replace_once(
        explorer,
        '    const char* vox_entry = std::getenv("SWAGE_EXTRACT_ENTRY");\n'
        '    const char* vox_out = std::getenv("SWAGE_EXTRACT_OUT");',
        '    const char* vox_entry = std::getenv("SWAGE_EXTRACT_ENTRY");\n'
        '    const char* vox_hash = std::getenv("SWAGE_DIRECT_HASH");\n'
        '    const char* vox_ext = std::getenv("SWAGE_DIRECT_EXTID");\n'
        '    const char* vox_out = std::getenv("SWAGE_EXTRACT_OUT");',
        "headless direct-hash environment",
    )

    direct_block = r'''    if (vox_rpf && vox_hash && vox_out)
    {
        const u32 direct_hash = static_cast<u32>(std::strtoul(vox_hash, nullptr, 16));
        const u8 direct_ext = vox_ext
            ? static_cast<u8>(std::strtoul(vox_ext, nullptr, 0))
            : 0xFF;

        SwLogInfo(
            "VOX headless direct-hash: archive='{}' hash=0x{:08X} ext=0x{:02X} out='{}'",
            vox_rpf, direct_hash, direct_ext, vox_out);

        Rc<Stream> input = Win32FileOpen(vox_rpf, true);
        if (!input)
        {
            SwLogError("VOX headless direct-hash: cannot open archive {}", vox_rpf);
            std::exit(30);
        }

        Rage::datResourceFileHeader resource_header {};
        Rc<Stream> handle;
        try
        {
            handle = Rage::OpenRPF8EntryByHash(std::move(input), direct_hash, direct_ext, &resource_header);
        }
        catch (const std::exception& ex)
        {
            SwLogError("VOX headless direct-hash extraction failed: {}", ex.what());
            std::exit(31);
        }

        if (!handle)
        {
            SwLogError("VOX headless direct-hash: entry returned no stream");
            std::exit(32);
        }

        String output_path(vox_out);
        Rc<Stream> output = LocalFiles()->Create(output_path, true, true);
        if (!output)
        {
            SwLogError("VOX headless direct-hash: cannot create output {}", vox_out);
            std::exit(33);
        }

        if (resource_header.Magic == 0x38435352)
            output->Write(&resource_header, sizeof(resource_header));

        handle->Rewind();
        if (i64 copied = handle->CopyTo(*output); copied != handle->Size())
        {
            SwLogError(
                "VOX headless direct-hash: incomplete copy ({} of {} bytes)", copied, handle->Size());
            std::exit(34);
        }

        SwLogInfo("VOX headless direct-hash: extraction complete ({} bytes)", handle->Size());
        std::exit(0);
    }

    if (vox_rpf && vox_entry && vox_out)
    {'''
    replace_once(
        explorer,
        '    if (vox_rpf && vox_entry && vox_out)\n    {',
        direct_block,
        "headless direct-hash extraction path",
    )

    print("[PATCH] Swage direct-by-hash extraction path is ready")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
