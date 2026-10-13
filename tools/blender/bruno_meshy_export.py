"""Export and inspect IGR-754's separate Blender scene, through Blender MCP."""
import bpy
import bmesh
import json
import math
import shutil
import struct
from pathlib import Path
from mathutils import Vector
from mathutils.kdtree import KDTree
from bpy_extras.object_utils import world_to_camera_view

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art-source/BrunoMeshy'
DESKTOP=Path.home()/'Desktop'
scene=bpy.data.scenes['Bruno_Meshy_2026_10']
bpy.context.window.scene=scene
body=scene.objects['Bruno_Meshy_BodyOnly']
bpy.ops.object.select_all(action='DESELECT')
body.select_set(True)
bpy.context.view_layer.objects.active=body

bm=bmesh.new()
bm.from_mesh(body.data)
assert all(e.is_manifold for e in bm.edges), 'Open or non-manifold mesh'
assert all(f.calc_area()>1e-12 for f in bm.faces), 'Zero-area face'
assert all(v.link_edges for v in bm.verts), 'Loose vertices'
remaining=set(bm.verts)
components=[]
while remaining:
    start=remaining.pop()
    comp={start}
    stack=[start]
    while stack:
        v=stack.pop()
        for edge in v.link_edges:
            other=edge.other_vert(v)
            if other in remaining:
                remaining.remove(other)
                comp.add(other)
                stack.append(other)
    components.append(comp)
skin=max(components,key=len)
kd=KDTree(len(skin))
for i,v in enumerate(skin):
    kd.insert(v.co,i)
kd.balance()
symmetry=max(kd.find(Vector((-v.co.x,v.co.y,v.co.z)))[2] for v in skin)
assert symmetry < .001, symmetry
assert all(math.isfinite(c) for v in bm.verts for c in v.co)
body.data.calc_loop_triangles()
metrics={
    'vertices':len(body.data.vertices), 'triangles':len(body.data.loop_triangles),
    'closed_components':len(components), 'continuous_skin_vertices':len(skin),
    'nonmanifold_edges':0, 'zero_area_faces':0, 'loose_vertices':0,
    'skin_symmetry_max_m':symmetry,
    'bounds_m':[list(map(float,body.dimensions))],
    'fur_geometry':False, 'armatures':0, 'animations':0,
    'ground_z_m':min(v.co.z for v in bm.verts),
    'validation_scope':'Static geometry, glTF and Blender round-trip. Meshy weights/walk untested.',
}
bm.free()
glb=OUT/'Bruno_Meshy_body_only.glb'
bpy.ops.export_scene.gltf(filepath=str(glb),export_format='GLB',use_selection=True,use_active_scene=True,
    export_animations=False,export_skins=False,export_morph=False,
    export_yup=True,export_cameras=False,export_lights=False,
    export_vertex_color='NAME',export_vertex_color_name='Color',export_all_vertex_colors=False,
    export_materials='EXPORT',export_extras=False,export_apply=True)
blob=glb.read_bytes()
n,typ=struct.unpack_from('<II',blob,12)
doc=json.loads(blob[20:20+n])
assert not doc.get('animations') and not doc.get('skins') and not doc.get('cameras')
assert len(doc.get('meshes',[]))==1 and len(doc.get('nodes',[]))==1
assert not doc.get('images'), 'This upload is intentionally texture-free'
assert all('COLOR_0' in p['attributes'] for p in doc['meshes'][0]['primitives'])
metrics['glb_bytes']=len(blob)
metrics['glb_triangles']=sum(doc['accessors'][p['indices']]['count']//3 for p in doc['meshes'][0]['primitives'])
assert metrics['glb_triangles']==metrics['triangles']
metrics['glb_materials']=[m['name'] for m in doc['materials']]
(OUT/'geometry-check.json').write_text(json.dumps(metrics,indent=2))
shutil.copy2(glb,DESKTOP/glb.name)

# Render an exact side orthographic plate for the separately annotated guide.
cam=scene.camera
old_pose=cam.matrix_world.copy()
old_scale=cam.data.ortho_scale
old_resolution=(scene.render.resolution_x,scene.render.resolution_y)
floor=scene.objects['PreviewGround_ONLY']
floor.hide_render=True
scene.render.film_transparent=True
scene.render.resolution_x,scene.render.resolution_y=1600,900
cam.location=(-8,.25,1.09)
cam.rotation_euler=(Vector((0,.25,1.09))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.ortho_scale=4.65
bpy.context.view_layer.update()
scene.render.filepath=str(OUT/'side.png')
bpy.ops.render.render(write_still=True)
points=[]
for key,label,coord,color,letter in json.loads((OUT/'markers.json').read_text()):
    v=world_to_camera_view(scene,cam,Vector(coord))
    points.append({'key':key,'label':label,'xy':[v.x*1600,(1-v.y)*900],
                   'color':color,'letter':letter,'source_coordinate_m':coord})
(OUT/'markers-projected.json').write_text(json.dumps(points,ensure_ascii=False,indent=2))
cam.matrix_world=old_pose
cam.data.ortho_scale=old_scale
scene.render.resolution_x,scene.render.resolution_y=old_resolution
scene.render.film_transparent=False
floor.hide_render=False
scene.render.filepath=str(OUT/'Bruno_preview.png')
# A regular .blend preserves the active scene/workspace on opening. A library
# file made by data.libraries.write contains geometry but opens without a scene.
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Bruno_Meshy_body.blend'),copy=True,compress=True)
shutil.copy2(OUT/'Bruno_preview.png',DESKTOP/'Bruno_Meshy_preview.png')
print(json.dumps(metrics,indent=2))
print('DELIVERED',DESKTOP/glb.name)
