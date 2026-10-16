"""Rig the supplied Meshy sculpt, preserving its geometry and reference paint."""
import bpy, math
from mathutils import Vector, Quaternion
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art-source/HorseRingmaster'
body=bpy.data.objects['Horse_Meshy_Skin']
s=bpy.context.scene
# Run on the prepared reference-painted mesh, before export.
assert not body.vertex_groups, 'Rig already built: reopen the prepared source before rerunning.'
rigdata=bpy.data.armatures.new('Ringmaster_Skeleton');rig=bpy.data.objects.new('Ringmaster_Rig',rigdata);s.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig;rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
def bone(name,a,b,parent=None):
    q=rigdata.edit_bones.new(name);q.head=a;q.tail=b
    if parent:q.parent=rigdata.edit_bones[parent]
    return q
bone('Root',(0,0,0),(0,0,.3));bone('Hips',(0,0,1.55),(0,0,1.9),'Root')
bone('Spine',(0,0,1.9),(0,0,2.3),'Hips');bone('Chest',(0,0,2.3),(0,0,2.68),'Spine')
bone('Neck',(0,0,2.68),(0,-.02,2.96),'Chest');bone('Head',(0,-.02,2.96),(0,-.12,3.65),'Neck')
bone('Jaw',(0,-.24,3.04),(0,-.82,2.92),'Head')
for side,sign in [('L',1),('R',-1)]:
    def p(x,y,z):return (x*sign,y,z)
    bone('Shoulder.'+side,p(.08,0,2.64),p(.45,0,2.65),'Chest')
    bone('UpperArm.'+side,p(.45,0,2.65),p(.88,-.02,2.25),'Shoulder.'+side)
    bone('Forearm.'+side,p(.88,-.02,2.25),p(1.27,-.03,1.88),'UpperArm.'+side)
    bone('Hand.'+side,p(1.27,-.03,1.88),p(1.45,-.04,1.65),'Forearm.'+side)
    # Index is the upper/front finger of the glove; thumb is on its inner side.
    for name,x,y,z,dx,dy,dz in [('Index',1.48,-.16,1.65,.055,-.025,-.16),('Middle',1.53,-.03,1.60,.04,0,-.13),('Ring',1.51,.08,1.62,.035,.01,-.13),('Thumb',1.28,-.16,1.70,-.045,-.035,-.10)]:
        a=Vector(p(x,y,z));b=a+Vector(p(dx,dy,dz));c=b+Vector(p(dx*.65,dy*.65,dz*.7))
        bone(name+'1.'+side,a,b,'Hand.'+side);bone(name+'2.'+side,b,c,name+'1.'+side)
    bone('UpperLeg.'+side,p(.30,0,1.65),p(.40,-.02,.93),'Hips')
    bone('LowerLeg.'+side,p(.40,-.02,.93),p(.42,.01,.32),'UpperLeg.'+side)
    bone('Foot.'+side,p(.42,.01,.32),p(.42,-.35,.14),'LowerLeg.'+side)
    bone('Ear.'+side,p(.17,.03,3.84),p(.28,.03,4.12),'Head')
    bone('Eye.'+side,p(.15,-.48,3.60),p(.15,-.58,3.60),'Head')
bone('Tail1',(0,.30,1.78),(0,.70,1.40),'Hips');bone('Tail2',(0,.70,1.40),(0,.94,.98),'Tail1');bone('Tail3',(0,.94,.98),(0,1.0,.52),'Tail2')
bpy.ops.object.mode_set(mode='OBJECT')
for p in rig.pose.bones:p.rotation_mode='QUATERNION'
rig.show_in_front=True
for b in rigdata.bones:body.vertex_groups.new(name=b.name)
def smooth(x):x=max(0,min(1,x));return x*x*(3-2*x)
def segment_distance(p,b):
    a=b.head_local;d=b.tail_local-a;t=max(0,min(1,(p-a).dot(d)/d.length_squared));return (p-a-d*t).length
def distance_weights(p,names):
    values=sorted([(n,segment_distance(p,rigdata.bones[n])) for n in names],key=lambda a:a[1])[:2]
    # Compact support avoids distant limbs influencing each other.
    raw=[1/max(.025,d)**4 for _,d in values];total=sum(raw)
    return {v[0]:w/total for v,w in zip(values,raw)}
