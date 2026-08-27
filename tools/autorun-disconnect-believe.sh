#!/usr/bin/env bash
#
# Дисконнекты «Верю / не верю» на настоящих отдельных процессах.
#
# Отдельно от экзаменовского скрипта, потому что различаются не моменты,
# а роли: спека 10.4 разводит уход Знающего (кон отменён, очко никому)
# и уход Решающего (засчитано «Оставить») — и перепутать их нельзя.
#
# Кого убивать, скрипт узнаёт из лога хоста: строка «кон N: за столом
# Игрок X и Игрок Y, знает Игрок X» называет обоих поимённо. Имена вида
# «Игрок N» соответствуют clientId N-1, а pids.txt связывает id с процессом.
#
# ⚠️ NGO замечает пропажу клиента дольше 30 с (IGR-415). Уговоры длятся
# 40 с, поэтому убитый Решающий попадает в свою ветку надёжно — решать
# больше некому, и стадия досиживает до конца. Убитый Знающий может не
# успеть: живой Решающий обычно решает раньше, и тогда сработает ветка
# «ушёл после решения». Обе законны, лог скажет какая.
#
#   tools/autorun-disconnect-believe.sh [каталог_логов]
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOGS="${1:-$ROOT/igruha/Builds/Autotest/logs/latest}"
HOST_LOG="$LOGS/host.log"
PIDS="$LOGS/pids.txt"

[[ -f "$HOST_LOG" ]] || { echo "Нет лога хоста: $HOST_LOG" >&2; exit 1; }

# Убиваем ДО уговоров, а не внутри них. NGO замечает пропажу дольше 30 с
# (IGR-415), уговоры длятся 40 с и болванка решает на 5–32-й секунде: убей
# мы в уговорах — решение почти всегда успевало бы раньше отсчёта, и вместо
# проверяемой ветки срабатывала бы «ушёл после решения». Убитый в рассадке
# исчезает для сервера примерно на 31-й секунде уговоров — внутри них.
SECONDS_BEFORE_KILL=1

pid_of_id() { awk -v want="$1" '$3 == want {print $2}' "$PIDS" | head -1; }

# Строка кона → «знающий_id решающий_id». Игрок N — это clientId N-1.
seated_of_round() {
    local round="$1" line
    local deadline=$((SECONDS + 400))
    while (( SECONDS < deadline )); do
        line=$(grep -h "🎴 кон $round: за столом" "$HOST_LOG" 2>/dev/null | tail -1 || true)
        if [[ -n "$line" ]]; then
            local a b knower
            a=$(sed -E 's/.*за столом Игрок ([0-9]+) и Игрок ([0-9]+).*/\1/' <<<"$line")
            b=$(sed -E 's/.*за столом Игрок ([0-9]+) и Игрок ([0-9]+).*/\2/' <<<"$line")
            knower=$(sed -E 's/.*знает Игрок ([0-9]+).*/\1/' <<<"$line")
            local decider=$([[ "$knower" == "$a" ]] && echo "$b" || echo "$a")
            echo "$((knower - 1)) $((decider - 1))"
            return 0
        fi
        sleep 1
    done
    echo "не дождался кона $round" >&2
    return 1
}

kill_role() {
    local id="$1" role="$2"
    # id=0 — это хост. Он записан в pids.txt наравне с клиентами, и без
    # этой проверки скрипт убивал бы сервер вместо участника: проверено.
    if [[ "$id" == "0" ]]; then
        echo "   $role — это сам хост, пропускаю"
        return
    fi
    local pid; pid="$(pid_of_id "$id")"
    if [[ -z "$pid" ]]; then
        echo "   $role — процесса для id=$id нет"
        return
    fi
    if ! kill -0 "$pid" 2>/dev/null; then
        echo "   $role — процесс id=$id уже мёртв"
        return
    fi
    echo "   убиваю $role id=$id (pid $pid)"
    kill -9 "$pid" 2>/dev/null || true
}

# ── 1. Решающий уходит в уговорах ─────────────────────────────────────────
# Ветка надёжная: решать больше некому, стадия досиживает 40 с, и отсчёт
# NGO успевает истечь внутри неё. Ожидаем «засчитано „Оставить"».
read -r knower2 decider2 <<<"$(seated_of_round 2)"
echo "① кон 2: знает id=$knower2, решает id=$decider2"
sleep "$SECONDS_BEFORE_KILL"
kill_role "$decider2" "Решающего"

# ── 2. Знающий уходит в уговорах ──────────────────────────────────────────
read -r knower3 decider3 <<<"$(seated_of_round 3)"
echo "② кон 3: знает id=$knower3, решает id=$decider3"
sleep "$SECONDS_BEFORE_KILL"
kill_role "$knower3" "Знающего"

echo "Дальше смотреть tools/autorun-report.sh $LOGS"
