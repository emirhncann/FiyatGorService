@echo off
setlocal enabledelayedexpansion

REM ============================================================
REM  Fiyatgor Servis - Kurulum Betigi
REM  Bu dosyayi FiyatgorService.exe ile AYNI klasore koy ve
REM  saga tiklayip "Yonetici olarak calistir" ile baslat.
REM ============================================================

REM --- Yonetici kontrolu ---
net session >nul 2>&1
if %errorLevel% NEQ 0 (
    echo HATA: Bu betik yonetici olarak calistirilmali.
    echo Dosyaya sag tiklayip "Yonetici olarak calistir" secin.
    pause
    exit /b 1
)

set SERVICE_NAME=FiyatgorService
set SERVICE_DISPLAY=Fiyatgor Servis
set EXE_NAME=FiyatgorService.exe
set SCRIPT_DIR=%~dp0
set EXE_PATH=%SCRIPT_DIR%%EXE_NAME%

REM --- exe var mi kontrolu ---
if not exist "%EXE_PATH%" (
    echo HATA: %EXE_PATH% bulunamadi.
    echo Bu betigi .exe dosyasiyla ayni klasore koydugundan emin ol.
    pause
    exit /b 1
)

REM --- Port sor ---
set /p PORT="Servisin calisacagi port numarasini gir (varsayilan 5080 icin Enter'a bas): "
if "%PORT%"=="" set PORT=5080

echo.
echo Kurulum bilgileri:
echo   Servis adi   : %SERVICE_NAME%
echo   Exe yolu     : %EXE_PATH%
echo   Port         : %PORT%
echo.

REM --- Zaten kurulu mu kontrolu ---
sc query %SERVICE_NAME% >nul 2>&1
if %errorLevel% EQU 0 (
    echo Bu servis zaten kurulu. Once uninstall-service.bat ile kaldirip tekrar dene.
    pause
    exit /b 1
)

REM --- Servisi olustur ---
echo Servis olusturuluyor...
sc create %SERVICE_NAME% binPath= "\"%EXE_PATH%\"" start= auto DisplayName= "%SERVICE_DISPLAY%"
if %errorLevel% NEQ 0 (
    echo HATA: Servis olusturulamadi.
    pause
    exit /b 1
)

sc description %SERVICE_NAME% "Fiyatgor fiyat sorgulama servisi"

REM --- Cokme sonrasi otomatik yeniden baslama ---
echo Otomatik yeniden baslama ayarlaniyor...
sc failure %SERVICE_NAME% reset= 86400 actions= restart/5000/restart/5000/restart/5000

REM --- Guvenlik duvari kurali ---
echo Guvenlik duvari kurali ekleniyor (port %PORT%)...
netsh advfirewall firewall show rule name="Fiyatgor Servis" >nul 2>&1
if %errorLevel% EQU 0 (
    netsh advfirewall firewall delete rule name="Fiyatgor Servis" >nul 2>&1
)
netsh advfirewall firewall add rule name="Fiyatgor Servis" dir=in action=allow protocol=TCP localport=%PORT%

REM --- Servisi baslat ---
echo Servis baslatiliyor...
sc start %SERVICE_NAME%

echo.
echo ============================================================
echo  Kurulum tamamlandi.
echo  Servis durumunu kontrol etmek icin: sc query %SERVICE_NAME%
echo  Tarayicidan test: http://localhost:%PORT%/api/health
echo ============================================================
echo.
pause
