#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <wchar.h>
int WINAPI wWinMain(HINSTANCE a,HINSTANCE b,PWSTR c,int d) {
    (void)a; (void)b; (void)c; (void)d;
    wchar_t cwd[32768];
    if (!GetCurrentDirectoryW(32768,cwd)) return 99;
    HANDLE f=CreateFileW(L"probe.txt",GENERIC_WRITE,0,NULL,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,NULL);
    if(f==INVALID_HANDLE_VALUE)return 98;
    DWORD bytes=0;
    BOOL ok=WriteFile(f,cwd,(DWORD)(wcslen(cwd)*sizeof(wchar_t)),&bytes,NULL);
    CloseHandle(f);
    wchar_t wait[2];
    if(GetEnvironmentVariableW(L"STARTAPP_PROBE_WAIT",wait,2)>0) Sleep(INFINITE);
    return ok ? 23 : 97;
}
