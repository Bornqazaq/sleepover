"""Retarget sampled Meshy body motion and four 3D foot paths to Bruno.

The immutable downloaded clip supplies timing, torso/head/tail rotations and
lateral foot travel. Only anatomical proportions and flat-ground support are
corrected; no sinusoidal replacement gait is used.
"""
import bpy, math, json, hashlib
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'art-source/BrunoMeshy/finished'
scene=bpy.data.scenes['Bruno_Finished'];rig=bpy.data.objects['BrunoRig'];body=bpy.data.objects['Bruno_Body']
rawscene=bpy.data.scenes['Bruno_Meshy_Return'];raw=bpy.data.objects['MeshyRig_RAW']
C=Matrix(((-1,0,0),(0,0,1),(0,1,0)));cq=C.to_quaternion()
N=120
names=['Hips','chest','head','tail1']
limbs={('Fore','R'):'frontleg',('Fore','L'):'R_frontleg',('Hind','R'):'backleg',('Hind','L'):'R_backleg'}
rawdata=[]
bpy.context.window.scene=rawscene
for i in range(N+1):
    rawscene.frame_set(int(i*24/N),subframe=(i*24/N)%1)
    hip=raw.pose.bones['Hips'];scale=sum(hip.scale)/3
    points={}
    for key,prefix in limbs.items():
        points[key]=[(C@(raw.pose.bones[prefix+j].head-hip.head)/scale) for j in ('0','1','2')]
    qs={n:cq@raw.pose.bones[n].matrix.to_quaternion()@raw.pose.bones[n].bone.matrix_local.to_quaternion().inverted()@cq.inverted() for n in names}
    rawdata.append({'hip':C@hip.head/scale,'q':qs,'p':points})
meanq={}
for n in names:
    q0=rawdata[0]['q'][n]
    vals=[q if q.dot(q0)>=0 else -q for q in [d['q'][n] for d in rawdata[:-1]]]
    meanq[n]=Quaternion(tuple(sum(q[k] for q in vals)/N for k in range(4))).normalized()
meanhip=sum((d['hip'] for d in rawdata[:-1]),Vector())/N
track={}
for key in limbs:
    # Meshy's final marker is the ground-contact end of the foot. The preceding
    # wrist/hock rises during a heel roll while that marker is still planted.
    # Treating the wrist rise as paw lift produced an abrupt knee fold.
    ps=[d['p'][key][2] for d in rawdata]
    lift=min(range(N),key=lambda i:ps[i].y)/N;land=max(range(N),key=lambda i:ps[i].y)/N
    duration=(land-lift)%1
    swingids=[i for i in range(N) if (i/N-lift)%1<duration]
    zlift=ps[round(lift*N)].z;zland=ps[round(land*N)].z
    peak=max(ps[i].z-(zlift+(zland-zlift)*((i/N-lift)%1)/duration) for i in swingids)
    rolls=[math.atan2((d['p'][key][2]-d['p'][key][1]).z,(d['p'][key][2]-d['p'][key][1]).y) for d in rawdata[:-1]]
    track[key]={'meanx':sum(p.x for p in ps[:-1])/N,'lift':lift,'land':land,'duration':duration,'lo':min(p.y for p in ps),'hi':max(p.y for p in ps),'zlift':zlift,'zland':zland,'peak':peak,'roll':sum(rolls)/N}

bpy.context.window.scene=scene
# Reuse the fixed-length IK solver, without generating procedural locomotion.
helpers=(ROOT/'tools/blender/circus_bear_motion.py').read_text().split("for title,count in")[0]
exec(compile(helpers,'circus_bear_motion_helpers.py','exec'),globals())
old=bpy.data.actions.get('Bruno_Walk')
if old:
    rig.animation_data.action=None;bpy.data.actions.remove(old)
action=bpy.data.actions.new('Bruno_Walk');action.use_fake_user=True;rig.animation_data.action=action
bones={b.name:(b.head_local.copy(),b.tail_local.copy()) for b in rig.data.bones}
soles={(limb,side):[v.index for v in body.data.vertices if v.co.z<.035 and (v.co.x>0)==(side=='L') and (v.co.y>-.2)==(limb=='Fore')] for limb,side in limbs}

def data(t):
    u=(t%1)*N;i=int(u);a=u-i;d0=rawdata[i];d1=rawdata[i+1]
    return {'hip':d0['hip'].lerp(d1['hip'],a),'q':{n:d0['q'][n].slerp(d1['q'][n],a)@meanq[n].inverted() for n in names},'p':{k:[v.lerp(w,a) for v,w in zip(d0['p'][k],d1['p'][k])] for k in limbs}}

def world_delta(name,q):
    p=rig.pose.bones[name]
    p.matrix=Matrix.LocRotScale(p.head,q@p.bone.matrix_local.to_quaternion(),Vector((1,1,1)))
    bpy.context.view_layer.update()

