"""Original short bear cues, synthesized without recordings or external assets.
Python standard library only. Mono PCM, headroom preserved for overlapping steps.
"""
import math
import random
import struct
import wave
from pathlib import Path

DEST = Path(__file__).resolve().parents[1] / 'igruha/Assets/_Project/Audio/Minigames/Circus'
RATE = 32000
DEST.mkdir(parents=True, exist_ok=True)


def write(name, seconds, peak):
    rng = random.Random(419 + len(name))
    values = []
    low = bass = phase = 0.0
    for i in range(int(RATE * seconds)):
        t = i / RATE
        x = t / seconds
        noise = rng.uniform(-1, 1)
        low += .095 * (noise - low)
        bass += .012 * (noise - bass)
        fade = min(1, t / .009, (seconds - t) / .04)
        if name == 'growl':
            phase += math.tau * (83 - 21*x + 3*math.sin(t*19) + bass*17) / RATE
            throaty = sum(math.sin(phase*k + .18*math.sin(t*21+k)) / k for k in range(1, 9))
            breath = (low - bass) * 2.4
            value = math.tanh(throaty*.62 + breath) * math.sin(math.pi*x)**.65 * (.78 + .22*math.sin(t*34))
        elif name == 'swipe':
            value = (low*.7 + noise*.2) * math.sin(math.pi*x)**1.6
        elif name == 'impact':
            phase += math.tau * (55 + 95*math.exp(-t*26)) / RATE
            value = math.sin(phase)*math.exp(-t*15) + low*3*math.exp(-t*26) + noise*.22*math.exp(-t*95)
        else:
            value = math.sin(math.tau*71*t)*math.exp(-t*35)*.65 + low*1.9*math.exp(-t*25)
        values.append(value * max(0, fade))
    gain = peak / max(abs(v) for v in values)
    pcm = b''.join(struct.pack('<h', round(v*gain*32767)) for v in values)
    with wave.open(str(DEST / ('Bruno_' + name + '.wav')), 'wb') as stream:
        stream.setnchannels(1)
        stream.setsampwidth(2)
        stream.setframerate(RATE)
        stream.writeframes(pcm)


for args in [('growl', .66, .63), ('swipe', .25, .55), ('impact', .32, .79), ('step', .18, .50)]:
    write(*args)
