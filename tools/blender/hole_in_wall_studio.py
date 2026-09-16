"""Original television studio modules. Execute through Blender MCP.

Creates its own scene; leaves the user's existing Blender scene untouched.
Source library contains only this scene. Metres, Z up, FBX -Z/Y.
"""
import bpy
import math
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'igruha/Assets/_Project/Art/HoleInWallStudio'
SOURCE = ROOT / 'igruha/Assets/_Project/Art/Source/HoleInWallStudio'
for directory in (ART / 'Models', SOURCE):
    directory.mkdir(parents=True, exist_ok=True)
previous = bpy.context.window.scene
if previous.name.startswith('HoleInWall_OriginalStudio'):
    previous = next((s for s in bpy.data.scenes if not s.name.startswith('HoleInWall_OriginalStudio')), None)
    if previous is None:
        previous = bpy.data.scenes.new('Workspace')
    bpy.context.window.scene = previous
for old_scene in list(bpy.data.scenes):
    if old_scene.name.startswith('HoleInWall_OriginalStudio'):
        for obj in list(old_scene.objects):
            data = obj.data
            bpy.data.objects.remove(obj, do_unlink=True)
            if isinstance(data, bpy.types.Mesh) and data.users == 0:
                bpy.data.meshes.remove(data)
        bpy.data.scenes.remove(old_scene)
for old_material in list(bpy.data.materials):
    if old_material.name.startswith('HS_') and old_material.users == 0:
        bpy.data.materials.remove(old_material)
scene = bpy.data.scenes.new('HoleInWall_OriginalStudio')
bpy.context.window.scene = scene
materials = {}
palette = []
def material(name, color, roughness=.45, metallic=0, emission=0):
    m = bpy.data.materials.get('HS_' + name) or bpy.data.materials.new('HS_' + name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    node = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    node.inputs['Base Color'].default_value = (*color, 1)
    node.inputs['Roughness'].default_value = roughness
    node.inputs['Metallic'].default_value = metallic
    node.inputs['Emission Color'].default_value = (*color, 1)
    node.inputs['Emission Strength'].default_value = emission
    materials[name] = m
    palette.append(dict(name='HS_' + name, color=[*color, 1], roughness=roughness,
                        metallic=metallic, emission=emission))

material('Ink', (.018,.033,.075), .55)
material('Blue', (.026,.12,.34), .38, .2)
material('Teal', (.015,.43,.52), .38, .15)
material('Coral', (.95,.15,.19), .5)
material('Ivory', (.86,.91,.87), .32)
material('Gold', (.94,.55,.14), .3, .65)
material('Steel', (.23,.32,.40), .32, .7)
material('Glow', (.10,.84,1), .3, 0, 2)
material('WarmGlow', (1,.64,.22), .35, 0, 2)
material('Skin', (.72,.40,.25), .8)
material('Hair', (.065,.032,.025), .85)
material('White', (.97,.97,.88), .65)

parts = []
def finish(obj, mat):
    obj.data.materials.append(materials[mat])
    parts.append(obj)
    return obj

def box(pos, size, mat, bevel=.04):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pos)
    obj = bpy.context.object
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = obj.modifiers.new('Crafted edge', 'BEVEL')
        mod.width = min(bevel, min(size)*.2)
        mod.segments = 3
        bpy.ops.object.modifier_apply(modifier=mod.name)
        mod = obj.modifiers.new('Corner normals', 'WEIGHTED_NORMAL')
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return finish(obj, mat)

def sphere(pos, size, mat):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=1, location=pos)
    obj=bpy.context.object; obj.scale=size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    for p in obj.data.polygons: p.use_smooth=True
    return finish(obj,mat)

def tube(a,b,r,mat):
    delta=Vector(b)-Vector(a)
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=r, depth=delta.length,
                                      location=(Vector(a)+Vector(b))*.5)
    obj=bpy.context.object
    obj.rotation_mode='QUATERNION'
    obj.rotation_quaternion=delta.to_track_quat('Z','Y')
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    return finish(obj,mat)

def path(points,r,mat):
    for a,b in zip(points,points[1:]): tube(a,b,r,mat)

