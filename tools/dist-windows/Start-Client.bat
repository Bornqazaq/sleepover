@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
title Komnata - CLIENT
cd /d "%~dp0"
if not exist "sleepover.exe" (
  echo [ОШИБКА] Рядом с этим файлом нет sleepover.exe.
  echo Запускать ярлык надо ИЗ распакованной папки с игрой.
  pause
  exit /b 1
)
if not exist "host.txt" (
  echo [ОШИБКА] Рядом нет файла host.txt - адрес хоста неизвестен.
  pause
  exit /b 1
)
set "HOSTIP="
for /f "usebackq tokens=* delims= " %%A in ("host.txt") do (
  set "LINE=%%A"
  if not defined HOSTIP if not "!LINE!"=="" if not "!LINE:~0,1!"=="#" set "HOSTIP=!LINE!"
)
if not defined HOSTIP (
  echo [ОШИБКА] В host.txt нет строки с адресом.
  pause
  exit /b 1
)
if "!HOSTIP!"=="100.0.0.0" (
  echo [ОШИБКА] В host.txt осталась заглушка 100.0.0.0.
  echo Организатор не вписал адрес - запускать бесполезно, напишите ему.
  pause
  exit /b 1
)
echo Подключаюсь к хосту !HOSTIP! порт 7777
echo Если Windows спросит про брандмауэр - разрешить.
start "" "sleepover.exe" --client --host !HOSTIP! --port 7777
