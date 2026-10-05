@echo off
setlocal

cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo .NET 8 SDK was not found.
  echo Install it from https://dotnet.microsoft.com/download/dotnet/8.0
  exit /b 1
)

if exist "artifacts\win-x64" rmdir /s /q "artifacts\win-x64"

dotnet publish "AutoHotkeyUX.Modern.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o "artifacts\win-x64"

if errorlevel 1 exit /b %errorlevel%

echo.
echo Build complete:
echo %CD%\artifacts\win-x64\AutoHotkeyUX.Modern.exe
