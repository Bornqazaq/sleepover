"""Hub billiards table — open apron, turned legs, recessed felt, pocket cups.
Metres. FBX -Z/Y for Unity. Run: python3 tools/blender_client.py execute_code --file tools/blender/hub_billiards_table.py
"""
import bpy
import bmesh
import math
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'igruha/Assets/_Project/Art/Hub/Billiards'
SOURCE = ROOT / 'igruha/Assets/_Project/Art/Source/HubBilliards'
for p in (ART / 'Models', ART / 'Textures', SOURCE):
    p.mkdir(parents=True, exist_ok=True)

NAME = 'Hub_Billiards_Table'
previous = bpy.context.window.scene
if previous.name == NAME:
    previous = next(s for s in bpy.data.scenes if s.name != NAME)
    bpy.context.window.scene = previous
old = bpy.data.scenes.get(NAME)
if old:
    for o in list(old.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.scenes.remove(old)
scene = bpy.data.scenes.new(NAME)
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'

OUTER_W, OUTER_L = 1.62, 2.92
CLOTH_W, CLOTH_L = 1.23, 2.51
CLOTH_Y = 0.95
RAIL_H = 0.06
APRON_H = 0.16
LEG_TOP = CLOTH_Y - 0.08
POCKET_R = 0.055

materials = {}
for name, col, rough, metal in [
    ('Walnut', (0.20, 0.105, 0.055), 0.42, 0.0),
    ('Oak', (0.45, 0.30, 0.15), 0.38, 0.0),
    ('Felt', (0.14, 0.42, 0.24), 0.94, 0.0),
    ('Ink', (0.03, 0.03, 0.035), 0.60, 0.0),
    ('Brass', (0.68, 0.48, 0.18), 0.25, 0.7),
]:
    m = bpy.data.materials.get('HB_' + name) or bpy.data.materials.new('HB_' + name)
    m.use_nodes = True
    n = m.node_tree.nodes.get('Principled BSDF')
    n.inputs['Base Color'].default_value = (*col, 1)
    n.inputs['Roughness'].default_value = rough
    n.inputs['Metallic'].default_value = metal
    materials[name] = m


def clean_mesh(name):
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    return obj


def box(name, cx, cy, cz, sx, sy, sz, mat, bevel=0.0):
    obj = clean_mesh(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.transform(bm, matrix=Matrix.Diagonal((sx, sy, sz, 1.0)), verts=bm.verts)
    bmesh.ops.translate(bm, verts=bm.verts, vec=Vector((cx, cy, cz)))
    if bevel > 0.001:
        bm.edges.ensure_lookup_table()
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=2, affect='EDGES')
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.materials.append(materials[mat])
    return obj


def turned_leg(name, cx, cy):
    profile = [
        (0.034, 0.0), (0.040, 0.025), (0.028, 0.07), (0.032, 0.18),
        (0.024, 0.34), (0.036, 0.52), (0.040, 0.68), (0.032, LEG_TOP - 0.03),
        (0.046, LEG_TOP),
    ]
    obj = clean_mesh(name)
    bm = bmesh.new()
    segs = 20
    rings = []
    for r, z in profile:
        ring = [bm.verts.new((cx + r * math.cos(i * math.tau / segs),
                              cy + r * math.sin(i * math.tau / segs), z))
                for i in range(segs)]
        rings.append(ring)
    bm.verts.ensure_lookup_table()
    for j in range(len(rings) - 1):
        for i in range(segs):
            bm.faces.new((rings[j][i], rings[j][(i + 1) % segs],
                          rings[j + 1][(i + 1) % segs], rings[j + 1][i]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.materials.append(materials['Walnut'])
    return obj


def pocket_cup(name, cx, cy, z):
    obj = clean_mesh(name)
    bm = bmesh.new()
    segs = 24
    layers = [
        (POCKET_R * 1.15, z + 0.015),
        (POCKET_R * 1.05, z - 0.01),
        (POCKET_R * 0.70, z - 0.06),
        (POCKET_R * 0.40, z - 0.12),
    ]
    rings = []
    for r, zz in layers:
        rings.append([bm.verts.new((cx + r * math.cos(i * math.tau / segs),
                                    cy + r * math.sin(i * math.tau / segs), zz))
                      for i in range(segs)])
    bm.verts.ensure_lookup_table()
    for j in range(len(rings) - 1):
        for i in range(segs):
            bm.faces.new((rings[j][i], rings[j][(i + 1) % segs],
                          rings[j + 1][(i + 1) % segs], rings[j + 1][i]))
    bottom = bm.verts.new((cx, cy, layers[-1][1] - 0.01))
    for i in range(segs):
        bm.faces.new((rings[-1][i], rings[-1][(i + 1) % segs], bottom))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.materials.append(materials['Ink'])
    return obj


def cloth_with_notches(name):
    """Сукно с вырезами под лузы — не сплошной прямоугольник."""
    obj = clean_mesh(name)
    bm = bmesh.new()
    hw, hl = CLOTH_W * 0.5, CLOTH_L * 0.5
    z = CLOTH_Y
    # Outer rectangle, then punch circles near pocket centres via dissolve of nearby faces —
    # simpler: build as grid and delete verts inside pocket radius.
    nx, ny = 28, 48
    verts = [[None] * (ny + 1) for _ in range(nx + 1)]
    pockets = [
        (-hw, -hl), (hw, -hl), (-hw, hl), (hw, hl), (-hw, 0.0), (hw, 0.0),
    ]

    def in_pocket(x, y):
        for px, py in pockets:
            if (x - px) ** 2 + (y - py) ** 2 < (POCKET_R * 0.95) ** 2:
                return True
        return False

    for i in range(nx + 1):
        for j in range(ny + 1):
            x = -hw + CLOTH_W * i / nx
            y = -hl + CLOTH_L * j / ny
            if in_pocket(x, y):
                continue
            verts[i][j] = bm.verts.new((x, y, z))
    bm.verts.ensure_lookup_table()
    for i in range(nx):
        for j in range(ny):
            a, b = verts[i][j], verts[i + 1][j]
            c, d = verts[i + 1][j + 1], verts[i][j + 1]
            if a and b and c and d:
                bm.faces.new((a, b, c, d))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.materials.append(materials['Felt'])
    return obj


parts = []
half_w, half_l = OUTER_W * 0.5, OUTER_L * 0.5
cloth_hw, cloth_hl = CLOTH_W * 0.5, CLOTH_L * 0.5
rail_w = (OUTER_W - CLOTH_W) * 0.5 * 0.92

# Thin bed under cloth only — NOT a solid box to the floor.
parts.append(box('Bed', 0, 0, CLOTH_Y - 0.04, CLOTH_W + 0.08, CLOTH_L + 0.08, 0.045, 'Oak', bevel=0.008))
parts.append(cloth_with_notches('Cloth'))

rail_y = CLOTH_Y + RAIL_H * 0.45
short_len = CLOTH_W - POCKET_R * 2.1
long_seg = (CLOTH_L - POCKET_R * 2.2) * 0.5
long_off = cloth_hl * 0.5 + POCKET_R * 0.02

for sx, tag in ((-1, 'W'), (1, 'E')):
    x = sx * (cloth_hw + rail_w * 0.5)
    parts.append(box(f'Rail_{tag}_S', x, -long_off, rail_y, rail_w, long_seg, RAIL_H, 'Walnut', 0.006))
    parts.append(box(f'Rail_{tag}_N', x, long_off, rail_y, rail_w, long_seg, RAIL_H, 'Walnut', 0.006))
for sy, tag in ((-1, 'S'), (1, 'N')):
    y = sy * (cloth_hl + rail_w * 0.5)
    parts.append(box(f'Rail_{tag}', 0, y, rail_y, short_len, rail_w, RAIL_H, 'Walnut', 0.006))

# Soft cushion strip
cw = 0.026
for sx, tag in ((-1, 'W'), (1, 'E')):
    x = sx * (cloth_hw + cw * 0.45)
    parts.append(box(f'Cushion_{tag}_S', x, -long_off, CLOTH_Y + 0.014, cw, long_seg * 0.96, 0.028, 'Oak', 0.004))
    parts.append(box(f'Cushion_{tag}_N', x, long_off, CLOTH_Y + 0.014, cw, long_seg * 0.96, 0.028, 'Oak', 0.004))
for sy, tag in ((-1, 'S'), (1, 'N')):
    y = sy * (cloth_hl + cw * 0.45)
    parts.append(box(f'Cushion_{tag}', 0, y, CLOTH_Y + 0.014, short_len * 0.96, cw, 0.028, 'Oak', 0.004))

# Open apron — thin skirts, you see through under the table
apron_z = LEG_TOP - APRON_H * 0.35
parts.append(box('Apron_W', -(half_w - 0.03), 0, apron_z, 0.035, OUTER_L - 0.28, APRON_H, 'Walnut', 0.004))
parts.append(box('Apron_E', (half_w - 0.03), 0, apron_z, 0.035, OUTER_L - 0.28, APRON_H, 'Walnut', 0.004))
parts.append(box('Apron_S', 0, -(half_l - 0.03), apron_z, OUTER_W - 0.28, 0.035, APRON_H, 'Walnut', 0.004))
parts.append(box('Apron_N', 0, (half_l - 0.03), apron_z, OUTER_W - 0.28, 0.035, APRON_H, 'Walnut', 0.004))

# Cross stretcher between legs — reads as furniture, not a crate
parts.append(box('Stretcher_X', 0, 0, 0.18, OUTER_W - 0.30, 0.04, 0.04, 'Walnut', 0.003))
parts.append(box('Stretcher_Y', 0, 0, 0.18, 0.04, OUTER_L - 0.30, 0.04, 'Walnut', 0.003))

leg_x = half_w - 0.13
leg_y = half_l - 0.13
for i, (lx, ly) in enumerate(((leg_x, leg_y), (-leg_x, leg_y), (leg_x, -leg_y), (-leg_x, -leg_y))):
    parts.append(turned_leg(f'Leg_{i}', lx, ly))

pockets = [
    (-cloth_hw, -cloth_hl), (cloth_hw, -cloth_hl),
    (-cloth_hw, cloth_hl), (cloth_hw, cloth_hl),
    (-cloth_hw, 0.0), (cloth_hw, 0.0),
]
for i, (px, py) in enumerate(pockets):
    parts.append(pocket_cup(f'Pocket_{i}', px, py, CLOTH_Y))
    sx = 0.012
    # Brass sights on rail tops near pockets
    ox = px * 0.78 if abs(px) > 0.01 else 0.0
    oy = py * 0.78 if abs(py) > 0.01 else (0.0 if abs(px) > 0.01 else 0.0)
    if abs(py) < 0.01:
        oy = 0.0
        ox = px * 0.88
    parts.append(box(f'Sight_{i}', ox, oy, CLOTH_Y + RAIL_H * 0.55, sx, sx, 0.012, 'Brass', 0.002))

root = bpy.data.objects.new('HubBilliardsTable', None)
scene.collection.objects.link(root)
for p in parts:
    p.parent = root

bpy.ops.object.select_all(action='DESELECT')
root.select_set(True)
for p in parts:
    p.select_set(True)
bpy.context.view_layer.objects.active = root
fbx = str(ART / 'Models' / 'HubBilliardsTable.fbx')
bpy.ops.export_scene.fbx(
    filepath=fbx, use_selection=True, object_types={'MESH', 'EMPTY'},
    axis_forward='-Z', axis_up='Y', global_scale=1, apply_unit_scale=True,
    bake_anim=False, add_leaf_bones=False, use_mesh_modifiers=True,
)
bpy.data.libraries.write(str(SOURCE / 'HubBilliards.blend'), {scene},
                         fake_user=True, compress=True, path_remap='RELATIVE')
print('Exported', fbx, 'parts', len(parts))
bpy.context.window.scene = previous
