"""Original stylised Duck Hunt rifle. Run through Blender MCP; no external assets.
Blender metres: X right, Y muzzle, Z up. Separate scene; preserves other scenes.
"""
import bpy
import math
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'igruha/Assets/_Project/Art/DuckHuntOriginal/Models'
SOURCE = ROOT / 'tools/blender/source/DuckHuntOriginal'
OUTPUT.mkdir(parents=True, exist_ok=True)
SOURCE.mkdir(parents=True, exist_ok=True)
scene = bpy.data.scenes.new('DuckHunt_Rifle_Source')
bpy.context.window.scene = scene
parts = []

def material(name, color, metal=0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    node = mat.node_tree.nodes.get('Principled BSDF')
    node.inputs['Base Color'].default_value = (*color, 1)
    node.inputs['Metallic'].default_value = metal
    node.inputs['Roughness'].default_value = .42
    return mat

wood = material('DH_Rifle_Walnut', (.29, .12, .055))
steel = material('DH_Rifle_Steel', (.10, .16, .19), .65)
brass = material('DH_Rifle_Brass', (.68, .43, .13), .55)
rubber = material('DH_Rifle_Rubber', (.025, .033, .039))

def finish(obj, name, mat, bevel=0):
    obj.name = name
    obj.data.materials.append(mat)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = obj.modifiers.new('Soft manufactured edges', 'BEVEL')
        mod.width = bevel
        mod.segments = 2
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
    parts.append(obj)
    return obj

def box(name, pos, size, mat, bevel=.012):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pos)
    obj = bpy.context.object
    obj.dimensions = size
    return finish(obj, name, mat, bevel)

def barrel(x):
    # Hollow, tapered, twelve-sided muzzle; no black disk pretending to be a hole.
    vertices=[]
    for y, radius in ((.18,.052),(1.1,.047),(1.1,.033),(.18,.033)):
        for i in range(12):
            a=i*math.tau/12
            vertices.append((x+radius*math.cos(a),y,.11+radius*math.sin(a)))
    faces=[]
    for ring in range(4):
        for i in range(12):
            a=ring*12+i; b=ring*12+(i+1)%12
            c=((ring+1)%4)*12+(i+1)%12; d=((ring+1)%4)*12+i
            faces.append((a,b,c,d))
    mesh=bpy.data.meshes.new('HollowBarrel'); mesh.from_pydata(vertices, [], faces); mesh.update()
    obj=bpy.data.objects.new('Barrel',mesh); scene.collection.objects.link(obj)
    finish(obj, 'Barrel_L' if x<0 else 'Barrel_R', steel)

# Hand-authored stock silhouette, extruded in X.
profile=[(-.39,-.16),(-.39,.13),(-.27,.14),(-.05,.095),(.11,.055),(.09,-.045),(-.10,-.055),(-.23,-.14)]
verts=[(x,y,z) for x in (-.062,.062) for y,z in profile]
n=len(profile); faces=[tuple(reversed(range(n))),tuple(range(n,n*2))]
faces += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
mesh=bpy.data.meshes.new('WalnutStock'); mesh.from_pydata(verts,[],faces);mesh.update()
obj=bpy.data.objects.new('WalnutStock',mesh);scene.collection.objects.link(obj)
bpy.context.view_layer.objects.active=obj; obj.select_set(True)
finish(obj,'WalnutStock',wood,.015)
box('ButtPad',(0,-.397,-.01),(.14,.035,.30),rubber)
box('Receiver',(0,.18,.093),(.17,.22,.145),steel,.024)
box('ForeStock',(0,.56,.005),(.15,.40,.10),wood,.026)
for x in (-.053,.053): barrel(x)
box('BarrelRib',(0,.70,.16),(.025,.79,.023),steel,.005)
box('FrontSight',(0,1.025,.187),(.035,.045,.04),brass,.006)
box('ActionLatch',(0,.11,.182),(.032,.15,.025),brass,.007)
for y in (.39,.71): box('ForeBand',(0,y,.012),(.16,.024,.11),brass,.007)
# Trigger guard formed from four short pieces, readable at game distance.
box('GuardBottom',(0,.135,-.102),(.023,.16,.02),brass,.007)
for y in (.06,.21): box('GuardSide',(0,y,-.062),(.023,.02,.09),brass,.007)
box('Trigger',(0,.135,-.04),(.016,.025,.067),steel,.005)
for name,location in [('GripMarker',(0,.055,0)),('ForeMarker',(0,.58,0)),('UpMarker',(0,.055,1))]:
    marker=bpy.data.objects.new(name,None); marker.location=location
    scene.collection.objects.link(marker); parts.append(marker)

bpy.ops.object.select_all(action='DESELECT')
for obj in parts: obj.select_set(True)
bpy.context.view_layer.objects.active=parts[0]
bpy.ops.export_scene.fbx(filepath=str(OUTPUT/'DH_OriginalRifle.fbx'),use_selection=True,
    object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,
    bake_anim=False,apply_unit_scale=True)

# Presentation camera and lights only in the source, not exported into gameplay.
bpy.ops.object.camera_add(location=(1.5,-1.4,1.05))
camera=bpy.context.object; camera.name='RiflePresentationCamera'
camera.rotation_euler=(Vector((0,.35,.04))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.type='ORTHO';camera.data.ortho_scale=1.85;scene.camera=camera
for pos,power,size in [((1,-1,3),450,4),((-2,1,1),250,3)]:
    bpy.ops.object.light_add(type='AREA',location=pos)
    lamp=bpy.context.object;lamp.data.energy=power;lamp.data.shape='DISK';lamp.data.size=size
    lamp.rotation_euler=(Vector((0,.3,0))-lamp.location).to_track_quat('-Z','Y').to_euler()
scene.world=bpy.data.worlds.new('RifleStudio');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.12,.15,.19,1)
scene.render.engine='CYCLES';scene.cycles.samples=24
scene.render.resolution_x=1200;scene.render.resolution_y=700;scene.render.resolution_percentage=100
scene.render.filepath=str(SOURCE/'rifle-preview.png')
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'DH_OriginalRifle.blend'))
bpy.ops.render.render(write_still=True)
print({'export':str(OUTPUT/'DH_OriginalRifle.fbx'),'parts':len(parts),
       'triangles':sum(len(o.data.polygons) for o in parts if o.type=='MESH')})
