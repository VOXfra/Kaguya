#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <wrl/client.h>

#include <chrono>
#include <fstream>
#include <iostream>
#include <string>
#include <thread>

using Microsoft::WRL::ComPtr;

namespace
{
    bool Contains(const std::string& text, const std::string& needle)
    {
        return text.find(needle) != std::string::npos;
    }
}

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2)
    {
        std::wcerr << L"Usage: ColorCoreVIOutputProbeHarness.exe <probe.asi>\n";
        return 2;
    }

    DeleteFileA("ColorCoreVI_OutputProbe.log");

    // Create a DXGI factory before the probe to reproduce the ordering seen in GTA.
    ComPtr<IDXGIFactory4> factory;
    if (FAILED(CreateDXGIFactory2(0, IID_PPV_ARGS(&factory)))) return 3;

    HMODULE probe = LoadLibraryW(argv[1]);
    if (!probe)
    {
        std::wcerr << L"LoadLibrary failed: " << GetLastError() << L"\n";
        return 4;
    }

    // Exercise normal DX12/DXGI activity after the passive probe has loaded.
    ComPtr<IDXGIAdapter> warp;
    if (FAILED(factory->EnumWarpAdapter(IID_PPV_ARGS(&warp)))) return 5;

    ComPtr<ID3D12Device> device;
    if (FAILED(D3D12CreateDevice(warp.Get(), D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&device)))) return 6;

    std::this_thread::sleep_for(std::chrono::milliseconds(2500));

    std::ifstream in("ColorCoreVI_OutputProbe.log");
    std::string log((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());

    const bool ok = Contains(log, "ColorCoreVI Output Probe P0005 v0.1.3") &&
                    Contains(log, "NO API hooks") &&
                    Contains(log, "PROBE_READY") &&
                    Contains(log, "MODULE_SCAN") &&
                    Contains(log, "SYSTEM_EXPORT") &&
                    Contains(log, "IAT_SUMMARY");

    if (!ok)
    {
        std::cerr << "Passive probe validation failed. Log follows:\n" << log << "\n";
        return 7;
    }

    std::cout << log;
    // Process exit unload is safest. Probe installs no hooks and modifies no API state.
    return 0;
}
