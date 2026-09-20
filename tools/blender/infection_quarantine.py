"""Original quarantine courtyard kit. Run through Blender MCP; preserves other scenes.
Coordinates are authored in Unity metres and converted to Blender at mesh creation.
"""
import bpy, math, random, json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'igruha/Assets/_Project/Art/Minigames/Infection/Quarantine'
SOURCE=ROOT/'igruha/Assets/_Project/Art/Source/Infection'
OUT.mkdir(parents=True,exist_ok=True); SOURCE.mkdir(parents=True,exist_ok=True)
scene=bpy.data.scenes.get('Infection_Quarantine') or bpy.data.scenes.new('Infection_Quarantine')
bpy.context.window.scene=scene
for ob in list(scene.objects): bpy.data.objects.remove(ob,do_unlink=True)
rng=random.Random(210918)
COLORS={'Rust':(.38,.135,.06),'Orange':(.63,.255,.10),'Yellow':(.82,.53,.12),'Blue':(.26,.39,.42),'Red':(.47,.12,.075),'Iron':(.085,.09,.083),'Steel':(.32,.32,.27),'Wood':(.33,.22,.12),'Sand':(.55,.47,.32),'Concrete':(.41,.38,.31),'Plaster':(.56,.51,.41),'Ivory':(.72,.67,.53),'Glass':(.065,.095,.10),'Paper':(.69,.66,.52),'Soot':(.095,.073,.057),'Tile':(.37,.17,.105),'TileBlue':(.12,.205,.245),'Brick':(.34,.19,.13),'Rubber':(.065,.063,.056)}
mats={}
for name,c in COLORS.items():
 m=bpy.data.materials.get('INF_'+name) or bpy.data.materials.new('INF_'+name); m.diffuse_color=(*c,1); m.use_nodes=True
 bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED'); bs.inputs['Base Color'].default_value=(*c,1); bs.inputs['Roughness'].default_value=.88
 mats[name]=m
class Mesh:
 def __init__(self): self.parts={}
 def face(self,pts,mat):
  v,f=self.parts.setdefault(mat,([],[])); n=len(v); v.extend((p[0],-p[2],p[1]) for p in pts); f.append(tuple(range(n,n+len(pts))))
 def box(self,c,s,mat,angle=0):
  x,y,z=c; a,b,d=[v*.5 for v in s]; co=math.cos(angle); si=math.sin(angle)
  pts=[(x+u*co+w*si,y+v,z-u*si+w*co) for u,v,w in [(-a,-b,-d),(a,-b,-d),(a,b,-d),(-a,b,-d),(-a,-b,d),(a,-b,d),(a,b,d),(-a,b,d)]]
  for face in [(0,3,2,1),(4,5,6,7),(0,4,7,3),(1,2,6,5),(3,7,6,2),(0,1,5,4)]: self.face([pts[i] for i in face],mat)
 def rod(self,a,b,r,mat,n=8,r2=None):
  a=Vector(a); b=Vector(b); axis=(b-a).normalized(); u=axis.cross(Vector((0,1,0)))
  if u.length<.01:u=axis.cross(Vector((1,0,0)))
  u.normalize(); v=axis.cross(u); r2=r if r2 is None else r2
  rings=[[p+(u*math.cos(j*math.tau/n)+v*math.sin(j*math.tau/n))*radius for j in range(n)] for p,radius in [(a,r),(b,r2)]]
  self.face(list(reversed(rings[0])),mat); self.face(rings[1],mat)
  for j in range(n): k=(j+1)%n; self.face([rings[0][j],rings[0][k],rings[1][k],rings[1][j]],mat)
 def ring(self,c,r,t,mat,axis='y',n=32):
  def point(a,h,radius):
   x=math.cos(a)*radius; z=math.sin(a)*radius
   if axis=='z':return(c[0]+x,c[1]+z,c[2]+h)
   if axis=='x':return(c[0]+h,c[1]+x,c[2]+z)
   return(c[0]+x,c[1]+h,c[2]+z)
  for j in range(n):
   a=j*math.tau/n; b=(j+1)*math.tau/n
   for rad,h,rad2,h2 in [(r,-t/2,r,t/2),(r-t,t/2,r-t,-t/2),(r,t/2,r-t,t/2),(r-t,-t/2,r,-t/2)]:
    self.face([point(a,h,rad),point(b,h,rad),point(b,h2,rad2),point(a,h2,rad2)],mat)
 def export(self,name):
  bpy.ops.object.select_all(action='DESELECT'); obs=[]
  for mat,(verts,faces) in self.parts.items():
   mesh=bpy.data.meshes.new(name+'_'+mat);mesh.from_pydata(verts,[],faces);mesh.materials.append(mats[mat]);mesh.update()
   ob=bpy.data.objects.new(name+'_'+mat,mesh);scene.collection.objects.link(ob);ob.select_set(True);obs.append(ob)
   # World-space surface coordinates, retained for conventional URP textures.
   uv=mesh.uv_layers.new(name='UVMap')
   for poly in mesh.polygons:
    axis=max(range(3),key=lambda a:abs(poly.normal[a])); axes=[a for a in range(3) if a!=axis]
    for li in poly.loop_indices:
     p=mesh.vertices[mesh.loops[li].vertex_index].co;uv.data[li].uv=(p[axes[0]],p[axes[1]])
  bpy.context.view_layer.objects.active=obs[0]
  bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
  print(name,sum(len(x.data.polygons) for x in obs),'faces')
  return obs

