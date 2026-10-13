"""Pair unretouched, identically lit renders of the existing bear and v6."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parents[2] / 'art-source/BrunoMeshy/polish-v6'
FONT = '/System/Library/Fonts/Supplemental/Arial.ttf'
font = ImageFont.truetype(FONT, 28)
small = ImageFont.truetype(FONT, 19)
titles = {'body': 'Общий силуэт', 'head': 'Морда',
          'profile': 'Профиль и задние лапы', 'head-side': 'Челюсть и посадка ушей'}
panels = []
for view, title in titles.items():
    panel = Image.new('RGB', (2200, 889), (24, 27, 31))
    draw = ImageDraw.Draw(panel)
    for x, label, tag in [(0, 'ДО', 'before'), (1100, 'ПОСЛЕ — v6', 'after')]:
        draw.text((x + 25, 12), label, font=font, fill=(242, 234, 220))
        draw.text((x + 265, 18), title, font=small, fill=(190, 195, 202))
        panel.paste(Image.open(OUT / f'{tag}-{view}.png').convert('RGB'), (x, 64))
    panel.save(OUT / f'Bruno_Compare_{view}.png')
    panels.append(panel)

width = 1600
height = round(panels[0].height * width / panels[0].width)
sheet = Image.new('RGB', (width, height * len(panels)), (24, 27, 31))
for i, panel in enumerate(panels):
    sheet.paste(panel.resize((width, height), Image.Resampling.LANCZOS), (0, i * height))
sheet.save(OUT / 'Bruno_Before_After_v6.png')
print(OUT / 'Bruno_Before_After_v6.png')
