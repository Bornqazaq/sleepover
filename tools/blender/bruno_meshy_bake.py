"""Bake body materials to portable PBR maps, then restore preview pose."""
import bpy
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'art-source/BrunoMeshy/finished'
scene=bpy.data.scenes['Bruno_Finished'];bpy.context.window.scene=scene
body=bpy.data.objects['Bruno_Body'];rig=bpy.data.objects['BrunoRig']
rig.data.pose_position='REST'
bpy.ops.object.select_all(action='DESELECT');body.select_set(True);bpy.context.view_layer.objects.active=body
scene.render.engine='CYCLES';scene.cycles.samples=16
scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False
scene.render.bake.use_pass_color=True;scene.render.bake.margin=8
scene.render.bake.normal_space='TANGENT'
maps={}
for label,bake,size in [('BaseColor','DIFFUSE',2048),('Normal','NORMAL',2048),('Roughness','ROUGHNESS',1024)]:
    im=bpy.data.images.new('Bruno_'+label,width=size,height=size,alpha=False)
    if label!='BaseColor':im.colorspace_settings.name='Non-Color'
    for mat in body.data.materials:
        nt=mat.node_tree;n=nt.nodes.new('ShaderNodeTexImage');n.image=im;nt.nodes.active=n
    bpy.ops.object.bake(type=bake)
    im.filepath_raw=str(OUT/('Bruno_'+label+'.png'));im.file_format='PNG';im.save();im.pack()
    maps[label]=im
    print('BAKED',label,flush=True)
mat=bpy.data.materials.new('Bruno_Body_PBR');mat.use_nodes=True
nt=mat.node_tree;bs=next(n for n in nt.nodes if n.type=='BSDF_PRINCIPLED')
for label in ('BaseColor','Normal','Roughness'):
    n=nt.nodes.new('ShaderNodeTexImage');n.image=maps[label]
    if label=='Normal':
        normal=nt.nodes.new('ShaderNodeNormalMap');nt.links.new(n.outputs['Color'],normal.inputs['Color'])
        nt.links.new(normal.outputs['Normal'],bs.inputs['Normal'])
    else:nt.links.new(n.outputs['Color'],bs.inputs['Base Color' if label=='BaseColor' else 'Roughness'])
body.data.materials.clear();body.data.materials.append(mat)
for p in body.data.polygons:p.material_index=0
for attr in list(body.data.color_attributes):body.data.color_attributes.remove(attr)
rig.data.pose_position='POSE';scene.frame_set(5)
scene.cycles.samples=48
