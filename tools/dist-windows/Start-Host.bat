@echo off
chcp 65001 >nul
title Komnata - HOST
cd /d "%~dp0"
if not exist "sleepover.exe" (
  echo [!] sleepover.exe not found next to this file.
  pause
  exit /b 1
)
echo Запускаю ХОСТ. Порт 7777.
echo Хост поднимается ПЕРВЫМ, остальные — в пределах пары минут.
echo Если Windows спросит про брандмауэр — разрешить для частных И публичных сетей.
start "" "sleepover.exe" --port 7777
