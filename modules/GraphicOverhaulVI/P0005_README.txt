GraphicOverhaulVI — P0005 ColorCoreVI DX12 Output Probe v0.1.0

THIS IS AN EXPERIMENTAL / DIAGNOSTIC PATCH.
It does NOT replace a complete GraphicOverhaulVI release.
It does NOT modify the rendered image.

Purpose
-------
P0004 proved that GTA V Enhanced already contains contextual exposure,
filmic tonemapping and a dedicated HDR branch. P0005 now observes the final
DX12 presentation path so ColorCoreVI can choose the correct insertion point
for the future FH6-style 3D SDR/HDR display mapper.

What the ASI observes
---------------------
- IDXGISwapChain::Present
- IDXGISwapChain::ResizeBuffers
- IDXGISwapChain3::SetColorSpace1
- IDXGISwapChain4::SetHDRMetaData
- swapchain dimensions, format, buffer count and flags
- active D3D12 backbuffer format
- DXGIOutput6 display color space / bits per color / luminance capabilities
- raw HDR10 metadata when GTA supplies it

What it DOES NOT do
-------------------
- no shader replacement
- no LUT
- no color grading
- no pixel writes
- no GTA memory scanning
- no gameplay changes

Installation
------------
1. Keep your existing ASI loader / GTA Enhanced mod loader setup.
2. Copy `ColorCoreVIOutputProbe.asi` into the GTA V Enhanced root folder,
   beside `GTA5_Enhanced.exe` and your other ASI plugins.
3. Delete an old `ColorCoreVI_OutputProbe.log` if one exists and you want a
   completely clean capture.
4. Launch GTA V Enhanced normally and load Story Mode.
5. Stay in-game for roughly 20-30 seconds, open/close the pause menu once and,
   if convenient, change resolution/window mode once so ResizeBuffers can also
   be observed. This is optional; the core Present/color-space data matters most.
6. Quit the game normally.
7. Send back `ColorCoreVI_OutputProbe.log` from the GTA root.

HDR note
--------
The first run should use your normal current display/GTA settings. Do not change
anything just for P0005 unless asked. The log will tell us whether GTA presented
an SDR, scRGB-like or PQ/Rec.2020 path. If a second SDR/HDR comparison becomes
necessary, it will be requested after the first log is analysed.

Removal
-------
Delete `ColorCoreVIOutputProbe.asi`. The probe has no persistent game-file edit.
You may also delete `ColorCoreVI_OutputProbe.log`.

Third party
-----------
The probe statically links MinHook 1.3.4, pinned to commit:
`c3fcafdc10146beb5919319d0683e44e3c30d537`.
MinHook is BSD-2-Clause licensed. Its license is included in the ZIP.

Validation
----------
The release ZIP is only published after GitHub Actions builds the x64 ASI and
executes an end-to-end Windows D3D12 WARP harness that verifies interception of
Present, ResizeBuffers, SetColorSpace1 and SetHDRMetaData and checks the emitted
log. That CI proves the hooking mechanism; only the user's real GTA run can prove
GTA Enhanced's actual output format/color-space behavior.
