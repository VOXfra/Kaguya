#!/usr/bin/env python3
# AC1-to-ACE v0.1.5 donor-compatibility launcher
# Standard-library only. Wraps the v0.1.4 converter and forces a mechanically safer donor.

from __future__ import annotations
import configparser, json, math, os, re, struct, subprocess, sys, threading, traceback
from dataclasses import dataclass, asdict
from pathlib import Path, PureWindowsPath
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

VERSION="0.1.5"
XOR_KEY=0x9F9721A97D1135C1
XOR_BYTES=XOR_KEY.to_bytes(8,"little")
TABLE_SIZES=(0x4000000,0x2000000)
ENTRY_SIZE=0x100
ENTRY_STRUCT=struct.Struct("<224siHhQqq")
FLAG_ENCRYPTED=1<<8

@dataclass
class Profile:
    car_id:str=""
    display_name:str=""
    brand:str=""
    year:int|None=None
    category:str="unknown"
    layout:str="UNKNOWN"
    mass:float|None=None
    wheelbase:float|None=None
    gears:int|None=None
    limiter:float|None=None
    brake_torque:float|None=None
    tyre_width_front:float|None=None
    tyre_width_rear:float|None=None
    tyre_radius_front:float|None=None
    tyre_radius_rear:float|None=None

@dataclass
class Candidate:
    car_id:str
    category:str
    layout:str
    mass:float|None
    wheelbase:float|None
    gears:int|None
    limiter:float|None
    brake_torque:float|None
    score:float=0.0
    coverage:float=0.0
    rejected:bool=False
    reasons:list[str]|None=None

def _cat(text:str)->str:
    t=(text or "").lower().replace("-","_").replace(" ","_")
    if any(x in t for x in ("bugatti","mistral","chiron","veyron","revuelto","aventador","huracan","ferrari","mclaren","pagani","koenigsegg","rimac","valkyrie")):
        return "hyper_super"
    if any(x in t for x in ("lmp","lmh","lmdh","prototype","9x8","499p","963","a424","sc63","v_series_r")):
        return "prototype"
    if any(x in t for x in ("gt3","gt2","gte","gt4","cup","race","competizione")):
        return "gt_race"
    if any(x in t for x in ("escort","cosworth","impreza","wrx","evo6","evo_","lancer","delta","integrale","rally","205_t16","celica","gr_yaris","clio","civic","golf_gti")):
        return "rally_compact"
    if any(x in t for x in ("classic","vintage","_197","_198","_196","964","930","countach","f40")):
        return "classic"
    if any(x in t for x in ("911","718","cayman","corvette","supra","m2","m3","m4","amg","mustang","camaro","gtr","gt_r","rs3","rs6","r8","vantage","db12")):
        return "sports"
    return "unknown"

RELATED={
    "hyper_super":{"sports","gt_race"},
    "sports":{"hyper_super","gt_race","rally_compact"},
    "gt_race":{"hyper_super","sports","prototype"},
    "prototype":{"gt_race"},
    "rally_compact":{"sports"},
    "classic":{"sports"},
    "unknown":set(),
}
HARD_CATEGORY={
    ("hyper_super","rally_compact"),("hyper_super","prototype"),("hyper_super","classic"),
    ("prototype","rally_compact"),("prototype","classic"),
    ("rally_compact","prototype"),
}

def _norm_layout(v)->str:
    if v is None: return "UNKNOWN"
    if isinstance(v,str):
        u=v.upper()
        if "AWD" in u or "4WD" in u: return "AWD"
        if "FWD" in u or "FRONT" in u: return "FWD"
        if "RWD" in u or "REAR" in u: return "RWD"
    try:
        return {0:"RWD",1:"FWD",2:"AWD"}.get(int(v),"UNKNOWN")
    except Exception:
        return "UNKNOWN"

def _rel(a,b):
    if a is None or b is None: return None
    a=float(a); b=float(b)
    d=max(abs(a),abs(b),1e-9)
    return abs(a-b)/d

def _component(rel, full, zero_at):
    if rel is None: return None
    return max(0.0, full*(1.0-rel/zero_at))

