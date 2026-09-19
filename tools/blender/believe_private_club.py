"""IGR-565: original modular club shell. Run via Blender MCP, metres, FBX -Z/Y.

Only the named scene is replaced. All other Blender work is preserved.
Wall modules: 2 m wide, 4.12 m high; ceiling: 2 x 2 m. No game geometry.
"""
import bpy
import math
import numpy as np
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'igruha/Assets/_Project/Art/BelievePrivateClub'
SOURCE = ROOT / 'igruha/Assets/_Project/Art/Source/BelievePrivateClub'
for folder in (ART / 'Models', ART / 'Textures', SOURCE):
    folder.mkdir(parents=True, exist_ok=True)
SCENE = 'Believe_PrivateClub_Modules'
previous = bpy.context.window.scene
if previous.name == SCENE:
    previous = next(s for s in bpy.data.scenes if s.name != SCENE)
    bpy.context.window.scene = previous
old = bpy.data.scenes.get(SCENE)
if old:
    for obj in list(old.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.scenes.remove(old)
scene = bpy.data.scenes.new(SCENE)
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
materials = {}
for name, color, rough, metal in [
    ('Navy', (.023,.039,.068), .76, 0),
    ('Wood', (.055,.033,.026), .56, 0),
    ('Brass', (.29,.17,.064), .43, .65),
    ('Velvet', (.17,.018,.036), .96, 0),
    ('Fringe', (.30,.20,.083), .78, .15),
    ('Ceiling', (.018,.024,.034), .86, 0),
    ('Carpet', (.085,.027,.040), 1, 0),
    ('Lining', (.74,.55,.29), .48, .18),
    ('Bulb', (1,.75,.39), .45, 0),
]:
    m = bpy.data.materials.get('BPC_' + name) or bpy.data.materials.new('BPC_' + name)
    m.diffuse_color = (*color,1)
    m.use_nodes = True
    node = m.node_tree.nodes.get('Principled BSDF')
    node.inputs['Base Color'].default_value = (*color,1)
    node.inputs['Roughness'].default_value = rough
    node.inputs['Metallic'].default_value = metal
    materials[name] = m

def texture(name, pixels):
    h,w,_ = pixels.shape
    im = bpy.data.images.new(name, width=w, height=h, alpha=True)
    im.pixels.foreach_set(pixels.astype(np.float32).ravel())
    im.filepath_raw = str(ART / 'Textures' / (name+'.png'))
    im.file_format = 'PNG'
    im.save()
    return im

# A woven, seamless ogee carpet: restrained burgundy leaves over ink wool.
n=1024
y,x=np.mgrid[0:n,0:n]/n
rng=np.random.default_rng(565)
dx=np.sin(x*math.tau*2)
dy=np.sin(y*math.tau*2)
ogee=np.exp(-((np.abs(dx)+np.abs(dy)-1.04)/.045)**2)
leaf=np.exp(-((dx*dx+dy*dy-.31)/.055)**2)
weave=(np.sin(x*math.tau*256)*np.cos(y*math.tau*256))*.013
grain=rng.normal(0,.008,(n,n))+weave
rgb=np.zeros((n,n,4)); rgb[:,:,3]=1
for c,(base,ink) in enumerate(zip((.125,.110,.130),(.10,.022,.040))):
    rgb[:,:,c]=np.clip(base+ink*(ogee*.65+leaf*.38)+grain,0,1)
texture('BPC_WovenCarpet',rgb)
# Fine grain for wood and navy stained timber, no photographic/store source.
g=np.sin(x*math.tau*37+np.sin(y*math.tau*2)*1.6)*.10
g+=np.sin(x*math.tau*121+np.sin(y*math.tau*3))*.05
g+=rng.normal(0,.025,(n,n))
wood=np.ones((n,n,4)); wood[:,:,:3]=np.clip(.72+g[:,:,None],0,1)
texture('BPC_TimberGrain',wood)
# Soft particles use an original radial/noise texture, including edge alpha.
u=x*2-1; v=y*2-1; r=np.sqrt(u*u+v*v)
smoke=np.ones((n,n,4))
smoke[:,:,:3]=.86
smoke[:,:,3]=np.clip(1-r,0,1)**2*(.6+.25*np.sin(u*13+v*7)*np.sin(v*11-u*5))
texture('BPC_Smoke',smoke)

parts=[]
modules=[]
def finish(o, mat):
    o.data.materials.append(materials[mat]); parts.append(o); return o

def box(p,s,mat,bevel=.012):
    bpy.ops.mesh.primitive_cube_add(size=1,location=p)
    o=bpy.context.object; o.scale=s
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        m=o.modifiers.new('Soft joinery edges','BEVEL'); m.width=min(bevel,min(s)*.24); m.segments=2
        bpy.ops.object.modifier_apply(modifier=m.name)
        m=o.modifiers.new('Joinery normals','WEIGHTED_NORMAL'); bpy.ops.object.modifier_apply(modifier=m.name)
    return finish(o,mat)

def mesh(name,verts,faces,mat,smooth=False):
    data=bpy.data.meshes.new(name); data.from_pydata(verts,[],faces); data.update()
    o=bpy.data.objects.new(name,data); scene.collection.objects.link(o); finish(o,mat)
    if smooth:
        for p in data.polygons:p.use_smooth=True
    return o

def tube(points,radius,mat):
    curve=bpy.data.curves.new('Cord','CURVE');curve.dimensions='3D';curve.bevel_depth=radius;curve.bevel_resolution=2
    spl=curve.splines.new('POLY');spl.points.add(len(points)-1)
    for p,co in zip(spl.points,points):p.co=(*co,1)
    o=bpy.data.objects.new('Cord',curve);scene.collection.objects.link(o)
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.object.convert(target='MESH');finish(bpy.context.object,mat)

def lathe(profile,mat,segments=64):
    verts=[(r*math.cos(i*math.tau/segments),r*math.sin(i*math.tau/segments),z) for r,z in profile for i in range(segments)]
    faces=[]
    for j in range(len(profile)-1):
        for i in range(segments):
            a=j*segments+i;b=j*segments+(i+1)%segments
            faces.append((a,b,b+segments,a+segments))
    return mesh('Turned profile',verts,faces,mat,True)

def module(name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]
    bpy.ops.object.join();o=bpy.context.object;o.name='BPC_'+name
    scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    # Architectural UVs in metres; projection chosen by polygon normal.
    uv=o.data.uv_layers.get('UVMap') or o.data.uv_layers.new(name='UVMap')
    for poly in o.data.polygons:
        axis=max(range(3),key=lambda a:abs(poly.normal[a]))
        for li in poly.loop_indices:
            co=o.data.vertices[o.data.loops[li].vertex_index].co
            uv.data[li].uv=(co.y,co.z) if axis==0 else ((co.x,co.z) if axis==1 else (co.x,co.y))
    modules.append(o);parts.clear();return o

# Raised rectangular joinery, recessed blue-black panels and a fine brass top rail.
box((0,.055,2.06),(2,.11,4.12),'Navy')
for xx in (-.96,0,.96):box((xx,-.025,2.06),(.07,.10,4.12),'Wood')
for zz in (.10,1.10,3.86,4.06):box((0,-.04,zz),(2,.12,.075),'Wood')
for xx in (-.49,.49):
    for zz,hh in ((.60,.78),(2.50,2.52)):
        for sx in (-1,1):box((xx+sx*.385,-.036,zz),(.018,.026,hh),'Wood',.004)
        for sz in (-1,1):box((xx,-.036,zz+sz*hh*.5),(.79,.026,.018),'Wood',.004)
box((0,-.10,3.90),(2,.026,.019),'Brass',.003)
module('WallPanel_2m')

# Timber cornice, small dentils and restrained brass bead. Pivot at lower rear.
for yy,zz,depth,height in ((0,.035,.15,.07),(-.025,.095,.20,.05),(-.045,.16,.25,.08)):
    box((0,yy,zz),(2,depth,height),'Wood')
box((0,-.176,.128),(2,.020,.019),'Brass',.003)
for i in range(20):box((-.95+i*.1,-.12,.046),(.045,.055,.053),'Wood',.004)
module('Cornice_2m')
box((0,-.02,.08),(2,.13,.16),'Wood')
box((0,-.04,.168),(2,.17,.026),'Wood',.006)
module('Skirting_2m')

# Ceiling coffers: shallow enough to disappear into the dark except near pendant.
box((0,0,.07),(2,2,.12),'Ceiling')
for a in (-.95,.95):
    box((a,0,-.015),(.10,2,.09),'Wood')
    box((0,a,-.015),(1.8,.10,.09),'Wood')
module('CeilingCoffer_2m')

# Velvet pair with real pleats, pulled-in waist, scalloped swag and woven fringe.
for side in (-1,1):
    verts=[];faces=[];nx=30;nz=36
    for j in range(nz+1):
        z=.045+j/nz*3.94
        gather=math.exp(-((z-1.45)/.62)**2)
        for i in range(nx+1):
            t=i/nx
            xx=side*(.99-(.72-.36*gather)*t)
            yy=-.18-.09*math.cos(t*math.tau*6)*(1-.55*gather)-.11*gather
            verts.append((xx,yy,z))
    for j in range(nz):
        for i in range(nx):
            a=j*(nx+1)+i;faces.append((a,a+1,a+nx+2,a+nx+1))
    o=mesh('Pleated velvet',verts,faces,'Velvet',True)
    sol=o.modifiers.new('Cloth thickness','SOLIDIFY');sol.thickness=.014
    bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=sol.name)
    # Gold tieback and hanging tassel.
    tube([(side*(.60+i*.018),-.36-.025*math.sin(i/20*math.pi),1.38+.10*math.sin(i/20*math.pi)) for i in range(21)],.015,'Fringe')
    tube([(side*.62,-.36,1.40),(side*.56,-.38,1.12)],.012,'Fringe')
    tassel=lathe([(.018,1.14),(.035,1.10),(.029,1.06),(.055,.88),(.048,.86)],'Fringe',16)
    tassel.location.x=side*.56;tassel.location.y=-.38
    for i in range(12):
        a=i*math.tau/12
        tube([(side*.56+.022*math.cos(a),-.38+.022*math.sin(a),1.055),(side*.56+.048*math.cos(a),-.38+.048*math.sin(a),.86)],.003,'Brass')
