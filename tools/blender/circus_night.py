"""Original modular tent lining, arena masonry and joinery for BOTH circus games.
Blender metres. Layout follows CircusArenaConfig: R18, pit R8.64, deck Z1.08.
"""
import bpy, math, random, json, ast
from pathlib import Path
from mathutils import Vector, Matrix
import bmesh
ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'igruha/Assets/_Project/Art/CircusNight'
SOURCE=ROOT/'igruha/Assets/_Project/Art/Source/CircusNight'
for p in (ART/'Models',SOURCE):p.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene;scene.name='GrandChapiteau_Circus';PI=math.pi
rng=random.Random(328)
materials=[]
def mat(name,color,rough=.7,metal=0):
    m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
    p=m.node_tree.nodes['Principled BSDF'];p.inputs['Base Color'].default_value=(*color,1);p.inputs['Roughness'].default_value=rough;p.inputs['Metallic'].default_value=metal
    materials.append(m);return len(materials)-1
WOOD=mat('CN_Walnut',(.20,.095,.048));WOOD2=mat('CN_WalnutLight',(.32,.18,.09))
RED=mat('CN_OxbloodVelvet',(.58,.028,.041),.95);TEAL=mat('CN_MidnightCanvas',(.045,.27,.30),.94)
GOLD=mat('CN_AgedBrass',(.47,.30,.11),.40,.72);IRON=mat('CN_BlackIron',(.045,.059,.069),.48,.65)
STONE=mat('CN_PitStone',(.29,.27,.23),.96);TRIM=mat('CN_StoneEdge',(.39,.35,.27),.83)
SAND=mat('CN_Sawdust',(.43,.30,.14),.96);BULB=mat('CN_WarmBulb',(1,.61,.22),.4)
CRACK=mat('CN_Seams',(.023,.022,.020),1);CREAM=mat('CN_Parchment',(.69,.52,.29),.8)
TIN=mat('CN_TinPaint',(.93,.93,.93),.4);STEEL=mat('CN_TinSteel',(.52,.56,.59),.32,.8)
STRIPE=mat('CN_CanvasRed',(.72,.045,.062),.93)
IVORY=mat('CN_CanvasIvory',(.92,.83,.65),.91)
ROPE=mat('CN_HempRope',(.52,.37,.19),.98)
bulb_shader=materials[BULB].node_tree.nodes['Principled BSDF']
bulb_shader.inputs['Emission Color'].default_value=(1,.77,.44,1)
bulb_shader.inputs['Emission Strength'].default_value=6
# Use the project's established Blender mesh primitives without running its scene generator.
# This keeps tube/lathe winding and Unity export conventions identical across original art kits.
tree=ast.parse((ROOT/'tools/blender/crying_angels.py').read_text())
names={'Mesh','transform','box','lathe','ellipsoid','tube','ring','arc'}
_box_cache={}
for node in tree.body:
    if isinstance(node,(ast.ClassDef,ast.FunctionDef)) and node.name in names:
        exec(compile(ast.Module(body=[node],type_ignores=[]),'gallery_geometry','exec'))

# The shared sphere primitive winds inward. Our solid bulbs/toys need outward normals.
_ellipsoid_primitive=ellipsoid
def ellipsoid(m,*args,**kwargs):
    first=len(m.f);_ellipsoid_primitive(m,*args,**kwargs)
    for i in range(first,len(m.f)):m.f[i]=tuple(reversed(m.f[i]))

def export(mesh,name):
    obj=mesh.object(name)
    # Projected UVs are stable in world metres on both scene copies.
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/f'{name}.fbx'),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
    return obj

# Individual deck sectors permit frustum culling. Gaps and grain break the old flat brown disc.
for sector in range(8):
    m=Mesh();a0=sector*PI/4
    for band in range(23):
        r0=8.94+band*.387;r1=r0+.376
        steps=max(6,int((r0+r1)*PI/8/1.4))
        for j in range(steps):
            aa=a0+PI/4*j/steps+.0008;bb=a0+PI/4*(j+1)/steps-.0008
            v=[(r*math.cos(a),r*math.sin(a),z) for z in (1.08,1.112) for r,a in [(r0,aa),(r1,aa),(r1,bb),(r0,bb)]]
            m.add(v,[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],WOOD if rng.random()<.66 else WOOD2)
    export(m,f'CN_Deck_{sector:02}')

# Dark continuous backing closes the plank joints without covering the pit.
m=Mesh()
for i in range(128):
    a=i*math.tau/128;b=(i+1)*math.tau/128
    m.add([(r*math.cos(t),r*math.sin(t),1.06) for r,t in [(8.92,a),(18,a),(18,b),(8.92,b)]],[(0,1,2,3)],WOOD)
