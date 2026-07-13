@echo off
setlocal enabledelayedexpansion

REM ============================================================
REM  Fiyatgor Servis - Kaldirma Betigi
REM  Sag tiklayip "Yonetici olarak calistir" ile baslat.
REM ============================================================

net session >nul 2>&1
if %errorLevel% NEQ 0 (
    echo HATA: Bu betik yonetici olarak calistirilmali.
    echo Dosyaya sag tiklayip "Yonetici olarak calistir" secin.
    pause
    exit /b 1
)

set SERVICE_NAME=FiyatgorService

echo Servis durduruluyor...
sc stop %SERVICE_NAME% >nul 2>&1

REM Servisin gercekten durmasini bekle (en fazla 10 saniye)
set /a COUNT=0
:WAIT_LOOP
sc query %SERVICE_NAME% | findstr /i "STOPPED" >nul 2>&1
if %errorLevel% EQU 0 goto STOPPED
set /a COUNT+=1
if %COUNT% GEQ 10 goto STOPPED
timeout /t 1 /nobreak >nul
goto WAIT_LOOP
:STOPPED

echo Servis siliniyor...
sc delete %SERVICE_NAME%

echo Guvenlik duvari kurali kaldiriliyor...
netsh advfirewall firewall delete rule name="Fiyatgor Servis" >nul 2>&1

echo.
echo ============================================================
echo  Kaldirma tamamlandi.
echo ============================================================
echo.
pause
