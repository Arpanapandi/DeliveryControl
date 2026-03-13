@echo off
:: Jalankan sebagai ADMINISTRATOR
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [ERROR] Harap jalankan script ini sebagai ADMINISTRATOR!
    echo Klik kanan DEPLOY_NOW.bat -> Run as Administrator
    pause
    exit /b 1
)

set "PUBLISH_DIR=C:\DeliveryControl\DeliveryControl\publish"
set "DEST_DIR=C:\inetpub\wwwroot\DeliveryControl_Published"
set "APP_POOL_NAME=DeliveryControl"

echo ===================================================
echo   Deploy Fix: Stock Bug + Label Validation
echo ===================================================
echo.

:: Cari App Pool yang aktif
echo Mencari Application Pool...
%systemroot%\system32\inetsrv\appcmd list apppool 2>nul | findstr /i "delivery"
if %errorLevel% neq 0 (
    echo [WARN] appcmd tidak ditemukan, coba cara alternatif...
    tasklist | findstr /i "w3wp" >nul
    if %errorLevel% equ 0 (
        echo Menghentikan semua W3WP...
        taskkill /F /IM w3wp.exe >nul 2>&1
    )
) else (
    echo Menghentikan App Pool...
    %systemroot%\system32\inetsrv\appcmd stop apppool /apppool.name:"%APP_POOL_NAME%" 2>nul
    timeout /t 3 /nobreak >nul
)

echo.
echo Menyalin file baru ke IIS...
echo Source: %PUBLISH_DIR%
echo Dest  : %DEST_DIR%
echo.

:: Copy hanya DLL yang berubah (bukan config)
robocopy "%PUBLISH_DIR%" "%DEST_DIR%" /IS /XF appsettings.Production.json web.config DeliveryControl.db* /XD logs
if %errorLevel% geq 8 (
    echo [ERROR] Robocopy gagal! Error code: %errorLevel%
    pause
    exit /b 1
)

echo.
echo Menyalakan kembali App Pool...
%systemroot%\system32\inetsrv\appcmd start apppool /apppool.name:"%APP_POOL_NAME%" 2>nul

echo.
echo ===================================================
echo   DEPLOY SELESAI! 
echo   Fix yang di-deploy:
echo   1. Bug: Stock tidak berkurang saat Preparation
echo      - FIFO fallback untuk Manual Stock Adjust 
echo        (tidak lagi dibatasi oleh waktu)
echo   2. Validasi Label harus mengandung kode VIN
echo ===================================================
echo.
pause
