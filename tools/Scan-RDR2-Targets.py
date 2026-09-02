#!/usr/bin/env python3
import argparse
import csv
import json
import math
import struct
from collections import Counter
from pathlib import Path

VERSION = "0.2.0"

SYSTEM_RULES = {
    "interaction": [("interaction",16),("scenario",14),("ambient",10),("script",8),("common",8),("packs",6),("speech",12),("dialog",12),("ped",7)],
    "melee_animation": [("anim",18),("clip",12),("motion",12),("melee",18),("combat",14),("fight",14),("move",8),("ped",6),("common",5),("packs",5)],
    "ped_ai": [("ped",16),("ai",12),("task",14),("scenario",12),("ambient",9),("population",12),("common",5),("packs",5)],
    "interiors_objects": [("interior",18),("door",18),("prop",12),("object",10),("map",8),("level",7),("common",4),("packs",4)],
    "audio": [("audio",20),("speech",16),("voice",16),("sound",14),("stream",8),("resident",8),("ambient",8),("weapon",5)],
    "weapons": [("weapon",18),("gun",12),("melee",10),("combat",8),("common",4)],
    "world_physics": [("physics",18),("material",14),("weather",14),("fire",14),("vegetation",12),("world",10),("level",7),("common",4)],
}

GENERIC_PRIORITIES = [
    ("common",14,"shared/common systems"),
    ("packs",12,"streamed/content packs"),
    ("update",11,"update/override data"),
    ("levels",7,"world/level resources"),
    ("x64",4,"platform data"),
]

SIGNATURES = {b"RPF8":"RPF8",b"8FPR":"8FPR",b"RSC8":"RSC8",b"8CSR":"8CSR",b"RSC7":"RSC7",b"7CSR":"7CSR"}


def normalize_path(s):
    return Path((s or "").strip().strip('"')).expanduser().resolve()


def is_rdr2_root(p):
    return p.is_dir() and (p / "RDR2.exe").is_file()


def find_root(explicit):
    if explicit:
        p = normalize_path(explicit)
        if is_rdr2_root(p):
            return p
        raise SystemExit(f"[ERREUR] Le dossier ne contient pas RDR2.exe : {p}")
    candidates = []
    for drive in "CDEFGH":
        base = Path(f"{drive}:/")
        candidates.extend([
            base / "Program Files/Rockstar Games/Red Dead Redemption 2",
            base / "Program Files (x86)/Steam/steamapps/common/Red Dead Redemption 2",
            base / "SteamLibrary/steamapps/common/Red Dead Redemption 2",
            base / "Epic Games/RedDeadRedemption2",
            base / "Epic Games/Red Dead Redemption 2",
            base / "Games/Red Dead Redemption 2",
            base / "Jeux/Red Dead Redemption 2",
            base / "Jeux Epic/RedDeadRedemption2",
            base / "Jeux Epic/Red Dead Redemption 2",
        ])
    for p in candidates:
        try:
            if is_rdr2_root(p):
                return p.resolve()
        except OSError:
            pass
    print("[INFO] RDR2 n'a pas ete trouve automatiquement.")
    while True:
        p = normalize_path(input("Dossier contenant RDR2.exe : ").strip())
        if is_rdr2_root(p):
            return p
        print("[ERREUR] RDR2.exe introuvable dans ce dossier.")


def shannon_entropy(data):
    if not data:
        return 0.0
    counts = Counter(data)
    total = len(data)
    return -sum((n/total)*math.log2(n/total) for n in counts.values())


def printable_ratio(data):
    if not data:
        return 0.0
    return sum(1 for b in data if b in (9,10,13) or 32 <= b <= 126) / len(data)


def zero_ratio(data):
    return data.count(0) / len(data) if data else 0.0


def plausible_header_values(entry_count, names_len, size):
    if not (0 < entry_count <= 10_000_000):
        return False
    if not (0 <= names_len <= min(size, 2_000_000_000)):
        return False
    return 16 + entry_count * 24 <= size + 64 * 1024 * 1024


