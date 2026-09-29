"""Original rounded sumo hall kit. Run in Blender through MCP; metres, fixed seed.
Only the owned Sumo_Dojo scene is rebuilt. Exports contain no collision or cameras.
"""
import bpy, math, random
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'igruha/Assets/_Project/Art/Minigames/SumoRing/Models'
SOURCE=ROOT/'tools/blender/source/sumo_ring'
OUT.mkdir(parents=True,exist_ok=True); SOURCE.mkdir(parents=True,exist_ok=True)
rng=random.Random(645)
scene=bpy.data.scenes.get('Sumo_Dojo') or bpy.data.scenes.new('Sumo_Dojo')
for o in list(scene.objects): bpy.data.objects.remove(o,do_unlink=True)
bpy.context.window.scene=scene
scene.unit_settings.system='METRIC'
parts=[]; finished=[]
def mat(name,color,rough=.8,metal=0):
 m=bpy.data.materials.get('SM_'+name) or bpy.data.materials.new('SM_'+name)
 m.diffuse_color=(*color,1);m.use_nodes=True
 n=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
 n.inputs['Base Color'].default_value=(*color,1);n.inputs['Roughness'].default_value=rough;n.inputs['Metallic'].default_value=metal
 return m
wood=mat('Wood',(.24,.095,.042));edge=mat('WoodEdge',(.40,.19,.08));straw=mat('Straw',(.68,.45,.19));strawLight=mat('StrawLight',(.82,.62,.30));strawDark=mat('StrawShadow',(.45,.27,.10))
rope=mat('Rope',(.77,.65,.43));paper=mat('Paper',(.95,.75,.39));ivory=mat('Ivory',(.84,.77,.59));ink=mat('Ink',(.07,.045,.03));iron=mat('Iron',(.105,.095,.085),.48,.45);bronze=mat('Bronze',(.26,.25,.135),.48,.65)
cloth=mat('Cloth',(.61,.43,.21));red=mat('Vermilion',(.43,.095,.042));spectator=mat('Audience',(.105,.105,.12))
def add(o,m):
 o.data.materials.append(m);parts.append(o);return o

def box(name,p,size,m=wood,bevel=.04):
 bpy.ops.mesh.primitive_cube_add(size=1,location=p);o=bpy.context.object;o.name=name;o.dimensions=size
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if bevel:
  b=o.modifiers.new('Rounded','BEVEL');b.width=bevel;b.segments=2;bpy.ops.object.modifier_apply(modifier=b.name)
 n=o.modifiers.new('Normals','WEIGHTED_NORMAL');bpy.ops.object.modifier_apply(modifier=n.name)
 return add(o,m)
def ball(name,p,size,m):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=10,radius=1,location=p);o=bpy.context.object;o.name=name;o.scale=size
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 for f in o.data.polygons:f.use_smooth=True
 return add(o,m)
def cyl(name,p,r,h,m,vertices=24):
 bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=r,depth=h,location=p);o=bpy.context.object;o.name=name
 b=o.modifiers.new('Rim','BEVEL');b.width=min(.025,h*.1);b.segments=2;bpy.ops.object.modifier_apply(modifier=b.name)
 n=o.modifiers.new('Normals','WEIGHTED_NORMAL');bpy.ops.object.modifier_apply(modifier=n.name)
 return add(o,m)
def tube(name,pts,r,m):
 c=bpy.data.curves.new(name,'CURVE');c.dimensions='3D';c.resolution_u=1;c.bevel_depth=r;c.bevel_resolution=1
 sp=c.splines.new('POLY');sp.points.add(len(pts)-1)
 for v,p in zip(sp.points,pts):v.co=(*p,1)
 o=bpy.data.objects.new(name,c);scene.collection.objects.link(o);return add(o,m)
def ring(name,p,r,thick,m,vertical=False,steps=48):
 pts=[]
 for i in range(steps+1):
  a=i*math.tau/steps;pts.append((p[0]+r*math.cos(a),p[1]+(0 if vertical else r*math.sin(a)),p[2]+(r*math.sin(a) if vertical else 0)))
 return tube(name,pts,thick,m)
def beam(name,a,b,w,m):
 mid=(Vector(a)+Vector(b))/2;o=box(name,mid,(w,w,(Vector(b)-Vector(a)).length),m,w*.18);o.rotation_euler=(Vector(b)-Vector(a)).to_track_quat('Z','Y').to_euler();return o

def finish(name):
 global parts
 bpy.ops.object.select_all(action='DESELECT')
 for o in parts:o.select_set(True)
 bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.convert(target='MESH');bpy.ops.object.join()
 o=bpy.context.object;o.name=name;scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
 bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,mesh_smooth_type='FACE')
 finished.append(o);parts=[];return o

