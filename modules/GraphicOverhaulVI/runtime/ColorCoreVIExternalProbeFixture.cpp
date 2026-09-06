#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <wrl/client.h>

#include <chrono>
#include <thread>

using Microsoft::WRL::ComPtr;

int wmain()
{
    HMODULE fakeStreamline = LoadLibraryW(L"sl.interposer.dll");

    ComPtr<IDXGIFactory6> factory;
    (void)CreateDXGIFactory2(0, IID_PPV_ARGS(&factory));

    if (factory)
    {
        ComPtr<IDXGIAdapter> warp;
        if (SUCCEEDED(factory->EnumWarpAdapter(IID_PPV_ARGS(&warp))) && warp)
        {
            ComPtr<ID3D12Device> device;
            (void)D3D12CreateDevice(warp.Get(), D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&device));
        }
    }

    std::this_thread::sleep_for(std::chrono::seconds(15));
    if (fakeStreamline) FreeLibrary(fakeStreamline);
    return 0;
}
