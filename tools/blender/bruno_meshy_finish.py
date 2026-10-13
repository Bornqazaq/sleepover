"""IGR-756. Rebuild Bruno's skin/coat around an anatomical rig, retarget Meshy walk.

Run through Blender MCP after opening Bruno_Meshy_body.blend and importing the
downloaded walk into its own scene. Incoming GLBs remain immutable references.
"""
import bpy, math, json, random
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion, Euler

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'art-source/BrunoMeshy/finished'
OUT.mkdir(exist_ok=True)
S = 1.076335156958268
if 'MeshyRig_RAW' not in bpy.data.objects:
    rawscene=bpy.data.scenes.new('Bruno_Meshy_Return')
    bpy.context.window.scene=rawscene
    incoming=ROOT/'art-source/BrunoMeshy/incoming'
    walking=next(incoming.glob('*Walking_withSkin.glb'))
    bpy.ops.import_scene.gltf(filepath=str(walking))
    for o in rawscene.objects:
        if o.type=='ARMATURE':o.name='MeshyRig_RAW'
        elif o.type=='MESH':o.name='MeshyBody_RAW'
raw = bpy.data.objects['MeshyRig_RAW']
rawscene = bpy.data.scenes['Bruno_Meshy_Return']
bpy.context.window.scene = rawscene
tracks = {}
for limb, prefix in [('Fore','frontleg'),('Hind','backleg')]:
    for side, name in [('R',prefix+'1'),('L','R_'+prefix+'1')]:
        samples=[]
        for i in range(121):
            f=i/120*24
            rawscene.frame_set(int(f),subframe=f%1)
            hip=raw.pose.bones['Hips']
            p=(raw.pose.bones[name].head-hip.head)/(sum(hip.scale)/3)
            samples.append([-p.x*S,p.z*S,p.y*S])
        ys=[p[1] for p in samples[:-1]]
        tracks[limb+'.'+side]={'samples':samples,'lift':ys.index(min(ys))/120,
            'land':ys.index(max(ys))/120,'range':max(ys)-min(ys)}
(OUT/'meshy-walk-tracks.json').write_text(json.dumps(tracks,indent=2))

name='Bruno_Finished'
if name in bpy.data.scenes:
    old=bpy.data.scenes[name]
    for o in list(old.objects): bpy.data.objects.remove(o,do_unlink=True)
    bpy.data.scenes.remove(old)
scene=bpy.data.scenes.new(name)
bpy.context.window.scene=scene
scene.unit_settings.system='METRIC'
source=bpy.data.objects[globals().get('BRUNO_SOURCE','Bruno_Meshy_BodyOnly')]
body=source.copy();body.data=source.data.copy();body.name='Bruno_Body'
scene.collection.objects.link(body)
for i,m in enumerate(body.data.materials):
    body.data.materials[i]=m.copy()
    body.data.materials[i].name=m.name.split('.')[0].replace('BM_','Bruno_')
body.vertex_groups.clear()

def active(o):
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True)
    bpy.context.view_layer.objects.active=o

arm=bpy.data.armatures.new('Bruno_Skeleton')
rig=bpy.data.objects.new('BrunoRig',arm);scene.collection.objects.link(rig)
active(rig);bpy.ops.object.mode_set(mode='EDIT')
bones={}
def bone(name,head,tail,parent=None,deform=True):
    head=globals().get('BRUNO_REMAP',Vector)(head);tail=globals().get('BRUNO_REMAP',Vector)(tail)
    b=arm.edit_bones.new(name);b.head=head;b.tail=tail
    if parent:b.parent=arm.edit_bones[parent]
    b.use_deform=deform;bones[name]=(Vector(head),Vector(tail))
bone('Root',(0,0,0),(0,0,.3),deform=False)
bone('Pelvis',(0,-.97,1.328),(0,-.65,1.43),'Root')
bone('Lumbar',(0,-.65,1.43),(0,-.15,1.53),'Pelvis')
bone('Spine',(0,-.15,1.53),(0,.35,1.61),'Lumbar')
bone('Chest',(0,.35,1.61),(0,.73,1.58),'Spine')
bone('Neck',(0,.73,1.58),(0,1.33,1.568),'Chest')
bone('Head',(0,1.33,1.568),(0,2.05,1.48),'Neck')
bone('Jaw',(0,1.48,1.285),(0,2.04,1.255),'Head',False)
bone('Tail',(0,-1.39,1.368),(0,-1.66,1.378),'Pelvis')
for side,s in [('L',1),('R',-1)]:
    x=s*.61
    bone('Scapula.'+side,(s*.27,.32,1.68),(x,.4,1.478),'Chest',False)
    bone('ForeUpper.'+side,(x,.4,1.478),(x,.30,.898),'Scapula.'+side)
    bone('ForeLower.'+side,(x,.30,.898),(x,.45,.328),'ForeUpper.'+side)
    bone('ForePaw.'+side,(x,.45,.328),(x,.82,.12),'ForeLower.'+side)
    bone('ForeToes.'+side,(x,.82,.12),(x,1.06,.065),'ForePaw.'+side,False)
    bone('HindUpper.'+side,(x,-.97,1.328),(x,-.86,.828),'Pelvis')
    bone('HindLower.'+side,(x,-.86,.828),(x,-.95,.388),'HindUpper.'+side)
    bone('HindPaw.'+side,(x,-.95,.388),(x,-.62,.12),'HindLower.'+side)
    bone('HindToes.'+side,(x,-.62,.12),(x,-.39,.065),'HindPaw.'+side,False)
    bone('Ear.'+side,(s*.272,1.23,1.83),(s*.276,1.235,2.005),'Head',False)
    bone('Lid.'+side,(s*.263,1.636,1.715),(s*.263,1.636,1.76),'Head',False)
