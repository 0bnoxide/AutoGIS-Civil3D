@echo off
setlocal
powershell.exe -NoLogo -NoProfile -File "%~dp0scripts\Uninstall-CurrentUser.ps1" %*
exit /b %ERRORLEVEL%

