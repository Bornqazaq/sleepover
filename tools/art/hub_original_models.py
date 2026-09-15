"""Original Sleepover prop collection. Run through Blender MCP, never depends on store meshes.
Coordinates passed to helpers are Unity metres (X right, Y up, Z forward).
Exports evaluated geometry with explicit Unity coordinates and UVs for the editor importer.
"""
import bpy, math, random, json, gzip
from pathlib import Path
from mathutils import Vector
# Set HUB_PROJECT_ROOT to the checkout root in the Blender MCP call.
ROOT = Path(HUB_PROJECT_ROOT)
OUT = ROOT/'igruha/Assets/_Project/Art/Hub/Original/Models'
OUT.mkdir(parents=True, exist_ok=True)
PALETTE = {'Oak':'B3834E','Walnut':'785039','Cream':'DBCBAC','Linen':'BEBD9A',
'Sage':'4E7764','Terracotta':'B96142','Ochre':'C79648','Ink':'232F32','Brass':'AD894D',
'Paper':'E9D9B5','Red':'B64637','Blue':'427A91','Leaf':'466B3E','LeafLight':'73924D',
'Screen':'487D86','Glow':'FFCE89','White':'EFE7D3'}
COLL = bpy.data.collections.get('SleepoverOriginal')
if COLL:
    for o in list(COLL.all_objects): bpy.data.objects.remove(o, do_unlink=True)
    for child in list(COLL.children): bpy.data.collections.remove(child)
else:
    COLL=bpy.data.collections.new('SleepoverOriginal');bpy.context.scene.collection.children.link(COLL)
MATS={}
for name,h in PALETTE.items():
    m=bpy.data.materials.get('HO_'+name) or bpy.data.materials.new('HO_'+name)
    c=tuple(int(h[i:i+2],16)/255 for i in (0,2,4));m.diffuse_color=(*c,1);MATS[name]=m
ASSETS={};current=None

def start(name):
    global current
    current=name;ASSETS[name]=[]

def own(o,mat):
    for c in list(o.users_collection):c.objects.unlink(o)
    COLL.objects.link(o);o.name=current+'_'+mat;o.data.materials.append(MATS[mat]);ASSETS[current].append(o);return o

def p(v): return (v[0],-v[2],v[1])
def box(pos,size,mat,bevel=.02,rot=(0,0,0)):
    bpy.ops.mesh.primitive_cube_add(size=1,location=p(pos));o=bpy.context.object;o.dimensions=(size[0],size[2],size[1]);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.rotation_euler=tuple(math.radians(a) for a in (rot[0],-rot[2],rot[1]))
    if bevel:
        mod=o.modifiers.new('Soft crafted edges','BEVEL');mod.width=min(bevel,min(size)*.45);mod.segments=3
        o.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL')
    return own(o,mat)
def sphere(pos,size,mat):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20,ring_count=12,location=p(pos));o=bpy.context.object;o.scale=(size[0],size[2],size[1]);
    for poly in o.data.polygons:poly.use_smooth=True
    return own(o,mat)
def cylinder(pos,r,h,mat,r2=None,rot=(0,0,0),verts=32):
    bpy.ops.mesh.primitive_cone_add(vertices=verts,radius1=r,radius2=r if r2 is None else r2,depth=h,location=p(pos));o=bpy.context.object
    o.rotation_euler=tuple(math.radians(a) for a in (rot[0],-rot[2],rot[1]));
    mod=o.modifiers.new('Rounded rim','BEVEL');mod.width=min(.008,h*.2);mod.segments=2;o.modifiers.new('Normals','WEIGHTED_NORMAL');return own(o,mat)
def line(a,b,r,mat):
    d=Vector(p(b))-Vector(p(a));o=cylinder(tuple((a[i]+b[i])/2 for i in range(3)),r,d.length,mat,verts=12);o.rotation_euler=d.to_track_quat('Z','Y').to_euler();return o