bpy.ops.object.mode_set(mode='OBJECT')
rig.show_in_front=True

# Isolate the closed continuous body for the heat solver. Details receive rigid
# anatomical weights so teeth, eyeballs and horn never melt during deformation.
active(body);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.separate(type='LOOSE');bpy.ops.object.mode_set(mode='OBJECT')
parts=[o for o in scene.objects if o.type=='MESH']
skin=max(parts,key=lambda o:len(o.data.vertices));skin.name='Bruno_Body'
active(skin);rig.select_set(True);bpy.context.view_layer.objects.active=rig
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
print('HEAT',len(skin.vertex_groups),len(skin.data.vertices))

def smooth(a,b,x):
    t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def setweights(o,index,weights):
    weights={k:v for k,v in weights.items() if v>1e-5}
    ranked=sorted(weights.items(),key=lambda p:-p[1])[:4]
    total=sum(v for k,v in ranked)
    for g in o.vertex_groups:g.remove([index])
    for n,w in ranked:
        g=o.vertex_groups.get(n) or o.vertex_groups.new(name=n)
        g.add([index],w/total,'REPLACE')

for v in skin.data.vertices:
    p=globals().get('BRUNO_UNMAP',Vector)(v.co)
    w={skin.vertex_groups[g.group].name:g.weight for g in v.groups}
    if p.z<1.50 and p.y<1.15:
        limb='Fore' if p.y>-.22 else 'Hind';side='L' if p.x>0 else 'R'
        knee=.898 if limb=='Fore' else .828
        ankle=.328 if limb=='Fore' else .388
        upper=smooth(knee-.15,knee+.16,p.z)
        lower=smooth(ankle-.12,ankle+.14,p.z)*(1-upper)
        paw=max(0,1-upper-lower)
        toe=(smooth(.79,.96,p.y) if limb=='Fore' else smooth(-.64,-.46,p.y))*.65
        an={limb+'Upper.'+side:upper,limb+'Lower.'+side:lower,
            limb+'Paw.'+side:paw*(1-toe),limb+'Toes.'+side:paw*toe}
        # Weight the fleshy limb as one anatomical volume. Heat diffusion through
        # the deep abdomen used to pull a strip of belly between opposite legs.
        foot=1-smooth(.28,.46,p.z)
        cy=(.36*(1-foot)+.63*foot) if limb=='Fore' else (-.89*(1-foot)-.79*foot)
        radius=.44*(1-foot)+.54*foot
        region=(1-smooth(radius*.90,radius*1.40,abs(p.y-cy)))*smooth(.20,.46,abs(p.x))
        region*=1-smooth(1.15,1.50,p.z)
        front=smooth(-.15,.55,p.y);back=1-smooth(-1.12,-.50,p.y)
        torso={'Pelvis':back,'Lumbar':(1-back)*(1-front),'Chest':(1-back)*front}
        anatomical={n:weight*(1-region) for n,weight in torso.items()}
        for n,weight in an.items():anatomical[n]=anatomical.get(n,0)+weight*region
        correction=(1-smooth(1.12,1.50,p.z))*(1-smooth(.90,1.15,p.y))
        w={n:weight*(1-correction) for n,weight in w.items()}
        for n,weight in anatomical.items():w[n]=w.get(n,0)+weight*correction
    if p.y>1.32:
        h=smooth(1.28,1.5,p.y)
        w={n:weight*(1-h) for n,weight in w.items()}
        w['Head']=w.get('Head',0)+h
    if not w:w={'Spine':1}
    setweights(skin,v.index,w)

for o in parts:
    if o==skin:continue
    c=sum((v.co for v in o.data.vertices),Vector())/len(o.data.vertices)
    c=globals().get('BRUNO_UNMAP',Vector)(c)
    mats={o.data.materials[p.material_index].name for p in o.data.polygons}
    side='L' if c.x>0 else 'R'
    n='Head'
    if any('Claws' in m for m in mats):n=('Fore' if c.y>0 else 'Hind')+'Toes.'+side
    elif c.z>1.84:n='Ear.'+side
    elif c.y>1.65 and (c.z<1.32 or (c.z<1.40 and any('Muzzle' in m for m in mats)) or (c.z<1.375 and any('Paws_and_lips' in m for m in mats)) or (c.y<1.94 and c.z<1.38 and any('Teeth' in m for m in mats))):n='Jaw'
    elif c.y>1.65 and any('Gums' in m for m in mats):n='Jaw'
    o.vertex_groups.clear();g=o.vertex_groups.new(name=n);g.add(list(range(len(o.data.vertices))),1,'REPLACE')
    o.parent=rig
    mod=o.modifiers.new('Skin','ARMATURE');mod.object=rig
