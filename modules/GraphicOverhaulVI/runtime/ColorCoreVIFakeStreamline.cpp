#define WIN32_LEAN_AND_MEAN
#include <windows.h>

extern "C" __declspec(dllexport) void* slGetNativeInterface(void* value)
{
    return value;
}

BOOL APIENTRY DllMain(HMODULE, DWORD, LPVOID)
{
    return TRUE;
}
