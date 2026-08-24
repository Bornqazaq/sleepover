#!/bin/bash
# Комната — ХОСТ. Двойной клик по этому файлу поднимает хост на порту 7777.
cd "$(dirname "$0")" || exit 1

APP="$(ls -d *.app 2>/dev/null | head -1)"
if [ -z "$APP" ]; then
  echo "[ОШИБКА] Рядом с этим файлом нет .app с игрой."
  echo "Запускать надо ИЗ распакованной папки."
  read -r -p "Enter — закрыть" _
  exit 1
fi

BIN="$APP/Contents/MacOS/$(ls "$APP/Contents/MacOS" | head -1)"
if [ ! -x "$BIN" ]; then
  echo "[ОШИБКА] Внутри $APP нет исполняемого файла."
  read -r -p "Enter — закрыть" _
  exit 1
fi

# Гейткипер помечает всё, что приехало архивом, карантином и не даёт запустить.
xattr -dr com.apple.quarantine "$APP" 2>/dev/null

echo "Запускаю ХОСТ. Порт 7777."
echo "Хост поднимается ПЕРВЫМ, остальные — в пределах пары минут."
"$BIN" --port 7777 &
