GraphicOverhaulVI — P0005 ColorCoreVI DX12 Output Probe v0.1.1

THIS IS AN EXPERIMENTAL / DIAGNOSTIC PATCH.
It does NOT replace a complete GraphicOverhaulVI release.
It does NOT modify the rendered image.

Purpose
-------
P0004 proved that GTA V Enhanced already contains contextual exposure,
filmic tonemapping and a dedicated HDR branch. P0005 now observes the final
DX12 presentation path so ColorCoreVI can choose the correct insertion point
for the future FH6-style 3D SDR/HDR display mapper.

Why v0.1.1 exists
-----------------
The real GTA V Enhanced run of v0.1.0 proved the ASI loaded and its hooks were
installed, but GTA produced no calls through the four v0.1.0 targets during the
capture. v0.1.1 therefore adds the modern IDXGISwapChain1::Present1 and
IDXGISwapChain3::ResizeBuffers1 paths while retaining the original hooks.

What the ASI observes
---------------------
- IDXGISwapChain::Present
- IDXGISwapChain1::Present1
- IDXGISwapChain::ResizeBuffers
- IDXGISwapChain3::ResizeBuffers1
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
1. REMOVE the v0.1.0 `ColorCoreVIOutputProbe.asi` if it is still installed.
2. Copy the v0.1.1 `ColorCoreVIOutputProbe.asi` into the GTA V Enhanced root
   folder beside `GTA5_Enhanced.exe` and your other ASI plugins.
3. Delete the old `ColorCoreVI_OutputProbe.log`.
4. Launch GTA V Enhanced normally and load Story Mode.
5. Stay in-game for roughly 20-30 seconds and open/close the pause menu once.
6. Quit the game normally.
7. Send back the new `ColorCoreVI_OutputProbe.log` from the GTA root.

HDR note
--------
Use your normal current display/GTA settings. Do not change HDR just for this
run unless requested. Once the real presentation path is captured, a deliberate
SDR/HDR A/B pass may be requested to identify both branches.

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
v0.1.1 is only published after GitHub Actions builds the x64 ASI and executes an
end-to-end Windows D3D12 WARP harness that explicitly invokes and verifies
Present1 as well as Present, ResizeBuffers, SetColorSpace1 and SetHDRMetaData.
The binary is also checked as an AMD64/x64 PE before packaging.

That CI proves the hook implementation. The user's real GTA V Enhanced run is
the validation gate for which DXGI presentation path Rockstar actually uses.
