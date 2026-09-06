GraphicOverhaulVI — P0005 ColorCoreVI DX12 Output Probe v0.1.2

THIS IS AN EXPERIMENTAL / DIAGNOSTIC PATCH.
It does NOT replace a complete GraphicOverhaulVI release.
It does NOT modify the rendered image.

Purpose
-------
P0004 proved that GTA V Enhanced already contains contextual exposure,
filmic tonemapping and a dedicated HDR branch. P0005 observes the final DX12
presentation path so ColorCoreVI can identify the exact insertion point for the
future FH6-style 3D SDR/HDR display mapper.

Why v0.1.2 exists
-----------------
The user's real GTA runs proved that v0.1.0 and v0.1.1 loaded successfully and
installed hooks, but neither observed runtime Present/Present1 traffic.
That strongly suggests the WARP-created dummy swapchain used by those builds
exposed different DXGI implementation addresses than GTA's real hardware
swapchain, or that the real DXGI factory already existed when the ASI loaded.

v0.1.2 therefore uses three complementary passive strategies:
1. create the probe swapchain on the highest-performance real hardware adapter
   when available, falling back to WARP only when hardware D3D12 is unavailable;
2. hook IDXGIFactory/IDXGIFactory2 CreateSwapChain* methods so a newly-created
   Rockstar swapchain is captured directly and its own vtable methods are hooked;
3. hook CreateDXGIFactory/CreateDXGIFactory1/CreateDXGIFactory2 exports so new
   factory implementations can also be discovered dynamically.

What the ASI observes
---------------------
- IDXGISwapChain::Present
- IDXGISwapChain1::Present1
- IDXGISwapChain::ResizeBuffers
- IDXGISwapChain3::ResizeBuffers1
- IDXGISwapChain3::SetColorSpace1
- IDXGISwapChain4::SetHDRMetaData
- IDXGIFactory::CreateSwapChain
- IDXGIFactory2::CreateSwapChainForHwnd
- IDXGIFactory2::CreateSwapChainForCoreWindow
- IDXGIFactory2::CreateSwapChainForComposition
- swapchain dimensions, format, buffer count and flags
- active D3D12 backbuffer format
- DXGIOutput6 display color space / bits per color / luminance capabilities
- raw HDR10 metadata when supplied

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
1. Delete/replace any older ColorCoreVIOutputProbe.asi.
2. Copy ColorCoreVIOutputProbe.asi into the GTA V Enhanced root folder beside
   GTA5_Enhanced.exe and the existing ASI loader/plugins.
3. Delete the previous ColorCoreVI_OutputProbe.log.
4. Launch GTA V Enhanced normally and load Story Mode.
5. Stay in-game for about 20-30 seconds and open/close the pause menu once.
6. Quit normally.
7. Send back ColorCoreVI_OutputProbe.log.

HDR note
--------
Use the normal current Windows/GTA display settings for the first v0.1.2 run.
Do not enable/disable HDR merely for the probe unless requested. Once a real
swapchain is captured, a controlled SDR/HDR comparison can be done if needed.

Removal
-------
Delete ColorCoreVIOutputProbe.asi. The probe makes no persistent game-file edit.
The generated log can also be deleted.

Third party
-----------
The probe statically links MinHook 1.3.4, pinned to commit:
c3fcafdc10146beb5919319d0683e44e3c30d537
MinHook is BSD-2-Clause licensed. Its license is included in the ZIP.

Validation
----------
The v0.1.2 CI harness intentionally creates a DXGI factory/device BEFORE loading
the ASI, then creates the swapchain after HOOKS_READY. This validates that the
factory-method interception can capture a swapchain even when the factory
pre-existed the probe. The harness then verifies real-object hooks for
SetColorSpace1, SetHDRMetaData, Present1, Present and ResizeBuffers. It also
creates a second DXGI factory after ASI load to exercise the export-hook path.

CI cannot reproduce the user's RTX hardware implementation, so only the real
GTA run can validate whether the hardware-target/factory-capture strategy finds
Rockstar's actual presentation object.
