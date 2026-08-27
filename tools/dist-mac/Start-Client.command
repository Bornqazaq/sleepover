#!/bin/bash
# Комната — КЛИЕНТ. Адрес хоста берётся из host.txt рядом с этим файлом.
cd "$(dirname "$0")" || exit 1

APP="$(ls -d *.app 2>/dev/null | head -1)"
if [ -z "$APP" ]; then
  echo "[ОШИБКА] Рядом с этим файлом нет .app с игрой."
  echo "Запускать надо ИЗ распакованной папки."
  read -r -p "Enter — закрыть" _
  exit 1
fi

if [ ! -f host.txt ]; then
  echo "[ОШИБКА] Рядом нет файла host.txt — адрес хоста неизвестен."
  read -r -p "Enter — закрыть" _
  exit 1
fi

# Первая строка, которая не пустая и не комментарий.
HOSTIP="$(grep -v '^[[:space:]]*#' host.txt | grep -v '^[[:space:]]*$' | head -1 | tr -d '[:space:]')"

if [ -z "$HOSTIP" ]; then
  echo "[ОШИБКА] В host.txt нет строки с адресом."
  read -r -p "Enter — закрыть" _
  exit 1
fi

# Молчаливый коннект в никуда — худший исход, поэтому заглушка отбивается явно.
if [ "$HOSTIP" = "100.0.0.0" ]; then
  echo "[ОШИБКА] В host.txt осталась заглушка 100.0.0.0."
  echo "Организатор не вписал адрес — запускать бесполезно, напишите ему."
  read -r -p "Enter — закрыть" _
  exit 1
fi

BIN="$APP/Contents/MacOS/$(ls "$APP/Contents/MacOS" | head -1)"
if [ ! -x "$BIN" ]; then
  echo "[ОШИБКА] Внутри $APP нет исполняемого файла."
  read -r -p "Enter — закрыть" _
  exit 1
fi

xattr -dr com.apple.quarantine "$APP" 2>/dev/null

echo "Подключаюсь к хосту $HOSTIP порт 7777"
"$BIN" --client --host "$HOSTIP" --port 7777 &
