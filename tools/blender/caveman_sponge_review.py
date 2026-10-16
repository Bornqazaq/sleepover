"""Validate the sandbox and queue review images/animation in the live Blender."""
import bpy
import bmesh
import math
import json
from pathlib import Path
from mathutils import Vector

OUT=Path(__file__).resolve().parents[2]/'art-source/CharacterSandbox/CavemanSponge'
s=bpy.context.scene;rig=bpy.data.objects['CavemanSponge_Rig'];s.frame_set(1)
active=rig.animation_data.action
for action in list(bpy.data.actions):
    if action!=active:bpy.data.actions.remove(action)
active.name='Sandbox • wave, bend, fingers, jaw and blink'
bpy.data.orphans_purge(do_recursive=True)

shape_col=bpy.data.collections.get('03_RIG_SHAPES')
if shape_col:
    for ob in list(shape_col.objects):bpy.data.objects.remove(ob,do_unlink=True)
else:
    shape_col=bpy.data.collections.new('03_RIG_SHAPES');s.collection.children.link(shape_col)
shape_col.hide_render=True
def shape(name,verts,edges):
    me=bpy.data.meshes.new(name);me.from_pydata(verts,edges,[]);me.update()
    ob=bpy.data.objects.new(name,me);shape_col.objects.link(ob);ob.hide_set(True);ob.hide_render=True
    return ob
root_shape=shape('WGT_Root',[(2.2*math.cos(i*math.pi/32),0,2.2*math.sin(i*math.pi/32)) for i in range(64)],[(i,(i+1)%64) for i in range(64)])
verts=[];edges=[]
for axis in range(3):
    for i in range(24):
        v=[0,0,0];v[(axis+1)%3]=.14*math.cos(i*math.pi/12);v[(axis+2)%3]=.14*math.sin(i*math.pi/12)
        verts.append(v);edges.append((axis*24+i,axis*24+(i+1)%24))
target_shape=shape('WGT_IK_Target',verts,edges)
root=rig.pose.bones['Root'];root.custom_shape=root_shape;root.use_custom_shape_bone_size=False
for p in rig.pose.bones:
    if '_IK.' in p.name or 'Pole.' in p.name:p.custom_shape=target_shape;p.use_custom_shape_bone_size=False

meshes=[o for o in bpy.data.collections['01_CHARACTER'].objects if o.type=='MESH']
result=json.loads((OUT/'validation.json').read_text())
result['mesh_objects']=len(meshes);result['animation']=active.name
result['sampled_frames']=[1,25,34,49,65,85,110,120]
result['finite_deformed_vertices']=True
max_radius=0
for frame in result['sampled_frames']:
    s.frame_set(frame);deps=bpy.context.evaluated_depsgraph_get()
    for ob in meshes:
        evaluated=ob.evaluated_get(deps);me=evaluated.to_mesh()
        for v in me.vertices:
            co=evaluated.matrix_world@v.co
            if not all(math.isfinite(x) for x in co):result['finite_deformed_vertices']=False
            max_radius=max(max_radius,co.length)
        evaluated.to_mesh_clear()
result['max_vertex_radius_m']=max_radius
result['uv_objects']=sum(bool(ob.data.uv_layers) for ob in meshes)
result['skin_modifiers']=sum(any(m.type=='ARMATURE' and m.object==rig for m in ob.modifiers) for ob in meshes)
result['ik']={};s.frame_set(1)
for side in ['L','R']:
    for limb,bone,target in [('Arm','Forearm.','Hand_IK.'),('Leg','LowerLeg.','Foot_IK.')]:
        p=rig.pose.bones[bone+side];prop=limb+'_IK_'+side;rest=p.bone.head_local.copy()
        rig[prop]=1;rig.update_tag();bpy.context.view_layer.update()
        result['ik'][prop]={'pole_angle':p.constraints[0].pole_angle,'rest_joint_error_m':(p.head-rest).length,'target_error_m':(p.tail-rig.pose.bones[target+side].head).length}
        rig[prop]=0;rig.update_tag();bpy.context.view_layer.update()
