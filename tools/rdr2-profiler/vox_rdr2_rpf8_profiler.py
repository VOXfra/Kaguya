#!/usr/bin/env python3
from __future__ import annotations
import argparse, csv, json, os, re, sys
from collections import Counter, defaultdict
from pathlib import Path

TOOL = "VOX RDR2 RPF8 Research Profiler"
VERSION = "0.5.0"
TEXT_EXTS = {'.lua','.md','.txt','.json','.xml','.meta','.csv','.yml','.yaml','.ini','.cfg','.c','.cpp','.h','.hpp'}
KNOWN_GAME_EXTS = {
    'rpf','ymf','ydr','yft','ydd','ytd','ybn','ybd','ypd','ybs','ysd','ymt','ysc','ycs',
    'mrf','cut','gfx','ycd','yld','ypmd','ypm','yed','ypt','ymap','ytyp','ych','yldb','yjd',
    'yad','ynv','yhn','ypl','ynd','yvr','ywr','ynh','yfd','yas','awc','rel','xml','meta','pso'
}
SYSTEM_KEYWORDS = {
    'interaction': ['greet','antagon','rob','defus','prompt','interact','conversation','dialog','social','ambient','context','talk','speech','threat','intimid','respond'],
    'melee': ['melee','combat','grapple','grab','punch','fight','brawl','choke','takedown','counter','block','struggle'],
    'locomotion': ['locom','walk','run','sprint','turn','slope','climb','vault','cover','movement','move_','ledge','mantle'],
    'ped_ai': ['ai/','blackboard','combat_style','combatstyle','decision','task','flee','relationship','perception','event','ped_','reaction'],
    'interiors_objects': ['door','interior','light','lamp','switch','room','prop','object','fixture','window','drawer','cabinet'],
    'scenario': ['scenario','world_human','amb_','ambient_','conditional_anim','conditionalanim'],
    'weapons': ['weapon','gun','holster','ammo','reload','rifle','pistol','revolver','shotgun','throwable'],
    'audio': ['audio','sfx','sound','speech','voice','resident','music'],
    'world_physics': ['physics','ragdoll','collision','material','wind','cloth','force','impact']
}
EXT_BONUS = {'ycd':45,'ymt':40,'yas':38,'ytyp':28,'ymap':24,'ych':24,'yld':20,'yldb':20,'ybn':18,'ynd':18,'ynv':18,'ypt':12,'ydr':8,'ydd':8,'yft':8,'ytd':4,'rpf':2,'bin':0}

def norm(s: str) -> str:
    return s.strip().replace('\\','/').lower()

def at_string_hash(s: str) -> int:
    h = 0
    for ch in norm(s):
        v = ord(ch)
        h = (h + v) & 0xFFFFFFFF
        h = (h + ((h << 10) & 0xFFFFFFFF)) & 0xFFFFFFFF
        h ^= (h >> 6)
    h = (h + ((h << 3) & 0xFFFFFFFF)) & 0xFFFFFFFF
    h ^= (h >> 11)
    h = (h + ((h << 15) & 0xFFFFFFFF)) & 0xFFFFFFFF
    return h & 0xFFFFFFFF

def parse_bool(v):
    return str(v).strip().lower() in ('true','1','yes','y')

def parse_int(v, default=0):
    try:
        s=str(v).strip()
        return int(s,16) if s.lower().startswith('0x') else int(s)
    except Exception:
        return default

def read_csv(path: Path):
    with path.open('r', encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f))

def write_csv(path: Path, fieldnames, rows):
    with path.open('w', encoding='utf-8-sig', newline='') as f:
        w=csv.DictWriter(f, fieldnames=fieldnames, extrasaction='ignore')
        w.writeheader(); w.writerows(rows)

def strip_wrapping(s: str) -> str:
    s=s.strip().strip('"\'`')
    s=s.strip('()[]{}<>;,')
    return s.strip()

