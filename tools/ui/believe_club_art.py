"""
Графика «Верю / не верю» (IGR-565): карточки исхода, ореол, облачко,
бархат подкладки шкатулок и две детали интерфейса в клубном стиле.

Рисуется кодом по той же причине, что и спрайты UiSpriteBaker: всё
остальное в проекте собирается пересборкой, и картинки не должны быть
единственным местом, которое живёт руками.

Запуск из корня репозитория (нужны numpy и Pillow):
    python3 tools/ui/believe_club_art.py

Сглаживание — рисованием в SS раз крупнее и уменьшением: линии знаков
и рамки на 4K иначе идут лесенкой.
"""

import os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ART = os.path.join(ROOT, "igruha", "Assets", "_Project", "Art")
CARDS = os.path.join(ART, "BelieveCards", "Textures")
UI = os.path.join(ART, "UI")

SS = 3
RNG = np.random.default_rng(565)

GOLD = (201, 158, 86)
GOLD_LIGHT = (236, 203, 134)
PAPER = (245, 236, 216)

WIN = {"main": (44, 118, 64), "dark": (24, 72, 38), "light": (132, 196, 140)}
LOSE = {"main": (156, 42, 36), "dark": (98, 22, 18), "light": (226, 128, 116)}

CARD_W, CARD_H = 512, 716
CARD_RADIUS = 42


def rounded_mask(w, h, r):
    mask = Image.new("L", (w * SS, h * SS), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, w * SS - 1, h * SS - 1), r * SS, fill=255)
    return mask


def grain(w, h, strength):
    noise = RNG.normal(0.0, strength, (h, w, 1))
    return noise


def paper(w, h):
    y = np.linspace(0.0, 1.0, h)[:, None, None]
    x = np.linspace(-1.0, 1.0, w)[None, :, None]
    base = np.array(PAPER, dtype=np.float32)[None, None, :]
    shade = 1.0 - 0.05 * y - 0.05 * (x ** 2)
    img = base * shade + grain(w, h, 3.0)
    return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))


def stroke(draw, points, width, color):
    draw.line(points, fill=color, width=width, joint="curve")
    r = width / 2.0
    for px, py in (points[0], points[-1]):
        draw.ellipse((px - r, py - r, px + r, py + r), fill=color)


def symbol(draw, cx, cy, size, win, scale=1.0):
    """Галочка или крест: тёмная подводка, основной тон, блик сверху."""
    tone = WIN if win else LOSE
    if win:
        shapes = [[(-0.40, 0.02), (-0.12, 0.30), (0.44, -0.34)]]
    else:
        shapes = [[(-0.34, -0.34), (0.34, 0.34)], [(-0.34, 0.34), (0.34, -0.34)]]

    width = size * 0.19 * scale
    for layer, extra, color in (("dark", width * 0.22, tone["dark"]), ("main", 0.0, tone["main"])):
        for shape in shapes:
            pts = [(cx + px * size, cy + py * size) for px, py in shape]
            stroke(draw, pts, int(width + extra), color)

    highlight = max(2, int(width * 0.16))
    for shape in shapes:
        pts = [(cx + px * size - width * 0.12, cy + py * size - width * 0.18) for px, py in shape]
        stroke(draw, pts, highlight, tone["light"])


def frame(draw, w, h, inset, radius, width, color):
    draw.rounded_rectangle((inset, inset, w - inset, h - inset), radius, outline=color, width=width)


def card_face(win):
    w, h = CARD_W * SS, CARD_H * SS
    img = paper(w, h).convert("RGBA")
    draw = ImageDraw.Draw(img)

    frame(draw, w, h, 22 * SS, 30 * SS, 6 * SS, GOLD)
    frame(draw, w, h, 36 * SS, 20 * SS, 2 * SS, GOLD)

    cx, cy = w / 2, h / 2
    ring = 168 * SS
    draw.ellipse((cx - ring, cy - ring, cx + ring, cy + ring), outline=GOLD, width=4 * SS)
    inner = ring - 12 * SS
    draw.ellipse((cx - inner, cy - inner, cx + inner, cy + inner), outline=GOLD_LIGHT, width=int(1.5 * SS))

    symbol(draw, cx, cy, 218 * SS, win)

    pip = Image.new("RGBA", (120 * SS, 120 * SS), (0, 0, 0, 0))
    symbol(ImageDraw.Draw(pip), 60 * SS, 60 * SS, 64 * SS, win, scale=1.15)
    img.alpha_composite(pip, (40 * SS, 44 * SS))
    img.alpha_composite(pip, (w - 160 * SS, h - 164 * SS))

    img.putalpha(rounded_mask(CARD_W, CARD_H, CARD_RADIUS))
    return img.resize((CARD_W, CARD_H), Image.LANCZOS)


