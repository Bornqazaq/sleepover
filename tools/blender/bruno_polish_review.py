"""Matching-camera beauty, modelling views and a compact six-clip review."""
import bpy,json
from pathlib import Path
from mathutils import Vector
OUT=Path('/Users/azamatyessimkhanuly/Dev/projects/sleepover/art-source/BrunoMeshy/polish-v5')
s=bpy.context.scene;r=bpy.data.objects['BrunoRig'];c=s.camera
def camera(pos,target,scale):
 c.location=pos;c.rotation_euler=(Vector(target)-c.location).to_track_quat('-Z','Y').to_euler();c.data.ortho_scale=scale
def render(name,res=(1200,900)):
 s.render.resolution_x,s.render.resolution_y=res;s.render.resolution_percentage=100;s.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
r.data.pose_position='REST';s.cycles.samples=40
camera((5.5,7.6,2.7),(0,.28,.9),4.95);render('after')
camera((0,7.6,1.9),(0,.25,.81),3.4);render('front',(1000,1000))
camera((7.6,.2,1.8),(0,.2,.80),4.55);render('side')
camera((2.9,5.8,2.05),(0,1.43,1.15),1.5);render('face',(1100,900))
camera((6,8,6),(0,.2,.8),6.5);render('game-distance',(1280,720))
# Neutral clay with fur temporarily hidden to expose the continuous skin.
mat=bpy.data.materials.new('V5_ReviewClay');mat.use_nodes=True
bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');bs.inputs['Base Color'].default_value=(.34,.34,.34,1);bs.inputs['Roughness'].default_value=.85
s.view_layers[0].material_override=mat;bpy.data.objects['Bruno_Coat'].hide_render=True
camera((5.5,7.6,2.7),(0,.28,.9),4.95);render('solid-clay')
s.view_layers[0].material_override=None;bpy.data.objects['Bruno_Coat'].hide_render=False
s.cycles.samples=24;r.data.pose_position='POSE'
for name,frame in [('Walk',10),('Run',7),('Alert',40),('Strike',19),('Roar',43),('Idle',40)]:
 r.animation_data.action=bpy.data.actions['Bruno_'+name];s.frame_set(frame)
 camera((5.5,7.6,3.9),(0,.25,1.6),5.0) if name=='Roar' else camera((5.5,7.6,2.7),(0,.28,.9),4.95)
 render('pose-'+name.lower(),(960,720))
r.data.pose_position='REST';r.animation_data.action=bpy.data.actions['Bruno_Walk'];s.frame_set(5)
camera((5.5,7.6,2.7),(0,.28,.9),4.95);s.render.resolution_x=1200;s.render.resolution_y=900;s.cycles.samples=48
for area in bpy.context.screen.areas:
 if area.type=='VIEW_3D':
  area.spaces.active.region_3d.view_perspective='CAMERA'
  area.spaces.active.overlay.show_overlays=False
bpy.ops.object.select_all(action='DESELECT');bpy.context.view_layer.objects.active=bpy.data.objects['Bruno_Body']
bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Bruno_Polished.blend'))
(OUT/'review-complete.txt').write_text('All review renders completed. Studio lighting unchanged from before.png.\n')
print('REVIEW COMPLETE')
