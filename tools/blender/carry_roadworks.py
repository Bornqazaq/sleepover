"""CarryItem roadworks kit, metres, Blender Z up. Run in live Blender via MCP.
Own meshes and deterministic concrete texture; other open scenes remain untouched.
"""
import bpy, bmesh, math, ast, json
import numpy as np
from pathlib import Path
from mathutils import Vector, Matrix
ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'igruha/Assets/_Project/Art/CarryItem/Roadworks'
SOURCE=ROOT/'igruha/Assets/_Project/Art/Source/CarrySkyscraper'
for p in (ART/'Models',ART/'Textures',SOURCE):p.mkdir(parents=True,exist_ok=True)
scene=bpy.data.scenes.new('CarryRoadworks_Build');bpy.context.window.scene=scene
for old in list(bpy.data.scenes):
 if old != scene and old.name.startswith('CarryRoadworks_'):
  for ob in list(old.objects):bpy.data.objects.remove(ob,do_unlink=True)
  bpy.data.scenes.remove(old)
scene.name='CarryRoadworks_695'
materials=[];palette=[];_box_cache={};STONE=0;PI=math.pi
for n in ast.parse((ROOT/'tools/blender/crying_angels.py').read_text()).body:
    if isinstance(n,(ast.ClassDef,ast.FunctionDef)) and n.name in {'Mesh','transform','box','lathe','tube'}:
        exec(compile(ast.Module(body=[n],type_ignores=[]),'geometry','exec'))
