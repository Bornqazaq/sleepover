"""Deterministic night-gallery layout, shared by Unity and the Blender source.

Third pass (scale pass): far fewer exhibits than the ring layout, arranged as
islands with open lanes between them so the keeper can actually catch runners
crossing the floor. Scale spans four octaves: knee-high plinths, human-sized
statues, four-metre giants, ruined wall segments and columns up to the dome.
"""
import json, math, random
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'igruha/Assets/_Project/Art/CryingAngels/CA_NightLayout.json'
rng=random.Random(935)
highs=['WeepingAngel','ShroudedFigure','ShatteredPillar','WarningAngel','MourningObelisk','PrayingAngel']
lows=['Sarcophagus','FallenVisage','FuneraryUrn','FallenColumn','Reliquary','BrokenPlinth']
sizes={
 'WeepingAngel':(1.2,2.3,1.05),'ShroudedFigure':(1.1,2.2,.94),
 'ShatteredPillar':(1.15,3.2,1.15),'WarningAngel':(1.18,2.25,1.05),
 'MourningObelisk':(1.12,2.8,.94),'PrayingAngel':(1.2,2.35,1.05),
 'Sarcophagus':(2.4,.864,1.06),'FallenVisage':(1.16,.864,1.05),
 'FuneraryUrn':(.98,.864,.92),'FallenColumn':(2.35,.864,.98),
 'Reliquary':(1.4,.864,1.08),'BrokenPlinth':(1.18,.864,1.05),
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
 rows.append(dict(name=name,model='CA_'+model,position=dict(x=x,y=size[1]/2,z=z),size=dict(zip(('x','y','z'),size)),yaw=yaw,group=group))
PEDESTAL_RADIUS=2.16;PEDESTAL_GAP=1.3
# Eight tall screens keep the unchanged outer spawns out of the opening sweep.
for i in range(8):
 x,z=polar(24.6,i*45)
 add(f'Cover_SpawnScreen_{i+1:02}_High',highs[i%6],x,z,i*45,(1.65,2.4,1.12),f'screen{i}')
# Near the dais: the final dash has something to break line of sight, but stays a dash.
# Low only: a tall exhibit 3.6 m from the keeper's eye eclipses a 15-degree wedge all the way to the wall,
# and runners walk up the visible beam without ever being lit (net watch 09.09).
for i,(r,deg,model) in enumerate([(5.3,15,'Reliquary'),(5.3,135,'BrokenPlinth'),(5.3,255,'Reliquary'),(7.6,75,'FallenVisage'),(7.6,195,'FuneraryUrn'),(7.6,315,'FallenVisage')]):
 x,z=polar(r,deg);high=model in highs
 add(f'Cover_Inner_{i:02}_'+('High' if high else 'Low'),model,x,z,deg+rng.uniform(-20,20),sizes[model],f'inner{i}')
# Columns to the dome, scattered rather than on a ring.
# Nothing tall inside 12 m: closer, a 2 m column hides a corridor wider than the beam itself.
for i,(r,deg) in enumerate([(12.4,40),(13.6,110),(12.8,175),(14.8,230),(13.0,300),(17.5,20),(19.2,150),(16.4,265)]):
 x,z=polar(r,deg);h=ceiling(r)+.35
 add(f'Cover_Column_{i:02}_High','GreatColumn',x,z,rng.uniform(0,360),(1.95,h,1.95),f'column{i}')
# Long ruined walls, tangential: a wall reads as a wall, not as another statue.
for i,(r,deg,tilt) in enumerate([(15.6,70,95),(19.0,205,80),(13.5,330,70),(20.2,120,100),(17.0,260,85),(21.0,285,92)]):
 x,z=polar(r,deg)
 add(f'Cover_Wall_{i:02}_High','RuinedWall',x,z,deg+tilt,sizes['RuinedWall'],f'wall{i}')
# Giants: twice human height, visible from anywhere in the hall.
for i,(r,deg,model) in enumerate([(12.6,100,'WeepingAngel'),(15.4,165,'PrayingAngel'),(13.0,355,'WarningAngel'),(18.2,315,'WeepingAngel'),(16.6,45,'ShroudedFigure')]):
 x,z=polar(r,deg)
 add(f'Cover_Giant_{i:02}_High',model,x,z,deg+180+rng.uniform(-25,25),GIANT[model],f'giant{i}')
# Islands of two to four human-scale exhibits; the lanes between islands stay open.
islands=[(10.8,20,3),(13.2,65,3),(11.4,140,4),(16.8,100,3),(12.2,205,3),(19.4,180,3),(11.0,320,3),(15.5,290,4),(20.5,235,3),(21.6,60,3),(18.9,335,3),(14.6,20,2)]
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
  if model in ('Sarcophagus','FallenColumn') and count>2:model=['FuneraryUrn','FallenVisage'][i%2]
  a=math.radians(deg+i*360/count+rng.uniform(-30,30));d=rng.uniform(1.3,2.2)
  scale=rng.uniform(.92,1.06);sz=sizes[model];sz=(sz[0]*scale,sz[1],sz[2]*scale)
  add(f'Cover_Island_{n:02}_{i}_'+('High' if high else 'Low'),model,cx+d*math.sin(a),cz+d*math.cos(a),math.degrees(a)+rng.uniform(-30,30),sz,f'island{n}')

# Conservative circles enclose each rotated box. Neighbours inside one island may
# stand close; anything else keeps a lane wide enough to be caught in.
GAP_ISLAND=1.5;GAP_LANE=3.0
def radius_of(r): return r['radius'] if 'radius' in r else math.hypot(r['size']['x'],r['size']['z'])/2
def needed(a,b):
 if 'radius' in a or 'radius' in b:return PEDESTAL_GAP
 return GAP_ISLAND if a['group']==b['group'] else GAP_LANE
def gap_of(a,b):
 dx=a['position']['x']-b['position']['x'];dz=a['position']['z']-b['position']['z']
 return math.hypot(dx,dz)-radius_of(a)-radius_of(b),dx,dz
def fixed(r): return 'radius' in r or 'SpawnScreen' in r['name'] or 'Column' in r['name']
WALL_RADIUS=28.08
# The dais takes part in the push as an immovable circle; the wall clamps radially.
rows.append(dict(name='Dais',position=dict(x=0,y=0,z=0),radius=PEDESTAL_RADIUS,group='dais'))
# Tall exhibits keep their distance from the dais even after the push: from 2 m up a statue
# 4 m from the keeper's eye eclipses a wedge wider than the beam, and the hall behind it goes blind.
TALL_MIN_RADIUS=11.5
def clamp(r):
 if 'radius' in r:return
 d=math.hypot(r['position']['x'],r['position']['z']);limit=WALL_RADIUS-1.0-radius_of(r)
 if d>limit:r['position']['x']*=limit/d;r['position']['z']*=limit/d;return
 if r['size']['y']>1.0 and 'SpawnScreen' not in r['name']:
  floor_r=TALL_MIN_RADIUS+radius_of(r)
  if d<floor_r and d>0:r['position']['x']*=floor_r/d;r['position']['z']*=floor_r/d
# Relax every offending pair a little per sweep (Jacobi style); one-worst-pair
# updates oscillate once islands and lanes compete.
for sweep in range(600):
 moved=False
 for i,a in enumerate(rows):
  for b in rows[i+1:]:
   g,dx,dz=gap_of(a,b);short=needed(a,b)-g
   if short<=0:continue
   moved=True;d=math.hypot(dx,dz) or 1.0;push=(short+.03)*.5
   if d<.01:dx,dz=1.0,0.0;d=1.0
   wa=0 if fixed(a) else (1 if fixed(b) else .5);wb=0 if fixed(b) else (1 if fixed(a) else .5)
   a['position']['x']+=dx/d*push*wa;a['position']['z']+=dz/d*push*wa
   b['position']['x']-=dx/d*push*wb;b['position']['z']-=dz/d*push*wb
   clamp(a);clamp(b)
 if not moved:break
rows.remove(next(r for r in rows if 'radius' in r))
minimum=(999,None);lane=(999,None)
for i,a in enumerate(rows):
 for b in rows[i+1:]:
  g,_,_=gap_of(a,b)
  if g<minimum[0]:minimum=(g,(a['name'],b['name']))
  if a['group']!=b['group'] and g<lane[0]:lane=(g,(a['name'],b['name']))
assert minimum[0]>=1.44,minimum
assert lane[0]>=2.9,lane
pedestal=min(math.hypot(r['position']['x'],r['position']['z'])-radius_of(r)-PEDESTAL_RADIUS for r in rows)
assert pedestal>=PEDESTAL_GAP-.02,pedestal
outer=max(math.hypot(r['position']['x'],r['position']['z'])+radius_of(r) for r in rows)
assert outer<=WALL_RADIUS-1.0,outer
nearest_tall=min(math.hypot(r['position']['x'],r['position']['z'])-radius_of(r) for r in rows if r['size']['y']>1.0 and 'SpawnScreen' not in r['name'])
assert nearest_tall>=TALL_MIN_RADIUS-.05,nearest_tall
for r in rows: del r['group']
OUT.write_text(json.dumps(dict(referenceRadius=WALL_RADIUS,covers=rows),indent=2)+'\n')
print(json.dumps(dict(covers=len(rows),high=sum(r['name'].endswith('High') for r in rows),minimumGap=round(minimum[0],2),minimumLane=round(lane[0],2),pedestalGap=round(pedestal,2),outermost=round(outer,2))))
