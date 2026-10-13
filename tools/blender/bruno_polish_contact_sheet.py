"""Lay out unretouched Blender/Unity review renders with labels."""
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
OUT=Path('/Users/azamatyessimkhanuly/Dev/projects/sleepover/art-source/BrunoMeshy/polish-v5')
FONT='/System/Library/Fonts/Supplemental/Arial.ttf'
font=ImageFont.truetype(FONT,30);small=ImageFont.truetype(FONT,21)
sheet=Image.new('RGB',(2400,990),(22,25,30));d=ImageDraw.Draw(sheet)
for x,name,label,sub in [(0,'before.png','ДО','220 960 треугольников'),(1200,'after.png','ПОСЛЕ','56 364 треугольника • 31 кость • 6 клипов')]:
 im=Image.open(OUT/name).convert('RGB');sheet.paste(im,(x,90));d.text((x+30,15),label,font=font,fill=(245,241,232));d.text((x+30,54),sub,font=small,fill=(185,189,198))
sheet.save(OUT/'Bruno_Before_After.png')
items=[('front.png','СПЕРЕДИ'),('side.png','СБОКУ'),('face.png','МОРДА'),('solid-clay.png','ГЕОМЕТРИЯ БЕЗ ТЕКСТУР'),('game-distance.png','ИГРОВАЯ ДИСТАНЦИЯ'),('unity-urp-walk.png','UNITY / URP — ХОДЬБА')]
contact=Image.new('RGB',(1800,1390),(22,25,30));d=ImageDraw.Draw(contact)
for k,(name,label) in enumerate(items):
 x=(k%3)*600;y=(k//3)*695;d.text((x+20,y+18),label,font=small,fill=(236,229,214))
 im=Image.open(OUT/name).convert('RGB');im.thumbnail((590,630));contact.paste(im,(x+(600-im.width)//2,y+54+(630-im.height)//2))
contact.save(OUT/'Bruno_Review_Sheet.png')
print('Comparison and review sheet saved')
