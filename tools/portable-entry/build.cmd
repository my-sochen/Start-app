@echo off
setlocal
rem Use the preinstalled Visual Studio x64 toolchain, never download a compiler.
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" exit /b 1
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSROOT=%%i"
if not defined VSROOT exit /b 1
call "%VSROOT%\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 exit /b 1
if not exist portable-build mkdir portable-build
cl /nologo /std:c17 /utf-8 /W4 /WX /O2 /MT /guard:cf /Foportable-build\launcher.obj tools\portable-entry\launcher.c /Feportable-build\Start-app.exe /link /SUBSYSTEM:WINDOWS /DYNAMICBASE /NXCOMPAT /guard:cf user32.lib
if errorlevel 1 exit /b 1
cl /nologo /std:c17 /utf-8 /W4 /WX /MT /Foportable-build\path-test.obj tests\portable-entry\path-test.c /Feportable-build\path-test.exe
if errorlevel 1 exit /b 1
cl /nologo /std:c17 /utf-8 /W4 /WX /MT /Foportable-build\probe.obj tests\portable-entry\probe.c /Feportable-build\probe.exe /link /SUBSYSTEM:WINDOWS
if errorlevel 1 exit /b 1
portable-build\path-test.exe
exit /b %errorlevel%
