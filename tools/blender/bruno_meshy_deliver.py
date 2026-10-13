"""Export only the finished bear, two animation clips and embedded PBR maps."""
import bpy, bmesh, json, shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'art-source/BrunoMeshy/finished'
DESK=Path.home()/'Desktop/Bruno_Finished';DESK.mkdir(exist_ok=True)
scene=bpy.data.scenes['Bruno_Finished'];bpy.context.window.scene=scene
rig=bpy.data.objects['BrunoRig'];body=bpy.data.objects['Bruno_Body'];coat=bpy.data.objects['Bruno_Coat']
for name in ('Bruno_Alert','Bruno_Strike','Bruno_Roar'):
    a=bpy.data.actions.get(name)
    if a:bpy.data.actions.remove(a)
rig.animation_data.action=bpy.data.actions['Bruno_Walk']
for t in list(rig.animation_data.nla_tracks):rig.animation_data.nla_tracks.remove(t)
track=rig.animation_data.nla_tracks.new();track.name='Idle';track.mute=True
track.strips.new('Idle',1,bpy.data.actions['Bruno_Idle'])
scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT')
for o in (body,coat,rig):o.select_set(True)
bpy.context.view_layer.objects.active=rig
parents={o:o.parent for o in (body,coat)}
source_meshes={o:o.data for o in (body,coat)}
for o in (body,coat):
    world=o.matrix_world.copy();o.parent=None;o.matrix_world=world
    o.data=o.data.copy();o.data.name=o.name+'_Export'
bm=bmesh.new();bm.from_mesh(body.data)
bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(body.data);bm.free()
for uv in list(coat.data.uv_layers):coat.data.uv_layers.remove(uv)
try:
    bpy.ops.export_scene.gltf(filepath=str(OUT/'Bruno_Finished_Walk.glb'),
        export_format='GLB',use_selection=True,use_active_scene=True,
        export_animations=True,export_animation_mode='ACTIONS',export_frame_range=False,
        export_anim_single_armature=False,
        export_force_sampling=True,export_skins=True,export_def_bones=False,
        export_all_influences=False,export_influence_nb=4,export_morph=False,export_tangents=True,
        export_cameras=False,export_lights=False,export_extras=True)
finally:
    for o,parent in parents.items():
        world=o.matrix_world.copy();o.parent=parent;o.matrix_world=world
        export_mesh=o.data;o.data=source_meshes[o];bpy.data.meshes.remove(export_mesh)
scene.frame_set(5)
scene.render.engine='CYCLES';scene.cycles.samples=48
scene.render.resolution_x=1400;scene.render.resolution_y=1000
# Finish on the art scene. The .blend retains the downloaded reference in its
# separate scene, and all PBR images are packed for a portable source file.
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Bruno_Finished.blend'),copy=True,compress=True)
for name in ('Bruno_Finished_Walk.glb','Bruno_Finished.blend','Bruno_beauty.png',
    'Bruno_BaseColor.png','Bruno_Normal.png','Bruno_Roughness.png'):
    shutil.copy2(OUT/name,DESK/name)
# Also place the main asset directly on the Desktop, as requested earlier.
shutil.copy2(OUT/'Bruno_Finished_Walk.glb',Path.home()/'Desktop/Bruno_Finished_Walk.glb')
(DESK/'Читать.txt').write_text('''БРУНО — чистовой медведь

Bruno_Finished_Walk.glb — готовая модель, шерсть, скелет, встроенные текстуры.
Два клипа: Bruno_Walk (1,3 с, ходьба на месте) и Bruno_Idle (6 с).
Bruno_beauty.png — внешний вид. Bruno_Walk_preview.mp4 — ходьба по кругу.
Bruno_Finished.blend — редактируемый исходник с упакованными текстурами.

Из Meshy взяты фазы шагов и траектория переноса лап. Суставы и веса исправлены;
опорные стопы горизонтальны, длины костей постоянны. Короткая шерсть движется
на тех же весах, что и тело. Шерсть — геометрия без прозрачности.

Модель: 2 меша, 31 кость, примерно 191 тыс. треугольников. Метры.
Удар, бег, рёв и подключение к сценам Unity — отдельный следующий этап.
Скачанные Character_output и Walking_withSkin сохранены без изменений.
''',encoding='utf-8')
print('DELIVERED',str(DESK))