def parse_header(head, size):
    if len(head) < 16:
        return {"magic":head[:4].decode("ascii","replace"),"byte_order":"unknown","entry_count":None,"names_length":None,"decryption_tag":None,"platform_id":None,"header_valid":False}
    raw_magic = head[:4]
    candidates = []
    for order, fmt in (("little","<"),("big",">")):
        entry_count, names_len = struct.unpack(fmt+"II", head[4:12])
        decryption_tag, platform_id = struct.unpack(fmt+"HH", head[12:16])
        candidates.append((plausible_header_values(entry_count,names_len,size),order,entry_count,names_len,decryption_tag,platform_id))
    candidates.sort(key=lambda x:(x[0],x[1]=="little"), reverse=True)
    chosen = candidates[0]
    return {
        "magic":raw_magic.decode("ascii","replace"),"byte_order":chosen[1],"entry_count":chosen[2],"names_length":chosen[3],
        "decryption_tag":f"0x{chosen[4]:04X}","platform_id":f"0x{chosen[5]:04X}",
        "header_valid":bool(raw_magic in (b"RPF8",b"8FPR") and chosen[0])
    }


def toc_plaintext_score(sample, entry_count, archive_size, order):
    if not sample or not entry_count:
        return 0.0, 0
    fmt = "<" if order == "little" else ">"
    tested = min(entry_count, len(sample)//24, 256)
    plausible = 0
    for i in range(tested):
        e = sample[i*24:(i+1)*24]
        if len(e) < 24:
            break
        words = struct.unpack(fmt+"IIIIII", e)
        nonzero = sum(1 for w in words if w != 0)
        smallish = sum(1 for w in words if w < archive_size or w < 0x01000000)
        if nonzero >= 2 and smallish >= 3:
            plausible += 1
    return (plausible/tested if tested else 0.0), tested


def sample_signature_hits(path, size, deep):
    hits = {name:0 for name in SIGNATURES.values()}
    offsets = {name:[] for name in SIGNATURES.values()}
    windows = []
    if size <= 0:
        return {"counts":hits,"offsets":offsets,"sampled_bytes":0}
    if deep:
        chunk = 8*1024*1024
        pos = 0
        while pos < size:
            windows.append((pos,min(chunk,size-pos)))
            pos += chunk
    else:
        window = 2*1024*1024
        positions = [0,max(0,size//4),max(0,size//2),max(0,(3*size)//4),max(0,size-window)]
        seen = set()
        for pos in positions:
            pos = max(0,min(pos,max(0,size-1)))
            if pos not in seen:
                seen.add(pos); windows.append((pos,min(window,size-pos)))
    sampled = 0
    with path.open("rb") as f:
        for pos,length in windows:
            f.seek(pos); data = f.read(length); sampled += len(data)
            for sig,name in SIGNATURES.items():
                start = 0
                while True:
                    idx = data.find(sig,start)
                    if idx < 0: break
                    absolute = pos+idx; hits[name] += 1
                    if len(offsets[name]) < 12: offsets[name].append(absolute)
                    start = idx+1
    return {"counts":hits,"offsets":offsets,"sampled_bytes":sampled}


def score_archive(relative):
    text = relative.lower().replace("\\","/")
    system_scores = {}; reasons = []
    for system,rules in SYSTEM_RULES.items():
        score = 0; matched = []
        for token,weight in rules:
            if token in text:
                score += weight; matched.append(token)
        system_scores[system] = score
        if matched: reasons.append(f"{system}:"+",".join(sorted(set(matched))))
    generic = 0
    for token,weight,label in GENERIC_PRIORITIES:
        if token in text:
            generic += weight; reasons.append(label)
    system_scores["generic"] = generic
    total = generic + sum(sorted((v for k,v in system_scores.items() if k != "generic"), reverse=True)[:3])
    return total, system_scores, "; ".join(reasons)


def scan_archive(root, path, deep):
    rel = str(path.relative_to(root)).replace("\\","/")
    size = path.stat().st_size
    total_score, systems, reasons = score_archive(rel)
    with path.open("rb") as f:
        head = f.read(64)
        hdr = parse_header(head,size)
        toc_sample = f.read(min(64*1024,max(0,size-64)))
    entropy = shannon_entropy(toc_sample); printable = printable_ratio(toc_sample); zeros = zero_ratio(toc_sample)
    plausibility,tested = toc_plaintext_score(toc_sample,hdr.get("entry_count") or 0,size,hdr.get("byte_order") or "little")
    if not hdr["header_valid"]:
        toc_state = "not_rpf8_or_unrecognized"
    elif entropy >= 7.55 and printable < 0.42 and zeros < 0.08:
        toc_state = "encrypted_or_high_entropy_likely"
    elif tested >= 8 and plausibility >= 0.55:
        toc_state = "plaintext_candidate_needs_decoder_validation"
    else:
        toc_state = "unknown_needs_reader"
    sig = sample_signature_hits(path,size,deep)
    resource_hint = sig["counts"].get("RSC8",0)+sig["counts"].get("8CSR",0)
    if resource_hint:
        total_score += min(20,resource_hint*2)
        reasons = (reasons+"; " if reasons else "")+f"sampled RSC8 signatures={resource_hint}"
    return {
        "RelativePath":rel,"SizeBytes":size,"Magic":hdr["magic"],"HeaderValid":hdr["header_valid"],"ByteOrderGuess":hdr["byte_order"],
        "EntryCountDeclared":hdr["entry_count"],"NamesLengthDeclared":hdr["names_length"],"DecryptionTag":hdr["decryption_tag"],"PlatformId":hdr["platform_id"],
        "TocEntropy":round(entropy,5),"TocPrintableRatio":round(printable,5),"TocZeroRatio":round(zeros,5),"TocPlainPlausibility":round(plausibility,5),
        "TocEntriesTested":tested,"TocState":toc_state,"SampledBytesForSignatures":sig["sampled_bytes"],
        "RPF8Hits":sig["counts"].get("RPF8",0)+sig["counts"].get("8FPR",0),"RSC8Hits":resource_hint,"RSC7Hits":sig["counts"].get("RSC7",0)+sig["counts"].get("7CSR",0),
        "PriorityScore":total_score,"TopSystem":max((k for k in systems if k != "generic"),key=lambda k:systems[k],default="generic"),
        "InteractionScore":systems.get("interaction",0),"MeleeAnimationScore":systems.get("melee_animation",0),"PedAiScore":systems.get("ped_ai",0),
        "InteriorsObjectsScore":systems.get("interiors_objects",0),"AudioScore":systems.get("audio",0),"WeaponsScore":systems.get("weapons",0),"WorldPhysicsScore":systems.get("world_physics",0),
        "Reasons":reasons,"SignatureOffsets":sig["offsets"],"HeaderHex":head[:32].hex(),
    }


def write_csv(path, rows, fields):
    with path.open("w",newline="",encoding="utf-8-sig") as f:
        w = csv.DictWriter(f,fieldnames=fields); w.writeheader()
        for row in rows:
            out = {}
            for field in fields:
                val = row.get(field)
                if isinstance(val,(dict,list)): val = json.dumps(val,ensure_ascii=False,separators=(",",":"))
                out[field] = val
            w.writerow(out)


def main():
    ap = argparse.ArgumentParser(description="VOX RDR2 targeted RPF8 reference inventory")
    ap.add_argument("root",nargs="?",default="",help="RDR2 directory containing RDR2.exe")
    ap.add_argument("--output",default="VOX-RDR2-Targets",help="Output directory")
    ap.add_argument("--deep",action="store_true",help="Scan every byte for RPF/RSC signatures (much slower)")
    ap.add_argument("--top",type=int,default=30,help="Number of priority archives in target report")
    args = ap.parse_args()
    root = find_root(args.root); outdir = Path(args.output).resolve(); outdir.mkdir(parents=True,exist_ok=True)
    print(f"VOX RDR2 Targeted Inventory v{VERSION}"); print(f"Root: {root}"); print(f"Mode: {'DEEP' if args.deep else 'sampled/read-only'}")
    archives = sorted(root.rglob("*.rpf"),key=lambda p:str(p).lower())
    if not archives: raise SystemExit("[ERREUR] Aucun .rpf trouve dans l'installation RDR2.")
    rows = []; failures = []
    for idx,path in enumerate(archives,1):
        rel = str(path.relative_to(root)).replace("\\","/"); print(f"[{idx}/{len(archives)}] {rel}")
        try: rows.append(scan_archive(root,path,args.deep))
        except Exception as e: failures.append({"RelativePath":rel,"Error":repr(e)})
    rows.sort(key=lambda r:(-int(r["PriorityScore"]),r["RelativePath"].lower())); top = rows[:max(1,args.top)]
    fields = ["RelativePath","SizeBytes","Magic","HeaderValid","ByteOrderGuess","EntryCountDeclared","NamesLengthDeclared","DecryptionTag","PlatformId","TocEntropy","TocPrintableRatio","TocZeroRatio","TocPlainPlausibility","TocEntriesTested","TocState","SampledBytesForSignatures","RPF8Hits","RSC8Hits","RSC7Hits","PriorityScore","TopSystem","InteractionScore","MeleeAnimationScore","PedAiScore","InteriorsObjectsScore","AudioScore","WeaponsScore","WorldPhysicsScore","Reasons","SignatureOffsets","HeaderHex"]
    inventory_csv = outdir/"RDR2-archive-inventory.csv"; targets_csv = outdir/"RDR2-targets.csv"
    write_csv(inventory_csv,rows,fields); write_csv(targets_csv,top,fields)
    summary = {
        "tool":f"VOX RDR2 Targeted Inventory {VERSION}","root":str(root),"read_only":True,"deep":bool(args.deep),"archive_count":len(archives),"scanned_ok":len(rows),"failures":failures,
        "toc_state_counts":dict(Counter(r["TocState"] for r in rows)),"magic_counts":dict(Counter(r["Magic"] for r in rows)),
        "top_targets":[{"path":r["RelativePath"],"priority":r["PriorityScore"],"system":r["TopSystem"],"toc":r["TocState"],"entries":r["EntryCountDeclared"],"rsc8_sample_hits":r["RSC8Hits"],"reasons":r["Reasons"]} for r in top],
        "notes":["RPF8 hashed names are not invented or guessed by this scanner.","TocState is heuristic only; encrypted/high-entropy data is not decrypted.","No RDR2 game file is modified, copied, or extracted.","RSC8/RPF8 signature hits are raw byte-pattern hints, not decoded assets."]
    }
    summary_json = outdir/"RDR2-target-summary.json"; summary_json.write_text(json.dumps(summary,indent=2,ensure_ascii=False),encoding="utf-8")
    txt = outdir/"RDR2-targets.txt"; lines = [f"VOX RDR2 Targeted Inventory v{VERSION}",f"Root: {root}",f"Archives: {len(archives)} | OK: {len(rows)} | Failures: {len(failures)}",f"Mode: {'deep' if args.deep else 'sampled/read-only'}","","TOP TARGET ARCHIVES","==================="]
    for i,r in enumerate(top,1):
        lines += [f"{i:02d}. {r['RelativePath']}",f"    priority={r['PriorityScore']} system={r['TopSystem']} entries={r['EntryCountDeclared']} toc={r['TocState']}",f"    sampled RSC8={r['RSC8Hits']} RPF8={r['RPF8Hits']} | {r['Reasons'] or 'generic archive; needs internal reader'}"]
    lines += ["","NEXT STEP","=========","Send RDR2-targets.csv + RDR2-target-summary.json + RDR2-targets.txt.","The next extractor stage can then focus only on the few archives that matter.","No game data is copied by this scanner."]
    txt.write_text("\n".join(lines)+"\n",encoding="utf-8")
    print(""); print(f"[OK] Inventory : {inventory_csv}"); print(f"[OK] Targets   : {targets_csv}"); print(f"[OK] Summary   : {summary_json}"); print(f"[OK] Text      : {txt}"); print("Lecture seule : aucun fichier RDR2 modifie ou extrait.")

if __name__ == "__main__":
    main()
