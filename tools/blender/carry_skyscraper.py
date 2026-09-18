"""Original CarryItem construction kit. Metres, Z-up in Blender; Y-up FBX.
Run through Blender MCP in an isolated new scene. --prototype exports one slab.
"""
import bpy, bmesh, math, ast, json, sys
from pathlib import Path
from mathutils import Vector, Matrix
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'igruha/Assets/_Project/Art/CarryItem/Original'
SOURCE=ROOT/'igruha/Assets/_Project/Art/Source/CarrySkyscraper'
for p in (ART/'Models',ART/'Textures',SOURCE):p.mkdir(parents=True,exist_ok=True)
scene=bpy.data.scenes.new('CarrySkyscraper_Build')
bpy.context.window.scene=scene
# Delete only previous scenes owned by this generator; unrelated Blender work stays intact.
for previous in list(bpy.data.scenes):
 if previous != scene and previous.name.startswith('CarrySkyscraper_'):
  for obj in list(previous.objects): bpy.data.objects.remove(obj,do_unlink=True)
  bpy.data.scenes.remove(previous)
for mesh in list(bpy.data.meshes):
 if mesh.name.startswith('CS_') and mesh.users==0:bpy.data.meshes.remove(mesh)
scene.name='CarrySkyscraper_Original'
bpy.context.preferences.filepaths.save_version=0
PI=math.pi;materials=[];palette=[];exports=[]
def mat(name,c,rough=.8,metal=0,texture=''):
 m=bpy.data.materials.get('CS_'+name) or bpy.data.materials.new('CS_'+name);m.diffuse_color=(*c,1);m.use_nodes=True
 bs=m.node_tree.nodes.get('Principled BSDF');bs.inputs['Base Color'].default_value=(*c,1);bs.inputs['Roughness'].default_value=rough;bs.inputs['Metallic'].default_value=metal
 materials.append(m);palette.append(dict(name='CS_'+name,color=[*c,1],roughness=rough,metallic=metal,texture=texture));return len(materials)-1
STONE=mat('Concrete',(.66,.675,.66),.92,0,'Concrete')
EDGE=mat('ConcreteEdge',(.47,.49,.48),.94,0,'Concrete')
WOOD=mat('Timber',(.69,.48,.26),.86,0,'Wood')
PLY=mat('Plywood',(.49,.34,.19),.86,0,'Wood')
STEEL=mat('Galvanized',(.43,.50,.53),.38,.65)
IRON=mat('Iron',(.14,.19,.21),.52,.58)
RUST=mat('Rebar',(.26,.18,.14),.86,.4)
GREEN=mat('SafetyMesh',(.24,.43,.24),.94,0,'Mesh')
RED=mat('HazardRed',(.76,.12,.105),.6)
WHITE=mat('Ivory',(.87,.865,.80),.7)
YELLOW=mat('CraneYellow',(.92,.64,.16),.48,.25)
BRICK=mat('Terracotta',(.55,.255,.19),.91,0,'Concrete')
SACK=mat('CementBag',(.70,.66,.53),.97,0,'Concrete')
BLUE=mat('CabinBlue',(.20,.41,.55),.58,.15)
GLASS=mat('Window',(.26,.48,.62),.22,.35)
RUBBER=mat('Rubber',(.075,.085,.087),.95)
WATER=mat('Puddle',(.37,.47,.50),.12,.35)
CITY=mat('City',(.42,.55,.64),.85)
GOLD=STEEL
for n in ast.parse((ROOT/'tools/blender/crying_angels.py').read_text()).body:
 if isinstance(n,(ast.ClassDef,ast.FunctionDef)) and n.name in {'Mesh','transform','box','lathe','tube','arc','ring','ellipsoid'}:exec(compile(ast.Module(body=[n],type_ignores=[]),'own_geometry','exec'))
