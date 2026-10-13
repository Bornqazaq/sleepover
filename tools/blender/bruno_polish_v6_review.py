"""Matched-camera review of the existing Bruno, before and after art polish."""
import bpy
from pathlib import Path
from mathutils import Vector
OUT=Path(__file__).resolve().parents[2]/'art-source/BrunoMeshy/polish-v6'
VIEWS={
 'body':((5.5,7.6,2.7),(0,.28,.90),4.95),
 'head':((2.4,5.8,2.05),(0,1.43,1.19),1.54),
 'profile':((6,0,2.15),(0,.25,.85),4.45),
 'head-side':((5,2.7,1.87),(0,1.47,1.16),1.47),
}
def render(label):
 s=bpy.context.scene;r=bpy.data.objects['BrunoRig'];cam=s.camera
 old=(cam.matrix_world.copy(),cam.data.ortho_scale,r.data.pose_position,s.render.resolution_x,s.render.resolution_y,s.cycles.samples)
 r.data.pose_position='REST';s.cycles.samples=24;s.cycles.use_denoising=True
 s.render.resolution_x=1100;s.render.resolution_y=825;s.render.resolution_percentage=100
 try:
  for name,(position,target,scale) in VIEWS.items():
   cam.location=position;cam.rotation_euler=(Vector(target)-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.ortho_scale=scale
   s.render.filepath=str(OUT/(label+'-'+name+'.png'));bpy.ops.render.render(write_still=True)
 finally:
  cam.matrix_world=old[0];cam.data.ortho_scale=old[1];r.data.pose_position=old[2]
  s.render.resolution_x=old[3];s.render.resolution_y=old[4];s.cycles.samples=old[5]
 (OUT/(label+'-render.done')).write_text('done')
