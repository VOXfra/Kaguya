#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cmath>
#include <cstdint>
#include <fstream>
#include <sstream>
#include <string>
#include <algorithm>

namespace
{
    using ScriptMain = void(*)();
    using ScriptRegisterFn = void(__cdecl*)(HMODULE, ScriptMain);
    using ScriptUnregisterFn = void(__cdecl*)(HMODULE);
    using ScriptWaitFn = void(__cdecl*)(DWORD);
    using GetHandleBaseFn = BYTE*(__cdecl*)(int);

    HMODULE g_self = nullptr;
    ScriptRegisterFn g_scriptRegister = nullptr;
    ScriptUnregisterFn g_scriptUnregister = nullptr;
    ScriptWaitFn g_scriptWait = nullptr;
    GetHandleBaseFn g_getHandleBase = nullptr;

    struct BodyState
    {
        bool enabled = false;
        int ped = 0;
        float x = 0.0f;
        float y = 0.0f;
        float z = 0.0f;
        float width = 1.0f;
    };

    BodyState g_state;
    DWORD g_lastStateRead = 0;
    DWORD g_lastMissingMatrixLog = 0;
    int g_cachedPed = 0;
    ptrdiff_t g_cachedMatrixOffset = -1;
    bool g_appliedLastFrame = false;

    const char* kStatePath = "scripts\\CharacterRuntimeVI\\BodyMorphState.txt";
    const char* kLogPath = "scripts\\CharacterRuntimeVI\\BodyMorphBridge.log";

    void Log(const std::string& text)
    {
        std::ofstream out(kLogPath, std::ios::app);
        if (!out) return;
        SYSTEMTIME st{};
        GetLocalTime(&st);
        char stamp[64]{};
        sprintf_s(stamp, "%04u-%02u-%02u %02u:%02u:%02u.%03u | ",
            st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, st.wMilliseconds);
        out << stamp << text << "\n";
    }

    bool ParseBool(const std::string& v)
    {
        return v == "True" || v == "true" || v == "1" || v == "yes";
    }

    void ReadState()
    {
        std::ifstream in(kStatePath);
        if (!in) return;

        BodyState next;
        std::string line;
        while (std::getline(in, line))
        {
            const auto eq = line.find('=');
            if (eq == std::string::npos) continue;
            const std::string key = line.substr(0, eq);
            const std::string value = line.substr(eq + 1);
            try
            {
                if (key == "enabled") next.enabled = ParseBool(value);
                else if (key == "ped") next.ped = std::stoi(value);
                else if (key == "x") next.x = std::stof(value);
                else if (key == "y") next.y = std::stof(value);
                else if (key == "z") next.z = std::stof(value);
                else if (key == "width") next.width = std::stof(value);
            }
            catch (...) { }
        }

        if (!std::isfinite(next.width)) next.width = 1.0f;
        next.width = std::clamp(next.width, 1.0f, 1.05f);
        if (next.ped != g_state.ped)
        {
            g_cachedPed = 0;
            g_cachedMatrixOffset = -1;
            g_appliedLastFrame = false;
        }
        g_state = next;
    }

    bool RangeReadableWritable(BYTE* address, size_t bytes)
    {
        MEMORY_BASIC_INFORMATION mbi{};
        if (!VirtualQuery(address, &mbi, sizeof(mbi))) return false;
        if (mbi.State != MEM_COMMIT) return false;
        if (mbi.Protect & (PAGE_GUARD | PAGE_NOACCESS)) return false;
        const DWORD p = mbi.Protect & 0xFF;
        const bool writable = p == PAGE_READWRITE || p == PAGE_WRITECOPY || p == PAGE_EXECUTE_READWRITE || p == PAGE_EXECUTE_WRITECOPY;
        if (!writable) return false;
        const uintptr_t start = reinterpret_cast<uintptr_t>(address);
        const uintptr_t end = start + bytes;
        const uintptr_t regionEnd = reinterpret_cast<uintptr_t>(mbi.BaseAddress) + mbi.RegionSize;
        return end <= regionEnd;
    }

    float Length3(const float* p)
    {
        return std::sqrt(p[0] * p[0] + p[1] * p[1] + p[2] * p[2]);
    }

    bool MatrixLooksLikeEntityTransform(float* m, const BodyState& s)
    {
        for (int i = 0; i < 16; ++i)
            if (!std::isfinite(m[i])) return false;

        if (std::fabs(m[15] - 1.0f) > 0.08f) return false;
        const float dx = m[12] - s.x;
        const float dy = m[13] - s.y;
        const float dz = m[14] - s.z;
        if (dx * dx + dy * dy + dz * dz > 6.25f) return false;

        const float a = Length3(m + 0);
        const float b = Length3(m + 4);
        const float c = Length3(m + 8);
        if (a < 0.70f || a > 1.20f) return false;
        if (b < 0.70f || b > 1.20f) return false;
        if (c < 0.70f || c > 1.20f) return false;

        const float dotAB = m[0] * m[4] + m[1] * m[5] + m[2] * m[6];
        const float dotAC = m[0] * m[8] + m[1] * m[9] + m[2] * m[10];
        const float dotBC = m[4] * m[8] + m[5] * m[9] + m[6] * m[10];
        return std::fabs(dotAB) < 0.20f && std::fabs(dotAC) < 0.20f && std::fabs(dotBC) < 0.20f;
    }

    bool SafeMatrixLooks(float* matrix, const BodyState* state)
    {
        __try { return matrix && state && MatrixLooksLikeEntityTransform(matrix, *state); }
        __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
    }

