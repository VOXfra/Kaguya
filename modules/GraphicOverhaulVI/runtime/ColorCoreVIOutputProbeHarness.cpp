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
    LRESULT CALLBACK HarnessWndProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
    {
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

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
    HMODULE probe = LoadLibraryW(argv[1]);
    if (!probe)
    {
        std::wcerr << L"LoadLibrary failed: " << GetLastError() << L"\n";
        return 3;
    }

    for (int i = 0; i < 50; ++i)
    {
        std::ifstream in("ColorCoreVI_OutputProbe.log");
        std::string text((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());
        if (Contains(text, "HOOKS_READY")) break;
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }

    ComPtr<IDXGIFactory4> factory;
    if (FAILED(CreateDXGIFactory2(0, IID_PPV_ARGS(&factory)))) return 4;

    ComPtr<IDXGIAdapter> warp;
    if (FAILED(factory->EnumWarpAdapter(IID_PPV_ARGS(&warp)))) return 5;

    ComPtr<ID3D12Device> device;
    if (FAILED(D3D12CreateDevice(warp.Get(), D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&device)))) return 6;

    D3D12_COMMAND_QUEUE_DESC queueDesc{};
    queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    ComPtr<ID3D12CommandQueue> queue;
    if (FAILED(device->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&queue)))) return 7;

    const wchar_t* className = L"ColorCoreVIOutputProbeHarnessWindow";
    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = HarnessWndProc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = className;
    if (!RegisterClassExW(&wc) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) return 8;

    HWND hwnd = CreateWindowExW(0, className, L"ColorCoreVI Harness", WS_OVERLAPPED,
        0, 0, 320, 180, nullptr, nullptr, wc.hInstance, nullptr);
    if (!hwnd) return 9;

    DXGI_SWAP_CHAIN_DESC1 desc{};
    desc.Width = 320;
    desc.Height = 180;
    desc.Format = DXGI_FORMAT_R10G10B10A2_UNORM;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = 2;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;

    ComPtr<IDXGISwapChain1> swapchain1;
    if (FAILED(factory->CreateSwapChainForHwnd(queue.Get(), hwnd, &desc, nullptr, nullptr, &swapchain1))) return 10;

    ComPtr<IDXGISwapChain4> swapchain4;
    if (FAILED(swapchain1.As(&swapchain4))) return 11;

    // Exercise the exact methods P0005 needs to observe. These calls are for
    // CI hook validation only; success of HDR activation is not required on WARP.
    (void)swapchain4->SetColorSpace1(DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709);

    DXGI_HDR_METADATA_HDR10 metadata{};
    metadata.RedPrimary[0] = 34000;
    metadata.RedPrimary[1] = 16000;
    metadata.GreenPrimary[0] = 13250;
    metadata.GreenPrimary[1] = 34500;
    metadata.BluePrimary[0] = 7500;
    metadata.BluePrimary[1] = 3000;
    metadata.WhitePoint[0] = 15635;
    metadata.WhitePoint[1] = 16450;
    metadata.MaxMasteringLuminance = 1000;
    metadata.MinMasteringLuminance = 1;
    metadata.MaxContentLightLevel = 1000;
    metadata.MaxFrameAverageLightLevel = 400;
    (void)swapchain4->SetHDRMetaData(DXGI_HDR_METADATA_TYPE_HDR10, sizeof(metadata), &metadata);

    (void)swapchain4->Present(0, DXGI_PRESENT_TEST);
    (void)swapchain4->ResizeBuffers(2, 640, 360, DXGI_FORMAT_R10G10B10A2_UNORM, 0);
    (void)swapchain4->Present(0, DXGI_PRESENT_TEST);

    std::this_thread::sleep_for(std::chrono::milliseconds(500));

    std::ifstream in("ColorCoreVI_OutputProbe.log");
    std::string log((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());

    const bool ok = Contains(log, "HOOKS_READY") &&
                    Contains(log, "SetColorSpace1") &&
                    Contains(log, "SetHDRMetaData") &&
                    Contains(log, "Present first-seen") &&
                    Contains(log, "ResizeBuffers begin") &&
                    Contains(log, "R10G10B10A2_UNORM");

    if (!ok)
    {
        std::cerr << "Probe hook validation failed. Log follows:\n" << log << "\n";
        return 12;
    }

    std::cout << log;
    // Do not FreeLibrary the probe: process exit is the safest unload path for
    // a DLL with active API detours.
    return 0;
}
