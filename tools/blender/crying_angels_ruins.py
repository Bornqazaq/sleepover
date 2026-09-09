"""Extend the existing Blender gallery with distinct ruined museum exhibits.
Run through blender_client.py after crying_angels.py has created the base kit.
"""
import ast
import bpy, bmesh, math, json, random
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT/'igruha/Assets/_Project/Art/CryingAngels'
PI = math.pi
RNG = random.Random(930)
scene = next(s for s in bpy.data.scenes if s.name == 'CA_MoonlitGallery')
bpy.context.window.scene = scene
kit = bpy.data.collections['00_ReusableKit']
names = ['PaleLimestone','BlueSlate','CarvedStone','Marble','AgedBrass','Crevices','MoonGlass','OxidizedIron','Cobweb']
materials = [bpy.data.materials['CA_'+n] for n in names]
STONE,DARK,TRIM,FLOOR,GOLD,CRACK,GLASS,IRON,WEB = range(9)
_box_cache = {}
# Reuse geometry primitives without executing the base generator or its renders.
tree = ast.parse((ROOT/'tools/blender/crying_angels.py').read_text())
helpers = ast.Module(body=[n for n in tree.body if isinstance(n,(ast.FunctionDef,ast.ClassDef))],type_ignores=[])
exec(compile(helpers,'gallery_geometry','exec'))
assets = {}

m=Mesh()
plinth(m,.28,1.05,1.05)
lathe(m,(0,0,.28),[(0,.39),(.10,.42),(.18,.34),(1.5,.29),(1.66,.34)],TRIM,40,16)
# Jagged broken capital, offset rings and carved iron bands.
for k in range(12):
    a=k*PI/6
    box(m,(.30*math.cos(a),.30*math.sin(a),2.00+RNG.uniform(-.12,.15)),(.15,.14,.32),STONE,.025,transform(rot=a))
for z in [.48,1.05,1.59]: lathe(m,(0,0,0),[(z,.352),(z+.038,.352)],DARK,40)
assets['CA_ShatteredPillar']=m.object('CA_ShatteredPillar')

m=Mesh();plinth(m,.26,1.05,.85)
for z,w in [(.32,.78),(.40,.69)]: box(m,(0,0,z),(w,.64,.10),TRIM)
verts=[(-.31,-.23,.43),(.31,-.23,.43),(.31,.23,.43),(-.31,.23,.43),(-.20,-.15,2.05),(.20,-.15,2.05),(.20,.15,2.05),(-.20,.15,2.05),(0,0,2.43)]
m.add(verts,[(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,8),(5,6,8),(6,7,8),(7,4,8)],DARK)
for z in [.70,.90,1.1,1.3,1.5,1.7]:
    box(m,(0,-.245+(z-.43)*.049,z),(.12,.015,.018),GOLD,.001)
arc(m,(0,-.212,1.65),.12,0,PI*2,.013,GOLD)
assets['CA_MourningObelisk']=m.object('CA_MourningObelisk')

m=Mesh();plinth(m,.19,1.08,.91)
# A continuous shroud: rounded head, shoulder mass, long hanging folds.
profile=[(.20,.47),(.40,.43),(.8,.37),(1.15,.33),(1.48,.44),(1.62,.35),(1.73,.22),(1.90,.24),(2.07,.19),(2.17,.02)]
vs=[];fs=[];n=56
for j,(z,r) in enumerate(profile):
    for k in range(n):
        a=k*2*PI/n;fold=.035*math.cos(a*12)*(1-j/len(profile))
        vs.append(((r+fold)*math.cos(a),(r*.68+fold)*math.sin(a)+.045*math.sin(j),z))
for j in range(len(profile)-1):
    for k in range(n): fs.append((j*n+k,j*n+(k+1)%n,(j+1)*n+(k+1)%n,(j+1)*n+k))
m.add(vs,fs,STONE)
for k in range(7):
    x=-.30+k*.1
    tube(m,[(x,-.3,.28),(x*.84,-.25,.7),(x*.75,-.245,1.15),(x*.8,-.29,1.48)],.011,TRIM,5)
assets['CA_ShroudedFigure']=m.object('CA_ShroudedFigure')

