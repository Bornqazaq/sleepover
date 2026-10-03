"""Own water cart kit, metres, Z up. Execute through Blender MCP."""
import bpy, bmesh, math, ast, json
from pathlib import Path
from mathutils import Vector, Matrix
ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'igruha/Assets/_Project/Art/CarryItem/Original'
SOURCE=ROOT/'igruha/Assets/_Project/Art/Source/CarrySkyscraper'
scene=bpy.data.scenes.new('CarryWaterCart_Build');bpy.context.window.scene=scene
for old in list(bpy.data.scenes):
 if old!=scene and old.name.startswith('CarryWaterCart_'):
  for obj in list(old.objects):bpy.data.objects.remove(obj,do_unlink=True)
  bpy.data.scenes.remove(old)
scene.name='CarryWaterCart_Original'
for mesh in list(bpy.data.meshes):
 if mesh.name.startswith('CW_') and mesh.users==0:bpy.data.meshes.remove(mesh)
PI=math.pi;materials=[];palette=[];_box_cache={};STONE=0
for n in ast.parse((ROOT/'tools/blender/crying_angels.py').read_text()).body:
 if isinstance(n,(ast.ClassDef,ast.FunctionDef)) and n.name in {'Mesh','transform','box','lathe','tube'}:exec(compile(ast.Module(body=[n],type_ignores=[]),'geometry','exec'))
exec(compile((ROOT/'tools/blender/carry_geometry.py').read_text(), 'carry_geometry', 'exec'))
def mat(name,c,rough=.4,metal=.0,transparent=False):
 name='CW_'+name;m=bpy.data.materials.get(name) or bpy.data.materials.new(name);m.diffuse_color=c;m.use_nodes=True
 bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Base Color'].default_value=c;bs.inputs['Roughness'].default_value=rough;bs.inputs['Metallic'].default_value=metal;bs.inputs['Alpha'].default_value=c[3]
 materials.append(m);palette.append(dict(name=name,color=list(c),roughness=rough,metallic=metal,transparent=transparent,doubleSided=transparent));return len(materials)-1
