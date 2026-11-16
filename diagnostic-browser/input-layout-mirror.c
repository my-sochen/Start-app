#include <stdint.h>
#include <stddef.h>
#include <stdio.h>
typedef struct { int32_t dx,dy; uint32_t mouseData,dwFlags,time; uint64_t dwExtraInfo; } MOUSEINPUT_X64;
typedef struct { uint16_t wVk,wScan; uint32_t dwFlags,time; uint64_t dwExtraInfo; } KEYBDINPUT_X64;
typedef struct { uint32_t uMsg; uint16_t wParamL,wParamH; } HARDWAREINPUT_X64;
typedef union { MOUSEINPUT_X64 mi; KEYBDINPUT_X64 ki; HARDWAREINPUT_X64 hi; } INPUT_UNION_X64;
typedef struct { uint32_t type; INPUT_UNION_X64 data; } INPUT_X64;
_Static_assert(sizeof(INPUT_X64)==40,"INPUT40");
_Static_assert(offsetof(INPUT_X64,data)==8,"DATA8");
_Static_assert(offsetof(KEYBDINPUT_X64,dwFlags)==4,"KEYFLAGS4");
int main(void){printf("pointer=%zu mouse=%zu keyboard=%zu union=%zu union-align=%zu input=%zu data-offset=%zu\n",sizeof(void*),sizeof(MOUSEINPUT_X64),sizeof(KEYBDINPUT_X64),sizeof(INPUT_UNION_X64),_Alignof(INPUT_UNION_X64),sizeof(INPUT_X64),offsetof(INPUT_X64,data));}
