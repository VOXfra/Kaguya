#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <tlhelp32.h>

#include <algorithm>
#include <cctype>
#include <cstdint>
#include <fstream>
#include <iostream>
#include <map>
#include <set>
#include <sstream>
#include <string>
#include <thread>
#include <vector>

namespace
{
    constexpr const wchar_t* kTargetProcess = L"GTA5_Enhanced.exe";
    constexpr const char* kLogPath = "ColorCoreVI_ExternalProbe.log";
    constexpr const char* kVersion = "0.1.4";

    struct ModuleInfo
    {
        std::wstring name;
        std::wstring path;
        uintptr_t base = 0;
        uint32_t size = 0;
    };

    std::set<std::wstring> g_seenModules;

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
        std::string result(static_cast<size_t>(needed), '\0');
        const int written = WideCharToMultiByte(CP_UTF8, 0, value.c_str(), -1, result.data(), needed, nullptr, nullptr);
        if (written <= 0) return {};
        if (!result.empty() && result.back() == '\0') result.pop_back();
        return result;
    }

    std::wstring LowerWide(std::wstring value)
    {
        std::transform(value.begin(), value.end(), value.begin(), [](wchar_t c) {
            if (c >= L'A' && c <= L'Z') return static_cast<wchar_t>(c - L'A' + L'a');
            return c;
        });
        return value;
    }

    std::string LowerAscii(std::string value)
    {
        std::transform(value.begin(), value.end(), value.begin(), [](unsigned char c) {
            return static_cast<char>(std::tolower(c));
        });
        return value;
    }

    std::string Hex(uintptr_t value)
    {
        std::ostringstream ss;
        ss << "0x" << std::hex << std::uppercase << value;
        return ss.str();
    }

    bool IsInterestingModule(const std::wstring& raw)
    {
        const std::wstring name = LowerWide(raw);
        const wchar_t* terms[] = {
            L"dxgi", L"d3d12", L"streamline", L"sl.", L"interposer", L"nvngx",
            L"dlss", L"nvidia", L"reshade", L"overlay", L"steam", L"epic",
            L"amd", L"fsr", L"xess", L"intel", L"framegen", L"frame_generation"
        };
        for (const wchar_t* term : terms)
            if (name.find(term) != std::wstring::npos) return true;
        return false;
    }

    bool IsRelevantImportDll(const std::string& raw)
    {
        const std::string dll = LowerAscii(raw);
        return dll.find("dxgi") != std::string::npos ||
               dll.find("d3d12") != std::string::npos ||
               dll.find("interposer") != std::string::npos ||
               dll.find("streamline") != std::string::npos ||
               dll.find("nvngx") != std::string::npos ||
               dll.find("dlss") != std::string::npos;
    }

    DWORD FindTargetProcess()
    {
        HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == INVALID_HANDLE_VALUE) return 0;
        PROCESSENTRY32W entry{};
        entry.dwSize = sizeof(entry);
        DWORD pid = 0;
        if (Process32FirstW(snap, &entry))
        {
            do
            {
                if (_wcsicmp(entry.szExeFile, kTargetProcess) == 0)
                {
                    pid = entry.th32ProcessID;
                    break;
                }
            } while (Process32NextW(snap, &entry));
        }
        CloseHandle(snap);
        return pid;
    }

    std::vector<ModuleInfo> SnapshotModules(DWORD pid)
    {
        std::vector<ModuleInfo> modules;
        HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
        if (snap == INVALID_HANDLE_VALUE) return modules;
        MODULEENTRY32W entry{};
        entry.dwSize = sizeof(entry);
        if (Module32FirstW(snap, &entry))
        {
            do
            {
                ModuleInfo m;
                m.name = entry.szModule;
                m.path = entry.szExePath;
                m.base = reinterpret_cast<uintptr_t>(entry.modBaseAddr);
                m.size = entry.modBaseSize;
                modules.push_back(std::move(m));
            } while (Module32NextW(snap, &entry));
        }
        CloseHandle(snap);
        return modules;
    }

    const ModuleInfo* FindOwner(const std::vector<ModuleInfo>& modules, uintptr_t address)
    {
        for (const auto& m : modules)
        {
            if (address >= m.base && address < m.base + static_cast<uintptr_t>(m.size)) return &m;
        }
        return nullptr;
    }

    void LogModules(const char* sample, const std::vector<ModuleInfo>& modules)
    {
        size_t interesting = 0;
        for (const auto& m : modules)
        {
            if (!IsInterestingModule(m.name)) continue;
            ++interesting;
            const std::wstring key = LowerWide(m.path);
            const bool first = g_seenModules.insert(key).second;
            std::ostringstream ss;
            ss << "MODULE_INTERESTING | sample=" << sample
               << " firstSeen=" << (first ? 1 : 0)
               << " name=" << Narrow(m.name)
               << " base=" << Hex(m.base)
               << " size=" << m.size
               << " path=" << Narrow(m.path);
            Log(ss.str());
        }
        std::ostringstream summary;
        summary << "MODULE_SCAN | sample=" << sample
                << " total=" << modules.size()
                << " interesting=" << interesting;
        Log(summary.str());
    }

    template <typename T>
    bool ReadRemote(HANDLE process, uintptr_t address, T& value)
    {
        SIZE_T read = 0;
        return ReadProcessMemory(process, reinterpret_cast<LPCVOID>(address), &value, sizeof(T), &read) && read == sizeof(T);
    }

    std::string ReadRemoteString(HANDLE process, uintptr_t address, size_t maxLength = 512)
    {
        std::string result;
        result.reserve(64);
        for (size_t i = 0; i < maxLength; ++i)
        {
            char c = 0;
            SIZE_T read = 0;
            if (!ReadProcessMemory(process, reinterpret_cast<LPCVOID>(address + i), &c, 1, &read) || read != 1) break;
            if (c == '\0') break;
            result.push_back(c);
        }
        return result;
    }

    const ModuleInfo* MainModule(const std::vector<ModuleInfo>& modules)
    {
        for (const auto& m : modules)
            if (_wcsicmp(m.name.c_str(), kTargetProcess) == 0) return &m;
        return modules.empty() ? nullptr : &modules.front();
    }

    bool ValidRva(uint32_t rva, uint32_t bytes, uint32_t imageSize)
    {
        if (rva == 0 || rva >= imageSize) return false;
        return bytes <= imageSize - rva;
    }

    void InspectImports(HANDLE process, const char* sample, const std::vector<ModuleInfo>& modules)
    {
        const ModuleInfo* main = MainModule(modules);
        if (!main)
        {
            Log(std::string("IAT_FAIL | sample=") + sample + " reason=no-main-module");
            return;
        }

        IMAGE_DOS_HEADER dos{};
        if (!ReadRemote(process, main->base, dos) || dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew <= 0)
        {
            Log(std::string("IAT_FAIL | sample=") + sample + " reason=bad-dos-header");
            return;
        }

        IMAGE_NT_HEADERS64 nt{};
        if (!ReadRemote(process, main->base + static_cast<uintptr_t>(dos.e_lfanew), nt) ||
            nt.Signature != IMAGE_NT_SIGNATURE || nt.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC)
        {
            Log(std::string("IAT_FAIL | sample=") + sample + " reason=bad-pe-header");
            return;
        }

        const uint32_t imageSize = nt.OptionalHeader.SizeOfImage;
        const auto& dir = nt.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
        if (!ValidRva(dir.VirtualAddress, sizeof(IMAGE_IMPORT_DESCRIPTOR), imageSize))
        {
            Log(std::string("IAT | sample=") + sample + " state=no-import-directory");
            return;
        }

        size_t allDlls = 0;
        size_t relevantDlls = 0;
        size_t symbols = 0;
        for (size_t di = 0; di < 1024; ++di)
        {
            IMAGE_IMPORT_DESCRIPTOR desc{};
            const uintptr_t descAddr = main->base + dir.VirtualAddress + di * sizeof(desc);
            if (!ReadRemote(process, descAddr, desc)) break;
            if (desc.Name == 0) break;
            ++allDlls;
            if (!ValidRva(desc.Name, 1, imageSize)) continue;
            const std::string dll = ReadRemoteString(process, main->base + desc.Name);
            if (!IsRelevantImportDll(dll)) continue;
            ++relevantDlls;
            Log(std::string("IMPORT_DLL | sample=") + sample + " dll=" + dll);

            const uint32_t lookupRva = desc.OriginalFirstThunk ? desc.OriginalFirstThunk : desc.FirstThunk;
            if (!ValidRva(lookupRva, sizeof(IMAGE_THUNK_DATA64), imageSize) ||
                !ValidRva(desc.FirstThunk, sizeof(IMAGE_THUNK_DATA64), imageSize)) continue;

            for (size_t ti = 0; ti < 8192; ++ti)
            {
                IMAGE_THUNK_DATA64 lookup{};
                IMAGE_THUNK_DATA64 iat{};
                if (!ReadRemote(process, main->base + lookupRva + ti * sizeof(lookup), lookup)) break;
                if (!ReadRemote(process, main->base + desc.FirstThunk + ti * sizeof(iat), iat)) break;
                if (lookup.u1.AddressOfData == 0) break;

                std::string symbol;
                if (IMAGE_SNAP_BY_ORDINAL64(lookup.u1.Ordinal))
                {
                    symbol = std::string("ordinal#") + std::to_string(IMAGE_ORDINAL64(lookup.u1.Ordinal));
                }
                else
                {
                    const uint64_t nameRva = lookup.u1.AddressOfData;
                    if (nameRva >= imageSize || !ValidRva(static_cast<uint32_t>(nameRva), sizeof(uint16_t) + 1, imageSize))
                        continue;
                    symbol = ReadRemoteString(process, main->base + static_cast<uint32_t>(nameRva) + sizeof(uint16_t));
                }

                const uintptr_t target = static_cast<uintptr_t>(iat.u1.Function);
                const ModuleInfo* owner = FindOwner(modules, target);
                std::ostringstream ss;
                ss << "IAT_SYMBOL | sample=" << sample
                   << " dll=" << dll
                   << " symbol=" << symbol
                   << " target=" << Hex(target)
                   << " owner=" << (owner ? Narrow(owner->name) : std::string("<unmapped>"));
                Log(ss.str());
                ++symbols;
            }
        }

        std::ostringstream summary;
        summary << "IAT_SUMMARY | sample=" << sample
                << " allDlls=" << allDlls
                << " relevantDlls=" << relevantDlls
                << " symbols=" << symbols;
        Log(summary.str());
    }

    void Sample(HANDLE process, DWORD pid, const char* label, bool inspectImports)
    {
        const auto modules = SnapshotModules(pid);
        if (modules.empty())
        {
            Log(std::string("MODULE_SCAN_FAIL | sample=") + label + " reason=empty-snapshot");
            return;
        }
        LogModules(label, modules);
        if (inspectImports) InspectImports(process, label, modules);
    }
}

