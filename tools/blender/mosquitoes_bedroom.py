"""Original bedroom and mosquito models. Run with tools/blender_client.py execute_code --file.
Coordinates below are Unity metres (Y up). The source scene is separate from the user's scene.
"""
import bpy, math, json, random
from pathlib import Path
from mathutils import Vector
ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'igruha/Assets/_Project/Art/Mosquitoes'
SOURCE = ROOT / 'igruha/Assets/_Project/Art/Source/Mosquitoes'
for p in (ART/'Models', SOURCE): p.mkdir(parents=True, exist_ok=True)
old_scenes=[s for s in bpy.data.scenes if s.name.startswith('Mosquitoes_OriginalBedroom')]
scene = bpy.data.scenes.new('Mosquitoes_OriginalBedroom_Rebuild')
bpy.context.window.scene = scene
for old in old_scenes:
    for o in list(old.objects):
        if len(o.users_scene)==1:bpy.data.objects.remove(o,do_unlink=True)
    bpy.data.scenes.remove(old)
scene.name='Mosquitoes_OriginalBedroom'
scene.unit_settings.system = 'METRIC'
rng = random.Random(613)
palette = {'Walnut':'4A3626','Wall':'2A3550','Ivory':'D8CFC0','Amber':'F0B860','Teal':'285861',
    'Blue':'53687F','Coral':'BB7159','Gold':'A68A49','Paper':'C9BDA2','Black':'202634',
    'Skin':'D59B71','Hair':'694639','Wing':'92ABB9','Glass':'6B8799','Leaf':'426A53','Cardboard':'876D53'}
mats = {}
for name, color in palette.items():
    rgb=tuple(int(color[i:i+2],16)/255 for i in (0,2,4))
    m=bpy.data.materials.get('MSQ_'+name) or bpy.data.materials.new('MSQ_'+name); m.use_nodes=True; m.diffuse_color=(*rgb,1)
    bs=m.node_tree.nodes.get('Principled BSDF'); bs.inputs['Base Color'].default_value=(*rgb,1); bs.inputs['Roughness'].default_value=.78
    mats[name]=m
def co(p): return (-p[0],-p[2],p[1])
def attach(o,name,mat):
    o.name=name; o.data.materials.append(mats[mat]); return o
def box(name,p,s,mat='Walnut',bevel=.025):
    bpy.ops.mesh.primitive_cube_add(size=1,location=co(p)); o=bpy.context.object
    o.scale=(s[0],s[2],s[1]); bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod=o.modifiers.new('Soft crafted edges','BEVEL'); mod.width=min(bevel,min(s)*.25); mod.segments=3
        bpy.ops.object.modifier_apply(modifier=mod.name)
        o.modifiers.new('Weighted normals','WEIGHTED_NORMAL')
    return attach(o,name,mat)
def ellipsoid(name,p,s,mat='Ivory'):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=16,radius=1,location=co(p)); o=bpy.context.object
    o.scale=(s[0],s[2],s[1]); bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    for poly in o.data.polygons: poly.use_smooth=True
    return attach(o,name,mat)
def cylinder(name,p,r,h,mat='Walnut',top=None):
    bpy.ops.mesh.primitive_cone_add(vertices=32,radius1=r,radius2=r if top is None else top,depth=h,location=co(p)); o=bpy.context.object
    mod=o.modifiers.new('Soft rim','BEVEL');mod.width=min(.018,h*.1);mod.segments=3
    bpy.ops.object.modifier_apply(modifier=mod.name)
    for poly in o.data.polygons: poly.use_smooth=True
    return attach(o,name,mat)
