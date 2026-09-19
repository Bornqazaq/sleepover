"""Duck Hunt final midway modules. Execute in Blender through MCP.
Authored geometry; source outside Assets. Coordinates: X right, -Y front, Z up.
"""
from pathlib import Path
base=Path(__file__).with_name('duck_hunt_carnival.py').read_text(encoding='utf-8').split("toy('Bear');export")[0]
base=base.replace('DH_Carnival_Kit','DH_Fairground_Kit').replace('DuckHuntCarnival','DuckHuntFairground').replace("startswith('DHC_')","startswith('DHF_')")
exec(compile(base,__file__,'exec'))
# Join through mesh data: live Blender operator context may point at another scene.
def export(name):
    global parts
    bpy.context.view_layer.update()
    vertices=[];faces=[];material_indices=[];materials=[]
    for part in parts:
        offset=len(vertices)
        vertices.extend(tuple(part.matrix_world@v.co) for v in part.data.vertices)
        for poly in part.data.polygons:
            faces.append(tuple(offset+i for i in poly.vertices))
            material=part.data.materials[poly.material_index]
            if material not in materials:materials.append(material)
            material_indices.append(materials.index(material))
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(vertices,[],faces);mesh.update()
    for material in materials:mesh.materials.append(material)
    for poly,index in zip(mesh.polygons,material_indices):poly.material_index=index
    uv=mesh.uv_layers.new().data
    for poly in mesh.polygons:
        axis=max(range(3),key=lambda i:abs(poly.normal[i]))
        for li in poly.loop_indices:
            co=mesh.vertices[mesh.loops[li].vertex_index].co
            uv[li].uv=(co.y,co.z) if axis==0 else ((co.x,co.z) if axis==1 else (co.x,co.y))
    model=bpy.data.objects.new(name,mesh);scene.collection.objects.link(model)
    for part in parts:bpy.data.objects.remove(part,do_unlink=True)
    parts=[];bpy.context.view_layer.update();bpy.ops.object.select_all(action='DESELECT');model.select_set(True);bpy.context.view_layer.objects.active=model
    markers=[]
    for label,loc in [('ForwardMarker',(0,1,0)),('UpMarker',(0,0,1))]:
        ob=bpy.data.objects.new(label,None);scene.collection.objects.link(ob);ob.location=loc;ob.select_set(True);markers.append(ob)
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False)
    for ob in markers:bpy.data.objects.remove(ob,do_unlink=True)
    model.location=(len(exported)*5,0,0);exported.append(model)
    print(name,len(mesh.polygons),'faces')

def ring(name,p,r,mat='Brass',tube=.025):
    for i in range(32):
        a=i*math.tau/32;b=(i+1)*math.tau/32
        beam(name,(p[0]+r*math.cos(a),p[1],p[2]+r*math.sin(a)),(p[0]+r*math.cos(b),p[1],p[2]+r*math.sin(b)),tube,mat)

def target(p,r=.32):
    for i,mat in enumerate(('Cream','Red','Cream','Red')):
        cyl('Target rings',(p[0],p[1]-.008*i,p[2]),r*(1-i*.23),.02,mat,(0,1,0),32,0)

def duck(p,s=1):
    start=len(parts)
    orb('Duck body',(0,0,.28),(.30,.32,.26),'Gold')
    orb('Duck head',(0,-.19,.59),(.19,.18,.20),'Gold')
    orb('Beak',(0,-.37,.56),(.16,.12,.05),'Red')
    for side in (-1,1):
        orb('Wing',(side*.26,.025,.31),(.07,.18,.12),'Brass')
        orb('Eye',(side*.13,-.32,.65),(.021,.015,.025),'Ink')
    for o in parts[start:]:o.location=Vector(p)+o.location*s;o.scale*=s

def cabinet(w=1.12,d=.72,h=1.05,color='Red'):
    box('Cabinet',(0,0,h*.5),(w-.08,d-.06,h-.06),'OakDark',.03)
    for i in range(6):box('Painted panels',(-w*.416+i*w/6,-d/2,h*.5),(w/6-.012,.045,h-.08),color if i%2==0 else 'Cream')
    for x in (-w/2,w/2):
        box('Corner timber',(x,-d*.5,h*.5),(.075,.085,h),'OakLight')
        for z in (.1,h-.1):cyl('Iron bolt',(x,-d*.57,z),.026,.02,'Iron',(0,1,0),8,0)
    box('Counter',(0,-.025,h),(w+.12,d+.16,.10),'OakLight',.025)

