"""One Bullet detail pass: compact revolver, ceramics, wall relief and foliage.
Run with Blender MCP. Rebuilds only OneBullet_Details; all exports retain metres.
"""
import bpy, math, random
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'igruha/Assets/_Project/Art/Minigames/OneBullet/Models'
SOURCE=ROOT/'tools/blender/source/one_bullet'
scene=bpy.data.scenes.get('OneBullet_Details') or bpy.data.scenes.new('OneBullet_Details')
for obj in list(scene.objects):bpy.data.objects.remove(obj,do_unlink=True)
bpy.context.window.scene=scene
parts=[];finished=[]
def material(name,rgb,rough=.65,metallic=0):
 m=bpy.data.materials.get('OB_'+name) or bpy.data.materials.new('OB_'+name);m.use_nodes=True;m.diffuse_color=(*rgb,1)
 n=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');n.inputs['Base Color'].default_value=(*rgb,1);n.inputs['Roughness'].default_value=rough;n.inputs['Metallic'].default_value=metallic
 return m
steel=material('BlueSteel',(.11,.16,.19),.28,.8)
edge=material('BurnishedEdge',(.36,.41,.42),.27,.8)
wood=material('Walnut',(.28,.095,.037),.46)
brass=material('RevolverBrass',(.62,.41,.16),.28,.75)
black=material('Crevice',(.035,.044,.047))
ivory=material('SightIvory',(.91,.80,.55),.35)
clay=material('Terracotta',(.57,.225,.105))
clayLight=material('ClayRim',(.77,.39,.20))
stone=material('PaleStone',(.71,.65,.53))
leaf=material('OliveLeaf',(.30,.36,.14));leaf2=material('SageLeaf',(.41,.46,.23));stem=material('Vine',(.22,.12,.045))
def add(o,m):o.data.materials.append(m);parts.append(o);return o
def bevel(o,width=.003,segments=3):
 bpy.context.view_layer.objects.active=o
 b=o.modifiers.new('Crafted edges','BEVEL');b.width=width;b.segments=segments;bpy.ops.object.modifier_apply(modifier=b.name)
 for p in o.data.polygons:p.use_smooth=True
 n=o.modifiers.new('Weighted surfaces','WEIGHTED_NORMAL');n.keep_sharp=True;bpy.ops.object.modifier_apply(modifier=n.name)
 return o
def box(name,p,size,m,width=.003):
 bpy.ops.mesh.primitive_cube_add(size=1,location=p);o=bpy.context.object;o.name=name;o.dimensions=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True);bevel(o,width);return add(o,m)
def cylinder(name,p,r,d,m,axis='Z',verts=32,width=.002):
 bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=r,depth=d,location=p);o=bpy.context.object;o.name=name
 if axis=='Y':o.rotation_euler.x=math.pi/2
 if axis=='X':o.rotation_euler.y=math.pi/2
 bevel(o,width);return add(o,m)
def tube(name,points,r,m):
 curve=bpy.data.curves.new(name,'CURVE');curve.dimensions='3D';curve.bevel_depth=r;curve.bevel_resolution=2
 sp=curve.splines.new('POLY');sp.points.add(len(points)-1)
 for p,co in zip(sp.points,points):p.co=(*co,1)
 o=bpy.data.objects.new(name,curve);scene.collection.objects.link(o);return add(o,m)
def prism(name,profile,thickness,m):
 verts=[(x,y,z) for x in [-thickness/2,thickness/2] for y,z in profile];n=len(profile)
 faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
 mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update();o=bpy.data.objects.new(name,mesh);scene.collection.objects.link(o);bevel(o,.004);return add(o,m)
def finish(name):
 global parts
 bpy.ops.object.select_all(action='DESELECT')
 for o in parts:o.select_set(True)
 bpy.context.view_layer.objects.active=parts[0];bpy.ops.object.convert(target='MESH');bpy.ops.object.join();o=bpy.context.object;o.name=name
 scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
 bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,mesh_smooth_type='FACE')
 finished.append(o);parts=[];return o
# Gun: forward -Y / Unity +Z, total 0.30 m. Grip is centred (0,+.06,-.067).
prism('Frame',[(-.048,.068),(.045,.070),(.075,.041),(.067,-.036),(.023,-.055),(-.057,-.019)],.052,steel)
for x in [-.029,.029]:
 plate=prism('InsetSideplate',[(-.047,.033),(.041,.038),(.053,.021),(.023,-.029),(-.044,-.013)],.004,edge);plate.location.x=x
 cylinder('PlateScrew',(x*1.13,.020,.019),.0043,.003,brass,'X',20,.0006)
