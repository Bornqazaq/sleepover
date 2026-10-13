"""IGR-754: standalone, unrigged Bruno for Meshy Quadruped.

Execute via Blender MCP in the live editor. Builds a separate scene without
touching the production bear. Metres, +Y muzzle, +Z up; glTF exports Y-up.
No coat geometry, old weights, armature, animation, or external textures.
"""
import bpy
import bmesh
import json
import math
from pathlib import Path
from mathutils import Vector, Quaternion

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'art-source/BrunoMeshy'
OUT.mkdir(parents=True, exist_ok=True)
DESKTOP = Path.home() / 'Desktop'
SCENE_NAME = 'Bruno_Meshy_2026_10'


def enum(operator, prop, choice):
    values = [i.identifier for i in operator.get_rna_type().properties[prop].enum_items]
    if choice not in values:
        raise ValueError((prop, choice, values))
    return choice


def activate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def material(name, color, roughness=.75):
    m = bpy.data.materials.new('BM_' + name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Roughness'].default_value = roughness
    return m


def mesh(name, verts, faces, mat):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.materials.append(mat)
    data.update()
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    bm = bmesh.new()
    bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(data)
    bm.free()
    for p in data.polygons:
        p.use_smooth = True
    return obj


def oval(name, pos, scale, mat, segments=32, rings=20):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=pos)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(mat)
    for p in obj.data.polygons:
        p.use_smooth = True
    return obj


def tube(name, points, radii, mat, sides=12):
    verts, faces = [], []
    for i, p in enumerate(points):
        tangent = Vector(points[min(i + 1, len(points) - 1)]) - Vector(points[max(0, i - 1)])
        tangent.normalize()
        u = tangent.cross(Vector((1, 0, 0)))
        if u.length < .01:
            u = tangent.cross(Vector((0, 0, 1)))
        u.normalize()
        v = tangent.cross(u).normalized()
        for j in range(sides):
            a = j * math.tau / sides
            verts.append(Vector(p) + radii[i] * (u * math.cos(a) + v * math.sin(a)))
        if i:
            for j in range(sides):
                faces.append(((i-1)*sides+j, (i-1)*sides+(j+1)%sides,
                              i*sides+(j+1)%sides, i*sides+j))
    faces.extend([tuple(reversed(range(sides))),
                  tuple((len(points)-1)*sides+j for j in range(sides))])
    return mesh(name, verts, faces, mat)


def limb(name, x, sections, mat):
    # Horizontal rings make the sculpt's joint centres explicit. Catmull-Rom
    # interpolation keeps a smooth contour without swollen overlapping balls.
    rows = []
    for i in range(len(sections)-1):
        p0 = Vector(sections[max(0, i-1)])
        p1 = Vector(sections[i])
        p2 = Vector(sections[i+1])
        p3 = Vector(sections[min(i+2, len(sections)-1)])
        for j in range(4):
            t = j/4
            rows.append(.5 * ((2*p1) + (-p0+p2)*t + (2*p0-5*p1+4*p2-p3)*t*t
                               + (-p0+3*p1-3*p2+p3)*t*t*t))
    rows.append(Vector(sections[-1]))
    verts, faces, sides = [], [], 24
    for i, (z, y, rx, ry) in enumerate(rows):
        inset = min(1.0, max(0.0, (z-1.15)/.65))
        cx = x * (1-.47*inset*inset)
        for j in range(sides):
            a = j/sides*math.tau
            verts.append((cx+rx*math.cos(a), y+ry*math.sin(a), z))
        if i:
            for j in range(sides):
                faces.append(((i-1)*sides+j, (i-1)*sides+(j+1)%sides,
                              i*sides+(j+1)%sides, i*sides+j))
    faces.extend([tuple(reversed(range(sides))), tuple((len(rows)-1)*sides+j for j in range(sides))])
    return mesh(name, verts, faces, mat)


def join(objects, name):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return obj