verts=[];faces=[]
for j in range(9):
    for i in range(49):
        xx=-1+i/24;edge=3.48+.45*abs(xx)**1.8
        z=edge+(4.02-edge)*j/8
        verts.append((xx,-.22-.045*math.cos(j/8*math.tau*3)-.13*(1-xx*xx),z))
for j in range(8):
    for i in range(48):
        a=j*49+i;faces.append((a,a+1,a+50,a+49))
mesh('Draped valance',verts,faces,'Velvet',True)
edge=[(-1+i/40,-.37,3.48+.45*abs(-1+i/40)**1.8) for i in range(81)]
tube(edge,.018,'Fringe')
for xx,yy,zz in edge:tube([(xx,yy,zz),(xx,yy,zz-.09)],.005,'Fringe')
module('Curtain_2m')

# Оконный проём за шторой, закрытый ставнями. Клуб сидит глубоко внутри
# здания (спека 14.1: ни окон, ни улицы, ни времени суток), и до сих пор
# шторы висели на глухой панели — по ним читалось, что за ними ничего нет.
# Ставни объясняют штору и дают стене глубину, не открывая улицу.
# Комната со стороны -Y, поэтому проём уходит вглубь по +Y, а обвязка
# рамы идёт по периметру: сплошной блок спереди закрыл бы саму нишу.
SB_W, SB_LOW, SB_HIGH, SB_DEPTH = 1.46, .93, 3.30, .17
SB_MID = (SB_LOW+SB_HIGH)*.5
SB_H = SB_HIGH-SB_LOW
for side in (-1, 1):
    box((side*(SB_W*.5+.075), SB_DEPTH*.5-.02, SB_MID), (.15, SB_DEPTH, SB_H+.30), 'Wood', .014)
