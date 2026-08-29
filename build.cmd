@echo off
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo ERROR: Could not find the in-box C# compiler ^(csc.exe^).
  echo Expected at %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
  exit /b 1
)

echo Building ScreenSwap.exe ...
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ /warn:4 ^
  /out:ScreenSwap.exe ^
  /win32manifest:app.manifest ^
  /reference:System.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  ScreenSwap.cs

if errorlevel 1 (
  echo.
  echo BUILD FAILED.
  exit /b 1
)

echo.
echo Built: %~dp0ScreenSwap.exe
endlocal
