@echo off
chcp 65001 >nul
title Komnata - CLIENT (адрес вшит)
cd /d "%~dp0"

if not exist "sleepover.exe" (
  echo [ОШИБКА] Этот файл лежит не в той папке.
  echo Положи его РЯДОМ с sleepover.exe и запусти оттуда.
  pause
  exit /b 1
)

echo ============================================
echo  Подключаюсь к хосту 100.98.180.3 порт 7777
echo  Адрес вшит в этот файл, host.txt не читается
echo ============================================
echo.
echo Если Windows спросит про брандмауэр - разрешить.
echo.

sleepover.exe --client --host 100.98.180.3 --port 7777

echo.
echo Игра закрыта. Окно можно закрывать.
pause
