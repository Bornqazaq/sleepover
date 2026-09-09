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
for ring,(radius,count,offset) in enumerate([(8.5,12,7),(12.7,18,17),(17.2,24,4),(21.6,32,5.625)]):
 for i in range(count):
  high=i%8<3
  pool=highs if high else lows; index=1 if high else 0
  model=pool[(counts[index]+ring)%len(pool)];counts[index]+=1
  if ring>=2 and model in ('Sarcophagus','FallenColumn'):model=['FuneraryUrn','FallenVisage'][i%2]
  a=math.radians(offset+i*360/count+rng.uniform(-1,1));r=radius+rng.uniform(-.24,.24)
  scale=rng.uniform(.94,1.04);sz=sizes[model];sz=(sz[0]*scale,sz[1],sz[2]*scale)
  add(f'Cover_Gallery_{ring}_{i:02}_'+('High' if high else 'Low'),model,r*math.sin(a),r*math.cos(a),math.degrees(a)+rng.uniform(-14,14),sz)
# Protect all eight unchanged outer spawns with an intentional tall exhibit.
for i in range(8):
 a=i*math.pi/4
 add(f'Cover_SpawnScreen_{i+1:02}_High',highs[i%6],24.6*math.sin(a),24.6*math.cos(a),i*45,(1.65,2.4,1.12))

# Conservative circles enclose each rotated box; a pass here guarantees the gap.
minimum=(999,None)
for i,a in enumerate(rows):
 for b in rows[i+1:]:
  dist=math.hypot(a['position']['x']-b['position']['x'],a['position']['z']-b['position']['z'])
  gap=dist-sum(math.hypot(c['size']['x'],c['size']['z'])/2 for c in (a,b))
  if gap<minimum[0]:minimum=(gap,(a['name'],b['name']))
assert minimum[0]>=1.44,minimum
OUT.write_text(json.dumps(dict(referenceRadius=28.08,covers=rows),indent=2)+'\n')
print(json.dumps(dict(covers=len(rows),high=sum(r['name'].endswith('High') for r in rows),minimumConservativeGap=minimum)))
