#!/usr/bin/env bash
#
# Сличить логи автопрогона между собой.
#
# Смысл в слове «между собой»: сборка прошла и матч доигрался — это не
# результат. Результат — что счёт, места и число вопросов совпали у всех
# машин. Расхождение хоста с клиентом иначе не поймать вовсе: у каждого
# своя картинка, и обе выглядят правдоподобно.
#
#   tools/autorun-report.sh [каталог_логов]
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOGS="${1:-$ROOT/igruha/Builds/Autotest/logs/latest}"

if [[ ! -d "$LOGS" ]]; then
    echo "Нет каталога логов: $LOGS" >&2
    exit 1
fi

echo "═══ Логи: $LOGS"
echo

echo "═══ Ошибки и исключения"
for log in "$LOGS"/*.log; do
    name="$(basename "$log" .log)"
    errors=$(grep -c -E 'Exception|error CS|NullReference|❌' "$log" 2>/dev/null || true)
    printf '  %-12s ошибок: %s\n' "$name" "${errors:-0}"
done
echo

echo "═══ Состав и роли (у хоста)"
grep -h -E 'матч на|порядок Ведущих|автопрогон [0-9]+/|«Верю / не верю»:' "$LOGS/host.log" 2>/dev/null || echo "  нет"
echo

echo "═══ Ход матча (у хоста)"
grep -h -E '\[Экзамен\] вопрос [0-9]+/|🎴 кон [0-9]+' "$LOGS/host.log" 2>/dev/null || echo "  нет"
echo

echo "═══ Начисления (у хоста)"
grep -h -E 'за раскол|🎴 места:' "$LOGS/host.log" 2>/dev/null || echo "  нет"
echo

echo "═══ Кто сколько раз в особой роли (у хоста)"
# Только строки входа в ход: строка подсчёта называет того же игрока
# второй раз, и без фильтра каждый ход считался бы за два.
{
    grep -h 'начат вопрос' "$LOGS/host.log" 2>/dev/null | grep -oh 'ведёт id=[0-9-]*'
    grep -h '🎴 кон [0-9]*:' "$LOGS/host.log" 2>/dev/null | grep -oh 'Знающий [^,]*'
} | sort | uniq -c || echo "  нет"
echo

echo "═══ Конец матча"
grep -h 'матч окончен' "$LOGS"/*.log 2>/dev/null | sort -u || echo "  нет"
echo

echo "═══ Итоговые таблицы: совпадают ли машины"
tables="$(mktemp)"
for log in "$LOGS"/*.log; do
    name="$(basename "$log" .log)"
    line=$(grep -h '📊 .* итог у' "$log" 2>/dev/null | tail -1 || true)
    if [[ -z "$line" ]]; then
        printf '  %-12s ТАБЛИЦЫ НЕТ\n' "$name"
        continue
    fi
    printf '  %-12s %s\n' "$name" "${line#*итог у }"
    # Убираем «у кого» — сравниваются только цифры.
    echo "${line#*:}" >> "$tables"
done
echo
distinct=$(sort -u "$tables" | wc -l | tr -d ' ')
machines=$(wc -l < "$tables" | tr -d ' ')
if [[ "$distinct" == "1" && "$machines" != "0" ]]; then
    echo "  ✅ все $machines машин видят одну таблицу"
else
    echo "  ❌ таблиц-вариантов: $distinct на $machines машин — состояние разъехалось"
    sort -u "$tables" | sed 's/^/     /'
fi
rm -f "$tables"
echo

echo "═══ Возврат в хаб"
grep -h -E 'возвращаю всех|🏠' "$LOGS"/*.log 2>/dev/null | sort -u || echo "  нет"
grep -c -h "Загружена сцена\|Hub" /dev/null 2>/dev/null || true
echo

echo "═══ Дисконнекты"
grep -h -E 'ушёл в фазе|ушёл после показа|ушёл в стадии|ушёл после решения|меньше двух игроков|играть не с кем' "$LOGS"/*.log 2>/dev/null | grep -v '^ *at ' | sort -u || echo "  нет"