def barrel(m,x,y,z):
 m.rod((x,y,z),(x,y+1.12,z),.43,'Orange',16)
 for h in (.06,.25,.83,1.08):m.ring((x,y+h,z),.45,.055,'Rust',n=16)
 m.box((x,y+.57,z-.427),(.38,.35,.022),'Yellow');m.rod((x+.18,y+1.12,z),(x+.18,y+1.15,z),.065,'Iron')
def tire(m,x,y,z):
 m.ring((x,y+.17,z),.57,.27,'Rubber',n=20)
 for i in range(20):
  a=i*math.tau/20;m.box((x+.55*math.sin(a),y+.17,z+.55*math.cos(a)),(.1,.29,.035),'Soot',a)
def weed(m,x,z,scale=1):
 for i in range(6):
  a=rng.random()*math.tau;h=rng.uniform(.18,.55)*scale;dx=math.cos(a)*.22*scale;dz=math.sin(a)*.22*scale
  m.face([(x-.018,.015,z),(x+dx,h,z+dz),(x+.018,.015,z+.014)],'Wood')

# First representative export, and optional stop for importer scale/material verification.
m=Mesh();barrel(m,0,0,0);m.export('Barrel')
if globals().get('INFECTION_SAMPLE_ONLY',False):
 print('Sample exported; full generation deferred until Unity verification.')
