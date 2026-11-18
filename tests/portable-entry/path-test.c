#include <assert.h>
#include <stdio.h>
#include "../../tools/portable-entry/launcher-path.h"
int main(void) {
    wchar_t out[1024];
    assert(payload_path(L"C:\\Downloads\\Start-app.exe",out,1024));
    assert(wcscmp(out,L"C:\\Downloads\\App\\StartApp.App.exe")==0);
    assert(payload_path(L"D:\\moved folder\\עברית\\Start-app.exe",out,1024));
    assert(wcscmp(out,L"D:\\moved folder\\עברית\\App\\StartApp.App.exe")==0);
    assert(payload_path(L"\\\\host\\share\\Start-app.exe",out,1024));
    assert(wcscmp(out,L"\\\\host\\share\\App\\StartApp.App.exe")==0);
    assert(payload_path(L"C:\\Start-app.exe",out,1024));
    assert(!payload_path(L"Start-app.exe",out,1024));
    assert(!payload_path(L"C:\\Start-app.exe",out,4));
    assert(!payload_path(L"",out,1024));
    puts("portable-entry path tests PASS (7 cases)");
}