def lathe(pos,profile,mat):
    vs=[];fs=[];n=48
    for y,r in profile:
        vs.extend([p((pos[0]+r*math.cos(i*math.tau/n),pos[1]+y,pos[2]+r*math.sin(i*math.tau/n))) for i in range(n)])
    for j in range(len(profile)-1):
        for i in range(n):a=j*n+i;b=j*n+(i+1)%n;fs.append((a,b,b+n,a+n))
    me=bpy.data.meshes.new(current);me.from_pydata(vs,[],fs);me.update();o=bpy.data.objects.new(current,me);COLL.objects.link(o)
    for poly in me.polygons:poly.use_smooth=True
    me.materials.append(MATS[mat]);ASSETS[current].append(o);return o

def torus(pos,r,t,mat,rot=(0,0,0)):
    bpy.ops.mesh.primitive_torus_add(major_segments=40,minor_segments=8,location=p(pos),major_radius=r,minor_radius=t);o=bpy.context.object;o.rotation_euler=tuple(math.radians(a) for a in (rot[0],-rot[2],rot[1]));return own(o,mat)

def legs(w,d,h,mat='Walnut'):
    for x in [-w/2,w/2]:
        for z in [-d/2,d/2]:box((x,h/2,z),(.09,h,.09),mat,.018)

def sofa(name,w,mat):
    start(name);legs(w-.35,.64,.24)
    box((0,.32,0),(w,.30,.92),mat,.10)
    box((0,.83,-.34),(w-.10,.78,.24),mat,.105,rot=(7,0,0))
    for x in [-w/2+.13,w/2-.13]:box((x,.62,0),(.28,.67,.95),mat,.125)
    count=3 if w>2 else 1;sw=(w-.59)/count
    for i in range(count):
        x=(i-(count-1)/2)*sw
        box((x,.51,.06),(sw-.018,.22,.70),mat,.085)
        box((x,.89,-.22),(sw-.025,.53,.20),mat,.08,rot=(9,0,0))
        box((x,.535,.412),(sw-.045,.017,.014),'Cream',.006)
    for x in [-w*.31,w*.31] if w>2 else [w*.20]:
        box((x,.78,-.025),(.40,.43,.14),'Ochre' if x<0 else 'Terracotta',.095,rot=(10,0,-12 if x<0 else 13))
    # woven throw draped across one arm, with embroidered end lines
    x=-w/2+.15
    box((x,.975,.10),(.31,.025,.54),'Cream',.015)
    box((x-.155,.68,.10),(.025,.58,.54),'Cream',.015)
    for z in [-.09,.04,.17,.30]:box((x-.17,.47,z),(.01,.12,.022),'Sage',.005)

sofa('Sofa',2.8,'Linen');sofa('Armchair',1.18,'Sage')
start('Beanbag');sphere((0,.31,0),(.58,.32,.53),'Terracotta');sphere((0,.50,-.22),(.48,.40,.31),'Terracotta');torus((0,.07,0),.42,.013,'Cream')
start('CoffeeTable');legs(1.08,.59,.43);box((0,.49,0),(1.48,.14,.88),'Oak',.15);box((0,.18,0),(1.15,.05,.61),'Walnut',.035)
start('ConsoleTable');legs(2.38,.43,.14);box((0,.32,0),(2.78,.42,.61),'Walnut',.035);box((0,.555,0),(2.88,.065,.66),'Oak',.025)
for x in [-.96,.96]:
    box((x,.34,.315),(.69,.31,.027),'Oak',.01)
    for j in range(8):box((x-.29+j*.081,.34,.335),(.035,.27,.018),'Walnut',.005)
box((0,.32,.316),(1.03,.27,.035),'Ink',.01)
for x in [-.37,-.23,-.08,.13,.31]:box((x,.27,.34),(.10,.17,.13),'Sage' if x<0 else 'Terracotta',.005)
start('Television');box((0,1,0),(3.38,1.90,.18),'Ink',.065);box((0,1,.097),(3.18,1.72,.014),'Screen',.02)
for x in [-1.06,1.06]:line((x,.16,0),(x-.12,0,.25),.027,'Ink');line((x,.16,0),(x+.12,0,-.18),.027,'Ink')
sphere((1.53,.10,.10),(.016,.01,.006),'Glow')
start('Speaker');box((0,.58,0),(.34,1.16,.32),'Walnut',.04);box((0,.59,.171),(.30,1.08,.018),'Ink',.025)
for y,r in [(.30,.112),(.70,.095),(.99,.04)]:cylinder((0,y,.19),r,.02,'Ink',rot=(90,0,0));torus((0,y,.21),r,.008,'Brass',rot=(90,0,0))
start('Controller');box((0,.06,0),(.31,.095,.15),'Ink',.045)
for x in [-.12,.12]:sphere((x,.04,.067),(.055,.052,.087),'Ink');cylinder((x*.55,.121,-.015),.025,.023,'Walnut')
for x,z in [(.108,-.035),(.14,-.013),(.108,.01),(.076,-.013)]:sphere((x,.118,z),(.01,.009,.01),'Terracotta')
start('SnackTray');box((0,.024,0),(.60,.045,.39),'Oak',.045);cylinder((-.12,.075,0),.12,.08,'Cream',r2=.15)
rng=random.Random(9)
for i in range(38):
    a=rng.random()*math.tau;r=math.sqrt(rng.random())*.125;sphere((-.12+r*math.cos(a),.12+rng.random()*.04,r*math.sin(a)),(.022,.019,.022),'Ochre' if i%7==0 else 'Paper')