# Only our own rebuild scene can be removed; the artist's original stays alive.
old = bpy.data.scenes.get(SCENE_NAME)
if old:
    for obj in list(old.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.scenes.remove(old)
scene = bpy.data.scenes.new(SCENE_NAME)
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
body_mat = material('Body_umber', (.185, .094, .046))
muzzle_mat = material('Muzzle_warm', (.30, .177, .087))
dark_mat = material('Paws_and_lips', (.052, .025, .014))
nose_mat = material('Nose', (.014, .009, .006), .33)
mouth_mat = material('Mouth', (.047, .008, .009), .65)
gum_mat = material('Gums', (.20, .045, .045), .6)
ivory_mat = material('Teeth_ivory', (.68, .57, .36), .35)
claw_mat = material('Claws_horn', (.16, .095, .040), .36)
eye_mat = material('Eyes_dark', (.009, .005, .003), .19)
iris_mat = material('Iris_amber', (.28, .105, .016), .26)
parts = []

# Retain Bruno's low head and distinctive grizzly shoulder hump. A tucked
# belly and shorter throat expose the axilla and shoulder/neck transition.
for name, pos, scale in [
    ('Ribcage', (0,-.10,1.32), (.55,.91,.51)),
    ('Rump', (0,-.91,1.27), (.51,.54,.48)),
    ('ShoulderHump', (0,.30,1.63), (.55,.68,.49)),
    ('Withers', (0,.43,1.78), (.36,.44,.30)),
    ('Chest', (0,.59,1.40), (.39,.35,.38)),
    ('Neck', (0,.96,1.55), (.375,.59,.33)),
    ('Throat', (0,1.12,1.40), (.265,.36,.22)),
    ('Skull', (0,1.40,1.63), (.345,.43,.30)),
    ('Brow', (0,1.55,1.76), (.30,.26,.13)),
    ('NasalBridge', (0,1.76,1.58), (.222,.31,.14)),
    ('Snout', (0,1.91,1.50), (.234,.26,.145)),
    ('TailRoot', (0,-1.41,1.38), (.12,.15,.105)),
    ('TailTip', (0,-1.55,1.39), (.095,.13,.085)),
]:
    parts.append(oval(name, pos, scale, body_mat))

for sign in (-1, 1):
    parts.append(oval('Cheek', (sign*.215,1.40,1.52), (.14,.26,.19), body_mat))
    parts.append(oval('HeavyBrow', (sign*.231,1.598,1.783), (.088,.12,.043), body_mat))
    parts.append(oval('EarRoot', (sign*.272,1.23,1.84), (.106,.135,.10), body_mat))
    x = sign*.53
    parts.append(limb('ForeLeg', x, [
        (1.90,.34,.11,.16), (1.73,.36,.195,.235),
        (1.48,.40,.235,.28), (1.24,.32,.215,.24),
        (1.03,.22,.172,.19), (.91,.20,.147,.16),
        (.80,.27,.165,.182), (.63,.34,.151,.17),
        (.46,.42,.118,.14), (.34,.45,.105,.119),
        (.25,.47,.122,.143), (.13,.54,.165,.205), (.07,.56,.11,.13),
    ], body_mat))
    parts.append(limb('HindLeg', x, [
        (1.68,-.92,.11,.18), (1.53,-.92,.205,.26),
        (1.39,-.91,.265,.32), (1.17,-.85,.248,.295),
        (.98,-.73,.195,.223), (.84,-.65,.159,.173),
        (.72,-.74,.155,.165), (.57,-.88,.133,.148),
        (.40,-1.01,.101,.12), (.32,-1.005,.103,.12),
        (.22,-.96,.126,.14), (.10,-.90,.15,.16), (.07,-.85,.10,.11),
    ], body_mat))
    for prefix, y in [('Fore',.63), ('Hind',-.79)]:
        parts.append(oval(prefix+'Palm', (x,y,.142), (.224,.284,.134), body_mat))
        for i in range(5):
            dx = (i-2)*.082
            edge = abs(i-2)/2
            parts.append(oval(prefix+'Toe', (x+dx,y+.193-edge*.035,.115),
                              (.054,.116,.088-edge*.008), body_mat, 20, 12))

skin = join(parts, 'Bruno_ContinuousBody')
remesh = skin.modifiers.new('Continuous sculpt', 'REMESH')
remesh.mode = next(i.identifier for i in bpy.types.RemeshModifier.bl_rna.properties['mode'].enum_items if i.identifier == 'VOXEL')
remesh.voxel_size = .016
remesh.use_smooth_shade = True
bpy.ops.object.modifier_apply(modifier=remesh.name)
smooth = skin.modifiers.new('Relax sculpt', 'SMOOTH')
smooth.factor, smooth.iterations = .58, 3
bpy.ops.object.modifier_apply(modifier=smooth.name)
skin.data.use_mirror_x = True
skin.data.use_mirror_y = False
skin.data.use_mirror_z = False
# Deterministic quad retopology distributes bending geometry over all limbs.
bpy.ops.object.quadriflow_remesh(target_faces=11000, use_mesh_symmetry=True,
    use_preserve_sharp=False, use_preserve_boundary=False, smooth_normals=True, seed=754)
bm = bmesh.new()
bm.from_mesh(skin.data)
# QuadriFlow symmetry emits coincident boundary vertices at X=0 in Blender 5.2.
bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.00002)
bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
bm.to_mesh(skin.data)
bm.free()
smooth = skin.modifiers.new('Retopology relaxation', 'SMOOTH')
smooth.factor, smooth.iterations = .30, 2
bpy.ops.object.modifier_apply(modifier=smooth.name)
skin.data.materials.clear()
skin.data.materials.append(body_mat)
for p in skin.data.polygons:
    p.use_smooth = True

