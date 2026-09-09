"""Large-scale exhibits for the night gallery: floor-to-dome columns and long
ruined wall segments. Run through blender_client.py after crying_angels.py and
crying_angels_ruins.py have created the base kit."""
import ast
import bpy, bmesh, math, json, random
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT/'igruha/Assets/_Project/Art/CryingAngels'
PI = math.pi
RNG = random.Random(934)
scene = next(s for s in bpy.data.scenes if s.name == 'CA_MoonlitGallery')
bpy.context.window.scene = scene
kit = bpy.data.collections['00_ReusableKit']
names = ['PaleLimestone','BlueSlate','CarvedStone','Marble','AgedBrass','Crevices','MoonGlass','OxidizedIron','Cobweb']
materials = [bpy.data.materials['CA_'+n] for n in names]
STONE,DARK,TRIM,FLOOR,GOLD,CRACK,GLASS,IRON,WEB = range(9)
_box_cache = {}
tree = ast.parse((ROOT/'tools/blender/crying_angels.py').read_text())
helpers = ast.Module(body=[n for n in tree.body if isinstance(n,(ast.FunctionDef,ast.ClassDef))],type_ignores=[])
exec(compile(helpers,'gallery_geometry','exec'))
assets = {}

# Great column, 16.2 m: the layout stretches it a little so the abacus meets the dome coffers.
COLUMN_HEIGHT = 16.2
m=Mesh()
box(m,(0,0,.20),(1.90,1.90,.40),DARK,.06)
box(m,(0,0,.50),(1.62,1.62,.22),STONE,.05)
lathe(m,(0,0,0),[(.60,.82),(.72,.82),(.80,.70),(.98,.66),(1.10,.62)],STONE,48)
lathe(m,(0,0,0),[(1.10,.62),(1.40,.60),(14.55,.53),(14.85,.58)],TRIM,80,flutes=24)
lathe(m,(0,0,0),[(14.85,.60),(15.02,.70),(15.28,.78),(15.42,.74),(15.60,.92),(15.74,.92)],STONE,48)
for k in range(16):
    a=2*PI*k/16
    leaf=[(.56*math.cos(a),.56*math.sin(a),14.70),(.72*math.cos(a),.72*math.sin(a),15.12),(.90*math.cos(a),.90*math.sin(a),15.40)]
    tube(m,leaf,[.12,.11,.03],STONE,6)
box(m,(0,0,15.94),(1.85,1.85,.28),STONE,.05)
box(m,(0,0,COLUMN_HEIGHT-.06),(1.95,1.95,.12),DARK,.02)
for z in [3.15,7.90]: lathe(m,(0,0,0),[(z,.578),(z+.07,.578)],IRON,80)
for k in range(4):
    x=-.45+k*.30
    tube(m,[(x,-.86,.70),(x+.07,-.862,.55),(x+.02,-.862,.40),(x+.11,-.862,.24)],.009,CRACK,4)
assets['CA_GreatColumn']=m.object('CA_GreatColumn')

# Ruined wall segment: ashlar courses with a jagged top, a moulding band and a plaque niche.
L=5.2; T=.9
m=Mesh()
box(m,(0,0,.14),(L+.30,T+.30,.28),DARK,.05)
profile=[3.40,3.05,2.55,2.25,2.70,3.25]
z=.28
for j,h in enumerate([.50,.50,.50,.50,.50,.50,.42]):
    n=5 if j%2 else 6
    for k in range(n):
        w=L/n; x=-L/2+w*(k+.5)
        top=profile[min(5,int((x+L/2)/L*6))]+RNG.uniform(-.08,.08)
        hh=min(h,top-z)
        if hh<.12: continue
        # Mostly one stone: an even checker of two tones read as toy bricks from across the hall.
        box(m,(x,0,z+hh/2),(w-.03,T-RNG.uniform(0,.05),hh-.02),TRIM if RNG.random()<.18 else STONE,.03)
    z+=h
box(m,(0,0,2.05),(L,T+.12,.14),TRIM,.03)
for side in [-1,1]:
    box(m,(-1.25*side,side*(T/2-.02),1.30),(1.10,.06,1.00),DARK,.02)
    arc(m,(-1.25*side,side*(T/2+.05),1.30),.32,0,2*PI,.02,GOLD,24)
    for zz in [.95,1.65]: box(m,(-1.25*side,side*(T/2+.045),zz),(.92,.012,.02),GOLD,.001)
# Rubble hugs the footing so the collider box stays a wall, not a moat.
for i in range(7):
    s=RNG.uniform(.16,.28)
    # transform() rotates about the wall origin, so the offset goes into the matrix, not the box.
    box(m,(0,0,0),(s,s*.7,s*.5),STONE if i%2 else TRIM,.02,transform(pos=(RNG.uniform(-L/2,L/2),RNG.choice([-1,1])*(T/2+s*.35),s*.26),rot=RNG.uniform(-.5,.5)))
assets['CA_RuinedWall']=m.object('CA_RuinedWall')

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
print(json.dumps({n:[round(d,2) for d in o.dimensions] for n,o in assets.items()}))
