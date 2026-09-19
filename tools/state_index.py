#!/usr/bin/env python3
"""Собрать указатель по архиву истории (docs/history/README.md).

Зачем. Архив — журнал на тысячи строк, и обычный grep по нему возвращает
чужие абзацы целиком: записи склеены длинными строками, одно совпадение
тащит десятки килобайт. Указатель даёт номер строки на каждый раздел,
чтобы читать точный кусок (`sed -n 'A,Bp'`), а не грепать вслепую.

Запуск после переноса свежих записей в архив:
    python3 tools/state_index.py
"""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ARCHIVE = ROOT / "docs/history/STATE-archive.md"
INDEX = ROOT / "docs/history/README.md"

HEADER = re.compile(r"^(#{2,3}) +(.*)$")
# Дата в заголовке: 24.08, 17.09.2026, 2026-09-18, 23–24.08
DATE = re.compile(r"(\d{4}-\d{2}-\d{2})|(\d{1,2}[.–—-]\d{1,2}\.\d{2}(?:\.\d{4})?)|(\d{1,2}\.\d{2})\b")


def sections(lines):
    found = []
    for i, line in enumerate(lines, start=1):
        m = HEADER.match(line)
        if m:
            found.append((i, len(m.group(1)), m.group(2).strip()))
    out = []
    for idx, (line_no, level, title) in enumerate(found):
        end = found[idx + 1][0] - 1 if idx + 1 < len(found) else len(lines)
        out.append((line_no, end, level, title))
    return out


def date_of(title):
    m = DATE.search(title)
    return m.group(0) if m else ""


def main():
    lines = ARCHIVE.read_text(encoding="utf-8").split("\n")
    items = sections(lines)
    chars = sum(len(x) + 1 for x in lines)

    out = [
        "# Архив истории проекта — указатель",
        "",
        "> Журнал проходов, перенесённый из `STATE.md`. **Читать целиком не надо.**",
        "> Текущее состояние проекта — в корневом `STATE.md`.",
        "",
        "## Как пользоваться",
        "",
        "Найди раздел в таблице ниже и прочитай ровно его диапазон строк:",
        "",
        "```bash",
        "sed -n '1390,1473p' docs/history/STATE-archive.md",
        "```",
        "",
        "Сплошной `grep` по архиву даёт мусорную выдачу: записи склеены длинными",
        "строками, и одно совпадение вытаскивает соседние темы на десятки килобайт.",
        "Ищи по этому указателю, потом читай диапазон.",
        "",
        "Архив: `docs/history/STATE-archive.md` — "
        f"{len(lines):,} строк".replace(",", " ") + ", "
        + f"{chars:,} символов".replace(",", " ")
        + f", {len(items)} разделов.",
        "",
        "## Разделы",
        "",
        "| Строки | Дата | Раздел |",
        "|---|---|---|",
    ]
    for start, end, level, title in items:
        indent = "" if level == 2 else "&nbsp;&nbsp;"
        clean = title.replace("|", "\\|")
        out.append(f"| `{start}–{end}` | {date_of(title)} | {indent}{clean} |")
    out.append("")
    INDEX.write_text("\n".join(out), encoding="utf-8")
    print(f"{INDEX.relative_to(ROOT)}: {len(items)} разделов, архив {len(lines)} строк")


if __name__ == "__main__":
    main()