for x in [.16,.26]:cylinder((x,.10,-.065),.035,.16,'Terracotta');cylinder((x,.184,-.065),.033,.006,'Brass')
start('BarCounter');box((0,.54,0),(1.08,1.08,4.05),'Walnut',.045);box((0,1.115,0),(1.27,.12,4.22),'Oak',.055)
for z in [-1.82+i*.26 for i in range(15)]:box((.55,.55,z),(.035,.87,.21),'Oak',.01)
line((.77,.28,-1.93),(.77,.28,1.93),.024,'Brass')
for z in [-1.65,0,1.65]:line((.52,.28,z),(.77,.28,z),.022,'Brass')
start('Stool');cylinder((0,.79,0),.30,.14,'Terracotta');cylinder((0,.70,0),.26,.035,'Walnut');legs(.36,.36,.69);torus((0,.26,0),.255,.019,'Brass')
start('Fridge');box((0,1.05,0),(1.04,2.10,.78),'Sage',.065);box((0,1.05,.401),(.91,1.82,.03),'Ink',.035);box((0,1.06,.425),(.78,1.61,.021),'Screen',.015)
for y in [.39,.75,1.10,1.45]:
    box((0,y,.48),(.84,.027,.09),'Cream',.01)
    for i in range(6):cylinder((-.32+i*.127,y+.13,.455),.04,.22,['Terracotta','Ochre','Sage'][i%3]);cylinder((-.32+i*.127,y+.246,.455),.035,.008,'Brass')
box((0,1.92,.425),(.82,.19,.025),'Glow',.025);line((.455,.74,.445),(.455,1.25,.445),.025,'Brass')
start('Shelf');box((0,1.10,-.23),(2.40,2.20,.09),'Walnut',.012)
for x in [-1.17,1.17]:box((x,1.10,0),(.09,2.20,.55),'Oak',.015)
for y in [.12,.65,1.18,1.71,2.18]:box((0,y,0),(2.4,.085,.58),'Oak',.016)
for row,y in enumerate([.21,.74,1.27,1.80]):
    for j in range(7):
        x=-1.02+j*.30;h=.22+rng.random()*.17
        box((x,y+h/2,0),(.24,h,.38),['Sage','Terracotta','Ochre','Blue','Cream'][(row+j)%5],.012)
        box((x,y+h*.65,.20),(.18,.025,.008),'Paper',.003)
start('PingPong');legs(2.05,1.11,.76,'Ink');box((0,.82,0),(2.74,.10,1.525),'Sage',.025)
for z in [-.72,.72]:box((0,.878,z),(2.65,.007,.026),'Paper',.002)
for x in [-1.32,1.32]:box((x,.878,0),(.025,.007,1.46),'Paper',.002)
box((0,.878,0),(2.65,.007,.02),'Paper',.002)
for z in [-.85,.85]:line((0,.83,z),(0,1.055,z),.016,'Ink')
for y in [.89+i*.027 for i in range(7)]:line((0,y,-.85),(0,y,.85),.003,'Cream')
for z in [-.85+i*.055 for i in range(32)]:line((0,.89,z),(0,1.05,z),.003,'Cream')
for x,z in [(-.83,.36),(.83,-.39)]:
    sphere((x,.9,z),(.11,.018,.14),'Red');box((x+.13,.898,z),(.17,.025,.042),'Oak',.02)