previous={};logs=[];duration_seconds=1.2;speed=.85
for f in range(1,38):
    t=(f-1)/36;d=data(t)
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    pose('Root',loc=(0,0,-.045+(d['hip'].z-meanhip.z)*.50))
    bpy.context.view_layer.update()
    world_delta('Pelvis',d['q']['Hips'])
    world_delta('Lumbar',d['q']['Hips'].slerp(d['q']['chest'],.33))
    world_delta('Spine',d['q']['Hips'].slerp(d['q']['chest'],.66))
    world_delta('Chest',d['q']['chest'])
    world_delta('Neck',d['q']['chest'].slerp(d['q']['head'],.5))
    world_delta('Head',d['q']['head'])
    world_delta('Tail',d['q']['Hips'].slerp(d['q']['tail1'],.35))
    targets={};rolls={};stances={}
    for key,tr in track.items():
        limb,side=key;ps=d['p'][key];p=ps[2];phase=(t-tr['lift'])%1;swing=phase<tr['duration'];stances[key]=not swing
        target=bones[limb+'Paw.'+side][0].copy()
        travel=speed*duration_seconds*(1-tr['duration'])
        if swing:
            u=phase/tr['duration'];y=(p.y-tr['lo'])/(tr['hi']-tr['lo'])-.5
            tangent=-tr['duration']/(1-tr['duration'])
            y+=tangent*(2*u*u*u-3*u*u+u)
            # Preserve the asymmetry of the actual lift path. Soft endpoints
            # correct the original rig's tilted ankles, without a new sine gait.
            envelope=min(1,u/.28,(1-u)/.28);envelope=max(0,envelope);envelope=envelope*envelope*(3-2*envelope)
            baseline=tr['zlift']+(tr['zland']-tr['zlift'])*u
            height=max(0,(p.z-baseline)/max(.001,tr['peak']))
            z=(.075 if limb=='Fore' else .060)*height*envelope
        else:
            u=(phase-tr['duration'])/(1-tr['duration']);y=.5-u;z=0
        target.y+=travel*y;target.x+=(p.x-tr['meanx'])*.85;target.z+=z
        v=ps[2]-ps[1]
        rawroll=max(-.12,min(.12,(math.atan2(v.z,v.y)-tr['roll'])*.25))
        # A bear loads the whole padded sole. Resetting a lifted wrist from the
        # previous stance heel-roll at takeoff caused a one-frame ankle jump.
        roll=rawroll*envelope if swing else 0
        if limb=='Fore':
            # Moving scapula carries the shoulder mass as the source paw advances.
            pose('Scapula.'+side,(-.13*y,0,0))
            scapula=rig.pose.bones['Scapula.'+side]
            scapula.location=scapula.bone.matrix_local.to_3x3().inverted()@Vector((0,.14*y,-.060*(2*y)**2))
        leg(limb,side,target,roll)
        targets[key]=target;rolls[key]=roll
        pose(limb+'Toes.'+side,(-roll*.55,0,0))
    bpy.context.view_layer.update()
    evaluated=body.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=evaluated.to_mesh()
    floors={key:min(mesh.vertices[i].co.z for i in ids) for key,ids in soles.items()};evaluated.to_mesh_clear()
    for key,floor in floors.items():
        if stances[key] or floor<0:
            target=rig.pose.bones[key[0]+'Paw.'+key[1]].head.copy();target.z-=floor
            leg(*key,target,rolls[key])
    for p in rig.pose.bones:
        if p.name in previous and p.rotation_quaternion.dot(previous[p.name])<0:p.rotation_quaternion.negate()
        previous[p.name]=p.rotation_quaternion.copy()
        for prop in ('rotation_quaternion','location','scale'):p.keyframe_insert(prop,frame=f)
    logs.append({'frame':f,'source_frame':(t%1)*24,'stance':[limb+'.'+side for (limb,side),plant in stances.items() if plant]})
scene.frame_set(1)
for p in rig.pose.bones:
    for prop in ('rotation_quaternion','location','scale'):p.keyframe_insert(prop,frame=37)
scene.frame_start=1;scene.frame_end=36;scene.frame_set(5)
rig['walk_origin']='Meshy original sampled hips/chest/head/tail rotations and 3D limb trajectories; compact rest skeleton and ground support correction'
rig['walk_cycle_seconds']=duration_seconds;rig['walk_authored_speed_mps']=speed
incoming=next((ROOT/'art-source/BrunoMeshy/incoming').glob('*Walking_withSkin.glb'))
(OUT/'meshy-retarget-check.json').write_text(json.dumps({'source':incoming.name,'sha256':hashlib.sha256(incoming.read_bytes()).hexdigest(),'source_frames':[0,24],'source_seconds':.8,'retarget_seconds':duration_seconds,'authored_speed':speed,'tracks':{a+'.'+b:v for (a,b),v in track.items()},'samples':logs},indent=2))
print('MESHY_FULL_RETARGET',len(rawdata),'samples',duration_seconds,'seconds')
