@echo off
setlocal
cd /d "%~dp0"
title s2 loader - build
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [X] csc.exe not found. Install .NET Framework 4.8 and try again.
  pause
  exit /b 1
)
echo  ==== s2 loader ====
echo  building s2loader.exe ...
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:s2loader.exe ^
  /win32icon:assets\app.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  /resource:assets\bg1.jpg,S2.bg1.jpg ^
  /resource:assets\bg2.jpg,S2.bg2.jpg ^
  /resource:assets\card.png,S2.card.png ^
  /resource:assets\paper.png,S2.paper.png ^
  /resource:assets\maroon.png,S2.maroon.png ^
  /resource:assets\band.png,S2.band.png ^
  /resource:assets\logo.png,S2.logo.png ^
  /resource:assets\HelveticaPresto.ttf,S2.HelveticaPresto.ttf ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo [X] build failed - send a screenshot of this window
  pause
  exit /b 1
)
echo  [OK] s2loader.exe ready - it is a single file, the rest of this folder is not needed to run it
start "" "%~dp0s2loader.exe"
