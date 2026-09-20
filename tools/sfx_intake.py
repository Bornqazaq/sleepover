#!/usr/bin/env python3
"""Приёмка поставки звука в проект — фаза 5.

Поставка приходит папкой с `sounds.json`: паспорт каждого звука (куда класть,
2D/3D, луп, тип загрузки) и сами WAV. Скрипт раскладывает файлы по
`Assets/_Project/Audio/**` ровно так, как сказано в паспорте, и собирает
манифесты `docs/art/<папка>-sfx.json`, из которых редактор строит библиотеки.

Зачем отдельный шаг. Поставок будет много: 18.09 приехало 110 файлов, и в том
же паспорте перечислено ещё пять пачек, которых пока нет. Раскладывать сотню
файлов мышью — это тот самый ручной шаг, на котором звук уже дважды уезжал
в долг (Duck Hunt и «Порядок банок»: файлы были, до игры не доехали).

Что скрипт НЕ делает: не трогает существующие манифесты игр и не перезаписывает
уже лежащие клипы без `--force`. Слот, который в манифесте уже есть, остаётся
как был — перецелить его на новый клип это решение прохода по игре, а не приёмки.
"""
import argparse
import json
import os
import shutil
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UNITY = os.path.join(REPO, "igruha")
MANIFESTS = os.path.join(REPO, "docs", "art")

#: Громкость слота по умолчанию. Клипы поставки нормализованы по пику,
#: так что единица — законное значение, а не «погромче на всякий случай».
DEFAULT_VOLUME = 1.0


def manifest_path(target_folder):
    """Путь манифеста этой папки.

    Сначала ищем среди уже лежащих: манифест опознаётся по `output_folder`, а не
    по имени файла. Иначе рядом с `cans-order-sfx.json` появляется второй
    манифест `cansorder-sfx.json` на ту же папку, и сборщик библиотек затирает
    одну другой — поймано на первой же приёмке.
    """
    for name in sorted(os.listdir(MANIFESTS)) if os.path.isdir(MANIFESTS) else []:
        if not name.endswith("-sfx.json"):
            continue
        path = os.path.join(MANIFESTS, name)
        try:
            with open(path, encoding="utf-8") as f:
                if json.load(f).get("output_folder") == target_folder:
                    return path
        except (ValueError, OSError):
            continue

    tail = target_folder.replace("\\", "/").split("_Project/Audio/", 1)[-1]
    return os.path.join(MANIFESTS, tail.replace("/", "-").lower() + "-sfx.json")


def load_delivery(folder):
    passport = os.path.join(folder, "sounds.json")
    if not os.path.exists(passport):
        sys.exit(f"В поставке нет sounds.json: {passport}")
    with open(passport, encoding="utf-8") as f:
        return json.load(f)


def copy_files(delivery, sound, force):
    """Копирует файлы одного слота. Возвращает (скопировано, пропущено, потеряно)."""
    target = os.path.join(UNITY, sound["unity_target_folder"].replace("/", os.sep))
    os.makedirs(target, exist_ok=True)

    copied = skipped = missing = 0
    for rel in sound["files"]:
        src = os.path.join(delivery, rel.replace("/", os.sep))
        if not os.path.exists(src):
            print(f"  НЕТ ФАЙЛА: {rel}")
            missing += 1
            continue
        dst = os.path.join(target, os.path.basename(rel))
        if os.path.exists(dst) and not force:
            skipped += 1
            continue
        shutil.copy2(src, dst)
        copied += 1
    return copied, skipped, missing


def slot_entry(sound):
    """Слот манифеста. `file` — базовое имя, варианты ищутся как `<file>_NN.wav`."""
    base = os.path.basename(sound["files"][0])[:-4]
    if sound["variations"] > 1 or base.endswith(tuple(f"_{i:02d}" for i in range(1, 100))):
        base = base.rsplit("_", 1)[0]
    return {
        "id": sound["id"],
        "file": base,
        "loop": bool(sound.get("loop")),
        "spatial": sound.get("spatial") == "3D",
        "volume": DEFAULT_VOLUME,
        "event": sound.get("event", ""),
        "when": sound.get("when", ""),
    }


def write_manifests(by_folder, force):
    """Пишет манифест на папку. Существующие слоты не трогает — только дополняет."""
    os.makedirs(MANIFESTS, exist_ok=True)
    for folder, sounds in sorted(by_folder.items()):
        path = manifest_path(folder)
        if os.path.exists(path):
            with open(path, encoding="utf-8") as f:
                manifest = json.load(f)
        else:
            manifest = {"game": folder.rsplit("/", 1)[-1],
                        "source": "поставка звука, sounds.json",
                        "output_folder": folder,
                        "slots": []}

        known = {s.get("id") for s in manifest.get("slots", [])}
        added = [slot_entry(s) for s in sounds if s["id"] not in known]
        if not added and not force:
            print(f"  манифест {os.path.basename(path)}: новых слотов нет")
            continue

        manifest.setdefault("slots", []).extend(added)
        with open(path, "w", encoding="utf-8") as f:
            json.dump(manifest, f, ensure_ascii=False, indent=2)
            f.write("\n")
        print(f"  манифест {os.path.basename(path)}: +{len(added)} слотов, всего {len(manifest['slots'])}")


def main():
    parser = argparse.ArgumentParser(description="Приёмка поставки звука в Unity-проект")
    parser.add_argument("delivery", help="папка поставки с sounds.json")
    parser.add_argument("--force", action="store_true",
                        help="перезаписывать уже лежащие клипы")
    parser.add_argument("--dry-run", action="store_true", help="только показать, что будет сделано")
    args = parser.parse_args()

    delivery = os.path.abspath(args.delivery)
    passport = load_delivery(delivery)
    sounds = passport["sounds"]
    print(f"Поставка {os.path.basename(delivery)}: слотов {len(sounds)}, "
          f"файлов {passport.get('total_files', '?')}\n")

    by_folder = {}
    totals = [0, 0, 0]
    for sound in sounds:
        by_folder.setdefault(sound["unity_target_folder"], []).append(sound)
        if args.dry_run:
            continue
        result = copy_files(delivery, sound, args.force)
        totals = [a + b for a, b in zip(totals, result)]

    for folder, group in sorted(by_folder.items()):
        print(f"{folder}: слотов {len(group)}, файлов {sum(len(s['files']) for s in group)}")

    if args.dry_run:
        print("\nПробный прогон — ничего не скопировано.")
        return

    print(f"\nСкопировано {totals[0]}, пропущено как уже лежащее {totals[1]}, потеряно {totals[2]}")
    print("\nМанифесты:")
    write_manifests(by_folder, args.force)
    print("\nДальше: в Unity — Igruha/Арт/Собрать библиотеку звука по каждому манифесту.")


if __name__ == "__main__":
    main()
