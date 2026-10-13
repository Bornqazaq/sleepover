"""Art direction pass on Bruno v5's existing vertices and UVs. No rig rebuild."""
import bpy, math, json
import numpy as np
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'art-source/BrunoMeshy/polish-v6'

def smooth(a,b,x):
 t=max(0.,min(1.,(x-a)/(b-a)));return t*t*(3-2*t)
def g(x,center,width):return math.exp(-((x-center)/width)**2)
def basis(mesh):
 attr=mesh.attributes.get('BrunoV6Basis')
 if attr is None:
  attr=mesh.attributes.new('BrunoV6Basis','FLOAT_VECTOR','POINT')
  coords=np.empty(len(mesh.vertices)*3,dtype=np.float32);mesh.vertices.foreach_get('co',coords);attr.data.foreach_set('vector',coords)
 coords=np.empty(len(mesh.vertices)*3,dtype=np.float32);attr.data.foreach_get('vector',coords)
 return coords.reshape(-1,3)
def parts(name):return json.loads((OUT/(name+'-islands.json')).read_text())
def put(ob,points):ob.data.vertices.foreach_set('co',np.asarray(points,dtype=np.float32).ravel());ob.data.update()

def head_shape(p):
 x,y,z=map(float,p);a=abs(x);sign=1 if x>=0 else -1
 # Seat the ears into the skull. Thick flared bases and recessed bowls replace disks.
 ear=g(a,.311,.13)*g(y,1.247,.13)*smooth(1.365,1.43,z)
 if ear>.001:
  x+=sign*(.012*ear+.045*(z-1.43)*ear)
  y+=(y-1.24)*.40*ear-.012*ear
  z-=.019*ear
  top=smooth(1.49,1.61,z)*ear
  x-=sign*(a-.311)*.14*top
 # Brow ridge casts a shaped shadow while the eye itself stays small.
 brow=g(a,.285,.128)*g(y,1.56,.17)*g(z,1.37,.068)
 y+=.027*brow;z+=(.009+.018*(a-.27)/.13)*brow
 # Lower orbital rim and a cheek-to-mandible transition, not a spherical cheek insert.
 orbit=g(a,.29,.105)*g(y,1.575,.13)*g(z,1.263,.043)
 y+=.011*orbit
 cheek=g(a,.27,.15)*g(y,1.52,.23)*g(z,1.04,.15)
 x+=sign*.015*cheek;z-=.018*cheek
 # Fill the rear corners around the jaw hinge, leaving the front muzzle intact.
 hinge=g(a,.23,.14)*g(y,1.53,.15)*g(z,.96,.13)
 z-=.030*hinge
 return (x,y,z)

def head():
 body=bpy.data.objects['Bruno_Body'];base=basis(body.data);points=base.copy();islands=parts(body.name)
 for vi in islands[0]['ids']:points[vi]=head_shape(base[vi])
 # Ear bowls follow the shell, then deepen at their centre.
 for part in islands[10:12]:
  for vi in part['ids']:
   x,y,z=head_shape(base[vi]);a=abs(x)
   sign=1 if x>0 else -1
   x=sign*(.321+(a-.321)*.81);z=1.467+(z-1.467)*.84
   center=g(abs(x),.321,.055)*g(z,1.47,.066)
   y-=.045*center*smooth(1.22,1.29,y)
   # Bury a flared lower concha into the skull; keep the rim leaning out/back.
   seat=1-smooth(1.435,1.493,z)
   x=sign*(.321+(abs(x)-.321)*(1+.18*seat)+.013*smooth(1.46,1.55,z))
   y-=.024*seat+.008*smooth(1.47,1.55,z)
   z-=.048*seat
   points[vi]=(x,y,z)
 # Upper lids slope toward the nose, without inflating the eyeballs.
 for index in (12,14):
  for vi in islands[index]['ids']:
   x,y,z=base[vi];points[vi]=(x,y+.004,z+.004+.007*(abs(x)-.29)/.04)
 put(body,points)
 details=bpy.data.objects['Bruno_Details'];base=basis(details.data);points=base.copy();islands=parts(details.name)
 # Reshape the existing mandible: narrow the front, bury the rami in the cheeks,
 # flatten the chin and close the neutral mouth gap. UV coordinates stay intact.
 for vi in islands[0]['ids']:
  x,y,z=map(float,base[vi]);back=1-smooth(1.50,1.87,y)
  x*=.94-.04*back
  y-=.022*smooth(1.78,1.99,y)
  z=.941+(z-.855)*.77+.12*back
  points[vi]=(x,y,z)
 for index in (2,4,5,32,33):
  for vi in islands[index]['ids']:
   x,y,z=map(float,base[vi]);y-=.012*smooth(1.74,1.99,y)
   z+=.064+.025*(1-smooth(1.58,1.9,y))
   points[vi]=(x,y,z)
 # Match the two existing lip centre lines, including their rear commissures.
 # The old lower U was further forward and read as a separate smiling plate.
 upper_ids=islands[3]['ids'];lower_ids=islands[4]['ids']
 for i in range(0,len(lower_ids),8):
  upper=base[upper_ids[i:i+8]].mean(axis=0)
  lower=base[lower_ids[i:i+8]].mean(axis=0)
  for vi in lower_ids[i:i+8]:points[vi]=upper+(base[vi]-lower)*.66+np.array((0,-.003,-.011))
 # Shorter seated upper canines: visible when roaring, just their tips at rest.
 for index in (30,31):
  for vi in islands[index]['ids']:
   x,y,z=map(float,base[vi]);t=max(0,min(1,(1.029-z)/.179))
   z=1.027-(1.029-z)*.64;y-=.031+.008*t;x*=.86
   points[vi]=(x,y,z)
 # Hide the tooth-row grin beneath the upper lip; teeth remain present in the mouth.
 for index in range(38,44):
  for vi in islands[index]['ids']:
   x,y,z=base[vi];points[vi]=(x,y-.018,z+.017)
 put(details,points)
 bpy.data.objects['BrunoRig'].data.pose_position='REST'
 print('V6_HEAD_RESHAPED',len(points))