def score_candidate(src:Profile,c:Candidate)->Candidate:
    reasons=[]
    earned=0.0
    available=0.0
    rejected=False

    sc,cc=src.category,c.category
    available+=20
    if sc!="unknown" and cc!="unknown":
        if (sc,cc) in HARD_CATEGORY:
            rejected=True; reasons.append(f"catégorie incompatible: {sc} vs {cc}")
        elif sc==cc:
            earned+=20; reasons.append("catégorie +20")
        elif cc in RELATED.get(sc,set()):
            earned+=12; reasons.append("catégorie voisine +12")
        else:
            earned+=3; reasons.append("catégorie éloignée +3")
    else:
        earned+=8; reasons.append("catégorie partiellement inconnue +8")

    available+=20
    if src.layout!="UNKNOWN" and c.layout!="UNKNOWN":
        if src.layout==c.layout:
            earned+=20; reasons.append(f"transmission {c.layout} +20")
        else:
            earned+=2; reasons.append(f"transmission {src.layout}/{c.layout} +2")
    else:
        earned+=8; reasons.append("transmission inconnue +8")

    r=_rel(src.wheelbase,c.wheelbase)
    if r is not None:
        available+=20
        if r>0.30:
            rejected=True; reasons.append(f"empattement écart {r*100:.1f}% >30%")
        else:
            pts=_component(r,20,0.18) or 0; earned+=pts; reasons.append(f"empattement +{pts:.1f} ({r*100:.1f}%)")

    r=_rel(src.mass,c.mass)
    if r is not None:
        available+=20
        if r>0.60:
            rejected=True; reasons.append(f"masse écart {r*100:.1f}% >60%")
        else:
            pts=_component(r,20,0.40) or 0; earned+=pts; reasons.append(f"masse +{pts:.1f} ({r*100:.1f}%)")

    if src.gears and c.gears:
        available+=10
        d=abs(int(src.gears)-int(c.gears))
        if d>3:
            rejected=True; reasons.append(f"rapports écart {d} >3")
        pts=max(0,10-d*3); earned+=pts; reasons.append(f"rapports +{pts:.1f} ({src.gears}/{c.gears})")

    r=_rel(src.limiter,c.limiter)
    if r is not None:
        available+=7
        pts=_component(r,7,0.35) or 0; earned+=pts; reasons.append(f"régime +{pts:.1f} ({r*100:.1f}%)")

    r=_rel(src.brake_torque,c.brake_torque)
    if r is not None:
        available+=3
        pts=_component(r,3,0.60) or 0; earned+=pts; reasons.append(f"freins +{pts:.1f}")

    coverage=min(1.0,available/100.0)
    raw=(earned/available*100.0) if available else 0.0
    # Unknown fields must not artificially create 100% confidence.
    final=raw*(0.72+0.28*coverage)
    if rejected: final=min(final,24.0)
    c.score=round(final,2); c.coverage=round(coverage*100,1); c.rejected=rejected; c.reasons=reasons
    return c

def choose(candidates,src,minimum=58.0):
    ranked=sorted((score_candidate(src,c) for c in candidates),key=lambda x:(x.rejected,-x.score,x.car_id))
    ok=[x for x in ranked if not x.rejected]
    best=max(ok,key=lambda x:x.score) if ok else None
    allrank=sorted(ranked,key=lambda x:(x.rejected,-x.score,x.car_id))
    if not best or best.score<minimum:
        return None,allrank
    return best,allrank

def _xor(data:bytes)->bytes:
    return bytes(b ^ XOR_BYTES[i&7] for i,b in enumerate(data))

def _entry(raw,data_end,require_content=False):
    if len(raw)!=ENTRY_SIZE: return None
    dec=_xor(raw)
    namebuf,unk,flags,nlen,hsh,size,offset=ENTRY_STRUCT.unpack(dec)
    if hsh==0: return None
    if nlen<=0 or nlen>224 or size<0 or offset<0 or offset+size>data_end: raise ValueError("entrée KSPKG invalide")
    name=namebuf[:nlen].decode("ascii","strict")
    if require_content and not name.lower().startswith("content\\"): raise ValueError("table KSPKG invalide")
    return name,flags,size,offset

