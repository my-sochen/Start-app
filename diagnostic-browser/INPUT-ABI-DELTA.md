# V8 LOCAL ABI correction, no browser/runtime/push

Native Windows x64 INPUT consists DWORD then union containing MOUSEINPUT, KEYBDINPUT, HARDWAREINPUT. MOUSEINPUT has ULONG_PTR (8byte) dwExtraInfo, imposesunion8alignment/32size. Microsoftx64aggregate rules maximum-memberalignment/padding produceDataoffset8/INPUTsize40. V7managedunion declaredonlyushort/uint+Size32, maximumfieldalignment4: Sequentialouterhasno8alignmentwitness. Therefore do not trust originalmanagedlayout.

Cross-check local Linuxx64C11mirror usingfixed-widthWin32types plus64bitULONG_PTR compiled gcc-Wall-Werror withstaticassertData8/INPUT40/KEYflags4; outputpointer8/mouse32/keyboard24/union32/align8/input40/offset8. Independentctypesmirroragrees. These are native-layout mirrors, NOT actualWindowsSDK/MSVC or managedMarshaltests; no dotnetavailablelocally.

Minimalfix explicitInputSize40/Typeoffset0/Dataoffset8, unchangedKeyboardUnionflags4, x64onlyexistinggate. AddedruntimeMarshalchecksPtr8/Input40/Data8/Flags4 beforeSendInput, failclosed; notcompilerconfirmedhere. Sourceunchangedotherwise. No silentbrowserinput/retry. AnyfuturepartialSendInput remains terminal and mayhaveinputeffect.

Sources https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-input https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-keybdinput https://learn.microsoft.com/en-us/cpp/build/x64-software-conventions?view=msvc-170 https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.layoutkind?view=net-10.0 . PriorV7notreadyABIreplacedbythisexplicitdeclaration.
