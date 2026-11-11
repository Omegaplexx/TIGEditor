@echo off
rem Build with the compiler that ships with Windows itself.
rem Nothing to install: csc.exe comes with .NET Framework 4.0,
rem powershell comes with Windows.
rem
rem Text here is English on purpose. A .bat file is read by the console in
rem its own code page, and Russian text saved as UTF-8 comes out as garbage.

setlocal enabledelayedexpansion
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
	echo csc.exe not found at %CSC%
	echo Install .NET Framework 4.0 or newer.
	pause
	exit /b 1
)

if not exist build mkdir build

rem Build patch.gz from the hex dump in src\GameCodePatchData.cs
"%CSC%" /nologo /target:exe /platform:x86 /langversion:4 /define:DUMP ^
	/out:build\dump.exe src\GameCodePatchData.cs || goto :fail
build\dump.exe build\patch.bin || goto :fail
call :gzip build\patch.bin build\patch.gz || goto :fail
del build\dump.exe build\patch.bin 2>nul

rem Gzip the sound libraries
call :gzip lib\NVorbis.dll build\NVorbis.dll.gz || goto :fail
call :gzip lib\NLayer.dll  build\NLayer.dll.gz  || goto :fail

rem Every lang\*.xml goes into the exe as lang.<name>.xml
set LANGS=
for %%f in (lang\*.xml) do set LANGS=!LANGS! /resource:%%f,lang.%%~nxf

"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ /langversion:4 ^
	/win32manifest:app.manifest /win32icon:tex\app.ico /out:ImpossibleEditor.exe ^
	/reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ^
	/reference:lib\NVorbis.dll /reference:lib\NLayer.dll ^
	/resource:build\patch.gz,patch.gz ^
	/resource:build\NVorbis.dll.gz,NVorbis.dll.gz /resource:build\NLayer.dll.gz,NLayer.dll.gz ^
	/resource:tex\block.png,block.png /resource:tex\spike.png,spike.png /resource:tex\pit.png,pit.png ^
	%LANGS% ^
	src\*.cs

if errorlevel 1 goto :fail

echo Done: ImpossibleEditor.exe
pause
exit /b 0

:gzip
powershell -NoProfile -Command ^
	"$in=[IO.File]::ReadAllBytes('%~1');" ^
	"$out=[IO.File]::Create('%~2');" ^
	"$gz=New-Object IO.Compression.GZipStream($out,[IO.Compression.CompressionMode]::Compress);" ^
	"$gz.Write($in,0,$in.Length); $gz.Close(); $out.Close()"
exit /b %errorlevel%

:fail
echo Build failed.
pause
exit /b 1
