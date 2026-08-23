@echo off
chcp 65001 >nul
title Komnata - HOST
cd /d "%~dp0"
if not exist "sleepover.exe" (
  echo [ОШИБКА] Рядом с этим файлом нет sleepover.exe.
  echo Запускать ярлык надо ИЗ распакованной папки с игрой.
  pause
  exit /b 1
)
echo Запускаю ХОСТ. Порт 7777.
echo Хост поднимается ПЕРВЫМ, остальные - в пределах пары минут.
echo Если Windows спросит про брандмауэр - разрешить для частных И публичных сетей.
start "" "sleepover.exe" --port 7777
