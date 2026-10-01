"""Original elevated construction kit. Run in the live Blender through MCP."""
import bpy,bmesh,math,ast,json
from pathlib import Path
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parents[2];ART=ROOT/'igruha/Assets/_Project/Art/CarryItem/HeistRoutes';SOURCE=ROOT/'igruha/Assets/_Project/Art/Source/CarrySkyscraper'
(ART/'Models').mkdir(parents=True,exist_ok=True)
scene=bpy.data.scenes.new('CarryHeist_Build');bpy.context.window.scene=scene
for old in list(bpy.data.scenes):
 if old!=scene and old.name.startswith('CarryHeist_'):
  for ob in list(old.objects):bpy.data.objects.remove(ob,do_unlink=True)
  bpy.data.scenes.remove(old)
scene.name='CarryHeist_696';materials=[];_box_cache={};STONE=0;PI=math.pi
for n in ast.parse((ROOT/'tools/blender/crying_angels.py').read_text()).body:
 if isinstance(n,(ast.ClassDef,ast.FunctionDef)) and n.name in {'Mesh','transform','box','lathe','tube'}:exec(compile(ast.Module(body=[n],type_ignores=[]),'geometry','exec'))
for e in json.loads((ART.parent/'Roadworks/palette.json').read_text())['materials']:
 mat=bpy.data.materials.get(e['name']) or bpy.data.materials.new(e['name']);mat.diffuse_color=tuple(e['color']);materials.append(mat)