def awning(w=1.4,depth=.95,z=2.1):
    n=10
    for i in range(n):
        x=-w/2+(i+.5)*w/n
        a=box('Striped canvas',(x,0,z),(w/n-.004,depth,.04),'Red' if i%2==0 else 'Cream',.006);a.rotation_euler.x=.12
        orb('Scallop',(x,-depth/2,z-.08),(w/n*.5,.025,.10),'Red' if i%2==0 else 'Cream')

# Hero cover: toy delivery trolley. Closed back gives one standing player cover.
cabinet(h=.98)
box('Solid prize back',(0,.20,1.36),(1.10,.10,1.10),'Teal',.025)
for z in (1.03,1.49):
    box('Display tray',(0,0,z),(1.20,.80,.075),'OakLight')
    for i in (-1,1):toy('Bear' if z<1.2 else 'Rabbit',(i*.26,-.07,z+.035),'Rose' if i<0 else 'Blue',.40)
box('Striped top',(0,.1,1.92),(1.22,.75,.10),'Red')
for s in (-1,1):
    cyl('Cart wheel',(s*.61,.12,.25),.25,.08,'OakDark',(1,0,0),20)
    cyl('Hub',(s*.66,.12,.25),.06,.08,'Brass',(1,0,0),12)
    for i in range(8):
        a=i*math.tau/8;beam('Spoke',(s*.658,.12,.25),(s*.658,.12+.21*math.cos(a),.25+.21*math.sin(a)),.016,'Brass')
export('DHF_PrizeCart')

# Wheel repair stand; disk is a real circular upper obstacle, not an invisible box.
cabinet(h=1.06,color='Teal')
box('Wheel spine',(0,.13,1.42),(.11,.14,.84),'OakDark')
target((0,-.02,1.53),.48)
for i in range(12):
    a=i*math.tau/12
    beam('Wheel spoke',(0,-.08,1.53),(.44*math.cos(a),-.08,1.53+.44*math.sin(a)),.022,'Brass')
for x in (-.40,.35):cyl('Paint tin',(x,-.19,1.19),.08,.19,'Blue' if x<0 else 'Red',verts=16)
export('DHF_WheelStand')

# Four-metre prize display: rear-wall dressing, no independent gameplay shield.
for x in (-1.92,1.92):box('Shelf upright',(x,.12,2.1),(.16,.36,4.2),'OakLight')
for row,z in enumerate((.55,1.49,2.48,3.40)):
    box('Shelf',(0,0,z),(4.05,.56,.105),'OakLight')
    for i in range(6):
        x=-1.57+i*.63
        if row%2==0:toy('Bear' if i%2==0 else 'Rabbit',(x,-.05,z+.05),('Rose','Blue','Gold','Purple')[i%4],.51)
        elif i%2==0:target((x,-.07,z+.38),.27)
        else:
            for j in range(3):cyl('Prize tin',(x+(j-1)*.15,-.02,z+.15),.068,.20,'Brass',verts=12)
for x in (-2.04,2.04):
    for z in (.6,1.6,2.6,3.6):orb('Warm bulb',(x,-.17,z),(.055,.055,.055),'Glow')
export('DHF_PrizeDisplay')

# Painted bas-relief shooting backdrop, intentionally very shallow.
for i in range(12):box('Blue painted slat',(-1.98+i*.36,.13,2.0),(.348,.06,3.85),'Blue' if i%3 else 'TealLight')
for x,z in ((-1.2,3.15),(.8,3.50)):
    for dx,dz,r in ((-.24,0,.22),(0,.1,.31),(.28,0,.19)):
        orb('Painted cloud',(x+dx,.079,z+dz),(r,.014,r*.60),'Cream')
for x in (-1.35,0,1.35):
    target((x,.035,1.93),.34)
    beam('Target hanging cord',(x,.12,3.8),(x,.12,2.28),.014,'Brass')
    orb('Duck relief',(x-.08,.055,2.77),(.30,.027,.18),'Gold')
    orb('Duck head relief',(x+.18,.055,2.98),(.135,.029,.135),'Gold')
    box('Beak relief',(x+.33,.055,2.96),(.14,.045,.07),'Red')
for z in (.13,3.98):box('Frame',(0,0,z),(4.4,.18,.12),'OakLight')
for x in (-2.15,2.15):box('Frame',(x,0,2.05),(.12,.18,3.9),'OakDark')
export('DHF_TargetPanel')

