@echo off
setlocal
powershell.exe -NoLogo -NoProfile -File "%~dp0scripts\Install-CurrentUser.ps1" %*
exit /b %ERRORLEVEL%

