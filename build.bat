@echo off
REM Builds RankBotSetup.exe using the C# compiler already inside Windows.
REM Nothing to install. Just double-click this file.

cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe

if not exist "%CSC%" (
  echo Could not find the C# compiler that ships with Windows.
  pause
  exit /b 1
)

"%CSC%" /nologo /target:winexe /out:RankBotSetup.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  RankBotSetup.cs

if errorlevel 1 (
  echo.
  echo Build FAILED.
  pause
  exit /b 1
)

echo.
echo Built RankBotSetup.exe
echo Double-click it to set up RankBot.
pause
