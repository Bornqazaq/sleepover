"""Original Duck Hunt / Copperwood shooting lodge kit. Execute through Blender MCP.
Metres, X right / -Y face / Z up. Source stays outside Assets: no auto .blend importer.
Each object is a crafted multi-material mesh; Unity owns gameplay collision.
"""
import bpy, math, random
from mathutils import Vector
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'igruha/Assets/_Project/Art/DuckHuntBarn/Models'
SOURCE = ROOT / 'tools/blender/source/DuckHuntBarn'
OUT.mkdir(parents=True, exist_ok=True)
SOURCE.mkdir(parents=True, exist_ok=True)
previous = bpy.data.scenes.get('DH_Copperwood_Kit')
scene = bpy.data.scenes.new('DH_Copperwood_Kit_Rebuild')
bpy.context.window.scene = scene
if previous and all(o.name.startswith('DHB_') for o in previous.objects):
    for o in list(previous.objects): bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.scenes.remove(previous)
scene.name = 'DH_Copperwood_Kit'
parts = []
palette = {
    'Oak': (.46,.255,.105), 'OakLight': (.63,.39,.18), 'OakDark': (.25,.125,.057),
    'Teal': (.12,.30,.285), 'TealLight': (.20,.405,.37), 'Cream': (.79,.71,.51),
    'Iron': (.075,.105,.11), 'Brass': (.63,.39,.125), 'Red': (.55,.16,.085),
    'Glow': (1,.63,.22), 'Leaf': (.16,.285,.20), 'LeafLight': (.27,.39,.22),
    'Stone': (.35,.385,.32), 'Ink': (.035,.065,.063)
}
mats = {}
for key, col in palette.items():
    m = bpy.data.materials.get('DHB_'+key) or bpy.data.materials.new('DHB_'+key)
    m.diffuse_color = (*col,1)
    node = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    node.inputs['Base Color'].default_value = (*col,1)
    node.inputs['Roughness'].default_value = .65
    if key in ('Iron','Brass'): node.inputs['Metallic'].default_value = .55
    mats[key] = m

def finish(o, name, mat, bevel=0):
    o.name = name
    o.data.materials.append(mats[mat])
    if bevel:
        m=o.modifiers.new('Crafted edge','BEVEL');m.width=bevel;m.segments=2
        bpy.context.view_layer.objects.active=o
        bpy.ops.object.modifier_apply(modifier=m.name)
    parts.append(o)
    return o

def box(name,p,s,mat='Oak',bevel=.012):
    bpy.ops.mesh.primitive_cube_add(size=1,location=p)
    o=bpy.context.object;o.dimensions=s
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,mat,min(bevel,min(s)*.23))

def cyl(name,p,r,d,mat='Brass',axis=None,verts=16,bevel=.006):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=r,depth=d,location=p)
    o=bpy.context.object
    if axis: o.rotation_euler=Vector(axis).to_track_quat('Z','Y').to_euler()
    return finish(o,name,mat,bevel)

def beam(name,a,b,w,mat='OakDark',depth=None):
    a,b=Vector(a),Vector(b)
    o=box(name,(a+b)/2,(w,depth or w,(a-b).length),mat)
    o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    return o

def sphere(name,p,s,mat='Brass'):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=8,radius=1,location=p)
    o=bpy.context.object;o.scale=s
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,mat)

def text(name,words,p,size,mat='Cream'):
    cu=bpy.data.curves.new(name,'FONT');cu.body=words;cu.size=size;cu.align_x='CENTER';cu.extrude=.003;cu.bevel_depth=.001
    cu.resolution_u=3;cu.bevel_resolution=0
    ob=bpy.data.objects.new(name,cu);scene.collection.objects.link(ob)
    ob.location=p;ob.rotation_euler=(math.pi/2,0,0)
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
    bpy.ops.object.convert(target='MESH');finish(bpy.context.object,name,mat)

def badge(z=1.32,y=-.302,scale=1):
    cyl('Cast brass badge',(0,y,z),.18*scale,.026,'Brass',(0,1,0),32)
    cyl('Enamel inset',(0,y-.019,z),.153*scale,.018,'Teal',(0,1,0),32)
    sphere('Duck body',(-.026*scale,y-.036,z-.022*scale),(.091*scale,.014,.053*scale),'Cream')
    sphere('Duck head',(.057*scale,y-.036,z+.04*scale),(.041*scale,.015,.041*scale),'Cream')
    box('Beak',(.102*scale,y-.037,z+.036*scale),(.05*scale,.022,.023*scale),'Brass',.004)
    sphere('Eye',(.067*scale,y-.053,z+.054*scale),(.006,.004,.006),'Ink')