def candidate_variants(raw: str):
    raw=strip_wrapping(raw)
    if len(raw)<4 or len(raw)>260: return []
    if any(ord(c)<32 for c in raw): return []
    s=norm(raw)
    if s.startswith(('http://','https://')): return []
    if ' ' in s and '/' not in s and '\\' not in raw and '.' not in s: return []
    out=[]
    def add(name, ext, mode):
        name=norm(name).strip('/')
        if len(name)>=2:
            out.append((name, ext, mode))
    m=re.match(r'^(.*)\.([a-z0-9]{2,5})$', s)
    if m and m.group(2) in KNOWN_GAME_EXTS:
        stem=m.group(1).strip('/')
        ext=m.group(2)
        add(stem,ext,'exact_path_ext')
        base=stem.rsplit('/',1)[-1]
        if base!=stem: add(base,ext,'basename_ext')
    else:
        add(s,'','raw_name_hash')
        if '/' in s: add(s.rsplit('/',1)[-1],'','basename_hash')
    return out

def harvest_knowledge(root: Path):
    candidates={}
    file_count=0
    for p in root.rglob('*'):
        if not p.is_file() or p.suffix.lower() not in TEXT_EXTS: continue
        try:
            if p.stat().st_size>8*1024*1024: continue
            text=p.read_text(encoding='utf-8',errors='ignore')
        except Exception:
            continue
        file_count += 1
        rel=str(p.relative_to(root)).replace('\\','/')
        tokens=[]
        tokens += [m.group(1) for m in re.finditer(r'"([^"\r\n]{3,260})"',text)]
        tokens += [m.group(1) for m in re.finditer(r"'([^'\r\n]{3,260})'",text)]
        ext_pat='|'.join(sorted(KNOWN_GAME_EXTS,key=len,reverse=True))
        tokens += [m.group(0) for m in re.finditer(rf'(?i)[A-Za-z0-9_@.\-/\\]+\.(?:{ext_pat})\b',text)]
        tokens += [m.group(0) for m in re.finditer(r'(?i)\b(?:WORLD_[A-Z0-9_@-]+|[A-Z0-9_@-]*(?:ANIM|SCENARIO|INTERACT|COMBAT|MELEE|GREET|ANTAGON|DOOR|INTERIOR)[A-Z0-9_@-]*)\b',text)]
        for tok in tokens:
            for name, ext, mode in candidate_variants(tok):
                key=(name,ext,mode)
                item=candidates.get(key)
                if item is None:
                    candidates[key]={'name':name,'ext':ext,'mode':mode,'sources':set()}
                candidates[key]['sources'].add(rel)
    return list(candidates.values()), file_count

def classify(text: str):
    t=norm(text)
    systems=[]
    for system,kws in SYSTEM_KEYWORDS.items():
        if any(k in t for k in kws): systems.append(system)
    return systems

def confidence(mode, source_count, ext_match):
    base={'exact_path_ext':100,'basename_ext':86,'raw_name_hash':68,'basename_hash':55}.get(mode,50)
    if not ext_match and mode in ('raw_name_hash','basename_hash'): base-=4
    base += min(10,max(0,source_count-1)*2)
    return min(100,base)

def priority(entry, resolved_name='', systems=None, conf=0):
    systems=systems or []
    score=0
    ext=entry.get('extension','').lower()
    score += EXT_BONUS.get(ext,5)
    if entry.get('archive','').lower()=='common_0.rpf': score += 25
    if '/audio/' not in entry.get('archive','').lower(): score += 8
    if resolved_name: score += min(35, int(conf*0.35))
    if systems: score += min(40,15+7*len(systems))
    if parse_bool(entry.get('is_resource')): score += 4
    if ext in ('ycd','ymt','yas'): score += 8
    return score

