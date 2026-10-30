@echo off
setlocal
rem Usage: Publish.bat [WindowsAot^|WindowsJit]
set "profile=%~1"
if not defined profile set "profile=WindowsAot"
if /I "%profile%"=="WindowsAot" goto publish
if /I "%profile%"=="WindowsJit" goto publish
echo Unsupported profile. Use WindowsAot or WindowsJit. 1>&2
exit /b 2
:publish
if not "%~2"=="" (
    echo Usage: Publish.bat [WindowsAot^|WindowsJit] 1>&2
    exit /b 2
)
rem The helper prepares LLVM for AOT; the pubxml defines the publish settings.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\..\..\scripts\Publish-WindowsAot.ps1" -PublishProfile "%profile%"
exit /b %errorlevel%
