@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
title Komnata - CLIENT
cd /d "%~dp0"
if not exist "sleepover.exe" (
  echo [!] sleepover.exe not found next to this file.
  pause
  exit /b 1
)
if not exist "host.txt" (
  echo [!] host.txt not found. Address of the host is unknown.
  pause
  exit /b 1
)
set "HOSTIP="
for /f "usebackq tokens=* delims= " %%A in ("host.txt") do (
  set "LINE=%%A"
  if not defined HOSTIP if not "!LINE!"=="" if not "!LINE:~0,1!"=="#" set "HOSTIP=!LINE!"
)
if not defined HOSTIP (
  echo [!] host.txt has no address line.
  pause
  exit /b 1
)
if "!HOSTIP!"=="100.0.0.0" (
  echo [!] host.txt still holds the placeholder 100.0.0.0 — the organizer has not filled it in.
  pause
  exit /b 1
)
echo Подключаюсь к хосту !HOSTIP! : 7777
echo Если Windows спросит про брандмауэр — разрешить.
start "" "sleepover.exe" --client --host !HOSTIP! --port 7777
