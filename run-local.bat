@echo off
REM Test RankBot on your own computer before putting it on GitHub.
REM
REM 1. Make a file next to this one called   apikey.txt
REM 2. Paste your Open Cloud API key into it and save.
REM 3. Double-click this file.
REM
REM apikey.txt is blocked by .gitignore, so it can never be uploaded by accident.

cd /d "%~dp0"

if not exist apikey.txt (
  echo.
  echo   Missing apikey.txt
  echo.
  echo   Make a file called apikey.txt in this folder and paste your
  echo   Roblox Open Cloud API key into it. Then run this again.
  echo.
  pause
  exit /b 1
)

set /p ROBLOX_API_KEY=<apikey.txt

node rankbot.js
echo.
pause