# Suspended hip roof: thick layered straw, quiet broad surfaces, sculpted timber eaves.
for side in range(4):
 a=side*math.pi/2
 def rot(p):return (p[0]*math.cos(a)-p[1]*math.sin(a),p[0]*math.sin(a)+p[1]*math.cos(a),p[2])
 beam('Eave',rot((-10.5,10.5,0)),rot((10.5,10.5,0)),.36,wood)
 for tier in range(7):
  y0=10.8-tier*1.35;y1=y0-1.7;z0=.14+tier*.53;z1=z0+.70
  # A sloped thatch panel, faceted only at the hipped corners.
  verts=[rot((-y0,y0,z0)),rot((y0,y0,z0)),rot((y1,y1,z1)),rot((-y1,y1,z1)),rot((-y0,y0,z0-.17)),rot((y0,y0,z0-.17)),rot((y1,y1,z1-.17)),rot((-y1,y1,z1-.17))]
  mesh=bpy.data.meshes.new('ThatchLayer');mesh.from_pydata(verts,[],[(3,2,1,0),(5,6,7,4),(1,5,4,0),(2,6,5,1),(3,7,6,2),(0,4,7,3)]);mesh.update();o=bpy.data.objects.new('Layer',mesh);scene.collection.objects.link(o);add(o,straw if tier%2 else strawLight)
  # Sparse parallel straw bundles articulate the slope, not a high-frequency texture.
  n=int(y0*5)
  for j in range(n):
   x0=-y0+(j+.5)*2*y0/n;x1=x0*y1/y0
   tube('StrawBundle',[rot((x0,y0+.015,z0+.02)),rot((x1,y1,z1+.025))],.035+rng.random()*.018,straw if j%3 else strawDark)
  beam('ThatchLip',rot((-y0,y0,z0-.03)),rot((y0,y0,z0-.03)),.09,strawDark)
 beam('HipRidge',rot((10.7,10.7,.25)),rot((1.8,1.8,3.85)),.24,edge)
for x in [-7,-3.5,0,3.5,7]:beam('UnderRafter',(x,-10.3,-.12),(x,10.3,-.12),.19,wood)
for y in [-7,-3.5,0,3.5,7]:beam('CrossRafter',(-10.3,y,-.26),(10.3,y,-.26),.19,wood)
box('Crown',(0,0,3.86),(3.6,3.6,.28),strawLight,.1)
roof=finish('SM_Canopy');roof.location=(0,0,7.8)

# Ribbed paper lantern with caps and an inset warm paper body.
ball('PaperBody',(0,0,.4),(.29,.29,.39),paper)
for j in range(8):
 z=.10+j*.085;r=.29*math.sqrt(max(0,1-((z-.4)/.4)**2));ring('PaperRib',(0,0,z),r,.012,rope,steps=32)
for z in [.02,.78]:cyl('Cap',(0,0,z),.15,.065,wood)
tube('Hanger',[(0,0,.8),(0,0,1.02)],.012,iron)
finish('SM_Lantern').location=(-8,-13,0)
# Four-strand hanging rope and generous soft tassel, total height 2.5m.
for j in range(3):
 tube('Twist',[(.09*math.cos(i*.22+j*math.tau/3),.09*math.sin(i*.22+j*math.tau/3),2.5-i*.022) for i in range(72)],.075,rope)
ball('Knot',(0,0,.85),(.23,.23,.19),rope)
for j in range(22):
 a=j*math.tau/22;r=.15+rng.random()*.06
 tube('Fringe',[(r*.55*math.cos(a),r*.55*math.sin(a),.84),(r*math.cos(a),r*math.sin(a),.40),(r*1.35*math.cos(a),r*1.35*math.sin(a),.06+rng.random()*.07)],.032,rope if j%3 else strawLight)
finish('SM_Tassel').location=(-6,-13,0)
# Alternating iron chain links.
for j in range(18):
 pts=[]
 for i in range(25):
  a=i*math.tau/24;x=.105*math.cos(a);z=j*.23+.17*math.sin(a)
  pts.append((x if j%2 else 0,0 if j%2 else x,z))
 tube('Link',pts,.027,iron)
finish('SM_Chain').location=(-4,-13,0)
# Taiko: bowed stave body, skin heads, studs, stout timber stand.
for x in [-.49,.49]:
 for y in [-.33,.33]:beam('Leg',(x*1.15,y*1.4,0),(x*.82,y,.95),.12,wood)
for z in [.25,.75]:beam('Brace',(-.52,-.35,z),(.52,-.35,z),.1,edge)
# Drum axis along Y; hoops and studs face the arena.
ball('DrumBody',(0,0,1.3),(.62,.43,.62),red)
for y in [-.4,.4]:
 o=cyl('Skin',(0,y,1.3),.54,.045,ivory,40);o.rotation_euler.x=math.pi/2
 ring('Hoop',(0,y,1.3),.565,.045,wood,True)
 for j in range(20):
  a=j*math.tau/20;ball('Stud',(.575*math.cos(a),y,1.3+.575*math.sin(a)),(.024,.025,.024),bronze)
for x in [-.22,.22]:beam('Drumstick',(x,-.44,1.73),(x+.1,-.55,2.14),.034,strawLight)
finish('SM_Taiko').location=(-2,-13,0)
# Tall cloth banner with a bold ring seal and the simple strength character 力.
beam('Pole',(0,0,0),(0,0,4.4),.105,wood);beam('TopBar',(-.08,0,4.22),(1.0,0,4.22),.085,edge)
box('Foot',(0,0,.08),(.7,.6,.16),wood,.07)
verts=[]
for row in range(13):
 for col in range(7):
  x=.12+col*.13;z=4.03-row*.23;y=.03*math.sin(col*.7+row*.5);verts.append((x,y,z))
