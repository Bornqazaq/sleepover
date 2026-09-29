"""Rebuild only the bedroom meshes in the dedicated source scene.

The accepted exterior and exported Wings/Dragonfly are kept byte for byte.
"""
import bpy, math, json, random
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2]
ART = ROOT / 'igruha/Assets/_Project/Art/Mosquitoes'
SOURCE = ROOT / 'art-source/Mosquitoes'
source_scene = bpy.context.scene
assert source_scene.name == 'Mosquitoes_OriginalBedroom'
kept_prefixes = ('WingPivot_', 'Membrane', 'Dragonfly', 'CompoundEye', 'AbdomenSegment', 'PredatorLeg')
previous_names = [(o,o.name) for o in source_scene.objects
                  if not o.get('msq_exterior') and not o.name.startswith(kept_prefixes)]
for ob,name in previous_names: ob.name = 'PreviousRoom_' + name
# Construct in an empty scene. Evaluating 854 exterior meshes after each sewing
# operation is unnecessary and made the room-only refresh several minutes long.
scene = bpy.data.scenes.new('Mosquitoes_RoomWorkbench')
bpy.context.window.scene = scene
src = (ROOT/'tools/blender/mosquitoes_bedroom.py').read_text(encoding='utf-8')
exec(src[src.index('palette ='):src.index("exec(compile((ROOT/'tools/blender/mosquitoes_compact_room.py')")])
details = (ROOT/'tools/blender/mosquitoes_detail_models.py').read_text(encoding='utf-8')
exec(details[:details.index('# A continuous')])
exec(src[src.index('def export('):src.index("exec(compile((ROOT/'tools/blender/mosquitoes_contact_details.py')")])
rng = random.Random(613)
room_revision_two = True
try:
    for filename in ('mosquitoes_compact_room.py', 'mosquitoes_contact_details.py',
                     'mosquitoes_finish_props.py', 'mosquitoes_prop_craft.py',
                     'mosquitoes_prop_lod.py', 'mosquitoes_room_models.py', 'mosquitoes_room_finish.py'):
        exec(compile((ROOT/'tools/blender'/filename).read_text(encoding='utf-8'), filename, 'exec'))
    room = list(scene.objects)
    for ob in room: ob['msq_room_revision'] = 2
    export('Bedroom', room)
except Exception:
    for ob in list(scene.objects): bpy.data.objects.remove(ob,do_unlink=True)
    bpy.context.window.scene = source_scene
    bpy.data.scenes.remove(scene)
    for ob,name in previous_names: ob.name = name
    raise
# Replace the old room only once its replacement has exported successfully.
for ob in list(source_scene.objects):
    if not ob.get('msq_exterior') and not ob.name.startswith(kept_prefixes):
        bpy.data.objects.remove(ob, do_unlink=True)
for ob in room:
    source_scene.collection.objects.link(ob)
bpy.context.window.scene = source_scene
bpy.data.scenes.remove(scene)
scene = source_scene
palette_path=ART/'Models/Palette.json'
saved_palette=json.loads(palette_path.read_text(encoding='utf-8'))
saved_palette.update(palette)
palette_path.write_text(json.dumps(saved_palette,indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'Mosquitoes.blend'))
print(json.dumps({'room_meshes': len(room), 'source': str(SOURCE/'Mosquitoes.blend')}))
