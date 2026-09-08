@echo off
setlocal enabledelayedexpansion
chcp 65001 >nul
title Froststrap Multi-Threaded Builder

echo =====================================================================
echo       FROSTSTRAP LITE MULTI-THREADED BUILD SYSTEM
echo =====================================================================
echo.

echo [*] Kiem tra moi truong .NET SDK...
dotnet --version >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo [!] LOI: Khong tim thay .NET SDK tren he thong!
    echo [!] Vui long cai dat .NET SDK 8.0 tro len tai: https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)

for /f "tokens=*" %%v in ('dotnet --version') do set DOTNET_VER=%%v
echo [+] Da tim thay .NET SDK: !DOTNET_VER!
echo [+] So luong CPU Cores / Threads su dung: %NUMBER_OF_PROCESSORS% Luong
echo.

echo [*] Dang don dep sach se cac thu muc build cu...
taskkill /f /im Froststrap.exe >nul 2>&1
taskkill /f /im Bloxstrap.exe >nul 2>&1
if exist "%~dp0Release" rd /s /q "%~dp0Release"
if exist "%~dp0Bloxstrap\bin" rd /s /q "%~dp0Bloxstrap\bin"
if exist "%~dp0Bloxstrap\obj" rd /s /q "%~dp0Bloxstrap\obj"
echo [+] Da xoa sach toan bo cache va ban build cu!
echo.

set OUTPUT_DIR=%~dp0Release
if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

echo [*] Bat dau build va dong goi Release Single-File...
set START_TIME=%TIME%

dotnet publish "%~dp0Bloxstrap\Bloxstrap.csproj" ^
    -c Release ^
    -r win-x64 ^
    --no-self-contained ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:ContinuousIntegrationBuild=true ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    /maxcpucount:%NUMBER_OF_PROCESSORS% ^
    /p:BuildInParallel=true ^
    -o "%OUTPUT_DIR%" ^
    /verbosity:minimal

if %ERRORLEVEL% neq 0 (
    echo.
    echo =====================================================================
    echo [X] BUILD / PUBLISH THAT BAI!
    echo =====================================================================
    echo.
    pause
    exit /b %ERRORLEVEL%
)

if exist "%OUTPUT_DIR%\*.pdb" del /q "%OUTPUT_DIR%\*.pdb"

echo.
echo =====================================================================
echo [V] BUILD VA DONG GOI THANH CONG RA THU MUC RELEASE!
echo =====================================================================
echo [+] Thu muc Release : "%OUTPUT_DIR%"
echo [+] File chay chinh : "%OUTPUT_DIR%\Froststrap.exe"
echo [+] Thoi gian bat dau: %START_TIME%
echo [+] Thoi gian hoan tat: %TIME%
echo =====================================================================
echo.

choice /C YN /M "Ban co muon mo thu muc Release de chay app khong?"
if errorlevel 2 goto :END
if errorlevel 1 (
    explorer "%OUTPUT_DIR%"
)

:END
echo.
echo Hoan tat. Bam phim bat ky de thoat.
pause >nul