def card_back():
    w, h = CARD_W * SS, CARD_H * SS
    felt = np.array((30, 74, 50), dtype=np.float32)[None, None, :] + grain(w, h, 4.0)
    img = Image.fromarray(np.clip(felt, 0, 255).astype(np.uint8)).convert("RGBA")
    draw = ImageDraw.Draw(img)

    step = 46 * SS
    lattice = GOLD + (70,)
    overlay = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    od = ImageDraw.Draw(overlay)
    for k in range(-h, w + h, step):
        od.line((k, 0, k + h, h), fill=lattice, width=2 * SS)
        od.line((k, h, k + h, 0), fill=lattice, width=2 * SS)
    clip = rounded_mask(CARD_W - 96, CARD_H - 96, 16)
    inner = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    inner.paste(overlay, (0, 0), None)
    lattice_mask = Image.new("L", (w, h), 0)
    lattice_mask.paste(clip, (48 * SS, 48 * SS))
    img.paste(overlay, (0, 0), Image.fromarray(
        (np.array(lattice_mask, dtype=np.float32) / 255.0 * np.array(overlay.split()[3], dtype=np.float32)).astype(np.uint8)))

    frame(draw, w, h, 22 * SS, 30 * SS, 6 * SS, GOLD)
    frame(draw, w, h, 36 * SS, 20 * SS, 2 * SS, GOLD)

    cx, cy = w / 2, h / 2
    r = 74 * SS
    draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(24, 58, 40), outline=GOLD, width=5 * SS)
    d = 34 * SS
    draw.polygon([(cx, cy - d), (cx + d, cy), (cx, cy + d), (cx - d, cy)], fill=GOLD_LIGHT)

    img.putalpha(rounded_mask(CARD_W, CARD_H, CARD_RADIUS))
    return img.resize((CARD_W, CARD_H), Image.LANCZOS)


def halo(size=256):
    y, x = np.mgrid[-1:1:complex(size), -1:1:complex(size)]
    r = np.clip(np.sqrt(x * x + y * y), 0, 1)
    a = (1.0 - r) ** 2.4
    rgba = np.zeros((size, size, 4), dtype=np.float32)
    rgba[..., :3] = 255
    rgba[..., 3] = a * 255
    return Image.fromarray(rgba.astype(np.uint8))


def fbm(size, octaves=5, seed_shift=0):
    total = np.zeros((size, size), dtype=np.float32)
    amp, weight = 1.0, 0.0
    for o in range(octaves):
        cells = 4 * (2 ** o)
        small = RNG.random((cells, cells)).astype(np.float32)
        layer = np.array(Image.fromarray((small * 255).astype(np.uint8)).resize((size, size), Image.BICUBIC),
                         dtype=np.float32) / 255.0
        total += layer * amp
        weight += amp
        amp *= 0.55
    return total / weight


