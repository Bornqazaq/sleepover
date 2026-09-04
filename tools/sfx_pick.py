#!/usr/bin/env python3
"""Предварительный отбор вариантов SFX замером — подфаза 4.5.

Зачем. Генератор даёт по нескольку вариантов на слот, и выбирает из них
геймдизайнер на слух — это единственный шаг подфазы, который не считается
числами. Но выбирать из трёх вариантов, два из которых заведомо непригодны,
он не обязан: клиппинг, тишина и рваный луп слышны, только если их сначала
найти, а найти их можно замером.

Скрипт не заменяет ухо. Он делает две вещи:

1. **Отбраковывает непригодное.** Клиппинг в исходнике, тишина, глухой клип —
   те же пороги, что у `sfx_normalize.py`, и по той же причине: нормализация
   такой брак не чинит, она его маскирует.
2. **Ставит первым лучший по форме.** Что значит «лучше», зависит от роли слота,
   и это не вкус, а арифметика:

   * **Одиночный звук** (`loop: false`) обязан иметь резкую атаку: почти вся
     его энергия — в первых долях секунды. Мерится отношением RMS первых 0.2 с
     к RMS всего файла. Урок «Дырки в стене» (STATE 3.47): короткий стинг
     сравнивали по среднему RMS и занижали его слышимость на 6 дБ — среднее по
     файлу у него ничего не значит.
   * **Луп** (`loop: true`) обязан быть ровным и без шва. Ровность мерится
     разбросом RMS по окнам, шов — разницей уровня между первыми и последними
     50 мс. Клип с нарастанием внутри лупа повторяет это нарастание каждый круг,
     и стена в «Дырке» начинала ехать в самый тихий его момент.

Что делает с файлами: ничего. Печатает таблицу и, с `--apply`, копирует
победителя из `Probe/` в папку слота под именем `<id>.wav` — забракованные
остаются в `Probe/` для приёмки на слух.

Команды:
    python tools/sfx_pick.py docs/art/<игра>-sfx.json
    python tools/sfx_pick.py docs/art/<игра>-sfx.json --apply

Требует ffmpeg в PATH.
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

# Пороги брака — те же, что у sfx_normalize.py: расходиться им нельзя.
CLIP_THRESHOLD_DB = -0.1
FLAT_FACTOR_LIMIT = 10.0
USABLE_LEVEL_DB = -30.0

# Длина «атаки» одиночного звука, сек.
ATTACK_WINDOW = 0.2

# Длина куска, по которому меряется шов лупа, сек.
SEAM_WINDOW = 0.05

# На сколько окон режется файл при замере ровности лупа.
EVENNESS_WINDOWS = 8

# Шов больше этого лечится кроссфейдом, а не перегенерацией, дБ.
SEAM_FIX_DB = 3.0

# Длина кроссфейда: доля файла, но не длиннее предела, сек.
SEAM_FADE_FRACTION = 0.15
SEAM_FADE_MAX = 0.35

# Атака ниже этого означает, что удар начинается не сразу: у клипа впереди
# тишина, и подрезать её дешевле, чем перегенерировать.
ATTACK_FIX_DB = 0.0

# Ниже какого уровня звук считается тишиной при подрезке начала, dBFS.
LEAD_SILENCE_DB = -45.0


def ffmpeg_missing() -> bool:
    return shutil.which("ffmpeg") is None


def probe(path: Path, start: float | None = None, length: float | None = None) -> tuple[float, float, float]:
    """(пик dBFS, flat factor, RMS dBFS) файла целиком или его куска."""
    cmd = ["ffmpeg", "-hide_banner"]
    if start is not None:
        cmd += ["-ss", f"{start:.3f}"]
    if length is not None:
        cmd += ["-t", f"{length:.3f}"]
    cmd += ["-i", str(path), "-af", "volumedetect,astats", "-f", "null", "-"]

    out = subprocess.run(cmd, capture_output=True, text=True).stderr

    peak_match = re.search(r"max_volume:\s*(-?[\d.]+) dB", out)
    peak = float(peak_match.group(1)) if peak_match else -99.0

    flats = [float(m) for m in re.findall(r"Flat factor:\s*([\d.]+)", out)]
    rms_values = [float(m) for m in re.findall(r"RMS level dB:\s*(-?[\d.]+)", out)]

    return peak, (max(flats) if flats else 0.0), (max(rms_values) if rms_values else -99.0)


def duration(path: Path) -> float:
    out = subprocess.run(
        ["ffprobe", "-v", "error", "-show_entries", "format=duration",
         "-of", "default=noprint_wrappers=1:nokey=1", str(path)],
        capture_output=True, text=True,
    ).stdout.strip()
    try:
        return float(out)
    except ValueError:
        return 0.0


def close_seam(path: Path) -> float | None:
    """
    Закрыть шов лупа кроссфейдом: хвост наложить на голову.

    Зачем это здесь, а не в перегенерации. Генератор не умеет делать луп: он
    делает отрезок, у которого начало и конец не совпадают, и на стыке слышен
    щелчок или провал. Перегенерация это не чинит — она даёт другой отрезок с
    другим швом. Кроссфейд чинит: файл укорачивается на длину перехода, его
    голова затухает поверх затухающего хвоста, и круг замыкается ровно.

    Возвращает новую длину файла или None, если файл слишком короток.
    """
    total = duration(path)
    fade = min(SEAM_FADE_MAX, total * SEAM_FADE_FRACTION)
    if total <= fade * 3:
        return None

    main = total - fade
    graph = (
        f"[0]atrim=0:{main:.3f},asetpts=PTS-STARTPTS,afade=t=in:st=0:d={fade:.3f}[a];"
        f"[0]atrim={main:.3f}:{total:.3f},asetpts=PTS-STARTPTS,afade=t=out:st=0:d={fade:.3f}[b];"
        f"[a][b]amix=inputs=2:duration=first:normalize=0[out]"
    )

    tmp = path.with_name("loopfix_" + path.name)
    subprocess.run(
        ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-i", str(path),
         "-filter_complex", graph, "-map", "[out]", "-c:a", "pcm_s16le", str(tmp)],
        check=True,
    )
    tmp.replace(path)
    return main


def trim_lead(path: Path) -> float | None:
    """
    Срезать тишину в начале одиночного звука.

    Зачем. Удар обязан звучать в тот кадр, в который случился. Генератор часто
    оставляет впереди полсекунды воздуха, и в игре это читается как задержка
    отклика: кирпич уже попал, а звук ещё не начался. Перегенерация лотерейна,
    подрезка — нет: началом становится первый сэмпл выше порога тишины.

    Возвращает срезанную длину в секундах или None, если резать нечего.
    """
    out = subprocess.run(
        ["ffmpeg", "-hide_banner", "-i", str(path),
         "-af", f"silencedetect=noise={LEAD_SILENCE_DB}dB:d=0.05", "-f", "null", "-"],
        capture_output=True, text=True,
    ).stderr

    # Интересует только тишина, начинающаяся с нуля: тишину в середине и в
    # хвосте трогать нельзя, она часть звука.
    starts = re.findall(r"silence_start:\s*(-?[\d.]+)", out)
    ends = re.findall(r"silence_end:\s*([\d.]+)", out)
    if not starts or not ends or float(starts[0]) > 0.02:
        return None

    lead = float(ends[0])
    total = duration(path)
    if lead <= 0.02 or total - lead < 0.15:
        return None

    tmp = path.with_name("trim_" + path.name)
    subprocess.run(
        ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-ss", f"{lead:.3f}",
         "-i", str(path), "-c:a", "pcm_s16le", str(tmp)],
        check=True,
    )
    tmp.replace(path)
    return lead


def rejection(peak: float, flat: float, rms: float) -> str | None:
    """Почему клип непригоден. Пусто — годен."""
    if peak >= CLIP_THRESHOLD_DB and flat >= FLAT_FACTOR_LIMIT:
        return "клиппинг"
    if peak < USABLE_LEVEL_DB:
        return "тишина"
    if rms < USABLE_LEVEL_DB:
        return "глухой"
    return None


def score_oneshot(path: Path, rms: float) -> float:
    """Насколько резкая атака: RMS первых 0.2 с минус RMS всего файла, дБ."""
    _, _, attack = probe(path, 0.0, ATTACK_WINDOW)
    return attack - rms


def score_loop(path: Path) -> tuple[float, float]:
    """(разброс RMS по окнам, шов) — оба в дБ, оба чем меньше, тем лучше."""
    total = duration(path)
    if total <= SEAM_WINDOW * 4:
        return 99.0, 99.0

    window = total / EVENNESS_WINDOWS
    levels = []
    for i in range(EVENNESS_WINDOWS):
        _, _, rms = probe(path, i * window, window)
        levels.append(rms)

    spread = max(levels) - min(levels)

    _, _, head = probe(path, 0.0, SEAM_WINDOW)
    _, _, tail = probe(path, max(0.0, total - SEAM_WINDOW), SEAM_WINDOW)
    seam = abs(head - tail)

    return spread, seam


def main() -> int:
    parser = argparse.ArgumentParser(description="Отбор вариантов SFX замером.")
    parser.add_argument("manifest", help="docs/art/<игра>-sfx.json")
    parser.add_argument("--probe-folder", default="Probe", help="подпапка с вариантами")
    parser.add_argument("--apply", action="store_true", help="скопировать победителей под именем слота")
    args = parser.parse_args()

    if ffmpeg_missing():
        print("ffmpeg не найден в PATH", file=sys.stderr)
        return 1

    manifest_path = Path(args.manifest)
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))

    root = manifest_path.resolve().parent.parent.parent / "igruha" / manifest["output_folder"]
    probe_dir = root / args.probe_folder

    if not probe_dir.is_dir():
        print(f"нет папки вариантов: {probe_dir}", file=sys.stderr)
        return 1

    chosen = 0
    empty = []

    for slot in manifest["slots"]:
        slot_id = slot["id"]
        is_loop = bool(slot.get("loop"))
        variants = sorted(probe_dir.glob(f"{slot_id}_v*.wav"))

        if not variants:
            empty.append(slot_id)
            print(f"{slot_id:<16} вариантов нет")
            continue

        rows = []
        for path in variants:
            peak, flat, rms = probe(path)
            why = rejection(peak, flat, rms)
            if why:
                rows.append((path, None, f"брак: {why}"))
                continue

            if is_loop:
                spread, seam = score_loop(path)
                # Ровность важнее шва: рваный по громкости луп слышно всё время,
                # а шов — раз в круг.
                rank = spread * 2.0 + seam
                note = f"разброс {spread:5.1f} дБ, шов {seam:4.1f} дБ"
            else:
                attack = score_oneshot(path, rms)
                rank = -attack
                note = f"атака {attack:+5.1f} дБ над средним"

            rows.append((path, rank, note))

        good = [r for r in rows if r[1] is not None]
        good.sort(key=lambda r: r[1])

        print(f"\n{slot_id}  ({'луп' if is_loop else 'одиночный'})")
        for path, rank, note in rows:
            mark = "  "
            if good and path == good[0][0]:
                mark = "→ "
            print(f"  {mark}{path.name:<24} {note}")

        if not good:
            empty.append(slot_id)
            continue

        if args.apply:
            target = root / f"{slot_id}.wav"
            shutil.copyfile(good[0][0], target)
            chosen += 1

            if is_loop:
                _, seam_before = score_loop(target)
                if seam_before > SEAM_FIX_DB and close_seam(target) is not None:
                    _, seam_after = score_loop(target)
                    print(f"     шов закрыт кроссфейдом: {seam_before:.1f} → {seam_after:.1f} дБ")
            else:
                _, _, rms_before = probe(target)
                attack_before = score_oneshot(target, rms_before)
                if attack_before < ATTACK_FIX_DB:
                    lead = trim_lead(target)
                    if lead is not None:
                        _, _, rms_after = probe(target)
                        attack_after = score_oneshot(target, rms_after)
                        print(f"     срезано {lead:.2f} с тишины в начале: "
                              f"атака {attack_before:+.1f} → {attack_after:+.1f} дБ")

    print(f"\nвыбрано {chosen} из {len(manifest['slots'])}")
    if empty:
        print("без годных вариантов, нужна перегенерация: " + ", ".join(empty))

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