m=Mesh()
box(m,(0,0,.08),(2.42,1.05,.16),TRIM,.06)
box(m,(0,0,.36),(2.16,.91,.48),DARK,.055)
box(m,(0,0,.65),(2.36,1.05,.16),STONE,.055)
box(m,(0,0,.75),(2.20,.92,.09),TRIM,.04)
for side in [-1,1]:
    for x in [-.78,-.26,.26,.78]:
        box(m,(x,side*.468,.36),(.36,.024,.28),TRIM,.02)
        arc(m,(x,side*.49,.39),.095,0,2*PI,.012,GOLD,20)
# Recumbent effigy carved into the lid.
ellipsoid(m,(-.78,0,.83),(.17,.16,.085),STONE)
ellipsoid(m,(.12,0,.82),(.68,.25,.10),STONE)
assets['CA_Sarcophagus']=m.object('CA_Sarcophagus')

m=Mesh()
matrix=Matrix.Translation(Vector((-1.04,0,.40)))@Matrix.Rotation(PI/2,4,'Y')
lathe(m,(0,0,0),[(0,.39),(.12,.41),(.19,.33),(1.85,.33),(2,.37)],TRIM,40,18,matrix)
for i in range(5):
    box(m,(RNG.uniform(-1.1,1.1),RNG.uniform(-.36,.36),.08),(.22,.2,.15),STONE,.04,transform(rot=RNG.random()*PI))
assets['CA_FallenColumn']=m.object('CA_FallenColumn')

m=Mesh()
lathe(m,(0,0,0),[(0,.42),(.10,.42),(.14,.31),(.21,.30),(.25,.19),(.34,.27),(.49,.39),(.64,.33),(.71,.19),(.75,.23),(.8,.24)],STONE,40)
lathe(m,(0,0,0),[(.795,.23),(.83,.18),(.86,0)],TRIM,40)
for side in [-1,1]: arc(m,(side*.30,0,.53),.16,-PI/2,PI/2,.036,GOLD)
assets['CA_FuneraryUrn']=m.object('CA_FuneraryUrn')

m=Mesh()
for i in range(17):
    w=RNG.uniform(.09,.29); h=RNG.uniform(.035,.13)
    box(m,(RNG.uniform(-.95,.95),RNG.uniform(-.6,.6),h*.5),(w,w*.7,h),TRIM if i%3 else STONE,.016,transform(rot=RNG.random()*PI))
assets['CA_StoneFragments']=m.object('CA_StoneFragments')

m=Mesh();vs=[];fs=[]
for j in range(19):
    for k in range(9):
        x=(k/8-.5)*1.35;z=4-j/18*4
        if j==18:z+=.30*(k%3)+RNG.random()*.18
        vs.append((x,.12*math.sin(k*1.6+j*.25)*(j/18),z))
for j in range(18):
    for k in range(8):
        if j>15 and (k+j)%7==0:continue
        a=j*9+k;fs.append((a,a+1,a+10,a+9))
m.add(vs,fs,DARK)
tube(m,[(-.8,0,4.03),(.8,0,4.03)],.045,IRON)
assets['CA_TatteredBanner']=m.object('CA_TatteredBanner')

for name,obj in assets.items():
    old=kit.objects.get(name)
    if old: bpy.data.objects.remove(old,do_unlink=True)
    obj.name=name
    bm=bmesh.new();bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(obj.data);bm.free()
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/f'{name}.fbx'),use_selection=True,object_types={'MESH'},mesh_smooth_type='FACE',add_leaf_bones=False,bake_anim=False,axis_forward='-Z',axis_up='Y',path_mode='RELATIVE')
    scene.collection.objects.unlink(obj);kit.objects.link(obj)

path=ART/'Models/CA_KitManifest.json'
rows=json.loads(path.read_text());rows=[r for r in rows if r['name'] not in assets]
rows += [dict(name=n,triangles=sum(len(p.vertices)-2 for p in o.data.polygons),vertices=len(o.data.vertices),dimensions=list(o.dimensions)) for n,o in assets.items()]
path.write_text(json.dumps(rows,indent=2)+'\n')
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'igruha/Assets/_Project/Art/Source/CryingAngels/CA_MoonlitGallery.blend'))
print('Exported eight additional ruined museum assets.')
