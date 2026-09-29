#!/usr/bin/env python3
"""Check routes, camera room, and an unavoidable exposed final approach."""
import heapq,json,math,pathlib
ROOT=pathlib.Path(__file__).resolve().parents[1]
data=json.loads((ROOT/'igruha/Assets/_Project/Art/CryingAngels/CA_NightLayout.json').read_text())
R=data['referenceRadius']; STEP=.3; PAD=.4
boxes=[]
for row in data['covers']:
 p=row['position'];s=row['size'];a=math.radians(row['yaw'])
 boxes.append((p['x'],p['z'],math.cos(a),math.sin(a),s['x']/2,s['z']/2))
def blocked(x,z,pad=PAD):
 if x*x+z*z>(R-pad)**2:return True
 for cx,cz,c,s,hx,hz in boxes:
  dx=x-cx;dz=z-cz
  if abs(dx*c-dz*s)<hx+pad and abs(dx*s+dz*c)<hz+pad:return True
 return False
N=math.ceil(R/STEP); free=set()
for i in range(-N,N+1):
 for j in range(-N,N+1):
  if not blocked(i*STEP,j*STEP):free.add((i,j))
# Start at the interaction area, not inside the pedestal collider.
start=(0,0); distances={start:0}; queue=[(0,start)]
while queue:
 cost,u=heapq.heappop(queue)
 if cost!=distances[u]:continue
 for dx,dz in [(1,0),(-1,0),(0,1),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)]:
  v=(u[0]+dx,u[1]+dz)
  if v not in free:continue
  if dx and dz and ((u[0]+dx,u[1]) not in free or (u[0],u[1]+dz) not in free):continue
  new=cost+STEP*math.hypot(dx,dz)
  if new<distances.get(v,float('inf')):distances[v]=new;heapq.heappush(queue,(new,v))
paths=[]
for i in range(8):
 a=math.radians(i*45);r=R*.86;x,z=r*math.sin(a),r*math.cos(a)
 assert not blocked(x,z),('spawn blocked',i)
 for back in range(15):
  camera_r=r+back*.3
  assert not blocked(camera_r*math.sin(a),camera_r*math.cos(a)),('spawn camera blocked',i,back)
 u=(round(x/STEP),round(z/STEP));assert u in distances,('unreachable spawn',i)
 paths.append(round(distances[u],1))
open_radius=12.5
for deg in range(360):
 a=math.radians(deg)
 for radius in [i*STEP for i in range(8,int(open_radius/STEP)+1)]:
  assert not blocked(radius*math.sin(a),radius*math.cos(a),pad=0),('covered inner approach',deg,radius)
# Cover cannot occlude a ray to a crouched player inside a disc containing no cover.
# The actual crouching capsule and server/client freeze are also checked in AngelsHuntCheck.
print(json.dumps({'covers':len(boxes),'radius':R,'reachable_spawns':len(paths),'shortest_routes_m':paths,
 'unavoidably_exposed_final_approach_m':open_radius-1.08,'clear_spawn_cameras':8,'tested_body_diameter':PAD*2}))
