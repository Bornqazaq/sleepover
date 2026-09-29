"""Deterministic night-gallery layout, shared by Unity and the Blender source.

Open-floor revision: the original sculptures and columns, arranged as
islands with open lanes between them so the keeper can actually catch runners
crossing the floor. Scale spans four octaves: knee-high plinths, human-sized
statues, four-metre giants, ruined wall segments and columns up to the dome.
"""
import json, math, random
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'igruha/Assets/_Project/Art/CryingAngels/CA_NightLayout.json'
rng=random.Random(935)
WALL_RADIUS=34.0
POSITION_SCALE=WALL_RADIUS/28.08
OPEN_RADIUS=12.5
EXHIBIT_OUTER_RADIUS=28.2
highs=['WeepingAngel','ShroudedFigure','ShatteredPillar','WarningAngel','MourningObelisk','PrayingAngel']
# Only solid-topped pieces hide a crouched head: the urn is narrow, the visage and reliquary crest are open.
# A sarcophagus stretched to LOW_H reads as a chest tomb, a plinth as a taller plinth.
lows=['BrokenPlinth','Sarcophagus']
# Low exhibits hide a crouched character completely: the crouch capsule is 0.8 m, but the crouch
# clip keeps the visual head at up to LOW_H_HEAD m on the tallest roster member (measured in the
# editor 11.09). The keeper's eye is at 2.06 m on the dais and looks down, so the box must clear
# that head with a margin: LOW_H. The shortest standing capsule is 1.65 m and the head ray hits
# 0.05 below its top (1.60), so anything under 1.60 keeps a standing runner lit.
LOW_H_HEAD=1.49
LOW_H=1.56
sizes={
 'WeepingAngel':(1.2,2.3,1.05),'ShroudedFigure':(1.1,2.2,.94),
 'ShatteredPillar':(1.15,3.2,1.15),'WarningAngel':(1.18,2.25,1.05),
 'MourningObelisk':(1.12,2.8,.94),'PrayingAngel':(1.2,2.35,1.05),
 'Sarcophagus':(2.4,LOW_H,1.06),'FallenVisage':(1.16,LOW_H,1.05),
 'FuneraryUrn':(.98,LOW_H,.92),'FallenColumn':(2.35,LOW_H,.98),
 'Reliquary':(1.4,LOW_H,1.08),'BrokenPlinth':(1.18,LOW_H,1.05),
 'GreatColumn':(1.95,16.2,1.95),'RuinedWall':(5.5,3.4,1.3)}
GIANT={'WeepingAngel':(2.3,4.6,2.1),'PrayingAngel':(2.3,4.6,2.1),'WarningAngel':(2.3,4.6,2.1),'ShroudedFigure':(2.2,4.5,1.9)}
# Dome coffer height by radius (art units); columns are stretched to meet it.
DOME=[(9,18.48),(14,17.84),(19,16.55),(23,14.88),(26,13.40),(28,11.84)]
def ceiling(r):
 if r<=DOME[0][0]:return DOME[0][1]
 for (r0,z0),(r1,z1) in zip(DOME,DOME[1:]):
  if r<=r1:return z0+(z1-z0)*(r-r0)/(r1-r0)
 return DOME[-1][1]
rows=[]
def polar(r,deg):
 a=math.radians(deg);return r*math.sin(a),r*math.cos(a)
def add(name,model,x,z,yaw,size,group):
 rows.append(dict(name=name,model='CA_'+model,position=dict(x=x*POSITION_SCALE,y=size[1]/2,z=z*POSITION_SCALE),size=dict(zip(('x','y','z'),size)),yaw=yaw,group=group))
PEDESTAL_RADIUS=2.16;PEDESTAL_GAP=1.3
# Eight tall screens keep the unchanged outer spawns out of the opening sweep.
for i in range(8):
 x,z=polar(26.5/POSITION_SCALE,i*45)
 add(f'Cover_SpawnScreen_{i+1:02}_High',highs[i%6],x,z,i*45,(1.65,2.4,1.12),f'screen{i}')
