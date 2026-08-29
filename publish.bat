@echo off
setlocal

set MSBUILD="C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\amd64\MSBuild.exe"

echo Cleaning runtime folder...
if exist runtime rmdir /s /q runtime

echo Publishing CPReboaMonitorLauncher...
dotnet publish "source\CPReboaMonitorLauncher\CPReboaMonitorLauncher\CPReboaMonitorLauncher.csproj" -c Release -o "runtime\CPReboaMonitorLauncher"
if errorlevel 1 goto :fail

echo Publishing VSCaptureMP...
%MSBUILD% "source\VSCaptureMP\VSCaptureMP\VSCaptureMP\VSCaptureMP.csproj" /p:Configuration=Release /p:OutDir=%CD%\runtime\tools\VSCaptureMP\
if errorlevel 1 goto :fail

echo Publishing Masimo_Root...
%MSBUILD% "source\Masimo_Root\Masimo_Root\Masimo_Root.csproj" /p:Configuration=Release /p:OutDir=%CD%\runtime\tools\Masimo_Root\
if errorlevel 1 goto :fail

echo Publishing MicroRecording...
dotnet publish "source\MicroRecording\MicroRecording\MicroRecording.csproj" -c Release -o "runtime\tools\MicroRecording"
if errorlevel 1 goto :fail

echo.
echo Publish complete.

echo Creating desktop shortcut...

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
"$s=(New-Object -COM WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Desktop') + '\CPReboa Launcher.lnk'); ^
$s.TargetPath='%CD%\runtime\CPReboaMonitorLauncher\CPReboaMonitorLauncher.exe'; ^
$s.WorkingDirectory='%CD%\runtime\CPReboaMonitorLauncher'; ^
$s.IconLocation='C:\dev\CPReboa_Server\source\CPReboaMonitorLauncher\CPReboaMonitorLauncher\CPReboaLogo.ico,0'; ^
$s.Save()"

echo Shortcut created on desktop.

pause
goto :eof

:fail
echo.
echo Publish failed.
pause
exit /b 1