box((0, SB_DEPTH*.5-.02, SB_HIGH+.075), (SB_W+.30, SB_DEPTH, .15), 'Wood', .014)
box((0, SB_DEPTH*.5-.02, SB_LOW-.055), (SB_W+.30, SB_DEPTH+.08, .11), 'Wood', .012)
# Дно ниши: тёмная доска, за ней ничего нет и быть не должно.
box((0, SB_DEPTH-.012, SB_MID), (SB_W, .024, SB_H), 'Navy', .004)
for side in (-1, 1):
    leaf_x = side*SB_W*.25
    box((leaf_x, .055, SB_MID), (SB_W*.5-.016, .050, SB_H-.03), 'Wood', .010)
    for k in range(4):
        z = SB_LOW + SB_H*(k+.5)/4
        box((leaf_x, .034, z), (SB_W*.5-.115, .022, SB_H/4-.085), 'Navy', .008)
    tube([(leaf_x, .026, SB_LOW+.10), (leaf_x, .026, SB_HIGH-.10)], .006, 'Brass')
# Шпингалет по стыку створок и петли на обвязке.
tube([(0, .022, 1.98), (0, .022, 2.26)], .013, 'Brass')
for z in (SB_LOW+.28, SB_MID, SB_HIGH-.28):
    for side in (-1, 1):
        box((side*(SB_W*.5-.022), .050, z), (.055, .040, .090), 'Brass', .006)
