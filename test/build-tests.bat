@echo off
rem Builds the tests. Can be run from anywhere.

setlocal
cd /d "%~dp0.."

set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
	echo csc.exe not found at %CSC%
	exit /b 1
)

if not exist bin mkdir bin
if not exist build mkdir build
copy /y lib\NVorbis.dll bin\ >nul
copy /y lib\NLayer.dll bin\ >nul

rem Build patch.gz if not already present
if not exist build\patch.gz (
	"%CSC%" /nologo /target:exe /platform:x86 /langversion:4 /define:DUMP ^
		/out:build\dump.exe src\GameCodePatchData.cs || exit /b 1
	build\dump.exe build\patch.bin || exit /b 1
	powershell -NoProfile -Command ^
		"$in=[IO.File]::ReadAllBytes('build\patch.bin');" ^
		"$out=[IO.File]::Create('build\patch.gz');" ^
		"$gz=New-Object IO.Compression.GZipStream($out,[IO.Compression.CompressionMode]::Compress);" ^
		"$gz.Write($in,0,$in.Length); $gz.Close(); $out.Close()"
	del build\dump.exe build\patch.bin 2>nul
)

set FLAGS=/nologo /target:exe /platform:x86 /langversion:4
set REFS=/r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll ^
 /r:lib\NVorbis.dll /r:lib\NLayer.dll
set RES=/res:build\patch.gz,patch.gz

echo Game patches
"%CSC%" %FLAGS% /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll ^
	/res:build\patch.gz,patch.gz /main:PatchGuard /out:bin\PatchGuard.exe ^
	src\GamePatcher.cs src\GameCodePatch.cs src\GamePhysics.cs ^
	src\LevelFormat.cs src\Strings.cs test\PatchGuard.cs || exit /b 1

echo Level format
"%CSC%" %FLAGS% /r:System.Windows.Forms.dll /r:System.Xml.dll ^
	/out:bin\RoundTrip.exe src\LevelFormat.cs src\Strings.cs test\RoundTrip.cs || exit /b 1

echo Effect stripes
"%CSC%" %FLAGS% /r:System.Windows.Forms.dll /r:System.Xml.dll ^
	/main:EffectShift /out:bin\EffectShift.exe ^
	src\LevelFormat.cs src\Strings.cs test\EffectShift.cs || exit /b 1

echo Jump physics
"%CSC%" %FLAGS% %REFS% %RES% /main:PhysicsTest ^
	/out:bin\PhysicsTest.exe src\*.cs test\PhysicsTest.cs || exit /b 1

echo Pits: move, undo, clipboard and the atlas
"%CSC%" %FLAGS% %REFS% %RES% /main:PitTest ^
	/out:bin\PitTest.exe src\*.cs test\PitTest.cs || exit /b 1

echo Playback: player and the green start line
"%CSC%" %FLAGS% %REFS% %RES% /main:AudioTest ^
	/out:bin\AudioTest.exe src\*.cs test\AudioTest.cs || exit /b 1

echo Level sounds: recoding
"%CSC%" %FLAGS% %REFS% %RES% /main:WaveTest ^
	/out:bin\WaveTest.exe src\*.cs test\WaveTest.cs || exit /b 1

echo.
echo Done. How to run:
echo     bin\PatchGuard.exe  "path\to\game\ImpossibleGame.exe"
echo     bin\RoundTrip.exe   "folder with level files"
echo     bin\EffectShift.exe "path\to\level.dat"
echo     bin\PhysicsTest.exe
echo     bin\WaveTest.exe
echo     bin\PitTest.exe     "game_1.png" "res\game1.bin"
echo     bin\AudioTest.exe   "path\to\level\music.mp3"   (optional)
endlocal