if __name__=='__main__':head()

def limbs():
 import bmesh
 head()
 body=bpy.data.objects['Bruno_Body'];base=np.array([v.co[:] for v in body.data.vertices]);points=base.copy();islands=parts(body.name)
 def volume(p):
  bm=bmesh.new();bm.from_mesh(body.data);bm.verts.ensure_lookup_table()
  for v,c in zip(bm.verts,p):v.co=c
  result=bm.calc_volume(signed=False);bm.free();return result
 target_volume=volume(base)
 for vi in islands[0]['ids']:
  x,y,z=map(float,base[vi]);a=abs(x);sign=1 if x>=0 else -1
  hind=(1-smooth(-.30,.02,y))*smooth(.30,.62,z)
  outer=smooth(.43,.84,a)*hind
  # Redistribute the spherical haunch into a sloped thigh and a fuller lower leg.
  x-=sign*.072*g(y,-.97,.39)*g(z,1.00,.26)*outer
  x+=sign*.055*g(y,-.55,.23)*g(z,.90,.31)*smooth(.43,.74,a)*hind
  x+=sign*.037*g(y,-.98,.51)*g(z,.57,.21)*smooth(.43,.78,a)*smooth(.25,.40,z)
  y-=.037*g(y,-1.22,.21)*g(z,.95,.35)*outer
  y+=.024*g(y,-.74,.27)*g(z,.75,.32)*outer
  y+=.035*g(y,-1.42,.22)*g(z,.90,.35)*outer
  # Short knuckle rolls seat the claws in the toe tissue, above the unchanged sole.
  for limb_y in (.855,-.565):
   for toe in range(-2,3):
    cx=sign*(.671+toe*.091);cy=limb_y-.016*abs(toe)
    w=g(x,cx,.043)*g(y,cy,.071)*g(z,.15,.063)*smooth(.045,.10,z)
    y+=.018*w;z+=.010*w
  points[vi]=(x,y,z)
 for island in islands[17:37]:
  ids=island['ids'];old=base[ids];cx=float(old[:,0].mean());sign=1 if cx>0 else -1
  toe=round((abs(cx)-.671)/.091);front=old[:,1].mean()>0
  root=float(old[:,1].min());span=float(np.ptp(old[:,1]));factor=1+.045*math.sin(toe*1.7+sign*.8+(0 if front else 1.3))
  for vi in ids:
   x,y,z=map(float,base[vi]);t=max(0,min(1,(y-root)/max(.001,span)))
   y=root+(y-root)*factor
   x+=sign*toe*.0035*t*t
   z+=.010*math.sin(math.pi*t)-.006*t*t
   points[vi]=(x,y,z)
 # Preserve overall volume by carrying mass into the rump's central saddle,
 # not restoring the lateral ball that was just reshaped.
 normals=np.array([v.normal[:] for v in body.data.vertices]);mask=np.zeros(len(points))
 for vi in islands[0]['ids']:
  x,y,z=base[vi];mask[vi]=g(y,-.94,.52)*g(z,1.15,.42)*(1-smooth(.46,.77,abs(x)))*smooth(.55,.85,z)
 offset=normals*mask[:,None];low=0.;high=.12
 if volume(points)<target_volume:
  for _ in range(14):
   mid=(low+high)/2
   if volume(points+offset*mid)<target_volume:low=mid
   else:high=mid
  points+=offset*((low+high)/2)
 put(body,points)
 (OUT/'sculpt-metrics.json').write_text(json.dumps({'body_volume_before_limbs':target_volume,'body_volume_after_limbs':volume(points),'max_displacement':float(np.linalg.norm(points-base,axis=1).max()),'body_vertices':len(points),'uv_changed':False},indent=2))
 print('V6_LIMBS_REFINED',volume(points)/target_volume)
