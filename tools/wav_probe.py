#!/usr/bin/env python3
"""Замер и нормализация WAV без ffmpeg — вспомогательный инструмент подфазы 4.5.

Зачем отдельный скрипт, когда есть `tools/sfx_pick.py`. Тот построен на ffmpeg
целиком: и замер, и обрезка тишины, и склейка петли идут через него. На части
машин команды ffmpeg в PATH нет (проверено 04.09 на ASRock), и весь отбор
встаёт. Здесь тот же замер сделан разбором PCM напрямую — стандартной
библиотекой, без единой зависимости.

Что он делает и чего не делает. Он **меряет**, **срезает тишину по краям**
и **выравнивает пик** — и всё. Ни склейки петли, ни перекодирования,
ни ресемплинга.

Обрезка тут не косметика. Генератор регулярно отдаёт полезный звук, перед
которым полсекунды тишины, и в игре такой клип звучит уже после события:
створки пошли, а удар придёт через кадр. Замер 04.09 показал у восьми файлов
из тринадцати атаку в середине длины — не потому, что звук плохой, а потому,
что перед ним пусто. Срез переднего края чинит и попадание в событие, и
измеренную форму заодно: пустое начало делает любую огибающую «растущей».

**Брак определяет пик, а не RMS.** У транзиента в двухсекундном файле девять
десятых длины — тишина, и RMS у него низкий по природе; правило «RMS ниже
−30 dBFS — брак» забраковало бы здоровый щелчок. Ниже −30 dBFS по **пику** —
это тишина, срезанные верхушки — это клиппинг, и нормализацией он не чинится.

**Форма важнее уровня.** Генератор регулярно отдаёт технически чистый файл,
который не подходит роли: ровный гул вместо нарастающей дроби, одиночный
щелчок вместо выдержанной фанфары. Поэтому каждому слоту задаётся роль, и
скрипт считает, насколько огибающая файла ей отвечает:

* `impact`  — удар: энергия в первой пятой, дальше спад;
* `rise`    — нарастание: громкость растёт к концу (барабанная дробь);
* `sustain` — выдержанный звук: энергия держится всю длину (фанфара);
* `descend` — уход: громкость падает к концу (свист падения);
* `loop`    — петля: ровная по всей длине и без шва на стыке.

Команды:
    python tools/wav_probe.py <папка или файл> [--roles docs/art/<игра>-sfx.json]
    python tools/wav_probe.py <папка> --trim --normalize -1
"""

from __future__ import annotations

import argparse
import array
import json
import math
import statistics
import sys
import wave
from pathlib import Path

# Ниже этого пика по всему файлу клип пришёл пустым.
SILENCE_PEAK_DB = -30.0

# Доля сэмплов у самого потолка, за которой верхушки считаются срезанными.
CLIP_SAMPLE_RATIO = 0.0002

# Длина окна огибающей. 25 мс — достаточно мелко для атаки и достаточно крупно,
# чтобы не ловить отдельные периоды низких частот.
WINDOW_SECONDS = 0.025

ROLES = ("impact", "rise", "sustain", "descend", "loop")


def db(value: float) -> float:
    return 20.0 * math.log10(value) if value > 1e-9 else -99.0


def read_wav(path: Path):
    """Вернуть (моно-сэмплы в -1..1, частота, пик по каналам). Поддержаны 8/16/24/32 бита PCM."""
    with wave.open(str(path), "rb") as handle:
        channels = handle.getnchannels()
        width = handle.getsampwidth()
        rate = handle.getframerate()
        raw = handle.readframes(handle.getnframes())

    if width == 1:
        data = array.array("b", bytes(byte - 128 for byte in raw))
        scale = 128.0
    elif width == 2:
        data = array.array("h")
        data.frombytes(raw)
        scale = 32768.0
    elif width == 3:
        data = array.array("i")
        for i in range(0, len(raw) - 2, 3):
            value = raw[i] | (raw[i + 1] << 8) | (raw[i + 2] << 16)
            if value & 0x800000:
                value -= 0x1000000
            data.append(value)
        scale = 8388608.0
    elif width == 4:
        data = array.array("i")
        data.frombytes(raw)
        scale = 2147483648.0
    else:
        raise ValueError(f"неподдержанная разрядность: {width * 8} бит")

    if sys.byteorder == "big":
        data.byteswap()

    # Пик считается по отдельным каналам, а огибающая — по сумме.
    # Разница не косметическая: у широкого стерео сумма тише каждого канала,
    # и пик по ней показывал −2.9 dBFS там, где на самом деле было −1.0.
    # Клиппинг живёт в канале, а не в сумме.
    channel_peak = max(abs(sample) for sample in data) / scale if data else 0.0

    if channels > 1:
        mono = [sum(data[i:i + channels]) / channels / scale
                for i in range(0, len(data) - channels + 1, channels)]
    else:
        mono = [sample / scale for sample in data]

    return mono, rate, channel_peak


