@echo off
setlocal
powershell.exe -NoLogo -NoProfile -File "%~dp0scripts\Build-Diagnostics.ps1" %*
exit /b %ERRORLEVEL%