# Short tapered barrel; dark recessed bore and subtle muzzle rim.
cylinder('Barrel',(0,-.144,.055),.023,.151,edge,'Y',32,.0018)
cylinder('MuzzleRim',(0,-.221,.055),.024,.010,steel,'Y',32,.001)
cylinder('Bore',(0,-.227,.055),.012,.002,black,'Y',28,.0003)
box('UnderBarrel',(0,-.137,.030),(.024,.145,.018),steel,.004)
cylinder('EjectorRod',(.024,-.128,.026),.006,.137,edge,'Y',20,.001)
# Six chambers with inset flutes, not oversized external pipes.
cylinder('Cylinder',(0,-.002,.038),.041,.080,steel,'Y',40,.003)
for i in range(6):
 a=math.tau*i/6
 cylinder('Chamber',(math.cos(a)*.025,-.044,.038+math.sin(a)*.025),.009,.003,black,'Y',20,.0005)
 flute=box('Flute',(math.cos(a)*.040,-.003,.038+math.sin(a)*.040),(.014,.052,.0015),edge,.001)
 flute.rotation_euler.y=math.pi/2-a
# Curved walnut panels; narrow brass pins.
gripProfile=[(.015,-.018),(.057,-.014),(.079,-.100),(.077,-.127),(.031,-.137),(.011,-.114),(.029,-.055)]
prism('GripFrame',gripProfile,.039,steel)
for x in [-.023,.023]:
 panel=prism('WalnutPanel',[(.024,-.031),(.053,-.029),(.070,-.103),(.068,-.121),(.034,-.128),(.022,-.109),(.038,-.056)],.010,wood);panel.location.x=x
 cylinder('GripPin',(x*1.25,.049,-.073),.004,.002,brass,'X',20,.0007)
 for k in range(5):
  tube('GripCheckering',[(x*1.24,.032+k*.006,-.096),(x*1.24,.040+k*.005,-.112)],.0007,edge)
# Open trigger guard and a small hammer below the sight line.
tube('TriggerGuard',[(0,.013,-.02),(0,.004,-.065),(0,-.027,-.071),(0,-.049,-.051),(0,-.044,-.020)],.0042,brass)
tube('Trigger',[(0,-.011,-.011),(0,-.006,-.041),(0,-.015,-.052)],.003,steel)
box('Hammer',(0,.061,.065),(.014,.025,.022),steel,.002)
box('HammerSpur',(0,.071,.073),(.020,.015,.008),edge,.001)
# Notched rear sight and fine contrasting front post.
for x in [-.009,.009]:box('RearSightEar',(x,.030,.083),(.008,.014,.011),steel,.001)
box('RearSightBase',(0,.030,.076),(.027,.014,.004),steel,.001)
box('FrontPost',(0,-.186,.080),(.007,.014,.014),steel,.001)
box('FrontDot',(0,-.180,.083),(.0035,.0015,.004),ivory,.0005)
finish('OB_Revolver')
# Hand-thrown amphora, with a real open lip and interior.
def lathe(name,profile,m,n=32):
 vs=[]
 for z,r in profile:
  for i in range(n):a=i*math.tau/n;vs.append((math.cos(a)*r,math.sin(a)*r,z))
 faces=[]
 for row in range(len(profile)-1):
  for i in range(n):a=row*n+i;b=row*n+(i+1)%n;faces.append((a,b,b+n,a+n))
 me=bpy.data.meshes.new(name);me.from_pydata(vs,[],faces);me.update();o=bpy.data.objects.new(name,me);scene.collection.objects.link(o)
 for f in me.polygons:f.use_smooth=True
 return add(o,m)
