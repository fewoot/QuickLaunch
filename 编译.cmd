@echo off
rem 一键启动 QuickLaunch —— 双击本文件即可编译
rem 等价于：powershell -ExecutionPolicy Bypass -File build.ps1
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
echo.
pause
