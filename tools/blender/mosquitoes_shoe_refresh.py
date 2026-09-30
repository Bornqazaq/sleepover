"""Replace only the rejected footwear, retaining the finished room and exterior."""
import bpy, math, json
from pathlib import Path
from mathutils import Vector, Matrix

ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'igruha/Assets/_Project/Art/Mosquitoes'
SOURCE=ROOT/'art-source/Mosquitoes'
original=bpy.context.scene
assert original.name=='Mosquitoes_OriginalBedroom'
previous=[(o,o.name) for o in original.objects if o.name.startswith('Sneaker')]
for ob,name in previous:ob.name='PreviousShoe_'+name
scene=bpy.data.scenes.new('Mosquitoes_ShoeWorkbench')
bpy.context.window.scene=scene
palette=json.loads((ART/'Models/Palette.json').read_text(encoding='utf-8'))
mats={name:bpy.data.materials['MSQ_'+name] for name in palette if 'MSQ_'+name in bpy.data.materials}
src=(ROOT/'tools/blender/mosquitoes_bedroom.py').read_text(encoding='utf-8')
exec(src[src.index('def co('):src.index('room_revision_two =')])
detail=(ROOT/'tools/blender/mosquitoes_detail_models.py').read_text(encoding='utf-8')
exec(detail[:detail.index('# A continuous')])
models=(ROOT/'tools/blender/mosquitoes_room_models.py').read_text(encoding='utf-8')
exec(models[models.index('def remove('):models.index('# ---- Garments.')])
try:
    exec(compile((ROOT/'tools/blender/mosquitoes_court_sneakers.py').read_text(encoding='utf-8'),
                 'mosquitoes_court_sneakers.py','exec'))
    shoes=list(scene.objects)
    for ob in shoes:
        ob.data.transform(ob.matrix_world);ob.matrix_world.identity()
        for old in list(ob.data.uv_layers):ob.data.uv_layers.remove(old)
        uv=ob.data.uv_layers.new(name='SurfaceMetres');uv.active_render=True
        for p in ob.data.polygons:
            axis=max(range(3),key=lambda a:abs(p.normal[a]));a,b=[k for k in range(3) if k!=axis]
            for loop in p.loop_indices:
                v=ob.data.vertices[ob.data.loops[loop].vertex_index].co;uv.data[loop].uv=(v[a],v[b])
        original.collection.objects.link(ob)
    bpy.context.window.scene=original
    room=[ob for ob in original.objects if ob.get('msq_room_revision')==2 and not ob.name.startswith('PreviousShoe_')]
    exec(src[src.index('def export('):src.index("exec(compile((ROOT/'tools/blender/mosquitoes_contact_details.py')")])
    export('Bedroom',room)
except Exception:
    for ob in list(scene.objects):bpy.data.objects.remove(ob,do_unlink=True)
    bpy.context.window.scene=original
    bpy.data.scenes.remove(scene)
    for ob,name in previous:ob.name=name
    raise
for ob,name in previous:bpy.data.objects.remove(ob,do_unlink=True)
bpy.data.scenes.remove(scene)
scene=original
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'Mosquitoes.blend'))
bpy.ops.object.select_all(action='DESELECT')
for ob in shoes:ob.select_set(True)
bpy.context.view_layer.objects.active=shoes[0]
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        area.spaces.active.shading.type='SOLID'
        area.spaces.active.shading.light='STUDIO'
        view=area.spaces.active.region_3d
        view.view_location=Vector(co((-3.01,.10,.47)))
        view.view_distance=.83
        view.view_rotation=Vector(co((.52,.65,-.67))).to_track_quat('Z','Y')
print(json.dumps({'room_meshes':len(room),'shoes':[{'name':o.name,'vertices':len(o.data.vertices)} for o in shoes]}))