export(m,'CN_Deck_Underlay')

# Masonry skin lies inside the existing collision rim. No ledges across a fall path.
# Correctly oriented masonry segments.
m=Mesh()
for row in range(6):
    for i in range(96):
        a=(i+row%2*.5)*math.tau/96
        box(m,(0,0,0),(.553,.25,.333),STONE,.018,transform((8.69*math.cos(a),8.69*math.sin(a),.18+row*.35),a+PI/2))
for z,r,w,ma in [(2.17,8.69,.15,TRIM),(2.285,8.69,.04,GOLD),(.075,8.57,.06,TRIM)]:ring(m,r,z,w,ma)
for i in range(96):
    a=i*math.tau/96
    ellipsoid(m,(8.505*math.cos(a),8.505*math.sin(a),1.78),(.045,.045,.045),GOLD,8,6)
export(m,'CN_PitMasonry')
m=Mesh();lathe(m,(0,0,0),[(.008,8.57),(.018,8.57)],SAND,128)
for r in (7.88,8.05):ring(m,r,.022,.017,CREAM)
for i in range(260):
    a=rng.uniform(0,math.tau);r=math.sqrt(rng.random())*8.48
    box(m,(r*math.cos(a),r*math.sin(a),.025),(rng.uniform(.028,.08),.016,.004),WOOD2,0)
export(m,'CN_PitSawdust')

