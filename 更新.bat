@echo off
chcp 936 >nul
setlocal
title 夜雀点单助手 - 更新
cd /d "%~dp0"
echo.
echo   正在检查更新（GitHub Releases）...
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0update.ps1" %*
set RC=%ERRORLEVEL%
echo.
if not "%RC%"=="0" echo   更新未完成，退出码 %RC%。按任意键关闭。
if "%RC%"=="0" echo   按任意键关闭。
pause >nul
endlocal
