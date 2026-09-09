"""Deterministic night-gallery layout, shared by Unity and the Blender source."""
import json, math, random
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'igruha/Assets/_Project/Art/CryingAngels/CA_NightLayout.json'
rng=random.Random(932)
highs=['WeepingAngel','ShroudedFigure','ShatteredPillar','WarningAngel','MourningObelisk','PrayingAngel']
lows=['Sarcophagus','FallenVisage','FuneraryUrn','FallenColumn','Reliquary','BrokenPlinth']
sizes={
 'WeepingAngel':(1.2,2.3,1.05),'ShroudedFigure':(1.1,2.2,.94),
 'ShatteredPillar':(1.15,3.2,1.15),'WarningAngel':(1.18,2.25,1.05),
 'MourningObelisk':(1.12,2.8,.94),'PrayingAngel':(1.2,2.35,1.05),
 'Sarcophagus':(2.4,.864,1.06),'FallenVisage':(1.16,.864,1.05),
 'FuneraryUrn':(.98,.864,.92),'FallenColumn':(2.35,.864,.98),
 'Reliquary':(1.4,.864,1.08),'BrokenPlinth':(1.18,.864,1.05)}
rows=[];counts=[0,0]
def add(name,model,x,z,yaw,size):
 rows.append(dict(name=name,model='CA_'+model,position=dict(x=x,y=size[1]/2,z=z),size=dict(zip(('x','y','z'),size)),yaw=yaw))
PEDESTAL_RADIUS=2.16;PEDESTAL_GAP=1.3
for ring,(radius,count,offset) in enumerate([(4.6,4,0),(7.8,8,22.5),(10.9,12,0),(13.9,18,5),(17.2,24,4),(21.6,32,5.625)]):
 for i in range(count):
  high=i%8<3
  pool=highs if high else lows; index=1 if high else 0
  model=pool[(counts[index]+ring)%len(pool)];counts[index]+=1
  if ring!=4 and model in ('Sarcophagus','FallenColumn'):model=['FuneraryUrn','FallenVisage'][i%2]
  a=math.radians(offset+i*360/count+rng.uniform(-1,1));r=radius+rng.uniform(-.24,.24)
  scale=rng.uniform(.94,1.04);sz=sizes[model];sz=(sz[0]*scale,sz[1],sz[2]*scale)
  add(f'Cover_Gallery_{ring}_{i:02}_'+('High' if high else 'Low'),model,r*math.sin(a),r*math.cos(a),math.degrees(a)+rng.uniform(-14,14),sz)
# Protect all eight unchanged outer spawns with an intentional tall exhibit.
for i in range(8):
 a=i*math.pi/4
 add(f'Cover_SpawnScreen_{i+1:02}_High',highs[i%6],24.6*math.sin(a),24.6*math.cos(a),i*45,(1.65,2.4,1.12))

# Conservative circles enclose each rotated box. Offending pairs are pushed
# apart deterministically; the intentional spawn screens never move.
MIN_GAP=1.5
def radius_of(r): return math.hypot(r['size']['x'],r['size']['z'])/2
def gap_of(a,b):
 dx=a['position']['x']-b['position']['x'];dz=a['position']['z']-b['position']['z']
 return math.hypot(dx,dz)-radius_of(a)-radius_of(b),dx,dz
for _ in range(400):
 worst=None
 for i,a in enumerate(rows):
  for b in rows[i+1:]:
   g,dx,dz=gap_of(a,b)
   if g<MIN_GAP and (worst is None or g<worst[0]):worst=(g,a,b,dx,dz)
 if worst is None:break
 g,a,b,dx,dz=worst;d=math.hypot(dx,dz) or 1.0;push=(MIN_GAP-g)+.02
 fixed_a='SpawnScreen' in a['name'];fixed_b='SpawnScreen' in b['name']
 wa=0 if fixed_a else (1 if fixed_b else .5);wb=0 if fixed_b else (1 if fixed_a else .5)
 a['position']['x']+=dx/d*push*wa;a['position']['z']+=dz/d*push*wa
 b['position']['x']-=dx/d*push*wb;b['position']['z']-=dz/d*push*wb
minimum=(999,None)
for i,a in enumerate(rows):
 for b in rows[i+1:]:
  g,_,_=gap_of(a,b)
  if g<minimum[0]:minimum=(g,(a['name'],b['name']))
assert minimum[0]>=1.44,minimum
# Nothing may crowd the keeper's dais: the final dash must stay a dash.
pedestal=min(math.hypot(r['position']['x'],r['position']['z'])-math.hypot(r['size']['x'],r['size']['z'])/2-PEDESTAL_RADIUS for r in rows)
assert pedestal>=PEDESTAL_GAP,pedestal
OUT.write_text(json.dumps(dict(referenceRadius=28.08,covers=rows),indent=2)+'\n')
print(json.dumps(dict(covers=len(rows),high=sum(r['name'].endswith('High') for r in rows),minimumConservativeGap=minimum,pedestalGap=round(pedestal,2))))
