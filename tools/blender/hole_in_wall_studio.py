"""Original sunlit aquatic pavilion modules. Execute through Blender MCP.

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

material('Ink', (.024,.115,.13), .65)
material('Blue', (.055,.32,.34), .40)
material('Teal', (.025,.60,.64), .35)
material('Coral', (.92,.26,.17), .5)
material('Ivory', (.96,.88,.70), .4)
material('Gold', (.73,.43,.16), .4, .4)
material('Steel', (.23,.32,.40), .32, .7)
material('Glow', (.24,.67,.61), .35, 0, .2)
material('WarmGlow', (1,.76,.38), .35, 0, .4)
material('Skin', (.72,.40,.25), .8)
material('Hair', (.065,.032,.025), .85)
material('White', (.97,.97,.88), .65)

material('Mint', (.25,.57,.49), .45)
material('Plaster', (.92,.68,.41), .85)
material('Tile', (.82,.85,.70), .4)
material('Glass', (.46,.79,.91), .5, 0, .55)
material('Sun', (1,.55,.12), .48)
material('Leaf', (.055,.27,.13), .78)
material('LeafLight', (.25,.49,.15), .7)
material('Soil', (.10,.064,.04), 1)
material('Cardboard', (.72,.50,.27), .82)

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

# Sunlit aquatic pavilion. New architecture, rather than a dressing over the stage.
# Mesh strips keep sweeping curves light enough to repeat around the hall.
def ribbon(points,width,depth,mat):
    verts=[]
    for i,p in enumerate(points):
        p=Vector(p); tangent=Vector(points[min(i+1,len(points)-1)])-Vector(points[max(i-1,0)])
        side=Vector((-tangent.z,0,tangent.x)).normalized()*width*.5
        verts.extend([p-side+Vector((0,-depth*.5,0)),p+side+Vector((0,-depth*.5,0)),p-side+Vector((0,depth*.5,0)),p+side+Vector((0,depth*.5,0))])
    faces=[]
    for i in range(len(points)-1):
        a=i*4;b=a+4
        faces.extend([(a,b,b+1,a+1),(a+2,a+3,b+3,b+2),(a,a+2,b+2,b),(a+1,b+1,b+3,a+3)])
    faces.extend([(0,1,3,2),(len(verts)-4,len(verts)-2,len(verts)-1,len(verts)-3)])
    mesh=bpy.data.meshes.new('Architectural sweep');mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new('Architectural sweep',mesh);scene.collection.objects.link(obj);finish(obj,mat)
    return obj

def arc(rx,rz,base=0,y=0,steps=48):
    return [(rx*math.cos(i*math.pi/steps),y,base+rz*math.sin(i*math.pi/steps)) for i in range(steps+1)]

# Large vaulted rib, measured from its springing line; six sections span the pool.
ribbon(arc(31,9),.42,.55,'Mint')
ribbon(arc(30.5,8.4,y=-.15),.14,.20,'Ivory')
for t in [math.pi*i/12 for i in range(1,12)]:
    tube((31*math.cos(t),0,9*math.sin(t)),(30.5*math.cos(t),0,8.4*math.sin(t)),.055,'Gold')
export('BathRoofRib')

# Arched glazed bay. Pale glass is opaque and softly luminous, no transparent overdraw.
pts=[(-2.4,0,0),(2.4,0,0)]+arc(2.4,2.4,5,0,32)
verts=[(0,.08,3.5)]+[(x,.08,z) for x,y,z in pts]
mesh=bpy.data.meshes.new('Glazed arch');mesh.from_pydata(verts,[],[(0,i+1,(i+1)%len(pts)+1) for i in range(len(pts))]);mesh.update()
obj=bpy.data.objects.new('Glazed arch',mesh);scene.collection.objects.link(obj);finish(obj,'Glass')
ribbon([(-2.5,0,0),(-2.5,0,5)]+list(reversed(arc(2.5,2.5,5)))+[(2.5,0,0)],.25,.30,'Ivory')
for x in (-1.2,0,1.2):box((x,-.10,3.1),(.055,.09,6.1),'Mint',.01)
for z in (2.4,4.8):box((0,-.10,z),(4.7,.10,.06),'Mint',.01)
box((0,-.05,-.13),(5.25,.65,.30),'Ivory',.06)
export('BathWindow')

# Rounded enamel game entrance: actual feet, vertical rails and a curved lintel.
pts=[(-4.75,0,0),(-4.75,0,5.2)]+list(reversed(arc(4.75,2,5.2)))+[(4.75,0,0)]
ribbon(pts,.64,.85,'Ivory')
ribbon([(x,y-.49,z) for x,y,z in pts],.18,.10,'Glow')
for x in (-4.75,4.75):
    box((x,0,.2),(1.1,1.3,.4),'Mint',.08)
    box((x,-.5,2.55),(.16,.12,4.45),'Gold',.03)
    for z in (1.1,2.5,3.9):tube((x,-.57,z),(x,-.63,z),.08,'Ivory')
export('BathGate')

# A tactile enamel score counter; existing text anchors remain measured in metres.
box((0,0,0),(5.3,.6,2.8),'Ivory',.27)
box((0,-.34,0),(5.02,.15,2.52),'Mint',.20)
box((.55,-.48,0),(3.65,.13,2.25),'Ink',.13)
tube((-1.88,-.48,.10),(-1.88,-.64,.10),.62,'Glow')
tube((-1.88,-.65,.10),(-1.88,-.69,.10),.52,'Ivory')
for x in (-2.45,2.45):
    for z in (-1.03,1.03):tube((x,-.43,z),(x,-.50,z),.045,'Gold')
for x in (-1.8,1.8):box((x,.05,1.68),(.10,.18,.74),'Gold',.02)
export('Scoreboard')

# A large wall-mounted sun medallion, no text billboard. Warm metal and sculpted waves.
tube((0,.18,0),(0,0,0),3.7,'Mint')
tube((0,-.02,0),(0,-.10,0),3.45,'Ivory')
tube((0,-.12,.45),(0,-.20,.45),1.30,'Sun')
for i in range(12):
    t=i*math.tau/12
    tube((1.63*math.sin(t),-.18,.45+1.63*math.cos(t)),(2.3*math.sin(t),-.18,.45+2.3*math.cos(t)),.11,'Sun')
for row in range(3):
    path([(-2.65+j*.166,-.31,-1.5-row*.40+.18*math.sin(j*.34)) for j in range(33)],.12,'Teal')
export('SunMedallion')

# Gallery balcony module. Ends are supported by the building's masonry pilasters.
for z in (0,1.10):box((0,0,z),(5.9,.12,.12),'Mint',.02)
for i in range(12):box((-2.75+i*.5,0,.55),(.075,.08,1.1),'Mint',.015)
box((0,0,1.2),(6,.22,.14),'Ivory',.045)
export('BathRailing')

# Potted palms have solid leaf ribbons, two-tone fronds and individual central veins.
tube((0,0,0),(0,0,.8),.7,'Coral')
tube((0,0,.80),(0,0,.96),.76,'Ivory')
tube((0,0,.97),(0,0,1.0),.62,'Soil')
path([(0,0,.9),(.08,.02,2),(.2,.02,3.1),(.38,0,4.1)],.14,'Gold')
for i in range(9):
    a=i*math.tau/9
    verts=[]
    for j in range(9):
        t=j/8;dist=t*2.7
        p=Vector((.38+math.cos(a)*dist,math.sin(a)*dist,4.1+1.05*math.sin(t*math.pi)-.65*t))
        side=Vector((-math.sin(a),math.cos(a),0))*(.32*math.sin(t*math.pi)+.015)
        verts.extend([p-side,p+Vector((0,0,.08)),p+side])
    faces=[]
    for j in range(8):
        for k in range(2):faces.append((j*3+k,(j+1)*3+k,(j+1)*3+k+1,j*3+k+1))
    mesh=bpy.data.meshes.new('Palm frond');mesh.from_pydata(verts,[],faces);mesh.update()
    ob=bpy.data.objects.new('Palm frond',mesh);scene.collection.objects.link(ob);finish(ob,'Leaf' if i%2 else 'LeafLight')
    mod=ob.modifiers.new('Leaf thickness','SOLIDIFY');mod.thickness=.025
    bpy.context.view_layer.objects.active=ob;bpy.ops.object.modifier_apply(modifier=mod.name)
    for j in range(1,8):
        t=j/8;dist=t*2.7
        p=Vector((.38+math.cos(a)*dist,math.sin(a)*dist,4.1+1.05*math.sin(t*math.pi)-.65*t))
        side=Vector((-math.sin(a),math.cos(a),0))*.30*math.sin(t*math.pi)
        tube(p+Vector((0,0,.09)),p+side+Vector((0,0,.02)),.016,'LeafLight')
export('PlanterPalm')

# Lifebuoy rack: poolside prop is visibly bolted to the deck.
box((0,0,.08),(.8,.7,.16),'Ivory',.08)
box((0,.08,1.05),(.11,.16,2.0),'Mint',.02)
for i in range(48):
    a=i*math.tau/48;b=(i+1)*math.tau/48
    tube((.58*math.sin(a),-.12,1.3+.58*math.cos(a)),(.58*math.sin(b),-.12,1.3+.58*math.cos(b)),.14,'Ivory' if i//6%2 else 'Coral')
export('Lifebuoy')

# One tile patch replaces hundreds of separate floor-trim objects.
for x in range(4):
    for y in range(4):box((x-1.5,y-1.5,0),(.977,.977,.06),'Tile' if (x+y)%2 else 'Ivory',.012)
export('TilePatch')

(ART/'palette.json').write_text(json.dumps(dict(materials=palette),indent=2)+'\n')
(ART/'models.json').write_text(json.dumps(exports,indent=2)+'\n')
# Library write preserves other unsaved user scenes and excludes them from this asset.
bpy.data.libraries.write(str(SOURCE/'HoleInWallStudio.blend'),{scene},fake_user=True)
# Display the rounded game gate; all exports retain origin pivots.
for obj in scene.objects: obj.hide_set(obj.name!='HS_BathGate')
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        area.spaces.active.region_3d.view_distance=15
        area.spaces.active.region_3d.view_location=(0,0,3.5)
print(json.dumps(exports))
