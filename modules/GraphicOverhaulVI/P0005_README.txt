GraphicOverhaulVI — P0005 ColorCoreVI Presentation Chain Probe v0.1.3

THIS IS AN EXPERIMENTAL / DIAGNOSTIC PATCH.
It does NOT replace a complete GraphicOverhaulVI release.
It does NOT modify the rendered image.

Why v0.1.3 exists
-----------------
v0.1.0 and v0.1.1 proved that the ASI loads but public DXGI Present/Present1
implementation addresses obtained from a dummy swapchain do not observe GTA's
real presentation path.

v0.1.2 then added global DXGI factory/export hooks to capture the real swapchain.
On the user's actual GTA V Enhanced installation that version crashed the game
before the first rendered image. The log proved the crash happened only after
those factory/export hooks became active. v0.1.2 is therefore REJECTED.

NVIDIA Streamline documentation warns that DXGI factories/swapchains may be SL
proxy interfaces and that third-party tools should avoid using those proxies as
native DXGI objects. GTA V Enhanced also supports NVIDIA DLSS/Frame Generation,
so P0005 now identifies the real presentation/interposer chain before any more
swapchain interception is attempted.

v0.1.3 safety model
-------------------
- NO MinHook
- NO API hooks
- NO DXGI factory interception
- NO vtable patching
- NO Present interception
- NO shader replacement
- NO pixel writes
- NO GTA game-file changes

What v0.1.3 observes
--------------------
For up to 60 seconds after ASI load it passively records:
- interesting loaded modules (DXGI, D3D12, Streamline, NVIDIA, DLSS, ReShade,
  overlays and related presentation components);
- whether sl.interposer.dll / sl.common.dll / sl.dlss_g.dll are loaded;
- selected Streamline exports if sl.interposer.dll is present;
- system DXGI/D3D12 export addresses;
- the GTA executable's relevant import-address-table entries and the module
  currently owning each resolved target.

This is intended to answer whether GTA calls native DXGI directly or whether its
presentation path is already redirected/proxied before the ASI loader runs.

Installation
------------
1. Remove the v0.1.2 ASI first.
2. Copy only the v0.1.3 `ColorCoreVIOutputProbe.asi` beside `GTA5_Enhanced.exe`.
3. Delete the previous `ColorCoreVI_OutputProbe.log`.
4. Launch GTA V Enhanced normally and load Story Mode.
5. Stay in game for at least 20 seconds if possible. 60 seconds gives the most
   complete sample, but quitting earlier is fine.
6. Quit normally and send back `ColorCoreVI_OutputProbe.log`.

Removal
-------
Delete `ColorCoreVIOutputProbe.asi` and optionally the log. No persistent game
file is edited.

Validation
----------
The release is built as an x64 ASI on Windows CI. The harness deliberately
creates a DXGI factory before loading the ASI, then performs normal D3D12 work.
The build only passes if passive module/IAT/system-export observations are
written and if no active-hook marker exists in the log.
