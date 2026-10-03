"""CarryItem transfer pump. Metres, Blender Z up; run via Blender MCP."""
import bpy, bmesh, math, ast, json
from pathlib import Path
from mathutils import Vector, Matrix
ROOT = Path(__file__).resolve().parents[2]
ART = ROOT/'igruha/Assets/_Project/Art/CarryItem/Original'
SOURCE = ROOT/'igruha/Assets/_Project/Art/Source/CarrySkyscraper'
scene = bpy.data.scenes.new('CarryPump_Build')
bpy.context.window.scene = scene
for old in list(bpy.data.scenes):
    if old != scene and old.name.startswith('CarryPump_'):
        for obj in list(old.objects): bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.scenes.remove(old)
scene.name = 'CarryPump_Original'
PI=math.pi; materials=[]; palette=[]; _box_cache={}; STONE=0
for n in ast.parse((ROOT/'tools/blender/crying_angels.py').read_text()).body:
    if isinstance(n,(ast.ClassDef,ast.FunctionDef)) and n.name in {'Mesh','transform','box','lathe','tube'}:
        exec(compile(ast.Module(body=[n],type_ignores=[]),'geometry','exec'))
exec(compile((ROOT/'tools/blender/carry_geometry.py').read_text(), 'carry_geometry', 'exec'))
_lathe = lathe
def lathe(m,pos,profile,mat=STONE,segments=32,flutes=0,matrix=None):
    # Rotate a part around its own centre, then translate into the assembly.
    _lathe(m,(0,0,0),profile,mat,segments,flutes,
           Matrix.Translation(Vector(pos)) @ (matrix or Matrix.Identity(4)))
def mat(name,c,rough=.4,metal=0):
    name='CP_'+name; m=bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color=(*c,1); m.use_nodes=True
    bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    bs.inputs['Base Color'].default_value=(*c,1);bs.inputs['Roughness'].default_value=rough;bs.inputs['Metallic'].default_value=metal
    materials.append(m);palette.append(dict(name=name,color=[*c,1],roughness=rough,metallic=metal))
    return len(materials)-1
DARK=mat('Frame',(.047,.070,.075),.48,.65)
YELLOW=mat('Enamel',(.94,.48,.065),.3,.35)
STEEL=mat('Steel',(.60,.70,.72),.23,.8)
TEAL=mat('Housing',(.055,.23,.25),.32,.55)
RUBBER=mat('Hose',(.022,.033,.035),.72)
BRASS=mat('Brass',(.64,.40,.12),.25,.72)
IVORY=mat('Dial',(.89,.88,.73),.52)
RED=mat('Red',(.6,.055,.025),.35,.15)
GREEN=mat('Green',(.09,.7,.34),.25)
def pipe(m,pts,r,ma): tube(m,pts,r,ma,12)
def ring(m,c,r,thick,ma,axis='Z'):
    rot=Matrix.Rotation(PI/2,3,'Y' if axis=='X' else 'X') if axis!='Z' else Matrix.Identity(3)
    pipe(m,[tuple(Vector(c)+rot@Vector((r*math.cos(i*2*PI/40),r*math.sin(i*2*PI/40),0))) for i in range(41)],thick,ma)