STEEL,EDGE,YELLOW,DARK,IVORY,WOOD,BRICK,TEAL,CONCRETE,RUST=range(10)
def export(m,n):
 o=m.object('HR_'+n);used=sorted(set(m.m));o.data.materials.clear()
 for i in used:o.data.materials.append(materials[i])
 for p,i in zip(o.data.polygons,m.m):p.material_index=used.index(i)
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
 bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/('HR_'+n+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
 o.location=(len(scene.objects)%3*7,len(scene.objects)//3*9,0)
# Deck has real thickness, longitudinal stringers, expansion seams and nonslip strips.
for name,width in [('Deck',4.4),('SideDeck',2.6)]:
 m=Mesh();box(m,(0,0,-.12),(4,width,.24),STEEL,.025)
 for y in (-width/2+.13,width/2-.13):
  box(m,(0,y,-.31),(4,.20,.22),TEAL,.012)
  box(m,(0,y,.007),(4,.085,.014),YELLOW,.003)
 for x in (-1.87,-.94,0,.94,1.87):
  box(m,(x,0,.008),(.027,width-.4,.016),EDGE,.004)
  for y in (-width/2+.24,width/2-.24):lathe(m,(x,y,.014),[(0,.026),(.008,.026)],DARK,6)
 for x in (-1.5,-.5,.5,1.5):
  for y in (-width*.22,width*.22):box(m,(x,y,.009),(.32,.48,.009),DARK,.018)
 export(m,name)
m=Mesh()
for x in (-1.92,0,1.92):
 box(m,(x,0,.55),(.055,.055,1.1),TEAL,.01);box(m,(x,0,.035),(.18,.2,.07),DARK,.008)
for z in (.48,1.08):tube(m,[(-2,0,z),(2,0,z)],.033,YELLOW,8)
box(m,(0,0,.11),(4,.04,.15),TEAL,.006);export(m,'Rail')
# Twin-legged trestle: open below, braces outside the driving envelope.
m=Mesh()
for y in (-2.7,2.7):
 box(m,(0,y,2.1),(.24,.24,4.2),TEAL,.018);box(m,(0,y,.07),(.7,.7,.14),CONCRETE,.04)
 for z in (.8,2.2,3.6):box(m,(0,y,z),(.30,.30,.14),YELLOW,.012)
box(m,(0,0,4.12),(.26,5.8,.26),TEAL,.012)
for y in (-2.7,2.7):tube(m,[(0,y,3.35),(0,y*.63,4.05)],.07,EDGE,8)
export(m,'Trestle')
# Crane stands beside the flyover, with a visible continuous suspension point.
m=Mesh();box(m,(0,0,.2),(2,2,.4),CONCRETE,.06)
for x in (-.42,.42):
 for y in (-.42,.42):tube(m,[(x,y,.4),(x,y,10.5)],.065,YELLOW,10)
for z in range(1,11):
 for x in (-.42,.42):tube(m,[(x,-.42,z),(x,.42,z)],.045,YELLOW,8)
 for y in (-.42,.42):
  tube(m,[(-.42,y,z),(.42,y,z)],.045,YELLOW,8)
  if z<10:tube(m,[(-.42,y,z),(.42,y,z+1)],.027,DARK,6)
for x in (-.32,.32):
 for z in (9.8,10.5):tube(m,[(x,1.4,z),(x,-4.5,z)],.075,YELLOW,10)
 for i in range(6):tube(m,[(x,1.4-i,9.8),(x,.4-i,10.5)],.035,YELLOW,8)
box(m,(0,1.15,9.7),(1,1.6,.45),DARK,.04);box(m,(0,-3.7,10.0),(.85,.55,.48),TEAL,.06)
# Fixed cable ends at rotating beam's hanger, at 5.9 m.
tube(m,[(0,-3.7,9.8),(0,-3.7,6.0)],.025,DARK,10)
export(m,'Crane')
m=Mesh()
for z in (-.24,.24):box(m,(0,0,z),(5.8,.36,.10),YELLOW,.022)
box(m,(0,0,0),(5.8,.075,.48),STEEL,.012)
for x in (-2.85,2.85):box(m,(x,0,0),(.12,.42,.62),DARK,.018)
for x in (-1.75,1.75):
 tube(m,[(x,0,.24),(0,0,.6)],.025,DARK,10)
 box(m,(x,0,.28),(.18,.48,.08),EDGE,.012)
export(m,'SuspendedBeam')
# Cable reel, barrier light, generator: recognisable work islands, no loose clutter in lanes.
m=Mesh()
for x in (-.45,.45):
 tube(m,[(x-.06,0,.52),(x+.06,0,.52)],.54,WOOD,24)
 for y in (-.6,.6):box(m,(x,y,.08),(.36,.18,.16),WOOD,.015)
tube(m,[(-.39,0,.52),(.39,0,.52)],.33,DARK,24)
for x in [i*.065-.33 for i in range(11)]:tube(m,[(x-.023,0,.52),(x+.023,0,.52)],.355,TEAL,24)
export(m,'CableReel')
m=Mesh();box(m,(0,0,.09),(1.6,1,.18),DARK,.035);box(m,(0,0,.66),(1.38,.87,1.05),TEAL,.075)
for x in [-.52+i*.09 for i in range(12)]:box(m,(x,-.442,.70),(.03,.01,.55),DARK,.003)
for y in (-.3,.3):tube(m,[(-.77,y,.2),(-.77,y,1.3),(.77,y,1.3),(.77,y,.2)],.04,EDGE,10)
box(m,(.705,0,.85),(.025,.46,.38),DARK,.025)
for y in (-.12,.12):tube(m,[(.72,y,.89),(.735,y,.89)],.06,YELLOW,12)
export(m,'Generator')
# Pump bay entry totems: large raised droplet and arrow rather than dense text.
m=Mesh();box(m,(0,0,.06),(.9,.65,.12),DARK,.035);box(m,(0,0,1.2),(.12,.14,2.3),TEAL,.018)
box(m,(0,0,1.84),(1.25,.16,1.25),IVORY,.075)
for y in (-.09,.09):
 for i in range(8):
  z=1.60+i*.075;r=.28*(1-i/9)
  box(m,(0,y,z),(r*2,.015,.08),TEAL,.015)
export(m,'PumpSign')
bpy.data.libraries.write(str(SOURCE/'CarryHeistRoutes.blend'),{scene},fake_user=True)
print('CarryHeist kit exported',len(scene.objects),'objects')