def export(name):
    global parts
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:o.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]
    bpy.ops.object.join();model=bpy.context.object;model.name=name
    scene.cursor.location=(0,0,0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    # Deterministic box UVs with metre-scale grain; no generated external texture dependencies.
    if not model.data.uv_layers: model.data.uv_layers.new()
    uv=model.data.uv_layers.active.data
    for poly in model.data.polygons:
        axis=max(range(3),key=lambda i:abs(poly.normal[i]))
        for li in poly.loop_indices:
            co=model.data.vertices[model.data.loops[li].vertex_index].co
            uv[li].uv=(co.y,co.z) if axis==0 else ((co.x,co.z) if axis==1 else (co.x,co.y))
    markers=[]
    for label,loc in [('ForwardMarker',(0,1,0)),('UpMarker',(0,0,1))]:
        o=bpy.data.objects.new(label,None);scene.collection.objects.link(o);o.location=loc;o.select_set(True);markers.append(o)
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',bake_anim=False,add_leaf_bones=False)
    for o in markers:bpy.data.objects.remove(o,do_unlink=True)
    # Display kit pieces side by side after exporting in local coordinates.
    model.location=(len(exported)*4,0,0);exported.append(model);parts=[]
    print(name, len(model.data.polygons),'faces')

exported=[]

# Hero single-person cover: closed, reinforced equipment cabinet, not a silhouette shield.
box('Solid carcass',(0,0,.93),(.80,.49,1.78),'OakDark',.03)
for i in range(5):
    box('Tongue and groove front',(-.32+i*.16,-.255,.97),(.154,.05,1.62),'Teal' if i%2 else 'TealLight',.009)
for side in (-1,1):
    box('Corner iron',(.399*side,-.275,.95),(.04,.025,1.75),'Iron')
    for z in (.18,.8,1.70):
        box('Wrap strap',(0,0,z),(.854,.55,.065),'Iron')
        for x in (-.355,.355):cyl('Rivet',(x,-.284,z),.014,.018,'Brass',(0,1,0),8)
    box('Side panel',(.412*side,.008,.94),(.023,.41,1.55),'Teal')
    box('Foot',(.315*side,0,.052),(.16,.55,.104),'Iron')
box('Crown moulding',(0,0,1.84),(.89,.61,.09),'OakLight',.024)
box('Top cap',(0,0,1.90),(.83,.56,.045),'Teal',.02)
box('Lower moulding',(0,0,.15),(.87,.57,.1),'OakLight')
badge()
text('Cabinet label','FIELD KIT',(0,-.292,.49),.085)
for x in (-.22,.22):
    box('Hinge',(x,-.30,.88),(.09,.025,.13),'Brass')
cyl('Round latch',(0,-.30,.81),.047,.04,'Brass',(0,1,0))
export('DHB_FieldCabinet')

# Deck module: exact 1x1, top at zero; six separate bevelled planks and forged underside strap.
for i in range(5):
    box('Floorboard',(0,-.4+i*.2,-.055),(.995,.194,.11),('Oak','OakLight','Oak','Oak','OakLight')[i],.006)
    for x in (-.43,.43):cyl('Nail',(x,-.4+i*.2,-.002),.006,.006,'Iron',verts=6,bevel=0)
box('Bearer',(0,0,-.17),(.15,.99,.18),'OakDark')
export('DHB_Deck')

# Wall cladding one square metre, front at Y=0, volume toward positive Y.
for i in range(5):box('Barn plank',(-.4+i*.2,.055,.5),(.194,.11,1),('Teal','TealLight','Teal','Teal','TealLight')[i],.007)
export('DHB_Cladding')

# Structural beam 1m high, base at zero. Separate wood core, iron shoes, bolt heads.
box('Timber',(0,0,.5),(.22,.22,1),'OakDark',.014)
for z in (.07,.93):
    box('Iron shoe',(0,0,z),(.239,.24,.11),'Iron')
    for side in (-1,1):cyl('Structural bolt',(0,.126*side,z),.023,.014,'Brass',(0,1,0),6)
export('DHB_Timber')

# Caged hanging lantern: curved shade, turned finials, glowing inner tube.
cyl('Canopy',(0,0,.90),.18,.055,'Iron',verts=24)
cyl('Stem',(0,0,.71),.029,.34,'Brass')
cyl('Shade lip',(0,0,.55),.27,.045,'Iron',verts=24)
bpy.ops.mesh.primitive_cone_add(vertices=24,radius1=.27,radius2=.11,depth=.19,location=(0,0,.66))
finish(bpy.context.object,'Spun metal shade','Teal',.01)
cyl('Glass light',(0,0,.32),.112,.37,'Glow',verts=20)
for z in (.11,.49):cyl('Cage ring',(0,0,z),.16,.026,'Brass',verts=24)
for a in range(6):
    ang=a*math.tau/6;beam('Cage bar',(.145*math.cos(ang),.145*math.sin(ang),.11),(.145*math.cos(ang),.145*math.sin(ang),.49),.018,'Iron')
cyl('Base',(0,0,.08),.13,.05,'Iron')
sphere('Finial',(0,0,.045),(.035,.035,.04))
export('DHB_Lantern')

# Remote-control pedestal, kept lower than a duck torso and narrow.
box('Pedestal',(0,0,.35),(.44,.42,.70),'Teal',.04)
box('Foot',(0,0,.045),(.59,.55,.09),'Iron',.023)
box('Console',(0,0,.75),(.64,.55,.15),'Iron',.035)
box('Top face',(0,0,.837),(.53,.43,.025),'Brass')
for x in (-.20,.20):
    for y in (-.16,.16):cyl('Console screw',(x,y,.855),.018,.015,'Iron',verts=8)
box('Inspection plate',(0,-.221,.43),(.3,.025,.24),'Cream')
text('Warning','PULL',(0,-.239,.45),.082,'Ink')
text('Warning small','TO TRAP',(0,-.239,.35),.044,'Ink')
export('DHB_Console')

# Lever on the existing animated pivot. +Z in Blender = Unity up.
cyl('Lever stem',(0,0,.20),.027,.4,'Brass')
cyl('Pivot',(0,0,0),.075,.15,'Iron',(1,0,0))
cyl('Handle grip',(0,0,.42),.049,.30,'Red',(1,0,0))
for x in (-.155,.155):sphere('Grip cap',(x,0,.42),(.032,.052,.052),'Brass')
export('DHB_Lever')

# Sliding door: normalized x width and z height, front y negative.
box('Door frame',(0,0,.5),(1,.14,1),'Iron')
for i in range(6):box('Door plank',(-.416+i/6,-.085,.5),(.16,.075,.87),'Red' if i%2 else 'Oak',.008)
for z in (.1,.9):box('Cross strap',(0,-.134,z),(.98,.025,.05),'Brass')
beam('Diagonal brace',(-.42,-.13,.17),(.42,-.13,.83),.054,'Iron',.025)
export('DHB_TrapDoor')

# Decorative window against a solid wall; not a view through collision.
box('Recess',(0,.04,1.0),(1.44,.13,1.86),'OakDark')
box('Dark glass',(0,-.045,1.0),(1.20,.06,1.6),'Ink')
for x in (-.68,0,.68):box('Mullion',(x,-.11,1.0),(.095,.12,1.88),'Cream')
for z in (.11,1,1.89):box('Transom',(0,-.11,z),(1.43,.12,.09),'Cream')
box('Sill',(0,-.12,.075),(1.65,.38,.14),'OakLight')
box('Crown',(0,-.05,1.97),(1.61,.23,.12),'OakLight')
export('DHB_Window')

# Full-size lodge sign for the roofline. Bold type with real depth and duck crest.
box('Sign timber',(0,0,0),(8,.22,1.48),'OakDark',.12)
box('Sign enamel',(0,-.13,0),(7.75,.07,1.25),'Teal',.1)
for z in (-.62,.62):box('Sign edging',(0,-.18,z),(7.76,.05,.06),'Brass')
text('Title','DUCK HUNT',(0,-.18,-.13),.79)
text('Subtitle','COPPERWOOD  /  SHOOTING LODGE',(0,-.185,-.48),.17,'Cream')
for x in (-3.65,3.65):cyl('Sign bolt',(x,-.19,0),.065,.04,'Brass',(0,1,0),8)
export('DHB_Sign')

# Original stylized layered pine: irregular silhouette, visible branching trunk.
cyl('Trunk',(0,0,1.6),.17,3.2,'OakDark',verts=10)
rng=random.Random(216)
for tier in range(5):
    z=1.1+tier*.63;r=1.38-tier*.22
    for j in range(5):
        a=j*math.tau/5+tier*.43
        p=(math.cos(a)*r*.35,math.sin(a)*r*.35,z)
        bpy.ops.mesh.primitive_cone_add(vertices=9,radius1=r*.80,radius2=.03,depth=1.62,location=p)
        o=bpy.context.object;o.rotation_euler.z=rng.random();finish(o,'Needle bough','Leaf' if j%2 else 'LeafLight')
export('DHB_Pine')

# Weathered rock cluster with chamfered faceted faces.
for p,s in [((0,0,.35),(.95,.7,.6)),((.6,.1,.20),(.55,.55,.36)),((-.5,.25,.18),(.42,.5,.3))]:
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=1,location=p)
    o=bpy.context.object;o.scale=s;finish(o,'Granite','Stone')
export('DHB_Rocks')

# Cable hoist wheel, aligned on its front/back axis.
for y in (-.17,.17):
    cyl('Wheel flange',(0,y,0),.60,.09,'Teal',(0,1,0),32)
    cyl('Inset boss',(0,y*1.4,0),.18,.10,'Brass',(0,1,0),20)
cyl('Cable drum',(0,0,0),.42,.34,'Iron',(0,1,0),32)
for a in range(8):
    ang=a*math.tau/8
    beam('Wheel rib',(.13*math.cos(ang),-.24,.13*math.sin(ang)),(.53*math.cos(ang),-.24,.53*math.sin(ang)),.07,'Brass')
export('DHB_Hoist')

# Keep the user's original scene intact; save the kit and arrange a readable viewport.
bpy.ops.object.select_all(action='DESELECT')
exported[0].select_set(True);bpy.context.view_layer.objects.active=exported[0]
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_location=(0,0,.9)
            area.spaces.active.region_3d.view_distance=3.8
            area.spaces.active.shading.color_type='MATERIAL'
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'DH_Copperwood_Kit.blend'))
print('COPPERWOOD KIT READY',len(exported),'models; original scene preserved.')