faces=[]
for row in range(12):
 for col in range(6):
  k=row*7+col;faces.append((k,k+1,k+8,k+7))
me=bpy.data.meshes.new('BannerCloth');me.from_pydata(verts,[],faces);me.update();o=bpy.data.objects.new('Banner',me);scene.collection.objects.link(o);add(o,cloth)
# Embossed crest/strokes on both faces, avoiding fake text.
for y in [-.055,.055]:
 ring('DohyoSeal',(.51,y,3.43),.24,.025,ink,True,32)
 tube('PowerStroke1',[(.26,y,2.77),(.77,y,2.77),(.72,y,2.15),(.58,y,2.08)],.038,ink)
 tube('PowerStroke2',[(.49,y,2.95),(.47,y,2.65),(.41,y,2.38),(.25,y,2.17)],.038,ink)
 box('Hem',(.51,y,1.29),(.78,.018,.05),red,.005)
finish('SM_Banner').location=(0,-13,0)
# Bench, stave bucket, rope coil and audience figure.
box('Seat',(0,0,.64),(2.3,.52,.15),wood,.06)
for x in [-.84,.84]:
 for y in [-.14,.14]:box('Foot',(x,y,.30),(.15,.13,.60),wood,.035)
beam('Stretcher',(-.88,0,.23),(.88,0,.23),.1,edge)
finish('SM_Bench').location=(2,-13,0)
for j in range(16):
 a=j*math.tau/16;o=box('Stave',(.29*math.cos(a),.29*math.sin(a),.30),(.115,.055,.58),edge if j%3 else wood,.012);o.rotation_euler.z=a+math.pi/2
cyl('DarkInside',(0,0,.10),.27,.05,wood)
for z in [.10,.49]:ring('Binding',(0,0,z),.32,.025,iron)
tube('Handle',[(-.3,0,.45),(-.3,0,.87),(.3,0,.87),(.3,0,.45)],.027,wood)
finish('SM_Bucket').location=(4,-13,0)
for j in range(4):
 r=.40+j*.065;ring('Coil',(0,0,.05+j*.024),r,.042,rope,steps=64)
tube('Tail',[(.63,0,.08),(.78,.15,.05),(.92,.08,.05),(1.05,.14,.05)],.042,rope)
finish('SM_RopeCoil').location=(6,-13,0)
ball('Head',(0,0,1.18),(.20,.19,.23),spectator);ball('Torso',(0,0,.73),(.29,.22,.37),spectator)
for x in [-.19,.19]:ball('Knee',(x,-.18,.37),(.17,.24,.20),spectator)
finish('SM_Spectator').location=(8,-13,0)
# Hanging bell in a broad timber frame.
for x in [-.73,.73]:
 box('Post',(x,0,1.45),(.16,.19,2.9),wood,.045);box('Foot',(x,0,.10),(.35,.7,.2),wood,.05)
beam('Lintel',(-.88,0,2.64),(.88,0,2.64),.19,edge)
# Bell profile revolved into a mesh, open lower mouth.
profile=[(.18,2.25),(.32,2.17),(.37,1.98),(.39,1.66),(.48,1.54),(.49,1.48),(.43,1.47),(.34,1.66),(.29,2.04),(.13,2.13)]
vs=[(r*math.cos(i*math.tau/40),r*math.sin(i*math.tau/40),z) for r,z in profile for i in range(40)]
fs=[]
for j in range(len(profile)-1):
 for i in range(40):a=j*40+i;b=j*40+(i+1)%40;fs.append((a,b,b+40,a+40))
me=bpy.data.meshes.new('Bell');me.from_pydata(vs,[],fs);me.update();o=bpy.data.objects.new('Bell',me);scene.collection.objects.link(o);add(o,bronze)
for z,r in [(1.53,.48),(1.7,.395),(2.05,.36)]:ring('BellBand',(0,0,z),r,.018,bronze)
tube('BellRope',[(0,0,2.6),(0,0,2.2)],.045,rope)
ball('Clapper',(0,0,1.54),(.06,.06,.14),wood)
finish('SM_Bell').location=(10,-13,0)
# Native source and useful inspection viewport.
scene.world=bpy.data.worlds.new('SumoWarmStudio');scene.world.use_nodes=True
bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs['Color'].default_value=(.13,.15,.21,1);bg.inputs['Strength'].default_value=.6
for area in bpy.context.screen.areas:
 if area.type=='VIEW_3D':
  area.spaces.active.region_3d.view_distance=37;area.spaces.active.region_3d.view_location=(0,-3,3)
  area.spaces.active.shading.color_type='MATERIAL'
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'sumo_dojo.blend'))
print('SUMO_KIT',len(finished),'models',sum(len(o.data.polygons) for o in finished),'polygons')