def tube(name,points,r,mat):
    curve=bpy.data.curves.new(name,'CURVE');curve.dimensions='3D';curve.bevel_depth=r;curve.bevel_resolution=3
    spline=curve.splines.new('POLY');spline.points.add(len(points)-1)
    for point,p in zip(spline.points,points):point.co=(*co(p),1)
    o=bpy.data.objects.new(name,curve);scene.collection.objects.link(o);o.data.materials.append(mats[mat]);
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o;bpy.ops.object.convert(target='MESH');return bpy.context.object
def cloth(name,origin,width,length,mat,curtain=False):
    verts=[];faces=[]; nx=36; ny=40
    for j in range(ny+1):
        v=j/ny
        for i in range(nx+1):
            u=i/nx
            if curtain:
                p=(origin[0]+.07*math.sin(u*math.pi*12)+.07*math.sin(v*math.pi),origin[1]-v*length,origin[2]+(u-.5)*width)
            else:
                x=(u-.5)*width; z=(v-.5)*length
                edge=max(0,(abs(x)-.72))
                p=(origin[0]+x,origin[1]+.05*math.sin(u*18+v*6)+.035*math.cos(v*20-u*7)-edge*1.25,origin[2]+z)
            verts.append(co(p))
    for j in range(ny):
        for i in range(nx):
            a=j*(nx+1)+i;faces.append((a,a+1,a+nx+2,a+nx+1))
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    o=bpy.data.objects.new(name,mesh);scene.collection.objects.link(o);mesh.materials.append(mats[mat])
    for poly in mesh.polygons:poly.use_smooth=True
    solid=o.modifiers.new('Fabric thickness','SOLIDIFY');solid.thickness=.012
    return o

# Architecture; no camera-facing wall removed, no texture noise.
box('RoomFloor',(0,-.12,0),(14.4,.24,11.52),'Walnut',0)
box('Ceiling',(0,3.72,0),(14.4,.24,11.52),'Wall',0)
for x in (-7.32,7.32):box('SideWall',(x,1.8,0),(.24,3.6,11.52),'Wall',0)
for z in (-5.88,5.88):box('EndWall',(0,1.8,z),(14.4,3.6,.24),'Wall',0)
for x in (-7.14,7.14):box('Skirting',(x,.08,0),(.07,.16,11.52),'Ivory',.015)
for z in (-5.70,5.70):box('Skirting',(0,.08,z),(14.4,.16,.07),'Ivory',.015)
for i in range(28):
    x=-6.95+i*.5
    box('FloorBoardSeam',(x,.002,0),(.007,.006,11.4),'Black',0)
    for j in range(3):box('FloorJoint',(x+.25,.002,-4+j*3.7+(i%2)*1.4),(.49,.007,.008),'Black',0)
# Bed matches the immutable gameplay collider (1.8 x 2.88, top .45).
box('BedFrame',(4.65,.22,-4.15),(1.88,.23,2.96),'Walnut',.06)
box('Mattress',(4.65,.365,-4.15),(1.8,.17,2.88),'Ivory',.065)
box('Headboard',(4.65,.68,-5.63),(1.99,1.18,.14),'Walnut',.055)
for x in (3.78,5.52):
    for z in (-5.44,-2.82):box('BedLeg',(x,.13,z),(.15,.26,.15),'Walnut')
ellipsoid('Pillow',(4.64,.54,-5.12),(.65,.13,.32),'Ivory')
cloth('RumpledBlueDuvet',(4.68,.55,-3.80),2.12,2.03,'Blue')
cloth('CoralThrow',(4.85,.62,-3.32),2.18,.72,'Coral')
# Rug, bedside table, shaded light, glass, alarm and books.
box('WovenRug',(3.6,.017,-3.63),(4.9,.025,4.0),'Blue',.01)
box('Nightstand',(3.43,.34,-5.18),(.58,.68,.58),'Walnut')
for y in (.27,.50):
    box('Drawer',(3.43,y,-4.875),(.51,.18,.027),'Cardboard',.02)
    ellipsoid('DrawerKnob',(3.43,y,-4.85),(.035,.027,.025),'Gold')
