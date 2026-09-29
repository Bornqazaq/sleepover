"""Authored funerary mosaic and small spent offerings; run through Blender MCP.

Metres, floor at Z=0, Unity conversion matches the existing gallery kit.
Separate source scene/file; does not replace the user's Blender scenes.
"""
import bpy, math, random, json
from pathlib import Path
from mathutils import Quaternion

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT/'igruha/Assets/_Project/Art/CryingAngels'
SOURCE = ROOT/'igruha/Assets/_Project/Art/Source/CryingAngels'
rng = random.Random(67029)
TAU = math.tau
# Replace only this generator's previous isolated scenes, for stable FBX names.
for previous in list(bpy.data.scenes):
 if previous.get('generator')=='crying_angels_paving' or previous.name in ('CA_FuneraryMosaic','CA_FuneraryMosaic.001'):
  for obj in list(previous.objects):
   if len(obj.users_scene)==1:bpy.data.objects.remove(obj,do_unlink=True)
  bpy.data.scenes.remove(previous)
scene = bpy.data.scenes.new('CA_FuneraryMosaic')
scene['generator']='crying_angels_paving'
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'

palette = [
 ('CA_PavingIvory', (.62,.59,.51)), ('CA_PavingAsh', (.33,.38,.41)),
 ('CA_PavingSlate', (.075,.105,.13)), ('CA_PavingInlay', (.27,.23,.15)),
 ('CA_Crevices', (.045,.062,.071)), ('CA_PaleLimestone', (.66,.70,.73)),
 ('CA_AgedBrass', (.34,.23,.105)), ('CA_OxidizedIron', (.052,.075,.09)),
]
materials=[]
for name,color in palette:
 mat=bpy.data.materials.get(name) or bpy.data.materials.new(name)
 mat.diffuse_color=(*color,1); mat.use_nodes=True
 bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
 bs.inputs['Base Color'].default_value=(*color,1)
 bs.inputs['Roughness'].default_value=.72
 materials.append(mat)

class Mesh:
 def __init__(self): self.vertices=[];self.faces=[];self.slots=[]
 def face(self,points,mat):
  # Horizontal inlays must face up after FBX conversion, including clockwise
  # lozenges and crack ribbons. Leave vertical candle sides untouched.
  area=sum(a[0]*b[1]-b[0]*a[1] for a,b in zip(points,points[1:]+points[:1]))
  if area<0 and max(p[2] for p in points)-min(p[2] for p in points)<.03:
   points=list(reversed(points))
  start=len(self.vertices);self.vertices.extend(points)
  self.faces.append(tuple(range(start,start+len(points))));self.slots.append(mat)
 def object(self,name):
  mesh=bpy.data.meshes.new(name);mesh.from_pydata(self.vertices,[],self.faces);mesh.update()
  for mat in materials:mesh.materials.append(mat)
  uv=mesh.uv_layers.new(name='StoneUV')
  for poly,slot in zip(mesh.polygons,self.slots):
   poly.material_index=slot
   for i in poly.loop_indices:
    co=mesh.vertices[mesh.loops[i].vertex_index].co
    uv.data[i].uv=(co.x*.55,co.y*.55)
  obj=bpy.data.objects.new(name,mesh);scene.collection.objects.link(obj)
  return obj

def point(r,a,z=0):return (r*math.cos(a),r*math.sin(a),z)

def sector(mesh,inner,outer,a,b,mat,gap=.014):
 # Chamfered tile corners and open mortar seams, not a flat painted circle.
 inner+=gap;outer-=gap;a+=gap/inner;b-=gap/inner
 bevel=min(.035,(outer-inner)*.12)
 cut=bevel/inner
 pts=[point(inner,a+cut),point(inner+bevel,a),point(outer-bevel,a),point(outer,a+cut)]
 steps=max(1,math.ceil((b-a)*outer/.45))
 pts += [point(outer,a+cut+(b-a-2*cut)*j/steps) for j in range(1,steps+1)]
 pts += [point(outer-bevel,b),point(inner+bevel,b),point(inner,b-cut)]
 pts += [point(inner,b-cut-(b-a-2*cut)*j/steps) for j in range(1,steps)]
 mesh.face(pts,mat)

floor=Mesh()
# Four inlaid borders frame the room; broad alternating marble fields between.
bands=[(2.1,4.7,'rose'),(4.7,5.1,'border'),(5.1,12.0,'field'),
 (12.0,12.55,'border'),(12.55,21.3,'field'),(21.3,21.85,'border'),
 (21.85,32.5,'field'),(32.5,33.2,'border'),(33.2,34.5,'field')]
