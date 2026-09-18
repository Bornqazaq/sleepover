"""CarryItem atmosphere kit (IGR-537): living city, street far below, storeys above
and below the deck, hero tank, slewing cranes, flags, site machinery and richer
surface textures. Metres, Z-up in Blender; Y-up FBX.

Run through Blender MCP from the repository root:
    python3 tools/blender_client.py execute_code --file tools/blender/carry_skyscraper_city.py

Adds to Art/CarryItem/Original next to carry_skyscraper.py and merges palette.json
(entries with the same name are overwritten, everything else is kept).
"""
import bpy, bmesh, math, ast, json, sys
from pathlib import Path
from mathutils import Vector, Matrix
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'igruha/Assets/_Project/Art/CarryItem/Original'
SOURCE=ROOT/'igruha/Assets/_Project/Art/Source/CarrySkyscraper'
for p in (ART/'Models',ART/'Textures',SOURCE):p.mkdir(parents=True,exist_ok=True)
scene=bpy.data.scenes.new('CarrySkyscraper_City_Build')
bpy.context.window.scene=scene
# Only scenes owned by this generator are replaced; other Blender work stays intact.
for previous in list(bpy.data.scenes):
 if previous!=scene and previous.name.startswith('CarrySkyscraper_City'):
  for obj in list(previous.objects): bpy.data.objects.remove(obj,do_unlink=True)
  bpy.data.scenes.remove(previous)
for mesh in list(bpy.data.meshes):
 if mesh.name.startswith('CS_') and mesh.users==0:bpy.data.meshes.remove(mesh)
scene.name='CarrySkyscraper_City'
bpy.context.preferences.filepaths.save_version=0
PI=math.pi;materials=[];palette={};exports=[]
PALETTE_PATH=ART/'palette.json'
existing=json.loads(PALETTE_PATH.read_text()) if PALETTE_PATH.exists() else {'materials':[],'models':[]}
for e in existing['materials']:palette[e['name']]=e
def mat(name,c,rough=.8,metal=0,texture='',emission=0,tiling=1,emissionTexture='',unlit=False,cutout=False,transparent=False,doubleSided=False):
 m=bpy.data.materials.get('CS_'+name) or bpy.data.materials.new('CS_'+name);m.diffuse_color=(*c[:3],1);m.use_nodes=True
 bs=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
 bs.inputs['Base Color'].default_value=(*c[:3],1);bs.inputs['Roughness'].default_value=rough;bs.inputs['Metallic'].default_value=metal
 materials.append(m)
 palette['CS_'+name]=dict(name='CS_'+name,color=[c[0],c[1],c[2],c[3] if len(c)>3 else 1],roughness=rough,metallic=metal,texture=texture,
  emission=emission,tiling=tiling,emissionTexture=emissionTexture,unlit=unlit,cutout=cutout,transparent=transparent,doubleSided=doubleSided)
 return len(materials)-1
# Shared palette of the base kit, declared identically so indices resolve here too.
STONE=mat('Concrete',(.66,.675,.66),.92,0,'Concrete')
EDGE=mat('ConcreteEdge',(.47,.49,.48),.94,0,'Concrete')
WOOD=mat('Timber',(.69,.48,.26),.86,0,'Wood')
PLY=mat('Plywood',(.49,.34,.19),.86,0,'Wood')
STEEL=mat('Galvanized',(.43,.50,.53),.38,.65)
IRON=mat('Iron',(.14,.19,.21),.52,.58)
RUST=mat('Rebar',(.26,.18,.14),.86,.4)
GREEN=mat('SafetyMesh',(.24,.43,.24),.94,0,'Mesh',cutout=True,doubleSided=True)
RED=mat('HazardRed',(.76,.12,.105),.6)
WHITE=mat('Ivory',(.87,.865,.80),.7)
YELLOW=mat('CraneYellow',(.92,.64,.16),.48,.25)
BRICK=mat('Terracotta',(.55,.255,.19),.91,0,'Concrete')
SACK=mat('CementBag',(.70,.66,.53),.97,0,'Concrete')
BLUE=mat('CabinBlue',(.20,.41,.55),.58,.15)
GLASS=mat('Window',(.26,.48,.62),.22,.35)
RUBBER=mat('Rubber',(.075,.085,.087),.95)
# New surfaces of this pass.
ASPHALT=mat('Asphalt',(.13,.13,.14),.95,0,'Asphalt',tiling=.12)
DIRT=mat('Dirt',(.40,.33,.26),1,0,'Ground',tiling=.06)
PAINT=mat('RoadPaint',(.85,.84,.78),.8)
LEAF=mat('TreeLeaf',(.38,.47,.22),.9)
TRUNK=mat('TreeTrunk',(.30,.22,.15),.95)
FGLASS=mat('FacadeGlass',(.50,.60,.74),.32,.25,'FacadeGlass',emission=2.6,emissionTexture='FacadeGlass_E')
FBRICK=mat('FacadeBrick',(.66,.52,.42),.92,0,'FacadeBrick',emission=2.2,emissionTexture='FacadeBrick_E')
FBAND=mat('FacadeBand',(.64,.64,.62),.85,0,'FacadeBand',emission=2.2,emissionTexture='FacadeBand_E')
FDARK=mat('FacadeDark',(.22,.26,.32),.40,.35,'FacadeGlass',emission=3.0,emissionTexture='FacadeGlass_E')
ROOF=mat('Roof',(.30,.31,.33),.92)
TEAMA=mat('TeamA',(.25,.55,1),.62)
TEAMB=mat('TeamB',(1,.45,.2),.62)
TARP=mat('TarpBlue',(.16,.34,.62),.55)
LAMP=mat('LampWarm',(1,.92,.72),.3,0,emission=3.5)
BEACON=mat('Beacon',(1,.12,.08),.3,0,emission=4)
CLOUD=mat('Cloud',(1,.96,.90,1),1,0,'Cloud',unlit=True,transparent=True,doubleSided=True)
HAZE=mat('Haze',(.80,.82,.88,.26),1,0,unlit=True,transparent=True,doubleSided=True)
PAINTA=mat('TeamAPaint',(.25,.55,1),.9,0,'Arrow',cutout=True)
PAINTB=mat('TeamBPaint',(1,.45,.2),.9,0,'Arrow',cutout=True)
VWHITE=mat('VehicleWhite',(.86,.86,.84),.45,.1)
GOLD=STEEL
for n in ast.parse((ROOT/'tools/blender/crying_angels.py').read_text()).body:
 if isinstance(n,(ast.ClassDef,ast.FunctionDef)) and n.name in {'Mesh','transform','box','lathe','tube','arc','ring','ellipsoid'}:exec(compile(ast.Module(body=[n],type_ignores=[]),'own_geometry','exec'))
