#!/usr/bin/env python3
import argparse, json, re
from collections import Counter
from pathlib import Path

HEX = re.compile(r"(?P<name>[A-Za-z_$][A-Za-z0-9_$]*)\s*(?::|=)\s*(?P<value>0x[0-9A-Fa-f]+)")

def parse_offsets(path):
    text = Path(path).read_text(encoding="utf-8", errors="replace")
    out = {}
    for m in HEX.finditer(text):
        out[m.group("name")] = int(m.group("value"), 16)
    return out

def local_votes(anchors, src, radius):
    vals = [a["delta"] for a in anchors if abs(a["src_vaddr"] - src) <= radius]
    return Counter(vals)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("map_json")
    ap.add_argument("offsets_js")
    ap.add_argument("-o", "--output", required=True)
    ap.add_argument("--module", default="libSceNKWebKit.sprx")
    ap.add_argument("--radius", type=lambda s:int(s,0), default=0x20000)
    args = ap.parse_args()

    db = json.loads(Path(args.map_json).read_text(encoding="utf-8"))
    modules = db.get("modules", {})
    chosen_key = None
    for k in modules:
        if k.endswith(args.module):
            chosen_key = k
            break
    if not chosen_key:
        raise SystemExit(f"module not found in map: {args.module}")

    m = modules[chosen_key]
    anchors = m.get("anchors", [])
    offsets = parse_offsets(args.offsets_js)
    rows = []

    global_delta = None
    if m.get("top_deltas"):
        global_delta = m["top_deltas"][0]["delta"]

    for name, src in sorted(offsets.items()):
        votes = local_votes(anchors, src, args.radius)
        if votes:
            delta, support = votes.most_common(1)[0]
            total = sum(votes.values())
            confidence = support / total if total else 0.0
            mode = "local"
        elif global_delta is not None:
            delta, support, total, confidence, mode = global_delta, 0, 0, 0.0, "global-fallback"
        else:
            continue
        rows.append({
            "name": name,
            "src_1360": src,
            "candidate_1400": src + delta,
            "delta": delta,
            "support": support,
            "local_votes": total,
            "confidence": round(confidence, 4),
            "mode": mode,
        })

    out = {
        "module": chosen_key,
        "radius": args.radius,
        "source_offset_count": len(offsets),
        "candidates": rows,
    }
    Path(args.output).write_text(json.dumps(out, indent=2), encoding="utf-8")

    md = Path(args.output).with_suffix(".md")
    lines = [
        "# PS5 13.60 -> 14.00 offset candidates",
        "",
        f"Module: `{chosen_key}`",
        f"Source constants parsed: **{len(offsets)}**",
        f"Candidates emitted: **{len(rows)}**",
        "",
        "| Name | 13.60 | 14.00 candidate | Delta | Confidence | Mode |",
        "|---|---:|---:|---:|---:|---|",
    ]
    for r in rows:
        lines.append(
            f"| `{r['name']}` | `0x{r['src_1360']:X}` | `0x{r['candidate_1400']:X}` | "
            f"`{r['delta']:+#x}` | {r['confidence']:.2f} | {r['mode']} |"
        )
    md.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"[OK] translated {len(rows)} offsets -> {args.output}")
    print(f"[OK] report -> {md}")

if __name__ == "__main__":
    main()