def envelope(samples: list[float], rate: int) -> list[float]:
    """RMS по окнам, в дБ. Огибающая — то, по чему видно форму звука."""
    step = max(1, int(rate * WINDOW_SECONDS))
    windows = []
    for start in range(0, len(samples), step):
        chunk = samples[start:start + step]
        if not chunk:
            continue
        power = sum(value * value for value in chunk) / len(chunk)
        windows.append(db(math.sqrt(power)))
    return windows


def slope(windows: list[float]) -> float:
    """Наклон огибающей в дБ на всю длину: плюс — растёт, минус — гаснет."""
    count = len(windows)
    if count < 4:
        return 0.0

    mean_x = (count - 1) / 2.0
    mean_y = statistics.fmean(windows)
    top = sum((i - mean_x) * (windows[i] - mean_y) for i in range(count))
    bottom = sum((i - mean_x) ** 2 for i in range(count))
    return top / bottom * (count - 1) if bottom else 0.0


def measure(path: Path) -> dict:
    samples, rate, peak = read_wav(path)
    if not samples:
        return {"file": path.name, "error": "пустой файл"}

    clipped = sum(1 for value in samples if abs(value) >= 0.999)
    power = sum(value * value for value in samples) / len(samples)
    windows = envelope(samples, rate)
    loud = max(windows) if windows else -99.0
    quiet_ratio = sum(1 for value in windows if value < loud - 30.0) / max(1, len(windows))

    # Где приходит атака: первое окно, поднявшееся до трёх четвертей максимума.
    attack = 0.0
    for index, value in enumerate(windows):
        if value >= loud - 6.0:
            attack = index / max(1, len(windows) - 1)
            break

    edge = max(1, len(windows) // 20)
    head = statistics.fmean(windows[:edge])
    tail = statistics.fmean(windows[-edge:])

    return {
        "file": path.name,
        "seconds": len(samples) / rate,
        "rate": rate,
        "peak_db": db(peak),
        "rms_db": db(math.sqrt(power)),
        "clipped": clipped,
        "clip_ratio": clipped / len(samples),
        "silence_ratio": quiet_ratio,
        "attack": attack,
        "slope_db": slope(windows),
        "seam_db": abs(head - tail),
        "steadiness_db": statistics.pstdev(windows) if len(windows) > 1 else 0.0,
    }


def reject(m: dict) -> str | None:
    if m.get("error"):
        return m["error"]
    if m["peak_db"] < SILENCE_PEAK_DB:
        return f"ТИШИНА (пик {m['peak_db']:.1f} dBFS)"
    if m["clip_ratio"] > CLIP_SAMPLE_RATIO:
        return f"КЛИППИНГ ({m['clipped']} срезанных сэмплов)"
    return None


def fitness(m: dict, role: str) -> tuple[float, str]:
    """Насколько форма файла отвечает роли слота. Больше — лучше."""
    if role == "impact":
        # Наклон весит втрое: удар с ранней атакой, но растущей громкостью —
        # это не удар, а нарастание с щелчком в начале. На пробе 04.09 такой
        # файл обходил правильный колокольчик только за счёт атаки.
        score = (1.0 - m["attack"]) * 60.0 - m["slope_db"] * 3.0
        return score, f"атака на {m['attack'] * 100:.0f}% длины, наклон {m['slope_db']:+.1f} дБ"
    if role == "rise":
        return m["slope_db"], f"наклон {m['slope_db']:+.1f} дБ (нужен рост)"
    if role == "sustain":
        return -abs(m["slope_db"]) - m["silence_ratio"] * 40.0, \
            f"наклон {m['slope_db']:+.1f} дБ, тишины {m['silence_ratio'] * 100:.0f}%"
    if role == "descend":
        return -m["slope_db"], f"наклон {m['slope_db']:+.1f} дБ (нужен спад)"
    if role == "loop":
        return -m["steadiness_db"] - m["seam_db"] * 2.0 - m["silence_ratio"] * 40.0, \
            f"разброс {m['steadiness_db']:.1f} дБ, шов {m['seam_db']:.1f} дБ"
    return 0.0, ""


def trim(path: Path, floor_db: float = -38.0, lead_ms: float = 12.0) -> tuple[float, float]:
    """
    Срезать тишину с обоих краёв. Возвращает (сколько срезано спереди, сзади) в секундах.

    Порог берётся не абсолютный, а относительно пика самого файла: тихий
    клип и громкий клип «молчат» на разных уровнях, и общий порог у первого
    съел бы полезное начало.

    Спереди оставляется <paramref name="lead_ms"/> — двенадцать миллисекунд
    воздуха. Резать вплотную к первому сэмплу нельзя: у транзиента срезается
    фронт, и удар начинает щёлкать.
    """
    with wave.open(str(path), "rb") as handle:
        params = handle.getparams()
        raw = handle.readframes(handle.getnframes())

    if params.sampwidth != 2:
        return 0.0, 0.0

    data = array.array("h")
    data.frombytes(raw)
    if sys.byteorder == "big":
        data.byteswap()

    channels = max(1, params.nchannels)
    frames = len(data) // channels
    if frames == 0:
        return 0.0, 0.0

    peak = max(abs(value) for value in data) or 1
    gate = peak * (10.0 ** (floor_db / 20.0))

    first = 0
    while first < frames and max(abs(data[first * channels + c]) for c in range(channels)) < gate:
        first += 1

    last = frames - 1
    while last > first and max(abs(data[last * channels + c]) for c in range(channels)) < gate:
        last -= 1

    if first >= last:
        return 0.0, 0.0

    lead = int(params.framerate * lead_ms / 1000.0)
    start = max(0, first - lead)
    end = min(frames, last + lead)
    if start == 0 and end == frames:
        return 0.0, 0.0

    cut = data[start * channels:end * channels]
    if sys.byteorder == "big":
        cut.byteswap()

    with wave.open(str(path), "wb") as handle:
        handle.setparams(params)
        handle.writeframes(cut.tobytes())

    return start / params.framerate, (frames - end) / params.framerate


def normalize(path: Path, target_db: float) -> float:
    """Поднять пик до целевого. Возвращает применённую поправку в дБ."""
    with wave.open(str(path), "rb") as handle:
        params = handle.getparams()
        raw = handle.readframes(handle.getnframes())

    if params.sampwidth != 2:
        raise ValueError("выравнивание пика сделано только для 16-битного PCM")

    data = array.array("h")
    data.frombytes(raw)
    if sys.byteorder == "big":
        data.byteswap()

    peak = max(abs(value) for value in data) / 32768.0
    if peak <= 1e-9:
        return 0.0

    gain_db = target_db - db(peak)
    gain = 10.0 ** (gain_db / 20.0)
    limit = 32767
    for index, value in enumerate(data):
        scaled = int(round(value * gain))
        data[index] = max(-limit, min(limit, scaled))

    if sys.byteorder == "big":
        data.byteswap()

    with wave.open(str(path), "wb") as handle:
        handle.setparams(params)
        handle.writeframes(data.tobytes())

    return gain_db


def main() -> int:
    parser = argparse.ArgumentParser(description="Замер и выравнивание WAV без ffmpeg")
    parser.add_argument("target", type=Path, help="папка или файл")
    parser.add_argument("--roles", type=Path, help="манифест слотов с полем role")
    parser.add_argument("--normalize", type=float, metavar="dBFS",
                        help="выровнять пик до значения, например -1")
    parser.add_argument("--trim", action="store_true",
                        help="срезать тишину по краям перед замером")
    args = parser.parse_args()

    files = sorted(args.target.glob("*.wav")) if args.target.is_dir() else [args.target]
    if not files:
        print("WAV не найдено", file=sys.stderr)
        return 1

    roles: dict[str, str] = {}
    if args.roles and args.roles.exists():
        manifest = json.loads(args.roles.read_text(encoding="utf-8"))
        for slot in manifest.get("slots", []):
            roles[slot["id"]] = slot.get("role", "loop" if slot.get("loop") else "impact")

    if args.trim:
        print("Срез тишины по краям:")
        for path in files:
            try:
                head, tail = trim(path)
            except Exception as error:  # noqa: BLE001
                print(f"  {path.name}: не обрезан — {error}")
                continue
            if head or tail:
                print(f"  {path.name}: спереди {head * 1000:.0f} мс, сзади {tail * 1000:.0f} мс")
        print()

    print(f"{'файл':<26}{'сек':>6}{'пик':>8}{'RMS':>8}{'срез':>7}{'тишина':>8}  форма")
    rows = []
    for path in files:
        try:
            m = measure(path)
        except Exception as error:  # noqa: BLE001 — печатаем и идём дальше
            print(f"{path.name:<26}  не разобран: {error}")
            continue

        slot = path.stem.rsplit("_v", 1)[0]
        role = roles.get(slot, "impact")
        bad = reject(m)
        score, shape = fitness(m, role)
        rows.append((slot, path, m, score, bad))

        note = bad if bad else f"{role}: {shape}"
        print(f"{m['file']:<26}{m['seconds']:>6.1f}{m['peak_db']:>8.1f}{m['rms_db']:>8.1f}"
              f"{m['clipped']:>7}{m['silence_ratio'] * 100:>7.0f}%  {note}")

    if args.normalize is not None:
        print()
        for slot, path, m, score, bad in rows:
            if bad:
                print(f"{path.name}: {bad} — НЕ поднят, перегенерировать")
                continue
            gain = normalize(path, args.normalize)
            print(f"{path.name}: {gain:+.1f} дБ -> пик {args.normalize:.1f} dBFS")

    best: dict[str, tuple[float, str]] = {}
    for slot, path, m, score, bad in rows:
        if bad:
            continue
        if slot not in best or score > best[slot][0]:
            best[slot] = (score, path.name)

    if best:
        print("\nЛучший по форме в каждом слоте:")
        for slot in sorted(best):
            print(f"  {slot:<20} {best[slot][1]}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
