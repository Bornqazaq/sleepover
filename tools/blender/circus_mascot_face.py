"""Face corrections and expression shapes for the integrated circus mascot.

Called by circus_mascot_export.py on a copy of the original sandbox.
"""
import bpy
import math
from mathutils import Vector


def smooth(a, b, value):
    t = max(0, min(1, (value-a)/(b-a)))
    return t*t*(3-2*t)


def prepare_face(rig):
    # The previous lip was an open sweep with coincident, unwelded endpoints.
    # Use a periodic mesh so the corner stays continuous through every expression.
    lip = bpy.data.objects['Mouth lip rim']
    verts, faces = [], []
    rings, sides = 128, 16
    for i in range(rings):
        t = 2*math.pi*i/rings
        center = Vector((.80*math.cos(t), -1.14, 2.25+.235*math.sin(t)))
        outward = Vector((math.cos(t), 0, math.sin(t)))
        radius = .064+.016*max(0, math.sin(t))
        for j in range(sides):
            a = 2*math.pi*j/sides
            verts.append(center + radius*(outward*math.cos(a)+Vector((0,-1,0))*math.sin(a)))
            faces.append((i*sides+j, ((i+1)%rings)*sides+j,
                          ((i+1)%rings)*sides+(j+1)%sides, i*sides+(j+1)%sides))
    data = bpy.data.meshes.new('Continuous mouth rim')
    data.from_pydata(verts, [], faces); data.update()
    for material in lip.data.materials: data.materials.append(material)
    lip.data = data
    for face in data.polygons: face.use_smooth = True

    for ob in bpy.data.collections['01_CHARACTER'].objects:
        if ob.type != 'MESH': continue
        if ob.name == 'Lower lip and chin':
            for v in ob.data.vertices:
                v.co.x *= .94
                v.co.y = -.75+(v.co.y+.78)*.76
                v.co.z = 1.84+(v.co.z-1.87)*.67
        elif ob.name == 'Mouth cavity':
            for v in ob.data.vertices: v.co.y -= .055
        elif ob.name == 'Long hooked nose':
            for v in ob.data.vertices:
                tip = smooth(-.70, -1.76, v.co.y)
                v.co.x *= 1.35
                v.co.y = -.57+(v.co.y+.57)*.80
                v.co.z -= .10*tip
        elif ob.name.startswith('Upper tusk'):
            old_x = -.43 if '-0.43' in ob.name else .34
            for v in ob.data.vertices: v.co.x += (-.38 if old_x<0 else .38)-old_x
        if ob.name.startswith(('EyeWhite.', 'Iris', 'Pupil.', 'EyeGlint.', 'UpperLid.', 'Lower eye fold.')):
            offset = .02 if ob.name.endswith('.L') else -.02
            if ob.data.shape_keys:
                for shape in ob.data.shape_keys.key_blocks:
                    for v in shape.data: v.co.z += offset
            else:
                for v in ob.data.vertices: v.co.z += offset

        region = None
        if ob.name in ('Mouth lip rim', 'Mouth cavity'): region = 'F_Mouth'
        elif ob.name == 'Lower lip and chin': region = 'F_Chin'
        elif ob.name == 'Tongue inside mouth': region = 'F_Tongue'
        if region:
            # Blend shapes move the whole lip/cavity/chin together; an additional
            # jaw skin rotation would break their common boundary a second time.
            ob.vertex_groups.clear()
            ob.vertex_groups.new(name='Head').add(list(range(len(ob.data.vertices))), 1, 'REPLACE')
            ob.vertex_groups.new(name=region).add(list(range(len(ob.data.vertices))), 1, 'REPLACE')
        elif ob.name.startswith(('SpongeBody', 'Freckle', 'Heavy brow', 'Lower eye fold')):
            ob.vertex_groups.new(name='F_Cheek').add(list(range(len(ob.data.vertices))), 1, 'REPLACE')


def add_expressions(body):
    body.shape_key_add(name='Basis')
    smile = body.shape_key_add(name='LaughSmile')
    opened = body.shape_key_add(name='LaughOpen')
    masks = {g.index: g.name for g in body.vertex_groups if g.name.startswith('F_')}
    for v in body.data.vertices:
        tags = {masks[g.group] for g in v.groups if g.group in masks and g.weight>.1}
        x,y,z = v.co
        if tags.intersection({'F_Mouth','F_Chin','F_Tongue'}):
            smile.data[v.index].co.x *= 1.09
            smile.data[v.index].co.z += .13*min(1,abs(x)/.80)**2
            amount = 1 if tags.intersection({'F_Chin','F_Tongue'}) else 1-smooth(2.03,2.49,z)
            opened.data[v.index].co.z -= .34*amount
            opened.data[v.index].co.y -= .035*amount
        elif 'F_Cheek' in tags:
            weight = math.exp(-((abs(x)-.77)/.28)**2-((z-2.40)/.28)**2)*(1-smooth(-.4,-.05,y))
            smile.data[v.index].co.z += .075*weight
            smile.data[v.index].co.y -= .035*weight
    for g in list(body.vertex_groups):
        if g.name.startswith('F_'): body.vertex_groups.remove(g)
    return smile, opened