for v in body.data.vertices:
    x,y,z=v.co;ax=abs(x);side='L' if x>0 else 'R'
    if body.data.attributes['LowerLip'].data[v.index].value>.001:
        jaw=body.data.attributes['LowerLip'].data[v.index].value
        weights={'Head':1-jaw,'Jaw':jaw}
    elif z>2.90:
        weights={'Head':1.0}
        if z>3.80 and ax>.14:
            e=smooth((z-3.80)/.14);weights={'Head':1-e,'Ear.'+side:e}
        elif z<3.12 and y<-.29:
            jaw=body.data.attributes['LowerLip'].data[v.index].value
            weights={'Head':1-jaw,'Jaw':jaw}
    elif z>2.65 and ax<.42:
        h=smooth((z-2.68)/.24);weights={'Neck':1-h,'Head':h}
    elif y>.48 and z<1.86:
        weights=distance_weights(v.co,['Tail1','Tail2','Tail3'])
    elif (ax>1.15 and z>1.1) or (ax>.52 and z>1.46 and z>2.85-1.15*ax):
        names=['Shoulder.'+side,'UpperArm.'+side,'Forearm.'+side,'Hand.'+side]
        if ax>1.25 and z<1.78:names+= [f+str(k)+'.'+side for f in ['Index','Middle','Ring','Thumb'] for k in [1,2]]
        weights=distance_weights(v.co,names)
    elif z<1.65:
        if y>.24 and z>1.1:weights={'Hips':1}
        else:weights=distance_weights(v.co,['UpperLeg.'+side,'LowerLeg.'+side,'Foot.'+side])
    else:
        weights=distance_weights(v.co,['Hips','Spine','Chest','Neck'])
    for n,w in weights.items():
        if w>.0001:body.vertex_groups[n].add([v.index],w,'REPLACE')
mod=body.modifiers.new('Ringmaster skin','ARMATURE');mod.object=rig;body.parent=rig
# Sculpted smile: pull corners up and out, lift cheek pads. Jaw is skeletal.
basis=body.shape_key_add(name='Basis');smile=body.shape_key_add(name='LaughSmile');smile.value=0
for v,k in zip(body.data.vertices,smile.data):
    x,y,z=v.co
    if y<-.30 and 2.98<z<3.43:
        corner=math.exp(-((abs(x)-.20)/.10)**2-((z-3.035)/.13)**2)*smooth((-.30-y)/.20)
        k.co.x+=math.copysign(.035*corner,x);k.co.z+=.045*corner
# Blinking is a thin skin surface over the original modeled eye.
def material(name,color,rough=.7):
    m=bpy.data.materials.new(name);m.use_nodes=True
    q=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED');q.inputs['Base Color'].default_value=(*color,1);q.inputs['Roughness'].default_value=rough
    return m
lidmat=material('Warm brown eyelid',(.27,.135,.065))
lids=[]
for side,sign in [('L',1),('R',-1)]:
    cx=sign*.153;cy=-.510;cz=3.546;rx=.083;ry=.046;rz=.079
    verts=[];closed=[];faces=[];rows=12;cols=24
    for i in range(rows+1):
        for j in range(cols+1):
            phi=math.pi*j/cols
            for target,maximum in [(verts,1.36),(closed,3.07)]:
                theta=.015+(maximum-.015)*i/rows
                target.append((cx+rx*math.cos(phi)*math.sin(theta),cy-ry*math.sin(phi)*math.sin(theta),cz+rz*math.cos(theta)))
    for i in range(rows):
        for j in range(cols):
            a=i*(cols+1)+j;faces.append((a,a+1,a+cols+2,a+cols+1))
    mesh=bpy.data.meshes.new('EyelidSurface.'+side);mesh.from_pydata(verts,[],faces);mesh.update()
    ob=bpy.data.objects.new('UpperLid.'+side,mesh);s.collection.objects.link(ob);mesh.materials.append(lidmat)
    for p in mesh.polygons:p.use_smooth=True
    ob.shape_key_add(name='Basis');key=ob.shape_key_add(name='Blink');key.value=0
    for v,p in zip(key.data,closed):v.co=p
    group=ob.vertex_groups.new(name='Head');group.add(list(range(len(verts))),1,'REPLACE')
    mod=ob.modifiers.new('Ringmaster skin','ARMATURE');mod.object=rig;ob.parent=rig;lids.append(ob)
# Save a rest-pose source before clip authoring.
s.frame_start=1;s.frame_end=121;s.render.fps=30
bpy.context.view_layer.update()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'HorseRingmaster.blend'))
print('Rig created:',len(rigdata.bones),'bones, eyelid shape keys and cheek smile')
