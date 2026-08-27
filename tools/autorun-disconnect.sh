#!/usr/bin/env bash
#
# Дисконнекты на НАСТОЯЩИХ отдельных процессах.
#
# Отдельный скрипт, потому что момент ухода важнее самого ухода: спека 10.4
# разводит два случая — Ведущий ушёл ДО показа вопроса (вопрос пропадает
# вместе с ним) и ПОСЛЕ (доигрывается, ответ уже на сервере). Убить процесс
# наугад значит проверить один из них случайно и не заметить, какой именно.
#
# Кого убивать, скрипт узнаёт из лога хоста: строка «начат вопрос N/M ...
# ведёт id=K» называет Ведущего поимённо, а pids.txt связывает id с процессом.
#
#   tools/autorun-disconnect.sh [каталог_логов]
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOGS="${1:-$ROOT/igruha/Builds/Autotest/logs/latest}"
HOST_LOG="$LOGS/host.log"
PIDS="$LOGS/pids.txt"

[[ -f "$HOST_LOG" ]] || { echo "Нет лога хоста: $HOST_LOG" >&2; exit 1; }

pid_of_id() { awk -v want="$1" '$3 == want {print $2}' "$PIDS" | head -1; }

wait_for_question() {
    local number="$1"
    local deadline=$((SECONDS + 400))
    while (( SECONDS < deadline )); do
        if grep -q "начат вопрос $number/" "$HOST_LOG"; then
            grep "начат вопрос $number/" "$HOST_LOG" | tail -1 | grep -o 'ведёт id=[0-9]*' | cut -d= -f2
            return 0
        fi
        sleep 1
    done
    echo "не дождался вопроса $number" >&2
    return 1
}

# ── 1. Ведущий уходит в фазе печати ───────────────────────────────────────
# Вопрос обязан пропасть вместе с ним, а матч — продолжиться.
host_id="$(wait_for_question 2)"
pid="$(pid_of_id "$host_id")"
if [[ -n "$pid" ]]; then
    echo "① вопрос 2 ведёт id=$host_id (pid $pid) — убиваю в фазе печати"
    kill -9 "$pid" 2>/dev/null || true
else
    echo "① вопрос 2 ведёт id=$host_id — это сам хост, пропускаю (его черёд в конце)"
fi

# ── 2. Ученик уходит в фазе выбора ────────────────────────────────────────
# Печать закрывается досрочно (болванка жмёт «Готово» через 4–9 с), дальше
# показ 3 с — к 20-й секунде идёт выбор наверняка.
host_id="$(wait_for_question 4)"
sleep 20
victim=""
while read -r _name pid id; do
    [[ "$id" == "0" || "$id" == "$host_id" ]] && continue
    kill -0 "$pid" 2>/dev/null || continue
    victim="$pid"; victim_id="$id"; break
done < "$PIDS"

if [[ -n "$victim" ]]; then
    echo "② убиваю ученика id=$victim_id (pid $victim) в фазе выбора"
    kill -9 "$victim" 2>/dev/null || true
else
    echo "② живых учеников не осталось"
fi

echo "Дальше смотреть tools/autorun-report.sh $LOGS"
