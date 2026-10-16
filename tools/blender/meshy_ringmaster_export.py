"""Bake reference colors and export the rigged Meshy horse into the mascot slot."""
import bpy,math,json
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'art-source/HorseRingmaster';GAME=ROOT/'igruha/Assets/_Project/Art/CircusNight/Mascot'
s=bpy.context.scene;rig=bpy.data.objects['Ringmaster_Rig'];body=bpy.data.objects['Horse_Meshy_Skin']
rig.animation_data.action=bpy.data.actions['Mascot_Idle'];s.frame_set(1);rig.data.pose_position='REST'
for ob in [o for o in s.objects if o.type=='MESH' and o.data.shape_keys]:
    for k in ob.data.shape_keys.key_blocks:k.value=0
# Save editable painted source before collapsing material slots/UVs for the engine.
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'HorseRingmaster_Painted.blend'))
reference_surface_vertices=len(body.data.vertices)
bpy.ops.object.select_all(action='DESELECT')
for ob in list(s.objects):
    if ob.type=='MESH' and not ob.name.startswith('UpperLid.'):ob.select_set(True)
bpy.context.view_layer.objects.active=body;bpy.ops.object.join();body.name='Horse_Meshy_Skin'
meshes=[body,bpy.data.objects['UpperLid.L'],bpy.data.objects['UpperLid.R']]
for i,ob in enumerate(meshes):
    bpy.ops.object.select_all(action='DESELECT');ob.select_set(True);bpy.context.view_layer.objects.active=ob
    layer=ob.data.uv_layers.new(name='GameAtlas');ob.data.uv_layers.active=layer;layer.active_render=True
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=math.radians(70),island_margin=.002,correct_aspect=False,scale_to_bounds=True);bpy.ops.object.mode_set(mode='OBJECT')
    layer=ob.data.uv_layers['GameAtlas']
    for uv in layer.data:
        if i==0:uv.uv.x=.004+uv.uv.x*.93;uv.uv.y=.004+uv.uv.y*.992
        else:uv.uv.x=.945+uv.uv.x*.05;uv.uv.y=.01+(i-1)*.5+uv.uv.y*.48
import runpy
atlas=runpy.run_path(str(ROOT/'tools/blender/meshy_reference_atlas.py'))['bake'](meshes,GAME/'CN_Mascot_Albedo.png')
runpy.run_path(str(ROOT/'tools/blender/meshy_ringmaster_facepaint.py'))['repair'](body,GAME/'CN_Mascot_Albedo.png',reference_surface_vertices)
bpy.data.images.remove(atlas)
atlas=bpy.data.images.load(str(GAME/'CN_Mascot_Albedo.png'),check_existing=False);atlas.pack()
mat=bpy.data.materials.new('HorseRingmaster_Atlas');mat.use_nodes=True
p=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');p.inputs['Roughness'].default_value=.72
tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=atlas;mat.node_tree.links.new(tex.outputs['Color'],p.inputs['Base Color'])
for ob in meshes:
    ob.data.materials.clear();ob.data.materials.append(mat)
    for face in ob.data.polygons:face.material_index=0
    coords=[tuple(v.uv) for v in ob.data.uv_layers['GameAtlas'].data]
    while len(ob.data.uv_layers):ob.data.uv_layers.remove(ob.data.uv_layers[0])
    layer=ob.data.uv_layers.new(name='GameAtlas')
    for v,uv in zip(layer.data,coords):v.uv=uv
    ob.data.uv_layers.active=layer;layer.active_render=True
rig.data.pose_position='POSE';rig.animation_data.action=bpy.data.actions['Mascot_Idle'];s.frame_set(1)
s.camera.location=(0,-9,2.1);s.camera.rotation_euler=(Vector((0,0,2.1))-s.camera.location).to_track_quat('-Z','Y').to_euler();s.camera.data.ortho_scale=4.6
bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
for ob in meshes:ob.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'HorseRingmaster.blend'))
bpy.ops.export_scene.fbx(filepath=str(GAME/'CN_Mascot.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},axis_forward='-Z',axis_up='Y',add_leaf_bones=False,use_armature_deform_only=True,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,use_mesh_modifiers=False,path_mode='AUTO')
report={'source':'Meshy_Source.blend supplied by user','bones':len(rig.data.bones),'vertices':sum(len(o.data.vertices) for o in meshes),'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes),'actions':[a.name for a in bpy.data.actions if a.name.startswith('Mascot_')],'blendshapes':{o.name:[k.name for k in o.data.shape_keys.key_blocks] for o in meshes if o.data.shape_keys},'atlas':'4096x4096','adaptations':['lip seam and mouth interior','articulated glove fingers','upper eyelid surfaces']}
(OUT/'export-check.json').write_text(json.dumps(report,indent=2));print(report)
# Blender-only facial preview, added after FBX export so Unity owns its timeline.
rig['LaughPreview']=0.0
rig.id_properties_ui('LaughPreview').update(min=0.0,max=1.0,description='Facial preview: 0 for Idle, 1 for Taunt. Unity controls these shapes independently.')
for side in ['L','R']:
    key=bpy.data.objects['UpperLid.'+side].data.shape_keys.key_blocks['Blink']
    driver=key.driver_add('value').driver
    driver.expression='max(0,1-abs(((frame/30)%3.4)-1.3)/0.1)'
key=body.data.shape_keys.key_blocks['LaughSmile'];driver=key.driver_add('value').driver
variable=driver.variables.new();variable.name='laugh';variable.type='SINGLE_PROP';variable.targets[0].id=rig;variable.targets[0].data_path='["LaughPreview"]'
driver.expression='laugh*(0.78+0.22*sin(frame/30*17.27876))'
s.camera.location=(1.9,-8,2.5);s.camera.rotation_euler=(Vector((0,0,2.08))-s.camera.location).to_track_quat('-Z','Y').to_euler();s.camera.data.ortho_scale=4.5
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'HorseRingmaster.blend'))
