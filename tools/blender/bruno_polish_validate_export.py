"""Validate mesh/skin data and preserve source actions before exporting copies."""
import bpy,bmesh,json,hashlib,math
from pathlib import Path
from mathutils import Vector
OUT=Path('/Users/azamatyessimkhanuly/Dev/projects/sleepover/art-source/BrunoMeshy/polish-v5')
s=bpy.context.scene;r=bpy.data.objects['BrunoRig']
names=['Bruno_Body','Bruno_Coat','Bruno_Details'];objects=[bpy.data.objects[n] for n in names]
report={}
for o in objects:
    me=o.data;me.calc_loop_triangles();bm=bmesh.new();bm.from_mesh(me)
    error=max(abs(1-sum(g.weight for g in v.groups)) for v in me.vertices)
    report[o.name]={'triangles':len(me.loop_triangles),'vertices':len(me.vertices),'uv_layers':[u.name for u in me.uv_layers],
        'unweighted':sum(not v.groups for v in me.vertices),'max_influences':max(len(v.groups) for v in me.vertices),
        'max_weight_error':error,'boundary_edges':sum(e.is_boundary for e in bm.edges),
        'non_manifold_edges':sum(not e.is_manifold for e in bm.edges),'loose_vertices':sum(not v.link_faces for v in bm.verts),
        'degenerate_triangles':sum(t.area<1e-12 for t in me.loop_triangles),
        'nonfinite_coordinates':sum(not all(math.isfinite(c) for c in v.co) for v in me.vertices)}
    assert report[o.name]['unweighted']==0 and error<1e-5
    assert report[o.name]['max_influences']<=4
    assert report[o.name]['loose_vertices']==0 and report[o.name]['nonfinite_coordinates']==0
    if 'Coat' not in o.name:assert report[o.name]['non_manifold_edges']==0
    bm.free()

action_names=['Bruno_Idle','Bruno_Walk','Bruno_Run','Bruno_Alert','Bruno_Strike','Bruno_Roar']
def fingerprint(action,ignore_fore=False):
    tracks=[]
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    if ignore_fore and fc.data_path.startswith('pose.bones["Fore'):continue
                    tracks.append((fc.data_path,fc.array_index,[(tuple(k.co),tuple(k.handle_left),tuple(k.handle_right),k.interpolation) for k in fc.keyframe_points]))
    return hashlib.sha256(repr(sorted(tracks)).encode()).hexdigest()
with bpy.data.libraries.load(str(OUT/'Bruno_Before.blend'),link=False) as (src,dst):
    dst.actions=list(action_names);dst.armatures=[r.data.name]
original_actions=dst.actions;original_rig=dst.armatures[0]
report['animation_preservation']={name:fingerprint(bpy.data.actions[name])==fingerprint(old) for name,old in zip(action_names,original_actions)}
report['roar_other_tracks_preserved']=fingerprint(bpy.data.actions['Bruno_Roar'],True)==fingerprint(original_actions[-1],True)
report['intentional_animation_changes']='Roar forelimbs: downward elbows, shoulder-relative reach and relaxed wrists'
report['skeleton_unchanged']=all(tuple(b.matrix_local)==tuple(original_rig.bones[b.name].matrix_local) for b in r.data.bones)
report['bones']=len(r.data.bones)
assert all(v for n,v in report['animation_preservation'].items() if n!='Bruno_Roar') and report['roar_other_tracks_preserved'] and report['skeleton_unchanged']
for old in original_actions:bpy.data.actions.remove(old)
bpy.data.armatures.remove(original_rig)
r.data.pose_position='POSE';save_action=r.animation_data.action
pose_checks={}
for name in action_names:
    action=bpy.data.actions[name];r.animation_data.action=action
    minimum=1000;maximum=-1000
    for frame in range(int(action.frame_range[0]),int(action.frame_range[1])+1,3):
        s.frame_set(frame);deps=bpy.context.evaluated_depsgraph_get()
        for ob in objects:
            ev=ob.evaluated_get(deps);me=ev.to_mesh()
            z=[(ev.matrix_world@v.co).z for v in me.vertices]
            assert all(math.isfinite(k) for k in z)
            minimum=min(minimum,min(z));maximum=max(maximum,max(z));ev.to_mesh_clear()
    pose_checks[name]={'sample_step':3,'min_z':minimum,'max_z':maximum}
