#!/usr/bin/env bash
#
# Автопрогон мини-игры на нескольких процессах одной машины.
#
# Зачем. Живьём восьмерых собрать можно раз в неделю, а проверять сеть надо
# каждый день. Редактор восьмерых не рисует (2 кадра за минуту, модели
# ~1 млн вершин), поэтому хост — билд, а клиенты — headless: они вообще
# ничего не рисуют, и долг с моделями им не мешает.
#
# Чего этот стенд НЕ проверяет и проверить не может: разный пинг и потери,
# Windows-билд, и всё, что живёт «по машине на игрока». Это остаётся живой
# катке.
#
# Использование:
#   tools/autorun-exam.sh [всего_участников] [очередь_игр]
#   tools/autorun-exam.sh 8 Exam
#   tools/autorun-exam.sh 8 Exam,BelieveOrNot
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/igruha/Builds/Autotest/sleepover.app/Contents/MacOS/sleepover"

PLAYERS="${1:-8}"
GAMES="${2:-Exam}"
HOST_ADDRESS="${HOST_ADDRESS:-127.0.0.1}"

if [[ ! -x "$APP" ]]; then
    echo "Нет тестового билда: $APP" >&2
    echo "Собрать отдельно от раздаточного — Builds/Autotest, не Builds/macOS." >&2
    exit 1
fi

if (( PLAYERS < 2 || PLAYERS > 8 )); then
    echo "Участников должно быть от 2 до 8, а не $PLAYERS" >&2
    exit 1
fi

RUN="$(date +%Y%m%d-%H%M%S)"
LOGS="$ROOT/igruha/Builds/Autotest/logs/$RUN"
mkdir -p "$LOGS"
ln -sfn "$LOGS" "$ROOT/igruha/Builds/Autotest/logs/latest"

# Прошлый прогон мог не дожить до конца: висящий хост держит порт 7777,
# и новый молча не поднимется.
pkill -f "Builds/Autotest/sleepover.app" 2>/dev/null || true
sleep 1

PIDS="$LOGS/pids.txt"
: > "$PIDS"

# Хост в окне: с него снимаются скриншоты ключевых фаз — картинку
# на шестерых и восьмерых иначе не увидеть никак. Именно в ОКНЕ, а не
# на весь экран: полноэкранная игра на macOS уезжает в отдельный Space,
# и screencapture снимает не её, а обои рабочего стола.
#
# Вывод в /dev/null: пишет игра всё равно в -logFile, а незакрытый
# stdout держал бы вызвавшую оболочку до конца прогона.
"$APP" --autostart "$GAMES" --wait-players "$PLAYERS" --bot \
       -screen-fullscreen 0 -screen-width 1280 -screen-height 720 \
       -logFile "$LOGS/host.log" >/dev/null 2>&1 &
echo "host $! 0" >> "$PIDS"
echo "🟢 хост поднят, лог $LOGS/host.log"

# Хосту нужно поднять сервер и загрузить хаб раньше, чем постучится первый.
sleep 6

# Клиенты стартуют по одному с паузой: NGO раздаёт clientId в порядке
# подключения, и без паузы соответствие «процесс → id» разъезжается,
# а без него не адресовать дисконнект нужному участнику.
for (( i = 1; i < PLAYERS; i++ )); do
    "$APP" --client --host "$HOST_ADDRESS" --bot \
           -batchmode -nographics \
           -logFile "$LOGS/client-$i.log" >/dev/null 2>&1 &
    echo "client-$i $! $i" >> "$PIDS"
    echo "   клиент $i (ожидаемый id=$i), лог $LOGS/client-$i.log"
    sleep 1.5
done

echo
echo "Логи прогона:  $LOGS"
echo "Процессы:      $PIDS"
echo "Остановить:    pkill -f Builds/Autotest/sleepover.app"