def scan_kspkg(path:Path):
    fs=path.stat().st_size
    with path.open("rb") as f:
        table=None
        for ts in TABLE_SIZES:
            if fs<=ts: continue
            f.seek(fs-ts); raw=f.read(ENTRY_SIZE)
            try:
                e=_entry(raw,fs-ts,True)
                if e: table=ts; break
            except Exception: pass
        if table is None: raise RuntimeError("content.kspkg non reconnu (table 64/32 MiB)")
        data_end=fs-table
        f.seek(data_end)
        cardata=[]
        for _ in range(table//ENTRY_SIZE):
            raw=f.read(ENTRY_SIZE)
            e=_entry(raw,data_end)
            if e is None: break
            name,flags,size,offset=e
            low=name.lower()
            parts=low.split("\\")
            if len(parts)>=5 and parts[0]=="content" and parts[1]=="cars" and "\\data\\" in low and low.endswith("cardata.car"):
                cardata.append((name,flags,size,offset,parts[2]))
        out=[]
        for name,flags,size,offset,carid in cardata:
            f.seek(offset); data=f.read(size)
            if flags&FLAG_ENCRYPTED: data=_xor(data)
            p=Profile(car_id=carid,category=_cat(carid))
            p.mass=_sane(_pbnum(data,(1,1)),300,5000)
            p.wheelbase=_sane(_pbnum(data,(2,2)),1.5,4.5)
            p.layout=_norm_layout(_pbnum(data,(3,2),integer=True))
            g=_pbnum(data,(23,1),integer=True)
            if g is None: g=_pbnum(data,(3,9),integer=True)
            p.gears=int(g) if g is not None and 1<=g<=12 else None
            p.limiter=_sane(_pbnum(data,(4,6)),500,25000)
            p.brake_torque=_sane(_pbnum(data,(5,1)),50,100000)
            out.append(Candidate(p.car_id,p.category,p.layout,p.mass,p.wheelbase,p.gears,p.limiter,p.brake_torque))
        return table,out

def _varint(data,off):
    val=0; sh=0
    while off<len(data) and sh<70:
        b=data[off]; off+=1; val|=(b&0x7f)<<sh
        if not b&0x80: return val,off
        sh+=7
    raise ValueError("varint")

def _fields(data):
    d={}; off=0
    while off<len(data):
        key,off=_varint(data,off); fn=key>>3; wt=key&7
        if fn<=0: raise ValueError("protobuf")
        if wt==0:
            v,off=_varint(data,off); val=("v",v)
        elif wt==1:
            if off+8>len(data): raise ValueError("protobuf")
            val=("d",data[off:off+8]); off+=8
        elif wt==2:
            n,off=_varint(data,off); end=off+n
            if end>len(data): raise ValueError("protobuf")
            val=("b",data[off:end]); off=end
        elif wt==5:
            if off+4>len(data): raise ValueError("protobuf")
            val=("f",data[off:off+4]); off+=4
        else: raise ValueError("wire")
        d.setdefault(fn,[]).append(val)
    return d

def _pbnum(data,path,integer=False):
    cur=[("b",data)]
    try:
        for depth,fn in enumerate(path):
            nxt=[]
            for typ,val in cur:
                if typ!="b": continue
                try: fld=_fields(val)
                except Exception: continue
                for item in fld.get(fn,[]): nxt.append(item)
            if depth==len(path)-1:
                for typ,val in nxt:
                    if typ=="v": return int(val) if integer else float(val)
                    if typ=="f":
                        x=struct.unpack("<f",val)[0]; return int(round(x)) if integer else float(x)
                    if typ=="d":
                        x=struct.unpack("<d",val)[0]; return int(round(x)) if integer else float(x)
                return None
            cur=nxt
    except Exception: return None
    return None

def _sane(v,lo,hi):
    try:
        x=float(v)
        return x if math.isfinite(x) and lo<=x<=hi else None
    except Exception:return None

def _json_candidates(text):
    dec=json.JSONDecoder()
    for i,ch in enumerate(text):
        if ch!="{": continue
        try:
            obj,end=dec.raw_decode(text[i:])
            if isinstance(obj,dict): yield obj
        except Exception: pass

def _recursive_find(obj,names):
    names={x.lower() for x in names}
    if isinstance(obj,dict):
        for k,v in obj.items():
            if str(k).lower() in names and not isinstance(v,(dict,list)):
                return v
        for v in obj.values():
            r=_recursive_find(v,names)
            if r is not None:return r
    elif isinstance(obj,list):
        for v in obj:
            r=_recursive_find(v,names)
            if r is not None:return r
    return None

def analyze_source(base:Path,source:str,log=print)->Profile:
    stem=Path(source.rstrip("\\/")).stem
    p=Profile(car_id=stem,display_name=stem,category=_cat(stem))
    cmd=[sys.executable,str(base/"ac1toace.py"),"analyze",source]
    try:
        cp=subprocess.run(cmd,capture_output=True,text=True,encoding="utf-8",errors="replace",timeout=180)
        text=(cp.stdout or "")+"\n"+(cp.stderr or "")
        objs=list(_json_candidates(text))
        obj=max(objs,key=lambda o:len(json.dumps(o,ensure_ascii=False))) if objs else None
        if obj:
            def pick(*n):return _recursive_find(obj,n)
            p.mass=_sane(pick("mass","total_mass","totalmass"),300,5000)
            p.wheelbase=_sane(pick("wheelbase","wheel_base"),1.5,4.5)
            p.layout=_norm_layout(pick("traction","layout","drivetrain","drive_layout"))
            g=pick("gear_count","gears_count","gearcount")
            try:p.gears=int(g) if g is not None else None
            except:p.gears=None
            p.limiter=_sane(pick("limiter","rpm_limiter","max_rpm"),500,25000)
            p.brake_torque=_sane(pick("brake_torque","max_brake_torque"),50,100000)
            p.display_name=str(pick("display_name","name","car_name") or p.display_name)
            p.brand=str(pick("brand","manufacturer") or "")
            y=pick("year")
            try:p.year=int(y) if y is not None else None
            except:p.year=None
            p.category=_cat(" ".join((p.car_id,p.display_name,p.brand))) or p.category
            log("Profil AC1 lu via l'analyseur v0.1.4.")
        else:
            log("Analyseur v0.1.4 sans JSON exploitable : score basé sur l'identité + les champs disponibles.")
    except Exception as e:
        log(f"Analyse source partielle ({e}).")
    return p

def detect_ace():
    guesses=[]
    for drive in "CDEFGHIJK":
        guesses += [
            Path(f"{drive}:/SteamLibrary/steamapps/common/Assetto Corsa EVO"),
            Path(f"{drive}:/Program Files (x86)/Steam/steamapps/common/Assetto Corsa EVO"),
        ]
    try:
        import winreg
        for root in (winreg.HKEY_CURRENT_USER,winreg.HKEY_LOCAL_MACHINE):
            for keyname in (r"SOFTWARE\Valve\Steam",r"SOFTWARE\WOW6432Node\Valve\Steam"):
                try:
                    with winreg.OpenKey(root,keyname) as k:
                        sp=winreg.QueryValueEx(k,"SteamPath")[0]
                        guesses.append(Path(sp)/"steamapps/common/Assetto Corsa EVO")
                except OSError:pass
    except Exception:pass
    for p in guesses:
        if (p/"content.kspkg").is_file():return str(p)
    return ""

def ranking_report(src,best,ranked,table):
    return {
        "tool":"AC1-to-ACE","version":VERSION,"selector":"compatibility-v2",
        "source_profile":asdict(src),"kspkg_table_mib":table//(1024*1024),
        "selected":best.car_id if best else None,
        "minimum_score":58.0,
        "top10":[asdict(x) for x in ranked[:10]],
    }

class App(tk.Tk):
    def __init__(self):
        super().__init__(); self.title(f"AC1 → ACE Converter {VERSION}"); self.geometry("860x650")
        self.base=Path(__file__).resolve().parent
        if not (self.base/"ac1toace.py").is_file():
            messagebox.showerror("v0.1.5","Ce launcher doit être installé dans le dossier complet créé par INSTALL-v0.1.5.bat.")
        self.source=tk.StringVar(); self.ace=tk.StringVar(value=detect_ace()); self.out=tk.StringVar(value=str(Path.home()/"Saved Games/ACE/mods")); self.forced=tk.StringVar()
        frm=ttk.Frame(self,padding=12); frm.pack(fill="both",expand=True)
        self._row(frm,0,"Mod AC1 (dossier/ZIP/7Z/RAR)",self.source,self.pick_source)
        self._row(frm,1,"Dossier Assetto Corsa EVO",self.ace,self.pick_ace)
        self._row(frm,2,"Sortie",self.out,self.pick_out)
        ttk.Label(frm,text="Donneur forcé (laisser vide = sélection sûre v0.1.5)").grid(row=3,column=0,sticky="w",pady=5)
        ttk.Entry(frm,textvariable=self.forced).grid(row=3,column=1,sticky="ew",pady=5)
        ttk.Button(frm,text="CONVERTIR",command=self.start).grid(row=4,column=0,columnspan=3,sticky="ew",pady=(12,8))
        self.logbox=tk.Text(frm,height=26,wrap="word"); self.logbox.grid(row=5,column=0,columnspan=3,sticky="nsew")
        frm.columnconfigure(1,weight=1); frm.rowconfigure(5,weight=1)
    def _row(self,f,r,label,var,cmd):
        ttk.Label(f,text=label).grid(row=r,column=0,sticky="w",pady=5); ttk.Entry(f,textvariable=var).grid(row=r,column=1,sticky="ew",pady=5); ttk.Button(f,text="…",width=4,command=cmd).grid(row=r,column=2,padx=(6,0))
    def pick_source(self):
        p=filedialog.askopenfilename(filetypes=[("Mods/archives","*.zip *.7z *.rar *.tar *.gz *.bz2 *.xz"),("Tous","*.*")])
        if not p:p=filedialog.askdirectory()
        if p:self.source.set(p)
    def pick_ace(self):
        p=filedialog.askdirectory()
        if p:self.ace.set(p)
    def pick_out(self):
        p=filedialog.askdirectory()
        if p:self.out.set(p)
    def log(self,s):
        self.logbox.insert("end",str(s)+"\n"); self.logbox.see("end"); self.update_idletasks()
    def start(self):
        threading.Thread(target=self.worker,daemon=True).start()
    def worker(self):
        try:
            src=self.source.get().strip(); ace=self.ace.get().strip(); out=self.out.get().strip()
            if not src or not Path(src).exists(): raise RuntimeError("Source AC1 introuvable.")
            if not ace or not (Path(ace)/"content.kspkg").is_file(): raise RuntimeError("content.kspkg AC EVO introuvable.")
            forced=self.forced.get().strip()
            donor=forced; report=None
            if not donor:
                self.log("Analyse du profil AC1…")
                sp=analyze_source(self.base,src,self.log)
                self.log(f"Profil: catégorie={sp.category}, transmission={sp.layout}, masse={sp.mass}, empattement={sp.wheelbase}, rapports={sp.gears}")
                self.log("Analyse des donneurs EVO…")
                table,cands=scan_kspkg(Path(ace)/"content.kspkg")
                if not cands:raise RuntimeError("Aucun donneur cardata détecté.")
                best,ranked=choose(cands,sp)
                report=ranking_report(sp,best,ranked,table)
                report_path=Path(out)/f"{sp.car_id}.donor-ranking.v015.json"
                report_path.parent.mkdir(parents=True,exist_ok=True); report_path.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
                self.log(f"{len(cands)} donneurs analysés. Top 10:")
                for i,c in enumerate(ranked[:10],1):
                    state="REJETÉ" if c.rejected else "OK"
                    self.log(f"  {i:02d}. {c.car_id}: {c.score:.1f}% [{state}] {c.category}/{c.layout} masse={c.mass} wb={c.wheelbase} gears={c.gears}")
                if best is None:
                    raise RuntimeError(f"AUCUN DONNEUR SÛR (seuil 58%). Classement: {report_path}")
                donor=best.car_id
                self.log(f"Donneur v0.1.5 sélectionné: {donor} — {best.score:.1f}% (couverture {best.coverage:.0f}%)")
            else:self.log(f"Donneur forcé: {donor}")
            cmd=[sys.executable,str(self.base/"ac1toace.py"),"convert",src,"--ace",ace,"--donor",donor]
            if out:cmd += ["-o",out]
            self.log("Lancement du pipeline v0.1.4 avec donneur verrouillé…")
            p=subprocess.Popen(cmd,cwd=self.base,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding="utf-8",errors="replace")
            for line in p.stdout:self.log(line.rstrip())
            rc=p.wait()
            if rc:raise RuntimeError(f"Convertisseur terminé avec code {rc}.")
            self.log("TERMINÉ — donneur contrôlé par v0.1.5.")
            messagebox.showinfo("AC1→EVO","Conversion terminée. Vérifie l'audit final puis teste dans AC EVO.")
        except Exception as e:
            self.log("ERREUR — "+str(e)); self.log(traceback.format_exc()); messagebox.showerror("AC1→EVO",str(e))

def main():
    if "--selftest" in sys.argv:
        a=Profile(car_id="exmods_bugatti_mistral_tech",category=_cat("bugatti mistral"),layout="AWD",mass=1900,wheelbase=2.71,gears=7,limiter=7100)
        escort=Candidate("ks_ford_escort_rs_cosworth",_cat("ford escort rs cosworth"),"AWD",1275,2.55,5,7000,4000)
        rev=Candidate("ks_lamborghini_revuelto",_cat("lamborghini revuelto"),"AWD",1772,2.779,8,9500,7000)
        best,ranked=choose([escort,rev],a)
        assert escort.rejected, escort
        assert best and best.car_id=="ks_lamborghini_revuelto",(best,ranked)
        print("SELFTEST DONOR v0.1.5 PASS"); return
    App().mainloop()
if __name__=="__main__":main()