# Workshop rack: rings, rope, hammer and stored tins in a coherent vignette.
for x in (-1.9,1.9):box('Workshop post',(x,.10,2),(.12,.32,4),'OakDark')
for z in (.55,2.30,3.65):box('Workshop shelf',(0,.08,z),(3.95,.50,.09),'OakLight')
for x in (-1.45,-.87,-.25):ring('Hanging hoop',(x,-.02,1.55),.40,('Red','Blue','Gold')[int((x+1.5)*3)%3],.025)
for i in range(4):ring('Rope coil',(.90,-.07-i*.01,1.48),.38-i*.025,'Brass',.023)
for i in range(9):cyl('Workshop tins',(-1.6+i*.40,-.03,2.50),.12,.30,'Brass',verts=12)
for i in range(4):box('Stored packet',(-1.4+i*.85,0,.93),(.62,.35,.65),'Red' if i%2 else 'Cream')
export('DHF_ServiceRack')

# Soft double-colour drape. Unit span six metres, hanging ~0.65 m.
for band,mat in ((0,'Red'),(1,'Cream')):
    verts=[];faces=[]
    for i in range(41):
        u=i/40;x=(u-.5)*6;sag=math.sin(math.pi*u)
        for edge in (0,1):verts.append((x,-.02*math.sin(u*math.pi*12),-.65*sag-(band*.11+edge*.17)*sag))
    for i in range(40):faces.append((i*2,i*2+1,i*2+3,i*2+2))
    mesh=bpy.data.meshes.new('Cloth folds');mesh.from_pydata(verts,[],faces);mesh.update()
    ob=bpy.data.objects.new('Draped cloth',mesh);scene.collection.objects.link(ob);finish(ob,'Scalloped drape',mat)
export('DHF_Swag')

# Reusable scenic furnishings. No collider baked into exports.
for x in (-.70,.70):
    beam('Bench leg',(x,-.22,0),(x,-.22,.52),.09,'Iron');beam('Bench leg',(x,.22,0),(x,.22,.91),.09,'Iron')
for y in (-.22,0,.22):box('Bench seat',(0,y,.52),(1.8,.19,.065),'OakLight')
for z in (.74,.94):box('Bench back',(0,.24,z),(1.8,.055,.16),'Oak')
export('DHF_Bench')
cyl('Queue base',(0,0,.035),.22,.07,'Iron',verts=20)
cyl('Queue post',(0,0,.57),.045,1.07,'Brass',verts=12)
orb('Post finial',(0,0,1.13),(.10,.10,.10),'Brass')
export('DHF_QueuePost')
for i in range(16):
    a=i*math.tau/16
    o=box('Barrel stave',(.36*math.cos(a),.36*math.sin(a),.49),(.15,.045,.93),'Oak' if i%3 else 'OakLight');o.rotation_euler.z=a+math.pi/2
for z in (.14,.79):cyl('Barrel hoop',(0,0,z),.394,.08,'Iron',verts=20)
cyl('Barrel lid',(0,0,.98),.37,.035,'OakDark',verts=20)
export('DHF_Barrel')

# Round striped tent (radius 3), scalloped valance and canvas walls.
for i in range(24):
    a=i*math.tau/24;b=(i+1)*math.tau/24
    verts=[(0,0,5.1),(3*math.cos(a),3*math.sin(a),3.1),(3*math.cos(b),3*math.sin(b),3.1)]
    mesh=bpy.data.meshes.new('Canvas');mesh.from_pydata(verts,[],[(0,1,2)]);mesh.update()
    ob=bpy.data.objects.new('Roof stripe',mesh);scene.collection.objects.link(ob);finish(ob,'Roof stripe','Red' if i%2 else 'Cream')
    mid=(a+b)/2
    orb('Canvas valance',(3*math.cos(mid),3*math.sin(mid),2.98),(.40,.16,.24),'Red' if i%2 else 'Cream')
    if i not in (16,17,18,19):
        ob=box('Canvas wall',(2.9*math.cos(mid),2.9*math.sin(mid),1.50),(.77,.045,2.85),'Red' if i%2 else 'Cream');ob.rotation_euler.z=mid+math.pi/2
for i in range(8):
    a=i*math.tau/8;beam('Tent mast',(2.9*math.cos(a),2.9*math.sin(a),0),(2.9*math.cos(a),2.9*math.sin(a),3.12),.075,'OakDark')
beam('Flagpole',(0,0,4.9),(0,0,5.9),.045,'Brass')
box('Top flag',(.38,0,5.68),(.75,.02,.35),'Red')
export('DHF_Tent')

