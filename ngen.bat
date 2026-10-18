@echo off
rem Optional. Precompiles the editor to native code once, after which it
rem starts noticeably faster. Needs administrator rights.
rem To undo: ngen uninstall ImpossibleEditor.exe

set NGEN=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\ngen.exe
if not exist "%NGEN%" (
    echo ngen.exe not found
    pause
    exit /b 1
)
"%NGEN%" install "%~dp0ImpossibleEditor.exe"
pause
