"""Finish the downloaded horse's hair, mouth, and authored gesture clips."""
import bpy,math
from pathlib import Path
from mathutils import Vector,Quaternion
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'art-source/HorseRingmaster'
s=bpy.context.scene;body=bpy.data.objects['Horse_Meshy_Skin'];rig=bpy.data.objects['Ringmaster_Rig']
for p in rig.pose.bones:p.rotation_quaternion=(1,0,0,0)
for ob in [body,bpy.data.objects['UpperLid.L'],bpy.data.objects['UpperLid.R']]:
    for k in ob.data.shape_keys.key_blocks:k.value=0
mat=bpy.data.materials.new('Chestnut mane and tail');mat.use_nodes=True
ns=mat.node_tree.nodes;ls=mat.node_tree.links;p=next(n for n in ns if n.type=='BSDF_PRINCIPLED');p.inputs['Roughness'].default_value=.83
noise=ns.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=28;noise.inputs['Detail'].default_value=2
ramp=ns.new('ShaderNodeValToRGB');ramp.color_ramp.elements[0].color=(.025,.012,.007,1);ramp.color_ramp.elements[1].color=(.12,.055,.027,1)
ls.new(noise.outputs['Fac'],ramp.inputs[0]);ls.new(ramp.outputs['Color'],p.inputs['Base Color'])
body.data.materials.append(mat);hair_index=len(body.data.materials)-1
for poly in body.data.polygons:
    x,y,z=poly.center;ax=abs(x)
    hair=(z>3.76 and ax<.18) or (z>3.69 and y<-.40 and ax<.24) or (z>3.05 and ax>.27 and y>-.34) or (z>2.82 and y>.14) or (z<1.82 and y>.49)
    if hair:poly.material_index=hair_index
# A recessed back to the mouth closes the opening behind the animated lining.
def sphere(name,pos,scale,color,bone):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32,ring_count=16,location=pos);ob=bpy.context.object;ob.name=name;ob.scale=scale
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    mat=bpy.data.materials.new(name+' material');mat.use_nodes=True
    p=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Roughness'].default_value=.8;ob.data.materials.append(mat)
    for f in ob.data.polygons:f.use_smooth=True
    ob.vertex_groups.new(name=bone).add(list(range(len(ob.data.vertices))),1,'REPLACE');mod=ob.modifiers.new('Ringmaster skin','ARMATURE');mod.object=rig;ob.parent=rig
sphere('MouthBack',(0,-.62,2.985),(.20,.018,.065),(.012,.003,.004),'Jaw')
sphere('Tongue',(0,-.69,2.925),(.105,.17,.024),(.24,.055,.055),'Jaw')
def rotate(name,axis,angle):
    b=rig.data.bones[name];v=b.matrix_local.to_quaternion().inverted()@Vector(axis)
    rig.pose.bones[name].rotation_quaternion=Quaternion(v,angle)
def aim(name,direction):
    bpy.context.view_layer.update();p=rig.pose.bones[name]
    delta=(p.tail-p.head).rotation_difference(Vector(direction))
    matrix=delta.to_matrix().to_4x4()@p.matrix;matrix.translation=p.head;p.matrix=matrix;bpy.context.view_layer.update()
def solve_arm(goal,side='R'):
    bpy.context.view_layer.update();p=rig.pose.bones['UpperArm.'+side];q=rig.pose.bones['Forearm.'+side];start=p.head.copy();delta=goal-start
    a=p.bone.length;b=q.bone.length;d=min(delta.length,a+b-.01);direction=delta.normalized()
    pole=Vector((-1 if side=='R' else 1,.15,-.5));pole=(pole-direction*pole.dot(direction)).normalized()
    along=(a*a+d*d-b*b)/(2*d);elbow=start+direction*along+pole*math.sqrt(max(.001,a*a-along*along))
    aim('UpperArm.'+side,elbow-start);aim('Forearm.'+side,goal-rig.pose.bones['Forearm.'+side].head)
    if side=='R':
        aim('Hand.R',(0,-1,.04));aim('Index1.R',(0,-1,.03));aim('Index2.R',(0,-1,.03))
def reset():
    for p in rig.pose.bones:p.location=(0,0,0);p.rotation_quaternion=(1,0,0,0);p.scale=(1,1,1)
def smooth(x):x=max(0,min(1,x));return x*x*(3-2*x)
rig.animation_data_create()
for name,length in [('Mascot_Idle',4),('Mascot_Taunt',3.6)]:
    old=bpy.data.actions.get(name)
    if old:bpy.data.actions.remove(old)
    action=bpy.data.actions.new(name);action.use_fake_user=True;rig.animation_data.action=action
    for f in range(int(length*30)+1):
        t=f/30;reset()
        rotate('UpperArm.L',(0,1,0),.28);rotate('UpperArm.R',(0,1,0),-.28)
        rotate('Head',(0,0,1),.02*math.sin(t*1.7));rotate('Chest',(1,0,0),.012*math.sin(t*2))
        for side,sign in [('L',1),('R',-1)]:rotate('Ear.'+side,(0,1,0),sign*.05*math.sin(t*2.3+.5*sign))
        for i in range(1,4):rotate('Tail'+str(i),(0,0,1),.055*math.sin(t*2-i*.5))
        if name=='Mascot_Taunt':
            env=smooth(t/.48)*(1-smooth((t-3.05)/.55));beat=max(0,math.sin((t-.40)*math.pi*5.5))*env
            rotate('Head',(1,0,0),(-.10+.07*beat)*env);rotate('Chest',(1,0,0),.025*beat);rotate('Spine',(0,1,0),.025*env)
            rotate('Jaw',(1,0,0),(.045+.20*beat)*env)
            old={n:rig.pose.bones[n].rotation_quaternion.copy() for n in ['UpperArm.R','Forearm.R','Hand.R','Index1.R','Index2.R','UpperArm.L','Forearm.L','Hand.L']}
            solve_arm(Vector((-.68,-.78,2.58+.025*beat)))
            solve_arm(Vector((.40,-.66,2.03)),'L');aim('Hand.L',(-.8,-.16,.05))
            for n,q in old.items():rig.pose.bones[n].rotation_quaternion=q.slerp(rig.pose.bones[n].rotation_quaternion,env)
            for finger in ['Middle','Ring','Thumb']:
                rotate(finger+'1.R',(0,1,0),-1.15*env);rotate(finger+'2.R',(0,1,0),-1.25*env)
        for p in rig.pose.bones:
            p.keyframe_insert('rotation_quaternion',frame=f+1,group=p.name);p.keyframe_insert('location',frame=f+1,group=p.name)
rig.animation_data.action=bpy.data.actions['Mascot_Idle'];s.frame_set(1)
s.camera.location=(0,-9,2.1);s.camera.rotation_euler=(Vector((0,0,2.1))-s.camera.location).to_track_quat('-Z','Y').to_euler();s.camera.data.ortho_scale=4.6
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'HorseRingmaster.blend'))
print('Authored Idle and Taunt, mouth cavity, painted mane')
