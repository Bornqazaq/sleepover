#!/usr/bin/env python3
"""Нормализация сгенерированных SFX — обязательный шаг подфазы 4.5.

Зачем: генератор не следит за уровнем. Замер на пробе 02.09.2026 (9 клипов
CassetteAI): пики разъехались на 25 дБ — от -32.8 до -7.8 dBFS. Без выравнивания
одни звуки в игре шепчут, другие бьют по ушам, и это слышно раньше, чем их
характер.

Что делает: приводит пик каждого файла к целевому (-1 dBFS по умолчанию) и
предупреждает о клиппинге внутри исходника — его нормализацией уже не вылечить,
такой клип надо перегенерировать.

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


def ffmpeg_missing() -> bool:
    return shutil.which("ffmpeg") is None


def measure(path: Path) -> tuple[float, float]:
    """Возвращает (пик в dBFS, flat factor) одного файла."""
    out = subprocess.run(
        ["ffmpeg", "-hide_banner", "-i", str(path), "-af", "volumedetect,astats", "-f", "null", "-"],
        capture_output=True, text=True,
    ).stderr

    peak_match = re.search(r"max_volume:\s*(-?[\d.]+) dB", out)
    peak = float(peak_match.group(1)) if peak_match else 0.0

    flats = [float(m) for m in re.findall(r"Flat factor:\s*([\d.]+)", out)]
    return peak, max(flats) if flats else 0.0


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

    print(f"{'файл':<24}{'пик было':>10}{'поправка':>10}   примечание")
    clipped = []

    for path in files:
        peak, flat = measure(path)
        gain = round(args.peak - peak, 2)

        note = ""
        if peak >= CLIP_THRESHOLD_DB and flat >= FLAT_FACTOR_LIMIT:
            note = f"КЛИППИНГ в исходнике (flat {flat:.1f}) — перегенерировать"
            clipped.append(path.name)

        if not args.dry_run:
            apply_gain(path, gain)

        print(f"{path.name:<24}{peak:>9.1f}{gain:>+10.1f}   {note}")

    print(f"\nОбработано: {len(files)}, целевой пик {args.peak} dBFS"
          + (" (dry-run, файлы не тронуты)" if args.dry_run else ""))
    if clipped:
        print("Перегенерировать из-за клиппинга: " + ", ".join(clipped))
    return 0


if __name__ == "__main__":
    sys.exit(main())
