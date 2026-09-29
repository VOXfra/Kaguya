#!/usr/bin/env python3
from pathlib import Path
import os, shutil, subprocess, sys, tempfile, zipfile, time

HERE=Path(__file__).resolve().parent
NAME="AC1-to-ACE-Converter-v0.1.5"

def roots():
    seen=set()
    for r in [HERE,HERE.parent,Path.cwd(),Path.home()/"Downloads"]:
        try:r=r.resolve()
        except:r=Path(r)
        if r not in seen:
            seen.add(r); yield r

def find_base_folder():
    for r in roots():
        if (r/"ac1toace.py").is_file() and (r/"ac1toace").is_dir(): return r
        for p in r.glob("AC1-to-ACE-Converter-v0.1.4*"):
            if p.is_dir():
                hits=list(p.rglob("ac1toace.py"))
                for h in hits:
                    if (h.parent/"ac1toace").is_dir(): return h.parent
    return None

def find_base_zip():
    cand=[]
    for r in roots():
        cand += list(r.glob("AC1-to-ACE-Converter-v0.1.4*.zip"))
    cand=[p for p in cand if p.is_file()]
    return max(cand,key=lambda p:p.stat().st_mtime) if cand else None

def main():
    base=find_base_folder()
    tmp=None
    if base is None:
        z=find_base_zip()
        if z is None:
            raise SystemExit("Impossible de trouver AC1-to-ACE-Converter-v0.1.4 (dossier ou ZIP) dans ce dossier/Downloads.")
        print("Base v0.1.4 trouvée:",z)
        tmp=Path(tempfile.mkdtemp(prefix="ac1toace_v015_"))
        with zipfile.ZipFile(z) as zz: zz.extractall(tmp)
        hits=list(tmp.rglob("ac1toace.py"))
        base=next((h.parent for h in hits if (h.parent/"ac1toace").is_dir()),None)
        if base is None: raise SystemExit("Le ZIP v0.1.4 ne contient pas la structure attendue.")
    else: print("Base v0.1.4 trouvée:",base)

    downloads=Path.home()/"Downloads"
    target=downloads/NAME
    if target.exists():
        backup=downloads/(NAME+"_backup_"+time.strftime("%Y%m%d-%H%M%S"))
        target.rename(backup); print("Ancienne v0.1.5 sauvegardée:",backup)
    shutil.copytree(base,target)

    for fn in ("AC1-to-ACE-v0.1.5.py","AC1-to-ACE-v0.1.5.bat","SELFTEST-v0.1.5.bat","README-v0.1.5.txt"):
        shutil.copy2(HERE/fn,target/fn)

    print("Self-test donneur…")
    cp=subprocess.run([sys.executable,str(target/"AC1-to-ACE-v0.1.5.py"),"--selftest"],text=True)
    if cp.returncode: raise SystemExit("SELFTEST v0.1.5 en échec; dossier non validé.")

    outzip=downloads/(NAME+".zip")
    if outzip.exists():outzip.unlink()
    shutil.make_archive(str(outzip.with_suffix("")),"zip",root_dir=downloads,base_dir=NAME)
    print()
    print("OK — v0.1.5 créée:")
    print(target)
    print("ZIP autonome créé:")
    print(outzip)
    print("Lance ensuite: AC1-to-ACE-v0.1.5.bat")
    input("Appuyez sur Entrée pour fermer…")
if __name__=="__main__":
    main()
