"""Rounded sunlit ruin kit. Execute through Blender MCP; units are metres.
Each model is authored at the origin in its own collection, exported with a fixed seed.
"""
import bpy, math, random
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'igruha/Assets/_Project/Art/Minigames/OneBullet/Models'
SOURCE=ROOT/'tools/blender/source/one_bullet'
OUT.mkdir(parents=True,exist_ok=True);SOURCE.mkdir(parents=True,exist_ok=True)
rng=random.Random(597)
scene=bpy.data.scenes.get('OneBullet_Craft') or bpy.data.scenes.new('OneBullet_Craft')
# Rebuild only the owned source scene.
for owned in list(scene.objects): bpy.data.objects.remove(owned,do_unlink=True)
bpy.context.window.scene=scene
scene.unit_settings.system='METRIC'

def mat(name,color,rough=.8,metal=0):
 m=bpy.data.materials.get('OB_'+name) or bpy.data.materials.new('OB_'+name);m.diffuse_color=(*color,1);m.use_nodes=True
 n=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
 n.inputs['Base Color'].default_value=(*color,1);n.inputs['Roughness'].default_value=rough;n.inputs['Metallic'].default_value=metal
 return m
stone=mat('Limestone',(.56,.51,.40));pale=mat('PaleStone',(.71,.65,.53));dark=mat('Crevice',(.18,.16,.13))
wood=mat('OldWood',(.31,.16,.065));leaf=mat('CopperLeaf',(.43,.17,.055));ochre=mat('AmberLeaf',(.64,.32,.075));stem=mat('Vine',(.19,.085,.035))
cloth=mat('Canvas',(.31,.29,.14));brass=mat('Brass',(.62,.38,.13),.38,.7);metal=mat('GunSteel',(.26,.31,.34),.3,.65);silver=mat('Silver',(.65,.67,.63),.29,.65)
parts=[]
def add(obj, material):
 obj.data.materials.append(material);parts.append(obj);return obj

def box(name,loc,scale,material=stone,bevel=.04):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc);o=bpy.context.object;o.name=name;o.dimensions=scale
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if bevel:
  b=o.modifiers.new('Soft edges','BEVEL');b.width=bevel;b.segments=2
  bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=b.name)
 for f in o.data.polygons:f.use_smooth=True
 n=o.modifiers.new('Weighted normals','WEIGHTED_NORMAL');n.keep_sharp=True
 bpy.ops.object.modifier_apply(modifier=n.name)
 return add(o,material)

def cylinder(name,loc,radius,depth,material=stone,vertices=16):
 bpy.ops.mesh.primitive_cylinder_add(vertices=vertices,radius=radius,depth=depth,location=loc)
 o=bpy.context.object;o.name=name
 b=o.modifiers.new('Worn rim','BEVEL');b.width=min(.025,depth*.15);b.segments=2
 bpy.ops.object.modifier_apply(modifier=b.name)
 for f in o.data.polygons:f.use_smooth=True
 n=o.modifiers.new('Weighted normals','WEIGHTED_NORMAL');bpy.ops.object.modifier_apply(modifier=n.name)
 return add(o,material)

def tube(name,points,radius,material):
 curve=bpy.data.curves.new(name,'CURVE');curve.dimensions='3D';curve.resolution_u=2;curve.bevel_depth=radius;curve.bevel_resolution=1
 sp=curve.splines.new('POLY');sp.points.add(len(points)-1)
 for p,co in zip(sp.points,points):p.co=(*co,1)
 o=bpy.data.objects.new(name,curve);scene.collection.objects.link(o);parts.append(o);curve.materials.append(material);return o

def uvball(name,loc,scale,material):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=8,radius=1,location=loc)
 o=bpy.context.object;o.name=name;o.scale=scale;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 for f in o.data.polygons:f.use_smooth=True
 return add(o,material)

