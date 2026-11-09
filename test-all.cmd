@echo off
rem ============================================================
rem  QuickLaunch - build and test everything
rem  ASCII only on purpose: cmd.exe reads .bat as the system ANSI
rem  code page, so non-ASCII bytes get mangled (see 注意事项.md #3).
rem ============================================================
setlocal
cd /d "%~dp0"

set "CSC=E:\visual studio IDE\MSBuild\Current\Bin\Roslyn\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

echo [1/4] Building main program ...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" >nul
if not exist "%~dp0dist\QuickLaunch.exe" (
  echo [ERROR] main program build failed. Run build.ps1 directly to see errors.
  pause
  exit /b 1
)

echo [2/4] Building test tools ...
if not exist "%~dp0bin" mkdir "%~dp0bin"
"%CSC%" /nologo /target:exe /platform:anycpu /langversion:latest /out:"%~dp0bin\LayoutTest.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "%~dp0tools\LayoutTest.cs" >nul
"%CSC%" /nologo /target:exe /platform:anycpu /langversion:latest /out:"%~dp0bin\Shot.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "%~dp0tools\Shot.cs" >nul
if not exist "%~dp0bin\LayoutTest.exe" (
  echo [ERROR] test tools failed to build.
  pause
  exit /b 1
)

echo [3/4] Layout regression (5 window sizes x 3 DPI scales) ...
"%~dp0bin\LayoutTest.exe" "%~dp0dist\QuickLaunch.exe" > "%~dp0bin\layout-report.txt" 2>&1
set "LT=%ERRORLEVEL%"
if "%LT%"=="0" (echo       PASS) else (echo       FAIL - see bin\layout-report.txt)

echo [4/4] Program self-tests ...
"%~dp0dist\QuickLaunch.exe" --selftest >nul 2>&1
set "ST=%ERRORLEVEL%"
if "%ST%"=="0" (echo       interface self-test PASS) else (echo       interface self-test FAIL)

echo.
echo ============================================================
if "%LT%"=="0" if "%ST%"=="0" (
  echo   ALL GREEN - dist\QuickLaunch.exe is ready
) else (
  echo   SOMETHING FAILED - check bin\layout-report.txt and dist\logs\
)
echo ============================================================
echo.
pause
exit /b 0
