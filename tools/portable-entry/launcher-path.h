#ifndef STARTAPP_LAUNCHER_PATH_H
#define STARTAPP_LAUNCHER_PATH_H
#include <stddef.h>
#include <wchar.h>
/* Resolve only the fixed payload next to this launcher, never CWD or PATH. */
static int payload_path(const wchar_t *module, wchar_t *out, size_t cap) {
    static const wchar_t tail[] = L"App\\StartApp.App.exe";
    size_t length = wcslen(module), prefix = length;
    while (prefix && module[prefix-1] != L'\\' && module[prefix-1] != L'/') --prefix;
    if (!prefix || prefix + sizeof(tail)/sizeof(tail[0]) > cap) return 0;
    wmemcpy(out, module, prefix);
    wmemcpy(out + prefix, tail, sizeof(tail)/sizeof(tail[0]));
    return 1;
}
#endif
