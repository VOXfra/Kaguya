#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <wrl/client.h>
#include <MinHook.h>

#include <atomic>
#include <cstdint>
#include <fstream>
#include <iomanip>
#include <mutex>
#include <set>
#include <sstream>
#include <string>

using Microsoft::WRL::ComPtr;

namespace
{
    HMODULE g_self = nullptr;
    std::mutex g_logMutex;
    std::mutex g_seenMutex;
    std::set<void*> g_seenSwapchains;
    std::atomic<uint64_t> g_presentCount{0};
    constexpr const wchar_t* kWindowClass = L"ColorCoreVIOutputProbeDummyWindow";
    constexpr const char* kLogPath = "ColorCoreVI_OutputProbe.log";

    using PresentFn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT);
    using ResizeBuffersFn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT, UINT, DXGI_FORMAT, UINT);
    using SetColorSpace1Fn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain3*, DXGI_COLOR_SPACE_TYPE);
    using SetHDRMetaDataFn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain4*, DXGI_HDR_METADATA_TYPE, UINT, void*);

    PresentFn g_present = nullptr;
    ResizeBuffersFn g_resizeBuffers = nullptr;
    SetColorSpace1Fn g_setColorSpace1 = nullptr;
    SetHDRMetaDataFn g_setHDRMetaData = nullptr;

    std::string Timestamp()
    {
        SYSTEMTIME st{};
        GetLocalTime(&st);
        char buffer[64]{};
        sprintf_s(buffer, "%04u-%02u-%02u %02u:%02u:%02u.%03u",
            st.wYear, st.wMonth, st.wDay,
            st.wHour, st.wMinute, st.wSecond, st.wMilliseconds);
        return buffer;
    }

    void Log(const std::string& message)
    {
        std::lock_guard<std::mutex> lock(g_logMutex);
        std::ofstream out(kLogPath, std::ios::app);
        if (!out) return;
        out << Timestamp() << " | " << message << "\n";
        out.flush();
    }

    const char* FormatName(DXGI_FORMAT format)
    {
        switch (format)
        {
        case DXGI_FORMAT_UNKNOWN: return "DXGI_FORMAT_UNKNOWN";
        case DXGI_FORMAT_R8G8B8A8_UNORM: return "DXGI_FORMAT_R8G8B8A8_UNORM";
        case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB: return "DXGI_FORMAT_R8G8B8A8_UNORM_SRGB";
        case DXGI_FORMAT_B8G8R8A8_UNORM: return "DXGI_FORMAT_B8G8R8A8_UNORM";
        case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB: return "DXGI_FORMAT_B8G8R8A8_UNORM_SRGB";
        case DXGI_FORMAT_R10G10B10A2_UNORM: return "DXGI_FORMAT_R10G10B10A2_UNORM";
        case DXGI_FORMAT_R16G16B16A16_FLOAT: return "DXGI_FORMAT_R16G16B16A16_FLOAT";
        case DXGI_FORMAT_R11G11B10_FLOAT: return "DXGI_FORMAT_R11G11B10_FLOAT";
        case DXGI_FORMAT_R32G32B32A32_FLOAT: return "DXGI_FORMAT_R32G32B32A32_FLOAT";
        default: return "DXGI_FORMAT_OTHER";
        }
    }

    const char* ColorSpaceName(DXGI_COLOR_SPACE_TYPE colorSpace)
    {
        switch (colorSpace)
        {
        case DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709: return "RGB_FULL_G22_NONE_P709";
        case DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709: return "RGB_FULL_G10_NONE_P709";
        case DXGI_COLOR_SPACE_RGB_STUDIO_G22_NONE_P709: return "RGB_STUDIO_G22_NONE_P709";
        case DXGI_COLOR_SPACE_RGB_STUDIO_G22_NONE_P2020: return "RGB_STUDIO_G22_NONE_P2020";
        case DXGI_COLOR_SPACE_RESERVED: return "RESERVED";
        case DXGI_COLOR_SPACE_YCBCR_FULL_G22_NONE_P709_X601: return "YCBCR_FULL_G22_NONE_P709_X601";
        case DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P601: return "YCBCR_STUDIO_G22_LEFT_P601";
        case DXGI_COLOR_SPACE_YCBCR_FULL_G22_LEFT_P601: return "YCBCR_FULL_G22_LEFT_P601";
        case DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P709: return "YCBCR_STUDIO_G22_LEFT_P709";
        case DXGI_COLOR_SPACE_YCBCR_FULL_G22_LEFT_P709: return "YCBCR_FULL_G22_LEFT_P709";
        case DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P2020: return "YCBCR_STUDIO_G22_LEFT_P2020";
        case DXGI_COLOR_SPACE_YCBCR_FULL_G22_LEFT_P2020: return "YCBCR_FULL_G22_LEFT_P2020";
        case DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020: return "RGB_FULL_G2084_NONE_P2020";
        case DXGI_COLOR_SPACE_YCBCR_STUDIO_G2084_LEFT_P2020: return "YCBCR_STUDIO_G2084_LEFT_P2020";
        case DXGI_COLOR_SPACE_RGB_STUDIO_G2084_NONE_P2020: return "RGB_STUDIO_G2084_NONE_P2020";
        case DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_TOPLEFT_P2020: return "YCBCR_STUDIO_G22_TOPLEFT_P2020";
        case DXGI_COLOR_SPACE_YCBCR_STUDIO_G2084_TOPLEFT_P2020: return "YCBCR_STUDIO_G2084_TOPLEFT_P2020";
        case DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P2020: return "RGB_FULL_G22_NONE_P2020";
        case DXGI_COLOR_SPACE_YCBCR_STUDIO_GHLG_TOPLEFT_P2020: return "YCBCR_STUDIO_GHLG_TOPLEFT_P2020";
        case DXGI_COLOR_SPACE_YCBCR_FULL_GHLG_TOPLEFT_P2020: return "YCBCR_FULL_GHLG_TOPLEFT_P2020";
        case DXGI_COLOR_SPACE_RGB_STUDIO_G24_NONE_P709: return "RGB_STUDIO_G24_NONE_P709";
        case DXGI_COLOR_SPACE_RGB_STUDIO_G24_NONE_P2020: return "RGB_STUDIO_G24_NONE_P2020";
        case DXGI_COLOR_SPACE_YCBCR_STUDIO_G24_LEFT_P709: return "YCBCR_STUDIO_G24_LEFT_P709";
        case DXGI_COLOR_SPACE_YCBCR_STUDIO_G24_LEFT_P2020: return "YCBCR_STUDIO_G24_LEFT_P2020";
        case DXGI_COLOR_SPACE_YCBCR_STUDIO_G24_TOPLEFT_P2020: return "YCBCR_STUDIO_G24_TOPLEFT_P2020";
        default: return "COLOR_SPACE_OTHER";
        }
    }

    std::string PtrText(const void* p)
    {
        std::ostringstream ss;
        ss << "0x" << std::hex << std::uppercase << reinterpret_cast<uintptr_t>(p);
        return ss.str();
    }

    void DescribeOutput(IDXGISwapChain* swapchain)
    {
        ComPtr<IDXGIOutput> output;
        if (FAILED(swapchain->GetContainingOutput(&output)) || !output)
        {
            Log("OUTPUT | GetContainingOutput unavailable");
            return;
        }

        ComPtr<IDXGIOutput6> output6;
        if (FAILED(output.As(&output6)) || !output6)
        {
            Log("OUTPUT | IDXGIOutput6 unavailable");
            return;
        }

        DXGI_OUTPUT_DESC1 desc{};
        if (FAILED(output6->GetDesc1(&desc)))
        {
            Log("OUTPUT | GetDesc1 failed");
            return;
        }

        std::ostringstream ss;
        ss << "OUTPUT | BitsPerColor=" << desc.BitsPerColor
           << " ColorSpace=" << ColorSpaceName(desc.ColorSpace) << "(" << static_cast<int>(desc.ColorSpace) << ")"
           << " MinLuminance=" << desc.MinLuminance
           << " MaxLuminance=" << desc.MaxLuminance
           << " MaxFullFrameLuminance=" << desc.MaxFullFrameLuminance;
        Log(ss.str());
    }

    void DescribeSwapchain(IDXGISwapChain* swapchain, const char* reason)
    {
        if (!swapchain) return;

        DXGI_SWAP_CHAIN_DESC desc{};
        const HRESULT descHr = swapchain->GetDesc(&desc);

        std::ostringstream ss;
        ss << "SWAPCHAIN | reason=" << reason
           << " this=" << PtrText(swapchain);
        if (SUCCEEDED(descHr))
        {
            ss << " size=" << desc.BufferDesc.Width << "x" << desc.BufferDesc.Height
               << " format=" << FormatName(desc.BufferDesc.Format) << "(" << static_cast<int>(desc.BufferDesc.Format) << ")"
               << " bufferCount=" << desc.BufferCount
               << " swapEffect=" << static_cast<int>(desc.SwapEffect)
               << " flags=0x" << std::hex << std::uppercase << desc.Flags << std::dec
               << " windowed=" << (desc.Windowed ? 1 : 0);
        }
        else
        {
            ss << " GetDescHR=0x" << std::hex << std::uppercase << static_cast<uint32_t>(descHr);
        }
        Log(ss.str());

        ComPtr<IDXGISwapChain1> swapchain1;
        if (SUCCEEDED(swapchain->QueryInterface(IID_PPV_ARGS(&swapchain1))) && swapchain1)
        {
            DXGI_SWAP_CHAIN_DESC1 desc1{};
            if (SUCCEEDED(swapchain1->GetDesc1(&desc1)))
            {
                std::ostringstream s1;
                s1 << "SWAPCHAIN1 | width=" << desc1.Width
                   << " height=" << desc1.Height
                   << " format=" << FormatName(desc1.Format) << "(" << static_cast<int>(desc1.Format) << ")"
                   << " stereo=" << (desc1.Stereo ? 1 : 0)
                   << " sampleCount=" << desc1.SampleDesc.Count
                   << " usage=0x" << std::hex << std::uppercase << desc1.BufferUsage
                   << " flags=0x" << desc1.Flags << std::dec;
                Log(s1.str());
            }
        }

        ComPtr<IDXGISwapChain3> swapchain3;
        if (SUCCEEDED(swapchain->QueryInterface(IID_PPV_ARGS(&swapchain3))) && swapchain3)
        {
            const UINT index = swapchain3->GetCurrentBackBufferIndex();
            ComPtr<ID3D12Resource> resource;
            if (SUCCEEDED(swapchain3->GetBuffer(index, IID_PPV_ARGS(&resource))) && resource)
            {
                const D3D12_RESOURCE_DESC rd = resource->GetDesc();
                std::ostringstream rs;
                rs << "BACKBUFFER | index=" << index
                   << " size=" << rd.Width << "x" << rd.Height
                   << " format=" << FormatName(rd.Format) << "(" << static_cast<int>(rd.Format) << ")"
                   << " flags=0x" << std::hex << std::uppercase << static_cast<uint32_t>(rd.Flags) << std::dec;
                Log(rs.str());
            }
        }

        DescribeOutput(swapchain);
    }

    HRESULT STDMETHODCALLTYPE HookPresent(IDXGISwapChain* swapchain, UINT syncInterval, UINT flags)
    {
        const uint64_t count = ++g_presentCount;
        bool first = false;
        {
            std::lock_guard<std::mutex> lock(g_seenMutex);
            first = g_seenSwapchains.insert(swapchain).second;
        }
        if (first)
        {
            std::ostringstream ss;
            ss << "Present first-seen | this=" << PtrText(swapchain)
               << " syncInterval=" << syncInterval
               << " flags=0x" << std::hex << std::uppercase << flags;
            Log(ss.str());
            DescribeSwapchain(swapchain, "first-present");
        }
        else if ((count % 600u) == 0u)
        {
            std::ostringstream ss;
            ss << "Present heartbeat | count=" << count << " this=" << PtrText(swapchain);
            Log(ss.str());
        }
        return g_present(swapchain, syncInterval, flags);
    }

    HRESULT STDMETHODCALLTYPE HookResizeBuffers(IDXGISwapChain* swapchain, UINT bufferCount, UINT width, UINT height, DXGI_FORMAT newFormat, UINT swapchainFlags)
    {
        std::ostringstream before;
        before << "ResizeBuffers begin | this=" << PtrText(swapchain)
               << " requested=" << width << "x" << height
               << " format=" << FormatName(newFormat) << "(" << static_cast<int>(newFormat) << ")"
               << " bufferCount=" << bufferCount
               << " flags=0x" << std::hex << std::uppercase << swapchainFlags;
        Log(before.str());

        const HRESULT hr = g_resizeBuffers(swapchain, bufferCount, width, height, newFormat, swapchainFlags);
        {
            std::lock_guard<std::mutex> lock(g_seenMutex);
            g_seenSwapchains.erase(swapchain);
        }
        std::ostringstream after;
        after << "ResizeBuffers end | hr=0x" << std::hex << std::uppercase << static_cast<uint32_t>(hr);
        Log(after.str());
        if (SUCCEEDED(hr)) DescribeSwapchain(swapchain, "post-resize");
        return hr;
    }

    HRESULT STDMETHODCALLTYPE HookSetColorSpace1(IDXGISwapChain3* swapchain, DXGI_COLOR_SPACE_TYPE colorSpace)
    {
        std::ostringstream before;
        before << "SetColorSpace1 | this=" << PtrText(swapchain)
               << " colorSpace=" << ColorSpaceName(colorSpace) << "(" << static_cast<int>(colorSpace) << ")";
        Log(before.str());
        const HRESULT hr = g_setColorSpace1(swapchain, colorSpace);
        std::ostringstream after;
        after << "SetColorSpace1 result | hr=0x" << std::hex << std::uppercase << static_cast<uint32_t>(hr);
        Log(after.str());
        return hr;
    }

    HRESULT STDMETHODCALLTYPE HookSetHDRMetaData(IDXGISwapChain4* swapchain, DXGI_HDR_METADATA_TYPE type, UINT size, void* metadata)
    {
        std::ostringstream ss;
        ss << "SetHDRMetaData | this=" << PtrText(swapchain)
           << " type=" << static_cast<int>(type)
           << " size=" << size;
        Log(ss.str());

        if (type == DXGI_HDR_METADATA_TYPE_HDR10 && metadata && size >= sizeof(DXGI_HDR_METADATA_HDR10))
        {
            const auto* hdr10 = static_cast<const DXGI_HDR_METADATA_HDR10*>(metadata);
            std::ostringstream h;
            h << "HDR10 raw | R=(" << hdr10->RedPrimary[0] << ',' << hdr10->RedPrimary[1] << ')'
              << " G=(" << hdr10->GreenPrimary[0] << ',' << hdr10->GreenPrimary[1] << ')'
              << " B=(" << hdr10->BluePrimary[0] << ',' << hdr10->BluePrimary[1] << ')'
              << " W=(" << hdr10->WhitePoint[0] << ',' << hdr10->WhitePoint[1] << ')'
              << " MaxMasteringLuminance=" << hdr10->MaxMasteringLuminance
              << " MinMasteringLuminance=" << hdr10->MinMasteringLuminance
              << " MaxContentLightLevel=" << hdr10->MaxContentLightLevel
              << " MaxFrameAverageLightLevel=" << hdr10->MaxFrameAverageLightLevel;
            Log(h.str());
        }

        const HRESULT hr = g_setHDRMetaData(swapchain, type, size, metadata);
        std::ostringstream after;
        after << "SetHDRMetaData result | hr=0x" << std::hex << std::uppercase << static_cast<uint32_t>(hr);
        Log(after.str());
        return hr;
    }

    LRESULT CALLBACK DummyWndProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
    {
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    bool CreateDummySwapchain(ComPtr<IDXGISwapChain4>& outSwapchain, HWND& outHwnd)
    {
        ComPtr<IDXGIFactory4> factory;
        if (FAILED(CreateDXGIFactory2(0, IID_PPV_ARGS(&factory))))
        {
            Log("INIT_FAIL | CreateDXGIFactory2");
            return false;
        }

        ComPtr<IDXGIAdapter> warp;
        if (FAILED(factory->EnumWarpAdapter(IID_PPV_ARGS(&warp))))
        {
            Log("INIT_FAIL | EnumWarpAdapter");
            return false;
        }

        ComPtr<ID3D12Device> device;
        if (FAILED(D3D12CreateDevice(warp.Get(), D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&device))))
        {
            Log("INIT_FAIL | D3D12CreateDevice(WARP)");
            return false;
        }

        D3D12_COMMAND_QUEUE_DESC queueDesc{};
        queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
        ComPtr<ID3D12CommandQueue> queue;
        if (FAILED(device->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&queue))))
        {
            Log("INIT_FAIL | CreateCommandQueue");
            return false;
        }

        WNDCLASSEXW wc{};
        wc.cbSize = sizeof(wc);
        wc.lpfnWndProc = DummyWndProc;
        wc.hInstance = GetModuleHandleW(nullptr);
        wc.lpszClassName = kWindowClass;
        RegisterClassExW(&wc);

        HWND hwnd = CreateWindowExW(0, kWindowClass, L"ColorCoreVI Probe Dummy", WS_OVERLAPPED,
            0, 0, 64, 64, nullptr, nullptr, wc.hInstance, nullptr);
        if (!hwnd)
        {
            Log("INIT_FAIL | CreateWindowExW");
            return false;
        }

        DXGI_SWAP_CHAIN_DESC1 desc{};
        desc.Width = 64;
        desc.Height = 64;
        desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        desc.SampleDesc.Count = 1;
        desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        desc.BufferCount = 2;
        desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;

        ComPtr<IDXGISwapChain1> swapchain1;
        const HRESULT hr = factory->CreateSwapChainForHwnd(queue.Get(), hwnd, &desc, nullptr, nullptr, &swapchain1);
        if (FAILED(hr))
        {
            DestroyWindow(hwnd);
            Log("INIT_FAIL | CreateSwapChainForHwnd");
            return false;
        }

        if (FAILED(swapchain1.As(&outSwapchain)) || !outSwapchain)
        {
            DestroyWindow(hwnd);
            Log("INIT_FAIL | Query IDXGISwapChain4");
            return false;
        }

        outHwnd = hwnd;
        return true;
    }

    bool CreateAndEnableHook(void* target, void* detour, void** original, const char* name)
    {
        const MH_STATUS createStatus = MH_CreateHook(target, detour, original);
        if (createStatus != MH_OK)
        {
            std::ostringstream ss;
            ss << "INIT_FAIL | MH_CreateHook " << name << " status=" << static_cast<int>(createStatus);
            Log(ss.str());
            return false;
        }
        return true;
    }

    DWORD WINAPI InitializeProbe(LPVOID)
    {
        DeleteFileA(kLogPath);
        Log("ColorCoreVI Output Probe P0005 v0.1.0 | START");
        Log("MODE | passive logging only; no pixel modification");

        ComPtr<IDXGISwapChain4> dummy;
        HWND hwnd = nullptr;
        if (!CreateDummySwapchain(dummy, hwnd))
        {
            Log("PROBE_DISABLED | dummy DX12 swapchain creation failed");
            return 0;
        }

        void** vtable = *reinterpret_cast<void***>(dummy.Get());
        void* presentTarget = vtable[8];
        void* resizeTarget = vtable[13];
        void* setColorSpaceTarget = vtable[38];
        void* setHDRTarget = vtable[40];

        {
            std::ostringstream ss;
            ss << "TARGETS | Present=" << PtrText(presentTarget)
               << " ResizeBuffers=" << PtrText(resizeTarget)
               << " SetColorSpace1=" << PtrText(setColorSpaceTarget)
               << " SetHDRMetaData=" << PtrText(setHDRTarget);
            Log(ss.str());
        }

        if (MH_Initialize() != MH_OK)
        {
            DestroyWindow(hwnd);
            Log("PROBE_DISABLED | MH_Initialize failed");
            return 0;
        }

        bool ok = true;
        ok = CreateAndEnableHook(presentTarget, reinterpret_cast<void*>(&HookPresent), reinterpret_cast<void**>(&g_present), "Present") && ok;
        ok = CreateAndEnableHook(resizeTarget, reinterpret_cast<void*>(&HookResizeBuffers), reinterpret_cast<void**>(&g_resizeBuffers), "ResizeBuffers") && ok;
        ok = CreateAndEnableHook(setColorSpaceTarget, reinterpret_cast<void*>(&HookSetColorSpace1), reinterpret_cast<void**>(&g_setColorSpace1), "SetColorSpace1") && ok;
        ok = CreateAndEnableHook(setHDRTarget, reinterpret_cast<void*>(&HookSetHDRMetaData), reinterpret_cast<void**>(&g_setHDRMetaData), "SetHDRMetaData") && ok;

        if (!ok || MH_EnableHook(MH_ALL_HOOKS) != MH_OK)
        {
            MH_Uninitialize();
            DestroyWindow(hwnd);
            Log("PROBE_DISABLED | hook activation failed");
            return 0;
        }

        Log("HOOKS_READY | Present ResizeBuffers SetColorSpace1 SetHDRMetaData");

        dummy.Reset();
        DestroyWindow(hwnd);
        UnregisterClassW(kWindowClass, GetModuleHandleW(nullptr));
        return 0;
    }
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_self = module;
        DisableThreadLibraryCalls(module);
        HANDLE thread = CreateThread(nullptr, 0, InitializeProbe, nullptr, 0, nullptr);
        if (thread) CloseHandle(thread);
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        if (g_present || g_resizeBuffers || g_setColorSpace1 || g_setHDRMetaData)
        {
            MH_DisableHook(MH_ALL_HOOKS);
            MH_Uninitialize();
        }
    }
    return TRUE;
}