module('ShutterBay_2m')

# A high-resolution round woven rug with a rolled bound edge, no collision.
lathe([(0,.012),(3.85,.012),(3.86,.008),(3.85,0)],'Carpet',160)
for r in (3.65,3.70,3.79):
    tube([(r*math.cos(i*math.tau/160),r*math.sin(i*math.tau/160),.017) for i in range(161)],.007,'Velvet')
module('RoundRug')
box((0,0,-.003),(2,2,.006),'Carpet',0)
module('CarpetTile_2m')

# Pendant lip at z=0; underside and bulb below the shade cannot cast a false lid shadow.
lathe([(.45,0),(.44,.05),(.38,.14),(.26,.26),(.13,.30),(.10,.40),(.065,.43)],'Brass')
lathe([(.44,.012),(.375,.13),(.25,.25),(.12,.28)],'Lining')
lathe([(.45,0),(.456,.006),(.454,.019),(.442,.028)],'Brass')
bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,radius=.075,location=(0,0,.025))
finish(bpy.context.object,'Bulb')
module('Pendant')
# One repeatable chain link, alternated at runtime by the editor builder.
tube([(.031*math.cos(i*math.tau/24),0,.048+.048*math.sin(i*math.tau/24)) for i in range(25)],.007,'Brass')
module('ChainLink')

def export(o):
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/(o.name+'.fbx')),use_selection=True,
        object_types={'MESH'},axis_forward='-Z',axis_up='Y',global_scale=1,
        apply_unit_scale=True,bake_anim=False,add_leaf_bones=False,use_mesh_modifiers=True)

# Export a panel first; set BPC_EXPORT_ALL after checking its Unity import.
# BPC_EXPORT_MODELS сужает список: FBX лежат в LFS, и переписывать девять
# старых модулей ради одного нового незачем.
wanted=globals().get('BPC_EXPORT_MODELS',None)
if wanted is None:
    export(modules[0])
    if globals().get('BPC_EXPORT_ALL',False):
        for o in modules[1:]:export(o)
else:
    for o in modules:
        if o.name in wanted or o.name.replace('BPC_','') in wanted:export(o)
bpy.data.libraries.write(str(SOURCE/'BelievePrivateClub.blend'),{scene},fake_user=True,compress=True)
print('BPC modules:',[(o.name,len(o.data.polygons)) for o in modules])
bpy.context.window.scene=previous
