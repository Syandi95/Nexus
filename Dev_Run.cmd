@echo off
setlocal
title NEXUS Dev Build ^& Run

:: 1. Cari lokasi SDK dotnet.exe yang aktif (prioritaskan %LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe)
set "DOTNET_SDK=%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe"
if not exist "%DOTNET_SDK%" (
    if exist "C:\Program Files\dotnet\sdk" (
        set "DOTNET_SDK=C:\Program Files\dotnet\dotnet.exe"
    ) else if exist "%LOCALAPPDATA%\Temp\dotnet8\dotnet.exe" (
        set "DOTNET_SDK=%LOCALAPPDATA%\Temp\dotnet8\dotnet.exe"
    ) else (
        set "DOTNET_SDK=dotnet"
    )
)

set "DOTNET_RUNTIME=C:\Program Files\dotnet\dotnet.exe"
if not exist "%DOTNET_RUNTIME%" set "DOTNET_RUNTIME=%DOTNET_SDK%"

set "APP_CSPROJ=%~dp0Nexus-CSharp\src\03_App\Nexus.App\Nexus.App.csproj"
set "APP_DLL=%~dp0Nexus-CSharp\src\03_App\Nexus.App\bin\Debug\net8.0-windows\Nexus.App.dll"
set "APP_EXE=%~dp0Nexus-CSharp\src\03_App\Nexus.App\bin\Debug\net8.0-windows\Nexus.App.exe"

:: 2. Tutup instance Nexus.App lama agar tidak mengunci file saat build
taskkill /F /IM "Nexus.App.exe" >nul 2>&1

echo [NEXUS DEV] Menggunakan compiler: "%DOTNET_SDK%"
echo [NEXUS DEV] Mengompilasi perubahan kode terbaru...
"%DOTNET_SDK%" build "%APP_CSPROJ%" -c Debug --no-restore -m:1 -p:BuildInParallel=false

if %ERRORLEVEL% EQU 0 (
    echo [NEXUS DEV] Kompilasi berhasil! Membuka aplikasi NEXUS...
    if exist "%APP_EXE%" (
        start "" "%APP_EXE%"
    ) else (
        start "" "%DOTNET_RUNTIME%" "%APP_DLL%"
    )
) else (
    echo [NEXUS DEV] Terjadi kesalahan saat kompilasi.
    pause
)
endlocal