def profile(entries_path: Path, archives_path: Path|None, knowledge: Path|None, out: Path):
    entries=read_csv(entries_path)
    archives=read_csv(archives_path) if archives_path and archives_path.exists() else []
    if not entries: raise RuntimeError('Entries CSV is empty')
    out.mkdir(parents=True,exist_ok=True)
    by_hash=defaultdict(list)
    ext_counts=Counter(); archive_counts=Counter(); archive_ext=Counter(); enc_keys=Counter(); compressors=Counter(); magics=Counter()
    for i,e in enumerate(entries):
        e['_row']=i
        h=parse_int(e.get('hash_hex'))
        e['_hash']=h
        ext=e.get('extension','').lower()
        by_hash[h].append(e)
        ext_counts[ext]+=1; archive_counts[e.get('archive','')]+=1; archive_ext[(e.get('archive',''),ext)]+=1
        if parse_bool(e.get('entry_encrypted')): enc_keys[e.get('enc_key_id','')]+=1
        compressors[e.get('compressor','')]+=1
        magics[e.get('raw_magic_at_offset','')]+=1
    matches=[]; candidate_count=0; knowledge_files=0
    if knowledge and knowledge.exists():
        candidates,knowledge_files=harvest_knowledge(knowledge)
        candidate_count=len(candidates)
        seen=set()
        for c in candidates:
            h=at_string_hash(c['name'])
            rows=by_hash.get(h,())
            if not rows: continue
            for e in rows:
                ext=e.get('extension','').lower()
                ext_match=(not c['ext']) or (ext==c['ext'])
                if c['ext'] and not ext_match: continue
                key=(e['_row'],c['name'],c['mode'])
                if key in seen: continue
                seen.add(key)
                srcs=sorted(c['sources'])
                systems=classify(c['name']+' '+' '.join(srcs))
                conf=confidence(c['mode'],len(srcs),ext_match)
                matches.append({'entry_row':e['_row'],'archive':e.get('archive',''),'index':e.get('index',''),'hash_hex':e.get('hash_hex',''),'extension':ext,'generated_name':e.get('generated_name',''),'resolved_candidate':c['name']+(('.'+c['ext']) if c['ext'] else ''),'candidate_stem':c['name'],'match_mode':c['mode'],'confidence':conf,'source_count':len(srcs),'sources':' | '.join(srcs[:8]),'systems':' | '.join(systems),'priority_score':priority(e,c['name'],systems,conf),'logical_size':e.get('logical_size',''),'on_disk_size':e.get('on_disk_size',''),'compressor':e.get('compressor',''),'enc_key_id':e.get('enc_key_id',''),'is_resource':e.get('is_resource',''),'raw_magic_at_offset':e.get('raw_magic_at_offset','')})
    best={}
    for m in matches:
        r=m['entry_row']; cur=best.get(r)
        rank=(int(m['confidence']),int(m['source_count']),int(m['priority_score']))
        if cur is None or rank>(int(cur['confidence']),int(cur['source_count']),int(cur['priority_score'])): best[r]=m
    pri=[]
    for e in entries:
        b=best.get(e['_row'])
        systems=[x.strip() for x in b['systems'].split('|') if x.strip()] if b else classify(e.get('archive','')+' '+e.get('extension',''))
        pri.append({'priority_score':priority(e,b['candidate_stem'] if b else '',systems,int(b['confidence']) if b else 0),'archive':e.get('archive',''),'index':e.get('index',''),'hash_hex':e.get('hash_hex',''),'extension':e.get('extension',''),'generated_name':e.get('generated_name',''),'resolved_candidate':b['resolved_candidate'] if b else '','confidence':b['confidence'] if b else '','systems':' | '.join(systems),'logical_size':e.get('logical_size',''),'on_disk_size':e.get('on_disk_size',''),'compressor':e.get('compressor',''),'enc_key_id':e.get('enc_key_id',''),'entry_encrypted':e.get('entry_encrypted',''),'is_resource':e.get('is_resource',''),'raw_magic_at_offset':e.get('raw_magic_at_offset','')})
    pri.sort(key=lambda r:(-int(r['priority_score']),r['archive'],parse_int(r['index'])))
    matches.sort(key=lambda r:(-int(r['confidence']),-int(r['priority_score']),r['archive'],parse_int(r['index'])))
    write_csv(out/'PROFILE-extension-counts.csv',['extension','count'],[{'extension':k,'count':v} for k,v in ext_counts.most_common()])
    write_csv(out/'PROFILE-archive-counts.csv',['archive','count'],[{'archive':k,'count':v} for k,v in archive_counts.most_common()])
    write_csv(out/'PROFILE-archive-extension-counts.csv',['archive','extension','count'],[{'archive':a,'extension':x,'count':n} for (a,x),n in sorted(archive_ext.items(),key=lambda kv:(kv[0][0],-kv[1],kv[0][1]))])
    write_csv(out/'PROFILE-entry-encryption-keys.csv',['enc_key_id','count'],[{'enc_key_id':k,'count':v} for k,v in enc_keys.most_common()])
    write_csv(out/'PROFILE-compressors.csv',['compressor','count'],[{'compressor':k,'count':v} for k,v in compressors.most_common()])
    write_csv(out/'PROFILE-raw-magics.csv',['raw_magic_at_offset','count'],[{'raw_magic_at_offset':k,'count':v} for k,v in magics.most_common()])
    resolved_fields=['entry_row','archive','index','hash_hex','extension','generated_name','resolved_candidate','candidate_stem','match_mode','confidence','source_count','sources','systems','priority_score','logical_size','on_disk_size','compressor','enc_key_id','is_resource','raw_magic_at_offset']
    write_csv(out/'PROFILE-resolved-names.csv',resolved_fields,matches)
    priority_fields=['priority_score','archive','index','hash_hex','extension','generated_name','resolved_candidate','confidence','systems','logical_size','on_disk_size','compressor','enc_key_id','entry_encrypted','is_resource','raw_magic_at_offset']
    write_csv(out/'PROFILE-priority-candidates.csv',priority_fields,pri)
    system_counts=Counter()
    for b in best.values():
        for s in b['systems'].split('|'):
            s=s.strip()
            if s: system_counts[s]+=1
    common=sum(1 for e in entries if e.get('archive','').lower()=='common_0.rpf')
    summary={'tool':TOOL,'version':VERSION,'entries':len(entries),'archives':len(set(e.get('archive','') for e in entries)),'common_0_entries':common,'knowledge_files_scanned':knowledge_files,'knowledge_candidates':candidate_count,'candidate_matches':len(matches),'unique_entries_resolved':len(best),'extension_counts':dict(ext_counts),'entry_encryption_key_counts':dict(enc_keys),'compressor_counts':dict(compressors),'resolved_system_counts':dict(system_counts),'limits':['Resolved names are hash matches against public candidate strings, not authoritative Rockstar name tables.','Confidence distinguishes exact path+extension matches from weaker hash-only candidates.','No RDR2 asset bytes are read or extracted by this profiler.']}
    (out/'PROFILE-summary.json').write_text(json.dumps(summary,indent=2,ensure_ascii=False),encoding='utf-8')
    with (out/'PROFILE-summary.txt').open('w',encoding='utf-8') as f:
        f.write(f"{TOOL} v{VERSION}\n"+"="*60+"\n\n")
        f.write(f"Entries profiled: {len(entries)}\nArchives: {summary['archives']}\ncommon_0.rpf entries: {common}\n")
        f.write(f"Knowledge files scanned: {knowledge_files}\nCandidate strings: {candidate_count}\nCandidate matches: {len(matches)}\nUnique entries with at least one candidate: {len(best)}\n\n")
        f.write("EXTENSIONS\n----------\n")
        for k,v in ext_counts.most_common(): f.write(f"{k or '(blank)'}: {v}\n")
        f.write("\nENTRY ENCRYPTION KEYS\n---------------------\n")
        if enc_keys:
            for k,v in enc_keys.most_common(): f.write(f"{k}: {v}\n")
        else: f.write("none\n")
        f.write("\nCOMPRESSORS\n-----------\n")
        for k,v in compressors.most_common(): f.write(f"{k}: {v}\n")
        f.write("\nRESOLVED SYSTEM HITS\n--------------------\n")
        if system_counts:
            for k,v in system_counts.most_common(): f.write(f"{k}: {v}\n")
        else: f.write("none\n")
        f.write("\nTOP RESOLVED CANDIDATES\n-----------------------\n")
        for m in matches[:80]: f.write(f"[{m['confidence']:>3}] {m['archive']} #{m['index']} {m['hash_hex']}.{m['extension']} -> {m['resolved_candidate']} | {m['systems']}\n")
        f.write("\nTOP PROJECT PRIORITIES\n----------------------\n")
        for r in pri[:100]:
            name=r['resolved_candidate'] or r['generated_name']
            f.write(f"[{r['priority_score']:>3}] {r['archive']} #{r['index']} {name} | ext={r['extension']} systems={r['systems']} enc={r['enc_key_id']} comp={r['compressor']}\n")
        f.write("\nNo game asset was extracted.\n")
    print(f"[OK] Profile complete: {out}")
    print(f"[OK] entries={len(entries)} common_0={common} resolved={len(best)} matches={len(matches)} candidates={candidate_count}")
    return summary