cylinder('LampBase',(3.48,.708,-5.25),.14,.05,'Gold')
cylinder('LampStem',(3.48,.93,-5.25),.022,.4,'Gold')
cylinder('LampShade',(3.48,1.18,-5.25),.235,.29,'Ivory',.135)
ellipsoid('WarmBulb',(3.48,1.08,-5.25),(.064,.064,.064),'Amber')
box('BedsideBook',(3.35,.712,-4.99),(.23,.05,.17),'Coral',.01)
box('BookPages',(3.35,.713,-4.989),(.21,.027,.159),'Paper',.005)
cylinder('WaterGlass',(3.25,.76,-5.3),.051,.15,'Glass')
box('AlarmClock',(3.55,.762,-4.985),(.18,.115,.06),'Teal',.025)
box('AlarmFace',(3.55,.774,-4.949),(.14,.066,.008),'Black',.003)
# Desk and occupied chair on far wall.
box('DeskTop',(3.65,.70,4.95),(2.16,.1,1.08),'Walnut',.035)
for x in (2.75,4.55):box('DeskSupport',(x,.34,4.95),(.12,.68,.85),'Walnut')
box('DeskDrawer',(4.23,.54,4.97),(.55,.22,.90),'Cardboard')
for k in range(3):box('DeskBooks',(2.99,.79+k*.045,4.85),(.38,.045,.28),('Ivory','Coral','Teal')[k],.008)
box('Notebook',(3.65,.769,4.71),(.43,.018,.29),'Paper',.006)
cylinder('DeskLampBase',(4.2,.78,5.11),.13,.03,'Blue')
tube('DeskLampArm',[(4.2,.8,5.11),(4.2,1.1,5.11),(3.98,1.3,5.1)],.017,'Blue')
cylinder('DeskLampShade',(3.98,1.25,5.1),.11,.14,'Blue',.045)
box('ChairSeat',(3.65,.44,3.83),(.65,.13,.6),'Blue',.07)
box('ChairBack',(3.65,.83,3.58),(.65,.7,.13),'Blue',.05)
cylinder('ChairPost',(3.65,.23,3.83),.045,.43,'Black')
for a in range(5):
    t=a*math.tau/5;tube('ChairFoot',[(3.65,.1,3.83),(3.65+math.cos(t)*.35,.06,3.83+math.sin(t)*.35)],.025,'Black')
ellipsoid('HoodieBody',(3.65,.78,3.52),(.31,.35,.11),'Teal')
for x in (3.3,4.0):ellipsoid('HoodieSleeve',(x,.68,3.53),(.09,.29,.09),'Teal')
ellipsoid('Hood',(3.65,1.06,3.55),(.20,.15,.13),'Teal')
# Open wardrobe: shelves and clothes visible through the gap.
box('WardrobeBack',(-5.8,1.15,4.78),(2.88,2.3,.08),'Black')
for x in (-7.19,-4.41):box('WardrobeSide',(x,1.15,4.30),(.10,2.3,1.08),'Walnut')
for y in (.10,1.8,2.25):box('WardrobeShelf',(-5.8,y,4.30),(2.88,.10,1.08),'Walnut')
box('WardrobeDoorClosed',(-6.49,1.16,3.75),(1.38,2.2,.09),'Walnut')
door=box('WardrobeDoorOpen',(-4.57,1.16,3.48),(1.25,2.2,.09),'Walnut');door.rotation_euler.z=math.radians(-61)
ellipsoid('WardrobeHandle',(-5.94,1.07,3.69),(.035,.035,.035),'Gold')
for i in range(4):
    box('FoldedClothes',(-5.30,1.92+i*.085,4.2),(.66,.07,.52),('Blue','Ivory','Coral','Teal')[i],.025)
for i in range(5):
    x=-5.5+i*.20
    ellipsoid('HangingShirt',(x,1.15,4.16),(.14,.46,.11),('Blue','Ivory','Coral','Teal','Paper')[i])
# Shelves and warm lived-in details.
for y in (1.4,2):
    box('Shelf',(3.5,y,5.42),(2.8,.08,.45),'Walnut')
    for x in (2.4,4.6):box('ShelfBracket',(x,y-.15,5.57),(.07,.30,.1),'Walnut')
    for j in range(8):
        box('Spine',(2.25+j*.11,y+.05+(h:=rng.uniform(.20,.34))/2,5.42),(.08,h,.20),('Ivory','Coral','Blue','Teal')[j%4],.008)
cylinder('PlantPot',(4.5,2.15,5.42),.12,.22,'Coral',.145)
for j in range(7):
    leaf=ellipsoid('PlantLeaf',(4.5+math.sin(j)*.12,2.38+(j%3)*.06,5.42+math.cos(j)*.08),(.045,.19,.025),'Leaf');leaf.rotation_euler.y=j*.33
