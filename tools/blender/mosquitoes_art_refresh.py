"""Refresh presentation in the existing own Blender scene; gameplay/wings stay intact."""
import bpy,math,json,random
from pathlib import Path
from mathutils import Vector,Matrix
ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'igruha/Assets/_Project/Art/Mosquitoes'
SOURCE=ROOT/'art-source/Mosquitoes'
scene=bpy.context.scene
assert scene.name=='Mosquitoes_OriginalBedroom', 'Select the dedicated Mosquitoes source scene first'
rng=random.Random(613)
src=(ROOT/'tools/blender/mosquitoes_bedroom.py').read_text(encoding='utf-8')
exec(src[src.index('palette ='):src.index("exec(compile((ROOT/'tools/blender/mosquitoes_compact_room.py')")])
details=(ROOT/'tools/blender/mosquitoes_detail_models.py').read_text(encoding='utf-8')
exec(details[:details.index('# A continuous')])
exec(src[src.index('def export('):src.index("exec(compile((ROOT/'tools/blender/mosquitoes_contact_details.py')")])
for ob in list(scene.objects):
    if ob.name.startswith('Courtyard_MSQ_') or ob.get('msq_exterior'):
        bpy.data.objects.remove(ob,do_unlink=True)
before=set(scene.objects)
exec(compile((ROOT/'tools/blender/mosquitoes_courtyard.py').read_text(encoding='utf-8'),'mosquitoes_courtyard.py','exec'))
for ob in set(scene.objects)-before:ob['msq_exterior']=True
(ART/'Models/Palette.json').write_text(json.dumps(palette,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'Mosquitoes.blend'))
