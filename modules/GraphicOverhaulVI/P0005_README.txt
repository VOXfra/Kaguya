GraphicOverhaulVI — P0005 ColorCoreVI Presentation Chain Probe v0.1.4

THIS IS AN EXPERIMENTAL / DIAGNOSTIC PATCH.
It does NOT replace a complete GraphicOverhaulVI release.
It does NOT modify the rendered image.

Why v0.1.4 exists
-----------------
v0.1.0/v0.1.1 loaded successfully but saw no GTA Present/Present1 traffic.
v0.1.2 added global DXGI factory/export hooks and crashed the user's actual
GTA V Enhanced before the first rendered image. v0.1.2 is REJECTED.

v0.1.3 removed all hooks and switched to passive in-process observation. It was
stable enough to initialize, but the user's log ended at t5 and contained zero
interesting modules / zero relevant IAT entries. The v0.1.3 CI was also found to
be too weak: it required only generic MODULE_SCAN/IAT markers, not proof that
DXGI/D3D12 were actually detected.

P0005 v0.1.4 therefore removes the probe from the GTA process entirely.

v0.1.4 safety model
-------------------
- STANDALONE EXE, not an ASI
- NO DLL/ASI injection
- NO MinHook
- NO API hooks
- NO DXGI/D3D12 interception
- NO vtable patching
- NO WriteProcessMemory
- NO VirtualAllocEx / CreateRemoteThread
- NO shader replacement
- NO pixel writes
- NO GTA game-file changes

The only process access requested is read/query access so the tool can inspect
module lists and PE import/IAT metadata from outside GTA.

What v0.1.4 observes
--------------------
The standalone monitor waits for `GTA5_Enhanced.exe`, then records:
- all relevant loaded presentation modules (DXGI, D3D12, Streamline, NVIDIA,
  DLSS, ReShade, overlays, FSR/XeSS and related components);
- module base addresses, sizes and paths;
- GTA5_Enhanced.exe's import DLLs relevant to DXGI/D3D12/Streamline/DLSS;
- relevant IAT symbols and the loaded module that owns each resolved target;
- whether the process exits before the requested observation window finishes.

Usage
-----
1. DELETE `ColorCoreVIOutputProbe.asi` from the GTA V Enhanced folder. v0.1.4
   does not use any ASI.
2. Extract this P0005 v0.1.4 package to any normal writable folder.
3. Double-click `RUN-COLORCORE-PRESENTATION-PROBE.cmd`.
4. Leave the console open and launch GTA V Enhanced normally through your usual
   Epic/Rockstar path.
5. Load Story Mode if possible and leave the game running. The monitor samples
   for up to 60 seconds.
6. Send back `ColorCoreVI_ExternalProbe.log` from the probe folder.

If GTA exits/crashes early, send the external log anyway. It will explicitly
record PROCESS_EXITED and the last module snapshot seen before exit.

Validation gate
---------------
The Windows x64 CI creates a fixture process named `GTA5_Enhanced.exe` that
really loads DXGI, D3D12 and a fake `sl.interposer.dll`. The external probe must:
- find that already-running process;
- detect dxgi.dll;
- detect d3d12.dll;
- detect sl.interposer.dll;
- parse relevant DXGI/D3D12 import/IAT entries;
- run without any injection or process-write API.

The workflow fails if any required module/evidence marker is missing. This fixes
the weak validation used for v0.1.3.