def self_test(tmp: Path):
    tmp.mkdir(parents=True,exist_ok=True)
    knowledge=tmp/'knowledge'; knowledge.mkdir(exist_ok=True)
    (knowledge/'anims.lua').write_text('local dict = "test/path/interaction_greet.ycd"\nlocal s="WORLD_HUMAN_LEAN_BACK_WALL"\n',encoding='utf-8')
    h=at_string_hash('test/path/interaction_greet')
    entries=tmp/'entries.csv'
    with entries.open('w',encoding='utf-8',newline='') as f:
        fields=['archive','index','hash_hex','extension','generated_name','enc_config','enc_key_id','entry_encrypted','is_resource','signature_protected','is_directory','compressor','byte_offset','on_disk_size','end_offset','logical_size','virtual_flags_hex','physical_flags_hex','offset_in_bounds','range_in_bounds','raw_magic_at_offset']
        w=csv.DictWriter(f,fieldnames=fields); w.writeheader(); w.writerow({'archive':'common_0.rpf','index':'0','hash_hex':f'0x{h:08X}','extension':'ycd','generated_name':f'{h:08X}.ycd','enc_key_id':'255','entry_encrypted':'false','is_resource':'true','compressor':'0','logical_size':'4096','on_disk_size':'4096','raw_magic_at_offset':'RSC8'})
    out=tmp/'out'; s=profile(entries,None,knowledge,out); rows=read_csv(out/'PROFILE-resolved-names.csv')
    ok=(s['entries']==1 and s['unique_entries_resolved']>=1 and any(r['resolved_candidate']=='test/path/interaction_greet.ycd' and int(r['confidence'])>=100 for r in rows))
    print('[SELFTEST] profiler exact hash+extension match '+('OK' if ok else 'FAILED'))
    return ok

