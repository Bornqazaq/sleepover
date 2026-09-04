#!/usr/bin/env python3
"""Ставит .unitypackage распаковкой, минуя редактор.

Пакет — обычный tar.gz: каталог на GUID, внутри `asset`, `asset.meta` и
`pathname` с путём внутри проекта. Кладём файл и его `.meta` по этому пути —
GUID'ы сохраняются один в один, Unity при следующем старте просто импортирует.

Нужно, когда паки Synty ставятся на машину, где редактор закрыт: импорт из-под
MCP блокирует главный поток и отваливается по таймауту (STATE.md, раздел 3a).

    python tools/unpack-unitypackage.py igruha ~/Downloads/POLYGON_*.unitypackage

Пути вне `Assets/` не распаковываются, а печатаются списком — пакет не должен
писать мимо проекта.
"""

import os
import sys
import tarfile

ALLOWED_PREFIX = "Assets/"
ENTRY_NAMES = ("asset", "asset.meta", "pathname")


def write_entry(project, buf, stats, outside, roots):
    raw = buf.get("pathname")
    if not raw:
        return

    path = raw.split("\n")[0].strip().replace("\\", "/")
    if not path.startswith(ALLOWED_PREFIX) or ".." in path.split("/"):
        outside.add(path)
        return

    roots.add("/".join(path.split("/")[:2]))
    full = os.path.join(project, *path.split("/"))

    if "asset" in buf:
        os.makedirs(os.path.dirname(full), exist_ok=True)
        with open(full, "wb") as f:
            f.write(buf["asset"])
        stats["files"] += 1
    else:
        os.makedirs(full, exist_ok=True)
        stats["dirs"] += 1

    if "asset.meta" in buf:
        with open(full + ".meta", "wb") as f:
            f.write(buf["asset.meta"])
        stats["metas"] += 1


def unpack(project, package):
    """Каталоги одного GUID лежат в архиве подряд — пишем на смене GUID."""
    stats = {"files": 0, "dirs": 0, "metas": 0}
    outside, roots = set(), set()
    current, buf = None, {}

    with tarfile.open(package, "r:gz") as tar:
        for member in tar:
            if not member.isfile():
                continue
            parts = member.name.split("/")
            if len(parts) < 2 or parts[-1] not in ENTRY_NAMES:
                continue

            guid, name = parts[0], parts[-1]
            if guid != current:
                if current is not None:
                    write_entry(project, buf, stats, outside, roots)
                current, buf = guid, {}

            data = tar.extractfile(member).read()
            buf[name] = data.decode("utf-8") if name == "pathname" else data

    if current is not None:
        write_entry(project, buf, stats, outside, roots)

    return stats, outside, roots


def main(argv):
    if len(argv) < 3:
        print("использование: unpack-unitypackage.py <папка проекта> <пакет...>")
        return 1

    project, packages = argv[1], argv[2:]
    if not os.path.isdir(os.path.join(project, "Assets")):
        print(f"не похоже на проект Unity: {project}")
        return 1

    failed = False
    for package in packages:
        stats, outside, roots = unpack(project, package)
        print(
            f"{os.path.basename(package)}: файлов {stats['files']}, "
            f"папок {stats['dirs']}, meta {stats['metas']}",
            flush=True,
        )
        print(f"  корни: {sorted(roots)}", flush=True)
        if outside:
            failed = True
            print(f"  ВНЕ Assets/, пропущено: {sorted(outside)[:10]}", flush=True)

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
