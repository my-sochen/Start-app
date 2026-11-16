$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
foreach($file in @('tools/portable-entry/package.ps1','tools/portable-entry/Create-DesktopShortcut.ps1','tests/portable-entry/verify-windows.ps1','qualification/icon/Verify.ps1')){
 $tokens=$null;$errors=$null
 [void][Management.Automation.Language.Parser]::ParseFile((Resolve-Path $file).Path,[ref]$tokens,[ref]$errors)
 if($errors.Count){throw ('PARSE_STOP '+$file)}
}
& cmd /c tools\portable-entry\build.cmd
if($LASTEXITCODE -ne 0){throw 'NATIVE_BUILD_STOP'}
dotnet test StartApp.slnx -c Release *> icon-tests.txt
if($LASTEXITCODE -ne 0){throw 'TEST_STOP'}
dotnet publish src/StartApp.App/StartApp.App.csproj -c Release -r win-x64 --self-contained true -o app-payload *> icon-publish.txt
if($LASTEXITCODE -ne 0){throw 'PUBLISH_STOP'}
Copy-Item .github/release/KNOWN-LIMITS.txt app-payload/KNOWN-LIMITS.txt
'UNRELEASED ICON VALIDATION ONLY'|Set-Content app-payload/PROTOTYPE-NOTICE.txt
[void](New-Item -ItemType Directory -Path probe-payload)
Copy-Item portable-build/probe.exe probe-payload/StartApp.App.exe
& ./qualification/icon/Verify.ps1
