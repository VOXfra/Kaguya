# Native-rehost method v2: post-decryption capture

## Decision

The project no longer treats **per-title retail key recovery** as the primary route.

That route is not scalable: even if a Dakar-specific content/image key were recovered, it would not provide a generic PS5 -> PC conversion path for unrelated titles.

The primary architecture is now:

```text
Retail title on PS5
        |
        | normal console authentication/decryption
        v
decrypted executable view
        |
        | capture/dump using an already-available console execution path
        v
eboot.bin + bundled PRX as clean ELF
        |
        | postdecrypt_intake.py
        v
validated native-relink input
        |
        | AnyPS5 / PS5NativeCore compatibility layer
        v
native Windows executable
```

## What we can build now

Without inventing a new exploit, host-side work can be completed in advance:

1. validate arbitrary title dumps;
2. identify the correct `eboot.bin`;
3. reject still-protected SELF/opaque binaries;
4. validate ELF64 LE program headers and executable PT_LOAD segments;
5. inventory and hash bundled PRX modules;
6. stage exactly the directory structure expected by a native relinker;
7. keep title assets separate from executable validation;
8. retain the 13.60 -> 14.00 firmware-diff lab for the day verified 14.00 firmware modules become available;
9. use a small decrypted NPXS system application as a lower-complexity proof target before attempting the full PS5 menu stack.

## What remains console-dependent

A PS5 14.00 path still needs **already-established code/payload execution** before a post-decryption capture can run. This repository deliberately does not claim to provide that exploit chain.

Once such execution exists, the capture target should be minimal:

- title `eboot.bin`;
- bundled `sce_module/*.prx`, `sce_modules/*.prx`, or `prx/*.prx`;
- `sce_sys/param.json` / `param.sfo` as useful metadata.

Large game assets are not required for the first executable/relink gate.

## First success condition

The first meaningful project transition is no longer “Dakar retail key recovered”.

It is:

```text
REAL_PS5_TITLE_CLEAN_ELF_CAPTURED
```

For Dakar, that means its real `eboot.bin` validates as a clean executable ELF and its bundled modules are readable. At that point the title can enter the native relink/runtime pipeline.

## Generic-title test

The intake tool intentionally contains no Dakar-specific assumptions beyond optional reporting. A Minecraft, Astro Bot, Dakar, or other legitimately dumped title follows the same executable validation path.

## System-app proof gate

The menu question is separated from the title question.

A copied PS5 menu tree is not expected to run on Windows as a single executable because it depends on multiple system processes, Sony PRX libraries, IPC/services, graphics/compositor behavior, databases and secure services. Instead, `system_app_intake.py` provides a smaller falsifiable gate:

```text
already-decrypted NPXS app
        |
        v
clean eboot ELF + readable bundled PRX
        |
        v
AnyPS5-compatible app0 staging
        |
        v
native relink attempt
```

The success marker is:

```text
SYSTEM_APP_READY_FOR_NATIVE_RELINK
```

This does not move the Dakar roadmap above 86.0%. It is an architectural proof only; Dakar still requires its own real decrypted executable/module input before the project can advance.