STEEL=mat('BrushedSteel',(.53,.65,.69,1),.28,.7)
DARK=mat('Chassis',(.055,.085,.10,1),.45,.6)
RUBBER=mat('Rubber',(.024,.03,.035,1),.86)
TEAM=mat('TeamEnamel',(.88,.67,.22,1),.33,.25)
IVORY=mat('Ivory',(.91,.91,.80,1),.5)
GLASS=mat('TankGlass',(.58,.86,.88,.20),.17,.05,True)
BLUE=mat('Valve',(.035,.25,.36,1),.33,.4)
YELLOW=mat('TapEnamel',(.92,.56,.12,1),.34,.35)
exports=[]
def export(m,name):
 o=m.object('CW_'+name);used=sorted(set(m.m));o.data.materials.clear()
 for i in used:o.data.materials.append(materials[i])
 for p,i in zip(o.data.polygons,m.m):p.material_index=used.index(i)
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
 bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/('CS_'+name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
 exports.append((name,len(m.f)));return o
def pipe(m,pts,r,ma):tube(m,pts,r,ma,12)
def torus(m,center,r,thick,ma,axis='Z'):
 pts=[]
 for i in range(33):
  a=i*2*PI/32;v=Vector((r*math.cos(a),r*math.sin(a),0))
  if axis=='X':v=Matrix.Rotation(PI/2,3,'Y')@v
  if axis=='Y':v=Matrix.Rotation(PI/2,3,'X')@v
  pts.append(tuple(v+Vector(center)))
 pipe(m,pts,thick,ma)
# Chassis has a low rounded skid, crossmembers and four independent castor forks.
m=Mesh()
for x in (-.36,.36):box(m,(x,0,.32),(.075,1.22,.095),DARK,.018)
for y in (-.47,0,.47):box(m,(0,y,.31),(.87,.065,.075),STEEL,.012)
box(m,(0,0,.385),(.87,1.12,.06),STEEL,.02)
for x in (-.48,.48):
 for y in (-.4,.4):
  lathe(m,(x,y,.32),[(0,.075),(.04,.075),(.045,.055)],STEEL,20)
  for dx in (-.055,.055):box(m,(x+dx,y,.255),(.026,.12,.17),STEEL,.01)
  box(m,(x,y,.215),(.16,.065,.065),DARK,.01)
export(m,'WaterCartChassis')
# Open tank: separate panes, metal corner protectors, rolled rim and rear pouring lip.
m=Mesh()
box(m,(0,0,.44),(.8,1.08,.07),IVORY,.027)
for x in (-.4,.4):
 box(m,(x,0,.70),(.018,1.06,.5),GLASS,.004)
 for y in (-.53,.53):box(m,(x,y,.72),(.065,.065,.54),STEEL,.013)
for y in (-.54,.54):box(m,(0,y,.7),(.77,.018,.5),GLASS,.004)
for z in (.47,.97):
 for x in (-.415,.415):box(m,(x,0,z),(.045,1.14,.047),STEEL,.011)
 for y in (-.55,.55):box(m,(0,y,z),(.87,.045,.047),STEEL,.011)
# Level graduation on both sides, legible dark marks over ivory label.
for x in (-.416,.416):
 box(m,(x,.30,.70),(.01,.095,.43),IVORY,.002)
 for i in range(9):box(m,(x*1.014,.285,.515+i*.044),(.008,.06 if i%2==0 else .035,.008),DARK,.001)
# Rear spillway, does not close the top.
box(m,(0,-.62,.945),(.32,.18,.026),STEEL,.008)
for x in (-.165,.165):box(m,(x,-.62,.975),(.02,.18,.075),STEEL,.008)
export(m,'WaterCartTank')
m=Mesh()
for x in (-.433,.433):
 box(m,(x,0,.87),(.027,1.06,.105),TEAM,.006)
 for y in (-.42,.42):box(m,(x,y,.57),(.035,.09,.18),TEAM,.008)
for y in (-.566,.566):box(m,(0,y,.86),(.71,.018,.13),TEAM,.005)
export(m,'WaterCartTrim')
# Wheel around origin, axle X. Chamfered tyre, twin hubs, tread cuts.
m=Mesh();rot=Matrix.Rotation(PI/2,4,'Y')
lathe(m,(0,0,0),[(-.055,.16),(-.045,.21),(-.026,.22),(.026,.22),(.045,.21),(.055,.16)],RUBBER,32,matrix=rot)
lathe(m,(0,0,0),[(-.06,.0),(-.06,.12),(.06,.12),(.06,0)],STEEL,24,matrix=rot)
lathe(m,(0,0,0),[(-.069,.0),(-.069,.04),(.069,.04),(.069,0)],DARK,12,matrix=rot)
export(m,'WaterCartWheel')
# Compact U grip at the authored handle anchor.
m=Mesh();pipe(m,[(-.10,0,-.12),(-.10,0,0),(-.075,0,.04),(.075,0,.04),(.10,0,0),(.10,0,-.12)],.025,STEEL)
pipe(m,[(-.073,0,.04),(.073,0,.04)],.033,RUBBER);export(m,'WaterCartGrip')
# Standpipe with overhead swan neck, union collars, valve wheel, braces and supply hose.
m=Mesh();box(m,(0,0,.055),(.65,.56,.11),DARK,.025)
for x in (-.24,.24):
 for y in (-.19,.19):lathe(m,(x,y,.11),[(0,.028),(.024,.028)],STEEL,6)
pipe(m,[(0,0,.1),(0,0,1.62),(0,.035,1.75),(0,.15,1.82),(0,1.24,1.82),(0,1.36,1.77),(0,1.4,1.66)],.072,YELLOW)
for z in (.24,.65,1.38):lathe(m,(0,0,z),[(0,.085),(.065,.085)],STEEL,20)
pipe(m,[(0,1.4,1.70),(0,1.4,1.60)],.083,STEEL)
for x in (-.22,.22):pipe(m,[(x,0,.14),(0,0,.73)],.026,STEEL)
pipe(m,[(0,-.03,.92),(0,-.18,.92)],.045,STEEL)
torus(m,(0,-.21,.92),.16,.022,BLUE,'Y')
for a in (0,2*PI/3,4*PI/3):pipe(m,[(0,-.21,.92),(.15*math.cos(a),-.21,.92+.15*math.sin(a))],.013,BLUE)
pipe(m,[(0,0,.24),(.2,-.18,.18),(.45,-.32,.06),(.62,-.05,.055),(.56,.27,.055),(.28,.42,.055),(-.1,.39,.055),(-.33,.12,.055),(-.3,-.22,.055)],.041,RUBBER)
box(m,(.16,-.09,1.30),(.28,.036,.18),IVORY,.009)
for z in (1.265,1.31,1.355):box(m,(.16,-.112,z),(.17,.009,.016),DARK,.002)
export(m,'WaterTapStation')
m=Mesh()
for x in (-.85,.85):box(m,(x,0,.006),(.07,1.8,.015),DARK,.002)
for y in (-.88,.88):box(m,(0,y,.006),(1.77,.04,.015),DARK,.002)
for i in range(19):box(m,(-.80+i*.089,0,.01),(.028,1.73,.012),STEEL,.002)
export(m,'WaterTapGrate')
# Low receiving hopper, plumbed into the existing tall storage tank.
m=Mesh()
box(m,(-1.96,0,.29),(.60,.95,.06),STEEL,.012)
for y in (-.48,.48):box(m,(-1.96,y,.40),(.62,.035,.22),STEEL,.008)
for x in (-2.26,-1.66):box(m,(x,0,.4),(.035,1,.22),YELLOW,.008)
for x in (-2.17,-1.73):
 for y in (-.38,.38):box(m,(x,y,.15),(.055,.055,.28),DARK,.007)
pipe(m,[(-1.67,0,.32),(-1.48,0,.32),(-1.40,0,.43),(-1.40,0,.68)],.065,BLUE)
export(m,'WaterReceivingHopper')
p=ART/'palette.json';data=json.loads(p.read_text());names={e['name'] for e in palette};data['materials']=[e for e in data['materials'] if e['name'] not in names]+palette;p.write_text(json.dumps(data,indent=2)+'\n')
# Assemble the source scene for inspection after exporting local-space modules.
wheel=bpy.data.objects['CW_WaterCartWheel']
for i,(x,y) in enumerate([(-.48,-.4),(.48,-.4),(-.48,.4),(.48,.4)]):
 obj=wheel if i==0 else wheel.copy()
 if i:scene.collection.objects.link(obj)
 obj.location=(x,y,.22)
grip=bpy.data.objects['CW_WaterCartGrip']
for i,(x,y) in enumerate([(-.45,-.65),(.45,-.65),(-.45,.65),(.45,.65)]):
 obj=grip if i==0 else grip.copy()
 if i:scene.collection.objects.link(obj)
 obj.location=(x,y,1)
bpy.data.objects['CW_WaterTapStation'].location.x=2.4
bpy.data.objects['CW_WaterTapGrate'].location=(2.4,1.4,0)
bpy.data.objects['CW_WaterReceivingHopper'].location.x=6
bpy.data.libraries.write(str(SOURCE/'WaterCart.blend'),{scene},fake_user=True)
print(exports)
