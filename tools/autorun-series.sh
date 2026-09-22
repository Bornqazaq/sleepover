#!/usr/bin/env bash
#
# Серия катки на нескольких процессах одной машины: очки за все игры,
# итоги раунда с очками, таблица катки и чемпион — и всё это сличается
# между хостом и клиентами дословно.
#
# Чем отличается от autorun-exam.sh: очередь играется одной серией
# (--series), как по кнопке «Полная игра» на телевизоре, а не по одной
# игре с возвратом в хаб. Стенд ждёт конца серии и сам печатает сверку.
#
# Использование:
#   tools/autorun-series.sh [всего_участников] [очередь_игр] [таймаут_с]
#   tools/autorun-series.sh 3 Stopwatch,Exam 900
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="$ROOT/igruha/Builds/Autotest/sleepover.app/Contents/MacOS/sleepover"

PLAYERS="${1:-3}"
GAMES="${2:-Stopwatch,Exam}"
TIMEOUT="${3:-900}"
HOST_ADDRESS="${HOST_ADDRESS:-127.0.0.1}"
# Дополнительные флаги болванкам всех процессов, например
# BOT_ARGS=--bot-sloppy — «Дырка в стене» встаёт как человек, примерно у дырки.
BOT_ARGS="${BOT_ARGS:-}"

if [[ ! -x "$APP" ]]; then
    echo "Нет тестового билда: $APP" >&2
    echo "Собрать: Unity -batchmode -quit -projectPath igruha -buildTarget OSXUniversal -executeMethod Igruha.EditorTools.AutotestBuild.BuildMac" >&2
    exit 1
fi

if (( PLAYERS < 2 || PLAYERS > 8 )); then
    echo "Участников должно быть от 2 до 8, а не $PLAYERS" >&2
    exit 1
fi

RUN="$(date +%Y%m%d-%H%M%S)"
LOGS="$ROOT/igruha/Builds/Autotest/logs/series-$RUN"
mkdir -p "$LOGS"
ln -sfn "$LOGS" "$ROOT/igruha/Builds/Autotest/logs/latest"

pkill -f "Builds/Autotest/sleepover.app" 2>/dev/null || true
sleep 1

pids=()
cleanup() { for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done; wait 2>/dev/null || true; }
trap cleanup EXIT INT TERM

"$APP" --autostart "$GAMES" --series --wait-players "$PLAYERS" --bot $BOT_ARGS \
       -screen-fullscreen 0 -screen-width 1280 -screen-height 720 \
       -logFile "$LOGS/host.log" >/dev/null 2>&1 &
pids+=("$!")
echo "🟢 хост поднят, лог $LOGS/host.log"
sleep 6

for (( i = 1; i < PLAYERS; i++ )); do
    "$APP" --client --host "$HOST_ADDRESS" --bot $BOT_ARGS \
           -batchmode -nographics \
           -logFile "$LOGS/client-$i.log" >/dev/null 2>&1 &
    pids+=("$!")
    echo "   клиент $i, лог $LOGS/client-$i.log"
    sleep 1.5
done

echo "Логи прогона: $LOGS"
echo "Жду конца серии (до $TIMEOUT с)…"

