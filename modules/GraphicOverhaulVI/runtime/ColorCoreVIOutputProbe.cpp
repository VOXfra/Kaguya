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
#include <mutex>
#include <set>
#include <sstream>
#include <string>
#include <unordered_map>

using Microsoft::WRL::ComPtr;

namespace
{
    constexpr const wchar_t* kWindowClass = L"ColorCoreVIOutputProbeDummyWindow";
    constexpr const char* kLogPath = "ColorCoreVI_OutputProbe.log";
    constexpr const char* kVersion = "0.1.2";

    std::mutex g_logMutex;
    std::mutex g_hookMutex;
    std::mutex g_seenMutex;
    std::unordered_map<void*, void*> g_trampolines;
    std::set<void*> g_seenPresent;
    std::set<void*> g_seenPresent1;
    std::atomic<uint64_t> g_presentCount{0};
    std::atomic<uint64_t> g_present1Count{0};
    std::atomic<bool> g_hooksActive{false};

    using PresentFn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT);
    using Present1Fn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain1*, UINT, UINT, const DXGI_PRESENT_PARAMETERS*);
    using ResizeBuffersFn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT, UINT, DXGI_FORMAT, UINT);
    using ResizeBuffers1Fn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain3*, UINT, UINT, UINT, DXGI_FORMAT, UINT, const UINT*, IUnknown* const*);
    using SetColorSpace1Fn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain3*, DXGI_COLOR_SPACE_TYPE);
    using SetHDRMetaDataFn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain4*, DXGI_HDR_METADATA_TYPE, UINT, void*);

    using FactoryCreateSwapChainFn = HRESULT(STDMETHODCALLTYPE*)(IDXGIFactory*, IUnknown*, DXGI_SWAP_CHAIN_DESC*, IDXGISwapChain**);
    using FactoryCreateSwapChainForHwndFn = HRESULT(STDMETHODCALLTYPE*)(IDXGIFactory2*, IUnknown*, HWND, const DXGI_SWAP_CHAIN_DESC1*, const DXGI_SWAP_CHAIN_FULLSCREEN_DESC*, IDXGIOutput*, IDXGISwapChain1**);
    using FactoryCreateSwapChainForCoreWindowFn = HRESULT(STDMETHODCALLTYPE*)(IDXGIFactory2*, IUnknown*, IUnknown*, const DXGI_SWAP_CHAIN_DESC1*, IDXGIOutput*, IDXGISwapChain1**);
    using FactoryCreateSwapChainForCompositionFn = HRESULT(STDMETHODCALLTYPE*)(IDXGIFactory2*, IUnknown*, const DXGI_SWAP_CHAIN_DESC1*, IDXGIOutput*, IDXGISwapChain1**);

    using CreateDXGIFactoryFn = HRESULT(WINAPI*)(REFIID, void**);
    using CreateDXGIFactory2Fn = HRESULT(WINAPI*)(UINT, REFIID, void**);

    CreateDXGIFactoryFn g_createDXGIFactory = nullptr;
    CreateDXGIFactoryFn g_createDXGIFactory1 = nullptr;
    CreateDXGIFactory2Fn g_createDXGIFactory2 = nullptr;

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

    std::string PtrText(const void* p)
    {
        std::ostringstream ss;
        ss << "0x" << std::hex << std::uppercase << reinterpret_cast<uintptr_t>(p);
        return ss.str();
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
        case DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020: return "RGB_FULL_G2084_NONE_P2020";
        case DXGI_COLOR_SPACE_RGB_STUDIO_G2084_NONE_P2020: return "RGB_STUDIO_G2084_NONE_P2020";
        case DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P2020: return "RGB_FULL_G22_NONE_P2020";
        case DXGI_COLOR_SPACE_RGB_STUDIO_G24_NONE_P709: return "RGB_STUDIO_G24_NONE_P709";
        case DXGI_COLOR_SPACE_RGB_STUDIO_G24_NONE_P2020: return "RGB_STUDIO_G24_NONE_P2020";
        case DXGI_COLOR_SPACE_YCBCR_STUDIO_G2084_LEFT_P2020: return "YCBCR_STUDIO_G2084_LEFT_P2020";
        case DXGI_COLOR_SPACE_YCBCR_STUDIO_G2084_TOPLEFT_P2020: return "YCBCR_STUDIO_G2084_TOPLEFT_P2020";
        default: return "COLOR_SPACE_OTHER";
        }
    }

    template <typename T>
    T OriginalFor(void* self, size_t index)
    {
        if (!self) return nullptr;
        void** vtable = *reinterpret_cast<void***>(self);
        if (!vtable) return nullptr;
        void* target = vtable[index];
        std::lock_guard<std::mutex> lock(g_hookMutex);
        auto it = g_trampolines.find(target);
        if (it == g_trampolines.end()) return nullptr;
        return reinterpret_cast<T>(it->second);
    }

    bool InstallTargetHook(void* target, void* detour, const char* name)
    {
        if (!target || !detour) return false;
        std::lock_guard<std::mutex> lock(g_hookMutex);
        if (g_trampolines.find(target) != g_trampolines.end()) return true;

        void* original = nullptr;
        const MH_STATUS createStatus = MH_CreateHook(target, detour, &original);
        if (createStatus != MH_OK)
        {
            std::ostringstream ss;
            ss << "HOOK_FAIL | " << name << " target=" << PtrText(target)
               << " createStatus=" << static_cast<int>(createStatus);
            Log(ss.str());
            return false;
        }
        g_trampolines.emplace(target, original);

        if (g_hooksActive.load())
        {
            const MH_STATUS enableStatus = MH_EnableHook(target);
            if (enableStatus != MH_OK && enableStatus != MH_ERROR_ENABLED)
            {
                std::ostringstream ss;
                ss << "HOOK_FAIL | " << name << " target=" << PtrText(target)
                   << " enableStatus=" << static_cast<int>(enableStatus);
                Log(ss.str());
                return false;
            }
        }
        return true;
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
        ss << "SWAPCHAIN | reason=" << reason << " this=" << PtrText(swapchain);
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
            first = g_seenPresent.insert(swapchain).second;
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

        PresentFn original = OriginalFor<PresentFn>(swapchain, 8);
        if (!original)
        {
            Log("CALL_FAIL | Present trampoline missing");
            return DXGI_ERROR_INVALID_CALL;
        }
        return original(swapchain, syncInterval, flags);
    }

    HRESULT STDMETHODCALLTYPE HookPresent1(IDXGISwapChain1* swapchain, UINT syncInterval, UINT flags, const DXGI_PRESENT_PARAMETERS* parameters)
    {
        const uint64_t count = ++g_present1Count;
        bool first = false;
        {
            std::lock_guard<std::mutex> lock(g_seenMutex);
            first = g_seenPresent1.insert(swapchain).second;
        }
        if (first)
        {
            std::ostringstream ss;
            ss << "Present1 first-seen | this=" << PtrText(swapchain)
               << " syncInterval=" << syncInterval
               << " flags=0x" << std::hex << std::uppercase << flags;
            Log(ss.str());
            DescribeSwapchain(swapchain, "first-present1");
        }
        else if ((count % 600u) == 0u)
        {
            std::ostringstream ss;
            ss << "Present1 heartbeat | count=" << count << " this=" << PtrText(swapchain);
            Log(ss.str());
        }

        Present1Fn original = OriginalFor<Present1Fn>(swapchain, 22);
        if (!original)
        {
            Log("CALL_FAIL | Present1 trampoline missing");
            return DXGI_ERROR_INVALID_CALL;
        }
        return original(swapchain, syncInterval, flags, parameters);
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

        ResizeBuffersFn original = OriginalFor<ResizeBuffersFn>(swapchain, 13);
        if (!original)
        {
            Log("CALL_FAIL | ResizeBuffers trampoline missing");
            return DXGI_ERROR_INVALID_CALL;
        }
        const HRESULT hr = original(swapchain, bufferCount, width, height, newFormat, swapchainFlags);
        std::ostringstream after;
        after << "ResizeBuffers end | hr=0x" << std::hex << std::uppercase << static_cast<uint32_t>(hr);
        Log(after.str());
        if (SUCCEEDED(hr)) DescribeSwapchain(swapchain, "post-resize");
        return hr;
    }

    HRESULT STDMETHODCALLTYPE HookResizeBuffers1(IDXGISwapChain3* swapchain, UINT bufferCount, UINT width, UINT height,
        DXGI_FORMAT newFormat, UINT swapchainFlags, const UINT* creationNodeMask, IUnknown* const* presentQueue)
    {
        std::ostringstream before;
        before << "ResizeBuffers1 begin | this=" << PtrText(swapchain)
               << " requested=" << width << "x" << height
               << " format=" << FormatName(newFormat) << "(" << static_cast<int>(newFormat) << ")"
               << " bufferCount=" << bufferCount
               << " flags=0x" << std::hex << std::uppercase << swapchainFlags;
        Log(before.str());

        ResizeBuffers1Fn original = OriginalFor<ResizeBuffers1Fn>(swapchain, 39);
        if (!original)
        {
            Log("CALL_FAIL | ResizeBuffers1 trampoline missing");
            return DXGI_ERROR_INVALID_CALL;
        }
        const HRESULT hr = original(swapchain, bufferCount, width, height, newFormat, swapchainFlags, creationNodeMask, presentQueue);
        std::ostringstream after;
        after << "ResizeBuffers1 end | hr=0x" << std::hex << std::uppercase << static_cast<uint32_t>(hr);
        Log(after.str());
        if (SUCCEEDED(hr)) DescribeSwapchain(swapchain, "post-resize1");
        return hr;
    }

    HRESULT STDMETHODCALLTYPE HookSetColorSpace1(IDXGISwapChain3* swapchain, DXGI_COLOR_SPACE_TYPE colorSpace)
    {
        std::ostringstream before;
        before << "SetColorSpace1 | this=" << PtrText(swapchain)
               << " colorSpace=" << ColorSpaceName(colorSpace) << "(" << static_cast<int>(colorSpace) << ")";
        Log(before.str());

        SetColorSpace1Fn original = OriginalFor<SetColorSpace1Fn>(swapchain, 38);
        if (!original)
        {
            Log("CALL_FAIL | SetColorSpace1 trampoline missing");
            return DXGI_ERROR_INVALID_CALL;
        }
        const HRESULT hr = original(swapchain, colorSpace);
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

        SetHDRMetaDataFn original = OriginalFor<SetHDRMetaDataFn>(swapchain, 40);
        if (!original)
        {
            Log("CALL_FAIL | SetHDRMetaData trampoline missing");
            return DXGI_ERROR_INVALID_CALL;
        }
        const HRESULT hr = original(swapchain, type, size, metadata);
        std::ostringstream after;
        after << "SetHDRMetaData result | hr=0x" << std::hex << std::uppercase << static_cast<uint32_t>(hr);
        Log(after.str());
        return hr;
    }

    bool InstallSwapchainHooks(IDXGISwapChain* swapchain, const char* source)
    {
        if (!swapchain) return false;
        ComPtr<IDXGISwapChain4> sc4;
        if (FAILED(swapchain->QueryInterface(IID_PPV_ARGS(&sc4))) || !sc4)
        {
            Log(std::string("SWAPCHAIN_HOOK_SKIP | source=") + source + " IDXGISwapChain4 unavailable");
            return false;
        }

        void** vtable = *reinterpret_cast<void***>(sc4.Get());
        std::ostringstream targets;
        targets << "SWAPCHAIN_TARGETS | source=" << source
                << " this=" << PtrText(sc4.Get())
                << " Present=" << PtrText(vtable[8])
                << " ResizeBuffers=" << PtrText(vtable[13])
                << " Present1=" << PtrText(vtable[22])
                << " SetColorSpace1=" << PtrText(vtable[38])
                << " ResizeBuffers1=" << PtrText(vtable[39])
                << " SetHDRMetaData=" << PtrText(vtable[40]);
        Log(targets.str());

        bool ok = true;
        ok = InstallTargetHook(vtable[8], reinterpret_cast<void*>(&HookPresent), "Present") && ok;
        ok = InstallTargetHook(vtable[13], reinterpret_cast<void*>(&HookResizeBuffers), "ResizeBuffers") && ok;
        ok = InstallTargetHook(vtable[22], reinterpret_cast<void*>(&HookPresent1), "Present1") && ok;
        ok = InstallTargetHook(vtable[38], reinterpret_cast<void*>(&HookSetColorSpace1), "SetColorSpace1") && ok;
        ok = InstallTargetHook(vtable[39], reinterpret_cast<void*>(&HookResizeBuffers1), "ResizeBuffers1") && ok;
        ok = InstallTargetHook(vtable[40], reinterpret_cast<void*>(&HookSetHDRMetaData), "SetHDRMetaData") && ok;
        return ok;
    }

    void RegisterRealSwapchain(IDXGISwapChain* swapchain, const char* source)
    {
        if (!swapchain) return;
        std::ostringstream ss;
        ss << "REAL_SWAPCHAIN_CREATED | source=" << source << " this=" << PtrText(swapchain);
        Log(ss.str());
        DescribeSwapchain(swapchain, "creation-capture");
        if (InstallSwapchainHooks(swapchain, source))
            Log(std::string("REAL_SWAPCHAIN_HOOKS_READY | source=") + source);
        else
            Log(std::string("REAL_SWAPCHAIN_HOOKS_FAILED | source=") + source);
    }

    HRESULT STDMETHODCALLTYPE HookFactoryCreateSwapChain(IDXGIFactory* factory, IUnknown* device,
        DXGI_SWAP_CHAIN_DESC* desc, IDXGISwapChain** swapchain)
    {
        FactoryCreateSwapChainFn original = OriginalFor<FactoryCreateSwapChainFn>(factory, 10);
        if (!original) return DXGI_ERROR_INVALID_CALL;
        const HRESULT hr = original(factory, device, desc, swapchain);
        if (SUCCEEDED(hr) && swapchain && *swapchain) RegisterRealSwapchain(*swapchain, "CreateSwapChain");
        return hr;
    }

    HRESULT STDMETHODCALLTYPE HookFactoryCreateSwapChainForHwnd(IDXGIFactory2* factory, IUnknown* device, HWND hwnd,
        const DXGI_SWAP_CHAIN_DESC1* desc, const DXGI_SWAP_CHAIN_FULLSCREEN_DESC* fullscreenDesc,
        IDXGIOutput* restrictOutput, IDXGISwapChain1** swapchain)
    {
        FactoryCreateSwapChainForHwndFn original = OriginalFor<FactoryCreateSwapChainForHwndFn>(factory, 15);
        if (!original) return DXGI_ERROR_INVALID_CALL;
        const HRESULT hr = original(factory, device, hwnd, desc, fullscreenDesc, restrictOutput, swapchain);
        if (SUCCEEDED(hr) && swapchain && *swapchain) RegisterRealSwapchain(*swapchain, "CreateSwapChainForHwnd");
        return hr;
    }

    HRESULT STDMETHODCALLTYPE HookFactoryCreateSwapChainForCoreWindow(IDXGIFactory2* factory, IUnknown* device, IUnknown* window,
        const DXGI_SWAP_CHAIN_DESC1* desc, IDXGIOutput* restrictOutput, IDXGISwapChain1** swapchain)
    {
        FactoryCreateSwapChainForCoreWindowFn original = OriginalFor<FactoryCreateSwapChainForCoreWindowFn>(factory, 16);
        if (!original) return DXGI_ERROR_INVALID_CALL;
        const HRESULT hr = original(factory, device, window, desc, restrictOutput, swapchain);
        if (SUCCEEDED(hr) && swapchain && *swapchain) RegisterRealSwapchain(*swapchain, "CreateSwapChainForCoreWindow");
        return hr;
    }

    HRESULT STDMETHODCALLTYPE HookFactoryCreateSwapChainForComposition(IDXGIFactory2* factory, IUnknown* device,
        const DXGI_SWAP_CHAIN_DESC1* desc, IDXGIOutput* restrictOutput, IDXGISwapChain1** swapchain)
    {
        FactoryCreateSwapChainForCompositionFn original = OriginalFor<FactoryCreateSwapChainForCompositionFn>(factory, 24);
        if (!original) return DXGI_ERROR_INVALID_CALL;
        const HRESULT hr = original(factory, device, desc, restrictOutput, swapchain);
        if (SUCCEEDED(hr) && swapchain && *swapchain) RegisterRealSwapchain(*swapchain, "CreateSwapChainForComposition");
        return hr;
    }

    bool InstallFactoryHooks(IDXGIFactory* factory, const char* source)
    {
        if (!factory) return false;
        void** baseVtable = *reinterpret_cast<void***>(factory);
        bool ok = InstallTargetHook(baseVtable[10], reinterpret_cast<void*>(&HookFactoryCreateSwapChain), "Factory::CreateSwapChain");

        ComPtr<IDXGIFactory2> factory2;
        if (SUCCEEDED(factory->QueryInterface(IID_PPV_ARGS(&factory2))) && factory2)
        {
            void** vtable = *reinterpret_cast<void***>(factory2.Get());
            ok = InstallTargetHook(vtable[15], reinterpret_cast<void*>(&HookFactoryCreateSwapChainForHwnd), "Factory2::CreateSwapChainForHwnd") && ok;
            ok = InstallTargetHook(vtable[16], reinterpret_cast<void*>(&HookFactoryCreateSwapChainForCoreWindow), "Factory2::CreateSwapChainForCoreWindow") && ok;
            ok = InstallTargetHook(vtable[24], reinterpret_cast<void*>(&HookFactoryCreateSwapChainForComposition), "Factory2::CreateSwapChainForComposition") && ok;

            std::ostringstream ss;
            ss << "FACTORY_TARGETS | source=" << source
               << " CreateSwapChain=" << PtrText(baseVtable[10])
               << " ForHwnd=" << PtrText(vtable[15])
               << " ForCoreWindow=" << PtrText(vtable[16])
               << " ForComposition=" << PtrText(vtable[24]);
            Log(ss.str());
        }
        if (ok) Log(std::string("FACTORY_HOOKS_READY | source=") + source);
        return ok;
    }

    HRESULT WINAPI HookCreateDXGIFactory(REFIID riid, void** ppFactory)
    {
        const HRESULT hr = g_createDXGIFactory(riid, ppFactory);
        if (SUCCEEDED(hr) && ppFactory && *ppFactory)
        {
            ComPtr<IDXGIFactory> factory;
            reinterpret_cast<IUnknown*>(*ppFactory)->QueryInterface(IID_PPV_ARGS(&factory));
            if (factory) InstallFactoryHooks(factory.Get(), "CreateDXGIFactory export");
        }
        return hr;
    }

    HRESULT WINAPI HookCreateDXGIFactory1(REFIID riid, void** ppFactory)
    {
        const HRESULT hr = g_createDXGIFactory1(riid, ppFactory);
        if (SUCCEEDED(hr) && ppFactory && *ppFactory)
        {
            ComPtr<IDXGIFactory> factory;
            reinterpret_cast<IUnknown*>(*ppFactory)->QueryInterface(IID_PPV_ARGS(&factory));
            if (factory) InstallFactoryHooks(factory.Get(), "CreateDXGIFactory1 export");
        }
        return hr;
    }

    HRESULT WINAPI HookCreateDXGIFactory2(UINT flags, REFIID riid, void** ppFactory)
    {
        const HRESULT hr = g_createDXGIFactory2(flags, riid, ppFactory);
        if (SUCCEEDED(hr) && ppFactory && *ppFactory)
        {
            ComPtr<IDXGIFactory> factory;
            reinterpret_cast<IUnknown*>(*ppFactory)->QueryInterface(IID_PPV_ARGS(&factory));
            if (factory) InstallFactoryHooks(factory.Get(), "CreateDXGIFactory2 export");
        }
        return hr;
    }

    bool InstallFactoryExportHooks()
    {
        HMODULE dxgi = GetModuleHandleW(L"dxgi.dll");
        if (!dxgi) dxgi = LoadLibraryW(L"dxgi.dll");
        if (!dxgi) return false;

        bool ok = true;
        auto install = [&](const char* exportName, void* detour, void** original) -> bool
        {
            void* target = reinterpret_cast<void*>(GetProcAddress(dxgi, exportName));
            if (!target)
            {
                Log(std::string("EXPORT_SKIP | ") + exportName + " unavailable");
                return true;
            }
            const MH_STATUS status = MH_CreateHook(target, detour, original);
            if (status != MH_OK)
            {
                std::ostringstream ss;
                ss << "EXPORT_HOOK_FAIL | " << exportName << " status=" << static_cast<int>(status);
                Log(ss.str());
                return false;
            }
            std::ostringstream ss;
            ss << "EXPORT_HOOK_TARGET | " << exportName << '=' << PtrText(target);
            Log(ss.str());
            return true;
        };

        ok = install("CreateDXGIFactory", reinterpret_cast<void*>(&HookCreateDXGIFactory), reinterpret_cast<void**>(&g_createDXGIFactory)) && ok;
        ok = install("CreateDXGIFactory1", reinterpret_cast<void*>(&HookCreateDXGIFactory1), reinterpret_cast<void**>(&g_createDXGIFactory1)) && ok;
        ok = install("CreateDXGIFactory2", reinterpret_cast<void*>(&HookCreateDXGIFactory2), reinterpret_cast<void**>(&g_createDXGIFactory2)) && ok;
        return ok;
    }

    LRESULT CALLBACK DummyWndProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
    {
        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    bool SelectProbeAdapter(IDXGIFactory6* factory, ComPtr<IDXGIAdapter1>& adapter, bool& usedWarp)
    {
        usedWarp = false;
        if (factory)
        {
            for (UINT i = 0; ; ++i)
            {
                ComPtr<IDXGIAdapter1> candidate;
                if (factory->EnumAdapterByGpuPreference(i, DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE,
                    IID_PPV_ARGS(&candidate)) == DXGI_ERROR_NOT_FOUND)
                    break;
                if (!candidate) continue;

                DXGI_ADAPTER_DESC1 desc{};
                candidate->GetDesc1(&desc);
                if ((desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0) continue;
                if (FAILED(D3D12CreateDevice(candidate.Get(), D3D_FEATURE_LEVEL_11_0, __uuidof(ID3D12Device), nullptr))) continue;

                adapter = candidate;
                std::ostringstream ss;
                ss << "DUMMY_ADAPTER | mode=hardware vendor=0x" << std::hex << std::uppercase << desc.VendorId
                   << " device=0x" << desc.DeviceId << std::dec
                   << " dedicatedVRAM=" << static_cast<unsigned long long>(desc.DedicatedVideoMemory);
                Log(ss.str());
                return true;
            }
        }

        ComPtr<IDXGIAdapter> warpBase;
        if (FAILED(factory->EnumWarpAdapter(IID_PPV_ARGS(&warpBase))) || !warpBase) return false;
        if (FAILED(warpBase.As(&adapter)) || !adapter) return false;
        usedWarp = true;
        Log("DUMMY_ADAPTER | mode=WARP fallback");
        return true;
    }

    bool CreateProbeSwapchain(ComPtr<IDXGIFactory6>& outFactory, ComPtr<IDXGISwapChain4>& outSwapchain, HWND& outHwnd)
    {
        if (FAILED(CreateDXGIFactory2(0, IID_PPV_ARGS(&outFactory))) || !outFactory)
        {
            Log("INIT_FAIL | CreateDXGIFactory2");
            return false;
        }

        ComPtr<IDXGIAdapter1> adapter;
        bool usedWarp = false;
        if (!SelectProbeAdapter(outFactory.Get(), adapter, usedWarp))
        {
            Log("INIT_FAIL | SelectProbeAdapter");
            return false;
        }

        ComPtr<ID3D12Device> device;
        if (FAILED(D3D12CreateDevice(adapter.Get(), D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&device))))
        {
            Log("INIT_FAIL | D3D12CreateDevice");
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
        const HRESULT hr = outFactory->CreateSwapChainForHwnd(queue.Get(), hwnd, &desc, nullptr, nullptr, &swapchain1);
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

    DWORD WINAPI InitializeProbe(LPVOID)
    {
        DeleteFileA(kLogPath);
        Log(std::string("ColorCoreVI Output Probe P0005 v") + kVersion + " | START");
        Log("MODE | passive logging only; no pixel modification");
        Log("STRATEGY | hardware swapchain targets + factory creation capture + DXGI factory exports");

        ComPtr<IDXGIFactory6> dummyFactory;
        ComPtr<IDXGISwapChain4> dummySwapchain;
        HWND hwnd = nullptr;
        if (!CreateProbeSwapchain(dummyFactory, dummySwapchain, hwnd))
        {
            Log("PROBE_DISABLED | probe swapchain creation failed");
            return 0;
        }

        if (MH_Initialize() != MH_OK)
        {
            DestroyWindow(hwnd);
            Log("PROBE_DISABLED | MH_Initialize failed");
            return 0;
        }

        bool ok = true;
        ok = InstallFactoryHooks(dummyFactory.Get(), "probe factory") && ok;
        ok = InstallSwapchainHooks(dummySwapchain.Get(), "probe swapchain") && ok;
        ok = InstallFactoryExportHooks() && ok;

        if (!ok || MH_EnableHook(MH_ALL_HOOKS) != MH_OK)
        {
            MH_Uninitialize();
            DestroyWindow(hwnd);
            Log("PROBE_DISABLED | hook activation failed");
            return 0;
        }
        g_hooksActive.store(true);

        Log("HOOKS_READY | factory creation + Present Present1 ResizeBuffers ResizeBuffers1 SetColorSpace1 SetHDRMetaData");

        dummySwapchain.Reset();
        dummyFactory.Reset();
        DestroyWindow(hwnd);
        UnregisterClassW(kWindowClass, GetModuleHandleW(nullptr));
        return 0;
    }
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(module);
        HANDLE thread = CreateThread(nullptr, 0, InitializeProbe, nullptr, 0, nullptr);
        if (thread) CloseHandle(thread);
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        if (g_hooksActive.load())
        {
            MH_DisableHook(MH_ALL_HOOKS);
            MH_Uninitialize();
        }
    }
    return TRUE;
}
