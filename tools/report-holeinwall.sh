#!/usr/bin/env bash
#
# Сличение итогов «Дырки в стене» между процессами стенда.
#
# Зачем отдельный скрипт. Приёмка фазы 3 проверяет не «игра не упала», а
# «все увидели один и тот же исход»: вердикт здесь парный и стоит места
# в таблице, и разойтись он может молча — у каждой машины своя правдоподобная
# картинка. Восемь логов глазами не сверить, поэтому сверяет машина.
#
# Две строки, и мерятся они в разные моменты нарочно:
#   [Дырка] итог      — таблица мест, по событию показа результатов;
#   [Дырка] роли      — снятие ролей, сразу после OnRoundEnded.
# Слить их в одну нельзя: у клиента места приезжают Rpc раньше смены фазы,
# то есть раньше снятия ролей, и общая строка врала бы на каждом прогоне.
#
# Использование:
#   tools/report-holeinwall.sh [каталог_логов]
#
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOGS="${1:-$ROOT/igruha/Builds/Autotest/logs/latest}"

if [[ ! -d "$LOGS" ]]; then
    echo "Нет каталога логов: $LOGS" >&2
    exit 1
fi

echo "Логи: $LOGS"
echo
fail=0

echo "-- Исключения --"
for f in "$LOGS"/*.log; do
    n=$(grep -c -E "Exception|NullReference|Assertion failed" "$f")
    printf '  %-14s %s\n' "$(basename "$f" .log)" "$n"
    if (( n > 0 )); then fail=1; fi
done
echo

echo "-- Итоговая таблица --"
first=""
for f in "$LOGS"/*.log; do
    name=$(basename "$f" .log)
    line=$(grep -h "\[Дырка\] итог" "$f" | tail -1)

    if [[ -z "$line" ]]; then
        printf '  %-14s [!] строки итога нет\n' "$name"
        fail=1
        continue
    fi

    printf '  %-14s %s\n' "$name" "$line"

    if [[ -z "$first" ]]; then
        first="$line"
    elif [[ "$line" != "$first" ]]; then
        printf '  %-14s [!] расходится с первой машиной\n' "$name"
        fail=1
    fi
done
echo

echo "-- Роли после раунда --"
for f in "$LOGS"/*.log; do
    name=$(basename "$f" .log)
    line=$(grep -h "\[Дырка\] роли" "$f" | tail -1)

    if [[ -z "$line" ]]; then
        printf '  %-14s [!] строки про роли нет\n' "$name"
        fail=1
        continue
    fi

    printf '  %-14s %s\n' "$name" "$line"

    # «сняты у N из M»: годится только N == M.
    released=$(sed -n 's/.*сняты у \([0-9]*\) из \([0-9]*\).*/\1/p' <<<"$line")
    total=$(sed -n 's/.*сняты у \([0-9]*\) из \([0-9]*\).*/\2/p' <<<"$line")

    if [[ -z "$released" || "$released" != "$total" ]]; then
        printf '  %-14s [!] роль уезжает в хаб\n' "$name"
        fail=1
    fi
done
echo

if (( fail )); then
    echo "[X] ПРИЁМКА НЕ ПРОЙДЕНА"
    exit 1
fi

echo "[OK] исход совпал у всех, ролей не осталось, исключений нет"
