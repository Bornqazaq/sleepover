"""Regenerate the explicitly requested temporary CarryItem voice on macOS."""
from pathlib import Path
import math
import random
import struct
import subprocess
import tempfile
import wave

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "igruha/Assets/_Project/Audio/Minigames/CarryItem/TemporaryVoice"
LINES = {
    "disagreement": "Перекос! Отстаёт",
    "turn": "Занесло на повороте!",
    "release": "бросил поручень!",
    "impact": "Удар! Вода за бортом!",
    "boss": "Босс",
    "aza": "Аза",
}
for number, word in enumerate(("один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь"), 1):
    LINES[f"player{number}"] = f"Игрок номер {word}"

OUT.mkdir(parents=True, exist_ok=True)
with tempfile.TemporaryDirectory(prefix="carry-voice-") as temporary:
    for key, text in LINES.items():
        intermediate = Path(temporary) / f"{key}.aiff"
        subprocess.run(["say", "-v", "Milena", "-r", "185", "-o", str(intermediate), text], check=True)
        subprocess.run(["afconvert", "-f", "WAVE", "-d", "LEI16@24000", str(intermediate), str(OUT / f"{key}.wav")], check=True)

# A quiet, seamlessly looped mechanical strain sketch; replace with a recorded wheel bearing later.
random.seed(690)
rate = 24000
samples = []
for i in range(rate * 2):
    t = i / rate
    wobble = 1 + 0.22 * math.sin(2 * math.pi * 3 * t)
    value = (0.11 * math.sin(2 * math.pi * 95 * t + 1.2 * math.sin(2 * math.pi * 2 * t)) +
             0.055 * math.sin(2 * math.pi * 285 * t) + 0.018 * random.uniform(-1, 1)) * wobble
    samples.append(struct.pack("<h", int(value * 32767)))
with wave.open(str(OUT / "wheel_strain.wav"), "wb") as output:
    output.setparams((1, 2, rate, 0, "NONE", "not compressed"))
    output.writeframes(b"".join(samples))
print(f"Generated {len(LINES)} voice clips and wheel_strain.wav in {OUT.relative_to(ROOT)}")