# Small recessed eye sockets cut into the brow, with the eye globe embedded.
for sign in (-1,1):
    cutter = oval('SocketCutter', (sign*.270,1.645,1.735), (.037,.042,.029), body_mat)
    activate(skin)
    cut = skin.modifiers.new('Eye socket', 'BOOLEAN')
    cut.operation = 'DIFFERENCE'
    cut.object = cutter
    bpy.ops.object.modifier_apply(modifier=cut.name)
    bpy.data.objects.remove(cutter, do_unlink=True)

# Vertex colour gradients survive glTF without textures or polygonal masks.
skin_mat = material('Body_vertex_colour', (1,1,1))
attr = skin_mat.node_tree.nodes.new('ShaderNodeVertexColor')
attr.layer_name = 'Color'
bsdf = next(n for n in skin_mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
skin_mat.node_tree.links.new(attr.outputs['Color'], bsdf.inputs['Base Color'])
skin.data.materials.clear()
skin.data.materials.append(skin_mat)
color_layer = skin.data.color_attributes.new(name='Color', type='FLOAT_COLOR', domain='POINT')
def smoothstep(a,b,value):
    t = max(0,min(1,(value-a)/(b-a)))
    return t*t*(3-2*t)
for v, col in zip(skin.data.vertices,color_layer.data):
    p=v.co
    paw = 1-smoothstep(.13,.38,p.z)
    muzzle = smoothstep(1.61,1.96,p.y-abs(p.x)*.36)*(1-smoothstep(1.60,1.77,p.z))
    base = Vector(body_mat.diffuse_color[:3])
    base = base.lerp(Vector(dark_mat.diffuse_color[:3]),paw*.80)
    base = base.lerp(Vector(muzzle_mat.diffuse_color[:3]),muzzle)
    col.color=(*base,1)

details = []
for sign in (-1,1):
    center = Vector((sign*.276,1.235,1.902))
    rotation = Quaternion(Vector((0,0,1)), -sign*math.radians(26)) @ Quaternion(Vector((1,0,0)),math.radians(14))
    verts, faces = [], []
    n = 24
    for radius, depth in ((.025,-.042),(.70,-.039),(1,-.005),(.86,.025),(.56,.009),(.025,-.016)):
        for i in range(n):
            a = i/n*math.tau
            verts.append(center + rotation @ Vector((math.cos(a)*.108*radius, depth, math.sin(a)*.115*radius)))
        ring = len(verts)//n-1
        if ring:
            for i in range(n):
                faces.append(((ring-1)*n+i,(ring-1)*n+(i+1)%n,ring*n+(i+1)%n,ring*n+i))
    faces.extend([tuple(reversed(range(n))),tuple(5*n+i for i in range(n))])
    ear = mesh('RoundedEar', verts, faces, body_mat)
    ear.data.materials.append(dark_mat)
    for p in ear.data.polygons:
        if p.index >= n*3:
            p.material_index = 1
    details.append(ear)
    ex, ey, ez = sign*.263, 1.636, 1.735
    # Outward-forward gaze. Iris/pupil discs follow the surface normal.
    normal = Vector((sign*.70,.71,.05)).normalized()
    eye = oval('Eye', (ex,ey,ez), (.027,.020,.019), eye_mat, 24,16)
    eye.rotation_mode = 'QUATERNION'
    eye.rotation_quaternion = Vector((0,1,0)).rotation_difference(normal)
    details.append(eye)
    for name, offset, scl, mat in [('AmberIris',.018,(.012,.003,.012),iris_mat),
                                  ('RoundPupil',.020,(.007,.002,.009),nose_mat)]:
        ob = oval(name, Vector((ex,ey,ez))+normal*offset, scl, mat,20,12)
        ob.rotation_mode = 'QUATERNION'
        ob.rotation_quaternion = Vector((0,1,0)).rotation_difference(normal)
        details.append(ob)
    tangent = Vector((sign*.71,-.70,0)).normalized()
    origin = Vector((ex,ey,ez))+normal*.010
    for upper,mat,r in [(True,body_mat,.007),(False,dark_mat,.004)]:
        points=[]
        for i in range(9):
            a=i/8*math.pi
            points.append(origin+tangent*(.026*math.cos(a))+
                Vector((0,0,(.017 if upper else -.016)*math.sin(a)+.003*math.cos(a))))
        details.append(tube('UpperLid' if upper else 'LowerLid',points,[r]*len(points),mat))

nose = oval('BroadNose', (0,2.128,1.538), (.159,.079,.083), nose_mat)
for v in nose.data.vertices:
    if v.co.y > .037:
        v.co.y = .037+(v.co.y-.037)*.36
    # Subtle central taper instead of a spherical nose bead.
    v.co.x *= .83+.17*(v.co.z/.083+1)/2
details.append(nose)
for sign in (-1,1):
    cutter = oval('NostrilCutter',(sign*.098,2.186,1.542),(.030,.031,.020),nose_mat,20,12)
    activate(nose)
    boolean = nose.modifiers.new('True recessed nostril','BOOLEAN')
    boolean.operation, boolean.object = 'DIFFERENCE', cutter
    bpy.ops.object.modifier_apply(modifier=boolean.name)
    bpy.data.objects.remove(cutter,do_unlink=True)
    details.append(oval('NostrilDark',(sign*.098,2.160,1.542),(.021,.009,.014),eye_mat,16,10))
details.append(tube('Philtrum',[(0,2.177,1.507),(0,2.153,1.445),(0,2.133,1.405)],[.008,.009,.005],nose_mat))

# A restrained open mouth shows the canines without forcing a roar rest pose.
details.append(oval('MouthCavity',(0,1.839,1.335),(.184,.249,.085),mouth_mat))
details.append(oval('LowerJaw',(0,1.798,1.263),(.206,.300,.075),muzzle_mat))
details.append(oval('Tongue',(0,1.911,1.326),(.100,.132,.013),gum_mat,24,12))
upper = [(.207*math.cos(i/24*math.pi),1.846+.270*math.sin(i/24*math.pi),
          1.375+.021*abs(math.cos(i/24*math.pi))) for i in range(25)]
lower = [(.189*math.cos(i/24*math.pi),1.810+.270*math.sin(i/24*math.pi),
          1.306-.009*math.sin(i/24*math.pi)) for i in range(25)]
details.append(tube('UpperLip',upper,[.010]*25,dark_mat))
details.append(tube('LowerLip',lower,[.009]*25,dark_mat))
for sign in (-1,1):
    details.append(tube('UpperCanine',[(sign*.176,1.963,1.414),
        (sign*.177,1.980,1.382),(sign*.169,1.993,1.325),(sign*.153,1.984,1.280)],
        [.028,.026,.015,.0015],ivory_mat,12))
    details.append(tube('LowerCanine',[(sign*.171,1.874,1.310),
        (sign*.166,1.892,1.335),(sign*.156,1.913,1.376)], [.021,.016,.0015],ivory_mat,12))
    for i in range(2):
        y = 1.81+i*.055
        details.append(tube('Premolar',[(sign*.164,y,1.382),(sign*.166,y+.007,1.357),
                                      (sign*.163,y+.012,1.348)],[.012,.010,.002],ivory_mat,8))
for i in range(6):
    x=(i-2.5)*.029
    y=2.102-abs(x)*.10
    details.append(tube('Incisor',[(x,y,1.401),(x,y+.003,1.377),(x,y+.001,1.369)],
                        [.013,.011,.006],ivory_mat,8))

for sign in (-1,1):
    x = sign*.53
    for prefix,y,length in [('Fore',.63,.157),('Hind',-.79,.117)]:
        for i in range(5):
            edge=abs(i-2)/2
            px=x+(i-2)*.082
            py=y+.280-edge*.035
            # Horn wedges taper in both axes, root embedded into each toe.
            details.append(tube(prefix+'Claw',[(px,py-.020,.139-edge*.006),
                (px,py+.024,.145-edge*.006),(px,py+length*.58,.119),
                (px,py+length*.88,.076),(px,py+length,.053)],
                [.024,.025,.016,.008,.0012],claw_mat,10))

for obj in details:
    colors = obj.data.color_attributes.new(name='Color', type='FLOAT_COLOR', domain='POINT')
    for col in colors.data:
        col.color=(1,1,1,1)
body = join([skin]+details, 'Bruno_Meshy_BodyOnly')
# All four soles share a single plane; claws stay above it.
for v in body.data.vertices:
    if v.co.z < .04:
        v.co.z = .012
    v.co.z -= .012
bm = bmesh.new()
bm.from_mesh(body.data)
bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
bm.to_mesh(body.data)
bm.free()
body['asset_purpose'] = 'Meshy Quadruped upload. Unrigged body with claws/eyes/teeth; no fur.'
body['source_axes'] = 'metres; +Y forward; Z up; symmetric X'
body['mesh_version'] = 'IGR-754 v2'
activate(body)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(island_margin=.018)
bpy.ops.object.mode_set(mode='OBJECT')
print('BODY_BUILT',len(body.data.vertices),'vertices',len(body.data.polygons),'faces')

# Marker coordinates are joint centres, not the visible outline. The guide
# uses their side projection; F/H follows Meshy's supplied reference labels.
markers = [
    ('chin','Подбородок',(0,1.47,1.47),'#51e9fb',''),
    ('shoulder','Плечи',(.53,.40,1.49),'#ffea3b',''),
    ('fore_elbow','Предплечье',(.53,.20,.91),'#f247d6','F'),
    ('fore_wrist','Пастерн',(.53,.45,.34),'#ff8e35','F'),
    ('fore_foot','Рука',(.53,.75,.14),'#bff542','F'),
    ('waist','Талия',(0,-.76,1.49),'#ffffff',''),
    ('hip','Ягодицы',(.53,-.97,1.34),'#438aff',''),
    ('hind_knee','Шин',(.53,-.65,.84),'#f247d6','H'),
    ('hind_hock','Крюк',(.53,-1.01,.40),'#ff8e35','H'),
    ('hind_foot','Ступня',(.53,-.73,.14),'#bff542','H'),
    ('tail','Хвост',(0,-1.56,1.39),'#69ff82',''),
]
markers = [(key,label,(p[0],p[1],p[2]-.012),color,letter)
           for key,label,p,color,letter in markers]
(OUT/'markers.json').write_text(json.dumps(markers,ensure_ascii=False,indent=2))

# Studio belongs only to the source scene, never to GLB selection.
floor_mat=material('Studio',(.050,.063,.076),.9)
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,0))
floor=bpy.context.object
floor.name='PreviewGround_ONLY'
floor.data.materials.append(floor_mat)
world=bpy.data.worlds.new('BM_StudioWorld')
scene.world=world
world.use_nodes=True
bg=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND')
bg.inputs['Color'].default_value=(.12,.15,.20,1)
bg.inputs['Strength'].default_value=.45
for name,loc,power,size,color in [
    ('Key',(-3,4,6),950,4.0,(1,.82,.63)),
    ('Fill',(4,4,3),700,3.5,(.70,.84,1)),
    ('Rim',(2,-4,5),1100,3.0,(1,.90,.77)),
]:
    data=bpy.data.lights.new('BM_'+name,'AREA')
    data.energy=power
    data.shape='DISK'
    data.size=size
    data.color=color
    ob=bpy.data.objects.new('BM_'+name,data)
    scene.collection.objects.link(ob)
    ob.location=loc
    ob.rotation_euler=(Vector((0,.2,1.0))-ob.location).to_track_quat('-Z','Y').to_euler()
camdata=bpy.data.cameras.new('BM_Camera')
cam=bpy.data.objects.new('BM_Camera',camdata)
scene.collection.objects.link(cam)
scene.camera=cam
cam.location=(5,7,3.0)
cam.rotation_euler=(Vector((0,.25,1.04))-cam.location).to_track_quat('-Z','Y').to_euler()
camdata.type='ORTHO'
camdata.ortho_scale=4.9
scene.render.engine='CYCLES'
scene.cycles.samples=32
scene.cycles.use_denoising=True
scene.render.resolution_x=1600
scene.render.resolution_y=1100
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.view_settings.view_transform='AgX'
activate(body)
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        area.spaces.active.region_3d.view_perspective='CAMERA'
        area.spaces.active.shading.type='MATERIAL'
print('SCENE_READY',scene.name)