sphere((-.54,.907,.38),(.021,.021,.021),'Paper')
start('Billiards');legs(1.02,2.0,.67);box((0,.72,0),(1.62,.29,2.92),'Walnut',.08);box((0,.897,0),(1.44,.09,2.74),'Oak',.035);box((0,.95,0),(1.23,.02,2.51),'Sage',.012)
for x in [-.59,.59]:
    for z in [-1.22,0,1.22]:cylinder((x,.968,z),.08,.014,'Ink')
for x,z in [(.12,.45),(.19,.52),(.05,.52),(.26,.59),(.12,.59),(-.02,.59),(-.3,-.75)]:sphere((x,.99,z),(.035,.035,.035),['Ochre','Red','Blue','Paper'][int(abs(z)*50)%4])
line((-.63,1.01,-1.21),(-.50,1.01,.6),.008,'Oak')
start('Foosball');legs(.88,1.76,.76,'Ink');box((0,.91,0),(1.16,.31,2.10),'Oak',.065);box((0,1.075,0),(.96,.035,1.87),'Sage',.014)
for z in [-.73,-.42,-.10,.2,.52,.80]:
    line((-.81,1.20,z),(.83,1.20,z),.014,'Brass');cylinder((.88,1.20,z),.027,.16,'Ink',rot=(0,0,90))
    for x in [-.3,0,.3]:box((x,1.18,z),(.085,.21,.07),'Terracotta' if z<0 else 'Cream',.025);sphere((x,1.32,z),(.043,.043,.043),'Walnut')
start('BowlingLane');box((0,.105,0),(1.70,.21,5.80),'Walnut',.05);box((0,.225,0),(1.30,.065,5.67),'Oak',.02)
for x in [-.76,.76]:box((x,.26,0),(.13,.10,5.76),'Walnut',.055)
for x in [-.51,-.26,0,.26,.51]:box((x,.263,0),(.008,.006,5.55),'Cream',.001)
box((0,.67,2.72),(1.65,.85,.22),'Walnut',.07)
for row in range(3):
    for i in range(row+1):
        x=(i-row/2)*.24;z=1.64+row*.25
        lathe((x,.26,z),[(0,.055),(.035,.075),(.13,.08),(.22,.047),(.30,.029),(.37,.05),(.42,.036),(.445,0)],'Cream');cylinder((x,.573,z),.036,.026,'Red')
sphere((.17,.42,-1.8),(.155,.155,.155),'Blue')
start('Arcade');box((0,.58,0),(.88,1.16,.76),'Sage',.055);box((0,1.54,-.15),(.89,.88,.52),'Sage',.055);box((0,1.95,-.02),(.98,.21,.69),'Walnut',.03)
box((0,1.50,.123),(.74,.58,.035),'Ink',.025,rot=(-9,0,0));box((0,1.5,.148),(.62,.45,.012),'Screen',.02,rot=(-9,0,0));box((0,1.95,.337),(.84,.14,.014),'Ochre',.01)
box((0,1.03,.26),(.96,.13,.48),'Walnut',.045);line((-.22,1.09,.26),(-.22,1.21,.26),.012,'Brass');sphere((-.22,1.22,.26),(.038,.038,.038),'Red')
for x,z in [(.15,.2),(.26,.2),(.20,.31),(.31,.31)]:cylinder((x,1.104,z),.026,.015,'Ochre' if x<.25 else 'Cream')
box((0,.72,.39),(.25,.13,.013),'Ink',.01)
# an original graphic pattern visible on the actual screen
for j in range(6):box((-.21+j*.085,1.40+(.04 if j%2 else 0),.178),(.052,.035,.01),'Glow',.003)
start('Plant');lathe((0,0,0),[(0,.20),(.04,.23),(.43,.29),(.45,.30),(.46,.26)],'Terracotta');cylinder((0,.425,0),.255,.012,'Walnut')
for i in range(13):
    a=i*2.4;y=.60+(i%5)*.18;r=.30+(i%3)*.10;x=math.cos(a)*r;z=math.sin(a)*r
    line((0,.41,0),(x*.7,y,z*.7),.012,'Leaf');o=sphere((x,y,z),(.14,.045,.31),'LeafLight' if i%3==0 else 'Leaf');o.rotation_euler[2]=a;o.rotation_euler[0]=.45