else:
 # Carousel: deck with radial sectors and worn railings.
 m=Mesh();m.rod((0,.06,0),(0,.4,0),3.98,'Rust',64)
 for j in range(12):
  a=j*math.tau/12;b=(j+1)*math.tau/12
  m.face([(0,.408,0),(3.91*math.cos(b),.408,3.91*math.sin(b)),(3.91*math.cos(a),.408,3.91*math.sin(a))],['Tile','Blue','Yellow'][j%3])
  m.rod((0,.42,0),(3.88*math.cos(a),.42,3.88*math.sin(a)),.023,'Iron',5)
 for j in range(6):
  a=j*math.tau/6
  p=lambda r,h:(r*math.cos(a),h,r*math.sin(a))
  m.rod(p(.48,.44),p(.48,1.4),.065,'Yellow');m.rod(p(.48,1.4),p(2.95,1.4),.065,'Red');m.rod(p(2.95,1.4),p(2.95,.44),.065,'Red')
  m.box(p(2.9,.72),(1.22,.12,.55),'Wood',-a)
 m.rod((0,.4,0),(0,1.58,0),.15,'Yellow',12);m.ring((0,.43,0),4.03,.10,'Iron',n=64);m.export('Carousel')
 # Slide matches physical deck, step tops and chute inclination.
 m=Mesh();m.box((0,2.85,1.5),(2.8,.3,3),'Yellow')
 for x in (-1.2,1.2):
  m.rod((x,0,1.5),(x,3.6,1.5),.12,'Rust');m.rod((x,3.55,.1),(x,3.55,3),.07,'Yellow')
 for i in range(8):
  h=3-.35*(i+1);z=3+.45*i+.225;m.box((0,h-.08,z),(2.4,.16,.45),'Steel')
  for x in(-1.16,1.16):m.rod((x,h,z),(x,h+.72,z),.05,'Rust')
 for x in(-1.16,1.16):m.rod((x,3.37,3.225),(x,.92,6.375),.065,'Yellow')
 run=3/math.tan(math.radians(50))
 for x1,x2 in[(-1.2,1.2)]:m.face([(x1,3,0),(x2,3,0),(x2,.05,-run),(x1,.05,-run)],'Yellow')
 for x in(-1.2,1.2):
  m.face([(x-.07,3.5,0),(x+.07,3.5,0),(x+.07,.55,-run),(x-.07,.55,-run)],'Orange')
  m.face([(x,3,0),(x,3.5,0),(x,.55,-run),(x,.05,-run)],'Yellow')
 # Rounded tubular lips, flare at the exit, worn centre and metal support braces.
 for side in(-1,1):
  m.rod((side*1.2,3.5,0),(side*1.2,.55,-run),.09,'Yellow',10)
  for j in range(8):
   a=j/8;b=(j+1)/8
   m.rod((side*(1.2+a*.15),.55*(1-a)**2,-run-a*.65),(side*(1.2+b*.15),.55*(1-b)**2,-run-b*.65),.09,'Yellow',8)
  m.rod((side*1.15,.12,2.8),(side*1.15,2.75,.2),.065,'Rust')
 for j in range(15):
  t=.15+j*.045;x=rng.uniform(-.8,.8)
  m.face([(x,3*(1-t)+.025,-run*t),(x+.10,3*(1-t)+.025,-run*t),(x+.08,3*(1-t-.09)+.025,-run*(t+.09)),(x-.02,3*(1-t-.09)+.025,-run*(t+.09))],'Sand')
 m.face([(-1.2,.05,-run),(1.2,.05,-run),(1.35,.015,-run-.65),(-1.35,.015,-run-.65)],'Yellow')
 m.export('Slide')
 m=Mesh()
 for x in(-4,4):
  for z in(-1,1):m.rod((x,0,z),(x,3.2,0),.115,'Rust',10)
  m.rod((x,1,-.69),(x,1,.69),.08,'Yellow')
 m.rod((-4.2,3.2,0),(4.2,3.2,0),.14,'Yellow',12);m.export('SwingFrame')
 m=Mesh();m.box((0,0,0),(1.2,.25,.8),'Rubber')
 for x in(-.47,.47):
  m.rod((x,.1,0),(x,2.6,0),.026,'Steel',6)
  for i in range(18):m.ring((x,.19+i*.13,0),.064,.016,'Iron',axis='z',n=8)
 m.export('SwingSeat')
 # Rounded rectangular culvert: exact 2.2 m clear passage, not a circular pipe that clips heads.
 m=Mesh();profile=[(-1.3,0),(-1.3,1.6),(-1.1,2.18),(-.8,2.4),(.8,2.4),(1.1,2.18),(1.3,1.6),(1.3,0)]
 for a,b in zip(profile,profile[1:]):m.face([(a[0],a[1],-3),(b[0],b[1],-3),(b[0],b[1],3),(a[0],a[1],3)],'Orange')
 for z in(-2.95,-1,1,2.95):
  for a,b in zip(profile,profile[1:]):m.rod((a[0],a[1],z),(b[0],b[1],z),.065,'Rust',6)
 for x in(-1.31,1.31):
  for z in(-2.8,-.9,1.1,2.8):m.rod((x,1,z),(x*1.02,1,z),.047,'Steel',6)
 # Corrugation seams and chipped coating along the visible roof.
 for z in(-2,-.02,2):
  for a,b in zip(profile,profile[1:]):m.rod((a[0]*1.012,a[1]+.018,z),(b[0]*1.012,b[1]+.018,z),.025,'Rust',5)
 for i in range(65):
  x=rng.uniform(-.72,.72);z=rng.uniform(-2.8,2.8);r=rng.uniform(.025,.13)
  m.face([(x,2.408,z),(x+r,2.408,z+.04),(x+r*.7,2.408,z+.18),(x-.04,2.408,z+.09)],'Rust' if i%3 else 'Sand')
 m.export('Tube')
 m=Mesh()
 for x in(-2.5,2.5):
  for z in(-2.5,2.5):m.rod((x,0,z),(x,2.8,z),.085,'Yellow')
 for y in(.25,1.25,2.65):
  for side in(-1,1):
   for lo,hi in ([(-2.5,-.8),(.8,2.5)] if y<2 else [(-2.5,2.5)]):
    m.rod((lo,y,side*2.5),(hi,y,side*2.5),.065,'Red' if y<2 else 'Yellow')
    m.rod((side*2.5,y,lo),(side*2.5,y,hi),.065,'Blue')
 # Colored end panels surround the 1.6 m cross passages; top canopy matches roof collider.
 for side in(-1,1):
  for q in(-1,1):
   m.box((q*1.65,1.3,side*2.5),(1.7,2.6,.2),'Blue' if q<0 else 'Red')
   m.box((side*2.5,1.3,q*1.65),(.2,2.6,1.7),'Yellow' if q<0 else 'Orange')
 # Plank roof and faded geometric play panels make this read as a former children's shelter.
 for i in range(16):m.box((-2.35+i*.31,2.7,0),(.295,.2,5),'Wood' if i%4 else 'Blue')
 for side in(-1,1):
  for q in(-1,1):
   for j in range(3):
    y=.6+j*.66
    m.ring((q*1.65,y,side*2.61),.22,.055,'Yellow',axis='z',n=16)
   for j in range(6):m.box((q*1.65+(-.62+j*.25),.18,side*2.61),(.075,.20,.018),'Rust')
  for q in(-1,1):
   m.face([(side*2.61,.7,q*1.65-.48),(side*2.61,1.6,q*1.65),(side*2.61,.7,q*1.65+.48)],'Blue' if q<0 else 'Ivory')
   m.box((side*2.62,.60,q*1.65),(.03,.12,1.0),'Red')
 # An uneven canvas corner hangs above the passage, clear of heads.
 for j in range(12):
  x=-2.45+j*.2;h=2.28+.12*math.sin(j*.7)
  m.face([(x,2.83,-2.57),(x+.2,2.83,-2.57),(x+.2,h,-2.70),(x,h+.05,-2.70)],'Sand')
 m.export('Climber')
 m=Mesh();m.box((0,.052,0),(9,.10,6),'Sand')
 for x in(-4.5,4.5):m.box((x,.2,0),(.2,.4,6),'Orange')
 for z in(-3,3):m.box((0,.2,z),(9.4,.4,.2),'Orange')
 for i in range(80):
  x=rng.uniform(-4.3,4.3);z=rng.uniform(-2.8,2.8);r=rng.uniform(.05,.22);m.box((x,.109,z),(r,.009,r*.6),'Concrete',rng.random()*6)
 m.export('Sandbox')
 # 4 metre chain-link module with individual diagonal wires, rails and post caps.
 m=Mesh()
 for x in(-2,2):m.rod((x,0,0),(x,2.35,0),.065,'Rust',8);m.box((x,2.36,0),(.18,.05,.16),'Steel')
 for y in(.13,2.2):m.rod((-2,y,0),(2,y,0),.036,'Rust',6)
 for slope in(-1,1):
  for k in range(-26,27):
   b=k*.22;pts=[]
   for x in(-2,2):
    y=slope*x+b
    if .16<=y<=2.17:pts.append((x,y,0))
   for y in(.16,2.17):
    x=(y-b)/slope
    if -2<x<2:pts.append((x,y,0))
   if len(pts)==2:m.rod(*pts,.009,'Iron',4)
 m.export('Fence')
 # Five-storey panel housing, deep window reveals, broken glazing, balconies and exposed masonry.
 for variant in range(2):
  m=Mesh();w=18;h=18+variant*3
  m.box((0,h/2,4),(w,h,8),'Plaster' if variant==0 else 'Concrete')
  m.box((0,.5,-.11),(w,1,.28),'Concrete');m.box((0,h+.13,4),(w+.45,.28,8.5),'Concrete')
  for x in(-6,0,6):m.box((x,h/2,-.065),(.13,h,.15),'Brick')
  for floor in range(5+variant):
   y=2.2+floor*3
   m.box((0,y-1.2,-.09),(w,.12,.17),'Concrete')
   for col in range(6):
    x=-7.5+col*3
    m.box((x,y,-.12),(1.68,1.8,.2),'Iron');m.box((x,y,-.24),(1.38,1.48,.055),'Glass')
    for dx in(-.78,.78):m.box((x+dx,y,-.29),(.09,1.8,.13),'Ivory')
    m.box((x,y,-.31),(.07,1.7,.09),'Steel');m.box((x,y-.87,-.36),(1.85,.13,.48),'Concrete')
    if (floor+col+variant)%4==0:
     m.box((x,y-.85,-1),(2.35,.18,1.9),'Concrete')
     m.box((x,y-.35,-1.9),(2.35,1,.10),'Rust')
     for dx in(-1.1,1.1):m.box((x+dx,y-.35,-1),(.10,1,1.85),'Steel')
    if rng.random()<.38:
     m.box((x,y,-.38),(1.5,.16,.06),'Wood',.13)
     m.face([(x-.64,y+.6,-.36),(x-.35,y-.2,-.37),(x+.67,y+.65,-.36)],'Soot')
    if rng.random()<.25:
     m.box((x,y+1.1,-.13),(2,1,.02),'Soot')
  for i in range(70):
   x=rng.uniform(-8.7,8.7);y=rng.uniform(.5,h-.5)
   m.box((x,y,-.13),(rng.uniform(.2,.9),rng.uniform(.07,.32),.035),'Brick' if i%3 else 'Sand')
  for x in(-8.7,8.7):m.rod((x,0,-.18),(x,h,-.18),.055,'Iron')
  for x in(-5,4):m.box((x,h+.6,4),(1.5,1.2,1.2),'Brick')
  m.export('Apartment'+str(variant+1))
 def vehicle(kind):
  m=Mesh();bus=kind=='Bus';amb=kind=='Ambulance';L=9 if bus else 5.6 if amb else 4.7;W=2.5 if bus else 2.1
  color='Yellow' if bus else 'Ivory' if amb else 'Concrete'
  # Extruded side profile: sloped bonnet, inset windshield, passenger roof and short tail.
  profile=[(-L*.5,.55),(-L*.5,1.04),(-L*.30,1.12),(-L*.18,2.45 if bus else 2.25 if amb else 1.65),(L*.37,2.45 if bus else 2.25 if amb else 1.65),(L*.49,1.18),(L*.49,.55)]
  for side in(-1,1):m.face([(side*W*.48,y,z) for z,y in (profile if side>0 else reversed(profile))],color)
  for (z,y),(z2,y2) in zip(profile,profile[1:]+profile[:1]):m.face([(-W*.48,y,z),(W*.48,y,z),(W*.48,y2,z2),(-W*.48,y2,z2)],color)
  roof=profile[3][1]
  # Front windshield and divided side glazing are proud of the metal by a few centimetres.
  m.face([(-W*.41,1.15,-L*.303),(W*.41,1.15,-L*.303),(W*.41,roof-.13,-L*.197),(-W*.41,roof-.13,-L*.197)],'Glass')
  for side in(-1,1):
   x=side*W*.486
   count=6 if bus else 2 if amb else 3
   for i in range(count):
    z=-L*.12+i*(L*.43/count);ww=L*.36/count
    m.box((x,roof-.46,z),(.035,.64,ww),'Glass')
    m.box((x*1.012,1.12,z),(.055,.05,.23),'Iron')
   m.box((x,1.02,0),(.034,.16,L*.92),'Red' if amb else 'Rust')
   # Door outlines and handle, mirrors, wheel arches and hubs.
   for z in(-L*.25,L*.38):m.box((x*1.006,1.25,z),(.025,1.20,.025),'Rust')
   m.rod((x,roof-.7,-L*.24),(x+side*.27,roof-.7,-L*.27),.035,'Iron')
   m.box((x+side*.30,roof-.65,-L*.27),(.12,.24,.19),'Iron')
   for z in(-L*.31,L*.30):
    m.ring((x,.52,z),.55,.25,'Rubber',axis='x',n=24)
    m.rod((x-side*.12,.52,z),(x+side*.17,.52,z),.29,'Steel',16)
    m.rod((x+side*.17,.52,z),(x+side*.20,.52,z),.11,'Rust',10)
    for j in range(12):
     a=j*math.pi/12;b=(j+1)*math.pi/12
     m.rod((x+side*.06,.52+math.sin(a)*.60,z+math.cos(a)*.60),(x+side*.06,.52+math.sin(b)*.60,z+math.cos(b)*.60),.052,color,6)
  m.box((0,.55,-L*.515),(W+.12,.17,.14),'Iron');m.box((0,.87,-L*.506),(W*.38,.24,.04),'Iron')
  for x in(-W*.34,W*.34):m.box((x,1,-L*.506),(.32,.21,.045),'Ivory')
  for j in range(5):m.box((-.33+j*.165,.88,-L*.532),(.04,.18,.02),'Steel')
  if amb:
   m.box((0,roof+.15,0),(1.35,.18,.32),'Red')
   for side in(-1,1):
    x=side*W*.50;m.box((x,1.5,L*.28),(.045,.6,.17),'Red');m.box((x,1.5,L*.28),(.045,.17,.6),'Red')
  for i in range(25):
   z=rng.uniform(-L*.47,L*.47);x=rng.choice([-1,1])*W*.49
   m.box((x,rng.uniform(.56,1.0),z),(.018,rng.uniform(.03,.12),rng.uniform(.08,.34)),'Rust')
  m.export(kind)
 for name in('Ambulance','Bus','Sedan'):vehicle(name)
 m=Mesh();m.rod((0,0,0),(.18,5,0),.28,'Wood',9,.10)
 for i in range(11):
  a=i*2.3;h=1.8+i*.27;tip=(math.cos(a)*(1.1+i*.12),h+1.8,math.sin(a)*(1+i*.10))
  m.rod((.07,h,0),tip,.1,'Wood',7,.025);m.rod(tip,(tip[0]*1.18,tip[1]+.8,tip[2]*1.2),.025,'Wood',5,.008)
 m.export('Tree')
 m=Mesh()
 for x in(-1.1,1.1):
  for z in(-.24,.24):m.rod((x,0,z),(x,.52,z),.05,'Iron')
  m.rod((x,.4,.28),(x,1.2,.43),.055,'Iron')
 for z in(-.25,-.08,.09,.26):m.box((0,.55,z),(2.8,.09,.14),'Wood')
 for y in(.78,.98,1.18):m.box((0,y,.39),(2.8,.15,.09),'Orange')
 m.export('Bench')
 m=Mesh()
 for row in range(3):
  for i in range(4-row%2):m.box(((i-1.5)*.73+(row%2)*.35,.16+row*.27,0),(.82,.28,.58),'Sand',rng.uniform(-.08,.08))
 m.export('Sandbags')
 m=Mesh()
 for y in(.12,.4):
  for i in range(6):m.box((0,y,-.57+i*.23),(1.8,.1,.16),'Wood')
 for x in(-.65,0,.65):m.box((x,.26,0),(.16,.25,1.35),'Wood')
 m.export('Pallet')
 m=Mesh()
 for y in(0,.32,.64):tire(m,0,y,0)
 m.export('Tires')
 m=Mesh()
 for i in range(18):weed(m,rng.uniform(-1.3,1.3),rng.uniform(-.5,.5))
 m.export('Weeds')
 m=Mesh()
 for i in range(24):
  x=rng.uniform(-1.6,1.6);z=rng.uniform(-1,1);r=rng.uniform(.08,.25)
  m.box((x,.035,z),(r*2,.06,r),'Concrete',rng.random()*6)
 for i in range(7):m.box((rng.uniform(-1.4,1.4),.012,rng.uniform(-1,1)),(.26,.01,.38),'Paper',rng.random()*6)
 m.export('Debris')
 # Fragmented rubber courtyard inside the shared irregular outline.
 layout=json.loads((OUT/'layout.json').read_text());outline=[(p['x'],p['z']) for p in layout['boundary']]
 def inside(x,z,margin=0):
  return all((b[0]-a[0])*(z-a[1])-(b[1]-a[1])*(x-a[0])>margin*math.hypot(b[0]-a[0],b[1]-a[1]) for a,b in zip(outline,outline[1:]+outline[:1]))
 m=Mesh();m.face([(x,-.024,z) for x,z in reversed(outline)],'Concrete')
 for ix in range(32):
  for iz in range(28):
   xx=-22+ix*1.4;zz=-19+iz*1.4;a=math.radians(7)
   x=xx*math.cos(a)+zz*math.sin(a);z=-xx*math.sin(a)+zz*math.cos(a)
   if not inside(x,z,.9):continue
   edge=not inside(x,z,2.5)
   if rng.random() < (.48 if edge else .055):continue
   blue=((x+2)**2+(z-.5)**2<36 and (x+z)>-2) or (x>6 and z>4) or (x<-7 and z<-3)
   mat='TileBlue' if blue else 'Tile'
   m.box((x,-.056,z),(1.365,.1,1.365),mat,a+rng.uniform(-.008,.008))
   if rng.random()<.34:
    d=rng.uniform(-.4,.4);m.face([(x-.58,.003,z+d),(x+.49,.004,z+d+.21),(x+.51,.004,z+d+.24),(x-.58,.003,z+d+.025)],'Soot')
   if rng.random()<.14:m.face([(x+.60,.003,z+.62),(x+.28,.003,z+.62),(x+.6,.003,z+.24)],'Concrete')
 for i in range(100):
  x=rng.uniform(-19,19);z=rng.uniform(-16,16)
  if inside(x,z,.6):m.box((x,.012,z),(rng.uniform(.06,.25),.018,rng.uniform(.1,.28)),'Paper' if i%3==0 else 'Brick',rng.random()*6)
 m.export('Courtyard')
 # Quarantine details: torn cloth, traffic barricade, bucket and forgotten bear.
 m=Mesh()
 for ix in range(12):
  for iy in range(7):
   if iy==0 and ix in (2,7,8):continue
   def pt(i,j):
    x=-1.8+i*.3;y=.3+j*.24
    return (x,y,.10*math.sin(i*.9+j*.23)+.045*math.sin(j*2))
   m.face([pt(ix,iy),pt(ix+1,iy),pt(ix+1,iy+1),pt(ix,iy+1)],'Blue')
 m.export('TornTarp')
 m=Mesh()
 m.box((0,.8,0),(3,.64,.22),'Yellow')
 for x in(-1.1,1.1):
  m.rod((x,0,-.4),(x,1.2,0),.055,'Steel');m.rod((x,0,.4),(x,1.2,0),.055,'Steel')
 for i in range(8):
  x=-1.5+i*.4
  m.face([(x,.48,-.115),(x+.17,.48,-.115),(x+.46,1.12,-.115),(x+.29,1.12,-.115)],'Iron')
 m.export('Barricade')
 m=Mesh()
 m.rod((0,.02,0),(0,.36,0),.19,'Yellow',16,.24);m.ring((0,.36,0),.25,.035,'Rust',n=16)
 for i in range(12):
  a=i*math.pi/12;b=(i+1)*math.pi/12
  m.rod((math.cos(a)*.23,.36+math.sin(a)*.25,0),(math.cos(b)*.23,.36+math.sin(b)*.25,0),.014,'Steel',5)
 m.export('Bucket')
 def ellipsoid(m,c,r,mat):
  for i in range(8):
   for j in range(12):
    def pt(a,b):return(c[0]+r[0]*math.sin(a)*math.cos(b),c[1]+r[1]*math.cos(a),c[2]+r[2]*math.sin(a)*math.sin(b))
    a=i*math.pi/8;b=(i+1)*math.pi/8;u=j*math.tau/12;v=(j+1)*math.tau/12
    m.face([pt(a,u),pt(b,u),pt(b,v),pt(a,v)],mat)
 m=Mesh();ellipsoid(m,(0,.32,0),(.24,.3,.17),'Wood');ellipsoid(m,(0,.66,0),(.23,.23,.2),'Wood')
 for x in(-.18,.18):ellipsoid(m,(x,.85,0),(.1,.1,.07),'Sand')
 for x in(-.25,.25):
  ellipsoid(m,(x,.43,0),(.13,.21,.12),'Wood');ellipsoid(m,(x*.7,.1,-.12),(.14,.11,.19),'Sand')
 ellipsoid(m,(0,.58,-.17),(.13,.10,.07),'Sand')
 for x in(-.09,.09):ellipsoid(m,(x,.70,-.19),(.026,.027,.015),'Iron')
 m.export('Teddy')
 # Rounded sandbags replace the earlier square placeholder geometry.
 m=Mesh()
 for row in range(3):
  for i in range(4-row%2):ellipsoid(m,((i-1.5)*.73+(row%2)*.35,.16+row*.27,0),(.44,.18,.31),'Sand')
 m.export('Sandbags')
 # Broad, irregular dirt islands are flat and cannot snag running players.
 m=Mesh()
 for i in range(16):
  a=i*math.tau/16;b=(i+1)*math.tau/16;r=1+rng.random()*.4;s=1+rng.random()*.4
  m.face([(0,.009,0),(math.cos(b)*s,.009,math.sin(b)*s*.65),(math.cos(a)*r,.009,math.sin(a)*r*.65)],'Sand')
 for i in range(40):
  x=rng.uniform(-1.1,1.1);z=rng.uniform(-.6,.6);r=rng.uniform(.018,.1);m.box((x,.022,z),(r,.02,r),'Concrete')
 m.export('DirtIsland')
 # Street storytelling props, built as actual hollow forms with frame and hardware.
 m=Mesh()
 for i in range(20):
  a=i*math.tau/20;b=(i+1)*math.tau/20
  m.face([(.36*math.cos(a),.08,.36*math.sin(a)),(.36*math.cos(b),.08,.36*math.sin(b)),(.47*math.cos(b),1.1,.47*math.sin(b)),(.47*math.cos(a),1.1,.47*math.sin(a))],'Steel')
  m.rod((.37*math.cos(a),.12,.37*math.sin(a)),(.46*math.cos(a),1.06,.46*math.sin(a)),.018,'Iron',5)
 m.ring((0,1.1,0),.49,.05,'Rust',n=24);m.rod((0,.12,0),(0,.14,0),.37,'Iron',20)
 m.rod((0,.80,0),(0,.82,0),.39,'Soot',16)
 for i in range(14):m.box((rng.uniform(-.3,.3),rng.uniform(.85,1.12),rng.uniform(-.3,.3)),(.23,.04,.17),'Paper',rng.random()*6)
 m.export('TrashCan')
 m=Mesh()
 for x in(-.37,.37):
  m.rod((x,.17,-.52),(x,.17,.61),.035,'Steel');m.rod((x,.17,-.42),(x,1.05,-.52),.035,'Steel')
  for z in(-.44,.48):m.ring((x,.13,z),.115,.07,'Rubber',axis='x',n=12)
 m.rod((-.42,1.12,-.65),(.42,1.12,-.65),.045,'Red')
 for x in(-.42,.42):m.rod((x,1.12,-.65),(x,.96,-.45),.028,'Steel')
 for y in(.55,.67,.79,.91,1.04):
  for x in(-.43,.43):m.rod((x,y,-.47),(x,y,.58),.016,'Steel',5)
  for z in(-.47,.58):m.rod((-.43,y,z),(.43,y,z),.016,'Steel',5)
 for i in range(9):
  z=-.47+i*.13
  for x in(-.43,.43):m.rod((x,.55,z),(x,1.04,z),.014,'Steel',5)
  m.rod((-.43,.55,z),(.43,.55,z),.014,'Rust',5)
 for x in(-.3,-.15,0,.15,.3):
  for z in(-.47,.58):m.rod((x,.55,z),(x,1.04,z),.014,'Steel',5)
 m.box((0,.58,.1),(.6,.10,.5),'Paper');m.export('Cart')
 m=Mesh()
 for x in(-.63,.63):
  m.ring((x,.38,0),.37,.05,'Rubber',axis='z',n=32);m.ring((x,.38,0),.32,.018,'Steel',axis='z',n=32)
  for i in range(12):
   a=i*math.tau/12;m.rod((x,.38,0),(x+math.cos(a)*.31,.38+math.sin(a)*.31,0),.006,'Steel',4)
 for a,b in [((-.63,.38,0),(-.24,.84,0)),((-.24,.84,0),(-.04,.34,0)),((-.04,.34,0),(-.63,.38,0)),((-.24,.84,0),(.41,.91,0)),((.41,.91,0),(-.04,.34,0)),((.41,.91,0),(.63,.38,0)),((-.24,.84,0),(-.28,1,0)),((.41,.91,0),(.38,1.16,0))]:m.rod(a,b,.035,'Rust',8)
 m.box((-.29,1.02,0),(.29,.055,.17),'Rubber');m.rod((.38,1.16,-.27),(.38,1.16,.27),.025,'Steel')
 m.rod((-.04,.34,-.18),(-.04,.34,.18),.025,'Steel');m.box((-.04,.34,.22),(.15,.055,.12),'Rubber')
 m.export('Bicycle')
 exec(compile((ROOT/'tools/blender/infection_ruins.py').read_text(),str(ROOT/'tools/blender/infection_ruins.py'),'exec'))
 exec(compile((ROOT/'tools/blender/infection_finishing.py').read_text(),str(ROOT/'tools/blender/infection_finishing.py'),'exec'))
 # Save a standalone source file containing only this kit, without replacing the user's open file.
 bpy.data.libraries.write(str(SOURCE/'Quarantine.blend'),{scene},fake_user=True)
 (OUT/'palette.json').write_text(json.dumps(COLORS,indent=2))
 for area in bpy.context.screen.areas:
  if area.type=='VIEW_3D':
   area.spaces.active.region_3d.view_location=(0,0,2);area.spaces.active.region_3d.view_distance=65
 print('QUARANTINE KIT COMPLETE',len(scene.objects))