# The final 12.5 m are open in every direction. Crouching only helps behind
# an actual exhibit; it cannot turn the entire approach into a hidden route.
# Columns to the dome, scattered rather than on a ring.
# Tall exhibits stay outside the exposed inner floor.
for i,(r,deg) in enumerate([(20.0,22.5),(23.0,67.5),(21.0,112.5),(24.0,157.5),(20.5,202.5),(23.5,247.5),(21.5,292.5),(24.5,337.5)]):
 r/=POSITION_SCALE
 x,z=polar(r,deg);h=ceiling(r)+.35
 add(f'Cover_Column_{i:02}_High','GreatColumn',x,z,rng.uniform(0,360),(1.95,h,1.95),f'column{i}')
# Long ruined walls, tangential: a wall reads as a wall, not as another statue.
for i,(r,deg,tilt) in enumerate([(17.8,70,95),(19.0,205,80),(18.0,330,70),(20.2,120,100)]):
 x,z=polar(r,deg)
 add(f'Cover_Wall_{i:02}_High','RuinedWall',x,z,deg+tilt,sizes['RuinedWall'],f'wall{i}')
# Giants: twice human height, visible from anywhere in the hall.
for i,(r,deg,model) in enumerate([(12.6,100,'WeepingAngel'),(15.4,165,'PrayingAngel'),(13.0,355,'WarningAngel'),(18.2,315,'WeepingAngel'),(16.6,45,'ShroudedFigure')]):
 x,z=polar(r,deg)
 add(f'Cover_Giant_{i:02}_High',model,x,z,deg+180+rng.uniform(-25,25),GIANT[model],f'giant{i}')
# Pairs of human-scale exhibits; the broad lanes between groups stay open.
islands=[(13.8,15,2),(16.2,65,2),(13.8,140,2),(17.0,180,2),(14.0,225,2),(18.0,280,2),(14.4,320,2),(21.6,40,2)]
hi=0;lo=0
for n,(r,deg,count) in enumerate(islands):
 cx,cz=polar(r,deg)
 for i in range(count):
  high=i%2==1
  # Islands closer than 10 m are crouch-only: no tall silhouette between the keeper and the far hall.
  if r<10.0:high=False
  pool=highs if high else lows
  model=pool[(hi if high else lo)%len(pool)]
  if high:hi+=1
  else:lo+=1
  # A 2.4 m chest does not fit a three-piece island with the 1.5 m gaps; islands of two keep it.
  if model=='Sarcophagus' and count>2:model='BrokenPlinth'
  a=math.radians(deg+i*360/count+rng.uniform(-30,30));d=rng.uniform(1.3,2.2)
  scale=rng.uniform(.92,1.06);sz=sizes[model];sz=(sz[0]*scale,sz[1],sz[2]*scale)
  add(f'Cover_Island_{n:02}_{i}_'+('High' if high else 'Low'),model,cx+d*math.sin(a),cz+d*math.cos(a),math.degrees(a)+rng.uniform(-30,30),sz,f'island{n}')

# Conservative circles enclose each rotated box. Neighbours inside one island may
# stand close; anything else keeps a lane wide enough to be caught in.
GAP_ISLAND=1.5;GAP_LANE=3.3
def radius_of(r): return r['radius'] if 'radius' in r else math.hypot(r['size']['x'],r['size']['z'])/2
def needed(a,b):
 if 'radius' in a or 'radius' in b:return PEDESTAL_GAP
 return GAP_ISLAND if a['group']==b['group'] else GAP_LANE
def gap_of(a,b):
 dx=a['position']['x']-b['position']['x'];dz=a['position']['z']-b['position']['z']
 return math.hypot(dx,dz)-radius_of(a)-radius_of(b),dx,dz
def fixed(r): return 'radius' in r or 'SpawnScreen' in r['name'] or 'Column' in r['name']

