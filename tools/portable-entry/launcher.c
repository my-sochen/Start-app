#define _CRT_SECURE_NO_WARNINGS
#define UNICODE
#define _UNICODE
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <wchar.h>
#include "launcher-path.h"

/* A launcher, not a loader: the original self-contained app stays intact in App/.
   No arguments, environment edits, elevation, shell dispatch, or PATH search.
   Waiting propagates the exact child's exit code and gives tests owned cleanup. */
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, PWSTR args, int show) {
    (void)instance; (void)previous; (void)args; (void)show;
    enum { PATH_CAP = 32768 };
    wchar_t *module = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, PATH_CAP * sizeof(wchar_t));
    wchar_t *target = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, PATH_CAP * sizeof(wchar_t));
    wchar_t *directory = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, PATH_CAP * sizeof(wchar_t));
    wchar_t *command = HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, (PATH_CAP + 3) * sizeof(wchar_t));
    DWORD result = ERROR_NOT_ENOUGH_MEMORY;
    const wchar_t *message = L"Start-app could not prepare to start. Close other apps and try again.";
    PROCESS_INFORMATION child = {0};
    STARTUPINFOW startup = {0}; startup.cb = sizeof(startup);
    if (!module || !target || !directory || !command) goto fail;
    DWORD length = GetModuleFileNameW(NULL, module, PATH_CAP);
    if (!length || length >= PATH_CAP || !payload_path(module, target, PATH_CAP)) {
        result = ERROR_BAD_PATHNAME;
        message = L"Start-app could not find its folder. Extract the entire ZIP and try again.";
        goto fail;
    }
    DWORD attributes = GetFileAttributesW(target);
    if (attributes == INVALID_FILE_ATTRIBUTES || (attributes & FILE_ATTRIBUTE_DIRECTORY)) {
        result = ERROR_FILE_NOT_FOUND;
        message = L"Keep Start-app.exe beside its App folder. Extract the entire ZIP again; do not move the EXE alone.\n\nיש להשאיר את קובץ ההפעלה לצד תיקיית App ולחלץ את כל קובצי ה-ZIP.";
        goto fail;
    }
    size_t n = wcslen(target);
    wmemcpy(directory, target, n + 1);
    wchar_t *last = wcsrchr(directory, L'\\');
    if (!last) last = wcsrchr(directory, L'/');
    if (!last) { result = ERROR_BAD_PATHNAME; goto fail; }
    *last = L'\0';
    command[0] = L'"'; wmemcpy(command + 1, target, n); command[n+1] = L'"'; command[n+2] = L'\0';
    if (!CreateProcessW(target, command, NULL, NULL, FALSE, 0, NULL, directory, &startup, &child)) {
        result = GetLastError();
        message = L"Start-app could not start. Keep the App folder intact. If Windows Security blocked a file, review its warning; do not turn protection off.";
        goto fail;
    }
    CloseHandle(child.hThread);
    if (WaitForSingleObject(child.hProcess, INFINITE) != WAIT_OBJECT_0 || !GetExitCodeProcess(child.hProcess, &result))
        result = ERROR_PROCESS_ABORTED;
    CloseHandle(child.hProcess);
    goto done;
fail:
    MessageBoxW(NULL, message, L"Start-app", MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
done:
    if (command) HeapFree(GetProcessHeap(), 0, command);
    if (directory) HeapFree(GetProcessHeap(), 0, directory);
    if (target) HeapFree(GetProcessHeap(), 0, target);
    if (module) HeapFree(GetProcessHeap(), 0, module);
    return (int)result;
}
