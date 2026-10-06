#!/usr/bin/env python3
import argparse, hashlib, json, struct
from collections import Counter, defaultdict
from pathlib import Path

PT_LOAD = 1
PF_X = 1

def exec_ranges(data):
    if len(data) < 64 or data[:4] != b"\x7fELF" or data[4] != 2 or data[5] != 1:
        return [(0, len(data), 0)]
    phoff = struct.unpack_from("<Q", data, 32)[0]
    entsz = struct.unpack_from("<H", data, 54)[0]
    num = struct.unpack_from("<H", data, 56)[0]
    out = []
    for i in range(num):
        off = phoff + i * entsz
        if off + 56 > len(data):
            break
        p_type, p_flags = struct.unpack_from("<II", data, off)
        p_offset = struct.unpack_from("<Q", data, off + 8)[0]
        p_vaddr = struct.unpack_from("<Q", data, off + 16)[0]
        p_filesz = struct.unpack_from("<Q", data, off + 32)[0]
        if p_type == PT_LOAD and (p_flags & PF_X) and p_filesz and p_offset + p_filesz <= len(data):
            out.append((p_offset, p_filesz, p_vaddr))
    return out or [(0, len(data), 0)]

def windows(data, win, stride):
    for fo, size, va in exec_ranges(data):
        end = fo + size
        for pos in range(fo, max(fo, end - win + 1), stride):
            chunk = data[pos:pos+win]
            if len(chunk) != win:
                continue
            # Skip unhelpful uniform/padding windows.
            if len(set(chunk)) < 6:
                continue
            yield hashlib.blake2b(chunk, digest_size=16).digest(), pos, va + (pos - fo)

def map_module(src_path, dst_path, win=48, stride=16):
    a = src_path.read_bytes()
    b = dst_path.read_bytes()

    aidx = defaultdict(list)
    bidx = defaultdict(list)
    for h, fo, va in windows(a, win, stride):
        aidx[h].append((fo, va))
    for h, fo, va in windows(b, win, stride):
        bidx[h].append((fo, va))

    anchors = []
    deltas = Counter()
    for h in aidx.keys() & bidx.keys():
        # Unique-on-both-sides anchors are strongest.
        if len(aidx[h]) == 1 and len(bidx[h]) == 1:
            afo, ava = aidx[h][0]
            bfo, bva = bidx[h][0]
            delta = bva - ava
            anchors.append({
                "src_file": afo, "dst_file": bfo,
                "src_vaddr": ava, "dst_vaddr": bva,
                "delta": delta
            })
            deltas[delta] += 1

    anchors.sort(key=lambda x: x["src_vaddr"])
    top = [{"delta": d, "count": n} for d, n in deltas.most_common(20)]
    return {
        "source": str(src_path),
        "target": str(dst_path),
        "window": win,
        "stride": stride,
        "anchor_count": len(anchors),
        "top_deltas": top,
        "anchors": anchors,
    }

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("fw1360")
    ap.add_argument("fw1400")
    ap.add_argument("-o", "--output", required=True)
    ap.add_argument("--window", type=int, default=48)
    ap.add_argument("--stride", type=int, default=16)
    args = ap.parse_args()

    aroot, broot = Path(args.fw1360), Path(args.fw1400)
    common = sorted(
        p.relative_to(aroot) for p in aroot.rglob("*")
        if p.is_file() and (broot / p.relative_to(aroot)).is_file()
    )

    modules = {}
    for rel in common:
        try:
            m = map_module(aroot / rel, broot / rel, args.window, args.stride)
            modules[rel.as_posix()] = m
            print(f"[MAP] {rel}: {m['anchor_count']} unique anchors")
        except Exception as e:
            modules[rel.as_posix()] = {"error": str(e)}
            print(f"[WARN] {rel}: {e}")

    Path(args.output).parent.mkdir(parents=True, exist_ok=True)
    Path(args.output).write_text(json.dumps({"modules": modules}, indent=2), encoding="utf-8")
    print(f"[OK] anchor map -> {args.output}")

if __name__ == "__main__":
    main()