for inner,outer,kind in bands:
 if kind=='rose':
  for i in range(32):
   a=i*TAU/32;b=(i+1)*TAU/32;mid=(a+b)*.5
   floor.face([point(inner,a),point(outer,mid),point(inner,b)],0 if i%2==0 else 3)
   floor.face([point(inner,a),point(outer,a),point(outer,mid)],2)
   floor.face([point(inner,b),point(outer,mid),point(outer,b)],1)
 elif kind=='border':
  mid=(inner+outer)/2;count=round(TAU*mid/.65)
  for i in range(count):
   a=i*TAU/count;b=(i+1)*TAU/count
   sector(floor,inner,inner+.075,a,b,3,.005)
   sector(floor,inner+.075,outer-.075,a,b,0 if i%2==0 else 2,.008)
   sector(floor,outer-.075,outer,a,b,3,.005)
 else:
  courses=math.ceil((outer-inner)/1.35)
  for j in range(courses):
   r0=inner+(outer-inner)*j/courses;r1=inner+(outer-inner)*(j+1)/courses
   count=round(TAU*((r0+r1)/2)/1.8)
   for i in range(count):
    a=(i+(j%2)*.5)*TAU/count;b=a+TAU/count
    # A narrow pale tile every third course gives a woven stone rhythm.
    slot=0 if (i+j)%4==0 else 1
    if rng.random()<.075:slot=2
    sector(floor,r0,r1,a,b,slot)
    # Small inset lozenges, flush to the paving; never luminous markers.
    if j%3==1 and i%4==2:
     r=(r0+r1)/2;angle=(a+b)/2
     floor.face([point(r-.32,angle,.001),point(r,angle+.23/r,.001),
                 point(r+.32,angle,.001),point(r,angle-.23/r,.001)],3)
# Hairline cracks branch naturally across a few stones; no raised obstacles.
for i in range(110):
 r=rng.uniform(5.2,32);a=rng.uniform(0,TAU);x,y,_=point(r,a)
 angle=rng.uniform(0,TAU)
 for j in range(rng.randint(3,7)):
  angle+=rng.uniform(-.6,.6);length=rng.uniform(.13,.45)
  nx=x+math.cos(angle)*length;ny=y+math.sin(angle)*length
  w=rng.uniform(.009,.018);dx=math.sin(angle)*w;dy=-math.cos(angle)*w
  floor.face([(x-dx,y-dy,.0015),(nx-dx,ny-dy,.0015),
              (nx+dx,ny+dy,.0015),(x+dx,y+dy,.0015)],4)
  x,y=nx,ny
paving=floor.object('CA_FuneraryMosaic')

def cylinder(mesh,x,y,r,h,mat,n=10):
 top=[(x+r*math.cos(i*TAU/n),y+r*math.sin(i*TAU/n),h) for i in range(n)]
 mesh.face(top,mat)
 for i in range(n):
  a=top[i];b=top[(i+1)%n]
  mesh.face([(a[0],a[1],.008),(b[0],b[1],.008),b,a],mat)

offering=Mesh()
# An old wreath, three spent votive candles and tiny flakes of broken relief.
for i in range(28):
 if i in (4,5,17,18,19):continue
 a=i*TAU/28;b=(i+.8)*TAU/28
 offering.face([point(.32,a,.01),point(.41,a,.01),point(.41,b,.01),point(.32,b,.01)],6)
 cx,cy,_=point(.44,a)
 offering.face([(cx-.06,cy,.012),(cx,cy-.035,.018),(cx+.085,cy,.012),(cx,cy+.035,.018)],7)
for x,y,r,h in [(-.24,.14,.07,.105),(.10,.22,.052,.15),(.19,-.09,.06,.065)]:
 cylinder(offering,x,y,r,h,5)
 cylinder(offering,x,y,.007,h+.009,7,6)
for i in range(13):
 x=rng.uniform(-.75,.75);y=rng.uniform(-.6,.6);s=rng.uniform(.035,.1)
 offering.face([(x-s,y-s,.01),(x+s,y-s,.012),(x+s*.5,y+s,.025),(x-s*.8,y+s*.4,.008)],5)
votives=offering.object('CA_SpentOfferings')

manifest=[]
for obj in (paving,votives):
 bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
 bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/f'{obj.name}.fbx'),use_selection=True,
  object_types={'MESH'},use_mesh_modifiers=True,mesh_smooth_type='FACE',add_leaf_bones=False,
  bake_anim=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_space_transform=True,path_mode='RELATIVE')
 manifest.append(dict(name=obj.name,vertices=len(obj.data.vertices),triangles=sum(len(p.vertices)-2 for p in obj.data.polygons)))
# Keep a separate inspectable .blend without copying unrelated open scenes.
SOURCE.mkdir(parents=True,exist_ok=True)
bpy.data.libraries.write(str(SOURCE/'CA_FuneraryMosaic.blend'),{scene},fake_user=True)
for screen in bpy.data.screens:
 for area in screen.areas:
  if area.type=='VIEW_3D':
   area.spaces.active.shading.color_type='MATERIAL'
   area.spaces.active.region_3d.view_distance=76
   area.spaces.active.region_3d.view_location=(0,0,0)
   area.spaces.active.region_3d.view_rotation=Quaternion((1,0,0,0))
   area.spaces.active.region_3d.view_perspective='ORTHO'
print(json.dumps(dict(blender=bpy.app.version_string,assets=manifest)))