# Carousel split into a turning frame and one separate horse.
# Unity spins the frame and bobs six copies of the horse on their poles;
# a single joined mesh could only ever turn as a lump.
CAROUSEL_RING=1.95
cyl('Carousel deck',(0,0,.20),2.9,.4,'Oak',verts=48)
cyl('Carousel rim',(0,0,.42),2.92,.09,'Brass',verts=48)
for i in range(16):
    a=i*math.tau/16;b=(i+1)*math.tau/16
    mesh=bpy.data.meshes.new('Canopy');mesh.from_pydata([(0,0,4.5),(3.1*math.cos(a),3.1*math.sin(a),3.3),(3.1*math.cos(b),3.1*math.sin(b),3.3)],[],[(0,1,2)]);mesh.update()
    ob=bpy.data.objects.new('Canopy stripe',mesh);scene.collection.objects.link(ob);finish(ob,'Canopy stripe','Red' if i%2 else 'Cream')
    orb('Canopy light',(3.1*math.cos(a),3.1*math.sin(a),3.26),(.09,.09,.09),'Glow')
beam('Carousel mast',(0,0,.4),(0,0,4.55),.115,'Brass')
orb('Mast finial',(0,0,4.72),(.17,.17,.22),'Gold')
for i in range(6):
    a=i*math.tau/6;p=Vector((CAROUSEL_RING*math.cos(a),CAROUSEL_RING*math.sin(a),0))
    beam('Carousel pole',p+Vector((0,0,.4)),p+Vector((0,0,3.35)),.055,'Brass')
export('DHF_CarouselFrame')

# One horse, nose along +Y so the Unity wrapper can point it along the ring.
orb('Horse body',(0,0,1.2),(.19,.46,.24),'Cream')
beam('Horse neck',(0,.26,1.3),(0,.38,1.7),.18,'Cream')
orb('Horse head',(0,.46,1.70),(.12,.22,.13),'Cream')
orb('Muzzle',(0,.60,1.63),(.085,.10,.085),'Rose')
box('Saddle',(0,-.04,1.41),(.36,.35,.075),'Red')
box('Saddle cloth',(0,-.05,1.33),(.40,.46,.05),'Blue')
for i in (-1,1):orb('Horse ear',(i*.06,.42,1.85),(.035,.03,.07),'Cream')
for y in (-.28,.28):
    for x in (-.13,.13):beam('Horse leg',(x,y,1.12),(x,y-.08,.67),.063,'Cream')
for i in range(5):
    t=i/4;beam('Tail',(0,-.42-t*.10,1.34-t*.34),(0,-.44-t*.10,1.22-t*.34),.05-t*.015,'Gold')
export('DHF_CarouselHorse')

# Parked car. Body is Teal so the Unity pass can repaint each copy.
box('Car body',(0,0,.60),(1.72,4.02,.56),'Teal')
box('Car skirt',(0,0,.34),(1.60,3.86,.22),'Teal')
box('Car cabin',(0,-.12,1.14),(1.50,1.94,.52),'Teal')
box('Windscreen',(0,.86,1.16),(1.36,.06,.42),'Ink')
box('Rear glass',(0,-1.10,1.16),(1.32,.06,.40),'Ink')
for i in (-1,1):box('Side glass',(i*.76,-.12,1.16),(.05,1.80,.38),'Ink')
box('Roof',(0,-.12,1.41),(1.46,1.90,.06),'Cream')
for i in (-1,1):
    box('Bumper',(0,i*2.02,.52),(1.66,.12,.26),'Iron')
    for y in (1.34,-1.34):cyl('Wheel',(i*.83,y,.36),.36,.24,'Ink',(1,0,0),14)
for i in (-1,1):
    box('Headlight',(i*.60,2.03,.72),(.34,.06,.16),'Glow')
    box('Tail light',(i*.60,-2.03,.72),(.30,.06,.14),'Red')
export('DHF_Car')

for i in range(9):
    a=i*2.4;r=.12+(.31 if i%2 else .1);p=(r*math.cos(a),r*math.sin(a),0)
    beam('Grass blade',p,(p[0]+.06,p[1],.22+i%3*.08),.022,'LeafLight' if i%2 else 'Leaf')
for i in range(4):
    p=(.28*math.cos(i*1.7),.28*math.sin(i*1.7),.25)
    beam('Flower stalk',(p[0],p[1],0),p,.012,'Leaf')
    for j in range(5):orb('Petal',(p[0]+.035*math.cos(j*math.tau/5),p[1]+.035*math.sin(j*math.tau/5),p[2]),(.025,.025,.014),'Gold' if i%2 else 'Cream')
export('DHF_Grass')

# One wide, subtly bevelled board: avoids a forest of subpixel seams in facade views.
box('Painted board',(0,.035,.5),(.988,.07,1),'Teal',.002)
export('DHF_PaintedBoard')

scene.render.image_settings.file_format='PNG'
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'DH_Fairground_Kit.blend'))
print('Exported final modules:',len(exported))