def autodetect(base: Path, name: str):
    candidates=[base/name, base/'VOX-RDR2-TFIT2-Catalog'/name, base.parent/'VOX-RDR2-TFIT2-Catalog'/name]
    for p in candidates:
        if p.exists(): return p
    return None

def main():
    ap=argparse.ArgumentParser(description=TOOL)
    ap.add_argument('--entries',type=Path); ap.add_argument('--archives',type=Path); ap.add_argument('--knowledge',type=Path); ap.add_argument('--out',type=Path); ap.add_argument('--self-test',action='store_true')
    args=ap.parse_args()
    if args.self_test:
        import tempfile
        with tempfile.TemporaryDirectory() as d: return 0 if self_test(Path(d)) else 2
    base=Path.cwd(); entries=args.entries or autodetect(base,'RPF8-decrypted-entries.csv'); archives=args.archives or autodetect(base,'RPF8-decrypted-archives.csv')
    if not entries:
        print('[ERROR] RPF8-decrypted-entries.csv not found. Put this tool next to VOX-RDR2-TFIT2-Catalog or pass --entries.',file=sys.stderr); return 1
    out=args.out or (base/'VOX-RDR2-RPF8-Research-Profile')
    try:
        profile(entries,archives,args.knowledge,out); return 0
    except Exception as e:
        print('[ERROR] '+str(e),file=sys.stderr); return 1

if __name__=='__main__': raise SystemExit(main())