_box_cache={}
def export(m,name,obj=None):
 o=obj or m.object('CS_'+name);used=sorted(set(m.m));o.data.materials.clear()
 for i in used:o.data.materials.append(materials[i])
 for p,i in zip(o.data.polygons,m.m):p.material_index=used.index(i)
 bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
 bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/('CS_'+name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
 exports.append(dict(name=name,vertices=len(m.v),faces=len(m.f)));return o
def pipe(m,pts,r=.035,ma=STEEL,sides=8):tube(m,pts,r,ma,sides)
def timber(m,pos,size,ma=WOOD):box(m,pos,size,ma,.016)
def wheel(m,pos,r=.5,w=.3,axis='Y'):
 lathe(m,(0,0,0),[(-w/2,r),(w/2,r)],RUBBER,16,matrix=Matrix.Translation(Vector(pos))@Matrix.Rotation(PI/2,4,'X' if axis=='Y' else 'Y'))
 lathe(m,(0,0,0),[(-w/2-.01,r*.55),(w/2+.01,r*.55)],STEEL,12,matrix=Matrix.Translation(Vector(pos))@Matrix.Rotation(PI/2,4,'X' if axis=='Y' else 'Y'))
def lattice(m,x0,x1,y0,y1,z0,z1,step=3,leg=.14,brace=.075,ma=YELLOW):
 for x in (x0,x1):
  for y in (y0,y1):pipe(m,[(x,y,z0),(x,y,z1)],leg,ma)
 z=z0
 while z<z1-.01:
  zn=min(z+step,z1)
  for y in (y0,y1):
   pipe(m,[(x0,y,z),(x1,y,z)],brace*1.2,ma);pipe(m,[(x0,y,z),(x1,y,zn)],brace,ma)
  for x in (x0,x1):
   pipe(m,[(x,y0,z),(x,y1,z)],brace*1.2,ma);pipe(m,[(x,y0,z),(x,y1,zn)],brace,ma)
  z=zn
def roof_cap(m,w,d,h,units=True,antenna=0,tank=False):
 box(m,(0,0,h+.18),(w+.36,d+.36,.36),ROOF,.03)
 for s in (-1,1):
  box(m,(s*(w/2+.12),0,h+.6),(.14,d+.36,.5),EDGE,.01);box(m,(0,s*(d/2+.12),h+.6),(w+.36,.14,.5),EDGE,.01)
 if units:
  box(m,(-w*.18,d*.12,h+1.4),(w*.36,d*.3,2.2),EDGE,.05);box(m,(w*.22,-d*.2,h+.9),(w*.22,d*.22,1.2),ROOF,.04)
  for i in range(3):lathe(m,(w*.25-i*1.1,d*.25,h+.36),[(0,.4),(.9,.4),(1.0,.3)],STEEL,12)
 if tank:
  for i in range(4):pipe(m,[(-w*.2+(i%2)*2.2,-d*.2+(i//2)*2.2,h+.36),(-w*.2+(i%2)*2.2,-d*.2+(i//2)*2.2,h+2.6)],.08,IRON)
  lathe(m,(-w*.2+1.1,-d*.2+1.1,h+2.4),[(0,1.7),(2.6,1.7),(3.4,.02)],RUST,16)
 if antenna:
  pipe(m,[(0,0,h+.36),(0,0,h+antenna)],.09,IRON);pipe(m,[(0,0,h+antenna),(0,0,h+antenna*1.25)],.045,IRON)
  lathe(m,(0,0,h+antenna*1.25),[(0,.2),(.4,.2)],BEACON,10)
def tower(name,w,d,h,fm,setbacks=(),spire=0,antenna=0,tank=False,units=True):
 m=Mesh();box(m,(0,0,h/2),(w,d,h),fm,0);roof_cap(m,w,d,h,units and not setbacks,0,tank)
 cw,cd,cz=w,d,h
 for (frac,hh) in setbacks:
  cw,cd=cw*frac,cd*frac
  box(m,(0,0,cz+hh/2),(cw,cd,hh),fm,0);roof_cap(m,cw,cd,cz+hh,units,0,False);cz+=hh
 if spire:
  lathe(m,(0,0,cz+.36),[(0,cw*.28),(spire*.55,cw*.10),(spire,.02)],STEEL,10)
  lathe(m,(0,0,cz+spire*.98),[(0,.35),(.7,.35)],BEACON,10)
 if antenna:pipe(m,[(0,0,cz+.36),(0,0,cz+antenna)],.11,IRON);lathe(m,(0,0,cz+antenna),[(0,.3),(.6,.3)],BEACON,10)
 export(m,name)
if '--prototype' not in sys.argv:
 # ---------- City: near and far towers with lit facades ----------
 tower('CityGlassA',16,16,40,FGLASS,antenna=6)
 tower('CityGlassB',18,14,66,FGLASS,setbacks=[(.7,14)],antenna=9)
 tower('CityGlassC',20,20,96,FDARK,setbacks=[(.78,22),(.62,18)],spire=22)
 tower('CityBrickA',22,16,30,FBRICK,tank=True)
 tower('CityBrickB',18,18,48,FBRICK,setbacks=[(.72,10)],tank=True)
 tower('CityBandA',24,18,54,FBAND,units=True)
 tower('CityBandB',14,26,76,FBAND,setbacks=[(.8,16)],antenna=7)
 tower('CityDark',24,24,120,FDARK,setbacks=[(.8,30),(.6,20)],antenna=16)
 tower('CityLandmark',26,26,150,FGLASS,setbacks=[(.78,34),(.6,30),(.42,24)],spire=40)
 m=Mesh()
 for z in range(0,61,4):box(m,(0,0,z),(16,14,.5),EDGE,.04)
 for x in (-7,0,7):
  for y in (-6,0,6):box(m,(x,y,30),(.8,.8,60),STONE,.04)
 for y in (-7.02,7.02):box(m,(0,y,44),(16,.04,32),GREEN,.001)
 box(m,(8.02,0,20),(.04,14,40),GREEN,.001)
 for i in range(6):pipe(m,[(-8,-7+i*2.8,60.5),(-8,-7+i*2.8,63)],.04,RUST)
 export(m,'CityFrameB')
 m=Mesh();box(m,(0,0,17.5),(34,13,35),FBAND,0);roof_cap(m,34,13,35,False)
 for z in range(3,35,3):box(m,(0,-6.9,z),(33,.9,.14),EDGE,.01);box(m,(0,-7.3,z+.55),(33,.06,1.0),STEEL,.004)
 export(m,'CitySlab')
 # ---------- Street far below ----------
 m=Mesh();m.add([(-300,-300,0),(300,-300,0),(300,300,0),(-300,300,0)],[(0,1,2,3)],DIRT);export(m,'StreetGround')
 m=Mesh();box(m,(0,0,.05),(60,11,.1),ASPHALT,0)
 for s in (-1,1):box(m,(0,s*5.6,.12),(60,.5,.24),EDGE,.01)
 for i in range(10):box(m,(-27+i*6,0,.11),(2.4,.16,.02),PAINT,0)
 export(m,'RoadStrip')
 m=Mesh();box(m,(0,0,.05),(11,11,.1),ASPHALT,0);export(m,'RoadCross')
 m=Mesh()
 for x in (-1.75,1.75):pipe(m,[(x,0,0),(x,0,2.1)],.045,STEEL)
 box(m,(0,0,1.02),(3.5,.05,1.95),STEEL,.004)
 for i in range(7):box(m,(-1.5+i*.5,0,1.02),(.06,.07,1.9),STEEL,.002)
 export(m,'FencePanel')
 m=Mesh();box(m,(0,-1.9,1.35),(2.4,2.2,1.9),VWHITE,.06);box(m,(0,-2.7,1.6),(2.2,.06,1.0),GLASS,.01)
 box(m,(0,1.1,.8),(2.4,4.6,.5),IRON,.04);box(m,(0,1.1,1.85),(2.5,4.8,1.6),YELLOW,.06)
 for y in (-2.2,.4,1.8):
  for x in (-1.15,1.15):wheel(m,(x,y,.55),.55,.35)
 export(m,'DumpTruck')
 m=Mesh()
 for x in (-1.2,1.2):box(m,(x,0,.5),(.7,4,1),RUBBER,.08)
 box(m,(0,-.4,1.65),(2.6,3.2,1.3),YELLOW,.08);box(m,(-.7,.9,2.6),(1.2,1.4,1.3),GLASS,.05)
 pipe(m,[(.6,1.5,2.2),(.6,4.5,4.2),(.6,7.5,2.0)],.22,YELLOW,8);box(m,(.6,7.9,1.4),(1.4,1.2,1.0),IRON,.05)
 export(m,'Excavator')
 m=Mesh();box(m,(0,-2.6,1.35),(2.4,2.2,1.9),VWHITE,.06);box(m,(0,-3.4,1.6),(2.2,.06,1.0),GLASS,.01)
 box(m,(0,1.0,1.1),(2.4,6.6,1.1),RED,.05)
 pipe(m,[(0,-1,1.8),(0,4,1.8)],.18,RED,8);pipe(m,[(0,4,1.8),(0,-2,2.2)],.15,RED,8);pipe(m,[(0,-2,2.2),(0,4.2,2.6)],.13,RED,8)
 for y in (-2.9,.6,2.2,3.7):
  for x in (-1.15,1.15):wheel(m,(x,y,.55),.55,.35)
 export(m,'PumpTruck')
 m=Mesh();box(m,(0,0,.62),(1.8,4.4,.62),VWHITE,.09);box(m,(0,-.1,1.15),(1.6,2.3,.55),GLASS,.09)
 for y in (-1.45,1.45):
  for x in (-.85,.85):wheel(m,(x,y,.33),.33,.2)
 export(m,'Car')
 m=Mesh();box(m,(0,0,1.3),(2.44,6.06,2.59),BLUE,.06)
 for i in range(11):box(m,(1.23,-2.8+i*.56,1.3),(.03,.14,2.5),BLUE,.005);box(m,(-1.23,-2.8+i*.56,1.3),(.03,.14,2.5),BLUE,.005)
 export(m,'Container')
 m=Mesh();ellipsoid(m,(0,0,0),(4.5,3.5,2.2),DIRT,20,8);export(m,'DirtPile')
 m=Mesh();pipe(m,[(0,0,0),(0,0,4.5)],.28,TRUNK,10);ellipsoid(m,(0,0,6.2),(3.2,3.0,2.6),LEAF,14,8);ellipsoid(m,(1.2,.6,7.6),(2.0,1.9,1.7),LEAF,12,6);ellipsoid(m,(-1.1,-.7,5.4),(2.2,2.0,1.6),LEAF,12,6);export(m,'Tree')
 m=Mesh();pipe(m,[(0,0,0),(0,0,8),(0,1.8,8.6)],.09,IRON,8);box(m,(0,2.1,8.5),(.5,1.0,.22),LAMP,.03);export(m,'StreetLamp')
 # ---------- Hero tank ----------
 m=Mesh()
 for y in (-1.45,1.45):box(m,(0,y,.12),(3.3,.25,.24),IRON,.01)
 for x in (-1.45,1.45):box(m,(x,0,.12),(.25,3.15,.24),IRON,.01)
 for x in (-1.42,1.42):
  for y in (-1.42,1.42):pipe(m,[(x,y,.2),(x,y,3.1)],.055,STEEL)
 for z in (.3,1.55,2.75):ring(m,1.52,z,.03,STEEL,48)
 lathe(m,(0,0,.22),[(0,1.45),(.06,1.45)],IRON,40)
 lathe(m,(0,0,2.95),[(0,1.0),(.45,.42),(.8,.34),(.9,.34)],IRON,24)
 ring(m,1.02,2.95,.05,STEEL,32)
 pipe(m,[(1.45,0,.5),(2.1,0,.5),(2.1,0,.15)],.075,IRON);arc(m,(2.1,0,.62),.2,0,2*PI,.025,RED,20)
 for z in (.4,.7,1.0,1.3,1.6,1.9,2.2,2.5):
  box(m,(-1.42,-1.42,z),(.14,.14,.03),WHITE,0)
 box(m,(-1.42,-1.42,2.8),(.16,.16,.04),RED,0)
 for x in (-1.75,-1.45):pipe(m,[(x,-1.85,0),(x,-1.85,3.3)],.03,STEEL)
 for i in range(9):pipe(m,[(-1.75,-1.85,.35+i*.35),(-1.45,-1.85,.35+i*.35)],.024,STEEL)
 export(m,'TankFrame')
 m=Mesh();ring(m,1.55,2.75,.055,WHITE,48);box(m,(0,-1.62,.12),(3.3,.06,.24),WHITE,0);box(m,(0,1.62,.12),(3.3,.06,.24),WHITE,0)
 lathe(m,(0,0,3.0),[(0,.44),(.36,.44)],WHITE,24);export(m,'TankRing')
 m=Mesh();n=40;top=[];bot=[]
 for i in range(n):
  a=2*PI*i/n;bot.append((1.4*math.cos(a),1.4*math.sin(a),.26));top.append((1.4*math.cos(a),1.4*math.sin(a),2.9))
 m.add(bot+top,[(i,(i+1)%n,n+(i+1)%n,n+i) for i in range(n)],GLASS);export(m,'TankGlass')
 m=Mesh();lathe(m,(0,0,0),[(0,1.34),(2.4,1.34)],BLUE,40);export(m,'TankWater')
 # ---------- Flags, bunting, lamps ----------
 m=Mesh();pipe(m,[(0,0,0),(0,0,6)],.045,STEEL);lathe(m,(0,0,6),[(0,.09),(.16,.01)],WHITE,10);lathe(m,(0,0,.02),[(0,.22),(.06,.22)],IRON,12);export(m,'FlagMast')
 m=Mesh()
 for i in range(6):box(m,(.03+i*.30,0,-.5),(.30,.02,1.0-.06*i),WHITE,0)
 export(m,'Flag')
 for suffix,col in (('A',TEAMA),('B',TEAMB)):
  m=Mesh();pts=[(-3+i*.25,0,-.35*math.sin(PI*(i/24)))for i in range(25)];pipe(m,pts,.012,IRON,5)
  for i in range(12):
   x=-2.75+i*.5;z=-.35*math.sin(PI*((x+3)/6))
   m.add([(x-.2,0,z),(x+.2,0,z),(x,0,z-.42)],[(0,1,2)],col if i%2==0 else WHITE)
   m.add([(x-.2,-.004,z),(x,-.004,z-.42),(x+.2,-.004,z)],[(0,1,2)],col if i%2==0 else WHITE)
  export(m,'Bunting'+suffix)
 m=Mesh()
 for a in range(3):
  t=a*2*PI/3;pipe(m,[(0,0,.9),(.8*math.cos(t),.8*math.sin(t),0)],.03,STEEL)
 pipe(m,[(0,0,.8),(0,0,4.6)],.05,STEEL);pipe(m,[(-.7,0,4.4),(.7,0,4.4)],.035,STEEL)
 for x in (-.5,.5):box(m,(x,.0,4.55),(.5,.22,.36),IRON,.02);box(m,(x,.14,4.55),(.44,.02,.3),LAMP,.004)
 export(m,'FloodTower')
 # ---------- Hoist, chute, facade fillers ----------
 m=Mesh();lattice(m,-1,1,-1,1,0,5.4,1.8,.09,.05,YELLOW)
 for i in range(12):pipe(m,[(-.25,-1.05,.25+i*.45),(.25,-1.05,.25+i*.45)],.02,STEEL)
 export(m,'HoistMast')
 m=Mesh()
 for x in (-1.1,1.1):
  for y in (-1.25,1.25):pipe(m,[(x,y,0),(x,y,2.6)],.05,YELLOW)
 for z in (.05,1.3,2.55):
  pipe(m,[(-1.1,-1.25,z),(1.1,-1.25,z),(1.1,1.25,z),(-1.1,1.25,z),(-1.1,-1.25,z)],.04,YELLOW)
 box(m,(0,0,.06),(2.2,2.5,.06),PLY,.005);box(m,(0,0,2.62),(2.3,2.6,.05),STEEL,.005)
 for y in (-1.25,1.25):box(m,(0,y,1.3),(2.2,.012,2.4),GREEN,0)
 box(m,(1.1,0,1.3),(.012,2.5,2.4),GREEN,0)
 export(m,'HoistCage')
 m=Mesh()
 for i in range(5):lathe(m,(0,0,-i*1.05),[(0,.46),(-1.15,.34)],STEEL,14)
 export(m,'ChuteRun')
 m=Mesh();box(m,(0,0,1.35),(4,.012,2.7),GREEN,0)
 for x in (-1.98,1.98):pipe(m,[(x,0,0),(x,0,2.7)],.03,STEEL)
 for z in (.05,2.68):pipe(m,[(-2,0,z),(2,0,z)],.025,STEEL)
 export(m,'NetPanel')
 m=Mesh();pipe(m,[(0,0,0),(.12,.05,-1.4),(-.08,-.04,-2.9),(.05,.08,-4.2),(0,0,-5.4)],.02,RUBBER,6);export(m,'CableDrop')
 m=Mesh();box(m,(0,0,1.35),(2.4,.05,2.7),PLY,.006)
 for z in (.15,1.35,2.55):box(m,(0,-.06,z),(2.4,.07,.12),YELLOW,.008)
 for x in (-1.1,1.1):box(m,(x,-.06,1.35),(.12,.07,2.7),YELLOW,.008)
 for x in (-.8,.8):pipe(m,[(x,-.1,1.9),(x,-1.7,.05)],.03,YELLOW)
 export(m,'Formwork')
 m=Mesh()
 for x in (-.26,.26):
  for y in (-.26,.26):pipe(m,[(x,y,0),(x,y,2.4)],.016,RUST,6)
 for i in range(10):
  z=.15+i*.25
  for (a,b) in (((-.28,-.28),(.28,-.28)),((.28,-.28),(.28,.28)),((.28,.28),(-.28,.28)),((-.28,.28),(-.28,-.28))):pipe(m,[(a[0],a[1],z),(b[0],b[1],z)],.011,RUST,5)
 export(m,'RebarCage')
 m=Mesh()
 for i in range(12):
  a=i*PI/6;r=.09 if i<6 else .18
  pipe(m,[(-2,r*math.cos(a),.09+r*math.sin(a)*.6+.1),(2,r*math.cos(a),.09+r*math.sin(a)*.6+.1)],.016,RUST,6)
 export(m,'RebarBundle')
 m=Mesh();ellipsoid(m,(0,0,.1),(1.7,1.2,.85),TARP,16,8);ellipsoid(m,(.5,.3,.4),(.8,.6,.55),TARP,10,5);ellipsoid(m,(-.6,-.2,.3),(.7,.5,.5),TARP,10,5)
 for a in range(6):
  t=a*PI/3;pipe(m,[(1.5*math.cos(t),1.05*math.sin(t),.35),(1.9*math.cos(t),1.35*math.sin(t),.02)],.012,RUBBER,5)
 export(m,'Tarp')
 m=Mesh()
 for x in (-.45,.45):
  for y in (-.3,.3):pipe(m,[(x,y,0),(x,y,1.6)],.025,STEEL)
 for z in (.05,.8,1.58):pipe(m,[(-.45,-.3,z),(.45,-.3,z),(.45,.3,z),(-.45,.3,z),(-.45,-.3,z)],.02,STEEL)
 for i,(x,y) in enumerate(((-.25,-.13),(.25,-.13),(-.25,.13),(.25,.13))):
  lathe(m,(x,y,.06),[(0,.13),(1.15,.13),(1.3,.05),(1.4,.05)],RED if i%2 else STEEL,14)
 export(m,'GasCylinders')
 m=Mesh();box(m,(0,0,.2),(.8,.4,.4),RED,.02);arc(m,(0,0,.4),.18,0,PI,.015,IRON,10);box(m,(0,0,.41),(.82,.42,.03),IRON,.004);export(m,'Toolbox')
 m=Mesh();lathe(m,(0,0,0),[(0,.24),(.05,.29),(.85,.29),(.9,.24)],BLUE,20)
 for z in (.25,.6):ring(m,.30,z,.012,BLUE,20)
 export(m,'WaterBarrel')
 m=Mesh()
 for i,(x,y,z) in enumerate(((-.35,0,.16),(.35,0,.16),(0,.4,.16),(0,-.4,.16),(-.18,.2,.45),(.18,-.2,.45),(0,0,.72))):ellipsoid(m,(x,y,z),(.42,.28,.17),SACK,10,5,matrix=Matrix.Rotation(i*.5,4,'Z'))
 export(m,'Sandbags')
 m=Mesh();box(m,(0,0,1.15),(1.1,1.1,2.3),BLUE,.05);box(m,(0,0,2.34),(1.16,1.16,.1),WHITE,.03)
 box(m,(0,-.56,1.1),(.7,.02,1.9),WHITE,.005);box(m,(.25,-.58,1.1),(.05,.02,.2),IRON,.003);pipe(m,[(0,0,2.38),(0,0,2.7)],.05,IRON)
 export(m,'PortaPotty')
 m=Mesh();v=[(-1.6,-.8,.25),(1.6,-.8,.25),(1.6,.8,.25),(-1.6,.8,.25),(-1.85,-.95,1.45),(1.85,-.95,1.45),(1.85,.95,1.45),(-1.85,.95,1.45)]
 m.add(v,[(0,1,2,3),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],IRON)
 for a,b in ((4,5),(5,6),(6,7),(7,4)):pipe(m,[v[a],v[b]],.04,IRON)
 for x in (-1.5,1.5):box(m,(x,0,.12),(.2,1.5,.24),IRON,.01)
 export(m,'SkipBin')
 m=Mesh()
 for s in (-1,1):
  box(m,(0,s*.3,.6),(1.2,.05,1.1),PLY,.006,matrix=Matrix.Rotation(-s*.28,4,'X'))
  for i in range(6):box(m,(-.5+i*.2,s*.335,.6),(.1,.01,1.05),YELLOW if i%2==0 else IRON,0,matrix=Matrix.Rotation(-s*.28,4,'X'))
 export(m,'SignBoard')
 m=Mesh();timber(m,(0,0,.74),(1.8,.8,.06))
 for s in (-1,1):timber(m,(0,s*.75,.44),(1.8,.3,.05))
 for x in (-.7,.7):
  timber(m,(x,0,.36),(.08,.08,.72));timber(m,(x,0,.42),(.08,1.7,.06))
  for s in (-1,1):timber(m,(x,s*.75,.2),(.08,.08,.42))
 export(m,'Bench')
 m=Mesh();pipe(m,[(-2,0,.05),(-1,.6,.05),(.2,.3,.05),(1.2,.8,.05),(2.2,.4,.05)],.045,RUBBER,8);export(m,'Hose')
 m=Mesh();lathe(m,(0,0,0),[(-.6,.32),(.6,.32)],YELLOW,16,matrix=Matrix.Translation(Vector((0,0,.55)))@Matrix.Rotation(PI/2,4,'Y'))
 box(m,(0,0,.95),(.6,.5,.35),IRON,.03);pipe(m,[(-.4,0,.4),(-1.2,0,.9)],.025,STEEL)
 for y in (-.32,.32):wheel(m,(.35,y,.22),.22,.12)
 export(m,'Compressor')
 m=Mesh()
 for x in (-.16,.16):lathe(m,(x,0,.06),[(0,.14),(1.3,.14),(1.45,.05),(1.55,.05)],STEEL if x<0 else RED,14)
 box(m,(0,0,.04),(.7,.4,.06),IRON,.01)
 for y in (-.2,.2):wheel(m,(-.3,y,.2),.2,.08)
 pipe(m,[(.2,.15,1.0),(.9,.5,.3),(1.6,.2,.05),(2.2,.6,.05)],.014,RUBBER,5)
 export(m,'WeldCart')
 m=Mesh()
 for x in (-1.35,1.35):
  for y in (-.85,.85):pipe(m,[(x,y,0),(x,y,2.7)],.05,IRON)
 for z in (.05,2.65):pipe(m,[(-1.35,-.85,z),(1.35,-.85,z),(1.35,.85,z),(-1.35,.85,z),(-1.35,-.85,z)],.04,IRON)
 for i in range(5):
  x=-1.1+i*.55;lathe(m,(x,0,.1),[(0,.2),(2.3,.2),(2.45,.06)],RUST if i%2 else STEEL,12)
 export(m,'CylinderRack')
 # ---------- Crane in three moving parts ----------
 m=Mesh();lattice(m,-1.2,1.2,-1.2,1.2,0,60,3,.15,.08,YELLOW);box(m,(0,0,.3),(4,4,.6),EDGE,.05);export(m,'CraneMast')
 m=Mesh();box(m,(0,0,.3),(3.6,3.6,.6),YELLOW,.1)
 box(m,(2.0,-1.0,1.3),(1.7,1.9,2.2),YELLOW,.09);box(m,(2.05,-1.98,1.55),(1.3,.02,1.3),GLASS,.02)
 for y in (-.75,.75):
  for z in (1.4,3.0):pipe(m,[(-12,y,z),(44,y,z)],.09,YELLOW)
  for x in range(-12,44,2):pipe(m,[(x,y,1.4),(x+2,y,3.0)],.05,YELLOW);pipe(m,[(x,y,3.0),(x+2,y,1.4)],.05,YELLOW)
 for x in range(-12,45,2):pipe(m,[(x,-.75,1.4),(x,.75,1.4)],.055,YELLOW);pipe(m,[(x,-.75,3.0),(x,.75,3.0)],.05,YELLOW)
 for y in (-.7,.7):
  pipe(m,[(0,y,1.4),(0,y,9.5)],.08,YELLOW);pipe(m,[(-11,y,3.0),(0,y,9.5),(40,y,3.0)],.028,IRON)
 for x in (-10.4,-9.1,-7.8):box(m,(x,0,.4),(1.05,2.2,2.4),EDGE,.05)
 pipe(m,[(0,0,9.5),(0,0,11.5)],.05,IRON);lathe(m,(0,0,11.5),[(0,.28),(.55,.28)],BEACON,10)
 box(m,(19,0,1.1),(1.4,1.8,.5),RED,.05)
 export(m,'CraneTop')
 m=Mesh()
 for y in (-.14,.14):pipe(m,[(0,y,0),(0,y,-12)],.02,IRON,5)
 lathe(m,(0,0,-12.5),[(0,.22),(.5,.22),(.6,.12)],RED,14);arc(m,(0,0,-12.9),.28,PI/2,PI*1.85,.06,IRON,14)
 for y in (-.5,.5):pipe(m,[(0,0,-13.2),(0,y,-15.2)],.02,IRON,5)
 lathe(m,(0,0,-16.6),[(0,.45),(.4,.62),(1.4,.62),(1.5,.55)],STEEL,18)
 export(m,'CraneHook')
 # ---------- Birds and sky ----------
 m=Mesh();ellipsoid(m,(0,0,0),(.09,.28,.08),IRON,10,5);ellipsoid(m,(0,.3,.03),(.07,.09,.07),IRON,8,4);export(m,'BirdBody')
 m=Mesh();m.add([(0,-.08,0),(.55,-.05,0),(.6,.06,0),(.3,.1,0),(0,.1,0)],[(0,1,2,3,4)],IRON);m.add([(0,-.08,-.002),(0,.1,-.002),(.3,.1,-.002),(.6,.06,-.002),(.55,-.05,-.002)],[(0,1,2,3,4)],IRON);export(m,'BirdWing')
 data=bpy.data.meshes.new('CS_SkyDome');verts=[];faces=[];seg=48;rings=10
 for j in range(rings+1):
  a=(PI/2)*j/rings*1.08
  for i in range(seg):
   b=2*PI*i/seg;verts.append((math.cos(a)*math.cos(b),math.cos(a)*math.sin(b),math.sin(a)-.08))
 for j in range(rings):
  for i in range(seg):
   a0=j*seg+i;b0=j*seg+(i+1)%seg;faces.append((b0,a0,a0+seg,b0+seg))
 data.from_pydata(verts,[],faces);data.update();uv=data.uv_layers.new(name='TopUV')
 for poly in data.polygons:
  for li in poly.loop_indices:
   co=data.vertices[data.loops[li].vertex_index].co;uv.data[li].uv=(co.x*.5+.5,co.y*.5+.5)
 dome=bpy.data.objects.new('CS_SkyDome',data);scene.collection.objects.link(dome)
 dm=Mesh();dm.v=verts;dm.f=faces;dm.m=[CLOUD]*len(faces);export(dm,'SkyDome',dome)
 # ---------- Roadside / deck decal quads ----------
 # Decal quads span exactly one planar UV tile (1/.55 m) from the origin corner, so the arrow texture maps once.
 L=1/.55
 m=Mesh();m.add([(0,0,.006),(L,0,.006),(L,L,.006),(0,L,.006)],[(0,1,2,3)],PAINTA);export(m,'ArrowA')
 m=Mesh();m.add([(0,0,.006),(L,0,.006),(L,L,.006),(0,L,.006)],[(0,1,2,3)],PAINTB);export(m,'ArrowB')
 m=Mesh();m.add([(-1,-1,0),(1,-1,0),(1,1,0),(-1,1,0)],[(0,1,2,3)],HAZE);export(m,'HazeSheet')
# ---------- Textures ----------
rng=np.random.default_rng(537)
def fbm(sz,octaves=5,base=8,persistence=.55,seed=0):
 r=np.random.default_rng(seed);out=np.zeros((sz,sz),np.float32);amp=1;tot=0;freq=base
 for o in range(octaves):
  g=r.random((freq+1,freq+1)).astype(np.float32);g[-1,:]=g[0,:];g[:,-1]=g[:,0]
  ys=np.linspace(0,freq,sz,endpoint=False);xs=ys
  y0=np.floor(ys).astype(int);x0=np.floor(xs).astype(int);ty=(ys-y0)[:,None];tx=(xs-x0)[None,:]
  ty=ty*ty*(3-2*ty);tx=tx*tx*(3-2*tx)
  a=g[y0][:,x0];b=g[y0][:,x0+1];c=g[y0+1][:,x0];d=g[y0+1][:,x0+1]
  out+=amp*((a*(1-tx)+b*tx)*(1-ty)+(c*(1-tx)+d*tx)*ty);tot+=amp;amp*=persistence;freq*=2
 return out/tot
def save(name,a):
 img=bpy.data.images.new('CS_'+name,width=a.shape[1],height=a.shape[0],alpha=True);img.pixels.foreach_set(np.clip(a,0,1).astype(np.float32).ravel())
 img.filepath_raw=str(ART/'Textures'/('CS_'+name+'.png'));img.file_format='PNG';img.save()
def rgba(sz,v=1.0):
 a=np.ones((sz,sz,4),np.float32);a[:,:,:3]=v;return a
sz=1024;yy,xx=np.mgrid[0:sz,0:sz]
# Concrete: mottled cast surface, faint form lines, tie holes, streaks below them.
n=fbm(sz,6,6,.55,11);streak=fbm(sz,4,3,.6,12)
v=.84+.14*n+.05*(streak-.5)*np.clip(1-yy/sz*1.4,0,1)
v-=.06*((xx%512<3)|(yy%512<3));v-=.04*((xx%256<2))
for cy,cx in ((128,128),(128,384),(384,128),(384,384),(640,640),(640,896),(896,640),(896,896)):
 rr=np.sqrt((xx-cx)**2+(yy-cy)**2);v-=.22*np.clip(1-rr/9,0,1);v-=.05*np.clip(1-np.abs(xx-cx)/4,0,1)*np.clip((yy-cy)/60,0,1)*np.clip(1-(yy-cy)/60,0,1)
spots=fbm(sz,3,4,.5,13);v-=.10*np.clip((spots-.62)*6,0,1)
a=rgba(sz);a[:,:,:3]=v[:,:,None];save('Concrete',a)
# Wood: grain with wobble and a few knots, planks read at bridge distance.
g=fbm(sz,4,2,.5,21)
v=.86+.09*np.sin(xx*.35+g*14)+.03*np.sin(xx*1.7+g*3)+.02*fbm(sz,5,16,.5,22)
for cy,cx in ((200,300),(700,760),(500,120)):
 rr=np.sqrt(((xx-cx)/1.6)**2+(yy-cy)**2);v-=.28*np.clip(1-rr/22,0,1)+.12*np.clip(np.cos(rr*.9)*np.clip(1-rr/40,0,1),0,1)
v-=.05*(yy%256<3)
a=rgba(sz);a[:,:,:3]=v[:,:,None];save('Wood',a)
# Asphalt and ground for the street far below (seen through height haze).
v=.75+.35*fbm(sz,6,8,.55,31);a=rgba(sz);a[:,:,:3]=v[:,:,None];save('Asphalt',a)
v=.70+.4*fbm(sz,6,5,.6,32);a=rgba(sz);a[:,:,:3]=v[:,:,None];a[:,:,1]*=.96;save('Ground',a)
# Facades: base colour with mullions; emission map lights a random share of windows.
def facade(name,wins_x,wins_y,frame,glass,lit,seed,band=False):
 s=512;y,x=np.mgrid[0:s,0:s];r=np.random.default_rng(seed)
 base=np.ones((s,s,4),np.float32);base[:,:,:3]=frame
 em=np.zeros((s,s,4),np.float32);em[:,:,3]=1
 cw=s/wins_x;ch=s/wins_y
 for j in range(wins_y):
  for i in range(wins_x):
   x0=int(i*cw+cw*.18);x1=int(i*cw+cw*.82);y0=int(j*ch+ch*.22);y1=int(j*ch+ch*.88)
   if band:x0=int(i*cw+cw*.03);x1=int(i*cw+cw*.97)
   g=np.array(glass)*(0.85+.3*r.random());base[y0:y1,x0:x1,:3]=g
   if r.random()<lit:
    warm=r.random()<.7;c=(1,.82,.55) if warm else (.72,.86,1);k=.5+.5*r.random()
    em[y0:y1,x0:x1,:3]=np.array(c)*k
 save(name,base);save(name+'_E',em)
facade('FacadeGlass',2,1,(.55,.62,.72),(.35,.48,.66),.42,41)
facade('FacadeBrick',1,1,(.72,.56,.44),(.30,.36,.44),.38,42)
facade('FacadeBand',1,1,(.70,.70,.68),(.32,.42,.55),.34,43,band=True)
# Clouds: layered noise, soft threshold, wide clear gaps.
c=fbm(sz,6,4,.6,51);d=fbm(sz,4,9,.5,52)
alpha=np.clip((c-.50)*4.2,0,1)*np.clip((d-.30)*2.4,0,1)
a=rgba(sz);a[:,:,3]=alpha**1.3;a[:,:,:3]=1-.08*(1-alpha)[:,:,None];save('Cloud',a)
# Arrow decal: one bold arrow per tile, alpha-cut, pointing +X.
s=256;y,x=np.mgrid[0:s,0:s];a=np.ones((s,s,4),np.float32)
shaft=(x>40)&(x<150)&(np.abs(y-128)<28);head=(x>=140)&(x<226)&(np.abs(y-128)<(226-x)*.9)
a[:,:,3]=(shaft|head).astype(np.float32);save('Arrow',a)
models={e['name']:e for e in existing.get('models',[])};models.update({e['name']:e for e in exports})
(ART/'palette.json').write_text(json.dumps({'materials':list(palette.values()),'models':list(models.values())},indent=2))
bpy.data.libraries.write(str(SOURCE/'CarrySkyscraperCity.blend'),{scene},fake_user=True,compress=True)
print(json.dumps({'exported':[e['name'] for e in exports],'count':len(exports),'source':str(SOURCE/'CarrySkyscraperCity.blend')}))