def export(m,name):
    o=m.object('CP_'+name);used=sorted(set(m.m));o.data.materials.clear()
    for i in used:o.data.materials.append(materials[i])
    for p,i in zip(o.data.polygons,m.m):p.material_index=used.index(i)
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/('CS_'+name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
    return o
m=Mesh()
for x in (-.30,.30):
    box(m,(x,0,.075),(.10,.91,.15),DARK,.025)
    for y in (-.34,.34):box(m,(x,y,.026),(.15,.16,.05),RUBBER,.012)
box(m,(0,0,.17),(.7,.82,.07),STEEL,.018)
for x in (-.24,.24):
    for y in (-.29,.29):lathe(m,(x,y,.205),[(0,.023),(.025,.023)],BRASS,6)
# Ribbed electric motor, horizontal shaft along Y, protective rear guard.
ry=Matrix.Rotation(PI/2,4,'X')
lathe(m,(0,-.1,.44),[(-.27,.16),(-.23,.20),(.2,.20),(.25,.16)],YELLOW,36,matrix=ry)
for y in (-.24,-.18,-.12,-.06,0,.06):ring(m,(0,y,.44),.203,.014,YELLOW,'Y')
ring(m,(0,.17,.44),.198,.027,DARK,'Y')
for x in (-.13,-.065,0,.065,.13):pipe(m,[(x,.185,.30),(x,.185,.58)],.009,STEEL)
# Cast pump volute on the front, bolted plate and brass intake coupling.
lathe(m,(0,-.39,.44),[(-.10,.14),(-.055,.215),(.055,.215),(.10,.14)],TEAL,40,matrix=ry)
for i in range(8):
    a=i*2*PI/8;lathe(m,(.17*math.cos(a),-.502,.44+.17*math.sin(a)),[(0,.021),(.024,.021)],STEEL,6,matrix=ry)
pipe(m,[(0,-.47,.44),(0,-.66,.44)],.072,BRASS)
ring(m,(0,-.63,.44),.082,.012,STEEL,'Y')
# Protection handle and control plate, gauge faces the approaching player (-X).
for x in (-.32,.32):pipe(m,[(x,-.32,.20),(x,-.32,.83),(x,-.23,.91),(x,.24,.91),(x,.32,.83),(x,.32,.20)],.027,DARK)
pipe(m,[(-.32,.02,.91),(.32,.02,.91)],.035,RUBBER)
box(m,(-.29,-.02,.70),(.065,.38,.22),YELLOW,.022)
rx=Matrix.Rotation(PI/2,4,'Y')
lathe(m,(-.345,-.06,.73),[(-.018,0),(-.018,.105),(.018,.105),(.018,0)],STEEL,32,matrix=rx)
lathe(m,(-.37,-.06,.73),[(0,0),(0,.088),(.006,.088)],IVORY,32,matrix=rx)
for i in range(11):
    a=(-.75+i*.15)*PI
    pipe(m,[(-.379,-.06+math.cos(a)*.069,.73+math.sin(a)*.069),(-.379,-.06+math.cos(a)*.082,.73+math.sin(a)*.082)],.003,DARK)
pipe(m,[(-.382,-.06,.73),(-.382,-.112,.772)],.004,RED)
for y,ma in ((.085,GREEN),(.145,RED)):
    lathe(m,(-.331,y,.68),[(0,.026),(.024,.026)],ma,20,matrix=rx)
# Delivery line runs into storage behind the unit.
pipe(m,[(.10,-.38,.59),(.10,-.38,.75),(.39,-.38,.75),(.53,-.38,.66),(.53,-.20,.36),(.53,.65,.36)],.044,BRASS)
for y in (-.29,.34):ring(m,(.53,y,.36),.054,.012,STEEL,'Y')
export(m,'WaterPump')
# A visible fan rotates only while pumping; object pivot is its shaft.
m=Mesh();ring(m,(0,0,0),.125,.01,DARK,'Y')
for i in range(5):
    a=i*2*PI/5
    pipe(m,[(0,0,0),(.11*math.cos(a),0,.11*math.sin(a))],.019,STEEL)
export(m,'WaterPumpRotor')
# Weighted suction strainer: stays just below the moving waterline.
m=Mesh();lathe(m,(0,0,0),[(-.16,.062),(-.14,.075),(-.025,.075),(0,.043),(.09,.043)],STEEL,24)
for i in range(8):
    a=i*2*PI/8
    pipe(m,[(.076*math.cos(a),.076*math.sin(a),-.13),(.076*math.cos(a),.076*math.sin(a),-.04)],.006,DARK)
ring(m,(0,0,-.14),.074,.01,BRASS)
export(m,'WaterPumpNozzle')
(ART/'pump-palette.json').write_text(json.dumps({'materials':palette},indent=2)+'\n')
bpy.data.objects['CP_WaterPumpRotor'].location=(0,.205,.44)
bpy.data.objects['CP_WaterPumpNozzle'].location=(-.8,-.1,.7)
bpy.data.libraries.write(str(SOURCE/'WaterPump.blend'),{scene},fake_user=True)
print('Carry pump exported: '+str(sum(len(o.data.polygons) for o in scene.objects if o.type=='MESH'))+' faces')