exports=[]
def export(name):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in parts: obj.select_set(True)
    bpy.context.view_layer.objects.active=parts[0]
    bpy.ops.object.join()
    obj=bpy.context.object; obj.name='HS_'+name
    scene.cursor.location=(0,0,0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.export_scene.fbx(filepath=str(ART/'Models'/('HS_'+name+'.fbx')),
        use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',bake_anim=False)
    exports.append(dict(name=obj.name,vertices=len(obj.data.vertices),polygons=len(obj.data.polygons)))
    parts.clear()
    return obj

box((0,0,0),(1,1,1),'Ivory',.065)
export('Panel')

# Octagonal portal: a recognisable stage entrance with a recessed light channel.
outline=[(-4.7,0,0),(-4.7,0,5.7),(-3.55,0,7),(3.55,0,7),(4.7,0,5.7),(4.7,0,0)]
path(outline,.31,'Blue')
path([(x,-.45,z) for x,y,z in outline],.10,'Glow')
path([(x,.26,z) for x,y,z in outline],.13,'Steel')
for sign in (-1,1):
    box((sign*4.7,0,.25),(.9,1.1,.5),'Ink',.08)
    for z in (1,2.1,3.2,4.3):box((sign*4.7,-.49,z),(.35,.08,.12),'Gold',.015)
export('Portal')

# A compact actual fixture, rather than a cone pretending to be a lamp.
box((0,0,.12),(.9,.72,.24),'Ink',.08)
for x in (-.38,.38):box((x,0,.52),(.12,.28,.9),'Steel',.025)
tube((0,-.34,.7),(0,.32,.7),.29,'Ink')
tube((0,-.37,.7),(0,-.345,.7),.245,'WarmGlow')
for x in (-.35,.35): box((x,-.3,.7),(.09,.35,.62),'Ink',.01)
for z in (.37,1.03): box((0,-.3,z),(.66,.35,.09),'Ink',.01)
export('Spot')

# Four-metre triangular lighting truss.
for y,z in ((-.28,0),(.28,0),(0,.48)):
    tube((-2,y,z),(2,y,z),.055,'Steel')
for i in range(8):
    x=-2+i*.5
    for y in (-.28,.28):tube((x,y,0),(x+.5,0,.48),.024,'Steel')
export('Truss')

# Upholstered spectator seat with a pedestal and arms.
box((0,0,.39),(.58,.58,.13),'Blue',.08)
box((0,.25,.74),(.58,.14,.65),'Blue',.08)
box((0,0,.18),(.12,.14,.36),'Steel',.02)
for x in (-.33,.33):box((x,0,.56),(.075,.46,.07),'Gold',.02)
export('Seat')

for x in (-.38,.38):
    path([(x,.3,0),(x,.3,3.3),(x,.26,3.52),(x,.05,3.68),(x,-.2,3.68),(x,-.35,3.50),(x,-.35,3.1)],.055,'Steel')
for i in range(9):tube((-.38,.3,.2+i*.34),(.38,.3,.2+i*.34),.047,'Steel')
export('Ladder')

# Own stylised spectators. Shared material order between idle/cheer pairs.
for variant in range(3):
    for cheer in (False,True):
        shirt=('Coral','Teal','Ivory')[variant]
        for x in (-.14,.14):
            box((x,-.035,.09),(.20,.36,.16),'Ink',.035)
            tube((x,0,.16),(x,0,.70),.10,'Blue')
        sphere((0,0,1.00),(.32,.18,.38),shirt)
        tube((0,0,1.28),(0,0,1.41),.085,'Skin')
        sphere((0,-.015,1.55),(.19,.16,.23),'Skin')
        sphere((0,.035,1.68),(.197,.16,.13),'Hair')
        for x in (-.066,.066):
            sphere((x,-.165,1.58),(.035,.018,.039),'White')
            sphere((x,-.181,1.58),(.013,.009,.021),'Ink')
        sphere((0,-.181,1.51),(.035,.035,.045),'Skin')
        box((0,-.163,1.445),(.08,.02,.023),'Hair',.005)
        for s in (-1,1):
            shoulder=(s*.27,0,1.20)
            elbow=(s*.43,-.03,1.51 if cheer else .91)
            wrist=(s*.49,-.06,1.90 if cheer else .70)
            tube(shoulder,elbow,.088,shirt)
            tube(elbow,wrist,.062,'Skin')
            sphere(wrist,(.082,.063,.09),'Skin')
        export('Fan%d_%s'%(variant,'Cheer' if cheer else 'Idle'))

# Stage vault: broad paired arcs create depth above the four lanes.
for radius,height,y,mat,r in [(24,9,0,'Ivory',.26),(23.25,8.5,-.18,'Glow',.07),(24.7,9.6,.3,'Blue',.38)]:
    points=[(radius*math.cos(i*math.pi/48),y,height*math.sin(i*math.pi/48)) for i in range(49)]
    path(points,r,mat)
export('StageVault')

# Floating elliptical light ring, suspended above the water.
for rad,mat,thick,z in [(1,'Steel',.12,0),(.96,'WarmGlow',.055,-.10)]:
    path([(7*rad*math.cos(i*math.tau/64),3.3*rad*math.sin(i*math.tau/64),z) for i in range(65)],thick,mat)
for x in (-4.9,4.9):
    for y in (-2.1,2.1):tube((x,y,0),(x,y,1.9),.018,'Steel')
export('Halo')

# Score instrument: recessed display, side badge and three-dimensional chassis.
box((0,0,0),(5.3,.60,2.8),'Steel',.16)
box((0,-.36,0),(5.15,.18,2.62),'Blue',.12)
box((.55,-.48,0),(3.65,.13,2.25),'Ink',.10)
tube((-1.88,-.48,.10),(-1.88,-.64,.10),.62,'Glow')
tube((-1.88,-.65,.10),(-1.88,-.69,.10),.53,'Ink')
for x in (-2.50,2.50):
    for z in (-1.08,1.08):tube((x,-.45,z),(x,-.53,z),.052,'Gold')
for x in (-1.8,1.8):box((x,.05,1.67),(.10,.18,.72),'Steel',.02)
for z in (-.95,0,.95):box((2.59,.02,z),(.09,.65,.12),'Ink',.02)
export('Scoreboard')

# Repeated folded acoustic cassette. Its fins catch the key at different angles.
box((0,.10,0),(3.65,.36,5.7),'Ink',.10)
for i in range(6):
    obj=box((-1.48+i*.59,-.17,0),(.44,.25,5.24),'Blue',.05)
    obj.rotation_euler.z=math.radians(-18)
for z in (-2.72,2.72):box((0,-.18,z),(3.35,.08,.08),'Steel',.015)
box((1.67,-.17,0),(.055,.08,4.5),'Glow',.012)
export('WallCassette')

# Ceiling coffers with warm recessed softboxes, supplied in horizontal orientation.
box((0,0,0),(7.4,4.8,.30),'Blue',.12)
box((0,0,-.19),(7.0,4.4,.16),'Ink',.10)
for x in (-3.12,3.12):
    box((x,0,-.30),(.34,3.85,.13),'Steel',.035)
    box((x,0,-.38),(.16,3.55,.035),'WarmGlow',.015)
for y in (-1.9,1.9):box((0,y,-.30),(5.5,.10,.05),'Glow',.02)
for x in (-1.5,0,1.5):box((x,0,-.30),(.12,3.6,.15),'Blue',.02)
export('CeilingCoffer')

(ART/'palette.json').write_text(json.dumps(dict(materials=palette),indent=2)+'\n')
(ART/'models.json').write_text(json.dumps(exports,indent=2)+'\n')
# Library write preserves other unsaved user scenes and excludes them from this asset.
bpy.data.libraries.write(str(SOURCE/'HoleInWallStudio.blend'),{scene},fake_user=True)
# Display the portal as a useful model preview; all exports retain origin pivots.
for obj in scene.objects: obj.hide_set(obj.name!='HS_Portal')
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        area.spaces.active.region_3d.view_distance=15
        area.spaces.active.region_3d.view_location=(0,0,3.5)
print(json.dumps(exports))