lathe('Amphora',[(0,.08),(.04,.095),(.12,.16),(.26,.19),(.37,.15),(.45,.08),(.55,.077),(.575,.097),(.59,.095),(.592,.073),(.55,.062),(.46,.061),(.37,.13),(.26,.165),(.12,.14),(.04,.064)],clay)
for sign in [-1,1]:tube('Handle',[(sign*.07,0,.53),(sign*.17,0,.54),(sign*.22,0,.45),(sign*.18,0,.34),(sign*.15,0,.34)],.018,clayLight)
lathe('Rim',[(.566,.097),(.58,.101),(.593,.096)],clayLight)
lathe('PaintedBand',[(.21,.187),(.235,.191)],clayLight)
finish('OB_Amphora')
# A compact dry fern/olive bush; leaves have creases, no alpha cards.
rng=random.Random(642)
for branch in range(9):
 angle=branch*math.tau/9;length=.26+rng.random()*.17
 base=Vector((0,0,0));tip=Vector((math.cos(angle)*length,math.sin(angle)*length,.18+rng.random()*.2))
 tube('Stem',[base,tip*.65+Vector((0,0,.07)),tip],.007,stem)
 for k in range(3,8):
  p=tip*(k/8)+Vector((0,0,.045));side=Vector((-math.sin(angle),math.cos(angle),.25));forward=Vector((math.cos(angle),math.sin(angle),.2))
  for sign in [-1,1]:
   end=p+side*sign*(.075-(k-3)*.005)+forward*.03
   mid=(p+end)*.5;v=[p,mid+forward*.025, end,mid-forward*.025,mid+Vector((0,0,.012))]
   me=bpy.data.meshes.new('Leaf');me.from_pydata(v,[],[(0,1,4),(1,2,4),(2,3,4),(3,0,4)]);me.update();o=bpy.data.objects.new('Leaf',me);scene.collection.objects.link(o);add(o,leaf if k%2 else leaf2)
finish('OB_Olive')
# Sun emblem in a carved wall plaque; Z-up, faces -Y after placement.
box('Plaque',(0,.03,.4),(.72,.10,.8),stone,.055)
cylinder('SunDisk',(0,-.026,.44),.13,.032,stone,'Y',24,.008)
for i in range(12):
 a=i*math.tau/12
 ray=box('SunRay',(math.cos(a)*.235,-.030,.44+math.sin(a)*.235),(.035,.035,.115),stone,.010);ray.rotation_euler.y=math.pi/2-a
box('PlaqueFoot',(0,-.035,.07),(.81,.18,.10),stone,.021)
finish('OB_SunRelief')
# Finer climbing vines with folded almond leaves instead of large flat cards.
russet=material('RussetLeaf',(.48,.20,.085));goldLeaf=material('DriedGoldLeaf',(.67,.38,.14))
rng=random.Random(642)
for branch in range(4):
 x0=(branch-1.5)*.23
 points=[(x0+.085*math.sin(j*.48+branch),.025*math.sin(j*.7+branch),2.5-j*.13) for j in range(20)]
 vine=tube('WeatheredStem',points,.012,stem);vine.data.bevel_resolution=1
 for j in range(2,19):
  if rng.random()<.28:continue
  p=Vector(points[j]);side=(-1)**(j+branch)
  end=p+Vector((side*rng.uniform(.10,.17),-.015,rng.uniform(-.045,.10)))
  twig=tube('FineTendril',[p,(p+end)*.5+Vector((0,-.01,.035)),end],.004,stem);twig.data.bevel_resolution=1
  direction=Vector((side*rng.uniform(.4,1),rng.uniform(-.1,.1),rng.uniform(-.9,.4))).normalized()
  across=Vector((-direction.z,0,direction.x)).normalized();length=rng.uniform(.12,.20);width=rng.uniform(.065,.10)
  profile=[(0,0),(.25,-.48),(.65,-.5),(1,0),(.65,.5),(.25,.48)]
  verts=[end+direction*(u*length)+across*(v*width) for u,v in profile]
  verts.append(end+direction*(length*.49)+Vector((0,-.016,0)))
  mesh=bpy.data.meshes.new('FoldedLeaf');mesh.from_pydata(verts,[],[(i,(i+1)%6,6) for i in range(6)]);mesh.update()
  o=bpy.data.objects.new('FoldedLeaf',mesh);scene.collection.objects.link(o);add(o,russet if j%3 else goldLeaf)
finish('OB_Ivy')
# Source inspection board, with the weapon presented at readable scale.
for i,o in enumerate(finished):o.location=(i*1.2,0,0)
scene.world=bpy.data.worlds.get('OB_DetailWorld') or bpy.data.worlds.new('OB_DetailWorld');scene.world.use_nodes=True
next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND').inputs['Strength'].default_value=.6
bpy.ops.object.light_add(type='AREA',location=(1,-3,4));bpy.context.object.data.energy=500;bpy.context.object.data.shape='DISK';bpy.context.object.data.size=4
bpy.ops.object.camera_add(location=(2.5,-4,2));cam=bpy.context.object;cam.rotation_euler=(Vector((1.5,0,.25))-cam.location).to_track_quat('-Z','Y').to_euler();scene.camera=cam
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'one_bullet_details.blend'))
print('DETAILS',[(o.name,tuple(round(v,3) for v in o.dimensions),len(o.data.polygons)) for o in finished])