# 32 continuous red/ivory stripes. A lower eave brings the tent into the
# cage sightline; the roof still clears the existing chain mounts at R8.
EAVE=13.1;APEX=23.3
def roof_height(t):return EAVE+(APEX-EAVE)*t-.55*math.sin(PI*t)
for sector in range(16):
    m=Mesh();a0=sector*math.tau/16;verts=[];faces=[]
    levels=[1.10,2,5,9,11.8,EAVE]
    for z in levels:
        for i in range(33):
            a=a0+math.tau/16*i/32;r=17.94-.105*(.5+.5*math.cos(i*PI/2))
            verts.append((r*math.cos(a),r*math.sin(a),z))
    for k in range(len(levels)-1):
        for i in range(32):v=k*33+i;faces.append((v,v+33,v+34,v+1))
    for half,material in enumerate((STRIPE,IVORY)):
        m.add(verts,[f for index,f in enumerate(faces) if (index%32)//16==half],material)
    # Roof folds are real geometry, not stacked blockout cubes.
    verts=[];faces=[]
    for j in range(17):
        t=j/16;r=17.94*(1-t)+.62*t;z=roof_height(t)
        for i in range(17):
            a=a0+math.tau/16*i/16
            zz=z-.18*math.sin(PI*i/16)*math.sin(PI*t)
            verts.append((r*math.cos(a),r*math.sin(a),zz))
    for j in range(16):
        # Interior normals face DOWN into the tent so stage lights light the fabric.
        for i in range(16):v=j*17+i;faces.append((v,v+17,v+18,v+1))
    for half,material in enumerate((STRIPE,IVORY)):
        m.add(verts,[f for index,f in enumerate(faces) if (index%16)//8==half],material)
    for a in (a0,a0+PI/16):
        tube(m,[(r*math.cos(a),r*math.sin(a),roof_height(t)-.07) for t in [j/24 for j in range(25)] for r in [17.94*(1-t)+.62*t]],.024,ROPE)
    for z in (3.75,11.95,EAVE-.05):
        tube(m,[(17.80*math.cos(a0+math.tau/16*j/32),17.80*math.sin(a0+math.tau/16*j/32),z) for j in range(33)],.036,GOLD)
    # Valance at the top of every bay.
    vv=[]
    for j in range(33):
        a=a0+math.tau/16*j/32
        for z in (EAVE-.08,EAVE-.55-.62*math.sin(PI*j/32)):
            vv.append((17.71*math.cos(a),17.71*math.sin(a),z))
    m.add(vv,[(2*j,2*j+2,2*j+3,2*j+1) for j in range(32)],RED)
    tube(m,[(17.68*math.cos(a0+math.tau/16*j/32),17.68*math.sin(a0+math.tau/16*j/32),EAVE-.56-.62*math.sin(PI*j/32)) for j in range(33)],.055,GOLD)
    # Small hanging gold tassels underline the sewn scallops.
    a=a0+PI/16
    lathe(m,(17.63*math.cos(a),17.63*math.sin(a),EAVE-1.55),[(0,.13),(.22,.07),(.29,.09),(.35,.04)],GOLD,10)
    for a in (a0,a0+PI/16):
        tube(m,[(17.65*math.cos(a),17.65*math.sin(a),z) for z in (1.1,EAVE-.15)],.045,ROPE)
    export(m,f'CN_CanvasBay_{sector:02}')

# A closed apex, radial ropes and festoon bulbs give the roof a finished crown.
m=Mesh()
lathe(m,(0,0,APEX-.10),[(0,.72),(.32,.42),(.48,.07)],GOLD,48)
ring(m,1.0,APEX-.32,.075,GOLD)
for spoke in range(16):
    a=spoke*math.tau/16
    points=[]
    for j in range(25):
        t=.045+j/24*.90;r=17.94*(1-t)+.62*t
        points.append((r*math.cos(a),r*math.sin(a),roof_height(t)-.42-.35*math.sin(PI*t)))
    tube(m,points,.025,IRON)
    for j in range(22):
        t=.07+j/21*.86;r=17.94*(1-t)+.62*t
        ellipsoid(m,(r*math.cos(a),r*math.sin(a),roof_height(t)-.55-.35*math.sin(PI*t)),(.065,.065,.09),BULB,8,6)
export(m,'CN_CupolaCrown')

# Triangular cloth bunting stays against the perimeter, clear of gameplay views.
m=Mesh()
for i in range(96):
    a=i*math.tau/96;r=16.75;z=10.9-.25*abs(math.sin(a*8))
    tangent=Vector((-math.sin(a),math.cos(a),0));p=Vector((r*math.cos(a),r*math.sin(a),z))
    m.add([tuple(p-tangent*.38),tuple(p+tangent*.38),tuple(p+Vector((-.10*math.cos(a),-.10*math.sin(a),-.76)))],[(0,2,1)],(STRIPE,IVORY,TEAL,GOLD)[i%4])
tube(m,[(16.75*math.cos(i*math.tau/384),16.75*math.sin(i*math.tau/384),10.9-.25*abs(math.sin(i*math.tau/384*8))) for i in range(385)],.024,ROPE)
export(m,'CN_PerimeterBunting')

# Original architectural kit: turned pilasters and hanging marquee lights.
m=Mesh()
lathe(m,(0,0,0),[(0,.23),(.14,.23),(.22,.15),(.36,.14),(3.1,.095),(3.2,.20),(3.3,.21)],IRON,16)
for z in (.12,.32,2.94,3.17):lathe(m,(0,0,z),[(0,.16),(.07,.16)],GOLD,16)
lathe(m,(0,0,3.30),[(0,.19),(.1,.27),(.18,.22),(.55,.22),(.67,.09)],GOLD,16)
ellipsoid(m,(0,0,3.67),(.15,.15,.24),BULB,16,10)
export(m,'CN_Lantern')
m=Mesh()
for x in (-.76,.76):
    box(m,(x,0,.65),(.13,.5,1.3),IRON,.025)
    for z in (.12,1.14):ellipsoid(m,(x,-.265,z),(.06,.03,.06),GOLD,10,6)
for z in (.20,.54,.88,1.22):box(m,(0,0,z),(1.8,.19,.14),WOOD,.02)
box(m,(0,0,1.37),(1.9,.34,.12),GOLD,.025)
export(m,'CN_RingBarrier')

# Hanging ceiling garland ring: one material for bulbs, one for cable.
for r,z in [(10.3,14.6),(14.8,11.9),(17.5,5.8)]:
    m=Mesh();points=[]
    for i in range(513):
        a=i*math.tau/512;points.append((r*math.cos(a),r*math.sin(a),z-.34*abs(math.sin(a*8))))
    tube(m,points,.022,IRON)
    for i in range(160):
        a=i*math.tau/160;zz=z-.34*abs(math.sin(a*8))
        lathe(m,(r*math.cos(a),r*math.sin(a),zz-.11),[(0,.035),(.10,.035)],IRON,8)
        ellipsoid(m,(r*math.cos(a),r*math.sin(a),zz-.15),(.055,.055,.075),BULB,10,6)
    export(m,'CN_Garland_'+str(int(r*10)))

# Entrance arch framed in gold, theatrical sunburst medallion and pleated velvet wings.
m=Mesh()
for s in (-1,1):
    for x,width in [(s*3.1,.40),(s*3.42,.12)]:box(m,(x,0,3.1),(width,.48,6.2),GOLD,.035)
    for j in range(16):
        x=s*(1.6+j*.09);lathe(m,(x,0,.08),[(0,.12),(6.0,.12)],RED,10)
    box(m,(s*3.15,0,.22),(.9,.85,.44),IRON,.06)
arc(m,(0,0,5.4),3.12,0,PI,.22,GOLD,64)
arc(m,(0,-.08,5.4),2.79,0,PI,.065,GOLD,64)
for i in range(33):
    a=i*PI/32;ellipsoid(m,(3.11*math.cos(a),-.26,5.4+3.11*math.sin(a)),(.072,.072,.072),BULB,10,6)
box(m,(0,0,6.45),(5.7,.25,1.0),TEAL,.09)
export(m,'CN_Entrance')

# Cage props retain their measured gameplay footprint; only the visual skin changes.
m=Mesh()
lathe(m,(0,0,0),[(0,.061),(.008,.067),(.019,.066),(.221,.066),(.232,.067),(.24,.061)],TIN,40)
export(m,'CN_TinBody')
m=Mesh()
for z in (.008,.231):
    lathe(m,(0,0,z),[(0,.069),(.006,.070),(.010,.067)],STEEL,40)
for z in (.038,.198):
    lathe(m,(0,0,z),[(0,.0665),(.002,.068),(.004,.0665)],STEEL,40)
lathe(m,(0,0,.237),[(0,.061),(.002,.061)],STEEL,40)
for r in (.046,.055):ring(m,r,.240,.0015,STEEL,40)
export(m,'CN_TinTrim')
m=Mesh()
lathe(m,(0,0,0),[(0,.31),(.065,.32),(.10,.285),(.17,.275),(.68,.285),(.78,.30),(.85,.29)],TEAL,48)
for z,rad in [(.035,.322),(.125,.287),(.705,.295),(.80,.309)]:
    lathe(m,(0,0,z),[(0,rad),(.035,rad)],GOLD,48)
for i in range(16):
    a=i*math.tau/16
    tube(m,[(r*math.cos(a),r*math.sin(a),z) for z,r in [(.16,.278),(.45,.284),(.695,.293)]],.009,GOLD,6)
    for z in (.135,.728):ellipsoid(m,(.298*math.cos(a),.298*math.sin(a),z),(.014,.014,.014),GOLD,8,6)
# Front maker's plaque and five brass stars; no moving hand or timer that reveals elapsed time.
box(m,(0,-.29,.46),(.29,.025,.22),IRON,.025)
for x in (-.125,.125):
    for z in (.38,.54):ellipsoid(m,(x,-.31,z),(.011,.011,.011),GOLD,8,6)
export(m,'CN_ButtonPedestal')

exec(compile((ROOT/'tools/blender/circus_fairground.py').read_text(),str(ROOT/'tools/blender/circus_fairground.py'),'exec'),globals())

# Small, tileable original surface maps, shared across all modules and both scenes.
import numpy as np
N=512
u,v=np.meshgrid(np.arange(N)/N,np.arange(N)/N)
r=np.random.default_rng(432)
noise=r.random((N,N))
wood=.72+.10*np.sin(u*math.tau*9+np.sin(v*math.tau*2)*1.3)+.035*np.sin(u*math.tau*53+np.sin(v*math.tau*4))+.07*noise
fabric=.81+.055*np.sin(u*math.tau*128)*np.sin(v*math.tau*128)+.05*noise
sand=.77+.13*noise+.012*np.sin(u*math.tau*3+v*math.tau*7)
(ART/'Textures').mkdir(exist_ok=True)
for name,data,mats in [('CN_WoodGrain',wood,[WOOD,WOOD2]),('CN_Fabric',fabric,[RED,TEAL,STRIPE,IVORY]),('CN_Sand',sand,[SAND,STONE,TRIM])]:
    rgba=np.ones((N,N,4),dtype=np.float32);rgba[:,:,:3]=data[:,:,None]
    im=bpy.data.images.new(name,width=N,height=N);im.pixels.foreach_set(rgba.ravel())
    im.filepath_raw=str(ART/'Textures'/f'{name}.png');im.file_format='PNG';im.save()
    for index in mats:
        nodes=materials[index].node_tree;tex=nodes.nodes.new('ShaderNodeTexImage');tex.image=im
        mult=nodes.nodes.new('ShaderNodeMixRGB');mult.blend_type='MULTIPLY';mult.inputs[0].default_value=1;mult.inputs[2].default_value=materials[index].diffuse_color
        nodes.links.new(tex.outputs['Color'],mult.inputs[1]);nodes.links.new(mult.outputs[0],nodes.nodes['Principled BSDF'].inputs['Base Color'])
# Pack source and palette for deterministic URP remapping.
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'CircusNightKit.blend'))
(ART/'palette.json').write_text(json.dumps([{'name':m.name,'color':list(m.diffuse_color)} for m in materials],indent=2))
print('CIRCUS_KIT_EXPORT_COMPLETE',flush=True)
