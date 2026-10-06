# PS5 14.00 Lab

Isolated research workspace for the Dakar PS5 -> PC native-rehost project.

## Goal

Turn a future **decrypted PS5 14.00 firmware module set** into actionable porting data as fast as possible.

This branch does **not** try to break retail encryption and does not contain an exploit. It prepares the parts we can solve now:

- inventory and hash decrypted firmware modules;
- compare 13.60 and 14.00 ELF executable regions;
- find stable byte anchors between firmware revisions;
- estimate source->target offset deltas;
- translate known 13.60 offsets into ranked 14.00 candidates;
- produce a compact Markdown report;
- prepare the minimal Dakar dump target set (eboot + bundled PRX) for use once payload execution is available.

## Windows-only quick start

No WSL is required.

```powershell
cd tools\ps5-1400-lab
.\run.ps1
```

Expected layout:

```text
tools/ps5-1400-lab/
  input/
    fw1360/
      libSceNKWebKit.sprx
      libkernel_web.sprx
      ...
    fw1400/
      libSceNKWebKit.sprx
      libkernel_web.sprx
      ...
    offsets-13.60.js   # optional
  output/
```

If `input/offsets-13.60.js` is present, known 13.60 constants are translated using the locally observed anchor map.

## What counts as progress

A useful 14.00 input is one of:

1. decrypted `libSceNKWebKit.sprx`;
2. decrypted `libkernel_web.sprx` / relevant kernel-facing modules;
3. a verified 14.00 offset table;
4. payload execution on a 14.00 console, allowing a targeted post-decryption dump.

The Dakar runtime roadmap remains frozen until a **real decrypted title executable/module** is obtained and validated.

## Dakar targeted dump set

Once an already-working payload execution path exists, the useful title data is intentionally small:

```text
app0/eboot.bin
app0/sce_module/*.prx
app0/sce_modules/*.prx
app0/prx/*.prx
app0/sce_sys/param.json
app0/sce_sys/param.sfo
```

There is no reason to copy the entire ~41 GiB title just to start native relinking.