result['closed_primary_meshes']={}
for name in ['SpongeBody • rounded porous skin','ArmSkin.L','ArmSkin.R','LegSkin.L','LegSkin.R','Long hooked nose']:
    bm=bmesh.new();bm.from_mesh(bpy.data.objects[name].data)
    result['closed_primary_meshes'][name]=all(e.is_manifold for e in bm.edges);bm.free()
result['connected_limb_meshes']={}
for name in ['ArmSkin.L','ArmSkin.R','LegSkin.L','LegSkin.R']:
    me=bpy.data.objects[name].data;parent=list(range(len(me.vertices)))
    def root_index(x):
        while parent[x]!=x:parent[x]=parent[parent[x]];x=parent[x]
        return x
    for edge in me.edges:
        a,b=edge.vertices;parent[root_index(a)]=root_index(b)
    result['connected_limb_meshes'][name]=len(set(root_index(i) for i in range(len(parent))))
assert result['finite_deformed_vertices'] and max_radius<5
assert result['unweighted_vertices']==0 and result['max_influences']<=4
assert result['uv_objects']==len(meshes) and result['skin_modifiers']==len(meshes)
assert all(n==1 for n in result['connected_limb_meshes'].values())
assert all(result['closed_primary_meshes'].values())
assert all(v['rest_joint_error_m']<.0003 for v in result['ik'].values())
s.frame_set(1);(OUT/'validation.json').write_text(json.dumps(result,indent=2),encoding='utf8')

scene_camera=s.camera;target=Vector((0,-.10,1.75))
def view(loc,scale=7.1):
    scene_camera.location=loc;scene_camera.rotation_euler=(target-scene_camera.location).to_track_quat('-Z','Y').to_euler();scene_camera.data.ortho_scale=scale
def shot(name,loc,frame=1,scale=7.1):
    view(loc,scale);s.frame_set(frame);s.render.filepath=str(OUT/'previews'/(name+'.png'));bpy.ops.render.render(write_still=True)

def render_reviews():
    try:
        s.render.resolution_percentage=100;s.cycles.samples=40
        shot('three-quarter',(5,-9,4.2))
        shot('front',(0,-10,3.2))
        shot('back',(5,9,4.2))
        s.render.resolution_percentage=65;s.cycles.samples=24
        shot('wave',(5,-9,4.2),25)
        shot('blink',(5,-9,4.2),34)
        shot('bend',(5,-9,4.2),85)
        # Full four second deformation review, actual Blender animation frames.
        movie=OUT/'previews'/'motion-frames';movie.mkdir(exist_ok=True)
        s.render.resolution_percentage=50;s.cycles.samples=16
        view((5,-9,4.2))
        for index,frame in enumerate(range(1,121,2)):
            s.frame_set(frame);s.render.filepath=str(movie/('%04d.png'%index));bpy.ops.render.render(write_still=True)
        (OUT/'previews'/'render-complete.txt').write_text('6 stills and 60 animation frames rendered.\n')
    finally:
        s.frame_set(1);view((5,-9,4.2));s.render.resolution_percentage=100;s.cycles.samples=64
        s.render.filepath=str(OUT/'previews'/'three-quarter.png')
        bpy.ops.object.select_all(action='DESELECT');rig.select_set(True);bpy.context.view_layer.objects.active=rig
        for screen in bpy.data.screens:
            for area in screen.areas:
                if area.type=='VIEW_3D':
                    area.spaces.active.region_3d.view_perspective='CAMERA'
                    area.spaces.active.shading.type='MATERIAL'
        bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'CavemanSponge.blend'))
    return None
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'CavemanSponge.blend'))
bpy.app.timers.register(render_reviews,first_interval=.1)
print(json.dumps(result));print('Review renders queued.')