for b in arm.bones:b.use_deform=b.name!='Root'
active(skin)
for o in parts:o.select_set(True)
bpy.ops.object.join();body=bpy.context.object
body.name='Bruno_Body'
for mod in body.modifiers:
    if mod.type=='ARMATURE':mod.use_deform_preserve_volume=False
body['rig_revision']='IGR-756 anatomical joints + rigid feet; 4 normalized influences'

# The authored resting action remains reusable; walking below derives
# phase and swing trajectory from the user's Meshy clip, with planted soles.
motion=(ROOT/'tools/blender/circus_bear_motion.py').read_text()
motion=motion.replace("[('Idle',180),('Walk',24),('Run',18),('Alert',90),('Strike',42),('Roar',96)]",
    "[('Idle',180)]")
exec(compile(motion,'circus_bear_motion.py','exec'),globals())

def sample(track,t):
    u=(t%1)*120;i=int(u);f=u-i
    return Vector(track['samples'][i]).lerp(Vector(track['samples'][i+1]),f)

action=bpy.data.actions.new('Bruno_Walk');action.use_fake_user=True
rig.animation_data.action=action
previous={};support_log=[]
for f in range(1,41):
    t=(f-1)/39;cycle=t*math.tau
    for p in rig.pose.bones:p.matrix_basis=Matrix.Identity(4)
    pose('Root',loc=(.025*math.sin(cycle),0,-.075+.018*math.cos(cycle*2)))
    pose('Pelvis',(.016*math.sin(cycle),.012*math.cos(cycle),.026*math.cos(cycle)))
    pose('Lumbar',(-.022*math.sin(cycle-.4),.010*math.sin(cycle),-.018*math.cos(cycle)))
    pose('Spine',(.018*math.sin(cycle-.8),.009*math.sin(cycle),-.015*math.cos(cycle+.5)))
    pose('Chest',(.016*math.sin(cycle-1),-.012*math.sin(cycle),.018*math.cos(cycle+.9)))
    pose('Neck',(-.026*math.sin(cycle-.7),.012*math.sin(cycle),0))
    pose('Head',(-.030*math.sin(cycle-1),.012*math.sin(cycle-.3),0))
    pose('Tail',(.03*math.sin(cycle-1),0,.06*math.sin(cycle-.8)))
    for limb in ('Fore','Hind'):
        for side in ('L','R'):
            tr=tracks[limb+'.'+side];p=sample(tr,t)
            duration=(tr['land']-tr['lift'])%1
            phase=(t-tr['lift'])%1;swing=phase<duration
            lo=min(p[1] for p in tr['samples']);hi=max(p[1] for p in tr['samples'])
            target=bones[limb+'Paw.'+side][0].copy()
            # All planted feet move backwards at one authored speed. This lets
            # Unity match playback rate to world travel without skating.
            travel=1.05*1.3*(1-duration)
            # Constant support velocity; the reference supplies all four phase
            # offsets and forward swing, not the broken Meshy ankle rotations.
            if swing:
                q=phase/duration
                y=(p.y-lo)/(hi-lo)-.5
                z=(.15 if limb=='Fore' else .12)*math.sin(math.pi*q)**2
            else:
                q=(phase-duration)/(1-duration);y=.5-q;z=0
            target.y+=travel*y;target.z+=z
            leg(limb,side,target)
            if not swing:support_log.append({'frame':f,'foot':limb+'.'+side,
                'error':(rig.pose.bones[limb+'Paw.'+side].head-target).length})
    for p in rig.pose.bones:
        if p.name in previous and p.rotation_quaternion.dot(previous[p.name])<0:p.rotation_quaternion.negate()
        previous[p.name]=p.rotation_quaternion.copy()
        for prop in ('rotation_quaternion','location','scale'):p.keyframe_insert(prop,frame=f)
scene.frame_set(1)
for p in rig.pose.bones:
    for prop in ('rotation_quaternion','location','scale'):p.keyframe_insert(prop,frame=40)
scene.frame_start=1;scene.frame_end=39;scene.frame_set(10)
rig['walk_origin']='Meshy Walking: footfall phases + swing; anatomical retarget, fixed-length limbs, flat support paws'
rig['walk_cycle_seconds']=1.3
rig['walk_authored_speed_mps']=1.05
(OUT/'rig-check.json').write_text(json.dumps({'bones':len(arm.bones),
    'body_vertices':len(body.data.vertices),'max_support_ankle_error':max(r['error'] for r in support_log),
    'support_samples':len(support_log),'actions':[a.name for a in bpy.data.actions if a.name.startswith('Bruno_')]},indent=2))
print('FINISHED_RIG',len(body.data.vertices),'support_max',max(r['error'] for r in support_log))
active(rig)