int wmain(int argc, wchar_t** argv)
{
    int waitSeconds = 180;
    int durationSeconds = 60;
    for (int i = 1; i + 1 < argc; ++i)
    {
        if (_wcsicmp(argv[i], L"--wait") == 0) waitSeconds = _wtoi(argv[++i]);
        else if (_wcsicmp(argv[i], L"--duration") == 0) durationSeconds = _wtoi(argv[++i]);
    }
    if (waitSeconds < 1) waitSeconds = 1;
    if (durationSeconds < 1) durationSeconds = 1;

    DeleteFileA(kLogPath);
    Log(std::string("ColorCoreVI External Presentation Chain Probe P0005 v") + kVersion + " | START");
    Log("MODE | EXTERNAL READ-ONLY OBSERVATION; NO DLL/ASI INJECTION; NO API HOOKS; NO PROCESS WRITES");
    Log("WAITING | target=GTA5_Enhanced.exe");
    std::cout << "ColorCoreVI P0005 v" << kVersion << " external probe\n";
    std::cout << "Waiting for GTA5_Enhanced.exe. Launch GTA V Enhanced normally now.\n";

    DWORD pid = 0;
    for (int i = 0; i < waitSeconds * 4 && pid == 0; ++i)
    {
        pid = FindTargetProcess();
        if (!pid) std::this_thread::sleep_for(std::chrono::milliseconds(250));
    }
    if (!pid)
    {
        Log("TIMEOUT | GTA5_Enhanced.exe was not found");
        std::cerr << "Timed out waiting for GTA V Enhanced.\n";
        return 2;
    }

    HANDLE process = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | SYNCHRONIZE, FALSE, pid);
    if (!process)
    {
        Log(std::string("OPEN_PROCESS_FAIL | pid=") + std::to_string(pid) + " error=" + std::to_string(GetLastError()));
        std::cerr << "Could not open GTA process for read-only inspection.\n";
        return 3;
    }

    Log(std::string("PROCESS_FOUND | pid=") + std::to_string(pid));
    Sample(process, pid, "t0", true);

    for (int second = 1; second <= durationSeconds; ++second)
    {
        if (WaitForSingleObject(process, 1000) == WAIT_OBJECT_0)
        {
            Log(std::string("PROCESS_EXITED | second=") + std::to_string(second));
            CloseHandle(process);
            return 4;
        }

        if (second == 2) Sample(process, pid, "t2", false);
        else if (second == 5) Sample(process, pid, "t5", true);
        else if (second == 10) Sample(process, pid, "t10", false);
        else if (second == 20) Sample(process, pid, "t20", true);
        else if (second == 40) Sample(process, pid, "t40", false);
        else if (second == 60) Sample(process, pid, "t60", true);
        else if (second == durationSeconds && durationSeconds > 5) Sample(process, pid, "final", true);
    }

    Log(std::string("PROBE_COMPLETE | durationSeconds=") + std::to_string(durationSeconds));
    CloseHandle(process);
    std::cout << "Probe complete. Send ColorCoreVI_ExternalProbe.log back in ChatGPT.\n";
    return 0;
}