start('Pendant');lathe((0,0,0),[(0,.47),(.025,.49),(.10,.46),(.23,.36),(.36,.19),(.39,.06)],'Ochre');lathe((0,.008,0),[(0,.455),(.03,.451),(.16,.36),(.29,.18)],'Cream');sphere((0,.052,0),(.15,.085,.15),'Glow');cylinder((0,.53,0),.015,.28,'Ink');cylinder((0,.688,0),.105,.025,'Walnut')
start('Crate');box((0,.26,0),(.61,.51,.49),'Walnut',.014)
for y in [.075,.245,.415]:
    for z in [-.254,.254]:box((0,y,z),(.65,.13,.034),'Oak',.01)
for x in [-.28,.28]:box((x,.26,.28),(.09,.55,.035),'Oak',.012)
start('RecordPlayer');box((0,.07,0),(.61,.14,.42),'Walnut',.035);cylinder((-.08,.15,0),.16,.018,'Ink');cylinder((-.08,.163,0),.035,.009,'Terracotta');line((.22,.18,-.14),(.19,.18,.07),.008,'Brass');line((.19,.18,.07),(.04,.18,.12),.008,'Brass')
# Crown chair is purpose-built, with shaped uprights and button detail.
sofa('CrownChair',1.34,'Terracotta')
box((0,1.24,-.31),(1.02,.72,.21),'Terracotta',.12)
for x in [-.59,.59]:cylinder((x,.91,-.36),.045,1.66,'Oak');sphere((x,1.76,-.36),(.07,.07,.07),'Brass')
for x in [-.27,0,.27]:
    for y in [1.13,1.39]:sphere((x,y,-.185),(.017,.017,.012),'Brass')
lathe((0,1.65,-.31),[(0,.23),(.07,.23),(.09,.20)],'Brass')
for i in range(5):a=i*math.tau/5;box((math.cos(a)*.21,1.79,-.31+math.sin(a)*.21),(.06,.20,.055),'Brass',.01);sphere((math.cos(a)*.21,1.895,-.31+math.sin(a)*.21),(.036,.036,.036),'Brass')

def export():
    deps=bpy.context.evaluated_depsgraph_get();models=[]
    for name,objects in ASSETS.items():
        parts=[]
        for o in objects:
            ob=o.evaluated_get(deps);me=ob.to_mesh();me.calc_loop_triangles();normalmat=ob.matrix_world.to_3x3().inverted().transposed();verts=[];norms=[];uv=[];idx=[]
            uvdata=me.uv_layers.active.data if me.uv_layers.active else None
            for t in me.loop_triangles:
                for li in t.loops:
                    loop=me.loops[li];v=ob.matrix_world @ me.vertices[loop.vertex_index].co
                    n=(normalmat @ me.corner_normals[li].vector).normalized()
                    verts.extend([round(v.x,6),round(v.z,6),round(-v.y,6)]);norms.extend([round(n.x,6),round(n.z,6),round(-n.y,6)])
                    u=uvdata[li].uv if uvdata else (v.x,v.z);uv.extend([round(u[0],6),round(u[1],6)]);idx.append(len(idx))
            parts.append({'material':o.data.materials[0].name,'vertices':verts,'normals':norms,'uv':uv,'triangles':idx});ob.to_mesh_clear()
        models.append({'name':name,'parts':parts})
    (OUT/'OriginalModels.json.gz').write_bytes(gzip.compress(json.dumps({'models':models},separators=(',',':')).encode(),mtime=0))
    # Present editable sources in separate named collections on a grid, after exporting local coordinates.
    for i,(name,objects) in enumerate(ASSETS.items()):
        sub=bpy.data.collections.new(name);COLL.children.link(sub)
        origin=bpy.data.objects.new(name+'_Origin',None);sub.objects.link(origin)
        for o in objects:
            for c in list(o.users_collection): c.objects.unlink(o)
            sub.objects.link(o);o.parent=origin
        origin.location=((i%5)*4.2,(i//5)*7.0,0)
    source=ROOT/'tools/art/source';source.mkdir(exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(source/'HubOriginal.blend'),copy=True)
    return [(m['name'],sum(len(p['triangles'])//3 for p in m['parts'])) for m in models]
print(export())
