#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <tlhelp32.h>

#include <algorithm>
#include <cctype>
#include <cstdint>
#include <fstream>
#include <mutex>
#include <set>
#include <sstream>
#include <string>
#include <vector>

namespace
{
    constexpr const char* kLogPath = "ColorCoreVI_OutputProbe.log";
    constexpr const char* kVersion = "0.1.3";
    std::mutex g_logMutex;
    std::set<std::wstring> g_seenInterestingModules;

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

    std::string Narrow(const std::wstring& value)
    {
        if (value.empty()) return {};
        const int needed = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, nullptr, 0, nullptr, nullptr);
        if (needed <= 1) return {};
        std::string result(static_cast<size_t>(needed - 1), '\0');
        WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, result.data(), needed, nullptr, nullptr);
        return result;
    }

    std::string LowerAscii(std::string value)
    {
        std::transform(value.begin(), value.end(), value.begin(), [](unsigned char c) {
            return static_cast<char>(std::tolower(c));
        });
        return value;
    }

    std::wstring LowerWide(std::wstring value)
    {
        std::transform(value.begin(), value.end(), value.begin(), [](wchar_t c) {
            if (c >= L'A' && c <= L'Z') return static_cast<wchar_t>(c - L'A' + L'a');
            return c;
        });
        return value;
    }

    std::string PtrText(const void* p)
    {
        std::ostringstream ss;
        ss << "0x" << std::hex << std::uppercase << reinterpret_cast<uintptr_t>(p);
        return ss.str();
    }

    std::wstring ModulePathFromAddress(const void* address)
    {
        if (!address) return L"<null>";
        HMODULE module = nullptr;
        if (!GetModuleHandleExW(
                GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                reinterpret_cast<LPCWSTR>(address), &module) || !module)
            return L"<unmapped>";

        wchar_t path[MAX_PATH * 4]{};
        const DWORD count = GetModuleFileNameW(module, path, static_cast<DWORD>(std::size(path)));
        if (count == 0) return L"<unknown>";
        return std::wstring(path, path + count);
    }

    std::wstring FileNameOnly(const std::wstring& path)
    {
        const size_t pos = path.find_last_of(L"\\/");
        return pos == std::wstring::npos ? path : path.substr(pos + 1);
    }

    bool IsInterestingModuleName(const std::wstring& rawName)
    {
        const std::wstring name = LowerWide(rawName);
        const wchar_t* terms[] = {
            L"dxgi", L"d3d12", L"streamline", L"sl.", L"interposer", L"nvngx",
            L"dlss", L"nvidia", L"reshade", L"overlay", L"steam", L"epic",
            L"amd", L"fsr", L"xess", L"intel", L"framegen", L"frame_generation"
        };
        for (const wchar_t* term : terms)
            if (name.find(term) != std::wstring::npos) return true;
        return false;
    }

    void EnumerateInterestingModules(const char* sample)
    {
        HANDLE snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, GetCurrentProcessId());
        if (snapshot == INVALID_HANDLE_VALUE)
        {
            Log(std::string("MODULE_SCAN_FAIL | sample=") + sample + " error=" + std::to_string(GetLastError()));
            return;
        }

        MODULEENTRY32W entry{};
        entry.dwSize = sizeof(entry);
        size_t total = 0;
        size_t interesting = 0;
        if (Module32FirstW(snapshot, &entry))
        {
            do
            {
                ++total;
                const std::wstring name(entry.szModule);
                if (!IsInterestingModuleName(name)) continue;
                ++interesting;

                const std::wstring path(entry.szExePath);
                const std::wstring key = LowerWide(path);
                const bool firstSeen = g_seenInterestingModules.insert(key).second;
                std::ostringstream ss;
                ss << "MODULE | sample=" << sample
                   << " firstSeen=" << (firstSeen ? 1 : 0)
                   << " name=" << Narrow(name)
                   << " base=" << PtrText(entry.modBaseAddr)
                   << " size=" << entry.modBaseSize
                   << " path=" << Narrow(path);
                Log(ss.str());
            } while (Module32NextW(snapshot, &entry));
        }
        CloseHandle(snapshot);

        std::ostringstream summary;
        summary << "MODULE_SCAN | sample=" << sample
                << " total=" << total
                << " interesting=" << interesting;
        Log(summary.str());
    }

    bool IsInterestingImportDll(const std::string& rawDll)
    {
        const std::string dll = LowerAscii(rawDll);
        return dll.find("dxgi") != std::string::npos ||
               dll.find("d3d12") != std::string::npos ||
               dll.find("interposer") != std::string::npos ||
               dll.find("streamline") != std::string::npos ||
               dll.find("nvngx") != std::string::npos;
    }

    bool RvaInImage(uint32_t rva, uint32_t bytes, uint32_t imageSize)
    {
        if (rva == 0 || rva >= imageSize) return false;
        return bytes <= imageSize - rva;
    }

    void InspectMainModuleImports(const char* sample)
    {
        auto* base = reinterpret_cast<uint8_t*>(GetModuleHandleW(nullptr));
        if (!base)
        {
            Log(std::string("IAT_FAIL | sample=") + sample + " reason=no-main-module");
            return;
        }

        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
        if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew <= 0)
        {
            Log(std::string("IAT_FAIL | sample=") + sample + " reason=bad-dos-header");
            return;
        }

        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
        if (nt->Signature != IMAGE_NT_SIGNATURE || nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC)
        {
            Log(std::string("IAT_FAIL | sample=") + sample + " reason=bad-pe-header");
            return;
        }

        const uint32_t imageSize = nt->OptionalHeader.SizeOfImage;
        const auto& dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
        if (!RvaInImage(dir.VirtualAddress, sizeof(IMAGE_IMPORT_DESCRIPTOR), imageSize))
        {
            Log(std::string("IAT | sample=") + sample + " state=no-import-directory");
            return;
        }

        const auto* desc = reinterpret_cast<const IMAGE_IMPORT_DESCRIPTOR*>(base + dir.VirtualAddress);
        size_t dllCount = 0;
        size_t symbolCount = 0;

        for (size_t di = 0; di < 512; ++di, ++desc)
        {
            const uintptr_t descOffset = reinterpret_cast<const uint8_t*>(desc) - base;
            if (descOffset + sizeof(*desc) > imageSize) break;
            if (desc->Name == 0) break;
            if (!RvaInImage(desc->Name, 2, imageSize)) continue;

            const char* dllNamePtr = reinterpret_cast<const char*>(base + desc->Name);
            std::string dllName(dllNamePtr, strnlen_s(dllNamePtr, imageSize - desc->Name));
            if (!IsInterestingImportDll(dllName)) continue;
            ++dllCount;

            const uint32_t lookupRva = desc->OriginalFirstThunk ? desc->OriginalFirstThunk : desc->FirstThunk;
            if (!RvaInImage(lookupRva, sizeof(IMAGE_THUNK_DATA64), imageSize) ||
                !RvaInImage(desc->FirstThunk, sizeof(IMAGE_THUNK_DATA64), imageSize))
                continue;

            const auto* lookup = reinterpret_cast<const IMAGE_THUNK_DATA64*>(base + lookupRva);
            const auto* iat = reinterpret_cast<const IMAGE_THUNK_DATA64*>(base + desc->FirstThunk);

            for (size_t ti = 0; ti < 4096; ++ti, ++lookup, ++iat)
            {
                const uintptr_t lookupOff = reinterpret_cast<const uint8_t*>(lookup) - base;
                const uintptr_t iatOff = reinterpret_cast<const uint8_t*>(iat) - base;
                if (lookupOff + sizeof(*lookup) > imageSize || iatOff + sizeof(*iat) > imageSize) break;
                if (lookup->u1.AddressOfData == 0) break;

                std::string symbol;
                if (IMAGE_SNAP_BY_ORDINAL64(lookup->u1.Ordinal))
                {
                    symbol = std::string("ordinal#") + std::to_string(IMAGE_ORDINAL64(lookup->u1.Ordinal));
                }
                else
                {
                    const uint64_t nameRva64 = lookup->u1.AddressOfData;
                    if (nameRva64 >= imageSize || !RvaInImage(static_cast<uint32_t>(nameRva64), sizeof(IMAGE_IMPORT_BY_NAME), imageSize))
                        continue;
                    const auto* byName = reinterpret_cast<const IMAGE_IMPORT_BY_NAME*>(base + static_cast<uint32_t>(nameRva64));
                    const char* symbolPtr = reinterpret_cast<const char*>(byName->Name);
                    const uintptr_t symbolOff = reinterpret_cast<const uint8_t*>(symbolPtr) - base;
                    if (symbolOff >= imageSize) continue;
                    symbol.assign(symbolPtr, strnlen_s(symbolPtr, imageSize - symbolOff));
                }

                void* target = reinterpret_cast<void*>(static_cast<uintptr_t>(iat->u1.Function));
                const std::wstring ownerPath = ModulePathFromAddress(target);
                std::ostringstream ss;
                ss << "IAT | sample=" << sample
                   << " dll=" << dllName
                   << " symbol=" << symbol
                   << " target=" << PtrText(target)
                   << " owner=" << Narrow(FileNameOnly(ownerPath))
                   << " ownerPath=" << Narrow(ownerPath);
                Log(ss.str());
                ++symbolCount;
            }
        }

        std::ostringstream summary;
        summary << "IAT_SUMMARY | sample=" << sample
                << " relevantDlls=" << dllCount
                << " symbols=" << symbolCount;
        Log(summary.str());
    }

    void InspectStreamline(const char* sample)
    {
        const wchar_t* candidates[] = { L"sl.interposer.dll", L"sl.common.dll", L"sl.dlss_g.dll", L"sl.dlss.dll" };
        for (const wchar_t* name : candidates)
        {
            HMODULE module = GetModuleHandleW(name);
            if (!module) continue;
            wchar_t path[MAX_PATH * 4]{};
            GetModuleFileNameW(module, path, static_cast<DWORD>(std::size(path)));
            std::ostringstream ss;
            ss << "STREAMLINE | sample=" << sample
               << " module=" << Narrow(name)
               << " base=" << PtrText(module)
               << " path=" << Narrow(path);
            Log(ss.str());

            if (_wcsicmp(name, L"sl.interposer.dll") == 0)
            {
                const char* exports[] = { "slGetNativeInterface", "slUpgradeInterface", "slInit", "slShutdown" };
                for (const char* exportName : exports)
                {
                    FARPROC proc = GetProcAddress(module, exportName);
                    std::ostringstream es;
                    es << "STREAMLINE_EXPORT | sample=" << sample
                       << " name=" << exportName
                       << " address=" << PtrText(reinterpret_cast<void*>(proc));
                    Log(es.str());
                }
            }
        }
    }

    void InspectSystemExports(const char* sample)
    {
        const wchar_t* modules[] = { L"dxgi.dll", L"d3d12.dll" };
        const char* dxgiExports[] = { "CreateDXGIFactory", "CreateDXGIFactory1", "CreateDXGIFactory2" };
        const char* d3d12Exports[] = { "D3D12CreateDevice", "D3D12GetDebugInterface" };

        for (const wchar_t* moduleName : modules)
        {
            HMODULE module = GetModuleHandleW(moduleName);
            if (!module) continue;
            const char** exports = (_wcsicmp(moduleName, L"dxgi.dll") == 0) ? dxgiExports : d3d12Exports;
            const size_t count = (_wcsicmp(moduleName, L"dxgi.dll") == 0) ? std::size(dxgiExports) : std::size(d3d12Exports);
            for (size_t i = 0; i < count; ++i)
            {
                FARPROC proc = GetProcAddress(module, exports[i]);
                std::ostringstream ss;
                ss << "SYSTEM_EXPORT | sample=" << sample
                   << " module=" << Narrow(moduleName)
                   << " name=" << exports[i]
                   << " address=" << PtrText(reinterpret_cast<void*>(proc));
                Log(ss.str());
            }
        }
    }

    void Sample(const char* name, bool inspectIat)
    {
        EnumerateInterestingModules(name);
        InspectStreamline(name);
        InspectSystemExports(name);
        if (inspectIat) InspectMainModuleImports(name);
    }

    DWORD WINAPI ProbeThread(LPVOID)
    {
        DeleteFileA(kLogPath);
        Log(std::string("ColorCoreVI Output Probe P0005 v") + kVersion + " | START");
        Log("MODE | observation only; NO API hooks; NO vtable patching; NO pixel modification");
        Log("PURPOSE | identify GTA presentation/interposer chain before touching the real swapchain");

        Sample("t0", true);
        Log("PROBE_READY | passive presentation-chain observation active");

        for (int second = 1; second <= 60; ++second)
        {
            Sleep(1000);
            if (second == 2) Sample("t2", false);
            else if (second == 5) Sample("t5", true);
            else if (second == 10) Sample("t10", false);
            else if (second == 20) Sample("t20", true);
            else if (second == 40) Sample("t40", false);
            else if (second == 60) Sample("t60", true);
        }

        Log("PROBE_COMPLETE | 60-second passive observation window finished");
        return 0;
    }
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(module);
        HANDLE thread = CreateThread(nullptr, 0, ProbeThread, nullptr, 0, nullptr);
        if (thread) CloseHandle(thread);
    }
    return TRUE;
}