exec(compile((ROOT/'tools/blender/carry_geometry.py').read_text(), 'carry_geometry', 'exec'))
def mat(n,c,r=.7,m=0):
    name='RW_'+n; material=bpy.data.materials.get(name) or bpy.data.materials.new(name);material.diffuse_color=(*c,1);material.use_nodes=True
    bs=next(n for n in material.node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Base Color'].default_value=(*c,1);bs.inputs['Roughness'].default_value=r;bs.inputs['Metallic'].default_value=m
    materials.append(material);palette.append(dict(name=name,color=[*c,1],roughness=r,metallic=m));return len(materials)-1
STEEL=mat('Steel',(.20,.26,.27),.48,.6);EDGE=mat('Edges',(.40,.45,.44),.35,.65)
YELLOW=mat('Ochre',(.94,.56,.13),.65);DARK=mat('Charcoal',(.045,.066,.072),.8)
IVORY=mat('Ivory',(.80,.81,.70),.8);WOOD=mat('Timber',(.34,.19,.08),.83)
ORANGE=mat('Brick',(.55,.24,.115),.91);TEAL=mat('Teal',(.075,.36,.34),.55,.2)
CONCRETE=mat('Concrete',(.44,.43,.38),.95);RUST=mat('Rust',(.32,.12,.058),.94)
def export(m,n):
    o=m.object('RW_'+n);used=sorted(set(m.m));o.data.materials.clear()
    for i in used:o.data.materials.append(materials[i])
    for p,i in zip(o.data.polygons,m.m):p.material_index=used.index(i)
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/('RW_'+n+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
    o.location=(len(scene.objects)%4*3,len(scene.objects)//4*3,0);return o
# Bolted modular speed cushion: a shallow rounded rubber profile, flush ends,
# moulded yellow grip panels and recessed fasteners. Height remains 6 cm.
m=Mesh()
def cushion(x0,x1,y0,y1,material,lift=0):
    segments=20;v=[]
    for y in (y0,y1):
        for i in range(segments+1):
            x=x0+(x1-x0)*i/segments
            z=.006+.054*max(0,math.cos(x/.24*PI/2))**1.3+lift
            v.append((x,y,z))
    faces=[]
    for i in range(segments):faces.append((i,i+1,segments+2+i,segments+1+i))
    m.add(v,faces,material)
for k in range(9):
    y=-2.7+k*.6
    cushion(-.24,.24,y+.008,y+.592,DARK)
    if k%2==0:
        cushion(-.185,.185,y+.085,y+.515,YELLOW,.0015)
    for yy in (y+.05,y+.55):
        for x in (-.18,.18):
            z=.006+.054*math.cos(x/.24*PI/2)**1.3
            lathe(m,(x,yy,z),[(0,.026),(.002,.026)],DARK,12)
            lathe(m,(x,yy,z+.001),[(0,.013),(.004,.013)],EDGE,6)
    for yy in (y+.18,y+.3,y+.42):
        # Fine raised tread follows the curved crown.
        tube(m,[(x,yy,.009+.054*math.cos(x/.24*PI/2)**1.3) for x in (-.15,-.1,0,.1,.15)],.0025,DARK,6)
# End noses slope into the floor; no square floating ends.
for end in (-1,1):
    m.add([(-.24,end*2.7,.006),(0,end*2.7,.06),(.24,end*2.7,.006),
           (-.16,end*2.77,.002),(0,end*2.8,.002),(.16,end*2.77,.002)],
          [(0,1,4,3),(1,2,5,4)],DARK)
m.v=[(x,y*5.4/5.6,z) for x,y,z in m.v]
export(m,'Joint')
# Patched plate, worn corners, weld marks, rivets. Main deck remains smooth.
m=Mesh();box(m,(0,0,.012),(1.20,2.64,.024),STEEL,.038)
for y in np.arange(-1.1,1.2,.32):
    for x in (-.49,.49):box(m,(x,float(y),.027),(.075,.025,.003),EDGE,.002)
for y in (-1.19,1.19):
    for x in (-.46,.46):lathe(m,(x,y,.027),[(0,.026),(.008,.026)],DARK,8)
export(m,'RepairPlate')
# Continuous concrete curb with ochre end caps, lifting eyes and weathered stripe.
m=Mesh();box(m,(0,0,.33),(2.6,.42,.66),CONCRETE,.075)
for x in (-1.12,1.12):box(m,(x,0,.35),(.22,.46,.68),YELLOW,.045)
for x in np.arange(-.85,.9,.33):box(m,(float(x),-.221,.42),(.12,.015,.12),DARK,.005)
for x in (-.72,.72):tube(m,[(x-.075,0,.64),(x-.075,0,.75),(x+.075,0,.75),(x+.075,0,.64)],.018,EDGE,8)
export(m,'Curb')
# Footplate drain gratings and debris strips distinguish service edges from road.
m=Mesh();box(m,(0,0,.014),(.40,2.0,.028),DARK,.018)
for y in np.arange(-.92,.93,.09):box(m,(0,float(y),.033),(.35,.032,.018),EDGE,.006)
export(m,'Drain')
m=Mesh()
for x in (-.43,.43):box(m,(x,0,.10),(.12,1.55,.2),WOOD,.015)
for y in (-.61,0,.61):box(m,(0,y,.21),(1.30,.22,.10),WOOD,.015)
for row in range(4):
 for y in range(4):
  for x in range(4):box(m,((x-1.5)*.29,(y-1.5)*.32,.32+row*.14),(.27,.30,.125),ORANGE,.012)
for x in (-.39,.39):box(m,(x,0,.89),(.038,1.29,.01),DARK,.003)
export(m,'BrickPallet')
m=Mesh()
for y in (-.72,.72):
 box(m,(0,y,.12),(1.4,.17,.24),WOOD,.012)
 for x in (-.61,.61):box(m,(x,y,.37),(.065,.12,.68),TEAL,.016)
for y in range(5):
 for z in range(2):
  x=(y-2)*.23+(z*.1);hh=.32+z*.22
  tube(m,[(x,-1.15,hh),(x,1.15,hh)],.092,EDGE,12)
  # Dark bore on both visible ends.
  for end in (-1.151,1.151):tube(m,[(x,end-.001,hh),(x,end+.001,hh)],.068,DARK,12)
export(m,'PipeCradle')
m=Mesh();box(m,(0,0,.055),(1.5,.95,.11),TEAL,.045)
for x in (-.65,.65):
 for y in (-.34,.34):box(m,(x,y,.52),(.07,.07,.96),EDGE,.02)
box(m,(0,0,.97),(1.58,1.04,.10),WOOD,.022)
for x in (-.34,.38):box(m,(x,0,1.085),(.38,.27,.13),YELLOW,.025)
for x in np.arange(-.5,.55,.18):tube(m,[(float(x),.2,1.027),(float(x),.40,1.027)],.012,DARK,8)
export(m,'ToolBench')
# Two roadside pictogram signs, double sided on a weighted stand.
for smooth in (False,True):
 m=Mesh();box(m,(0,0,.055),(.66,.66,.11),DARK,.04);tube(m,[(0,0,.08),(0,0,1.48)],.035,EDGE,12)
 box(m,(0,0,1.44),(1.12,.075,.92),TEAL if smooth else YELLOW,.065)
 for face in (-1,1):
  y=face*.044
  # Wavy / level tyre tracks, a symbolic route rather than text signage.
  for z in (1.33,1.58):
   pts=[(-.41+i*.041,y,z+(0 if smooth else math.sin(i*.63)*.075)) for i in range(21)]
   tube(m,pts,.017,IVORY if smooth else DARK,8)
 export(m,'SmoothSign' if smooth else 'RoughSign')
# Small debris islands only on storage margins; contiguous low mesh, not loose physics.
m=Mesh();rng=np.random.default_rng(695)
for i in range(30):
 x,y=rng.uniform(-.85,.85),rng.uniform(-.5,.5);sx,sy=rng.uniform(.06,.21,2)
 box(m,(float(x),float(y),.026),(float(sx),float(sy),.052),CONCRETE if i%3 else ORANGE,.015)
export(m,'Debris')
# Seamless neutral concrete: broad cloudy colour, fine aggregate, no checkerboard.
N=1024;u,v=np.meshgrid(np.arange(N)/N,np.arange(N)/N);noise=np.zeros((N,N))
for f,a in ((1,.004),(3,.003),(7,.002),(19,.0015)):
 for k in range(4):noise+=a*np.sin(2*PI*(int(rng.integers(1,f+1))*u+int(rng.integers(1,f+1))*v)+rng.random()*PI)
noise+=rng.normal(0,.005,(N,N));rgba=np.ones((N,N,4),dtype=np.float32)
for i,b in enumerate((.50,.49,.445)):rgba[:,:,i]=np.clip(b+noise,0,1)
im=bpy.data.images.new('RW_Concrete',width=N,height=N,alpha=True);im.pixels.foreach_set(rgba.ravel());im.filepath_raw=str(ART/'Textures/RW_Concrete.png');im.file_format='PNG';im.save()
(ART/'palette.json').write_text(json.dumps({'materials':palette},indent=2)+'\n')
bpy.data.libraries.write(str(SOURCE/'CarryRoadworks.blend'),{scene},fake_user=True)
for area in bpy.context.screen.areas:
 if area.type=='VIEW_3D':
  area.spaces.active.region_3d.view_distance=18;area.spaces.active.region_3d.view_location=(4,3,0)
print('Roadworks: '+str(len(scene.objects))+' models, '+str(sum(len(o.data.polygons) for o in scene.objects))+' faces')