_box_cache={}
def export(m,name):
 o=m.object('CS_'+name);used=sorted(set(m.m));o.data.materials.clear()
 for i in used:o.data.materials.append(materials[i])
 for p,i in zip(o.data.polygons,m.m):p.material_index=used.index(i)
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
 bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/('CS_'+name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
 exports.append(dict(name=name,vertices=len(m.v),faces=len(m.f)));return o

def pipe(m,pts,r=.035,ma=STEEL):tube(m,pts,r,ma,8)
def timber(m,pos,size,ma=WOOD):box(m,pos,size,ma,.016)
def bolt(m,x,y,z):lathe(m,(x,y,z),[(0,.022),(.015,.022)],IRON,6)
def pallet(m,z=0):
 for y in (-.59,0,.59):
  timber(m,(0,y,z+.06),(1.6,.18,.12))
  for x in (-.64,0,.64):timber(m,(x,y,z+.16),(.22,.18,.14))
 for x in (-.68,-.34,0,.34,.68):
  timber(m,(x,0,z+.27),(.28,1.4,.10))
  for y in (-.58,.58):bolt(m,x,y,z+.32)
def rebar(m,x,y,z,h):
 pipe(m,[(x,y,z),(x,y,z+h*.8),(x+.1,y,z+h)],.021,RUST)
 for k in range(int(h/.12)):lathe(m,(x,y,z+.07+k*.12),[(0,.025),(.016,.025)],RUST,6)
def board_stripes(m,length,z,y=0):
 box(m,(0,y,z),(length,.065,.22),WHITE,.009)
 for i in range(int(length/.42)):
  x=-length/2+.035+i*.42
  m.add([(x,y-.034,z-.10),(x+.18,y-.034,z-.10),(x+.34,y-.034,z+.10),(x+.16,y-.034,z+.10)],[(0,1,2,3)],RED)
  m.add([(x,y+.034,z-.10),(x+.16,y+.034,z+.10),(x+.34,y+.034,z+.10),(x+.18,y+.034,z-.10)],[(0,1,2,3)],RED)
# Slab: finished structural element, clean walking plane and chamfered arrises.
m=Mesh();box(m,(0,0,-.36),(4,4,.72),STONE,.035)
for y in (-2.001,2.001):
 for x in (-1.55,-.5,.65,1.55):box(m,(x,y,-.37),(.028,.012,.37),EDGE,.002)
for x in (-2.001,2.001):
 for y in (-1.3,.1,1.45):box(m,(x,y,-.4),(.012,.028,.3),EDGE,.002)
export(m,'Slab')
if '--prototype' not in sys.argv:
 m=Mesh()
 for j in range(8):
  y=-1.62+(j+.5)*.405;timber(m,(0,y,-.12),(8,.396,.24))
  for x in (-3.7,3.7):bolt(m,x,y,-.003)
 for x in (-3.5,0,3.5):timber(m,(x,0,-.265),(.19,3.24,.048),PLY)
 export(m,'Bridge')
 m=Mesh();box(m,(0,0,1.30),(4,.38,2.6),STONE,.045)
 for x in (-1.8,-1.4,-.8,-.2,.45,1.05,1.7):rebar(m,x,0,2.57,.50+(x%0.3))
 for y in (-.197,.197):
  for x in (-1.25,0,1.25):
   for z in (.6,1.8):lathe(m,(0,0,0),[(0,.033),(.008,.033)],EDGE,8, matrix=Matrix.Translation(Vector((x,y,z)))@Matrix.Rotation(PI/2,4,'X'))
 export(m,'Wall')
 m=Mesh();box(m,(0,0,3.25),(.74,.74,6.5),STONE,.042)
 for z in (1.5,3,4.5,6):
  for x in (-.375,.375):box(m,(x,0,z),(.01,.73,.014),EDGE,0)
 for x in (-.23,.23):
  for y in (-.23,.23):rebar(m,x,y,6.45,.72)
 export(m,'Column')
 # Solid cast cores replace shipping containers, same playable obstruction envelope.
 m=Mesh();box(m,(0,0,1.06),(4.32,8.64,2.12),STONE,.04)
 for x in (-2.16,2.16):
  for y in (-3.7,-2.2,-.7,.8,2.3,3.8):
   box(m,(x,y,1.05),(.025,.018,2.05),EDGE,0)
   for z in (.45,1.5):box(m,(x,y,z),(.03,.06,.06),IRON,.007)
 for y in (-4.25,4.25):
  for x in (-1.8,-1.2,-.6,0,.6,1.2,1.8):timber(m,(x,y,1.03),(.55,.09,2.05),PLY)
 export(m,'CoreBlock')
 m=Mesh();pallet(m);export(m,'Pallet')
 m=Mesh();pallet(m)
 for k in range(4):
  for j in range(3):
   for i in range(4):box(m,(-.55+i*.36,-.45+j*.42,.42+k*.16),(.335,.39,.145),BRICK,.018)
 export(m,'BrickStack')
 m=Mesh();box(m,(0,0,0),(.35,.2,.2),BRICK,.021)
 for x in (-.10,0,.10):lathe(m,(x,0,.093),[(0,.022),(.006,.022)],IRON,10)
 export(m,'Brick')
 m=Mesh()
 for j in range(4):
  for i in range(4):timber(m,((i-1.5)*.33,0,.07+j*.14),(.31,3.2,.12))
 for y in (-1,1):box(m,(0,y,.31),(1.36,.035,.65),IRON,.006)
 export(m,'Lumber')
 m=Mesh();pallet(m)
 for k in range(3):
  for x in (-.4,.4):
   box(m,(x,0,.47+k*.31),(.77,1.14,.32),SACK,.14)
   box(m,(x,0,.637+k*.31),(.44,.42,.008),RED,.002)
   for y in (-.51,.51):box(m,(x,y,.47+k*.31),(.62,.018,.07),PLY,.008)
 export(m,'CementBags')
 m=Mesh()
 for z in (.08,.91):
  lathe(m,(0,0,z),[(0,.64),(.10,.64)],WOOD,40)
  for a in range(8):
   t=a*PI/4;bolt(m,.44*math.cos(t),.44*math.sin(t),z+.10)
 lathe(m,(0,0,.18),[(0,.31),(.70,.31)],IRON,32)
 for j in range(15):ring(m,.39,.22+j*.043,.025,RUBBER,48)
 export(m,'CableReel')
 m=Mesh()
 pts=[]
 for i in range(360):
  a=i*PI/36;r=.23+.0016*i;pts.append((r*math.cos(a),r*math.sin(a),.033))
 pipe(m,pts,.027,IRON);pipe(m,[pts[-1],(.9,-.3,.033),(1.1,-.5,.033)],.027,IRON)
 export(m,'CableCoil')
 m=Mesh()
 for x in (-1.5,1.5):
  for y in (-.6,.6):
   pipe(m,[(x,y,0),(x,y,7.4)],.043)
   box(m,(x,y,.035),(.26,.26,.07),IRON,.009)
 for z in (.18,2.3,4.6,6.9):
  for y in (-.6,.6):
   pipe(m,[(-1.5,y,z),(1.5,y,z)],.036)
   if z<6.9:
    pipe(m,[(-1.5,y,z),(1.5,y,z+2.3)],.025)
    pipe(m,[(1.5,y,z),(-1.5,y,z+2.3)],.025)
  for x in (-1.5,1.5):pipe(m,[(x,-.6,z),(x,.6,z)],.035)
  if z>.2:
   for j in range(4):timber(m,(0,-.45+j*.30,z),(3.1,.28,.07),PLY)
 # Taut perforated green fabric, separate cutout texture material.
 for z in (1.25,3.55,5.85):box(m,(0,.625,z),(2.91,.012,2.13),GREEN,.002)
 for x in (-1.5,1.5):
  for z in (1.0,3.3,5.6):pipe(m,[(x,-.6,z),(x,.6,z)],.025)
 export(m,'Scaffold')
 m=Mesh()
 for x in (-1.42,1.42):
  pipe(m,[(x,0,0),(x,0,1.24)],.04)
  box(m,(x,0,.035),(.38,.55,.07),EDGE,.025)
 for z in (.55,1.12):board_stripes(m,3,z)
 box(m,(0,0,.17),(3,.07,.18),PLY,.009)
 export(m,'Guardrail')
 m=Mesh()
 for x in (-1,1):
  pipe(m,[(x,-.32,0),(x,0,1.1),(x,.32,0)],.033)
 for z in (.56,.98):board_stripes(m,2.25,z)
 export(m,'Barricade')
 m=Mesh();box(m,(0,0,.035),(.42,.42,.07),RUBBER,.028)
 lathe(m,(0,0,.07),[(0,.19),(.16,.15),(.28,.11),(.42,.067),(.58,.04)],RED,24)
 lathe(m,(0,0,.29),[(0,.126),(.13,.094)],WHITE,24);export(m,'Cone')
 m=Mesh()
 for x in (-.30,.30):
  pipe(m,[(x,-.45,.05),(x,-.32,.72),(x,.8,.65)],.032)
  pipe(m,[(x,.58,.65),(x,.91,.67)],.044,RUBBER)
 # Open folded steel tray, not a closed box.
 v=[(-.3,-.5,.48),(.3,-.5,.48),(.3,.35,.48),(-.3,.35,.48),(-.48,-.62,.91),(.48,-.62,.91),(.48,.48,.91),(-.48,.48,.91)]
 m.add(v,[(0,1,2,3),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],STEEL)
 for a,b in ((4,5),(5,6),(6,7),(7,4)):pipe(m,[v[a],v[b]],.025)
 lathe(m,(0,0,0),[(-.075,.24),(.075,.24)],RUBBER,24,matrix=Matrix.Translation(Vector((0,-.58,.24)))@Matrix.Rotation(PI/2,4,'Y'))
 pipe(m,[(-.37,-.58,.24),(.37,-.58,.24)],.04,IRON);export(m,'Wheelbarrow')
 m=Mesh()
 pipe(m,[(0,0,.07),(0,0,1.45),(0,-.33,1.55),(0,-.55,1.42)],.115,STEEL)
 for z in (.15,.95):lathe(m,(0,0,z),[(0,.19),(.07,.19)],IRON,20)
 box(m,(0,0,.04),(.42,.42,.08),IRON,.022);export(m,'Standpipe')
 m=Mesh();pipe(m,[(0,0,0),(0,.20,.2),(0,.40,.2)],.12);export(m,'Spout')
 m=Mesh()
 for x in (-.26,.26):pipe(m,[(x,.18,0),(x,-.18,2.2)],.025)
 for j in range(8):pipe(m,[(-.26,.14-j*.045,.2+j*.26),(.26,.14-j*.045,.2+j*.26)],.022)
 export(m,'Ladder')
 m=Mesh();lathe(m,(0,0,0),[(0,.18),(.38,.235),(.40,.235),(.40,.205),(.06,.16)],STEEL,24)
 arc(m,(0,0,.38),.24,0,PI,.012,IRON,18);export(m,'Bucket')
 m=Mesh()
 for y in (-.7,.7):
  for x in (-.65,.65):timber(m,(x,y,.52),(.09,.12,1.04),PLY)
 timber(m,(0,0,1.06),(1.6,1.85,.12))
 box(m,(-.3,.2,1.145),(.65,.85,.008),WHITE,.002)
 lathe(m,(.42,-.2,1.12),[(0,.20),(.10,.23),(.24,.13),(.27,.01)],WHITE,24)
 export(m,'Workbench')
 m=Mesh()
 for y in (-.36,.36):
  pipe(m,[(-.65,y,.15),(-.65,y,.85),(.65,y,.85),(.65,y,.15),(-.65,y,.15)],.045,IRON)
 box(m,(0,0,.53),(1.13,.65,.54),YELLOW,.08)
 box(m,(.05,-.343,.55),(.78,.025,.34),IRON,.012)
 for x in (-.2,.15,.4):lathe(m,(0,0,0),[(0,.067),(.025,.067)],WHITE,16,matrix=Matrix.Translation(Vector((x,-.36,.57)))@Matrix.Rotation(PI/2,4,'X'))
 export(m,'Generator')
 m=Mesh()
 for x in (-.45,.45):
  pipe(m,[(x,-.48,.22),(x,0,1.02),(x,.5,.1)],.045,IRON)
  lathe(m,(0,0,0),[(-.07,.23),(.07,.23)],RUBBER,24,matrix=Matrix.Translation(Vector((x,-.48,.23)))@Matrix.Rotation(PI/2,4,'Y'))
 lathe(m,(0,0,0),[(0,.28),(.15,.44),(.62,.49),(.88,.33),(.89,.27),(.68,.31)],YELLOW,32,matrix=Matrix.Translation(Vector((0,.2,.91)))@Matrix.Rotation(PI/3,4,'X'))
 arc(m,(.57,0,.98),.30,0,2*PI,.028,IRON,24);export(m,'Mixer')
 m=Mesh();box(m,(0,0,1.08),(1.08,1.17,2.16),BLUE,.09);box(m,(0,0,2.2),(1.2,1.29,.16),WHITE,.08)
 box(m,(0,-.60,1.08),(.83,.045,1.91),GLASS,.045)
 for x in (-.40,.40):box(m,(x,-.635,1.08),(.06,.035,1.93),WHITE,.008)
 for z in (.14,2.02):box(m,(0,-.635,z),(.84,.035,.06),WHITE,.008)
 box(m,(.25,-.67,1.05),(.045,.045,.2),WHITE,.012);export(m,'SiteCabin')
 m=Mesh()
 for y in (-.15,.15):box(m,(0,y,0),(2.9,.08,.36),RED,.013)
 box(m,(0,0,0),(2.9,.22,.06),IRON,.012);export(m,'Beam')
 # Tower crane: square lattice mast, jib, counterweight, cabin, stays and suspended hook.
 m=Mesh()
 for x in (-.9,.9):
  for y in (-.9,.9):pipe(m,[(x,y,-48),(x,y,12)],.14,YELLOW)
 for z in range(-48,12,3):
  for y in (-.9,.9):
   pipe(m,[(-.9,y,z),(.9,y,z)],.095,YELLOW)
   pipe(m,[(-.9,y,z),(.9,y,z+3)],.075,YELLOW)
  for x in (-.9,.9):
   pipe(m,[(x,-.9,z),(x,.9,z)],.095,YELLOW)
   pipe(m,[(x,-.9,z),(x,.9,z+3)],.075,YELLOW)
 box(m,(0,0,12),(3.4,3.4,.48),YELLOW,.1)
 box(m,(1.9,-.7,11.8),(1.6,1.8,2.1),YELLOW,.09);box(m,(2,-1.62,12.1),(1.23,.02,1.25),GLASS,.025)
 for y in (-.7,.7):
  for z in (13,14.6):pipe(m,[(-12,y,z),(32,y,z)],.09,YELLOW)
  for x in range(-12,32,2):
   pipe(m,[(x,y,13),(x+2,y,14.6)],.05,YELLOW);pipe(m,[(x,y,14.6),(x+2,y,13)],.05,YELLOW)
 for x in range(-12,33,2):pipe(m,[(x,-.7,13),(x,.7,13)],.055,YELLOW)
 for y in (-.65,.65):
  pipe(m,[(0,y,13),(0,y,20)],.07,YELLOW)
  pipe(m,[(-11,y,14.6),(0,y,20),(28,y,14.6)],.026,IRON)
 for x in (-10,-8.7,-7.4):box(m,(x,0,12),(1.05,2.1,2.4),EDGE,.05)
 box(m,(19,0,12.9),(1.3,1.7,.4),RED,.05)
 for y in (-.12,.12):pipe(m,[(19,y,12.9),(19,y,2.6)],.021,IRON)
 lathe(m,(19,0,2.25),[(0,.20),(.38,.20),(.53,.11)],RED,16)
 arc(m,(19,0,2.1),.24,PI/2,PI*1.85,.065,IRON,18)
 pipe(m,[(19,0,1.9),(16,0,.4)],.023,IRON);pipe(m,[(19,0,1.9),(22,0,.4)],.023,IRON)
 for z in (.2,.55):box(m,(19,0,z),(6.3,.65,.13),RED,.02)
 box(m,(19,0,.38),(6.3,.11,.36),RED,.015);export(m,'Crane')
 m=Mesh()
 box(m,(0,0,20),(12,12,40),CITY,.14)
 for z in range(1,40,3):
  for y in (-6.01,6.01):box(m,(0,y,z),(11.8,.045,.13),STEEL,.005)
  for x in (-6.01,6.01):box(m,(x,0,z),(.045,11.8,.13),STEEL,.005)
 for i in (-4.5,-1.5,1.5,4.5):
  for y in (-6.025,6.025):box(m,(i,y,20),(.12,.06,39.8),EDGE,.005)
  for x in (-6.025,6.025):box(m,(x,i,20),(.06,.12,39.8),EDGE,.005)
 box(m,(0,0,41),(7.5,8,2),CITY,.1);export(m,'CityTower')
 m=Mesh()
 for z in range(0,37,4):box(m,(0,0,z),(14,12,.5),EDGE,.04)
 for x in (-6,0,6):
  for y in (-5,0,5):box(m,(x,y,18),(.8,.8,36),STONE,.04)
 for y in (-6.02,6.02):box(m,(0,y,30),(14,.04,10),GREEN,.001)
 export(m,'CityFrame')
 m=Mesh();v=[(0,0,.012)];n=48
 for i in range(n):
  a=i*2*PI/n;r=1+.12*math.sin(a*3)+.08*math.sin(a*7);v.append((math.cos(a)*r,math.sin(a)*r*.62,.012))
 m.add(v,[(0,i+1,(i+1)%n+1) for i in range(n)],WATER);export(m,'Puddle')
 # Bottle pieces use body-relative radii, height normalized per section.
 for name,profile in {
  'BottleBody':[(0,.42),(.025,.48),(.06,.49),(.12,.49),(.14,.50),(.18,.50),(.20,.48),(.72,.48),(.74,.50),(.79,.50),(.81,.48),(.94,.48),(1,.46)],
  'BottleShoulder1':[(0,.46),(.15,.46),(.75,.39),(1,.3715)],
  'BottleShoulder2':[(0,.3715),(.25,.35),(.75,.27),(1,.243)],
  'BottleNeck':[(0,.243),(.18,.17),(.24,.157),(.82,.157),(.86,.17),(1,.17)],
  'BottleCap':[(0,.20),(.10,.2145),(.86,.2145),(1,.20)]}.items():
  m=Mesh();lathe(m,(0,0,0),profile,BLUE,48);export(m,name)
 m=Mesh();pipe(m,[(-.095,0,-.04),(-.095,0,.075),(-.06,0,.105),(.06,0,.105),(.095,0,.075),(.095,0,-.04)],.027,WHITE)
 pipe(m,[(-.095,0,-.04),(-.095,-.16,-.04)],.018,STEEL);pipe(m,[(.095,0,-.04),(.095,-.16,-.04)],.018,STEEL);export(m,'BottleHandle')
 m=Mesh()
 for y in (-1.16,0,1.16):timber(m,(0,y,.09),(2.88,.22,.18),PLY)
 for x in (-1.35,-.9,-.45,0,.45,.9,1.35):timber(m,(x,0,.24),(.17,2.88,.12))
 for x in (-1.34,1.34):
  for y in (-1.34,1.34):pipe(m,[(x,y,.29),(x,y,1.65)],.04,STEEL)
 for z in (.35,.75,1.18,1.62):
  pipe(m,[(-1.34,-1.34,z),(1.34,-1.34,z),(1.34,1.34,z),(-1.34,1.34,z),(-1.34,-1.34,z)],.034,STEEL)
 for q in (-.8,-.27,.27,.8):
  for y in (-1.34,1.34):pipe(m,[(q,y,.35),(q,y,1.62)],.025,STEEL)
  for x in (-1.34,1.34):pipe(m,[(x,q,.35),(x,q,1.62)],.025,STEEL)
 pipe(m,[(-1.28,0,.5),(-1.42,0,.5)],.09,STEEL);export(m,'TankCage')
 m=Mesh();pipe(m,[(-1.35,-1.35,1.67),(1.35,-1.35,1.67),(1.35,1.35,1.67),(-1.35,1.35,1.67),(-1.35,-1.35,1.67)],.065,WHITE)
 lathe(m,(0,0,1.65),[(0,.23),(.09,.23)],WHITE,32);export(m,'TankBand')
# Deterministic authored surface and soft-particle textures.
rng=np.random.default_rng(535);sz=512;yy,xx=np.mgrid[0:sz,0:sz]
for name in ('Concrete','Wood','Mesh','Particle'):
 a=np.ones((sz,sz,4),np.float32)
 if name=='Concrete':
  v=.93+.014*rng.standard_normal((sz,sz))+.009*np.sin(xx*.047)*np.sin(yy*.03);v-=.05*(rng.random((sz,sz))>.998);a[:,:,:3]=v[:,:,None]
 elif name=='Wood':
  v=.85+.023*np.sin(xx*.45+np.sin(yy*.013)*2)+.01*np.sin(xx*1.5)+.013*rng.standard_normal((sz,sz));a[:,:,:3]=v[:,:,None]
 elif name=='Mesh':
  a[:,:,:3]=.92;a[:,:,3]=np.where((xx%12<8)|(yy%12<8),1,0)
 else:
  rr=np.sqrt(((xx-sz/2)/(sz/2))**2+((yy-sz/2)/(sz/2))**2);a[:,:,3]=np.maximum(0,1-rr)**2
 img=bpy.data.images.new('CS_'+name,width=sz,height=sz,alpha=True);img.pixels.foreach_set(a.ravel());img.filepath_raw=str(ART/'Textures'/('CS_'+name+'.png'));img.file_format='PNG';img.save()
(ART/'palette.json').write_text(json.dumps({'materials':palette,'models':exports},indent=2))
bpy.data.libraries.write(str(SOURCE/'CarrySkyscraper.blend'), {scene}, fake_user=True, compress=True)
print(json.dumps({'exported':exports,'source':str(SOURCE/'CarrySkyscraper.blend')}))
