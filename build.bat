@echo off
rem Build with the compiler that ships with Windows itself.
rem Nothing to install: csc.exe comes with .NET Framework 4.0.

setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo csc.exe not found at %CSC%
    echo Install .NET Framework 4.0 or newer.
    pause
    exit /b 1
)

"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ /langversion:4 ^
    /win32manifest:app.manifest /win32icon:tex\app.ico /out:ImpossibleEditor.exe ^
    /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ^
    /reference:lib\NVorbis.dll /reference:lib\NLayer.dll ^
    /resource:lib\patch.gz,patch.gz ^
    /resource:lib\NVorbis.dll.gz,NVorbis.dll.gz /resource:lib\NLayer.dll.gz,NLayer.dll.gz ^
    /resource:tex\block.png,block.png /resource:tex\spike.png,spike.png /resource:tex\pit.png,pit.png ^
    /resource:lang\en.xml,en.xml ^
    src\*.cs

if errorlevel 1 (
    echo Build failed.
    pause
    exit /b 1
)

echo Done: ImpossibleEditor.exe
pause