cylinder('TrophyBase',(3.6,2.07,5.42),.11,.08,'Black')
cylinder('TrophyStem',(3.6,2.17,5.42),.025,.15,'Gold')
cylinder('TrophyCup',(3.6,2.31,5.42),.06,.17,'Gold',.13)
# Moonlit window on opposite side, 1.44m square, soft folded curtains.
box('WindowGlass',(7.174,2.02,3.35),(.025,1.44,1.44),'Glass',.01)
for y in (1.26,2.78):box('WindowFrame',(7.13,y,3.35),(.12,.09,1.60),'Ivory')
for z in (2.59,4.11):box('WindowFrame',(7.13,2.02,z),(.12,1.52,.09),'Ivory')
box('WindowMullion',(7.08,2.02,3.35),(.08,1.44,.045),'Ivory',.01)
box('WindowSill',(6.97,1.24,3.35),(.44,.09,1.73),'Ivory')
tube('CurtainRail',[(6.91,2.92,2.23),(6.91,2.92,4.47)],.022,'Gold')
for z in (2.48,4.22):cloth('Curtain',(6.92,2.90,z),.58,1.93,'Ivory',True)
# Packing boxes, laundry basket, backpack and a closed door.
for x,y,z,s in [(-3.64,.30,4.63,.60),(-3.62,.92,4.64,.59),(-2.92,.25,4.83,.5)]:
    box('PackingBox',(x,y,z),(s,s,s),'Cardboard',.018)
    box('PackingTape',(x,y+s/2+.004,z),(.09,.007,s),'Paper',.001)
box('Door',(-7.12,1.15,-2.65),(.12,2.3,1.25),'Blue')
for z in (-3.32,-1.98):box('DoorFrame',(-7.03,1.18,z),(.12,2.4,.10),'Ivory')
box('DoorTop',(-7.03,2.39,-2.65),(.12,.12,1.44),'Ivory')
ellipsoid('DoorKnob',(-6.97,1.06,-2.24),(.045,.04,.04),'Gold')
ellipsoid('Backpack',(2.30,.38,-2.47),(.28,.37,.20),'Coral')
ellipsoid('BackpackPocket',(2.30,.29,-2.25),(.22,.18,.07),'Cardboard')
tube('BackpackHandle',[(2.19,.65,-2.47),(2.19,.80,-2.47),(2.41,.80,-2.47),(2.41,.65,-2.47)],.017,'Walnut')
for x in (-6.15,-5.80):ellipsoid('Sneaker',(x,.10,-3.48),(.12,.10,.28),'Ivory')
# Unlit pendant stays above the gameplay ceiling.
cylinder('PendantCable',(0,3.29,0),.012,.57,'Black')
cylinder('PendantShade',(0,2.87,0),.43,.34,'Blue',.17)
ellipsoid('PendantBulb',(0,2.74,0),(.085,.085,.085),'Ivory')

def export(name,objects):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/f'{name}.fbx'),use_selection=True,object_types={'MESH'},use_mesh_modifiers=True,mesh_smooth_type='FACE',add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',apply_unit_scale=True,use_space_transform=True,path_mode='RELATIVE')
bedroom=list(scene.objects);export('Bedroom',bedroom)
# Tiny winged character, separate body; no changes to any roster prefab.
start=set(scene.objects)
ellipsoid('MosquitoShirt',(0,0,0),(.060,.055,.040),'Teal')
ellipsoid('MosquitoShorts',(0,-.040,0),(.052,.031,.036),'Blue')
ellipsoid('MosquitoHead',(0,.066,.004),(.038,.039,.036),'Skin')
ellipsoid('MosquitoHair',(0,.087,-.003),(.038,.021,.033),'Hair')
ellipsoid('MosquitoNose',(0,.061,.039),(.011,.011,.012),'Skin')
for x in (-.014,.014):
    ellipsoid('Eye',(x,.073,.035),(.004,.004,.003),'Black')
for side in (-1,1):
    arm=ellipsoid('Arm',(side*.063,-.003,.006),(.013,.036,.013),'Skin');arm.rotation_euler.y=side*.3
    ellipsoid('Foot',(side*.025,-.073,.012),(.017,.025,.015),'Skin')
    wing=ellipsoid('Wing',(side*.088,.045,-.027),(.071,.018,.029),'Wing');wing.rotation_euler.y=side*.35
    tube('WingVein',[(side*.025,.03,-.027),(side*.135,.053,-.027)],.0015,'Ivory')
mosquito=[o for o in scene.objects if o not in start];export('Mosquito',mosquito)
for o in mosquito:o.location.x-=2
(ART/'Models/Palette.json').write_text(json.dumps(palette,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'Mosquitoes.blend'))
print(json.dumps({'bedroom_meshes':len(bedroom),'mosquito_meshes':len(mosquito),'source':str(SOURCE/'Mosquitoes.blend')}))
