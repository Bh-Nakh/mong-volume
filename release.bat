@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
rem App name (Korean) lives in app.name so this file stays ASCII-only.
set /p APPNAME=<app.name
rem Build the exe (build.ps1 -> bin\) and make the installer (setup\<name>-Setup-<version>.exe).
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
set "VER="
for /f "usebackq delims=" %%v in (`powershell -NoProfile -ExecutionPolicy Bypass -File installer\version.ps1`) do set "VER=%%v"
if "%VER%"=="" (
  echo VERSION NOT FOUND
  exit /b 1
)
set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
"%ISCC%" /DSrcDir="%~dp0bin" /DAppVersion=%VER% installer\%APPNAME%.iss || (echo SETUP FAILED & exit /b 1)
echo RELEASE OK: setup\
exit /b 0
