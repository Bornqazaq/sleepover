#!/usr/bin/env python3
"""Нормализация сгенерированных SFX — обязательный шаг подфазы 4.5.

Зачем: генератор не следит за уровнем. Замер на пробе 02.09.2026 (9 клипов
CassetteAI): пики разъехались на 25 дБ — от -32.8 до -7.8 dBFS. Без выравнивания
одни звуки в игре шепчут, другие бьют по ушам, и это слышно раньше, чем их
характер.

Что делает: приводит пик каждого файла к целевому (-1 dBFS по умолчанию) и
отбраковывает то, что нормализацией не чинится, — такие клипы надо
перегенерировать, а не выравнивать:

* КЛИППИНГ в исходнике: верхушки уже срезаны, поднимать нечего.
* ТИШИНА и ГЛУХОЙ клип: пик или RMS ниже -30 dBFS. Это второй вид брака,
  и он опаснее первого, потому что выглядит починенным. Замер 02.09.2026
  (57 клипов «Дырки в стене»): пять пришли практически тишиной, до
  -54.6 dBFS. Нормализация «чинит» их, поднимая пик до -1 dBFS вместе
  с шумовой полкой, — на выходе шипение вместо звука. Поэтому такие файлы
  скрипт НЕ трогает: тихий брак слышно, поднятый — уже нет.

Команды:
    python tools/sfx_normalize.py <папка>              — нормализовать до -1 dBFS
    python tools/sfx_normalize.py <папка> --peak -3    — другой целевой пик
    python tools/sfx_normalize.py <папка> --dry-run    — только замер, без правки

Требует ffmpeg в PATH.
"""

from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

DEFAULT_PEAK_DB = -1.0
# Пик ближе этого к потолку означает, что сигнал в исходнике уже срезан.
CLIP_THRESHOLD_DB = -0.1
# Flat factor выше этого — длинные плоские участки, надёжный признак срезанных верхушек.
FLAT_FACTOR_LIMIT = 10.0
# Пик или RMS ниже этого — клип пришёл пустым. Порог снят с прогона 4.5:
# годные клипы легли в -12…-20 dBFS RMS, брак — в -34…-54.
USABLE_LEVEL_DB = -30.0


def ffmpeg_missing() -> bool:
    return shutil.which("ffmpeg") is None


def measure(path: Path) -> tuple[float, float, float]:
    """Возвращает (пик в dBFS, flat factor, RMS в dBFS) одного файла."""
    out = subprocess.run(
        ["ffmpeg", "-hide_banner", "-i", str(path), "-af", "volumedetect,astats", "-f", "null", "-"],
        capture_output=True, text=True,
    ).stderr

    peak_match = re.search(r"max_volume:\s*(-?[\d.]+) dB", out)
    peak = float(peak_match.group(1)) if peak_match else 0.0

    flats = [float(m) for m in re.findall(r"Flat factor:\s*([\d.]+)", out)]

    # astats печатает RMS по каналу и общий; берём худший — брак хотя бы в одном
    # канале остаётся браком.
    rms_values = [float(m) for m in re.findall(r"RMS level dB:\s*(-?[\d.]+)", out)]
    rms = max(rms_values) if rms_values else -99.0

    return peak, max(flats) if flats else 0.0, rms


def apply_gain(path: Path, gain_db: float) -> None:
    tmp = path.with_name("tmp_" + path.name)
    subprocess.run(
        ["ffmpeg", "-hide_banner", "-loglevel", "error", "-i", str(path),
         "-af", f"volume={gain_db}dB", "-c:a", "pcm_s16le", str(tmp)],
        check=True,
    )
    tmp.replace(path)


def main() -> int:
    parser = argparse.ArgumentParser(description="Нормализация SFX по пику.")
    parser.add_argument("folder", help="папка с .wav")
    parser.add_argument("--peak", type=float, default=DEFAULT_PEAK_DB, help="целевой пик в dBFS")
    parser.add_argument("--dry-run", action="store_true", help="только замер")
    args = parser.parse_args()

    if ffmpeg_missing():
        print("ffmpeg не найден в PATH. macOS: brew install ffmpeg, Windows: winget install ffmpeg")
        return 1

    folder = Path(args.folder)
    files = sorted(folder.glob("*.wav"))
    if not files:
        print(f"В {folder} нет .wav")
        return 1

    print(f"{'файл':<24}{'пик было':>10}{'RMS':>8}{'поправка':>10}   примечание")
    clipped = []
    empty = []
    normalized = 0

    for path in files:
        peak, flat, rms = measure(path)
        gain = round(args.peak - peak, 2)

        note = ""
        skip = False

        if peak >= CLIP_THRESHOLD_DB and flat >= FLAT_FACTOR_LIMIT:
            note = f"КЛИППИНГ в исходнике (flat {flat:.1f}) — перегенерировать"
            clipped.append(path.name)
        elif peak < USABLE_LEVEL_DB or rms < USABLE_LEVEL_DB:
            what = "ТИШИНА" if peak < USABLE_LEVEL_DB else "ГЛУХОЙ"
            note = f"{what} (пик {peak:.1f}, RMS {rms:.1f}) — перегенерировать, НЕ поднят"
            empty.append(path.name)
            skip = True

        if not args.dry_run and not skip:
            apply_gain(path, gain)
            normalized += 1

        shown = "  —  " if skip else f"{gain:>+10.1f}"
        print(f"{path.name:<24}{peak:>9.1f}{rms:>8.1f}{shown:>10}   {note}")

    print(f"\nФайлов: {len(files)}, нормализовано: {normalized}, целевой пик {args.peak} dBFS"
          + (" (dry-run, файлы не тронуты)" if args.dry_run else ""))
    if clipped:
        print("Перегенерировать из-за клиппинга: " + ", ".join(clipped))
    if empty:
        print("Перегенерировать из-за пустоты (подъём только поднял бы шум): "
              + ", ".join(empty))
    return 0


if __name__ == "__main__":
    sys.exit(main())