def finish(name):
 # All origins are zero; kit copies are parented to a named root for Unity.
 global parts
 bpy.ops.object.select_all(action='DESELECT')
 for o in parts:o.select_set(True)
 bpy.context.view_layer.objects.active=parts[0]
 bpy.ops.object.convert(target='MESH')
 if len(parts)>1:bpy.ops.object.join()
 o=bpy.context.object;o.name=name
 scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
 bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,mesh_smooth_type='FACE')
 # Keep a spaced inspection board in the source scene, exports stay at origin.
 index=len([x for x in scene.objects if x.name.startswith('OB_')])-1
 o.location=(index%5*3,index//5*3,0)
 parts=[]
 return o

for i in range(3):
 o=box('Stone',(0,0,0),(1,.45,.64),stone,.065)
 # Sparse asymmetry gives broad weathering, without a noisy surface texture.
 for v in o.data.vertices:
  if v.co.z>.1 and v.co.x>.25:v.co.z+=.016*(i-1)
 finish('OB_Stone_'+str(i))
for i in range(3):
 o=box('Slab',(0,0,-.045),(1,.86,.09),pale,.032)
 for v in o.data.vertices:
  if v.co.x>0:v.co.x+=.025*math.sin(v.co.y*6+i)
 finish('OB_Slab_'+str(i))
# Broken Doric column, including visible pale chipped core.
box('Plinth',(0,0,.13),(1.0,1.0,.26),stone,.08)
cylinder('Foot',(0,0,.32),.44,.18,pale)
cylinder('Torus',(0,0,.46),.39,.12,pale)
cylinder('Shaft',(0,0,1.25),.31,1.52,stone,20)
for i in range(12):
 a=i*math.tau/12;cylinder('Flute',(math.cos(a)*.30,math.sin(a)*.30,1.25),.032,1.43,pale,8)
cylinder('BrokenCore',(0,0,2.02),.29,.08,pale,9)
for i in range(5):
 a=i*1.256;box('ChippedEdge',(math.cos(a)*.19,math.sin(a)*.19,2.05+rng.random()*.1),(.17,.16,.12),pale,.025)
finish('OB_BrokenColumn')
# Dry well: rings of irregular voussoirs and a dark recess.
cylinder('WellInside',(0,0,.09),.70,.12,dark,24)
for row in range(2):
 for i in range(14):
  a=(i+row*.5)*math.tau/14
  o=box('WellStone',(math.cos(a)*.83,math.sin(a)*.83,.2+row*.34),(.43,.32,.32),pale if (i+row)%4==0 else stone,.045)
  o.rotation_euler.z=a+math.pi*.5
finish('OB_Well')
# Wall vine: three hanging stems, smaller tendrils and copper leaves.
for branch in range(4):
 pts=[];x0=(branch-1.5)*.24
 for j in range(12):pts.append((x0+.13*math.sin(j*.7+branch),.035*math.sin(j),2.5-j*.22))
 tube('ClimbingVine',pts,.018,stem)
 for j in range(1,11):
  x,y,z=pts[j];side=(-1)**(j+branch)
  tube('Tendril',[(x,y,z),(x+side*.14,y-.025,z+.09),(x+side*.25,y,z+.12)],.009,stem)
  m=bpy.data.meshes.new('Leaf');m.from_pydata([(x+side*.09,y-.018,z+.03),(x+side*.20,y-.08,z+.015),(x+side*.31,y-.04,z+.17),(x+side*.18,y+.005,z+.20)],[],[(0,1,2),(0,2,3)])
  o=bpy.data.objects.new('CopperLeaf',m);scene.collection.objects.link(o);add(o,leaf if j%3 else ochre)
finish('OB_Ivy')
# A weathered sign with crossed-out attempt marks.
box('Stake',(0,0,.58),(.11,.12,1.16),wood,.025)
box('Board',(0,-.07,1.05),(.72,.105,.32),wood,.055)
for z in [.94,1.16]:uvball('Nail',(-.25,-.129,z),(.021,.013,.021),metal)
for i in range(5):
 o=box('Tally',(-.13+i*.052,-.127,1.025),(.012,.01,.12),dark,.002);o.rotation_euler.y=.13
bar=box('TallySlash',(-.025,-.134,1.025),(.29,.009,.013),dark,.002);bar.rotation_euler.y=-.13
finish('OB_Sign')
# Abandoned canvas bag.
box('Backpack',(0,0,.31),(.48,.27,.55),cloth,.09)
box('Flap',(0,-.08,.56),(.51,.25,.16),cloth,.07)
for x in [-.16,.16]:
 box('Pocket',(x,-.18,.24),(.19,.14,.25),cloth,.04)
 box('Strap',(x,-.263,.35),(.035,.024,.12),wood,.008)
 box('Buckle',(x,-.28,.31),(.059,.018,.043),brass,.008)
tube('Handle',[(-.12,0,.54),(-.12,0,.68),(.12,0,.68),(.12,0,.54)],.025,wood)
finish('OB_Backpack')
# Rope coil, three loops and loose tail.
for k in range(3):
 pts=[((.31+k*.046)*math.cos(a),(.24+k*.042)*math.sin(a),.035+k*.018) for a in [i*math.tau/64 for i in range(65)]]
 tube('Rope',pts,.021,wood)
tube('Tail',[(.38,0,.06),(.5,.12,.045),(.61,.08,.03),(.68,.19,.03)],.021,wood)
finish('OB_Rope')
for i in range(9):
 a=i*math.tau/9;o=uvball('FireStone',(math.cos(a)*.43,math.sin(a)*.35,.10),(.15,.12,.11),stone)
for i in range(3):
 o=cylinder('CharredLog',(0,(i-1)*.11,.13),.045,.56,dark,10);o.rotation_euler.y=math.pi*.5;o.rotation_euler.z=(i-1)*.35
finish('OB_Campfire')
# Readable compact revolver, forward is -Y in Blender / +Z in Unity.
box('Frame',(0,0,.04),(.095,.20,.12),metal,.02)
barrel=cylinder('Barrel',(0,-.19,.075),.039,.29,silver,12);barrel.rotation_euler.x=math.pi*.5
muzzle=cylinder('Bore',(0,-.342,.075),.022,.006,dark,12);muzzle.rotation_euler.x=math.pi*.5
cyl=cylinder('Chamber',(0,-.016,.05),.068,.11,metal,12);cyl.rotation_euler.x=math.pi*.5
for i in range(6):
 a=i*math.tau/6
 c=cylinder('ChamberFlute',(math.cos(a)*.062,-.016,.05+math.sin(a)*.062),.012,.115,silver,8);c.rotation_euler.x=math.pi*.5
handle=box('WalnutGrip',(0,.078,-.075),(.079,.093,.21),wood,.031);handle.rotation_euler.x=-.27
for x in [-.044,.044]:uvball('GripScrew',(x,.082,-.085),(.007,.013,.013),brass)
tube('TriggerGuard',[(-.015,.045,-.025),(-.015,.025,-.094),(-.015,-.063,-.096),(-.015,-.08,-.028)],.009,metal)
box('Hammer',(0,.083,.12),(.035,.06,.032),silver,.006)
box('FrontSight',(0,-.28,.12),(.013,.028,.028),metal,.003)
finish('OB_Revolver')
# Save source and a preview camera, without touching the initial user scene.
scene.world=bpy.data.worlds.new('OB_Daylight');scene.world.use_nodes=True
bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND');bg.inputs['Color'].default_value=(.55,.68,.82,1);bg.inputs['Strength'].default_value=.5
bpy.ops.object.light_add(type='SUN',location=(4,-8,12));sun=bpy.context.object;sun.rotation_euler=(.45,-.5,-.4);sun.data.energy=2.5;sun.data.angle=.08
bpy.ops.object.camera_add(location=(13,-14,14));cam=bpy.context.object;cam.rotation_euler=(Vector((5,3,0))-cam.location).to_track_quat('-Z','Y').to_euler();scene.camera=cam
scene.render.resolution_x=1400;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'one_bullet_kit.blend'))
print('ONE_BULLET_KIT',len([p for p in OUT.glob('*.fbx')]),'models',sum(len(o.data.polygons) for o in scene.objects if o.type=='MESH'),'faces')
