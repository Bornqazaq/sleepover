"""Compose a technical marker diagram from Blender's orthographic projection.

Run with Python + Pillow after bruno_meshy_export.py; no 3D scene mutations.
"""
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art-source/BrunoMeshy'
font_root=Path('/System/Library/Fonts/Supplemental')
regular=font_root/'Arial.ttf'
bold=font_root/'Arial Bold.ttf'
if not regular.exists():
    regular=Path('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf')
    bold=Path('/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf')
def font(size,heavy=False):
    return ImageFont.truetype(str(bold if heavy else regular),size)

canvas=Image.new('RGBA',(1600,1580),'#151b20')
draw=ImageDraw.Draw(canvas)
draw.text((68,45),'BRUNO  /  MESHY',font=font(48,True),fill='#f4eee2')
draw.text((68,112),'Quadruped • стартовая разметка для Bruno_Meshy_body_only.glb',font=font(25),fill='#b4bfc8')
draw.text((68,155),'Смотри строго сбоку. Центр кольца — центр сустава, а не край силуэта.',font=font(24),fill='#d7dce0')
plate=Image.open(OUT/'side.png').convert('RGBA')
canvas.alpha_composite(plate,(0,180))
markers=json.loads((OUT/'markers-projected.json').read_text())
lookup={m['key']:m for m in markers}
overlay=Image.new('RGBA',canvas.size,(0,0,0,0))
pen=ImageDraw.Draw(overlay)
def point(key):
    p=lookup[key]['xy']
    return p[0],p[1]+180
for chain in [('shoulder','fore_elbow','fore_wrist','fore_foot'),
              ('waist','hip','hind_knee','hind_hock','hind_foot')]:
    pen.line([point(k) for k in chain],fill=(255,255,255,90),width=3)
for m in markers:
    x,y=point(m['key'])
    r=21
    pen.ellipse((x-r,y-r,x+r,y+r),fill=(12,17,20,160),outline=m['color'],width=4)
    if m['letter']:
        pen.text((x,y),m['letter'],anchor='mm',font=font(23,True),fill=m['color'])
    else:
        pen.ellipse((x-4,y-4,x+4,y+4),fill=m['color'])
canvas=Image.alpha_composite(canvas,overlay)
draw=ImageDraw.Draw(canvas)
draw.line((68,1070,1532,1070),fill='#37424a',width=2)
draw.text((68,1090),'ПЕРЕДНЯЯ ЧАСТЬ  /  F',font=font(24,True),fill='#f4eee2')
draw.text((825,1090),'ЗАДНЯЯ ЧАСТЬ  /  H',font=font(24,True),fill='#f4eee2')
columns=[(68,[
    ('chin','Подбородок','основание головы / челюсти'),
    ('shoulder','Плечи','сустав под плечевым горбом'),
    ('fore_elbow','Предплечье (F)','локоть, верхний сгиб'),
    ('fore_wrist','Пастерн (F)','узкое запястье над лапой'),
    ('fore_foot','Рука (F)','передняя лапа, до когтей'),
]),(825,[
    ('waist','Талия','спина перед тазом'),
    ('hip','Ягодицы','тазобедренный сустав'),
    ('hind_knee','Шин (H)','колено, сгиб вперёд'),
    ('hind_hock','Крюк (H)','скакательный сустав, сгиб назад'),
    ('hind_foot','Ступня (H)','задняя стопа, до когтей'),
    ('tail','Хвост','центр короткого хвоста'),
])]
for x,items in columns:
    for i,(key,label,meaning) in enumerate(items):
        y=1150+i*49
        color=lookup[key]['color']
        draw.ellipse((x,y-9,x+16,y+7),fill=color)
        draw.text((x+31,y-16),label,font=font(23,True),fill='#eef0ed')
        draw.text((x+291,y-15),meaning,font=font(21),fill='#b4bfc8')
draw.text((68,1471),'Оранжевые F/H ставь выше зелёных: это разные суставы.',font=font(25,True),fill='#ffbe83')
draw.text((68,1517),'После Rig проверь сгибы в Walk: созданные Meshy веса и походка ещё требуют проверки.',
          font=font(22),fill='#a7b4bd')
canvas.convert('RGB').save(OUT/'Bruno_Meshy_markers.png')
canvas.convert('RGB').save(Path.home()/'Desktop/Bruno_Meshy_markers.png')
print(OUT/'Bruno_Meshy_markers.png')
