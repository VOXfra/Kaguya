#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <fstream>
#include <string>

namespace
{
    using ScriptMain=void(*)();
    using ScriptRegisterFn=void(__cdecl*)(HMODULE,ScriptMain);
    using ScriptUnregisterFn=void(__cdecl*)(HMODULE);
    using ScriptWaitFn=void(__cdecl*)(DWORD);

    HMODULE g_self=nullptr;
    ScriptRegisterFn g_register=nullptr;
    ScriptUnregisterFn g_unregister=nullptr;
    ScriptWaitFn g_wait=nullptr;

    void Log(const char* text)
    {
        std::ofstream out("scripts\\CharacterRuntimeVI\\BodyMorphBridge.log",std::ios::app);
        if(!out)return;SYSTEMTIME st{};GetLocalTime(&st);char stamp[64]{};
        sprintf_s(stamp,"%04u-%02u-%02u %02u:%02u:%02u.%03u | ",st.wYear,st.wMonth,st.wDay,st.wHour,st.wMinute,st.wSecond,st.wMilliseconds);
        out<<stamp<<text<<"\n";
    }

    void MainScript()
    {
        // RC4 intentionally performs no entity-matrix writes. The earlier bridge
        // searched writable CPed memory for a transform and could overlap the new
        // managed skeleton morph. CharacterRuntimeVI now has one body-morph owner:
        // MuscleMorphRuntimeScript, through validated protagonist bones.
        Log("VOXBodyMorphVI compatibility bridge active; native matrix morph disabled. Managed MuscleMorphRuntimeScript owns physique changes.");
        while(true){if(g_wait)g_wait(1000);else Sleep(1000);}
    }

    DWORD WINAPI Init(LPVOID)
    {
        HMODULE shv=nullptr;for(int i=0;i<200&&!shv;i++){shv=GetModuleHandleW(L"ScriptHookV.dll");if(!shv)Sleep(50);}if(!shv){Log("ScriptHookV.dll not found; compatibility bridge inactive.");return 0;}
        g_register=reinterpret_cast<ScriptRegisterFn>(GetProcAddress(shv,"?scriptRegister@@YAXPEAUHINSTANCE__@@P6AXXZ@Z"));
        g_unregister=reinterpret_cast<ScriptUnregisterFn>(GetProcAddress(shv,"?scriptUnregister@@YAXPEAUHINSTANCE__@@@Z"));
        g_wait=reinterpret_cast<ScriptWaitFn>(GetProcAddress(shv,"?scriptWait@@YAXK@Z"));
        if(!g_register||!g_wait){Log("Required ScriptHookV exports unavailable; compatibility bridge inactive.");return 0;}
        g_register(g_self,&MainScript);return 0;
    }
}

BOOL APIENTRY DllMain(HMODULE module,DWORD reason,LPVOID)
{
    if(reason==DLL_PROCESS_ATTACH){g_self=module;DisableThreadLibraryCalls(module);HANDLE t=CreateThread(nullptr,0,&Init,nullptr,0,nullptr);if(t)CloseHandle(t);}
    else if(reason==DLL_PROCESS_DETACH){if(g_unregister&&g_self)g_unregister(g_self);}
    return TRUE;
}