def puff_sheet(frame_size=256):
    """2×2 кадра клубка пудры: мягкий край, объём от светлого верха к тёмному низу."""
    sheet = Image.new("RGBA", (frame_size * 2, frame_size * 2), (0, 0, 0, 0))
    y, x = np.mgrid[-1:1:complex(frame_size), -1:1:complex(frame_size)]
    for i in range(4):
        # Клуб из нескольких наложенных шаров разного размера: один ровный
        # круг читается ватным шариком. Шары складываются, а не берётся
        # максимум: внутри клуб сплошной, рваный только силуэт, иначе у
        # каждого шара своя светлая середина и облако читается гроздью пузырей.
        body = np.zeros_like(x)
        for _ in range(8):
            angle = RNG.uniform(0, 2 * np.pi)
            dist = RNG.uniform(0.0, 0.40)
            cx, cy = np.cos(angle) * dist, np.sin(angle) * dist
            radius = RNG.uniform(0.30, 0.50)
            d = np.sqrt((x - cx) ** 2 + (y - cy) ** 2) / radius
            body += np.clip(1.0 - d * d, 0.0, 1.0)
        n = fbm(frame_size)
        edge = np.clip((1.0 - np.sqrt(x * x + y * y)) * 2.2, 0.0, 1.0)
        alpha = np.clip(body * 1.4, 0.0, 1.0) * (0.55 + 0.45 * n) * edge
        shade = 0.78 + 0.22 * np.clip(-y * 0.55 - x * 0.25 + (n - 0.5) * 0.9, -1, 1)
        rgba = np.zeros((frame_size, frame_size, 4), dtype=np.float32)
        rgba[..., 0] = 255 * shade
        rgba[..., 1] = 250 * shade
        rgba[..., 2] = 242 * shade
        rgba[..., 3] = alpha * 255
        tile = Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(4))
        sheet.paste(tile, ((i % 2) * frame_size, (i // 2) * frame_size))
    return sheet


def velvet(size=512):
    """Бархат: бесшовный шум, ворс мелкой зернью. Серый — цвет даёт материал."""
    white = RNG.normal(0, 1, (size, size))
    fy = np.fft.fftfreq(size)[:, None]
    fx = np.fft.fftfreq(size)[None, :]
    f = np.sqrt(fx * fx + fy * fy)
    soft = np.real(np.fft.ifft2(np.fft.fft2(white) * np.exp(-(f / 0.02) ** 2)))
    fine = np.real(np.fft.ifft2(np.fft.fft2(RNG.normal(0, 1, (size, size))) * np.exp(-(f / 0.25) ** 2)))
    soft = (soft - soft.min()) / (soft.max() - soft.min())
    fine = (fine - fine.min()) / (fine.max() - fine.min())
    v = 0.78 + 0.16 * soft + 0.06 * fine
    return Image.fromarray((np.clip(v, 0, 1) * 255).astype(np.uint8)).convert("RGB")


def chip_stroke(size=128, width=5.0):
    """Обводка капсулы под UI_Chip: тот же размер, радиус и толщина, что у UiSpriteBaker."""
    s = size * SS
    img = Image.new("L", (s, s), 0)
    d = ImageDraw.Draw(img)
    r = s // 2
    d.rounded_rectangle((0, 0, s - 1, s - 1), r, fill=255)
    w = int(width * SS)
    d.rounded_rectangle((w, w, s - 1 - w, s - 1 - w), r - w, fill=0)
    alpha = img.resize((size, size), Image.LANCZOS)
    out = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    out.putalpha(alpha)
    return out


def ornament(w=512, h=24):
    """Разделитель: волосяная линия, гаснущая к краям, и ромб в центре."""
    s_w, s_h = w * SS, h * SS
    img = Image.new("L", (s_w, s_h), 0)
    d = ImageDraw.Draw(img)
    cy = s_h // 2
    d.line((0, cy, s_w, cy), fill=255, width=2 * SS)
    dia = 7 * SS
    cx = s_w // 2
    d.polygon([(cx, cy - dia), (cx + dia, cy), (cx, cy + dia), (cx - dia, cy)], fill=255)
    alpha = np.array(img.resize((w, h), Image.LANCZOS), dtype=np.float32)
    t = np.linspace(0, 1, w)[None, :]
    fade = np.clip(np.minimum(t, 1 - t) / 0.28, 0, 1)
    fade = fade * fade * (3 - 2 * fade)
    alpha *= fade
    out = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    out.putalpha(Image.fromarray(alpha.astype(np.uint8)))
    return out


def main():
    os.makedirs(CARDS, exist_ok=True)
    card_face(True).save(os.path.join(CARDS, "BC_CardWin.png"))
    card_face(False).save(os.path.join(CARDS, "BC_CardLose.png"))
    card_back().save(os.path.join(CARDS, "BC_CardBack.png"))
    halo().save(os.path.join(CARDS, "BC_Halo.png"))
    puff_sheet().save(os.path.join(CARDS, "BC_Puff.png"))
    velvet().save(os.path.join(CARDS, "BC_Velvet.png"))
    chip_stroke().save(os.path.join(UI, "UI_ChipStroke.png"))
    ornament().save(os.path.join(UI, "UI_Ornament.png"))
    print("ok:", CARDS, UI)


if __name__ == "__main__":
    main()