# The dais takes part in the push as an immovable circle; the wall clamps radially.
rows.append(dict(name='Dais',position=dict(x=0,y=0,z=0),radius=PEDESTAL_RADIUS,group='dais'))
# Tall exhibits keep their distance from the dais even after the push: from 2 m up a statue
# 4 m from the keeper's eye eclipses a wedge wider than the beam, and the hall behind it goes blind.
TALL_MIN_RADIUS=16.5
def is_tall(r): return 'radius' not in r and r['name'].endswith('High') and 'SpawnScreen' not in r['name']
def clamp(r):
 if 'radius' in r:return
 d=math.hypot(r['position']['x'],r['position']['z']);limit=EXHIBIT_OUTER_RADIUS-radius_of(r)
 if d>limit:r['position']['x']*=limit/d;r['position']['z']*=limit/d;return
 if 'SpawnScreen' not in r['name']:
  floor_r=(TALL_MIN_RADIUS if is_tall(r) else OPEN_RADIUS)+radius_of(r)
  if d<floor_r and d>0:r['position']['x']*=floor_r/d;r['position']['z']*=floor_r/d
# Pack the fixed landmarks first, then preserve each exhibit's intended area
# using the nearest valid location. A constructive search avoids the old
# relaxation pushing long walls back and forth through neighbouring columns.
preferred={r['name']:(r['position']['x'],r['position']['z']) for r in rows}
placed=[r for r in rows if fixed(r)]
for row in placed: clamp(row)
pending=[r for r in rows if not fixed(r)]
pending.sort(key=lambda row:-radius_of(row))
for row in pending:
    floor_r=(TALL_MIN_RADIUS if is_tall(row) else OPEN_RADIUS)+radius_of(row)
    max_r=EXHIBIT_OUTER_RADIUS-radius_of(row)
    px,pz=preferred[row['name']]
    best=None
    for radial_step in range(math.ceil((max_r-floor_r)/.35)+1):
        r=min(max_r,floor_r+radial_step*.35)
        for angle in range(360):
            x,z=polar(r,angle)
            cost=(x-px)**2+(z-pz)**2
            if best and cost>=best[0]: continue
            row['position']['x'],row['position']['z']=x,z
            if all(gap_of(row,other)[0]>=needed(row,other)+.015 for other in placed):
                best=(cost,x,z)
    assert best is not None,('no clear location',row['name'])
    row['position']['x'],row['position']['z']=best[1:]
    placed.append(row)
rows=placed
rows.remove(next(r for r in rows if 'radius' in r))
minimum=(999,None);lane=(999,None)
for i,a in enumerate(rows):
 for b in rows[i+1:]:
  g,_,_=gap_of(a,b)
  if g<minimum[0]:minimum=(g,(a['name'],b['name']))
  if a['group']!=b['group'] and g<lane[0]:lane=(g,(a['name'],b['name']))
assert minimum[0]>=1.44,minimum
assert lane[0]>=GAP_LANE-.1,lane
pedestal=min(math.hypot(r['position']['x'],r['position']['z'])-radius_of(r)-PEDESTAL_RADIUS for r in rows)
assert pedestal>=PEDESTAL_GAP-.02,pedestal
outer=max(math.hypot(r['position']['x'],r['position']['z'])+radius_of(r) for r in rows)
assert outer<=EXHIBIT_OUTER_RADIUS+.01,outer
nearest_tall=min(math.hypot(r['position']['x'],r['position']['z'])-radius_of(r) for r in rows if is_tall(r))
assert nearest_tall>=TALL_MIN_RADIUS-.05,nearest_tall
nearest_cover=min(math.hypot(r['position']['x'],r['position']['z'])-radius_of(r) for r in rows)
assert nearest_cover>=OPEN_RADIUS-.05,nearest_cover
for r in rows: del r['group']
OUT.write_text(json.dumps(dict(referenceRadius=WALL_RADIUS,covers=rows),indent=2)+'\n')
print(json.dumps(dict(covers=len(rows),high=sum(r['name'].endswith('High') for r in rows),minimumGap=round(minimum[0],2),minimumLane=round(lane[0],2),pedestalGap=round(pedestal,2),outermost=round(outer,2),openRadius=round(nearest_cover,2))))