report['animation_bounds']=pose_checks
r.animation_data.action=save_action;s.frame_set(5)

# Clean, portable exports: only the requested bear, never the studio/source archive.
def select(obs):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in obs+[r]:ob.select_set(True)
    bpy.context.view_layer.objects.active=r
select(objects)
parents={o:o.parent for o in objects}
for o in objects:
    matrix=o.matrix_world.copy();o.parent=None;o.matrix_world=matrix
source_meshes={o:o.data for o in objects}
for o in objects:
    o.data=o.data.copy();o.data.name=o.name+'_Export'
    bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
gltf_action=r.animation_data.action;gltf_mutes=[t.mute for t in r.animation_data.nla_tracks]
r.animation_data.action=None
for t in r.animation_data.nla_tracks:t.mute=False
bpy.ops.export_scene.gltf(filepath=str(OUT/'Bruno_Polished_LOD0.glb'),export_format='GLB',use_selection=True,use_active_scene=True,
    export_animations=True,export_animation_mode='NLA_TRACKS',export_frame_range=False,export_anim_single_armature=False,
    export_force_sampling=True,export_skins=True,export_def_bones=False,export_all_influences=False,export_influence_nb=4,
    export_morph=False,export_tangents=True,export_vertex_color='NONE',export_cameras=False,export_lights=False,export_extras=True)
r.animation_data.action=gltf_action
for t,mute in zip(r.animation_data.nla_tracks,gltf_mutes):t.mute=mute
for o,parent in parents.items():
    matrix=o.matrix_world.copy();o.parent=parent;o.matrix_world=matrix
active=r.animation_data.action;r.animation_data.action=None
mutes=[t.mute for t in r.animation_data.nla_tracks]
for t in r.animation_data.nla_tracks:t.mute=False
try:
    bpy.ops.export_scene.fbx(filepath=str(OUT/'Bruno_Polished_LOD0.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=True,
        bake_anim_simplify_factor=0.0,use_mesh_modifiers=True,mesh_smooth_type='FACE',path_mode='COPY',embed_textures=True)
finally:
    r.animation_data.action=active
    for t,mute in zip(r.animation_data.nla_tracks,mutes):t.mute=mute
for o,original in source_meshes.items():
    temporary=o.data;o.data=original;bpy.data.meshes.remove(temporary)
lodcol=bpy.data.collections['Bruno_LOD1'];lodcol.hide_viewport=False
try:
    lod=[bpy.data.objects[n+'_LOD1'] for n in names];select(lod)
    for ob in lod:
        for v in ob.data.vertices:
            weights=sorted([(g.group,g.weight) for g in v.groups],key=lambda x:-x[1])[:4]
            total=sum(w for _,w in weights)
            for g in list(v.groups):ob.vertex_groups[g.group].remove([v.index])
            for g,w in weights:ob.vertex_groups[g].add([v.index],w/total,'REPLACE')
    lod_source={o:o.data for o in lod}
    lod_parents={o:o.parent for o in lod}
    for o in lod:
        matrix=o.matrix_world.copy();o.parent=None;o.matrix_world=matrix
        o.data=o.data.copy();bm=bmesh.new();bm.from_mesh(o.data);bmesh.ops.triangulate(bm,faces=list(bm.faces));bm.to_mesh(o.data);bm.free()
    bpy.ops.export_scene.gltf(filepath=str(OUT/'Bruno_Polished_LOD1.glb'),export_format='GLB',use_selection=True,use_active_scene=True,
        export_animations=False,export_skins=True,export_tangents=True,export_vertex_color='NONE',export_cameras=False,export_lights=False)
finally:
    for o,original in lod_source.items():
        temporary=o.data;o.data=original;bpy.data.meshes.remove(temporary)
        matrix=o.matrix_world.copy();o.parent=lod_parents[o];o.matrix_world=matrix
    lodcol.hide_viewport=True
r.data.pose_position='REST';select(objects)
body=bpy.data.objects['Bruno_Body']
normal=next(n.image for n in body.data.materials[0].node_tree.nodes if n.type=='TEX_IMAGE' and 'Normal' in n.image.name)
normal.filepath_raw=str(OUT/'Bruno_Normal.png');normal.file_format='PNG';normal.save();normal.pack()
(OUT/'validation.json').write_text(json.dumps(report,indent=2))
bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Bruno_Polished.blend'))
print(json.dumps(report,indent=2))