elapsed=0
while (( elapsed < TIMEOUT )); do
    if grep -q "PARTY SERIES COMPLETE" "$LOGS/host.log" 2>/dev/null; then
        # Таблица катки печатается после итогов последней игры на всех машинах.
        all=1
        for log in "$LOGS"/*.log; do
            grep -q "🏁 таблица катки" "$log" 2>/dev/null || all=0
        done
        if (( all == 1 )); then
            # Таблица висит finalStandingsSeconds, потом хост везёт всех в
            # хаб, где чемпиону надевают корону. Ждём и это: возврат — часть
            # серии, а корона — часть финала.
            hub_wait=0
            while (( hub_wait < 60 )); do
                crowned=1
                for log in "$LOGS"/*.log; do
                    grep -q "👑 корона надета" "$log" 2>/dev/null || crowned=0
                done
                if (( crowned == 1 )) && grep -q "очередь автопрогона кончилась" "$LOGS/host.log" 2>/dev/null; then
                    break
                fi
                sleep 5
                hub_wait=$(( hub_wait + 5 ))
            done
            sleep 3
            break
        fi
    fi
    if (( elapsed % 30 == 0 )); then
        echo "  … $elapsed с: $(grep -h -c '📊 итоги раунда' "$LOGS/host.log" 2>/dev/null || echo 0) раундов у хоста"
    fi
    sleep 5
    elapsed=$(( elapsed + 5 ))
done

echo
echo "═══ Ошибки и исключения"
for log in "$LOGS"/*.log; do
    name="$(basename "$log" .log)"
    errors=$(grep -c -E 'Exception|error CS|NullReference|❌' "$log" 2>/dev/null || true)
    printf '  %-12s ошибок: %s\n' "$name" "${errors:-0}"
done

echo
echo "═══ Серия и начисления (у хоста)"
grep -h -E 'PARTY SERIES|⭐ \[СЕРВЕР\]|📒|👑' "$LOGS/host.log" 2>/dev/null || echo "  нет"

echo
echo "═══ Возврат в хаб и корона чемпиона"
for log in "$LOGS"/*.log; do
    name="$(basename "$log" .log)"
    grep -h -o -E '🏠 HOST.*|очередь автопрогона кончилась.*|👑 корона надета.*' "$log" 2>/dev/null | sed "s/^/  $name: /"
done
for log in "$LOGS"/*.log; do
    if ! grep -q "👑 корона надета" "$log" 2>/dev/null; then
        echo "  ✗ $(basename "$log" .log): корона в хабе не надета"
        status_crown=1
    fi
done

echo
echo "═══ Итоги раундов: совпадают ли машины"
status=${status_crown:-0}
for log in "$LOGS"/*.log; do
    name="$(basename "$log" .log)"
    grep -h -o '📊 итоги раунда.*' "$log" 2>/dev/null | sed "s/^/  $name: /"
done
# Ключ игры в строке не сравниваем: он берётся у каждой машины из своей
# сцены, а сличать надо места и очки.
rounds_of() { grep -h -o '📊 итоги раунда.*' "$1" 2>/dev/null | sed -E 's/итоги раунда [^[]*\[/итоги раунда [/' || true; }
host_rounds="$(rounds_of "$LOGS/host.log")"
for log in "$LOGS"/client-*.log; do
    client_rounds="$(rounds_of "$log")"
    if [[ "$host_rounds" != "$client_rounds" ]]; then
        echo "  ✗ $(basename "$log" .log): итоги раундов отличаются от хоста"
        status=1
    fi
done

echo
echo "═══ Таблица катки: совпадают ли машины"
for log in "$LOGS"/*.log; do
    name="$(basename "$log" .log)"
    grep -h -o '🏁 таблица катки.*' "$log" 2>/dev/null | sed "s/^/  $name: /"
done
# В серии каждый раунд помечен «серия»: одиночный зачёт сюда попасть не должен.
if grep -h -o '📊 итоги раунда.*' "$LOGS/host.log" 2>/dev/null | grep -q -v 'серия\]'; then
    echo "  ✗ у хоста есть раунд вне зачёта серии"
    status=1
fi
host_table="$(grep -h -o '🏁 таблица катки.*' "$LOGS/host.log" 2>/dev/null || true)"
if [[ -z "$host_table" ]]; then
    echo "  ✗ у хоста нет таблицы катки"
    status=1
fi
for log in "$LOGS"/client-*.log; do
    client_table="$(grep -h -o '🏁 таблица катки.*' "$log" 2>/dev/null || true)"
    if [[ "$host_table" != "$client_table" ]]; then
        echo "  ✗ $(basename "$log" .log): таблица катки отличается от хоста"
        status=1
    fi
done

echo
if (( status == 0 )); then
    echo "✅ Хост и клиенты сошлись: раунды, очки, таблица катки, корона в хабе"
else
    echo "❌ Есть расхождения — см. выше"
fi
exit "$status"