    void NormalizeAndScale(float* p, float scale)
    {
        const float len = Length3(p);
        if (len < 0.0001f) return;
        const float mul = scale / len;
        p[0] *= mul;
        p[1] *= mul;
        p[2] *= mul;
    }

    bool SafeScaleMatrix(float* matrix, const BodyState* state, float width)
    {
        __try
        {
            if (!matrix || !state || !MatrixLooksLikeEntityTransform(matrix, *state)) return false;
            NormalizeAndScale(matrix + 0, width);
            NormalizeAndScale(matrix + 4, width);
            NormalizeAndScale(matrix + 8, 1.0f);
            return true;
        }
        __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
    }

    ptrdiff_t FindTransformOffset(BYTE* base, const BodyState& s)
    {
        if (!base) return -1;
        for (ptrdiff_t offset = 0x20; offset <= 0x240; offset += 4)
        {
            BYTE* address = base + offset;
            if (!RangeReadableWritable(address, sizeof(float) * 16)) continue;
            if (SafeMatrixLooks(reinterpret_cast<float*>(address), &s)) return offset;
        }
        return -1;
    }

    bool ApplyWidth(float targetWidth)
    {
        if (!g_getHandleBase || g_state.ped <= 0) return false;
        BYTE* base = g_getHandleBase(g_state.ped);
        if (!base) return false;

        if (g_cachedPed != g_state.ped)
        {
            g_cachedPed = g_state.ped;
            g_cachedMatrixOffset = -1;
        }

        if (g_cachedMatrixOffset >= 0)
        {
            BYTE* candidate = base + g_cachedMatrixOffset;
            if (!RangeReadableWritable(candidate, sizeof(float) * 16) ||
                !SafeMatrixLooks(reinterpret_cast<float*>(candidate), &g_state))
                g_cachedMatrixOffset = -1;
        }

        if (g_cachedMatrixOffset < 0)
        {
            g_cachedMatrixOffset = FindTransformOffset(base, g_state);
            if (g_cachedMatrixOffset >= 0)
            {
                std::ostringstream ss;
                ss << "Validated entity transform matrix for ped=" << g_state.ped << " at base+0x" << std::hex << g_cachedMatrixOffset << ".";
                Log(ss.str());
            }
            else return false;
        }

        float* matrix = reinterpret_cast<float*>(base + g_cachedMatrixOffset);
        if (SafeScaleMatrix(matrix, &g_state, targetWidth)) return true;
        g_cachedMatrixOffset = -1;
        return false;
    }

    void RunFrame()
    {
        const DWORD now = GetTickCount();
        if (now - g_lastStateRead >= 120)
        {
            g_lastStateRead = now;
            ReadState();
        }

        if (g_state.ped <= 0)
        {
            g_appliedLastFrame = false;
            return;
        }

        if (!g_state.enabled)
        {
            if (g_appliedLastFrame) ApplyWidth(1.0f);
            g_appliedLastFrame = false;
            return;
        }

        if (ApplyWidth(g_state.width)) g_appliedLastFrame = true;
        else if (now - g_lastMissingMatrixLog > 5000)
        {
            g_lastMissingMatrixLog = now;
            Log("No validated writable entity transform matrix found; morph skipped safely for this frame.");
        }
    }

    void MainScript()
    {
        Log("VOX Body Morph VI native bridge script started.");
        while (true)
        {
            RunFrame();
            if (g_scriptWait) g_scriptWait(0);
            else Sleep(16);
        }
    }

    FARPROC Resolve(HMODULE module, const char* decorated)
    {
        return module ? GetProcAddress(module, decorated) : nullptr;
    }

    DWORD WINAPI InitializeBridge(LPVOID)
    {
        HMODULE shv = nullptr;
        for (int i = 0; i < 200 && !shv; ++i)
        {
            shv = GetModuleHandleW(L"ScriptHookV.dll");
            if (!shv) Sleep(50);
        }
        if (!shv)
        {
            Log("ScriptHookV.dll was not found; body bridge remains inactive.");
            return 0;
        }

        g_scriptRegister = reinterpret_cast<ScriptRegisterFn>(Resolve(shv, "?scriptRegister@@YAXPEAUHINSTANCE__@@P6AXXZ@Z"));
        g_scriptUnregister = reinterpret_cast<ScriptUnregisterFn>(Resolve(shv, "?scriptUnregister@@YAXPEAUHINSTANCE__@@@Z"));
        g_scriptWait = reinterpret_cast<ScriptWaitFn>(Resolve(shv, "?scriptWait@@YAXK@Z"));
        g_getHandleBase = reinterpret_cast<GetHandleBaseFn>(Resolve(shv, "?getScriptHandleBaseAddress@@YAPEAEH@Z"));

        if (!g_scriptRegister || !g_scriptWait || !g_getHandleBase)
        {
            Log("Required ScriptHookV exports are unavailable; no memory writes will be attempted.");
            return 0;
        }

        Log("ScriptHookV exports resolved. Registering validated body-morph script.");
        g_scriptRegister(g_self, &MainScript);
        return 0;
    }
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_self = module;
        DisableThreadLibraryCalls(module);
        HANDLE thread = CreateThread(nullptr, 0, &InitializeBridge, nullptr, 0, nullptr);
        if (thread) CloseHandle(thread);
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        if (g_scriptUnregister && g_self) g_scriptUnregister(g_self);
    }
    return TRUE;
}
